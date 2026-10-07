// Project: sboxgamejam3
// File:    AICarDriver.cs
// Author:  cseliot
// Created: 2026.10.03
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 

using System;
using System.Collections.Generic;
using Bugges.VehicleController;
using Sandbox.Engine.Resources;
using Sandbox.Navigation;

namespace Sandbox;

/// <summary>
/// A single AI driver for a bugge <see cref="VehicleController"/> car (not a traffic system).
/// Picks random reachable destinations on the scene navmesh, follows the path with pure-pursuit
/// steering, probes ahead with ray "whiskers" to slow down and steer around anything solid
/// (world, other cars, players), and backs up while steering away when blocked or stuck.
/// The longer it stays stuck in one spot, the more aggressive it drives; past a time limit it
/// teleports to a fresh spot on the navmesh.
///
/// Drives the car through <see cref="VehicleController.UseExternalInput"/>, so the car never reads
/// the local player's keyboard. Runs only where the car is simulated: the host for a scene-placed
/// unowned car, or solo play. On clients the car is a physics proxy and this component idles.
/// </summary>
[Icon( "smart_toy" )]
public sealed partial class AICarDriver : Component
{
	public enum DriveState
	{
		/// <summary>No usable path yet (navmesh not ready, no destination found).</summary>
		Idle,
		/// <summary>Following the navmesh path.</summary>
		Driving,
		/// <summary>Stopped with something right in front; waits for it to move before backing up.</summary>
		Blocked,
		/// <summary>Backing up and steering away from an obstacle.</summary>
		Reversing
	}

	// ------------------------------------------------------------------ Destinations

	/// <summary>Closest a new destination may be to the car (flat distance, world units).</summary>
	[Property, Group( "Destinations" )] public float MinDestinationDistance { get; set; } = 1500f;

	/// <summary>Farthest a new destination may be from the car (flat distance, world units).</summary>
	[Property, Group( "Destinations" )] public float MaxDestinationDistance { get; set; } = 6000f;

	/// <summary>Random candidates tried per destination pick. Each one costs a navmesh path query.</summary>
	[Property, Group( "Destinations" ), Range( 1, 32 )] public int DestinationPickAttempts { get; set; } = 8;

	/// <summary>
	/// Search box half-size used to snap the car and destination candidates onto the navmesh.
	/// Too small and a car parked off the road mesh never finds a start point.
	/// </summary>
	[Property, Group( "Destinations" )] public float NavMeshSnapRadius { get; set; } = 500f;

	/// <summary>
	/// v17: minimum free width (units, measured across the route at whisker height) a new route
	/// may have anywhere past its first CorridorCheckSkip units. Routes narrower than this are
	/// declined at destination pick (the thin waterfront-promenade band north of Bld_OfficeOctagon_01
	/// is ~160u for a 217x100 car: drivable straight, unrecoverable once blocked). 0 on a hotloaded
	/// instance reads as 200; negative disables the corridor check.
	/// </summary>
	[Property, Group( "Destinations" )] public float MinCorridorWidth { get; set; } = 200f;

	/// <summary>
	/// v17: route length (flat) at the start (where the car already is) and at the end (destination
	/// approach) that the corridor check skips. 0 on a hotloaded instance reads as 200; negative reads as 0.
	/// </summary>
	[Property, Group( "Destinations" )] public float CorridorCheckSkip { get; set; } = 200f;

	/// <summary>
	/// v17: distance between corridor samples along a route. 0 on a hotloaded instance reads as 120.
	/// Long routes grow it to keep the sample count at or under ~150.
	/// </summary>
	[Property, Group( "Destinations" )] public float CorridorSampleSpacing { get; set; } = 120f;

	/// <summary>
	/// v18: flat radius (units) around a learned no-go zone that a route may not pass within, and
	/// the merge radius when recording a new stuck spot into the shared zone store. 0 on a hotloaded
	/// instance reads as 250; negative disables the whole feature.
	/// </summary>
	[Property, Group( "Destinations" )] public float NoGoZoneRadius { get; set; } = 250f;

	/// <summary>
	/// v18: hits a spot needs before its zone starts rejecting routes (and teleport targets).
	/// 0 on a hotloaded instance reads as 2.
	/// </summary>
	[Property, Group( "Destinations" )] public int NoGoZoneMinHits { get; set; } = 2;

	/// <summary>
	/// v18b: seconds a zone must go without a counted hit before the next hit counts again (one
	/// trap episode = one hit). 0 on a hotloaded instance reads as 30; negative disables the gap,
	/// so every hit counts. Compared against the last COUNTED hit: a skipped hit never refreshes
	/// the clock, so two cars re-sticking every ~20 s still accumulate.
	/// </summary>
	[Property, Group( "Destinations" )] public float NoGoEpisodeGap { get; set; } = 30f;

	/// <summary>Flat distance to the destination that counts as arrived; a new one is picked.</summary>
	[Property, Group( "Destinations" )] public float ArrivalDistance { get; set; } = 250f;

	/// <summary>Seconds to wait before retrying when no destination or path could be found.</summary>
	[Property, Group( "Destinations" )] public float RetryDelay { get; set; } = 1f;

	// ------------------------------------------------------------------ Navigation areas

	/// <summary>
	/// Named navmesh areas (.navarea) a destination may be picked in. Empty = any named area
	/// except <see cref="DestinationForbiddenAreas"/>. Has no effect on the route itself.
	/// </summary>
	[Property, Group( "Navigation Areas" )] public HashSet<NavMeshAreaDefinition> DestinationAllowedAreas { get; set; } = new();

	/// <summary>Named navmesh areas a destination is never picked in (e.g. sidewalks).</summary>
	[Property, Group( "Navigation Areas" )] public HashSet<NavMeshAreaDefinition> DestinationForbiddenAreas { get; set; } = new();

	/// <summary>May a destination be picked on navmesh with no area assigned (plain road)?</summary>
	[Property, Group( "Navigation Areas" )] public bool DestinationAllowDefaultArea { get; set; } = true;

	/// <summary>
	/// Named navmesh areas the route may never cross. Leave empty to let each area's
	/// CostMultiplier only discourage it (a 100x sidewalk is still used when it is the only way).
	/// </summary>
	[Property, Group( "Navigation Areas" )] public HashSet<NavMeshAreaDefinition> RouteForbiddenAreas { get; set; } = new();

	// ------------------------------------------------------------------ Path following

	/// <summary>Base distance ahead along the path the car steers toward.</summary>
	[Property, Group( "Path Following" )] public float LookAheadDistance { get; set; } = 300f;

	/// <summary>Extra look-ahead per unit of speed (seconds). Higher = smoother, wider corners.</summary>
	[Property, Group( "Path Following" )] public float LookAheadTime { get; set; } = 0.3f;

	/// <summary>Flat distance from the path that triggers a re-path from the car's position.</summary>
	[Property, Group( "Path Following" )] public float OffPathDistance { get; set; } = 500f;

	/// <summary>How far ahead along the path corners are checked to slow down for them.</summary>
	[Property, Group( "Path Following" )] public float CornerLookDistance { get; set; } = 800f;

	/// <summary>
	/// v12: minimum turn angle (degrees) for a path point to count as a real corner the car may
	/// aim at across a straight stretch. 0 on a hotloaded instance falls back to 15.
	/// </summary>
	[Property, Group( "Path Following" )] public float CornerAimMinAngle { get; set; } = 15f;

	/// <summary>
	/// v12: maximum flat distance (units) at which the next real corner may replace the
	/// pure-pursuit look-ahead target. 0 on a hotloaded instance falls back to 1500.
	/// </summary>
	[Property, Group( "Path Following" )] public float CornerAimDistance { get; set; } = 1500f;

	/// <summary>
	/// v12: how far (units) the intermediate path points may sit from the straight car-to-corner
	/// line while the car still aims at the corner. A bend stepping outside this corridor is a
	/// jog around something real and keeps the polyline target. v12b (F2): 60 measured against
	/// widened points let inside-cut through; 0 on a hotloaded instance falls back to 30.
	/// </summary>
	[Property, Group( "Path Following" )] public float CornerAimCorridor { get; set; } = 30f;

	/// <summary>
	/// v12c: radius of the clear-shot sphere sweep in the corner-aim gate, in world units. It is a
	/// coarse "is the run to the corner roughly clear" gate, not a collision guarantee (the full-width
	/// whiskers are the real safety), so it is deliberately smaller than the body. v12 used 0.4 *
	/// half hull width (~40) and the aim fired ~40% of driving samples; v12b's half width + 20 (~70)
	/// and even a plain half width (~50) swept into the light poles and signs lining most roads and
	/// vetoed nearly every chord (aim fired 0-1%). Default ~40. 0 on a hotloaded instance falls back
	/// to 0.4 * hull width; live-tunable via tuning.json so it can be swept without a recompile.
	/// </summary>
	[Property, Group( "Path Following" )] public float CornerAimTraceRadius { get; set; } = 40f;

	/// <summary>
	/// v12 A/B switch for corner aiming. Inverted so a hotloaded instance (bools read false)
	/// keeps the feature ON: set true to fall back to pure pursuit everywhere.
	/// </summary>
	[Property, Group( "Path Following" )] public bool DisableCornerAim { get; set; } = false;

	/// <summary>
	/// v12f: how far along the car-to-corner chord the clear-shot trace runs, in world units. The
	/// chord can be up to CornerAimDistance (1500) long, and in a prop-lined city a body-width
	/// sweep over that whole length clips a light pole, sign or building on nearly every chord
	/// (measured: gate d blocked ~100% of chords that reached it, so the aim fired 0%). Corner aim
	/// only changes where the car POINTS; the whiskers still stop it from hitting anything, and the
	/// corridor gate already keeps the path within CornerAimCorridor of the chord. So the trace only
	/// needs to confirm the NEAR run is not straight into something. 0 on a hotloaded instance
	/// falls back to 500; live-tunable via tuning.json.
	/// </summary>
	[Property, Group( "Path Following" )] public float CornerAimTraceDistance { get; set; } = 500f;

	// ------------------------------------------------------------------ Speed

	/// <summary>Target forward speed on straights (world units per second).</summary>
	[Property, Group( "Speed" )] public float CruiseSpeed { get; set; } = 800f;

	/// <summary>
	/// Target speed at the corner itself (the final approach, a large heading error, or a turn of
	/// CornerFullAngle degrees). v10 curve: below CornerFullAngle the limit eases back up toward
	/// Min(CruiseSpeed, CornerSpeed*4), and from CornerFullAngle to 120 degrees it eases down to
	/// SharpCornerSpeed. See CornerSpeedForAngleHelper.
	/// </summary>
	[Property, Group( "Speed" )] public float CornerSpeed { get; set; } = 350f;

	/// <summary>
	/// Turn angle (degrees) at and beyond which CornerSpeed applies in full. Below it the limit
	/// eases from CornerSpeed toward Min(CruiseSpeed, CornerSpeed*4), easing out (speed drops fast
	/// early). 0 on a hotloaded instance falls back to 45.
	/// </summary>
	[Property, Group( "Speed" )] public float CornerFullAngle { get; set; } = 45f;

	/// <summary>
	/// Target speed for turns of 120 degrees or more (hairpins, U-turns via the path). Between
	/// CornerFullAngle and 120 the limit lerps CornerSpeed down to this. Clamped to never exceed
	/// CornerSpeed; 0 on a hotloaded instance falls back to 200.
	/// </summary>
	[Property, Group( "Speed" )] public float SharpCornerSpeed { get; set; } = 200f;

	/// <summary>
	/// Speed the car crawls at while closing the last gap to <see cref="StopDistance"/>. Must be
	/// stoppable inside StopDistance (CreepSpeed / ObstacleBrakingRate &lt; StopDistance) or the car
	/// touches what it stopped for.
	/// </summary>
	[Property, Group( "Speed" )] public float CreepSpeed { get; set; } = 80f;

	/// <summary>Speed error (units/s) that maps to full throttle. Lower = more aggressive throttle.</summary>
	[Property, Group( "Speed" )] public float ThrottleResponse { get; set; } = 250f;

	/// <summary>Brake when going this much faster than the target speed. Below it the car only coasts.</summary>
	[Property, Group( "Speed" )] public float BrakeMargin { get; set; } = 50f;

	/// <summary>
	/// Target-speed slope (per second) while closing on an obstacle: allowed speed =
	/// (distance - StopDistance) * this. Keep it below the car's real braking decay, or the car
	/// overshoots StopDistance. For car_normal (Brake_Strength 10, mass 2000) the library's brake
	/// math works out to roughly 1.2-1.4 per second: estimated from VehicleController.Brakes.cs,
	/// not measured in play.
	/// </summary>
	[Property, Group( "Speed" )] public float ObstacleBrakingRate { get; set; } = 1f;

	/// <summary>
	/// Deceleration (units/s per second) assumed when braking ahead of a corner or the destination:
	/// the speed allowed at distance d before a corner is sqrt(cornerSpeed^2 + 2 * this * d).
	/// v1 telemetry measured ~390 median with brakes held, so the default leaves margin.
	/// </summary>
	[Property, Group( "Speed" )] public float CornerBrakeDecel { get; set; } = 300f;

	// ------------------------------------------------------------------ Cornering

	/// <summary>
	/// How far (units) each path corner is pushed toward the outside of the turn, clamped to the
	/// navmesh. Navmesh corners hug the inside of a turn, but the car turns on a 250-500 unit radius,
	/// so following them exactly drags the inside of the car through the corner.
	/// </summary>
	[Property, Group( "Cornering" )] public float CornerOutsideOffset { get; set; } = 150f;

	/// <summary>
	/// Path points closer than this to the previous kept point are merged away, so one turn split
	/// over several tiny navmesh segments is read as one corner with its full angle.
	/// </summary>
	[Property, Group( "Cornering" )] public float MinPathPointSpacing { get; set; } = 120f;

	// ------------------------------------------------------------------ Steering

	/// <summary>Heading error (degrees) that gets full steering lock.</summary>
	[Property, Group( "Steering" ), Range( 5f, 90f )] public float FullSteerAngle { get; set; } = 35f;

	/// <summary>How fast the steering input follows its target (per second, exponential). 0 = instant.</summary>
	[Property, Group( "Steering" )] public float SteerResponse { get; set; } = 8f;

	/// <summary>
	/// Heading error (degrees) below which nothing is steered; it is subtracted from errors
	/// above it, so the car stops twitching around the aim line. Cannot be disabled: 0 or a
	/// negative value (0 is what a hotloaded instance of a new property reads) falls back to 2.
	/// </summary>
	[Property, Group( "Steering" )] public float SteerDeadband { get; set; } = 2f;

	/// <summary>
	/// Seconds of yaw rate subtracted from the heading error before it is steered (PD damping:
	/// a car already rotating toward the aim line gets a smaller correction, which kills the
	/// 3-4 s limit-cycle weave). Negative disables; 0 (what a hotloaded instance reads) falls
	/// back to 0.3.
	/// </summary>
	[Property, Group( "Steering" )] public float SteerYawDamping { get; set; } = 0.3f;

	// ------------------------------------------------------------------ Avoidance

	/// <summary>Base length of the forward whisker rays.</summary>
	[Property, Group( "Avoidance" )] public float WhiskerLength { get; set; } = 400f;

	/// <summary>
	/// Extra whisker length per unit of forward speed (seconds of look-ahead). Keep it at or above
	/// 1 / ObstacleBrakingRate so the car sees an obstacle early enough to stop for it.
	/// </summary>
	[Property, Group( "Avoidance" )] public float WhiskerLengthPerSpeed { get; set; } = 1f;

	/// <summary>Yaw angle of the two angled side whiskers (degrees).</summary>
	[Property, Group( "Avoidance" ), Range( 5f, 80f )] public float SideWhiskerAngle { get; set; } = 30f;

	/// <summary>Length of the side whiskers as a fraction of the forward whisker length.</summary>
	[Property, Group( "Avoidance" ), Range( 0.1f, 1.5f )] public float SideWhiskerScale { get; set; } = 0.7f;

	/// <summary>
	/// Height above the car origin the whiskers are cast at. Anything lower (curbs) is driven over.
	/// </summary>
	[Property, Group( "Avoidance" )] public float WhiskerHeight { get; set; } = 35f;

	/// <summary>Sphere-sweep radius of every whisker. 0 = pure rays (thin poles can slip between them).</summary>
	[Property, Group( "Avoidance" )] public float WhiskerThickness { get; set; } = 0f;

	/// <summary>
	/// Hits whose surface normal points up at least this much (normal.z) are treated as drivable
	/// ground (ramps) and ignored. 0.7 is roughly a 45 degree slope.
	/// </summary>
	[Property, Group( "Avoidance" ), Range( 0f, 1f )] public float GroundNormalZ { get; set; } = 0.7f;

	/// <summary>
	/// v16: how far beyond the hull side each front-flank probe looks (units). 0 (what a hotloaded
	/// instance reads) falls back to 60; a negative value disables the flank steering cap entirely.
	/// </summary>
	[Property, Group( "Avoidance" )] public float FlankProbeLength { get; set; } = 60f;

	/// <summary>
	/// v16: side gap (units between hull side and object) below which steering toward that side is
	/// scaled down linearly, reaching 0 at gap 0. 0 (what a hotloaded instance reads) falls back to
	/// 40; the effective margin is clamped to the effective probe length.
	/// </summary>
	[Property, Group( "Avoidance" )] public float FlankMargin { get; set; } = 40f;

	/// <summary>Extra steering (degrees) added when a whisker is touching at zero distance; scales with closeness.</summary>
	[Property, Group( "Avoidance" )] public float AvoidSteerAngle { get; set; } = 45f;

	/// <summary>An obstacle this close in front makes the car stop (see <see cref="BlockedWaitTime"/>).</summary>
	[Property, Group( "Avoidance" )] public float StopDistance { get; set; } = 120f;

	/// <summary>
	/// Seconds a stopped car waits for the obstacle in front to move before backing up.
	/// Randomised by +-25% per stop so two cars blocking each other don't move in lockstep.
	/// </summary>
	[Property, Group( "Avoidance" )] public float BlockedWaitTime { get; set; } = 1.5f;

	/// <summary>
	/// A point ahead/behind with no ground within this many units below the car's base (plus 0.6
	/// per unit of distance, for downhill roads) is a ledge and treated as an obstacle. 0 disables.
	/// </summary>
	[Property, Group( "Avoidance" )] public float LedgeDropHeight { get; set; } = 80f;

	// ------------------------------------------------------------------ Reversing / stuck

	/// <summary>Throttle used while backing up (0-1).</summary>
	[Property, Group( "Reversing" ), Range( 0.1f, 1f )] public float ReverseThrottle { get; set; } = 0.5f;

	/// <summary>Minimum time spent backing up once started (seconds).</summary>
	[Property, Group( "Reversing" )] public float MinReverseTime { get; set; } = 0.75f;

	/// <summary>Maximum time spent backing up (seconds).</summary>
	[Property, Group( "Reversing" )] public float MaxReverseTime { get; set; } = 2f;

	/// <summary>Reverse speed cap (units/s); throttle is cut above it.</summary>
	[Property, Group( "Reversing" )] public float MaxReverseSpeed { get; set; } = 250f;

	/// <summary>Length of the rear rays checked while backing up.</summary>
	[Property, Group( "Reversing" )] public float RearProbeLength { get; set; } = 250f;

	/// <summary>Stop backing up when something behind is this close.</summary>
	[Property, Group( "Reversing" )] public float RearStopDistance { get; set; } = 80f;

	/// <summary>Below this forward speed while throttling the car counts as stuck.</summary>
	[Property, Group( "Reversing" )] public float StuckSpeed { get; set; } = 40f;

	/// <summary>Seconds of being stuck before backing up.</summary>
	[Property, Group( "Reversing" )] public float StuckTime { get; set; } = 1.5f;

	/// <summary>
	/// Body up-vector z below which a forward-driving car is treated as climbing an obstacle:
	/// throttle is cut and, if it stays tilted, the car backs off. 0 on a hotloaded instance
	/// falls back to 0.9; negative disables.
	/// </summary>
	[Property, Group( "Reversing" )] public float TiltGuardUpZ { get; set; } = 0.9f;

	/// <summary>
	/// Back-up attempts without reaching a new path corner before dropping the destination for one
	/// whose route starts more than 60 degrees away from the blocked heading. 0 = never.
	/// </summary>
	[Property, Group( "Reversing" )] public int RepickAfterReverses { get; set; } = 3;

	/// <summary>
	/// Flat distance the car must put between reverses for the second one to start a fresh reverse
	/// count: a reverse entered within this distance of the last one is counted toward
	/// <see cref="RepickAfterReverses"/>, one entered beyond it is the first reverse at a new spot.
	/// Keep it under <see cref="StuckAreaRadius"/> and over the ~118u median shuffle displacement.
	/// 0 (hotload default) falls back to 150.
	/// </summary>
	[Property, Group( "Reversing" )] public float ReverseProgressDistance { get; set; } = 150f;

	/// <summary>
	/// Repicks chained within <see cref="StuckAreaRadius"/> of one another escalate to an
	/// immediate teleport once the chain reaches this length (v11c: the new route keeps starting
	/// into the same blocked spot and each loop burns up to <see cref="TeleportAfter"/> seconds
	/// before the stuck teleport). 0 (hotload default) falls back to 2; a negative value disables
	/// the escalation and keeps plain repicks.
	/// </summary>
	[Property, Group( "Reversing" )] public int RepickChainTeleport { get; set; } = 2;

	/// <summary>
	/// Path heading error (degrees) beyond which the car stops and backs around (three-point turn)
	/// instead of trying a forward full-lock turn. 0 disables.
	/// </summary>
	[Property, Group( "Reversing" )] public float UTurnAngle { get; set; } = 120f;

	/// <summary>A reverse ends early once the path heading error is below this (degrees).</summary>
	[Property, Group( "Reversing" )] public float ReverseAlignedAngle { get; set; } = 60f;

	// ------------------------------------------------------------------ Stuck area / teleport

	/// <summary>
	/// The car counts as "not moving" while it stays within this flat distance of where it last
	/// settled. Drives both <see cref="Aggression"/> and the stuck teleport.
	/// </summary>
	[Property, Group( "Stuck Area" )] public float StuckAreaRadius { get; set; } = 300f;

	/// <summary>Seconds inside the stuck area before teleporting to a new spot on the navmesh. 0 = never teleport.</summary>
	[Property, Group( "Stuck Area" )] public float TeleportAfter { get; set; } = 20f;

	/// <summary>Random navmesh spots tried per teleport attempt (each one costs path queries).</summary>
	[Property, Group( "Stuck Area" ), Range( 1, 32 )] public int TeleportAttempts { get; set; } = 8;

	/// <summary>Height above the navmesh the car is dropped at when teleported.</summary>
	[Property, Group( "Stuck Area" )] public float TeleportDropHeight { get; set; } = 20f;

	/// <summary>
	/// Teleport right away (instead of after <see cref="TeleportAfter"/>) once the car has been on
	/// its side/roof, or with no navmesh within 400 units (fell off the map), for this many seconds.
	/// 0 disables.
	/// </summary>
	[Property, Group( "Stuck Area" )] public float LostRecoverTime { get; set; } = 3f;

	// ------------------------------------------------------------------ Aggression

	/// <summary>Aggression when not stuck (0-1).</summary>
	[Property, Group( "Aggression" ), Range( 0f, 1f )] public float BaseAggression { get; set; } = 0f;

	/// <summary>Seconds inside the stuck area before aggression starts rising.</summary>
	[Property, Group( "Aggression" )] public float AggressionDelay { get; set; } = 4f;

	/// <summary>Seconds for aggression to rise from <see cref="BaseAggression"/> to 1 once it starts.</summary>
	[Property, Group( "Aggression" )] public float AggressionRampTime { get; set; } = 8f;

	/// <summary>Seconds for aggression to fall from 1 back to <see cref="BaseAggression"/> once the car gets moving.</summary>
	[Property, Group( "Aggression" )] public float AggressionDecayTime { get; set; } = 4f;

	/// <summary>
	/// At full aggression: multiplier on <see cref="StopDistance"/>, <see cref="BlockedWaitTime"/>
	/// and <see cref="StuckTime"/> (lower = waits less, stops closer, backs up sooner).
	/// </summary>
	[Property, Group( "Aggression" ), Range( 0.1f, 1f )] public float AggressiveCautionScale { get; set; } = 0.35f;

	/// <summary>
	/// At full aggression: multiplier on <see cref="CreepSpeed"/> and throttle response (higher =
	/// pushes in harder). Reverse throttle rises to full. Above 1 the creep speed outruns the
	/// shortened stop distance, so an aggressive car nudges what it stops for; that is intended.
	/// </summary>
	[Property, Group( "Aggression" ), Range( 1f, 5f )] public float AggressiveDriveScale { get; set; } = 2.5f;

	/// <summary>
	/// At or above this aggression dynamic physics bodies (other cars, players, loose props) no
	/// longer stop the car: it shoves into them at the aggressive creep speed instead of stopping and
	/// waiting. World geometry, including anything behind the shoved body, is still avoided. Above 1 = never.
	/// </summary>
	[Property, Group( "Aggression" ), Range( 0f, 1.01f )] public float PushThroughAggression { get; set; } = 0.75f;

	/// <summary>Current aggression (0-1). Rises while the car stays inside the stuck area.</summary>
	[Property, Group( "Aggression" ), ReadOnly] public float Aggression { get; private set; }

	// ------------------------------------------------------------------ Debug / status

	/// <summary>Draw whiskers, path and destination in the world while playing.</summary>
	[Property, Group( "Debug" )] public bool DrawDebug { get; set; } = false;

	[Property, Group( "Debug" ), ReadOnly] public DriveState State { get; private set; } = DriveState.Idle;

	[Property, Group( "Debug" ), ReadOnly] public Vector3 Destination { get; private set; }

	// ------------------------------------------------------------------ Internals

	private struct Whisker
	{
		public Vector3 LocalOrigin;
		public Vector3 LocalDirection;
		public float LengthScale;
		/// <summary>+1 left side, -1 right side, 0 center.</summary>
		public float Side;
		/// <summary>True for the straight-ahead rays used for stopping/braking.</summary>
		public bool IsForward;
	}

	private VehicleController _vehicle;
	private Rigidbody _body;
	private readonly Random _random = new();

	private readonly List<Vector3> _path = new();
	private int _waypointIndex;
	// Corner-aim trace cache (v12, see CornerAimTraceClearHelper): the clear/blocked decision
	// for one corner index, reused for 0.2 s. Hotload-safe: the default index 0 never matches a
	// real corner index (those are >= 1), so the first tick after a hotload recomputes.
	private int _cornerAimCacheIndex;
	private int _cornerAimCacheWaypoint;
	private bool _cornerAimCacheClear;
	private TimeSince _cornerAimCache = 10f;

	private Whisker[] _frontWhiskers;
	private Whisker[] _rearWhiskers;
	// Last results, kept for debug drawing in OnUpdate. Distance == length means no hit.
	private float[] _frontHitDistance;
	private float[] _frontLength;
	private float[] _rearHitDistance;
	// Nearest dynamic body straight ahead while pushing through; MaxValue otherwise.
	private float _dynamicAhead = float.MaxValue;
	private Vector3 _hullCenter;
	private Vector3 _hullSize;

	// v16: last front-flank gaps from ProbeFlanksHelper (samples.csv lflank/rflank columns).
	private float _flankLeftGap;
	private float _flankRightGap;

	// Hidden, never-enabled agents that only carry area filters into NavMesh.CalculatePath.
	private GameObject _filterObject;
	private NavMeshAgent _destinationFilter;
	private NavMeshAgent _routeFilter;

	private float _steer;
	private TimeSince _sinceStuckCheckOk;
	private TimeSince _sinceTilted;
	private TimeSince _sinceStateChange;
	private TimeSince _sinceRepath = 10f;
	private TimeUntil _untilRetry;
	private TimeUntil _untilTeleportRetry;
	private TimeSince _sinceEngineRequest = 10f;
	private Vector3 _stuckAnchor;
	private TimeSince _sinceStuckAnchor;
	private int _reverseCount;
	private float _reverseSteer;
	private string _reverseReason = "";
	private float _blockedWaitFactor = 1f;
	private Vector3 _blockedHeading;
	// Pivot livelock escape (v5): failed turn-reverses counted at one spot (see TickDrivingHelper).
	// Hotload-safe defaults: _pivotFails 0 keeps the old reverse-first behaviour, and a zero
	// _pivotAnchor just re-anchors on the first failure.
	private int _pivotFails;
	private Vector3 _pivotAnchor;
	// Progress-aware reverse counting (v11b): _progressMark is the car position at the last counted
	// reverse (or the fresh-spot reset to 1). Hotload-safe: _progressMarkSet false means no mark
	// yet, so the first reverse always counts.
	private Vector3 _progressMark;
	private bool _progressMarkSet;
	// Same-spot repick chain (v11c): repicks stacked within StuckAreaRadius of _repickAnchor
	// escalate to a teleport. Hotload-safe: _repickChain 0 means no chain, and a zero
	// _repickAnchor just re-anchors on the first repick.
	private Vector3 _repickAnchor;
	private int _repickChain;
	// v11c review fix: chains die of old age too. TimeSince reads 0 ("just now") on a hotloaded
	// instance, which only ever keeps an existing chain for at most 90 more seconds.
	private TimeSince _sinceRepickChain;
	// Blocked-state clear timer (v7). Hotload default reads as "long ago", so a car already Blocked
	// at hotload may resume once immediately; it is reset on every Blocked entry after that.
	private TimeSince _sinceBlockedObstacle;
	private bool _warnedNoNavStart;
	// Lost recovery (flipped / far off the navmesh). A hotload that adds these mid-play leaves them
	// at default(T); harmless, because UpdateStuckAreaHelper runs the nav check (default
	// _sinceLostCheck is huge) and resets _sinceNotLost before the lost-teleport test reads it.
	private TimeSince _sinceNotLost;
	private TimeSince _sinceLostCheck;
	private bool _lastNearNav = true;
	// Distance to the first missing floor ahead (LedgeProbeHelper). 0 = not probed yet (hotload default), ignored.
	private float _ledgeAhead;
	// Distance to the first missing floor behind (LedgeProbeHelper). Same 0 = not probed convention.
	private float _ledgeBehind;

	// Last control decisions, kept for telemetry.
	private float _lastThrottle;
	private bool _lastBrake;
	private float _lastTargetSpeed;
	private float _lastPathAngle;
	private float _lastAvoidAngle;
	// v12: true while GetPathAngleHelper aimed at the next real corner instead of the
	// pure-pursuit point; logged in the samples `aim` column for A/B analysis.
	private bool _aimingAtCorner;
	private float _lastDistanceFromPath;
	// Name of the object the last CastWhiskerHelper call hit ("" on a miss), and per front whisker.
	private string _lastCastHitName = "";
	private string[] _frontHitName;
	// v17 corridor check: min free width of the route last committed (-1 = unchecked), where it was
	// measured, and how many candidates this pick's corridor check rejected (LegStart detail).
	private float _legMinWidth = -1f;
	private Vector3 _legMinWidthAt;
	private int _legRoutesRejected;
	// v17b review F4: the worst (narrowest) corridor-rejected candidate of the last pick, for the
	// LegStart detail's rej_minw / rej_minw_at (-1 / 0,0 = nothing was rejected this pick).
	private float _rejMinWidth = -1f;
	private Vector3 _rejMinWidthAt;
	// v17 scratch buffers for MeasureCorridorHelper (instance fields: hotload rule, no statics).
	private readonly List<Vector3> _corridorScratch = new();
	private readonly List<float> _corridorCumLen = new();
	// v18: how many candidates this pick's no-go zone check rejected (LegStart detail).
	private int _legNogoRejected;

	// v18b shared learned no-go zones (see RecordNoGoZoneHelper). Deliberately STATIC so the 3
	// cars in the session learn one shared map of trap spots. Hotload note: the engine COPIES
	// static fields across hotloads (Sandbox.Hotload/UpdateReferences.cs), so the store SURVIVES
	// a hotload instead of resetting - that is why it is keyed to the scene it belongs to:
	// _nogoZonesSceneId records the Id of the scene that filled it, and every entry point clears
	// the store when the car's current Scene differs (a new play session, or the human loading a
	// different map). The key is Scene.Id, a stable per-instance Guid (GameObject.Id, assigned in
	// the GameObject ctor) - a value, so no strong static reference keeps an old scene alive.
	// The list is still null-check guarded because the first play session starts with null.
	private struct NoGoZone
	{
		public Vector3 Position;
		public int Hits;
		public float LastCountedHit; // Time.Now of the last hit that counted (episode gap)
	}
	private static List<NoGoZone> _nogoZones;
	private static Guid _nogoZonesSceneId;

	protected override void OnStart()
	{
		_vehicle = GetComponent<VehicleController>();
		_body = GetComponent<Rigidbody>();

		if ( !_vehicle.IsValid() || !_body.IsValid() )
		{
			Log.Error( $"AICarDriver on '{GameObject.Name}': needs a VehicleController and a Rigidbody on the same GameObject. Disabling." );
			Enabled = false;
			return;
		}

		BuildWhiskersHelper();

		// Proxies never simulate this car; the prefab's serialized UseExternalInput keeps their
		// VehicleController off the local keyboard.
		if ( IsProxy ) return;

		// First tuning pass: applies code/file tuning over the inspector values and latches the
		// configuration the first telemetry epoch is keyed to. Must run before TelemetryStartHelper.
		TuningTickHelper();

		_vehicle.UseExternalInput = true;
		_stuckAnchor = WorldPosition;
		_sinceStuckAnchor = 0f;
		Aggression = float.Clamp( BaseAggression, 0f, 1f );
		SetStateHelper( DriveState.Idle );

		TelemetryStartHelper();
		SpawnTestCarsHelper();
	}

	protected override void OnDisabled()
	{
		if ( _vehicle.IsValid() )
		{
			_vehicle.ExternalThrottle = 0f;
			_vehicle.ExternalSteer = 0f;
			_vehicle.ExternalBrake = true;
		}
	}

	protected override void OnDestroy()
	{
		TelemetryFlushHelper( true );

		if ( _filterObject.IsValid() )
			_filterObject.Destroy();
	}

	protected override void OnFixedUpdate()
	{
		// Host (or solo) only: scene-placed cars are unowned, so the host simulates them.
		if ( IsProxy ) return;
		if ( !_vehicle.IsValid() || !_body.IsValid() ) return;

		// Live loop: a tuning file or hotloaded DriverVersion change mid-play re-applies tuning and
		// opens a new telemetry epoch. No duplicate epoch on the first tick after OnStart: the
		// epoch already carries this exact key, so TelemetryNewEpochHelper no-ops.
		if ( TuningTickHelper() ) TelemetryNewEpochHelper();

		EnsureEngineRunningHelper();

		// Extra test cars start stacked on the original: move them to their own spot first.
		if ( _teleportPending )
		{
			if ( _untilTeleportRetry )
			{
				_untilTeleportRetry = 0.25f;
				if ( TryTeleportHelper( "spawn" ) ) _teleportPending = false;
			}
			ApplyControlsHelper( 0f, 0f, true );
			return;
		}

		Vector3 position = WorldPosition;

		// A sleeping body never wakes on its own here: v7 telemetry had a car parked mid-air at
		// teleport drop height for 500+ s with full throttle (wheels not grounded, so the vehicle
		// applies no force, so nothing wakes the body). Keep the driven car awake.
		if ( _body.Sleeping ) _body.Sleeping = false;

		UpdateStuckAreaHelper( position );

		if ( TeleportAfter > 0f && _sinceStuckAnchor > TeleportAfter && _untilTeleportRetry )
		{
			_untilTeleportRetry = MathF.Max( RetryDelay, 0.5f );
			if ( TryTeleportHelper( "stuck" ) ) return;
		}

		// Flipped, or fell off the map (v1: drove off a bridge edge and dropped thousands of units,
		// then sat there for the full TeleportAfter): recover after LostRecoverTime instead.
		if ( LostRecoverTime > 0f && _sinceNotLost > LostRecoverTime && _untilTeleportRetry )
		{
			_untilTeleportRetry = MathF.Max( RetryDelay, 0.5f );
			if ( TryTeleportHelper( "lost" ) ) return;
		}

		float forwardSpeed = Vector3.Dot( _body.Velocity, WorldRotation.Forward );

		ProbeFrontHelper( forwardSpeed );

		switch ( State )
		{
			case DriveState.Idle:
				TickIdleHelper( position );
				break;
			case DriveState.Driving:
				TickDrivingHelper( position, forwardSpeed );
				break;
			case DriveState.Blocked:
				TickBlockedHelper( position );
				break;
			case DriveState.Reversing:
				TickReversingHelper( position, forwardSpeed );
				break;
		}

		TelemetryTickHelper( position, forwardSpeed );
	}

	protected override void OnUpdate()
	{
		if ( !DrawDebug || IsProxy ) return;
		DrawDebugHelper();
	}

	// ================================================================== States

	private void TickIdleHelper( Vector3 position )
	{
		ApplyControlsHelper( 0f, 0f, true );

		if ( !_untilRetry ) return;
		_untilRetry = RetryDelay;

		if ( TryPickDestinationHelper( position ) )
		{
			_reverseCount = 0;
			_pivotFails = 0; // v11b: a new route starts with a clean pivot count
			_repickChain = 0; // v11c review fix: a fresh destination also clears the repick chain
			SetStateHelper( DriveState.Driving );
		}
	}

	private void TickDrivingHelper( Vector3 position, float forwardSpeed )
	{
		if ( _path.Count < 2 )
		{
			SetStateHelper( DriveState.Idle );
			return;
		}

		// Arrived? Prefer a next route that doesn't start with a U-turn.
		if ( FlatDistance( position, _path[^1] ) < ArrivalDistance )
		{
			TelemetryLegEndHelper( "arrived" );
			_repickChain = 0; // v11c: a finished leg ends any same-spot repick chain
			PickNewDestinationOrIdleHelper( position, -FlatForwardHelper() );
			return;
		}

		AdvanceWaypointHelper( position, out float distanceFromPath );
		_lastDistanceFromPath = distanceFromPath;

		// Off the path: re-path, at most twice a second (each try is a navmesh query, and a
		// failure falls through to a full destination pick).
		if ( distanceFromPath > OffPathDistance && _sinceRepath > 0.5f )
		{
			_sinceRepath = 0f;
			TelemetryEventHelper( "OffPath", $"dev={F( distanceFromPath, 0 )}" );
			if ( !TryRepathHelper( position ) )
			{
				TelemetryLegEndHelper( "repath-failed" );
				PickNewDestinationOrIdleHelper( position );
				return;
			}
		}

		// --- Path steering: aim at a look-ahead point along the path.
		// v12b (F1): two angles. pathAngle (corner aim allowed) steers and sets the heading-error
		// speed limit; pathAnglePp (pure pursuit) drives every decision about where the path
		// actually goes (path-behind, pivot side, reverse side choices) - aiming at a far corner
		// can hide a bend that is now beside or behind the car.
		float pathAnglePp = GetPathAngleHelper( position, forwardSpeed, false );
		float pathAngle = GetPathAngleHelper( position, forwardSpeed, true );

		// --- Whisker clearance. Probed before the path-behind branch below so the pivot escape
		// can read forwardClear; same values as before (the whiskers were cast earlier this tick).
		GetFrontClearanceHelper( out float forwardClear, out float forwardLength, out float leftClear, out float rightClear );

		// --- Path is behind the car (new leg pointing back, overshot corner): a forward full-lock
		// turn needs 250-500 units of radius and drags the car through whatever is beside the road.
		// Stop and back around instead (three-point turn); TickReversingHelper ends it once the
		// nose points along the path. Only from (near) standstill: reversing out of speed swings
		// the car (v2: two cars backed off a bridge doing this at 680 u/s); faster, it brakes first.
		bool pathBehind = UTurnAngle > 0f && MathF.Abs( pathAnglePp ) > UTurnAngle;
		// Forward pivot stays on at any speed once chosen; otherwise creeping past StuckSpeed*2 would
		// drop out of it into the straight-line brake (steer 0) and the pivot would stall.
		bool pivotForward = pathBehind && _pivotFails < 4 && _pivotFails % 2 == 1 && forwardClear > EffectiveStopDistance * 2f;
		if ( pathBehind && !pivotForward && MathF.Abs( forwardSpeed ) < StuckSpeed * 2f )
		{
			// v5 pivot livelock escape: a turn-reverse with the rear against a wall ends after
			// MinReverseTime still pointing away from the path, Driving sees pathBehind again and
			// reverses forever. _pivotFails counts failed turn-reverses at this spot: even (0, 2)
			// does today's turn-reverse, odd (1, 3) pivots forward instead when the front is
			// clear, and 4 gives up and teleports.
			if ( _pivotFails >= 4 )
			{
				if ( _untilTeleportRetry )
				{
					_untilTeleportRetry = MathF.Max( RetryDelay, 0.5f );
					if ( TryTeleportHelper( "pivot" ) ) return;
				}
				// No teleport spot yet: fall through, the path-behind brake below holds the car.
			}
			else
			{
				EnterTurnReverseHelper( pathAnglePp, leftClear, rightClear );
				return;
			}
		}

		// --- Whisker avoidance. v13: the free-side tie-break is a "where is the path" question,
		// so it reads the pure-pursuit angle, not the corner aim.
		float avoidAngle = GetAvoidAngleHelper( pathAnglePp, leftClear, rightClear );
		_lastPathAngle = pathAngle;
		_lastAvoidAngle = avoidAngle;

		// --- Blocked right in front: stop, wait for it to clear, then back up (TickBlockedHelper).
		float stopDistance = EffectiveStopDistance;
		if ( forwardClear < stopDistance )
		{
			EnterBlockedHelper();
			return;
		}

		// --- Target speed: cruise, corners (with braking distance), heading error, final approach, obstacles.
		float targetSpeed = CruiseSpeed;

		// Heading error now: the v10 curve gives full corner speed at CornerFullAngle degrees of error.
		targetSpeed = MathF.Min( targetSpeed, CornerSpeedForAngleHelper( pathAngle ) );

		// Upcoming corners and the destination: the fastest speed from which the car can still brake
		// down to each one's corner speed by the time it gets there.
		targetSpeed = MathF.Min( targetSpeed, GetCornerSpeedLimitHelper( position ) );

		// Obstacle ahead: a speed the car can still shed before StopDistance at ObstacleBrakingRate,
		// floored at CreepSpeed so it reaches StopDistance instead of stalling short of it.
		if ( forwardClear < forwardLength )
		{
			float stoppable = (forwardClear - stopDistance) * MathF.Max( ObstacleBrakingRate, 0.01f );
			targetSpeed = MathF.Min( targetSpeed, MathF.Max( EffectiveCreepSpeed, stoppable ) );
		}

		// Pushing through a dynamic body (see PushThroughAggression): shove at creep speed, never ram.
		if ( _dynamicAhead < forwardLength )
			targetSpeed = MathF.Min( targetSpeed, EffectiveCreepSpeed );

		// v9: near a drop (quay edge, bridge), the ledge alone within 3 stop distances keeps the car
		// at creep speed even when no whisker sees an obstacle (telemetry e005: cars ran over low
		// water-edge rocks straight into the sea).
		if ( _ledgeAhead > 0f && _ledgeAhead < EffectiveStopDistance * 3f )
			targetSpeed = MathF.Min( targetSpeed, EffectiveCreepSpeed );

		// Path behind and not pivoting: brake to a stop in a straight line, then back around.
		// A forward pivot (v5) crawls around the blockage at creep speed instead.
		if ( pathBehind )
			targetSpeed = pivotForward ? EffectiveCreepSpeed : 0f;

		_lastTargetSpeed = targetSpeed;

		float throttle;
		bool brake = false;
		// v14: reverse-to-drive lurch under the retune (torque 90000). A car leaving Reversing is
		// still rolling backward (telemetry: fwd -256 at the handover); the formula below reads
		// (target - forwardSpeed) / ThrottleResponse as full throttle while rolling in reverse.
		// Mirror of the forward-roll brake at the top of TickReversingHelper: stop the backward
		// roll first, only then drive. MathF.Max( StuckSpeed, 1f ) keeps the deadband even if a
		// hotloaded instance reads StuckSpeed as 0.
		if ( forwardSpeed < -MathF.Max( StuckSpeed, 1f ) )
		{
			throttle = 0f;
			brake = true;
		}
		else if ( forwardSpeed > targetSpeed + BrakeMargin )
		{
			throttle = 0f;
			brake = true;
		}
		else
		{
			throttle = float.Clamp( (targetSpeed - forwardSpeed) / EffectiveThrottleResponse, 0f, 1f );
		}

		// v15 tilt guard: under torque 90000 a 0.17-0.35 throttle at full steer lock climbs
		// ~100u-tall obstacles and tips the car (telemetry 20261007-024815: 4 of 5 flips at one
		// spot, z 224 -> 333 in 1.5 s with fwd ~0). WorldRotation.Up.z is the same reading the
		// telemetry upz column writes; legit road ramps keep it well above 0.9. Cut the throttle
		// (no brake: rolling back down the obstacle is the point). Throttle 0 keeps tryingToMove
		// false in the stuck detector below, so the two never double-fire while the guard holds.
		// Driving only: Reversing/Blocked/Idle already cut or reverse the throttle.
		bool tiltGuardActive = false;
		if ( EffectiveTiltGuardUpZ > 0f )
		{
			if ( WorldRotation.Up.z < EffectiveTiltGuardUpZ )
			{
				throttle = 0f;
				// v15 review fix: keep v14's backward-roll brake. The library brake is velocity damping
				// (zero force at rest), so it caps the roll back down the obstacle without pinning the
				// car on it; dropping it let a tilted car coast backward off the bottom unguarded.
				if ( forwardSpeed >= -MathF.Max( StuckSpeed, 1f ) )
					brake = false;
				tiltGuardActive = _sinceTilted > 0.5f;
			}
			else
			{
				_sinceTilted = 0f;
			}
		}

		float steerTarget;
		bool normalSteerBranch = !pivotForward && !pathBehind;
		if ( pivotForward )
			steerTarget = pathAnglePp > 0f ? 1f : -1f; // full lock toward the path side (ExternalSteer + = left)
		else if ( pathBehind )
			steerTarget = 0f;
		else
			steerTarget = SteerFromAngleHelper( DampedSteerAngleHelper( pathAngle, forwardSpeed ) + avoidAngle );

		// v16 front-flank steering cap: the inside front flank scrapes walls mid-turn (79/83 side
		// contacts while turning are on the inside of the turn; the flank sits in the blind spot of
		// the forward whisker fan). When a flank probe is close, scale down steering TOWARD that
		// side only (steer + = left), reaching 0 at gap 0. Never adds steering away: avoidance
		// already does that. Normal forward branch only, not pivotForward or pathBehind.
		if ( normalSteerBranch )
		{
			ProbeFlanksHelper( out float flankLeftGap, out float flankRightGap );
			float flankMargin = EffectiveFlankMargin;
			if ( flankMargin > 0f )
			{
				if ( steerTarget > 0f && flankLeftGap < flankMargin )
					steerTarget *= flankLeftGap / flankMargin;
				else if ( steerTarget < 0f && flankRightGap < flankMargin )
					steerTarget *= flankRightGap / flankMargin;
			}
		}
		else
		{
			// Not probed this tick (pivot or path-behind): report "no flank info", not a stale gap.
			_flankLeftGap = _flankRightGap = MathF.Max( EffectiveFlankProbeLength, 0f );
		}

		ApplyControlsHelper( throttle, steerTarget, brake );

		// Still tilted after 0.5 s of cut throttle (the roll-down did not straighten it and it
		// is not about to fall off by itself): back off. Triggered after ApplyControlsHelper so
		// the zeroed throttle lands this same tick, mirroring how the stuck detector below
		// enters reverse only after the controls are applied.
		if ( tiltGuardActive )
		{
			_tiltTrigger = true;
			EnterReverseHelper( pathAnglePp, leftClear, rightClear );
			return;
		}

		// --- Stuck: throttling but not moving (pinned against something the whiskers miss).
		// v17b: 0.2 -> 0.05. A wedged car can command ~0.09 throttle and sit inert for seconds
		// (v17 forensics: ~5 s before the creep into Blocked even fired). The detector's job is to
		// catch throttling-but-not-moving, and any commanded throttle means the car is trying.
		bool tryingToMove = throttle > 0.05f && _vehicle.IsEngineOn;
		if ( !tryingToMove || MathF.Abs( forwardSpeed ) > StuckSpeed )
			_sinceStuckCheckOk = 0f;
		else if ( _sinceStuckCheckOk > EffectiveStuckTime )
		{
			_stuckTrigger = true;
			EnterReverseHelper( pathAnglePp, leftClear, rightClear );
		}
	}

	/// <summary>
	/// Stopped with something within <see cref="StopDistance"/> in front. Resumes driving as soon as
	/// it clears (with hysteresis, so a car following slow traffic holds instead of backing up);
	/// backs up only after waiting <see cref="BlockedWaitTime"/>.
	/// </summary>
	private void TickBlockedHelper( Vector3 position )
	{
		ApplyControlsHelper( 0f, 0f, true );

		GetFrontClearanceHelper( out float forwardClear, out _, out float leftClear, out float rightClear );

		// Resume only once the front has stayed clear for a moment. A car pinned with a bumper corner
		// against a tree trunk reads hit / miss on alternate ticks (the thick whisker starts inside the
		// trunk), and resuming on the first clear tick restarted the wait every time: v6 had one car
		// flip Blocked <-> Driving 126 times at one tree without ever backing up.
		if ( forwardClear <= EffectiveStopDistance * 1.5f )
			_sinceBlockedObstacle = 0f;
		else if ( _sinceBlockedObstacle > 0.3f )
		{
			SetStateHelper( _path.Count >= 2 ? DriveState.Driving : DriveState.Idle );
			return;
		}

		if ( _sinceStateChange < MathF.Max( BlockedWaitTime, 0f ) * _blockedWaitFactor * CautionScale ) return;

		// v12b (F1): reverse side choice uses the pure-pursuit angle, never the corner-aim one.
		float pathAngle = _path.Count >= 2 ? GetPathAngleHelper( position, 0f, false ) : 0f;
		EnterReverseHelper( pathAngle, leftClear, rightClear );
	}

	private void EnterBlockedHelper()
	{
		_blockedWaitFactor = _random.Float( 0.75f, 1.25f );
		_sinceBlockedObstacle = 0f;
		SetStateHelper( DriveState.Blocked );
	}

	/// <summary>
	/// Progress-aware reverse counting (v11b): a reverse only adds to <see cref="_reverseCount"/>
	/// when the car has made no real progress since the last counted reverse. One entered at least
	/// <see cref="EffectiveReverseProgressDistance"/> from the last mark is the first reverse at a
	/// fresh spot and restarts the count at 1. <paramref name="position"/> is where the reverse began.
	/// </summary>
	private void CountReverseHelper( Vector3 position )
	{
		if ( !_progressMarkSet || FlatDistance( position, _progressMark ) < EffectiveReverseProgressDistance )
			_reverseCount++;
		else
			_reverseCount = 1; // fresh spot: this reverse is the first one here

		_progressMark = position;
		_progressMarkSet = true;
	}

	private void TickReversingHelper( Vector3 position, float forwardSpeed )
	{
		// Still rolling forward: brake to a stop before backing up. The reverse timers start once
		// the car has stopped. The library brakes along each steered wheel's axis with a force far
		// above its sideways grip (VehicleController.Brakes.cs ApplyBrake vs ApplyAntiSlip), so while
		// braking the nose swings AWAY from the lock, same as in reverse: keep the reverse lock.
		if ( forwardSpeed > StuckSpeed )
		{
			_sinceStateChange = 0f;
			ApplyControlsHelper( 0f, _reverseSteer, true );
			return;
		}

		float rearClear = ProbeRearHelper();
		// v9: the rear ledge must stop a fast reverse in time, not just at the fixed RearStopDistance
		// (telemetry e005: car0 reversed off a quay at 184 u/s with the whiskers clear). A drop closer
		// than RearStopDistance plus half a second of stopping counts as blocked, and brakes hard.
		// v14: obstacles scale with reverse speed the same way as the ledge now (post-retune torque
		// 90000 reaches MaxReverseSpeed almost instantly; ~10 of 25 hard collisions backed into poles
		// and stairs at -245..-274 u/s). RearProbeLength (250) caps what can be seen: at
		// MaxReverseSpeed 250 the threshold is RearStopDistance 80 + 125 = 205 < 250, so the scaled
		// stop still fits inside the probe.
		float reverseSpeed = MathF.Max( -forwardSpeed, 0f );
		bool ledgeBehindBlocked = _ledgeBehind > 0f && _ledgeBehind < RearStopDistance + reverseSpeed * 0.5f;
		bool rearBlocked = rearClear < RearStopDistance + reverseSpeed * 0.5f || ledgeBehindBlocked;

		GetFrontClearanceHelper( out float forwardClear, out _, out _, out _ );
		bool frontClear = forwardClear > EffectiveStopDistance * 2f;

		// Done once the nose points roughly along the path again (a reverse that ends while still
		// facing away just drives back into the same spot), or the rear is blocked, or time is up.
		// v12b (F1): while reversing _waypointIndex is frozen and a corner may sit behind or beside
		// the car; the exit check must read the polyline, not the corner aim.
		float pathAngleNow = _path.Count >= 2 ? GetPathAngleHelper( position, 0f, false ) : 0f;
		bool aligned = MathF.Abs( pathAngleNow ) < ReverseAlignedAngle;
		bool turning = _reverseReason == "turn";

		bool done = _sinceStateChange > (turning ? MaxReverseTime * 2f : MaxReverseTime)
			|| (_sinceStateChange > MinReverseTime && (rearBlocked || (aligned && (frontClear || turning))));

		if ( done )
		{
			_sinceStuckCheckOk = 0f;

			// Pivot livelock accounting (v5): a turn-reverse that ends still facing away from the
			// path is a failed pivot at this spot. Count it while the car stays in the stuck area;
			// a new spot restarts the count. An aligned end means the turn worked: clear it.
			if ( turning )
			{
				if ( aligned )
				{
					_pivotFails = 0;
				}
				else if ( FlatDistance( position, _pivotAnchor ) <= MathF.Max( StuckAreaRadius, 1f ) )
				{
					_pivotFails++;
				}
				else
				{
					_pivotFails = 1;
					_pivotAnchor = position;
				}
			}

			if ( RepickAfterReverses > 0 && _reverseCount >= RepickAfterReverses )
			{
				// Same-spot repick chain (v11c): a repick whose replacement route starts into the
				// same blocked spot repeats the whole reverse-then-repick loop, each pass costing
				// up to TeleportAfter seconds before the stuck teleport. Chain repicks made inside
				// the stuck area and escalate a long chain to an immediate teleport.
				// v11c review fix: a chain older than 90 s is stale - the car has clearly not kept
				// looping on this spot - and breaks like a moved anchor does (chain = 1, re-anchor).
				if ( _repickChain > 0 && _sinceRepickChain < 90f &&
					FlatDistance( position, _repickAnchor ) <= MathF.Max( StuckAreaRadius, 1f ) )
				{
					_repickChain++;
				}
				else
				{
					_repickChain = 1;
					_repickAnchor = position;
				}
				_sinceRepickChain = 0f; // any increment or re-anchor restarts the chain's clock

				if ( _repickChain >= EffectiveRepickChainTeleport )
				{
					if ( _untilTeleportRetry )
					{
						_untilTeleportRetry = MathF.Max( RetryDelay, 0.5f );
						// TelemetryTeleportHelper inside logs the LegEnd as "teleport", like the
						// pivot path above does, and counts the teleport.
						if ( TryTeleportHelper( "repick-loop" ) ) return;
					}
					// No teleport spot yet: fall through to the normal repick below.
				}

				// Same route keeps getting blocked: pick one that starts away from the blocked heading.
				TelemetryLegEndHelper( "repick" );
				PickNewDestinationOrIdleHelper( position, _blockedHeading );
				return;
			}

			SetStateHelper( _path.Count >= 2 ? DriveState.Driving : DriveState.Idle );
			return;
		}

		if ( rearBlocked )
		{
			ApplyControlsHelper( 0f, _reverseSteer, true );
			return;
		}

		// Cap reverse speed: v3 telemetry had cars backing up at 500-640 u/s under full aggression
		// throttle, swinging wide into whatever was behind or beside them. Coast above the cap.
		bool tooFast = -forwardSpeed > MaxReverseSpeed;
		ApplyControlsHelper( tooFast ? 0f : -EffectiveReverseThrottle, _reverseSteer, false );
	}

	private void EnterReverseHelper( float pathAngle, float leftClear, float rightClear )
	{
		// v17b straight-back wedge escape: full-lock reverse at ~0 speed rotates the car inside
		// its own V-wedge - the rear quarters hit the adjacent walls, the reverse reads rearBlocked
		// and ends after MinReverseTime having moved ~12u, never backing out (v17 forensics, the
		// "pinned" cluster at -876,3300). Backing straight instead uses the astern corridor that
		// already cleared the wedge; it re-presents the car with room to turn once backing clears
		// the StopDistance hysteresis. Gate mirrors the telemetry trigger string's "blocked"
		// (from the Blocked state, not the stuck/tilt latches, and the turn reverse never comes
		// through here), plus a near-zero entry speed and a blocked front. If the rear probe
		// says a straight back is impossible - the same rearBlocked threshold
		// TickReversingHelper applies at this reverse speed, so no new dead end is created -
		// fall back to the locked swing. v14/v15 rear-speed/ledge protections run untouched.
		float entryForwardSpeed = Vector3.Dot( _body.Velocity, WorldRotation.Forward );
		// v17c: straight back only on the FIRST reverse at a spot. Backing straight never changes
		// the heading, so a car whose approach line runs into the obstacle drives straight back
		// into it (v17b telemetry car0 t12588-12607: x 1397 <-> 1660 five times, stuck teleport).
		// The first straight back frees a wedge; a repeat at the same spot needs the locked swing
		// to rotate onto a new line. Same same-spot test CountReverseHelper applies right below,
		// evaluated before it counts this reverse.
		bool repeatAtSpot = _reverseCount >= 1 && _progressMarkSet
			&& FlatDistance( WorldPosition, _progressMark ) < EffectiveReverseProgressDistance;
		bool straightBackEscape = State == DriveState.Blocked && !_stuckTrigger && !_tiltTrigger
			&& !repeatAtSpot && MathF.Abs( entryForwardSpeed ) < StuckSpeed;
		if ( straightBackEscape )
		{
			GetFrontClearanceHelper( out float entryForwardClear, out _, out _, out _ );
			straightBackEscape = entryForwardClear < EffectiveStopDistance;
		}
		if ( straightBackEscape )
		{
			float entryRearStop = RearStopDistance + MathF.Max( -entryForwardSpeed, 0f ) * 0.5f;
			straightBackEscape = ProbeRearHelper() >= entryRearStop
				&& !( _ledgeBehind > 0f && _ledgeBehind < entryRearStop );
		}

		// Pick the side to swing the nose toward: away from the obstacle (the side with more room),
		// or the path side on a tie (obstacle dead ahead). Room must win even at a corner: a car
		// that clipped the INSIDE of a turn has to swing its nose away from that corner and take the
		// turn wider; swinging toward the turn points it back into the corner.
		float noseSide;
		if ( MathF.Abs( leftClear - rightClear ) > 1f )
		{
			noseSide = leftClear > rightClear ? 1f : -1f;
			_reverseReason = "room";
		}
		else
		{
			noseSide = pathAngle >= 0f ? 1f : -1f;
			_reverseReason = "tie";
		}

		// Front-wheel steering in reverse swings the nose AWAY from the steered side,
		// so steer opposite to where the nose should end up. The wedge escape steers 0
		// instead; "straight" keeps it identifiable in the telemetry's why= column
		// (trigger= stays "blocked", the tick logic only tests _reverseReason == "turn").
		_reverseSteer = straightBackEscape ? 0f : -noseSide;
		if ( straightBackEscape )
			_reverseReason = "straight";
		// Snap the wheels to the reverse lock. Smoothing from the driving lock would spend the start
		// of the reverse with the wheels on the wrong side.
		_steer = _reverseSteer;
		_blockedHeading = FlatForwardHelper();
		CountReverseHelper( WorldPosition );
		TelemetryReverseHelper( leftClear, rightClear );
		SetStateHelper( DriveState.Reversing );
	}

	/// <summary>
	/// Three-point-turn reverse: the path lies behind the car. Swing the nose toward the path side
	/// (front-wheel steering in reverse swings the nose away from the steered side, so steer away).
	/// After one failed pivot (_pivotFails == 2) try the other arc so the second reverse does not
	/// repeat the exact same blocked swing.
	/// </summary>
	private void EnterTurnReverseHelper( float pathAngle, float leftClear, float rightClear )
	{
		float noseSide = pathAngle >= 0f ? 1f : -1f;
		if ( _pivotFails == 2 )
			noseSide = -noseSide;
		_reverseReason = "turn";
		_reverseSteer = -noseSide;
		_steer = _reverseSteer;
		_blockedHeading = FlatForwardHelper();
		CountReverseHelper( WorldPosition );
		TelemetryReverseHelper( leftClear, rightClear );
		SetStateHelper( DriveState.Reversing );
	}

	/// <summary>
	/// Picks a new destination, or goes Idle (retrying every <see cref="RetryDelay"/>) if none is found.
	/// <paramref name="awayFrom"/>: prefer routes whose first leg points more than 60 degrees off this heading.
	/// </summary>
	private void PickNewDestinationOrIdleHelper( Vector3 position, Vector3? awayFrom = null )
	{
		_reverseCount = 0;
		_pivotFails = 0; // v11b: a new route starts with a clean pivot count
		if ( TryPickDestinationHelper( position, awayFrom ) )
		{
			SetStateHelper( DriveState.Driving );
			return;
		}

		_path.Clear();
		_untilRetry = RetryDelay;
		SetStateHelper( DriveState.Idle );
	}

	private void SetStateHelper( DriveState state )
	{
		TelemetryStateChangeHelper( State, state );
		State = state;
		_sinceStateChange = 0f;
		_sinceStuckCheckOk = 0f;
		_sinceTilted = 0f;
	}

	// ================================================================== Stuck area, aggression, teleport

	/// <summary>
	/// Re-anchors the stuck area whenever the car leaves it, and moves <see cref="Aggression"/>
	/// toward its target: base while moving, ramping to 1 after <see cref="AggressionDelay"/>
	/// inside the same area.
	/// </summary>
	private void UpdateStuckAreaHelper( Vector3 position )
	{
		// Lost tracking: upright and near the navmesh resets the timer. The navmesh query runs 4x a
		// second; between queries the last answer stands.
		if ( _sinceLostCheck > 0.25f )
		{
			_sinceLostCheck = 0f;
			var nav = Scene.NavMesh;
			_lastNearNav = nav is null || !nav.IsEnabled || nav.GetClosestPoint( position, 400f ).HasValue;
		}
		if ( WorldRotation.Up.z > 0.5f && _lastNearNav )
			_sinceNotLost = 0f;

		if ( FlatDistance( position, _stuckAnchor ) > MathF.Max( StuckAreaRadius, 1f ) )
		{
			_stuckAnchor = position;
			_sinceStuckAnchor = 0f;
		}

		// v11c review fix: a repick chain is meaningless once the car has driven out of the stuck
		// area; decay it here so a later repick near the old anchor cannot resume a stale chain.
		if ( _repickChain > 0 && FlatDistance( position, _repickAnchor ) > MathF.Max( StuckAreaRadius, 1f ) ) _repickChain = 0;

		float baseline = float.Clamp( BaseAggression, 0f, 1f );
		float stuckFor = _sinceStuckAnchor - AggressionDelay;
		float ramp = AggressionRampTime <= 0f
			? (stuckFor > 0f ? 1f : 0f)
			: float.Clamp( stuckFor / AggressionRampTime, 0f, 1f );
		float target = baseline + (1f - baseline) * ramp;

		if ( target >= Aggression )
			Aggression = target;
		else
			Aggression = MathF.Max( target, Aggression - Time.Delta / MathF.Max( AggressionDecayTime, 0.01f ) );
	}

	/// <summary>Multiplier on stop distance and waits: 1 when calm, <see cref="AggressiveCautionScale"/> at full aggression.</summary>
	private float CautionScale => Lerp( 1f, AggressiveCautionScale, Aggression );

	private float EffectiveStopDistance => StopDistance * CautionScale;

	private float EffectiveStuckTime => StuckTime * CautionScale;

	/// <summary>TiltGuardUpZ with the hotload fallback: 0 (default(T) on live instances) reads as 0.9; negative disables.</summary>
	private float EffectiveTiltGuardUpZ => TiltGuardUpZ == 0f ? 0.9f : TiltGuardUpZ;

	private float EffectiveCreepSpeed => CreepSpeed * Lerp( 1f, AggressiveDriveScale, Aggression );

	private float EffectiveThrottleResponse => MathF.Max( ThrottleResponse, 1f ) / Lerp( 1f, AggressiveDriveScale, Aggression );

	private float EffectiveReverseThrottle => Lerp( ReverseThrottle, 1f, Aggression );

	/// <summary>ReverseProgressDistance with the hotload fallback: 0 (default(T) on live instances) reads as 150.</summary>
	private float EffectiveReverseProgressDistance => ReverseProgressDistance > 0f ? ReverseProgressDistance : 150f;

	/// <summary>RepickChainTeleport with the hotload fallback: 0 (default(T) on live instances) reads as 2; negative disables.</summary>
	private int EffectiveRepickChainTeleport => RepickChainTeleport < 0 ? int.MaxValue : (RepickChainTeleport == 0 ? 2 : RepickChainTeleport);

	/// <summary>SteerDeadband with the hotload fallback: 0 or negative (default(T) on live instances) reads as 2; it cannot be disabled.</summary>
	private float EffectiveSteerDeadband => SteerDeadband > 0f ? SteerDeadband : 2f;

	/// <summary>SteerYawDamping with the hotload fallback: 0 (default(T) on live instances) reads as 0.3; negative disables.</summary>
	private float EffectiveSteerYawDamping => SteerYawDamping != 0f ? SteerYawDamping : 0.3f;

	/// <summary>FlankProbeLength with the hotload fallback: 0 (default(T) on live instances) reads as 60; negative disables the flank cap.</summary>
	private float EffectiveFlankProbeLength => FlankProbeLength == 0f ? 60f : FlankProbeLength;

	/// <summary>FlankMargin with the hotload fallback: 0 (default(T) on live instances) reads as 40; clamped to the effective probe length.</summary>
	private float EffectiveFlankMargin => MathF.Min( FlankMargin == 0f ? 40f : FlankMargin, MathF.Max( EffectiveFlankProbeLength, 0f ) );

	/// <summary>MinCorridorWidth with the hotload fallback: 0 (default(T) on live instances) reads as 200; negative disables the corridor check.</summary>
	private float EffectiveMinCorridorWidth => MinCorridorWidth == 0f ? 200f : MinCorridorWidth;

	/// <summary>CorridorCheckSkip with the hotload fallback: 0 (default(T) on live instances) reads as 200; negative reads as 0.</summary>
	private float EffectiveCorridorCheckSkip => CorridorCheckSkip == 0f ? 200f : MathF.Max( CorridorCheckSkip, 0f );

	/// <summary>CorridorSampleSpacing with the hotload fallback: 0 (default(T) on live instances) reads as 120; clamped to at least 10.</summary>
	private float EffectiveCorridorSampleSpacing => CorridorSampleSpacing == 0f ? 120f : MathF.Max( CorridorSampleSpacing, 10f );

	/// <summary>NoGoZoneRadius with the hotload fallback: 0 (default(T) on live instances) reads as 250; negative disables the whole no-go zone feature.</summary>
	private float EffectiveNoGoZoneRadius => NoGoZoneRadius == 0f ? 250f : NoGoZoneRadius;

	/// <summary>NoGoZoneMinHits with the hotload fallback: 0 (default(T) on live instances) reads as 2.</summary>
	private int EffectiveNoGoZoneMinHits => NoGoZoneMinHits == 0 ? 2 : Math.Max( NoGoZoneMinHits, 1 );

	/// <summary>NoGoEpisodeGap with the hotload fallback: 0 (default(T) on live instances) reads as 30; negative disables the gap (every hit counts).</summary>
	private float EffectiveNoGoEpisodeGap => NoGoEpisodeGap == 0f ? 30f : NoGoEpisodeGap;

	/// <summary>
	/// v18b: how close (flat) two stuck spots must be to merge into ONE zone. 1.5x
	/// <see cref="EffectiveNoGoZoneRadius"/>: decoupled from the reject radius so one physical
	/// trap (a stuck area of ~300 units) does not fragment into several 1-hit zones the reject
	/// radius then has to catch individually.
	/// </summary>
	private float EffectiveNoGoZoneMergeRadius => EffectiveNoGoZoneRadius * 1.5f;

	/// <summary>
	/// v18b: returns the shared store, clearing it first when it belongs to a different scene.
	/// The engine copies statics across hotloads (Sandbox.Hotload/UpdateReferences.cs), so a
	/// plain null check would keep stale zones alive after the human loads a new map. The scene
	/// key is Scene.Id - a stable per-instance Guid (GameObject.Id) - held as a value so no
	/// strong static Scene reference keeps an old scene alive. Null on first call: created here.
	/// </summary>
	private static List<NoGoZone> NoGoZonesForSceneHelper( Scene scene )
	{
		if ( _nogoZones is not null && _nogoZonesSceneId != scene.Id )
		{
			_nogoZones = null;
			_nogoZonesSceneId = default;
		}
		if ( _nogoZones is null )
		{
			_nogoZones = new List<NoGoZone>();
			_nogoZonesSceneId = scene.Id;
		}
		return _nogoZones;
	}

	private bool IsPushingThrough => Aggression >= PushThroughAggression;

	/// <summary>
	/// Moves the car to a random clear spot on the navmesh that has a usable route out, facing along
	/// that route. False if no spot was found this attempt (retried after <see cref="RetryDelay"/>).
	/// </summary>
	private bool TryTeleportHelper( string reason )
	{
		var nav = Scene.NavMesh;
		if ( nav is null || !nav.IsEnabled ) return false;

		Vector3 from = WorldPosition;
		int attempts = Math.Max( 1, TeleportAttempts );

		for ( int i = 0; i < attempts; i++ )
		{
			Vector3? point = nav.GetRandomPoint();
			if ( !point.HasValue ) return false; // navmesh not loaded

			if ( FlatDistance( point.Value, from ) < StuckAreaRadius * 2f ) continue;
			if ( !IsInDestinationAreaHelper( nav, point.Value ) ) continue;
			// v18: never drop a car (teleport OR spawn) back within NoGoZoneRadius of an active
			// learned trap zone. The route-from-target check below rejects no-go routes too -
			// TryFindDestinationHelper now runs the zone rejection on every caller.
			if ( IsInNoGoZoneHelper( point.Value ) ) continue;
			if ( !TryFindDestinationHelper( nav, point.Value, null, out NavMeshPath path, out Vector3 target ) ) continue;

			Vector3 heading = GetFirstLegHelper( path );
			if ( !IsSpawnClearHelper( point.Value, heading ) ) continue;

			// v18b: this trap claimed a car - merge the right anchor into the shared zone store
			// BEFORE moving the car. "repick-loop" records _repickAnchor (the chain's first repick
			// position, the actual blocked spot the car kept re-routing into); "stuck" records
			// _stuckAnchor (the spot the car has been looping around). Not the post-teleport point.
			// Only the recovery teleports that indicate a trap spot ("stuck" and "repick-loop");
			// spawn, lost/flip and pivot escapes are geometry-agnostic recoveries and teach
			// nothing. Recorded once per successful teleport, not per failed candidate attempt,
			// so one trap episode adds one hit.
			if ( reason == "repick-loop" )
				RecordNoGoZoneHelper( _repickAnchor );
			else if ( reason == "stuck" )
				RecordNoGoZoneHelper( _stuckAnchor );

			TelemetryTeleportHelper( reason, point.Value );

			WorldPosition = point.Value + Vector3.Up * TeleportDropHeight;
			WorldRotation = Rotation.LookAt( heading, Vector3.Up );
			_body.Velocity = Vector3.Zero;
			_body.AngularVelocity = Vector3.Zero;
			_body.Sleeping = false;
			Transform.ClearInterpolation();

			_steer = 0f;
			ApplyControlsHelper( 0f, 0f, false );
			_reverseCount = 0;
			_pivotFails = 0;
			_repickChain = 0; // v11c: a teleport is a fresh start, not part of a repick chain
			_stuckAnchor = WorldPosition;
			_sinceStuckAnchor = 0f;
			_sinceNotLost = 0f;
			Aggression = float.Clamp( BaseAggression, 0f, 1f );

			CommitPathHelper( path, target );
			SetStateHelper( DriveState.Driving );

			if ( reason != "spawn" )
				Log.Info( $"AICarDriver on '{GameObject.Name}': {reason} near {from}, teleported to {point.Value}." );
			return true;
		}

		return false;
	}

	/// <summary>True if the car's hull fits at <paramref name="point"/> facing <paramref name="heading"/> without touching anything.</summary>
	private bool IsSpawnClearHelper( Vector3 point, Vector3 heading )
	{
		float radius = _hullSize.y * 0.5f + 10f;
		float halfLength = MathF.Max( _hullSize.x * 0.5f - radius, 1f );
		Vector3 center = point + Vector3.Up * (TeleportDropHeight + radius);

		SceneTraceResult result = Scene.Trace.Sphere( radius, center - heading * halfLength, center + heading * halfLength )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		return !result.Hit && !result.StartedSolid;
	}

	// ================================================================== No-go zones (v18/v18b)

	/// <summary>
	/// v18b: merges a stuck position into the shared zone store BEFORE a teleport moves the car.
	/// Within <see cref="EffectiveNoGoZoneMergeRadius"/> (flat) an existing zone absorbs the hit:
	/// its count increments and its position moves to the running mean of the hits it absorbed.
	/// Past that radius a new zone is added with count 1. The store is capped at 64 zones: on
	/// overflow the lowest-count zone goes, oldest first among equals. When a zone's count reaches
	/// <see cref="EffectiveNoGoZoneMinHits"/> it becomes ACTIVE (starts rejecting routes) and a
	/// "NoGoZone" telemetry event is emitted. One trap EPISODE is one hit: a hit closer than
	/// <see cref="EffectiveNoGoEpisodeGap"/> seconds to the zone's last COUNTED hit is skipped and
	/// does not refresh the clock, so two cars re-sticking every ~20 s still accumulate.
	/// </summary>
	private void RecordNoGoZoneHelper( Vector3 stuckPosition )
	{
		float radius = EffectiveNoGoZoneRadius;
		if ( radius <= 0f ) return; // negative (or a disabled 0 after the fallback) = feature off

		var zones = NoGoZonesForSceneHelper( Scene );
		float mergeRadius = EffectiveNoGoZoneMergeRadius;
		float episodeGap = EffectiveNoGoEpisodeGap;
		float now = Time.Now;

		int merge = -1;
		float bestDist = float.MaxValue;
		for ( int i = 0; i < zones.Count; i++ )
		{
			float d = FlatDistance( zones[i].Position, stuckPosition );
			if ( d <= mergeRadius && d < bestDist )
			{
				bestDist = d;
				merge = i;
			}
		}

		bool wasActive;
		int hits;
		int idx; // zone this hit touched (-1 if the new zone was evicted on the spot)
		if ( merge >= 0 )
		{
			// Episode gap against the last COUNTED hit (v18b review F4): a skipped hit keeps the
			// old timestamp, so repeated same-episode hits cannot indefinitely defer the next one.
			if ( episodeGap > 0f && now - zones[merge].LastCountedHit < episodeGap )
				return;
			idx = merge;
			var merged = zones[merge];
			hits = merged.Hits + 1;
			merged.Hits = hits;
			merged.LastCountedHit = now;
			// Running mean over the hits this zone absorbed (its position already averages hits 1..n-1).
			merged.Position = merged.Position * ((hits - 1) / (float)hits) + stuckPosition * (1f / hits);
			zones[merge] = merged; // struct copy-back: List indexers return a copy, so assign through a local
			wasActive = hits - 1 >= EffectiveNoGoZoneMinHits; // active BEFORE this hit too?
		} else
		{
			hits = 1;
			wasActive = false;
			idx = zones.Count;
			zones.Add( new NoGoZone { Position = stuckPosition, Hits = hits, LastCountedHit = now } );

			if ( zones.Count > 64 )
			{
				// Drop the lowest-count zone; among equals the oldest (first added) goes first.
				int worst = 0;
				for ( int i = 1; i < zones.Count; i++ )
					if ( zones[i].Hits < zones[worst].Hits ) worst = i;
				zones.RemoveAt( worst );
				if ( worst == idx ) idx = -1; // the new zone was the least-worthy of all
				else if ( worst < idx ) idx--; // ...otherwise the survivor shifted down one slot
			}
		}

		if ( idx >= 0 && !wasActive && hits >= EffectiveNoGoZoneMinHits )
			TelemetryEventHelper( "NoGoZone", $"at={F( zones[idx].Position.x, 0 )},{F( zones[idx].Position.y, 0 )} hits={hits} radius={F( radius, 0 )}" );
	}

	/// <summary>
	/// v18: true if <paramref name="point"/> lies within <see cref="EffectiveNoGoZoneRadius"/> (flat)
	/// of any ACTIVE zone. Used on teleport target points so a car is not dropped back into a trap.
	/// </summary>
	private bool IsInNoGoZoneHelper( Vector3 point )
	{
		float radius = EffectiveNoGoZoneRadius;
		if ( radius <= 0f ) return false;

		var zones = NoGoZonesForSceneHelper( Scene );
		for ( int i = 0; i < zones.Count; i++ )
		{
			if ( zones[i].Hits < EffectiveNoGoZoneMinHits ) continue;
			Vector3 d = (zones[i].Position - point).WithZ( 0f );
			if ( d.LengthSquared <= radius * radius ) return true;
		}
		return false;
	}

	/// <summary>
	/// v18: true if any ACTIVE zone lies within <see cref="EffectiveNoGoZoneRadius"/> (flat) of the
	/// raw navmesh polyline, excluding its first <see cref="EffectiveCorridorCheckSkip"/> units of
	/// length - the stretch where the car already is, so a car parked next to a zone can still
	/// leave. Plain point-to-segment flat distance, no traces.
	/// </summary>
	private bool RouteHitsNoGoZoneHelper( IReadOnlyList<Vector3> path )
	{
		float radius = EffectiveNoGoZoneRadius;
		if ( radius <= 0f ) return false;
		if ( path.Count < 2 ) return false;

		var zones = NoGoZonesForSceneHelper( Scene );
		// Exclude the first CorridorCheckSkip units of route length: the stretch where the car
		// already is, so a car parked next to a zone can still route away from it.
		float skip = EffectiveCorridorCheckSkip;
		float travelled = 0f;
		for ( int i = 1; i < path.Count; i++ )
		{
			Vector3 a = path[i - 1], b = path[i];
			Vector3 ab = (b - a).WithZ( 0f );
			float segLen = ab.Length;
			if ( segLen < 0.001f ) continue;

			if ( travelled + segLen > skip )
			{
				// Clip the segment start to the skip line for the checked part.
				Vector3 checkedFrom = travelled >= skip ? a : a + ab * ((skip - travelled) / segLen);
				for ( int z = 0; z < zones.Count; z++ )
				{
					if ( zones[z].Hits < EffectiveNoGoZoneMinHits ) continue;
					if ( FlatDistanceToSegmentHelper( zones[z].Position, checkedFrom, b ) <= radius ) return true;
				}
			}
			travelled += segLen;
		}
		return false;
	}

	/// <summary>Flat point-to-segment distance (perp distance inside the span, endpoint distance outside).</summary>
	private static float FlatDistanceToSegmentHelper( Vector3 p, Vector3 a, Vector3 b )
	{
		Vector3 ab = (b - a).WithZ( 0f );
		float lengthSq = ab.LengthSquared;
		if ( lengthSq < 0.0001f )
			return (p - b).WithZ( 0f ).Length;

		float t = float.Clamp( Vector3.Dot( (p - a).WithZ( 0f ), ab ) / lengthSq, 0f, 1f );
		Vector3 closest = a + ab * t;
		return (p - closest).WithZ( 0f ).Length;
	}

	// ================================================================== Navmesh

	/// <summary>Finds a destination (see <see cref="TryFindDestinationHelper"/>) and makes it the current one.</summary>
	private bool TryPickDestinationHelper( Vector3 position, Vector3? awayFrom = null )
	{
		var nav = Scene.NavMesh;
		if ( nav is null || !nav.IsEnabled ) return false;

		Vector3? start = nav.GetClosestPoint( position, NavMeshSnapRadius );
		if ( !start.HasValue )
		{
			if ( !_warnedNoNavStart )
			{
				_warnedNoNavStart = true;
				Log.Warning( $"AICarDriver on '{GameObject.Name}': no navmesh within {NavMeshSnapRadius} units of the car (navmesh still loading, not baked, or car off the road mesh). Retrying every {RetryDelay}s." );
			}
			return false;
		}

		if ( !TryFindDestinationHelper( nav, start.Value, awayFrom, out NavMeshPath path, out Vector3 target ) )
			return false;

		CommitPathHelper( path, target );
		_warnedNoNavStart = false;
		return true;
	}

	/// <summary>
	/// Tries random candidates in the distance band around <paramref name="start"/> (already on the
	/// navmesh) and returns the first one that sits in an allowed destination area, that the
	/// navmesh can reach completely and that the corridor check accepts. Partial paths mean the
	/// candidate sits on another navmesh island. With <paramref name="awayFrom"/> set, a route whose
	/// first leg points within 60 degrees of it is only used if no other complete route turns up.
	/// v17: candidates whose drivable route is narrower than EffectiveMinCorridorWidth are rejected
	/// and the loop keeps trying; the widest-min candidate is kept as a last resort so the check
	/// alone never makes the car idle. v17b review F2: teleport picks run the corridor check too -
	/// the widest-min fallback commits whenever any complete route exists, so rejecting candidates
	/// over the check can never idle a car, and the teleport keeps its escape hatch.
	/// v18: candidates whose raw route passes within EffectiveNoGoZoneRadius of an ACTIVE learned
	/// no-go zone (see RecordNoGoZoneHelper) are rejected the same way, counted separately
	/// (routes_rejected_nogo). v18b review F1: the last-resort pools are split - widest clean
	/// (width-only rejections) is committed before widest no-go, and the fallback slot is
	/// unchanged, so rejecting candidates over the checks can never idle a car and a route
	/// THROUGH an active trap is never preferred over a narrow clean one.
	/// </summary>
	private bool TryFindDestinationHelper( NavMesh nav, Vector3 start, Vector3? awayFrom, out NavMeshPath foundPath, out Vector3 foundTarget, bool applyCorridorCheck = true )
	{
		foundPath = default;
		foundTarget = default;

		float minDistance = MathF.Max( 0f, MathF.Min( MinDestinationDistance, MaxDestinationDistance ) );
		float maxDistance = MathF.Max( MinDestinationDistance, MaxDestinationDistance );
		int attempts = Math.Max( 1, DestinationPickAttempts );

		float threshold = EffectiveMinCorridorWidth;
		bool check = applyCorridorCheck && threshold > 0f;
		// v18: the no-go check is independent of the corridor check (own enable rule: radius).
		bool nogoOn = EffectiveNoGoZoneRadius > 0f && NoGoZonesForSceneHelper( Scene ).Count > 0;
		_legRoutesRejected = 0;
		_legNogoRejected = 0;
		_rejMinWidth = -1f; // v17b F4: worst rejected candidate is tracked per pick
		_rejMinWidthAt = default;

		// First awayFrom-restricted candidate that also passes the corridor check: the legacy
		// fallback slot (used only if nothing better turns up in the budget).
		bool haveFallback = false;
		NavMeshPath fallbackPath = default;
		Vector3 fallbackTarget = default;
		float fallbackWidth = -1f;
		Vector3 fallbackAt = default;
		// Widest-min candidate across the whole attempt budget, used only when every route fails
		// the checks: the car takes the least-bad route rather than idling over this. v18b review
		// F1: TWO pools. bestClean holds routes rejected for width only; bestNogo holds any route
		// that touches an active zone. A wide route THROUGH a trap must never outrank a narrow
		// clean route, so the fallback order is fallback slot, then bestClean, then bestNogo.
		bool haveClean = false;
		float bestCleanWidth = float.MinValue;
		NavMeshPath bestCleanPath = default;
		Vector3 bestCleanTarget = default;
		Vector3 bestCleanAt = default;
		bool haveNogo = false;
		float bestNogoWidth = float.MinValue;
		NavMeshPath bestNogoPath = default;
		Vector3 bestNogoTarget = default;
		Vector3 bestNogoAt = default;

		for ( int i = 0; i < attempts; i++ )
		{
			float yaw = _random.Float( 0f, 360f );
			float distance = _random.Float( minDistance, maxDistance );
			Vector3 candidate = start + Rotation.FromYaw( yaw ).Forward * distance;

			Vector3? target = nav.GetClosestPoint( candidate, NavMeshSnapRadius );
			if ( !target.HasValue ) continue;

			float flat = FlatDistance( start, target.Value );
			if ( flat < minDistance || flat > maxDistance ) continue;
			if ( !IsInDestinationAreaHelper( nav, target.Value ) ) continue;

			NavMeshPath path = CalculateRouteHelper( nav, start, target.Value );
			if ( path.Status != NavMeshPathStatus.Complete || path.Points is null || path.Points.Count < 2 ) continue;

			// v17: measure the RAW navmesh path. SetPathHelper's snap-merge, StraightenPathHelper
			// and WidenCornersHelper run at commit, after this decision, so this is the route
			// before corner widening. Deliberate: the raw string-pulled path hugs corners and
			// obstacles tighter than the widened line the car drives, so it is the conservative
			// reading - a route that passes here stays passable once widened.
			bool measured = false;
			float minWidth = float.MaxValue;
			Vector3 minAt = default;
			if ( check )
			{
				BuildCorridorRouteHelper( path, _corridorScratch );
				MeasureCorridorHelper( _corridorScratch, out minWidth, out minAt );
				measured = minWidth >= 0f; // -1 = window empty, nothing to check
			} else if ( nogoOn )
			{
				BuildCorridorRouteHelper( path, _corridorScratch ); // raw polyline for the zone test
			}
			// v18: a route whose raw polyline passes within NoGoZoneRadius of an ACTIVE zone is
			// declined too (the first CorridorCheckSkip units are excluded: a car parked next to a
			// zone can still leave). Rejected routes still feed the widest-min fallback below, so
			// this check alone can never idle a car - if the fallback takes a no-go route, fine.
			bool nogo = nogoOn && RouteHitsNoGoZoneHelper( _corridorScratch );
			bool bad = (measured && minWidth < threshold) || nogo;
			if ( bad )
			{
				_legRoutesRejected++;
				CountHelper( "routes_rejected" );
				if ( nogo )
				{
					_legNogoRejected++;
					CountHelper( "routes_rejected_nogo" );
				}
				// v17b F4: remember the narrowest rejected candidate of this pick for LegStart's
				// rej_minw. v18: only corridor-measured rejections carry a width for this.
				if ( measured && (_rejMinWidth < 0f || minWidth < _rejMinWidth) )
				{
					_rejMinWidth = minWidth;
					_rejMinWidthAt = minAt;
				}
			}

			if ( awayFrom.HasValue && Vector3.Dot( GetFirstLegHelper( path ), awayFrom.Value ) > 0.5f )
			{
				// Legacy fallback slot: first awayFrom-restricted candidate that passes the check.
				if ( !bad && !haveFallback )
				{
					haveFallback = true;
					fallbackPath = path;
					fallbackTarget = target.Value;
					fallbackWidth = measured ? minWidth : -1f;
					fallbackAt = minAt;
				}
			} else if ( !bad )
			{
				_legMinWidth = measured ? minWidth : -1f;
				_legMinWidthAt = minAt;
				foundPath = path;
				foundTarget = target.Value;
				return true;
			}

			// Widest-min candidate per pool (rejected ones included): if nothing passes, the car
			// takes the least-bad route rather than idling over these checks. v18b review F1: a
			// no-go rejection goes in the nogo pool ONLY - never in the widest-clean pool, and an
			// unmeasured no-go candidate carries width -1 (worst), never float.MaxValue, so a wide
			// route through an active trap can never outrank a narrow clean route.
			if ( bad )
			{
				if ( nogo )
				{
					float nogoWidth = measured ? minWidth : -1f;
					if ( !haveNogo || nogoWidth > bestNogoWidth )
					{
						haveNogo = true;
						bestNogoWidth = nogoWidth;
						bestNogoPath = path;
						bestNogoTarget = target.Value;
						bestNogoAt = minAt;
					}
				} else if ( !haveClean || minWidth > bestCleanWidth )
				{
					haveClean = true;
					bestCleanWidth = minWidth;
					bestCleanPath = path;
					bestCleanTarget = target.Value;
					bestCleanAt = minAt;
				}
			}
		}

		if ( haveFallback )
		{
			_legMinWidth = fallbackWidth;
			_legMinWidthAt = fallbackAt;
			foundPath = fallbackPath;
			foundTarget = fallbackTarget;
			return true;
		}

		// Every route that reached this point failed a check: clean-but-narrow first, only then a
		// route through a trap - never prefer a trap (v18b review F1).
		if ( haveClean )
		{
			_legMinWidth = bestCleanWidth;
			_legMinWidthAt = bestCleanAt;
			foundPath = bestCleanPath;
			foundTarget = bestCleanTarget;
			return true;
		}

		if ( haveNogo )
		{
			_legMinWidth = bestNogoWidth;
			_legMinWidthAt = bestNogoAt;
			foundPath = bestNogoPath;
			foundTarget = bestNogoTarget;
			return true;
		}

		return false;
	}

	/// <summary>
	/// v17: copies a navmesh path's points into a flat list for <see cref="MeasureCorridorHelper"/>.
	/// </summary>
	private static void BuildCorridorRouteHelper( NavMeshPath path, List<Vector3> into )
	{
		into.Clear();
		foreach ( var point in path.Points )
			into.Add( point.Position );
	}

	/// <summary>
	/// v17: narrowest free corridor along a route, in units. Walks the polyline (flat length) from
	/// <see cref="EffectiveCorridorCheckSkip"/> to (length - skip), every
	/// <see cref="EffectiveCorridorSampleSpacing"/> units, at most ~150 samples (spacing grows on
	/// long routes). At each sample p with the segment's flat direction d, a thin ray (no radius)
	/// runs from p + Up * WhiskerHeight toward flat-left and flat-right of d, each
	/// EffectiveMinCorridorWidth long, ignoring our own hierarchy and dynamic bodies (other cars
	/// must not make a route look narrow), with ground hits slid past on the same
	/// Normal.z >= GroundNormalZ rule as the whiskers. width = leftDist + rightDist, a miss
	/// counting as the full cast length. Early-outs on the first sample below the threshold (only
	/// the minimum matters for rejection); a passing route keeps its real minimum for telemetry.
	/// minWidth = -1 when nothing was checked: the check disabled, a route under 2 points, or the
	/// whole route inside the skip window.
	/// </summary>
	private void MeasureCorridorHelper( IReadOnlyList<Vector3> path, out float minWidth, out Vector3 minAt )
	{
		minWidth = -1f;
		minAt = default;

		float threshold = EffectiveMinCorridorWidth;
		if ( threshold <= 0f || path.Count < 2 ) return;

		_corridorCumLen.Clear();
		_corridorCumLen.Add( 0f );
		for ( int i = 1; i < path.Count; i++ )
			_corridorCumLen.Add( _corridorCumLen[^1] + FlatDistance( path[i - 1], path[i] ) );

		float total = _corridorCumLen[^1];
		float skip = MathF.Min( EffectiveCorridorCheckSkip, total * 0.5f );
		float end = total - skip;
		if ( end - skip < 1f ) return;

		float spacing = EffectiveCorridorSampleSpacing;
		if ( (end - skip) > 149f * spacing ) spacing = (end - skip) / 149f;

		int seg = 0;
		bool any = false;
		float best = float.MaxValue;
		Vector3 bestAt = default;

		for ( float t = skip; ; t += spacing )
		{
			float sample = MathF.Min( t, end );

			// Segment under the sample...
			while ( seg + 2 < path.Count && _corridorCumLen[seg + 1] <= sample ) seg++;
			// ...stepping off a degenerate (<1u) leg onto the next one, never past the last leg.
			while ( seg + 1 < path.Count - 1 && _corridorCumLen[seg + 1] - _corridorCumLen[seg] < 1f ) seg++;

			Vector3 a = path[seg], b = path[seg + 1];
			Vector3 d = b - a;
			d.z = 0f;
			float segLen = d.Length;
			if ( segLen < 1f ) break;
			d /= segLen;

			// Sample point kept at the polyline's own z (interpolated); the trace origin is that
			// point lifted by WhiskerHeight, so slopes and bridges read at a constant height
			// above the road surface rather than a fixed world z.
			float f = float.Clamp( (sample - _corridorCumLen[seg]) / segLen, 0f, 1f );
			Vector3 p = a + (b - a) * f;

			Vector3 left = new( -d.y, d.x, 0f );
			Vector3 origin = p + Vector3.Up * WhiskerHeight;
			float width = CorridorSideTraceHelper( origin, left, threshold ) + CorridorSideTraceHelper( origin, -left, threshold );

			if ( !any || width < best )
			{
				any = true;
				best = width;
				bestAt = p;
			}

			if ( best < threshold || sample >= end ) break;
		}

		if ( any )
		{
			minWidth = best;
			minAt = bestAt;
		}
	}

	/// <summary>
	/// v17: one cross-route ray for <see cref="MeasureCorridorHelper"/>: distance from origin to
	/// the first solid non-ground hit, castLength on a miss. Ground (Normal.z >= GroundNormalZ,
	/// not StartedSolid) does not narrow a corridor - the same rule the whiskers use so ramps and
	/// slopes stay road. StartedSolid reads as distance 0.
	/// </summary>
	private float CorridorSideTraceHelper( Vector3 origin, Vector3 direction, float castLength )
	{
		SceneTraceResult result = Scene.Trace.Ray( origin, origin + direction * castLength )
			.IgnoreGameObjectHierarchy( GameObject )
			.IgnoreDynamic()
			.Run();

		if ( !result.Hit ) return castLength;
		if ( !result.StartedSolid && result.Normal.z >= GroundNormalZ ) return castLength;
		return result.StartedSolid ? 0f : result.Distance;
	}

	/// <summary>Recomputes the path from the car to the current destination. False if it can't be reached.</summary>
	private bool TryRepathHelper( Vector3 position )
	{
		var nav = Scene.NavMesh;
		if ( nav is null || !nav.IsEnabled ) return false;

		Vector3? start = nav.GetClosestPoint( position, NavMeshSnapRadius );
		if ( !start.HasValue ) return false;

		NavMeshPath path = CalculateRouteHelper( nav, start.Value, Destination );
		if ( path.Status != NavMeshPathStatus.Complete || path.Points is null || path.Points.Count < 2 ) return false;

		SetPathHelper( path );
		TelemetryEventHelper( "Repath", $"points={_path.Count} path={FmtPath( _path )}" );
		return true;
	}

	/// <summary>Route query. Only passes an area filter when <see cref="RouteForbiddenAreas"/> is set.</summary>
	private NavMeshPath CalculateRouteHelper( NavMesh nav, Vector3 start, Vector3 target )
	{
		NavMeshAgent filter = null;
		if ( RouteForbiddenAreas is { Count: > 0 } )
		{
			EnsureFilterAgentsHelper();
			_routeFilter.AllowedAreas = new();
			_routeFilter.ForbiddenAreas = RouteForbiddenAreas;
			_routeFilter.AllowDefaultArea = true;
			filter = _routeFilter;
		}

		return nav.CalculatePath( new CalculatePathRequest { Start = start, Target = target, Agent = filter } );
	}

	/// <summary>
	/// True if <paramref name="point"/> (on the navmesh) lies in an allowed destination area.
	/// GetClosestPoint/GetRandomPoint ignore areas, so the point is re-snapped through a path query
	/// carrying the area filter: it stays put only when its own polygon is allowed.
	/// </summary>
	private bool IsInDestinationAreaHelper( NavMesh nav, Vector3 point )
	{
		bool filtered = DestinationAllowedAreas is { Count: > 0 }
			|| DestinationForbiddenAreas is { Count: > 0 }
			|| !DestinationAllowDefaultArea;
		if ( !filtered ) return true;

		EnsureFilterAgentsHelper();
		_destinationFilter.AllowedAreas = DestinationAllowedAreas ?? new();
		_destinationFilter.ForbiddenAreas = DestinationForbiddenAreas ?? new();
		_destinationFilter.AllowDefaultArea = DestinationAllowDefaultArea;

		NavMeshPath probe = nav.CalculatePath( new CalculatePathRequest { Start = point, Target = point, Agent = _destinationFilter } );
		if ( probe.Points is null || probe.Points.Count == 0 ) return false;

		return FlatDistance( probe.Points[0].Position, point ) < 8f;
	}

	/// <summary>
	/// The area filters live on NavMeshAgent, which CalculatePath accepts as its filter source. These
	/// two agents stay disabled on a hidden, unsaved, unnetworked child object, so they never join
	/// the crowd simulation or move anything.
	/// </summary>
	private void EnsureFilterAgentsHelper()
	{
		if ( _filterObject.IsValid() && _destinationFilter.IsValid() && _routeFilter.IsValid() ) return;

		_filterObject = new GameObject( false, "AICarDriver nav filters" );
		_filterObject.Flags = GameObjectFlags.Hidden | GameObjectFlags.NotSaved;
		_filterObject.NetworkMode = NetworkMode.Never;
		_filterObject.SetParent( GameObject, false );

		_destinationFilter = _filterObject.AddComponent<NavMeshAgent>( false );
		_routeFilter = _filterObject.AddComponent<NavMeshAgent>( false );
	}

	private void CommitPathHelper( NavMeshPath path, Vector3 target )
	{
		SetPathHelper( path );
		Destination = target;
		TelemetryLegStartHelper();
	}

	private void SetPathHelper( NavMeshPath path )
	{
		_path.Clear();
		foreach ( var point in path.Points )
		{
			// Merge points closer than MinPathPointSpacing to the last kept one (never the start or
			// the destination), so a turn split over short segments reads as one corner.
			if ( _path.Count > 0 && FlatDistance( _path[^1], point.Position ) < MinPathPointSpacing && _path.Count > 1 )
				_path[^1] = point.Position;
			else
				_path.Add( point.Position );
		}

		StraightenPathHelper();

		WidenCornersHelper();

		// Points[0] is the snapped start; drive toward the first corner after it.
		_waypointIndex = 1;
		_sinceRepath = 0f;

		// v12b (F3): the corner-aim trace cache is keyed on (corner index, waypoint index); a new
		// path reuses both indices for different world points, so stale clear/blocked answers
		// must not survive the handover. Force the next gate-d call to re-trace.
		_cornerAimCache = 10f;
		_cornerAimCacheIndex = 0;
	}

	/// <summary>
	/// Drops near-collinear interior path points (v12): a bend under 12 degrees that sits within
	/// 25 units (flat, perpendicular) of the line from its predecessor to its successor is a
	/// navmesh/string-pull artifact, not a dodge around something. Removing them gives the
	/// corner-aim gate in GetPathAngleHelper real corners to find; braking
	/// (GetCornerSpeedLimitHelper) still reads the same polyline, now simpler.
	/// v12b (F4): single forward pass instead of one-point-at-a-time re-scans. A point i is only
	/// skipped when EVERY point skipped since the last kept point (the anchor) up to i stays
	/// within 25u of the long chord anchor -> i+1 AND the turn at i measured off the anchor is
	/// under 12 degrees: a gentle multi-point walk-around each of whose vertices sat under the
	/// old per-point thresholds no longer collapses into a chord through what the navmesh
	/// routed around. Index 0 (snapped start) and the destination are never touched.
	/// </summary>
	private void StraightenPathHelper()
	{
		if ( _path.Count < 3 ) return;

		var kept = new List<Vector3>( _path.Count ) { _path[0] };
		int anchor = 0;
		for ( int i = 1; i < _path.Count - 1; i++ )
		{
			// Turn at i with the skipped points removed: legs anchor -> i and i -> i+1.
			bool onChord = LegAngleHelper( _path[i] - _path[anchor], _path[i + 1] - _path[i] ) < 12f;
			if ( onChord )
			{
				// The whole walk-around so far must fit under the chord anchor -> i+1.
				for ( int k = anchor + 1; k <= i; k++ )
				{
					if ( PerpendicularFlatDistanceHelper( _path[k], _path[anchor], _path[i + 1] ) >= 25f )
					{
						onChord = false;
						break;
					}
				}
			}

			if ( onChord ) continue; // i lies on the chord: skip it, the anchor stays

			kept.Add( _path[i] );
			anchor = i;
		}
		kept.Add( _path[^1] );

		_path.Clear();
		_path.AddRange( kept );
	}

	/// <summary>
	/// Turn angle (degrees) at interior point <paramref name="i"/> of <paramref name="path"/>:
	/// the angle between the legs i-1 -&gt; i and i -&gt; i+1 in the flat plane. 0 = straight
	/// through, 180 = full reversal. Degenerate legs read as 0.
	/// </summary>
	private static float TurnAngleAtHelper( int i, List<Vector3> path )
	{
		if ( i <= 0 || i >= path.Count - 1 ) return 0f;
		return LegAngleHelper( path[i] - path[i - 1], path[i + 1] - path[i] );
	}

	/// <summary>Angle (degrees) between two flat direction vectors; 0 for a degenerate leg.</summary>
	private static float LegAngleHelper( Vector3 a, Vector3 b )
	{
		Vector3 fa = a.WithZ( 0f );
		Vector3 fb = b.WithZ( 0f );
		if ( fa.LengthSquared < 1f || fb.LengthSquared < 1f ) return 0f;
		return MathF.Acos( float.Clamp( Vector3.Dot( fa.Normal, fb.Normal ), -1f, 1f ) ).RadianToDegree();
	}

	/// <summary>
	/// Flat perpendicular distance from <paramref name="p"/> to the infinite line a-b
	/// (well, the segment: t is clamped, so past-the-end points measure to the nearer end).
	/// </summary>
	private static float PerpendicularFlatDistanceHelper( Vector3 p, Vector3 a, Vector3 b )
	{
		ProjectFlat( p, a, b, out Vector3 closest );
		return FlatDistance( p, closest );
	}

	/// <summary>
	/// Pushes every interior corner toward the outside of its turn by up to
	/// <see cref="CornerOutsideOffset"/> (scaled by the turn angle, limited by the shorter adjacent
	/// segment so a short jog is not turned into a zig-zag), then snaps it back onto the navmesh.
	/// The navmesh's string-pulled path runs tight against the inside of every turn; a car on a
	/// 250-500 unit turning circle that follows it clips the inside corner (v1: building corners
	/// were the top collision and block spots).
	/// </summary>
	private void WidenCornersHelper()
	{
		if ( CornerOutsideOffset <= 0f || _path.Count < 3 ) return;
		var nav = Scene.NavMesh;
		if ( nav is null || !nav.IsEnabled ) return;

		// Work from the original points so one moved corner doesn't skew the next one's angle.
		var original = _path.ToArray();
		for ( int i = 1; i < original.Length - 1; i++ )
		{
			Vector3 inLeg = (original[i] - original[i - 1]).WithZ( 0f );
			Vector3 outLeg = (original[i + 1] - original[i]).WithZ( 0f );
			if ( inLeg.LengthSquared < 1f || outLeg.LengthSquared < 1f ) continue;

			Vector3 inDir = inLeg.Normal;
			Vector3 outDir = outLeg.Normal;
			float turn = MathF.Acos( float.Clamp( Vector3.Dot( inDir, outDir ), -1f, 1f ) ).RadianToDegree();
			if ( turn < 15f ) continue;

			// Outside of the turn = opposite the bisector direction (outDir - inDir points inside).
			Vector3 inside = (outDir - inDir).WithZ( 0f );
			if ( inside.LengthSquared < 0.0001f ) continue;
			Vector3 outside = -inside.Normal;

			float shortest = MathF.Min( inLeg.Length, outLeg.Length );
			float offset = MathF.Min( CornerOutsideOffset * float.Clamp( turn / 90f, 0f, 1f ), shortest * 0.4f );

			Vector3 wanted = original[i] + outside * offset;
			Vector3? snapped = nav.GetClosestPoint( wanted, offset + 50f );
			if ( !snapped.HasValue ) continue;

			// Only accept a snap that still moved outward; a snap back past the original corner
			// (thin road, nothing outside) keeps the original point.
			if ( Vector3.Dot( (snapped.Value - original[i]).WithZ( 0f ), outside ) <= 0f ) continue;

			// v5 M1: the cube search box has no area filter and no island check, so a snap can
			// land on a disconnected piece of navmesh far away. Reject a snap that drifted too
			// far from the original corner or off its level; the original point stays.
			if ( FlatDistance( snapped.Value, original[i] ) > offset + 60f ) continue;
			if ( MathF.Abs( snapped.Value.z - original[i].z ) > 60f ) continue;
			_path[i] = snapped.Value;
		}
	}

	/// <summary>Flat direction of the first non-degenerate path leg.</summary>
	private static Vector3 GetFirstLegHelper( NavMeshPath path )
	{
		for ( int i = 1; i < path.Points.Count; i++ )
		{
			Vector3 leg = (path.Points[i].Position - path.Points[0].Position).WithZ( 0f );
			if ( leg.LengthSquared > 1f ) return leg.Normal;
		}
		return Vector3.Forward;
	}

	/// <summary>
	/// Moves past every corner the car has driven by (projection beyond the segment end, or within
	/// <see cref="ArrivalDistance"/> of the corner when cutting it), and reports the flat distance
	/// from the car to the current segment.
	/// </summary>
	private void AdvanceWaypointHelper( Vector3 position, out float distanceFromPath )
	{
		while ( _waypointIndex < _path.Count - 1 )
		{
			float t = ProjectFlat( position, _path[_waypointIndex - 1], _path[_waypointIndex], out _ );
			if ( t < 1f && FlatDistance( position, _path[_waypointIndex] ) > ArrivalDistance ) break;

			_waypointIndex++;

			// Progress made (v11b gate, v11c: Driving check dropped - this only runs from
			// TickDrivingHelper, so State is always Driving here): reset the counters only once
			// the car is at least ReverseProgressDistance from the last mark, so a car rocking in
			// place across short merged path segments does not clear its reverse count. No mark
			// yet: reset as before.
			if ( !_progressMarkSet || FlatDistance( position, _progressMark ) >= EffectiveReverseProgressDistance )
			{
				_reverseCount = 0;
				_pivotFails = 0;
				_progressMark = position;
				_progressMarkSet = true;
			}
		}

		ProjectFlat( position, _path[_waypointIndex - 1], _path[_waypointIndex], out Vector3 closest );
		distanceFromPath = FlatDistance( position, closest );
	}

	/// <summary>
	/// Signed yaw (degrees, + = left) from the car's heading to the path look-ahead point.
	/// v12: on a clean straight the pure-pursuit target (420-510 u at speed) sits just past every
	/// shallow road bend, and telemetry showed a 3-4 s weave as the car chases them. When the
	/// next real corner passes the gates (near, farther along the path, corridor-straight, clear
	/// shot), aim at that corner instead; everything else keeps the old behaviour. The decision
	/// is exposed as <see cref="_aimingAtCorner"/> for the samples `aim` column.
	/// v12b (F1): <paramref name="allowCornerAim"/> false forces the pure-pursuit polyline angle.
	/// Decisions about where the path actually goes (path-behind, pivot/reverse side choice,
	/// reverse-exit alignment) must pass false; only steering and the heading-error speed limit
	/// use the corner-aim angle.
	/// </summary>
	private float GetPathAngleHelper( Vector3 position, float forwardSpeed, bool allowCornerAim )
	{
		float lookAhead = LookAheadDistance + MathF.Max( forwardSpeed, 0f ) * LookAheadTime;
		Vector3 pp = GetLookAheadPointHelper( position, lookAhead );

		_aimingAtCorner = false;
		if ( allowCornerAim && !DisableCornerAim && _path.Count >= 2 )
		{
			int j = NextCornerIndexHelper();
			if ( j >= _waypointIndex && CornerAimGateHelper( position, lookAhead, j ) )
			{
				_aimingAtCorner = true;
				return LocalYawAngleTo( _path[j] );
			}
		}

		return LocalYawAngleTo( pp );
	}

	/// <summary>
	/// Index of the next real corner at or after <see cref="_waypointIndex"/>: the first path
	/// point whose turn angle is at least <see cref="EffectiveCornerAimMinAngleHelper"/> degrees,
	/// or the destination (last point), which always counts as a corner. After
	/// <see cref="StraightenPathHelper"/> everything before it is a bend under the aim gate's
	/// noise floor. The path always has a destination, so this never returns -1.
	/// </summary>
	private int NextCornerIndexHelper()
	{
		float minAngle = EffectiveCornerAimMinAngleHelper();
		for ( int j = _waypointIndex; j < _path.Count - 1; j++ )
		{
			if ( TurnAngleAtHelper( j, _path ) >= minAngle ) return j;
		}
		return _path.Count - 1;
	}

	/// <summary>CornerAimMinAngle with the hotload fallback (0 on a live instance reads 15).</summary>
	private float EffectiveCornerAimMinAngleHelper() => CornerAimMinAngle > 1f ? CornerAimMinAngle : 15f;

	/// <summary>CornerAimDistance with the hotload fallback (0 on a live instance reads 1500).</summary>
	private float EffectiveCornerAimDistance => CornerAimDistance > 1f ? CornerAimDistance : 1500f;

	/// <summary>CornerAimCorridor with the hotload fallback (0 on a live instance reads 30).</summary>
	private float EffectiveCornerAimCorridor => CornerAimCorridor > 1f ? CornerAimCorridor : 30f;

	/// <summary>CornerAimTraceDistance with the hotload fallback (0 on a live instance reads 500).</summary>
	private float EffectiveCornerAimTraceDistance => CornerAimTraceDistance > 0f ? CornerAimTraceDistance : 500f;

	/// <summary>
	/// The corner-aim trace radius actually used: CornerAimTraceRadius with the hotload fallback
	/// (0 reads as 0.4 * hull width, ~40) and the v12f clamp under WhiskerHeight, so the sphere
	/// never dips into the road and StartedSolid-vetoes every chord.
	/// </summary>
	private float EffectiveCornerAimTraceRadius
	{
		get
		{
			float radius = CornerAimTraceRadius > 0f
				? CornerAimTraceRadius
				: (_hullSize.y > 1f ? _hullSize.y * 0.4f : 40f);
			return MathF.Min( radius, MathF.Max( WhiskerHeight - 5f, 5f ) );
		}
	}

	/// <summary>CornerFullAngle with the hotload fallback (0 reads 45) and the v10 guard under 120.</summary>
	private float EffectiveCornerFullAngle => MathF.Min( CornerFullAngle > 1f ? CornerFullAngle : 45f, 119f );

	/// <summary>
	/// The four corner-aim gates (v12 spec): a) the corner is within CornerAimDistance; b) it is
	/// farther along the path than the pure-pursuit point, so aiming at it can only lengthen the
	/// look-ahead, never cut a corner short; c) the intermediate path points stay within the
	/// CornerAimCorridor of the straight car-to-corner line (bends leaving the corridor are jogs
	/// around something real); d) a whisker-style ray from the car to the corner is clear.
	/// The trace is the only costly gate and only runs when a-c passed.
	/// </summary>
	private bool CornerAimGateHelper( Vector3 position, float lookAhead, int j )
	{
		Vector3 corner = _path[j];

		float maxDistance = CornerAimDistance > 1f ? CornerAimDistance : 1500f;
		if ( FlatDistance( position, corner ) > maxDistance ) return false;

		float cornerPathDistance = PathDistanceToIndexHelper( position, j );
		float ppPathDistance = MathF.Min( lookAhead, PathDistanceToIndexHelper( position, _path.Count - 1 ) );
		if ( cornerPathDistance <= ppPathDistance ) return false;

		float corridor = CornerAimCorridor > 1f ? CornerAimCorridor : 30f;
		for ( int i = _waypointIndex; i < j; i++ )
		{
			if ( PerpendicularFlatDistanceHelper( _path[i], position, corner ) > corridor ) return false;
		}

		return CornerAimTraceClearHelper( position, corner, j );
	}

	/// <summary>
	/// Flat distance along the path from the car's projection on the current segment to point
	/// <paramref name="index"/> (walking _path[_waypointIndex..index]).
	/// </summary>
	private float PathDistanceToIndexHelper( Vector3 position, int index )
	{
		ProjectFlat( position, _path[_waypointIndex - 1], _path[_waypointIndex], out Vector3 from );
		float total = 0f;
		for ( int i = _waypointIndex; i <= index && i < _path.Count; i++ )
		{
			total += FlatDistance( from, _path[i] );
			from = _path[i];
		}
		return total;
	}

	/// <summary>
	/// Gate d: is the NEAR run along the car-to-corner chord clear? The ray copies the whisker cast
	/// setup (at WhiskerHeight, ignoring this car and dynamic bodies, rounded by
	/// <see cref="CornerAimTraceRadius"/>) and the whisker ground filter (a hit whose normal is
	/// steep enough is a ramp or slope, i.e. road). v12f: the chord can be up to CornerAimDistance
	/// (1500) long, and in a prop-lined city a body-width sweep over the whole length clips
	/// something on nearly every chord (measured: gate d blocked ~100% of chords reaching it, aim
	/// fired 0%), so the trace stops at CornerAimTraceDistance (default 500) toward the corner. The
	/// far run is safe without it: corner aim only steers, the whiskers stop the car before any real
	/// hit, and the corridor gate keeps the path within CornerAimCorridor of the chord. Runs at most
	/// every 0.2 s per corner index (cached), so a held gate costs one trace per fifth of a second.
	/// </summary>
	private bool CornerAimTraceClearHelper( Vector3 position, Vector3 corner, int j )
	{
		if ( _cornerAimCacheIndex == j && _cornerAimCacheWaypoint == _waypointIndex && _cornerAimCache < 0.2f )
			return _cornerAimCacheClear;

		// v12f: trace only the near CornerAimTraceDistance of the chord, not all the way to a
		// corner that can be 1500u away (see the method doc for why the far run needs no trace).
		float traceDistance = EffectiveCornerAimTraceDistance;
		Vector3 to = corner - position;
		float chordLength = to.WithZ( 0f ).Length;
		Vector3 traceEnd = chordLength > traceDistance && chordLength > 0.001f
			? position + to * (traceDistance / chordLength)
			: corner;

		// v12c/v12f: tunable with hotload fallback and the under-WhiskerHeight clamp; see
		// EffectiveCornerAimTraceRadius for why a radius >= the trace height vetoes every chord.
		float radius = EffectiveCornerAimTraceRadius;
		SceneTraceResult result = Scene.Trace.Ray(
				position + Vector3.Up * WhiskerHeight,
				traceEnd + Vector3.Up * WhiskerHeight )
			.IgnoreGameObjectHierarchy( GameObject )
			.IgnoreDynamic()
			.Radius( radius )
			.Run();

		bool clear = !result.Hit || (!result.StartedSolid && result.Normal.z >= GroundNormalZ);

		_cornerAimCacheIndex = j;
		_cornerAimCacheWaypoint = _waypointIndex;
		_cornerAimCacheClear = clear;
		_cornerAimCache = 0f;
		return clear;
	}

	/// <summary>Point <paramref name="distance"/> further along the path from the car's projection onto it.</summary>
	private Vector3 GetLookAheadPointHelper( Vector3 position, float distance )
	{
		ProjectFlat( position, _path[_waypointIndex - 1], _path[_waypointIndex], out Vector3 current );

		float remaining = distance;
		for ( int i = _waypointIndex; i < _path.Count; i++ )
		{
			Vector3 next = _path[i];
			float segment = FlatDistance( current, next );
			if ( segment >= remaining && segment > 0.001f )
				return Vector3.Lerp( current, next, remaining / segment );

			remaining -= segment;
			current = next;
		}

		return _path[^1];
	}

	/// <summary>
	/// SharpCornerSpeed with the hotload fallback (0 on a live instance) and the clamp so a sharp
	/// turn is never faster than CornerSpeed. Shared by CornerSpeedForAngleHelper and the
	/// braking horizon in GetCornerSpeedLimitHelper.
	/// </summary>
	private float EffectiveSharpCornerSpeedHelper()
	{
		float sharp = SharpCornerSpeed > 1f ? SharpCornerSpeed : 200f;
		return MathF.Min( sharp, CornerSpeed );
	}

	/// <summary>
	/// v10 corner speed for a turn of this many degrees, independent of CruiseSpeed on straights:
	/// a 0 degree bend allows Min(CruiseSpeed, CornerSpeed*4), easing out to CornerSpeed at
	/// CornerFullAngle (default 45), then down to SharpCornerSpeed (default 200, clamped to
	/// CornerSpeed) at 120 degrees. Callers take the Min with their current target so corners only
	/// ever limit, never raise the speed.
	/// </summary>
	private float CornerSpeedForAngleHelper( float angleDegrees )
	{
		float a = MathF.Abs( angleDegrees );
		float full = EffectiveCornerFullAngle;
		float sharp = EffectiveSharpCornerSpeedHelper();

		if ( a <= full )
		{
			float t = a / full;
			return Lerp( MathF.Min( CruiseSpeed, CornerSpeed * 4f ), CornerSpeed, t * (2f - t) );
		}

		return Lerp( CornerSpeed, sharp, (a - full) / (120f - full) );
	}

	/// <summary>
	/// Speed limit from the corners ahead (and the destination, treated as a 90 degree corner):
	/// each corner's speed comes from CornerSpeedForAngleHelper (the v10 curve: eases from
	/// Min(Cruise, 4*Corner) at 0 degrees down to CornerSpeed at CornerFullAngle and to
	/// SharpCornerSpeed at 120), and the limit now is sqrt(cornerSpeed^2 + 2 * CornerBrakeDecel * d),
	/// so braking starts early enough instead of when the corner is already within
	/// <see cref="CornerLookDistance"/>. The horizon uses the slowest speed the curve can produce
	/// (SharpCornerSpeed) so a hairpin far ahead is still braked for early. Corners within
	/// CornerLookDistance also cap the speed directly (the old rule), so a corner the car is
	/// already inside of stays slow until it is past it.
	/// </summary>
	private float GetCornerSpeedLimitHelper( Vector3 position )
	{
		float decel = MathF.Max( CornerBrakeDecel, 1f );
		float limit = CruiseSpeed;
		// Beyond this distance no corner can lower the limit below cruise. Based on the slowest
		// speed the corner curve allows (SharpCornerSpeed), so braking for a far hairpin starts early.
		float sharp = EffectiveSharpCornerSpeedHelper();
		float horizon = (CruiseSpeed * CruiseSpeed - sharp * sharp) / (2f * decel) + CornerLookDistance;
		// CruiseSpeed below SharpCornerSpeed makes the first term negative; never scan less than the
		// CornerLookDistance window the direct cap below relies on.
		horizon = MathF.Max( horizon, CornerLookDistance );

		ProjectFlat( position, _path[_waypointIndex - 1], _path[_waypointIndex], out Vector3 current );
		float travelled = FlatDistance( current, _path[_waypointIndex] );

		for ( int i = _waypointIndex; i < _path.Count && travelled < horizon; i++ )
		{
			float angle = 90f; // destination: full corner, arrive at or below CornerSpeed (v10: ~260 at defaults)
			if ( i < _path.Count - 1 )
			{
				Vector3 inDir = (_path[i] - _path[i - 1]).WithZ( 0f ).Normal;
				Vector3 outDir = (_path[i + 1] - _path[i]).WithZ( 0f ).Normal;
				angle = MathF.Acos( float.Clamp( Vector3.Dot( inDir, outDir ), -1f, 1f ) ).RadianToDegree();
			}

			float cornerSpeed = CornerSpeedForAngleHelper( angle );
			float allowed = travelled < CornerLookDistance
				? cornerSpeed
				: MathF.Sqrt( cornerSpeed * cornerSpeed + 2f * decel * (travelled - CornerLookDistance) );
			limit = MathF.Min( limit, allowed );

			if ( i < _path.Count - 1 )
				travelled += FlatDistance( _path[i], _path[i + 1] );
		}

		return limit;
	}

	// ================================================================== Whiskers

	/// <summary>Lays out the whiskers from the car's hull BoxCollider (falls back to car_normal's hull size).</summary>
	private void BuildWhiskersHelper()
	{
		Vector3 center = new( 3.6f, 0f, 33f );
		Vector3 size = new( 217f, 100f, 47.5f );

		var box = GetComponent<BoxCollider>();
		if ( box.IsValid() )
		{
			center = box.Center;
			size = box.Scale;
		}
		else
		{
			Log.Warning( $"AICarDriver on '{GameObject.Name}': no BoxCollider on the car root, using default whisker layout." );
		}

		_hullCenter = center;
		_hullSize = size;

		float front = center.x + size.x * 0.5f + 2f;
		float rear = center.x - size.x * 0.5f - 2f;
		float halfWidth = size.y * 0.5f;
		Vector3 sideLeft = Rotation.FromYaw( SideWhiskerAngle ).Forward;
		Vector3 sideRight = Rotation.FromYaw( -SideWhiskerAngle ).Forward;

		_frontWhiskers = new[]
		{
			new Whisker { LocalOrigin = new Vector3( front, 0f, 0f ), LocalDirection = Vector3.Forward, LengthScale = 1f, Side = 0f, IsForward = true },
			new Whisker { LocalOrigin = new Vector3( front, halfWidth, 0f ), LocalDirection = Vector3.Forward, LengthScale = 1f, Side = 1f, IsForward = true },
			new Whisker { LocalOrigin = new Vector3( front, -halfWidth, 0f ), LocalDirection = Vector3.Forward, LengthScale = 1f, Side = -1f, IsForward = true },
			new Whisker { LocalOrigin = new Vector3( front, halfWidth, 0f ), LocalDirection = sideLeft, LengthScale = -1f, Side = 1f, IsForward = false },
			new Whisker { LocalOrigin = new Vector3( front, -halfWidth, 0f ), LocalDirection = sideRight, LengthScale = -1f, Side = -1f, IsForward = false },
		};

		_rearWhiskers = new[]
		{
			new Whisker { LocalOrigin = new Vector3( rear, 0f, 0f ), LocalDirection = Vector3.Backward, LengthScale = 1f },
			new Whisker { LocalOrigin = new Vector3( rear, halfWidth, 0f ), LocalDirection = Vector3.Backward, LengthScale = 1f },
			new Whisker { LocalOrigin = new Vector3( rear, -halfWidth, 0f ), LocalDirection = Vector3.Backward, LengthScale = 1f },
		};

		_frontHitDistance = new float[_frontWhiskers.Length];
		_frontHitName = new string[_frontWhiskers.Length];
		_frontLength = new float[_frontWhiskers.Length];
		_rearHitDistance = new float[_rearWhiskers.Length];
	}

	private void ProbeFrontHelper( float forwardSpeed )
	{
		float length = WhiskerLength + MathF.Max( forwardSpeed, 0f ) * WhiskerLengthPerSpeed;
		bool pushThrough = IsPushingThrough;
		_dynamicAhead = float.MaxValue;
		float ledgeHalfWidth = _hullSize.y * 0.5f;
		_ledgeAhead = LedgeProbeSideHelper( _hullCenter.x + _hullSize.x * 0.5f, 1f, length, ledgeHalfWidth );

		for ( int i = 0; i < _frontWhiskers.Length; i++ )
		{
			// LengthScale < 0 marks a side whisker, which follows SideWhiskerScale live.
			float scale = _frontWhiskers[i].LengthScale < 0f ? SideWhiskerScale : _frontWhiskers[i].LengthScale;
			_frontLength[i] = length * scale;

			if ( !pushThrough )
			{
				_frontHitDistance[i] = CastWhiskerHelper( _frontWhiskers[i], _frontLength[i], false );
				_frontHitName[i] = _lastCastHitName;
				continue;
			}

			// Pushing through: steer and stop only for world geometry (seen even behind a shoved
			// body), but remember the nearest dynamic body straight ahead to cap the shove speed.
			_frontHitDistance[i] = CastWhiskerHelper( _frontWhiskers[i], _frontLength[i], true );
			_frontHitName[i] = _lastCastHitName;
			if ( _frontWhiskers[i].IsForward )
			{
				float any = CastWhiskerHelper( _frontWhiskers[i], _frontLength[i], false );
				if ( any < _frontHitDistance[i] ) _dynamicAhead = MathF.Min( _dynamicAhead, any );
			}
		}
	}

	/// <summary>Casts the rear rays (and the rear ledge probe) and returns the nearest hit distance (or the probe length).</summary>
	private float ProbeRearHelper()
	{
		float nearest = RearProbeLength;
		for ( int i = 0; i < _rearWhiskers.Length; i++ )
		{
			_rearHitDistance[i] = CastWhiskerHelper( _rearWhiskers[i], RearProbeLength, false );
			nearest = MathF.Min( nearest, _rearHitDistance[i] );
		}
		_ledgeBehind = LedgeProbeSideHelper( _hullCenter.x - _hullSize.x * 0.5f, -1f, RearProbeLength, _hullSize.y * 0.5f );
		return MathF.Min( nearest, _ledgeBehind );
	}

	/// <summary>
	/// v16: two sideways casts across the front quarter of the hull. The inside FRONT flank is where
	/// mid-turn side scrapes happen (telemetry 20261007-024815: 118/147 inside scrapes ahead of +40),
	/// and the forward whisker fan cannot see an object sitting beside it. Reuses the whisker trace
	/// setup (CastWhiskerHelper: same origin frame plus WhiskerHeight, radius, ignore rules, ground
	/// rejection). gap = distance from the hull side to the first solid non-ground hit, clamped >= 0;
	/// no hit = effective FlankProbeLength. Feature disabled (negative length) or hull not built:
	/// both gaps = effective FlankProbeLength (clamped >= 0). Writes _flankLeftGap/_flankRightGap
	/// for the samples.csv lflank/rflank columns.
	/// </summary>
	private void ProbeFlanksHelper( out float leftGap, out float rightGap )
	{
		float length = EffectiveFlankProbeLength;
		if ( length <= 0f || _hullSize.y < 1f )
		{
			leftGap = MathF.Max( length, 0f );
			rightGap = leftGap;
			_flankLeftGap = leftGap;
			_flankRightGap = rightGap;
			return;
		}

		float halfWidth = _hullSize.y * 0.5f;
		float castLength = halfWidth + length;
		// CastWhiskerHelper returns how far the sphere CENTER travelled, so the object surface sits
		// one sweep radius further out: gap = hit + radius - halfWidth (a miss clamps to length).
		// v16 review: clamp the radius to the hull half width - if WhiskerThickness were tuned past
		// halfWidth, a StartedSolid hit (distance 0) would report a POSITIVE gap and weaken the cap.
		float radius = MathF.Min( MathF.Max( WhiskerThickness, 0f ), halfWidth );
		// v16 review: front quarter -> front half of the hull. Contacts cluster at +78..+109 along
		// the car (217-long hull) while the old 0.3 origin (+69, radius 25) only swept +44..+94;
		// 12 of 17 v16 scrapes never registered under 60 on the probe. At 0.45 (+98) the sweep
		// covers +73..+123. Centerline origin as before: Side = 0 keeps WhiskerOriginHelper's
		// thickness pull-in off these casts (they deliberately start inside our own hull, which
		// the trace ignores).
		Vector3 origin = new( _hullCenter.x + _hullSize.x * 0.45f, _hullCenter.y, 0f );

		float hitLeft = CastWhiskerHelper( new Whisker { LocalOrigin = origin, LocalDirection = Rotation.FromYaw( 90f ).Forward, LengthScale = 1f, Side = 0f, IsForward = false }, castLength, false );
		float hitRight = CastWhiskerHelper( new Whisker { LocalOrigin = origin, LocalDirection = Rotation.FromYaw( -90f ).Forward, LengthScale = 1f, Side = 0f, IsForward = false }, castLength, false );

		leftGap = float.Clamp( hitLeft + radius - halfWidth, 0f, length );
		rightGap = float.Clamp( hitRight + radius - halfWidth, 0f, length );
		_flankLeftGap = leftGap;
		_flankRightGap = rightGap;
	}

	/// <summary>
	/// LedgeProbeHelper across the body width (v9): centerline plus the two wheel lines at
	/// local y = -halfWidth*0.8 and +halfWidth*0.8, minimum of the three (a wheel going over the
	/// edge is enough to fall). Side samples only run the near and middle fractions inside
	/// LedgeProbeHelper.
	/// </summary>
	private float LedgeProbeSideHelper( float edgeX, float direction, float length, float halfWidth )
	{
		float lateral = halfWidth * 0.8f;
		float nearest = MathF.Min( LedgeProbeHelper( edgeX, direction, length, -lateral ), LedgeProbeHelper( edgeX, direction, length, lateral ) );
		return MathF.Min( nearest, LedgeProbeHelper( edgeX, direction, length, 0f ) );
	}

	/// <summary>
	/// Ground check ahead of (<paramref name="direction"/> +1) or behind (-1) the car: drops rays at
	/// points out to <paramref name="length"/> past the bumper at local x <paramref name="edgeX"/> and
	/// local y <paramref name="lateralOffset"/>, and returns the distance to the nearest point with no
	/// ground (a bridge edge, quay or drop into the sea), or <paramref name="length"/> if all have
	/// ground. Whiskers run horizontally at WhiskerHeight, so they never see a missing floor; v1/v2
	/// telemetry lost cars this way (forward off quay edges, backward off a bridge). The allowed drop
	/// grows with distance so a downhill road far ahead does not read as a ledge. Fractions are
	/// inlined (hotload rule: no static reference-type fields): the centerline (offset 0) checks all
	/// three, the side samples only the near and middle ones to bound the ray count.
	/// </summary>
	private float LedgeProbeHelper( float edgeX, float direction, float length, float lateralOffset = 0f )
	{
		if ( LedgeDropHeight <= 0f || length <= 0f ) return length;

		float nearest = LedgeRayHelper( edgeX, direction, length, lateralOffset, 0.1f );
		if ( nearest >= length ) nearest = LedgeRayHelper( edgeX, direction, length, lateralOffset, 0.45f );
		if ( nearest >= length && lateralOffset == 0f ) nearest = LedgeRayHelper( edgeX, direction, length, lateralOffset, 0.85f );
		return nearest;
	}

	/// <summary>
	/// One downward ledge ray at <paramref name="fraction"/> of the probe length: the distance to it
	/// when no ground is found, or <paramref name="length"/> when ground is found.
	/// </summary>
	private float LedgeRayHelper( float edgeX, float direction, float length, float lateralOffset, float fraction )
	{
		float d = MathF.Max( 30f, length * fraction );
		Vector3 local = new( edgeX + direction * d, lateralOffset, WhiskerHeight );
		Vector3 start = WorldTransform.PointToWorld( local );
		float depth = WhiskerHeight + LedgeDropHeight + d * 0.6f;

		SceneTraceResult result = Scene.Trace.Ray( start, start + Vector3.Down * depth )
			.IgnoreGameObjectHierarchy( GameObject )
			.IgnoreDynamic()
			.Run();

		if ( !result.Hit && !result.StartedSolid ) return d;

		return length;
	}

	/// <summary>
	/// World start point of a whisker. With <see cref="WhiskerThickness"/> the edge whiskers are pulled
	/// in toward the center line by that radius, so the swept spheres cover exactly the car's width:
	/// thick enough to leave no gap between the three forward rays for a thin pole to slip through,
	/// without reaching past the car's sides and catching things beside the lane. Computed per cast,
	/// so a live WhiskerThickness change (tuning.json) takes effect immediately.
	/// </summary>
	private Vector3 WhiskerOriginHelper( in Whisker whisker )
	{
		Vector3 local = whisker.LocalOrigin;
		if ( WhiskerThickness > 0f && whisker.Side != 0f )
			local.y -= whisker.Side * MathF.Min( WhiskerThickness, MathF.Abs( local.y ) );
		return WorldTransform.PointToWorld( local + Vector3.Up * WhiskerHeight );
	}

	/// <summary>
	/// Distance to the first solid, non-ground hit along a whisker; <paramref name="length"/> on a miss.
	/// <paramref name="ignoreDynamic"/> traces through dynamic physics bodies (cars, players, props).
	/// </summary>
	private float CastWhiskerHelper( in Whisker whisker, float length, bool ignoreDynamic )
	{
		Vector3 origin = WhiskerOriginHelper( whisker );
		Vector3 direction = WorldRotation * whisker.LocalDirection;

		var trace = Scene.Trace.Ray( origin, origin + direction * length )
			.IgnoreGameObjectHierarchy( GameObject );

		if ( ignoreDynamic )
			trace = trace.IgnoreDynamic();

		if ( WhiskerThickness > 0f )
			trace = trace.Radius( WhiskerThickness );

		_lastCastHitName = "";
		SceneTraceResult result = trace.Run();
		if ( !result.Hit ) return length;

		// Ramps and slopes are road, not obstacles. A started-in-solid hit has a zero normal and
		// stays an obstacle at distance 0.
		if ( !result.StartedSolid && result.Normal.z >= GroundNormalZ ) return length;

		_lastCastHitName = result.GameObject.IsValid() ? result.GameObject.Name : "?";
		return result.StartedSolid ? 0f : result.Distance;
	}

	/// <summary>
	/// Nearest straight-ahead hit plus how much room each side has (nearest hit on that side's
	/// whiskers). All values equal the whisker length when nothing is hit.
	/// </summary>
	private void GetFrontClearanceHelper( out float forwardClear, out float forwardLength, out float leftClear, out float rightClear )
	{
		forwardClear = float.MaxValue;
		forwardLength = 0f;
		leftClear = float.MaxValue;
		rightClear = float.MaxValue;

		for ( int i = 0; i < _frontWhiskers.Length; i++ )
		{
			if ( !_frontWhiskers[i].IsForward ) continue;
			forwardClear = MathF.Min( forwardClear, _frontHitDistance[i] );
			forwardLength = MathF.Max( forwardLength, _frontLength[i] );
		}

		// A missing floor ahead counts as an obstacle straight ahead (slows, then Blocked/reverse).
		// Not fed into left/right: it says nothing about which side has room.
		if ( _ledgeAhead > 0f ) forwardClear = MathF.Min( forwardClear, _ledgeAhead );

		for ( int i = 0; i < _frontWhiskers.Length; i++ )
		{
			// Normalise by length so the shorter side whiskers compare fairly with the corner rays.
			float normalised = _frontHitDistance[i] / MathF.Max( _frontLength[i], 1f ) * forwardLength;
			if ( _frontWhiskers[i].Side > 0f ) leftClear = MathF.Min( leftClear, normalised );
			if ( _frontWhiskers[i].Side < 0f ) rightClear = MathF.Min( rightClear, normalised );
		}
	}

	/// <summary>
	/// Steering offset (degrees, + = left) pushing away from whisker hits. A side hit pushes to the
	/// other side; a center hit pushes toward the side with more room (path side on a tie).
	/// </summary>
	private float GetAvoidAngleHelper( float pathAngle, float leftClear, float rightClear )
	{
		float freeSide;
		if ( MathF.Abs( leftClear - rightClear ) > 1f )
			freeSide = leftClear > rightClear ? 1f : -1f;
		else
			freeSide = pathAngle >= 0f ? 1f : -1f;

		float push = 0f;
		for ( int i = 0; i < _frontWhiskers.Length; i++ )
		{
			float closeness = 1f - _frontHitDistance[i] / MathF.Max( _frontLength[i], 1f );
			if ( closeness <= 0f ) continue;

			float side = _frontWhiskers[i].Side;
			push += side == 0f ? freeSide * closeness : -side * closeness;
		}

		return float.Clamp( push, -1f, 1f ) * AvoidSteerAngle;
	}

	// ================================================================== Controls

	/// <summary>
	/// v13: heading error (degrees, + = left) with yaw damping and the steer deadband applied
	/// before it becomes steering. Yaw damping subtracts <see cref="SteerYawDamping"/> seconds
	/// of the body's yaw rate (a car already rotating toward the aim line gets a smaller
	/// correction; this is the D of a PD controller and kills the 3-4 s weave). The deadband
	/// then eats <see cref="SteerDeadband"/> degrees off what is left, so small errors steer
	/// nothing. Only the path angle goes through this: the avoidance offset is a commanded
	/// direction, not a heading error, and is added after. Below <see cref="StuckSpeed"/> the
	/// car crawls or pivots and its yaw rate is not line-following, so the angle is untouched.
	/// </summary>
	private float DampedSteerAngleHelper( float angle, float forwardSpeed )
	{
		if ( forwardSpeed <= StuckSpeed ) return angle;

		float damping = EffectiveSteerYawDamping;
		if ( damping > 0f )
			angle -= damping * _body.AngularVelocity.z.RadianToDegree();

		float magnitude = MathF.Max( MathF.Abs( angle ) - EffectiveSteerDeadband, 0f );
		return MathF.Sign( angle ) * magnitude;
	}

	/// <summary>
	/// Heading error in degrees to steering input. The library squares its turn input
	/// (VehicleController.ApplyTurn), so the command is square-rooted to keep the response linear.
	/// </summary>
	private float SteerFromAngleHelper( float angle )
	{
		float fraction = float.Clamp( angle / MathF.Max( FullSteerAngle, 1f ), -1f, 1f );
		return MathF.Sign( fraction ) * MathF.Sqrt( MathF.Abs( fraction ) );
	}

	private void ApplyControlsHelper( float throttle, float steer, bool brake )
	{
		float blend = SteerResponse <= 0f ? 1f : 1f - MathF.Exp( -SteerResponse * Time.Delta );
		_steer += (steer - _steer) * blend;

		_lastThrottle = throttle;
		_lastBrake = brake;

		_vehicle.UseExternalInput = true;
		_vehicle.ExternalThrottle = throttle;
		_vehicle.ExternalSteer = _steer;
		_vehicle.ExternalBrake = brake;
	}

	/// <summary>
	/// The library only starts its engine from a one-shot request. Re-request every 2 s while it is
	/// off (start takes Engine_StartDelay). The library ignores a request while the engine is on,
	/// so a late one cannot switch it off.
	/// </summary>
	private void EnsureEngineRunningHelper()
	{
		if ( _vehicle.IsEngineOn ) return;
		if ( _sinceEngineRequest < 2f ) return;

		_vehicle.RequestEngineStart();
		_sinceEngineRequest = 0f;
	}

	// ================================================================== Debug

	private void DrawDebugHelper()
	{
		if ( _frontWhiskers is null ) return;

		for ( int i = 0; i < _frontWhiskers.Length; i++ )
			DrawWhiskerHelper( _frontWhiskers[i], _frontLength[i], _frontHitDistance[i] );

		if ( State == DriveState.Reversing )
		{
			for ( int i = 0; i < _rearWhiskers.Length; i++ )
				DrawWhiskerHelper( _rearWhiskers[i], RearProbeLength, _rearHitDistance[i] );
		}

		if ( _path.Count >= 2 )
		{
			DebugOverlay.Line( _path, Color.Cyan );
			DebugOverlay.Sphere( new Sphere( Destination, 40f ), Color.Magenta );
		}

		string text = $"{State}  aggr {Aggression:0.00}{(IsPushingThrough ? " PUSH" : "")}";
		if ( State == DriveState.Reversing )
			text += $"\nnose {(_reverseSteer < 0f ? "left" : "right")} ({_reverseReason})";

		DebugOverlay.Text( WorldPosition + Vector3.Up * 120f, text, 16f );
	}

	private void DrawWhiskerHelper( in Whisker whisker, float length, float hitDistance )
	{
		Vector3 origin = WhiskerOriginHelper( whisker );
		Vector3 direction = WorldRotation * whisker.LocalDirection;
		bool hit = hitDistance < length;

		DebugOverlay.Line( origin, origin + direction * hitDistance, hit ? Color.Red : Color.Green );
		if ( hit )
			DebugOverlay.Line( origin + direction * hitDistance, origin + direction * length, Color.Gray );
	}

	// ================================================================== Math

	/// <summary>Signed yaw (degrees, + = left) from the car's forward to a world point.</summary>
	private float LocalYawAngleTo( Vector3 worldPoint )
	{
		Vector3 local = WorldRotation.Inverse * (worldPoint - WorldPosition);
		return MathF.Atan2( local.y, local.x ).RadianToDegree();
	}

	private static float FlatDistance( Vector3 a, Vector3 b ) => (a - b).WithZ( 0f ).Length;

	/// <summary>
	/// The car's heading flattened to the XY plane, safe against a degenerate rotation: falls back
	/// to Vector3.Forward when the flattened forward is shorter than 0.001 (Normal would be NaN).
	/// </summary>
	private Vector3 FlatForwardHelper()
	{
		Vector3 flat = WorldRotation.Forward.WithZ( 0f );
		return flat.LengthSquared < 0.000001f ? Vector3.Forward : flat.Normal;
	}

	private static float Lerp( float a, float b, float t ) => a + (b - a) * float.Clamp( t, 0f, 1f );

	/// <summary>Flat projection of <paramref name="p"/> onto segment a-b. Returns the unclamped t; closest is clamped.</summary>
	private static float ProjectFlat( Vector3 p, Vector3 a, Vector3 b, out Vector3 closest )
	{
		Vector3 ab = (b - a).WithZ( 0f );
		float lengthSq = ab.LengthSquared;
		if ( lengthSq < 0.0001f )
		{
			closest = b;
			return 1f;
		}

		float t = Vector3.Dot( (p - a).WithZ( 0f ), ab ) / lengthSq;
		closest = Vector3.Lerp( a, b, float.Clamp( t, 0f, 1f ) );
		return t;
	}
}
