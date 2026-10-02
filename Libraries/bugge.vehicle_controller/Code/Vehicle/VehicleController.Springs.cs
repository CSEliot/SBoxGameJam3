using Sandbox;

namespace Bugges.VehicleController;

partial class VehicleController : Component
{
	public const string SPRINGS = "Springs";

	[Order( -7 )]
	[Property( Title = "Strength" ), Feature( SPRINGS )]
	private float Spring_Strength { get; set; } = 100000.0f;

	[Property( Title = "Damping" ), Feature( SPRINGS )]
	private float Spring_Damping { get; set; } = 10000.0f;

	[Property( Title = "Min Distance" ), Feature( SPRINGS )]
	private float Spring_MinDistance { get; set; } = 15.0f;

	[Property( Title = "Max Distance" ), Feature( SPRINGS )]
	private float Spring_MaxDistance { get; set; } = 35.0f;

	[Property( Title = "Rest Distance" ), Feature( SPRINGS )]
	private float Spring_RestDistance { get; set; } = 30.0f;

	[Property( Title = "Compression Multiplier" ), Feature( SPRINGS )]
	private float Spring_OverCompressionMultiplier { get; set; } = 5.0f;


	private void ApplySpring( Wheel wheel, SceneTraceResult trace )
	{
		if ( !trace.Hit )
		{
			wheel.CurrentDownforce = 0f;
			return;
		}

		float traceDistance = GetTraceDistance( wheel, trace );

		Vector3 contactPoint = GetWheelContactPoint( wheel, trace );
		Vector3 tireWorldVel = Body.GetVelocityAtPoint( contactPoint );
		float offset = Spring_RestDistance - traceDistance;

		float velocity = Vector3.Dot( trace.Normal, tireWorldVel );
		float force = (offset * Spring_Strength) - (velocity * Spring_Damping);

		if ( traceDistance < Spring_MinDistance )
		{
			float overCompression = Spring_MinDistance - traceDistance;
			force += overCompression * Spring_Strength * Spring_OverCompressionMultiplier;
		}

		if ( force < 0 ) force = 0;

		wheel.CurrentDownforce = force;

		Body.ApplyForceAt( wheel.Spring.WorldPosition, trace.Normal * force );
	}
}
