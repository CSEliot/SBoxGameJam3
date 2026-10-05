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
