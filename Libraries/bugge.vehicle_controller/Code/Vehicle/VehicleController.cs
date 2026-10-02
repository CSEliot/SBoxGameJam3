using System;
using Sandbox;

namespace Bugges.VehicleController;

[Icon( "directions_car" )]
public sealed partial class VehicleController : Component
{
	// Events
	public event Action<int> OnGearChanged;
	public event Action<bool> OnEngineStateChanged;
	public event Action OnEngineStart;
	public event Action OnEngineStop;

	[RequireComponent]
	private Rigidbody Body { get; set; }


	protected override void OnAwake()
	{
		InitAudio();
		Body = GetComponent<Rigidbody>();
	}

	protected override void OnUpdate()
	{
		UpdateEngine();
		SwitchGear();

		foreach ( var wheel in Wheels )
		{
			var trace = Trace( wheel );
			wheel.IsGrounded = trace.Hit;

			UpdateWheelSpin( wheel, trace );
			UpdateTireMarks( wheel, trace );
			UpdateSkidAudio( wheel, trace );
			Visualize( wheel, trace );
		}

		UpdateGears();
		UpdateEngineAudio();
	}

	protected override void OnFixedUpdate()
	{
		foreach ( var wheel in Wheels )
		{
			var trace = Trace( wheel );
			wheel.IsGrounded = trace.Hit;

			ApplyTurn( wheel );
			ApplySpring( wheel, trace );
			ApplyAntiSlip( wheel, trace );
			ApplyAcceleration( wheel, trace );
			ApplyRollingFriction( wheel, trace );
			ApplyBrake( wheel, trace );
		}
	}

	private void Visualize( Wheel wheel, SceneTraceResult trace )
	{
		float wheelAngleAdd = float.RadiansToDegrees( wheel.SpinSpeed ) * Time.Delta;
		wheel.WheelAngle += wheel.IsRotationReversed ? -wheelAngleAdd : wheelAngleAdd;

		GameObject visual = wheel.Visual;
		visual.WorldRotation = wheel.Rotation;

		Vector3 springDown = wheel.Down;
		Vector3 springPos = wheel.Spring.WorldPosition;

		if ( Wheel_VisualizeTrace )
			DebugOverlay.Trace( trace, overlay: true );

		if ( trace.Hit )
		{
			float traceDistance = GetTraceDistance( wheel, trace );
			float visualDistance = float.Max( traceDistance, Spring_MinDistance );
			visual.WorldPosition = springPos + springDown * visualDistance;
			return;
		}

		visual.WorldPosition = springPos + springDown * Spring_MaxDistance;
	}
}
