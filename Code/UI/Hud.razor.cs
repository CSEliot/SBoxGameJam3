// Project: sboxgamejam3
// File:    Hud.razor.cs
// Author:  cseliot
// Created: 2026.09.19
//
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
//
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
//
// Description:
// Pub-crawling HUD (Design.md, Screen 1): binding + display formatting for
// Hud.razor. Reads the local player's PlayerProgress (Timer/Score) and DrunkCC
// (BeerLevel) through a [Property] GameManager reference - never mutates them
// (PlayerProgress is owner-authoritative; this is a read-only view). All state
// lives in those components, so this partial holds no game state of its own.
//
// Naming note: this file is Hud.razor.cs (longer path than Hud.razor), so the
// engine's shortest-source-path stylesheet derivation still resolves to
// Hud.razor.scss. Do NOT add a bare Hud.cs to this partial - it would become the
// shortest path and make the panel look for a nonexistent Hud.cs.scss, silently
// loading no styles (same trap documented on Minigame).
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
using Sandbox;

namespace Sandbox.UI;

public partial class Hud : PanelComponent
{
	/// <summary>
	/// The scene's GameManager, the source of the local player's PlayerProgress/DrunkCC.
	/// Set this explicitly in the scene (do NOT rely on GetComponent on this panel's own
	/// GameObject - the HUD panel and GameManager live on different objects).
	/// </summary>
	[Property] private GameManager _GameManager { get; set; }

	/// <summary>Below this many seconds the timer text turns orange (Design Screen 1).</summary>
	[Property] private float _TimerWarnSeconds { get; set; } = 30f;

	/// <summary>Below this many seconds the timer text flashes (Design Screen 1).</summary>
	[Property] private float _TimerCriticalSeconds { get; set; } = 10f;

	/// <summary>
	/// DEBUG: show the local DrunkCC's CurrentPantsState (Up/Down) pinned mid-right edge.
	/// Toggle at runtime with F2; this is a raw sync-state readout for pants debugging,
	/// not the Design Screen 1 pants meter (which remains unimplemented).
	/// </summary>
	[Property] private bool _ShowPantsDebug { get; set; } = true;

	// Cached each frame in OnUpdate so the render-tree accessors below don't each re-resolve.
	private PlayerProgress _progress;
	private DrunkCC _drunk;

	protected override void OnStart()
	{
		if ( _GameManager is null )
			Log.Warning( "Hud: _GameManager is not wired - the HUD will stay hidden. Set the GameManager property on the UI-Hud GameObject in the scene." );
	}

	protected override void OnUpdate()
	{
		_progress = _GameManager?.GetLocalPlayerProgress();
		_drunk = _GameManager?.GetLocalDrunkCC();

		// DEBUG toggle for the pants readout (raw key, not an input action - nothing binds F2).
		if ( Input.Keyboard.Pressed( "F2" ) )
			_ShowPantsDebug = !_ShowPantsDebug;
	}

	/// <summary>
	/// Shown once the player exists AND the run is live (Running or Paused). Hidden before
	/// spawn, before the first bar starts the run (PreRun), and after Ended. Intentionally NOT
	/// gated on LocalGameState: Design #45 wants the HUD dimmed-and-paused WHILE in a bar, and
	/// the paused window coincides with WaitingToStartMinigame/PlayingMinigame (the timer is
	/// paused the same frame the state leaves PubCrawling), so gating on PubCrawling would make
	/// the paused HUD unreachable. RunState alone is the correct signal.
	/// </summary>
	private bool Visible
	{
		get
		{
			if ( _progress is null )
				return false;

			var state = _progress.RunState;
			return state == PlayerProgress.RunStateEnum.Running
				|| state == PlayerProgress.RunStateEnum.Paused;
		}
	}

	/// <summary>Design #45: timer freezes in the bar - dim the HUD and show a paused banner.</summary>
	private bool IsPaused => _progress?.RunState == PlayerProgress.RunStateEnum.Paused;

	private float TimerSeconds => MathF.Max( 0f, _progress?.Timer ?? 0f );

	/// <summary>Design Screen 1: M:SS, e.g. 2:00. Floors partial seconds so 119.6s reads 1:59.</summary>
	private string TimerText
	{
		get
		{
			int total = (int)MathF.Floor( TimerSeconds );
			int minutes = total / 60;
			int seconds = total % 60;
			return $"{minutes}:{seconds:D2}";
		}
	}

	/// <summary>orange under _TimerWarnSeconds, flashing under _TimerCriticalSeconds.</summary>
	private string TimerClass
	{
		get
		{
			if ( TimerSeconds < _TimerCriticalSeconds )
				return "critical";
			if ( TimerSeconds < _TimerWarnSeconds )
				return "warning";
			return "";
		}
	}

	/// <summary>Design Screen 1: 4-digit zero-padded score, e.g. 0042.</summary>
	private string ScoreText => ( (int)MathF.Floor( _progress?.Score ?? 0f ) ).ToString( "D4" );

	/// <summary>Design Screen 1: one-decimal beer count, e.g. 3.5.</summary>
	private string BeersText => ( _drunk?.BeerLevel ?? 0f ).ToString( "F2" );

	/// <summary>
	/// DEBUG: raw synced pants state of the local DrunkCC, e.g. "PANTS: UP".
	/// Null player still reads Up (the enum's default), matching what a proxy would receive.
	/// </summary>
	private string PantsDebugText => _drunk?.CurrentPantsState == DrunkCC.PantsState.Down ? "PANTS: DOWN" : "PANTS: UP";

	/// <summary>DEBUG: true colour so a stuck state is obvious at a glance (Up=green, Down=red).</summary>
	private bool PantsDebugIsDown => _drunk?.CurrentPantsState == DrunkCC.PantsState.Down;

	// Rebuild every frame while visible so timer/score/drunkenness track their live values;
	// hash to a constant while hidden so the idle panel stays put (mirrors Minigame.BuildHash).
	// The pants debug branch exists so the readout also tracks the synced state while the run
	// HUD itself is hidden (PreRun etc.) - that's when checking the Up default matters.
	protected override int BuildHash() => Visible
		? HashCode.Combine( Visible, RealTime.Now )
		: HashCode.Combine( Visible, _ShowPantsDebug, PantsDebugIsDown );
}
