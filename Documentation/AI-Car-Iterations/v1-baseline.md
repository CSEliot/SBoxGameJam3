# v1-baseline: original path-following driver before the live loop iterated it

## Context
The human asked for a live tuning loop where play never stops and the LLM patches the driver via hotload. v1 is the driver as it stood when that loop started: it was not produced by the loop, so this entry records its measured baseline behavior and the problems the loop then attacked (corner cutting into buildings, stuck reverses, ledge falls, long stalls before teleport). It is the first epoch keyed `v1-baseline|none` in session 20261005-205604.

## Changes
none (baseline). What the driver already did (commit f4042e0, Code/AICarDriver.cs, ~1336 lines):
- Picked random reachable destinations on the navmesh within MinDestinationDistance 1500 to MaxDestinationDistance 6000, snapped with NavMeshSnapRadius 500, arrival at ArrivalDistance 250 (PickNewDestinationOrIdleHelper, TryPickDestinationHelper, TryFindDestinationHelper).
- Followed the path with pure-pursuit steering: LookAheadDistance 300 + LookAheadTime 0.3 (AdvanceWaypointHelper, GetPathAngleHelper, SteerFromAngleHelper); re-pathed when off path by OffPathDistance 500 (TryRepathHelper).
- Slowed for corners only when a corner came within CornerLookDistance 800, targeting CornerSpeed 350 on straights up to CruiseSpeed 800 (TickDrivingHelper, GetUpcomingCornerAngleHelper). Scene prefab overrides raised these to CruiseSpeed 1200 / CornerSpeed 700.
- Probed ahead and behind with ray "whiskers" (BuildWhiskersHelper, ProbeFrontHelper, ProbeRearHelper, CastWhiskerHelper) and steered around hits via the clearer side (GetAvoidAngleHelper); scene override set SideWhiskerAngle 45, WhiskerLengthPerSpeed 0.5, WhiskerThickness 10.
- State machine Idle/Driving/Blocked/Reversing: backed up while steering away when blocked (EnterReverseHelper, TickReversingHelper); growing aggression in a stuck area (UpdateStuckAreaHelper) and a teleport to fresh navmesh after TeleportAfter 20 s (TryTeleportHelper).
- Drove through VehicleController.UseExternalInput so the car never reads player input; idles on clients.

## Results
Epoch rows from the facts file (metrics per car-minute unless noted):

| session | key | car-min | score | arr | blk | rev | tele | flip | offnav |
| 20261005-205604 | v1-baseline|none | 63.3 | -69.5 | 0.81 | 8.20 | 7.83 | 39 | 27 | 56% |

No previous version exists; v1 is the baseline. The next row in the same session, for forward reference:

| 20261005-205604 | v2-corners|none | 16.6 | -45.6 | 1.33 | 5.07 | 12.08 | 18 | 5 | 26% |

## LLM observations
From the v1 telemetry analysis (facts file):
- Cars cut the inside of corners into building corners; top collision hotspots SM_Bld_Shop_Corner_07 and _02. Median path angle at a block was 60-93 deg.
- Target speed only dropped when a corner was within CornerLookDistance, which is too late for the car's measured braking (brake decel median ~390 u/s^2, so from 680+ u/s the stop needs well over 800 u).
- 75 of 82 legs ended in 'repick' (destination unreachable / no path), so most minutes were spent repathing rather than arriving.
- Cars that fell off a bridge edge (often following a repath at ~680 u/s) or flipped sat for the full TeleportAfter 20 s before recovering; 30% of samples were idle.
- Measured car facts used later: turning radius ~260 at 60-150 u/s, ~380 at 350-500, ~500 at 500-800; hull ~217 x 100; front-wheel steering.
- Inference: the -69.5 score with 39 flips and 56% off-navmesh time reflects corner cutting plus falls and long stalls, not slow driving (arrivals 0.81/min were near the worst of any epoch, but the loop's score penalizes flips/offnav heavily).

## Human observations
None recorded.

## Outcome
kept as the baseline reference for all later epochs; superseded by v2-corners, which targeted the corner cutting (braking-distance corner speed limit, widened corners), the stall-then-teleport recovery (lost teleport after 3 s), and the reverse behavior (three-point turn, reverse alignment).
