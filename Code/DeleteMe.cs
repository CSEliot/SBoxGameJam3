// Project: sboxgamejam3
// File:    DeleteMe.cs
// Author:  cseliot
// Created: 2026.09.21.02.09.48
// Edited: 2026.09.21.02.22.49
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

namespace Sandbox;

public sealed class DeleteMe : Component
{
	
	[Property] private float _TimeToDelete { get; set; } = 5;

	private float _spawnTime;
	
	protected override void OnStart()
	{
		_spawnTime = Time.Now;
	}

	protected override void OnUpdate()
	{
		if ( Time.Now - _spawnTime > _TimeToDelete )
		{
			DestroyGameObject();
			SkinnedModelRenderer s = GetComponent<SkinnedModelRenderer>();
			// s.Sequence = 
		}
	}
}

