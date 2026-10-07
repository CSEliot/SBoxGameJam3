# v7-block-hysteresis: Blocked state only resumes after 0.3 s of clear front

## Context
Part of the live feedback loop (never-stop play, LLM tunes/patches via hotload) on
`sboxgamejam3`, driver in `Code/AICarDriver.cs`. v6 added thick edge whiskers
(WhiskerThickness 25, WhiskerHeight 45) and the human's scene rework (sidewalks in nav at
lowest priority, props tagged as roadnav blockers, AgentRadius 60 -> 110). That fixed offnav
(33% -> 5%) but exposed a state-machine bug: a thick whisker whose origin starts inside an
obstacle (e.g. a tree trunk) alternates hit/miss on consecutive ticks, and Blocked resumed on
the first clear tick. One car flipped Blocked <-> Driving 126 times at one tree without ever
reversing, driving blocked events to 16.7/min. v7 is the fix for that oscillation.

## Changes
Logic (one change): Blocked now requires the front to stay clear continuously for 0.3 s
before returning to Driving/Idle. A new `TimeSince _sinceBlockedObstacle` field
(AICarDriver.cs ~line 382) is zeroed every tick the front is within
`EffectiveStopDistance * 1.5` (~line 703) and on entry to Blocked via EnterBlockedHelper
(~line 720); resumption only happens when `_sinceBlockedObstacle > 0.3f`. A hotload note in
the code: the default TimeSince reads as "long ago", so a car already Blocked at hotload may
resume once immediately, but the timer is reset on every later Blocked entry.

Tuning experiment: `thick25-wide260` raised CornerOutsideOffset 150 -> 260 (epoch e002),
then reverted to 150 after the regression (see Results / LLM observations).

## Results
Metrics per car-minute (analyzer composite score, arrivals, blocked events, reverses,
teleports, flips, % time off navmesh). Session 20261006-000523.

| key | car-min | score | arr | blk | rev | tele | flip | offnav |
|---|---|---|---|---|---|---|---|---|
| v6-thick-whiskers, thick25 | 22.4 | -29.8 | 0.58 | 16.73 | 10.30 | 6 | 0 | 5% |
| v7-block-hysteresis, thick25 (first run, e001) | 21.4 | -8.2 | 1.08 | 8.28 | 10.02 | 8 | 1 | 4% |
| v7-block-hysteresis, thick25-wide260 (e002) | 52.4 | -48.8 | 0.76 | 6.91 | 15.79 | 51 | 1 | 5% |
| v7-block-hysteresis, thick25 (e001+e003 combined) | 106.5 | -31.6 | 0.83 | 6.43 | 13.13 | 74 | 1 | 5% |

## LLM observations
- The hysteresis halved blocks: 16.7 -> 8.3/min, and score improved -29.8 -> -8.2 in the
  clean e001 run. Arrivals nearly doubled (0.58 -> 1.08). Reverses stayed ~10/min, so the
  fix stopped the flip-flopping without creating new escape churn.
- Why wide260 regressed: score fell to -48.8 with 51 teleports. The subagent analysis found
  329 of 330 stuck reverses of the worst car within 80 u of a widened path point. Widening
  corners by 260 u pushes targets past the road edge, and the widening snap lands them on
  the lowest-priority sidewalk nav that the scene rework added, i.e. corners get pulled
  into street furniture (poles, bus stops, trees). The car then reverse-loops against that
  furniture until the stuck-recovery teleports it. Offset reverted to 150.
- The combined thick25 figure (e001+e003, score -31.6, 74 teleports) is misleading: e003 was
  dominated by one car frozen in mid-air after a teleport. Root cause only found later in
  v8: the rigidbody went to sleep at drop height with wheels off the ground, so no vehicle
  force was applied and nothing woke it (1528 of 3182 samples at an identical position,
  frozen 500+ s). That is a v8 fix (keep the body awake), not a v7 logic fault.

## Human observations
None recorded.

## Outcome
Kept. The 0.3 s hysteresis is the direct fix for v6's whisker hit/miss oscillation and held
the best score of its era (-8.2) until corner speed tuning landed in v8. wide260 is a
negative result on record: corner widening beyond roughly the road width interacts badly
with low-priority sidewalk nav. The stuck-in-air teleports that polluted later v7 runs
shaped the v8 wake-body change.
