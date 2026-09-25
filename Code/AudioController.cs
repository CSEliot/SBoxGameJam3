// Project: sboxgamejam3
// File:    AudioController.cs
// Author:  cseliot
// Created: 2026.09.24.00.09.26
// Edited: 2026.09.24.00.50.27
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Handles music and menu sfx.
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

public sealed class AudioController : Component
{
	[Property] private SoundFile _musicIntro { get; set; }
	[Property] private SoundFile _musicLoop { get; set; }
	[Property] private SoundFile _musicOutro { get; set; }
	[Property] private SoundFile _musicPub { get; set; }
	[Property] private SoundFile _musicMainMenu { get; set; }
	[Property] private SoundEvent _sfxDrinkOnce { get; set; }
	[Property] private SoundEvent _sfxDrinkFull { get; set; }
	[Property] private SoundEvent _sfxUIInteract { get; set; }
	[Property] private SoundEvent _sfxPressPlay { get; set; }

	/// <summary>
	/// Global instance so UI code can play menu sfx without a scene reference.
	/// </summary>
	public static AudioController Instance { get; private set; }

	public MusicState State { get; set; } = MusicState.None;
	
	public MusicComponent MusicComponent { get; private set; }
	public bool IsPlaying { get; private set; }
	
	public enum MusicState
	{
		None,
		Intro,
		Loop,
		Outro,
		PubNoise,
		MainMenu,
	}

	private MusicState _StateLastFrame { get; set; } = MusicState.None;

	protected override void OnStart()
	{
		Instance = this;
		MusicComponent = GetComponent<MusicComponent>();
		IsPlaying = false;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this )
			Instance = null;
	}

	/// <summary>
	/// Plays the generic UI interact sound from menu/screen code.
	/// </summary>
	public void PlayUIInteract()
	{
		PlayUiHelper( _sfxUIInteract );
	}

	/// <summary>
	/// Plays the press-play sound from menu/screen code.
	/// </summary>
	public void PlayPressPlay()
	{
		PlayUiHelper( _sfxPressPlay );
	}

	private static void PlayUiHelper( SoundEvent soundEvent )
	{
		if ( soundEvent is null )
			return;

		var handle = Sound.Play( soundEvent );
		if ( !handle.IsValid() )
		{
			// Usually means the .sound asset carries no usable sample (empty Sounds
			// list) - surface it instead of failing silently.
			Log.Warning( $"AudioController: UI sfx '{soundEvent.ResourceName}' failed to play - check the .sound asset's Sounds list." );
			return;
		}

		// The .sound assets ship with UI=false, so mirror what the engine's own
		// UI-flag branch of Sound.Play applies: pin the sound to the local listener
		// (no world position/distance attenuation), disable the spatial effects
		// and route it to the UI mixer.
		if ( !soundEvent.UI )
		{
			handle.ListenLocal = true;
			handle.DistanceAttenuation = false;
			handle.AirAbsorption = false;
			handle.OcclusionEnabled = false;
			handle.ReverbEnabled = false;
			handle.TargetMixer ??= Sandbox.Audio.Mixer.FindMixerByName( "UI" );
		}
	}

	protected override void OnUpdate()
	{
		if ( State != _StateLastFrame )
		{
			switch (State)
			{
				case MusicState.None:
					MusicComponent.Stop();
					break;
				case MusicState.Intro:
					MusicComponent.Track = _musicIntro;
					break;
				case MusicState.Loop:
					MusicComponent.Track = _musicLoop;
					break;
				case MusicState.Outro:
					MusicComponent.Track = _musicOutro;
					break;
				case MusicState.PubNoise:
					MusicComponent.Track = _musicPub;
					break;
				case MusicState.MainMenu:
					MusicComponent.Track = _musicMainMenu;
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
			if(IsPlaying == false)
				MusicComponent.Play();
			_StateLastFrame = State;
		}
		
	}
}

