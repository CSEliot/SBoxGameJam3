# v12-corner-aim: aim steering at the next real corner, and the long debug that proved it wasn't firing

Covers v12-corner-aim, v12b-aim-fixes, and the v12c-g debugging that ended at v12g on revision
aim-clamp-d500-c30. Driver code: Code/AICarDriver.cs, telemetry const in Code/AICarDriver.Telemetry.cs
(line 35). Session 20261006-163149, scene3.

## Context
The human, watching v11 play: "The cars veer left and right towards the next corner despite having
no obstacles. My guess is 2 things happening together: 1. The car is strictly following the
navmesh-given path, instead of just aiming for the next corner turn. 2. The ai is over correcting on
each turn. Consider these. I think the ai should have 2 modes: Mode A: strictly following the given
nav path. and Mode B: The "follow target" is just the next turn in the nav. (as usual, validate, i
could be wrong)". Sent to an analysis subagent (v12/BRIEF.md).

v12/ANALYSIS.md verdict: PARTIALLY CONFIRMED.
- Clean straights (whiskers quiet): steer sign flips 29.5/min, lateral RMS 89u, period 3-4 s.
- Corner widening swing confirmed: on >=15 deg turns the cars sit on the turn's outside in 62-68% of
  samples (mean +49u at 1000-1500u out) because WidenCornersHelper targets a point 150u outside.
- Over-correction confirmed: the sqrt steering map gives |steer| 0.25 at 3.8 deg error (predicted
  0.24) and has infinite gain at zero error.
- Refuted: zig-zag paths. 0.01 alternating small corners per km over 3191 km. Whiskers are a
  secondary agitator (|avoidang| > 10 deg in 49% of Driving samples, mostly side whiskers passing
  light poles and signs).
- Mode B verdict: directionally right, the bare version unsafe (cuts jogs around obstacles). A
  gated version was specified (v12/SPEC.md).

## Changes
v12-corner-aim (live ~18:34, rebased onto v11c; also carries the v11c review fixes: the repick chain
decays when the car leaves StuckAreaRadius or after 90 s, and resets on the idle pick):
- StraightenPathHelper drops near-collinear path points below 12 deg / 25u.
- Gated corner aim: steering aims at the next path corner of >=15 deg (or the destination) when it
  is within 1500u, is farther than the pure-pursuit point, the intermediate points stay within 60u
  of the straight line (corridor), and a hull-width trace to it is clear.
- Steering law, avoidance and widening deliberately unchanged so the effect is attributable.
Adversarial review of v12 found F1 (corner-aim angle leaked into pathBehind/pivot/reverse-side/
aligned decisions), F2 (corridor too weak: 60u vs widened points, ~40u sweep, IgnoreDynamic), F3
(trace cache not invalidated on new path), F4 (StraightenPathHelper can collapse a multi-point
gentle walk-around). v12b-aim-fixes (live ~19:23): pure-pursuit angle for all decisions, only
steering + heading-error speed use corner aim; corridor 60 -> 30u; clear-shot sweep radius half hull
+ 20 (~70u) without IgnoreDynamic; trace cache invalidated on SetPathHelper; StraightenPathHelper
became a single forward pass keeping a point if any skipped point sits >=25u off the anchor-to-next
chord. Compiles 0 errors.

## The v12b-g debugging (aim fired 0%)
- v12b made corner aim fire in 0% of driving samples (v12 fired ~40%). v12c (radius 40 +
  IgnoreDynamic restored) still 0%. Diagnostic build v12e wrote the failing gate code into the aim
  telemetry column: gate d (the clear-shot trace) blocked ~100% of chords that reached it (43% of
  driving samples); gates a/b/c were fine (corridor blocked only 2%).
- Root cause, found via the epoch summary config rather than the code: the LIVE instances had
  CornerAimTraceRadius=50, not the code default 40. A sphere trace of radius 50 cast at WhiskerHeight
  45 has its bottom 5u below the road, so every Scene.Trace.Ray returned StartedSolid=true and gate d
  always failed. Setting radius 40 (<45) via tuning.json, which writes by name and overrides the
  stale value, immediately gave 38% aim.
- Hotload lesson: [Property] values on live instances are preserved across hotload; a code default
  change only affects newly created instances. Always confirm the effective value from telemetry
  config (raw-property logging hid the stale value; v13 added effective-value logging).
- v12f fix: trace only the first CornerAimTraceDistance (default 500u) instead of the whole chord. A
  body-width sweep over the full 1500u chord clips a light pole or sign on nearly every chord in a
  prop-dense city (66% of driving samples already have a front-whisker hit). Corner aim only steers;
  whiskers remain the real safety and the 30u corridor gate keeps the path near the chord.
- v12g fix: clamp the trace radius to min(radius, WhiskerHeight - 5) so the sphere can never dip into
  the ground regardless of the hotloaded value; default 40 exceeds default WhiskerHeight 35, so even
  a fresh restart would have dipped. v12g aims ~37% of driving samples on aim-clamp-d500-c30.

## Results
Per car-minute (score = composite, arr = arrivals/min, blk = blocks/min, rev = reverses/min):

| version | tuning | car-min | score | arr | blk | rev | tele | flip | offnav |
|---|---|---|---|---|---|---|---|---|---|
| v12-corner-aim | thick25-corner350-cruise1800-scene3 | 120.0 | +26.1 | 1.48 | 5.33 | 7.04 | 35 | 0 | 1% |
| v11c-repick-loop (before v12) | same | 69.9 | +26.1 | 1.39 | 4.89 | 6.44 | 21 | 3 | 0% |

v12 vs v11c: arrivals up (1.39 -> 1.48, best so far), score equal, reverses +9%, blocked +9%, hard
collisions 0.31 -> 0.37/min. Corner aim fired in ~34-56% of cruising samples but steer flips/min and
path deviation were essentially unchanged, consistent with the weave baseline (at fwd > 300, whisker
avoidance is nonzero in 65% of samples, >10 deg in 42%; avoidance-free cruising is only ~1-1.5 min per
version): avoidance and the sqrt gain matter more than the look-ahead target.
v12g only got ~15 car-min before play stopped. Its adversarial review: no major defect; the clamp is
correct for WhiskerHeight >= 10 (fail-closed below); F1 wiring verified at 4 call sites; StartedSolid
fail-closed is right. The per-km trend in an 11-minute open window looked 1.5-2x worse than v11c on
car0/car1 (collisions 0.18 -> 0.32/km car0, blocked 0.27 -> 0.48/km), but the reviewer called it
inconclusive (needs >=30 min) and the physics retune invalidates those baselines anyway. Data caveat
for the session: after the human modified 'ai_car (test 1)' (telemetry car2), comparisons pool
car0+car1 only; the table rows above are the all-car keys as logged.

## LLM observations
The whole v12b-e saga is a measurement lesson: the code was verified line by line, the gates were
sound, and the aim still never fired, because the running objects carried stale property values.
The diagnostic column in v12e (which gate rejected each chord) is what localized it; the epoch config
block then named the stale value. v12g's clamp makes the failure mode structurally impossible rather
than depending on a tuned constant. Review follow-ups: StraightenPathHelper's 25u chord band can
collapse a navmesh detour around a thin obstacle (a light pole is <25u half-width) into a chord
through it, queued as v14; GetAvoidAngleHelper tie-break should use pathAnglePp (folded into v13);
the trace uses world Up while whiskers use car-space up (harmless, doc slightly wrong).

## Human observations
- The weaving quote and Mode B proposal at the top of Context (verbatim there).
- Physics retune (~21:15, play stopped, applied to the scene ai_car and saved, verbatim): "Set max
  wheel turn radius to 45. Set Torque to 90000. Increase the vehicle weight by 1.25x. The idea and
  intent: Instead of cars slowing down so much so they better handle, they have stronger turning,
  more aggressive (and therefor more fun) movement, and the weight helps the car from flipping over."
  Applied: Engine_Torque 30000 -> 90000, Wheel_MaxTurnAngle 30 -> 45, mass 2000 -> 2500. All
  pre-change epochs (v1..v12g) form a separate measurement era; derived tables (turn radius vs speed,
  brake decel ~390, accel by band) are stale and must be re-measured.
- "don't start it back up until i tell you." (play stayed stopped; v13 was staged while stopped)

## Outcome
Corner aim kept: v12g (v12b's fixes + near-trace + radius clamp) was live at the physics-retune
boundary and will be re-judged post-retune on the new measurement era. StraightenPathHelper's 25u
thin-obstacle chord issue is queued as a v14 candidate (lower the band or trace candidate chords at
repath). Steering-law work (deadband + yaw damping) continued as v13.
