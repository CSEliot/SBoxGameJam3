// Project: sboxgamejam3
// File:    AICarDriver.Tuning.cs
// Author:  cseliot
// Created: 2026.10.05
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 

namespace Sandbox;

/// <summary>
/// Code-side tuning for the AI car feedback loop. Applied over the inspector / scene values at
/// start while <see cref="UseDevTuning"/> is on, so tuning changes arrive through a code pull
/// (hotload) without touching the scene or prefab files you may have open. Once the car drives
/// well, bake these values into ai_car.prefab and turn UseDevTuning off.
/// </summary>
public sealed partial class AICarDriver
{
	private void ApplyDevTuningHelper()
	{
		if ( !UseDevTuning ) return;

		// v1-baseline: the scene's own values, plus test cars for more data per play session.
		ExtraTestCars = 2;
	}
}
