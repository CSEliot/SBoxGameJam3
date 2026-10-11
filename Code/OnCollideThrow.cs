// Project: sboxgamejam3
// File:    OnCollideThrow.cs
// Author:  cseliot
// Created: 2026.10.10.05.10.35
// Edited: 2026.10.10.05.55.35
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Portable class for doing a physics force when 2 triggers collide.
// 
// License:
// This code is provided "as is," without warranty of any kind, express or
// implied, including but not limited to the warranties of merchantability,
// fitness for a particular purpose, and noninfringement. In no event shall
// the authors or copyright holders be liable for any claim, damages, or
// other liability, whether in an action of contract, tort, or otherwise,
// arising from, out of, or in connection with the software or the use or
// other dealings in the software.

namespace Sandbox;

/// <summary>
/// Specifically or throwing the player on car collide
/// </summary>
public sealed class OnCollideThrow : Component, Component.ICollisionListener
{
	
	[Property] private float _ThrowForce { get; set; } = 2000;

	private GameManager _gameManager;

	public void OnCollisionStart ( Collision collision)
	{
		var otherCollision = collision.Other;
		var selfCollision = collision.Self;
		var other =  otherCollision.Collider;
		var self = selfCollision.Collider;
		
		bool isOtherAPlayer = other.Tags.Contains( "player" );
		bool isPlayerAProxy = other.IsProxy;
		var drunkCC = other.GetComponentInChildren<DrunkCC>();
		if ( isOtherAPlayer && isPlayerAProxy == false  && drunkCC != null )
		{
			var throwVector3 = self.WorldPosition - other.WorldPosition;
			other.GetComponentInChildren<Rigidbody>().ApplyForce( _ThrowForce * throwVector3 );	
			drunkCC.Ragdollify(false, _ThrowForce * throwVector3);
			Invoke(_gameManager.ResetPlayerOnCopCatchDelay, DoResetHelper  );
		}
	}

	private void DoResetHelper()
	{
		if ( _gameManager != null )
		{
			_gameManager.ResetPlayer();
		}
	}

	protected override void OnUpdate()
	{
		if(_gameManager == null)
			_gameManager = Scene.FindAllWithTag( "gamemanager" ).FirstOrDefault()?.GetComponentInChildren<GameManager>();
	}
}

