using System.Linq;
using Sandbox;

namespace Bugges.VehicleController;

partial class VehicleController : Component
{
	public const string WHEELS = "Wheels";
	public enum WheelCastType { Ray, Sphere, Cylinder }

	[Order( -8 )]
	[Property( Name = "Cast Type" ), Feature( WHEELS )]
	private WheelCastType Wheel_CastType { get; set; } = WheelCastType.Cylinder;

	[Property( Name = "Max Turn Angle" ), Feature( WHEELS )]
	private float Wheel_MaxTurnAngle { get; set; } = 30.0f;

	[Property, Feature( WHEELS )]
	private bool Wheel_VisualizeTrace { get; set; } = false;

	[Feature( WHEELS ), InlineEditor, WideMode, Property]
	private Wheel[] Wheels { get; set; }


	private float GetAverageDrivenWheelRPS( out bool anyGrounded )
	{
		anyGrounded = false;
		float totalAngularVelocity = 0f;
		int motorCount = 0;

		foreach ( var wheel in Wheels )
		{
			if ( !wheel.IsMotor ) continue;
			motorCount++;
			totalAngularVelocity += wheel.SpinSpeed / (2f * float.Pi);

			if ( wheel.IsGrounded )
				anyGrounded = true;
		}

		return motorCount > 0 ? totalAngularVelocity / motorCount : 0f;
	}

	private void ApplyTurn( Wheel wheel )
	{
		if ( !wheel.IsTurnable ) return;

		float speed = Vector3.Dot( WorldTransform.Forward, Body.Velocity );
		float normalizedSpeed = float.Clamp( float.Abs( speed ) / Engine_TopSpeed, 0.0f, 1.0f );
		float turnMultiplier = float.Pow( 1.0f - normalizedSpeed, 2 );
		float rawInput = TurnInput;
		float turnInput = float.Sign( rawInput ) * rawInput * rawInput;
		wheel.TurnAngle = turnInput * Wheel_MaxTurnAngle * turnMultiplier;
	}

	private void UpdateWheelSpin( Wheel wheel, SceneTraceResult trace )
	{
		if ( wheel.IsBraking )
		{
			wheel.SpinSpeed += (0f - wheel.SpinSpeed) * Time.Delta * 10f;
			return;
		}

		if ( trace.Hit )
		{
			Vector3 contactPoint = GetWheelContactPoint( wheel, trace );
			Vector3 tireWorldVel = Body.GetVelocityAtPoint( contactPoint );

			float forwardVelocity = Vector3.Dot( wheel.Forward, tireWorldVel );
			// Convert linear speed to angular velocity (radians per second)
			wheel.SpinSpeed = forwardVelocity / wheel.Radius;
			return;
		}

		if ( wheel.IsMotor && IsEngineOn && (Gear_Current == Gears.Drive || Gear_Current == Gears.Reverse) )
		{
			float gearMult = Gear_CurrentRatio * Gear_FinalDriveRatio;
			if ( Gear_Current == Gears.Reverse ) gearMult *= -1f;

			float targetSpinRpm = gearMult != 0f ? CurrentRPM / gearMult : 0f;
			float targetSpinAngularVel = targetSpinRpm / 60f * (2f * float.Pi);

			wheel.SpinSpeed += (targetSpinAngularVel - wheel.SpinSpeed) * Time.Delta * 5f;
			return;
		}

		wheel.SpinSpeed += (0f - wheel.SpinSpeed) * Time.Delta * 1f;
	}

	private SceneTraceResult Trace( Wheel wheel )
	{
		SceneTraceResult trace = Wheel_CastType switch
		{
			WheelCastType.Cylinder => CylinderTrace( wheel ),
			WheelCastType.Sphere => SphereTrace( wheel ),
			_ => RayTrace( wheel )
		};

		return trace;
	}

	private SceneTraceResult RayTrace( Wheel wheel )
	{
		Vector3 springDown = wheel.Spring.WorldTransform.Down;
		Vector3 springPos = wheel.Spring.WorldPosition;
		var wheelRay = new Ray( springPos, springDown );
		return Scene.Trace
			.Ray( wheelRay, Spring_MaxDistance + wheel.Radius )
			.IgnoreGameObject( GameObject )
			.IgnoreGameObject( wheel.Visual )
			.Run();
	}

	private SceneTraceResult SphereTrace( Wheel wheel )
	{
		Vector3 springDown = wheel.Spring.WorldTransform.Down;
		Vector3 springPos = wheel.Spring.WorldPosition + springDown * wheel.Radius;
		var wheelRay = new Ray( springPos, springDown );

		return Scene.Trace
			.Sphere( wheel.Radius, wheelRay, Spring_MaxDistance - wheel.Radius )
			.IgnoreGameObject( GameObject )
			.IgnoreGameObject( wheel.Visual )
			.Run();
	}

	private SceneTraceResult CylinderTrace( Wheel wheel )
	{
		Vector3 springDown = wheel.Spring.WorldTransform.Down;
		Vector3 springPos = wheel.Spring.WorldPosition + springDown * wheel.Radius;
		var wheelRay = new Ray( springPos, springDown );

		Rotation cylinderRotation = wheel.SteerRotation * Rotation.FromAxis( Vector3.Forward, 90f );
		return Scene.Trace
			.Cylinder( wheel.Width, wheel.Radius, wheelRay, Spring_MaxDistance - wheel.Radius )
			.Rotated( cylinderRotation )
			.IgnoreGameObject( GameObject )
			.IgnoreGameObject( wheel.Visual )
			.Run();
	}

	private float GetTraceDistance( Wheel wheel, SceneTraceResult trace )
	{
		return Wheel_CastType == WheelCastType.Ray
			? float.Max( trace.Distance - wheel.Radius, 0.0f )
			: trace.Distance + wheel.Radius;
	}

	private Vector3 GetWheelContactPoint( Wheel wheel, SceneTraceResult trace )
	{
		float traceDistance = GetTraceDistance( wheel, trace );
		return wheel.Spring.WorldPosition + wheel.Down * traceDistance;
	}
}
