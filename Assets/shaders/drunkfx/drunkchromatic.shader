
HEADER
{
	Description = "";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	#ifndef S_ALPHA_TEST
	#define S_ALPHA_TEST 0
	#endif
	#ifndef S_TRANSLUCENT
	#define S_TRANSLUCENT 1
	#endif
	
	#include "common/shared.hlsl"
	#include "procedural.hlsl"

	#define S_UV2 1
	#define CUSTOM_MATERIAL_INPUTS
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float3 vPositionOs : TEXCOORD14;
	float3 vNormalOs : TEXCOORD15;
	float4 vTangentUOs_flTangentVSign : TANGENT	< Semantic( TangentU_SignV ); >;
	float4 vColor : COLOR0;
	float4 vTintColor : COLOR1;
	#if ( PROGRAM == VFX_PROGRAM_PS )
		bool vFrontFacing : SV_IsFrontFace;
	#endif
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		
		PixelInput i;
		i.vPositionPs = float4(v.vPositionOs.xy, 0.0f, 1.0f );
		i.vPositionWs = float3(v.vTexCoord, 0.0f);
		
		return i;
		
	}
}

PS
{
	#include "common/pixel.hlsl"
	#include "postprocess/functions.hlsl"
	#include "postprocess/common.hlsl"
		
	Texture2D g_tColorBuffer < Attribute( "ColorBuffer" ); SrgbRead ( true ); >;
	float g_flScalingFactor < UiGroup( ",0/,0/0" ); Default1( 0.1 ); Range1( 0, 1 ); >;
	float g_flDontTouch < UiGroup( ",0/,0/0" ); Default1( 0.23481488 ); Range1( 0, 1 ); >;
	float g_flDrunkeneffectopacity < UiGroup( ",0/,0/0" ); Default1( 0.39510602 ); Range1( 0, 1 ); >;
		
	float2 MapSceneColorCoords( float2 vInput, float2 modes )
	{
		float2 result;
	
		// X
		if ( modes.x == 1 ) // Mirror
		{
			float xx = abs( vInput.x );
			result.x = (fmod( floor( xx ), 2.0 ) == 0.0) ? frac( xx ) : 1.0 - frac( xx );
		}
		else if ( modes.x == 2 ) // Clamp
		{
			result.x = clamp( vInput.x, 0.0, 1.0 );
		}
		else if ( modes.x == 3 ) // Border
		{
			result.x = (vInput.x < 0.0 || vInput.x > 1.0) ? 0.5 : vInput.x;
		}
		else if ( modes.x == 4 ) // MirrorOnce
		{
	        float xx = abs( vInput.x );
			float floorX = floor( xx );
			if ( floorX < 1.0 )
			{
				result.x = frac( xx );
			}
			else if ( floorX < 2.0 )
			{
				result.x = 1.0 - frac( xx );
			}
			else
			{
				result.x = vInput.x;
			}
		}
		else // Wrap by default
		{
			result.x = vInput.x;
		}
	
		// Y
		if ( modes.y == 1 ) // Mirror
		{
			float yy = abs( vInput.y );
			result.y = (fmod( floor( yy ), 2.0 ) == 0.0) ? frac( yy ) : 1.0 - frac( yy );
		}
		else if ( modes.y == 2 ) // Clamp
		{
			result.y = clamp( vInput.y, 0.0, 1.0 );
		}
		else if ( modes.y == 3 ) // Border
		{
			result.y = (vInput.y < 0.0 || vInput.y > 1.0) ? 0.5 : vInput.y;
		}
		else if ( modes.y == 4 ) // MirrorOnce
		{
			float yy = abs( vInput.y );
			float floorY = floor( yy );
			if ( floorY < 1.0 )
			{
				result.y = frac( yy );
			}
			else if ( floorY < 2.0 )
			{
				result.y = 1.0 - frac( yy );
			}
			else
			{
				result.y = vInput.y;
			}
		}
		else // Wrap by default
		{
			result.y = vInput.y;
		}
	
		return result;
	}
	
	float4 MainPs( PixelInput i ) : SV_Target0
	{

		
		float2 l_0 = CalculateViewportUv( i.vPositionSs.xy );
		float l_1 = l_0.x;
		float l_2 = g_flScalingFactor;
		float l_3 = g_flTime * 0.8;
		float l_4 = sin( l_3 );
		float l_5 = l_2 * l_4;
		float l_6 = l_5 + 0.3;
		float l_7 = l_1 - l_6;
		float l_8 = l_2 + 1;
		float l_9 = l_8 + l_5;
		float l_10 = 1 / l_9;
		float l_11 = l_7 * l_10;
		float l_12 = l_11 + l_6;
		float l_13 = l_0.y;
		float l_14 = sin( g_flTime );
		float l_15 = g_flDontTouch;
		float l_16 = l_14 * l_15;
		float l_17 = l_16 + 0.3;
		float l_18 = l_13 - l_17;
		float l_19 = g_flScalingFactor;
		float l_20 = l_19 + 1;
		float l_21 = l_19 * l_14;
		float l_22 = l_20 + l_21;
		float l_23 = 1 / l_22;
		float l_24 = l_18 * l_23;
		float l_25 = l_24 + l_17;
		float l_26 = 0.0f;
		float l_27 = 0.0f;
		float4 l_28 = float4( l_12, l_25, l_26, l_27 );
		float3 l_29 = g_tColorBuffer.Sample( g_sAniso, MapSceneColorCoords( l_28.xy, float2(0,0) ) ).rgb;
		float l_30 = l_29.x;
		float4 l_31 = l_28 + float4( 0.005, 0.005, 0.005, 0.005 );
		float3 l_32 = g_tColorBuffer.Sample( g_sAniso, MapSceneColorCoords( l_31.xy, float2(0,0) ) ).rgb;
		float l_33 = l_32.y;
		float l_34 = l_32.z;
		float4 l_35 = float4( l_30, l_33, l_34, 0 );
		float3 l_36 = g_tColorBuffer.Sample( g_sAniso, CalculateViewportUv( MapSceneColorCoords( i.vPositionSs.xy, float2(0,0) ) ) ).rgb;
		float l_37 = g_flDrunkeneffectopacity;
		float4 l_38 = saturate( lerp( l_35, float4( l_36, 0 ), l_37 ) );
		

		return float4( l_38.xyz, 1 );
	}
}
