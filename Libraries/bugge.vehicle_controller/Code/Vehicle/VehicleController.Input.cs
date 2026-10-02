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

	public bool EngineInput => Input.Pressed( Input_Engine );

	public bool GearInput => Input.Pressed( Input_Gear );

	public bool BrakeInput => Input.Down( Input_Brake );

	public float TurnInput => Input.AnalogMove.y;

	public float AccelerateInput
	{
		get
		{
			float trigger = Input.GetAnalog( InputAnalog.RightTrigger ) - Input.GetAnalog( InputAnalog.LeftTrigger );
			if ( Input.UsingController || trigger > 0.1f ) return trigger;

			float ws = Input.Down( "forward" ) ? 1 : 0 - (Input.Down( "backward" ) ? 1 : 0);
			return ws;
		}
	}
}
