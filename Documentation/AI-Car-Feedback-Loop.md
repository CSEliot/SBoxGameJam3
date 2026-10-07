# AI car feedback loop (live)

A closed loop for improving `AICarDriver` in `scenes/minimal.scene` where the only human steps
are: **start play, walk away, come back and stop play**. The analysis happens WHILE the game runs.
A local LLM agent on the same machine polls the telemetry the game is writing right now, changes
runtime values or code, and watches the effect appear as a new measurement epoch, without ever
stopping the session and without any git round trip.

## Why this design

The constraints:

- The driver can only be judged in the real game: bugge's vehicle physics, the city geometry and
  the baked navmesh. There is no headless simulator.
- Stopping and restarting play loses warm-up state and wastes the scarcest resource: wall-clock
  driving time. One long session should serve many tuning iterations.
- The agent is local (same disk, shell access, sees the editor log), so telemetry does not need
  to travel anywhere; the analysis just reads the files the game writes.

So the game measures itself continuously, the editor relays the agent's changes back into the
running game, and telemetry is segmented into epochs so before/after comparisons work inside one
continuous session.

## The live loop

```
 you: Play ──► AICarDriver drives (3 cars: the scene car + 2 test clones), one session runs
                │  AICarDriver.Telemetry.cs records 2 Hz samples + incident events per EPOCH
                │  AICarTelemetrySink ──► Editor/AICarTelemetryExporter.cs
                │  mirrors files to PROJECT/.aicar-telemetry/ every 5 s, playing or not
                ▼
 LLM agent (local): polls ──► python3 Tools/aicar/analyze.py --live
                │  reads the epoch comparison table + open-epoch hotspots
                │  decides it has enough data, makes ONE change:
                │    runtime value: edits .aicar-telemetry/tuning.json   (no compile)
                │    logic:         edits Code/AICarDriver*.cs, bumps DriverVersion (hotload)
                ▼
 editor: relays tuning.json into the game (SetTuning, 1 s poll) and forces a project recompile
                │  when Code/*.cs change (debounced); game opens a NEW EPOCH on the new config
                ▼
 LLM agent: confirms the new epoch key appears, lets it run to enough car-minutes, compares
                │  score vs the previous epoch, keeps or reverts the change, iterates
                ▼
 you: come back, Stop ──► every car closes its open epoch (closed=1), the loop ends
```

No play stop/start and no remote round trip: everything crosses the editor boundary through the
`.aicar-telemetry/` folder and hotload.

## Epochs: how one session compares configurations

An **epoch** is a stretch of driving under one distinct `(DriverVersion, tuning)` configuration.
A new epoch opens the moment the applied configuration changes: a code hotload bumps
`DriverVersion`, or a changed `tuning.json` is relayed into the game. The epoch key is
`"{DriverVersion}|{TuningRevision}"`. `TuningRevision` is `none` when no tuning file applies,
otherwise `<revision>#<hash>`: the file's `revision` label plus a 6-hex hash of the values that
actually landed. So `v12|none`, `v12|t3-shorter-stop#a91c04` and `v12|t3-shorter-stop#5be210`
(same label, different numbers) are three separate rows, and re-applying identical values (a
revert) maps back onto the same key on every car. Closing an epoch ends the active leg with
outcome `epoch-split`, writes the epoch's files one final time with `closed=1`, and drops its
buffers, so a multi-hour session stays bounded in memory.

A leg that is in progress when an epoch splits is recorded twice: once as `epoch-split` in the old
epoch, then restarted in the new one. `legs` and `arrived` per epoch are therefore per-epoch leg
records, not physical journeys; compare rates between epochs, don't sum legs across them.

## Telemetry layout on disk

Everything lives under `PROJECT/.aicar-telemetry/`, rewritten every 5 s (flush interval). Files
are never written atomically, so the analyzer tolerates a truncated last CSV line or bad JSON line
and just skips it.

```
PROJECT/.aicar-telemetry/
    tuning.json                              the agent's runtime-values file (below)
    aicar/current_session.txt                id of the most recently STARTED session (unchanged when play stops)
    aicar/<session>/navsample.csv            4000 random navmesh points (how the analyzer sees the map)
    aicar/<session>/<car>/epochs.csv         one row per epoch INCLUDING the open one; rewritten every flush
    aicar/<session>/<car>/e<NNN>/samples.csv   2 Hz driving samples, t = seconds since session start
    aicar/<session>/<car>/e<NNN>/events.jsonl  state changes + incidents with context, plus "epoch":N
    aicar/<session>/<car>/e<NNN>/legs.csv      one row per destination leg
    aicar/<session>/<car>/e<NNN>/summary.json  totals/counters/state_time scoped to THIS epoch,
                                               plus key, tuning_revision, tuning, start_t, closed, config
```

`epochs.csv` header:
`epoch,key,version,tuning_revision,start_t,duration,closed,distance,arrived,legs,blocked,reverses,collisions,collisions_static,collisions_dynamic,collisions_hard,teleports,flips,offnav_time,idle_time,steer_sign_flips`

Old sessions (pre-epoch layout: `summary.json` per car, no `epochs.csv`) are reported by
`analyze.py` as unsupported. They have no epoch segmentation, so they cannot be compared per key.

## The tuning file (runtime values, no compile)

The agent writes `PROJECT/.aicar-telemetry/tuning.json`; the editor polls it every 1 s and relays
the text into the game via `AICarTelemetrySink.SetTuning`. The driver applies it on the next
fixed update (file values win over code tuning; unknown property names are skipped with one
`Log.Warning` each). Deleting the file removes the tuning. Format:

```json
{ "revision": "t3-shorter-stop", "values": { "StopDistance": 80, "CruiseSpeed": 650 } }
```

- **Write it atomically**: write `tuning.json.tmp`, then rename it over `tuning.json`
  (`os.replace` in Python, `mv` in a shell). The editor also ignores a file modified less than
  1 s ago, but an in-place editor save can still be caught half-written, which opens a junk epoch.
- `revision` is a human label for the experiment. Changing values without changing it still opens
  a new epoch (the key carries a hash of the values), but a new label per idea keeps the table
  readable. Commas, quotes, `|` and `#` in it are replaced with `_`; an empty label falls back to
  `file-v<N>`.
- `values` maps `AICarDriver` property names to numbers/booleans; they are converted to each
  property's type. Numeric tuning only: no logic in this file. `UseDevTuning` and
  `RecordTelemetry` are refused (they switch the loop itself; toggle them in the inspector).
- Tuning applies only while the driver's `UseDevTuning` is on. `false` restores all scene values
  and the revision reads `none`.
- The two test-car clones inherit the original car's captured scene values, so removing the file
  restores all three cars to the real scene values.

## Hotload rules the agent must respect

- `OnStart` does NOT re-run on a hotload. Anything reacting to a code change must run from a tick
  (that is why the tuning tick polls `DriverVersion`).
- Field initializers of NEWLY ADDED fields do not run for already-existing instances after a
  hotload; treat new fields as `default(T)` on live objects or initialize them lazily.
- A FAILED compile keeps the OLD assembly running, so the epoch key does NOT change. Never trust
  data until the new epoch appears (see the operating procedure).

## What gets measured

Every incident (Blocked, Reverse, Collision, Teleport, Flipped, OffNav) records **what was around
the car**: which whiskers hit which named object, a 16-ray 360-degree scan out to 800 units with
the object names, and the ground object under the car. That replaces seeing the scene: a hotspot
reads like "Blocked 6, Collision 4 at (-2300, 5100), hit: `street_lamp` x7, close: `curb_corner` x3".

Nothing is logged per frame beyond the 2 Hz sample stream. The console stays quiet; the files are
the channel.

## How epochs are judged

`analyze.py` prints one table row per **epoch key**, merged across the cars that drove under it
(counts and car-minutes summed), ordered by first appearance, with closed/open state. Keys with
under 3 car-minutes are flagged `insufficient`. The **score** is useful progress per minute minus
incident costs per minute (the same formula the old per-car report used):

```
score = metres/min / 10  + 20·arrivals/min
      − 4·static collisions/min − 2·dynamic collisions/min − 6·hard (>200 u/s) collisions/min
      − 3·reverses/min − 25·teleports/min − 25·flips/min − 0.5·offnav% − 0.3·idle%
```

The diagnostic columns point at causes:

| symptom | likely cause |
|---|---|
| high `weave/min` | steering oscillation: SteerResponse / FullSteerAngle / look-ahead too short |
| high `dev p95` + OffNav at corners | corner speed too high or look-ahead cutting corners |
| `under%` high, few incidents | over-cautious: whiskers/stop distance triggering on harmless geometry |
| `over%` high | can't shed speed: braking model or corner anticipation |
| reverses with `trigger=stuck` | pinned on something the whiskers miss (low curbs, thin poles, whisker height) |
| repeated hotspot, same object name | a specific prop or road feature the logic mishandles |
| teleports | a failure the driver could not recover from on its own: always worth a look |

## Using analyze.py

```
python3 Tools/aicar/analyze.py [root] [--session ID] [--live] [--since-epoch KEY]
                               [--maps | --no-maps] [--out DIR]
```

- `root` defaults to `<project>/.aicar-telemetry`; the session defaults to
  `aicar/current_session.txt`, else the newest session directory.
- `--live`: compact poll for the loop: the epoch comparison table plus the open epoch's incident
  hotspots, nothing else. No map code runs.
- `--since-epoch KEY`: restrict the comparison to epochs from this key (or version/revision
  substring) onwards, so the agent only sees the current experiment and its predecessors.
- Full mode (no `--live`): per-car per-epoch detail, incidents, legs, incident samples, history
  across all sessions, and map PNGs per epoch key (`--maps` to force them on, default on outside
  `--live`; needs matplotlib, harmless message without it).

## LLM operating procedure

You (the agent) run this while the user is away. Never stop the play session; never use git for
the loop.

1. **Poll.** Every ~60 s run `python3 Tools/aicar/analyze.py --live`. Cheap: one table, plus the
   open epoch's hotspots. Read the epoch key marked `OPEN`.
2. **Decide you have enough data on the open epoch.** Require at least 3 car-minutes (the table's
   `car-min` column; below that it says `insufficient`) AND either at least a handful of incidents
   or a score that has stopped moving between two consecutive polls. Never judge a change on less.
3. **Pick ONE change per iteration.** One idea only, or the comparison means nothing.
   - Runtime value (speeds, distances, gains, timeouts): write
     `.aicar-telemetry/tuning.json` atomically (temp file + rename) with a new `revision` label
     (e.g. `t4-wider-whisker`). No code touched, no compile needed.
   - Logic change: edit `Code/AICarDriver*.cs` and bump `AICarDriver.DriverVersion`. Respect the
     hotload rules above; put tunable constants into tuning names where you can.
4. **Confirm the epoch actually switched.** Within ~30 s (a couple of flushes + relay) the `--live`
   table must show a new OPEN row with your new key. If it does not:
   - tuning change: check the editor log line for the relay (it logs revision + length per change);
     check your JSON is not empty or malformed at the relay level.
   - code change: the editor logs `marked the game assembly for recompile` about 2 s after it
     sees the edit. If that line is missing, the exporter itself is not running (check for an
     editor-project compile error). If it is there, the compile probably FAILED and the old
     assembly is still running. Check the running engine build's log, e.g.
     `/home/cseliot/1-Development/1-SBox/1-Engine-Builds/Dev/sbox-public/game/logs/sbox-dev.log`,
     `grep 'Compile of'` and look for `Error |` lines. Fix the code, wait for a clean compile,
     then confirm the key.
5. **Let the new epoch run to step 2's threshold.** Ignore its first ~30 s of data if a hotspot
   list looks cold-started.
6. **Compare and decide.** New key's score vs the key it replaced (the `--live` table shows both;
   `--since-epoch` narrows it). Improvement or a fix for the specific hotspot you targeted: keep.
   Worse, or unchanged with a new incident type: REVERT the change (write back the previous
   tuning values, or restore the code and DriverVersion). Identical values hash to the same key,
   so a tuning revert continues under its old key; a code revert needs its DriverVersion back too.
7. **Diagnose with the hotspots, not the score alone.** The open-epoch hotspot lines tell you
   WHERE and with WHAT the incidents cluster; that picks the next change.
8. **Stop when the user is back.** Primary signal: `--live` shows no `OPEN` row (when play stops,
   every car closes its epoch with `closed=1`). Backup signal: no file under the session
   directory has been modified for more than ~60 s (flushes run every 5 s during play). Do NOT
   use `current_session.txt` for this: it only changes when a NEW session starts. Before
   stopping, leave `tuning.json` holding the best config found. Run one full `analyze.py`
   (no `--live`) and give the user the final epoch table.
9. **When the car is good:** bake the tuned values into `ai_car.prefab`, set `UseDevTuning` off
   and `ExtraTestCars` back to 0, and stop touching the loop.

One extra rule: the loop mutates live game behavior. Keep every edit inside the files named above;
never edit scenes, prefabs, or skills while play is running for the loop.

## Files

- `Code/AICarDriver.cs`: the driver (state machine, pure pursuit, whiskers, reversing, teleport)
- `Code/AICarDriver.Telemetry.cs`: per-epoch recording, incident context, test-car spawning
- `Code/AICarDriver.Tuning.cs`: applies code tuning + `tuning.json` overrides over scene values,
  detects config changes and opens epochs
- `Code/AICarTelemetrySink.cs`: hand-off from the sandboxed game to the editor, plus the tuning
  text `SetTuning` receives
- `Editor/AICarTelemetryExporter.cs`: mirrors sink files to `.aicar-telemetry/`, relays
  `tuning.json` into the game, marks the game assembly for recompile on any `Code/` .cs/.razor change
- `Tools/aicar/analyze.py`: epoch comparison, live mode, detail report and maps

---

# Campaign results: the cornering and stuck-recovery work (2026-10-06/07)

Section added at the end of the live-tuning campaign. Everything below comes from the epoch tables
of session `20261007-024815` (the long campaign session) and `20261007-163810` (the final check
run), read back with `Tools/aicar/analyze.py --live`. Figures that come from a subagent analysis
rather than the epoch tables are marked inline (subagent-recomputed, forensics).

## What the loop was asked to fix

Two symptoms, measured by the loop itself: cars that stop making progress at corners (`Blocked`,
`Reverse`) and cars that cannot free themselves and have to be teleported (the 25-point penalty in
the score). The metric that drove every decision was teleports per car-minute, with `Blocked` and
collisions per minute as the secondary columns.

## The version chain

Each step is one idea, compared against the previous epoch at matched car-minutes. Kept only if the
targeted column moved.

| version | idea | outcome |
|---|---|---|
| v13 | steer deadband 2 deg, yaw damping 0.3 s on path error only, tie-break toward path angle | kept |
| v14 | brake a backward roll before it sets; speed-scaled rear obstacle stop | kept |
| v15 | tilt guard: cut throttle when `WorldRotation.Up.z < 0.9` | kept |
| v16 | flank side probes (probe origin 0.45 hull length, gap radius clamp) | kept: inside-turn scrapes 0.25 vs 0.73/min (82.7 car-min) |
| v17 | corridor check: reject routes narrower than `MinCorridorWidth` 200 past a 200 u skip | kept |
| v17b | straight-back wedge escape (no heading change) | 0 pinned teleports in 51.8 car-min, but 82 strict bounce repeats ~1.6/min |
| v17c | alternate straight-back and a locked swing, gated on repeat at the same spot | kept |
| v18 | learned no-go zones: a spot that teleports a car twice becomes a rejected route region | kept, teleports 0.193/min |
| v18b | review fixes: two-pool fallback, repick anchor, merge radius 1.5x, episode gap, struct store | best measured config |

## Final numbers (best validated configuration)

`v18b-nogo-fixes | physcal-avoid30` closed at **201.9 car-min** on all 3 cars (session
`20261007-024815`):

    score    +39.9        m/min 402      arrivals/min 1.64
    blocked  3.92/min     reverses 5.40/min
    collisions 3.04/min   (553 static / 60 dynamic / 78 hard, subagent-recomputed)
    teleports 24 = 0.119/min    flips 0    inside-turn scrapes 0.14/min (at 156.7 car-min)

Reference points in the same session: v17c closed at 117.3 car-min, score +22.9, teleports
0.171/min. v18 base closed at 107.5 car-min, score +29.4, teleports 0.167/min (18 teleports; an
earlier draft said 0.193/min, which belongs to the 119.3 car-min window the v18 review used). So
against the pre-physcal v13 baseline the chain cut blocked-stop density by about half (7.39/min in
session `20261007-014136` to 3.92) and teleports by about half again (0.246/min at v13's 20.3
car-min to 0.119). Scoped honestly: against the in-session v13 window the blocked win is only
4.3 to 3.92, so the larger claim needs the pre-physcal baseline named. Caveat that matters when
reading the table: score drifts DOWN as an epoch grows (v17 base scored +37.9 at 20.8 car-min and
+13.9 at 183.8), so only matched-exposure comparisons are meaningful.

`AvoidSteerAngle` 30 was kept over the code default 45 on a 70.5 car-min window: blocked 4.27 vs
4.36, collisions 3.52 vs 4.09/min (hard 0.40 vs 0.68, dynamic 0.34 vs 0.67), scrapes 0.20 vs 0.23,
score +31.2 vs +29.4, teleports 0.24 vs 0.17/min. The hypothesis that avoidance was fighting the
turn (and so causing mid-corner `Blocked`) was refuted: blocked density did not move. The win is
fewer collisions, not fewer blocks.

## What the learned no-go zones actually do

The store records the position of a teleport whose reason is `stuck` or `repick-loop` (the code
deliberately excludes spawn, `lost`/flip and pivot escapes), as a running mean, and counts a
second hit only when it arrives at least `NoGoEpisodeGap` (30 s) after the last COUNTED hit, so two
cars jammed and teleported together count as one. At `NoGoZoneMinHits` 2 the zone becomes active,
and from then on route candidates that pass within `NoGoZoneRadius` of it (after the first
`CorridorCheckSkip` 200 u, the stretch the car already occupies) are rejected, as are teleport and
spawn targets. If every candidate is rejected the driver still commits the best one, by design: the
driver never idles.

Measured effect at 156.7 car-min: 3 zones active ((-131,-626), (-1314,3299), (-877,3366)), 76 of
323 legs rejected at least one route, teleport rate 0.109/min, the best of the campaign.

The honest limits of the mechanism, from the adversarial reviews:

- A zone protects only from its second hit, so the first two teleports at any new spot are the
  price of learning it. Of 20 teleports analysed, 4 to 5 were the activation hits themselves.
- The never-idle fallback commits a no-go route when nothing else passes, and at the OfficeSquare
  corner every road passes within 250 u of the corner, so this happens often: 8 of 87 such commits
  crossed an active zone, 2 of them ended in a teleport at the crossing.
- Route-start exemption loophole hypothesis was tested and refuted: 0 of 343 committed routes
  entered an active zone only inside the skipped first 200 u.
- Residual failures are mostly first hits at new spots (15 of 20 in the analysed window), which no
  learned store can prevent. Those need either scene fixes at the specific geometry or a better
  in-place escape; the v17c locked swing is what fails there.

## Corrections and traps found the hard way

- **Changing a property default in code does not reach live instances.** Hotload preserves each
  instance's serialized value, so a changed default only affects newly created objects. Live
  tunables have to go through `tuning.json` or the inspector, and the EFFECTIVE value should be
  read back from the epoch's `config` block, never from the code default.
- **Statics survive a hotload but are copied by FIELD NAME.** Renaming the no-go zone fields
  (v18's three parallel lists to v18b's `List<NoGoZone>`) silently emptied the store: all five
  learned zones were lost at that boundary. Same-shape hotloads keep their statics.
- **A struct in a `List<T>` cannot be mutated through the indexer** (`zones[i].Hits = x` is
  CS1612, the indexer returns a copy). Copy to a local, mutate, assign back.
- **Judge at matched exposure.** Within one version the score falls as the epoch grows; a short
  window on a good version can outscore a long window on a better one. Two policies came out of
  this: compare at equal car-minutes, and treat an early window that scores LOWER than a long
  reference window as a strong negative signal (it should have looked flattering).
- **Single-variable A/B is worth the wait.** The window at the end of the campaign was NOT one, and
  the cost of assuming it was is in the section below.

## Final checked window: two variables changed, so it decides nothing

The last window the user checked by eye is session `20261007-163810`, `30.8 car-min`, epoch key
`v18b-nogo-fixes | physcal-nogor330`:

    score    +19.3        m/min 424      arrivals/min 1.56
    blocked  4.49/min     reverses 6.57/min
    collisions 5.14/min   (118 static / 40 dynamic / 33 hard)
    teleports 12 = 0.39/min   (8 repick-loop, 1 stuck, 3 lost, plus 2 spawn)

Against the morning reference `v18b | physcal-avoid30` (201.9 car-min, teleports 0.119/min,
collisions 3.04/min) every incident rate is worse while throughput is actually up (424 vs 402
m/min). Two things differ between the two windows, not one:

1. `NoGoZoneRadius` 250 to 330 (the tuning change).
2. A live aggression experiment, applied through the inspector while play ran, and therefore
   invisible in the epoch key: `BaseAggression` 1 (was 0), `AggressiveCautionScale` 0.1 (was 0.35),
   `AggressiveDriveScale` 5 (was 2.5), `PushThroughAggression` 1.01 (was 0.75, meaning always shove
   a dynamic body instead of stopping for it).

The user suspected this himself ("I was playing around with aggression values... perhaps I didn't
turn it off soon enough") and the telemetry confirms it. The `config` block of that epoch's
`summary.json` carries those four values, and the 2 Hz samples agree: samples at `aggr >= 0.75` are
23.2% of the window against 5.0% in the morning reference, and `aggr > 0` 41.1% against 25.5%. A
driver at base aggression 1 with a tenth of its normal stop distance and a 5x drive scale will hit
things more often, which is exactly the column that moved.

**Withdrawn: the claim that `NoGoZoneRadius` 330 caused this regression.** The window cannot
attribute the delta to either variable, and the radius specifically has a much larger window
pointing the other way.

### The radius does have a large clean window, and it is not the bad one

After the tuning change at 09:21 the same session kept driving the radius-330 config for
**1,231.2 car-min** (`20261007-024815`, e018, same key `physcal-nogor330#8a9ea4`), on default
aggression (3.75% of its samples at `aggr >= 0.75`, i.e. no experiment in force). That window:

    score    +46.7        m/min 413      arrivals/min 1.93
    blocked  3.53/min     reverses 5.36/min
    collisions 2.90/min   (3118 static / 450 dynamic / 516 hard)
    teleports 175 = 0.142/min

That is better than the radius-250 window on score, blocked density, arrivals and collisions, and
about 19% worse on teleports (0.142 vs 0.119/min, z about 2.3 on the two counts, borderline). An
earlier draft of this section compared the final 30.8 car-min against only the 250 window and
called the radius change unsupported; the honest reading is the opposite: **the 1,231 car-min
radius-330 window is the larger dataset and it does not show harm.** Keep or revert 330 on other
grounds, not on this window.

Why the final window looked so bad anyway, beyond the aggression values:

- The store is scene-keyed and clears on play restart, so the 16:38 restart began with no learned
  zones: exactly **1** zone activation in the whole window and 2 of 76 legs rejecting any route,
  against 110 of 411 in the reference window. With an empty store the radius was close to inert, so
  it cannot be the cause of that window's failures.
- The failures there are one burst, not a rate: 11 of 14 teleports fall before t=400 s of a 615 s
  window, and car0 alone took 6, three of them `lost` at speeds 4021/1356/549 u/s with z down to
  -4349, i.e. a physics ejection rather than a routing failure.

The epoch table could not have caught the aggression confound: an inspector-level value change
opens no new epoch. The only place a covariate like this shows up is the epoch's `config` block, so
comparing the `config` blocks of the two epochs, not just their tuning revisions, is what makes an
A/B readable.

## Shipped defaults do not match the validated configuration

Worth flagging before anyone turns the loop off. The validated behaviour lives in
`.aicar-telemetry/tuning.json`, which is gitignored and not part of the repo. The values the repo
would actually use are the code defaults plus the `AICarDriver` overrides serialized in
`minimal.scene`:

| property | scene / code | validated tuning |
|---|---|---|
| `ThrottleResponse` | 125 (scene) | 900 |
| `CornerSpeed` | 700 (scene) | 350 |
| `CruiseSpeed` | 1200 (scene) | 1800 |
| `CornerAimCorridor` | 60 (scene) | 30 |
| `CornerAimTraceRadius` | 50 (scene), effective 30 after the clamp | unset; the tuned `WhiskerHeight` 45 moves the clamp instead (effective 40) |
| `WhiskerThickness` | 10 (scene) | 25 |
| `FullSteerAngle` | 70 (scene) | 70 (unset) |
| `AvoidSteerAngle` | 45 (code) | 30 |
| `NoGoZoneRadius` | 250 (code) | 250 (validated) / 330 (larger window) |
| `CornerBrakeDecel` | 300 (code) | 240 |
| `WhiskerHeight` | 35 (code) | 45 |

Turning `UseDevTuning` off or deleting `tuning.json` reverts the cars to the left column, which is
not the configuration that was validated. Bringing them together is step 9 of the operating
procedure above and needs scene or prefab edits.

The aggression group is a second, separate case and it does NOT live in `tuning.json`, so a
copy-the-file-onto-the-prefab step will not carry it. The four values that were live in the last
checked window (`BaseAggression` 1, `AggressiveCautionScale` 0.1, `AggressiveDriveScale` 5,
`PushThroughAggression` 1.01) were set through the inspector during that session and are not saved
anywhere: they vanish on play stop. The prefab's own values are the defaults (0 / 0.35 / 2.5 /
0.75), which are the ones the validated morning window ran with. Decide per value which of the two
is wanted, and set it on the prefab explicitly rather than expecting a file copy to bring it over.

## Left staged, not deployed

`v18c-nogo-clearance` is complete and compile-clean in
`~/.hermes/profiles/hermes-sbox/cache/scratch/aicar-live/v18c/stage/`, not deployed. It re-ranks the
never-idle fallback pool by clearance to the nearest active zone instead of by road width, which
directly targets the 8 fallback commits that crossed a zone (2 ending in teleports). The skip
window is deliberately untouched. No effect size was ever estimated for it; the case for it is the
targeted evidence (the fallback commits above), nothing more.
