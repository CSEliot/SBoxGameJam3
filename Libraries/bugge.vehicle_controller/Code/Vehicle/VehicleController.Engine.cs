using Sandbox;

namespace Bugges.VehicleController;

partial class VehicleController : Component
{
	public const string ENGINE = "Engine";
	public const string RPM = "RPM";

	[Order( -9 )]
	[Property, Feature( ENGINE ), ReadOnly]
	public float Kmh
	{
		get
		{
			Vector3 forward = WorldTransform.Forward;
			Vector3 velocity = Body.Velocity;
			float msToKmh = 1f / 100.0f * 3.6f;
			float ms = Vector3.Dot( forward, velocity );
			float kmh = ms * msToKmh;
			return kmh;
		}
	}

	[Property( Title = "Top Speed" ), Feature( ENGINE )]
	private float Engine_TopSpeed { get; set; } = 3600.0f;

	[Property( Title = "Torque" ), Feature( ENGINE )]
	private float Engine_Torque { get; set; } = 20000.0f;

	[Property( Title = "Start Delay" ), Feature( ENGINE )]
	private float Engine_StartDelay { get; set; } = 0.5f;

	[Property( Title = "Idle RPM" ), Feature( ENGINE ), Group( RPM )]
	public float Engine_IdleRPM { get; set; } = 800f;

	[Property( Title = "Low RPM" ), Feature( ENGINE ), Group( RPM )]
	public float Engine_LowRPM { get; set; } = 1000f;

	[Property( Title = "High RPM" ), Feature( ENGINE ), Group( RPM )]
	public float Engine_HighRPM { get; set; } = 2000f;

	[Property( Title = "Max RPM" ), Feature( ENGINE ), Group( RPM )]
	public float Engine_MaxRPM { get; set; } = 7000f;

	[Property( Title = "Smoothing" ), Feature( ENGINE ), Group( RPM ), Description( "How quickly the engine's audio RPM catches up to the actual wheel RPM" )]
	public float Engine_RPMSmoothing { get; set; } = 8f;

	[Property, Feature( ENGINE ), Group( RPM ), ReadOnly]
	public float CurrentRPM { get; private set; }

	private bool _isEngineOn = false;
	[Property, Feature( ENGINE ), ReadOnly]
	public bool IsEngineOn
	{
		get => _isEngineOn;
		private set
		{
			bool isSameAsValue = _isEngineOn == value;
			if ( isSameAsValue ) return;

			_isEngineOn = value;
			OnEngineStateChanged?.Invoke( value );

			if ( value ) OnEngineStart?.Invoke();
			else OnEngineStop?.Invoke();
		}
	}


	private void ApplyAcceleration( Wheel wheel, SceneTraceResult trace )
	{
		bool isAccelerating = true;
		if ( !trace.Hit ) isAccelerating = false;
		if ( !IsEngineOn ) isAccelerating = false;
		if ( !wheel.IsMotor ) isAccelerating = false;
		if ( IsBraking() ) isAccelerating = false;
		if ( Gear_Current == Gears.Neutral ) isAccelerating = false;

		wheel.IsAccelerating = isAccelerating;
		if ( !isAccelerating ) return;

		float accelerateInput = AccelerateInput;
		float absAccelerateInput = float.Abs( accelerateInput );
		if ( absAccelerateInput <= 0.1f ) return;

		Vector3 contactPoint = GetWheelContactPoint( wheel, trace );
		Vector3 accelDir = wheel.Forward;
		float gearMultiplier = Gear_CurrentRatio * Gear_FinalDriveRatio;
		float availableTorque = accelerateInput * Engine_Torque * float.Abs( gearMultiplier );
		Body.ApplyForceAt( contactPoint, accelDir * availableTorque );
	}

	public bool IsAccelerating()
	{
		if ( !IsEngineOn ) return false;
		if ( IsBraking() ) return false;
		if ( Gear_Current == Gears.Neutral ) return false;

		float absAccelInput = float.Abs( AccelerateInput );
		if ( absAccelInput <= 0.1f ) return false;

		return true;
	}

	private void UpdateEngine()
	{
		UpdateEngineRPM();

		if ( !EngineInput ) return;
		if ( IsEngineOn ) StopEngine();
		else StartEngine();
	}

	private async void StartEngine()
	{
		await Task.DelaySeconds( Engine_StartDelay );
		IsEngineOn = true;
	}

	private void StopEngine() => IsEngineOn = false;

	private void UpdateEngineRPM()
	{
		if ( !IsEngineOn )
		{
			CurrentRPM = 0f;
			return;
		}

		float targetRpm = CalculateTargetRPM();
		CurrentRPM += (targetRpm - CurrentRPM) * Time.Delta * 5f;
		CurrentRPM = float.Clamp( CurrentRPM, Engine_IdleRPM, Engine_MaxRPM );
	}

	private float CalculateTargetRPM()
	{
		float absAccelerateInput = float.Abs( AccelerateInput );
		bool inGear = Gear_Current == Gears.Drive || Gear_Current == Gears.Reverse;

		if ( !inGear )
			return Engine_IdleRPM + (absAccelerateInput * (Engine_MaxRPM - Engine_IdleRPM));

		float avgMotorSpeed = GetAverageDrivenWheelRPS( out bool anyGrounded );
		if ( !anyGrounded )
			return Engine_IdleRPM + (absAccelerateInput * (Engine_MaxRPM - Engine_IdleRPM));

		float driveAxleRpm = avgMotorSpeed * 60f;
		float targetRpm = float.Clamp( float.Abs( driveAxleRpm * Gear_FinalDriveRatio * Gear_CurrentRatio ), Engine_IdleRPM, Engine_MaxRPM );

		// Add a slight RPM bump when pressing the gas while in gear, so you hear the engine working under load
		targetRpm += absAccelerateInput * 800f;
		return float.Min( targetRpm, Engine_MaxRPM );
	}
}
