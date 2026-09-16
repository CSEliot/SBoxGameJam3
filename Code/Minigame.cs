// Project: sboxgamejam3
// File:    Minigame.cs
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
	
	/// <summary>Fired when the countdown hits zero. Argument is the number of beers drunk this round.</summary>
	public Action<float> OnFinished { get; set; }

	public bool IsActive { get; private set; }

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

	/// <summary>Start a round as soon as the scene plays. Editor testing only.</summary>
	[Property] private bool _StartOnPlay { get; set; } = false;
	[Property] private GameObject _Mug { get; set; }
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
		IsActive = true;
	}

	protected override void OnUpdate()
	{
		if ( !IsActive )
			return;

		if ( Input.Keyboard.Pressed( "space" ) )
		{
			SpawnABeerHelper();

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
			IsActive = false;
			// OnFinished?.Invoke( Beers );
		}
	}

	[Rpc.Broadcast(NetFlags.Unreliable)]
	public void SpawnABeerHelper()
	{
		// _Mug.Clone( _BeerSpawnLocation.WorldPosition, _BeerSpawnLocation.WorldRotation );
	}
}
