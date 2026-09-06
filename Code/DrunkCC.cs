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
	[Property] private float _MaxRunningSpeed { get; set; } = 1;
	
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
	[Property] private float _TurnRadius { get; set; } = 1;

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
	[Property] private Rigidbody _Rigidbody { get; set; }

	protected override void OnStart()
	{
		_Rigidbody = GetComponent<Rigidbody>();
	}

	protected override void OnUpdate()
	{

		//todo: Don't use strings! -ecs
		if ( Input.Down( "Forward" ) )
		{
			_Rigidbody.Velocity += _Rigidbody.Velocity.WithY( _MaxRunningSpeed );
		}
		if ( Input.Down( "Backward" ) )
		{
			_Rigidbody.Velocity -= _Rigidbody.Velocity.WithY( _MaxRunningSpeed );
		}
		if ( Input.Down( "Left" ) )
		{
			_Rigidbody.Velocity -= _Rigidbody.Velocity.WithX( _MaxRunningSpeed );
		}
		
		if ( Input.Down( "Right" ) )
		{
			_Rigidbody.Velocity += _Rigidbody.Velocity.WithX( _MaxRunningSpeed );
		}
		
	}
}
