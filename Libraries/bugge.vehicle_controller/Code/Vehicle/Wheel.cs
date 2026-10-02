using Sandbox;

namespace Bugges.VehicleController;

public enum WheelSide { Left, Right };
public class Wheel
{
	[Property]
	public float Radius { get; set; } = 20.0f;

	[Property]
	public float Width { get; set; } = 20.0f;

	[Range( 0.0f, 1.0f ), Step( 0.1f ), Property]
	public float Grip { get; set; } = 1.0f;

	[Property]
	public GameObject Visual { get; set; }

	[Property]
	public GameObject Spring { get; set; }

	[Property]
	public WheelSide Side { get; set; }

	[Property]
	public bool IsRotationReversed { get; set; }

	[Property]
	public bool IsBrake { get; set; } = true;

	[Property]
	public bool IsTurnable { get; set; }

	[Property]
	public bool IsMotor { get; set; }

	[Property]
	public SoundPointComponent Audio_SkidMark { get; set; }

	[Hide]
	public bool IsBraking { get; set; }

	[Hide]
	public bool IsAccelerating { get; set; }

	[Hide]
	public bool IsGrounded { get; set; }

	[Hide]
	public float SpinSpeed { get; set; }

	[Hide]
	public float TurnAngle { get; set; }

	[Hide]
	public float WheelAngle { get; set; }

	[Hide]
	public Vector3 Forward => SteerRotation.Forward;

	[Hide]
	public Vector3 Up => Spring.WorldTransform.Up;

	[Hide]
	public Vector3 Down => Spring.WorldTransform.Down;

	[Hide]
	public global::Rotation SteerRotation => global::Rotation.FromAxis( Up, TurnAngle ) * Spring.WorldRotation;

	[Hide]
	public Vector3 Outwards => Side == WheelSide.Left ? SteerRotation.Left : SteerRotation.Right;

	[Hide]
	public global::Rotation Rotation => global::Rotation.FromAxis( Outwards, WheelAngle ) * SteerRotation;

	[Hide]
	public float CurrentDownforce { get; set; }

	[Hide]
	public bool _isSkidding;
	[Property, ReadOnly]
	public bool IsSkidding
	{
		get => _isSkidding;
		set
		{
			if ( _isSkidding == value ) return;
			_isSkidding = value;

			if ( Audio_SkidMark is null ) return;

			if ( value ) Audio_SkidMark.StartSound();
			if ( !value ) Audio_SkidMark.StopSound();
		}
	}
}
