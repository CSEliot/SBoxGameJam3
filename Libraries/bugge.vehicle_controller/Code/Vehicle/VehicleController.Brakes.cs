using Sandbox;

namespace Bugges.VehicleController;

partial class VehicleController : Component
{
	private const string BRAKES = "Brakes";

	[Order( -6 )]
	[Property( Title = "Strength" ), Feature( BRAKES )]
	private float Brake_Strength { get; set; } = 40.0f;

	[Property, Feature( BRAKES )]
	private float Brakes_RollingFriction { get; set; } = 4.0f;

	[Property( Title = "Max Grip" ), Feature( BRAKES )]
	private float Brakes_SlidingFriction { get; set; } = 0.7f;


	private void ApplyBrake( Wheel wheel, SceneTraceResult trace )
	{
		if ( !wheel.IsBrake ) return;

		bool shouldApplyBrakes = BrakeInput || Gear_Current == Gears.Park;
		wheel.IsBraking = shouldApplyBrakes;

		if ( !shouldApplyBrakes ) return;
		if ( !trace.Hit ) return;

		Vector3 contactPoint = GetWheelContactPoint( wheel, trace );
		Vector3 brakeDir = wheel.Forward;
		Vector3 tireWorldVel = Body.GetVelocityAtPoint( contactPoint );
		float brakeVel = Vector3.Dot( brakeDir, tireWorldVel );
		float desiredVelChange = -brakeVel * wheel.Grip;
		float desiredAccel = desiredVelChange / Time.Delta;

		Body.ApplyForceAt( contactPoint, Brake_Strength * desiredAccel * brakeDir );
	}

	public bool IsBraking()
	{
		if ( BrakeInput ) return true;
		if ( Gear_Current == Gears.Park ) return true;
		return false;
	}

	private void ApplyRollingFriction( Wheel wheel, SceneTraceResult trace )
	{
		if ( !trace.Hit ) return;

		float absAccelerateInput = float.Abs( AccelerateInput );
		if ( absAccelerateInput > 0.1f ) return;

		Vector3 contactPoint = GetWheelContactPoint( wheel, trace );
		Vector3 frictionDir = wheel.Forward;
		Vector3 tireWorldVel = Body.GetVelocityAtPoint( contactPoint );
		float frictionVel = Vector3.Dot( frictionDir, tireWorldVel );
		float desiredVelChange = -frictionVel * wheel.Grip;
		float desiredAccel = desiredVelChange / Time.Delta;

		Body.ApplyForceAt( contactPoint, Brakes_RollingFriction * desiredAccel * frictionDir );
	}

	private void ApplyAntiSlip( Wheel wheel, SceneTraceResult trace )
	{
		if ( !trace.Hit ) return;

		Vector3 contactPoint = GetWheelContactPoint( wheel, trace );
		Vector3 steeringDir = wheel.Outwards;
		Vector3 forwardDir = wheel.Forward;
		Vector3 tireWorldVel = Body.GetVelocityAtPoint( contactPoint );

		float sidewaysVelocity = Vector3.Dot( steeringDir, tireWorldVel );
		float forwardVelocity = Vector3.Dot( forwardDir, tireWorldVel );

		float desiredVelChange = -sidewaysVelocity * wheel.Grip;
		float desiredAccel = desiredVelChange / Time.Delta;

		float restingMass = Body.Mass / Wheels.Length;
		float idealForceToStop = restingMass * desiredAccel;

		float slipAngle = float.Abs( float.Atan2( sidewaysVelocity, float.Abs( forwardVelocity ) + 0.1f ) );
		float gripCurve = CalculateSlipCurve( slipAngle );

		float baselineGravity = restingMass * 9.81f;
		float normalForce = baselineGravity + wheel.CurrentDownforce;

		float maxLateralGrip = normalForce * wheel.Grip * gripCurve;

		float lateralForce = float.Clamp( idealForceToStop, -maxLateralGrip, maxLateralGrip );

		Body.ApplyForceAt( contactPoint, steeringDir * lateralForce );
	}

	private float CalculateSlipCurve( float slipAngle )
	{
		float peakAngle = 0.2f;
		if ( slipAngle < peakAngle )
			return slipAngle / peakAngle;

		return float.Max( Brakes_SlidingFriction, 1.0f - (slipAngle - peakAngle) * 0.5f );
	}
}
