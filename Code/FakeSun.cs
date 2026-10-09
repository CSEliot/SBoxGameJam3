// Project: sboxgamejam3
// File:    FakeSun.cs
// Author:  cseliot
// Created: 2026.10.09.08.30.00
// Edited: 2026.10.09.08.30.00
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Places a tiny fake sun sphere along the real directional sun's direction,
// relative to the local camera, so it reads as the real sun from the viewer's eye.
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

/// <summary>
/// A very small sphere that mimics the real directional sun for one viewer.
///
/// Every player prefab carries one of these, so each client must only see its own copy:
/// proxy copies are hidden (renderers disabled) and never moved, otherwise every remote
/// player would contribute a second false sun to the sky.
///
/// It orbits the local camera rather than the player root. The chase camera sits well
/// behind the player, so a sun pinned to the player would drift off the real sun's screen
/// line; anchoring the offset to the eye keeps the illusion aligned for whoever is looking.
///
/// Work happens in OnPreRender because the chase camera is repositioned in OnUpdate.
/// OnPreRender runs after all updates for the frame, so the offset is measured against the
/// camera's current pose rather than a stale one. All writes are world space, so the player
/// parent's roll or ragdoll animation cannot drag the fake sun around with it.
/// </summary>
public sealed class FakeSun : Component
{
	/// <summary>Optional explicit sun object. When null or invalid, a scene object named
	/// <see cref="SunObjectName"/> is looked up instead.</summary>
	[Property] public GameObject Sun { get; set; }

	/// <summary>Name of the real sun GameObject to fall back to when <see cref="Sun"/> is unset.</summary>
	[Property] public string SunObjectName { get; set; } = "Sun";

	/// <summary>How far from the camera the fake sun is placed, along the real sun's direction.</summary>
	[Property] public float Distance { get; set; } = 100f;

	private GameObject _sunObject;
	private bool _renderersVisible = true;
	private float _sunResolveCooldown;

	protected override void OnPreRender()
	{
		// Only the owning client (or a solo session with no network) shows a fake sun.
		if ( !Network.IsMine() )
		{
			SetRenderersVisibleHelper( false );
			return;
		}

		SetRenderersVisibleHelper( true );

		if ( !_sunObject.IsValid() )
		{
			// Look again at most once per second while the real sun is unresolved.
			if ( _sunResolveCooldown > 0f )
			{
				_sunResolveCooldown -= Time.Delta;
				return;
			}

			_sunObject = ResolveSunHelper();

			if ( !_sunObject.IsValid() )
			{
				_sunResolveCooldown = 1f;
				return;
			}
		}

		var cam = Scene.Camera;
		if ( cam is null )
			return;

		// A directional light shines along its Forward, so the sun itself sits opposite that.
		WorldPosition = cam.WorldPosition + ( -_sunObject.WorldRotation.Forward ) * Distance;
		WorldRotation = _sunObject.WorldRotation;
	}

	/// <summary>
	/// Resolves the real sun: the explicit <see cref="Sun"/> if valid, otherwise the first
	/// scene object named <see cref="SunObjectName"/>. Returns null when none is found.
	/// </summary>
	private GameObject ResolveSunHelper()
	{
		if ( Sun.IsValid() )
			return Sun;

		foreach ( var obj in Scene.GetAllObjects( true ) )
		{
			if ( obj.Name == SunObjectName )
				return obj;
		}

		return null;
	}

	/// <summary>
	/// Toggles every ModelRenderer on this GameObject. Tracked so the component list is only
	/// walked when the visibility actually changes.
	/// </summary>
	private void SetRenderersVisibleHelper( bool visible )
	{
		if ( _renderersVisible == visible )
			return;

		_renderersVisible = visible;

		foreach ( var renderer in GameObject.GetComponents<ModelRenderer>( true ) )
			renderer.Enabled = visible;
	}
}
