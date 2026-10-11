//=========================================================================================================================
// Fake light beam cone (additive, unlit)
//
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
//
// A translucent additive cone mesh drawn along a SpotLight's aim so the beam
// reads as light in air. Unlit; the edge fade comes from an analytic cone
// normal (apex + axis + half-angle pushed from C#), not the mesh normal, so
// it survives flat shading and the non-uniform scale used to fit the light.
// No depth write in any mode, no shadow casting - the component-owned
// SceneObject that draws this sets Flags.CastShadows = false.
//
// Per-beam values arrive through Attribute() uniforms pushed by
// Code/FakeLightBeam.cs every frame via SceneObject.Attributes; one shared
// material (Material.FromShader) then serves every beam.
//=========================================================================================================================
HEADER
{
	DevShader = true;
	Description = "Fake light beam cone: additive, unlit, with edge/length/depth/camera fades";
	Version = 1;
}

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
	// Additive, depth-tested (reversed-Z), no depth write in ANY mode.
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, ONE );
	RenderState( BlendOp, ADD );
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );

	// Own all three depth states and the blend state before pixel.hlsl pulls its
	// own defaults in. S_TRANSLUCENT marks the material translucent to the renderer.
	#define DEPTH_STATE_ALREADY_SET 1
	#define BLEND_MODE_ALREADY_SET 1
	#define S_TRANSLUCENT 1

	#include "common/pixel.hlsl"
	#include "common/classes/Depth.hlsl"

	// Both faces. Must come AFTER the include: sbox_pixel.fxc emits CullMode BACK
	// with no ALREADY_SET guard, and every engine shader overrides it after the include.
	RenderState( CullMode, NONE );

	// Per-beam, pushed from C# every frame. Reads 0 until pushed, so the material
	// editor preview is black. That is expected.
	float3 g_vBeamApex       < Attribute( "BeamApex" ); >;
	float3 g_vBeamAxis       < Attribute( "BeamAxis" ); >;
	float  g_flBeamLength    < Attribute( "BeamLength" ); >;
	float  g_flBeamSin       < Attribute( "BeamSin" ); >;
	float  g_flBeamCos       < Attribute( "BeamCos" ); >;
	float3 g_vBeamColor      < Attribute( "BeamColor" ); >;
	float  g_flBeamIntensity < Attribute( "BeamIntensity" ); >;

	// Look tuning, set in the material editor.
	float g_flEdgePower       < Default( 2.0 );  Range( 0.5, 8.0 );    UiGroup( "Beam,10/10" ); >;
	float g_flLengthPower     < Default( 1.5 );  Range( 0.1, 8.0 );    UiGroup( "Beam,10/20" ); >;
	float g_flDepthFeather    < Default( 32.0 ); Range( 0.0, 256.0 );  UiGroup( "Beam,10/30" ); >;
	float g_flCameraFadeStart < Default( 16.0 ); Range( 0.0, 512.0 );  UiGroup( "Beam,10/40" ); >;
	float g_flCameraFadeEnd   < Default( 96.0 ); Range( 0.0, 1024.0 ); UiGroup( "Beam,10/50" ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init( i );
		float3 posWs = m.WorldPosition;

		// Analytic outward cone normal at this point.
		float3 p      = posWs - g_vBeamApex;
		float  h      = dot( p, g_vBeamAxis );
		float3 radial = p - h * g_vBeamAxis;
		float3 rDir   = radial / max( length( radial ), 0.0001 );
		float3 nWs    = rDir * g_flBeamCos - g_vBeamAxis * g_flBeamSin;

		// 1. Edge fade: 0 where the view ray grazes the surface (silhouette).
		float3 toCam   = g_vCameraPositionWs - posWs;
		float  camDist = max( length( toCam ), 0.0001 );
		float  edge    = pow( saturate( abs( dot( nWs, toCam / camDist ) ) ), g_flEdgePower );

		// 2. Length fade.
		float t          = saturate( h / max( g_flBeamLength, 0.001 ) );
		float lengthFade = pow( 1.0 - t, g_flLengthPower );

		// 3. Soft intersection with scene geometry.
		float depthFade = 1.0;
		if ( g_flDepthFeather > 0 )
		{
			float3 scenePos = Depth::GetWorldPosition( m.ScreenPosition.xy );
			depthFade = saturate( distance( scenePos, posWs ) / g_flDepthFeather );
		}

		// 4. Fade out when the camera is inside or right next to the cone.
		float camFade = saturate( ( camDist - g_flCameraFadeStart ) /
		                          max( g_flCameraFadeEnd - g_flCameraFadeStart, 0.001 ) );

		float a = g_flBeamIntensity * edge * lengthFade * depthFade * camFade;
		clip( a - 0.0001 );

		// Pushed colors are sRGB; linearize like line.shader does (section 5 notes).
		// SRC_ALPHA + ONE: adds color * a.
		return float4( SrgbGammaToLinear( g_vBeamColor ), a );
	}
}
