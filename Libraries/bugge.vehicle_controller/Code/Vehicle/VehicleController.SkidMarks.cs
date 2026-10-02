using System.Collections.Generic;
using Sandbox;

namespace Bugges.VehicleController;

partial class VehicleController : Component
{
	private const string SKID_MARKS = "Skid Marks";

	[Property, FeatureEnabled( SKID_MARKS )]
	private bool IsSkidMarksEnabled = false;

	[Property( Title = "Decal" ), Feature( SKID_MARKS )]
	private DecalDefinition SkidMark_Decal { get; set; }

	[Property( Title = "Distance Between" ), Feature( SKID_MARKS )]
	private float SkidMark_DistanceBetween { get; set; } = 6.0f;

	[Property( Title = "Tolerance" ), Feature( SKID_MARKS )]
	private float SkidMark_Tolerance { get; set; } = 200.0f;

	private readonly Dictionary<string, Vector3> _lastSkidPositions = [];


	private void UpdateTireMarks( Wheel wheel, SceneTraceResult trace )
	{
		wheel.IsSkidding = ShouldSkid( wheel, trace );
		if ( !IsSkidMarksEnabled ) return;
		if ( !wheel.IsSkidding ) return;

		Vector3 wheelBottom = wheel.Visual.WorldPosition + wheel.Down * wheel.Radius;
		string key = wheel.Visual.Name;
		float distToPlane = Vector3.Dot( wheelBottom - trace.HitPosition, trace.Normal );
		Vector3 position = wheelBottom - trace.Normal * distToPlane + trace.Normal * 1f; bool hasKey = _lastSkidPositions.ContainsKey( key );
		if ( !hasKey ) _lastSkidPositions.Add( key, position );

		float dist = Vector3.DistanceBetween( _lastSkidPositions[key], position );
		if ( dist < SkidMark_DistanceBetween ) return;
		_lastSkidPositions[key] = position;

		Vector3 tireVelocity = Body.GetVelocityAtPoint( wheelBottom );
		float sidewaysVelocity = float.Abs( Vector3.Dot( wheel.Outwards, tireVelocity ) );

		float slipIntensity = sidewaysVelocity;
		if ( wheel.IsBraking ) slipIntensity = float.Max( slipIntensity, tireVelocity.Length );

		float normalizedSlip = float.Clamp( (slipIntensity - SkidMark_Tolerance) / 300f, 0.1f, 1.0f );

		var decal = new GameObject( $"{wheel.Visual.Name}_{SkidMark_Decal.ResourceName}" )
		{
			WorldPosition = position,
			WorldRotation = Rotation.LookAt( -trace.Normal, wheel.Forward )
		}.AddComponent<Decal>();

		decal.Rotation = 0f;
		decal.Transient = true;

		decal.ColorMix = normalizedSlip;

		decal.Decals.Add( SkidMark_Decal );
		decal.Size = new Vector3( wheel.Width, 1, 1 );

		Invoke( 10f, decal.DestroyGameObject );
	}

	private bool ShouldSkid( Wheel wheel, SceneTraceResult trace )
	{
		if ( !trace.Hit ) return false;

		Vector3 wheelBottom = wheel.Visual.WorldPosition + wheel.Down * wheel.Radius;
		Vector3 tireVelocity = Body.GetVelocityAtPoint( wheelBottom );
		if ( tireVelocity.LengthSquared < 10000.0f ) return false;

		float sidewaysVelocity = Vector3.Dot( wheel.Outwards, tireVelocity );
		bool isDrifting = float.Abs( sidewaysVelocity ) > SkidMark_Tolerance;
		if ( !wheel.IsBraking && !isDrifting ) return false;

		return true;
	}
}
