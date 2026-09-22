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
/// Sits on the player prefab's event-arrow node (wired into DrunkCC.BarArrow). GameManager
/// resolves it off the local player's DrunkCC and calls PointAt every frame with the world
/// position of the bar at _targetBarWaiting; this component owns the node's WORLD rotation,
/// yawing it level at the target so it's always readable, matching Design.md's "Arrow
/// indicator stays crisp, gameplay critical" rule. Below this node the model hangs off an
/// intermediate tilt node ("Object", pitched -90° about Y so the model's local Z lies along
/// the pointing axis) and carries a SpinMe that rolls the arrow about its own shaft -
/// local-space spin on descendants composes with this world-space aim instead of fighting it.
/// The arrow is local-only (nobody feeds PointAt for remote players), so on proxies the
/// node is disabled in OnStart - same visibility the old runtime-spawned indicator had.
/// </summary>
public sealed class BarArrowIndicator : Component
{
	private Vector3? _targetWorldPosition;

	protected override void OnStart()
	{
		// Remote players' copies would sit frozen pointing forward (PointAt only runs on
		// the owning client's GameManager), so hide them - matches the old local-only
		// runtime-spawned indicator's behavior.
		if ( GameObject.Network.IsProxy )
			GameObject.Enabled = false;
	}

	protected override void OnUpdate()
	{
		if ( _targetWorldPosition is null )
			return;

		// Flatten to the horizontal plane so the arrow only yaws - it should never pitch up
		// or down toward a bar that's higher/lower than the player, just point the way to walk.
		// World-space aim also keeps the arrow level while the player's body rolls/leans.
		Vector3 toTarget = (_targetWorldPosition.Value - WorldPosition).WithZ( 0f );
		if ( toTarget.IsNearlyZero() )
			return;

		WorldRotation = Rotation.LookAt( toTarget.Normal, Vector3.Up );
	}

	/// <summary>
	/// Called by GameManager every frame with the world position of the current target bar.
	/// </summary>
	public void PointAt( Vector3 worldPosition )
	{
		_targetWorldPosition = worldPosition;
	}
}
