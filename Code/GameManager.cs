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
// Singleton Handling Core Loop Logic. Boots into LocalGameState.InMainMenu (pre-gamestart
// state: main menu over the merged minimal.scene) and stays there until the menu calls
// StartGame(), which flips this client to WaitingToStartMinigame.
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
		InMainMenu,
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
	
	/// <summary>
	/// After this many beers, the difficulty effects that scale with drunkenness (run speed,
	/// lean tap impulse, knockdown recovery rewind) stop getting worse - they saturate at this
	/// level. BeerLevel itself keeps growing: the score multiplier and the HUD/End-Game beer
	/// readouts always use the raw level (see BeerLevelCap.md). 0 or below disables the cap
	/// entirely (difficulty keeps scaling forever, the original behavior).
	/// </summary>
	[Property] public float BeerDifficultyCap { get; set; } = 10f;
	[Property] private int _StartingBar { get; set; } = 0;
	[Property] private int _StartingSeed { get; set; } = 0;
	[Property] private Clothing _HeartUnderwear { get; set; }
	[Property] private Clothing _LongPants { get; set; }
	[Property] private GameObject _PlayerPrefab { get; set; }
	[Property] private GameObject _DefaultSpawnLocation { get; set; }
	[Property] private GameObject _CityMesh { get; set; }
	[Property] private GameObject _Dome { get; set; }
	
	[Property, ReadOnly] private GameObject _CCCamera { get; set; } = null;
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
	private LocalGameState _localGameState = LocalGameState.InMainMenu;
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
	private bool _startGameCalled;
	/// <summary>
	/// Gate for UpdateEnterRingVisibilityHelper: which _targetBarWaiting value the bar rings
	/// were last synced to. -1 = never applied yet, so the helper runs (and retries while
	/// bars aren't ready) until a pass completes.
	/// </summary>
	private int _ringsSyncedForTarget = -1;
	/// <summary>
	/// TEMP diagnostic latch (enter-ring bug hunt) - remove with the diag block.
	/// </summary>
	private bool _ringDiagLogged;

	// every time a player joins, they join the latest group within 30 seconds (group has a timecreated) and if there isn't a group w 
	// timecreated within 30 seconds, make a new one.
	// also if they join as party, they get the same startingBar (targetBarWaiting).
	
	protected override void OnStart()
	{
		Log.Info( "!!GameManager.OnStart() ID: " + Network.OwnerId );
		_targetBarWaiting = _StartingBar;
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
		// Deliberately BEFORE the local-player early-out: ring visibility only depends on
		// _Bars/_targetBarWaiting, so bars get sorted out even while sitting in the menu
		// (no local player resolves yet) instead of every prefab ring rendering until PLAY.
		UpdateEnterRingVisibilityHelper();

		if (ResolveLocalPlayerHelper() == false)
			return;

		if ( _localGameState != LocalGameState.InMainMenu && Input.Keyboard.Down( "R" ) )
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
				_StartingBar = RandomBarIndexHelper();
			if(_SecondsUptime % (long)60 != 0)
				_canUpdateBars = true;
		}

		UpdateArrowIndicatorHelper();
	}

	/// <summary>
	/// Keeps exactly one bar's Enter-Ring visible: the one at _targetBarWaiting (the bar the
	/// local player must reach next). Mirrors UpdateArrowIndicatorHelper's guards - sparse
	/// [Sync] _Bars may not be replicated yet, so a pass that finds an unready bar leaves
	/// _ringsSyncedForTarget alone and simply retries next frame. It also refuses to commit
	/// an entirely empty pass (no occupied slot toggled yet = _Bars not replicated at all),
	/// which would otherwise latch the gate at the initial target and strand the target
	/// bar's ring hidden (rings are authored Enabled=false) until the next target change.
	/// Runs on every client against its OWN target: the ring nodes are NetworkMode.Never in
	/// bar.prefab, so GameObject.Enabled here is never replicated and per-client targets
	/// can't clobber each other. A host handoff's local-object refresh can still reset the
	/// Never-mode nodes to serialized state, hence the gate re-arm in OnActive.
	/// </summary>
	private void UpdateEnterRingVisibilityHelper()
	{
		if ( _Bars == null )
			return;

		if ( _ringsSyncedForTarget == _targetBarWaiting )
			return;

		bool allReady = true;
		bool anyToggled = false;
		for ( int i = 0; i < _Bars.Length; i++ )
		{
			if ( _Bars[i] == null )
				continue;

			anyToggled = true;
			if ( !_Bars[i].TrySetEnterRingVisible( i == _targetBarWaiting ) )
				allReady = false;
		}

		if ( allReady && anyToggled )
			_ringsSyncedForTarget = _targetBarWaiting;

		// TEMP DIAGNOSTIC (enter-ring bug hunt) - remove once root-caused.
		if ( !_ringDiagLogged )
		{
			_ringDiagLogged = true;
			var sb = new System.Text.StringBuilder( $"RINGDIAG pass target={_targetBarWaiting} " );
			for ( int i = 0; i < _Bars.Length; i++ )
			{
				if ( _Bars[i] == null )
					continue;

				sb.Append( _Bars[i].EnterRingDiagHelper( i ) ).Append( "; " );
			}
			sb.Append( $"allReady={allReady} anyToggled={anyToggled} committed={_ringsSyncedForTarget}" );
			Log.Info( sb.ToString() );
		}
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
		if ( isBarExit )
		{
			spawnRotation = spawnLocation.WorldRotation;
			_LocalDrunkCC.TimeSinceBarSpawn = Time.Now;
		}
		
		if ( _LocalPlayerRigidbody == null )
		{
			Log.Error( "LocalPlayer Rigidbody is null in ResetPlayerHelper(), called too early or ref lost!" );
			return;
		}
		
		// Teleports invalidate recovery history and the double-knockdown reference point.
		_LocalDrunkCC?.InvalidateRecoveryStateHelper();
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
	private bool ResolveLocalPlayerHelper(GameObject localPlayer = null)
	{
		
		bool isSearchForLocalPlayer = false;
		if ( localPlayer == null )
			isSearchForLocalPlayer = true;
		
		if (isSearchForLocalPlayer == false && _LocalPlayer.IsValid() && _LocalPlayer.Network.IsMine())
			return true;

		localPlayer ??= Scene.Scene.FindAllWithTagOrigin("player").FirstOrDefault(p => p.Network.IsMine());
		
		if ( localPlayer is null )
		{
			// Silent while in the main menu: no local player existing yet is EXPECTED
			// during menu-sit (spawn is per-session/inert by design), and pollers call
			// this every frame - logging here floods the console for the menu's duration.
			if ( _localGameState != LocalGameState.InMainMenu )
				Log.Error("Couldn't find local player.");
			return false;
		}

		_LocalPlayer = localPlayer;
		_LocalPlayerProgress = localPlayer.GetComponent<PlayerProgress>( true );
		_LocalDrunkCC = localPlayer.GetComponent<DrunkCC>(true);
		_LocalPlayerRigidbody = localPlayer.GetComponent<Rigidbody>(true);
		_LocalArrowIndicator = _LocalDrunkCC?.BarArrow?.GetComponent<BarArrowIndicator>( true );
		
		if ( _LocalPlayerProgress == null )
		{
			if ( _localGameState != LocalGameState.InMainMenu )
				Log.Warning("Couldn't find local player's progress component. " + localPlayer.Name);
			return false;
		}

		if ( _LocalDrunkCC == null )
		{
			if ( _localGameState != LocalGameState.InMainMenu )
				Log.Warning("Couldn't find local player's DrunkCC component.");
			return false;
		}

		if ( _LocalPlayerRigidbody == null )
		{
			if ( _localGameState != LocalGameState.InMainMenu )
				Log.Warning("Couldn't find local player's Rigidbody component.");
			return false;
		}

		if ( _LocalArrowIndicator == null )
		{
			if ( _localGameState != LocalGameState.InMainMenu )
				Log.Warning("Couldn't find local player's BarArrowIndicator component.");
			return false;
		}
		
		return true;
	}

	/// <inheritdoc/>
	void INetworkListener.OnActive( Connection connection )
	{
		if ( connection == Connection.Local )
		{
			Log.Info("IS LOCAL CONNECTION GETTING H CLIENT SETUP HELPER"  );
			_serverActive = true;
			_localRandom =  new Random(_StartingSeed);
			_targetBarWaiting = _StartingBar;
			// Re-arm the ring gate: becoming (or (re)starting as) host triggers a scene
			// handoff refresh that re-Deserializes NetworkMode.Never objects back to their
			// authored Enabled=false, silently wiping whatever ring visibility this client
			// had applied locally. Forcing the next OnUpdate pass to re-apply fixes it.
			_ringsSyncedForTarget = -1;
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
		_targetBarWaiting = _StartingBar;
		_localGameState = LocalGameState.WaitingToStartMinigame;
		_CCCamera.Enabled = true;
		ResetPlayerHelper();
	}

	/// <summary>
	/// MainMenu PLAY click. Per-client only (D-B): no RPC of any kind - it just flips THIS
	/// client from InMainMenu to WaitingToStartMinigame, and the existing OnUpdate
	/// WaitingToStartMinigame block drives the rest of the flow unchanged.
	/// Solo/editor fallback: when no local player can be resolved AND no network session is
	/// active, clone the player prefab at the default spawn ourselves - deliberately WITHOUT
	/// NetworkSpawn (it returns false in editor scenes; a non-networked clone still satisfies
	/// Extensions.IsMine(), so ResolveLocalPlayerHelper picks it up). When Networking IS active
	/// and no player resolves, log an error and do NOT clone: NetworkHelper owns spawning
	/// players in sessions. Mirrors NetworkHelper.OnActive's Clone(WorldTransform) idiom.
	/// The state flips to WaitingToStartMinigame ONLY once a local player actually resolves;
	/// otherwise it stays InMainMenu (menu visible, pollers silent) and PLAY is retryable -
	/// e.g. a late joiner clicking before the host's spawn has replicated just tries again.
	/// </summary>
	public void StartGame()
	{
		if ( _localGameState != LocalGameState.InMainMenu || _startGameCalled)
			return;

		_startGameCalled = true;

		_Dome.Enabled = true;
		_CityMesh.Enabled = true;
		
		if ( ResolveLocalPlayerHelper() == false )
		{
			if ( Networking.IsActive )
			{
				Log.Error( "StartGame: local player not resolved while a network session is active; NetworkHelper owns player spawning - not cloning. Staying in InMainMenu; retry PLAY once the player replicates." );
				return;
			}

			if ( _PlayerPrefab.IsValid() && _DefaultSpawnLocation.IsValid() )
			{
				_PlayerPrefab.Clone( _DefaultSpawnLocation.WorldTransform.WithScale( 1 ) );
			}
			else
			{
				Log.Error( "StartGame: solo fallback spawn skipped - _PlayerPrefab or _DefaultSpawnLocation is unset." );
			}

			// Solo fallback either cloned or failed: only leave the menu with a real
			// local player, otherwise stay InMainMenu so the menu stays up and PLAY
			// can be retried (a misconfigured prefab ref must not soft-start the game).
			if ( ResolveLocalPlayerHelper() == false )
				return;
		}

		_localGameState = LocalGameState.WaitingToStartMinigame;
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

	/// <summary>
	/// This is called by the proxy and nonproxy players when they spawn in.
	/// </summary>
	/// <param name="connection"></param>
	/// <param name="newPlayer"></param>
	public void OnSpawn( Connection connection, GameObject newPlayer )
	{
		if ( Connection.Local.Id == connection.Id )
		{
			ResolveLocalPlayerHelper(newPlayer);
		}
	}
	
}

