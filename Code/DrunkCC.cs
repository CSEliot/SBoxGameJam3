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
	/// Pants state. Only the STATE travels over the wire ([Sync]) so proxies can mimic the
	/// pants visuals; the movement tunables are never synced because only the owning client
	/// simulates physics.
	/// </summary>
	public enum PantsState
	{
		Up,
		Down,
	}
	
	/// <summary>
	/// Todo: delete if remain unused - ecs
	/// </summary>
	[Sync]
	public Guid ConnectionID { get; set; }
	
	/// <summary>
	/// How many beers the player has in them. No maximum. Difficulty effects that scale with
	/// this saturate at GameManager.BeerDifficultyCap (see DifficultyBeerHelper()); score, HUD
	/// and End-Game readouts always use the raw value.
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
	/// Whether this player's pants are Up or Down. Only this STATE travels over the wire, so
	/// proxies can mimic the pants visuals; the movement tunables never sync — the owning
	/// client is authoritative and the only one simulating physics. Written through
	/// SetPantsState; gameplay triggers (obstacle drop, pull-up) call that, effects read this.
	/// </summary>
	[Sync] public PantsState CurrentPantsState { get; set; } = PantsState.Up;

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
	/// Position queue of the running player: index 0 = oldest/bottom, last index =
	/// newest/top. Only recorded while Running (grounded). Recovery after a knockdown
	/// picks an entry by beer ratio: beer 0 takes the newest (top), beer at/above
	/// MaxBeerLevel the oldest (bottom), the raw level interpolating between them.
	/// </summary>
	private readonly List<HistorySample> _history = new();

	/// <summary>
	/// Where the player last entered KnockedDown, for the double-knockdown check in
	/// OwnerEnterKnockedDownHelper. Only meaningful once _hasLastKnockdownPosition is true
	/// (the first knockdown of a run has nothing to compare against).
	/// </summary>
	private Vector3 _lastKnockdownPosition;
	private bool _hasLastKnockdownPosition;

	/// <summary>
	/// Timer gating how often a HistorySample is appended.
	/// </summary>
	private TimeSince _sinceLastSample;
	private bool _hasCheckedHistObstacle;
	private float _rollSin;
	private float _rollCos;
	private float _rollDeg;

	/// <summary>
	/// Recovery queue capacity. When full, the oldest entry (bottom) is dropped, so the
	/// deepest rewind available is bounded by how many positions this holds.
	/// </summary>
	[Property] public int MaxQueuePositions { get; set; } = 10;

	/// <summary>
	/// Minimum units travelled from the newest recorded position before another position is
	/// queued. Keeps the queue spread over space instead of piling up under the body.
	/// </summary>
	[Property] public float MinDistancePerPosition { get; set; } = 100f;

	/// <summary>
	/// Minimum seconds between recorded positions. Even at a standstill the queue grows at
	/// most one entry per this interval; the distance gate adds the spatial spacing on top.
	/// </summary>
	[Property] public float MinTimePerPosition { get; set; } = 2f;

	/// <summary>
	/// Beer level that maps to the oldest queue entry (deepest rewind); higher levels saturate
	/// there. Defaults to GameManager.BeerDifficultyCap's default. This system's own
	/// saturation — RecoveryQueueIndexHelper() reads the RAW BeerLevel against this, so
	/// DifficultyBeerHelper() is deliberately not involved.
	/// </summary>
	[Property] public float MaxBeerLevel { get; set; } = 10f;

	/// <summary>
	/// If a knockdown happens within this distance (units) of the PREVIOUS knockdown's
	/// location, the newest recovery-history entry is dropped before the recovery point is
	/// picked, so a player falling twice in the same stretch respawns one position further
	/// back instead of on top of the same spot. 0 or below disables the check.
	/// </summary>
	[Property] private float _DoubleKnockdownDistance { get; set; } = 100f;

	/// <summary>
	/// Search radius (units) when snapping the recovery point onto the navmesh.
	/// </summary>
	[Property] private float _RecoveryNavSearchRadius { get; set; } = 1024f;
	/// <summary>
	/// How much height to add to the recovery point when snapping it onto the navmesh.
	/// </summary>
	[Property] private float _RecoveryAdditionalHeight { get; set; } = 100f;
	
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
	/// Tap impulse multiplier per capped beer (see DifficultyBeerHelper()): impulse =
	/// _LeanImpulse * (1 + cappedBeer * this). At a 10-beer cap with 0.5 here a tap hits
	/// 6x. Tune so the cap is where "dangerously strong" maxes out.
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
	/// While pants are Down, top forward speed is cut by this percentage (0-100). Scales the
	/// velocity ceiling and actively holds forward speed at the reduced cap (the movement sphere
	/// is frictionless, so cutting thrust alone would never slow a coasting body); 100 pins
	/// forward speed at zero. Note this makes turning moot at 100 — the CC only steers while
	/// moving forward. Only the owning client simulates this.
	/// </summary>
	[Property, Range( 0f, 100f, clamped: true )] private float _SpeedPantsDownDisabler { get; set; } = 0f;

	/// <summary>
	/// While pants are Down, turn rate is multiplied by (1 + this FRACTION), no cap. This is NOT
	/// a 0-100 percentage like the speed knob above: 1.0 = +100% = doubled turn rate. Multiplies
	/// the target yaw rate, so it genuinely turns faster rather than just snapping to the same
	/// rate sooner. Only the owning client simulates this.
	/// </summary>
	[Property] private float _RotationPantsDownIncreaser { get; set; } = 1f;

	/// <summary>
	/// While pants are Down, jump force is cut by this percentage (0-100). 100 removes the jump
	/// entirely (the press is ignored — no animation trigger, no jump-gate latch). Only the
	/// owning client simulates this.
	/// </summary>
	[Property, Range( 0f, 100f, clamped: true )] private float _JumpPantsDownDisabler { get; set; } = 0f;

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
	/// can-jump check both use this downward trace. Small values are stricter about "_isGrounded".
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
	[Property] private float _BarSpawnNoKnockdownSeconds { get; set; } = 2;
	
	private CCCamera _ccCamera;
	private GameManager _gameManager;
	private bool _isGrounded;
	public float TimeSinceBarSpawn;

	/// <summary>
	/// The BeerLevel value difficulty effects should read: the raw level saturated at
	/// GameManager.BeerDifficultyCap (cap &lt;= 0 or no GameManager in scene = no cap).
	/// Difficulty sites (speed, lean impulse) use THIS;
	/// score/HUD readouts keep using the raw BeerLevel. Resolves _gameManager lazily so the
	/// cap works even before OnSpawnHelper's RPC has landed.
	/// </summary>
	private float DifficultyBeerHelper()
	{
		_gameManager ??= Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( _gameManager is null ) return BeerLevel;
		float cap = _gameManager.BeerDifficultyCap;
		return cap > 0f ? MathF.Min( BeerLevel, cap ) : BeerLevel;
	}

	/// <summary>
	/// True while the LOCAL client sits in the main menu (GameManager boots into
	/// InMainMenu — spawn-inert architecture). Resolves _gameManager lazily like
	/// DifficultyBeerHelper(). Fail-open: no GameManager in the scene means NOT in the
	/// menu (InMainMenu is only meaningful when a GameManager exists), so the body
	/// behaves exactly as before the migration when the gate source is missing.
	/// </summary>
	private bool InMainMenuHelper()
	{
		_gameManager ??= Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( _gameManager is null ) return false;
		return _gameManager.GetLocalGameState() == GameManager.LocalGameState.InMainMenu;
	}

	/// <summary>
	/// Sets the pants state. Owner-authoritative: only the owning client may write it (mirrors the
	/// IsProxy gates on the physics code); [Sync] on CurrentPantsState replicates the STATE alone
	/// to proxies so they can mimic the pants visuals. The movement tunables
	/// (_SpeedPantsDownDisabler / _RotationPantsDownIncreaser) never travel — proxies never
	/// simulate this body's physics. Gameplay triggers (obstacle drop, pull-up mash) call this;
	/// the pants visual system reads CurrentPantsState.
	/// </summary>
	public void SetPantsState( PantsState state )
	{
		if ( IsProxy )
			return;

		if ( CurrentPantsState == state )
			return;

		CurrentPantsState = state;
		Log.Info( $"DrunkCC: pants state -> {state}" );
	}

	protected override void OnStart()
	{
		WallHitColliderReporter.OnTriggerEnterCallback += OnWallHitColliderEnterHelper;
		WallHitColliderReporter.OnTriggerExitCallback += OnWallHitColliderExitHelper;
		_ccCamera = Scene.Scene.FindAllWithTagOrigin( "cccamera" ).FirstOrDefault()?.GetComponent<CCCamera>(); //SceneNetworkSystem Get<CCCamera>();
		if(_ccCamera == null)
		{
			Log.Warning( "DrunkCC: CCCamera not found!" );
		}
		// Owner-only spawn wiring. OnStart runs on EVERY client for EVERY DrunkCC (incl. remote
		// proxies), but OnSpawnHelper is [Rpc.Broadcast] and a broadcast SENDS on any local call
		// before any permission check (Rpc.InstanceRpc.cs:283-286 -> SendInstanceRpc:331-335). So
		// an ungated call here makes each proxy broadcast OnSpawnHelper(itsOwnConnection); the
		// owner then runs the body with the proxy's connection and dresses itself in the PROXY's
		// account clothing. Gating the call site (not just the body) is what actually stops that -
		// only the owner initiates, and its broadcast carries the right connection to everyone.
		if ( !IsProxy )
			OnSpawnHelper( Connection.Local );
		Log.Info( "~~~I AM ALIVE~~~~DrunkCC: OnStart" );
	}
	
	protected override void OnUpdate()
	{
		// Animgraph writes run on EVERY client (proxies need them to blend the remote body's
		// run/land poses). The camera writes are owner-only: _ccCamera is the one scene-wide
		// camera shared by all, so letting every proxy DrunkCC drive it means any remote
		// player's knockdown flips the LOCAL camera (and multiple remote knockdowns fight,
		// last-writer-wins). Only this client's own player should steer its camera. Null-guard
		// too: OnStart only warns if the cccamera tag is missing, so an ungated deref NREs.
		// While the local client is in the main menu the owned body is inert (spawn-inert), so
		// its camera-mode writes (UseAltTargets/StayBehind) are suppressed too — the menu owns
		// the screen until PLAY. Animgraph writes stay unconditional: proxies need them.
		bool driveCamera = !IsProxy && _ccCamera != null && !InMainMenuHelper();
		switch ( CurrentState )
		{
			case State.Running:
				_CitizenAnimationHelper.MoveStyle = CitizenAnimationHelper.MoveStyles.Run;
				_SkinnedModelRenderer.Set( "move_style", 2 );
				_SkinnedModelRenderer.Set( "move_x", 10000 );
				if ( driveCamera )
				{
					_ccCamera.UseAltTargets = false;
					_ccCamera.UseAltFollowSpeed = false;
					_ccCamera.UseAltLookAtSpeed = false;
					_ccCamera.UseAltTargets = false;
					_ccCamera.StayBehind = true;
				}
				break;
			case State.KnockedDown:
				if ( driveCamera )
				{
					_ccCamera.UseAltTargets = true;
					_ccCamera.UseAltFollowSpeed = true;
					_ccCamera.UseAltLookAtSpeed = true;
					_ccCamera.StayBehind = false;
				}
				break;
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( _Rigidbody == null ) 
			return;

		// Landing detection: once a jump has carried the body clear of the ground, the first time
		// we touch down again clears the jump gate so the next Jump press is allowed. Feeding
		// IsGroundedHelper here also lets the animgraph blend out of the jump/fall pose on landing.
		_isGrounded = IsGroundedHelper();
		_CitizenAnimationHelper.IsGrounded = _isGrounded;

		
		if ( IsProxy )
			return;

		// Spawn-inert body: while the local client sits in the main menu, this owned body runs
		// no owner physics — lean/jump input and the pants poll below never fire, and the
		// shared CCCamera is not driven (see the driveCamera gate in OnUpdate). The body just
		// rests at SPAWNLOCATION behind the menu backdrop until PLAY flips GameManager out of
		// InMainMenu. Deliberately placed AFTER the IsProxy early-out: the grounded/animgraph
		// lines above run for proxies too — they must keep blending remote bodies.
		if ( InMainMenuHelper() )
			return;

		// Pants control (Design #13-#16): HOLD Shift to keep them up; released = down to the
		// ankles. Polled every owner tick as a continuous hold (not an edge/press), so it tracks
		// the key state in both Running and KnockedDown and a player who recovers while still
		// holding Shift comes back with pants up. NOTE: the bound ACTION is "Run" (Shift is its
		// KeyboardCode, Input.config:39-44) - Input.Down takes the action NAME, and there is no
		// action literally called "Shift", so Input.Down("Shift") would warn and always read false.
		// SetPantsState early-returns when unchanged, so the per-tick call costs nothing on the wire.
		SetPantsState( Input.Down( "Run" ) ? PantsState.Up : PantsState.Down );

		if ( CurrentState == State.Running)
		{
				OwnerHandleRunningHelper();
				if ( Time.Now - TimeSinceBarSpawn > _BarSpawnNoKnockdownSeconds )
				{
					if ( _MaxHitRoll > 0f && MathF.Abs( _rollDeg ) > _MaxHitRoll )
					{
						OwnerEnterKnockedDownHelper();
					}
					else if ( _HasHitObstacle )
					{
						_HasHitObstacle = false;
						OwnerEnterKnockedDownHelper();
					}
				}
				else
				{
					// Bar-spawn grace: obstacle touches during the window must not latch, or the
					// stale flag fires a knockdown the instant grace expires, wherever the player
					// has driven to by then. Clearing here makes the obstacle check level-triggered
					// like the roll check above it.
					_HasHitObstacle = false;
				}
		}
		if(CurrentState == State.KnockedDown)
			HandleKnockedDownHelper();
	}

	private void OwnerHandleRunningHelper()
	{
		
		if(IsProxy)
			Log.Error("OwnerHandleRunningHelper should only be called by owner!");

		// Only record recovery history while _isGrounded. Airborne/off-map positions must never
		// become a stand-up target: falling off the map would otherwise poison the trail and
		// recovery would teleport the player to a point they were never validly standing on.
		if ( _isGrounded )
			RecordRunningHistoryHelper();
		if ( _isJumping )
		{
			if ( !_isGrounded )
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

		// Pants-down modifiers. Only the owner reaches here (IsProxy early-out above); proxies
		// just receive CurrentPantsState over [Sync] for visuals, never simulate it.
		float pantsSpeedScale = 1f;
		float pantsTurnScale = 1f;
		float pantsJumpScale = 1f;
		if ( CurrentPantsState == PantsState.Down )
		{
			pantsSpeedScale = 1f - ( _SpeedPantsDownDisabler / 100f ).Clamp( 0f, 1f );
			pantsTurnScale = 1f + MathF.Max( 0f, _RotationPantsDownIncreaser );
			pantsJumpScale = 1f - ( _JumpPantsDownDisabler / 100f ).Clamp( 0f, 1f );
		}

		// Rule 1 / 7: always running, always gaining speed up to the ceiling (reduced while pants are down).
		float speedCeiling = _VelocityCeiling * pantsSpeedScale;
		if ( _Rigidbody.Velocity.Length < speedCeiling )
		{
			float beerSpeedMultiplier = (_BeerToSpeedMultiplier + 1) * (DifficultyBeerHelper() + 1); // todo: consider better handle to edge case than " add 1" -ecs
			_Rigidbody.ApplyForce( _Rigidbody.WorldRotation.Forward * _RunningForce * beerSpeedMultiplier );
		}

		// Enforce the reduced forward cap while pants are down: strip forward velocity above it,
		// so X% means X% off top speed even when coasting (the movement sphere is frictionless, so
		// cutting thrust alone would never slow an already-moving body), and 100% means the
		// forward speed is actively held at zero.
		if ( pantsSpeedScale < 1f )
		{
			var fwd = _Rigidbody.WorldRotation.Forward;
			float fwdSpeed = Vector3.Dot( _Rigidbody.Velocity, fwd );
			if ( fwdSpeed > speedCeiling )
				_Rigidbody.Velocity -= fwd * ( fwdSpeed - speedCeiling );
		}

		// Lateral grip: oppose sideways velocity so the heading change from yaw actually turns
		// the path. Without this a sphere just keeps rolling along its old line.
		if ( _LateralGrip > 0f )
		{
			float lateralSpeed = Vector3.Dot( _Rigidbody.Velocity, _Rigidbody.WorldRotation.Left );
			_Rigidbody.ApplyForce( -_Rigidbody.WorldRotation.Left * lateralSpeed * _LateralGrip );
		}

		// Lean input: taps only, each tap is an angular impulse about the forward axis.
		// Rule 2 / 4: drunkenness multiplies the impulse, saturating at BeerDifficultyCap.
		// Sign mapping (Right -> -fwd) was verified in-editor with the previous torque version.
		float leanDir = 0f;
		if ( Input.Down( "Left" ) ) leanDir -= 1f;
		if ( Input.Down( "Right" ) )  leanDir += 1f;
		if ( leanDir != 0f )
		{
			float impulse = _LeanImpulse * ( 1f + DifficultyBeerHelper() * _DrunkLeanScale );
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
		float damping = _CorrectionDamping > 0f ? _CorrectionDamping : CriticalRollDampingHelper( _Rigidbody );
		_Rigidbody.ApplyTorque( _Rigidbody.WorldRotation.Forward * ( spring - rollRateDeg * damping ) );

		// Rule 3: yaw follows roll, linearly, while moving forward. Done as a rate-tracking torque
		// about the body's up axis rather than setting angular velocity. Standing still or moving
		// backward the target is zero, which also damps out residual yaw. Pants-down scales the
		// target rate, so the steady-state turn rate really is (1 + Y) times faster.
		float forwardSpeed = Vector3.Dot( _Rigidbody.Velocity, _Rigidbody.WorldRotation.Forward );
		// At a full stop (_SpeedPantsDownDisabler = 100 -> pantsSpeedScale exactly 0) the stripped
		// forward residual is float noise around zero whose sign flips per substep, which would make
		// the gate below flicker the yaw torque on/off nondeterministically. This CC's steering
		// model needs forward motion to turn at all, so a full stop deterministically means "not
		// moving forward" instead of reading the noisy dot.
		bool movingForward = pantsSpeedScale > 0f && forwardSpeed > 0f;
		float targetYawRateDeg = movingForward ? _rollDeg * _RollTurnRate * pantsTurnScale : 0f;
		_Rigidbody.ApplyTorque( _Rigidbody.WorldRotation.Up * ( targetYawRateDeg - yawRateDeg ) * _YawGain );

		ApplyDebugLocksHelper( _Rigidbody );

		// Pants-down can weaken or fully remove the jump (_JumpPantsDownDisabler = 100 -> scale 0
		// -> press ignored entirely: no force, no animation trigger, no jump-gate latch).
		if ( Input.Pressed( "Jump" ) && !_isJumping && _isGrounded && pantsJumpScale > 0f )
		{
			_Rigidbody.ApplyForce( _Rigidbody.WorldRotation.Up * _JumpForce * pantsJumpScale );
			_CitizenAnimationHelper.TriggerJump();
			_isJumping = true;
			_hasLeftGround = false;
		}
	}

	private void HandleKnockedDownHelper()
	{
		// Rule 6: ragdoll. No forces, no locks, physics owns the body until the timer runs out.
		if ( _knockdownEnds )
		{
			ResetFromKnockdownHelper();
		}
	}
	
	/// <summary>
	/// 
	/// </summary>
	/// <param name="recoveryQueueIndex">Automatically calculated by RecoveryQueueIndexHelper() unless passed.</param>
	private void OwnerEnterKnockedDownHelper(int? recoveryQueueIndex = null)
	{
		if(IsProxy)
			Log.Error("OwnerEnterKnockedDownHelper CALLED BY PROXY! THIS IS WRONG!");

		CurrentState = State.KnockedDown;
		_knockdownEnds = _KnockdownRecoveryTime;

		RagdollifyHelper();

		// Double-knockdown pushback: falling within _DoubleKnockdownDistance of where we last
		// fell means the recovery point we're about to pick is in the same stretch of road we
		// already failed to get out of, so drop the newest queue entry first. On the
		// beer-scaled path the entry index counts back from the newest, so dropping the top
		// shifts every candidate one sample further up the trail; repeated falls in the same
		// place keep walking it back. (No caller passes an explicit index anymore; if one
		// ever did, it would resolve against the post-removal queue like any other pick.)
		// Skipped when there's only one entry left: removing it would empty the queue, and the
		// empty-queue fallback stands you up exactly where you fell, which is the opposite of
		// further back.
		if ( _hasLastKnockdownPosition
			&& _history.Count > 1
			&& _DoubleKnockdownDistance > 0f
			&& _Rigidbody.WorldPosition.Distance( _lastKnockdownPosition ) <= _DoubleKnockdownDistance )
		{
			_history.RemoveAt( _history.Count - 1 );
		}

		// Record where THIS knockdown happened for the next one to compare against.
		_lastKnockdownPosition = _Rigidbody.WorldPosition;
		_hasLastKnockdownPosition = true;

		if(recoveryQueueIndex == null)
			recoveryQueueIndex = RecoveryQueueIndexHelper();
		
		// Recovery picks its queue entry by beer ratio: the drunker you are, the further back
		// down the queue (toward the oldest position) you get thrown. Short queues clamp to
		// the oldest available; an empty queue stands you up where you fell.
		var restore = GetRecoverySampleHelper( recoveryQueueIndex.Value );

		_knockdownRestorePosition = restore.Position + Vector3.Up * _RecoveryAdditionalHeight;
		_knockdownHeading = restore.Heading;
		if ( _knockdownHeading.IsNearlyZero() ) _knockdownHeading = Vector3.Forward;
	}

	[Rpc.Broadcast]
	private void RagdollifyHelper(bool undoRagdoll = false)
	{
		if ( undoRagdoll == false)
		{
			_Ragdoll.Mode = RagdollMode.Enabled;
			_Ragdoll.ApplyVelocity( _Rigidbody.Velocity );
		}
		else
		{
			_Ragdoll.Mode = RagdollMode.None;
		}

	}

	/// <summary>
	/// Target recovery index counted back from the newest queue entry (top): beer 0 = newest,
	/// beer at/above MaxBeerLevel = oldest. ratio = BeerLevel / MaxBeerLevel;
	/// index = Clamp(Floor(ratio * MaxQueuePositions), 0, MaxQueuePositions - 1).
	/// Uses the RAW BeerLevel (the spec's 'current beer level'); MaxBeerLevel is this system's
	/// own saturation, so DifficultyBeerHelper() is deliberately NOT used here.
	/// </summary>
	private int RecoveryQueueIndexHelper()
	{
		int max = Math.Max( 1, MaxQueuePositions );
		float ratio = MaxBeerLevel > 0f ? BeerLevel / MaxBeerLevel : 1f;
		return (int)Math.Clamp( MathF.Floor( ratio * max ), 0, max - 1 );
	}

	/// <summary>
	/// The sample indexFromNewest back from the top of the queue. Short queue clamps to the
	/// oldest available; empty queue falls back to the live transform (stand up where you fell).
	/// </summary>
	private HistorySample GetRecoverySampleHelper( int indexFromNewest )
	{
		if ( _history.Count == 0 )
		{
			var heading = _Rigidbody.WorldRotation.Forward.WithZ( 0f );
			return new HistorySample { Time = Time.Now, Position = _Rigidbody.WorldPosition, Heading = heading };
		}

		int idx = _history.Count - 1 - indexFromNewest;
		if ( idx < 0 ) idx = 0;
		return _history[idx];
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
	private Vector3 SnapRecoveryToNavMeshHelper( Vector3 pos )
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
				? snappedHere.Value + Vector3.Up * RecoveryStandOffsetHelper()
				: here;
		}

		return snapped.Value + Vector3.Up * RecoveryStandOffsetHelper();
	}

	/// <summary>
	/// Height of the rigidbody origin above the ground while standing on the movement sphere
	/// collider (Radius minus the collider's local Center.z). Lets the navmesh snap reproduce
	/// the standing pose instead of embedding the sphere.
	/// </summary>
	private float RecoveryStandOffsetHelper()
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
	private bool IsGroundedHelper()
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
	private void ResetFromKnockdownHelper()
	{
		RagdollifyHelper(true);
		if(IsProxy)
			return;
		
		Log.Info("Resetting From Knockdown.");
		
		var rb = _Rigidbody;

		// Snap the rewound point onto the navmesh if one exists. Quietly falls back to the raw
		// position when there's no mesh, it's disabled, or the point is beyond the search radius.
		rb.WorldPosition = SnapRecoveryToNavMeshHelper( _knockdownRestorePosition );
		rb.WorldRotation = Rotation.LookAt( _knockdownHeading.Normal, Vector3.Up );
		rb.Velocity = Vector3.Zero;
		rb.AngularVelocity = Vector3.Zero;
		CurrentState = State.Running;

		// A knockdown can interrupt a jump; clear the gate so recovery doesn't leave jumping stuck off.
		_isJumping = false;
		_hasLeftGround = false;
		
		// reset timer to give player travel time.
		_sinceLastSample = 0f;
	}

	/// <summary>
	/// Owner-side: drop recovery state that a teleport invalidates. Pre-teleport history
	/// samples would rewind across the map, and a stale last-knockdown position would
	/// trigger a false double-knockdown pushback at the new location. With the queue
	/// empty, a knockdown before the next sample accrues stands the player up where they
	/// fell (GetRecoverySampleHelper's live-transform fallback).
	/// </summary>
	public void InvalidateRecoveryStateHelper()
	{
		if ( IsProxy )
			return;
		_history.Clear();
		_hasLastKnockdownPosition = false;
		_sinceLastSample = 0f; // fresh post-teleport sampling window
	}

	/// <summary>
	/// Critical damping for the roll spring, in torque per deg/s. For a spring k (per radian) on
	/// inertia I, c = 2*sqrt(k*I). Converting both gains to per-degree gives Kd = 2*sqrt(Kp*I/57.3).
	/// Uses the inertia about the body's forward (local X) axis.
	/// </summary>
	private float CriticalRollDampingHelper( Rigidbody rb )
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
	private void ApplyDebugLocksHelper( Rigidbody rb )
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

	private void OnWallHitColliderEnterHelper( Collider other )
	{
		Log.Info("Istrigger: " + other.IsTrigger + "---OnWallHitColliderEnterHelper + " + other);

		if ( other.IsTrigger || CurrentState == State.KnockedDown)
			return;
		
		_HasHitObstacle = true;
		_hasCheckedHistObstacle = false;
	}
	
	private void OnWallHitColliderExitHelper( Collider other )
	{
		// Log.Info("OnWallHitColliderExitHelper + " + other);
	}

	/// <summary>
	/// Appends a position/heading sample to the recovery queue, gated by MinTimePerPosition
	/// and MinDistancePerPosition, and drops the oldest entries when MaxQueuePositions is
	/// exceeded. Called only while Running (grounded).
	/// </summary>
	private void RecordRunningHistoryHelper()
	{
		// No empty-history bypass: recording the instant the queue empties used to seed a
		// sample AT the recovery position right after ResetFromKnockdownHelper cleared it,
		// so a second knockdown before the next gate recovered to that same spot. The queue
		// now starts a full MinTimePerPosition after recovery (timer reset there); while
		// it's empty, GetRecoverySampleHelper falls back to the live transform, i.e.
		// stand up where you fell.
		if ( _sinceLastSample < MinTimePerPosition )
			return;

		// Distance gate: the queue must also spread out in space. Deferred until the time
		// gate passes so it never re-arms the timer — on a spacing miss _sinceLastSample
		// keeps counting, and the sample lands the instant spacing is reached. An empty
		// queue has nothing to measure against, so it records on the time gate alone.
		if ( _history.Count > 0 && _Rigidbody.WorldPosition.Distance( _history[^1].Position ) < MinDistancePerPosition )
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

		// Overflow drops the oldest/bottom entry. Max(1,...) so a 0/negative inspector
		// value can't empty the queue every tick and leave recovery with nothing to pick.
		while ( _history.Count > Math.Max( 1, MaxQueuePositions ) )
			_history.RemoveAt( 0 );
	}
	
	/// <summary>
	/// Spawn wiring, owner-initiated: the OnStart call site is gated `if (!IsProxy)`, because a
	/// [Rpc.Broadcast] SENDS on any local call before permission checks (Rpc.InstanceRpc.cs:283-
	/// 286) - an ungated proxy call would broadcast the proxy's own connection and the owner
	/// would dress itself in the proxy's account clothing. The IsProxy gate in the body is the
	/// second layer: it stops proxy-side broadcast executions (the owner's broadcast reaches
	/// every client) from re-dressing or re-registering with GameManager. The owner's execution
	/// broadcasts DressPlayerHelper, whose own [Rpc.Broadcast] carries the correct outfit to all
	/// clients.
	/// </summary>
	[Rpc.Broadcast]
	private void OnSpawnHelper(Connection connection)
	{
		if ( IsProxy )
			return;

		// DressPlayerHelper(GameObject, connection);

		_gameManager = Scene.Scene.FindAllWithTagOrigin( "gamemanager" ).FirstOrDefault()?.GetComponent<GameManager>();
		if ( _gameManager != null )
		{
			_gameManager.OnSpawn( connection, GameObject );
		}
		else
		{
			Log.Error( "GameManager not found, cannot apply account clothing." );
		}
	}
	
	/// <summary>
	/// Applies the owning player's account clothing (Steam avatar) to their own player body's
	/// Dresser. [Rpc.Broadcast] so every currently-connected client sees the same outfit on that
	/// player, mirroring the drinker-dressing pattern in Bar.cs's DressDrinkerHelper. Only the
	/// Clothing list comes from the account - Height/Age/Tint stay whatever the player prefab's
	/// Dresser was preset to (see Extensions.ApplyClothingOnlyAsync), so every player keeps the
	/// same body proportions regardless of their account avatar.
	/// </summary>
	[Rpc.Broadcast]
	private async void DressPlayerHelper( GameObject player, Connection playerConnection )
	{
		var dresser = player.GetComponentInChildren<Dresser>( true );
		if ( dresser is null || !dresser.BodyTarget.IsValid() )
		{
			Log.Error( "Player has no valid Dresser/BodyTarget, cannot apply account clothing." );
			return;
		}

		var clothing = ClothingContainer.CreateFromConnection( playerConnection );
		foreach ( var item in clothing.Clothing )
		{
			// if ( item.Clothing.SlotsOver.
		}
		await dresser.ApplyClothingOnlyAsync( clothing );
	}
}
