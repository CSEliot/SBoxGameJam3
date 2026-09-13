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
	/// How fast to reach max distance.
	/// </summary>
	[Property, MinMax(1,99)] private float _ScaleSpeedToBeerLevel { get; set; }
	
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
	[Property] private GameObject _Target { get; set; }
	
	/// <summary>
	/// How quickly this object catches up to the target's position. Higher is snappier.
	/// </summary>
	[Property] private float _FollowSpeed { get; set; } = 5f;

	/// <summary>
	/// If true, this object rotates to face the target instead of keeping a fixed rotation.
	/// </summary>
	[Property] private bool _LookAtTarget { get; set; }

	/// <summary>
	/// How quickly this object turns to face the target when _LookAtTarget is enabled. Higher is snappier.
	/// </summary>
	[Property] private float _LookAtSpeed { get; set; } = 5f;
	[Property] private Vector3 _Offset { get; set; }

	/// <summary>
	/// How far the camera is allowed to drift angularly from directly behind the target.
	/// 0 = locked perfectly behind (0 radians). 1 = up to PI radians (no real constraint).
	/// Typical values: 0.15 (~27 deg) to 0.5 (~90 deg).
	/// </summary>
	[Property, MinMax(0f, 1f)] private float _BehindCap { get; set; } = 0.25f;

	/// <summary>
	/// Closest the camera is allowed to get to the target, in units.
	/// If the camera ends up nearer than this it is pushed straight back out along
	/// its current direction from the target. 0 disables the check.
	/// </summary>
	[Property] private float _MinDistance { get; set; } = 150f;

	private float _startJerkTime;
	/// <summary>
	/// Manual target override. If unset, the local (non-proxy) DrunkCC in the scene is used.
	/// </summary>
	[Property] private DrunkCC _drunkCC
	{
		get;
		set;
	}


	protected override void OnStart()
	{
		
	}

	protected override void OnUpdate()
	{
		if ( (TryResolveGetTargetHelper() && TryResolveGetDrunkHelper()) == false)
			return;

		var targetPosition = _Target.WorldPosition + _Offset;
		WorldPosition = Vector3.Lerp( WorldPosition, targetPosition, _FollowSpeed * Time.Delta );

		if ( _LookAtTarget )
		{
			var lookRotation = Rotation.LookAt( _Target.WorldPosition - WorldPosition );
			WorldRotation = Rotation.Lerp( WorldRotation, lookRotation, _LookAtSpeed * Time.Delta );
		}
		
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

		EnforceBehindCap();
		EnforceMinDistance();
	}

	/// <summary>
	/// Pushes the camera back out to _MinDistance if it has drifted closer than that to the target.
	/// Direction from the target is preserved, only the distance is corrected.
	/// </summary>
	private void EnforceMinDistance()
	{
		if ( _MinDistance <= 0f ) return;

		var currentOffset = WorldPosition - _Target.WorldPosition;
		var distance = currentOffset.Length;

		if ( distance >= _MinDistance ) return;

		// Degenerate case: camera sitting on top of the target gives no usable direction,
		// so fall back to placing it directly behind.
		var direction = distance < 0.0001f
			? -_Target.WorldRotation.Forward
			: currentOffset.Normal;

		WorldPosition = _Target.WorldPosition + direction * _MinDistance;
	}

	/// <summary>
	/// Constrains the camera to stay within an angular cone behind the target.
	/// _BehindCap is 0-1 where 0 = locked perfectly behind, 1 = no constraint.
	/// </summary>
	private void EnforceBehindCap()
	{
		if ( _BehindCap >= 1f ) return;

		var currentOffset = WorldPosition - _Target.WorldPosition;
		if ( currentOffset.LengthSquared < 0.0001f ) return;

		var distance = currentOffset.Length;
		var forwardDir = _Target.WorldRotation.Forward;
		var idealBehindPos = _Target.WorldPosition - forwardDir * distance;
		var idealOffset = idealBehindPos - _Target.WorldPosition;

		// Check angle between current position and ideal behind
		var dot = Vector3.Dot( currentOffset.Normal, idealOffset.Normal ).Clamp( -1f, 1f );
		var angle = MathF.Acos( dot );
		var maxAngle = _BehindCap * MathF.PI;

		if ( angle <= maxAngle ) return;

		// Use spherical interpolation for correct angular correction
		var correctionFactor = (angle - maxAngle) / angle;
		var correctedDir = Vector3.Slerp( currentOffset.Normal, idealOffset.Normal, correctionFactor );
		var correctedOffset = correctedDir * distance;

		WorldPosition = _Target.WorldPosition + correctedOffset;
	}

	/// <summary>
	/// Handles the lateral movement of the camera when the player hits left or right.
	/// </summary>
	/// <param name="direction"></param>
	private void DoJerkHelper( bool direction )
	{
		// 2x^3-3x^2+1
		
		float jerkSpeedBeerified = _JerkSpeed * _ScaleSpeedToBeerLevel;
		
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
	private bool TryResolveGetTargetHelper()
	{
		if ( _Target.IsValid() )
			return true;

		var localCamTarget = Scene.FindAllWithTag( "follow-target" ).FirstOrDefault( d => !d.IsProxy );
		if ( localCamTarget is null || localCamTarget.Network.IsMine() == false)
			return false;

		_Target = localCamTarget;
		return _Target.IsValid();
	}
	
	/// <summary>
	/// Ensures _Target points at a valid GameObject, falling back to the scene's local DrunkCC.
	/// </summary>
	/// <returns>True if _Target is valid.</returns>
	private bool TryResolveGetDrunkHelper()
	{
		if ( _Target.IsValid() )
			return true;
		
		var drunkCC = Scene.GetAllComponents<DrunkCC>().FirstOrDefault( d => !d.IsProxy );
		if ( drunkCC is null || drunkCC.GameObject.Network.IsMine() == false)
			return false;
		
		_drunkCC = drunkCC;
		return _Target.IsValid();
	}
}
