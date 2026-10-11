// Project: sboxgamejam3
// File:    CopCarLights.cs
// Author:  cseliot
// Created: 2026.10.11.09.00.00
// Edited:  2026.10.11.09.00.00
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Runtime built cop car light rig for a ghost car: a spinning roof light bar
// with two back-to-back beacons tinted with the car's ghost colour, plus two
// forward headlamps. Purely visual, built on every client, toggled with the
// networked LightsOn flag. Owns the roof and headlamp placement for the
// polygon police body.
// 
// License:
// This code is provided "as is," without warranty of any kind, express or
// implied, including but not limited to the warranties of merchantability,
// fitness for a particular purpose, and noninfringement. In no event shall
// the authors or copyright holders be liable for any claim, damages, or
// other liability, whether in an action of contract, tort, or otherwise,
// arising from, out of, or in connection with the software or the use or
// other dealings in the software.

using System;
using System.Collections.Generic;

namespace Sandbox;

/// <summary>
/// The fake police light rig for one ghost car: a roof light bar with two back-to-back
/// beacons that sweep as the bar spins, and two forward headlamps.
///
/// Everything is built at runtime in <see cref="OnStart"/> on every client (it is purely
/// visual, so there is no host-only gate), out of children marked
/// <see cref="NetworkMode.Never"/> and <see cref="GameObjectFlags.NotSaved"/> so the rig
/// is never networked or written back to disk. Each light is a real
/// <see cref="SpotLight"/> with a <see cref="FakeLightBeam"/> on the same object, so it works
/// as a light source and as a visible volumetric beam (the beam component renders its own
/// cone via a SceneObject). Every spot light has shadows off;
/// there are 16 of them across four cars.
///
/// The beacon colour is the car's ghost tint, read from <see cref="TintSource"/> or
/// auto-resolved from the police body renderer. <see cref="LightsOn"/> toggles the whole
/// rig, written by the host and replicated.
///
/// The default offsets were measured from the police body mesh, so they sit on the
/// polygon police roof bar and on its front headlights.
/// </summary>
[Icon( "light_mode" )]
public sealed class CopCarLights : Component
{
	// ------------------------------------------------------------------ State

	/// <summary>Whether the rig is lit. Written by the host (the ghost brain) and replicated to clients.</summary>
	[Sync( SyncFlags.FromHost )] public bool LightsOn { get; set; } = false;

	// ------------------------------------------------------------------ Placement

	/// <summary>Roof light bar pivot, local to the car root. Measured: the top of the police roof bar.</summary>
	[Property, Group( "Placement" )] public Vector3 LightBarOffset { get; set; } = new Vector3( -8.5f, 0f, 78.5f );

	/// <summary>Degrees per second the light bar spins about the car's local up (Z).</summary>
	[Property, Group( "Placement" )] public float SpinDegreesPerSecond { get; set; } = 540f;

	/// <summary>How far each beacon is pitched below horizontal.</summary>
	[Property, Group( "Placement" )] public float BeaconPitchDegrees { get; set; } = 8f;

	/// <summary>Front left headlamp, local to the car root; the second lamp mirrors Y. Measured: front fascia, headlight height.</summary>
	[Property, Group( "Placement" )] public Vector3 HeadlampOffset { get; set; } = new Vector3( 123f, 29f, 34f );

	/// <summary>How far each headlamp is pitched below horizontal.</summary>
	[Property, Group( "Placement" )] public float HeadlampPitchDegrees { get; set; } = 4f;

	// ------------------------------------------------------------------ Colour

	/// <summary>Optional body renderer whose Tint is the ghost colour. Left null, the police body renderer is auto-resolved.</summary>
	[Property, Group( "Colour" )] public ModelRenderer TintSource { get; set; }

	/// <summary>Beacon colour used when no tint could be resolved (white).</summary>
	[Property, Group( "Colour" )] public Color FallbackBeaconColor { get; set; } = Color.White;

	/// <summary>Scales the resolved ghost tint into the beacon light colour (linear, may exceed one).</summary>
	[Property, Group( "Colour" )] public float BeaconBrightness { get; set; } = 2f;

	/// <summary>Warm white headlamp colour.</summary>
	[Property, Group( "Colour" )] public Color HeadlampColor { get; set; } = new Color( 1f, 0.945f, 0.839f );

	/// <summary>Scales the headlamp light colour (linear, may exceed one).</summary>
	[Property, Group( "Colour" )] public float HeadlampBrightness { get; set; } = 1.5f;

	// ------------------------------------------------------------------ Beacon tunables

	/// <summary>How far each beacon reaches.</summary>
	[Property, Group( "Beacons" )] public float BeaconRadius { get; set; } = 700f;

	/// <summary>Angle from the beacon axis where the light starts to fade.</summary>
	[Property, Group( "Beacons" )] public float BeaconConeInner { get; set; } = 18f;

	/// <summary>Angle from the beacon axis where the light has faded out.</summary>
	[Property, Group( "Beacons" )] public float BeaconConeOuter { get; set; } = 45f;

	/// <summary>Quadratic falloff for the beacons, one is the sensible default.</summary>
	[Property, Group( "Beacons" )] public float BeaconAttenuation { get; set; } = 1f;

	/// <summary>Intensity pushed into the beacon's fake beam cone.</summary>
	[Property, Group( "Beacons" )] public float BeaconBeamIntensity { get; set; } = 0.25f;

	/// <summary>Maximum length of the beacon's fake beam cone.</summary>
	[Property, Group( "Beacons" )] public float BeaconBeamMaxLength { get; set; } = 700f;

	// ------------------------------------------------------------------ Headlamp tunables

	/// <summary>How far each headlamp reaches.</summary>
	[Property, Group( "Headlamps" )] public float HeadlampRadius { get; set; } = 900f;

	/// <summary>Angle from the headlamp axis where the light starts to fade.</summary>
	[Property, Group( "Headlamps" )] public float HeadlampConeInner { get; set; } = 12f;

	/// <summary>Angle from the headlamp axis where the light has faded out.</summary>
	[Property, Group( "Headlamps" )] public float HeadlampConeOuter { get; set; } = 35f;

	/// <summary>Quadratic falloff for the headlamps, one is the sensible default.</summary>
	[Property, Group( "Headlamps" )] public float HeadlampAttenuation { get; set; } = 1f;

	/// <summary>Intensity pushed into the headlamp's fake beam cone.</summary>
	[Property, Group( "Headlamps" )] public float HeadlampBeamIntensity { get; set; } = 0.2f;

	/// <summary>Maximum length of the headlamp's fake beam cone.</summary>
	[Property, Group( "Headlamps" )] public float HeadlampBeamMaxLength { get; set; } = 900f;

	// ------------------------------------------------------------------ Internals

	/// <summary>Root of the runtime rig, a child of this GameObject. Destroyed with the component.</summary>
	private GameObject _rig;

	/// <summary>The spinning light bar pivot, a child of the rig.</summary>
	private GameObject _lightBarPivot;

	/// <summary>Every created light GameObject, so LightsOn can be applied to them.</summary>
	private readonly List<GameObject> _lightObjects = new();

	/// <summary>Current spin angle of the light bar, degrees.</summary>
	private float _spinAngle;

	private bool _appliedOnce;
	private bool _appliedLightsOn;
	private bool _warnedNoTint;

	// ================================================================== Lifecycle

	protected override void OnStart()
	{
		// A code hotload re-runs OnStart on the same GameObjects without destroying the
		// runtime children the previous assembly built; sweep any stale rigs by name first
		// so every rebuild stays at exactly one rig per car.
		DestroyStaleRigsHelper();

		BuildRigHelper();
	}

	protected override void OnDestroy()
	{
		if ( _rig.IsValid() )
			_rig.Destroy();
	}

	protected override void OnUpdate()
	{
		// Spin the bar. Kept on every client because the rig is purely visual.
		if ( _lightBarPivot.IsValid() )
		{
			_spinAngle = (_spinAngle + SpinDegreesPerSecond * Time.Delta) % 360f;
			_lightBarPivot.LocalRotation = Rotation.FromAxis( Vector3.Up, _spinAngle );
		}

		// Apply LightsOn to the created lights only when it changed (and once on the first frame).
		if ( !_appliedOnce || _appliedLightsOn != LightsOn )
		{
			_appliedOnce = true;
			_appliedLightsOn = LightsOn;

			foreach ( var light in _lightObjects )
			{
				if ( light.IsValid() )
					light.Enabled = LightsOn;
			}
		}
	}

	// ================================================================== Rig construction

	/// <summary>
	/// Destroys rig children left over from a previous assembly (a code hotload re-runs
	/// OnStart without destroying the old runtime children). The rig name is unique to this
	/// car so only stale rigs under this GameObject are touched.
	/// </summary>
	private void DestroyStaleRigsHelper()
	{
		for ( int i = GameObject.Children.Count - 1; i >= 0; i-- )
		{
			var child = GameObject.Children[i];
			if ( child.IsValid() && child.Name == $"{GameObject.Name}-coplights" )
				child.Destroy();
		}
	}

	/// <summary>
	/// Builds the whole rig as runtime children. Each object is never networked and never
	/// saved, so it exists only for the current session on every client.
	/// </summary>
	private void BuildRigHelper()
	{
		Color beaconColor = ResolveBeaconColorHelper();

		_rig = MakeRigObjectHelper( GameObject, $"{GameObject.Name}-coplights" );

		// --- Roof light bar and its two back-to-back beacons.
		_lightBarPivot = MakeRigObjectHelper( _rig, "lightbar" );
		_lightBarPivot.LocalPosition = LightBarOffset;
		_lightBarPivot.LocalRotation = Rotation.Identity;

		for ( int i = 0; i < 2; i++ )
		{
			float yaw = i == 0 ? 0f : 180f;
			Rotation rotation = Rotation.FromYaw( yaw ) * Rotation.FromPitch( BeaconPitchDegrees );

			BuildSpotLightHelper( _lightBarPivot, $"beacon{i}", Vector3.Zero, rotation,
				beaconColor, BeaconRadius, BeaconConeInner, BeaconConeOuter, BeaconAttenuation,
				BeaconBeamIntensity, BeaconBeamMaxLength );
		}

		// --- Two headlamps pointing forward, Y mirrored.
		Rotation headlampRotation = Rotation.FromPitch( HeadlampPitchDegrees );
		Color headlampColor = ScaleColorHelper( HeadlampColor, HeadlampBrightness );

		BuildSpotLightHelper( _rig, "headlamp_l",
			new Vector3( HeadlampOffset.x, HeadlampOffset.y, HeadlampOffset.z ), headlampRotation,
			headlampColor, HeadlampRadius, HeadlampConeInner, HeadlampConeOuter, HeadlampAttenuation,
			HeadlampBeamIntensity, HeadlampBeamMaxLength );

		BuildSpotLightHelper( _rig, "headlamp_r",
			new Vector3( HeadlampOffset.x, -HeadlampOffset.y, HeadlampOffset.z ), headlampRotation,
			headlampColor, HeadlampRadius, HeadlampConeInner, HeadlampConeOuter, HeadlampAttenuation,
			HeadlampBeamIntensity, HeadlampBeamMaxLength );
	}

	/// <summary>
	/// Creates one SpotLight GameObject (with a FakeLightBeam on the same object) under
	/// parent, records it so LightsOn can toggle it, and returns it.
	/// </summary>
	private GameObject BuildSpotLightHelper( GameObject parent, string name, Vector3 localPosition, Rotation localRotation,
		Color color, float radius, float coneInner, float coneOuter, float attenuation,
		float beamIntensity, float beamMaxLength )
	{
		var lightObject = MakeRigObjectHelper( parent, name );
		lightObject.LocalPosition = localPosition;
		lightObject.LocalRotation = localRotation;

		var spot = lightObject.Components.Create<SpotLight>();
		spot.LightColor = color;
		spot.Radius = radius;
		spot.ConeInner = coneInner;
		spot.ConeOuter = coneOuter;
		spot.Attenuation = attenuation;
		// Shadows off: sixteen of these across four cars would be far too expensive.
		spot.Shadows = false;

		// The beam component owns its cone SceneObject, so no child GameObject is needed.
		var beam = lightObject.Components.Create<FakeLightBeam>();
		beam.Light = spot;
		beam.MaxLength = beamMaxLength;
		beam.Intensity = beamIntensity;
		// The length-clip trace must never hit the car itself.
		beam.TraceIgnore = GameObject;

		_lightObjects.Add( lightObject );
		return lightObject;
	}

	/// <summary>Creates a runtime-only child object: never networked, never saved.</summary>
	private static GameObject MakeRigObjectHelper( GameObject parent, string name )
	{
		var go = new GameObject( parent, true, name );
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		return go;
	}

	// ================================================================== Colour

	/// <summary>
	/// Beacon colour from the resolved tint source. Prefers TintSource; otherwise finds the
	/// first descendant renderer whose model is the police body. Falls back to
	/// FallbackBeaconColor, warning once.
	/// </summary>
	private Color ResolveBeaconColorHelper()
	{
		ModelRenderer source = TintSource;

		if ( !source.IsValid() )
			source = FindPoliceBodyRendererHelper();

		if ( !source.IsValid() )
		{
			if ( !_warnedNoTint )
			{
				_warnedNoTint = true;
				Log.Warning( $"CopCarLights on '{GameObject.Name}': no tint source found, using FallbackBeaconColor." );
			}

			return ScaleColorHelper( FallbackBeaconColor, BeaconBrightness );
		}

		return ScaleColorHelper( source.Tint.WithAlpha( 1f ), BeaconBrightness );
	}

	/// <summary>The first descendant ModelRenderer showing the police body model, or null.</summary>
	private ModelRenderer FindPoliceBodyRendererHelper()
	{
		foreach ( var renderer in GameObject.GetComponentsInChildren<ModelRenderer>( true ) )
		{
			if ( !renderer.IsValid() || renderer.Model is null )
				continue;

			var path = renderer.Model.ResourcePath;
			if ( !string.IsNullOrEmpty( path ) && path.EndsWith( "sm_veh_car_police_01.vmdl", StringComparison.OrdinalIgnoreCase ) )
				return renderer;
		}

		return null;
	}

	/// <summary>Multiplies an RGB colour by a linear brightness, keeping alpha.</summary>
	private static Color ScaleColorHelper( Color color, float brightness )
	{
		return new Color( color.r * brightness, color.g * brightness, color.b * brightness, color.a );
	}

	// ================================================================== Editor gizmos

	/// <summary>Marks the light bar and headlamp offsets so they can be tuned without playing.</summary>
	protected override void DrawGizmos()
	{
		var origin = WorldPosition;
		var rotation = WorldRotation;

		DrawMarkerHelper( origin + rotation * LightBarOffset, rotation * Vector3.Forward, Color.Yellow );

		var left = rotation * new Vector3( HeadlampOffset.x, HeadlampOffset.y, HeadlampOffset.z );
		var right = rotation * new Vector3( HeadlampOffset.x, -HeadlampOffset.y, HeadlampOffset.z );
		DrawMarkerHelper( origin + left, rotation * Vector3.Forward, HeadlampColor );
		DrawMarkerHelper( origin + right, rotation * Vector3.Forward, HeadlampColor );
	}

	private static void DrawMarkerHelper( Vector3 position, Vector3 direction, Color color )
	{
		Gizmo.Draw.Color = color;
		Gizmo.Draw.LineSphere( position, 6f );
		Gizmo.Draw.Arrow( position, position + direction * 24f );
	}
}
