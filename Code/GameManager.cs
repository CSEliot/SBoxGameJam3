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

namespace Sandbox;

public sealed class GameManager : Component, Component.INetworkListener
{
	
	[Property] private GameObject _PlayerPrefab { get; set; }
	[Property] private GameObject _SpawnLocation { get; set; }
	private GameObject _LocalPlayer { get; set; }
	private Rigidbody _LocalPlayerRigidbody { get; set; }
	
	protected override void OnStart()
	{
		if ( SpawnPlayerHelper() )
		{
			if( _LocalPlayer == null )
			{
				Log.Error( "LocalPlayer is null, despite successful spawn!" );
			}
		}
		else
		{
			Log.Error( "Failed to spawn player" );
		}
	}

	protected override void OnUpdate()
	{
		if ( Input.Keyboard.Down( "R" ) )
		{
			 ResetPlayerHelper();
		}
	}

	/// <summary>
	/// Resets all physics incoming and outgoing relative to Player and also 
	/// </summary>
	private void ResetPlayerHelper()
	{
		if ( _LocalPlayerRigidbody == null )
		{
			Log.Error( "LocalPlayer Rigidbody is null in ResetPlayerHelper(), called too early or ref lost!" );
			return;
		}
		_LocalPlayer.WorldPosition = _SpawnLocation.WorldPosition;
		_LocalPlayer.WorldRotation = _SpawnLocation.WorldRotation;
		_LocalPlayerRigidbody.Velocity = Vector3.Zero;
		_LocalPlayerRigidbody.AngularVelocity = Vector3.Zero;
		_LocalPlayerRigidbody.Sleeping = true;
		_LocalPlayerRigidbody.Sleeping = false;
		_LocalPlayerRigidbody.Reset();
	}

	/// <summary>
	/// 
	/// </summary>
	/// <returns>True if success</returns>
	private bool SpawnPlayerHelper()
	{
		_LocalPlayer = _PlayerPrefab.Clone();
		_LocalPlayerRigidbody = _LocalPlayer.GetComponent<Rigidbody>();
		if(_LocalPlayerRigidbody == null) {
			Log.Error( "LocalPlayer Rigidbody is null, despite successful spawn!" );
		}
		ResetPlayerHelper();
		return _LocalPlayer != null;
	}

	void INetworkListener.OnActive( Connection connection )
	{
		_LocalPlayer.NetworkSpawn( connection );
	}
}

