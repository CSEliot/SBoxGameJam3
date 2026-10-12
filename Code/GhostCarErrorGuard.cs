// Project: sboxgamejam3
// File:    GhostCarErrorGuard.cs
// Author:  cseliot
// Created: 2026.10.12.10.00.00
// Edited: 2026.10.12.10.00.00
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Shared error-state guard for a ghost car. Detects and recovers from the
// failure modes both drivers hit (stuck, on its roof or side, off the map
// and in water) so the navmesh driver and the chase driver behave the same.
// Self-recovers first (reverse out, flip upright) and teleports to the
// nearest navmesh point on the car's route once that keeps failing, or at
// once when the car is off the map or in the water.
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

namespace Sandbox;

/// <summary>Outcome of one <see cref="GhostCarErrorGuard.Tick"/> call.</summary>
public enum GhostCarGuardResult
{
	/// <summary>Nothing wrong; the driver applies its own controls.</summary>
	None,
	/// <summary>A self-recovery window is running; the driver must apply <see cref="GhostCarErrorGuard.Throttle"/>/<see cref="GhostCarErrorGuard.Steer"/>/<see cref="GhostCarErrorGuard.Brake"/> this tick instead of its own.</summary>
	Recovering,
	/// <summary>The car was teleported; the driver must reset its path/state and re-route from the new position.</summary>
	Teleported
}

/// <summary>
/// Owns the ghost car's error states. One instance on the car GameObject (created by
/// <see cref="GhostCarBrain"/> on the host) and ticked once per fixed step by whichever driver is
/// active, before it applies its controls. Stuck and reverse-out are self-recoveries; a flip is
/// done in place; and off-the-map or in-water conditions teleport straight away (teleporting in
/// view is fine). When a self-recovery keeps failing without progress the guard falls back to the
/// same teleport. The teleport moves the car to the nearest navmesh point on its route, exactly
/// like AICarDriver's own teleport.
/// </summary>
[Title( "Ghost Car Error Guard" ), Category( "Vehicles" ), Icon( "healing" )]
public sealed class GhostCarErrorGuard : Component
{
	// ------------------------------------------------------------------ Stuck (only when the caller passes handleStuck: true)

	/// <summary>|forward speed| below this while throttling counts as stuck.</summary>
	[Property, Group( "Stuck" )] public float StuckSpeed { get; set; } = 10f;
	[Property, Group( "Stuck" )] public float StuckTime { get; set; } = 3f;
	[Property, Group( "Stuck" )] public float ReverseTime { get; set; } = 1.5f;
	[Property, Group( "Stuck" )] public float ReverseThrottle { get; set; } = -0.6f;

	// ------------------------------------------------------------------ Upside down / on its side

	/// <summary>WorldRotation.Up.z below this counts as tilted (roof or side down).</summary>
	[Property, Group( "Flip" )] public float TiltUpZ { get; set; } = 0.5f;
	[Property, Group( "Flip" )] public float TiltTime { get; set; } = 1.5f;
	[Property, Group( "Flip" )] public float FlipLiftHeight { get; set; } = 60f;

	// ------------------------------------------------------------------ Off the map

	[Property, Group( "Off Map" )] public float NavSearchRadius { get; set; } = 500f;
	/// <summary>Car this far below its closest navmesh point counts as fallen off.</summary>
	[Property, Group( "Off Map" )] public float MaxDropBelowNav { get; set; } = 250f;
	[Property, Group( "Off Map" )] public float OffMapTime { get; set; } = 0.5f;

	// ------------------------------------------------------------------ Water

	/// <summary>Origin this far under the water surface counts as in the water. 0 is the default: a buoyant car floats with its origin around the surface, so any real fall puts it under.</summary>
	[Property, Group( "Water" )] public float WaterDepthMargin { get; set; } = 0f;

	// ------------------------------------------------------------------ Teleport fallback

	/// <summary>Self-recoveries without progress before teleporting.</summary>
	[Property, Group( "Teleport" )] public int MaxRecoverAttempts { get; set; } = 3;
	/// <summary>Moving this far from the anchor counts as progress; attempts reset.</summary>
	[Property, Group( "Teleport" )] public float ProgressDistance { get; set; } = 150f;
	[Property, Group( "Teleport" )] public float DropHeight { get; set; } = 20f;
	/// <summary>A teleport landing this close to the previous one counts as the same spot.</summary>
	[Property, Group( "Teleport" )] public float RepeatRadius { get; set; } = 500f;
	/// <summary>Teleports closer together than this in time count as repeats.</summary>
	[Property, Group( "Teleport" )] public float RepeatWindow { get; set; } = 20f;

	[Property, Group( "Debug" )] public bool LogRecoveries { get; set; } = true;

	/// <summary>A reverse-out is running; the driver must apply <see cref="Throttle"/>, <see cref="Steer"/> and <see cref="Brake"/>.</summary>
	public bool IsRecovering { get; private set; }
	public float Throttle { get; private set; }
	public float Steer { get; private set; }
	public bool Brake { get; private set; }
	/// <summary>Last recovery reason: "water", "off-map", "flip", "stuck-reverse" or "stuck-teleport".</summary>
	public string LastReason { get; private set; }

	/// <summary>Optional point the car should face after a teleport (further along its route than routeAheadPoint). Drivers set it before Tick; null = unknown.</summary>
	public Vector3? RouteFacingPoint { get; set; }

	/// <summary>Consecutive teleports that landed within <see cref="RepeatRadius"/> of the previous teleport's destination within <see cref="RepeatWindow"/> seconds. 0 after a fresh area.</summary>
	public int RepeatRecoveries { get; private set; }

	// ------------------------------------------------------------------ Internals

	private Rigidbody _body;
	// Plain accumulators, not TimeSince: each condition is checked every tick and the timer only
	// advances while the condition holds, so a TimeSince advanced with += would double-count.
	private float _stuckSeconds;
	private float _tiltedSeconds;
	private float _offMapSeconds;
	private TimeSince _sinceReverse;
	private TimeSince _sinceWaterCheck = 10f;
	private bool _reversing;
	private int _recoverAttempts;
	private Vector3 _progressAnchor;
	// Where the car last was on a healthy tick; the teleport heading falls back to it.
	private Vector3 _lastGoodPosition;
	// Destination and time of the last teleport, for the repeat-failure escalation.
	private Vector3 _lastTeleportDestination;
	private TimeSince _sinceLastTeleport = 1000f;
	// Rate limit for retrying teleports once every candidate has failed, and the throttle for
	// the "no floor at" log (one line per car per 5 s).
	private TimeUntil _teleportRetryAfter;
	private TimeSince _sinceNoFloorLog = 1000f;
	// Rate limit for the once-per-tick off-map query (the navmesh query is not free) and the
	// last answer it produced, reused in between.
	private TimeSince _sinceOffMapProbe = 1000f;
	private bool _offMapProbeResult;
	private List<WaterEntry> _waters = new();
	// True once the water list has been built at least once, even if the scene has no water:
	// an empty list must not mean "query the scene again every tick".
	private bool _waterCacheBuilt;
	private bool _loggedWaterBounds;

	/// <summary>A cached water volume and its GameObject's world bounds. GetBounds is slow and the
	/// water is static, so the bounds are rebuilt only when the cache is refreshed.</summary>
	private readonly struct WaterEntry
	{
		public WaterEntry( WaterVolume volume, BBox bounds )
		{
			Volume = volume;
			Bounds = bounds;
		}

		public WaterVolume Volume { get; }
		public BBox Bounds { get; }
	}

	// ------------------------------------------------------------------ API

	/// <summary>
	/// Call once per fixed step from the ACTIVE driver, host only, before it applies its controls.
	/// <paramref name="routeAheadPoint"/> is the point ahead on the car's route the teleport aims
	/// for; <paramref name="handleStuck"/> lets the caller keep its own stuck handling (the chase
	/// driver already reverses out itself, so it passes false).
	/// </summary>
	public GhostCarGuardResult Tick( float commandedThrottle, float forwardSpeed, Vector3 routeAheadPoint, bool handleStuck )
	{
		if ( IsProxy ) return GhostCarGuardResult.None;

		if ( !_body.IsValid() )
		{
			_body = GetComponent<Rigidbody>();
			_progressAnchor = WorldPosition;
			_lastGoodPosition = WorldPosition;
		}

		Vector3 position = WorldPosition;

		// Water: immediate teleport, no waiting.
		if ( IsInWaterHelper( position ) )
		{
			if ( TeleportHelper( routeAheadPoint, "water" ) )
				return GhostCarGuardResult.Teleported;
			// No navmesh point available yet: brake and retry next tick.
			return ApplyBrakeHelper( "water" );
		}

		// Progress: moving far enough from the anchor resets the attempt counter.
		if ( FlatDistance( position, _progressAnchor ) > MathF.Max( ProgressDistance, 1f ) )
		{
			_progressAnchor = position;
			_recoverAttempts = 0;
			_stuckSeconds = 0f;
		}

		// Off the map: no navmesh within range, or far below it. Teleport after OffMapTime.
		bool offMap = IsOffMapProbeHelper( position );
		if ( offMap )
		{
			_offMapSeconds += Time.Delta;
			if ( _offMapSeconds >= MathF.Max( OffMapTime, 0f ) )
			{
				if ( TeleportHelper( routeAheadPoint, "off-map" ) )
					return GhostCarGuardResult.Teleported;
				return ApplyBrakeHelper( "off-map" );
			}
		}
		else
		{
			_offMapSeconds = 0f;
		}

		// On its roof or side: after TiltTime, flip upright in place. Counts one attempt.
		if ( WorldRotation.Up.z < TiltUpZ )
		{
			_tiltedSeconds += Time.Delta;
			if ( _tiltedSeconds >= MathF.Max( TiltTime, 0f ) )
			{
				FlipUprightHelper();
				_recoverAttempts++;
				LastReason = "flip";
				LogRecoveryHelper( "flip", position );

				if ( _recoverAttempts >= MaxRecoverAttempts )
					if ( TeleportHelper( routeAheadPoint, "stuck-teleport" ) )
						return GhostCarGuardResult.Teleported;

				return ApplyBrakeHelper( "flip" );
			}
		}
		else
		{
			_tiltedSeconds = 0f;
			// Healthy tick (not in water, not off the map, not tilted): remember the spot so a
			// later teleport can face away from it when the route point gives no usable heading.
			if ( !offMap )
				_lastGoodPosition = position;
		}

		// Reverse-out window: keep applying the reverse controls until it elapses.
		if ( _reversing )
		{
			if ( _sinceReverse < MathF.Max( ReverseTime, 0f ) )
				return ApplyReverseHelper();

			// Window over: try again, or give up to a teleport.
			_reversing = false;
			IsRecovering = false;
			_stuckSeconds = 0f; // let the driver get StuckTime of forward driving before the next window
			if ( _recoverAttempts >= MaxRecoverAttempts )
			{
				if ( TeleportHelper( routeAheadPoint, "stuck-teleport" ) )
					return GhostCarGuardResult.Teleported;
			}
		}

		// Stuck: throttling but not moving (only when the caller asked us to handle it).
		if ( handleStuck )
		{
			if ( commandedThrottle > 0.05f && MathF.Abs( forwardSpeed ) < StuckSpeed )
			{
				_stuckSeconds += Time.Delta;
				if ( _stuckSeconds >= MathF.Max( StuckTime, 0f ) )
				{
					_reversing = true;
					_sinceReverse = 0f;
					_stuckSeconds = 0f; // restart the clock so a chained window needs fresh forward driving
					_recoverAttempts++;
					LastReason = "stuck-reverse";
					LogRecoveryHelper( "stuck-reverse", position );
					return ApplyReverseHelper();
				}
			}
			else
			{
				_stuckSeconds = 0f;
			}
		}

		// Not recovering: nothing to override.
		if ( !IsRecovering )
		{
			Throttle = 0f;
			Steer = 0f;
			Brake = false;
		}

		return GhostCarGuardResult.None;
	}

	/// <summary>Clears timers, the reverse window and the attempt counter. Called on driver handoff.</summary>
	public void ResetGuard()
	{
		_stuckSeconds = 0f;
		_tiltedSeconds = 0f;
		_offMapSeconds = 0f;
		_sinceReverse = 0f;
		_reversing = false;
		IsRecovering = false;
		_recoverAttempts = 0;
		_progressAnchor = WorldPosition;
		Throttle = 0f;
		Steer = 0f;
		Brake = false;
	}

	// ------------------------------------------------------------------ Recovery

	/// <summary>Sets the recovering controls to a reverse-out and returns Recovering.</summary>
	private GhostCarGuardResult ApplyReverseHelper()
	{
		IsRecovering = true;
		Throttle = ReverseThrottle;
		Steer = 0f;
		Brake = false;
		return GhostCarGuardResult.Recovering;
	}

	/// <summary>Sets the recovering controls to a full brake and returns Recovering.</summary>
	private GhostCarGuardResult ApplyBrakeHelper( string reason )
	{
		IsRecovering = false;
		Throttle = 0f;
		Steer = 0f;
		Brake = true;
		LastReason = reason;
		return GhostCarGuardResult.Recovering;
	}

	/// <summary>Lifts the car and resets it to yaw-only, killing all velocity.</summary>
	private void FlipUprightHelper()
	{
		WorldPosition += Vector3.Up * FlipLiftHeight;
		WorldRotation = Rotation.FromYaw( WorldRotation.Yaw() );

		if ( _body.IsValid() )
		{
			_body.Velocity = Vector3.Zero;
			_body.AngularVelocity = Vector3.Zero;
			_body.Sleeping = false;
		}

		Transform.ClearInterpolation();

		_reversing = false;
		_tiltedSeconds = 0f;
		_stuckSeconds = 0f;
	}

	/// <summary>
	/// Teleports to the nearest navmesh point on the car's route: the route-ahead point first,
	/// then the car itself, then random navmesh rolls. A candidate that would drop the car in the
	/// water or off the map is rejected and the next one tried. Holds (returns false) when no
	/// usable point is available yet, so the caller retries next tick.
	/// </summary>
	private bool TeleportHelper( Vector3 routeAheadPoint, string reason )
	{
		var nav = Scene.NavMesh;
		if ( nav is null || !nav.IsEnabled ) return false;

		// Every candidate failed a moment ago: retry no more often than every 0.25 s instead of
		// hammering the navmesh and the floor traces on every fixed step.
		if ( !_teleportRetryAfter ) return false;

		// Candidate destinations in preference order.
		var candidates = new List<Vector3>( 10 );
		AddCandidateHelper( candidates, nav.GetClosestPoint( routeAheadPoint, NavSearchRadius ) );
		AddCandidateHelper( candidates, nav.GetClosestPoint( WorldPosition, NavSearchRadius ) );
		for ( int i = 0; i < 8; i++ )
			AddCandidateHelper( candidates, nav.GetRandomPoint() );

		Vector3 currentPosition = WorldPosition;

		foreach ( var destination in candidates )
		{
			// A teleport that lands where the car already is changes nothing. When the reason
			// is a stuck fallback the car is usually parked on solid ground braking at a
			// ledge, so the snap of its own position is always valid and the loop would never
			// end. Skip spots too close to where it is now, and - once it has started
			// repeating - too close to the last teleport's destination, so the random rolls
			// are what actually moves it.
			if ( reason == "stuck-teleport" && FlatDistance( destination, currentPosition ) <= RepeatRadius )
				continue;
			if ( RepeatRecoveries > 0 && FlatDistance( destination, _lastTeleportDestination ) <= RepeatRadius )
				continue;

			// Never drop the car back into the water, off the map, or onto a navmesh point with
			// no floor under it: a stale bake (navmesh present, colliders gone) would land the
			// car in mid-air and drop it straight back off the map. Skip and try the next spot.
			if ( IsInWaterHelper( destination ) ) continue;
			if ( IsOffMapHelper( destination ) ) continue;
			if ( !HasFloorHelper( destination ) )
			{
				LogNoFloorHelper( destination );
				continue;
			}

			// Face along the route. The destination is usually the navmesh snap OF routeAheadPoint,
			// so routeAheadPoint - destination is ~zero and LookAt would leave an arbitrary yaw;
			// prefer the caller's RouteFacingPoint, then where the car last was healthy, and only
			// then keep the current yaw.
			Vector3 heading = Vector3.Zero;
			if ( RouteFacingPoint.HasValue )
				heading = (RouteFacingPoint.Value - destination).WithZ( 0f );
			if ( heading.Length <= 50f )
				heading = (destination - _lastGoodPosition).WithZ( 0f );

			WorldPosition = destination + Vector3.Up * DropHeight;
			if ( heading.Length > 50f )
				WorldRotation = Rotation.LookAt( heading.Normal, Vector3.Up );
			else
				WorldRotation = Rotation.FromYaw( WorldRotation.Yaw() );

			if ( _body.IsValid() )
			{
				_body.Velocity = Vector3.Zero;
				_body.AngularVelocity = Vector3.Zero;
				_body.Sleeping = false;
			}

			Transform.ClearInterpolation();

			// Repeat failures: teleports that land near the previous destination in quick
			// succession mean the car keeps failing the same stretch, so the drivers push the
			// next route-ahead target further along (see NavMeshCarController).
			bool repeat = _sinceLastTeleport < MathF.Max( RepeatWindow, 0f )
				&& FlatDistance( destination, _lastTeleportDestination ) <= RepeatRadius;
			RepeatRecoveries = repeat ? RepeatRecoveries + 1 : 0;
			_lastTeleportDestination = destination;
			_sinceLastTeleport = 0f;

			LastReason = reason;
			LogRecoveryHelper( reason, destination );

			ResetGuard();
			Throttle = 0f;
			Steer = 0f;
			Brake = true;
			return true;
		}

		// Nothing usable this tick: hold, and let the retry rate limit keep the next attempt cheap.
		_teleportRetryAfter = 0.25f;
		return false;
	}

	/// <summary>Adds a navmesh candidate to the teleport list when the query returned one.</summary>
	private static void AddCandidateHelper( List<Vector3> candidates, Vector3? point )
	{
		if ( point.HasValue )
			candidates.Add( point.Value );
	}

	/// <summary>
	/// True when a downward ray through <paramref name="point"/> reaches solid ground within its
	/// range. A navmesh point over a hole (a stale bake: the navmesh is there but the colliders
	/// were disabled after baking) must not be used as a teleport destination, or the car lands
	/// and falls straight back off the map. Only the car's own hierarchy is ignored; triggers are
	/// not hit by default, so they cannot mask a real drop.
	/// </summary>
	private bool HasFloorHelper( Vector3 point )
	{
		SceneTraceResult result = Scene.Trace.Ray( point + Vector3.Up * 100f, point - Vector3.Up * 200f )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		return result.Hit;
	}

	/// <summary>Logs a rejected floor-less teleport candidate at most once every 5 s per car.</summary>
	private void LogNoFloorHelper( Vector3 point )
	{
		if ( !LogRecoveries || _sinceNoFloorLog < 5f ) return;

		_sinceNoFloorLog = 0f;
		Log.Info( $"GhostCarErrorGuard: {GameObject.Name} no floor at {point}" );
	}

	// ------------------------------------------------------------------ Detection

	/// <summary>
	/// True when the car's origin is inside a WaterVolume's cached world bounds (X and Y) and below
	/// its surface. Bounds come from the water GameObject's <see cref="GameObject.GetBounds"/> (the
	/// only public source; WaterVolume itself exposes no "who is inside" query) and are cached,
	/// because the water is static and GetBounds is slow.
	/// </summary>
	private bool IsInWaterHelper( Vector3 position )
	{
		if ( !_waterCacheBuilt || _sinceWaterCheck >= 3f )
			RefreshWaterCacheHelper();

		foreach ( var water in _waters )
		{
			var go = water.Volume.GameObject;
			if ( !go.IsValid() ) continue;

			BBox bounds = water.Bounds;
			if ( position.x < bounds.Mins.x || position.x > bounds.Maxs.x ) continue;
			if ( position.y < bounds.Mins.y || position.y > bounds.Maxs.y ) continue;

			float surfaceZ = go.WorldPosition.z + water.Volume.SurfaceOffset;
			if ( position.z < surfaceZ - WaterDepthMargin )
			{
				if ( LogRecoveries && !_loggedWaterBounds )
				{
					_loggedWaterBounds = true;
					Log.Info( $"GhostCarErrorGuard: water '{go.Name}' bounds X[{bounds.Mins.x:0},{bounds.Maxs.x:0}] Y[{bounds.Mins.y:0},{bounds.Maxs.y:0}] surface Z {surfaceZ:0}" );
				}

				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Refreshes the cached WaterVolume list and their world bounds. Disabled volumes and volumes
	/// on inactive GameObjects are skipped (the scene's water is not expected to change).
	/// </summary>
	private void RefreshWaterCacheHelper()
	{
		_sinceWaterCheck = 0f;
		_waterCacheBuilt = true;
		_waters = new List<WaterEntry>();
		foreach ( var water in Scene.GetAllComponents<WaterVolume>() )
		{
			if ( !water.IsValid() || !water.Enabled ) continue;

			var go = water.GameObject;
			if ( !go.IsValid() || !go.Active ) continue;

			_waters.Add( new WaterEntry( water, go.GetBounds() ) );
		}
	}

	/// <summary>True when there is no navmesh near the car, or it sits well below its closest navmesh point.</summary>
	private bool IsOffMapHelper( Vector3 position )
	{
		var nav = Scene.NavMesh;
		if ( nav is null || !nav.IsEnabled ) return false;

		Vector3? closest = nav.GetClosestPoint( position, NavSearchRadius );
		if ( !closest.HasValue ) return true;

		return position.z < closest.Value.z - MaxDropBelowNav;
	}

	/// <summary>
	/// The once-per-tick off-map probe: the navmesh query runs at most every 0.25 s and the
	/// last answer is reused in between. The OffMapTime accumulator in Tick still advances on
	/// every tick, so the delay is unaffected. Candidate checks inside TeleportHelper call
	/// <see cref="IsOffMapHelper"/> directly: they test a different point each time, so a
	/// latched answer would be wrong there.
	/// </summary>
	private bool IsOffMapProbeHelper( Vector3 position )
	{
		if ( _sinceOffMapProbe < 0.25f ) return _offMapProbeResult;

		_sinceOffMapProbe = 0f;
		_offMapProbeResult = IsOffMapHelper( position );
		return _offMapProbeResult;
	}

	// ------------------------------------------------------------------ Helpers

	private void LogRecoveryHelper( string reason, Vector3 position )
	{
		if ( LogRecoveries )
			Log.Info( $"GhostCarErrorGuard: {GameObject.Name} {reason} at {position}" );
	}

	private static float FlatDistance( Vector3 a, Vector3 b ) => (b - a).WithZ( 0f ).Length;
}
