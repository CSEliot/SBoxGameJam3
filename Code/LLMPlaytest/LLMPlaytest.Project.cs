// Project: sboxgamejam3
// File:    LLMPlaytest.Project.cs
// Author:  cseliot
// Created: 2026.10.10.00.00.00
// Edited:  2026.10.10.00.00.00
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Project-specific half of the LLM playtest harness: the shortcut handlers and
// the extra telemetry fields for THIS game. This is the ONLY file rewritten when
// the Code/LLMPlaytest/ folder is copied into another project.
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
using System.Globalization;
using System.Linq;

namespace Sandbox;

public sealed partial class LLMPlaytest
{
	private const float StartTimeoutSeconds = 30f;

	// Change-detection memory (events fire on change only).
	private string _lastGameState = "", _lastPlayerState = "", _lastRunState = "";
	private float _lastBeers = float.NaN;
	private bool _firstSample = true;
	private readonly HashSet<GameObject> _knownCars = new();

	// Blocking `shortcut start` state.
	private bool _startWaiting;
	private float _startDeadline;
	private string _startFrom = "";

	// Handled: start, beers <n>, teleport <x> <y> <z>, time [seconds]. Shortcuts are cheats by design.
	partial void ProjectShortcutHelper( string name, string[] args, ref bool handled )
	{
		switch ( name )
		{
			case "start":
				handled = true;
				RunStartShortcutHelper();
				break;
			case "beers":
				handled = RunBeersShortcutHelper( args );
				break;
			case "teleport":
				handled = RunTeleportShortcutHelper( args );
				break;
			case "time":
				handled = RunTimeShortcutHelper( args );
				break;
		}
	}

	// StartGame is async when alone; block the script until PubCrawling, or report the stuck state after 30 s.
	private void RunStartShortcutHelper()
	{
		var gm = GetGameManagerHelper();
		if ( !gm.IsValid() )
		{
			EmitEventHelper( "error", "shortcut start reason=no-gamemanager" );
			return;
		}

		var before = gm.GetLocalGameState();
		if ( before == GameManager.LocalGameState.PubCrawling )
		{
			EmitEventHelper( "shortcut", $"start from={before} to={before}" );
			return;
		}

		gm.StartGame();
		_startWaiting = true;
		_startFrom = before.ToString();
		_startDeadline = Time.Now + StartTimeoutSeconds;
		EmitEventHelper( "shortcut", $"start from={_startFrom} pending" );
		BlockScriptHelper( "shortcut start" );
	}

	// Called every frame: complete a blocking `shortcut start`, or time it out with the stuck state.
	partial void ProjectFrameHelper()
	{
		if ( !_startWaiting ) return;

		var gm = GetGameManagerHelper();
		var state = gm.IsValid() ? gm.GetLocalGameState() : GameManager.LocalGameState.InMainMenu;

		if ( state == GameManager.LocalGameState.PubCrawling )
		{
			EmitEventHelper( "shortcut", $"start from={_startFrom} to={state}" );
			_startWaiting = false;
			_cmdWait = 0f; // generic emits `done shortcut start`
			return;
		}

		if ( Time.Now >= _startDeadline )
		{
			EmitEventHelper( "error", $"shortcut start reason=timeout sticky={state}" );
			_startWaiting = false;
			_pendingDone = null; // suppress the generic done; the error above is the completion
			_cmdWait = 0f;
		}
	}

	private bool RunBeersShortcutHelper( string[] args )
	{
		if ( args.Length < 1 || !float.TryParse( args[0], NumberStyles.Float, Culture, out var beers ) )
		{
			EmitEventHelper( "error", "shortcut beers reason=bad-args" );
			return true;
		}

		var cc = GetLocalDrunkCCHelper();
		if ( !cc.IsValid() )
		{
			EmitEventHelper( "error", "shortcut beers reason=no-local-player" );
			return true;
		}

		var before = cc.BeerLevel;
		cc.BeerLevel = beers;
		EmitEventHelper( "shortcut", $"beers from={before.ToString( "0.0", Culture )} to={beers.ToString( "0.0", Culture )}" );
		return true;
	}

	private bool RunTeleportShortcutHelper( string[] args )
	{
		if ( args.Length < 3
			|| !float.TryParse( args[0], NumberStyles.Float, Culture, out var x )
			|| !float.TryParse( args[1], NumberStyles.Float, Culture, out var y )
			|| !float.TryParse( args[2], NumberStyles.Float, Culture, out var z ) )
		{
			EmitEventHelper( "error", "shortcut teleport reason=bad-args" );
			return true;
		}

		var player = GetLocalPlayerRootHelper();
		if ( !player.IsValid() )
		{
			EmitEventHelper( "error", "shortcut teleport reason=no-local-player" );
			return true;
		}

		player.WorldPosition = new Vector3( x, y, z );
		player.Transform.ClearInterpolation();
		var body = player.GetComponent<Rigidbody>( true );
		if ( body.IsValid() ) body.Velocity = Vector3.Zero;

		EmitEventHelper( "shortcut", $"teleport to={x.ToString( "0.0", Culture )},{y.ToString( "0.0", Culture )},{z.ToString( "0.0", Culture )}" );
		return true;
	}

	// `time [seconds]`: add time to the local run timer. Seconds optional, default 60 (the Q debug key's +60 s). Must be a finite number; negatives allowed.
	private bool RunTimeShortcutHelper( string[] args )
	{
		var seconds = 60f;
		if ( args.Length >= 1 && ( !float.TryParse( args[0], NumberStyles.Float, Culture, out seconds ) || !float.IsFinite( seconds ) ) )
		{
			EmitEventHelper( "error", "shortcut time reason=bad-args" );
			return true;
		}

		var gm = GetGameManagerHelper();
		var prog = gm.IsValid() ? gm.GetLocalPlayerProgress() : null;
		if ( !prog.IsValid() )
		{
			EmitEventHelper( "error", "shortcut time reason=no-progress" );
			return true;
		}

		var before = prog.Timer;
		prog.AddTime( seconds );
		EmitEventHelper( "shortcut", $"time from={before.ToString( "0.0", Culture )} to={prog.Timer.ToString( "0.0", Culture )}" );
		return true;
	}

	// Always emits the same field names ("-" when unavailable) so the CSV header stays fixed.
	partial void ProjectSampleHelper( List<(string Name, string Value)> fields )
	{
		var gm = GetGameManagerHelper();
		var state = gm.IsValid() ? gm.GetLocalGameState().ToString() : "-";

		var cc = GetLocalDrunkCCHelper();
		var playerState = cc.IsValid() ? cc.CurrentState.ToString() : "-";
		var beers = cc.IsValid() ? cc.BeerLevel.ToString( "0.0", Culture ) : "-";
		var pants = cc.IsValid() ? cc.CurrentPantsState.ToString() : "-";

		var cars = GetLiveCarsHelper();
		var carNames = string.Join( '|', cars.Select( c => c.Name ) );

		var prog = gm.IsValid() ? gm.GetLocalPlayerProgress() : null;
		var run = prog.IsValid() ? prog.RunState.ToString() : "-";
		var timer = prog.IsValid() ? prog.Timer.ToString( "0.0", Culture ) : "-";

		fields.Add( ("state", state) );
		fields.Add( ("player", playerState) );
		fields.Add( ("beers", beers) );
		fields.Add( ("pants", pants) );
		fields.Add( ("cars", cars.Count.ToString( Culture )) );
		fields.Add( ("carnames", carNames.Length > 0 ? carNames : "-") );
		fields.Add( ("run", run) );
		fields.Add( ("timer", timer) );
	}

	// Change-detection events: game state, DrunkCC state, beer level, run state, ghost-car adds/removes.
	partial void ProjectEventsHelper()
	{
		var gm = GetGameManagerHelper();
		var state = gm.IsValid() ? gm.GetLocalGameState().ToString() : "-";

		var cc = GetLocalDrunkCCHelper();
		var playerState = cc.IsValid() ? cc.CurrentState.ToString() : "-";
		var beers = cc.IsValid() ? MathF.Round( cc.BeerLevel, 1 ) : float.NaN;

		var prog = gm.IsValid() ? gm.GetLocalPlayerProgress() : null;
		var run = prog.IsValid() ? prog.RunState.ToString() : "-";

		var cars = GetLiveCarsHelper();

		if ( _firstSample )
		{
			_firstSample = false;
			_lastGameState = state;
			_lastPlayerState = playerState;
			_lastRunState = run;
			_knownCars.UnionWith( cars );
			return;
		}

		if ( state != _lastGameState )
		{
			EmitEventHelper( "state", $"from={_lastGameState} to={state}" );
			_lastGameState = state;
		}
		if ( playerState != _lastPlayerState )
		{
			EmitEventHelper( "player", $"from={_lastPlayerState} to={playerState}" );
			_lastPlayerState = playerState;
		}

		if ( !float.IsNaN( beers ) )
		{
			if ( float.IsNaN( _lastBeers ) ) _lastBeers = beers; // first real reading: seed silently
			else if ( MathF.Abs( beers - _lastBeers ) >= 0.05f )
			{
				EmitEventHelper( "beers", $"from={_lastBeers.ToString( "0.0", Culture )} to={beers.ToString( "0.0", Culture )}" );
				_lastBeers = beers;
			}
		}

		if ( run != _lastRunState )
		{
			EmitEventHelper( "run", $"from={_lastRunState} to={run}" );
			_lastRunState = run;
		}

		// Cars: car+ for a newly seen car, car- for one that went away.
		foreach ( var car in cars )
		{
			if ( _knownCars.Add( car ) )
			{
				var p = car.WorldPosition;
				EmitEventHelper( "car+", $"name={car.Name} pos={p.x.ToString( "0.0", Culture )},{p.y.ToString( "0.0", Culture )},{p.z.ToString( "0.0", Culture )}" );
			}
		}

		foreach ( var car in _knownCars.ToList() )
		{
			if ( !car.IsValid() || !cars.Contains( car ) )
			{
				EmitEventHelper( "car-", $"name={car.Name}" );
				_knownCars.Remove( car );
			}
		}
	}

	// Local GameManager: the origin object tagged "gamemanager", else the first in the scene.
	private GameManager GetGameManagerHelper()
	{
		var tagged = Scene.FindAllWithTagOrigin( "gamemanager" ).FirstOrDefault();
		if ( tagged.IsValid() )
		{
			var gm = tagged.GetComponent<GameManager>( true );
			if ( gm.IsValid() ) return gm;
		}
		return Scene.GetAllComponents<GameManager>().FirstOrDefault();
	}

	private DrunkCC GetLocalDrunkCCHelper()
	{
		var player = GetLocalPlayerRootHelper();
		if ( !player.IsValid() ) return null;
		return player.GetComponent<DrunkCC>( true );
	}

	// Every live car: objects with a GhostCarBrain, plus AICarDriver objects that have none.
	private List<GameObject> GetLiveCarsHelper()
	{
		var cars = new List<GameObject>();
		if ( Scene is null ) return cars;

		foreach ( var brain in Scene.GetAllComponents<GhostCarBrain>() )
			if ( brain.IsValid() && brain.GameObject.IsValid() ) cars.Add( brain.GameObject );

		foreach ( var ai in Scene.GetAllComponents<AICarDriver>() )
		{
			if ( !ai.IsValid() || !ai.GameObject.IsValid() ) continue;
			if ( ai.GameObject.GetComponent<GhostCarBrain>( true ).IsValid() ) continue;
			if ( !cars.Contains( ai.GameObject ) ) cars.Add( ai.GameObject );
		}
		return cars;
	}
}
