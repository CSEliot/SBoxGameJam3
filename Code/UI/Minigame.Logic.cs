// Project: sboxgamejam3
// File:    Minigame.Logic.cs
// Author:  cseliot
// Created: 2026.09.15.22.09.46
//
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
//
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
//
// Description:
// Bar mini-game (Design.md, Screen 3): GAME LOGIC half of the Minigame
// partial class. Spacebar mashing fills a progress bar that decays over
// time. Each time the bar hits 100% it resets and one beer is added. The
// round runs for a fixed duration; OnFinished reports the beers drunk.
// Rendering/display-only members live in UI/Minigame.razor.cs. Both files
// must stay in namespace Sandbox.UI so they merge into the single
// PanelComponent-derived type wired on the UI GameObject in the scene.
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

public partial class Minigame
{
	// GAME LOGIC: state, tunables and simulation for the mini-game. Base type
	// (PanelComponent) and rendering-only members are declared in the .razor
	// and .razor.cs parts of this partial class.
	
	public bool IsPlaying { get; private set; }

	/// <summary>Current bar fill, 0..1. Resets to 0 each time it reaches 1.</summary>
	public float Progress { get; private set; }

	/// <summary>Beers finished this round (one per full bar).</summary>
	public float Beers { get; private set; }

	/// <summary>
	/// Set when a bar is told to sit down a player.
	/// </summary>
	public Vector3 BeerSpawnLocation;
	
	/// <summary>Round length in seconds.</summary>
	[Property] private float _Duration { get; set; } = 10f;

	/// <summary>Fill added per spacebar press (0..1 scale).</summary>
	[Property] private float _FillPerPress { get; set; } = 0.06f;

	/// <summary>Fill lost per second while the round is running.</summary>
	[Property] private float _DecayPerSecond { get; set; } = 0.25f;

	/// <summary>Pitch multiplier for the per-press drink sfx at 0% bar fill.</summary>
	[Property] private float _DrinkPitchMin { get; set; } = 1f;

	/// <summary>Pitch multiplier at 100% bar fill - the drink sfx pitch rises linearly with the fill.</summary>
	[Property] private float _DrinkPitchMax { get; set; } = 2f;

	/// <summary>Start a round as soon as the scene plays. Editor testing only.</summary>
	[Property] private bool _StartOnPlay { get; set; } = false;
	[Property] private GameObject _MugSmall { get; set; }
	[Property] private GameObject _MugLarge { get; set; }
	[Property] private GameManager _GameManager { get; set; }


	TimeUntil _endsAt;
	float _punch;

	protected override void OnStart()
	{
		if ( _StartOnPlay )
			Begin();
	}

	/// <summary>Start a round using the configured duration.</summary>
	public void Begin() => Begin( _Duration );

	/// <summary>Start a round of the given length in seconds.</summary>
	public void Begin( float durationSeconds )
	{
		Progress = 0f;
		Beers = 0;
		_punch = 0f;
		_endsAt = durationSeconds;
		IsPlaying = true;
	}

	protected override void OnUpdate()
	{
		if ( !IsPlaying )
			return;

		if(Input.Keyboard.Pressed("Z"))
			_endsAt += 10f;
		
		if ( Input.Keyboard.Pressed( "space" ) )
		{
			// The fill the bar reaches with this press: it drives the drink sfx
			// pitch (rises with the minigame %) and, when it hits 100%, the LARGE
			// mug spawn - its own SoundPointComponent has PlayOnStart=true, so
			// drinkfull plays from the beer itself.
			float fillAfterPress = Math.Min( 1f, Progress + _FillPerPress );
			bool completedBeer = fillAfterPress >= 1f;
			SpawnABeerHelper( BeerSpawnLocation, fillAfterPress, completedBeer );

			Progress += _FillPerPress;
			Beers += _FillPerPress;
			_punch = 1f;

			if ( Progress >= 1f )
			{
				Progress -= 1f;
			}
		}

		Progress = Math.Max( 0f, Progress - _DecayPerSecond * Time.Delta );
		_punch = Math.Max( 0f, _punch - 8f * Time.Delta );

		if ( _endsAt )
		{
			IsPlaying = false;
		}
	}

	// The spawn position must come in as an RPC argument: BeerSpawnLocation is a
	// viewer-local, non-synced field (only set for the local seated player in
	// Bar.ApplySitDownPlayer), so an argument-less broadcast made every client
	// clone the mug at its OWN BeerSpawnLocation - remote players' beers showed
	// up at the wrong drinker seat. Passing it replicates the presser's
	// authoritative position to everyone. fillPercent and large ride the same
	// argument list: this runs on every client, so per-play values must arrive
	// as arguments rather than being read from viewer-local Progress.
	[Rpc.Broadcast(NetFlags.Unreliable)]
	public void SpawnABeerHelper( Vector3 spawnLocation, float fillPercent, bool large )
	{
		var prefab = large ? _MugLarge : _MugSmall;
		if ( prefab is null )
			return;

		var mug = prefab.Clone( spawnLocation );

		// The large mug plays its drinkfull sfx itself (PlayOnStart=true on its
		// SoundPointComponent). The small mug ships PlayOnStart=false: its drink-once
		// sfx is started manually here, AFTER the pitch is set, so the pitch rises
		// with the minigame fill percentage. fillPercent arrives as an RPC argument
		// because Progress is viewer-local state.
		if ( large )
			return;

		var soundPoint = mug?.GetComponent<SoundPointComponent>();
		if ( soundPoint is null )
			return;

		soundPoint.Pitch = _DrinkPitchMin + ( _DrinkPitchMax - _DrinkPitchMin ) * fillPercent.Clamp( 0f, 1f );
		soundPoint.StartSound();
	}
}
