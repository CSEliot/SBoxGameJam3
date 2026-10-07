// Project: sboxgamejam3
// File:    NavmeshCarController.cs
// Author:  cseliot
// Created: 2026.10.07.20.10.35
// Edited: 2026.10.07.20.03.35
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Handles smooth controlling of the car using sbox navmesh properties.
// 
// License:
// This code is provided "as is," without warranty of any kind, express or
// implied, including but not limited to the warranties of merchantability,
// fitness for a particular purpose, and noninfringement. In no event shall
// the authors or copyright holders be liable for any claim, damages, or
// other liability, whether in an action of contract, tort, or otherwise,
// arising from, out of, or in connection with the software or the use or
// other dealings in the software.

using System;
using System.Collections.Generic;
using Bugges.VehicleController;
using Sandbox.Navigation;

namespace Sandbox;

/// <summary>
/// Default AI driver for a bugge <see cref="VehicleController"/> car, strictly tied to the navmesh
/// path. The car drives to a target authored externally (the GameManager sets
/// <see cref="FollowTarget"/> when it spawns the car and retargets it as the run's target bar
/// changes) along a Catmull-Rom-smoothed navmesh route: the string-pulled
/// navmesh polyline is resampled into a tangent-continuous path so node-to-node turns are taken
/// smoothly instead of as hard corners. Pure-pursuit steering (with yaw damping) and a
/// curvature-based speed profile (corner speeds + braking-distance horizon) are authored against
/// that smoothed path, and all controls are pushed through <see cref="VehicleController"/>'s
/// UseExternalInput seam so the car keeps the library's full force-based realism.
///
/// Strictness is a spectrum: <see cref="PathStrictness"/> 0 (default) is pure steering input —
/// the car follows the path like any driven vehicle, with the library's anti-slip grip as the only
/// lateral authority. Higher values remove the lateral velocity component relative to the path
/// tangent every physics step (in PrePhysicsStep, the hook that runs after all wheel forces are
/// applied and before integration), pulling the car onto the path line; 1 is direction-strict.
/// Longitudinal dynamics (acceleration, braking, engine RPM) always stay the library's.
///
/// Runs only where the car is simulated: the host for a spawned car the host owns, or solo play.
/// On clients the car is a physics proxy and this component idles. Movement is navmesh-only: no
/// obstacle avoidance (that is AICarDriver's job) — a blocked car rebuilds its path after a
/// stuck timeout and otherwise holds.
/// </summary>
[Icon( "smart_toy" )]
public sealed class NavMeshCarController : Component, IScenePhysicsEvents
{
	public enum DriveState
	{
		/// <summary>No target or no usable path yet.</summary>
		Idle,
		/// <summary>Following the smoothed navmesh path to the target.</summary>
		Driving,
		/// <summary>Holding at the target; resumes automatically if the target moves away.</summary>
		Arrived
	}

	// ------------------------------------------------------------------ Target

	/// <summary>
	/// The destination to drive to, authored externally (the GameManager sets it when it
	/// spawns the car and retargets it whenever the target bar changes). When unset the car
	/// idles with the brakes on. The navmesh snaps the target to the nearest drivable point,
	/// so a bar sitting off the road mesh is still reachable (the car stops at the closest road).
	/// </summary>
	[Property, Group( "Target" )] public GameObject FollowTarget { get; set; }

	/// <summary>Flat distance to the target that counts as arrived; the car brakes and holds.</summary>
	[Property, Group( "Target" )] public float ArrivalDistance { get; set; } = 250f;

	/// <summary>
	/// Search box half-size used to snap the car and the target onto the navmesh.
	/// Too small and a car parked off the road mesh never finds a start point.
	/// </summary>
	[Property, Group( "Target" )] public float NavMeshSnapRadius { get; set; } = 500f;

	/// <summary>Raised once per arrival (fires again only after the target moves away past <see cref="ArrivalDistance"/>).</summary>
	public event Action OnArrived;

	// ------------------------------------------------------------------ Speed

	/// <summary>Target forward speed on straights (world units per second).</summary>
	[Property, Group( "Speed" )] public float CruiseSpeed { get; set; } = 800f;

	/// <summary>Target speed at a turn of <see cref="CornerFullAngle"/> degrees.</summary>
	[Property, Group( "Speed" )] public float CornerSpeed { get; set; } = 350f;

	/// <summary>
	/// Turn angle (degrees) at and beyond which <see cref="CornerSpeed"/> applies in full. Below
	/// it the limit eases back up toward Min(CruiseSpeed, CornerSpeed*4).
	/// </summary>
	[Property, Group( "Speed" )] public float CornerFullAngle { get; set; } = 45f;

	/// <summary>Target speed for turns of 120 degrees or more (hairpins). Clamped to never exceed <see cref="CornerSpeed"/>.</summary>
	[Property, Group( "Speed" )] public float SharpCornerSpeed { get; set; } = 200f;

	/// <summary>Speed error (units/s) that maps to full throttle. Lower = more aggressive throttle.</summary>
	[Property, Group( "Speed" )] public float ThrottleResponse { get; set; } = 250f;

	/// <summary>Brake when going this much faster than the target speed. Below it the car only coasts.</summary>
	[Property, Group( "Speed" )] public float BrakeMargin { get; set; } = 50f;

	/// <summary>
	/// Deceleration (units/s per second) assumed when braking ahead of a corner or the
	/// destination: the speed allowed at distance d before a corner is sqrt(cornerSpeed^2 + 2*this*d).
	/// </summary>
	[Property, Group( "Speed" )] public float CornerBrakeDecel { get; set; } = 300f;

	// ------------------------------------------------------------------ Path

	/// <summary>
	/// Resample the navmesh polyline into a Catmull-Rom spline so turns are taken smoothly.
	/// Disable to follow the raw string-pulled corners. NOTE: a Catmull-Rom spline passes through
	/// every corner but can bow OUTSIDE the polyline between them, so it can clip geometry the
	/// string-pulled path was hugging — the original corner angles still drive the speed limits,
	/// but the steering line is the spline.
	/// </summary>
	[Property, Group( "Path" )] public bool SmoothPath { get; set; } = true;

	/// <summary>Interpolated points generated between each pair of navmesh corners by the spline (higher = rounder path).</summary>
	[Property, Group( "Path" ), Range( 1, 16 )] public int SplineSubdivisions { get; set; } = 4;

	/// <summary>Path corners closer than this (flat, units) to the previous kept corner are merged away.</summary>
	[Property, Group( "Path" )] public float MinPathPointSpacing { get; set; } = 120f;

	/// <summary>Base distance ahead along the path the car steers toward (pure pursuit).</summary>
	[Property, Group( "Path" )] public float LookAheadDistance { get; set; } = 300f;

	/// <summary>Extra look-ahead per unit of speed (seconds). Higher = smoother, wider corners. 0 is also the hotload read and falls back to 0.3.</summary>
	[Property, Group( "Path" )] public float LookAheadTime { get; set; } = 0.3f;

	/// <summary>How far ahead along the path corners are checked to slow down for them.</summary>
	[Property, Group( "Path" )] public float CornerLookDistance { get; set; } = 800f;

	/// <summary>Flat distance from the path that triggers a re-path from the car's position.</summary>
	[Property, Group( "Path" )] public float RepathDistance { get; set; } = 500f;

	/// <summary>Minimum seconds between re-path attempts (each attempt is a navmesh query).</summary>
	[Property, Group( "Path" )] public float RepathInterval { get; set; } = 0.5f;

	// ------------------------------------------------------------------ Steering

	/// <summary>Heading error (degrees) that gets full steering lock.</summary>
	[Property, Group( "Steering" ), Range( 5f, 90f )] public float FullSteerAngle { get; set; } = 35f;

	/// <summary>
	/// How fast the steering input follows its target (per second, exponential). 0 or negative =
	/// instant (no smoothing). 0 is also what a hotloaded instance reads, so 0 falls back to 8.
	/// </summary>
	[Property, Group( "Steering" )] public float SteerResponse { get; set; } = 8f;

	/// <summary>Heading error (degrees) below which nothing is steered; subtracted from errors above it.</summary>
	[Property, Group( "Steering" )] public float SteerDeadband { get; set; } = 2f;

	/// <summary>
	/// Seconds of yaw rate subtracted from the heading error before it is steered (PD damping:
	/// a car already rotating toward the aim line gets a smaller correction, which kills the
	/// weave limit-cycle). Negative disables; 0 is also the hotload read and falls back to 0.3.
	/// </summary>
	[Property, Group( "Steering" )] public float SteerYawDamping { get; set; } = 0.3f;

	// ------------------------------------------------------------------ Strictness

	/// <summary>
	/// 0 (default) = pure steering input; the car follows the path like any driven vehicle and
	/// may run wide on corners or slide. 1 = the lateral velocity component relative to the path
	/// tangent is removed every physics step, pulling the car onto the path line (direction-
	/// strict; natural sliding is suppressed and the car can no longer drift). The longitudinal
	/// dynamics (acceleration, braking) stay the library's at every value.
	/// </summary>
	[Property, Group( "Strictness" ), Range( 0f, 1f )] public float PathStrictness { get; set; } = 0f;

	// ------------------------------------------------------------------ Debug

	/// <summary>Draw the smoothed path, the destination and the current state while playing.</summary>
	[Property, Group( "Debug" )] public bool DrawDebug { get; set; } = false;

	[Property, Group( "Debug" ), ReadOnly] public DriveState State { get; private set; } = DriveState.Idle;

	[Property, Group( "Debug" ), ReadOnly] public Vector3 Destination { get; private set; }

	// ------------------------------------------------------------------ Internals

	private VehicleController _vehicle;
	private Rigidbody _body;
	private GameObject _targetObject;
	private Vector3 _targetPosition;
	private bool _hasTarget;

	// The smoothed drive path (what the car steers along) and the merged raw navmesh corners
	// (what corner speeds are computed from, with their cumulative along-path distances).
	private readonly List<Vector3> _path = new();
	private readonly List<Vector3> _rawCorners = new();
	private readonly List<float> _rawCornerDistances = new();
	private int _nearestIndex;
	private int _rawNearestIndex;
	private GameObject _pathTargetObject;
	private Vector3 _pathTargetPosition;

	private float _steer;
	private TimeSince _sinceRepath = 10f;
	private TimeSince _sinceEngineRequest = 10f;
	private TimeSince _sinceStuckMoving;
	private bool _warnedNoNavMesh;
	private bool _warnedNoNavStart;

	// ------------------------------------------------------------------ Lifecycle

	protected override void OnStart()
	{
		_vehicle = GetComponent<VehicleController>();
		_body = GetComponent<Rigidbody>();

		if ( !_vehicle.IsValid() || !_body.IsValid() )
		{
			Log.Error( $"NavMeshCarController on '{GameObject.Name}': needs a VehicleController and a Rigidbody on the same GameObject. Disabling." );
			Enabled = false;
			return;
		}

		// Proxies never simulate this car; the prefab's serialized UseExternalInput keeps their
		// VehicleController off the local keyboard.
		if ( IsProxy ) return;

		_vehicle.UseExternalInput = true;
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

	protected override void OnFixedUpdate()
	{
		// Host (or solo) only: scene-placed cars are unowned, so the host simulates them.
		if ( IsProxy ) return;
		if ( !_vehicle.IsValid() || !_body.IsValid() ) return;

		// A sleeping body never wakes on its own (wheels ungrounded => no force => nothing wakes
		// it). Keep the driven car awake.
		if ( _body.Sleeping ) _body.Sleeping = false;

		Vector3 position = WorldPosition;

		ResolveTargetHelper();

		if ( !_hasTarget )
		{
			_path.Clear();
			ApplyControlsHelper( 0f, 0f, true );
			SetStateHelper( DriveState.Idle );
			return;
		}

		EnsureEngineRunningHelper();

		float forwardSpeed = Vector3.Dot( _body.Velocity, WorldRotation.Forward );

		// Arrived: hold. Resume automatically if the target moves away past ArrivalDistance.
		if ( FlatDistance( position, _targetPosition ) < EffectiveArrivalDistance )
		{
			ApplyControlsHelper( 0f, 0f, true );
			if ( State != DriveState.Arrived )
			{
				SetStateHelper( DriveState.Arrived );
				OnArrived?.Invoke();
			}
			return;
		}

		if ( State != DriveState.Driving )
		{
			bool wasArrived = State == DriveState.Arrived;
			SetStateHelper( DriveState.Driving );
			if ( wasArrived )
				_path.Clear(); // resuming after arrival: rebuild the route to the target's current position
		}

		// Path maintenance: rebuild when missing, when the target changed, or when off-path.
		if ( !EnsurePathHelper( position, out float distanceFromPath ) )
			return;

		if ( distanceFromPath > EffectiveRepathDistance && _sinceRepath > EffectiveRepathInterval )
		{
			_sinceRepath = 0f;
			BuildPathHelper( position );
		}

		// Steering: pure pursuit on the smoothed path, look-ahead scaled by speed.
		Vector3 lookAhead = GetPathPointAtDistanceHelper( position,
			EffectiveLookAheadDistance + MathF.Max( forwardSpeed, 0f ) * EffectiveLookAheadTime );
		float pathAngle = GetPathAngleHelper( position, lookAhead );
		float steer = SteerFromAngleHelper( DampedSteerAngleHelper( pathAngle, forwardSpeed ) );

		// Speed: cruise, heading error, corners ahead (braking horizon), destination.
		float targetSpeed = EffectiveCruiseSpeed;
		targetSpeed = MathF.Min( targetSpeed, CornerSpeedForAngleHelper( pathAngle ) );
		targetSpeed = MathF.Min( targetSpeed, GetCornerSpeedLimitHelper( position ) );

		float throttle;
		bool brake;
		if ( forwardSpeed < -1f )
		{
			throttle = 0f; // rolling backward: stop first, then drive forward
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
			brake = false;
		}

		// Minimal stuck guard: full throttle but no forward progress means the car grinds on
		// something the navmesh path did not account for (a car is wider than the agent radius).
		// Rebuild the path after a timeout; full stuck recovery is AICarDriver's job.
		if ( throttle > 0.05f && MathF.Abs( forwardSpeed ) < 10f )
		{
			if ( _sinceStuckMoving > 4f )
			{
				_sinceStuckMoving = 0f;
				_path.Clear();
				_sinceRepath = 0f;
			}
		}
		else
		{
			_sinceStuckMoving = 0f;
		}

		ApplyControlsHelper( throttle, steer, brake );
	}

	protected override void OnUpdate()
	{
		if ( !DrawDebug || IsProxy ) return;
		DrawDebugHelper();
	}

	/// <summary>
	/// Path strictness (see <see cref="PathStrictness"/>): runs after the whole OnFixedUpdate
	/// loop — so after VehicleController's wheel forces for this tick are accumulated — and
	/// before the physics integration step. Removes the lateral velocity component relative to
	/// the smoothed path tangent. No forces are cleared and gravity is untouched, so the wheel
	/// forces still add their per-tick contribution on top; that residual is what keeps the car
	/// physical at high strictness instead of a teleporting train.
	/// </summary>
	void IScenePhysicsEvents.PrePhysicsStep()
	{
		if ( IsProxy ) return;
		if ( PathStrictness <= 0f ) return;
		if ( !_body.IsValid() || _path.Count < 2 || State != DriveState.Driving ) return;

		Vector3 velocity = _body.Velocity;
		if ( Vector3.Dot( velocity, WorldRotation.Forward ) <= 0f ) return;

		Vector3 tangent = TangentAtNearestHelper();
		if ( tangent.LengthSquared < 0.0001f ) return;

		Vector3 flat = velocity.WithZ( 0f );
		float along = Vector3.Dot( flat, tangent );
		Vector3 lateral = flat - tangent * along;
		_body.Velocity = new Vector3(
			velocity.x - lateral.x * PathStrictness,
			velocity.y - lateral.y * PathStrictness,
			velocity.z );
	}

	// ================================================================== Target

	private void ResolveTargetHelper()
	{
		// The target is authored externally (the GameManager sets FollowTarget at spawn and
		// retargets it on bar changes); the car itself never searches for one.
		_targetObject = FollowTarget.IsValid() ? FollowTarget : null;

		_hasTarget = _targetObject.IsValid();
		if ( _hasTarget )
			_targetPosition = _targetObject.WorldPosition;
	}

	private void SetStateHelper( DriveState state )
	{
		State = state;
		_sinceStuckMoving = 0f;
	}

	// ================================================================== Navmesh path

	/// <summary>
	/// Rebuilds the path when needed: empty, target changed (object switched or moved past the
	/// re-path distance), or stale. Sets <see cref="_nearestIndex"/> and
	/// <see cref="_rawNearestIndex"/> from the car position and returns false only when there is
	/// nothing to drive on (no navmesh or no start point), leaving the brakes applied.
	/// </summary>
	private bool EnsurePathHelper( Vector3 position, out float distanceFromPath )
	{
		// A target that moved more than the arrival radius while being driven to needs a fresh
		// route; the drift rebuild is rate-limited (each build is a navmesh query).
		if ( _path.Count < 2
			|| _pathTargetObject != _targetObject
			|| (FlatDistance( _pathTargetPosition, _targetPosition ) > MathF.Min( EffectiveArrivalDistance, EffectiveRepathDistance )
				&& _sinceRepath > EffectiveRepathInterval) )
		{
			_sinceRepath = 0f;
			if ( !BuildPathHelper( position ) )
			{
				distanceFromPath = 0f;
				ApplyControlsHelper( 0f, 0f, true );
				return false;
			}
		}

		FindNearestSegmentHelper( position, _path, ref _nearestIndex, out distanceFromPath );
		FindNearestSegmentHelper( position, _rawCorners, ref _rawNearestIndex, out _ );
		return true;
	}

	/// <summary>
	/// Queries a Complete navmesh route to the target, merges sub-spacing corners, and resamples
	/// it into the smoothed drive path (see <see cref="SmoothPath"/>). The raw merged corners and
	/// their cumulative distances are kept for corner speed limiting. False = no route right now
	/// (navmesh still loading, target unreachable, path not Complete); the car holds and the
	/// caller retries on the next trigger.
	/// </summary>
	private bool BuildPathHelper( Vector3 position )
	{
		var nav = Scene.NavMesh;
		if ( nav is null || !nav.IsEnabled )
		{
			if ( !_warnedNoNavMesh )
			{
				_warnedNoNavMesh = true;
				Log.Warning( $"NavMeshCarController on '{GameObject.Name}': scene has no enabled navmesh. Holding." );
			}
			return false;
		}

		Vector3? start = nav.GetClosestPoint( position, EffectiveNavMeshSnapRadius );
		if ( !start.HasValue )
		{
			if ( !_warnedNoNavStart )
			{
				_warnedNoNavStart = true;
				Log.Warning( $"NavMeshCarController on '{GameObject.Name}': no navmesh within {EffectiveNavMeshSnapRadius} units of the car (still loading, not baked, or car off the road mesh)." );
			}
			return false;
		}

		Vector3? end = nav.GetClosestPoint( _targetPosition, EffectiveNavMeshSnapRadius );
		if ( !end.HasValue )
			return false;

		NavMeshPath route = nav.CalculatePath( new CalculatePathRequest { Start = start.Value, Target = end.Value } );
		if ( route.Status != NavMeshPathStatus.Complete || route.Points is null || route.Points.Count < 2 )
			return false;

		_rawCorners.Clear();
		foreach ( var point in route.Points )
		{
			if ( _rawCorners.Count > 0 && FlatDistance( _rawCorners[^1], point.Position ) < EffectiveMinPathPointSpacing )
				continue;
			_rawCorners.Add( point.Position );
		}

		// Whole route merged away: too short to drive (the arrival check owns that case).
		if ( _rawCorners.Count < 2 )
			return false;

		_path.Clear();
		if ( EffectiveSmoothPath && _rawCorners.Count >= 3 )
			_path.AddRange( _rawCorners.CatmullRomSpline( EffectiveSplineSubdivisions ) );
		else
			_path.AddRange( _rawCorners );

		_rawCornerDistances.Clear();
		float cumulative = 0f;
		for ( int i = 0; i < _rawCorners.Count; i++ )
		{
			if ( i > 0 )
				cumulative += FlatDistance( _rawCorners[i - 1], _rawCorners[i] );
			_rawCornerDistances.Add( cumulative );
		}

		_pathTargetObject = _targetObject;
		_pathTargetPosition = _targetPosition;
		_warnedNoNavMesh = false;
		_warnedNoNavStart = false;
		Destination = _targetPosition;
		_nearestIndex = 0;
		_rawNearestIndex = 0;
		return true;
	}

	// ================================================================== Steering

	/// <summary>Heading error (degrees, + = left) from the car to <paramref name="lookAhead"/>.</summary>
	private float GetPathAngleHelper( Vector3 position, Vector3 lookAhead )
	{
		Vector3 flat = (lookAhead - position).WithZ( 0f );
		if ( flat.LengthSquared < 0.01f ) return 0f;

		Vector3 local = WorldRotation.Inverse * flat;
		return MathF.Atan2( local.y, local.x ).RadianToDegree();
	}

	/// <summary>Point on the smoothed path at <paramref name="distance"/> units past the car's projection.</summary>
	private Vector3 GetPathPointAtDistanceHelper( Vector3 position, float distance )
	{
		if ( _path.Count < 2 ) return position;

		ProjectFlatHelper( position, _path[_nearestIndex], _path[_nearestIndex + 1], out Vector3 current );
		float remaining = distance;
		for ( int i = _nearestIndex + 1; i < _path.Count; i++ )
		{
			Vector3 next = _path[i];
			float segment = FlatDistance( current, next );
			if ( segment >= remaining || i == _path.Count - 1 )
				return segment <= 0.001f ? next : Vector3.Lerp( current, next, float.Clamp( remaining / segment, 0f, 1f ) );
			remaining -= segment;
			current = next;
		}

		return _path[^1];
	}

	/// <summary>Flat tangent of the smoothed path segment nearest the car.</summary>
	private Vector3 TangentAtNearestHelper()
	{
		int i = _nearestIndex;
		if ( i >= _path.Count - 1 ) i = _path.Count - 2;
		Vector3 tangent = (_path[i + 1] - _path[i]).WithZ( 0f );
		float length = tangent.Length;
		return length < 0.0001f ? Vector3.Zero : tangent / length;
	}

	// ================================================================== Speed

	/// <summary>
	/// Corner speed for a turn of this many degrees: a 0 degree bend allows Min(CruiseSpeed,
	/// CornerSpeed*4), easing out to <see cref="CornerSpeed"/> at <see cref="CornerFullAngle"/>,
	/// then down to <see cref="SharpCornerSpeed"/> at 120 degrees. Callers take the Min with their
	/// current target so corners only ever limit, never raise the speed.
	/// </summary>
	private float CornerSpeedForAngleHelper( float angleDegrees )
	{
		float a = MathF.Abs( angleDegrees );
		float full = EffectiveCornerFullAngle;
		float sharp = EffectiveSharpCornerSpeed;

		if ( a <= full )
		{
			float t = a / full;
			return Lerp( MathF.Min( EffectiveCruiseSpeed, EffectiveCornerSpeed * 4f ), EffectiveCornerSpeed, t * (2f - t) );
		}

		return Lerp( EffectiveCornerSpeed, sharp, (a - full) / (120f - full) );
	}

	/// <summary>
	/// Speed limit from the raw navmesh corners ahead (and the destination, treated as a 90
	/// degree corner): each corner's speed comes from <see cref="CornerSpeedForAngleHelper"/>,
	/// and the limit now is sqrt(cornerSpeed^2 + 2 * CornerBrakeDecel * d), so braking starts
	/// early enough instead of when the corner is already within <see cref="CornerLookDistance"/>.
	/// The horizon uses the slowest speed the curve can produce so a far hairpin is still braked
	/// for early. Corner angles come from the RAW polyline: the smoothed path has near-zero turn
	/// angles by design, so measuring curvature there would hide the real corners. NOTE: the
	/// car's distance-along-path is also measured on the raw polyline, and the smoothed path cuts
	/// corners, so near a corner the car sits slightly ahead of its raw projection and the horizon
	/// reads a little longer than the real remaining distance — the heading-error speed term
	/// covers the shortfall.
	/// </summary>
	private float GetCornerSpeedLimitHelper( Vector3 position )
	{
		if ( _rawCorners.Count < 2 || _rawCornerDistances.Count != _rawCorners.Count ) return EffectiveCruiseSpeed;

		float decel = MathF.Max( EffectiveCornerBrakeDecel, 1f );
		float limit = EffectiveCruiseSpeed;
		float sharp = EffectiveSharpCornerSpeed;
		float horizon = (limit * limit - sharp * sharp) / (2f * decel) + EffectiveCornerLookDistance;
		horizon = MathF.Max( horizon, EffectiveCornerLookDistance );

		// Distance the car has travelled along the raw polyline (projection onto its nearest segment).
		ProjectFlatHelper( position, _rawCorners[_rawNearestIndex], _rawCorners[_rawNearestIndex + 1], out Vector3 projection );
		float carDistance = _rawCornerDistances[_rawNearestIndex] + FlatDistance( _rawCorners[_rawNearestIndex], projection );

		for ( int i = _rawNearestIndex + 1; i < _rawCorners.Count; i++ )
		{
			float travelled = _rawCornerDistances[i] - carDistance;
			if ( travelled < 0f ) continue;
			if ( travelled > horizon ) break;

			float angle = 90f; // destination: full corner
			if ( i < _rawCorners.Count - 1 )
			{
				Vector3 inDir = (_rawCorners[i] - _rawCorners[i - 1]).WithZ( 0f );
				Vector3 outDir = (_rawCorners[i + 1] - _rawCorners[i]).WithZ( 0f );
				if ( inDir.LengthSquared < 0.0001f || outDir.LengthSquared < 0.0001f ) continue;
				angle = MathF.Acos( float.Clamp( Vector3.Dot( inDir.Normal, outDir.Normal ), -1f, 1f ) ).RadianToDegree();
			}

			float cornerSpeed = CornerSpeedForAngleHelper( angle );
			float allowed = travelled < EffectiveCornerLookDistance
				? cornerSpeed
				: MathF.Sqrt( cornerSpeed * cornerSpeed + 2f * decel * (travelled - EffectiveCornerLookDistance) );
			limit = MathF.Min( limit, allowed );
		}

		return limit;
	}

	// ================================================================== Controls

	/// <summary>
	/// Heading error (degrees, + = left) with yaw damping and the steer deadband applied before
	/// it becomes steering. Yaw damping subtracts <see cref="SteerYawDamping"/> seconds of the
	/// body's yaw rate (a car already rotating toward the aim line gets a smaller correction;
	/// this is the D of a PD controller and kills the weave limit-cycle). The deadband then eats
	/// <see cref="SteerDeadband"/> degrees off what is left, so small errors steer nothing.
	/// Below a crawl threshold the car's yaw rate is not line-following, so the angle is untouched.
	/// </summary>
	private float DampedSteerAngleHelper( float angle, float forwardSpeed )
	{
		if ( forwardSpeed <= 10f ) return angle;

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
		float fraction = float.Clamp( angle / MathF.Max( EffectiveFullSteerAngle, 1f ), -1f, 1f );
		return MathF.Sign( fraction ) * MathF.Sqrt( MathF.Abs( fraction ) );
	}

	private void ApplyControlsHelper( float throttle, float steer, bool brake )
	{
		// <= 0 = instant steering (no smoothing). Effective* maps a hotload 0 back to the default
		// 8, so only an authored negative value reaches the instant branch.
		float blend = EffectiveSteerResponse <= 0f ? 1f : 1f - MathF.Exp( -EffectiveSteerResponse * Time.Delta );
		_steer += (steer - _steer) * blend;

		_vehicle.UseExternalInput = true;
		_vehicle.ExternalThrottle = throttle;
		_vehicle.ExternalSteer = _steer;
		_vehicle.ExternalBrake = brake;
	}

	/// <summary>
	/// The library only starts its engine from a one-shot request. Re-request every 2 s while it
	/// is off (start takes Engine_StartDelay). The library ignores a request while the engine is
	/// on, so a late one cannot switch it off.
	/// </summary>
	private void EnsureEngineRunningHelper()
	{
		if ( _vehicle.IsEngineOn ) return;
		if ( _sinceEngineRequest < 2f ) return;

		_vehicle.RequestEngineStart();
		_sinceEngineRequest = 0f;
	}

	// ================================================================== Geometry helpers

	/// <summary>Flat (X/Y) distance between two points.</summary>
	private static float FlatDistance( Vector3 a, Vector3 b ) => (b - a).WithZ( 0f ).Length;

	/// <summary>Clamped linear interpolation (same helper AICarDriver carries; not an engine global).</summary>
	private static float Lerp( float a, float b, float t ) => a + (b - a) * float.Clamp( t, 0f, 1f );

	/// <summary>Flat point-to-segment distance (perp distance inside the span, endpoint distance outside).</summary>
	private static float FlatDistanceToSegmentHelper( Vector3 p, Vector3 a, Vector3 b )
	{
		Vector3 ab = (b - a).WithZ( 0f );
		float lengthSq = ab.LengthSquared;
		if ( lengthSq < 0.0001f )
			return (p - b).WithZ( 0f ).Length;

		float t = float.Clamp( Vector3.Dot( (p - a).WithZ( 0f ), ab ) / lengthSq, 0f, 1f );
		return (p - (a + ab * t)).WithZ( 0f ).Length;
	}

	/// <summary>Projects <paramref name="p"/> onto the flat segment a-b (clamped to the span).</summary>
	private static void ProjectFlatHelper( Vector3 p, Vector3 a, Vector3 b, out Vector3 projection )
	{
		Vector3 ab = (b - a).WithZ( 0f );
		float lengthSq = ab.LengthSquared;
		if ( lengthSq < 0.0001f )
		{
			projection = a;
			return;
		}

		float t = float.Clamp( Vector3.Dot( (p - a).WithZ( 0f ), ab ) / lengthSq, 0f, 1f );
		projection = a + ab * t;
	}

	/// <summary>
	/// Finds the segment of <paramref name="path"/> nearest to <paramref name="position"/> (flat),
	/// scanning FORWARD from the previous index only (monotonic). A global scan would let a
	/// parallel or returning leg of the path (adjacent street, switchback) steal the nearest slot
	/// and yank the look-ahead point and tangent onto the wrong leg. Every path rebuild resets
	/// the index, and the car only ever moves forward along the path, so the monotonic scan is
	/// safe; a shove backward reads as off-path distance and triggers a re-path instead.
	/// </summary>
	private static void FindNearestSegmentHelper( Vector3 position, IReadOnlyList<Vector3> path, ref int index, out float distanceFromPath )
	{
		distanceFromPath = float.MaxValue;
		if ( path.Count < 2 ) return;

		if ( index >= path.Count - 1 ) index = path.Count - 2;

		for ( int i = index; i < path.Count - 1; i++ )
		{
			float d = FlatDistanceToSegmentHelper( position, path[i], path[i + 1] );
			if ( d < distanceFromPath )
			{
				distanceFromPath = d;
				index = i;
			}
		}
	}

	// ================================================================== Debug

	private void DrawDebugHelper()
	{
		if ( _path.Count >= 2 )
		{
			DebugOverlay.Line( _path, Color.Cyan );
			if ( _hasTarget )
				DebugOverlay.Sphere( new Sphere( _targetPosition, 40f ), Color.Magenta );
		}

		DebugOverlay.Text( WorldPosition + Vector3.Up * 120f, $"{State}  strict {PathStrictness:0.00}", 16f );
	}

	// ================================================================== Hotload fallbacks

	// The user hotloads this component mid-play while tuning; a property added to the assembly
	// after the instance was created reads 0 (default(T)) until the instance is recreated, so
	// every critical tunable falls back to its shipping default on a 0 read.

	private float EffectiveArrivalDistance => ArrivalDistance > 0f ? ArrivalDistance : 250f;
	private float EffectiveNavMeshSnapRadius => NavMeshSnapRadius > 0f ? NavMeshSnapRadius : 500f;
	private float EffectiveCruiseSpeed => CruiseSpeed > 0f ? CruiseSpeed : 800f;
	private float EffectiveCornerSpeed => CornerSpeed > 0f ? CornerSpeed : 350f;
	private float EffectiveCornerFullAngle => CornerFullAngle > 0f ? CornerFullAngle : 45f;
	private float EffectiveSharpCornerSpeed => MathF.Min( SharpCornerSpeed > 1f ? SharpCornerSpeed : 200f, EffectiveCornerSpeed );
	private float EffectiveThrottleResponse => MathF.Max( ThrottleResponse, 1f );
	private float EffectiveCornerBrakeDecel => CornerBrakeDecel > 0f ? CornerBrakeDecel : 300f;
	private float EffectiveLookAheadDistance => LookAheadDistance > 0f ? LookAheadDistance : 300f;
	private float EffectiveLookAheadTime => LookAheadTime != 0f ? LookAheadTime : 0.3f;
	private float EffectiveCornerLookDistance => CornerLookDistance > 0f ? CornerLookDistance : 800f;
	private float EffectiveRepathDistance => RepathDistance > 0f ? RepathDistance : 500f;
	private float EffectiveRepathInterval => RepathInterval > 0f ? RepathInterval : 0.5f;
	private float EffectiveFullSteerAngle => FullSteerAngle > 0f ? FullSteerAngle : 35f;
	private float EffectiveSteerResponse => SteerResponse != 0f ? SteerResponse : 8f;
	private float EffectiveSteerDeadband => SteerDeadband > 0f ? SteerDeadband : 2f;
	private float EffectiveSteerYawDamping => SteerYawDamping != 0f ? SteerYawDamping : 0.3f;
	private bool EffectiveSmoothPath => SmoothPath; // the spline needs >= 3 corners, guarded at the call site
	private int EffectiveSplineSubdivisions => SplineSubdivisions > 0 ? SplineSubdivisions : 4;
	private float EffectiveMinPathPointSpacing => MinPathPointSpacing > 0f ? MinPathPointSpacing : 120f;
}
