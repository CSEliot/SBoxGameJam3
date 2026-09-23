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

using System.Threading.Tasks;

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
	
	/// <summary>
	/// Is this recursive or only returns top level children? The world may never know ...
	/// </summary>
	/// <param name="gameObject"></param>
	/// <param name="tag"></param>
	/// <returns></returns>
	public static GameObject[] GetTagInChildren( this GameObject gameObject, string tag )
	{
		var children = new List<GameObject>();
		// ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
		foreach ( var child in gameObject.Children )
		{
			if(child.Tags.Contains( tag ))
				children.Add(child);
		}
		return children.ToArray();
	}

	/// <summary>
	/// Same as <see cref="Scene.FindAllWithTag"/> but only returns the objects where the tag
	/// originates: the object either has no parent, or its parent does not have that tag
	/// (checked with inherited tags too, so an object whose ancestor carries the tag is not
	/// an origin). Because Scene.Parent is null and the tag walk stops at the scene, a
	/// top-level object in the scene counts as an origin unless the scene itself is tagged.
	/// Effectively: the topmost tagged object of each tagged subtree.
	/// </summary>
	public static IEnumerable<GameObject> FindAllWithTagOrigin( this Scene scene, string tag )
	{
		return scene.Scene.FindAllWithTag( tag ).Where( x => x.Parent is null || !x.Parent.Tags.Has( tag ) );
	}

	/// <summary>
	/// Applies only the Clothing list from an account's ClothingContainer to this Dresser's
	/// BodyTarget - Height, Age and Tint are overwritten with the Dresser's own preset
	/// (Manual*) values first, so an account's appearance data never overrides a character's
	/// designed look (e.g. a drinker NPC or the player body's baked height/age/tint). Height
	/// is neutralized to 1 when ApplyHeightScale is off, matching Dresser.Apply()'s own
	/// convention for "don't scale this body at all".
	///
	/// Only meaningful when dresser.Source == Manual: that's the only mode where ManualHeight/
	/// ManualAge/ManualTint are fixed author-set presets rather than a readback of whatever the
	/// last-applied ClothingContainer (LocalUser/OwnerConnection - i.e. a real account's own
	/// data) produced. Called on a non-Manual Dresser this would feed a real account's
	/// height/age/tint back in as the "preset", silently defeating the whole point.
	///
	/// Also cancels any in-flight/queued dressing on this Dresser first. Dresser.OnAwake() fires
	/// its own fire-and-forget Apply() on every non-proxy Dresser the moment the GameObject is
	/// created (e.g. immediately on Clone(), before a caller gets a chance to call this), and
	/// both paths call ClothingContainer.ApplyAsync on the SAME BodyTarget (Reset + recreate
	/// clothing children) with no other coordination - unsequenced, last-to-finish wins.
	/// </summary>
	public static async Task ApplyClothingOnlyAsync( this Dresser dresser, ClothingContainer accountClothing )
	{
		if ( dresser is null || accountClothing is null || !dresser.BodyTarget.IsValid() )
			return;

		if ( dresser.Source != Dresser.ClothingSource.Manual )
		{
			Log.Warning( $"ApplyClothingOnlyAsync called on a Dresser with Source={dresser.Source}, not Manual - " +
				"Height/Age/Tint presets aren't fixed in that mode, refusing to apply." );
			return;
		}

		// Cancel Dresser's own OnAwake auto-apply (or any other in-flight Apply()) so it can't
		// race with the ApplyAsync below on the same BodyTarget.
		dresser.CancelDressing();

		accountClothing.Height = dresser.ApplyHeightScale ? dresser.ManualHeight : 1f;
		accountClothing.Age = dresser.ManualAge;
		accountClothing.Tint = dresser.ManualTint;

		await accountClothing.ApplyAsync( dresser.BodyTarget, default );
	}
}
