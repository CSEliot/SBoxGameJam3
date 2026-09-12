// Bar sign material shader with emission (glow)
FEATURES
{
    #include "common/features.hlsl"
}

MODES
{
    Forward();
    Depth();
}

COMMON
{
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		return FinalizeVertex( o );
	}
}

PS
{
    #include "common/pixel.hlsl"

	CreateTexture2D( g_tColor ) < Attribute( "TextureColor" ); SrgbRead( true ); Filter( TEXTURE_FILTERING ); >;

	float g_flEmissionStrength < Default( 3.0 ); UiGroup( "Emission,10/10" ); Range( 0.0, 20.0 ); >;
	float3 g_vEmissionColor < Default( 1.0, 0.8, 0.3 ); UiGroup( "Emission,10/20" ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init( i );

		float4 vColor = g_tColor.Sample( TextureFiltering, i.vTextureCoords.xy );
		m.Albedo = vColor.rgb;
		m.Opacity = vColor.a;

		// Emission makes the sign glow
		m.Emission = g_vEmissionColor * g_flEmissionStrength;

		return ShadingModelStandard::Shade( m );
	}
}
