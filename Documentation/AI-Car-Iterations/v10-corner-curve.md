# v10-corner-curve: stop corner speed from scaling with CruiseSpeed

## Context
v9 ran on the human's scene2 (session 20261006-120427: Engine_Torque 20000 -> 30000, AgentRadius
110 -> 55, 36 buildings grouped under IsBlocker volumes) with tuning thick25-corner350-cruise1800,
scoring +3.4 over 255.6 car-minutes. The subagent analysis (v10/ANALYSIS.md) found the corner speed
was Lerp(CruiseSpeed, CornerSpeed, angle / 90) in two places, so with CruiseSpeed 1800 a 45 degree
turn allowed ~1075 u/s. Measured: 59% of corner entries above CornerSpeed 350 (entry p50 402,
p90 631), while the car's turning radius at 350-500 u/s is ~380-500; turn-trigger reverses were 27%
of all reverses. v10 was implemented by a subagent from v10/SPEC.md. Driver code:
Code/AICarDriver.cs, telemetry version const in Code/AICarDriver.Telemetry.cs (line 35).

## Changes
- New curve: CornerSpeedForAngleHelper eases from Min(CruiseSpeed, CornerSpeed*4) (1400 at
  CornerSpeed 350) at 0 deg to CornerSpeed (350) at CornerFullAngle (45), then to SharpCornerSpeed
  (200) at 120 deg. Ease-out means merged small-angle segments (~10 deg on a straight) cap near
  ~990 rather than at full cruise.
- New tunables CornerFullAngle (45) and SharpCornerSpeed (200, clamped to never exceed CornerSpeed),
  each with a hotload fallback when a live instance reads 0.
- Both old Lerp call sites replaced with the helper: the heading-error target in TickDrivingHelper
  and every upcoming path corner in GetCornerSpeedLimitHelper; the destination case keeps angle 90.
- The braking horizon uses the sharp speed: (CruiseSpeed^2 - sharp^2) / (2*decel) +
  CornerLookDistance, so braking for a far hairpin starts early.
- Two review minors fixed: EffectiveCornerFullAngle clamps to 119 so a value >= 120 cannot make the
  45..120 sharp segment divide by zero length, and the horizon is floored at CornerLookDistance
  because a CruiseSpeed below the sharp speed makes the brake term negative.
- Destination approach now ~260. DriverVersion bumped to "v10-corner-curve".

## Results
Sessions 20261006-120427 (scene2) and 20261006-163149 (scene3), per car-minute (score = composite,
arr = arrivals/min, blk = blocks/min, rev = reverses/min, tele = teleports, flip = flips,
offnav = % time off navmesh).

| version | tuning | car-min | score | arr | blk | rev | tele | flip | offnav |
|---|---|---|---|---|---|---|---|---|---|
| v10-corner-curve | thick25-corner350-cruise1800-scene2 (early) | 38.4 | +26.7 | 1.30 | 4.95 | 6.38 | 6 | 1 | 1% |
| v10-corner-curve | thick25-corner350-cruise1800-scene3 (car0+car1) | 54.0 | +15.7 | 1.30 | 5.85 | 7.42 | 14 | 1 | 1% |

Other v10 scene3 rows from the facts: the first scene3 run (43.7 car-min) scored +15.5 with
arr 1.49, blk 6.16, rev 8.01, tele 17; the two scene3 epochs pooled (58.8 car-min) gave +15.3,
arr 1.38, blk 6.16, rev 7.81, tele 20; the same-session all-car comparison (81.0 car-min) was
+15.2, arr 1.32, blk 5.95, rev 7.66, tele 25 (17 pivot, 7 stuck).

## LLM observations
The early scene2 row (+26.7) was the best score recorded at that point, well ahead of v8's +16.9
and v9's scene2 +3.4, with blocks down to 4.95/min and reverses to 6.38/min. Inference: the gain
comes from entering corners near 350 instead of the measured 402-631, which removes the front-swing
into inside blockers that the analysis tied to turn-trigger reverses (27% of reverses). The scene3
rows settling at +15.x suggest part of the scene2 number was epoch luck (destinations are random,
and 38.4 car-minutes is short). v10 deliberately left the reverse counter alone: the analysis found
41/64 teleports preceded by ~6 reverses in 10 s because _reverseCount resets on any waypoint
advance, and that became the v11 job. The dominant blocked causes (props 1715 events 84% on-path,
buildings 1163 79% on-path) are zero-clearance nav problems the corner curve cannot fix.

## Human observations
Standing directive (verbatim): "Utilise wise use of subagents for adversarial review and basic
labor in order to make an ai car controller that can travel around the map without getting stuck
and without issues in taking corners, without needing input (unless necessary) from non-llm (aka
human) users for help. But assistance and feedback from human user input is always considered
though not treated as strict accuracy."

## Outcome
Kept. v11 (reverse-progress counters) regressed and was reverted to v10 live at 17:13; v10 became
the baseline every later version (v11b, v11c, v12 corner aim) was measured against, and its corner
curve is still in the driver.
