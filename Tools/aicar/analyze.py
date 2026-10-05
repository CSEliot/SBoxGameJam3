#!/usr/bin/env python3
"""Analyze AI car telemetry pushed by Editor/AICarTelemetryExporter.cs.

Usage:
    python3 Tools/aicar/analyze.py <telemetry-root> [--session ID] [--out DIR] [--no-maps]

<telemetry-root> is a checkout of the telemetry branch (it contains aicar/<session>/<car>/).
Without --session the newest session is analyzed. Prints a compact markdown report (stdout)
and, unless --no-maps, writes map PNGs to --out (default: <telemetry-root>/analysis/<session>/).
A history table across all sessions is always included, so tuning changes can be compared.
See AI-Car-Feedback-Loop.md for what the numbers mean.
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


# ---------------------------------------------------------------- loading

def load_car(car_dir):
    car = {"dir": car_dir, "id": os.path.basename(car_dir)}
    path = os.path.join(car_dir, "summary.json")
    car["summary"] = json.load(open(path)) if os.path.exists(path) else {}
    car["samples"] = []
    path = os.path.join(car_dir, "samples.csv")
    if os.path.exists(path):
        with open(path, newline="") as f:
            for row in csv.DictReader(f):
                car["samples"].append(row)
    car["events"] = []
    path = os.path.join(car_dir, "events.jsonl")
    if os.path.exists(path):
        for line in open(path):
            line = line.strip()
            if line:
                try:
                    car["events"].append(json.loads(line))
                except json.JSONDecodeError:
                    pass
    car["legs"] = []
    path = os.path.join(car_dir, "legs.csv")
    if os.path.exists(path):
        with open(path, newline="") as f:
            car["legs"] = list(csv.DictReader(f))
    return car


def load_session(session_dir):
    cars = []
    for name in sorted(os.listdir(session_dir)):
        d = os.path.join(session_dir, name)
        if os.path.isdir(d) and os.path.exists(os.path.join(d, "summary.json")):
            cars.append(load_car(d))
    nav = []
    path = os.path.join(session_dir, "navsample.csv")
    if os.path.exists(path):
        with open(path, newline="") as f:
            nav = [(float(r["x"]), float(r["y"])) for r in csv.DictReader(f)]
    meta = {}
    path = os.path.join(session_dir, "meta.txt")
    if os.path.exists(path):
        for line in open(path):
            if "=" in line:
                k, v = line.strip().split("=", 1)
                meta[k] = v
    return cars, nav, meta


def sessions_in(root):
    base = os.path.join(root, "aicar")
    if not os.path.isdir(base):
        return []
    return sorted(d for d in os.listdir(base) if os.path.isdir(os.path.join(base, d)))


# ---------------------------------------------------------------- metrics

def fnum(row, key, default=0.0):
    try:
        return float(row.get(key, default))
    except (TypeError, ValueError):
        return default


def metrics(car):
    s = car["summary"]
    c = s.get("counters", {})
    duration = max(s.get("duration", 0.0), 0.01)
    minutes = duration / 60.0
    st = s.get("state_time", {})
    m = {
        "version": s.get("version", "?"),
        "duration": duration,
        "distance_m": s.get("distance", 0.0) / UNITS_PER_METER,
        "avg_speed": s.get("avg_driving_speed", 0.0),
        "legs": c.get("legs_started", 0),
        "arrived": c.get("legs_arrived", 0),
        "repick": c.get("legs_repick", 0),
        "teleports": c.get("teleports", 0),
        "blocked": c.get("blocked", 0),
        "reverses": c.get("reverses", 0),
        "reverses_stuck": c.get("reverses_stuck", 0),
        "collisions": c.get("collisions", 0),
        "collisions_static": c.get("collisions_static", 0),
        "collisions_dynamic": c.get("collisions_dynamic", 0),
        "collisions_hard": c.get("collisions_hard", 0),
        "flips": c.get("flips", 0),
        "offnav_pct": 100.0 * s.get("offnav_time", 0.0) / duration,
        "driving_pct": 100.0 * st.get("Driving", 0.0) / duration,
        "blocked_pct": 100.0 * st.get("Blocked", 0.0) / duration,
        "reversing_pct": 100.0 * st.get("Reversing", 0.0) / duration,
        "idle_pct": 100.0 * st.get("Idle", 0.0) / duration,
        "weave_per_min": s.get("steer_sign_flips", 0) / minutes,
        "max_impact": s.get("max_impact_speed", 0.0),
    }

    drive = [r for r in car["samples"] if r.get("state") == "Driving"]
    if drive:
        devs = [abs(fnum(r, "pathdev")) for r in drive]
        m["pathdev_mean"] = sum(devs) / len(devs)
        m["pathdev_p95"] = sorted(devs)[int(0.95 * (len(devs) - 1))]
        slow = [r for r in drive if fnum(r, "target") > 100 and fnum(r, "fwd") < 0.5 * fnum(r, "target")]
        m["underspeed_pct"] = 100.0 * len(slow) / len(drive)
        over = [r for r in drive if fnum(r, "fwd") > fnum(r, "target") + 150]
        m["overspeed_pct"] = 100.0 * len(over) / len(drive)
    else:
        m["pathdev_mean"] = m["pathdev_p95"] = m["underspeed_pct"] = m["overspeed_pct"] = 0.0

    # Score: useful progress per minute, minus incident costs per minute. Higher is better.
    per_min = lambda k: m[k] / minutes
    m["score"] = (
        m["distance_m"] / minutes / 10.0
        + 20.0 * per_min("arrived")
        - 4.0 * per_min("collisions_static")
        - 2.0 * per_min("collisions_dynamic")
        - 6.0 * per_min("collisions_hard")
        - 3.0 * per_min("reverses")
        - 25.0 * per_min("teleports")
        - 25.0 * per_min("flips")
        - 0.5 * m["offnav_pct"]
        - 0.3 * m["idle_pct"]
    )
    return m


# ---------------------------------------------------------------- detail parsing

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


# ---------------------------------------------------------------- report

def fmt(v, d=0):
    return f"{v:.{d}f}"


def report_session(root, session, out_dir, maps):
    cars, nav, meta = load_session(os.path.join(root, "aicar", session))
    lines = [f"# AI car session {session}"]
    if meta:
        lines.append(f"code {meta.get('code_commit', '?')} (dirty: {meta.get('code_dirty', '?')})")
    if not cars:
        lines.append("no cars recorded")
        return "\n".join(lines)

    lines.append("")
    lines.append("| car | ver | min | m | arrive/legs | score | drv% | blk% | rev% | idle% | offnav% | coll s/d/hard | rev(stuck) | tele | flip | weave/min | dev mean/p95 | under% | over% |")
    lines.append("|" + "---|" * 19)
    all_m = []
    for car in cars:
        m = metrics(car)
        all_m.append(m)
        lines.append(
            f"| {car['id']} | {m['version']} | {fmt(m['duration']/60,1)} | {fmt(m['distance_m'])} | {m['arrived']}/{m['legs']} | "
            f"{fmt(m['score'],1)} | {fmt(m['driving_pct'])} | {fmt(m['blocked_pct'])} | {fmt(m['reversing_pct'])} | {fmt(m['idle_pct'])} | "
            f"{fmt(m['offnav_pct'])} | {m['collisions_static']}/{m['collisions_dynamic']}/{m['collisions_hard']} | "
            f"{m['reverses']}({m['reverses_stuck']}) | {m['teleports']} | {m['flips']} | {fmt(m['weave_per_min'],1)} | "
            f"{fmt(m['pathdev_mean'])}/{fmt(m['pathdev_p95'])} | {fmt(m['underspeed_pct'])} | {fmt(m['overspeed_pct'])} |")

    # Incidents: counts, objects, hotspots.
    incidents = []
    for car in cars:
        for e in car["events"]:
            if e.get("type") in INCIDENTS:
                incidents.append((car["id"], e))

    lines.append("")
    lines.append("## Incidents")
    type_counts = Counter(e["type"] for _, e in incidents)
    lines.append(", ".join(f"{k} {v}" for k, v in type_counts.most_common()) or "none")

    objects = Counter()
    for _, e in incidents:
        for n in detail_objects(e.get("detail", "")):
            objects[n] += 1
    if objects:
        lines.append("objects involved: " + ", ".join(f"{n} x{c}" for n, c in objects.most_common(12)))

    grounds = Counter(g for _, e in incidents if (g := ground_of(e.get("detail", ""))))
    if grounds:
        lines.append("ground under car at incidents: " + ", ".join(f"{n} x{c}" for n, c in grounds.most_common(6)))

    reasons = Counter()
    for _, e in incidents:
        if e["type"] == "Reverse":
            m = re.search(r"trigger=(\w+)", e.get("detail", ""))
            reasons[m.group(1) if m else "?"] += 1
    if reasons:
        lines.append("reverse triggers: " + ", ".join(f"{k} {v}" for k, v in reasons.items()))

    # Hotspots: 800-unit grid cells.
    cell = 800.0
    cells = defaultdict(list)
    for cid, e in incidents:
        key = (math.floor(e["x"] / cell), math.floor(e["y"] / cell))
        cells[key].append((cid, e))
    hot = sorted(cells.items(), key=lambda kv: -len(kv[1]))[:8]
    if hot:
        lines.append("")
        lines.append("## Hotspots (800u cells)")
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

    # Legs.
    legs = [l for car in cars for l in car["legs"]]
    if legs:
        outcomes = Counter(l["outcome"] for l in legs)
        done = [l for l in legs if l["outcome"] == "arrived"]
        lines.append("")
        lines.append("## Legs")
        lines.append(", ".join(f"{k} {v}" for k, v in outcomes.most_common()))
        if done:
            eff = [float(l["planned"]) / max(float(l["driven"]), 1.0) for l in done]
            spd = [float(l["avg_speed"]) for l in done]
            lines.append(f"arrived legs: route efficiency (planned/driven) mean {sum(eff)/len(eff):.2f}, avg speed {sum(spd)/len(spd):.0f} u/s")

    # First few incident details, trimmed, as concrete examples.
    lines.append("")
    lines.append("## Incident samples (first 10)")
    for cid, e in incidents[:10]:
        d = e.get("detail", "")
        d = re.sub(r"path=\S+", "path=...", d)
        if len(d) > 260:
            d = d[:260] + "..."
        lines.append(f"- {cid} t={e['t']} {e['type']} @({e['x']},{e['y']}) spd={e['speed']} st={e['state']}: {d}")

    if maps:
        try:
            draw_maps(cars, nav, incidents, hot, out_dir, session)
            lines.append("")
            lines.append(f"maps: {out_dir}")
        except ImportError:
            lines.append("(matplotlib missing, no maps)")

    return "\n".join(lines)


def history(root):
    rows = ["## History (all sessions)", "", "| session | version | cars | min | m/min | arrive/min | coll/min | rev/min | tele | flips | offnav% | score |", "|" + "---|" * 12]
    for session in sessions_in(root):
        cars, _, _ = load_session(os.path.join(root, "aicar", session))
        if not cars:
            continue
        ms = [metrics(c) for c in cars]
        minutes = sum(m["duration"] for m in ms) / 60.0
        if minutes < 0.2:
            continue
        tot = lambda k: sum(m[k] for m in ms)
        versions = ",".join(sorted({m["version"] for m in ms}))
        score = sum(m["score"] * m["duration"] for m in ms) / max(sum(m["duration"] for m in ms), 0.01)
        offnav = sum(m["offnav_pct"] * m["duration"] for m in ms) / max(sum(m["duration"] for m in ms), 0.01)
        rows.append(f"| {session} | {versions} | {len(cars)} | {minutes:.1f} | {tot('distance_m')/minutes:.0f} | {tot('arrived')/minutes:.2f} | "
                    f"{tot('collisions')/minutes:.1f} | {tot('reverses')/minutes:.1f} | {tot('teleports')} | {tot('flips')} | {offnav:.0f} | {score:.1f} |")
    return "\n".join(rows)


# ---------------------------------------------------------------- maps

STATE_COLORS = {"Driving": "#2a9d8f", "Blocked": "#e9c46a", "Reversing": "#e76f51", "Idle": "#8d99ae"}
EVENT_STYLE = {"Collision": ("x", "#d62828"), "Blocked": ("s", "#f4a261"), "Reverse": ("v", "#e76f51"),
               "Teleport": ("*", "#7209b7"), "Flipped": ("P", "#000000"), "OffNav": ("o", "#3a86ff")}


def draw_maps(cars, nav, incidents, hot, out_dir, session):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    os.makedirs(out_dir, exist_ok=True)

    def base(ax):
        if nav:
            ax.scatter([p[0] for p in nav], [p[1] for p in nav], s=1, c="#cccccc", zorder=1)
        for car in cars:
            pts = [(fnum(r, "x"), fnum(r, "y"), r.get("state", "")) for r in car["samples"]]
            for i in range(1, len(pts)):
                (x0, y0, _), (x1, y1, st) = pts[i - 1], pts[i]
                if math.hypot(x1 - x0, y1 - y0) > 1500:
                    continue  # teleport
                ax.plot([x0, x1], [y0, y1], color=STATE_COLORS.get(st, "#555"), lw=1, zorder=2)
        for _, e in incidents:
            mk, col = EVENT_STYLE.get(e["type"], ("o", "#000"))
            ax.scatter([e["x"]], [e["y"]], marker=mk, c=col, s=28, zorder=3)
        ax.set_aspect("equal")

    fig, ax = plt.subplots(figsize=(11, 11))
    base(ax)
    for i, (key, evs) in enumerate(hot):
        cx = sum(e["x"] for _, e in evs) / len(evs)
        cy = sum(e["y"] for _, e in evs) / len(evs)
        ax.annotate(str(i + 1), (cx, cy), fontsize=12, weight="bold", color="#000")
    ax.set_title(f"{session}  (grey=navmesh, line colour=state, markers=incidents)")
    fig.savefig(os.path.join(out_dir, "overview.png"), dpi=80, bbox_inches="tight")
    plt.close(fig)

    for i, (key, evs) in enumerate(hot[:4]):
        cx = sum(e["x"] for _, e in evs) / len(evs)
        cy = sum(e["y"] for _, e in evs) / len(evs)
        fig, ax = plt.subplots(figsize=(7, 7))
        base(ax)
        # Scan rays of the incidents here: short segments to what the car saw.
        for _, e in evs:
            for a, d, _ in scan_close(e.get("detail", ""), 800):
                yaw = math.radians(e.get("yaw", 0.0) + a)
                ax.plot([e["x"], e["x"] + math.cos(yaw) * d], [e["y"], e["y"] + math.sin(yaw) * d], color="#999", lw=0.4, zorder=1)
                ax.scatter([e["x"] + math.cos(yaw) * d], [e["y"] + math.sin(yaw) * d], s=3, c="#444", zorder=2)
        ax.set_xlim(cx - 1500, cx + 1500)
        ax.set_ylim(cy - 1500, cy + 1500)
        ax.set_title(f"hotspot {i+1} ({cx:.0f},{cy:.0f})")
        fig.savefig(os.path.join(out_dir, f"hotspot{i+1}.png"), dpi=70, bbox_inches="tight")
        plt.close(fig)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("root")
    ap.add_argument("--session")
    ap.add_argument("--out")
    ap.add_argument("--no-maps", action="store_true")
    args = ap.parse_args()

    sessions = sessions_in(args.root)
    if not sessions:
        print("no sessions found under", os.path.join(args.root, "aicar"))
        return 1
    session = args.session or sessions[-1]
    out = args.out or os.path.join(args.root, "analysis", session)
    print(report_session(args.root, session, out, not args.no_maps))
    print()
    print(history(args.root))
    return 0


if __name__ == "__main__":
    sys.exit(main())
