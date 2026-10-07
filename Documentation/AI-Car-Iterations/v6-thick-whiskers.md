# v6-thick-whiskers: Thicker edge whiskers plus the human's sidewalk/navmesh scene rework

## Context
v5 ended with a split diagnosis. The LLM found that blocked-car hotspots were sidewalk
props (SidewalkPoles, BusStop, GiveWay sign, trees) because the navmesh only baked
'roadnav' bodies with AgentRadius 60, so paths legally ran past and through them. The
human watching play agreed and pointed at the whiskers. This version pairs a code fix
(thick whiskers that actually cover the car width) with a large scene rework by the
human, so driver changes and scene changes are tangled in the same run.

## Changes
Code (driver, v6):
- Edge whiskers are now inset by their own radius via WhiskerOriginHelper
  (Code/AICarDriver.cs, ~line 1506). With WhiskerThickness > 0 the side whisker
  origins pull inward by min(WhiskerThickness, |lateral offset|), so the swept
  spheres cover exactly the car width instead of leaving gaps at the hull edges
  or overhanging past them. The helper reads the live property each call, so a
  tuning.json change to WhiskerThickness takes effect immediately.

Tuning (thick25 revision):
- WhiskerThickness 10 -> 25.
- WhiskerHeight 35 -> 45.

Human scene changes (before this run, not driver code):
- Sidewalk pieces included in the navmesh at lowest priority so they are avoided
  but reachable as a fallback.
- Various sidewalk props tagged with 'roadnav' and marked as blockers, carving
  them out of the mesh.
- Some collider pieces fixed.
- AgentRadius 60 -> 110.

## Results
Metrics are per car-minute; score is the analyzer composite (higher better).

| version | session | car-min | score | arr | blk | rev | tele | flip | offnav |
| v6-thick-whiskers (thick25) | 20261006-000523 | 22.4 | -29.8 | 0.58 | 16.73 | 10.30 | 6 | 0 | 5% |
| v5-pivot-escape (no tuning) | 20261005-223357 | 29.5 | -26.8 | 1.15 | 10.10 | 10.50 | 11 | 2 | 33% |

Caveats: these are different sessions with random destinations, so numbers are only
roughly comparable. The scene itself changed a lot between the two runs (sidewalks
in nav, blocker tags, AgentRadius 60 -> 110), so most of the offnav and some of the
block movement is scene effect, not whisker effect.

## LLM observations
- offnav dropped 33% -> 5%, almost certainly the scene rework doing what v5 asked
  for, not the whiskers.
- Blocks got worse: 10.1 -> 16.7 per minute, and arrivals fell (1.15 -> 0.58).
  The dominant cause was flapping, not new collisions: one car toggled Blocked
  <-> Driving 126 times at a single tree without ever reversing.
- Mechanism: a thick whisker whose sphere starts inside the trunk reads hit/miss
  on alternate ticks as the car jitters, and Blocked resumed driving on the first
  clear tick. The exact-fit origin math was fine; the state machine had no
  hysteresis to absorb the flicker.
- Teleports (11 -> 6) and flips (2 -> 0) both improved.

## Human observations
Motivating feedback on v5 that led to this version's tuning:
- "tons of light poles and other similar small objects not getting caught by the
  whiskers. I think the whiskers could probably be thicker."
Scene work report before the v6 run:
- "Sidewalk pieces are now included in nav but w lowest prio so it's avoided.
  Various sidewalk props are now tagged with roadnav and marked as blockers.
  Some collider pieces are fixed."

## Outcome
Mixed. The scene rework was the clear win: offnav collapsed to 5% and the sidewalk
props v5 complained about are now carved out of the navmesh. The thick whiskers
themselves made the score worse (-29.8 vs -26.8) because the Blocked state flapped
against them at obstacles, inflating block counts and stalling arrivals. The fix is
state-machine hysteresis, not thinner whiskers, so thick25 was kept and v7 added a
0.3 s clear-front requirement before resuming Driving. Open question carried
forward: whether AgentRadius 110 is now too generous after the v5 corner-cutting
problem was solved differently.
