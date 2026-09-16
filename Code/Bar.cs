// Project: sboxgamejam3
// File:    Bar.cs
// Author:  cseliot
// Created: 2026.09.14.16.09.57
// Edited: 2026.09.14.16.50.58
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Handles keeping data on who is at this particular bar instance, and other responsiblities.
// 
// License:
// This code is provided "as is," without warranty of any kind, express or
// implied, including but not limited to the warranties of merchantability,
// fitness for a particular purpose, and noninfringement. In no event shall
// the authors or copyright holders be liable for any claim, damages, or
// other liability, whether in an action of contract, tort, or otherwise,
// arising from, out of, or in connection with the software or the use or
// other dealings in the software.

using Sandbox.UI;

namespace Sandbox;

public sealed class Bar : Component
{

	public Bar NextBar;

	/// <summary>
	/// Players currently playing the minigame at this bar, by their client id (or whatever sandbox offers).
	/// </summary>
	private List<int> CurrentPlayers = [];

	[Sync, Property, ReadOnly] private int _CurrentPatronCount { get; set; } = 0;
	
	/// <summary>
	/// Unique ID for this bar instance.
	/// </summary>
	// [Property, ReadOnly] private int? _ID { get; set; } //todo: BARS NO LONGER TRACK THEIR IDs??? -ecs
	[Property, ReadOnly] private string _activeModule { get; set; } = "";
	private GameManager _gameManager = null;
	
	private readonly List<BarModule> _barModules = [];
	
	protected override void OnStart()
	{
		_gameManager = Scene.GetAllComponents<GameManager>().First();
		if ( _gameManager is null )
			Log.Error( "GameManager not found!" );
	
		foreach ( var child in GetComponentsInChildren<BarModule>( true ) )
		{
			
			_barModules.Add( child );
			if ( child.Active )
			{
				if ( _activeModule != "" )
					Log.Error( "Bar has multiple active modules!" );
				_activeModule = child.Name;
			}
		}
	}

	protected override void OnUpdate()
	{

	}

	[Rpc.Broadcast]
	public void SitDownPlayer( Connection playerConnection )
	{
		_CurrentPatronCount++;
		_Dri
	}
	
	[Rpc.Broadcast]
	public void ExitPlayer( Connection playerConnection )
	{
		
	}

}

