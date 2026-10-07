# v11-reverse-counting: honest stuck counters (v11 / v11b / v11c)

## Context
The v10 analysis of scene2 (v10/ANALYSIS.md) found 41 of 64 teleports were preceded by ~6 reverses
within 10 s: the cars stuck in place, rocking between reverses, until the stuck timer fired. The cause
was that `_reverseCount` reset on any waypoint advance, so a car rocking in place "passed" short merged
path segments and cleared the counter before it could escalate. One saga, three attempts: v11 (reverted),
v11b (kept, but its fast escape tier died), v11c (kept). v11 was implemented by a subagent from
v11/SPEC.md on staging copies, parent copied it in at ~16:58, reverted at 17:13; v11b live ~17:26
(spec v11b/SPEC.md); v11c live ~18:10 (spec v11c/SPEC.md).

## Changes
- v11-reverse-progress (REVERTED): `_reverseCount` and `_pivotFails` no longer reset on waypoint advance;
  they reset only when the car leaves StuckAreaRadius (300u) of its stuck anchor.
- v11b-progress-count (kept): count a reverse only if the car is within ReverseProgressDistance (150) of
  the previous counted reverse; the waypoint-advance reset stays, but only while Driving and away from that
  mark; `_pivotFails` resets on new destinations.
- v11c-repick-loop (kept): the 2nd repick within StuckAreaRadius of the previous one teleports immediately
  (reason "repick-loop", RepickChainTeleport 2); chain resets on arrival and teleport. Review fixes: the
  chain now decays when the car leaves StuckAreaRadius or after 90 s, resets on the idle pick, and the dead
  `_reverseStartPos` field (plus a tautological Driving-state check) was removed; these shipped when v12
  rebased onto v11c.

## Results
Session 20261006-163149 (scene3), per car-minute. Rows copied exactly from the shared facts file.

| session | key | car-min | score | arr | blk | rev | tele | flip | offnav |
|---|---|---|---|---|---|---|---|---|---|
| 20261006-163149 | v11-reverse-progress\|thick25-corner350-cruise1800-scene3 | 79.0 | +1.7 | 1.16 | 6.93 | 9.48 | 28 | 0 | 3% |
| 20261006-163149 | v11b-progress-count\|thick25-corner350-cruise1800-scene3 | 90.2 | +11.3 | 1.20 | 5.72 | 8.81 | 17 | 0 | 1% |
| 20261006-163149 | v11c-repick-loop\|thick25-corner350-cruise1800-scene3 | 49.5 | +24.2 | 1.29 | 5.17 | 6.60 | 15 | 1 | 0% |
| 20261006-163149 (car0+car1) | v10-corner-curve\|...scene3 | 54.0 | +15.7 | 1.30 | 5.85 | 7.42 | 14 | 1 | 1% |
| 20261006-163149 (car0+car1) | v11-reverse-progress\|...scene3 | 53.3 | +4.2 | 1.15 | 6.46 | 9.12 | 17 | 0 | 2% |
| 20261006-163149 (car0+car1) | v11b-progress-count\|...scene3 (open) | 73.4 | +14.2 | 1.23 | 5.68 | 8.14 | 10 | 0 | 0% |

Prose rows from the same facts: v10 baseline on scene3 43.7 car-min, +15.5, arr 1.49, blk 6.16, rev 8.01,
tele 17; the v10 revert run before v11b pooled both v10 scene3 epochs, 58.8 car-min, +15.3, arr 1.38,
blk 6.16, rev 7.81, tele 20; v10 same session (81.0 car-min) +15.2, arr 1.32, blk 5.95, rev 7.66, tele 25
(17 pivot, 7 stuck); v11b final row (134.6 car-min, all 3 cars incl. the human-modified car2 for ~25 min)
+13.5, arr 1.32, blk 5.85, rev 8.95, tele 27; v11c final-ish row before v12 (69.9 car-min, 3 cars) +26.1,
arr 1.39, blk 4.89, rev 6.44, tele 21, flips 3.
car2 caveat: the human changed car2's torque to 90000 and max wheel angle to 45 for ~20+ min mid-window
and asked to throw its data away; from then on comparisons use car0 + car1 only (analyzer run on a
symlinked copy without car2: scratch/aicar-live/nocar2), i.e. the (car0+car1) rows above.

## LLM observations
- v11 regressed the score from +15.5 to +1.7: honest slow progress in the 100-300u band no longer reset
  the counter, so cars dropped fresh routes in slow traffic. Same-spot repick chains +121%
  (12 chains/28 repicks -> 25/62) and teleports did not drop. The v11 review additionally found
  `_pivotAnchor` not moved on the re-anchor reset and new destinations resetting `_reverseCount` but not
  `_pivotFails`; both accepted and fixed in v11b.
- v11b: counting works (count=3 chains repick at 1-3u steps); teleports/min 0.31 -> 0.19, all "stuck",
  0 pivot; but same-spot repick chains 0.35 -> 0.60/min and the pivot teleport tier was dead because the
  repick zeroes `_pivotFails` (pivot teleports 17 -> 0, stuck teleports 7 -> 16, each costing the full
  TeleportAfter 20 s). Accepted -> v11c. The review rejected the "150-300u oscillation livelock" claim
  (the stuck timer still fires) and flagged `_reverseStartPos` dead plus the Driving-state check
  tautological; both removed in v11c.
- v11c: the first two repick-loop teleports were both genuine. Trace 1: one car pinned between two trees,
  3 reverses moved it <30u. Trace 2: one car boxed in by a skip and a shop wall, 0u moved. Both teleported
  ~10 s after first getting stuck instead of 20 s+. Wedging pattern: reverses end at the 0.8 s minimum
  because the rear is blocked, so the car barely moves.
- v11c review: no major defect; a car is never teleported during normal driving (only reachable after 3
  no-progress reverses). Accepted as moderate: the chain had no time/distance decay, so a new stuck episode
  near an old repick spot skipped one repick try (~3 times per 45 car-min in replay); fixed with the decay,
  90 s window and idle-pick reset above.
- Weaving note seen while v11 ran (from code, to be measured): steering maps heading error through
  sign*sqrt(angle/FullSteerAngle), a very high gain near zero error, a likely weaving source; sent to the
  v12 analysis.

## Human observations
While watching v11 (verbatim): "The cars veer left and right towards the next corner despite having no
obstacles. My guess is 2 things happening together: 1. The car is strictly following the navmesh-given
path, instead of just aiming for the next corner turn. 2. The ai is over correcting on each turn. Consider
these. I think the ai should have 2 modes: Mode A: strictly following the given nav path. and Mode B: The
"follow target" is just the next turn in the nav. (as usual, validate, i could be wrong)" -> sent to an
analysis subagent (v12/BRIEF.md). On the data (verbatim): "throw away data from ai car 2 for this round. I
changed its torque to 90000 and max wheel angle to 45 for testing purposes over the last ~20+ minutes.
It'll muddy the data." then "Sorry "ai car (test 1)" not ai car 2."

## Outcome
v11 reverted; v11b's counting and v11c's repick-loop escape kept. v11c is the base for v12: v12-corner-aim
went live ~18:34 rebased onto v11c (carrying the v11c review fixes), and v11c's best 3-car row (+26.1,
arr 1.39, rev 6.44) was the joint-best score in the loop at that point.
