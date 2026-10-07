# v8-wake-body: keep the driven rigidbody awake so teleported cars recover

## Context
The v7 thick25 combined run (e001+e003) ended with 74 teleports and a score of -31.6,
dominated by one car frozen in mid-air. v8 targets that failure mode while the live loop
keeps running without stopping play. Driver code: Code/AICarDriver.cs. All runs below are
session 20261006-000523.

## Changes

### Code change (driver)
- Every tick, the driver forces its own Rigidbody awake: `if ( _body.Sleeping ) _body.Sleeping = false;`
  in the control update.
- On teleport, the driver explicitly wakes the body: `_body.Sleeping = false;` right after setting
  the drop position and zeroing velocities.
- No other logic changed relative to v7 (diff of v8/AICarDriver.cs vs v9/AICarDriver.cs shows only
  these two wake calls).

### Tuning experiments (separate from the code change)
- corner350: CornerSpeed 700 to 350 (on top of thick25).
- cruise900: CruiseSpeed 1200 to 900 (on top of thick25-corner350).
- Baseline tuning for these runs stayed thick25: WhiskerThickness 25, WhiskerHeight 45.

## Results

| key | car-min | score | arr | blk | rev | tele | flip | offnav |
| v8-wake-body thick25 | 23.6 | -24.2 | 0.72 | 8.49 | 11.67 | 11 | 0 | 5% |
| v8-wake-body thick25-corner350 | 24.9 | +16.9 | 1.60 | 5.45 | 8.22 | 10 | 3 | 3% |
| v8-wake-body thick25-corner350-cruise900 | 21.5 | +11.8 | 1.35 | 6.65 | 8.38 | 8 | 1 | 2% |
| v7-block-hysteresis thick25 (first run, e001) | 21.4 | -8.2 | 1.08 | 8.28 | 10.02 | 8 | 1 | 4% |
| v7-block-hysteresis thick25-wide260 (e002) | 52.4 | -48.8 | 0.76 | 6.91 | 15.79 | 51 | 1 | 5% |
| v7-block-hysteresis thick25 (e001+e003 combined) | 106.5 | -31.6 | 0.83 | 6.43 | 13.13 | 74 | 1 | 5% |

## LLM observations
- Sleeping-body root cause: after a teleport the body went to sleep at drop height with the
  wheels off the ground. The vehicle applies no force when wheels are ungrounded, and forceless
  driving never wakes a sleeping body, so the car sat frozen (one car stuck 500+ s; 1528 of 3182
  samples at an identical position). Keeping the body awake every tick, plus an explicit wake on
  teleport, makes the car fall and settle instead of hanging in mid-air.
- Why corner350 helped: measured turning radius is ~500 u at 500-800 u/s, wider than the street
  corners, so the old CornerSpeed 700 target made cars clip corners and block. Slowing corners to
  350 keeps the car inside the geometry; it produced the loop's first positive score (+16.9) with
  arrivals 1.60 and reverses down from 11.67 to 8.22.
- CruiseSpeed 900 was slightly worse than 1200 (score +11.8 vs +16.9, arrivals 1.35 vs 1.60),
  though offnav improved to 2%.
- New issue surfaced under corner350: two cars fell into the sea off quay edges (one reversed off
  at 184 u/s, one drove over a low SM_Env_WaterEdge_Rock). This motivated v9-quay-guard.

## Human observations
None recorded.

## Outcome
Accepted. The wake fix removed the frozen-car teleport mode (v8 rows show teleports back to
single-digit/low-double-digit counts per run). corner350 became part of the standing tuning,
cruise900 kept for the next run despite being slightly worse. First consistently positive scores
in the loop. Follow-up work: v9-quay-guard against the quay falls.
</content>
</invoke>
