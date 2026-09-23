// Project: sboxgamejam3
// File:    CCCamera.cs
// Author:  cseliot
// Created: 2026.09.11.17.09.08
// Edited: 2026.09.11.17.18.09
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Follows the local player.
// 
// License:
// This code is provided "as is," without warranty of any kind, express or
// implied, including but not limited to the warranties of merchantability,
// fitness for a particular purpose, and noninfringement. In no event shall
// the authors or copyright holders be liable for any claim, damages, or
// other liability, whether in an action of contract, tort, or otherwise,
// arising from, out of, or in connection with the software or the use or
// other dealings in the software.

using System;

namespace Sandbox;

/// <summary>
/// Tracks the local player's position with a fixed offset, chase-cam style.
/// Rotation is left untouched (the camera angle is fixed by design, see Design.md),
/// only position follows.
/// </summary>
public sealed class CCCamera : Component
{
	
	/// <summary>
	/// Master switch for everything that keeps the camera "behind" the player: the
	/// behind-cap cone (EnforceBehindCap) and its squeeze-back-to-center tracker
	/// (UpdateBehindCapSqueezeHelper). When false, the camera just lerps toward
	/// target+offset (plus jerk) with no angular constraint toward dead-center behind.
	/// EnforceMinDistance stays active either way - it only corrects radius along the
	/// camera's CURRENT direction from the target, so it is a "don't get swallowed by
	/// the follow-lerp" guard, not a behind-keeping function.
	/// </summary>
	[Property] public bool StayBehind { get; set; } = true;

	[Property] public bool UseAltTargets { get; set; }
	[Property] public bool UseAltFollowSpeed { get; set; }
	[Property] public bool UseAltLookAtSpeed { get; set; }

	/// <summary>
	/// How fast to reach max distance.
	/// </summary>
	[Property, MinMax(1,99)] private float _ScaleJerkSpeedToBeerLevel { get; set; }
	
	/// <summary>
	/// How fast to reach max distance.
	/// </summary>
	[Property] private float _JerkTime { get; set; }

	/// <summary>
	/// Left goes left, or left goes right? etc.
	/// </summary>
	[Property] private bool _JerkDirection { get; set; }

	/// <summary>
	/// How fast the jerk reaches max distance.
	/// </summary>
	[Property] private float _JerkSpeed { get; set; }
	
	/// <summary>
	/// Max Left and Right Distance camera moves laterally when first hitting left or right.
	/// </summary>
	[Property] private float _JerkMaxDistance { get; set; }
	
	/// <summary>
	/// Manual target override. If unset, the local (non-proxy) DrunkCC in the scene is used.
	/// </summary>
	[Property] private GameObject _FollowTarget { get; set; }
	/// <summary>
	/// Manual target override. If unset, the local (non-proxy) DrunkCC in the scene is used.
	/// </summary>
	[Property] private GameObject _LookAtTarget { get; set; }
	/// <summary>
	/// Use if @UseAltTargets is true.
	/// </summary>
	[Property] private GameObject _AltFollowTarget { get; set; }
	/// <summary>
	/// Manual target override. If unset, the local (non-proxy) DrunkCC in the scene is used.
	/// </summary>
	[Property] private GameObject _AltLookAtTarget { get; set; }
	[Property] private float _AltFollowSpeed { get; set; }
	/// <summary>
	/// Manual target override. If unset, the local (non-proxy) DrunkCC in the scene is used.
	/// </summary>
	[Property] private float _AltLookAtSpeed { get; set; }

	private GameObject _followTargetBackup;
	private GameObject _lookAtTargetBackup;
	private float? _followSpeedBackup = null;
	private float? _lookAtSpeedBackup = null;
	
	/// <summary>
	/// How quickly this object catches up to the target's position. Higher is snappier.
	/// </summary>
	[Property] private float _FollowSpeed { get; set; } = 5f;

	/// <summary>
	/// If true, this object rotates to face the target instead of keeping a fixed rotation.
	/// </summary>
	[Property] private bool _LookAt { get; set; }

	/// <summary>
	/// Distance along the camera's forward axis for the obstruction box trace.
	/// </summary>
	[Property] private float _FadeDetectionDistance { get; set; } = 400f;

	/// <summary>
	/// Half-size of the box trace used for obstruction detection (X/Y extents).
	/// </summary>
	[Property] private Vector2 _FadeDetectionSize { get; set; } = new( 50f, 50f );

	/// <summary>
	/// Speed at which rendered models fade in/out when obstruction is detected.
	/// </summary>
	[Property] private float _CameraFadeSpeed { get; set; } = 5f;

	/// <summary>
	/// When true, obstruction fading uses <see cref="UpdateObstructionFadeTriangles"/> (only the
	/// triangles/pixels of a hit mesh that fall inside the detection box dissolve, via a custom
	/// shader) instead of <see cref="UpdateObstructionFade"/> (fades the whole ModelRenderer's Tint
	/// alpha). Same detection box/trace in both, just a different fade target. Toggle to A/B compare.
	/// </summary>
	[Property] public bool UseTriangleFade { get; set; }

	/// <summary>
	/// How quickly this object turns to face the target when _LookAt is enabled. Higher is snappier.
	/// </summary>
	[Property] private float _LookAtSpeed { get; set; } = 5f;
	[Property] private Vector3 _Offset { get; set; }

	/// <summary>
	/// The behind-cap's resting/ceiling value: how far the camera is allowed to drift angularly
	/// from directly behind the target before any squeeze has kicked in.
	/// 0 = locked perfectly behind (0 radians). 1 = up to PI radians (no real constraint).
	/// Typical values: 0.15 (~27 deg) to 0.5 (~90 deg). The actual cap enforced each frame
	/// (_currentBehindCap) starts here and can only shrink from it via the squeeze below.
	/// </summary>
	[Property, MinMax(0f, 1f)] private float _MaxDefaultBehindCap { get; set; } = 0.25f;

	/// <summary>
	/// Seconds the camera is allowed to sit off dead-center before the behind-cap starts
	/// squeezing back in. Resets whenever the camera actually reaches dead-center.
	/// </summary>
	[Property] private float _ReturnToCenterSpeed { get; set; } = 1.5f;

	/// <summary>
	/// Once squeezing has started (see _ReturnToCenterSpeed), how fast the effective behind-cap
	/// shrinks toward 0, in cap-units (0-1) per second. This is what actually forces the camera
	/// back toward dead-center over time, independent of _MaxDefaultBehindCap.
	/// </summary>
	[Property] private float _BehindCapSqueezeSpeed { get; set; } = 0.15f;

	/// <summary>
	/// Closest the camera is allowed to get to the target, in units.
	/// If the camera ends up nearer than this it is pushed straight back out along
	/// its current direction from the target. 0 disables the check.
	/// </summary>
	[Property] private float _MinDistance { get; set; } = 150f;

	private float _startJerkTime;

	/// <summary>
	/// How long (seconds) the camera has continuously sat off dead-center. Reset to 0 whenever
	/// the camera reaches dead-center; once it exceeds _ReturnToCenterSpeed the behind-cap starts
	/// squeezing (see _currentBehindCap).
	/// </summary>
	private float _timeOffCenter;

	/// <summary>
	/// The behind-cap actually enforced this frame. Starts at/resets to _MaxDefaultBehindCap and
	/// shrinks toward 0 at _BehindCapSqueezeSpeed once _timeOffCenter exceeds _ReturnToCenterSpeed.
	/// </summary>
	private float _currentBehindCap;

	/// <summary>
	/// Manual target override. If unset, the local (non-proxy) DrunkCC in the scene is used.
	/// </summary>
	private DrunkCC _drunkCC;

	/// <summary>
	/// Tracks which ModelRenderers are currently obstructed and how far each has faded.
	/// Values range 0..1 where 1 = fully opaque (default).
	/// </summary>
	private readonly Dictionary<ModelRenderer, float> _rendererFadeOut = new();

	/// <summary>
	/// Per-renderer state for <see cref="UpdateObstructionFadeTriangles"/>: how far the
	/// in-box dissolve has progressed (0 = fully opaque, 1 = fully dissolved inside the
	/// detection box), plus the per-slot material overrides that existed BEFORE the fade
	/// swap so they can be restored verbatim on release.
	/// </summary>
	private sealed class TriangleFadeState
	{
		public float Amount;

		/// <summary>
		/// Slot index -> the override that was on that slot before the swap
		/// (null = the slot had no override). Authored overrides (e.g. the bar
		/// prefab's glow materials) must survive a fade cycle, so release
		/// restores these instead of clearing to null.
		/// </summary>
		public readonly Dictionary<int, Material> SavedOverrides = new();
	}

	private readonly Dictionary<ModelRenderer, TriangleFadeState> _triangleFade = new();

	/// <summary>
	/// Renderers whose fade-shader swap failed (no usable material slots / copy
	/// creation failed). Negative-cached so the swap loop doesn't re-run and
	/// re-log every frame while they stay obstructed.
	/// </summary>
	private readonly HashSet<ModelRenderer> _triangleFadeFailed = new();

	/// <summary>
	/// Cache of fade-shader material copies keyed by the source material, so repeated
	/// obstructions of the same material reuse one procedural copy instead of making a
	/// new one every time.
	/// </summary>
	private readonly Dictionary<Material, Material> _fadeMaterialCopies = new();

	private Shader _fadeShader;
	private bool _fadeShaderLoadFailed;

	protected override void OnStart()
	{
		_currentBehindCap = _MaxDefaultBehindCap;
	}

	protected override void OnUpdate()
	{
		if ( (TryResolveGetTargetLookAtHelper() && TryResolveGetTargetFollowHelper() && TryResolveGetDrunkHelper()) == false)
			return;

		HandleAlternatesHelper();
		
		if ( Input.Pressed( "Left" ) && Input.Pressed( "Right" ))
		{
			_startJerkTime = Time.Now;
		}
		if ( Input.Down( "Left" ) )
		{
			DoJerkHelper( false );
		}
		if ( Input.Down( "Right" ) )
		{
			DoJerkHelper( true );
		}
		
		if ( _LookAt )
		{
			var lookRotation = Rotation.LookAt( _LookAtTarget.WorldPosition - WorldPosition );
			WorldRotation = Rotation.Lerp( WorldRotation, lookRotation, _LookAtSpeed * Time.Delta );
		}
		
		var targetPosition = _FollowTarget.WorldPosition + _Offset;
		WorldPosition = Vector3.Lerp( WorldPosition, targetPosition, _FollowSpeed * Time.Delta );

		if ( StayBehind )
		{
			if ( _drunkCC.CurrentState == DrunkCC.State.Running )
			{
				UpdateBehindCapSqueezeHelper( _FollowTarget );
				EnforceBehindCap( _FollowTarget );
			}
		}
		else
		{
			// Keep the squeeze tracker fresh so re-enabling StayBehind doesn't resume
			// from a stale shrunken cap.
			_timeOffCenter = 0f;
			_currentBehindCap = _MaxDefaultBehindCap;
		}

		EnforceMinDistance( _FollowTarget );

		// Mode switched since last frame (inspector toggle at runtime): immediately
		// release the other mode's state so nothing is stranded on a half-faded
		// override shader or a partial Tint fade that no path updates anymore.
		if ( _lastUseTriangleFade != UseTriangleFade )
		{
			_lastUseTriangleFade = UseTriangleFade;
			ReleaseAllTriangleFades();
			ResetAllLegacyFades();
		}

		if ( UseTriangleFade )
			UpdateObstructionFadeTriangles( _FollowTarget );
		else
			UpdateObstructionFade( _FollowTarget );
	}

	/// <summary>
	/// Casts a box from the camera towards the follow-target and fades out any
	/// ModelRenderer it hits, while fading back in those no longer obstructed.
	/// </summary>
	private void UpdateObstructionFade( GameObject followTarget )
	{
		// No usable detection volume (fade disabled, or camera sitting on top of the target):
		// still release anything mid-fade so renderers aren't stranded at partial Tint alpha,
		// mirroring the triangle path's FadeAllTrianglesBackIn(null, ...) calls.
		if ( _FadeDetectionDistance <= 0f || _FadeDetectionSize == Vector2.Zero )
		{
			UpdateLegacyFadeBackIn( null );
			return;
		}

		var camPos = WorldPosition;
		var dirToTarget = (followTarget.WorldPosition - camPos);
		var distToTarget = dirToTarget.Length;
		if ( distToTarget < 0.001f )
		{
			UpdateLegacyFadeBackIn( null );
			return;
		}
	
		var forwardDir = dirToTarget.Normal;
		var endPos = camPos + forwardDir * _FadeDetectionDistance.Clamp( 1f, distToTarget );
		var zThick = 2f;
		var extents = new BBox( new Vector3( -_FadeDetectionSize.x, -_FadeDetectionSize.y, -zThick ),
		                        new Vector3(  _FadeDetectionSize.x,  _FadeDetectionSize.y,  zThick ) );
	
		// Build rotation so the Z-axis points toward the target (box traces are unrotated unless we supply Rotated).
		var rot = Rotation.LookAt( forwardDir );
	
		var trace = Scene.Trace
			.Box( extents, camPos, endPos )
			.Rotated( rot )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();
	
		var currentlyObstructed = new HashSet<ModelRenderer>();
		if ( trace.Hit )
		{
			var obj = trace.GameObject;
			while ( obj.IsValid() )
			{
				var mr = obj.GetComponent<ModelRenderer>();
				if ( mr.IsValid() )
				{
					currentlyObstructed.Add( mr );
				}
				obj = obj.Parent;
			}
		}
	
		// Fade out any newly obstructed renderer.
		foreach ( var mr in currentlyObstructed )
		{
			if ( !mr.IsValid() ) continue;
			if ( !_rendererFadeOut.ContainsKey( mr ) )
				_rendererFadeOut[mr] = mr.Tint.a;
			_rendererFadeOut[mr] = MathF.Max( 0f, _rendererFadeOut[mr] - _CameraFadeSpeed * Time.Delta );
			var alpha = _rendererFadeOut[mr].Clamp( 0f, 1f );
			mr.Tint = new Color( mr.Tint.r, mr.Tint.g, mr.Tint.b, alpha );
		}
	
		// Fade in any renderer that is no longer obstructed.
		UpdateLegacyFadeBackIn( currentlyObstructed );
	}

	/// <summary>
	/// Fades back in (and drops state for) every tracked renderer that is not in
	/// <paramref name="stillObstructed"/>; pass null when nothing is obstructed this frame
	/// (detection disabled, or camera coincident with the target) so a mid-fade renderer
	/// is never stranded at partial Tint alpha by an early return above the normal
	/// fade-in pass. Counterpart to <see cref="FadeAllTrianglesBackIn"/> on the legacy path.
	/// </summary>
	private void UpdateLegacyFadeBackIn( HashSet<ModelRenderer> stillObstructed )
	{
		if ( _rendererFadeOut.Count == 0 ) return;

		var toRemove = new List<ModelRenderer>();
		foreach ( var kvp in _rendererFadeOut )
		{
			var mr = kvp.Key;
			if ( !mr.IsValid() )
			{
				// Dead renderer: drop tracking (matches FadeAllTrianglesBackIn).
				toRemove.Add( mr );
				continue;
			}
			if ( stillObstructed is null || !stillObstructed.Contains( mr ) )
			{
				var currentVal = kvp.Value + _CameraFadeSpeed * Time.Delta;
				mr.Tint = new Color( mr.Tint.r, mr.Tint.g, mr.Tint.b, currentVal.Clamp( 0f, 1f ) );
				if ( currentVal >= 1f )
					toRemove.Add( mr );
				else
					_rendererFadeOut[mr] = currentVal;
			}
		}
		foreach ( var mr in toRemove )
			_rendererFadeOut.Remove( mr );
	}

	/// <summary>
	/// Triangle-level variant of <see cref="UpdateObstructionFade"/>: same box trace from the
	/// camera towards the follow-target, but instead of fading whole ModelRenderers it swaps
	/// hit renderers' materials to <c>shaders/obstruction_fade.shader</c> copies and pushes
	/// the swept detection box to each renderer as SceneObject attributes. The shader then
	/// dissolves ONLY the fragments inside that box (soft edge band), so the rest of each
	/// mesh stays fully opaque. Fades back in (dissolve recedes) once no longer obstructed,
	/// and restores the original materials when the fade completes.
	/// </summary>
	private void UpdateObstructionFadeTriangles( GameObject followTarget )
	{
		if ( _FadeDetectionDistance <= 0f || _FadeDetectionSize == Vector2.Zero )
		{
			FadeAllTrianglesBackIn( null, null, null, null, 0f, 0f );
			return;
		}

		var camPos = WorldPosition;
		var dirToTarget = (followTarget.WorldPosition - camPos);
		var distToTarget = dirToTarget.Length;
		if ( distToTarget < 0.001f )
		{
			FadeAllTrianglesBackIn( null, null, null, null, 0f, 0f );
			return;
		}

		var forwardDir = dirToTarget.Normal;
		var endPos = camPos + forwardDir * _FadeDetectionDistance.Clamp( 1f, distToTarget );
		var zThick = 2f;
		var extents = new BBox( new Vector3( -_FadeDetectionSize.x, -_FadeDetectionSize.y, -zThick ),
		                        new Vector3(  _FadeDetectionSize.x,  _FadeDetectionSize.y,  zThick ) );

		// Same rotation convention as UpdateObstructionFade: box traces are unrotated
		// unless we supply Rotated, and the box's Z axis points at the target.
		var rot = Rotation.LookAt( forwardDir );

		var trace = Scene.Trace
			.Box( extents, camPos, endPos )
			.Rotated( rot )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		var currentlyObstructed = new HashSet<ModelRenderer>();
		if ( trace.Hit )
		{
			var obj = trace.GameObject;
			while ( obj.IsValid() )
			{
				var mr = obj.GetComponent<ModelRenderer>();
				if ( mr.IsValid() )
					currentlyObstructed.Add( mr );
				obj = obj.Parent;
			}
		}

		var sweepLength = Vector3.DistanceBetween( camPos, endPos );
		var halfExtents = new Vector3( _FadeDetectionSize.x, _FadeDetectionSize.y, zThick );

		FadeAllTrianglesBackIn( currentlyObstructed, camPos, forwardDir, rot, sweepLength, halfExtents );

		// Advance the dissolve on obstructed renderers and push the box attributes.
		foreach ( var mr in currentlyObstructed )
		{
			if ( !mr.IsValid() ) continue;

			var state = GetOrCreateTriangleFadeState( mr );
			if ( state is null ) continue;

			state.Amount = MathF.Min( 1f, state.Amount + _CameraFadeSpeed * Time.Delta );

			PushTriangleFadeAttributes( mr, state, camPos, forwardDir, rot, sweepLength, halfExtents );
		}
	}

	/// <summary>
	/// Gets (creating + material-swapping on first use) the fade state for a renderer.
	/// Returns null when the swap is not possible (no usable material slots, shader or
	/// copy creation failed); failures are negative-cached so they don't retry every frame.
	/// </summary>
	private TriangleFadeState GetOrCreateTriangleFadeState( ModelRenderer mr )
	{
		if ( _triangleFade.TryGetValue( mr, out var state ) )
			return state;

		if ( _triangleFadeFailed.Contains( mr ) )
			return null;

		if ( _fadeShader is null && !_fadeShaderLoadFailed )
		{
			_fadeShader = Shader.Load( "shaders/obstruction_fade.shader" );
			if ( _fadeShader is null )
			{
				_fadeShaderLoadFailed = true;
				Log.Warning( $"{nameof( CCCamera )}: could not load shaders/obstruction_fade.shader, triangle fade disabled" );
			}
		}
		if ( _fadeShader is null )
			return null;

		state = new TriangleFadeState();

		var materials = mr.Materials;
		var swappedSlots = 0;
		for ( var i = 0; i < materials.Count; i++ )
		{
			var savedOverride = materials.HasOverride( i ) ? materials.GetOverride( i ) : null;
			state.SavedOverrides[i] = savedOverride;

			var source = savedOverride is { IsValid: true } ? savedOverride : materials.GetOriginal( i );
			if ( source is null || !source.IsValid ) continue;

			if ( !_fadeMaterialCopies.TryGetValue( source, out var copy ) || !copy.IsValid() )
			{
				copy = source.CreateCopy( $"{source.Name}_obstruction_fade" );
				if ( copy is null || !copy.IsValid() ) continue;
				copy.Shader = _fadeShader;
				_fadeMaterialCopies[source] = copy;
			}

			materials.SetOverride( i, copy );
			swappedSlots++;
		}

		// No slot ended up on the fade shader: negative-cache so the swap loop
		// doesn't retry (and re-log) every frame while this renderer is obstructed.
		if ( swappedSlots == 0 )
		{
			_triangleFadeFailed.Add( mr );
			return null;
		}

		_triangleFade[mr] = state;
		return state;
	}

	/// <summary>
	/// Pushes the swept detection box and fade amount to a faded renderer's SceneObject
	/// so obstruction_fade.shader can dissolve exactly the fragments inside it.
	/// </summary>
	private static void PushTriangleFadeAttributes( ModelRenderer mr, TriangleFadeState state,
	                                                Vector3 rayStart, Vector3 rayDir, Rotation rot,
	                                                float sweepLength, Vector3 halfExtents )
	{
		var so = mr.SceneObject;
		if ( so is null || !so.IsValid() ) return;

		so.Attributes.Set( "FadeRayStart", rayStart );
		so.Attributes.Set( "FadeRayDir", rayDir );
		so.Attributes.Set( "FadeRayLength", sweepLength );
		so.Attributes.Set( "FadeBoxAxisX", rot.Forward );
		so.Attributes.Set( "FadeBoxAxisY", rot.Left );
		so.Attributes.Set( "FadeBoxAxisZ", rot.Up );
		so.Attributes.Set( "FadeBoxHalfExtents", halfExtents );
		so.Attributes.Set( "FadeAmount", state.Amount );
		so.Attributes.Set( "FadeSoftness", 10f );
	}

	/// <summary>
	/// Recedes the dissolve on renderers no longer obstructed (or on all of them when
	/// <paramref name="currentlyObstructed"/> is null), restoring original materials and
	/// dropping state once fully opaque again. When the current sweep is known, the box
	/// attributes are refreshed so a shrinking fade tracks the camera; when it isn't
	/// (detection disabled or camera on top of the target), the dissolve recedes against
	/// the last pushed box.
	/// </summary>
	private void FadeAllTrianglesBackIn( HashSet<ModelRenderer> currentlyObstructed,
	                                     Vector3? rayStart, Vector3? rayDir, Rotation? rot,
	                                     float sweepLength, Vector3 halfExtents )
	{
		if ( _triangleFade.Count == 0 ) return;

		var finished = new List<ModelRenderer>();
		foreach ( var kvp in _triangleFade )
		{
			var mr = kvp.Key;
			if ( !mr.IsValid() || (currentlyObstructed is not null && !currentlyObstructed.Contains( mr )) )
			{
				var state = kvp.Value;
				state.Amount -= _CameraFadeSpeed * Time.Delta;
				if ( state.Amount <= 0f || !mr.IsValid() )
				{
					finished.Add( mr );
					continue;
				}

				// Still partially dissolved: keep the box pinned to the current sweep
				// so the dissolve edge shrinks toward the live detection volume.
				if ( rayStart is not null && rayDir is not null && rot is not null )
					PushTriangleFadeAttributes( mr, state, rayStart.Value, rayDir.Value, rot.Value, sweepLength, halfExtents );
			}
		}

		foreach ( var mr in finished )
			ReleaseTriangleFade( mr );
	}

	/// <summary>
	/// Restores the renderer's pre-swap material overrides (authored overrides like the
	/// bar prefab's glow materials survive; slots that had none are cleared).
	/// </summary>
	private void ReleaseTriangleFade( ModelRenderer mr )
	{
		if ( !_triangleFade.Remove( mr, out var state ) )
			return;

		_triangleFadeFailed.Remove( mr );

		if ( !mr.IsValid() ) return;

		var materials = mr.Materials;
		foreach ( var kvp in state.SavedOverrides )
			materials.SetOverride( kvp.Key, kvp.Value );
	}

	/// <summary>
	/// Immediately releases every triangle-fade state, restoring original materials.
	/// Used when the mode is switched off or the component is disabled/destroyed so no
	/// renderer is left stranded on the override shader with a frozen dissolve.
	/// </summary>
	private void ReleaseAllTriangleFades()
	{
		foreach ( var mr in _triangleFade.Keys.ToList() )
			ReleaseTriangleFade( mr );
		_triangleFade.Clear();
		_triangleFadeFailed.Clear();
	}

	/// <summary>
	/// Immediately restores Tint alpha on every renderer the legacy
	/// <see cref="UpdateObstructionFade"/> path had faded, so switching
	/// <see cref="UseTriangleFade"/> on mid-fade doesn't strand a partial Tint fade
	/// that this path will no longer update.
	/// </summary>
	private void ResetAllLegacyFades()
	{
		foreach ( var kvp in _rendererFadeOut )
		{
			var mr = kvp.Key;
			if ( !mr.IsValid() ) continue;
			mr.Tint = new Color( mr.Tint.r, mr.Tint.g, mr.Tint.b, 1f );
		}
		_rendererFadeOut.Clear();
	}

	private bool _lastUseTriangleFade;

	protected override void OnEnabled()
	{
		_lastUseTriangleFade = UseTriangleFade;
	}

	protected override void OnDisabled()
	{
		// Component switched off mid-fade: don't leave renderers on the override
		// shader or mid-Tint-fade.
		ReleaseAllTriangleFades();
		ResetAllLegacyFades();
	}

	protected override void OnDestroy()
	{
		// Restore every faded renderer so nothing is left on the override shader
		// if the camera component dies mid-fade.
		ReleaseAllTriangleFades();
		ResetAllLegacyFades();
	}

	/// <summary>
	/// Pushes the camera back out to _MinDistance if it has drifted closer than that to the target.
	/// Direction from the target is preserved, only the distance is corrected.
	/// </summary>
	private void EnforceMinDistance( GameObject target )
	{
		if ( _MinDistance <= 0f ) return;

		var currentOffset = WorldPosition - target.WorldPosition;
		var distance = currentOffset.Length;

		if ( distance >= _MinDistance ) return;

		// Degenerate case: camera sitting on top of the target gives no usable direction,
		// so fall back to placing it directly behind.
		var direction = distance < 0.0001f
			? -target.WorldRotation.Forward
			: currentOffset.Normal;

		WorldPosition = target.WorldPosition + direction * _MinDistance;
	}

	/// <summary>
	/// Tracks how long the camera has sat off dead-center behind the target and, once that
	/// exceeds _ReturnToCenterSpeed seconds, starts squeezing _currentBehindCap down toward 0 at
	/// _BehindCapSqueezeSpeed. EnforceBehindCap then has less and less room to work with, which is
	/// what actually forces the camera back to dead-center over time regardless of _MaxDefaultBehindCap
	/// or continued jerk input. Reaching dead-center resets both the timer and the cap back to
	/// _MaxDefaultBehindCap.
	/// </summary>
	private void UpdateBehindCapSqueezeHelper( GameObject target )
	{
		var currentOffset = WorldPosition - target.WorldPosition;
		var forwardDir = target.WorldRotation.Forward;

		var dot = currentOffset.LengthSquared < 0.0001f
			? 1f
			: Vector3.Dot( currentOffset.Normal, -forwardDir ).Clamp( -1f, 1f );
		var angle = MathF.Acos( dot );

		// Dead-center (or degenerate zero-offset case): relax back to the default cap.
		if ( angle <= 0.001f )
		{
			_timeOffCenter = 0f;
			_currentBehindCap = _MaxDefaultBehindCap;
			return;
		}

		_timeOffCenter += Time.Delta;

		if ( _timeOffCenter < _ReturnToCenterSpeed ) return;

		_currentBehindCap = MathF.Max( 0f, _currentBehindCap - _BehindCapSqueezeSpeed * Time.Delta );
	}

	/// <summary>
	/// Constrains the camera to stay within an angular cone behind the target.
	/// _currentBehindCap is 0-1 where 0 = locked perfectly behind, 1 = no constraint; it starts
	/// at _MaxDefaultBehindCap and is squeezed down over time by UpdateBehindCapSqueezeHelper.
	/// </summary>
	private void EnforceBehindCap( GameObject target )
	{
		if ( _currentBehindCap >= 1f ) return;

		var currentOffset = WorldPosition - target.WorldPosition;
		if ( currentOffset.LengthSquared < 0.0001f ) return;

		var distance = currentOffset.Length;
		var forwardDir = target.WorldRotation.Forward;
		var idealBehindPos = target.WorldPosition - forwardDir * distance;
		var idealOffset = idealBehindPos - target.WorldPosition;

		// Check angle between current position and ideal behind
		var dot = Vector3.Dot( currentOffset.Normal, idealOffset.Normal ).Clamp( -1f, 1f );
		var angle = MathF.Acos( dot );
		var maxAngle = _currentBehindCap * MathF.PI;

		if ( angle <= maxAngle ) return;

		// Use spherical interpolation for correct angular correction
		var correctionFactor = (angle - maxAngle) / angle;
		var correctedDir = Vector3.Slerp( currentOffset.Normal, idealOffset.Normal, correctionFactor );
		var correctedOffset = correctedDir * distance;

		WorldPosition = target.WorldPosition + correctedOffset;
	}

	/// <summary>
	/// Handles the lateral movement of the camera when the player hits left or right.
	/// </summary>
	/// <param name="direction"></param>
	private void DoJerkHelper( bool direction )
	{
		// 2x^3-3x^2+1
		
		float jerkSpeedBeerified = _JerkSpeed * _ScaleJerkSpeedToBeerLevel;
		
		float progress = Math.Clamp( ((Time.Now - _startJerkTime) * jerkSpeedBeerified) / _JerkTime, 0, 1); // Todo: var vs type declaration ... ?
		
		float distance = _JerkMaxDistance * 2 * MathF.Pow(progress, 3) - 3 * MathF.Pow(progress, 2) + 1;
		
		if (_JerkDirection == false)
			direction = !direction;

		if(direction)
			LocalPosition += LocalRotation.Right * distance * Time.Delta;
		else
			LocalPosition += LocalRotation.Left * distance * Time.Delta;
	}

	/// <summary>
	/// Ensures _Target points at a valid GameObject, falling back to the scene's local DrunkCC.
	/// </summary>
	/// <returns>True if _Target is valid.</returns>
	private bool TryResolveGetTargetLookAtHelper()
	{
		if ( _LookAtTarget.IsValid() )
			return true;

		var localCamTarget = Scene.FindAllWithTagOrigin( "lookat-target" ).FirstOrDefault( d => !d.IsProxy );
		if ( localCamTarget is null || localCamTarget.Network.IsMine() == false)
			return false;
		
		var altLocalCamTarget = Scene.Scene.FindAllWithTagOrigin( "alt-target" ).FirstOrDefault( d => !d.IsProxy );
		if ( altLocalCamTarget is null || altLocalCamTarget.Network.IsMine() == false)
			return false;

		_AltLookAtTarget = altLocalCamTarget;
		_LookAtTarget = localCamTarget;
		return _LookAtTarget.IsValid() && _AltLookAtTarget.IsValid();
	}

	/// <summary>
	/// Ensures _Target points at a valid GameObject, falling back to the scene's local DrunkCC.
	/// </summary>
	/// <returns>True if _Target is valid.</returns>
	private bool TryResolveGetTargetFollowHelper()
	{
		if ( _FollowTarget.IsValid() )
			return true;

		var localCamTarget = Scene.Scene.FindAllWithTagOrigin( "follow-target" ).FirstOrDefault( d => !d.IsProxy );
		if ( localCamTarget is null || localCamTarget.Network.IsMine() == false)
			return false;

		var altLocalCamTarget = Scene.Scene.FindAllWithTagOrigin( "alt-target" ).FirstOrDefault( d => !d.IsProxy );
		if ( altLocalCamTarget is null || altLocalCamTarget.Network.IsMine() == false)
			return false;

		_AltFollowTarget = altLocalCamTarget;
		_FollowTarget = localCamTarget;
		return _FollowTarget.IsValid() && _AltFollowTarget.IsValid();
	}

	/// <summary>
	/// Ensures _Target points at a valid GameObject, falling back to the scene's local DrunkCC.
	/// </summary>
	/// <returns>True if _Target is valid.</returns>
	private bool TryResolveGetDrunkHelper()
	{
		if ( _drunkCC.IsValid() )
			return true;
		
		var drunkCC = Scene.GetAllComponents<DrunkCC>().FirstOrDefault( d => !d.IsProxy );
		if ( drunkCC is null || drunkCC.GameObject.Network.IsMine() == false)
			return false;
		
		_drunkCC = drunkCC;
		return _LookAtTarget.IsValid() && _FollowTarget.IsValid();
	}
	
	private void HandleAlternatesHelper()
	{
		if ( UseAltTargets && _followTargetBackup == null && _lookAtTargetBackup == null )
		{
			_followTargetBackup = _FollowTarget;
			_lookAtTargetBackup = _LookAtTarget;
			
			_FollowTarget = _AltFollowTarget;
			_LookAtTarget = _AltLookAtTarget;
		}

		if ( UseAltTargets == false && _followTargetBackup != null && _lookAtTargetBackup != null )
		{
			_FollowTarget = _followTargetBackup;
			_LookAtTarget = _lookAtTargetBackup;
			
			_followTargetBackup = null;
			_lookAtTargetBackup = null;
		}
		
		if ( UseAltFollowSpeed && _followSpeedBackup == null )
		{
			_followSpeedBackup = _FollowSpeed;
			_FollowSpeed = _AltFollowSpeed;
		}
		if ( UseAltLookAtSpeed && _lookAtSpeedBackup == null )
		{
			_lookAtSpeedBackup = _LookAtSpeed;
			_LookAtSpeed = _AltLookAtSpeed;
		}
		
		if ( UseAltFollowSpeed == false && _followSpeedBackup != null )
		{
			_FollowSpeed = _followSpeedBackup.Value;
			_followSpeedBackup = null;
		}
		if ( UseAltLookAtSpeed == false && _lookAtSpeedBackup != null )
		{
			_LookAtSpeed = _lookAtSpeedBackup.Value;
			_lookAtSpeedBackup = null;
		}
	}
}
