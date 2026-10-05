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

	// ------------------------------------------------------------------ Speed

	/// <summary>Target forward speed on straights (world units per second).</summary>
	[Property, Group( "Speed" )] public float CruiseSpeed { get; set; } = 800f;

	/// <summary>Target speed for a 90 degree (or sharper) corner, the final approach, or a large heading error.</summary>
	[Property, Group( "Speed" )] public float CornerSpeed { get; set; } = 350f;

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
	/// Back-up attempts without reaching a new path corner before dropping the destination for one
	/// whose route starts more than 60 degrees away from the blocked heading. 0 = never.
	/// </summary>
	[Property, Group( "Reversing" )] public int RepickAfterReverses { get; set; } = 3;

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

	// Hidden, never-enabled agents that only carry area filters into NavMesh.CalculatePath.
	private GameObject _filterObject;
	private NavMeshAgent _destinationFilter;
	private NavMeshAgent _routeFilter;

	private float _steer;
	private TimeSince _sinceStuckCheckOk;
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

	// Last control decisions, kept for telemetry.
	private float _lastThrottle;
	private bool _lastBrake;
	private float _lastTargetSpeed;
	private float _lastPathAngle;
	private float _lastAvoidAngle;
	private float _lastDistanceFromPath;
	// Name of the object the last CastWhiskerHelper call hit ("" on a miss), and per front whisker.
	private string _lastCastHitName = "";
	private string[] _frontHitName;

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
		float pathAngle = GetPathAngleHelper( position, forwardSpeed );

		// --- Whisker clearance. Probed before the path-behind branch below so the pivot escape
		// can read forwardClear; same values as before (the whiskers were cast earlier this tick).
		GetFrontClearanceHelper( out float forwardClear, out float forwardLength, out float leftClear, out float rightClear );

		// --- Path is behind the car (new leg pointing back, overshot corner): a forward full-lock
		// turn needs 250-500 units of radius and drags the car through whatever is beside the road.
		// Stop and back around instead (three-point turn); TickReversingHelper ends it once the
		// nose points along the path. Only from (near) standstill: reversing out of speed swings
		// the car (v2: two cars backed off a bridge doing this at 680 u/s); faster, it brakes first.
		bool pathBehind = UTurnAngle > 0f && MathF.Abs( pathAngle ) > UTurnAngle;
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
				EnterTurnReverseHelper( pathAngle, leftClear, rightClear );
				return;
			}
		}

		// --- Whisker avoidance.
		float avoidAngle = GetAvoidAngleHelper( pathAngle, leftClear, rightClear );
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

		// Heading error now: a car pointing well away from its path slows to corner speed at 90 degrees.
		targetSpeed = MathF.Min( targetSpeed, Lerp( CruiseSpeed, CornerSpeed, MathF.Abs( pathAngle ) / 90f ) );

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

		// Path behind and not pivoting: brake to a stop in a straight line, then back around.
		// A forward pivot (v5) crawls around the blockage at creep speed instead.
		if ( pathBehind )
			targetSpeed = pivotForward ? EffectiveCreepSpeed : 0f;

		_lastTargetSpeed = targetSpeed;

		float throttle;
		bool brake = false;
		if ( forwardSpeed > targetSpeed + BrakeMargin )
		{
			throttle = 0f;
			brake = true;
		}
		else
		{
			throttle = float.Clamp( (targetSpeed - forwardSpeed) / EffectiveThrottleResponse, 0f, 1f );
		}

		float steerTarget;
		if ( pivotForward )
			steerTarget = pathAngle > 0f ? 1f : -1f; // full lock toward the path side (ExternalSteer + = left)
		else if ( pathBehind )
			steerTarget = 0f;
		else
			steerTarget = SteerFromAngleHelper( pathAngle + avoidAngle );

		ApplyControlsHelper( throttle, steerTarget, brake );

		// --- Stuck: throttling but not moving (pinned against something the whiskers miss).
		bool tryingToMove = throttle > 0.2f && _vehicle.IsEngineOn;
		if ( !tryingToMove || MathF.Abs( forwardSpeed ) > StuckSpeed )
			_sinceStuckCheckOk = 0f;
		else if ( _sinceStuckCheckOk > EffectiveStuckTime )
		{
			_stuckTrigger = true;
			EnterReverseHelper( pathAngle, leftClear, rightClear );
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

		float pathAngle = _path.Count >= 2 ? GetPathAngleHelper( position, 0f ) : 0f;
		EnterReverseHelper( pathAngle, leftClear, rightClear );
	}

	private void EnterBlockedHelper()
	{
		_blockedWaitFactor = _random.Float( 0.75f, 1.25f );
		_sinceBlockedObstacle = 0f;
		SetStateHelper( DriveState.Blocked );
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
		bool rearBlocked = rearClear < RearStopDistance;

		GetFrontClearanceHelper( out float forwardClear, out _, out _, out _ );
		bool frontClear = forwardClear > EffectiveStopDistance * 2f;

		// Done once the nose points roughly along the path again (a reverse that ends while still
		// facing away just drives back into the same spot), or the rear is blocked, or time is up.
		float pathAngleNow = _path.Count >= 2 ? GetPathAngleHelper( position, 0f ) : 0f;
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
		// so steer opposite to where the nose should end up.
		_reverseSteer = -noseSide;
		// Snap the wheels to the reverse lock. Smoothing from the driving lock would spend the start
		// of the reverse with the wheels on the wrong side.
		_steer = _reverseSteer;
		_blockedHeading = FlatForwardHelper();
		_reverseCount++;
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
		_reverseCount++;
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

	private float EffectiveCreepSpeed => CreepSpeed * Lerp( 1f, AggressiveDriveScale, Aggression );

	private float EffectiveThrottleResponse => MathF.Max( ThrottleResponse, 1f ) / Lerp( 1f, AggressiveDriveScale, Aggression );

	private float EffectiveReverseThrottle => Lerp( ReverseThrottle, 1f, Aggression );

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
			if ( !TryFindDestinationHelper( nav, point.Value, null, out NavMeshPath path, out Vector3 target ) ) continue;

			Vector3 heading = GetFirstLegHelper( path );
			if ( !IsSpawnClearHelper( point.Value, heading ) ) continue;

			TelemetryTeleportHelper( reason, point.Value );

			WorldPosition = point.Value + Vector3.Up * TeleportDropHeight;
			WorldRotation = Rotation.LookAt( heading, Vector3.Up );
			_body.Velocity = Vector3.Zero;
			_body.AngularVelocity = Vector3.Zero;
			Transform.ClearInterpolation();

			_steer = 0f;
			ApplyControlsHelper( 0f, 0f, false );
			_reverseCount = 0;
			_pivotFails = 0;
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
	/// navmesh) and returns the first one that sits in an allowed destination area and that the
	/// navmesh can reach completely. Partial paths mean the candidate sits on another navmesh island.
	/// With <paramref name="awayFrom"/> set, a route whose first leg points within 60 degrees of it
	/// is only used if no other complete route turns up.
	/// </summary>
	private bool TryFindDestinationHelper( NavMesh nav, Vector3 start, Vector3? awayFrom, out NavMeshPath foundPath, out Vector3 foundTarget )
	{
		foundPath = default;
		foundTarget = default;

		float minDistance = MathF.Max( 0f, MathF.Min( MinDestinationDistance, MaxDestinationDistance ) );
		float maxDistance = MathF.Max( MinDestinationDistance, MaxDestinationDistance );
		int attempts = Math.Max( 1, DestinationPickAttempts );
		bool haveFallback = false;

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

			if ( awayFrom.HasValue && Vector3.Dot( GetFirstLegHelper( path ), awayFrom.Value ) > 0.5f )
			{
				if ( !haveFallback )
				{
					haveFallback = true;
					foundPath = path;
					foundTarget = target.Value;
				}
				continue;
			}

			foundPath = path;
			foundTarget = target.Value;
			return true;
		}

		return haveFallback;
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

		WidenCornersHelper();

		// Points[0] is the snapped start; drive toward the first corner after it.
		_waypointIndex = 1;
		_sinceRepath = 0f;
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
			_reverseCount = 0; // progress made
			_pivotFails = 0;
		}

		ProjectFlat( position, _path[_waypointIndex - 1], _path[_waypointIndex], out Vector3 closest );
		distanceFromPath = FlatDistance( position, closest );
	}

	/// <summary>Signed yaw (degrees, + = left) from the car's heading to the path look-ahead point.</summary>
	private float GetPathAngleHelper( Vector3 position, float forwardSpeed )
	{
		float lookAhead = LookAheadDistance + MathF.Max( forwardSpeed, 0f ) * LookAheadTime;
		return LocalYawAngleTo( GetLookAheadPointHelper( position, lookAhead ) );
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
	/// Speed limit from the corners ahead (and the destination, treated as a full corner): for each
	/// corner at path distance d with turn angle a, the corner speed is Lerp(Cruise, Corner, a/90)
	/// and the limit now is sqrt(cornerSpeed^2 + 2 * CornerBrakeDecel * d), so braking starts early
	/// enough instead of when the corner is already within <see cref="CornerLookDistance"/>.
	/// Corners within CornerLookDistance also cap the speed directly (the old rule), so a corner the
	/// car is already inside of stays slow until it is past it.
	/// </summary>
	private float GetCornerSpeedLimitHelper( Vector3 position )
	{
		float decel = MathF.Max( CornerBrakeDecel, 1f );
		float limit = CruiseSpeed;
		// Beyond this distance no corner can lower the limit below cruise.
		float horizon = (CruiseSpeed * CruiseSpeed - CornerSpeed * CornerSpeed) / (2f * decel) + CornerLookDistance;

		ProjectFlat( position, _path[_waypointIndex - 1], _path[_waypointIndex], out Vector3 current );
		float travelled = FlatDistance( current, _path[_waypointIndex] );

		for ( int i = _waypointIndex; i < _path.Count && travelled < horizon; i++ )
		{
			float angle = 90f; // destination: arrive at corner speed
			if ( i < _path.Count - 1 )
			{
				Vector3 inDir = (_path[i] - _path[i - 1]).WithZ( 0f ).Normal;
				Vector3 outDir = (_path[i + 1] - _path[i]).WithZ( 0f ).Normal;
				angle = MathF.Acos( float.Clamp( Vector3.Dot( inDir, outDir ), -1f, 1f ) ).RadianToDegree();
			}

			float cornerSpeed = Lerp( CruiseSpeed, CornerSpeed, angle / 90f );
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
		_ledgeAhead = LedgeProbeHelper( _hullCenter.x + _hullSize.x * 0.5f, 1f, length );

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
		return MathF.Min( nearest, LedgeProbeHelper( _hullCenter.x - _hullSize.x * 0.5f, -1f, RearProbeLength ) );
	}

	/// <summary>
	/// Ground check ahead of (<paramref name="direction"/> +1) or behind (-1) the car: drops a ray at
	/// 3 points out to <paramref name="length"/> past the bumper at local x <paramref name="edgeX"/>
	/// and returns the distance to the first point with no ground (a bridge edge, quay or drop into
	/// the sea), or <paramref name="length"/> if all have ground. Whiskers run horizontally at
	/// WhiskerHeight, so they never see a missing floor; v1/v2 telemetry lost cars this way
	/// (forward off quay edges, backward off a bridge). The allowed drop grows with distance so a
	/// downhill road far ahead does not read as a ledge.
	/// </summary>
	private static readonly float[] LedgeProbeFractions = { 0.1f, 0.45f, 0.85f };

	private float LedgeProbeHelper( float edgeX, float direction, float length )
	{
		if ( LedgeDropHeight <= 0f || length <= 0f ) return length;

		foreach ( float f in LedgeProbeFractions )
		{
			float d = MathF.Max( 30f, length * f );
			Vector3 local = new( edgeX + direction * d, 0f, WhiskerHeight );
			Vector3 start = WorldTransform.PointToWorld( local );
			float depth = WhiskerHeight + LedgeDropHeight + d * 0.6f;

			SceneTraceResult result = Scene.Trace.Ray( start, start + Vector3.Down * depth )
				.IgnoreGameObjectHierarchy( GameObject )
				.IgnoreDynamic()
				.Run();

			if ( !result.Hit && !result.StartedSolid ) return d;
		}

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
