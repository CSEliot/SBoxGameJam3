// Project: sboxgamejam3
// File:    MainMenu.cs
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
// Title-screen main menu, merged inside the startup scene (scenes/minimal.scene) instead
// of living on its own dedicated scene. The panel is state-gated: it renders only while
// GameManager.GetLocalGameState() == LocalGameState.InMainMenu (the opaque backdrop sits
// INSIDE the gate, so it can never cover gameplay). PLAY calls GameManager.StartGame();
// SETTINGS opens the engine settings modal (mirrors EscMenu.OpenSettings); HOW TO PLAY
// opens a paginated rules/controls modal; CREDITS opens a small inline modal; QUIT closes
// the game (mirrors EscMenu/EndGame's Game.Close()). Also shows a compact Top-N
// leaderboard to the left of the menu column. The panel tree and every visual are built
// and painted in code (see MainMenu.Draw.cs); the former razor markup and scss are gone.
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
using System.Collections.Generic;
using Sandbox;

namespace Sandbox.UI;

/// <summary>
/// The title screen: menu column, leaderboard column and the two modals, all drawn with
/// Painter over a code-built panel tree.
/// </summary>
public partial class MainMenu : PaintPanelComponent
{
	/// <summary>
	/// The scene's GameManager - source of the local game state gating this panel and
	/// owner of StartGame(). Set this explicitly in the scene (do NOT rely on
	/// GetComponent on this panel's own GameObject - the menu panel and GameManager
	/// live on different objects). Unwired = menu stays hidden (fail-closed, like Hud).
	/// </summary>
	[Property] private GameManager _GameManager { get; set; }

	/// <summary>How many leaderboard rows to show, mirrors EndGame's _DisplayTopCount but
	/// smaller since this shares the screen with the menu column rather than owning it.</summary>
	[Property] private int _DisplayTopCount { get; set; } = 5;

	/// <summary>One leaderboard row as rendered on this screen, mirrors EndGame.Row.</summary>
	public readonly struct Row
	{
		public int Rank { get; init; }
		public Sandbox.Services.Leaderboards.Board2.Entry Entry { get; init; }
		public bool IsLocal { get; init; }
	}

	public bool ShowCredits { get; private set; }
	public bool ShowHowToPlay { get; private set; }

	/// <summary>How-to-play modal pages: a cover page per section (Win / Controls /
	/// Lose) plus one page per rules line, so each page has room for its own image.</summary>
	private const int PageCount = 14;
	private int CurrentPage;

	/// <summary>The panel renders only while the local client is in the main-menu
	/// state; a null (unwired) GameManager keeps it hidden.</summary>
	private bool Visible => _GameManager is not null &&
		_GameManager.GetLocalGameState() == GameManager.LocalGameState.InMainMenu;

	/// <summary>PLAY is off finding/joining a lobby (GameManager.IsSearchingForLobby): the
	/// button reads SEARCHING... and ignores clicks until it lands.</summary>
	private bool IsSearchingForLobby => _GameManager?.IsSearchingForLobby ?? false;

	private Sandbox.Services.Leaderboards.Board2 _leaderboard;
	private bool _leaderboardLoaded;
	private int _refreshKey;

	protected override void OnStart()
	{
		if ( _GameManager is null )
			Log.Warning( "MainMenu: _GameManager is not wired - the menu will stay hidden." );

		_leaderboard = Sandbox.Services.Leaderboards.GetFromStat( "score" );
		_leaderboard.SetAggregationMax(); // Best single run per player - the backend defaults to Sum, which would add every run together
		_leaderboard.SetSortDescending();
		_leaderboard.MaxEntries = 100;
	}

	protected override void OnUpdate()
	{
		// The root panel is recreated by the host without OnEnabled, so rebuild first.
		EnsureTreeHelper();

		if ( !Visible )
		{
			Panel.SetVisible( false );
			Panel.Style.PointerEvents = PointerEvents.None;
			return;
		}

		// The menu owns the whole screen while it is up: the cursor shows and clicks stay in.
		Panel.SetVisible( true );
		Panel.Style.PointerEvents = PointerEvents.All;

		// Page navigation while the how-to-play modal is up. Raw KeyboardCode names
		// (KeyTranslation lookup is case-insensitive), same style as Hud's "P" and
		// Minigame's "space". Only when visible - A/D are gameplay keys elsewhere.
		if ( Visible && ShowHowToPlay )
		{
			if ( Input.Keyboard.Pressed( "a" ) || Input.Keyboard.Pressed( "leftarrow" ) )
				PrevPage();
			else if ( Input.Keyboard.Pressed( "d" ) || Input.Keyboard.Pressed( "rightarrow" ) )
				NextPage();
		}

		if ( !_leaderboardLoaded && _leaderboard != null )
		{
			_ = RefreshLeaderboardAsync();
		}

		// Place the buttons and keep their hit areas in step with the drawing. LocalSize is
		// panel units, matching LayoutHelper and PlaceAt; the drawing converts to pixels.
		LayoutHelper( Panel.LocalSize(), true );
	}

	private async System.Threading.Tasks.Task RefreshLeaderboardAsync()
	{
		if ( _leaderboard == null )
			return;

		// Guard against firing a second overlapping refresh while the first is in flight -
		// OnUpdate keeps calling this every frame until _leaderboardLoaded flips true.
		_leaderboardLoaded = true;

		await _leaderboard.Refresh();
		_refreshKey++;
	}

	private IReadOnlyList<Sandbox.Services.Leaderboards.Board2.Entry> Leaderboard => _leaderboard?.Entries ?? Array.Empty<Sandbox.Services.Leaderboards.Board2.Entry>();

	private List<Row> TopRows
	{
		get
		{
			var list = Leaderboard;
			var local = Connection.Local;
			SteamId localSteamId = local?.SteamId ?? default;
			var rows = new List<Row>();
			int count = Math.Min( _DisplayTopCount, list.Count );
			for ( int i = 0; i < count; i++ )
			{
				// local null-guard: without it, a null Connection.Local (editor solo mode)
				// makes localSteamId default(0) and any fetched entry with SteamId 0 would
				// falsely render as the local player's row (mirrors EndGame's guard).
				rows.Add( new Row { Rank = i + 1, Entry = list[i], IsLocal = local is not null && list[i].SteamId == localSteamId } );
			}
			return rows;
		}
	}

	/// <summary>Fires on mouse-down (before the click on release) so the PLAY sfx starts the
	/// instant the button is pressed; only a left-button press reaches this.</summary>
	private void PlayPressedHelper()
	{
		if ( !Visible || IsSearchingForLobby )
			return;

		AudioController.Instance?.PlayPressPlay();
	}

	private void PlayClicked()
	{
		if ( !Visible || IsSearchingForLobby )
			return;

		_GameManager.StartGame();
	}

	private void SettingsClicked()
	{
		AudioController.Instance?.PlayUIInteract();
		Game.Overlay.ShowSettingsModal();
	}

	private void HowToPlayClicked()
	{
		AudioController.Instance?.PlayUIInteract();
		CurrentPage = 0;
		ShowHowToPlay = true;
	}

	private void CloseHowToPlay()
	{
		AudioController.Instance?.PlayUIInteract();
		ShowHowToPlay = false;
	}

	private void PrevPage()
	{
		if ( CurrentPage > 0 )
		{
			AudioController.Instance?.PlayUIInteract();
			CurrentPage--;
		}
	}

	private void NextPage()
	{
		AudioController.Instance?.PlayUIInteract();
		if ( CurrentPage < PageCount - 1 )
			CurrentPage++;
		else if ( CurrentPage == PageCount - 1 )
			CurrentPage = 0;
	}

	private void CreditsClicked()
	{
		AudioController.Instance?.PlayUIInteract();
		ShowCredits = true;
	}

	private void CloseCredits()
	{
		AudioController.Instance?.PlayUIInteract();
		ShowCredits = false;
	}

	private void QuitClicked()
	{
		AudioController.Instance?.PlayUIInteract();
		Game.Close();
	}

	/// <summary>
	/// A self-painting button that also reports the mouse-down, so the PLAY jingle can start on
	/// press rather than on release (the former markup hit both onmousedown and onclick).
	/// </summary>
	sealed class MenuButton : PaintButton
	{
		/// <summary>Invoked on a left mouse-down over this button.</summary>
		public Action Pressed { get; set; }

		protected override void OnMouseDown( MousePanelEvent e )
		{
			base.OnMouseDown( e );

			if ( e.MouseButton != MouseButtons.Left )
				return;

			Pressed?.Invoke();
		}
	}

	/// <summary>
	/// Invisible full-screen click catcher for the two modal backdrops: the darkened tint is
	/// painted by the canvas, this panel only receives the click that closes the modal.
	/// </summary>
	sealed class OverlayCatcher : Panel
	{
		/// <summary>Invoked on a click anywhere on this overlay.</summary>
		public Action Clicked { get; set; }

		public OverlayCatcher()
		{
			Style.PointerEvents = PointerEvents.All;
		}

		protected override void OnClick( MousePanelEvent e )
		{
			base.OnClick( e );

			Clicked?.Invoke();
		}
	}
}
