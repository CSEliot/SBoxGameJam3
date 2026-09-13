// Project: sboxgamejam3
// File:    DeleteIfProxy.cs
// Author:  cseliot
// Created: 2026.09.13.21.09.13
// Edited: 2026.09.13.21.44.13
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Helper script to delete gameobject that is not ours and/or one that's orphaned but sim'd by host.
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

public sealed class DeleteIfProxy : Component
{

	[Property] private GameObject Target { get; set; }

	protected override void OnStart()
	{
		if ( Target.IsValid() == false)
		{
			Target = this.GameObject;
		}
		if(Target.Network.IsProxy)
			DestroyGameObject();
	}

}

