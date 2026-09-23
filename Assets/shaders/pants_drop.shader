//=========================================================================================================================
// Pants drop shader (bind-pose gradient vertex displacement)
//
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
//
// Drop-in replacement for shaders/complex.shader on the FORCED pants garment
// (models/citizen_clothes/trousers/trackiebottoms/trackie_bottoms_black.vmdl),
// driven per-instance by PantsDropController.cs. When the player stops holding
// Shift the pants state machine (DrunkCC.[Sync] CurrentPantsState) reads Down
// and the controller tweens PantsDrop 0 -> 1: the garment slides down the legs
// toward the ankles, revealing the always-worn heart boxers + exposed Legs
// bodygroup underneath.
//
// HOW: clothing is multi-bone skinned, so by the time this MainVs runs the
// engine has ALREADY posed o.vPositionWs (compute skinning pre-pass feeding the
// D_CS_VERTEX_ANIMATION branch of VS_CommonProcessing, vr_common_vs_code.fxc:89-
// 108; rigid single-bone path at :109-140). Material code cannot deform
// pre-skin. So we key the slide off i.vPositionOs - the RAW bind-pose position,
// a plain POSITION stream always present regardless of skin path
// (common/vertexinput.hlsl:6, untouched by the engine's VS code) - through a
// 0..1 height gradient t, and offset the posed world position along a
// C#-pushed axis (pelvis bone up, NOT world-up, so the slide tracks the lean):
//     o.vPositionWs += axis * t * PantsDrop * PantsDropDistance
// t=1 at the waistband, t=0 at the ankle cuffs, calibrated once from THIS mesh
// (1225-vert LOD0; see Pants-Calibration/README.md "MEASURED"): the raw FBX is
// Y-up cm (ankle 11.42, waist 95.78), the compiled model is Z-up (the 0.3937
// cm->in compile scale cancels inside a normalized ratio), so the defaults
// below are in the COMPILED object-space Z the shader actually reads.
//
// DISPLACEMENT MUST REACH THE DEPTH PASS - opposite of the dissolve rule in
// obstruction_fade.shader: a moved silhouette has to write moved depth/shadow
// or shadows and the prepass keep the undisplaced shape. MODES therefore uses
// the in-shader `Depth( S_MODE_DEPTH )` form (precedent: terrain.shader,
// line.shader) instead of vertex_color.shader's `Depth("depth_only.shader")`
// override, so the same MainVs below compiles for Forward AND depth modes, and
// the offset applies in every pass. MainPs bails to a trivial return under
// S_MODE_DEPTH (terrain.shader:206 precedent) - no lighting needed there.
//
// KNOWN v0 LIMITS (both consciously deferred, Pants-Drop.md items 3 and 6):
// - Shear vs fold: a height-scaled slide translates bands at different rates
//   but never compresses them, and normals keep pointing bind-pose directions,
//   so lighting can look slightly off in the falloff band at large drops. If
// it reads wrong in-engine, add the analytic-derivative normal correction or
// at minimum rotate the normal into the displacement gradient (deferred;
// Pants-Drop.md item 3).
// - The gradient defaults assume vPositionOs is compiled-model object space
//   (engine inches, Z-up, mesh feet-origin). If a render capture shows it
//   carries raw cm instead, only the two height constants change (t is a ratio
//   - 11.42/95.78 cm vs 4.50/37.71 in give the SAME gradient; that is why the
//   constants are normalized). The absolute PantsDropDistance default DOES
//   assume inches (~25 = the 29cm waistband->crotch geometric ceiling).
//=========================================================================================================================
HEADER
{
	DevShader = true;
	Description = "Standard PBR surface that slides worn pants down the legs via a bind-pose height gradient, driven per-instance by render attributes (pants drop visual)";
	Version = 1;
}

MODES
{
	Forward();

	// In-shader depth mode (NOT the generic depth_only.shader override): the
	// vertex offset must also apply in the depth prepass and shadow maps, so
	// shadows and depth-tested geometry follow the slid garment. MainVs
	// compiles once for all modes - the displacement reaches every pass.
	Depth( S_MODE_DEPTH );

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

	//
	// Drop parameters, pushed every frame by PantsDropController (per-instance:
	// all players share ONE pants material, so these MUST ride on the
	// SceneObject attribute stream - a Material.Set would couple every player
	// to one value).
	//

	// 0 = pants fully up (identity), 1 = fully dropped to the ankles.
	float g_flPantsDrop < Attribute( "PantsDrop" ); Default( 0.0 ); >;

	// Full-drop slide distance in world units (engine inches). v0 default ~25
	// = the measured waistband->crotch geometric ceiling (29cm); sliding past
	// it pushes the band through the hips, so the controller clamps its tween
	// to at most one full slide until a fold profile exists.
	float g_flPantsDropDistance < Attribute( "PantsDropDistance" ); Default( 25.0 ); >;

	// Slide axis: pelvis bone UP in world space, pushed by the controller.
	// NOT hardcoded float3(0,0,1): the drunk CC leans/rolls hard, and a
	// world-up slide would shear the garment sideways through the legs.
	// Bone-merged clothing renders in the body's world frame, so the axis and
	// the posed vPositionWs share one frame (SkinnedModelRenderer.MergeDescendants).
	float3 g_vPantsDropAxis < Attribute( "PantsDropAxis" ); Default3( 0.0, 0.0, 1.0 ); >;

	// Bind-pose height calibration for THIS mesh, in compiled object-space Z
	// (see header: normalized ratio makes the cm-vs-inches question moot).
	// Measured 2026-09-23 from trackie_bottoms.fbx LOD0 (see
	// Pants-Calibration/README.md): raw FBX ankle cuff 11.42cm, waistband top
	// 95.78cm -> engine-space (x0.3937) 4.50 / 37.71. Defaults use the
	// compiled-space numbers; a non-destructive gradient either way.
	float g_flPantsWaistZ < Default( 37.71 ); >;
	float g_flPantsAnkleZ < Default( 4.50 ); >;

	// Soft falloff width at the ankle end of the gradient, same units. The
	// measured mesh has no per-vertex weight data, so the v0 profile is a
	// smoothed linear ramp over the full leg length (below the cuff, t=0).
	float g_flPantsGradientSoftness < Default( 4.0 ); >;

	PS_INPUT MainVs( VS_INPUT i )
	{
		PS_INPUT o = VS_SharedStandardProcessing( i );

		// Bind-pose height gradient t: 1 at the waistband, 0 at the cuffs.
		// i.vPositionOs is the untouched POSITION stream - identical on both
		// skin paths - so this works whether the posed position came from the
		// compute cache or the single-bone VS branch.
		float flSpan = max( g_flPantsWaistZ - g_flPantsAnkleZ, 0.001 );
		float t = saturate( (i.vPositionOs.z - g_flPantsAnkleZ) / flSpan );

		// Ease the very bottom into the cuffs so the gradient has no hard
		// kink at the ankle ring (garment-shape-blind v0 profile).
		t = smoothstep( 0.0, g_flPantsGradientSoftness / flSpan, t );

		// Slide DOWN the legs = against the pushed up-axis.
		o.vPositionWs.xyz -= g_vPantsDropAxis * (t * saturate( g_flPantsDrop ) * g_flPantsDropDistance);

		// Re-project clip space after the world-space offset - the position
		// VS_CommonProcessing computed is now stale. (Same tail-fix the fur
		// shader's wind displacement uses, fur.shader:61-62.)
		o.vPositionPs = Position3WsToPs( o.vPositionWs.xyz );

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

	PS_OUTPUT MainPs( PS_INPUT i )
	{
		// Depth prepass / shadow maps only need the (displaced) geometry from
		// MainVs - skip all lighting, matching the engine's S_MODE_DEPTH bail
		// (terrain.shader:174-177/205-207, glass, sprite - and note those all
		// return 1, NOT 0: the depth-normal prepass G-buffer consumes the color
		// target (GBuffer.hlsl packs normal/roughness from SV_Target0), so a
		// black bail encodes a garbage normal while 1 stays what the engine's
		// own bails write).
		#if S_MODE_DEPTH
		{
			PS_OUTPUT depth_output;
			depth_output.vColor = 1;
			return depth_output;
		}
		#endif

		FinalCombinerInput_t finalCombinerInput = PS_SharedStandardProcessing( i );

		LightingTerms_t lightingTerms = InitLightingTerms();

		PS_OUTPUT ps_output;
		ps_output = PS_FinalCombinerDoLighting( finalCombinerInput, lightingTerms );
		ps_output = PS_FinalCombinerDoPostProcessing( finalCombinerInput, lightingTerms, ps_output );

		return ps_output;
	}
}
