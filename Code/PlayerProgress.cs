// Project: sboxgamejam3
// File:    PlayerProgress.cs
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
// Per-player run state (timer, score, bars visited, emergency beer) and the
// score/timer simulation math. Implements Architecture.md's "Score & Timer
// Architecture - v0 plan": owner-authoritative simulation ([Sync] state,
// only the owning client ticks it), finalized via Sandbox.Services.Stats
// into the s&box API's persistent global leaderboard. GameManager calls
// into this component at its existing LocalGameState transition points
// (StartTimer/PauseTimer/AddTime/ResumeTimer/RegisterBarVisit) - this
// component does not poll GameManager.
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

namespace Sandbox;

/// <summary>
/// Todo: GameManager should be author of all gameplay stats. These should be moved there and ref'd here.
/// </summary>
public sealed class PlayerProgress : Component
{
	public enum RunStateEnum
	{
		PreRun,
		Running,
		Paused,
		Ended,
	}

	/// <summary>
	/// Seconds remaining in the current run. Owner-authoritative: only the owning
	/// client ticks this down (see OnUpdate); everyone else just reads it.
	/// </summary>
	[Sync] public float Timer { get; private set; }

	/// <summary>
	/// Accumulated points this run. Float internally (fractional accrual per tick);
	/// HUD/finalization floor this to an int.
	/// </summary>
	[Sync] public float Score { get; private set; }

	/// <summary>
	/// Count of finished bar visits this run, for Game Over / leaderboard display.
	/// </summary>
	[Sync] public int BarsVisited { get; private set; }

	/// <summary>
	/// One-shot timeout save (Design #20). Consumed automatically by OnTimerZero.
	/// </summary>
	[Sync] public bool HasEmergencyBeer { get; set; }

	/// <summary>
	/// Not networked: only the owning client's local loop (GameManager) reads/drives
	/// this, matching the "one local player per client" model the rest of the local
	/// loop already uses (see GameManager._localGameState).
	/// </summary>
	public RunStateEnum RunState { get; private set; } = RunStateEnum.PreRun;

	/// <summary>
	/// Design.md Screen 4: the frozen final score shown on the End-Game screen, set once by
	/// EndRun (0 on a scoreWiped timeout, floor(Score) on a voluntary cash-in). Score itself
	/// keeps its raw float value; UI should read THIS, not Score, once RunState is Ended.
	/// </summary>
	public int FinalScore { get; private set; }

	/// <summary>
	/// Design.md Screen 4: true if this run ended via a voluntary "CASH IN SCORE" (title
	/// reads "YOU CASHED IN"), false if it ended via timeout / score wipe (title reads
	/// "GAME OVER"). Only meaningful once RunState == Ended.
	/// </summary>
	public bool EndedByCashOut { get; private set; }

	/// <summary>Design #2: 2:00 on first bar exit.</summary>
	[Property] public float InitialTime { get; set; } = 120f;

	/// <summary>Design Section 1 #10: loot pickup time reward.</summary>
	[Property] public float HourglassTime { get; set; } = 3f;

	/// <summary>Design #20: auto-consumed on timeout.</summary>
	[Property] public float EmergencyBeerTime { get; set; } = 20f;

	/// <summary>Minigame time reward per beer drunk this round. Tune.</summary>
	[Property] public float SecPerBeerWon { get; set; } = 5f;

	/// <summary>North Star: 1 point/sec baseline.</summary>
	[Property] public float BasePointsPerSec { get; set; } = 1f;

	/// <summary>How hard drunkenness multiplies points. Tune.</summary>
	[Property] public float DrunkScoreScale { get; set; } = 0.1f;

	private DrunkCC _drunkCC;

	protected override void OnStart()
	{
		_drunkCC = GetComponent<DrunkCC>();
		if ( _drunkCC is null )
			Log.Error( "PlayerProgress requires a sibling DrunkCC component to read BeerLevel from." );
	}

	/// <summary>
	/// Owner-only per-frame simulation: countdown + score accrual, ONLY while Running
	/// (edge case: score must not accrue during Paused/PreRun/Ended).
	/// </summary>
	protected override void OnUpdate()
	{
		if ( !GameObject.Network.IsMine() )
			return;

		if ( Input.Keyboard.Pressed( "Q" ) )
		{
			Timer += 60;
		}

		if ( RunState != RunStateEnum.Running )
			return;

		Timer -= Time.Delta;

		float multiplier = 1f + ( _drunkCC?.BeerLevel ?? 0f ) * DrunkScoreScale;
		Score += BasePointsPerSec * multiplier * Time.Delta;

		if ( Timer <= 0f )
			OnTimerZero();
	}

	/// <summary>
	/// Enter PubCrawling for the FIRST time (Design #2: timer starts when you first
	/// leave the bar). No-ops if already started - call once, from the PreRun branch
	/// of GameManager's end-of-minigame handling.
	/// </summary>
	public void StartTimer()
	{
		if ( !GameObject.Network.IsMine() )
			return;

		Timer = InitialTime;
		RunState = RunStateEnum.Running;
	}

	/// <summary>
	/// Bar trigger hit (Design #45: timer freezes in the bar). No-op outside Running
	/// so the starting bar's own trigger (RunState still PreRun) can't pause a timer
	/// that hasn't started yet.
	/// </summary>
	public void PauseTimer()
	{
		if ( !GameObject.Network.IsMine() )
			return;

		if ( RunState != RunStateEnum.Running )
			return;

		RunState = RunStateEnum.Paused;
	}

	/// <summary>Design #46: called after AddTime, once back in PubCrawling.</summary>
	public void ResumeTimer()
	{
		if ( !GameObject.Network.IsMine() )
			return;

		if ( RunState != RunStateEnum.Paused )
			return;

		RunState = RunStateEnum.Running;
	}

	/// <summary>
	/// Adds seconds to the timer (minigame reward, hourglass pickup, emergency beer).
	/// </summary>
	public void AddTime( float seconds )
	{
		if ( !GameObject.Network.IsMine() )
			return;

		Timer += seconds;
	}

	/// <summary>Increments the bar-visit counter used for Game Over / leaderboard stats.</summary>
	public void RegisterBarVisit()
	{
		if ( !GameObject.Network.IsMine() )
			return;

		BarsVisited++;
	}

	/// <summary>
	/// Timer hit zero. Consumes an Emergency Beer if held (Design #19/#20) and stays
	/// Running; otherwise ends the run with the score wiped (Design #19).
	/// </summary>
	private void OnTimerZero()
	{
		if ( HasEmergencyBeer )
		{
			HasEmergencyBeer = false;
			if ( EmergencyBeerTime > 0 )
				AddTime( EmergencyBeerTime );
			return;
		}

		EndRun( scoreWiped: true );
	}

	/// <summary>
	/// Ends the run (voluntary cash-in or timeout) and reports the final score to the
	/// s&box API's persistent global leaderboard via Sandbox.Services.Stats.SetValue.
	/// Design Section 2: Game Over has two forms (cash-in vs timeout/score-wipe).
	/// Note: Design Screen 4 (Game Over UI) is not built yet - see
	/// sboxgamejam3-ui-context skill's "what UI actually exists" table - so this only
	/// updates state/leaderboard; nothing currently reacts visually to RunState.Ended.
	/// </summary>
	public void EndRun( bool scoreWiped )
	{
		if ( !GameObject.Network.IsMine() )
			return;

		if ( RunState == RunStateEnum.Ended )
			return;

		RunState = RunStateEnum.Ended;

		int final = scoreWiped ? 0 : (int)MathF.Floor( Score );
		FinalScore = final;
		EndedByCashOut = !scoreWiped;

		// Submit to s&box API leaderboard. The stat name "score" must be defined in the
		// game's backend (sbox.game dashboard). The API stores a single numeric value per
		// stat per player; aggregation (highest/last/etc) is configured on the backend.
		Sandbox.Services.Stats.SetValue( "score", final );
		Sandbox.Services.Stats.Flush();
	}

	/// <summary>
	/// Design.md Screen 4 "TRY AGAIN": resets this player's run back to PreRun so a fresh
	/// StartTimer() call (on next bar exit) begins a new run, exactly like a freshly spawned
	/// player. Does not touch the leaderboard - past runs already submitted stay recorded.
	/// Only the owning client may reset its own progress.
	/// </summary>
	public void ResetRun()
	{
		if ( !GameObject.Network.IsMine() )
			return;

		Timer = 0f;
		Score = 0f;
		BarsVisited = 0;
		HasEmergencyBeer = false;
		FinalScore = 0;
		EndedByCashOut = false;
		RunState = RunStateEnum.PreRun;
	}
}
