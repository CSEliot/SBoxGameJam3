// Project: sboxgamejam3
// File:    Extensions.cs
// Author:  cseliot
// Created: 2026.09.13.21.09.39
// Edited: 2026.09.13.21.38.41
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Extension methods on engine types.
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

public static class Extensions
{
	/// <summary>
	/// True when this object is the local machine's to simulate.
	///
	/// Covers both of our cases with one check:
	///
	/// 1. Solo playtest, nothing network spawned. NetworkAccessor.Active is
	///    'go._net is not null', so with no lobby and no NetworkSpawn call there is no
	///    NetworkObject and Active is false -> true. This is what lets you hit Play in the
	///    editor without hosting and still have the local player answer true.
	///
	/// 2. Hosting (or joined), object is net-synced and owned by us. IsOwner is
	///    'OwnerId == Connection.Local.Id' -> true for our own player, false for every
	///    other client's, which is the ownership test we actually want.
	///
	/// Deliberately not '!IsProxy'. NetworkObject.UpdateIsProxy() clears IsProxy when
	/// '_isNetworkSpawning || IsOwner || (IsUnowned &amp;&amp; Networking.IsHost)', so on the host
	/// every unowned networked object in the scene also reports !IsProxy. That is correct
	/// for "should I simulate this", but wrong for "is this mine", and it would quietly
	/// hand the host every unowned prop the moment this is used outside player code.
	///
	/// Safe on child GameObjects: the Network accessor resolves to the object's
	/// NetworkRoot before answering, so calling this on a tagged child of the player
	/// prefab reports the root's ownership.
	/// </summary>
	public static bool IsMine( this GameObject.NetworkAccessor net )
	{
		if ( net is null )
			return false;

		return !net.Active || net.IsOwner;
	}

	/// <summary>
	/// Is this recursive or only returns top level children? The world may never know ...
	/// </summary>
	/// <param name="component"></param>
	/// <param name="tag"></param>
	/// <returns></returns>
	public static GameObject[] GetTagInChildren( this Component component, string tag )
	{
		var children = new List<GameObject>();
		// ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
		foreach ( var child in component.GameObject.Children )
		{
			if(child.Tags.Contains( tag ))
				children.Add(child);
		}
		return children.ToArray();
	}
}
