// Project: sboxgamejam3
// File:    FakeLightBeam.cs
// Author:  cseliot
// Created: 2026.10.11.10.07.08
// Edited: 2026.10.11.10.07.08
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Drives one fake light beam: sizes a shared unit-cone mesh to the light's
// aim/angle and pushes the shader uniforms (apex, axis, length, half-angle,
// colour, intensity) every frame. Optionally clips the cone length to the
// first world hit so the beam does not poke through walls.
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

/// <summary>
/// Fits a shared unit cone mesh to a <see cref="SpotLight"/> and feeds the
/// fake_light_beam shader its per-beam uniforms every frame. Put this on the
/// light and point <see cref="Cone"/> at a child ModelRenderer whose Model is
/// <see cref="GetUnitConeModel"/>.
/// </summary>
public sealed class FakeLightBeam : Component
{
	[Property] public SpotLight Light { get; set; }

	/// <summary>Unit cone child (apex at origin, axis +X, length 1, base radius 1).</summary>
	[Property] public ModelRenderer Cone { get; set; }

	[Property] public float MaxLength { get; set; } = 400f;

	/// <summary>Fraction of the light's ConeOuter half-angle used for the visible beam.</summary>
	[Property, Range( 0, 1 )] public float AngleScale { get; set; } = 0.85f;

	[Property] public float Intensity { get; set; } = 0.15f;

	[Property] public bool ClipToWorld { get; set; } = true;

	/// <summary>Extra length past the trace hit so the shader's depth feather can blend into the surface.</summary>
	[Property] public float ClipOvershoot { get; set; } = 16f;

	/// <summary>Hierarchy the clip trace ignores. Empty = this GameObject. Set it to the car root when the beam is on a vehicle.</summary>
	[Property] public GameObject TraceIgnore { get; set; }

	private static Model _unitConeModel;
	private static bool _unitConeBuilt;

	/// <summary>
	/// One shared unit cone model for every beam in the game. Built lazily on first
	/// use with the fake_light_beam material; callers must not rebuild it per beam.
	/// </summary>
	public static Model GetUnitConeModel()
	{
		if ( _unitConeBuilt )
			return _unitConeModel;

		var material = Material.FromShader( "shaders/fake_light_beam.shader" );
		var model = BuildUnitConeHelper( material, 24 );
		if ( model is null )
		{
			// Leave the latch clear so a later caller retries instead of every beam
			// silently rendering nothing behind a half-set latch.
			Log.Error( "FakeLightBeam: failed to build the shared unit cone model." );
			return null;
		}

		_unitConeModel = model;
		_unitConeBuilt = true;
		return _unitConeModel;
	}

	/// <summary>
	/// Procedural cone: apex at origin, axis +X, length 1, base radius 1 (45 deg),
	/// open base. One unit mesh fits any light once scaled to
	/// (length, length*tan(a), length*tan(a)).
	/// </summary>
	private static Model BuildUnitConeHelper( Material material, int sides )
	{
		var vb = new VertexBuffer();
		vb.Init( true );

		// One apex copy per side so UVs/normals don't fight at the tip.
		for ( int i = 0; i <= sides; i++ )
		{
			float u = (float)i / sides;
			float ang = u * MathF.PI * 2f;
			var rim = new Vector3( 1f, MathF.Cos( ang ), MathF.Sin( ang ) );
			var normal = new Vector3( -1f, MathF.Cos( ang ), MathF.Sin( ang ) ).Normal;
			var tangent = new Vector3( 0f, -MathF.Sin( ang ), MathF.Cos( ang ) );

			vb.Add( new Vertex( Vector3.Zero, normal, tangent, new Vector4( u, 0, 0, 0 ) ) );
			vb.Add( new Vertex( rim, normal, tangent, new Vector4( u, 1, 0, 0 ) ) );
		}

		for ( int i = 0; i < sides; i++ )
		{
			int apex = i * 2;
			vb.AddRawIndex( apex );
			vb.AddRawIndex( apex + 1 );
			vb.AddRawIndex( apex + 3 );
		}

		var mesh = new Mesh( "beam_cone", material );
		mesh.CreateBuffers( vb );
		return Model.Builder.AddMesh( mesh ).Create();
	}

	protected override void OnPreRender()
	{
		if ( !Light.IsValid() || !Cone.IsValid() )
			return;

		Cone.RenderType = ModelRenderer.ShadowRenderType.Off;

		var apex = Light.WorldPosition;
		var axis = Light.WorldRotation.Forward;
		var length = MaxLength;

		if ( ClipToWorld )
		{
			var tr = Scene.Trace.Ray( apex, apex + axis * MaxLength )
				.IgnoreGameObjectHierarchy( TraceIgnore.IsValid() ? TraceIgnore : GameObject )
				.Run();

			if ( tr.Hit )
				length = MathF.Min( MaxLength, tr.Distance + ClipOvershoot );
		}

		var halfAngle = (Light.ConeOuter * AngleScale).DegreeToRadian();
		var radius = length * MathF.Tan( halfAngle );
		Cone.LocalScale = new Vector3( length, radius, radius );

		var c = Light.LightColor;
		var attrs = Cone.Attributes;
		attrs.Set( "BeamApex", apex );
		attrs.Set( "BeamAxis", axis );
		attrs.Set( "BeamLength", length );
		attrs.Set( "BeamSin", MathF.Sin( halfAngle ) );
		attrs.Set( "BeamCos", MathF.Cos( halfAngle ) );
		attrs.Set( "BeamColor", new Vector3( c.r, c.g, c.b ) );
		attrs.Set( "BeamIntensity", Light.Active ? Intensity : 0f );
	}
}
