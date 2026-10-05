#!/usr/bin/env python3
"""Analyze AI car telemetry written to PROJECT/.aicar-telemetry/ by the editor exporter.

Usage:
    python3 Tools/aicar/analyze.py [telemetry-root] [--session ID] [--live]
                                   [--since-epoch KEY] [--maps | --no-maps] [--out DIR]

telemetry-root defaults to <project>/.aicar-telemetry (this script lives in <project>/Tools/aicar).
The session defaults to the id in <root>/aicar/current_session.txt, else the newest session.

Layout, one epoch per distinct (DriverVersion, tuning) configuration so configs can be compared
inside ONE continuous play session:

    aicar/current_session.txt                 single line: the live session id
    aicar/<session>/navsample.csv             navmesh points (how the analyzer "sees" the map)
    aicar/<session>/<car>/epochs.csv          one row per epoch incl. the OPEN one; rewritten every flush
    aicar/<session>/<car>/e<NNN>/             samples.csv, events.jsonl, legs.csv, summary.json

Every file is rewritten live (default flush 5 s), so half-written lines happen: a truncated CSV
row or a bad JSON line is skipped, never a crash. The score uses the same formula as the old
per-car report but is computed PER EPOCH KEY, merging the same key across cars (summed counts
and car-minutes). --live prints only the epoch comparison table plus the open epoch's incident
hotspots: compact output meant for an LLM polling cheaply during play.

Sessions in the old pre-epoch layout (summary.json per car, no epochs.csv) are reported as
unsupported. See AI-Car-Feedback-Loop.md for what the numbers mean.
"""
import argparse
import csv
import json
import math
import os
import re
import sys
from collections import Counter, defaultdict

UNITS_PER_METER = 39.37
INCIDENTS = ("Blocked", "Reverse", "Collision", "Teleport", "Flipped", "OffNav")
MIN_CAR_MINUTES = 3.0  # below this an epoch key is flagged 'insufficient'
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
DEFAULT_ROOT = os.path.normpath(os.path.join(SCRIPT_DIR, os.pardir, os.pardir, ".aicar-telemetry"))


# ---------------------------------------------------------------- tolerant loading

def fnum(row, key, default=0.0):
    try:
        return float(row.get(key, default))
    except (TypeError, ValueError):
        return default


def truthy(v):
    return str(v).strip().lower() in ("1", "true", "yes", "on")


def read_csv_rows(path):
    """CSV rows as dicts, skipping any half-written line (the game rewrites these files live)."""
    rows = []
    try:
        with open(path, newline="") as f:
            reader = csv.reader(f)
            header = next(reader, None)
            if not header:
                return rows
            for rec in reader:
                if len(rec) != len(header):
                    continue  # truncated last line mid-flush
                rows.append(dict(zip(header, rec)))
    except OSError:
        return rows
    return rows


def read_jsonl(path):
    events = []
    try:
        with open(path) as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    obj = json.loads(line)
                except json.JSONDecodeError:
                    continue  # half-written line mid-flush
                if isinstance(obj, dict):
                    events.append(obj)
    except OSError:
        pass
    return events


def read_json(path):
    try:
        with open(path) as f:
            return json.load(f)
    except (OSError, json.JSONDecodeError, ValueError):
        return {}


def clean_events(events):
    """Keep only events with usable coordinates."""
    out = []
    for e in events:
        try:
            x, y = float(e["x"]), float(e["y"])
        except (KeyError, TypeError, ValueError):
            continue
        e = dict(e)
        e["x"], e["y"] = x, y
        out.append(e)
    return out


# ---------------------------------------------------------------- session loading

NUM_FIELDS = ("start_t", "duration", "distance", "arrived", "legs", "blocked", "reverses",
              "collisions", "collisions_static", "collisions_dynamic", "collisions_hard",
              "teleports", "flips", "offnav_time", "idle_time", "steer_sign_flips")


def load_session(session_dir):
    """Returns (epoch_rows, old_layout_car_names, nav). One row per (car, epoch)."""
    rows, old = [], []
    if not os.path.isdir(session_dir):
        return rows, old, []
    for name in sorted(os.listdir(session_dir)):
        d = os.path.join(session_dir, name)
        if not os.path.isdir(d):
            continue
        car_rows = read_csv_rows(os.path.join(d, "epochs.csv"))
        if car_rows:
            for r in car_rows:
                if not r.get("key"):
                    continue
                try:
                    idx = int(fnum(r, "epoch"))
                except (ValueError, OverflowError):
                    idx = 0
                row = {"car": name, "dir": d, "epoch": idx}
                for k in ("key", "version", "tuning_revision"):
                    row[k] = r.get(k, "?")
                for k in NUM_FIELDS:
                    row[k] = fnum(r, k)
                row["closed"] = truthy(r.get("closed"))
                rows.append(row)
        elif os.path.exists(os.path.join(d, "summary.json")):
            old.append(name)
    nav = []
    for r in read_csv_rows(os.path.join(session_dir, "navsample.csv")):
        nav.append((fnum(r, "x"), fnum(r, "y")))
    return rows, old, nav


def read_text_lines(path):
    try:
        with open(path) as f:
            return f.readlines()
    except OSError:
        return []


def load_epoch_detail(row):
    """samples/events/legs/summary of one (car, epoch) folder, all tolerant."""
    d = os.path.join(row["dir"], f"e{row['epoch']:03d}")
    samples = read_csv_rows(os.path.join(d, "samples.csv"))
    events = clean_events(read_jsonl(os.path.join(d, "events.jsonl")))
    legs = read_csv_rows(os.path.join(d, "legs.csv"))
    summary = read_json(os.path.join(d, "summary.json"))
    return samples, events, legs, summary


def sessions_in(root):
    base = os.path.join(root, "aicar")
    if not os.path.isdir(base):
        return []
    return sorted(d for d in os.listdir(base) if os.path.isdir(os.path.join(base, d)))


def pick_session(root, requested):
    """--session ID wins; else aicar/current_session.txt; else newest."""
    sessions = sessions_in(root)
    if not sessions:
        return None, []
    if requested:
        return (requested if requested in sessions else None), sessions
    current = ""
    p = os.path.join(root, "aicar", "current_session.txt")
    try:
        current = open(p).read().strip()
    except OSError:
        pass
    if current in sessions:
        return current, sessions
    return sessions[-1], sessions


# ---------------------------------------------------------------- metrics

def compute_score(minutes, distance_m, arrived, coll_s, coll_d, coll_hard, reverses,
                  teleports, flips, offnav_pct, idle_pct):
    """Useful progress per minute minus incident costs per minute. Higher is better.
    Same formula as the old per-car score, applied to merged epoch totals (car-minutes)."""
    if minutes <= 0:
        return 0.0
    per_min = lambda k: k / minutes
    return (distance_m / minutes / 10.0
            + 20.0 * per_min(arrived)
            - 4.0 * per_min(coll_s)
            - 2.0 * per_min(coll_d)
            - 6.0 * per_min(coll_hard)
            - 3.0 * per_min(reverses)
            - 25.0 * per_min(teleports)
            - 25.0 * per_min(flips)
            - 0.5 * offnav_pct
            - 0.3 * idle_pct)


def row_metrics(row):
    """Metrics of one (car, epoch) row straight from epochs.csv (always available)."""
    minutes = max(row["duration"] / 60.0, 0.01)
    m = dict(row)
    m["minutes"] = minutes
    m["distance_m"] = row["distance"] / UNITS_PER_METER
    m["offnav_pct"] = 100.0 * row["offnav_time"] / minutes / 60.0
    m["idle_pct"] = 100.0 * row["idle_time"] / minutes / 60.0
    m["score"] = compute_score(minutes, m["distance_m"], row["arrived"],
                               row["collisions_static"], row["collisions_dynamic"], row["collisions_hard"],
                               row["reverses"], row["teleports"], row["flips"],
                               m["offnav_pct"], m["idle_pct"])
    return m


def path_stats(samples):
    """Path deviation and speed-vs-target stats from Driving samples."""
    drive = [r for r in samples if r.get("state") == "Driving"]
    if not drive:
        return 0.0, 0.0, 0.0, 0.0
    devs = [abs(fnum(r, "pathdev")) for r in drive]
    mean = sum(devs) / len(devs)
    p95 = sorted(devs)[int(0.95 * (len(devs) - 1))]
    slow = [r for r in drive if fnum(r, "target") > 100 and fnum(r, "fwd") < 0.5 * fnum(r, "target")]
    over = [r for r in drive if fnum(r, "fwd") > fnum(r, "target") + 150]
    return mean, p95, 100.0 * len(slow) / len(drive), 100.0 * len(over) / len(drive)


# ---------------------------------------------------------------- detail parsing (event 'detail' strings)

def detail_objects(detail):
    """Object names mentioned in an event detail (whiskers, scan, other=, ground=)."""
    names = []
    m = re.search(r"whiskers=(\S+)", detail)
    if m and m.group(1) != "clear":
        for part in m.group(1).split("|"):
            bits = part.split(":", 2)
            if len(bits) == 3:
                names.append(bits[2])
    m = re.search(r"other=(.+?) kind=", detail)
    if m:
        names.append(m.group(1))
    return names


def scan_close(detail, within=250):
    """Scan rays closer than `within`: list of (angle, dist, name)."""
    m = re.search(r"scan=(.+?)(?: ground=|$)", detail)
    out = []
    if not m or m.group(1) == "clear":
        return out
    for part in m.group(1).split("|"):
        bits = part.split(":", 2)
        if len(bits) == 3:
            try:
                a, d = float(bits[0]), float(bits[1])
            except ValueError:
                continue
            if d <= within:
                out.append((a, d, bits[2]))
    return out


def ground_of(detail):
    m = re.search(r"ground=(.+)$", detail)
    return m.group(1).strip() if m else None


# ---------------------------------------------------------------- epoch key grouping

def appearance_order(rows):
    return sorted(rows, key=lambda r: (r["start_t"], r["car"], r["epoch"]))


def group_by_key(rows):
    """[(key, [rows...])] in order of first appearance."""
    order, groups = [], {}
    for r in rows:
        if r["key"] not in groups:
            groups[r["key"]] = []
            order.append(r["key"])
        groups[r["key"]].append(r)
    return [(k, groups[k]) for k in order]


def merge_key(key, rows):
    """Totals for one epoch key merged across cars: sums of counts and car-minutes."""
    minutes = sum(r["duration"] for r in rows) / 60.0
    duration = max(sum(r["duration"] for r in rows), 0.01)
    tot = lambda k: sum(r[k] for r in rows)
    m = {
        "key": key,
        "version": rows[0]["version"],
        "tuning_revision": rows[0]["tuning_revision"],
        "cars": len({r["car"] for r in rows}),
        "minutes": minutes,
        "closed": all(r["closed"] for r in rows),
        "distance_m": tot("distance") / UNITS_PER_METER,
        "arrived": tot("arrived"),
        "legs": tot("legs"),
        "blocked": tot("blocked"),
        "reverses": tot("reverses"),
        "collisions": tot("collisions"),
        "collisions_static": tot("collisions_static"),
        "collisions_dynamic": tot("collisions_dynamic"),
        "collisions_hard": tot("collisions_hard"),
        "teleports": tot("teleports"),
        "flips": tot("flips"),
        "offnav_pct": 100.0 * tot("offnav_time") / duration,
        "idle_pct": 100.0 * tot("idle_time") / duration,
        "weave_per_min": tot("steer_sign_flips") / max(minutes, 0.01),
    }
    m["score"] = compute_score(minutes, m["distance_m"], m["arrived"],
                               m["collisions_static"], m["collisions_dynamic"], m["collisions_hard"],
                               m["reverses"], m["teleports"], m["flips"],
                               m["offnav_pct"], m["idle_pct"])
    return m


def apply_since(rows, since):
    """Filter rows to `since` (an epoch key, version or tuning revision) onwards.
    Returns (rows, warning_or_None)."""
    if not since:
        return rows, None
    for i, r in enumerate(rows):
        if since == r["key"] or since == r["version"] or since == r["tuning_revision"] or since in r["key"]:
            return rows[i:], None
    keys = ", ".join(sorted({r["key"] for r in rows})) or "(none)"
    return rows, f"WARN --since-epoch '{since}' matched no key; known keys: {keys} (showing all)"


# ---------------------------------------------------------------- report pieces

def fmt(v, d=0):
    return f"{v:.{d}f}"


def esc(key):
    return key.replace("|", "\\|")


def comparison_table(groups):
    lines = [
        "| epoch key | cars | car-min | score | m/min | arr/min | blk/min | rev/min | coll s/d/h per min | tele | flip | offnav% | idle% | status |",
        "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|",
    ]
    for key, rows in groups:
        m = merge_key(key, rows)
        minutes = m["minutes"]
        per_min = lambda k: m[k] / minutes if minutes > 0 else 0.0
        status = "closed" if m["closed"] else "OPEN"
        if minutes < MIN_CAR_MINUTES:
            status += ", insufficient (<3 car-min)"
        lines.append(
            f"| {esc(m['key'])} | {m['cars']} | {fmt(minutes,1)} | {fmt(m['score'],1)} | "
            f"{fmt(m['distance_m'] / minutes if minutes > 0 else 0)} | {fmt(per_min('arrived'),2)} | "
            f"{fmt(per_min('blocked'),2)} | {fmt(per_min('reverses'),2)} | "
            f"{m['collisions_static']}/{m['collisions_dynamic']}/{m['collisions_hard']} "
            f"({fmt(per_min('collisions'),2)}) | {int(m['teleports'])} | {int(m['flips'])} | "
            f"{fmt(m['offnav_pct'])} | {fmt(m['idle_pct'])} | {status} |")
    return lines


def hotspot_lines(incidents, top=8, label="Hotspots (800u cells)"):
    """Grid-cell hotspots for a list of (car_id, event)."""
    lines = []
    cell = 800.0
    cells = defaultdict(list)
    for cid, e in incidents:
        key = (math.floor(e["x"] / cell), math.floor(e["y"] / cell))
        cells[key].append((cid, e))
    hot = sorted(cells.items(), key=lambda kv: -len(kv[1]))[:top]
    if hot:
        lines.append(f"## {label}")
        for i, (key, evs) in enumerate(hot):
            cx = sum(e["x"] for _, e in evs) / len(evs)
            cy = sum(e["y"] for _, e in evs) / len(evs)
            types = Counter(e["type"] for _, e in evs)
            objs = Counter(n for _, e in evs for n in detail_objects(e.get("detail", "")))
            close = Counter(n for _, e in evs for (_, _, n) in scan_close(e.get("detail", "")))
            cars_here = sorted({cid for cid, _ in evs})
            lines.append(
                f"{i+1}. ({fmt(cx)},{fmt(cy)}) n={len(evs)} cars={','.join(cars_here)} "
                f"[{', '.join(f'{k} {v}' for k, v in types.most_common())}] "
                f"hit: {', '.join(f'{n} x{c}' for n, c in objs.most_common(3)) or '-'}; "
                f"close (<250u): {', '.join(f'{n} x{c}' for n, c in close.most_common(3)) or '-'}")
    return hot, lines


# ---------------------------------------------------------------- full report (non-live)

def report_session(root, session, out_dir, maps, since):
    session_dir = os.path.join(root, "aicar", session)
    rows, old, nav = load_session(session_dir)
    lines = [f"# AI car session {session}"]
    if not rows:
        if old:
            lines.append(f"UNSUPPORTED: session '{session}' uses the old pre-epoch layout "
                         f"(cars {', '.join(old)} have summary.json but no epochs.csv). "
                         "Epoch analysis needs data written by the current exporter; this session "
                         "cannot be compared per epoch key.")
        else:
            lines.append("no cars recorded")
        return "\n".join(lines)

    ordered = appearance_order(rows)
    ordered, warn = apply_since(ordered, since)
    if warn:
        lines.append(warn)
    groups = group_by_key(ordered)

    # Epoch comparison: one row per key, merged across cars, by first appearance.
    lines.append("")
    lines.append("## Epoch comparison (score merged across cars per key)")
    lines.extend(comparison_table(groups))
    lines.append("")
    lines.append(f"(keys with under {MIN_CAR_MINUTES:.0f} car-minutes are 'insufficient': judge them by trend, not score)")

    # Per car per epoch detail.
    lines.append("")
    lines.append("## Per car, per epoch")
    lines.append("| car | e | epoch key | min | score | m/min | arr/legs | blk rev | coll s/d/h | tele | flip | offnav% | idle% | weave/min | dev mean/p95 | under% | over% |")
    lines.append("|" + "---|" * 17)
    all_incidents = []
    all_legs = []
    details = {}
    for r in ordered:
        samples, events, legs, summary = load_epoch_detail(r)
        details[(r["car"], r["epoch"])] = (samples, events)
        for e in events:
            if e.get("type") in INCIDENTS:
                all_incidents.append((r["car"], e))
        all_legs.extend(legs)
        m = row_metrics(r)
        mean, p95, under, over = path_stats(samples)
        lines.append(
            f"| {r['car']} | {r['epoch']} | {esc(r['key'])} | {fmt(r['duration']/60.0,1)} | {fmt(m['score'],1)} | "
            f"{fmt(m['distance_m'] / m['minutes'])} | {int(r['arrived'])}/{int(r['legs'])} | "
            f"{int(r['blocked'])} {int(r['reverses'])} | "
            f"{int(r['collisions_static'])}/{int(r['collisions_dynamic'])}/{int(r['collisions_hard'])} | "
            f"{int(r['teleports'])} | {int(r['flips'])} | {fmt(m['offnav_pct'])} | {fmt(m['idle_pct'])} | "
            f"{fmt(r['steer_sign_flips'] / m['minutes'],1)} | {fmt(mean)}/{fmt(p95)} | {fmt(under)} | {fmt(over)} |")

    # Incidents.
    lines.append("")
    lines.append("## Incidents")
    type_counts = Counter(e["type"] for _, e in all_incidents)
    lines.append(", ".join(f"{k} {v}" for k, v in type_counts.most_common()) or "none")
    objects = Counter()
    for _, e in all_incidents:
        for n in detail_objects(e.get("detail", "")):
            objects[n] += 1
    if objects:
        lines.append("objects involved: " + ", ".join(f"{n} x{c}" for n, c in objects.most_common(12)))
    grounds = Counter(g for _, e in all_incidents if (g := ground_of(e.get("detail", ""))))
    if grounds:
        lines.append("ground under car at incidents: " + ", ".join(f"{n} x{c}" for n, c in grounds.most_common(6)))
    reasons = Counter()
    for _, e in all_incidents:
        if e["type"] == "Reverse":
            m = re.search(r"trigger=(\w+)", e.get("detail", ""))
            reasons[m.group(1) if m else "?"] += 1
    if reasons:
        lines.append("reverse triggers: " + ", ".join(f"{k} {v}" for k, v in reasons.items()))

    _, hl = hotspot_lines(all_incidents)
    lines.extend(hl)

    # Legs.
    if all_legs:
        outcomes = Counter(l.get("outcome", "?") for l in all_legs)
        done = [l for l in all_legs if l.get("outcome") == "arrived"]
        lines.append("")
        lines.append("## Legs")
        lines.append(", ".join(f"{k} {v}" for k, v in outcomes.most_common()))
        if done:
            try:
                eff = [float(l["planned"]) / max(float(l["driven"]), 1.0) for l in done]
                spd = [float(l["avg_speed"]) for l in done]
                lines.append(f"arrived legs: route efficiency (planned/driven) mean {sum(eff)/len(eff):.2f}, avg speed {sum(spd)/len(spd):.0f} u/s")
            except (KeyError, TypeError, ValueError):
                pass

    lines.append("")
    lines.append("## Incident samples (first 10)")
    for cid, e in all_incidents[:10]:
        d = e.get("detail", "")
        d = re.sub(r"path=\S+", "path=...", d)
        if len(d) > 260:
            d = d[:260] + "..."
        lines.append(f"- {cid} t={e.get('t','?')} {e.get('type','?')} @({fmt(e['x'])},{fmt(e['y'])}) "
                     f"spd={e.get('speed','?')} st={e.get('state','?')}: {d}")

    if maps:
        try:
            written = draw_maps_by_key(groups, details, nav, out_dir, session)
            lines.append("")
            lines.append(f"maps: {written} (one overview per epoch key, in {out_dir})")
        except ImportError:
            lines.append("(matplotlib missing, no maps)")

    return "\n".join(lines)


def history(root):
    """One row per (session, epoch key) across all sessions, by first appearance."""
    rows = ["## History (all sessions, per epoch key)", "",
            "| session | epoch key | cars | car-min | score | tele | flips | status |",
            "|" + "---|" * 8]
    for session in sessions_in(root):
        srows, _, _ = load_session(os.path.join(root, "aicar", session))
        if not srows:
            continue
        for key, grp in group_by_key(appearance_order(srows)):
            m = merge_key(key, grp)
            rows.append(f"| {session} | {esc(m['key'])} | {m['cars']} | {fmt(m['minutes'],1)} | "
                        f"{fmt(m['score'],1)} | {int(m['teleports'])} | {int(m['flips'])} | "
                        f"{'closed' if m['closed'] else 'OPEN'} |")
    return "\n".join(rows)


# ---------------------------------------------------------------- live report

def report_live(root, session, since):
    rows, old, _ = load_session(os.path.join(root, "aicar", session))
    if not rows:
        if old:
            return f"# live {session}\nUNSUPPORTED: old pre-epoch layout (cars: {', '.join(old)}), no epochs.csv"
        return f"# live {session}\nno epoch data yet"
    ordered = appearance_order(rows)
    ordered, warn = apply_since(ordered, since)
    groups = group_by_key(ordered)
    lines = [f"# live session {session}" + (f" since '{since}'" if since else "")]
    if warn:
        lines.append(warn)
    lines.append("")
    lines.extend(comparison_table(groups))

    # Open epoch hotspots: incidents from the still-open epochs only.
    open_rows = [r for r in ordered if not r["closed"]]
    incidents = []
    for r in open_rows:
        _, events, _, _ = load_epoch_detail(r)
        for e in events:
            if e.get("type") in INCIDENTS:
                incidents.append((r["car"], e))
    lines.append("")
    open_keys = ", ".join(sorted({esc(r["key"]) for r in open_rows})) or "none (play stopped?)"
    lines.append(f"## Open epoch {open_keys}: {len(incidents)} incidents")
    if incidents:
        counts = Counter(e["type"] for _, e in incidents)
        lines.append(", ".join(f"{k} {v}" for k, v in counts.most_common()))
        _, hl = hotspot_lines(incidents, top=5, label="Hotspots (800u cells)")
        lines.extend(hl)
    return "\n".join(lines)


# ---------------------------------------------------------------- maps

STATE_COLORS = {"Driving": "#2a9d8f", "Blocked": "#e9c46a", "Reversing": "#e76f51", "Idle": "#8d99ae"}
EVENT_STYLE = {"Collision": ("x", "#d62828"), "Blocked": ("s", "#f4a261"), "Reverse": ("v", "#e76f51"),
               "Teleport": ("*", "#7209b7"), "Flipped": ("P", "#000000"), "OffNav": ("o", "#3a86ff")}


def draw_maps_by_key(groups, details, nav, out_dir, session):
    """One overview PNG per epoch key; hotspot close-ups for the newest key. Returns count written."""
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    os.makedirs(out_dir, exist_ok=True)

    def base(ax, samples_by_car, incidents):
        if nav:
            ax.scatter([p[0] for p in nav], [p[1] for p in nav], s=1, c="#cccccc", zorder=1)
        for sample_rows in samples_by_car:
            pts = [(fnum(r, "x"), fnum(r, "y"), r.get("state", "")) for r in sample_rows]
            for i in range(1, len(pts)):
                (x0, y0, _), (x1, y1, st) = pts[i - 1], pts[i]
                if math.hypot(x1 - x0, y1 - y0) > 1500:
                    continue  # teleport
                ax.plot([x0, x1], [y0, y1], color=STATE_COLORS.get(st, "#555"), lw=1, zorder=2)
        for _, e in incidents:
            mk, col = EVENT_STYLE.get(e.get("type"), ("o", "#000"))
            ax.scatter([e["x"]], [e["y"]], marker=mk, c=col, s=28, zorder=3)
        ax.set_aspect("equal")

    written = 0
    for gi, (key, rows) in enumerate(groups):
        samples_by_car = []
        incidents = []
        for r in rows:
            samples, events, _, _ = details.get((r["car"], r["epoch"]), ([], [], [], {}))
            samples_by_car.append(samples)
            incidents += [(r["car"], e) for e in events if e.get("type") in INCIDENTS]
        slug = re.sub(r"[^A-Za-z0-9._-]+", "_", key).strip("_") or f"key{gi}"
        hot, _ = hotspot_lines(incidents, top=3, label="")
        fig, ax = plt.subplots(figsize=(11, 11))
        base(ax, samples_by_car, incidents)
        for i, (hk, evs) in enumerate(hot):
            cx = sum(e["x"] for _, e in evs) / len(evs)
            cy = sum(e["y"] for _, e in evs) / len(evs)
            ax.annotate(str(i + 1), (cx, cy), fontsize=12, weight="bold", color="#000")
        ax.set_title(f"{session} [{key}]  (grey=navmesh, line colour=state, markers=incidents)")
        fig.savefig(os.path.join(out_dir, f"overview_{slug}.png"), dpi=80, bbox_inches="tight")
        plt.close(fig)
        written += 1
        if gi == len(groups) - 1:  # close-ups only for the newest key: older configs are settled
            for i, (hk, evs) in enumerate(hot[:2]):
                cx = sum(e["x"] for _, e in evs) / len(evs)
                cy = sum(e["y"] for _, e in evs) / len(evs)
                fig, ax = plt.subplots(figsize=(7, 7))
                base(ax, samples_by_car, incidents)
                for _, e in evs:
                    for a, d, _ in scan_close(e.get("detail", ""), 800):
                        yaw = math.radians(fnum(e, "yaw") + a)
                        ax.plot([e["x"], e["x"] + math.cos(yaw) * d], [e["y"], e["y"] + math.sin(yaw) * d],
                                color="#999", lw=0.4, zorder=1)
                        ax.scatter([e["x"] + math.cos(yaw) * d], [e["y"] + math.sin(yaw) * d], s=3, c="#444", zorder=2)
                ax.set_xlim(cx - 1500, cx + 1500)
                ax.set_ylim(cy - 1500, cy + 1500)
                ax.set_title(f"hotspot {i+1} ({cx:.0f},{cy:.0f}) [{key}]")
                fig.savefig(os.path.join(out_dir, f"hotspot_{slug}_{i+1}.png"), dpi=70, bbox_inches="tight")
                plt.close(fig)
                written += 1
    return written


# ---------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser(description="Analyze AI car telemetry (epoch layout, live mode).")
    ap.add_argument("root", nargs="?", default=DEFAULT_ROOT,
                    help="telemetry root, default: project .aicar-telemetry")
    ap.add_argument("--session", help="session id, default: aicar/current_session.txt else newest")
    ap.add_argument("--live", action="store_true",
                    help="compact poll: epoch comparison table + open-epoch hotspots only")
    ap.add_argument("--since-epoch", metavar="KEY",
                    help="only compare/report epochs from this key (or version/revision) onwards")
    ap.add_argument("--maps", action="store_true", help="force map PNGs (default: on off-line, off in --live)")
    ap.add_argument("--no-maps", action="store_true", help="skip map PNGs")
    ap.add_argument("--out", help="map output dir, default: <root>/analysis/<session>")
    args = ap.parse_args()

    session, sessions = pick_session(args.root, args.session)
    if not sessions:
        print("no sessions found under", os.path.join(args.root, "aicar"))
        return 1
    if not session:
        print(f"session '{args.session}' not found; known: {', '.join(sessions)}")
        return 1

    maps = args.maps or (not args.live and not args.no_maps)
    out = args.out or os.path.join(args.root, "analysis", session)
    if args.live:
        print(report_live(args.root, session, args.since_epoch))
    else:
        print(report_session(args.root, session, out, maps, args.since_epoch))
        print()
        print(history(args.root))
    return 0


if __name__ == "__main__":
    sys.exit(main())
