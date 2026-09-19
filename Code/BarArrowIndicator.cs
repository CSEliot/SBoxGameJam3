// Project: sboxgamejam3
// File:    BarArrowIndicator.cs
// Author:  cseliot
// Created: 2026.09.19.00.50.48
// Edited: 2026.09.19.00.50.48
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Procedural 3D arrow mesh, spawned above the player's head by GameManager, that points
// toward whichever bar _targetBarWaiting currently targets.
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
/// Owns a small extruded arrow model built at runtime with ModelBuilder/Mesh (no .vmdl asset).
/// GameManager creates one of these as a child of the local player (see
/// GameManager.SpawnArrowIndicatorHelper) and calls PointAt every frame with the world position
/// of the bar at _targetBarWaiting. The arrow only yaws (stays level) so it's always readable,
/// matching Design.md's "Arrow indicator stays crisp, gameplay critical" rule.
/// </summary>
public sealed class BarArrowIndicator : Component
{
	/// <summary>
	/// Local height above the parent (the player root) the arrow floats at.
	/// </summary>
	[Property] private float _HeightAboveHead { get; set; } = 80f;

	/// <summary>
	/// Tip-to-tail length of the arrow mesh, in units.
	/// </summary>
	[Property] private float _ArrowLength { get; set; } = 16f;

	/// <summary>
	/// Width of the arrowhead at its widest point, in units.
	/// </summary>
	[Property] private float _ArrowWidth { get; set; } = 8f;

	/// <summary>
	/// Extrusion depth of the arrow mesh, in units.
	/// </summary>
	[Property] private float _ArrowThickness { get; set; } = 2f;

	/// <summary>
	/// Baked-in vertex color. Defaults to Design.md's primary glow (#4C74E5).
	/// </summary>
	[Property] private Color _ArrowColor { get; set; } = new Color( 0.298f, 0.455f, 0.898f );

	private ModelRenderer _modelRenderer;
	private Vector3? _targetWorldPosition;

	protected override void OnStart()
	{
		LocalPosition = Vector3.Up * _HeightAboveHead;
		LocalRotation = Rotation.Identity;

		_modelRenderer = Components.GetOrCreate<ModelRenderer>();
		_modelRenderer.Model = BuildArrowModelHelper();
	}

	protected override void OnUpdate()
	{
		if ( _targetWorldPosition is null )
			return;

		// Flatten to the horizontal plane so the arrow only yaws - it should never pitch up
		// or down toward a bar that's higher/lower than the player, just point the way to walk.
		Vector3 toTarget = (_targetWorldPosition.Value - WorldPosition).WithZ( 0f );
		if ( toTarget.IsNearlyZero() )
			return;

		WorldRotation = Rotation.LookAt( toTarget.Normal, Vector3.Up );
	}

	/// <summary>
	/// Called by GameManager every frame with the world position of the current target bar.
	/// </summary>
	public void PointAt( Vector3 worldPosition )
	{
		_targetWorldPosition = worldPosition;
	}

	/// <summary>
	/// Builds a small extruded arrow model at runtime via ModelBuilder/Mesh - see engine
	/// PreviewPhysics.cs (AddPart) for the same VertexBuffer -> Mesh.CreateBuffers -> ModelBuilder
	/// pattern this follows. No collision shapes are added; this is render-only.
	/// </summary>
	private Model BuildArrowModelHelper()
	{
		Vector3[] outline = ArrowOutlineHelper();
		int[] topTriangles = Mesh.TriangulatePolygon( outline ).ToArray();
		float halfThickness = _ArrowThickness * 0.5f;

		var vb = new VertexBuffer();
		vb.Init( true );

		AddFlatFaceHelper( vb, outline, topTriangles, halfThickness, Vector3.Up );
		AddFlatFaceHelper( vb, outline, topTriangles, -halfThickness, Vector3.Down );
		AddSideWallsHelper( vb, outline, halfThickness );

		var mesh = new Mesh( "bar_arrow", ArrowMaterialHelper(), MeshPrimitiveType.Triangles );
		mesh.CreateBuffers( vb );

		return Model.Builder
			.WithName( "bar_arrow_indicator" )
			.AddMesh( mesh )
			.Create();
	}

	/// <summary>
	/// Flat arrow silhouette in local space (forward = +X, matching Vector3.Forward), tip
	/// pointing toward +Forward - the direction OnUpdate rotates the whole GameObject to face.
	/// Seven points forming a simple (non-self-intersecting) polygon so Mesh.TriangulatePolygon
	/// can fan/ear-clip it directly.
	/// </summary>
	private Vector3[] ArrowOutlineHelper()
	{
		float halfShaft = _ArrowWidth * 0.25f;
		float halfHead = _ArrowWidth * 0.5f;
		float shaftLength = _ArrowLength * 0.55f;

		return new[]
		{
			new Vector3( 0f, halfShaft, 0f ),
			new Vector3( shaftLength, halfShaft, 0f ),
			new Vector3( shaftLength, halfHead, 0f ),
			new Vector3( _ArrowLength, 0f, 0f ),
			new Vector3( shaftLength, -halfHead, 0f ),
			new Vector3( shaftLength, -halfShaft, 0f ),
			new Vector3( 0f, -halfShaft, 0f ),
		};
	}

	/// <summary>
	/// Adds the top or bottom cap of the extrusion. Each triangle is added in both windings so
	/// the face renders regardless of the material's front-face culling convention - this is a
	/// tiny HUD compass mesh, the doubled triangle count is negligible.
	/// </summary>
	private void AddFlatFaceHelper( VertexBuffer vb, Vector3[] outline, int[] triangleIndices, float z, Vector3 normal )
	{
		for ( int i = 0; i < triangleIndices.Length; i += 3 )
		{
			var a = MakeVertexHelper( outline[triangleIndices[i]].WithZ( z ), normal );
			var b = MakeVertexHelper( outline[triangleIndices[i + 1]].WithZ( z ), normal );
			var c = MakeVertexHelper( outline[triangleIndices[i + 2]].WithZ( z ), normal );

			vb.AddTriangle( a, b, c );
			vb.AddTriangle( c, b, a );
		}
	}

	/// <summary>
	/// Adds one quad per outline edge connecting the top and bottom caps, giving the arrow real
	/// thickness instead of a flat cutout. Both windings added, same reasoning as AddFlatFaceHelper.
	/// </summary>
	private void AddSideWallsHelper( VertexBuffer vb, Vector3[] outline, float halfThickness )
	{
		for ( int i = 0; i < outline.Length; i++ )
		{
			Vector3 a = outline[i];
			Vector3 b = outline[(i + 1) % outline.Length];

			Vector3 topA = a.WithZ( halfThickness );
			Vector3 topB = b.WithZ( halfThickness );
			Vector3 bottomA = a.WithZ( -halfThickness );
			Vector3 bottomB = b.WithZ( -halfThickness );

			Vector3 normal = Vector3.Cross( topB - topA, bottomA - topA ).Normal;

			var vTopA = MakeVertexHelper( topA, normal );
			var vTopB = MakeVertexHelper( topB, normal );
			var vBottomA = MakeVertexHelper( bottomA, normal );
			var vBottomB = MakeVertexHelper( bottomB, normal );

			vb.AddQuad( vTopA, vTopB, vBottomB, vBottomA );
			vb.AddQuad( vBottomA, vBottomB, vTopB, vTopA );
		}
	}

	private Vertex MakeVertexHelper( Vector3 position, Vector3 normal )
	{
		var vertex = new Vertex( position, normal, Vector3.Left, Vector4.Zero );
		vertex.Color = _ArrowColor;
		return vertex;
	}

	private Material ArrowMaterialHelper()
	{
		return Material.Load( "materials/default/vertex_color.vmat" );
	}
}
