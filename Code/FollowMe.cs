// Project: sboxgamejam3
// File:    FollowMe.cs
// Author:  cseliot
// Created: 2026.10.13.21.10.51
// Edited: 2026.10.13.21.55.53
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

public sealed class FollowMe : Component
{
	
	[Property] protected GameObject FollowThis { get; set; }

	[Property] protected bool WithoutParent { get; set; } = true;
	
	[Property] protected bool FollowX { get; set; } = true;
	[Property] protected bool FollowY { get; set; } = true;
	[Property] protected bool FollowZ { get; set; } = true;

	[Property] protected bool FollowRoll { get; set; } = true;
	[Property] protected bool FollowPitch { get; set; } = true;
	[Property] protected bool FollowYaw { get; set; } = true;
	
	[Property] protected float OffsetX { get; set; }
	[Property] protected float OffsetY { get; set; }
	[Property] protected float OffsetZ { get; set; }
	
	[Property, MinMax(0, 1)] protected float FollowTightness { get; set; }
	[Property, MinMax( 0, 1 )] protected float FollowRotationTightness { get; set; } = 1;
	
	protected override void OnStart()
	{
		if(WithoutParent)
			this.GameObject.SetParent(null);
	}

	/// <summary>
	/// todo: update the sbox engine as I've found it doesn't follow DRY, and the Angles() doesn't use Roll(), Pitch(), Yaw() when it should.
	/// </summary>
	protected override void OnUpdate()
	{
		if(FollowX)
			WorldPosition = WorldPosition.WithX( 
				MathX.Lerp(WorldPosition.x, FollowThis.WorldPosition.x + OffsetX,  FollowTightness)
				);
		if(FollowY)
			WorldPosition = WorldPosition.WithY( 
				MathX.Lerp(WorldPosition.y, FollowThis.WorldPosition.y + OffsetY,  FollowTightness)
				);
		if(FollowZ)
			WorldPosition = WorldPosition.WithZ( 
				MathX.Lerp(WorldPosition.z, FollowThis.WorldPosition.z + OffsetZ,  FollowTightness)
				);
		
		if(FollowRoll)
			WorldRotation = WorldRotation.Angles().WithRoll( 
				MathX.Lerp(WorldRotation.Roll(), FollowThis.WorldRotation.Roll(),  FollowRotationTightness)
			);
		if(FollowPitch)
			WorldRotation = WorldRotation.Angles().WithPitch( 
				MathX.Lerp(WorldRotation.Pitch(), FollowThis.WorldRotation.Pitch(),  FollowRotationTightness)
			);
		if(FollowYaw)
			WorldRotation = WorldRotation.Angles().WithYaw( 
				MathX.Lerp(WorldRotation.Yaw(), FollowThis.WorldRotation.Yaw(),  FollowRotationTightness)
			);
	}
}

