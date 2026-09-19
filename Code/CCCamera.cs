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

	protected override void OnStart()
	{
		_currentBehindCap = _MaxDefaultBehindCap;
	}

	protected override void OnUpdate()
	{
		if ( (TryResolveGetTargetHelper() && TryResolveGetDrunkHelper()) == false)
			return;

		var followTarget = GetFollowTargetHelper();

		var targetPosition = followTarget.WorldPosition + _Offset;
		WorldPosition = Vector3.Lerp( WorldPosition, targetPosition, _FollowSpeed * Time.Delta );

		if ( _LookAtTarget )
		{
			var lookRotation = Rotation.LookAt( followTarget.WorldPosition - WorldPosition );
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

		if(_drunkCC.CurrentState == DrunkCC.State.Running)
		{
			UpdateBehindCapSqueezeHelper( followTarget );
			EnforceBehindCap( followTarget );
		}
		
		EnforceMinDistance( followTarget );
	}

	/// <summary>
	/// Picks which GameObject to actually follow this frame. Normally _Target itself, but while
	/// the local DrunkCC is KnockedDown (ragdolling) _Target's own transform can flail around with
	/// the ragdoll, so we temporarily follow _Target's parent instead for a steadier camera.
	/// </summary>
	private GameObject GetFollowTargetHelper()
	{
		if ( _drunkCC.CurrentState == DrunkCC.State.KnockedDown && _Target.Parent.IsValid() )
			return _Target.Parent;

		return _Target;
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
		if ( _drunkCC.IsValid() )
			return true;
		
		var drunkCC = Scene.GetAllComponents<DrunkCC>().FirstOrDefault( d => !d.IsProxy );
		if ( drunkCC is null || drunkCC.GameObject.Network.IsMine() == false)
			return false;
		
		_drunkCC = drunkCC;
		return _Target.IsValid();
	}
}
