// Project: sboxgamejam3
// File:    LeaderboardEntry.cs
// Author:  cseliot
// Created: 2026.09.19
//
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
//
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
//
// Description:
// One entry in GameManager's host-authoritative leaderboard (Architecture.md
// "Score & Timer Architecture - v0 plan", section 6). Kept as a small struct
// per that plan so the whole [Sync(SyncFlags.FromHost)] array re-syncs
// cheaply.
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

public struct LeaderboardEntry
{
	public string Name { get; set; }
	public int Score { get; set; }
	public float Beers { get; set; }
	public int BarsVisited { get; set; }
}
