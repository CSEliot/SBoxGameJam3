# v5-pivot-escape: break the three-point-turn livelock with counted pivot escapes

## Context
v4 (reverse cap) had the best score so far but left a livelock: one car did 25
turn-reverses in 20 s at speed 0 with its rear against a wall, ending in the 20 s
stuck teleport (v4/ANALYSIS.md, Cause 1). The code review (v4/REVIEW.md) also flagged
an unbounded corner-widening snap (M1, could jump to another road/level) and NaN-prone
flat headings (N5). v5 was implemented by a subagent from v5/SPEC.md, on top of the
v4-reverse-cap state, while play kept running (hotload-safe new fields).

## Changes
- Pivot fail counter and anchor: new instance fields `_pivotFails` and `_pivotAnchor`
  (Code/AICarDriver.cs:378-379), safe at default(T) for hotload.
- Failed turn-reverses are counted: in TickReversingHelper, a `turn` reverse that ends
  without `aligned` within StuckAreaRadius of the anchor increments `_pivotFails`;
  farther away re-anchors at 1; an aligned reverse resets to 0
  (Code/AICarDriver.cs:782-791). Also reset on waypoint progress
  (Code/AICarDriver.cs:985) and on successful teleport (Code/AICarDriver.cs:1273).
- Forward pivot escape: odd `_pivotFails` (1, 3) drives forward at EffectiveCreepSpeed
  with full steering lock toward the path side instead of reversing, gated on
  forwardClear > EffectiveStopDistance * 2 (Code/AICarDriver.cs:597). Front clearances
  are now computed before this branch (GetFrontClearanceHelper at line 587).
- 4 failures teleport: `_pivotFails >= 4` calls TryTeleportHelper( "pivot" ) directly,
  respecting _untilTeleportRetry (Code/AICarDriver.cs:605-611).
- Second reverse swings the other way: EnterTurnReverseHelper flips noseSide when
  `_pivotFails == 2` (Code/AICarDriver.cs:858).
- Reverse log carries real clearances: EnterTurnReverseHelper takes leftClear/rightClear
  and passes them to TelemetryReverseHelper instead of 0f, 0f (REVIEW N3; call site
  Code/AICarDriver.cs:616, definition Code/AICarDriver.cs:855).
- Bounded widening snap: WidenCornersHelper rejects a navmesh snap farther than
  offset + 60 flat or more than 60 in z from the original corner point, keeping the
  original (REVIEW M1; Code/AICarDriver.cs:1242-1243). CornerOutsideOffset default
  stays 150.
- NaN-safe flat heading helper: FlatForwardHelper (Code/AICarDriver.cs:1694) replaces
  the three raw `WorldRotation.Forward.WithZ( 0f ).Normal` uses (lines 561, 843, 863).
- DriverVersion const bumped to "v5-pivot-escape"
  (Code/AICarDriver.Telemetry.cs:35; the file now shows v9).
- Parent fix on top of the implementer: the forward pivot previously dropped out above
  StuckSpeed*2, stalling the creep; it now stays on at any speed once chosen
  (Code/AICarDriver.cs:595-597).

## Results
Epoch rows from analyze.py --live (metrics per car-minute). v5 and v4 ran in different
sessions with random destinations, so numbers are only roughly comparable.

| version | session | car-min | score | arr | blk | rev | tele | flip | offnav |
| v5-pivot-escape (none) | 20261005-223357 | 29.5 | -26.8 | 1.15 | 10.10 | 10.50 | 11 | 2 | 33% |
| v4-reverse-cap (none) | 20261005-205604 | 15.0 | -9.4 | 1.60 | 10.13 | 11.93 | 7 | 0 | 26% |

## LLM observations
- Turn reverses 85 -> 62, but blocked-triggered reverses 79 -> 159; total reverses only
  slightly down. The livelock escape worked but shifted pain elsewhere.
- Block hotspots were sidewalk props: SidewalkPoles, BusStop, GiveWay sign, trees.
- Root-cause conclusion: the navmesh bakes only the 'roadnav' bodies with AgentRadius 60,
  so paths legally ran past or through sidewalk props the car then bumped; the agent asked
  the human to change the scene rather than patch the driver.

## Human observations
- "tons of light poles and other similar small objects not getting caught by the whiskers.
  I think the whiskers could probably be thicker."

## Outcome
Mixed. The pivot escape removed the turn-reverse livelock (turn reverses down a third),
and the M1/N5 fixes closed the review findings, but the score regressed (-9.4 -> -26.8),
arrivals dropped and teleports rose, partly against a much longer v5 session (29.5 vs 15.0
car-min). The blocking problem was diagnosed as a navmesh/scene gap, not a driver bug:
this led directly to the human's v6 scene work (sidewalks in nav, props tagged as blockers,
AgentRadius 60 -> 110) plus WhiskerThickness 25.
