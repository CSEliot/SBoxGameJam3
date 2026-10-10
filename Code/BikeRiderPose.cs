// Project: sboxgamejam3
// File:    BikeRiderPose.cs
// Author:  cseliot
// Created: 2026.10.10
// Edited:  2026.10.10
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Visual-only rider pose. Puts the Citizen anim graph into its seated pose and lets the body's
// CitizenAnimationHelper drive the hand/foot two-bone IK from the bike's IK target nodes. IK in
// the anim graph is driven purely by parameters (ik.<name>.enabled/position/rotation), so this
// only works with SkinnedModelRenderer.UseAnimGraph true; with the graph off those writes are
// never read. No colliders, no ragdoll change, no networking change.
// 
// License:
// This code is provided "as is," without warranty of any kind, express or
// implied, including but not limited to the warranties of merchantability,
// fitness for a particular purpose, and noninfringement. In no event shall
// the authors or copyright holders be liable for any claim, damages, or
// other liability, whether in an action of contract, tort, or otherwise,
// arising from, out of, or in connection with the software or the use or
// other dealings in the software.

using System.Linq;
using Sandbox.Citizen;

namespace Sandbox;

/// <summary>
/// Seats the rider on the bike using the Citizen anim graph's seated pose plus the bike's IK
/// target nodes. Attach to the body node (the same GameObject as the SkinnedModelRenderer and
/// CitizenAnimationHelper). The bike's IK nodes are siblings of the body under the player root,
/// found by name; any inspector-wired reference wins over the lookup. Runs ungated on every
/// client so remote riders sit too. Purely cosmetic: the bike stays where it is and knockdown
/// keeps its own ragdoll path. If a target is still unresolved two seconds after the first
/// update it is logged once, so a mis-wired or misnamed target cannot fail silently.
/// </summary>
public sealed class BikeRiderPose : Component
{
	/// <summary> Body renderer whose graph is posed. Defaults to the sibling SkinnedModelRenderer. </summary>
	[Property] public SkinnedModelRenderer Renderer { get; set; }

	/// <summary> Helper that turns the Ik* refs into graph IK parameters each frame. Defaults to the sibling helper. </summary>
	[Property] public CitizenAnimationHelper Helper { get; set; }

	/// <summary> Bike node holding the IK target children. Defaults to the "bike" sibling under the player root. </summary>
	[Property] public GameObject Bike { get; set; }

	/// <summary> World target for the left hand. Defaults to the bike's "lefthand" child. </summary>
	[Property] public GameObject LeftHand { get; set; }

	/// <summary> World target for the right hand. Defaults to the bike's "righthand" child. </summary>
	[Property] public GameObject RightHand { get; set; }

	/// <summary> World target for the left foot. Defaults to the bike's "leftfoot" child. </summary>
	[Property] public GameObject LeftFoot { get; set; }

	/// <summary> World target for the right foot. Defaults to the bike's "rightfoot" child. </summary>
	[Property] public GameObject RightFoot { get; set; }

	/// <summary> Seated pose written to the graph's "sit" enum. Chair1 is the plain sitting pose. </summary>
	[Property] public CitizenAnimationHelper.SittingStyle SitPose { get; set; } = CitizenAnimationHelper.SittingStyle.Chair1;

	/// <summary> True once every reference resolved and the pose was applied. </summary>
	private bool _applied;

	/// <summary> Seconds since the first update, used to delay the missing-target warning. </summary>
	private float _resolveTime;

	/// <summary> True once the missing-target warning has been written, so it is logged at most once. </summary>
	private bool _warnedMissing;

	protected override void OnUpdate()
	{
		if ( !_applied )
		{
			if ( !ResolveHelper() )
			{
				// Give the freshly cloned player a moment to come up before calling a target
				// genuinely missing; then say exactly which one, once.
				_resolveTime += Time.Delta;
				if ( !_warnedMissing && _resolveTime >= 2f )
				{
					_warnedMissing = true;
					Log.Warning( $"BikeRiderPose: no {FirstMissingTargetHelper()}; rider is not posed" );
				}

				return;
			}

			_applied = true;
		}

		// DrunkCC writes Sequence.PlaybackRate whenever it changes its locomotion clip, and that
		// accessor forwards UNCONDITIONALLY to SkinnedModelRenderer.PlaybackRate, which the engine
		// also applies to the anim graph ("Control playback rate of animgraph or current
		// sequence"). With the graph on that would speed the seated pose up to the walk/run clip
		// rate. Pin it to 1; the setter no-ops while unchanged, so DrunkCC's rare writes just get
		// corrected on the next frame.
		Renderer.PlaybackRate = 1f;
	}

	protected override void OnDisabled()
	{
		// CitizenAnimationHelper re-applies its sit and IK state every frame from its own
		// OnUpdate, so a disabled pose would stay seated unless the shared helper is cleared
		// here; undo what ResolveHelper latched onto it.
		if ( _applied && Helper.IsValid() )
		{
			Helper.Sitting = CitizenAnimationHelper.SittingStyle.None;
			Helper.IkLeftHand = null;
			Helper.IkRightHand = null;
			Helper.IkLeftFoot = null;
			Helper.IkRightFoot = null;
		}

		// This component's inspector wiring is left intact so OnUpdate re-applies it on re-enable.
		_applied = false;
	}

	/// <summary>
	/// Fills any unset reference (editor-wired values win) and applies the seated pose. Returns
	/// false while something is still missing, so it can be retried next frame (e.g. before the
	/// freshly cloned player is active).
	/// </summary>
	private bool ResolveHelper()
	{
		Renderer ??= GetComponent<SkinnedModelRenderer>( true );
		Helper ??= GetComponent<CitizenAnimationHelper>( true );
		if ( !Renderer.IsValid() || !Helper.IsValid() )
			return false;

		// The body and the bike share the player root, so search the bike from the parent.
		Bike ??= FindChildHelper( GameObject.Parent, "bike" );
		if ( !Bike.IsValid() )
			return false;

		LeftHand ??= FindChildHelper( Bike, "lefthand" );
		RightHand ??= FindChildHelper( Bike, "righthand" );
		LeftFoot ??= FindChildHelper( Bike, "leftfoot" );
		RightFoot ??= FindChildHelper( Bike, "rightfoot" );
		if ( !LeftHand.IsValid() || !RightHand.IsValid() || !LeftFoot.IsValid() || !RightFoot.IsValid() )
			return false;

		// IK is animgraph-only; make sure the graph is on (the prefab sets this too).
		Renderer.UseAnimGraph = true;

		// CitizenAnimationHelper.OnUpdate turns these into SetIk( "hand_left"/"hand_right"/
		// "foot_left"/"foot_right" ) every frame; the graph's two-bone IK nodes consume them.
		Helper.IkLeftHand = LeftHand;
		Helper.IkRightHand = RightHand;
		Helper.IkLeftFoot = LeftFoot;
		Helper.IkRightFoot = RightFoot;

		// Seated pose: graph enum "sit" (Chair1 maps to sitting_01).
		Helper.Sitting = SitPose;
		return true;
	}

	/// <summary>
	/// Names the first unresolved target after a failed resolve, so the one-time warning points
	/// at the exact field the inspector still needs.
	/// </summary>
	private string FirstMissingTargetHelper()
	{
		if ( !Renderer.IsValid() )
			return "Renderer target";

		if ( !Helper.IsValid() )
			return "Helper target";

		if ( !Bike.IsValid() )
			return "Bike target (expected a sibling named bike)";

		if ( !LeftHand.IsValid() )
			return "LeftHand target (expected a bike child named lefthand)";

		if ( !RightHand.IsValid() )
			return "RightHand target (expected a bike child named righthand)";

		if ( !LeftFoot.IsValid() )
			return "LeftFoot target (expected a bike child named leftfoot)";

		return "RightFoot target (expected a bike child named rightfoot)";
	}

	/// <summary> Returns the direct child of <paramref name="parent"/> named <paramref name="name"/>, or null. </summary>
	private static GameObject FindChildHelper( GameObject parent, string name )
	{
		if ( !parent.IsValid() )
			return null;

		return parent.Children.FirstOrDefault( x => x.Name == name );
	}
}
