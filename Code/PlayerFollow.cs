// Project: sboxgamejam3
// File:    PlayerFollow.cs
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

namespace Sandbox;

/// <summary>
/// Tracks the local player's position with a fixed offset, chase-cam style.
/// Rotation is left untouched (the camera angle is fixed by design, see Design.md),
/// only position follows.
/// </summary>
public sealed class PlayerFollow : Component
{
	/// <summary>
	/// Manual target override. If unset, the local (non-proxy) DrunkCC in the scene is used.
	/// </summary>
	[Property] private GameObject _Target { get; set; }

	/// <summary>
	/// How quickly this object catches up to the target's position. Higher is snappier.
	/// </summary>
	[Property] private float _FollowSpeed { get; set; } = 5f;

	/// <summary>
	/// If true, the offset between this object and the target at OnStart is preserved forever
	/// (matches however the camera was placed in the editor). If false, sits directly on the target.
	/// </summary>
	[Property] private bool _UseInitialOffset { get; set; } = true;

	/// <summary>
	/// If true, this object rotates to face the target instead of keeping a fixed rotation.
	/// </summary>
	[Property] private bool _LookAtTarget { get; set; }

	/// <summary>
	/// How quickly this object turns to face the target when _LookAtTarget is enabled. Higher is snappier.
	/// </summary>
	[Property] private float _LookAtSpeed { get; set; } = 5f;

	private Vector3 _offset;
	private bool _hasOffset;

	protected override void OnStart()
	{
		TryResolveTarget();
	}

	protected override void OnUpdate()
	{
		if ( !TryResolveTarget() )
			return;

		if ( !_hasOffset )
		{
			_offset = _UseInitialOffset ? WorldPosition - _Target.WorldPosition : Vector3.Zero;
			_hasOffset = true;
		}

		var targetPosition = _Target.WorldPosition + _offset;
		WorldPosition = Vector3.Lerp( WorldPosition, targetPosition, _FollowSpeed * Time.Delta );

		if ( _LookAtTarget )
		{
			var lookRotation = Rotation.LookAt( _Target.WorldPosition - WorldPosition );
			WorldRotation = Rotation.Lerp( WorldRotation, lookRotation, _LookAtSpeed * Time.Delta );
		}
	}

	/// <summary>
	/// Ensures _Target points at a valid GameObject, falling back to the scene's local DrunkCC.
	/// </summary>
	/// <returns>True if _Target is valid.</returns>
	private bool TryResolveTarget()
	{
		if ( _Target.IsValid() )
			return true;

		var localDrunk = Scene.GetAllComponents<DrunkCC>().FirstOrDefault( d => !d.IsProxy );
		if ( localDrunk is null )
			return false;

		_Target = localDrunk.GameObject;
		_hasOffset = false; // target changed, recompute the offset next frame
		return _Target.IsValid();
	}
}
