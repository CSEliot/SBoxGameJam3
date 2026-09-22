using System;
using Sandbox.Citizen;
using ShrimpleRagdolls;

namespace Sandbox;

/// <summary>
/// Character controller for Drunk Player.
///
/// Motorcycle-feel human CC driven by forces, not set velocities. Players tap Left/Right to
/// lean; lean both steers (yaw follows roll while moving forward) and must be actively
/// counter-tapped to stay upright. Drunkenness exaggerates every tap. Lean past _MaxHitRoll
/// and the CC ragdolls for _KnockdownRecoveryTime, then resets at zero velocity.
/// </summary>
public sealed class DrunkCC : Component
{
	
	/// <summary>
	/// The current state of the character.
	/// </summary>
	public enum State
	{
		Running,
		KnockedDown,
	}
	
	/// <summary>
	/// Todo: delete if remain unused - ecs
	/// </summary>
	[Sync]
	public Guid ConnectionID { get; set; }
	
	/// <summary>
	/// How many beers the player has in them. No maximum.
	/// </summary>
	[Property] public float BeerLevel { get; set; } = 1;

	/// <summary>
	/// Debug: hard-zero spin and orientation on this axis every tick.
	/// </summary>
	[Property] public bool LockPitch { get; set; }
	/// <summary>
	/// Debug: hard-zero spin and orientation on this axis every tick.
	/// </summary>
	[Property] public bool LockYaw   { get; set; }
	/// <summary>
	/// Debug: hard-zero spin and orientation on this axis every tick.
	/// </summary>
	[Property] public bool LockRoll  { get; set; }
	/// <summary>
	/// Show DEBUG collision test box.
	/// </summary>
	[Property] public bool ShowCollisionBox  { get; set; }

	[Property] public CollisionReporter WallHitColliderReporter  { get; set; }
	[Property] public GameObject BarArrow  { get; set; }

	/// <summary>
	/// Size of the collider and direction.
	/// </summary>
	// [Property] public Vector3 CollisionBoxExtents  { get; set; }
	// /// <summary>
	// /// Where it starts from character.
	// /// </summary>
	// [Property] public float CollisionBoxRelativeStartX  { get; set; }
	// [Property] public float CollisionBoxRelativeStartY  { get; set; }
	// [Property] public float CollisionBoxRelativeStartZ  { get; set; }
	
	[Sync] public State CurrentState { get; set; } = State.Running;

	/// <summary>
	/// True from the moment a jump launches until we detect a fresh landing. Gates re-jumping.
	/// </summary>
	private bool _isJumping;

	/// <summary>
	/// Set once the body has actually left the ground after a jump, so the sphere still touching
	/// the floor for a tick right after launch isn't mistaken for a landing.
	/// </summary>
	private bool _hasLeftGround;

	/// <summary>
	/// Counts down while knocked down. Reset happens when it reaches zero.
	/// </summary>
	private TimeUntil _knockdownEnds;

	/// <summary>
	/// At knockdown, get heading direction.
	/// </summary>
	private Vector3 _knockdownHeading;

	/// <summary>
	/// At knockdown, get heading direction.
	/// </summary>
	private Vector3 _knockdownRestorePosition;

	/// <summary>
	/// One recorded moment of the running player: where they were and which way they faced.
	/// </summary>
	private struct HistorySample
	{
		public float Time;
		public Vector3 Position;
		public Vector3 Heading;
	}

	/// <summary>
	/// Rolling trail of recent running positions/headings, oldest first. Only recorded while
	/// Running; recovery after a knockdown rewinds into this by beer level.
	/// </summary>
	private readonly List<HistorySample> _history = new();

	/// <summary>
	/// Timer gating how often a HistorySample is appended.
	/// </summary>
	private TimeSince _sinceLastSample;
	private bool _hasCheckedHistObstacle;
	private float _rollSin;
	private float _rollCos;
	private float _rollDeg;

	/// <summary>
	/// Seconds of rewind per beer. rewindSeconds = _BeerLevel * this. Default 1 -> 1 beer rewinds ~1s.
	/// </summary>
	[Property] private float _SecondsPerBeer { get; set; } = 1f;

	/// <summary>
	/// Extra seconds of history kept beyond the current beer's rewind, so higher beer levels
	/// already have trail to rewind into. Buffer length = _BeerLevel * _SecondsPerBeer + this.
	/// </summary>
	[Property] private float _RecoveryHistoryHeadroom { get; set; } = 3f;

	// /// <summary>
	// /// Rec Distance No Matter Beer Level
	// /// </summary>
	// [Property] private float _RecoveryMinTracking { get; set; } = 2f;

	/// <summary>
	/// Seconds between recorded HistorySamples. Smaller = finer rewind, more memory.
	/// </summary>
	[Property] private float _RecoverySampleInterval { get; set; } = 0.1f;

	/// <summary>
	/// Search radius (units) when snapping the recovery point onto the navmesh.
	/// </summary>
	[Property] private float _RecoveryNavSearchRadius { get; set; } = 1024f;
	
	/// <summary>
	/// Upright spring stiffness: corrective torque per degree of roll beyond _RollResponseFloor.
	/// </summary>
	[Property] private float _CorrectionStrength { get; set; } = 1;

	/// <summary>
	/// Upright damping: torque per deg/s of roll rate, always active. Kills the wobble.
	/// Leave at 0 to auto-compute critical damping from _CorrectionStrength and the body's inertia.
	/// </summary>
	[Property] private float _CorrectionDamping { get; set; } = 0;

	/// <summary>
	/// Degrees of roll the spring ignores. The damper still acts inside it.
	/// </summary>
	[Property] private float _RollResponseFloor { get; set; } = 1;

	/// <summary>
	/// Angular impulse about the forward axis per Left/Right tap, at zero drunkenness.
	/// </summary>
	[Property] private float _LeanImpulse { get; set; } = 1;

	/// <summary>
	/// Tap impulse multiplier per beer: impulse = _LeanImpulse * (1 + _BeerLevel * this).
	/// At 10 beers with 0.5 here a tap hits 6x. Tune so 10+ is "dangerously strong".
	/// </summary>
	[Property] private float _DrunkLeanScale { get; set; } = 0.5f;

	/// <summary>
	/// Target yaw rate (deg/s) per degree of roll while moving forward. Motorcycle steering.
	/// </summary>
	[Property] private float _RollTurnRate { get; set; } = 1f;

	/// <summary>
	/// Yaw torque per deg/s of error between target and actual yaw rate. Higher = crisper heading.
	/// </summary>
	[Property] private float _YawGain { get; set; } = 1f;

	/// <summary>
	/// How fast a character goes per beers gained.
	/// </summary>
	[Property, MinMax(0, 100)] private float _BeerToSpeedMultiplier { get; set; } = 1.1f;
	
	/// <summary>
	/// Sideways force per unit of lateral velocity. Makes the sphere carve with its heading
	/// instead of sliding on its old line. 0 = ice.
	/// </summary>
	[Property] private float _LateralGrip { get; set; } = 1f;
	
	/// <summary>
	/// Constant forward drive while below _VelocityCeiling.
	/// </summary>
	[Property] private float _RunningForce { get; set; } = 1;

	/// <summary>
	/// Drive cuts out above this speed.
	/// </summary>
	[Property] private float _VelocityCeiling { get; set; } = 1;
	
	/// <summary>
	/// How strong the character will jump.
	/// </summary>
	[Property] private float _JumpForce { get; set; } = 1;

	/// <summary>
	/// Distance (units) below the sphere's bottom to probe for ground. The landing check and the
	/// can-jump check both use this downward trace. Small values are stricter about "grounded".
	/// </summary>
	[Property] private float _GroundCheckDistance { get; set; } = 4f;
	
	/// <summary>
	/// Seconds spent ragdolling after a knockdown before reset.
	/// </summary>
	[Property] private float _KnockdownRecoveryTime { get; set; } = 1;

	// /// <summary>
	// /// Distance (units) forward from the body that the wall-trace casts.
	// /// 0 disables the wall-trace check entirely.
	// /// </summary>
	// [Property, Range(0, 1000)] private float _WallHitDistance { get; set; }
	//
	// /// <summary>
	// /// Max angle (degrees) between player forward and the inward-facing wall normal (-trace.Normal).
	// /// Measures how squarely the player is driving into the wall: 0 = perfectly straight-on,
	// /// 90 = grazing parallel. Only hits with approach angle AT MOST this trigger knockdown.
	// /// 0 disables the wall-trace check regardless of _WallHitDistance.
	// /// </summary>
	// [Property, Range(0, 90)] private float _WillCollideApproachAngle { get; set; }

	/// <summary>
	/// True while the wall-trace connects this tick (regardless of whether the
	/// angle gate passes). Driven every tick by CheckWallHit — not [Sync], local-only.
	/// </summary>
	private bool _HasHitObstacle { get; set; }

	// /// <summary>
	// /// Forward offset (units) from the body origin at which to start the wall-trace.
	// /// Defaults to 0 (start at body centre).
	// /// </summary>
	// [Property] private float _ForwardRayCastPosition { get; set; }

	/// <summary>
	/// Roll (degrees, either sign) at which the CC is knocked down. 0 disables knockdown.
	/// </summary>
	[Property] private float _MaxHitRoll { get; set; }
	
	/// <summary>
	/// Local Rigidbody Component
	/// </summary>
	[Property] private Rigidbody _Rigidbody { get; set; }

	/// <summary>
	/// Component to control citizen via code.
	/// </summary>
	[Property] private CitizenAnimationHelper _CitizenAnimationHelper { get; set; }

	/// <summary>
	/// Component to control citizen via code.
	/// </summary>
	[Property] private SkinnedModelRenderer _SkinnedModelRenderer { get; set; }

	/// <summary>
	/// Custom Ragdoll Code by Small Fish Library
	/// </summary>
	[Property] private ShrimpleRagdoll _Ragdoll { get; set; }
	
	private CCCamera _ccCamera;
 	
	protected override void OnStart()
	{
		WallHitColliderReporter.OnTriggerEnterCallback += OnWallHitColliderEnter;
		WallHitColliderReporter.OnTriggerExitCallback += OnWallHitColliderExit;
		_ccCamera = Scene.FindAllWithTag( "cccamera" ).FirstOrDefault()?.GetComponent<CCCamera>(); //SceneNetworkSystem Get<CCCamera>();
		if(_ccCamera == null)
		{
			Log.Warning( "DrunkCC: CCCamera not found!" );
		}
	}
	
	protected override void OnUpdate()
	{
		switch ( CurrentState )
		{
			case State.Running:
				_CitizenAnimationHelper.MoveStyle = CitizenAnimationHelper.MoveStyles.Run;
				_SkinnedModelRenderer.Set( "move_style", 2 );
				_SkinnedModelRenderer.Set( "move_x", 10000 );
				_ccCamera.UseAltTargets = false;
				break;
			case State.KnockedDown:
				_ccCamera.UseAltTargets = true;
				break;
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( _Rigidbody == null ) return;

		if ( CurrentState == State.Running)
		{
				HandleRunning();
				// Rule 5: leaned too far -> knocked down.
				if ( _MaxHitRoll > 0f && MathF.Abs( _rollDeg ) > _MaxHitRoll )
				{
					EnterKnockedDown();
				}
				else if ( _HasHitObstacle )
				{
					_HasHitObstacle = false;
					EnterKnockedDown();
				}
		}
		if(CurrentState == State.KnockedDown)
			HandleKnockedDown();
	}

	private void HandleRunning()
	{
		// Landing detection: once a jump has carried the body clear of the ground, the first time
		// we touch down again clears the jump gate so the next Jump press is allowed. Feeding
		// IsGrounded here also lets the animgraph blend out of the jump/fall pose on landing.
		bool grounded = IsGrounded();
		_CitizenAnimationHelper.IsGrounded = grounded;
		
		if(IsProxy)
			return;

		// Only record recovery history while grounded. Airborne/off-map positions must never
		// become a stand-up target: falling off the map would otherwise poison the trail and
		// recovery would teleport the player to a point they were never validly standing on.
		if ( grounded )
			RecordRunningHistoryHelper();
		if ( _isJumping )
		{
			if ( !grounded )
				_hasLeftGround = true;
			else if ( _hasLeftGround )
				_isJumping = false;
		}

		// Roll measured geometrically: cross(up, worldUp) is the axis that rotates 'up' back toward
		// world up, so projecting it onto forward gives a signed roll that doesn't depend on Euler
		// sign conventions. Sign: matches the +fwd torque direction, so +K*roll along fwd restores.
		// Verified in-editor with the earlier torque version.
		_rollSin = Vector3.Dot( Vector3.Cross( _Rigidbody.WorldRotation.Up, Vector3.Up ), _Rigidbody.WorldRotation.Forward );
		_rollCos = Vector3.Dot( _Rigidbody.WorldRotation.Up, Vector3.Up );
		_rollDeg = MathF.Atan2( _rollSin, _rollCos ).RadianToDegree();
		
		// Spin in the body's own frame. Source convention: +X forward, +Y left, +Z up, so
		// .x = roll rate (about forward), .y = pitch rate, .z = yaw rate. Radians/s.
		var localAv = _Rigidbody.WorldRotation.Inverse * _Rigidbody.AngularVelocity;
		float rollRateDeg = localAv.x.RadianToDegree();
		float yawRateDeg  = localAv.z.RadianToDegree();

		// Rule 1 / 7: always running, always gaining speed up to the ceiling.
		if ( _Rigidbody.Velocity.Length < _VelocityCeiling )
		{
			float beerSpeedMultiplier = (_BeerToSpeedMultiplier + 1) * (BeerLevel + 1); // todo: consider better handle to edge case than " add 1" -ecs
			_Rigidbody.ApplyForce( _Rigidbody.WorldRotation.Forward * _RunningForce * beerSpeedMultiplier );
		}

		// Lateral grip: oppose sideways velocity so the heading change from yaw actually turns
		// the path. Without this a sphere just keeps rolling along its old line.
		if ( _LateralGrip > 0f )
		{
			float lateralSpeed = Vector3.Dot( _Rigidbody.Velocity, _Rigidbody.WorldRotation.Left );
			_Rigidbody.ApplyForce( -_Rigidbody.WorldRotation.Left * lateralSpeed * _LateralGrip );
		}

		// Lean input: taps only, each tap is an angular impulse about the forward axis.
		// Rule 2 / 4: drunkenness multiplies the impulse with no cap.
		// Sign mapping (Right -> -fwd) was verified in-editor with the previous torque version.
		float leanDir = 0f;
		if ( Input.Down( "Left" ) ) leanDir -= 1f;
		if ( Input.Down( "Right" ) )  leanDir += 1f;
		if ( leanDir != 0f )
		{
			float impulse = _LeanImpulse * ( 1f + BeerLevel * _DrunkLeanScale );
			_Rigidbody.PhysicsBody?.ApplyTorque( _Rigidbody.WorldRotation.Forward * leanDir * impulse );
		}

		// Upright controller: spring on roll (outside the floor) plus damper on roll rate (always).
		// Positive roll (left lean) needs +fwd torque to come back, so the spring is +K*roll.
		// The damper opposes whatever spin about fwd exists, so it is -Kd*rate.
		float spring = 0f;
		if ( MathF.Abs( _rollDeg ) > _RollResponseFloor )
		{
			// Error measured from the floor edge so response ramps smoothly instead of stepping.
			float error = _rollDeg - MathF.Sign( _rollDeg ) * _RollResponseFloor;
			spring = error * _CorrectionStrength;
		}
		float damping = _CorrectionDamping > 0f ? _CorrectionDamping : CriticalRollDamping( _Rigidbody );
		_Rigidbody.ApplyTorque( _Rigidbody.WorldRotation.Forward * ( spring - rollRateDeg * damping ) );

		// Rule 3: yaw follows roll, linearly, while moving forward. Done as a rate-tracking torque
		// about the body's up axis rather than setting angular velocity. Standing still or moving
		// backward the target is zero, which also damps out residual yaw.
		float forwardSpeed = Vector3.Dot( _Rigidbody.Velocity, _Rigidbody.WorldRotation.Forward );
		float targetYawRateDeg = forwardSpeed > 0f ? _rollDeg * _RollTurnRate : 0f;
		_Rigidbody.ApplyTorque( _Rigidbody.WorldRotation.Up * ( targetYawRateDeg - yawRateDeg ) * _YawGain );

		ApplyDebugLocks( _Rigidbody );

		if ( Input.Pressed( "Jump" ) && !_isJumping && grounded )
		{
			_Rigidbody.ApplyForce( _Rigidbody.WorldRotation.Up * _JumpForce );
			_CitizenAnimationHelper.TriggerJump();
			_isJumping = true;
			_hasLeftGround = false;
		}
	}

	private void HandleKnockedDown()
	{
		// Rule 6: ragdoll. No forces, no locks, physics owns the body until the timer runs out.
		if ( _knockdownEnds )
		{
			ResetFromKnockdown();
		}
	}
	
	
	private void EnterKnockedDown()
	{
		CurrentState = State.KnockedDown;
		_knockdownEnds = _KnockdownRecoveryTime;
		_Ragdoll.Mode = RagdollMode.Enabled;
		_Ragdoll.ApplyVelocity( _Rigidbody.Velocity );

		// Recovery rewinds further back the drunker you are. Pull the sample from
		// rewindSeconds ago; if history is shorter than that (early game, or beer just spiked),
		// fall back to the oldest sample we have.
		float rewindSeconds = BeerLevel * _SecondsPerBeer;
		var restore = GetRewoundSample( rewindSeconds );

		_knockdownRestorePosition = restore.Position;
		_knockdownHeading = restore.Heading;
		if ( _knockdownHeading.IsNearlyZero() ) _knockdownHeading = Vector3.Forward;
		
		Log.Info("Entering Knockdown.");
	}

	/// <summary>
	/// Records a position/heading sample at _RecoverySampleInterval and trims the trail to the
	/// window we need: current beer's rewind plus headroom, so higher future beer levels already
	/// have trail to rewind into. Called only while Running.
	/// </summary>
	private void RecordRunningHistoryHelper()
	{
		if ( _sinceLastSample < _RecoverySampleInterval && _history.Count > 0 )
			return;

		_sinceLastSample = 0f;

		var heading = _Rigidbody.WorldRotation.Forward.WithZ( 0f );
		if ( heading.IsNearlyZero() ) heading = _Rigidbody.WorldRotation.Up.WithZ( 0f );

		_history.Add( new HistorySample
		{
			Time = Time.Now,
			Position = _Rigidbody.WorldPosition,
			Heading = heading,
		} );

		// Keep buffer sized to the deepest rewind we might need plus headroom.
		float window = BeerLevel * _SecondsPerBeer + _RecoveryHistoryHeadroom;
		float cutoff = Time.Now - window;
		int keepFrom = 0;
		while ( keepFrom < _history.Count - 1 && _history[keepFrom].Time < cutoff )
			keepFrom++;
		if ( keepFrom > 0 )
			_history.RemoveRange( 0, keepFrom );
	}

	/// <summary>
	/// Returns the sample from approximately secondsAgo in the past, clamped to the oldest sample
	/// when history doesn't reach that far back. Falls back to the live transform if empty.
	/// </summary>
	private HistorySample GetRewoundSample( float secondsAgo )
	{
		if ( _history.Count == 0 )
		{
			var heading = _Rigidbody.WorldRotation.Forward.WithZ( 0f );
			return new HistorySample { Time = Time.Now, Position = _Rigidbody.WorldPosition, Heading = heading };
		}

		float target = Time.Now - secondsAgo;
		// History is oldest-first; walk newest->oldest and take the first sample at or before target.
		for ( int i = _history.Count - 1; i >= 0; i-- )
		{
			if ( _history[i].Time <= target )
				return _history[i];
		}

		// Requested further back than we have -> oldest available.
		return _history[0];
	}

	/// <summary>
	/// Snaps a recovery position onto the navmesh so recovery always lands on walkable ground.
	/// If the rewound point is beyond _RecoveryNavSearchRadius (e.g. an off-map sample), it falls
	/// back to snapping the body's current resting position, and only if that also misses the mesh
	/// does it stand up in place - it never teleports to the raw off-mesh point. Returns the point
	/// unchanged only when there is no navmesh or it is disabled. Navmesh points sit on the walkable
	/// surface while the rigidbody origin sits above the sphere's contact point, so the point is
	/// lifted by that standing offset to land resting on the ground rather than buried in it.
	/// </summary>
	private Vector3 SnapRecoveryToNavMesh( Vector3 pos )
	{
		var nav = Scene.NavMesh;
		if ( nav is null || !nav.IsEnabled )
			return pos;

		var snapped = nav.GetClosestPoint( pos, _RecoveryNavSearchRadius );
		if ( !snapped.HasValue )
		{
			// Rewound point is off-mesh. Never teleport to it - snap the body's current resting
			// position instead, and if even that misses the mesh, stand up in place.
			var here = _Rigidbody.WorldPosition;
			var snappedHere = nav.GetClosestPoint( here, _RecoveryNavSearchRadius );
			return snappedHere.HasValue
				? snappedHere.Value + Vector3.Up * RecoveryStandOffset()
				: here;
		}

		return snapped.Value + Vector3.Up * RecoveryStandOffset();
	}

	/// <summary>
	/// Height of the rigidbody origin above the ground while standing on the movement sphere
	/// collider (Radius minus the collider's local Center.z). Lets the navmesh snap reproduce
	/// the standing pose instead of embedding the sphere.
	/// </summary>
	private float RecoveryStandOffset()
	{
		var collider = _Rigidbody.GameObject.Components.Get<SphereCollider>();
		if ( collider is null ) return 0f;
		return collider.Radius - collider.Center.z;
	}

	/// <summary>
	/// True when solid ground is within _GroundCheckDistance below the movement sphere. Probes
	/// straight down (world) from the sphere's centre out to its radius plus that skin distance,
	/// ignoring the whole player hierarchy so neither the sphere itself nor the ragdoll's bone
	/// colliders count as ground. Returns true when there's no sphere collider so a misconfigured
	/// prefab doesn't silently make jumping impossible.
	/// </summary>
	private bool IsGrounded()
	{
		var collider = _Rigidbody.GameObject.Components.Get<SphereCollider>();
		if ( collider is null ) return true;

		var centre = _Rigidbody.WorldPosition + _Rigidbody.WorldRotation * collider.Center;
		float reach = collider.Radius + _GroundCheckDistance;
		var trace = Scene.Trace.Ray( centre, centre + Vector3.Down * reach )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();
		return trace.Hit;
	}

	/// <summary>
	/// Rule 7: stand back up at the rewound recovery point, facing that moment's heading, at zero velocity.
	/// </summary>
	private void ResetFromKnockdown()
	{
		_Ragdoll.Mode = RagdollMode.None;
		if(IsProxy)
			return;
		
		Log.Info("Resetting From Knockdown.");
		
		var rb = _Rigidbody;

		// Snap the rewound point onto the navmesh if one exists. Quietly falls back to the raw
		// position when there's no mesh, it's disabled, or the point is beyond the search radius.
		rb.WorldPosition = SnapRecoveryToNavMesh( _knockdownRestorePosition );
		rb.WorldRotation = Rotation.LookAt( _knockdownHeading.Normal, Vector3.Up );
		rb.Velocity = Vector3.Zero;
		rb.AngularVelocity = Vector3.Zero;
		CurrentState = State.Running;

		// A knockdown can interrupt a jump; clear the gate so recovery doesn't leave jumping stuck off.
		_isJumping = false;
		_hasLeftGround = false;

		// The rewound trail is spent; don't let stale pre-knockdown samples seed the next rewind.
		_history.Clear();
	}

	/// <summary>
	/// Critical damping for the roll spring, in torque per deg/s. For a spring k (per radian) on
	/// inertia I, c = 2*sqrt(k*I). Converting both gains to per-degree gives Kd = 2*sqrt(Kp*I/57.3).
	/// Uses the inertia about the body's forward (local X) axis.
	/// </summary>
	private float CriticalRollDamping( Rigidbody rb )
	{
		float inertiaFwd = rb.InertiaTensor.x;
		if ( inertiaFwd <= 0f || _CorrectionStrength <= 0f ) return 0f;
		return 2f * MathF.Sqrt( _CorrectionStrength * inertiaFwd / 57.2958f );
	}

	/// <summary>
	/// Debug helper. Hard-zeroes spin and orientation on the locked axes. Overrides everything
	/// above. LockPitch stays on (the sphere would tumble as it rolls otherwise); LockYaw and
	/// LockRoll must be off for the motorcycle behaviour to show.
	/// </summary>
	private void ApplyDebugLocks( Rigidbody rb )
	{
		if ( !LockPitch && !LockYaw && !LockRoll ) return;

		var rot = rb.WorldRotation;
		var localAv = rot.Inverse * rb.AngularVelocity;
		if ( LockRoll )  localAv.x = 0f;
		if ( LockPitch ) localAv.y = 0f;
		if ( LockYaw )   localAv.z = 0f;
		rb.AngularVelocity = rot * localAv;

		var angles = rot.Angles();
		if ( LockPitch ) angles.pitch = 0f;
		if ( LockYaw )   angles.yaw   = 0f;
		if ( LockRoll )  angles.roll  = 0f;
		rb.WorldRotation = angles.ToRotation();
	}

	private void OnWallHitColliderEnter( Collider other )
	{
		Log.Info("Istrigger: " + other.IsTrigger + "---OnWallHitColliderEnter + " + other);

		if ( other.IsTrigger || CurrentState == State.KnockedDown)
			return;
		
		_HasHitObstacle = true;
		_hasCheckedHistObstacle = false;
	}
	
	private void OnWallHitColliderExit( Collider other )
	{
		// Log.Info("OnWallHitColliderExit + " + other);
	}

}
