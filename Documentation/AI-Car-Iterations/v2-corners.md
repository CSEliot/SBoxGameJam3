# v2-corners: corner widening, brake-aware corner speed, and U-turn reverses

## Context
First iteration of the live tuning loop (infra in commit f4042e0; v2..v7 code landed together in
commit afc810f). v1 telemetry showed cars cutting the inside of corners into building corners
(top hotspots SM_Bld_Shop_Corner_07/_02), the target speed only dropping once a corner was within
CornerLookDistance (too late for the measured ~390 u/s^2 braking), 75 of 82 legs ending in
'repick', and flipped/off-edge cars idling 20 s before TeleportAfter recovered them. Measured
turning radius: ~260 at 60-150 u/s up to ~500 at 500-800 u/s; the navmesh path corners hug the
inside, far tighter than the car can physically turn.

## Changes
- Braking-distance corner speed limit: `GetCornerSpeedLimitHelper` (Code/AICarDriver.cs:1315,
  applied to target speed at :642) with new property `CornerBrakeDecel` 300
  (Code/AICarDriver.cs:140). Allowed speed at distance d before a corner is
  sqrt(cornerSpeed^2 + 2 * CornerBrakeDecel * d), so braking starts early enough.
- Path corners pushed toward the outside of each turn and snapped to navmesh: `WidenCornersHelper`
  (Code/AICarDriver.cs:1204, called at :1189) with new property `CornerOutsideOffset` 150
  (Code/AICarDriver.cs:149), scaled by turn angle and clamped by the shorter adjacent segment
  (Code/AICarDriver.cs:1229).
- Short path segments merged: new property `MinPathPointSpacing` 120 (Code/AICarDriver.cs:155);
  points closer than that to the last kept point are merged away (:1183) so one turn split over
  tiny navmesh segments reads as one corner with its full angle.
- Three-point-turn reverse when the path angle exceeds `UTurnAngle` 120
  (Code/AICarDriver.cs:250; check `pathBehind` at :594); reverses end early once the nose is
  aligned within `ReverseAlignedAngle` 60 (Code/AICarDriver.cs:253; :765).
- Next destination picking prefers routes that do not start with a U-turn
  (Code/AICarDriver.cs:557, :871).
- 'Lost' recovery: teleport after `LostRecoverTime` 3 s (Code/AICarDriver.cs:277; logic at :498)
  when flipped or more than 400 u from the navmesh, instead of waiting the full TeleportAfter 20 s.

## Results
Epoch rows from analyze.py --live (v1 baseline row above is the previous version, copied exactly):

| session | key | car-min | score | arr | blk | rev | tele | flip | offnav |
| 20261005-205604 | v1-baseline|none | 63.3 | -69.5 | 0.81 | 8.20 | 7.83 | 39 | 27 | 56% |
| 20261005-205604 | v2-corners|none | 16.6 | -45.6 | 1.33 | 5.07 | 12.08 | 18 | 5 | 26% |

## LLM observations
- Arrivals +64%, blocks -38%, offnav 56% -> 26%, idle 30% -> 0. Score improved -69.5 -> -45.6 (see note below), but reverses rose 7.83 -> 12.08/min.
- Two cars backed off a bridge while doing a three-point turn at ~680 u/s: the new U-turn reverse
  had no speed or ledge guard (inference: the fast reverse, not the turn logic itself, caused the
  falls).
- Note on score: the -45.6 figure is the composite as reported; direction of travel is ambiguous
  against v1's -69.5 given arrivals roughly doubled, so read it together with arr/blk/offnav.
  [inference]

## Human observations
None recorded.

## Outcome
Kept. The corner widening, brake-aware speed limit and lost-teleport carried forward; the
unguarded high-speed three-point turn was the problem v3-ledge-uturn went after next (downward
ledge probe plus three-point turns only from a near standstill).
