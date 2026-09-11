using System;

namespace Sandbox;

/// <summary>
/// Character controller for Drunk Player
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
	
	/// <summary>
	/// 
	/// </summary>
	[Property] public bool LockPitch { get; set; }
	/// <summary>
	/// 
	/// </summary>
	[Property] public bool LockYaw   { get; set; }
	/// <summary>
	/// 
	/// </summary>
	[Property] public bool LockRoll  { get; set; }
	
	/// <summary>
	/// How quickly the character corrects itself after drifting left or right.
	/// </summary>
	[Property] private float _State { get; set; }
	
	/// <summary>
	/// How quickly the character corrects itself after drifting left or right.
	/// </summary>
	[Property] private float _CorrectionStrength { get; set; } = 1;

	/// <summary>
	/// How far the character can roll before code will correct the roll.
	/// </summary>
	[Property] private float _RollResponseFloor { get; set; } = 1;

	/// <summary>
	/// Yaw rate (rad/s) per degree of roll while moving forward. Motorcycle-style steering.
	/// </summary>
	[Property] private float _RollTurnRate { get; set; } = 0.05f;
	
	/// <summary>
	/// How quickly the character slows down after rotating itself after drifting left or right.
	/// </summary>
	[Property] private float _RotationDampeningStrength { get; set; } = 1;
	
	/// <summary>
	/// How fast the character will eventually hit.
	/// </summary>
	[Property] private float _MaxRunningForce { get; set; } = 1;

	/// <summary>
	/// How fast the character will eventually hit.
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
	/// How quickly the character will recover after being knocked down.
	/// </summary>
	[Property] private float _KnockdownRecoveryTime { get; set; } = 1;
	
	/// <summary>
	/// How many beers the player has in them.
	/// </summary>
	[Property] private float _BeerLevel { get; set; } = 1;

	/// <summary>
	/// How strong the character turns when moving.
	/// </summary>
	[Property] private float _TurnForce { get; set; } = 1;

	/// <summary>
	/// How strong the character turns when moving.
	/// </summary>
	[Property] private float _SoberRate { get; set; } = 1;
	
	/// <summary>
	/// How strong the character turns when moving.
	/// </summary>
	[Property] private bool _CanSober { get; set; }

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
	/// The Max abs value of the roll where after 'hit' occurs.
	/// </summary>
	[Property] private float _MaxHitRoll { get; set; }
	
	/// <summary>
	/// Local Rigidbody Component
	/// </summary>
	[Property] private Rigidbody _RigidbodySphere { get; set; }
	
	protected override void OnStart()
	{

	}
	
	protected override void OnUpdate()
	{

	}

	protected override void OnFixedUpdate()
	{
		
		// Log.Info("Mass:" + _RigidbodySphere.Mass );
		
		Log.Info(_RigidbodySphere.Velocity.Length);
		float newForwardForce = MathX.Lerp(_RigidbodySphere.Velocity.Length, _MaxRunningForce, _RunningAcceleration );
		
		var facingDirection = _RigidbodySphere.WorldRotation.Forward;

		if( _RigidbodySphere.Velocity.Length < _VelocityCeiling )
			_RigidbodySphere.ApplyForce(facingDirection * _MaxRunningForce);// = (_RigidbodySphere.Velocity * WorldRotation.Forward) * newForwardForce;
		//todo: Don't use strings! -ecs
		// if ( Input.Down( "Forward" ) )
		// {
		// }
		// // if ( Input.Down( "Backward" ) )
		// {
		// 	var facingDirection = _RigidbodySphere.WorldRotation.Forward;
		// 	_RigidbodySphere.ApplyForce(facingDirection * _MaxRunningForce * -1);
		// }
		// Local Z axis (rigidbody's own up/turn axis) expressed in world space, since AngularVelocity is worldspace.
		var rot = _RigidbodySphere.WorldRotation;
		var fwd = rot.Forward;
		var up = rot.Up;

		// Roll measured geometrically: cross(up, worldUp) is the axis that rotates 'up' back toward
		// world up, so projecting it onto forward gives a signed roll that doesn't depend on Euler
		// sign conventions. Positive = leaning left (+Y), negative = leaning right.
		float rollSin = Vector3.Dot( Vector3.Cross( up, Vector3.Up ), fwd );
		float rollCos = Vector3.Dot( up, Vector3.Up );
		float rollDeg = MathF.Atan2( rollSin, rollCos ).RadianToDegree();

		// Lean input (motorcycle style): Left/Right roll the body about its own forward axis.
		// Positive rollDeg is a left lean, and +fwd torque rolls toward the right, so left input
		// torques along -fwd.
		float lean = 0f;
		if ( Input.Down( "Right" ) )  lean += 1f;
		if ( Input.Down( "Left" ) ) lean -= 1f;
		if ( lean != 0f )
		{
			_RigidbodySphere.ApplyTorque( -fwd * lean * _TurnForce );
		}

		// Roll corrector: torque back toward upright once roll exceeds _RollResponseFloor. Runs
		// even while leaning, so held input settles at the lean where _TurnForce balances the
		// correction rather than rolling over.
		if ( MathF.Abs( rollDeg ) > _RollResponseFloor )
		{
			// Error measured from the floor edge so response ramps smoothly instead of stepping.
			float error = rollDeg - MathF.Sign( rollDeg ) * _RollResponseFloor;
			_RigidbodySphere.ApplyTorque( fwd * error * _CorrectionStrength );
		}
		
		// World angular velocity -> local frame (relative to the sphere's own rotation, not this GameObject's)
		var localSphere = rot.Inverse * _RigidbodySphere.AngularVelocity;
		// var localBody = WorldRotation.Inverse * _RigidbodySphere.AngularVelocity;

		// Motorcycle turn: yaw rate is linear in roll while moving forward. Set (not added) so it
		// tracks the lean exactly instead of accumulating. Left lean (+roll) yaws left (+Z).
		float forwardSpeed = Vector3.Dot( _RigidbodySphere.Velocity, fwd );
		if ( forwardSpeed > 0f )
		{
			localSphere.z = rollDeg * _RollTurnRate;
		}
		else
		{
			localSphere.z = MathX.Lerp( localSphere.z, 0f, _RotationDampeningStrength ); //todo: if _RotationDampeningStrength is  > 1, it shouldn't make a difference but ot does -ecs
		}

		// Source convention: +X forward, +Y left, +Z up. Angular velocity components are spin
		// about those axes, so .x = roll (about forward), .y = pitch (about left), .z = yaw.
		if ( LockRoll )  localSphere.x = 0f;
		if ( LockPitch ) localSphere.y = 0f;
		if ( LockYaw )   localSphere.z = 0f;
		// if ( LockPitch ) localBody.x = 0f;
		// if ( LockRoll )  localBody.y = 0f;
		// if ( LockYaw )   localBody.z = 0f;

		// Back to world space
		_RigidbodySphere.AngularVelocity = rot * localSphere;
		// _RigidbodyBody.AngularVelocity = WorldRotation * locsalBody;

		// Zeroing AngularVelocity only stops *future* drift - it doesn't undo orientation that's
		// already accumulated (e.g. a rolling sphere constantly gets fed angular velocity by ground
		// friction before we get a chance to zero it). So also clamp the actual orientation directly
		// on the locked axes every tick.
		if ( LockPitch || LockYaw || LockRoll )
		{
			var angles = _RigidbodySphere.WorldRotation.Angles();
			if ( LockPitch ) angles.pitch = 0f;
			if ( LockYaw )   angles.yaw   = 0f;
			if ( LockRoll )  angles.roll  = 0f;
			_RigidbodySphere.WorldRotation = angles.ToRotation();
		}
	}
}
