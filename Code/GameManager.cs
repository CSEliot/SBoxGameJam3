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
	[Property] private GameObject _MiniGamePanel { get; set; }
	[Sync, Property, ReadOnly] private long _SecondsUptime { get; set; }
	private bool _canUpdateBars = false;
	private GameObject _LocalPlayer { get; set; }
	private DrunkCC _LocalDrunkCC { get; set; }
	private bool _SpawnPlayerHelperCalled { get; set; }
	private Rigidbody _LocalPlayerRigidbody { get; set; }
	private BarArrowIndicator _LocalArrowIndicator { get; set; }
	private LocalGameState _localGameState = LocalGameState.WaitingToStartMinigame;
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
		_minigameController = _MiniGamePanel.GetComponent<Minigame>();
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
			int finishedBar = _targetBarWaiting;
			if ( EndMiniGameHelper( finishedBar ) )
			{	
				_localGameState = LocalGameState.PubCrawling;
				// Respawn at the bar we JUST finished, not the advanced target.
				// EndMiniGameHelper has moved _targetBarWaiting to NextBar, so the
				// finished bar's trigger no longer matches and won't re-loop; the
				// player must travel to the next bar to start again.
				ResetPlayerHelper(_Bars[finishedBar].PlayerSpawnLocation);
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
				_startingBar = RandomBarIndexHelper();
			if(_SecondsUptime % (long)60 != 0)
				_canUpdateBars = true;
		}

		UpdateArrowIndicatorHelper();
	}

	/// <summary>
	/// Keeps the local player's floating arrow pointed at whichever bar _targetBarWaiting
	/// currently names. Guarded against every "not ready yet" case (arrow not spawned, sparse
	/// _Bars not populated/replicated, target index out of range) since this runs every frame.
	/// </summary>
	private void UpdateArrowIndicatorHelper()
	{
		if ( _LocalArrowIndicator == null || _Bars == null )
			return;

		if ( _targetBarWaiting < 0 || _targetBarWaiting >= _Bars.Length )
			return;

		Bar targetBar = _Bars[_targetBarWaiting];
		if ( targetBar == null )
			return;

		_LocalArrowIndicator.PointAt( targetBar.WorldPosition );
	}

	private bool StartMiniGameHelper( int targetBar )
	{
		// Guard against the first-round / freshly-joined case: the sparse [Sync] _Bars
		// array may not have replicated yet, or targetBar may point at a null slot. This
		// is an EXPECTED transient during replication, so warn (not error) and return
		// false so Block START stays in WaitingToStartMinigame and simply retries next
		// frame once _Bars is populated.
		if ( _Bars == null || targetBar < 0 || targetBar >= _Bars.Length || _Bars[targetBar] == null )
		{
			Log.Warning( $"StartMiniGameHelper: bar {targetBar} not ready (null/out-of-range); retrying next frame." );
			return false;
		}
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
		// Advance the target to this bar's freshly-assigned NextBar so the player
		// must reach a DIFFERENT bar to start again; respawning inside the just-
		// finished bar's trigger no longer matches _targetBarWaiting.
		Bar nextBar = _Bars[targetBar].NextBar;
		int nextBarIndex = nextBar != null ? Array.IndexOf( _Bars, nextBar ) : -1;
		if ( nextBarIndex >= 0 )
			_targetBarWaiting = nextBarIndex;
		else
			Log.Error( "NextBar is null or not in _Bars; _targetBarWaiting unchanged (check UpdateNextBarData / sparse _Bars)." );
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

			int nextBar = RandomOtherBarIndexHelper(barID);
			_Bars[barID].NextBar = _Bars[nextBar];
		}
	}

	/// <summary>
	/// Returns a valid (non-null) index into the sparse _Bars array, chosen uniformly
	/// among the occupied slots. Uses _localRandom so the pick stays deterministic per
	/// client. Falls back to 0 only if no bars exist (should not happen in a live session).
	/// </summary>
	private int RandomBarIndexHelper()
	{
		List<int> occupied = new();
		for ( int i = 0; i < _Bars.Length; i++ )
		{
			if ( _Bars[i] != null )
				occupied.Add( i );
		}
		if ( occupied.Count == 0 )
			return 0;
		return occupied[_localRandom.Next( 0, occupied.Count )];
	}

	/// <summary>
	/// Returns a valid (non-null) index into the sparse _Bars array, excluding excludeBarID
	/// when another occupied slot exists. If excludeBarID is the only occupied slot it is
	/// returned as-is (no infinite loop / empty-list crash).
	/// </summary>
	private int RandomOtherBarIndexHelper(int excludeBarID)
	{
		List<int> occupied = new();
		for ( int i = 0; i < _Bars.Length; i++ )
		{
			if ( _Bars[i] != null && i != excludeBarID )
				occupied.Add( i );
		}
		if ( occupied.Count == 0 )
			return excludeBarID;
		return occupied[_localRandom.Next( 0, occupied.Count )];
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
		SpawnArrowIndicatorHelper();
		ResetPlayerHelper();
		return _LocalPlayer != null;
	}

	/// <summary>
	/// Creates the floating 3D arrow above the local player's head that points at the bar
	/// _targetBarWaiting. Only spawned once per SpawnPlayerHelper() call (mirrors the rest of
	/// that method's one-shot setup); OnUpdate keeps it pointed at the live target every frame.
	/// </summary>
	private void SpawnArrowIndicatorHelper()
	{
		if ( _LocalArrowIndicator != null )
			return;

		if ( _LocalPlayer == null )
		{
			Log.Error( "SpawnArrowIndicatorHelper called before LocalPlayer exists!" );
			return;
		}

		var arrowObject = new GameObject( _LocalPlayer, true, "bar_arrow_indicator" );
		_LocalArrowIndicator = arrowObject.AddComponent<BarArrowIndicator>();
	}

	/// <summary>
	/// Only host receives this, on pc connect
	/// </summary>
	/// <param name="connection"></param>
	void INetworkListener.OnActive( Connection connection )
	{
		var spawned = SpawnPlayerHelper();
		if(spawned)
			_LocalPlayer.NetworkSpawn( connection ); //todo: ASAP - is "network spawn" not to be done w .Clone??
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
			// The seed doubles as the starting bar index. OnStart ran BEFORE this RPC
			// landed, so it captured _targetBarWaiting = _startingBar while _startingBar
			// was still 0. Reassign it now so the first target reflects the real seed.
			_targetBarWaiting = _startingBar;
		}
	}

	private void HandlePlayerBarTriggerEnter( Guid playerID, Bar enteredBar )
	{
		// Only begin a mini-game from the overworld, and only at the bar the
		// player is currently being sent to. Any other bar's trigger is ignored,
		// which also means the post-round respawn (which lands back inside the
		// finished bar's trigger) can't restart it, because _targetBarWaiting has
		// already advanced to the next bar in EndMiniGameHelper.
		if ( _localGameState != LocalGameState.PubCrawling )
			return;

		int barIndex = Array.IndexOf( _Bars, enteredBar );
		if ( barIndex != _targetBarWaiting )
			return;

		_localGameState = LocalGameState.WaitingToStartMinigame;
	}
	
	private void HandlePlayerBarTriggerExit( Guid playerID )
	{
		
	}
}

