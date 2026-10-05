# AI car feedback loop

A closed loop for improving `AICarDriver` in `scenes/minimal.scene` where the only human step is
**start play, let it run, stop play**. Everything else (recording, export, analysis, code changes,
getting the new code back into the editor) is automated.

## Why this design

The constraints:

- The driver can only be judged in the real game: bugge's vehicle physics, the city geometry and
  the baked navmesh. There is no headless simulator.
- The analysis side (Claude, in a cloud container) has no s&box, no MCP and cannot see the scene.
  It only sees files that reach git.
- Human time is the scarcest thing in the loop. Each play session has to produce as much signal
  as possible, and the human must not have to copy logs or describe what happened.

So the game measures itself, the editor ships the measurements through git, and analysis runs on
condensed numbers and maps, never on raw frame-by-frame logs.

## The loop

```
 you: Play ──► AICarDriver drives (3 cars: the scene car + 2 test clones)
                 │  AICarDriver.Telemetry.cs records 2 Hz samples + incident events
                 ▼
 you: Stop ──► AICarTelemetrySink ──► Editor/AICarTelemetryExporter.cs
                 │  copies files out of the sandbox to .aicar-telemetry/ (git-ignored)
                 │  mirrors them into ../<project>-aicar-telemetry (separate git worktree)
                 │  commits + pushes branch  claude/ai-car-feedback-loop-bxjrbe-telemetry
                 ▼
 Claude: notices the push ──► Tools/aicar/analyze.py ──► report + maps
                 │  diagnoses, changes AICarDriver*.cs, bumps DriverVersion
                 │  pushes branch  claude/ai-car-feedback-loop-bxjrbe
                 ▼
 editor: fast-forwards that branch every 30 s while not playing ──► hotload ──► next Play
```

Telemetry and code live on **separate branches** so the two sides never conflict, and the
telemetry worktree sits **next to** the project folder so your working tree is never touched.
The code pull is fast-forward only and only runs while the code branch is checked out. If you have
local edits to a file that a fix touches, it refuses and logs a warning instead of overwriting.

## What gets measured

Per car, per session (`aicar/<session>/<car>/`):

| file | contents |
|---|---|
| `summary.json` | totals, time per state, counters, and the full driving config that produced the run |
| `samples.csv` | 2 Hz: position, yaw, speed vs target speed, throttle/brake/steer, state, aggression, path deviation, path/avoid angles, whisker clearances, off-navmesh distance, uprightness, name of what the whiskers see |
| `events.jsonl` | state changes and incidents with context (below) |
| `legs.csv` | one row per destination: outcome (arrived / repick / teleport / repath-failed / unfinished), planned vs driven length, reverses, collisions |
| `../navsample.csv` | 4000 random navmesh points, which is how the analyzer "sees" the road network |

Every incident (Blocked, Reverse, Collision, Teleport, Flipped, OffNav) records **what was
around the car**: which whiskers hit which named object, a 16-ray 360° scan out to 800 units with
the object names, and the ground object under the car. That replaces seeing the scene: a hotspot
reads like "Blocked 6, Collision 4 at (-2300, 5100), hit: `street_lamp` x7, close: `curb_corner` x3".

Nothing is logged per frame. The console gets one summary line per car when play stops.

## How runs are judged

`analyze.py` prints one table row per car plus a history table across sessions. The **score**
is useful progress per minute minus incident costs per minute:

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

## Iteration rules (analysis side)

1. Change one idea per iteration and bump `AICarDriver.DriverVersion`, so the history table
   attributes each change.
2. Tuning goes in `AICarDriver.Tuning.cs` (applied over the scene values while `UseDevTuning` is on),
   so fixes arrive by hotload without editing the scene or prefab you may have open.
   Logic fixes go in `AICarDriver.cs`.
3. Judge a change by the score and its incident mix over at least ~3 car-minutes, not one anecdote.
   If it got worse, revert.
4. When the car is good: bake the tuned values into `ai_car.prefab`, set `UseDevTuning` and
   `ExtraTestCars` back to off/0.

## One-time setup (you)

1. Check out the code branch locally: `git fetch origin && git checkout claude/ai-car-feedback-loop-bxjrbe`
2. Make sure `git push` works from that machine without a prompt (credential manager or SSH key).
3. Open the project in s&box and open `scenes/minimal.scene`.

After that, just Play / Stop. A run of 3–5 minutes is a good length. Watch the s&box console for:

- `AICarTelemetryExporter: telemetry pushed to ...`: the run was delivered.
- `AICarTelemetryExporter: pulled N new commit(s) ...`: a new driver version is live for the next Play.

To pause the git automation, create an empty file `.aicar-telemetry/off` in the project folder.
Data is still written to `.aicar-telemetry/` (and to FileSystem.Data), so a run can be shared by hand.

## Files

- `Code/AICarDriver.cs`: the driver (state machine, pure pursuit, whiskers, reversing, teleport)
- `Code/AICarDriver.Telemetry.cs`: recording, incident context, test-car spawning
- `Code/AICarDriver.Tuning.cs`: code-side tuning for the loop
- `Code/AICarTelemetrySink.cs`: hand-off from the sandboxed game to the editor
- `Editor/AICarTelemetryExporter.cs`: export, git push of telemetry, git pull of code
- `Tools/aicar/analyze.py`: report, history and maps (`python3 Tools/aicar/analyze.py <telemetry checkout>`)
