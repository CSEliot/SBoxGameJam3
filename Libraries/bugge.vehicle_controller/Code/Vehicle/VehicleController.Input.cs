using Sandbox;

namespace Bugges.VehicleController;

partial class VehicleController : Component
{
	private const string INPUT = "Input";

	[Order( -1 )]
	[Property( Title = "Toggle Engine" ), Feature( INPUT ), InputAction]
	private string Input_Engine { get; set; } = "use";

	[Property( Title = "Brake" ), Feature( INPUT ), InputAction]
	private string Input_Brake { get; set; } = "jump";

	[Property( Title = "Switch Gear" ), Feature( INPUT ), InputAction]
	private string Input_Gear { get; set; } = "menu";

	/// <summary>When true, every input getter reads the External* members below instead of the player's keyboard/controller.</summary>
	[Property( Title = "Use External Input" ), Feature( INPUT )]
	public bool UseExternalInput { get; set; } = false;

	/// <summary>External throttle, -1..1, negative = reverse.</summary>
	[Hide] public float ExternalThrottle { get; set; }

	/// <summary>External steering, -1..1, POSITIVE = LEFT (same sign as Input.AnalogMove.y).</summary>
	[Hide] public float ExternalSteer { get; set; }

	/// <summary>External brake, true = braking.</summary>
	[Hide] public bool ExternalBrake { get; set; }

	private bool _externalEngineStartPending;

	/// <summary>Requests an engine start, consumed by the next EngineInput read on the external path. No effect while the engine is already on, so it can never switch a running engine off.</summary>
	public void RequestEngineStart() => _externalEngineStartPending = true;

	/// <summary>Engine toggle. On the external path this returns the pending start request (only while the engine is off) and clears it; read once per frame at VehicleController.Engine.cs:110 inside UpdateEngine.</summary>
	public bool EngineInput
	{
		get
		{
			if ( UseExternalInput )
			{
				bool pending = _externalEngineStartPending;
				_externalEngineStartPending = false;
				return pending && !IsEngineOn;
			}

			return Input.Pressed( Input_Engine );
		}
	}

	public bool GearInput => UseExternalInput ? false : Input.Pressed( Input_Gear );

	public bool BrakeInput => UseExternalInput ? ExternalBrake : Input.Down( Input_Brake );

	public float TurnInput => UseExternalInput ? float.Clamp( ExternalSteer, -1f, 1f ) : Input.AnalogMove.y;

	public float AccelerateInput
	{
		get
		{
			if ( UseExternalInput ) return float.Clamp( ExternalThrottle, -1f, 1f );

			float trigger = Input.GetAnalog( InputAnalog.RightTrigger ) - Input.GetAnalog( InputAnalog.LeftTrigger );
			if ( Input.UsingController || trigger > 0.1f ) return trigger;

			float ws = Input.Down( "forward" ) ? 1 : 0 - (Input.Down( "backward" ) ? 1 : 0);
			return ws;
		}
	}
}
