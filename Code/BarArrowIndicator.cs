// Project: sboxgamejam3
// File:    BarArrowIndicator.cs
// Author:  cseliot
// Created: 2026.09.19.00.50.48
// Edited:  2026.09.22.06.25.00
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Aims the player prefab's event-arrow model (the DrunkCC.BarArrow node) toward whichever
// bar GameManager._targetBarWaiting currently targets.
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
/// </summary>
public sealed class BarArrowIndicator : Component
{
	[Property, ReadOnly] private GameObject _TargetWorldPosition { get; set; }

	protected override void OnStart()
	{
		if ( GameObject.Network.IsProxy )
			GameObject.Enabled = false;
	}

	protected override void OnUpdate()
	{
		if ( _TargetWorldPosition is null )
			return;
		
		// Flatten to the horizontal plane so the arrow only yaws - it should never pitch up
		// or down toward a bar that's higher/lower than the player, just point the way to walk.
		// World-space aim also keeps the arrow level while the player's body rolls/leans.
		Vector3 toTarget = (_TargetWorldPosition.WorldPosition - WorldPosition).WithZ( 0f );
		if ( toTarget.IsNearlyZero() )
			return;

		WorldRotation = Rotation.LookAt( toTarget.Normal, Vector3.Up );
	}

	/// <summary>
	/// Called by GameManager every frame with the world position of the current target bar.
	/// </summary>
	public void PointAt( GameObject worldPosition )
	{
		_TargetWorldPosition = worldPosition;
	}
}
