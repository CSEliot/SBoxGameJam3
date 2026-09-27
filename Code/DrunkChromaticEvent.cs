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
/// 6. Optional ramp: set MaxBeerLevel above MinBeerLevel and the Starting/Ending
///    ScalingFactor and Wobble values; the effect then scales linearly with the run's
///    total BeerLevel between the two levels.
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
	/// Seconds the effect stays on before auto-disabling. 0 = forever (stays on until
	/// Try Again resets the run, which calls StopEvent).
	/// </summary>
	[Property] public float DurationSeconds { get; set; } = 0f;

	/// <summary>
	/// Run-total beer level (DrunkCC.BeerLevel, the same count MinBeerLevel gates on) at
	/// which the effect reaches its Ending values. From MinBeerLevel up to this level the
	/// scaling factor and wobble move linearly from their Starting to their Ending values;
	/// past it they hold at Ending. If this is left at or below MinBeerLevel (e.g. 0),
	/// there is no ramp and the effect uses its Ending values from the start.
	/// </summary>
	[Property] public int MaxBeerLevel { get; set; } = 0;

	/// <summary>
	/// Warp strength (ChromaticPostProcess.ScalingFactor) at MinBeerLevel.
	/// Shader default 0.1.
	/// </summary>
	[Property, Range( 0, 1 )] public float StartingScalingFactor { get; set; } = 0.1f;

	/// <summary>
	/// Warp strength (ChromaticPostProcess.ScalingFactor) at MaxBeerLevel and beyond.
	/// Shader default 0.1.
	/// </summary>
	[Property, Range( 0, 1 )] public float EndingScalingFactor { get; set; } = 0.1f;

	/// <summary>
	/// Vertical wobble (ChromaticPostProcess.Wobble) at MinBeerLevel.
	/// Shader default 0.23481488.
	/// </summary>
	[Property, Range( 0, 1 )] public float StartingWobble { get; set; } = 0.23481488f;

	/// <summary>
	/// Vertical wobble (ChromaticPostProcess.Wobble) at MaxBeerLevel and beyond.
	/// Shader default 0.23481488.
	/// </summary>
	[Property, Range( 0, 1 )] public float EndingWobble { get; set; } = 0.23481488f;

	private float _elapsedSinceStart;
	private DrunkCC _drunkCC;

	public override bool StartEvent()
	{
		_PostProcess ??= GameObject.GetComponent<ChromaticPostProcess>( true );

		if ( _PostProcess is null )
		{
			Log.Warning( $"DrunkChromaticEvent on {GameObject?.Name} has no ChromaticPostProcess to enable - add one to the same GameObject." );
			return false;
		}

		_elapsedSinceStart = 0f;
		// Ramp + material push before enabling, so the first rendered frame already
		// shows the ramped values (ApplyProgressionHelper pushes to the shader directly).
		ApplyProgressionHelper();
		_PostProcess.Enabled = true;
		return true;
	}

	/// <summary>
	/// Run reset (Try Again): GameManager re-arms every event, and this switches the
	/// payload back off so a previous run's post-process doesn't bleed into the fresh run.
	/// </summary>
	public override void StopEvent()
	{
		if ( _PostProcess.IsValid() )
			_PostProcess.Enabled = false;
	}

	protected override void OnUpdate()
	{
		// Only runs the ramp/timer while this event's effect is actually on: IsStarted is
		// set by GameManager after StartEvent succeeds, and the post-process is switched
		// off again by DurationSeconds or StopEvent.
		if ( !IsStarted || !_PostProcess.IsValid() || !_PostProcess.Enabled )
			return;

		// Every frame, not just at start: BeerLevel keeps rising at later bars, and the
		// ramp should follow it up to MaxBeerLevel.
		ApplyProgressionHelper();

		// DurationSeconds 0 = forever.
		if ( DurationSeconds <= 0f )
			return;

		_elapsedSinceStart += Time.Delta;

		if ( _elapsedSinceStart >= DurationSeconds )
			_PostProcess.Enabled = false;
	}

	/// <summary>
	/// Writes the ramped ScalingFactor/Wobble onto the post-process for the local player's
	/// CURRENT run-total BeerLevel and pushes them to the shader immediately. With
	/// MaxBeerLevel > MinBeerLevel: BeerLevel at or below MinBeerLevel -> Starting values,
	/// at or above MaxBeerLevel -> Ending values, linear in between. With MaxBeerLevel at
	/// or below MinBeerLevel (e.g. left at 0) there is no ramp and the Ending values apply.
	/// Leaves the values untouched while no local DrunkCC resolves.
	/// </summary>
	private void ApplyProgressionHelper()
	{
		// Cached: GameManager.GetLocalDrunkCC rescans the scene on every call, so only
		// re-resolve when the cached player is gone (e.g. respawned/replaced).
		if ( !_drunkCC.IsValid() )
			_drunkCC = Scene.GetAllComponents<GameManager>().FirstOrDefault()?.GetLocalDrunkCC();

		if ( !_drunkCC.IsValid() )
			return;

		float t = MaxBeerLevel > MinBeerLevel
			? _drunkCC.BeerLevel.LerpInverse( MinBeerLevel, MaxBeerLevel )
			: 1f;

		_PostProcess.ScalingFactor = MathX.Lerp( StartingScalingFactor, EndingScalingFactor, t );
		_PostProcess.Wobble = MathX.Lerp( StartingWobble, EndingWobble, t );
		_PostProcess.PushToMaterial();
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
	/// Overwritten by DrunkChromaticEvent's Starting/Ending ramp while that event runs,
	/// and keeps the last ramped value afterwards (until play mode restarts).
	/// </summary>
	[Property, Range( 0, 1 )] public float ScalingFactor { get; set; } = 0.1f;

	/// <summary>
	/// Vertical wobble amount -> g_flDontTouch (shader default 0.23481488, range 0-1).
	/// The shader-side name is legacy; it scales a sin(g_flTime) vertical offset.
	/// Overwritten by DrunkChromaticEvent's Starting/Ending ramp while that event runs,
	/// and keeps the last ramped value afterwards (until play mode restarts).
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
		PushToMaterial();
	}

	/// <summary>
	/// Copies ScalingFactor/Wobble/Opacity onto the shared shader material. Called from
	/// OnUpdate, and directly by DrunkChromaticEvent right after it writes ramped values:
	/// same-GameObject components update in no guaranteed order (Scene.Tick.cs iterates a
	/// HashSet-backed list), and a component enabled this frame is not updated until the
	/// next one (HashSetEx.Add during enumeration), so relying on this OnUpdate alone
	/// would render one frame with stale values.
	/// </summary>
	public void PushToMaterial()
	{
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
