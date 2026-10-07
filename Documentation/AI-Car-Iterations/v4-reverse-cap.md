# v4-reverse-cap: standstill check fixed to use absolute speed and reverse speed capped

## Context

v3's three-point-turn standstill check used signed forward speed, so a car rolling
backwards fast re-entered a turn reverse and reversed at 500-640 u/s under aggression
throttle. v4 closed that hole (part of the same uncommitted v2..v7 work; commit f4042e0
was the live-loop infrastructure). Telemetry: session 20261005-205604, epoch e003,
3 cars, ~15 car-min. Destinations are random, so cross-session numbers are roughly
comparable only.

## Changes

All in Code/AICarDriver.cs:
- Standstill check for the three-point turn now uses |forward speed| instead of the
  signed value, so a car rolling backwards cannot re-enter a turn reverse at speed.
- Reverse speed capped: MaxReverseSpeed 250, throttle cut above the cap.

## Results

| session | key | car-min | score | arr | blk | rev | tele | flip | offnav |
| 20261005-205604 | v3-ledge-uturn|none | 14.2 | -54.2 | 0.92 | 8.60 | 14.17 | 6 | 0 | 34% |
| 20261005-205604 | v4-reverse-cap|none | 15.0 | -9.4 | 1.60 | 10.13 | 11.93 | 7 | 0 | 26% |

Best score of the loop so far. Flips stayed at 0 and the dangerous high-speed reverses
were gone (stuck-trigger reverses 51 -> 15, no turn reverse entered above 80 u/s), but
blocked events regressed to 10.13/min (events 84 -> 152).

## LLM observations

From v4/ANALYSIS.md (epoch e003 vs v2 baseline e001):
- Score improved mostly via fewer teleports (1.08 -> 0.47/min), flips 5 -> 0, hard
  collisions down; arrivals 1.60/min, the best rate yet.
- The Blocked regression is hull-corner contact: 72 of 152 blocks (47%) had only
  front-left/front-right whisker hits and no center hit; the corner-only class grew
  +132% over v2. Median FL hit distance on thin sidewalk props was 0 units; median
  block speed 106 u/s, so cars were creeping, not slamming.
- 71% of reverses (127 of 179) sat inside 17 detected loops. Worst case: car2 ran 25
  turn-reverses in 20 s at speed 0 with its rear against a wall while the front
  whiskers read clear, ending in a stuck teleport. A livelock, not a parameter issue.
- Proposed fixes in priority order: count failed pivot reverses and escape forward or
  teleport early; split "path blocked" from "corner brushing" (center-ray-only block
  gate); widen corner offsets further (150 vs a measured ~260-380 turning radius).

From v4/REVIEW.md (code review of v2-v4):
- Accepted finding M1: the corner-widening navmesh snap was unbounded - a 200-unit
  half-extent box search with only a direction check could jump a path point to a
  different road or level. Fixed in v5 (shift bounded to offset+60 flat, 60 in z).
- Accepted minors feeding v5: fake l=0 r=0 clearances in turn-reverse logs, and a
  NaN-prone flat-heading normal at arrival; both fixed in v5.
- Rejected finding M2: claim that the new static readonly ledge-probe array would be
  null after hotload and crash every tick. Rejected because v3/v4 kept driving after
  that hotload; telemetry contradicts the failure mode.
- Review confirmed v4 itself sign-correct: the abs() standstill test and the reverse
  cap work; noted the cap coasts rather than brakes, so a downhill reverse can exceed
  250 u/s.
- The v5 spec adopted the pivot-escape (cause 1) first; the corner brushing split and
  bigger widening came later via thick whiskers (v6) and tuning experiments (v7).

## Human observations

None recorded.

## Outcome

v4 removed the unsafe high-speed reverse class from v3 and produced the best score to
date (-9.4), while exposing the real blockers: standstill turn-reverse livelocks and
hull-corner whisker grazes firing full stops. Its analysis directly specified
v5-pivot-escape (failed-reverse counting, forward creep, alternate swing direction,
4-failure early teleport) and its review produced the bounded widening snap.
