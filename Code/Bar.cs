// Project: sboxgamejam3
// File:    Bar.cs
// Author:  cseliot
// Created: 2026.09.14.16.09.57
// Edited: 2026.09.14.16.50.58
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Handles keeping data on who is at this particular bar instance, and other responsiblities.
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
using Sandbox.UI;

namespace Sandbox;

public sealed class Bar : Component, Component.ITriggerListener
{

	public Bar NextBar;
	public Action<Guid, Bar> NotifyGameManagerOfPlayerTriggerEnter;
	public Action<Guid> NotifyGameManagerOfPlayerTriggerExit;
	public BarModule ActiveBarModule;

	// /// <summary>
	// /// Players currently playing the minigame at this bar, by their client id (or whatever sandbox offers).
	// /// </summary>
	// private List<int> CurrentPlayers = [];

	/// <summary>
	/// Unique ID for this bar instance.
	/// </summary>
	// [Property, ReadOnly] private int? _ID { get; set; } //todo: BARS NO LONGER TRACK THEIR IDs??? -ecs
	[Property, ReadOnly] private string _activeModuleName { get; set; } = "";
	[Property] private GameObject[] _Drinkers { get; set; }
	private GameManager _gameManager = null;
	private Dictionary<Guid, int> _connectionToPatronIndex =  new();
	private GameObject _enterRing = null;
	
	protected override void OnStart()
	{
		_gameManager = Scene.GetAllComponents<GameManager>().First();
		if ( _gameManager is null )
			Log.Error( "GameManager not found!" );
	
		int activeModulesCount = 0;
		foreach ( var child in GetComponentsInChildren<BarModule>( true ) )
		{
			if ( child.Active )
			{
				activeModulesCount++;
				if ( activeModulesCount > 1 )
				{
					Log.Error( "Bar has multiple active modules!" );
					break;
				}
				ActiveBarModule = child;
				_activeModuleName = child.Name;
			}
		}
	}

	protected override void OnUpdate()
	{

	}

	// [Rpc.Host]: seat selection must happen exactly once, on one authoritative machine.
	// Two clients sitting down at the same bar in the same window used to each broadcast
	// their own independently-scanned "first Enabled == false" index; relative RPC arrival
	// order isn't guaranteed to match across every receiver, so client A could end up
	// holding seat 0 on the host and seat 1 on client C, with _connectionToPatronIndex
	// disagreeing between machines. Routing seat selection through the host and then
	// broadcasting the already-decided index removes that race - a non-host caller is
	// automatically forwarded to the host by the Rpc framework.
	[Rpc.Host]
	public void SitDownPlayer( Connection playerConnection, Minigame minigame )
	{
		Log.Info( "SITTING DOWN: " + playerConnection.Id );

		// Get next available drinker
		int drinkerIndex = -1;
		for ( int i = 0; i < _Drinkers.Length; i++ )
		{
			if ( _Drinkers[i].Enabled == false )
			{
				drinkerIndex = i;
				break;
			}
		}

		if ( drinkerIndex == -1 )
		{
			Log.Error( "Drinker not found! Is the bar full?" );
			return;
		}

		ApplySitDownPlayer( playerConnection, minigame, drinkerIndex );
	}

	[Rpc.Broadcast]
	private void ApplySitDownPlayer( Connection playerConnection, Minigame minigame, int drinkerIndex )
	{
		var availableDrinker = _Drinkers[drinkerIndex];
		var drinkerCam = availableDrinker.GetTagInChildren( "barcam" ).First();
		var drinkerBeerSpawnLocation = drinkerCam.GetTagInChildren( "beerspawnlocation" ).First();

		availableDrinker.Enabled = true;
		if ( _connectionToPatronIndex.ContainsKey( playerConnection.Id ) )
			_connectionToPatronIndex[playerConnection.Id] = drinkerIndex;
		else
			_connectionToPatronIndex.Add( playerConnection.Id, drinkerIndex );

		// Dress the drinker in the sitting player's account clothing. Runs on every client
		// since this whole method is [Rpc.Broadcast]. Drinkers aren't tied to one player, so
		// this gets undone again in SitUpPlayer.
		DressDrinkerHelper( availableDrinker, playerConnection );
		
		if ( playerConnection == Connection.Local )
		{
			drinkerCam.Enabled = true;
			minigame.BeerSpawnLocation = drinkerBeerSpawnLocation.WorldPosition;
		}
	}
	
	[Rpc.Broadcast]
	public void SitUpPlayer( Connection playerConnection )
	{
		// Get next available drinker
		if ( _connectionToPatronIndex.TryGetValue( playerConnection.Id, out int exitingDrinkerIndex ) == false )
		{
			Log.Warning( "SitUpPlayer called for a connection that isn't seated at this bar; ignoring." );
			return;
		}
		var exitingDrinker = _Drinkers[exitingDrinkerIndex];

		if ( exitingDrinker == null )
		{
			Log.Error( "Exiting Drinker not found! Guid issue?" );
			return;
		}

		// Only decrement once we know this connection really held a seat here.
		
		var drinkerCam = exitingDrinker.GetTagInChildren( "barcam" ).First();

		_connectionToPatronIndex.Remove( playerConnection.Id );

		// Undo the account clothing applied in SitDownPlayer so the drinker goes back to
		// its own default outfit before it's handed to the next player.
		UndressDrinkerHelper( exitingDrinker );
		
		if ( playerConnection == Connection.Local )
		{
			drinkerCam.Enabled = false;
		}
		exitingDrinker.Enabled = false;
	}

	/// <summary>
	/// Applies the sitting player's account clothing (Steam avatar) to a drinker's Dresser,
	/// leaving the drinker's own preset Height/Age/Tint untouched (see
	/// Extensions.ApplyClothingOnlyAsync - only the Clothing list comes from the account).
	/// Note: RemoveUnownedItems(Connection) only actually filters unowned items when called
	/// by the host or for the local connection (see ClothingContainer.cs) - on non-host clients
	/// receiving this broadcast for a remote player, the ownership filter silently no-ops and
	/// the outfit renders unfiltered. Accepted as a cosmetic-only limitation for this NPC.
	/// </summary>
	private async void DressDrinkerHelper( GameObject drinker, Connection playerConnection )
	{
		var dresser = drinker.GetComponent<Dresser>();
		if ( dresser is null || !dresser.BodyTarget.IsValid() )
		{
			Log.Error( "Drinker has no valid Dresser/BodyTarget, cannot apply account clothing." );
			return;
		}

		var clothing = ClothingContainer.CreateFromConnection( playerConnection );
		await dresser.ApplyClothingOnlyAsync( clothing );
	}

	/// <summary>
	/// Reverts a drinker's Dresser back to its own manually-configured outfit.
	/// Fire-and-forget: not sequenced against DressDrinkerHelper's ApplyAsync, so a very fast
	/// re-seat of the same drinker slot could theoretically race with an in-flight revert.
	/// Not observed in practice since the drinker's own manual clothing list has no pending
	/// downloads, but would need proper sequencing (e.g. via Dresser.IsDressing) if that changes.
	/// </summary>
	private void UndressDrinkerHelper( GameObject drinker )
	{
		var dresser = drinker.GetComponent<Dresser>();
		if ( dresser is null || !dresser.BodyTarget.IsValid() )
		{
			Log.Error( "Drinker has no valid Dresser/BodyTarget, cannot revert clothing." );
			return;
		}

		_ = dresser.Apply();
	}

	/// <summary>
	/// Toggles this bar's Enter-Ring particle effect (the ring node named "Enter-Ring" under
	/// the active BarModule that marks where the player should walk in; resolved by name
	/// because several nodes in the prefab share the "particles" tag). Called by GameManager
	/// whenever the current target bar changes, so only the bar the player is meant to reach
	/// next shows its ring. Returns false if the bar hasn't resolved its active module yet
	/// (OnStart still pending), so the caller retries next frame instead of silently skipping
	/// the toggle. A bar that IS started but has no ring node returns true (nothing to do).
	/// </summary>
	public bool TrySetEnterRingVisible( bool visible )
	{
		if ( _enterRing is null || !_enterRing.IsValid() )
		{
			if ( ActiveBarModule is null )
				return false;

			_enterRing = ActiveBarModule.GameObject.Children
				.FirstOrDefault( c => c.Name.Contains( "Enter-Ring", StringComparison.OrdinalIgnoreCase ) );

			if ( _enterRing is null )
			{
				Log.Warning( $"Bar '{GameObject.Name}' active module '{_activeModuleName}' has no Enter-Ring child; its ring can't be shown/hidden." );
				return true;
			}
		}

		_enterRing.Enabled = visible;
		return true;
	}

	public void OnTriggerExit( Collider other )
	{
		if(other.GameObject.Network.IsOwner)
		{
			NotifyGameManagerOfPlayerTriggerExit?.Invoke(other.GameObject.Network.OwnerId);
			// Log.Info("COLLIDdER EXIT: " + other.GameObject.Name + "Tags: " + other.GameObject.Tags);
		}

	}

	public void OnTriggerEnter( Collider other )
	{
		if(other.GameObject.Network.IsOwner)
		{
			NotifyGameManagerOfPlayerTriggerEnter?.Invoke(other.GameObject.Network.OwnerId, this);
			// Log.Info("COLLIDER Enter: " + other.GameObject.Name + "Tags: " + other.GameObject.Tags);
		}
	}

}

