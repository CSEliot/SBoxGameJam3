// Project: sboxgamejam3
// File:    PantsDropController.cs
// Author:  cseliot
// Created: 2026.09.23
//
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.

using System;
using System.Collections.Generic;

/// <summary>
/// Drives the shader-based pants-drop VISUAL on the forced trackie bottoms
/// (Assets/shaders/pants_drop.shader), reading the replicated pants/knockdown
/// state from DrunkCC. Every client runs this for every player (local + proxy):
/// only the STATE crosses the wire ([Sync]), the tween, bodygroup flip, and
/// attribute push are per-client visuals.
///
/// Responsibilities (Pants-Drop.md delivery pattern + HideBody/knockdown rules):
/// 1. Find the PANTS garment's own SkinnedModelRenderer (the clothing child GO
///    tagged "clothing" that ClothingContainer.Apply creates - NOT the body
///    renderer), and re-find it whenever a re-dress destroys it (Reset kills
///    the clothing children; the destroyed component goes invalid).
/// 2. Swap that renderer's material slots to copies re-shaded with
///    pants_drop.shader (per-player state, shared material copies - every
///    player wears the same source vmat). Copies are built from the EFFECTIVE
///    material (override if present, else original), authored overrides are
///    snapshotted and preserved - same discipline as CCCamera's triangle fade.
/// 3. Tween PantsDrop 0..1 toward DrunkCC.CurrentPantsState and push it plus
///    the pelvis-bone slide axis as per-instance SceneObject attributes every
///    frame (Material.Set is banned - it would couple all players to one value).
/// 4. Legs bodygroup on the BODY renderer: pants fully up (and not knocked
///    down) = hidden (the empty choice, computed like the engine as
///    choices.Count-1, never hardcoded); mid-drop or knocked down = visible.
///    The citizen Legs group is all-or-nothing and BOTH the pants and the
///    heart boxers hide it, so this runtime toggle is the only thing keeping
///    slid-down pants from revealing a legless gap.
/// 5. Knockdown: the worn pants hide entirely (renderer disabled) while Legs
///    is shown - the flying-pants prefab (ecs's system) replaces them and
///    reads the same state; no bodygroup trickery substitutes for this hide.
///
/// Lives on the player prefab alongside DrunkCC/Dresser. No scene wiring
/// beyond existing nodes needed; the pants asset is matched by model path.
/// </summary>
public sealed class PantsDropController : Component
{
	/// <summary>
	/// How fast the drop tween runs (fraction of full drop per second) while
	/// pants are Down, and how much slower pulling them back up is.
	/// </summary>
	[Property, Range( 0.05f, 5f )] private float _DropDownSpeed { get; set; } = 1.2f;
	[Property, Range( 0.05f, 5f )] private float _DropUpSpeed { get; set; } = 2.5f;

	/// <summary>
	/// Full-drop slide distance in world units (engine inches). The measured
	/// geometric ceiling for THIS mesh is ~25 (waistband->crotch, see
	/// Pants-Calibration/README.md); more than that slides the band through the
	/// hips. Only a v1 fold profile makes going past it read correctly.
	/// </summary>
	[Property, Range( 0f, 40f )] private float _DropDistance { get; set; } = 25f;

	/// <summary>
	/// Pelvis bone name for the slide axis (Citizen skeleton; the pants share
	/// it). Identity on lookup falls back to world up.
	/// </summary>
	[Property] private string _PelvisBoneName { get; set; } = "pelvis";

	/// <summary>
	/// Substring identifying the forced pants garment among clothing children
	/// (asset path: models/citizen_clothes/trousers/trackiebottoms/...).
	/// </summary>
	[Property] private string _PantsModelMatch { get; set; } = "trackiebottoms";

	/// <summary>
	/// Shader and its material copies are shared by every player instance -
	/// all wear the same one forced garment, so one copy per source material
	/// serves the whole scene. Static so N controllers don't each rebuild it.
	/// A failed load is negative-cached for the whole session: these statics
	/// survive a component hotload (only a full domain reload clears them),
	/// same shape as CCCamera's fade-copy cache.
	/// </summary>
	private static Shader _dropShader;
	private static bool _dropShaderLoadFailed;
	private static readonly Dictionary<Material, Material> ShaderCopies = new();

	private DrunkCC _drunkCC;
	private SkinnedModelRenderer _bodyRenderer;
	private SkinnedModelRenderer _pantsRenderer;

	/// <summary>
	/// Slot index -> the override that existed on that slot BEFORE our swap
	/// (null = none). Authored overrides must survive; release restores these,
	/// never a blanket clear.
	/// </summary>
	private readonly Dictionary<int, Material> _savedOverrides = new();
	private bool _overrideActive;
	private bool _overrideFailed;

	/// <summary>
	/// Current tween value 0..1 (0 = up, 1 = fully at the ankles) and the last
	/// Legs-bodygroup choice we forced, so we only write on change.
	/// </summary>
	private float _dropAmount;
	private int _legsChoice = -1;

	protected override void OnStart()
	{
		_drunkCC = GameObject.GetComponent<DrunkCC>( true );
		if ( _drunkCC is null )
		{
			Log.Error( $"{nameof( PantsDropController )}: no DrunkCC on this GameObject, pants visual disabled" );
			Enabled = false;
			return;
		}
	}

	protected override void OnUpdate()
	{
		if ( _drunkCC is null ) return;

		EnsureRefsHelper();

		// No pants renderer yet (dress is async), permanently failed, or the
		// body itself is gone: nothing to drive. Keep polling cheaply - the
		// re-acquire path covers re-dresses destroying the clothing children.
		if ( !_pantsRenderer.IsValid() )
			return;

		if ( !_overrideActive && !_overrideFailed )
			TryApplyOverrideHelper();

		HandleKnockdownHideHelper();
		TweenAndPushHelper();
		SyncLegsBodyGroupHelper();
	}

	/// <summary>
	/// (Re)acquires body/pants renderers. The cached pants renderer goes invalid
	/// on any re-dress (ClothingContainer.Reset destroys the clothing children),
	/// so a failed IsValid here triggers a fresh walk; the material swap re-runs
	/// against the new renderer because _overrideActive is reset with it.
	/// </summary>
	private void EnsureRefsHelper()
	{
		if ( !_bodyRenderer.IsValid() )
		{
			var dresser = GameObject.GetComponentInChildren<Dresser>( true );
			_bodyRenderer = dresser.IsValid() ? dresser.BodyTarget : null;
		}

		if ( _pantsRenderer.IsValid() || !_bodyRenderer.IsValid() )
			return;

		// Previous pants renderer died (re-dress) - drop our slot state so the
		// new one gets swapped from scratch; there is nothing to restore on a
		// destroyed renderer. ClothingContainer.Reset also rewrote every
		// bodygroup back to the clothing-derived defaults, so the Legs cache is
		// stale too and must be re-asserted from scratch next frame.
		ReleaseOverrideStateHelper( restore: false );
		_overrideFailed = false;
		_legsChoice = -1;
		_pantsRenderer = FindPantsRendererHelper();
	}

	/// <summary>
	/// Walks the body's descendants for the clothing child whose model is the
	/// forced trackie bottoms. Children are tagged "clothing" by the engine's
	/// ClothingContainer.Apply (ClothingContainer.Dressing.cs:228); the tag is
	/// the stable contract, the model path the garment discriminator (drinkers
	/// wear account clothing too and could coincidentally own the same asset).
	/// </summary>
	private SkinnedModelRenderer FindPantsRendererHelper()
	{
		// Direct children only: ClothingContainer.Apply parents each clothing GO
		// straight under the body's GameObject.
		foreach ( var child in _bodyRenderer.GameObject.Children )
		{
			if ( child.Tags.Has( "clothing" ) == false )
				continue;

			var renderer = child.GetComponent<SkinnedModelRenderer>( true );
			if ( !renderer.IsValid() )
				continue;

			var modelPath = renderer.Model?.ResourcePath ?? "";
			if ( modelPath.Contains( _PantsModelMatch, StringComparison.OrdinalIgnoreCase ) )
				return renderer;
		}

		return null;
	}

	/// <summary>
	/// Swaps every pants material slot to a copy re-shaded with pants_drop.shader,
	/// snapshotting prior overrides first. One-time per renderer acquisition;
	/// failure is negative-cached so the walk doesn't retry and re-log per frame.
	/// </summary>
	private void TryApplyOverrideHelper()
	{
		var shader = LoadShaderHelper();
		if ( shader is null )
		{
			_overrideFailed = true;
			return;
		}

		var materials = _pantsRenderer.Materials;
		var swapped = 0;
		for ( var i = 0; i < materials.Count; i++ )
		{
			var savedOverride = materials.HasOverride( i ) ? materials.GetOverride( i ) : null;
			_savedOverrides[i] = savedOverride;

			var source = savedOverride.IsValid() ? savedOverride : materials.GetOriginal( i );
			if ( !source.IsValid() )
				continue;

			var copy = GetOrCreateCopyHelper( source, shader );
			if ( !copy.IsValid() )
				continue;

			materials.SetOverride( i, copy );
			swapped++;
		}

		if ( swapped == 0 )
		{
			Log.Warning( $"{nameof( PantsDropController )}: no material slot could be swapped to the drop shader on {_pantsRenderer}, pants will not visually drop" );
			_overrideFailed = true;
			return;
		}

		_overrideActive = true;
	}

	private static Shader LoadShaderHelper()
	{
		if ( _dropShader.IsValid() || _dropShaderLoadFailed )
			return _dropShader;

		_dropShader = Shader.Load( "shaders/pants_drop.shader" );
		if ( !_dropShader.IsValid() )
		{
			_dropShaderLoadFailed = true;
			Log.Warning( $"{nameof( PantsDropController )}: could not load shaders/pants_drop.shader, pants drop visual disabled" );
			return null;
		}

		return _dropShader;
	}

	private static Material GetOrCreateCopyHelper( Material source, Shader shader )
	{
		if ( ShaderCopies.TryGetValue( source, out var cached ) && cached.IsValid() )
			return cached;

		var copy = source.CreateCopy( $"{source.Name}_pants_drop" );
		if ( copy is null || !copy.IsValid() )
			return null;

		copy.Shader = shader;
		ShaderCopies[source] = copy;
		return copy;
	}

	/// <summary>
	/// Puts each slot back exactly as found (authored override restored, or
	/// cleared if the slot had none). Skipped when the renderer itself is dead
	/// (restore: false) - there is nothing to hand state back to.
	/// </summary>
	private void ReleaseOverrideStateHelper( bool restore )
	{
		if ( restore && _overrideActive && _pantsRenderer.IsValid() )
		{
			var materials = _pantsRenderer.Materials;
			foreach ( var kvp in _savedOverrides )
			{
				if ( kvp.Key >= materials.Count ) continue;
				materials.SetOverride( kvp.Key, kvp.Value );
			}
		}

		_savedOverrides.Clear();
		_overrideActive = false;
	}

	/// <summary>
	/// Knockdown hides the worn pants entirely (the flying prefab replaces them
	/// - ecs's system, reading the same replicated state). Hide = renderer off;
	/// Legs visibility is handled by SyncLegsBodyGroupHelper from the combined
	/// knockdown+state, matching the "flip Legs with the hide, restore by STATE
	/// not by knockdown alone" rule.
	/// </summary>
	private void HandleKnockdownHideHelper()
	{
		var knockedDown = _drunkCC.CurrentState == DrunkCC.State.KnockedDown;

		// The pants GO may also be mid-teleport/off-screen during knockdown
		// rewind; toggling Enabled every frame is idempotent.
		if ( _pantsRenderer.Enabled == knockedDown )
			_pantsRenderer.Enabled = !knockedDown;
	}

	/// <summary>
	/// Tween PantsDrop toward the replicated state and push it plus the slide
	/// axis (pelvis up in world space - the CC leans/rolls, world-up would shear
	/// the garment sideways through the legs) every frame. Attributes are not
	/// persistent; the engine reads them per-render, so always re-push while the
	/// renderer is active.
	/// </summary>
	private void TweenAndPushHelper()
	{
		var target = _drunkCC.CurrentPantsState == DrunkCC.PantsState.Down ? 1f : 0f;
		var speed = target > _dropAmount ? _DropDownSpeed : _DropUpSpeed;
		var step = speed * Time.Delta;
		if ( Math.Abs( target - _dropAmount ) <= step )
		{
			_dropAmount = target;
		}
		else
		{
			_dropAmount = Math.Clamp( _dropAmount + Math.Sign( target - _dropAmount ) * step, 0f, 1f );
		}

		var sceneObject = _pantsRenderer.SceneObject;
		if ( sceneObject is null || !sceneObject.IsValid() )
			return;

		var attrs = sceneObject.Attributes;
		attrs.Set( "PantsDrop", _dropAmount );
		attrs.Set( "PantsDropDistance", _DropDistance );
		attrs.Set( "PantsDropAxis", GetPelvisUpHelper() );
	}

	/// <summary>
	/// Pelvis bone world up from the BODY's SceneModel. Bone-merged clothing
	/// renders in the body's frame (MergeDescendants assigns the clothing
	/// SceneModel transform from its BoneMergeTarget), so a body-scene bone
	/// transform is the right frame for offsetting the posed clothing vertices.
	/// Falls back to world up when the model/bone lookup is not ready.
	/// </summary>
	private Vector3 GetPelvisUpHelper()
	{
		var model = _bodyRenderer.IsValid() ? _bodyRenderer.SceneModel : null;
		if ( model is null || !model.IsValid() )
			return Vector3.Up;

		var pelvis = model.GetBoneWorldTransform( _PelvisBoneName );
		var up = pelvis.Up;
		return up.LengthSquared > 0.0001f ? up.Normal : Vector3.Up;
	}

	/// <summary>
	/// Legs bodygroup on the body renderer, driven by the COMBINED state:
	/// visible (choice 0) while dropped/knocked-down, hidden (the empty choice,
	/// count-1 like the engine's HiddenChoice) only when pants are fully up AND
	/// not knocked down AND the drop tween has actually returned to zero.
	/// Writing only on change keeps SetBodyGroup (a mask rewrite + potentially
	/// mesh re-pick) off the hot path.
	/// </summary>
	private void SyncLegsBodyGroupHelper()
	{
		if ( !_bodyRenderer.IsValid() )
			return;

		var knockedDown = _drunkCC.CurrentState == DrunkCC.State.KnockedDown;
		var showLegs = knockedDown || _drunkCC.CurrentPantsState == DrunkCC.PantsState.Down || _dropAmount > 0.001f;

		var target = showLegs ? 0 : HiddenLegsChoiceHelper();
		if ( target < 0 ) target = 0; // no empty choice exists to hide into - legs stay visible

		if ( target == _legsChoice )
			return;

		_bodyRenderer.SetBodyGroup( "Legs", target );
		_legsChoice = target;
	}

	/// <summary>
	/// The empty-mesh choice index for Legs - last choice, computed like
	/// ClothingContainer.HiddenChoice (ClothingContainer.cs:223). Returns -1
	/// (write skipped) when the model has no Legs part with a real empty
	/// choice: choice 0 is always the visible meshes, so a single-choice part
	/// has nothing to hide and an out-of-range index would feed
	/// GetBodyPartMeshMask garbage (ModelRenderer.cs:174-186).
	/// </summary>
	private int HiddenLegsChoiceHelper()
	{
		var part = _bodyRenderer.Model?.Parts.Get( "Legs" );
		var count = part?.Choices?.Count ?? 0;
		return count > 1 ? count - 1 : -1;
	}

	protected override void OnEnabled()
	{
		// Re-enabling after a knockdown: the renderer Enabled toggle in
		// HandleKnockdownHideHelper re-asserts from state every frame, but
		// re-stamp it here so a single render after enable is never stale.
		if ( _pantsRenderer.IsValid() && _drunkCC.IsValid() )
			_pantsRenderer.Enabled = _drunkCC.CurrentState != DrunkCC.State.KnockedDown;
	}

	protected override void OnDestroy()
	{
		ReleaseOverrideStateHelper( restore: true );
	}
}
