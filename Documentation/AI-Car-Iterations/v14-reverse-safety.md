# v14-reverse-safety: backward-roll brake on Driving entry, speed-scaled rear stop

## Context
The human retuned the car physics with play stopped (2026-10-06 ~21:15), verbatim: "Set max wheel turn
radius to 45. Set Torque to 90000. Increase the vehicle weight by 1.25x. The idea and intent: Instead of
cars slowing down so much so they better handle, they have stronger turning, more aggressive (and therefor
more fun) movement, and the weight helps the car from flipping over." Applied on the scene ai_car:
Engine_Torque 30000 -> 90000, Wheel_MaxTurnAngle 30 -> 45, Rigidbody MassOverride 2000 -> 2500. Every
pre-retune epoch (v1..v12g) is a separate measurement era.
First post-retune data (v13, human-run play): session 20261007-014136, 9.1 car-min, score -32.6, m/min 384,
arr 0.99/min, blk 7.39/min, rev 10.47/min, collisions 8.38/min (s/d/h 74/2/27), tele 3, flip 0; session
20261007-023523, 2.7 car-min, insufficient (-85.9). Measured brake decel fell ~20% as predicted (band
400-800 median 242 vs 300). Two new failure modes:
- Hard hits while Reversing: ~10 of the first 25 hard collisions (impact >200) happened at fwd -245..-274
  inside the MaxReverseSpeed 250 cap (one at impact 995); the old physics had 1 of 25. Example: car1 at
  155-161 s rocking between SM_Bld_Apartment_Stairs_12 and SM_Bld_Shop_Corner_10, hard hit each way.
- Reverse-to-drive lurch: -256 -> +252 in one 0.5 s sample. Driving throttle is proportional,
  throttle = (target - fwd)/ThrottleResponse, and full throttle was computed while still rolling backward.
Calibration A/B first (play restarted 02:41 with human permission; sessions 024257 + 024815), revision
physcal-thr900-brk240: ThrottleResponse 250 -> 900, CornerBrakeDecel 300 -> 240 (measured median brake decel).
CORRECTION 2: the effective ThrottleResponse before physcal was 125 (a scene override, confirmed from
20261007-014136 e000 config), not the code default 250, so physcal was 125 -> 900 = 7.2x, not 3.6x. The
torque/mass-equivalent gain is 125*3.6 = 450; 900 is ~2x softer, which explains the median Driving
fwd-target of -82 and lower m/min.

| session | key | car-min | score | m/min | arr/min | blk/min | rev/min | coll/min (s/d/h) |
| 024257 | v13|aim-clamp-d500-c30 (baseline, new navmesh+physics) | 13.3 | +20.8 | 469 | 1.73 | 6.84 | 8.04 | 6.09 (77/4/18) |
| 024257 | v13|physcal-thr900-brk240 | 2.4 | +28.1 | 384 | 2.11 | 2.95 | 5.89 | 2.53 (6/0/1) |
| 024815 | v13|physcal-thr900-brk240 (open at 02:55) | 16.8 | +23.8 | 361 | 1.55 | 4.34 | 6.30 | 4.40 (66/8/13) |

physcal cuts collisions ~30%, hard hits ~45% (1.35 -> ~0.73/min), blocks ~40%, reverses ~20%; costs ~22%
m/min and ~6-10% arrivals. Score about equal.

## Changes
Spec v14/SPEC.md; staged by a subagent, compiled clean in a scratch mirror, deployed live ~02:55, editor
compile green, all 3 cars on key v14-reverse-safety|physcal-thr900-brk240.
- TickDrivingHelper: while rolling backward faster than Max(StuckSpeed, 1), throttle is 0 and the car
  brakes, before any throttle is computed. Fixes the reverse-to-drive lurch.
- TickReversingHelper: the rear-obstacle stop scales with speed like the v9 ledge stop: a rear obstacle
  within rearClear < RearStopDistance + reverseSpeed*0.5 counts as blocked (max 205 at the 250 cap, inside
  RearProbeLength 250). Targets the hard hits at full reverse speed.

## Results
v14 vs v13, same tuning physcal-thr900-brk240, same session 024815 (v14/cmp.py):

| key | car-min | score | m/min | arr/min | blk/min | rev/min | coll/min (s/d/h) | tele | hard/min | hard in Reversing |
| v13 | 20.3 | +25.4 | 370 | 1.58 | 4.29 | 6.22 | 4.29 (79/8/15) | 3 | 0.74 | 8 |
| v14 (open) | 21.2 | +22.4 | 355 | 1.37 | 4.01 | 6.00 | 3.73 (77/2/6) | 4 | 0.28 | 1 |

Reverse median duration unchanged (0.80 vs 0.76 s): the speed-scaled rear stop did not starve reverses.
Hard collisions while Reversing down 65% (0.66 -> 0.23 per 100 car-s). Driving samples rolling backward
>40 u/s rose 36 -> 134: the exit brake spends ~1 s per reverse exit stopping the roll. The arrivals
difference (~32 vs ~29) and teleports (3 vs 4) are within Poisson noise.

## LLM observations
- Adversarial review: no major defect, keep. Verified: every entry into Driving while rolling backward is
  covered by the new brake; library ApplyBrake is velocity-proportional damping (zero force at zero speed),
  so the brake cannot pin a car; the rear stop factor 0.5 matches v/(2*242)=0.516 at the measured decel;
  repick-loop teleports flat within noise.
- Deferred (low, not observed): the steep-slope brake/roll loop has no escape timer (pre-existing in v13
  too); steering keeps the reverse-aim lock during the exit brake hold (nose swing, no collision
  attributed); the MaxReverseSpeed cap coasts instead of braking (worst observed -303 vs the 250 cap,
  caught by the v14 brake).
- Info: the epochs.csv collisions_hard counter and the events.jsonl detail speed= use different speed
  definitions; quote one consistently.
- Slope-hold check (stager concern: the roll brake could pin a car on a slope with no escape, since the
  stuck detector needs throttle > 0.2): checked live, the longest Driving backward-roll run is 1.0-1.5 s
  per car and every run decays to 0 (e.g. -238, -131, -74, -42). Not occurring on this map; no action.
- The lurch fix was motivated by telemetry diagnosis: car2 commanded throttle ~0.10 at 340 and gained
  +111 u/s in 0.5 s, overshooting target 353 -> 451 into a tree. The other Driving hard hits (target 80,
  brake=1, fwd 450-630, braking late for the weaker decel) are addressed by the physcal CornerBrakeDecel
  240, not by v14's code (inference: attribution, not A/B tested).

## Human observations
Physics retune request quoted verbatim in Context. Play permission (verbatim): "Also, go ahead and you're
aallowed to play the editor again. forgot to mention sorry." Watching physcal at 02:55 (verbatim): "hey
btw, i was watching the cars and they are performing MUCH better, imo." Consistent with the incident
metrics (fewer hits and blocks); throughput metrics are slightly lower.

## Outcome
v14 KEPT: no major defect on review, hard collisions while Reversing -65%, reverse durations unchanged,
score and throughput within noise of v13 at the same tuning. physcal-thr900-brk240 stays the tuning
baseline; a forensics subagent ranked the remaining failure modes.
