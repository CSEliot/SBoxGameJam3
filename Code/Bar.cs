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

	/// <summary>
	/// Players currently playing the minigame at this bar, by their client id (or whatever sandbox offers).
	/// </summary>
	private List<int> CurrentPlayers = [];

	[Sync, Property, ReadOnly] private int _CurrentPatronCount { get; set; } = 0;
	/// <summary>
	/// Unique ID for this bar instance.
	/// </summary>
	// [Property, ReadOnly] private int? _ID { get; set; } //todo: BARS NO LONGER TRACK THEIR IDs??? -ecs
	[Property, ReadOnly] private string _activeModule { get; set; } = "";
	[Property] private GameObject[] _Drinkers { get; set; }
	[Property] public GameObject PlayerSpawnLocation { get; private set; }
	private GameManager _gameManager = null;
	private readonly List<BarModule> _barModules = [];
	private Dictionary<Guid, int> _connectionToPatronIndex =  new();
	
	protected override void OnStart()
	{
		_gameManager = Scene.GetAllComponents<GameManager>().First();
		if ( _gameManager is null )
			Log.Error( "GameManager not found!" );
	
		foreach ( var child in GetComponentsInChildren<BarModule>( true ) )
		{
			_barModules.Add( child );
			if ( child.Active )
			{
				if ( _activeModule != "" )
					Log.Error( "Bar has multiple active modules!" );
				_activeModule = child.Name;
			}
		}
	}

	protected override void OnUpdate()
	{

	}

	[Rpc.Broadcast]
	public void SitDownPlayer( Connection playerConnection, Minigame minigame )
	{
		Log.Info( "SITTING DOWN: " + playerConnection.Id );
		_CurrentPatronCount++;
		
		// Get next available drinker
		GameObject availableDrinker = null;
		int drinkerIndex = -1;
		for ( int i = 0; i < _Drinkers.Length; i++ )
		{
			if ( _Drinkers[i].Enabled == false )
			{
				availableDrinker = _Drinkers[i];
				drinkerIndex = i;
				break;
			}
		}

		if ( availableDrinker == null )
		{
			Log.Error( "Drinker not found! Is the bar full?" );
			return;
		}
		
		var drinkerCam = availableDrinker.GetTagInChildren( "barcam" ).First();
		var drinkerBeerSpawnLocation = drinkerCam.GetTagInChildren( "beerspawnlocation" ).First();

		availableDrinker.Enabled = true;
		_connectionToPatronIndex.Add( playerConnection.Id, drinkerIndex );

		// Dress the drinker in the sitting player's account clothing. Runs on every client
		// since this whole method is [Rpc.Broadcast]. Drinkers aren't tied to one player, so
		// this gets undone again in SitUpPlayer.
		DressDrinkerHelper( availableDrinker, playerConnection );
		
		if ( IsProxy == false)
		{
			Log.Info( "NOT PROXY" );
			drinkerCam.Enabled = true;
			minigame.BeerSpawnLocation = drinkerBeerSpawnLocation.WorldPosition;
		}
	}
	
	[Rpc.Broadcast]
	public void SitUpPlayer( Connection playerConnection )
	{
		_CurrentPatronCount--;
		
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
		
		var drinkerCam = exitingDrinker.GetTagInChildren( "barcam" ).First();

		_connectionToPatronIndex.Remove( playerConnection.Id );

		// Undo the account clothing applied in SitDownPlayer so the drinker goes back to
		// its own default outfit before it's handed to the next player.
		UndressDrinkerHelper( exitingDrinker );
		
		if ( IsProxy == false)
		{
			drinkerCam.Enabled = false;
		}
		exitingDrinker.Enabled = false;
	}

	/// <summary>
	/// Applies the sitting player's account clothing (Steam avatar) to a drinker's Dresser.
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
		await clothing.ApplyAsync( dresser.BodyTarget, default );
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

	public void OnTriggerExit( Collider other )
	{
		if(other.GameObject.Network.IsOwner)
		{
			NotifyGameManagerOfPlayerTriggerExit?.Invoke(other.GameObject.Network.OwnerId);
			Log.Info("COLLIDdER EXIT: " + other.GameObject.Name + "Tags: " + other.GameObject.Tags);
		}

	}

	public void OnTriggerEnter( Collider other )
	{
		if(other.GameObject.Network.IsOwner)
		{
			NotifyGameManagerOfPlayerTriggerEnter?.Invoke(other.GameObject.Network.OwnerId, this);
			Log.Info("COLLIDER Enter: " + other.GameObject.Name + "Tags: " + other.GameObject.Tags);
		}
	}

}

