# v9-quay-guard: stop cars falling off quay and water edges

## Context
In the v8 corner350 epoch two cars fell into the sea off quay edges (z dropped from ~224 to -445/-1249
within ~1 s): one reversed off an edge at 184 u/s with the rear whiskers clear, one drove over a low
SM_Env_WaterEdge_Rock at ~205 u/s and went over. The rear ledge probe existed but only fed rearClear at a
fixed distance, and all ledge probes sampled the centerline only, so a wheel could go over the edge unseen.
v9 was implemented by a subagent from v9/SPEC.md. Driver code: Code/AICarDriver.cs, telemetry version
const in Code/AICarDriver.Telemetry.cs (line 35).

## Changes
- Rear ledge stops in time: ProbeRearHelper now stores the rear ledge distance in a new field `_ledgeBehind`
  (line 393; set at line 1440). In TickReversingHelper (line 756) a drop closer than
  `RearStopDistance + reverseSpeed * 0.5f` counts as rearBlocked and the car brakes hard. Before, a fast
  reverse reached the fixed RearStopDistance 80 too late to stop a rolling car.
- Edge sampling: new LedgeProbeSideHelper (line 1450) runs the downward probe at local y =
  +-halfWidth*0.8 plus the centerline and takes the minimum distance (a wheel over the edge is enough to
  fall). Side samples use the near and middle fractions (0.1, 0.45), the center all three; fractions inlined
  (no static array, hotload rule).
- Creep cap near drops: in TickDrivingHelper (lines 659-660), if `_ledgeAhead` is within
  `EffectiveStopDistance * 3`, targetSpeed is capped at EffectiveCreepSpeed. Whisker logic untouched.
- DriverVersion bumped to "v9-quay-guard" (AICarDriver.Telemetry.cs line 35).
- Tuning (human request, now running): CruiseSpeed 1200 -> 1800 on top of thick25 + corner350, for
  intimidating straight-line speed.

## Results
Session 20261006-000523, per car-minute (score = composite, arr = arrivals/min, blk = blocks/min,
rev = reverses/min, tele = teleports, flip = flips, offnav = % time off navmesh). All rows in progress.

| version | tuning | car-min | score | arr | blk | rev | tele | flip | offnav |
|---|---|---|---|---|---|---|---|---|---|
| v9-quay-guard | thick25-corner350-cruise900 | 24.9 | +7.0 | 1.25 | 6.59 | 9.76 | 9 | 0 | 3% |
| v9-quay-guard | thick25-corner350 (cut short) | 9.2 | -17.1 | 1.30 | 9.10 | 11.59 | 5 | 0 | 2% |
| v9-quay-guard | thick25-corner350-cruise1800 | 51.0 | +12.0 | 1.39 | 7.25 | 9.19 | 13 | 1 | 2% |
| v8-wake-body (best v8 row) | thick25-corner350 | 24.9 | +16.9 | 1.60 | 5.45 | 8.22 | 10 | 3 | 3% |

v9 matches v8's car-minutes with zero flips and fewer teleports, but score (+7.0 vs +16.9) and arrivals
lag; reverses are up (9.76 vs 8.22).

## LLM observations
No flips or falls into the sea in 34 car-minutes so far, which is the direct goal of the version. Score is
not better than v8 yet and the data is still coming in. The cut-short thick25-corner350 row is not
comparable (9.2 car-minutes, roughly the first cruise900 setting changed mid-epoch). Open question the
numbers raise: the creep cap near drops and the hard rear-ledge brake may be trading arrivals away;
the reverse-heavy rows are candidates for that.

Top speed (cruise1800 run, 51 car-minutes): the cars never exceeded 1124 u/s and averaged ~520 u/s on
clear straights, even with the driver asking for 1700+ at full throttle. The limit is the vehicle, not the
driver: measured full-throttle acceleration falls from 562 u/s^2 (0-200 u/s) to ~200 u/s^2 above 600 u/s,
because the bugge VehicleController pushes with throttle * Engine_Torque * gear ratio and higher gears have
lower ratios. Reaching 1800 from 600 would take over 6 s and ~7000 u of straight road. Raising CruiseSpeed
alone cannot deliver the intimidating straights; the prefab's Engine_Torque (20000, private library
property) has to go up (suggested ~40000, not yet measured).

AgentRadius critique (critic subagent, engine source checked by the parent): the navmesh is eroded by
WalkableRadius = ceil(AgentRadius / CellSize) (Generation/Config.cs:34, HeightFieldGenerator.cs:228), so every
roadnav-tagged prop collider grows by ~110 u on every side. IsBlocker NavMeshArea volumes are applied as
NULL_AREA when marking areas (NavMeshTile.cs:226); the critic reports this happens after erosion, so the
volume itself carves at its true size (inference from bake order, not tested in play). The critic also
reports buildings are not in the roadnav bake (not verified by the parent), in which case the large radius
no longer protects building corners and whiskers plus corner widening carry that load.
Recommendation: AgentRadius ~55, keep the IsBlocker volumes, untag small prop colliders from roadnav, and
express road-over-sidewalk preference with area cost. Scene change and rebake are the human's.

## Human observations
Watching play, on the speed request (applied as tuning CruiseSpeed 1200 -> 1800): "Let's increase the top
speed by 1.5x. The intent is when they have a long straight-away, they're going at an intimidating speed."
On the scene: "The 'isBlocker' marked props on the road cut HUGE into the road ... due to the agent radius
size i believe. We increased agent radius initially because of corner cutting, but that's changed now that
we are including sidewalks in nav." (sent to a critic subagent).

## Outcome
In progress. Being evaluated: the critic subagent's review of AgentRadius 110 vs the isBlocker-marked
props cutting into the road (possible navmesh-side fix instead of driver-side), and the cruise1800 run, to
see whether the higher top speed keeps the zero-fall record and how it affects score, arrivals, and
reverses against v8's +16.9.
