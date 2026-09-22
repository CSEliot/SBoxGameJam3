// Project: sboxgamejam3
// File:    SpinMe.cs
// Author:  cseliot
// Created: 2026.09.22.04.09.40
// Edited: 2026.09.22.04.50.40
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

public sealed class SpinMe : Component
{
	[Property] private Vector3 _SpinValues { get; set; }

	protected override void OnStart()
	{
		
	}

	protected override void OnUpdate()
	{
		LocalRotation = LocalRotation.Angles()
			.WithPitch( LocalRotation.Angles().pitch + _SpinValues.y )
			.WithRoll(  LocalRotation.Angles().roll + _SpinValues.x )
			.WithYaw(   LocalRotation.Angles().yaw + _SpinValues.z );

	}
}

