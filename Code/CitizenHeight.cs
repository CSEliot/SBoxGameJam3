// Project: sboxgamejam3
// File:    CitizenHeight.cs
// Author:  cseliot
// Created: 2026.09.27.10.21.42
// Edited: 2026.09.27.10.21.42
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Recreates the stock citizen height slider (Dresser.ManualHeight / CitizenAnimationHelper.Height)
// on a body that plays sequences with UseAnimGraph off. Those two only write the animgraph param
// "scale_height", which nothing reads without the graph. Inside the stock graph
// (citizen_scale.vsubgrph) that param ADDS a parent-local translation to ~20 bones, blended from
// two single-frame delta poses: Scale_Half_delta at 0.5 and Scale_Twice_delta at 2.0. This adds the
// same translations through bone overrides, after ModelPhysics has pinned its bones for the frame.
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
using System.Collections.Generic;
using Sandbox.Citizen;
using ShrimpleRagdolls;

namespace Sandbox;

/// <summary>
/// Citizen height without citizen.vanmgrph. Put it next to the body SkinnedModelRenderer.
/// Height source uses the stock precedence: an active CitizenAnimationHelper with Height set
/// (raw 0.5..1.5, written every frame by stock) wins, else an active Dresser
/// (ManualHeight 0..1 -> 0.8..1.2, or 1 when ApplyHeightScale is off), else 1.
/// Requires UseAnimGraph off on the renderer, otherwise the graph and these offsets both apply.
/// Not reproduced: the graph's spring damping on height changes, and the neck/head rotation
/// deltas (2.0 deg at half, 2.7 deg at twice). While the ragdoll owns the pose
/// (Mode != None or lerping back) nothing is written, so the ragdoll shows normal proportions.
/// </summary>
public sealed class CitizenHeight : Component
{
	/// <summary>
	/// Body renderer to adjust. Empty = the SkinnedModelRenderer on this GameObject.
	/// </summary>
	[Property] private SkinnedModelRenderer _Renderer { get; set; }

	/// <summary>
	/// One bone of the stock delta poses, engine inches. Bind is the bone's bind position relative
	/// to its parent, used only to reject a skeleton the table doesn't fit. Half/Twice are the
	/// Scale_Half_delta / Scale_Twice_delta translations in the same space.
	/// Measured from citizen.fbx + Citizen@Scale_Half/Twice.fbx in Blender (source cm x 0.3937).
	/// Child rows are bone-local (X along the bone), which the source files and engine share.
	/// The pelvis row is the exception: Blender measured it in the Y-up armature frame
	/// (bind 0, 31.074, 1.267; delta purely on Y), so it is stored here in the engine's Z-up model
	/// space. Only its Z (bind height and delta) is used.
	/// </summary>
	private readonly record struct HeightBone( string Name, Vector3 Bind, Vector3 Half, Vector3 Twice );

	/// <summary>
	/// Parent-first order: every row's parent appears above it. Bones absent here get no offset
	/// and ride along with their parent (face, fingers, twist/helper bones).
	/// </summary>
	private static readonly HeightBone[] _table =
	{
		new( "pelvis", new Vector3( 0f, 0f, 31.074f ), new Vector3( 0f, 0f, -14.027f ), new Vector3( 0f, 0f, 23.555f ) ),
		new( "spine_0", new Vector3( 3.922f, -0.002f, 0f ), new Vector3( -1.889f, 0f, 0f ), new Vector3( 1.370f, 0.001f, 0f ) ),
		new( "spine_1", new Vector3( 5.610f, 0f, 0f ), new Vector3( -3.383f, 0f, 0.002f ), new Vector3( 2.885f, 0f, 0f ) ),
		new( "spine_2", new Vector3( 5.610f, 0f, 0f ), new Vector3( -2.234f, 0f, 0f ), new Vector3( 4.172f, 0f, 0f ) ),
		new( "neck_0", new Vector3( 5.820f, -0.093f, -0.003f ), new Vector3( -3.970f, -0.001f, 0.002f ), new Vector3( 1.493f, -0.001f, -0.001f ) ),
		new( "head", new Vector3( 4.511f, -0.001f, 0f ), new Vector3( -2.235f, -0.003f, 0f ), new Vector3( 0.204f, 0.217f, 0f ) ),
		new( "clavicle_L", new Vector3( 4.038f, 0.275f, 1.104f ), new Vector3( -2.569f, 0.002f, 0.001f ), new Vector3( 0.011f, -0.046f, 0.191f ) ),
		new( "arm_upper_L", new Vector3( 6.218f, 0f, 0f ), new Vector3( 0.001f, 0f, 0f ), new Vector3( 0.921f, 0f, 0f ) ),
		new( "arm_lower_L", new Vector3( 10.045f, 0f, 0f ), new Vector3( -4.435f, 0f, 0f ), new Vector3( 6.378f, 0f, 0f ) ),
		new( "hand_L", new Vector3( 7.707f, 0f, 0f ), new Vector3( -3.278f, 0f, 0f ), new Vector3( 4.386f, 0f, 0f ) ),
		new( "clavicle_R", new Vector3( 4.037f, 0.275f, -1.108f ), new Vector3( -2.569f, 0.002f, 0.001f ), new Vector3( 0.011f, -0.047f, -0.191f ) ),
		new( "arm_upper_R", new Vector3( 6.218f, 0f, 0f ), new Vector3( 0.001f, 0f, 0f ), new Vector3( 0.938f, 0f, 0f ) ),
		new( "arm_lower_R", new Vector3( 10.045f, 0f, 0f ), new Vector3( -4.435f, 0f, 0f ), new Vector3( 6.375f, 0f, 0f ) ),
		new( "hand_R", new Vector3( 7.707f, 0f, 0f ), new Vector3( -3.278f, 0f, 0f ), new Vector3( 4.282f, 0f, 0f ) ),
		new( "leg_upper_L", new Vector3( -0.288f, -0.426f, 4.427f ), Vector3.Zero, new Vector3( -1.165f, 0f, 0f ) ),
		new( "leg_lower_L", new Vector3( 14.470f, 0f, 0f ), new Vector3( -7.519f, 0f, 0f ), new Vector3( 10.312f, 0f, 0f ) ),
		new( "ankle_L", new Vector3( 12.516f, 0f, 0f ), new Vector3( -6.258f, 0f, 0f ), new Vector3( 12.544f, 0f, 0f ) ),
		new( "leg_upper_R", new Vector3( -0.288f, -0.426f, -4.427f ), Vector3.Zero, new Vector3( -1.165f, 0f, 0f ) ),
		new( "leg_lower_R", new Vector3( 14.470f, 0f, 0f ), new Vector3( -7.519f, 0f, 0f ), new Vector3( 10.312f, 0f, 0f ) ),
		new( "ankle_R", new Vector3( 12.516f, 0f, 0f ), new Vector3( -6.258f, 0f, 0f ), new Vector3( 12.544f, 0f, 0f ) ),
	};

	/// <summary>
	/// How far (inches) a bone's bind position may sit from the table before the skeleton is
	/// rejected. Well above rounding noise, far below any real axis or rig mismatch.
	/// </summary>
	private const float BindTolerance = 0.05f;

	private Dresser _dresser;
	private CitizenAnimationHelper _helper;
	private ShrimpleRagdoll _ragdoll;

	private Model _checkedModel;
	private bool _skeletonMatches;
	private readonly BoneCollection.Bone[] _modelBones = new BoneCollection.Bone[_table.Length];
	private readonly int[] _parentSlot = new int[_table.Length];
	private readonly Transform[] _animPose = new Transform[_table.Length];
	private readonly Transform[] _heightPose = new Transform[_table.Length];
	private bool _writingOverrides;

	protected override void OnStart()
	{
		ResolveReferences();
	}

	protected override void OnDisabled()
	{
		StopWritingOverrides();
	}

	private void ResolveReferences()
	{
		if ( !_Renderer.IsValid() )
			_Renderer = GetComponent<SkinnedModelRenderer>();

		// includeDisabled: a component disabled right now can still become the height source later;
		// Active is checked at read time.
		_dresser = GetComponent<Dresser>( true );
		_helper = GetComponent<CitizenAnimationHelper>( true );
		_ragdoll = GetComponent<ShrimpleRagdoll>( true );
	}

	/// <summary>
	/// Called by <see cref="CitizenHeightSystem"/> once per frame at Stage.UpdateBones, before the
	/// animation system composes the pose. Runs on every client for every body, like the clip picker.
	/// </summary>
	internal void ApplyHeightOffsets()
	{
		if ( !Active )
			return;

		if ( !_Renderer.IsValid() )
		{
			ResolveReferences();
			if ( !_Renderer.IsValid() )
				return;
		}

		var sceneModel = _Renderer.SceneModel;
		if ( !sceneModel.IsValid() )
			return;

		GetDeltaWeights( ResolveHeight(), out var halfWeight, out var twiceWeight );

		// While ragdolled, ModelPhysics writes every physics bone from its bodies. Our table covers
		// almost all of those bones, so writing here would pull the ragdoll off its bodies.
		bool ragdollOwnsPose = _ragdoll.IsValid() && _ragdoll.Renderer == _Renderer
			&& (_ragdoll.Mode != RagdollMode.None || _ragdoll.IsLerping);

		if ( ragdollOwnsPose || (halfWeight <= 0f && twiceWeight <= 0f) || !SkeletonMatches() )
		{
			StopWritingOverrides();
			return;
		}

		// Start from the animation channel every frame, never from final bones: final bones include
		// last frame's overrides, and reading them back would compound the offset.
		var modelTransform = sceneModel.Transform;

		for ( int i = 0; i < _table.Length; i++ )
		{
			if ( !_Renderer.TryGetBoneTransformAnimation( _modelBones[i], out var world ) || !world.IsValid )
				return;

			_animPose[i] = modelTransform.ToLocal( world );
		}

		// Overrides are full model-space transforms, so each child is rebuilt on top of its
		// already-offset parent. Bones outside the table follow their parent without an override.
		for ( int i = 0; i < _table.Length; i++ )
		{
			int parent = _parentSlot[i];
			var local = parent < 0 ? _animPose[i] : _animPose[parent].ToLocal( _animPose[i] );
			local.Position += _table[i].Half * halfWeight + _table[i].Twice * twiceWeight;

			_heightPose[i] = parent < 0 ? local : _heightPose[parent].ToWorld( local );
			sceneModel.SetBoneOverride( _modelBones[i].Index, _heightPose[i] );
		}

		_writingOverrides = true;
	}

	private float ResolveHeight()
	{
		if ( _helper.IsValid() && _helper.Active && _helper.Target == _Renderer && _helper.Height.HasValue )
			return _helper.Height.Value;

		if ( _dresser.IsValid() && _dresser.Active && _dresser.BodyTarget == _Renderer )
			return _dresser.ApplyHeightScale ? _dresser.ManualHeight.Remap( 0f, 1f, 0.8f, 1.2f, true ) : 1f;

		return 1f;
	}

	/// <summary>
	/// The subgraph's blend keys on scale_height: 0.5 = Scale_Half_delta, 1 = bind (no offset),
	/// 2 = Scale_Twice_delta, linear in between. The graph clamps the param to 0.5..2.
	/// </summary>
	private static void GetDeltaWeights( float height, out float halfWeight, out float twiceWeight )
	{
		height = height.Clamp( 0.5f, 2f );
		halfWeight = height < 1f ? (1f - height) * 2f : 0f;
		twiceWeight = height > 1f ? height - 1f : 0f;
	}

	/// <summary>
	/// Resolves the table's bones on the current model and checks their bind positions against
	/// the table, once per model. A mismatch (different rig, or engine bone axes not matching the
	/// measured ones) logs one warning and disables the offsets instead of mangling the body.
	/// </summary>
	private bool SkeletonMatches()
	{
		var model = _Renderer.Model;
		if ( model == _checkedModel )
			return _skeletonMatches;

		_checkedModel = model;
		_skeletonMatches = false;

		if ( model is null )
			return false;

		for ( int i = 0; i < _table.Length; i++ )
		{
			var row = _table[i];
			var bone = model.Bones.GetBone( row.Name );
			if ( bone is null )
			{
				Log.Warning( $"CitizenHeight: {model.ResourcePath} has no bone '{row.Name}', height disabled." );
				return false;
			}

			_modelBones[i] = bone;
			_parentSlot[i] = -1;

			if ( bone.Parent is null )
			{
				// Only the pelvis row may be a root. Its model-space bind height confirms Z is up;
				// its horizontal bind (1.267 in forward) is not compared because the sign of that
				// axis was never confirmed and the offset does not use it.
				if ( i != 0 || MathF.Abs( bone.LocalTransform.Position.z - row.Bind.z ) > BindTolerance )
				{
					Log.Warning( $"CitizenHeight: root bone '{row.Name}' on {model.ResourcePath} does not match the delta table, height disabled." );
					return false;
				}

				continue;
			}

			for ( int p = 0; p < i; p++ )
			{
				if ( _modelBones[p].Index == bone.Parent.Index )
				{
					_parentSlot[i] = p;
					break;
				}
			}

			if ( _parentSlot[i] < 0 )
			{
				Log.Warning( $"CitizenHeight: '{row.Name}' on {model.ResourcePath} is parented to '{bone.Parent.Name}', which the delta table does not expect. Height disabled." );
				return false;
			}

			var bind = _modelBones[_parentSlot[i]].LocalTransform.ToLocal( bone.LocalTransform ).Position;
			if ( (bind - row.Bind).Length > BindTolerance )
			{
				Log.Warning( $"CitizenHeight: bind position of '{row.Name}' on {model.ResourcePath} is {bind}, table expects {row.Bind}. Height disabled." );
				return false;
			}
		}

		_skeletonMatches = true;
		return true;
	}

	/// <summary>
	/// Overrides persist until cleared, and spine_1/neck_0 are not ModelPhysics bones, so nothing
	/// else would ever reset them. Clear all overrides once; ModelPhysics re-writes its own bones
	/// on its next update (at worst one frame of plain animation pose on a ragdoll's first frame,
	/// when the bodies still sit at that pose anyway).
	/// </summary>
	private void StopWritingOverrides()
	{
		if ( !_writingOverrides )
			return;

		_writingOverrides = false;

		if ( _Renderer.IsValid() )
			_Renderer.ClearPhysicsBones();
	}
}

/// <summary>
/// Drives every <see cref="CitizenHeight"/> at Stage.UpdateBones, order -50. That slot is after all
/// component OnUpdates (ModelPhysics re-pins its bones to the raw animation pose there every
/// frame) and before SceneAnimationSystem (order 0) evaluates the pose, so our overrides win.
/// </summary>
public sealed class CitizenHeightSystem : GameObjectSystem
{
	private readonly List<CitizenHeight> _components = new();

	public CitizenHeightSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.UpdateBones, -50, ApplyAll, "Citizen height offsets" );
	}

	private void ApplyAll()
	{
		if ( Scene.IsEditor )
			return;

		_components.Clear();
		Scene.GetAll( _components );

		foreach ( var component in _components )
			component.ApplyHeightOffsets();
	}
}
