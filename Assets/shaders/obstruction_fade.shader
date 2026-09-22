//=========================================================================================================================
// Obstruction fade shader (per-pixel box dissolve)
//
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
//
// Drop-in replacement for shaders/complex.shader used by CCCamera's triangle-level
// obstruction fade (UpdateObstructionFadeTriangles). CCCamera box-traces from the
// camera to the follow target; for every ModelRenderer the trace hits it swaps each
// material slot to a copy of the original material re-shaded with this shader, and
// pushes the swept trace box + a fade amount to the renderer's SceneObject as
// render attributes every frame.
//
// The pixel shader tests each fragment's absolute world position against that box
// (closest point on the sweep segment, then an oriented-box distance with the
// trace's own axes/extents, so the dissolved region matches the trace volume
// exactly). Fragments inside the box dissolve through a soft edge band; fragments
// outside are untouched, so only the obstructing part of a mesh fades, not the
// whole renderer.
//
// Fade mechanism mirrors the engine's own opaque Tint fade (D_OPAQUE_FADE path in
// vr_shared_standard_ps_code.fxc): opacity is dither-clipped against the blue-noise
// texture, so this stays in the opaque pass. ModelRenderer.Tint (including Tint.a)
// still works: Tint flows through the per-instance vTint into vVertexColor/flOpacity,
// and the D_OPAQUE_FADE combo below keeps the engine's own Tint-alpha clip active
// exactly like complex.shader does.
//
// Forward-only on purpose (same trade as glow_pulse_outline.shader): the generic
// depth_only.shader used by the depth prepass and shadow maps knows nothing about
// the Fade* attributes and would write solid depth for dissolved pixels, hiding
// whatever is behind them (the forward pass depth-tests against prepass depth).
// Skipping the Depth mode means the object casts no shadows and writes no prepass
// depth while overridden; dissolved pixels then correctly reveal the geometry
// behind, which wrote its own prepass depth.
//
// Structure copied from the engine's vertex_color.shader (same vr_shared_standard
// family complex.shader is built from), minus the COLOR0 vertex-color stream:
// scene meshes do not carry baked vertex colors (only vertex_color/fur/foliage/UI
// shaders consume that semantic, and complex.shader ignores them too), so reading
// a missing stream here could only zero the albedo. Standard material parameters
// in the project's .vmat files (TextureColor, tint, texcoord controls, fog) bind by
// name when the material copy switches shaders.
//=========================================================================================================================
HEADER
{
	DevShader = true;
	Description = "Standard PBR surface that dissolves only the fragments inside a world-space box pushed via render attributes (camera obstruction fade)";
	Version = 1;
}

MODES
{
	// Forward only: no Depth() on purpose, see file header (the generic depth-only
	// pass would write solid depth for the dissolved region).
	Forward();

	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

FEATURES
{
	#include "vr_common_features.fxc"
}

COMMON
{
	#define BLEND_MODE_ALREADY_SET

	#include "system.fxc"
	#include "vr_common.fxc"
}

struct VS_INPUT
{
	#include "vr_shared_standard_vs_input.fxc"
};

struct PS_INPUT
{
	#include "vr_shared_standard_ps_input.fxc"
};

VS
{
	#include "vr_shared_standard_vs_code.fxc"

	PS_INPUT MainVs( VS_INPUT i )
	{
		// Standard chain only: VS_SharedStandardProcessing already applies the
		// per-instance Tint (extraShaderData.vTint -> vVertexColor, whose alpha
		// feeds flOpacity in the pixel shader), model tint and fade exponent.
		PS_INPUT o = VS_SharedStandardProcessing( i );
		return VS_CommonProcessing_Post( o );
	}
}

PS
{
	// Same combo complex.shader/vertex_color.shader declare: keeps the engine's
	// Tint-alpha dither clip (vr_shared_standard_ps_code.fxc) working on this
	// shader, and enables alpha-to-coverage for it under MSAA.
	DynamicCombo( D_OPAQUE_FADE, 0..1, Sys( ALL ) );

	#include "vr_shared_standard_ps_code.fxc"

	//
	// Fade box, pushed per-object every frame by CCCamera (world space).
	// Sweep segment the trace box travelled along:
	//
	float3 g_vFadeRayStart < Attribute( "FadeRayStart" ); >;
	float3 g_vFadeRayDir < Attribute( "FadeRayDir" ); >;      // normalized
	float g_flFadeRayLength < Attribute( "FadeRayLength" ); >;

	//
	// Oriented box axes (the trace rotation's basis: X=Forward, Y=Left, Z=Up)
	// and half extents, matching the BBox handed to Scene.Trace.Box.
	//
	float3 g_vFadeBoxAxisX < Attribute( "FadeBoxAxisX" ); >;
	float3 g_vFadeBoxAxisY < Attribute( "FadeBoxAxisY" ); >;
	float3 g_vFadeBoxAxisZ < Attribute( "FadeBoxAxisZ" ); >;
	float3 g_vFadeBoxHalfExtents < Attribute( "FadeBoxHalfExtents" ); >;

	// 0 = no dissolve (fully opaque), 1 = fully dissolved inside the box.
	float g_flFadeAmount < Attribute( "FadeAmount" ); Default( 0.0 ); >;

	// Width of the soft dissolve band just inside the box faces, world units.
	float g_flFadeSoftness < Attribute( "FadeSoftness" ); Default( 10.0 ); >;

	// Signed distance from a world position to the swept trace volume:
	// >0 outside, <0 inside (magnitude = distance to the nearest face).
	float FadeBoxDistance( float3 vPositionWs )
	{
		// Closest point on the sweep segment, then test the offset against the
		// oriented box. This reproduces the exact Minkowski-sum volume the
		// physics box sweep covered, for any segment/box orientation.
		float3 vToPixel = vPositionWs - g_vFadeRayStart;
		float flT = clamp( dot( vToPixel, g_vFadeRayDir ), 0.0, g_flFadeRayLength );
		float3 vDelta = vPositionWs - ( g_vFadeRayStart + g_vFadeRayDir * flT );

		float3 vLocal = float3( dot( vDelta, g_vFadeBoxAxisX ),
		                        dot( vDelta, g_vFadeBoxAxisY ),
		                        dot( vDelta, g_vFadeBoxAxisZ ) );

		float3 vOver = abs( vLocal ) - max( g_vFadeBoxHalfExtents, float3( 0.001, 0.001, 0.001 ) );
		return max( vOver.x, max( vOver.y, vOver.z ) );
	}

	PS_OUTPUT MainPs( PS_INPUT i )
	{
		FinalCombinerInput_t finalCombinerInput = PS_SharedStandardProcessing( i );

		// Per-pixel dissolve: fragments inside the box fade by g_flFadeAmount
		// with a soft band of g_flFadeSoftness at the boundary; everything
		// outside the volume keeps full opacity.
		float flFade = saturate( g_flFadeAmount );
		if ( flFade > 0.0 )
		{
			float flDist = FadeBoxDistance( finalCombinerInput.vPositionWs.xyz );
			float flSoft = max( g_flFadeSoftness, 0.001 );
			float flMask = saturate( -flDist / flSoft ); // 0 outside, 1 well inside

			// flOpacity already carries Tint.a (via vVertexColor), so the dither
			// clip below honors both the box dissolve and any renderer tint fade.
			finalCombinerInput.flOpacity *= 1.0 - flFade * flMask;

			// Same blue-noise dither clip the engine's D_OPAQUE_FADE path uses
			// (vr_shared_standard_ps_code.fxc), applied to the combined opacity.
			float flEps = 1.0 / 255.0;
			OpaqueFadeDepth( ( finalCombinerInput.flOpacity + 0.5 + flEps ) * 0.5, i.vPositionSs.xy );
		}

		LightingTerms_t lightingTerms = InitLightingTerms();

		PS_OUTPUT ps_output;
		ps_output = PS_FinalCombinerDoLighting( finalCombinerInput, lightingTerms );
		ps_output = PS_FinalCombinerDoPostProcessing( finalCombinerInput, lightingTerms, ps_output );

		return ps_output;
	}
}
