// Project: sboxgamejam3
// File:    BarModules.cs
// Author:  cseliot
// Created: 2026.09.14.21.09.41
// Edited: 2026.09.14.21.56.41
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Data type mostly for organizing.
// 
// License:
// This code is provided "as is," without warranty of any kind, express or
// implied, including but not limited to the warranties of merchantability,
// fitness for a particular purpose, and noninfringement. In no event shall
// the authors or copyright holders be liable for any claim, damages, or
// other liability, whether in an action of contract, tort, or otherwise,
// arising from, out of, or in connection with the software or the use or
// other dealings in the software.

namespace Sandbox.UI;

public sealed class BarModule : Component
{
	[Property] public string Name { get; private set; }
}

