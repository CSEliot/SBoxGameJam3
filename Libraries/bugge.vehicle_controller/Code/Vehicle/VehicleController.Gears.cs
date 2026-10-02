using System;
using Sandbox;

namespace Bugges.VehicleController;

partial class VehicleController : Component
{
	public const string GEARS = "Gears";
	public enum Gears { Park, Reverse, Neutral, Drive, Low }

	[Property( Title = "Final Drive Ratio" ), Feature( GEARS )]
	public float Gear_FinalDriveRatio { get; set; } = 3.5f;

	[Property( Title = "Auto Switch to Reverse" ), Feature( GEARS )]
	public bool Gear_AutoSwitchToReverse { get; set; } = true;

	[Property( Title = "Shift Down RPM" ), Feature( GEARS )]
	public float Gear_ShiftDownRPM { get; set; } = 2500f;

	[Property( Title = "Shift Up RPM" ), Feature( GEARS )]
	public float Gear_ShiftUpRPM { get; set; } = 5500f;

	[Property( Title = "Ratios" ), Feature( GEARS )]
	public float[] Gear_Ratios { get; set; }

	[Property( Title = "Gear" ), Feature( GEARS ), ReadOnly]
	public Gears Gear_Current { get; set; } = Gears.Park;

	[Property( Title = "Ratio" ), Feature( GEARS ), ReadOnly]
	public float Gear_CurrentRatio
		=> Gear_CurrentRatioIndex != -1
			? Gear_Ratios[Gear_CurrentRatioIndex]
			: 0.0f;

	private int _currentGearRatioIndex = 1;
	[Property( Title = "Ratio Index" ), Feature( GEARS ), ReadOnly,]
	public int Gear_CurrentRatioIndex
	{
		get => _currentGearRatioIndex;
		set
		{
			bool isSameAsValue = _currentGearRatioIndex == value;
			if ( isSameAsValue ) return;

			_currentGearRatioIndex = value;
			OnGearChanged?.Invoke( value );
		}
	}

	private TimeSince _timeSinceLastShift;


	private void SwitchGear()
	{
		if ( Gear_AutoSwitchToReverse )
		{
			Gear_Current = GetGearFromInput();
			return;
		}

		if ( !GearInput ) return;
		int gearCount = Enum.GetValues<Gears>().Length;
		Gear_Current = (Gears)(((int)Gear_Current + 1) % gearCount);
	}

	private Gears GetGearFromInput()
	{
		float accelerateInput = AccelerateInput;
		if ( accelerateInput < 0 )
			return Gears.Reverse;

		if ( accelerateInput > 0 )
			return Gears.Drive;

		return Gear_Current;
	}

	private void UpdateGears()
	{
		if ( Gear_Current == Gears.Reverse )
		{
			Gear_CurrentRatioIndex = 0;
			return;
		}

		if ( Gear_Current != Gears.Drive )
		{
			Gear_CurrentRatioIndex = -1;
			return;
		}

		if ( _timeSinceLastShift < 0.5f ) return;

		float avgMotorSpeed = GetAverageDrivenWheelRPS( out _ );
		float driveAxleRpm = avgMotorSpeed * 60f;
		float targetRpm = float.Abs( driveAxleRpm * Gear_FinalDriveRatio * Gear_CurrentRatio );

		if ( targetRpm > Gear_ShiftUpRPM && Gear_CurrentRatioIndex < Gear_Ratios.Length - 1 )
		{
			Gear_CurrentRatioIndex++;
			_timeSinceLastShift = 0;
		}
		else if ( targetRpm < Gear_ShiftDownRPM && Gear_CurrentRatioIndex > 1 )
		{
			Gear_CurrentRatioIndex--;
			_timeSinceLastShift = 0;
		}

		Gear_CurrentRatioIndex = int.Clamp( Gear_CurrentRatioIndex, 1, Gear_Ratios.Length - 1 );
	}
}
