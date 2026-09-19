// Project: sboxgamejam3
// File:    GameManager.cs
// Author:  cseliot
// Created: 2026.09.09.14.09.47
// Edited: 2026.09.09.14.11.47
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Singleton Handling Core Loop Logic
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

public sealed class GameManager : Component, Component.INetworkListener
{
	public enum LocalGameState
	{
		PubCrawling,
		WaitingToStartMinigame,
		PlayingMinigame,
	}
	
	public LocalGameState GetLocalGameState() => _localGameState;
	
	[Property] private bool _ImmediatelySpawnRunner { get; set; } = false;
	[Property] private GameObject _PlayerPrefab { get; set; }
	[Property] private GameObject _DefaultSpawnLocation { get; set; }
	[Property] private GameObject _CCCamera { get; set; }
	[Sync, Property, ReadOnly] private long _SecondsUptime { get; set; }
	private bool _canUpdateBars = false;
	private GameObject _LocalPlayer { get; set; }
	private DrunkCC _LocalDrunkCC { get; set; }
	private bool _SpawnPlayerHelperCalled { get; set; }
	private Rigidbody _LocalPlayerRigidbody { get; set; }
	private LocalGameState _localGameState = LocalGameState.WaitingToStartMinigame;
	/// <summary>
	/// Gate so a finished minigame can't immediately restart: the player must
	/// physically leave the bar trigger before another minigame can begin.
	/// Cleared when a minigame ends, re-armed on bar trigger exit.
	/// </summary>
	private bool _canStartMinigameAtBar = true;
	/// <summary>
	/// Where the bar index in the array is its ID.
	/// </summary>
	[Sync, Property] private Bar[] _Bars { get; set; } = new Bar[10];
	/// <summary>
	/// Is also the SEED for joiners' local Random!
	/// </summary>
	private int _startingBar;
	private int _totalBars;
	private float _startTime = -1;
	private bool _serverActive;
	private Random _localRandom = new(0);
	private Random _randomForNewJoiners = new(0);
	private Minigame _minigameController;
	/// <summary>
	/// The bar that player will start minigame in upon collision of sphere.
	/// </summary>
	private int _targetBarWaiting = 1;
	
	// every time a player joins, they join the latest group within 30 seconds (group has a timecreated) and if there isn't a group w 
	// timecreated within 30 seconds, make a new one.
	// also if they join as party, they get the same startingBar (targetBarWaiting).
	
	protected override void OnStart()
	{
		_targetBarWaiting = _startingBar;
		_minigameController = GetComponent<Minigame>();
		if ( _ImmediatelySpawnRunner && SpawnPlayerHelper() )
		{
			if( _LocalPlayer == null )
			{
				Log.Error( "LocalPlayer is null, despite successful spawn!" );
			}
		}
		else
		{
			if ( _ImmediatelySpawnRunner )
				Log.Error( "Failed to spawn player" );
		}
		_startTime = Time.Now;
		for ( int i = 0; i < _Bars.Length; i++ )
		{
			if ( _Bars[i] != null )
			{
				_totalBars++;
				_Bars[i].NotifyGameManagerOfPlayerTriggerEnter += HandlePlayerBarTriggerEnter;
				_Bars[i].NotifyGameManagerOfPlayerTriggerExit += HandlePlayerBarTriggerExit;
			}
		}
	}

	protected override void OnUpdate()
	{
		if ( Input.Keyboard.Down( "R" ) )
		{
			 ResetPlayerHelper();
		}

		if ( _localGameState == LocalGameState.PlayingMinigame && _minigameController.IsPlaying == false )
		{
			if ( EndMiniGameHelper( _targetBarWaiting ) )
			{	
				_localGameState = LocalGameState.PubCrawling;
				_canStartMinigameAtBar = false;
				ResetPlayerHelper(_Bars[_targetBarWaiting].PlayerSpawnLocation);
			}
			else
			{
				Log.Error("Failed to end mini game!");
			}
		}
		
		if ( _localGameState == LocalGameState.WaitingToStartMinigame )
		{
			if(StartMiniGameHelper(_targetBarWaiting))
				_localGameState = LocalGameState.PlayingMinigame;
			else
				Log.Error("Failed to start mini game!");
		}

		if ( Networking.IsHost )
		{
			if(_startTime == -1)
				_startTime = Time.Now;
			if ( _serverActive )
				_SecondsUptime = (long)(Time.Now - _startTime);
			if(_SecondsUptime % (long)60 == 0 && _canUpdateBars )
				_startingBar = _localRandom.Next(0, _totalBars);
			if(_SecondsUptime % (long)60 != 0)
				_canUpdateBars = true;
		}
	}

	private bool StartMiniGameHelper( int targetBar )
	{
		Log.Info("Starting mini game");
		_Bars[targetBar].SitDownPlayer( Connection.Local, _minigameController );
		_minigameController.Begin();
		_CCCamera.Enabled = false;
		return true;
	}

	private bool EndMiniGameHelper( int targetBar )
	{
		_Bars[targetBar].SitUpPlayer( Connection.Local);
		_LocalDrunkCC.BeerLevel += _minigameController.Beers;
		_CCCamera.Enabled = true;
		UpdateNextBarData();
		// _minigameController.End(); - mingame ends itself, itself. -ecs
		return true;
	}

	
	/// <summary>
	/// Determine their nextbar.
	/// </summary>
	private void UpdateNextBarData()
	{
		Log.Info("Updating session bar data");
		_canUpdateBars = false;
		for ( int barID = 0; barID < _Bars.Length; barID++)
		{
			if(_Bars[barID] == null)
				continue;
			
			int nextBar = barID;
			while(nextBar == barID && _totalBars > 1)
				nextBar = _localRandom.Next(0, _totalBars);
			
			if(barID == nextBar)
				nextBar = _localRandom.Next(0, _totalBars);
			_Bars[barID].NextBar = _Bars[nextBar];
		}
	}

	/// <summary>
	/// Resets all physics incoming and outgoing relative to Player and also 
	/// </summary>
	private void ResetPlayerHelper(GameObject spawnLocation = null)
	{
		spawnLocation ??= _DefaultSpawnLocation;
		
		if ( _LocalPlayerRigidbody == null )
		{
			Log.Error( "LocalPlayer Rigidbody is null in ResetPlayerHelper(), called too early or ref lost!" );
			return;
		}
		_LocalPlayer.WorldPosition = spawnLocation.WorldPosition;
		_LocalPlayer.WorldRotation = spawnLocation.WorldRotation;
		_LocalPlayerRigidbody.Velocity = Vector3.Zero;
		_LocalPlayerRigidbody.AngularVelocity = Vector3.Zero;
		_LocalPlayerRigidbody.Sleeping = true;
		_LocalPlayerRigidbody.Sleeping = false;
		_LocalPlayer.Enabled = true;
	}

	// todo: how does this work w multiplaeyr???
	/// <summary>
	/// 
	/// </summary>
	/// <returns>True if success</returns>
	private bool SpawnPlayerHelper()
	{
		if ( _SpawnPlayerHelperCalled == true )
		{
			Log.Info("Spawn Player helper already called.");
			return true;
		}
		
		_LocalPlayer = _PlayerPrefab.Clone();
		_LocalPlayerRigidbody = _LocalPlayer.GetComponent<Rigidbody>();
		_LocalDrunkCC = _LocalPlayer.GetComponent<DrunkCC>();
		if(_LocalPlayerRigidbody == null) {
			Log.Error( "LocalPlayer Rigidbody is null, despite successful spawn!" );
		}
		ResetPlayerHelper();
		return _LocalPlayer != null;
	}

	/// <summary>
	/// Only host receives this, on pc connect
	/// </summary>
	/// <param name="connection"></param>
	void INetworkListener.OnActive( Connection connection )
	{
		var spawned = SpawnPlayerHelper();
		if(spawned)
			_LocalPlayer.NetworkSpawn( connection );
		else
		{
			Log.Error( "Failed to spawn net player" );
		}

		_LocalPlayer.GetComponent<DrunkCC>().ConnectionID = connection.Id;
		_LocalPlayer.Enabled = false;
		DressPlayerHelper( _LocalPlayer, connection );
		InitialClientSetupHelper(_startingBar, connection);
	}

	/// <summary>
	/// Applies the owning player's account clothing (Steam avatar) to their own player body's
	/// Dresser. [Rpc.Broadcast] so every currently-connected client sees the same outfit on that
	/// player, mirroring the drinker-dressing pattern in Bar.cs's DressDrinkerHelper. Only the
	/// Clothing list comes from the account - Height/Age/Tint stay whatever the player prefab's
	/// Dresser was preset to (see Extensions.ApplyClothingOnlyAsync), so every player keeps the
	/// same body proportions regardless of their account avatar.
	/// </summary>
	[Rpc.Broadcast]
	private async void DressPlayerHelper( GameObject player, Connection playerConnection )
	{
		var dresser = player.GetComponentInChildren<Dresser>( true );
		if ( dresser is null || !dresser.BodyTarget.IsValid() )
		{
			Log.Error( "Player has no valid Dresser/BodyTarget, cannot apply account clothing." );
			return;
		}

		var clothing = ClothingContainer.CreateFromConnection( playerConnection );
		await dresser.ApplyClothingOnlyAsync( clothing );
	}

	[Rpc.Broadcast]
	private void InitialClientSetupHelper( int seed, Connection connection )
	{
		if ( connection == Connection.Local )
		{
			Log.Info("IS LOCAL CONNECTION GETTING H CLIENT SETUP HELPER"  );
			_serverActive = true;
			_localRandom =  new Random(seed);
			_startingBar = seed;
		}
	}

	private void HandlePlayerBarTriggerEnter( Guid playerID, Bar enteredBar )
	{
		// Only begin a mini-game from the overworld. Ignoring triggers while
		// already waiting/playing prevents mid-round retarget (which would
		// re-seat a second drinker and crash SitUpPlayer on the wrong bar),
		// and the gate stops the post-round respawn-inside-trigger re-loop.
		if ( _localGameState != LocalGameState.PubCrawling )
			return;
		if ( !_canStartMinigameAtBar )
			return;

		int barIndex = Array.IndexOf( _Bars, enteredBar );
		if ( barIndex < 0 )
		{
			Log.Error( "Entered bar not found in _Bars!" );
			return;
		}
		_targetBarWaiting = barIndex;
		_localGameState = LocalGameState.WaitingToStartMinigame;
	}
	
	private void HandlePlayerBarTriggerExit( Guid playerID )
	{
		_canStartMinigameAtBar = true;
	}
}

