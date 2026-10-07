# v13-steer-damping: steering deadband + yaw-rate damping

## Context
The human saw the cars veer left and right toward the next corner with no obstacles (v11 era). The v12
analysis subagent partially confirmed it: over-correction is real. The sqrt steering map gives |steer| 0.25
at only 3.8 deg of heading error (prediction 0.24) and has infinite gain near zero error, so the wheel
flips sign 7-14 times per minute while cruising straight. Quantifying that was hard: whisker avoidance is
nonzero in 65% of driving samples at fwd > 300 (side whiskers passing light poles and signs), so
avoidance-free cruising data is scarce, roughly 1-1.5 min per version. v13 attacks the steering law itself
(spec v13/SPEC.md, rebased onto v12g per v13/REBASE-SPEC.md): damp the commanded angle by the car's yaw
rate and deadband small errors, so the wheel stops chasing noise around zero. Driver code:
Code/AICarDriver.cs, telemetry version const in Code/AICarDriver.Telemetry.cs (line 35).

## Changes
- New [Properties] (AICarDriver.cs lines 238, 246): `SteerDeadband` = 2 deg and `SteerYawDamping` = 0.3 s,
  both in the Steering group, live-tunable via tuning.json.
- DampedSteerAngleHelper applies damping-then-deadband: the yaw rate (times SteerYawDamping seconds) is
  subtracted from the commanded angle first, then SteerDeadband degrees are eaten off what is left (line
  2104 ff). Hotload fallbacks: EffectiveSteerDeadband treats a hotloaded 0 as 2 (the deadband cannot be
  disabled), EffectiveSteerYawDamping treats 0 as 0.3 but any negative value disables (lines 1141-1145).
- Scope: the helper is applied to pathAngle ONLY in the forward-Driving steerTarget branch (forwardSpeed >
  StuckSpeed), and avoidAngle is added AFTER it (line 755 area). This is review fix 6a: damping the sum of
  path and avoidance angles would cancel evasion steering against an obstacle, since the avoidance and the
  yaw it induces would both be damped.
- GetAvoidAngleHelper tie-break now reads pathAnglePp (the pure-pursuit polyline angle) instead of the
  corner-aim angle, closing v12g review item 5.
- The telemetry config block logs EFFECTIVE values, not raw properties (AICarDriver.Telemetry.cs line 681,
  plus EffectiveCornerAimDistance/Corridor/TraceDistance/TraceRadius/FullAngle helpers). This is a review
  fix: raw logging is exactly what hid the stale CornerAimTraceRadius=50 on live instances through the
  whole v12b-e debugging.
- The damping constant started at 0.15 s and was raised to 0.3 after the reviewer measured the weave: at
  0.15 s the median weave yaw rate of 8.9 deg/s only cancels ~1.3 deg while the errors run ~20 deg.
- DriverVersion bumped to "v13-steer-damping" (AICarDriver.Telemetry.cs line 35).

## Results
Not yet measured. Play stopped before the first v13 epoch could run, and the human's physics retune
(Engine_Torque 90000, Wheel_MaxTurnAngle 45, mass 2500) applies from the first frame: the first v13 epoch
also re-baselines the car's turn radius, acceleration, and brake decel, since every pre-retune epoch
(v1 through v12g) is a separate measurement era and the derived tables are stale.

## LLM observations
Yaw sign proven correct before writing the damping: +AngularVelocity.z means turning left, same sign
convention as +pathAngle. Verified from engine source and from telemetry (98% sign agreement between yaw
rate and steer angle in the data). The damping math depends on this, so it was checked rather than assumed.
Quantitative effect at zero speed error: with the 2 deg deadband, a 4 deg heading error now commands
sqrt(2/70) = 0.17 instead of sqrt(4/70) = 0.24 under the old map; errors at or below 2 deg command zero.
Whether that actually suppresses the 7-14 flips/min is pending the first measurement.

## Human observations
Physics retune request (verbatim): "Set max wheel turn radius to 45. Set Torque to 90000. Increase the
vehicle weight by 1.25x. The idea and intent: Instead of cars slowing down so much so they better handle,
they have stronger turning, more aggressive (and therefor more fun) movement, and the weight helps the car
from flipping over." Also (verbatim): "don't start it back up until i tell you."

## Outcome
Deployed to Code/ while play is stopped; compiles 0 errors (editor LastCompileSucceeded). The v13
adversarial verification review found no major defect and confirmed all six review-fix requirements
landed. Awaiting play resume on the human's word. On resume: re-measure turn radius / accel / brake decel
under the new physics before any speed retune. v14 candidate queued: StraightenPathHelper's 25u chord
band can cut through thin (<50u wide) obstacles.
