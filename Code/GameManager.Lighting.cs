// Project: sboxgamejam3
// File:    GameManager.Lighting.cs
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
// Day to night lighting for GameManager, driven by the leading player's beers.
// Day values are read from the live scene once at start; only night values are exposed.
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

namespace Sandbox;

public sealed partial class GameManager
{
	/// <summary>
	/// Fog Start/EndDistance during the day. Set well beyond the camera ZFar (100000) so the fog
	/// does not read at all in daylight; the night values are reached by geometric (log space)
	/// interpolation, so the fog closes in evenly as night falls.
	/// </summary>
	private const float _DayFogDistance = 100000f;

	/// <summary>
	/// Beers the leading player needs before the world reaches full night. The night factor is
	/// clamp( leaderBeers / this, 0, 1 ). 0 or below pins the factor at 0 (permanent afternoon).
	/// </summary>
	[Property, Group( "Night Lighting" )] private float _BeersMaxNightTime { get; set; } = 20f;

	/// <summary>
	/// Seconds the lighting takes to blend toward a new target factor after the leading player's
	/// beers change. 0 or below snaps instantly.
	/// </summary>
	[Property, Group( "Night Lighting" )] private float _NightBlendSeconds { get; set; } = 3f;

	/// <summary>
	/// World rotation of the sun directional light at full night (from city.scene). The day value
	/// is read from the live scene at start.
	/// </summary>
	[Property, Group( "Night Lighting" )] private Rotation _NightSunRotation { get; set; } = new Rotation( 0.379222363f, 0.102005303f, -0.299588025f, 0.869501114f );

	/// <summary>
	/// DirectionalLight.LightColor at full night (from city.scene).
	/// </summary>
	[Property, Group( "Night Lighting" )] private Color _NightLightColor { get; set; } = new Color( 0.11617f, 0.13019f, 0.13953f, 1f );

	/// <summary>
	/// DirectionalLight.SkyColor (world ambient) at full night (from city.scene).
	/// </summary>
	[Property, Group( "Night Lighting" )] private Color _NightSkyColor { get; set; } = new Color( 0.16162f, 0.14278f, 0.25581f, 1f );

	/// <summary>
	/// SkyBox2D.Tint at full night (from city.scene).
	/// </summary>
	[Property, Group( "Night Lighting" )] private Color _NightSkyboxTint { get; set; } = new Color( 0.11628f, 0.10114f, 0.10114f, 1f );

	/// <summary>
	/// polygoncity_texture_01_a.vmat g_flSelfIllumScale at full night. 1.0 in both the day and
	/// night references; kept exposed so the window glow can be tuned.
	/// </summary>
	[Property, Group( "Night Lighting" )] private float _NightSelfIllumScale { get; set; } = 1f;

	/// <summary>
	/// polygoncity_texture_01_a.vmat g_flSelfIllumBrightness at full night (window glow). The day
	/// value is read from the live material at start (0.000, unlit windows). 3.528 is the only
	/// authored lit value in the project and is the night reference.
	/// </summary>
	[Property, Group( "Night Lighting" )] private float _NightSelfIllumBrightness { get; set; } = 3.528f;

	/// <summary>
	/// Sky material the night fog samples. When left unset at start it falls back to
	/// materials/skybox/skybox_dark_01.vmat (the city.scene cubemap fog sky).
	/// </summary>
	[Property, Group( "Night Fog" )] private Material _NightFogSky { get; set; }

	/// <summary>
	/// CubemapFog.Blur at night (from city.scene).
	/// </summary>
	[Property, Group( "Night Fog" )] private float _NightFogBlur { get; set; } = 1f;

	/// <summary>
	/// CubemapFog.StartDistance at full night (from city.scene). Day uses a very large distance
	/// (_DayFogDistance) so the fog does not read at all; the value is blended to this in log space
	/// as night falls, so the fog closes in evenly rather than only near the end.
	/// </summary>
	[Property, Group( "Night Fog" )] private float _NightFogStartDistance { get; set; } = 10f;

	/// <summary>
	/// CubemapFog.EndDistance at full night (from city.scene). Day uses a very large distance
	/// (_DayFogDistance) so the fog does not read at all; the value is blended to this in log space
	/// as night falls, so the fog closes in evenly rather than only near the end.
	/// </summary>
	[Property, Group( "Night Fog" )] private float _NightFogEndDistance { get; set; } = 4000f;

	/// <summary>
	/// CubemapFog.FalloffExponent at night (from city.scene).
	/// </summary>
	[Property, Group( "Night Fog" )] private float _NightFogFalloffExponent { get; set; } = 1f;

	/// <summary>
	/// CubemapFog.HeightWidth at night (from city.scene).
	/// </summary>
	[Property, Group( "Night Fog" )] private float _NightFogHeightWidth { get; set; } = 0f;

	/// <summary>
	/// CubemapFog.HeightStart at night (from city.scene).
	/// </summary>
	[Property, Group( "Night Fog" )] private float _NightFogHeightStart { get; set; } = 2000f;

	/// <summary>
	/// CubemapFog.HeightExponent at night (from city.scene).
	/// </summary>
	[Property, Group( "Night Fog" )] private float _NightFogHeightExponent { get; set; } = 2f;

	/// <summary>
	/// CubemapFog.Tint at full night (from city.scene). The day state blends from a transparent
	/// version of this color.
	/// </summary>
	[Property, Group( "Night Fog" )] private Color _NightFogTint { get; set; } = new Color( 0.05394f, 0.00147f, 0.31628f, 1f );

	// ---------------------------------------------------------------------------------------
	// Captured day state (A13: read from the live scene at start, never exposed).
	// ---------------------------------------------------------------------------------------

	/// <summary>
	/// Sun directional light resolved at start: the scene root named "Sun" when present, else the
	/// first DirectionalLight in the scene.
	/// </summary>
	private DirectionalLight _daySun;

	/// <summary>Sun world rotation at the moment the game started (the "afternoon" pose).</summary>
	private Rotation _daySunRotation = Rotation.Identity;

	/// <summary>Sun LightColor at the moment the game started.</summary>
	private Color _dayLightColor = Color.White;

	/// <summary>Sun SkyColor (world ambient) at the moment the game started.</summary>
	private Color _daySkyColor = Color.White;

	/// <summary>The scene's 2D skybox, resolved at start, or null when the scene has none.</summary>
	private SkyBox2D _skybox;

	/// <summary>SkyBox2D tint at the moment the game started.</summary>
	private Color _daySkyboxTint = Color.White;

	/// <summary>
	/// The shared city buildings material (imported/polygoncity/materials/polygoncity_texture_01_a.vmat).
	/// Its self-illumination is edited live for the window glow and restored on teardown.
	/// </summary>
	private Material _cityMaterial;

	/// <summary>g_flSelfIllumScale read from the live material at start (day window glow scale).</summary>
	private float _daySelfIllumScale = 1f;

	/// <summary>g_flSelfIllumBrightness read from the live material at start (day window glow brightness).</summary>
	private float _daySelfIllumBrightness;

	/// <summary>
	/// Local holder for the runtime cubemap fog. minimal.scene carries no CubemapFog, so one is
	/// created at start (the engine queries CubemapFog scene-wide every frame), disabled while the
	/// night factor is effectively zero, and destroyed on teardown.
	/// </summary>
	private GameObject _fogHolder;

	/// <summary>The runtime CubemapFog living on <see cref="_fogHolder"/>.</summary>
	private CubemapFog _fog;

	/// <summary>Set once the day state has been captured, so teardown never writes unset values.</summary>
	private bool _lightingCaptured;

	/// <summary>The blended night factor currently applied to the world (0 day, 1 full night).</summary>
	private float _currentFactor;

	/// <summary>The factor the blend is heading toward, from the leading player's beers.</summary>
	private float _targetFactor;

	/// <summary>The factor the current blend started from.</summary>
	private float _fromFactor;

	/// <summary>Seconds elapsed in the current blend.</summary>
	private float _blendElapsed;

	/// <summary>Last factor written to the world, so unchanged frames skip every write. Starts negative.</summary>
	private float _lastWrittenFactor = -1f;

	private bool _warnedNoSun;
	private bool _warnedNoSkybox;
	private bool _warnedNoCityMaterial;

	/// <summary>
	/// The leading player's BeerLevel: the max over every player root (disabled roots included, so
	/// a leader parked in a bar still counts), 0 when there are none. Same scan the ghost car count
	/// uses, so the lighting and the car count always agree on who is leading.
	/// </summary>
	private float LeaderBeersHelper()
	{
		float leader = 0f;
		foreach ( var player in GhostPlayerRootsHelper() )
		{
			var cc = player.GetComponent<DrunkCC>( true );
			if ( cc is not null && cc.BeerLevel > leader )
				leader = cc.BeerLevel;
		}

		return leader;
	}

	/// <summary>
	/// Reads the "afternoon" look from the live scene once at start: the sun light's rotation,
	/// LightColor and SkyColor, the 2D skybox tint, the city material's self-illumination values,
	/// and builds the local cubemap fog holder. Night values are GameManager properties, so only
	/// these day values are captured here. A missing piece logs one warning and skips only that
	/// value, leaving the rest of the lighting working.
	/// </summary>
	private void CaptureDayLightingHelper()
	{
		// Sun: prefer the scene root named "Sun", fall back to the first directional light.
		foreach ( var light in Scene.GetAllComponents<DirectionalLight>() )
		{
			if ( light is null )
				continue;

			if ( _daySun is null || light.GameObject.Name == "Sun" )
				_daySun = light;

			if ( light.GameObject.Name == "Sun" )
				break;
		}

		if ( _daySun is not null && _daySun.IsValid() )
		{
			_daySunRotation = _daySun.WorldRotation;
			_dayLightColor = _daySun.LightColor;
			_daySkyColor = _daySun.SkyColor;
		}
		else if ( !_warnedNoSun )
		{
			_warnedNoSun = true;
			Log.Warning( "GameManager: no DirectionalLight in the scene; day to night lighting will not move the sun." );
		}

		_skybox = Scene.GetAllComponents<SkyBox2D>().FirstOrDefault();
		if ( _skybox is not null && _skybox.IsValid() )
		{
			_daySkyboxTint = _skybox.Tint;
		}
		else if ( !_warnedNoSkybox )
		{
			_warnedNoSkybox = true;
			Log.Warning( "GameManager: no SkyBox2D in the scene; day to night lighting will not tint the skybox." );
		}

		_cityMaterial = Material.Load( "imported/polygoncity/materials/polygoncity_texture_01_a.vmat" );
		if ( _cityMaterial is not null )
		{
			_daySelfIllumScale = _cityMaterial.GetVector4( "g_flSelfIllumScale" ).x;
			_daySelfIllumBrightness = _cityMaterial.GetVector4( "g_flSelfIllumBrightness" ).x;
		}
		else if ( !_warnedNoCityMaterial )
		{
			_warnedNoCityMaterial = true;
			Log.Warning( "GameManager: could not load polygoncity_texture_01_a.vmat; day to night lighting will not glow the windows." );
		}

		BuildFogHolderHelper();

		_currentFactor = 0f;
		_targetFactor = 0f;
		_fromFactor = 0f;
		_blendElapsed = 0f;
		_lastWrittenFactor = -1f;
		_lightingCaptured = true;
	}

	/// <summary>
	/// Creates the one local cubemap fog holder this feature uses. minimal.scene has no CubemapFog
	/// (its camera prefab instance removes it), so a dedicated hidden, unsaved, never-networked
	/// GameObject carries the component instead. The engine finds it scene-wide on the next frame.
	/// Static night values are written once here; the per-frame pass slides the distances and tint.
	/// </summary>
	private void BuildFogHolderHelper()
	{
		if ( _fogHolder is not null && _fogHolder.IsValid() )
			return;

		_fogHolder = new GameObject( true, "GameManager-NightFog" );
		_fogHolder.NetworkMode = NetworkMode.Never;
		_fogHolder.Flags = GameObjectFlags.Hidden | GameObjectFlags.NotSaved;

		_fog = _fogHolder.AddComponent<CubemapFog>();
		if ( _fog is null || !_fog.IsValid() )
		{
			Log.Warning( "GameManager: could not add a CubemapFog for the night fog." );
			return;
		}

		if ( _NightFogSky is null )
			_NightFogSky = Material.Load( "materials/skybox/skybox_dark_01.vmat" );

		_fog.Sky = _NightFogSky;
		_fog.Blur = _NightFogBlur;
		_fog.FalloffExponent = _NightFogFalloffExponent;
		_fog.HeightWidth = _NightFogHeightWidth;
		_fog.HeightStart = _NightFogHeightStart;
		_fog.HeightExponent = _NightFogHeightExponent;

		// Day: keep the fog off entirely so the scene reads as true no-fog afternoon.
		_fog.Enabled = false;
	}

	/// <summary>
	/// Per-frame lighting pass, run on every client before the local-player early-out (local
	/// visuals only, no host gate). It turns the leading player's beers into a night factor, blends
	/// smoothly toward that factor over the exposed seconds after each change (A11), and writes the
	/// six slid values plus the cubemap fog only when the applied factor actually changed, since the
	/// material Set marks the shared city material edited.
	/// </summary>
	private void UpdateDayNightLightingHelper()
	{
		float leaderBeers = LeaderBeersHelper();

		float target = _BeersMaxNightTime > 0f
			? Math.Clamp( leaderBeers / _BeersMaxNightTime, 0f, 1f )
			: 0f;

		// A new target restarts the blend from wherever the factor is right now.
		if ( MathF.Abs( target - _targetFactor ) > 0.0001f )
		{
			_fromFactor = _currentFactor;
			_targetFactor = target;
			_blendElapsed = 0f;
		}

		if ( _NightBlendSeconds <= 0f )
		{
			_currentFactor = _targetFactor;
		}
		else if ( MathF.Abs( _currentFactor - _targetFactor ) > 0.0001f )
		{
			_blendElapsed += Time.Delta;
			float t = Math.Clamp( _blendElapsed / _NightBlendSeconds, 0f, 1f );
			// Smoothstep: ease in and out so a beer batch does not snap the sky.
			float s = t * t * ( 3f - 2f * t );
			_currentFactor = MathX.Lerp( _fromFactor, _targetFactor, s );
		}

		float factor = _currentFactor;

		// The material edit marks the shared resource dirty, so skip every write while the factor holds.
		if ( MathF.Abs( factor - _lastWrittenFactor ) < 0.0001f )
			return;

		_lastWrittenFactor = factor;
		ApplyLightingFactorHelper( factor );
	}

	/// <summary>
	/// Applies a night factor to the world: the sun rotation and its two colors, the skybox tint,
	/// the city material's self-illumination, and the cubemap fog gate. Each block guards its own
	/// missing piece so the rest still blends.
	/// </summary>
	private void ApplyLightingFactorHelper( float factor )
	{
		if ( _daySun is not null && _daySun.IsValid() )
		{
			_daySun.WorldRotation = Rotation.Slerp( _daySunRotation, _NightSunRotation, factor );
			_daySun.LightColor = Color.Lerp( _dayLightColor, _NightLightColor, factor );
			// SkyColor feeds world ambient on the next frame; sliding it is the light's ambient path.
			_daySun.SkyColor = Color.Lerp( _daySkyColor, _NightSkyColor, factor );
		}

		if ( _skybox is not null && _skybox.IsValid() )
			_skybox.Tint = Color.Lerp( _daySkyboxTint, _NightSkyboxTint, factor );

		if ( _cityMaterial is not null )
		{
			_cityMaterial.Set( "g_flSelfIllumScale", MathX.Lerp( _daySelfIllumScale, _NightSelfIllumScale, factor ) );
			_cityMaterial.Set( "g_flSelfIllumBrightness", MathX.Lerp( _daySelfIllumBrightness, _NightSelfIllumBrightness, factor ) );
		}

		ApplyFogFactorHelper( factor );
	}

	/// <summary>
	/// Gates and slides the runtime cubemap fog. At factor 0 (or below the fog floor) the component
	/// is disabled, which removes it from the scene-wide CubemapFog query and gives a true no-fog
	/// day look. Above the floor it is enabled and its distances blend from a "beyond everything"
	/// day value down to the night values in log space, with the tint fading in from a transparent
	/// version of the night tint, so the fog closes in evenly as night falls.
	/// </summary>
	private void ApplyFogFactorHelper( float factor )
	{
		if ( _fog is null || !_fog.IsValid() )
			return;

		if ( factor <= 0.001f )
		{
			_fog.Enabled = false;
			return;
		}

		_fog.Enabled = true;
		_fog.Tint = Color.Lerp( new Color( _NightFogTint.r, _NightFogTint.g, _NightFogTint.b, 0f ), _NightFogTint, factor );
		_fog.EndDistance = BlendFogDistanceHelper( _DayFogDistance, _NightFogEndDistance, factor );
		_fog.StartDistance = BlendFogDistanceHelper( _DayFogDistance, _NightFogStartDistance, factor );
	}

	/// <summary>
	/// Geometric (log space) blend from a day distance to a night distance: day * pow( night / day, factor ).
	/// A linear blend from 100000 stays beyond the visible depth until the factor is very high, so a
	/// log blend is used instead to close the ramp in evenly across the whole factor range (e.g.
	/// 100000 -> 4000 reads at ~20000 halfway, 100000 -> 10 reads at ~1000 halfway). When the night
	/// value is not positive the log blend is undefined, so this falls back to a linear blend.
	/// </summary>
	private static float BlendFogDistanceHelper( float day, float night, float factor )
	{
		if ( night <= 0f )
			return MathX.Lerp( day, night, factor );

		return day * MathF.Pow( night / day, factor );
	}

	/// <summary>
	/// Teardown, run when this manager is destroyed (play stop tears the scene down and destroys
	/// every component). The material edit lives on the shared loaded resource and does not revert
	/// when play stops, so the captured day self-illumination values are written back here. Sun,
	/// skybox and tint are restored too, and the runtime fog holder is destroyed. Safe to call more
	/// than once.
	/// </summary>
	private void TeardownDayNightLightingHelper()
	{
		if ( !_lightingCaptured )
			return;

		if ( _daySun is not null && _daySun.IsValid() )
		{
			_daySun.WorldRotation = _daySunRotation;
			_daySun.LightColor = _dayLightColor;
			_daySun.SkyColor = _daySkyColor;
		}

		if ( _skybox is not null && _skybox.IsValid() )
			_skybox.Tint = _daySkyboxTint;

		// Restore the shared material last: this is the edit that would otherwise leak into the editor session.
		if ( _cityMaterial is not null )
		{
			_cityMaterial.Set( "g_flSelfIllumScale", _daySelfIllumScale );
			_cityMaterial.Set( "g_flSelfIllumBrightness", _daySelfIllumBrightness );
		}

		if ( _fogHolder is not null && _fogHolder.IsValid() )
			_fogHolder.Destroy();

		_fogHolder = null;
		_fog = null;
		_lightingCaptured = false;
	}

	/// <summary>
	/// Restores the day look and destroys the runtime fog when this manager is destroyed.
	/// </summary>
	protected override void OnDestroy()
	{
		TeardownDayNightLightingHelper();
	}
}
