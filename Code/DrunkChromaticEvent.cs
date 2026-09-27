// Project: sboxgamejam3
// File:    DrunkChromaticEvent.cs
// Author:  cseliot
// Created: 2026.09.27.03.30.42
// Edited: 2026.09.27.03.30.42
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// First concrete bar-exit event: a fullscreen chromatic-drunk post-process driven by
// the existing Assets/shaders/drunkfx/drunkchromatic.shader. Authored for "always after
// 20 beers" by setting MinBeerLevel=20 / TriggerChancePercent=100 in the editor; the
// code defaults stay neutral like every other [Property] here.
// 
// License:
// This code is provided "as is," without warranty of any kind, express or
// implied, including but not limited to the warranties of merchantability,
// fitness for a particular purpose, and noninfringement. In no event shall
// the authors or copyright holders be liable for any claim, damages, or
// other liability, whether in an action of contract, tort, or otherwise,
// arising from, out of, or in connection with the software or the use or
// other dealings in the software.

using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Node setup (the user authors this GameObject in the scene later; both components go on
/// ONE node):
/// 1. Create an empty GameObject in minimal.scene (e.g. "DrunkChromaticEvent").
/// 2. Add DrunkChromaticEvent and ChromaticPostProcess components to it.
/// 3. Author ChromaticPostProcess Enabled=false - enabling it IS starting the event
///    (same pattern as the bar Enter-Ring nodes).
/// 4. Either parent the node under the scene camera (the cccamera node) or add an
///    infinite PostProcessVolume to it: PostProcessSystem.UpdateCamera collects
///    BasePostProcess components only from camera children and volumes
///    (engine Scene/Components/PostProcessing/PostProcessSystem.cs).
/// 5. Set MinBeerLevel=20 and TriggerChancePercent=100 on DrunkChromaticEvent for the
///    designed "always fires past 20 beers" behaviour.
/// LOCAL-only, like all GameEvents: each client rolls against its own (un-synced)
/// BeerLevel and the post-process rides its own camera.
/// </summary>
public sealed class DrunkChromaticEvent : GameEvent
{
	/// <summary>
	/// The post-process payload this event switches on. Leave unset in the editor to have
	/// StartEvent auto-resolve the ChromaticPostProcess on the same GameObject
	/// (includeDisabled=true: the node is authored disabled).
	/// </summary>
	[Property] private ChromaticPostProcess _PostProcess { get; set; }

	/// <summary>
	/// Seconds the effect stays on before auto-disabling. 0 = forever (stays until the
	/// run/session ends - there is no Stop path).
	/// </summary>
	[Property] public float DurationSeconds { get; set; } = 0f;

	private float _elapsedSinceStart;

	public override void StartEvent()
	{
		_PostProcess ??= GameObject.GetComponent<ChromaticPostProcess>( true );

		if ( _PostProcess is null )
		{
			Log.Warning( $"DrunkChromaticEvent on {GameObject?.Name} has no ChromaticPostProcess to enable - add one to the same GameObject." );
			return;
		}

		_elapsedSinceStart = 0f;
		_PostProcess.Enabled = true;
	}

	protected override void OnUpdate()
	{
		// GameManager latches IsStarted before calling StartEvent, so the duration timer
		// only ticks once the event has actually fired. DurationSeconds 0 = forever.
		if ( !IsStarted || DurationSeconds <= 0f || _PostProcess is null )
			return;

		_elapsedSinceStart += Time.Delta;

		if ( _elapsedSinceStart >= DurationSeconds )
			_PostProcess.Enabled = false;
	}
}

/// <summary>
/// Fullscreen blit of drunkchromatic.shader. The shader samples
/// g_tColorBuffer < Attribute( "ColorBuffer" ) >, so the blit must use
/// BlitMode.WithBackbuffer (the engine copies the frame into "ColorBuffer" first -
/// BasePostProcess.Blit). g_flTime is pushed automatically by the engine
/// (Utility/Time.cs: "Sync g_flTime in shaders"), so it is never set here.
/// Each [Property] mirrors a pinned shader attribute of the same meaning (do not rename
/// the attribute strings; they bind Assets/shaders/drunkfx/drunkchromatic.shader as-is).
/// </summary>
[Title( "Drunk Chromatic Post Process" )]
[Category( "Post Processing" )]
public sealed class ChromaticPostProcess : BasePostProcess<ChromaticPostProcess>
{
	/// <summary>
	/// Warp strength -> g_flScalingFactor (shader default 0.1, range 0-1).
	/// Drives the horizontal UV stretch/offset of the drunken smear.
	/// </summary>
	[Property, Range( 0, 1 )] public float ScalingFactor { get; set; } = 0.1f;

	/// <summary>
	/// Vertical wobble amount -> g_flDontTouch (shader default 0.23481488, range 0-1).
	/// The shader-side name is legacy; it scales a sin(g_flTime) vertical offset.
	/// </summary>
	[Property, Range( 0, 1 )] public float Wobble { get; set; } = 0.23481488f;

	/// <summary>
	/// Blend weight -> g_flDrunkeneffectopacity (shader default 0.39510602, range 0-1).
	/// Per the shader's lerp this is the weight of the UNDISTORTED original frame:
	/// 0 = fully warped (effect at max), 1 = original frame (effect invisible).
	/// </summary>
	[Property, Range( 0, 1 )] public float Opacity { get; set; } = 0.39510602f;

	// Project-relative shader path (Assets/ stripped), same form the engine's
	// ChromaticAberration uses for its own post shader. FromShader caches ONE shared
	// material per shader path (Material.Static.cs) - fine here because this is a
	// single per-client fullscreen effect, unlike pants_drop where Material.Set was
	// banned (per-player values on a shared garment material would couple players).
	private static Material _shader = Material.FromShader( "shaders/drunkfx/drunkchromatic.shader" );

	protected override void OnUpdate()
	{
		// Material parameter push happens on the game thread, NOT in Render(): Render runs
		// inside the post-process build at scene end, and mutating material state during
		// an active draw is what the engine's Graphics.IsActive guard warns about
		// (Material.Create reentrancy check, Material.Static.cs). Setting every frame is
		// cheap (native attr override) and picks up live editor tweaks for free.
		if ( !_shader.IsValid() )
			return;

		_shader.Set( "g_flScalingFactor", ScalingFactor );
		_shader.Set( "g_flDontTouch", Wobble );
		_shader.Set( "g_flDrunkeneffectopacity", Opacity );
	}

	public override void Render()
	{
		// The shader's three floats are Shader-Graph material parameters (declared with
		// UiGroup/Default1 only, NO Attribute() annotation - see drunkchromatic.shdrgrph
		// IsAttribute:false), so the documented render-attribute bag may not bind them
		// (only < Attribute("Name"); > variables do - docs/rendering/shaders/
		// attributes-and-variables.md). The proven route is Material.Set by variable
		// name (engine BeamEffect.cs sets "g_tColor" on a material this way), done
		// per-frame in OnUpdate above; these bag sets stay as a fallback in case the
		// shader gains Attribute() annotations later. If neither binds, the baked shader
		// defaults still render the authored look.
		Attributes.Set( "g_flScalingFactor", ScalingFactor );
		Attributes.Set( "g_flDontTouch", Wobble );
		Attributes.Set( "g_flDrunkeneffectopacity", Opacity );

		// Order 5000: after ChromaticAberration (1000) / FilmGrain (200) /
		// ColorGrading (4000), before Pixelate (10000). Lower runs first within the
		// stage (BasePostProcess.BlitMode.Order docs).
		var blit = BlitMode.WithBackbuffer( _shader, Stage.AfterPostProcess, 5000, false );
		Blit( blit, "DrunkChromatic" );
	}
}
