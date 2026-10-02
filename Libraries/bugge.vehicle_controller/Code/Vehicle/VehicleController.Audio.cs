using Sandbox;

namespace Bugges.VehicleController;

partial class VehicleController : Component
{
	private const string AUDIO = "Audio";

	[Order( 0 )]
	[Property, FeatureEnabled( AUDIO )]
	private bool IsAudioEnabled { get; set; } = false;

	[Property( Title = "Engine Start" ), Feature( AUDIO )]
	private SoundPointComponent Audio_EngineStart { get; set; }

	[Property( Title = "Engine Stop" ), Feature( AUDIO )]
	private SoundPointComponent Audio_EngineStop { get; set; }

	[Property( Title = "Engine Idle" ), Feature( AUDIO )]
	private SoundPointComponent Audio_EngineIdle { get; set; }

	[Property( Title = "Engine Low RPM" ), Feature( AUDIO )]
	private SoundPointComponent Audio_EngineLowRPM { get; set; }

	[Property( Title = "Engine Hight RPM" ), Feature( AUDIO )]
	private SoundPointComponent Audio_EngineHighRPM { get; set; }

	[Property( Title = "Gear Switch" ), Feature( AUDIO )]
	private SoundPointComponent Audio_GearSwitch { get; set; }


	private void InitAudio()
	{
		OnGearChanged += PlayGearAudio;
		OnEngineStart += StartEngineAudio;
		OnEngineStop += StopEngineAudio;
	}

	private void PlayGearAudio( int gear )
	{
		if ( gear == -1 ) return;
		PlayGearAudio();
	}

	private void PlayGearAudio()
		=> Audio_GearSwitch?.StartSound();

	private async void StartEngineAudio()
	{
		Audio_EngineStart?.StartSound();
		await Task.DelaySeconds( Engine_StartDelay );

		Audio_EngineIdle?.StartSound();
		Audio_EngineLowRPM?.StartSound();
		Audio_EngineHighRPM?.StartSound();
	}

	private void StopEngineAudio()
	{
		Audio_EngineStop?.StartSound();
		Audio_EngineIdle?.StopSound();
		Audio_EngineLowRPM?.StopSound();
		Audio_EngineHighRPM?.StopSound();
	}

	private void UpdateEngineAudio()
	{
		if ( !IsEngineOn ) return;

		float rpm = float.Max( CurrentRPM, Engine_IdleRPM );
		CalculateVolumes( rpm, out float idleVolume, out float lowRpmVolume, out float highRpmVolume );

		if ( Audio_EngineIdle is not null )
		{
			Audio_EngineIdle.Volume = float.Lerp( Audio_EngineIdle.Volume, idleVolume, 1f - float.Exp( -10f * Time.Delta ) );
			Audio_EngineIdle.Pitch = float.Clamp( rpm / Engine_HighRPM, 0.8f, 2.0f );
		}

		if ( Audio_EngineLowRPM is not null )
		{
			Audio_EngineLowRPM.Volume = float.Lerp( Audio_EngineLowRPM.Volume, lowRpmVolume, 1f - float.Exp( -10f * Time.Delta ) );
			Audio_EngineLowRPM.Pitch = float.Clamp( rpm / Engine_LowRPM, 0.5f, 2.5f );
		}

		if ( Audio_EngineHighRPM is not null )
		{
			Audio_EngineHighRPM.Volume = float.Lerp( Audio_EngineHighRPM.Volume, highRpmVolume, 1f - float.Exp( -10f * Time.Delta ) );
			Audio_EngineHighRPM.Pitch = float.Clamp( rpm / Engine_HighRPM, 0.6f, 3.5f );
		}
	}

	private void CalculateVolumes( float rpm, out float idleVolume, out float lowRpmVolume, out float highRpmVolume )
	{
		if ( !IsAccelerating() )
		{
			idleVolume = 1f;
			lowRpmVolume = 0f;
			highRpmVolume = 0f;
			return;
		}

		idleVolume = 0.2f;
		if ( rpm < Engine_LowRPM )
		{
			lowRpmVolume = 1f;
			highRpmVolume = 0f;
			return;
		}

		if ( rpm > Engine_HighRPM )
		{
			highRpmVolume = 1f;
			lowRpmVolume = 0f;
			return;
		}

		float t = float.Clamp( (rpm - Engine_LowRPM) / (Engine_HighRPM - Engine_LowRPM), 0f, 1f );
		lowRpmVolume = 1f - t;
		highRpmVolume = t;
	}

	private void UpdateSkidAudio( Wheel wheel, SceneTraceResult trace )
	{
		if ( wheel.Audio_SkidMark is null ) return;

		if ( !wheel.IsSkidding || !trace.Hit )
		{
			wheel.Audio_SkidMark.Volume -= wheel.Audio_SkidMark.Volume * (1f - float.Exp( -10f * Time.Delta ));
			return;
		}

		Vector3 contactPoint = GetWheelContactPoint( wheel, trace );
		Vector3 tireVelocity = Body.GetVelocityAtPoint( contactPoint );

		float sidewaysVelocity = float.Abs( Vector3.Dot( wheel.Outwards, tireVelocity ) );

		float slipIntensity = sidewaysVelocity;
		if ( wheel.IsBraking ) slipIntensity = float.Max( slipIntensity, tireVelocity.Length );

		float normalizedSlip = float.Clamp( (slipIntensity - 100f) / 500f, 0.0f, 1.0f );

		float targetVolume = normalizedSlip * wheel.Audio_SkidMark.SoundEvent.Volume.GetValue();
		float targetPitch = float.Lerp( 0.8f, 1.3f, normalizedSlip );

		float expFactor = 1f - float.Exp( -10f * Time.Delta );

		wheel.Audio_SkidMark.Volume += (targetVolume - wheel.Audio_SkidMark.Volume) * expFactor;
		wheel.Audio_SkidMark.Pitch += (targetPitch - wheel.Audio_SkidMark.Pitch) * expFactor;
	}
}
