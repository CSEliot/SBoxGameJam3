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
using System.Linq;
using Sandbox.Services;
using Sandbox.UI;

namespace Sandbox;

public sealed class GameManager : Component, Component.INetworkListener
{
	public enum LocalGameState
	{
		PubCrawling,
		/// <summary>
		/// Player is in a bar trigger, past the starting bar, deciding via the BarMenu
		/// ("ONE MORE ROUND?") whether to play the mini-game again or cash in their score.
		/// The starting bar (RunState still PreRun) skips this and goes straight to
		/// WaitingToStartMinigame - see HandlePlayerBarTriggerEnter.
		/// </summary>
		AtBarMenu,
		WaitingToStartMinigame,
		PlayingMinigame,
	}
	
	public LocalGameState GetLocalGameState() => _localGameState;



	/// <summary>
	/// The local player's PlayerProgress (timer/score/bars/emergency-beer source), or null
	/// before the player has spawned/replicated. Read-only accessor for HUD/UI: PlayerProgress
	/// is owner-authoritative, so callers must only READ it. Resolves by ownership on demand
	/// (see ResolveLocalPlayerHelper), so it is correct on every client, not just the host.
	/// </summary>
	public PlayerProgress GetLocalPlayerProgress()
	{
		ResolveLocalPlayerHelper();
		return _LocalPlayerProgress;
	}

	/// <summary>
	/// The local player's DrunkCC (BeerLevel / drunkenness source), or null before the player
	/// has spawned/replicated. Read-only accessor for HUD/UI. Resolves by ownership on demand.
	/// </summary>
	public DrunkCC GetLocalDrunkCC()
	{
		ResolveLocalPlayerHelper();
		return _LocalDrunkCC;
	}
	
	[Property] private bool _ImmediatelySpawnRunner { get; set; } = false;
	[Property] private Clothing _HeartUnderwear { get; set; }
	[Property] private Clothing _LongPants { get; set; }
	[Property] private GameObject _PlayerPrefab { get; set; }
	[Property] private GameObject _DefaultSpawnLocation { get; set; }
	[Property] private GameObject _CCCamera { get; set; }
	[Property] private GameObject _MiniGamePanel { get; set; }
	/// <summary>
	/// [Property] GameObject pointing at the scene's BarMenu UI panel object, mirroring
	/// _MiniGamePanel's wiring pattern. Resolved to _barMenuController in OnStart.
	/// </summary>
	[Property] private GameObject _BarMenuPanel { get; set; }
	/// <summary>
	/// [Property] GameObject pointing at the scene's End-Game UI panel object (Design.md
	/// Screen 4), mirroring _MiniGamePanel/_BarMenuPanel's wiring pattern. Resolved to
	/// _endGameController in OnStart.
	/// </summary>
	[Property] private GameObject _EndGamePanel { get; set; }
	[Sync, Property, ReadOnly] private long _SecondsUptime { get; set; }

	/// <summary>
	/// Where the bar index in the array is its ID. // todo: move these private var comments to same-line
	/// </summary>
	[Sync, Property] private Bar[] _Bars { get; set; } = new Bar[10];

	private bool _canUpdateBars = false;
	private GameObject _LocalPlayer { get; set; }
	private DrunkCC _LocalDrunkCC { get; set; }
	private PlayerProgress _LocalPlayerProgress { get; set; }
	private bool _SpawnPlayerHelperCalled { get; set; }
	private Rigidbody _LocalPlayerRigidbody { get; set; }
	[Property, ReadOnly] private BarArrowIndicator _LocalArrowIndicator { get; set; }
	private LocalGameState _localGameState = LocalGameState.WaitingToStartMinigame;
	private EndGame _endGameController;
	/// <summary>
	/// One-shot latch so the local player/camera freeze on entering PlayerProgress.RunState.
	/// Ended runs exactly once per Ended transition (see OnUpdate) - Try Again clears it by
	/// driving RunState back to PreRun via PlayerProgress.ResetRun().
	/// </summary>
	private bool _endGameActive;
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
	private BarMenu _barMenuController;
	/// <summary>
	/// The bar that player will start minigame in upon collision of sphere.
	/// </summary>
	[Property, ReadOnly] private int _targetBarWaiting = 1;
	
	// every time a player joins, they join the latest group within 30 seconds (group has a timecreated) and if there isn't a group w 
	// timecreated within 30 seconds, make a new one.
	// also if they join as party, they get the same startingBar (targetBarWaiting).
	
	protected override void OnStart()
	{
		_targetBarWaiting = _startingBar;
		_minigameController = _MiniGamePanel.GetComponent<Minigame>();
		_barMenuController = _BarMenuPanel != null ? _BarMenuPanel.GetComponent<BarMenu>() : null;
		if ( _barMenuController is null )
		{
			Log.Error( "_BarMenuPanel is unset or has no BarMenu component; bar menu feature disabled, bars will start the mini-game directly." );
		}
		else
		{
			_barMenuController.OnPlayMiniGame += HandleBarMenuPlayMiniGame;
			_barMenuController.OnCashOut += HandleBarMenuCashOut;
		}
		_endGameController = _EndGamePanel != null ? _EndGamePanel.GetComponent<EndGame>() : null;
		if ( _endGameController is null )
		{
			Log.Error( "_EndGamePanel is unset or has no EndGame component; End-Game screen disabled." );
		}
		else
		{
			_endGameController.OnTryAgain += HandleEndGameTryAgain;
		}
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
		ResolveLocalPlayerHelper();

		if ( Input.Keyboard.Down( "R" ) )
		{
			 ResetPlayerHelper();
		}

		// Design.md Screen 4: freeze the local player/camera the moment the run ends
		// (timeout or cash-in) so they don't keep running under the End-Game overlay.
		// One-shot via _endGameActive - Try Again re-enables both in HandleEndGameTryAgain.
		if ( _LocalPlayerProgress != null )
		{
			bool ended = _LocalPlayerProgress.RunState == PlayerProgress.RunStateEnum.Ended;
			if ( ended && !_endGameActive )
			{
				_endGameActive = true;
				// _CCCamera.Enabled = false;
				if ( _LocalPlayer != null )
					_LocalPlayer.Enabled = false;
			}
			else if ( !ended && _endGameActive )
			{
				_endGameActive = false;
			}
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
				ResetPlayerHelper(_Bars[finishedBar].ActiveBarModule.SpawnPoint, true);
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
	/// currently names. The arrow is the event-arrow node on the player prefab (resolved off
	/// DrunkCC.BarArrow in ResolveLocalPlayerHelper). Guarded against every "not ready yet"
	/// case (arrow not resolved, sparse _Bars not populated/replicated, target index out of
	/// range) since this runs every frame.
	/// </summary>
	private void UpdateArrowIndicatorHelper()
	{
		if ( _LocalArrowIndicator == null || _Bars == null )
			return;

		if ( _targetBarWaiting < 0 || _targetBarWaiting >= _Bars.Length )
			return;

		var targetBar = _Bars[_targetBarWaiting];
		if ( targetBar == null )
			return;

		_LocalArrowIndicator.PointAt( targetBar.GameObject );
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
		_LocalPlayer.Enabled = false; //todo: is this networked? -ecs
		return true;
	}

	private bool EndMiniGameHelper( int targetBar )
	{
		_Bars[targetBar].SitUpPlayer( Connection.Local);
		_LocalDrunkCC.BeerLevel += _minigameController.Beers;
		_CCCamera.Enabled = true;
		_CCCamera.WorldPosition = _Bars[targetBar].ActiveBarModule.SpawnPoint.WorldPosition;

		// Score & Timer Architecture - v0 plan, section 3: AddTime must run BEFORE the
		// state flips back to PubCrawling/ResumeTimer, or the first tick of the resumed
		// timer races the reward. First-drink case (starting bar, RunState still
		// PreRun): start the timer instead of paying a reward into a run that hasn't
		// begun yet.
		if ( _LocalPlayerProgress != null )
		{
			_LocalPlayerProgress.RegisterBarVisit();
			if ( _LocalPlayerProgress.RunState == PlayerProgress.RunStateEnum.PreRun )
			{
				_LocalPlayerProgress.StartTimer();
			}
			else
			{
				float won = ((int)_minigameController.Beers) * _LocalPlayerProgress.SecPerBeerWon;
				_LocalPlayerProgress.AddTime( won );
				_LocalPlayerProgress.ResumeTimer();
			}
		}

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
	private void ResetPlayerHelper(GameObject spawnLocation = null, bool isBarExit = false)
	{
		spawnLocation ??= _DefaultSpawnLocation;
		var spawnRotation = spawnLocation.Parent.LocalRotation;
		if(isBarExit)
			spawnRotation = spawnLocation.WorldRotation;
		
		if ( _LocalPlayerRigidbody == null )
		{
			Log.Error( "LocalPlayer Rigidbody is null in ResetPlayerHelper(), called too early or ref lost!" );
			return;
		}
		
		_LocalPlayer.WorldPosition = spawnLocation.WorldPosition;
		_LocalPlayer.WorldRotation = spawnRotation; //todo: ASAP is this fix?!
		_CCCamera.WorldPosition = spawnLocation.WorldPosition;
		_CCCamera.WorldRotation = spawnRotation;
		_LocalPlayerRigidbody.Velocity = Vector3.Zero;
		_LocalPlayerRigidbody.AngularVelocity = Vector3.Zero;
		_LocalPlayerRigidbody.Sleeping = true;
		_LocalPlayerRigidbody.Sleeping = false;
		_LocalPlayer.Enabled = true;
	}

	// todo: how does this work w multiplayer???
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
		
		var newPlayer = _PlayerPrefab.Clone();
		ResolveLocalPlayerHelper(newPlayer);
		return _LocalPlayer != null;
	}

	/// <summary>
	/// Ensures _LocalPlayer / _LocalDrunkCC / _LocalPlayerProgress / _LocalPlayerRigidbody /
	/// _LocalArrowIndicator all point at the player THIS client owns, resolving by ownership
	/// on demand. Necessary because SpawnPlayerHelper only runs host-side in
	/// INetworkListener.OnActive, once per connecting client - so on the host those fields get
	/// clobbered to the last-joined player, and on non-host clients they are never assigned at
	/// all. Mirrors the ownership-resolve pattern in CCCamera
	/// (Scene.GetAllComponents&lt;T&gt;().FirstOrDefault(!IsProxy) + IsMine()). Cheap early-out
	/// once a valid owned ref is cached; only rescans while the cache is null/stale. Read-only
	/// use: never mutates PlayerProgress (owner-authoritative).
	/// </summary>
	private void ResolveLocalPlayerHelper(GameObject localPlayer = null)
	{
		// Already holding the player we own - nothing to do.
		if ( _LocalPlayer.IsValid() && _LocalPlayer.Network.IsMine())
			return;

		localPlayer ??= Scene.FindAllWithTag("player").FirstOrDefault(p => p.Network.IsMine());
		
		if ( localPlayer is null )
		{
			Log.Error("Couldn't find local player.");
			return;
		}

		_LocalPlayer = localPlayer;
		_LocalPlayerProgress = localPlayer.GetComponent<PlayerProgress>( true );
		_LocalDrunkCC = localPlayer.GetComponent<DrunkCC>(true);
		_LocalPlayerRigidbody = localPlayer.GetComponent<Rigidbody>(true);
		_LocalArrowIndicator = _LocalDrunkCC.BarArrow.GetComponent<BarArrowIndicator>( true );

		if ( _LocalArrowIndicator == null )
		{
			Log.Error( "~~Player prefab's DrunkCC.BarArrow node is unset or has no BarArrowIndicator component; bar-direction arrow disabled." );
		}
		else
		{
			Log.Info( "~~Player prefab's DrunkCC.BarArrow node has BarArrowIndicator component; bar-direction arrow enabled." );
		}
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

	/// <summary>
	/// Score & Timer Architecture - v0 plan, section 5/6: finalization is reported to
	/// the host, which appends/updates the host-authoritative leaderboard and keeps it
	/// sorted (highest score first), capped to _MaxLeaderboardEntries. Called by
	/// PlayerProgress.EndRun on the owning client; only actually runs on the host
	/// thanks to [Rpc.Host].
	/// </summary>


	/// <summary>
	/// EndGame.OnTryAgain: player chose "TRY AGAIN" on the End-Game screen. Resets the local
	/// player's run state and respawns at the original default spawn (mirrors SpawnPlayerHelper's
	/// own initial ResetPlayerHelper() call, not the just-finished bar used mid-run), clears
	/// drunkenness back to sober, and un-freezes the camera/player that HandlePlayerBarTriggerEnter's
	/// End-Game freeze block disabled.
	/// </summary>
	private void HandleEndGameTryAgain()
	{
		_LocalPlayerProgress?.ResetRun();
		if ( _LocalDrunkCC != null )
			_LocalDrunkCC.BeerLevel = 0f;
		_targetBarWaiting = _startingBar;
		_localGameState = LocalGameState.WaitingToStartMinigame;
		_CCCamera.Enabled = true;
		ResetPlayerHelper();
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

		// Design #45: timer freezes in the bar. No-op on the starting bar (RunState
		// still PreRun - the timer hasn't started yet, see PlayerProgress.PauseTimer).
		_LocalPlayerProgress?.PauseTimer();

		// Starting bar (run hasn't begun, nothing to cash out yet): skip the "ONE MORE
		// ROUND?" menu entirely and go straight into the mini-game, same as before this
		// feature existed.
		if ( _LocalPlayerProgress == null || _LocalPlayerProgress.RunState == PlayerProgress.RunStateEnum.PreRun )
		{
			_localGameState = LocalGameState.WaitingToStartMinigame;
			return;
		}

		// Run already ended (cashed out or timed out): nothing left to decide at a bar.
		if ( _LocalPlayerProgress.RunState == PlayerProgress.RunStateEnum.Ended )
		{
			_localGameState = LocalGameState.PubCrawling;
			return;
		}

		_localGameState = LocalGameState.AtBarMenu;
		_barMenuController?.Open( _LocalPlayerProgress.Score );
		// _CCCamera.Enabled = false;
		_LocalPlayer.Enabled = false; 
	}

	/// <summary>
	/// BarMenu.OnPlayMiniGame: player chose "PLAY MINI-GAME". Hands off to the existing
	/// WaitingToStartMinigame -> StartMiniGameHelper flow, same as the starting-bar path.
	/// </summary>
	private void HandleBarMenuPlayMiniGame()
	{
		if ( _localGameState != LocalGameState.AtBarMenu )
			return;

		_localGameState = LocalGameState.WaitingToStartMinigame;
	}

	/// <summary>
	/// BarMenu.OnCashOut: player chose "CASH IN SCORE". Ends the run (Design Section 2 /
	/// Architecture.md "Score &amp; Timer Architecture - v0 plan" section 5) and reports the
	/// final score to the host-authoritative leaderboard via PlayerProgress.EndRun. No Game
	/// Over screen exists yet (sboxgamejam3-ui-context: Design Screen 4 is design-only), so
	/// this only updates state/leaderboard - left as-is, out of scope for this feature.
	/// Must release _localGameState from AtBarMenu or HandlePlayerBarTriggerEnter's
	/// PubCrawling guard would silently swallow every future bar trigger for this client.
	/// </summary>
	private void HandleBarMenuCashOut()
	{
		if ( _localGameState != LocalGameState.AtBarMenu )
			return;

		_localGameState = LocalGameState.PubCrawling;
		_LocalPlayerProgress?.EndRun( scoreWiped: false );
	}
	
	private void HandlePlayerBarTriggerExit( Guid playerID )
	{
		// // Player physically left the bar trigger without picking a BarMenu option (e.g.
		// // walked back out). Cancel the pending decision instead of leaving the timer
		// // paused and the modal stuck open forever - close the menu and resume the run
		// // exactly like never having entered the trigger.
		// if ( _localGameState == LocalGameState.AtBarMenu )
		// {
		// 	_barMenuController?.Close();
		// 	_localGameState = LocalGameState.PubCrawling;
		// 	_LocalPlayerProgress?.ResumeTimer();
		// }
	}
}

