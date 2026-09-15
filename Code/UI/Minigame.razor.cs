// Project: sboxgamejam3
// File:    Minigame.razor.cs
// Author:  cseliot
// Created: 2026.09.14
//
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
//
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
//
// Description:
// Bar mini-game overlay: RENDERING-ONLY half of the Minigame partial class.
// All game state and simulation logic live in ../Minigame.cs. This file
// only holds display-formatting values consumed by Minigame.razor markup
// (percent text, timer text, title punch scale) and the render-tree hash.

using System;
using Sandbox;
using Sandbox.UI;

namespace Sandbox.UI;

public partial class Minigame : PanelComponent
{
	// RENDERING-ONLY: formatting/display values consumed by the .razor markup.
	// Game state (Progress, Beers, _punch, _endsAt) and all simulation logic
	// live in ../Minigame.cs.

	int Percent => (int)MathF.Round( Progress * 100f );
	int SecondsLeft => Math.Max( 0, (int)MathF.Ceiling( _endsAt.Relative ) );
	float TitleScale => 1f + 0.15f * _punch;

	// Rebuild every frame while a round is running so the bar, percentage, timer and
	// title punch track their values; idle panel hashes to a constant and stays put.
	protected override int BuildHash() => IsActive ? HashCode.Combine( IsActive, RealTime.Now ) : HashCode.Combine( IsActive );
}
