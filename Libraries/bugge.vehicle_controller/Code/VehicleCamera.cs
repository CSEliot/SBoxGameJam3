using Sandbox;

namespace Bugges.VehicleController;

[Icon( "local_taxi" )]
public sealed class VehicleCamera : Component
{
	[Property]
	private GameObject Target { get; set; }

	[Property]
	private float DefaultDistance { get; set; } = 500.0f;

	[Property]
	private Vector2 DistanceRange { get; set; } = new( 200.0f, 800.0f );

	[Property]
	private bool IsOffsetLocalSpace { get; set; } = false;

	[Property]
	private Vector3 Offset { get; set; }

	[FeatureEnabled( "Auto Center" ), Property]
	private bool ShouldAutoCenter { get; set; } = false;

	[Feature( "Auto Center" ), Property]
	private float AutoCenterDelay { get; set; } = 1.5f;

	[Feature( "Auto Center" ), Property]
	private float AutoCenterSpeed { get; set; } = 4.0f;

	[Feature( "Auto Center" ), Property]
	private float DefaultPitch { get; set; } = 15.0f;

	[ReadOnly, Property]
	private float CurrentDistance { get; set; }

	private Angles _eyeAngles;
	private TimeSince _timeSinceLastLook;


	protected override void OnStart()
	{
		if ( IsProxy )
		{
			DestroyGameObject();
			return;
		}

		CurrentDistance = DefaultDistance;
		_eyeAngles = WorldRotation.Angles();
	}

	protected override void OnUpdate()
	{
		if ( Target is null ) return;

		Zoom();
		AutoCenter();
		_eyeAngles.pitch = float.Clamp( _eyeAngles.pitch, -89f, 89f );
		KeepDistance();
	}

	private void KeepDistance()
	{
		_eyeAngles.roll = 0f;
		Rotation rotation = _eyeAngles.ToRotation();
		Vector3 targetPosition = GetTargetPosition();
		Vector3 backwardDir = -rotation.Forward;

		var ray = new Ray( targetPosition, backwardDir );
		var trace = Scene.Trace
			.Sphere( 10f, ray, CurrentDistance )
			.IgnoreGameObject( Target )
			.IgnoreDynamic()
			.Run();

		float targetDist = trace.Hit ? trace.Distance : CurrentDistance;

		WorldPosition = targetPosition + backwardDir * targetDist;
		WorldRotation = rotation;
	}

	private void Zoom()
	{
		CurrentDistance -= Input.MouseWheel.y * 100.0f;
		CurrentDistance = float.Clamp( CurrentDistance, DistanceRange.x, DistanceRange.y );
	}

	private void AutoCenter()
	{
		Angles lookInput = Input.AnalogLook;
		if ( lookInput.pitch != 0f || lookInput.yaw != 0f )
		{
			_timeSinceLastLook = 0;
			_eyeAngles += lookInput;
			return;
		}

		if ( !ShouldAutoCenter ) return;
		if ( _timeSinceLastLook <= AutoCenterDelay ) return;

		var rb = Target.GetComponent<Rigidbody>();
		if ( rb is not null && rb.Velocity.Length < 1.0f ) return;

		Rotation targetRot = Rotation.From( DefaultPitch, Target.WorldRotation.Yaw(), 0f );
		Rotation currentRot = _eyeAngles.ToRotation();

		currentRot = Rotation.Slerp( currentRot, targetRot, Time.Delta * AutoCenterSpeed );

		_eyeAngles = currentRot.Angles();
	}

	private Vector3 GetTargetPosition()
	{
		Vector3 worldSpaceOffset = Target.WorldTransform.Right * Offset.x + Target.WorldTransform.Forward * Offset.y + Target.WorldTransform.Up * Offset.z;
		return Target.WorldPosition + (IsOffsetLocalSpace ? worldSpaceOffset : Offset);
	}
}
