// Project: sboxgamejam3
// File:    EndGame.cs
// Author:  cseliot
// Created: 2026.10.12
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// End-Game screen (Design.md Screen 4): shown once the local player's run
// has Ended (timeout/score-wipe or voluntary cash-in). Embeds the Top 10
// leaderboard inline (Design.md Screen 5's row spec), the local player's own
// score beneath it (WASTED stamp on an exact-zero score), then Try Again /
// Quit. Built entirely in code and drawn with Painter (no razor markup, no
// scss) - see EndGame.Draw.cs for the panel tree, layout and painting.
// OnTryAgain is a public field on this component; GameManager subscribes to
// it from outside this panel.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.UI;

public partial class EndGame : PaintPanelComponent
{
	/// <summary>The root Panel instance the code tree was last built into.</summary>
	private Panel _builtFor;

	/// <summary>The fullscreen click-through surface everything is painted on.</summary>
	private PaintCanvas _canvas;

	private PaintButton _tryAgainButton;
	private PaintButton _quitButton;

	
	/// <summary>Raised when the player clicks "TRY AGAIN". GameManager subscribes and resets
	/// the local run (PlayerProgress.ResetRun) then respawns the player.</summary>
	public Action OnTryAgain;
	
	/// <summary>One leaderboard row as rendered on this screen: its 1-based rank, the display
	/// name/score to show, and whether it belongs to the local player (for the highlight glow).
	/// Name/value are carried directly instead of a Board2.Entry because the local player's
	/// score may be corrected against a stale backend board, and Entry is a readonly struct
	/// with an internal ctor that game code cannot construct or mutate.</summary>
	public readonly struct Row
	{
		public int Rank { get; init; }
		public string DisplayName { get; init; }
		public double Value { get; init; }
		public bool IsLocal { get; init; }

		/// <summary>Copy with a recomputed 1-based rank (structs are readonly, so ranks are
		/// assigned after the merged/corrected row list has been ordered).</summary>
		public Row WithRankHelper( int rank ) => new Row
		{
			Rank = rank,
			DisplayName = DisplayName,
			Value = Value,
			IsLocal = IsLocal,
		};
	}
	
	/// <summary>
	/// The scene's GameManager, the source of the local player's PlayerProgress/DrunkCC.
	/// Set this explicitly in the scene (do NOT rely on GetComponent on this panel's own
	/// GameObject - mirrors Hud's wiring pattern).
	/// </summary>
	[Property] private GameManager _GameManager { get; set; }

	/// <summary>Design.md Screen 4/5: how many leaderboard rows to show before pinning the
	/// local player's row separately if they're outside this range.</summary>
	[Property] private int _DisplayTopCount { get; set; } = 10;


	private PlayerProgress _progress;
	private DrunkCC _drunk;
	private Sandbox.Services.Leaderboards.Board2 _leaderboard;
	private bool _leaderboardLoaded;

	/// <summary>Previous frame's Visible state, for detecting the hidden->visible transition
	/// that re-arms the leaderboard refresh on each finished run.</summary>
	private bool _leaderboardWasVisible;

	protected override void OnStart()
	{
		if ( _GameManager is null )
			Log.Warning( "EndGame: _GameManager is not wired - the End-Game screen will stay hidden. Set the GameManager property on the UI-EndGame GameObject in the scene." );

		// Initialize the s&box API leaderboard for the "score" stat
		_leaderboard = Sandbox.Services.Leaderboards.GetFromStat( "score" );
		_leaderboard.SetAggregationMax(); // Show each player's best single run - the backend defaults to Sum, which would add every run together
		_leaderboard.SetSortDescending(); // Highest scores first
		_leaderboard.MaxEntries = 100; // Fetch more than we display to find local player
	}

	private void TryAgainClicked()
	{
		if ( IsSearchingForLobby )
			return;

		AudioController.Instance?.PlayUIInteract();
		OnTryAgain?.Invoke();
	}
	protected override void OnUpdate()
	{
		EnsureTreeHelper();

		_progress = _GameManager?.GetLocalPlayerProgress();
		_drunk = _GameManager?.GetLocalDrunkCC();

		// Re-arm the one-shot leaderboard refresh every time the screen becomes visible
		// (each finished run), so the merged rows work against freshly fetched entries
		// instead of whatever the first run's fetch returned. The engine rate-limits
		// Board2.Refresh itself (static semaphore).
		bool visibleNow = Visible;
		if ( visibleNow && !_leaderboardWasVisible && !_leaderboardLoaded && _leaderboard != null )
		{
			_ = RefreshLeaderboardAsync();
		}
		if ( !visibleNow )
			_leaderboardLoaded = false;
		_leaderboardWasVisible = visibleNow;

		// Modal screen: fully interactive while the run has ended, hidden and inert otherwise.
		Panel.SetVisible( visibleNow );
		Panel.Style.PointerEvents = visibleNow ? PointerEvents.All : PointerEvents.None;

		LayoutHelper( Panel.LocalSize() );

		_tryAgainButton.Label = IsSearchingForLobby ? "SEARCHING..." : "TRY AGAIN";
		_tryAgainButton.PlaceAt( _tryAgainRect );
		_tryAgainButton.SetVisible( visibleNow );
		_quitButton.PlaceAt( _quitRect );
		_quitButton.SetVisible( visibleNow );
	}

	private async System.Threading.Tasks.Task RefreshLeaderboardAsync()
	{
		if ( _leaderboard == null )
			return;

		await _leaderboard.Refresh();
		_leaderboardLoaded = true;
	}

	/// <summary>Shown once the local run has Ended (timeout/score-wipe or cash-in).</summary>
	private bool Visible => _progress?.RunState == PlayerProgress.RunStateEnum.Ended;

	/// <summary>TRY AGAIN is off finding/joining a lobby (GameManager.IsSearchingForLobby): the
	/// button reads SEARCHING... and ignores clicks until it lands.</summary>
	private bool IsSearchingForLobby => _GameManager?.IsSearchingForLobby ?? false;

	/// <summary>Design Screen 4: "GAME OVER" on timeout, "YOU CASHED IN" on voluntary cash-in.</summary>
	private string TitleText => ( _progress?.EndedByCashOut ?? false ) ? "YOU CASHED IN" : "GAME OVER";

	/// <summary>Design Screen 4: frozen final score (0 on a wiped timeout run).</summary>
	private int FinalScore => _progress?.FinalScore ?? 0;

	/// <summary>4-digit zero-padded, e.g. 0042 or 0000.</summary>
	private string ScoreText => FinalScore.ToString( "D4" );

	/// <summary>Design Screen 4: WASTED stamp triggers on an EXACT zero score only.</summary>
	private bool IsWasted => FinalScore == 0;

	/// <summary>
	/// Crooked WASTED stamp rotation, -12 to -18 degrees (Design Screen 4), varied per local
	/// player/run via a deterministic hash rather than a fixed angle so repeat WASTED screens
	/// don't look identically neat. Purely cosmetic - not gameplay data.
	/// </summary>
	private float WastedRotationDeg
	{
		get
		{
			int seed = ( LocalDisplayName?.GetHashCode() ?? 0 ) ^ BarsVisited;
			float t = MathF.Abs( seed % 100 ) / 100f;
			return -12f - t * 6f;
		}
	}

	private string BeersText => ( _drunk?.BeerLevel ?? 0f ).ToString( "F1" );

	private int BarsVisited => _progress?.BarsVisited ?? 0;

	private string LocalDisplayName => Connection.Local?.DisplayName ?? "";

	private IReadOnlyList<Sandbox.Services.Leaderboards.Board2.Entry> Leaderboard => _leaderboard?.Entries ?? Array.Empty<Sandbox.Services.Leaderboards.Board2.Entry>();

	/// <summary>
	/// The fetched board entries with the local player's just-finished run merged in. The
	/// backend can lag the score we just submitted (and summed every run before the
	/// SetAggregationMax fix in OnStart), so the local row shows max(fetched value,
	/// FinalScore), and a player with no fetched entry yet (first run / backend lag) gets a
	/// synthetic row. ONLY the local row is ever adjusted - everyone else is rendered exactly
	/// as fetched. Stable-sorted descending by display value (equal scores keep the backend's
	/// own order), ranks recomputed as display-only positions in this merged list. Built fresh
	/// from _leaderboard.Entries + FinalScore on every call - no accumulated state.
	/// </summary>
	private List<Row> MergedRowsHelper()
	{
		var list = Leaderboard;
		var local = Connection.Local;
		var localSteamId = local?.SteamId ?? default;
		var rows = new List<Row>( list.Count + 1 );
		bool localFound = false;

		for ( int i = 0; i < list.Count; i++ )
		{
			bool isLocal = local is not null && list[i].SteamId == localSteamId;
			double value = list[i].Value;
			if ( isLocal )
			{
				localFound = true;
				// Stale-board correction: only ever raise the local score to the frozen
				// FinalScore when it beats what the backend returned - never lower it.
				if ( FinalScore > value )
					value = FinalScore;
			}
			rows.Add( new Row { Rank = i + 1, DisplayName = list[i].DisplayName, Value = value, IsLocal = isLocal } );
		}

		if ( !localFound && local is not null && FinalScore > 0 )
		{
			// First run, or the refresh hasn't returned us yet: synthetic row at FinalScore,
			// slotted below equal scores by the stable sort below.
			rows.Add( new Row { Rank = 0, DisplayName = LocalDisplayName, Value = FinalScore, IsLocal = true } );
		}

		var ordered = rows.OrderByDescending( r => r.Value ).ToList();
		for ( int i = 0; i < ordered.Count; i++ )
			ordered[i] = ordered[i].WithRankHelper( i + 1 );
		return ordered;
	}

	/// <summary>Design Screen 4/5: the always-visible Top N rows of the merged/corrected
	/// board, local player highlighted in place if they're within range.</summary>
	private List<Row> TopRows
	{
		get
		{
			var rows = MergedRowsHelper();
			return rows.GetRange( 0, Math.Min( _DisplayTopCount, rows.Count ) );
		}
	}

	/// <summary>
	/// Design Screen 4: if the local player's corrected position is outside the visible
	/// Top N, their row is pinned below it (separated by a divider) so they can always see
	/// their own placement. Null when they're within the Top N already (shown in TopRows -
	/// never both) or have no row at all yet.
	/// </summary>
	private Row? LocalPinnedRow
	{
		get
		{
			var rows = MergedRowsHelper();
			for ( int i = _DisplayTopCount; i < rows.Count; i++ )
			{
				if ( rows[i].IsLocal )
					return rows[i];
			}
			return null;
		}
	}

	private void QuitClicked()
	{
		AudioController.Instance?.PlayUIInteract();
		// Mirrors EscMenu.LeaveToMainMenu: exits playmode in editor, closes to platform main
		// menu in retail.
		Game.Close();
	}
}
