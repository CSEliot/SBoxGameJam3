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
	private enum State
	{
		Running,
		KnockedDown,
	}

	private State _CurrentState = State.Running;

	/// <summary>
	/// Counts down while knocked down. Reset happens when it reaches zero.
	/// </summary>
	private TimeUntil _KnockdownEnds;

	/// <summary>
	/// At knockdown, get heading direction.
	/// </summary>
	private Vector3 _KnockdownHeading;

	/// <summary>
	/// At knockdown, get heading direction.
	/// </summary>
	private Vector3 _KnockdownRestorePosition;

	
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
	/// Sideways force per unit of lateral velocity. Makes the sphere carve with its heading
	/// instead of sliding on its old line. 0 = ice.
	/// </summary>
	[Property] private float _LateralGrip { get; set; } = 1f;
	
	/// <summary>
	/// Constant forward drive while below _VelocityCeiling.
	/// </summary>
	[Property] private float _MaxRunningForce { get; set; } = 1;

	/// <summary>
	/// Drive cuts out above this speed.
	/// </summary>
	[Property] private float _VelocityCeiling { get; set; } = 1;
	
	/// <summary>
	/// How much effort into hitting max speed.
	/// </summary>
	[Property, Range(0, 1)] private float _RunningAcceleration { get; set; } = 1;
	
	/// <summary>
	/// How strong the character will jump.
	/// </summary>
	[Property] private float _JumpForce { get; set; } = 1;
	
	/// <summary>
	/// Seconds spent ragdolling after a knockdown before reset.
	/// </summary>
	[Property] private float _KnockdownRecoveryTime { get; set; } = 1;
	
	/// <summary>
	/// How many beers the player has in them. No maximum.
	/// </summary>
	[Property] private float _BeerLevel { get; set; } = 1;
	
	/// <summary>
	/// The acute angle at which the forward ray cast will trigger a "hasHitObstacle" event.
	/// </summary>
	[Property] private float _WillCollideApproachAngle { get; set; }

	/// <summary>
	/// The acute angle at which the forward ray cast will trigger a "hasHitObstacle" event.
	/// </summary>
	[Property] private bool _HasHitObstacle { get; set; }
	
	/// <summary>
	/// The source of the forward ray cast.
	/// </summary>
	[Property] private float _ForwardRayCastPosition { get; set; }

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
	
	protected override void OnStart()
	{
	}
	
	protected override void OnUpdate()
	{
		_CitizenAnimationHelper.MoveStyle = CitizenAnimationHelper.MoveStyles.Run;
		_SkinnedModelRenderer.Set( "move_style", 2 );
		_SkinnedModelRenderer.Set( "move_x", 10000 );
		Log.Info(_CitizenAnimationHelper.MoveStyle);
	}

	protected override void OnFixedUpdate()
	{
		if ( _Rigidbody == null ) return;

		switch ( _CurrentState )
		{
			case State.Running:
				TickRunning();
				break;
			case State.KnockedDown:
				TickKnockedDown();
				break;
		}
	}

	private void TickRunning()
	{
		var rb = _Rigidbody;
		var rot = rb.WorldRotation;
		var fwd = rot.Forward;
		var up = rot.Up;
		var left = rot.Left;

		// Roll measured geometrically: cross(up, worldUp) is the axis that rotates 'up' back toward
		// world up, so projecting it onto forward gives a signed roll that doesn't depend on Euler
		// sign conventions. Sign: matches the +fwd torque direction, so +K*roll along fwd restores.
		// Verified in-editor with the earlier torque version.
		float rollSin = Vector3.Dot( Vector3.Cross( up, Vector3.Up ), fwd );
		float rollCos = Vector3.Dot( up, Vector3.Up );
		float rollDeg = MathF.Atan2( rollSin, rollCos ).RadianToDegree();

		// Rule 5: leaned too far -> knocked down.
		if ( _MaxHitRoll > 0f && MathF.Abs( rollDeg ) > _MaxHitRoll )
		{
			EnterKnockedDown();
			return;
		}

		// Spin in the body's own frame. Source convention: +X forward, +Y left, +Z up, so
		// .x = roll rate (about forward), .y = pitch rate, .z = yaw rate. Radians/s.
		var localAV = rot.Inverse * rb.AngularVelocity;
		float rollRateDeg = localAV.x.RadianToDegree();
		float yawRateDeg  = localAV.z.RadianToDegree();

		// Rule 1 / 7: always running, always gaining speed up to the ceiling.
		if ( rb.Velocity.Length < _VelocityCeiling )
		{
			rb.ApplyForce( fwd * _MaxRunningForce );
		}

		// Lateral grip: oppose sideways velocity so the heading change from yaw actually turns
		// the path. Without this a sphere just keeps rolling along its old line.
		if ( _LateralGrip > 0f )
		{
			float lateralSpeed = Vector3.Dot( rb.Velocity, left );
			rb.ApplyForce( -left * lateralSpeed * _LateralGrip );
		}

		// Lean input: taps only, each tap is an angular impulse about the forward axis.
		// Rule 2 / 4: drunkenness multiplies the impulse with no cap.
		// Sign mapping (Right -> -fwd) was verified in-editor with the previous torque version.
		float leanDir = 0f;
		if ( Input.Pressed( "Left" ) ) leanDir -= 1f;
		if ( Input.Pressed( "Right" ) )  leanDir += 1f;
		if ( leanDir != 0f )
		{
			float impulse = _LeanImpulse * ( 1f + _BeerLevel * _DrunkLeanScale );
			rb.PhysicsBody?.ApplyAngularImpulse( fwd * leanDir * impulse );
		}

		// Upright controller: spring on roll (outside the floor) plus damper on roll rate (always).
		// Positive roll (left lean) needs +fwd torque to come back, so the spring is +K*roll.
		// The damper opposes whatever spin about fwd exists, so it is -Kd*rate.
		float spring = 0f;
		if ( MathF.Abs( rollDeg ) > _RollResponseFloor )
		{
			// Error measured from the floor edge so response ramps smoothly instead of stepping.
			float error = rollDeg - MathF.Sign( rollDeg ) * _RollResponseFloor;
			spring = error * _CorrectionStrength;
		}
		float damping = _CorrectionDamping > 0f ? _CorrectionDamping : CriticalRollDamping( rb );
		rb.ApplyTorque( fwd * ( spring - rollRateDeg * damping ) );

		// Rule 3: yaw follows roll, linearly, while moving forward. Done as a rate-tracking torque
		// about the body's up axis rather than setting angular velocity. Standing still or moving
		// backward the target is zero, which also damps out residual yaw.
		float forwardSpeed = Vector3.Dot( rb.Velocity, fwd );
		float targetYawRateDeg = forwardSpeed > 0f ? rollDeg * _RollTurnRate : 0f;
		rb.ApplyTorque( up * ( targetYawRateDeg - yawRateDeg ) * _YawGain );

		ApplyDebugLocks( rb );
	}

	private void TickKnockedDown()
	{
		// Rule 6: ragdoll. No forces, no locks, physics owns the body until the timer runs out.
		if ( _KnockdownEnds )
		{
			ResetFromKnockdown();
		}
	}

	private void EnterKnockedDown()
	{
		_CurrentState = State.KnockedDown;
		_KnockdownEnds = _KnockdownRecoveryTime;
		_Ragdoll.Mode = RagdollMode.Enabled;
		_Ragdoll.ApplyVelocity( _Rigidbody.Velocity );
		
		_KnockdownHeading = _Rigidbody.WorldRotation.Forward.WithZ( 0f );
		if ( _KnockdownHeading.IsNearlyZero() ) _KnockdownHeading = _Rigidbody.WorldRotation.Up.WithZ( 0f );
		if ( _KnockdownHeading.IsNearlyZero() ) _KnockdownHeading = Vector3.Forward;

	}

	/// <summary>
	/// Rule 7: stand back up where we fell, facing the same way, at zero velocity.
	/// </summary>
	private void ResetFromKnockdown()
	{
		var rb = _Rigidbody;

		// Heading from the forward vector flattened onto the ground plane. Euler Yaw() is not
		// trustworthy on a body that ragdolled past 90 degrees of pitch or roll.

		rb.WorldRotation = Rotation.LookAt( _KnockdownHeading.Normal, Vector3.Up );
		rb.Velocity = Vector3.Zero;
		rb.AngularVelocity = Vector3.Zero;
		_CurrentState = State.Running;
		_Ragdoll.Mode = RagdollMode.None;
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
		var localAV = rot.Inverse * rb.AngularVelocity;
		if ( LockRoll )  localAV.x = 0f;
		if ( LockPitch ) localAV.y = 0f;
		if ( LockYaw )   localAV.z = 0f;
		rb.AngularVelocity = rot * localAV;

		// Zeroing AngularVelocity only stops *future* drift - it doesn't undo orientation that's
		// already accumulated (e.g. a rolling sphere constantly gets fed angular velocity by ground
		// friction before we get a chance to zero it). So also clamp the actual orientation.
		var angles = rot.Angles();
		if ( LockPitch ) angles.pitch = 0f;
		if ( LockYaw )   angles.yaw   = 0f;
		if ( LockRoll )  angles.roll  = 0f;
		rb.WorldRotation = angles.ToRotation();
	}
}
