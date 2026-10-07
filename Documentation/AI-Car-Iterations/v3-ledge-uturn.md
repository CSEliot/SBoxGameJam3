# v3-ledge-uturn: ledge probes stop falls; a signed-speed standstill check makes reverses runaway

## Context
v2-corners added the three-point-turn reverse for U-turns, but two cars backed off a bridge
doing that turn at ~680 u/s, and cars still flipped (5) or fell off edges (18 teleports).
v3 aimed to make edges visible to the driver (a downward ledge probe ahead and behind) and to
make the turn reverse safe by only starting it from a near standstill, braking straight first.
The live loop never stops play: the LLM patches Code/AICarDriver.cs via hotload between epochs.

## Changes
- Downward ledge probe ahead and behind the hull: `LedgeProbeHelper` (cast down from the
  front/rear edge to find the first missing floor), `LedgeDropHeight` = 80u defines a drop.
  - `LedgeDropHeight` property: AICarDriver.cs:212
  - `LedgeProbeHelper`: AICarDriver.cs:1468 (probe stores `_ledgeAhead` AICarDriver.cs:391, 1404;
    `_ledgeBehind` AICarDriver.cs:393, 1440 - the ahead/behind split into wheel-line sampling is
    a later v9 refinement, the v3 probe was the single centerline cast).
- Missing floor ahead counts as a forward obstacle: `forwardClear` is clamped to `_ledgeAhead`
  (AICarDriver.cs:1564), so whisker logic brakes/steers away from the edge; a drop within
  3 stop distances caps target speed (AICarDriver.cs:659, tightened later).
- Missing floor behind marks the rear blocked (AICarDriver.cs:756, refined in v9).
- Three-point turn only from a (near) standstill: the `pathBehind` branch (AICarDriver.cs:594)
  requires low speed before `EnterTurnReverseHelper` (AICarDriver.cs:616); at speed the car
  brakes straight first instead of reversing.
  Note: the current line 598 test `MathF.Abs( forwardSpeed ) < StuckSpeed * 2f` is the v4 fix;
  in v3 this check used signed forward speed, the bug below.

## Results
| session | key | car-min | score | arr | blk | rev | tele | flip | offnav |
| 20261005-205604 | v3-ledge-uturn|none | 14.2 | -54.2 | 0.92 | 8.60 | 14.17 | 6 | 0 | 34% |
| 20261005-205604 | v2-corners|none | 16.6 | -45.6 | 1.33 | 5.07 | 12.08 | 18 | 5 | 26% |

(v3 = 8.60 blocked/min vs 5.07, 14.17 reverses/min vs 12.08, arrivals 0.92 vs 1.33;
teleports 18 -> 6, flips 5 -> 0, offnav 26% -> 34%.)

## LLM observations
- The ledge probe did its job: flips went to 0 and teleports to 6, exactly the failure mode
  it targeted.
- Rejected hypothesis: that the ledge probe caused false blocks (phantom "no floor" reads
  blocking the car). Checked against the block events: only ~3 of 91 blocks had no whisker
  hit, so the probe was not the block source.
- Real cause of the regression: the standstill check used signed forward speed. A car already
  rolling backwards fast (negative speed) failed the `speed < StuckSpeed * 2f` style test,
  re-entered the turn reverse, and accelerated further in reverse under the aggression
  throttle. Reverse speeds reached 500-640 u/s, driving blk and rev up and arr down.
- Inference (confirmed by v4): the runaway reverses, not the probes, explain the worse score;
  the 34% offnav is partly the cars swinging wide while reversing fast.
- Aggression throttle interacting with an uncapped reverse was an unmeasured design hole:
  nothing limited reverse speed at all until v4's `MaxReverseSpeed` 250.

## Human observations
None recorded.

## Outcome
Mixed: eliminated falls and flips (the goal of the ledge probe) but regressed everything
else through the signed-speed standstill bug. Superseded immediately by v4-reverse-cap,
which fixed the check to use |forward speed| and capped reverse speed, restoring blocks,
reverses and arrivals while keeping v3's zero-flip record.
