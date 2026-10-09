// Project: sboxgamejam3
// File:    GhostCarBrain.cs
// Author:  cseliot
// Created: 2026.10.09.08.30.00
// Edited: 2026.10.09.08.30.00
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Pac-Man style ghost targeting for one AI car, and the line-of-sight handoff
// between the navmesh controller and the chase driver.
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

namespace Sandbox;

/// <summary>
/// Which classic ghost this car imitates. The order matches the original arcade cast and
/// the spawn order used by the GameManager.
/// </summary>
public enum GhostPersonality
{
	Blinky,
	Pinky,
	Inky,
	Clyde
}

/// <summary>
/// Drives one ai car like a classic Pac-Man ghost.
///
/// Targeting follows the original rules, with each offset exposed as its own tunable
/// distance: Blinky aims straight at the nearest player, Pinky aims ahead of them by
/// PinkyLeadDistance, Inky mirrors the point InkyPivotDistance ahead of the player through
/// Blinky's position, and Clyde chases until within ClydeShyRadius and then retreats to his
/// scatter corner. The defaults match the arcade tile counts at 150 world units per tile.
/// Scatter and Chase modes alternate on a schedule the GameManager owns, and in Scatter
/// every ghost walks its own corner instead of chasing.
///
/// The car roams on the navmesh by default (NavMeshCarController steering toward a hidden
/// target marker placed at the current ghost target). The moment the car has line of sight
/// to its target player, control hands over to AICarDriver, which chases the player
/// directly with its obstacle avoidance; when sight is lost for longer than the grace
/// window, control returns to the navmesh controller. Only the host (or solo play) runs
/// any of this: the car is a physics proxy everywhere else.
///
/// Unlike the arcade ghosts there is no Frightened mode, and a mode switch does not reverse
/// the car's direction (a wheeled vehicle cannot pivot in place).
/// </summary>
[Icon( "directions_car" )]
public sealed class GhostCarBrain : Component
{
	// ------------------------------------------------------------------ Editor tunables

	/// <summary>Which classic ghost targeting rule this car uses.</summary>
	[Property] public GhostPersonality Personality { get; set; } = GhostPersonality.Blinky;

	/// <summary>How far ahead of the player Pinky aims. Default 600 is the classic four tiles at 150 units per tile.</summary>
	[Property, ShowIf( nameof( Personality ), GhostPersonality.Pinky )] public float PinkyLeadDistance { get; set; } = 600f;

	/// <summary>How far ahead of the player Inky's pivot sits before it is mirrored through Blinky. Default 300 is the classic two tiles at 150 units per tile.</summary>
	[Property, ShowIf( nameof( Personality ), GhostPersonality.Inky )] public float InkyPivotDistance { get; set; } = 300f;

	/// <summary>Flat distance at which Clyde gives up the chase and retreats to his scatter corner. Default 1200 is the classic eight tiles at 150 units per tile.</summary>
	[Property, ShowIf( nameof( Personality ), GhostPersonality.Clyde )] public float ClydeShyRadius { get; set; } = 1200f;

	/// <summary>Flat distance (units) at which the car can see its target player.</summary>
	[Property] public float SightRange { get; set; } = 3000f;

	/// <summary>Height the sight ray starts above the car origin.</summary>
	[Property] public float SightEyeHeight { get; set; } = 60f;

	/// <summary>Height the sight ray ends above the player origin.</summary>
	[Property] public float SightTargetHeight { get; set; } = 50f;

	/// <summary>Seconds of lost sight before the handoff back to the navmesh controller.</summary>
	[Property] public float LoseSightGraceSeconds { get; set; } = 1f;

	/// <summary>Seconds between target recomputations (the marker is moved in steps, not every frame).</summary>
	[Property] public float TargetUpdateInterval { get; set; } = 0.25f;

	// ------------------------------------------------------------------ Runtime state

	/// <summary>Scatter corner this car retreats to. Set by the GameManager at spawn; may be null.</summary>
	public GameObject ScatterCorner { get; set; }

	/// <summary>True while the ghost mode clock is in Scatter. Pushed every frame by the GameManager.</summary>
	public bool IsScatterMode { get; set; }

	/// <summary>The live Blinky brain, used by Inky's mirror targeting only. Set by the GameManager.</summary>
	public GhostCarBrain BlinkyPartner { get; set; }

	/// <summary>True while the car currently has (or recently had) sight of its target player.</summary>
	[Property, ReadOnly] public bool HasLineOfSight { get; private set; }

	/// <summary>Nearest player root this car is chasing, or null when no player is available.</summary>
	public GameObject TargetPlayer { get; private set; }

	/// <summary>The current ghost target position (chase point or scatter corner), in world space.</summary>
	public Vector3 GhostTargetPosition { get; private set; }

	// ------------------------------------------------------------------ Internals

	private NavMeshCarController _nav;
	private AICarDriver _ai;
	private GameObject _marker;
	private TimeSince _sinceTargetUpdate = 10f;
	private TimeSince _sinceLastSight = 10f;

	// ------------------------------------------------------------------ Lifecycle

	protected override void OnStart()
	{
		_nav = GetComponent<NavMeshCarController>( true );
		_ai = GetComponent<AICarDriver>( true );

		// Force the dev/test knobs off before the AICarDriver can first enable. Its OnStart
		// clones the car ExtraTestCars times and applies dev tuning, and the authored car
		// prefab ships these switched on, so they must be zeroed here or every ghost spawns a
		// crowd of copies the first time sight is gained.
		if ( _ai.IsValid() )
		{
			_ai.ExtraTestCars = 0;
			_ai.UseDevTuning = false;
			_ai.RecordTelemetry = false;
		}

		if ( !_nav.IsValid() || !_ai.IsValid() )
		{
			Log.Error( $"GhostCarBrain on '{GameObject.Name}': needs a NavMeshCarController and an AICarDriver on the same GameObject. Disabling." );
			Enabled = false;
			return;
		}

		// Host (or solo) owns the ghost. Clients never simulate the car, so they never create
		// the target marker and never drive the controllers.
		if ( IsProxy ) return;

		// One top-level marker for the navmesh to steer toward. Deliberately not parented to
		// the car, so the car's own motion and physics never drag the target along with it,
		// and Never networked because it is purely local host-side steering state.
		_marker = new GameObject( true, $"{GameObject.Name}-ghost-target" );
		_marker.NetworkMode = NetworkMode.Never;
		// Keep this pure runtime helper out of the editor hierarchy and out of saves, so it
		// is never mistaken for authored scene content.
		_marker.Flags |= GameObjectFlags.Hidden | GameObjectFlags.NotSaved;
		_nav.FollowTarget = _marker;

		// Ghosts roam the navmesh by default; the driver only takes over with line of sight.
		_nav.Enabled = true;
		_ai.Enabled = false;
		_ai.ChaseTarget = null;
	}

	protected override void OnDestroy()
	{
		if ( _marker.IsValid() )
			_marker.Destroy();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		if ( !_nav.IsValid() || !_ai.IsValid() || !_marker.IsValid() ) return;

		if ( _sinceTargetUpdate >= MathF.Max( TargetUpdateInterval, 0.01f ) )
		{
			_sinceTargetUpdate = 0f;
			UpdateTargetHelper();
		}

		// Line of sight runs every frame (it is cheap relative to the target query) so the
		// handoff reacts promptly and the grace window is measured finely.
		UpdateLineOfSightHelper();
	}

	// ================================================================== Targeting

	/// <summary>
	/// Recomputes which player to chase and where the ghost target sits, then moves the marker
	/// there. The marker is the navmesh controller's destination; while the driver owns the car
	/// the marker is still kept current so the navmesh leg starts from the right point when
	/// sight is lost.
	/// </summary>
	private void UpdateTargetHelper()
	{
		var nearest = FindNearestPlayerHelper();

		if ( !nearest.IsValid() )
		{
			// No player to chase: hold the marker where it is and let the sight update drop
			// the car back to navmesh mode.
			TargetPlayer = null;
			return;
		}

		TargetPlayer = nearest;

		Vector3 playerPosition = nearest.WorldPosition;
		Vector3 forward = nearest.WorldRotation.Forward.WithZ( 0f );
		if ( forward.LengthSquared < 0.0001f )
			forward = Vector3.Forward;
		forward = forward.Normal;

		Vector3 target = ChaseTargetHelper( playerPosition, forward );

		if ( IsScatterMode && ScatterCorner.IsValid() )
			target = ScatterCorner.WorldPosition;

		GhostTargetPosition = target;
		_marker.WorldPosition = target;
	}

	/// <summary>
	/// The classic chase point for this personality, in world space, from the player's flat
	/// forward vector. The distance offsets are per ghost and only the matching one is used.
	/// </summary>
	private Vector3 ChaseTargetHelper( Vector3 playerPosition, Vector3 forward )
	{
		switch ( Personality )
		{
			case GhostPersonality.Pinky:
				// Aim ahead of the player by the classic four tiles.
				return playerPosition + forward * PinkyLeadDistance;

			case GhostPersonality.Inky:
			{
				// Mirror the pivot ahead of the player through Blinky's position.
				Vector3 pivot = playerPosition + forward * InkyPivotDistance;
				Vector3 blinky = BlinkyPartner.IsValid() ? BlinkyPartner.WorldPosition : WorldPosition;
				Vector3 mirrored = blinky + 2f * (pivot - blinky);
				mirrored.z = playerPosition.z;
				return mirrored;
			}

			case GhostPersonality.Clyde:
			{
				// Chase until inside the shy radius, then retreat to the scatter corner.
				if ( FlatDistanceHelper( WorldPosition, playerPosition ) > ClydeShyRadius )
					return playerPosition;

				return ScatterCorner.IsValid() ? ScatterCorner.WorldPosition : playerPosition;
			}

			default:
				// Blinky: straight at the player.
				return playerPosition;
		}
	}

	/// <summary>Nearest valid player root by flat distance from this car, or null when there is none.</summary>
	private GameObject FindNearestPlayerHelper()
	{
		GameObject best = null;
		float bestDistance = float.MaxValue;

		foreach ( var drunk in Scene.GetAllComponents<DrunkCC>() )
		{
			if ( !drunk.IsValid() ) continue;

			var player = drunk.GameObject;
			if ( !player.IsValid() || !player.Enabled ) continue;

			float distance = FlatDistanceHelper( WorldPosition, player.WorldPosition );
			if ( distance < bestDistance )
			{
				bestDistance = distance;
				best = player;
			}
		}

		return best;
	}

	// ================================================================== Line of sight & handoff

	/// <summary>
	/// Tests sight to the current target player and drives the controller handoff. A sighting
	/// sets <see cref="HasLineOfSight"/> at once; losing sight only drops it after
	/// <see cref="LoseSightGraceSeconds"/> without a fresh sighting, so a car is not flipped
	/// between controllers by a single occluded frame.
	/// </summary>
	private void UpdateLineOfSightHelper()
	{
		if ( !TargetPlayer.IsValid() )
		{
			// Target gone: no sight to hold, so give the car straight back to the navmesh.
			_sinceLastSight = 0f;
			SetLineOfSightHelper( false );
			return;
		}

		Vector3 playerPosition = TargetPlayer.WorldPosition;
		bool seen = false;

		if ( FlatDistanceHelper( WorldPosition, playerPosition ) <= SightRange )
		{
			Vector3 from = WorldPosition + Vector3.Up * SightEyeHeight;
			Vector3 to = playerPosition + Vector3.Up * SightTargetHeight;

			// Ignore the whole car hierarchy so the car never blocks its own view. Anything
			// hit whose scene root is the player counts as the player (their colliders live
			// under the player root).
			var trace = Scene.Trace.Ray( from, to )
				.IgnoreGameObjectHierarchy( GameObject )
				.Run();

			seen = !trace.Hit
				|| (trace.GameObject.IsValid() && trace.GameObject.Root == TargetPlayer.Root);
		}

		if ( seen )
		{
			_sinceLastSight = 0f;
			SetLineOfSightHelper( true );

			// The nearest player can change between target updates, so keep the driver aimed
			// at the current one while it owns the car.
			_ai.ChaseTarget = TargetPlayer;
		}
		else if ( HasLineOfSight && _sinceLastSight >= MathF.Max( LoseSightGraceSeconds, 0f ) )
		{
			SetLineOfSightHelper( false );
		}
	}

	/// <summary>
	/// Applies a change in sight state. Handoff happens only on a change: the outgoing
	/// controller is disabled first (both brake in OnDisabled), then the incoming one takes
	/// over. Gaining sight points the driver at the player; losing it sends the car back to
	/// the navmesh marker.
	/// </summary>
	private void SetLineOfSightHelper( bool seen )
	{
		if ( HasLineOfSight == seen ) return;
		HasLineOfSight = seen;

		if ( seen )
		{
			_nav.Enabled = false;
			_ai.ChaseTarget = TargetPlayer;
			_ai.Enabled = true;
		}
		else
		{
			_ai.Enabled = false;
			_ai.ChaseTarget = null;
			_nav.Enabled = true;
		}
	}

	// ================================================================== Geometry helpers

	/// <summary>Flat (X/Y) distance between two world points.</summary>
	private static float FlatDistanceHelper( Vector3 a, Vector3 b ) => (b - a).WithZ( 0f ).Length;
}
