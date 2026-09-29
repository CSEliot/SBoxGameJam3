// Project: sboxgamejam3
// File:    CollisionReporter.cs
// Author:  cseliot
// Created: 2026.09.21.05.09.07
// Edited: 2026.09.21.05.17.08
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// [Provide a brief description of what this file/class does.]
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

public sealed class CollisionReporter : Component, Component.ITriggerListener
{

	public Action<Collider> OnTriggerEnterCallback;
	public Action<Collider> OnTriggerStayCallback;
	public Action<Collider> OnTriggerExitCallback;
	
	[Property] private TagSet _TagsWhiteList { get; set; }
	[Property] private float _OnStayExpiration { get; set; }
	
	public List<Collider> TriggeredColliders { get; private set; } = new();
	public List<float> TriggeredCollidersLifetime { get; private set; } = new();

	public void OnTriggerExit( Collider other )
	{
		OnTriggerExitCallback?.Invoke(other);
		int indexOf = TriggeredColliders.IndexOf(other);
		if ( indexOf >= 0 )
		{
			TriggeredColliders.RemoveAt( indexOf );
			TriggeredCollidersLifetime.RemoveAt( indexOf );
		}
	}
	
	public void OnTriggerEnter( Collider other )
	{
		TriggeredColliders.Add(other);
		TriggeredCollidersLifetime.Add(0);
		OnTriggerEnterCallback?.Invoke(other);
	}

	protected override void OnFixedUpdate()
	{
		OnTriggerStay();
		for ( int x = 0; x < TriggeredColliders.Count; x++ )
		{
			TriggeredCollidersLifetime[x] += Time.Delta;
			if ( TriggeredCollidersLifetime[x] > _OnStayExpiration )
			{
				TriggeredCollidersLifetime.RemoveAt( x );
				TriggeredColliders.RemoveAt( x );
				x--;
			}
		}
	}
	public void OnTriggerStay( )
	{
		foreach ( var collider in TriggeredColliders )
		{
			if ( _TagsWhiteList.GetTokens().Count == 0 )
			{
				OnTriggerStayCallback?.Invoke(collider);
			}
			else
			{
				foreach ( string tag in collider.GameObject.Tags )
				{
					if(_TagsWhiteList.Contains(tag))
						OnTriggerStayCallback?.Invoke(collider);
				}
			}
		}
	}
	
}

