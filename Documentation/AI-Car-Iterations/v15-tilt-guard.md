# v15-tilt-guard: cut throttle and reverse when the car noses up on an obstacle

## Context
After v14 was kept, a forensics subagent ranked the remaining failure modes. Its top finding claimed
"Collision 3" (48% of stuck time) and "Collision 2" (20%) were invisible blockers and proposed
ExcludeTags("roadnav") on the whiskers. Verification REJECTED this: the scene compile
(minimal_scene_data/compiled/.../.scene.json, built at play start) merges all static geometry by tag set
into 'Collision 0' (dome,world), 'Collision 1' (world), 'Collision 2' (roadnav: roads, sidewalks,
re-tagged props), 'Collision 3' (untagged statics) plus ~139 'Aggregate N' renderers. They are the normal
visible world, and contact z~281 is just the whisker height (road ~234 + WhiskerHeight 45). Excluding
roadnav would blind the whiskers to geometry the cars physically hit 63 times. Side effect: telemetry
object names are lost for most static hits since the scene compile; only positions (at=) survive. The
forensics clearance claim (AgentRadius 55 < hull half-width 50 + whisker 25) was also not supported as the
scrape cause; inside-turn scrapes ran ~0.7/min.

Three tuning A/Bs then tested driver-side causes of those scrapes; all three were reverted:
- physcal-la180 (LookAheadDistance 300 -> 180, 18.7 car-min vs 91.1): inside-turn scrapes 0.70 vs
  0.71/min, turning |pathdev| p50 107 vs 107 (unchanged), score +27.0 vs +22.1, arr 1.55 vs 1.39,
  blk 3.26 vs 4.11, but hard 0.59 vs 0.30/min, dynamic coll 0.75 vs 0.31/min, flips 2 vs 0,
  tele 0.32 vs 0.25/min. Pure-pursuit corner-cut hypothesis REFUTED; reverted 03:25.
- physcal-widen250 (CornerOutsideOffset 250, 17.5 car-min): score +6.2, arr 1.03, blk 4.64,
  coll 4.69/min, flips 3, inside-turn scrapes 0.92/min (worse than 0.71), turning pathdev p50 110
  unchanged. REVERTED 03:32:53.
- physcal-thr450 (ThrottleResponse 900 -> 450, 19.9 car-min vs 78.6): m/min 413 vs 375, arr 1.66 vs
  1.40, but blk 5.37 vs 4.22, rev 7.67 vs 6.21, coll 5.37 vs 4.41/min, hard 0.80 vs 0.37/min,
  tele 0.35 vs 0.24/min, score +16.9 vs +21.0; scrapes 0.70/min unchanged. Speed-for-safety trade;
  goal and the human's observation favor 900. REVERTED at 03:41.

Two corrections came out of this window. CORRECTION 1: the scene ai_car instance carries serialized
overrides CornerAimTraceRadius=50, CornerAimCorridor=60, ThrottleResponse=125, CornerSpeed=700,
CruiseSpeed=1200 (plus Engine_Torque, Wheel_MaxTurnAngle, MassOverride and others). The stale radius 50
that hid during the v12b-e debugging came from this scene override: a hotload-preserved value and a scene
override look identical in telemetry, so the lesson "confirm effective values from telemetry" stands.
CORRECTION 2: effective ThrottleResponse before physcal was 125 (scene override), not the code default
250, so physcal changed it 125 -> 900 = 7.2x, not the intended 3.6x; the torque/mass-equivalent gain is
125*3.6 = 450 and 900 is ~2x softer, which explains the median Driving fwd-target of -82.

The trigger for v15 was a flip trap: 4 of 5 flips at ~(-5650,795), heading ~-170. Sample trace (car0
e002, car1 e004): Blocked heading west by Collision 2 (roadnav geometry) at ~(-5627,730), reverse, then
Driving with pathang 110-130 and steer full lock noses into a ~100u-tall obstacle at ~(-5715,790): z
224 -> 333, upz 0.98 -> 0.26, at fwd ~0 and throttle 0.17-0.35 over ~1 s, then flip. Torque 90000 lets a
partial throttle climb what the old car stalled against. The 5th flip fell off the map at (-5607,-198),
z -633. Flips appeared only in the la180/widen250 epochs (0 in 75 car-min baseline), so likely route
exposure to the trap, not the tuning itself. v15 implemented by a subagent from v15/SPEC.md. Driver code:
Code/AICarDriver.cs, telemetry version const in Code/AICarDriver.Telemetry.cs (line 35).

## Changes
- TickDrivingHelper tilt guard: WorldRotation.Up.z (same value the telemetry logs as upz) below
  `TiltGuardUpZ` 0.9 -> throttle 0 and brake false, so the car rolls back down instead of climbing.
  Hotload 0 -> 0.9; a negative value disables.
- Tilted for more than 0.5 s continuously (TimeSince `_sinceTilted`, reset when upright and on state
  entry) -> EnterReverseHelper, with telemetry trigger "tilt" so the reverses are attributable.
- The telemetry config block logs the effective TiltGuardUpZ.
- DriverVersion bumped to "v15-tilt-guard". Staged by subagent, compiled clean in the scratch mirror,
  deployed live 03:44:26, editor compile green, all 3 cars on
  v15-tilt-guard|physcal-thr900-brk240.

## Results
Stager's false-trigger quantification on old data (session 024815, 15,441 Driving samples): upz < 0.9 in
110 samples (0.7%); 93 of them are obstacle-stall episodes (fwd ~0, full lock, fclear 26-42) at hotspots
(-4241,4495), (1270,-3428), (111,-2049), (98,1903); only 2 are legit crest driving (fwd 134-178,
upz 0.87-0.88, ~0.2 s, throttle cut only, never reaching the reverse timer).

v15 review data: 27.1 car-min, 3 tilt reverses (0.11/min), each upright within 10 s, no loop; 0 flips,
but v14 on thr900 also had 0 flips in 88.8 car-min, so no credit to the guard yet. Hard collisions
0.52 vs 0.28-0.37/min (n=14, Poisson-plausible, watch).

## LLM observations
Adversarial review: NO major defect, keep. The loop is bounded (live chain: stuck -> tilt -> blocked
reverse -> repick -> repick-loop teleport in ~12 s); WorldRotation is the car root = Rigidbody object,
identical to the telemetry upz; the analyzer parses trigger=tilt generically.
- Accepted and folded into the v16 deploy: (3) the guard forced brake=false, overriding v14's
  backward-roll brake; keep the v14 brake when rolling backward. (6) `_tiltTrigger`/`_stuckTrigger`
  were not cleared when TelemetryReverseHelper early-returns with telemetry off; clear before the
  early return.
- REJECTED: (4) a 0.90/0.95 hysteresis. A blip under 0.9 followed by a legit ~20 deg ramp (upz ~0.94)
  would keep the timer running and reverse on a good road; resetting on any upright tick means only
  continuous tilt reverses, and the allowed chatter only costs brief throttle dips on crests.
- CORRECTION 3 from the same window: for 147 inside-turn scrapes the contact sits at longitudinal p50
  +78 (front bumper ~+108), lateral 49-51: the inside FRONT flank clips the corner, not rear-quarter
  off-tracking as first said, and the front whisker fan cannot see objects beside the flank. After
  la180/widen250/thr450 all left the ~0.7/min scrapes unchanged, that needs a driver-side change:
  v16 (front-flank sideways probes, FlankMargin 40) was specified from this.

## Human observations
None recorded.

## Outcome
Kept. Review fixes 3 and 6 folded into the v16 deploy; tuning stays physcal-thr900-brk240. The tilt
guard stays in as cheap insurance for the flip trap; the real inside-scrape fix moves to v16-flank-cap.
