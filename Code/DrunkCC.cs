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
	/// How fast the character will eventually hit.
	/// </summary>
	[Property] private float _MaxRunningForce { get; set; } = 1;

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
	[Property] private Rigidbody _RigidbodyBody { get; set; }

	
	protected override void OnStart()
	{
		_RigidbodySphere = GetComponent<Rigidbody>();
		_RigidbodyBody = GetComponentInChildren<Rigidbody>();
	}
	
	protected override void OnUpdate()
	{

	}

	protected override void OnFixedUpdate()
	{
		//todo: Don't use strings! -ecs
		if ( Input.Down( "Forward" ) )
		{
			Log.Info(_RigidbodySphere.Velocity.Length);
			float newForwardForce = MathX.Lerp(_RigidbodySphere.Velocity.Length, _MaxRunningForce, _RunningAcceleration );
			
			var facingDirection = _RigidbodySphere.WorldRotation.Forward;
		
			_RigidbodySphere.ApplyForce(facingDirection * _MaxRunningForce);// = (_RigidbodySphere.Velocity * WorldRotation.Forward) * newForwardForce;
		}
		if ( Input.Down( "Backward" ) )
		{
			var facingDirection = _RigidbodySphere.WorldRotation.Forward;
			_RigidbodySphere.ApplyForce(facingDirection * _MaxRunningForce * -1);
		}
		if ( Input.Down( "Left" ) )
		{
			// _RigidbodySphere.AngularVelocity += _RigidbodySphere.AngularVelocity.WithY( _TurnForce );
			_RigidbodySphere.AngularVelocity += _RigidbodySphere.AngularVelocity.WithZ( _TurnForce );
		}
		
		if ( Input.Down( "Right" ) )
		{
			// _RigidbodySphere.AngularVelocity -= _RigidbodySphere.AngularVelocity.WithY( _TurnForce );
			_RigidbodySphere.AngularVelocity -= _RigidbodySphere.AngularVelocity.WithZ( _TurnForce );
		}
		
		// World angular velocity -> local frame
		var local = WorldRotation.Inverse * _RigidbodySphere.AngularVelocity;

		if ( LockPitch ) local.x = 0f;
		if ( LockRoll )  local.y = 0f;
		if ( LockYaw )   local.z = 0f;

		// Back to world space
		_RigidbodySphere.AngularVelocity = WorldRotation * local;

	}
}
