// using System;
// using System.Collections.Generic;
// using Sandbox;
// using Sandbox.Rendering;
//
// namespace PhysicsAndShaders;
//
// [Title( "Gpu Physics Post" )]
// [Category( "Post Processing" )]
// [Icon( "lens_blur" )]
// public sealed class GpuPhysicsPost : BasePostProcess
// {
// 	[Property] public bool ImpactRipples { get; set; } = true;
// 	[Property, Range( 1f, 20f )] public float RippleVelThreshold { get; set; } = 6.0f;
// 	[Property, Range( 0f, 3f )] public float RippleStrength { get; set; } = 1.0f;
// 	[Property, Range( 500f, 8000f )] public float RippleSpeed { get; set; } = 2600.0f;
// 	[Property, Range( 20f, 400f )] public float RippleThickness { get; set; } = 110.0f;
// 	[Property, Range( 300f, 5000f )] public float RippleRange { get; set; } = 1600.0f;
// 	[Property, Range( 0.3f, 3f )] public float RippleLife { get; set; } = 1.1f;
// 	[Property, Range( 1, 16 )] public int MaxRipplesPerFrame { get; set; } = 5;
// 	[Property] public bool ContactShadows { get; set; } = true;
// 	[Property, Range( 0f, 3f )] public float ShadowStrength { get; set; } = 1.0f;
// 	[Property] public bool SpeedHaze { get; set; } = true;
// 	[Property, Range( 0f, 3f )] public float HazeStrength { get; set; } = 1.0f;
// 	[Property, Range( 4f, 30f )] public float HazeMinSpeed { get; set; } = 12.0f;
//
// 	const int RingCap = 64;
//
// 	GpuPhysicsRain _rain;
// 	ComputeShader _cs;
// 	Material _material;
// 	GpuBuffer<float> _ring;
// 	GpuBuffer<uint> _meta;
// 	bool _ready;
//
// 	protected override void OnEnabled()
// 	{
// 		_ready = false;
// 	}
//
// 	void Setup()
// 	{
// 		_ready = true;
//
// 		_cs = new ComputeShader( "shaders/b3_impacts_cs.shader" );
// 		_material = Material.FromShader( "shaders/b3_physics_post.shader" );
// 		_ring = new GpuBuffer<float>( RingCap * 8, GpuBuffer.UsageFlags.Structured );
// 		_meta = new GpuBuffer<uint>( 4, GpuBuffer.UsageFlags.Structured );
//
// 		_ring.SetData( new float[RingCap * 8].AsSpan() );
// 		_meta.SetData( new uint[4].AsSpan() );
//
// 		var a = _cs.Attributes;
// 		a.Set( "ImpactRing", _ring );
// 		a.Set( "ImpactMeta", _meta );
// 		a.Set( "B3Bodies", _rain.BodiesBuffer );
// 		a.Set( "B3Pairs", _rain.PairsBuffer );
// 		a.Set( "B3Manifolds", _rain.ManifoldsBuffer );
// 		a.Set( "B3Constraints", _rain.ConstraintsBuffer );
// 		a.Set( "B3BpCounters", _rain.CountersBuffer );
// 		a.Set( "B3PairCap", _rain.PairCapacity );
// 		a.Set( "ImpRingCap", RingCap );
// 		a.Set( "ImpRenderScale", _rain.RenderScale );
// 	}
//
// 	protected override void OnUpdate()
// 	{
// 		if ( !_ready )
// 		{
// 			_rain ??= Scene.GetAllComponents<GpuPhysicsRain>().FirstOrDefault();
// 			if ( _rain is null || _rain.BodiesBuffer is null )
// 				return;
//
// 			Setup();
// 		}
//
// 		if ( !ImpactRipples )
// 			return;
//
// 		var a = _cs.Attributes;
// 		a.Set( "ImpTime", Time.Now );
// 		a.Set( "ImpVelMin", RippleVelThreshold );
// 		a.Set( "ImpFrameCap", MaxRipplesPerFrame );
//
// 		a.Set( "ImpMode", 1 );
// 		_cs.Dispatch( 1, 1, 1 );
// 		a.Set( "ImpMode", 0 );
// 		_cs.Dispatch( _rain.PairCapacity, 1, 1 );
// 	}
//
// 	public override void Render()
// 	{
// 		if ( !_ready || !_material.IsValid() )
// 			return;
//
// 		Attributes.Set( "ImpactRing", _ring );
// 		Attributes.Set( "B3Bodies", _rain.BodiesBuffer );
// 		Attributes.Set( "B3GridCount", _rain.GridCountBuffer );
// 		Attributes.Set( "B3GridCells", _rain.GridCellsBuffer );
// 		Attributes.Set( "PostTime", Time.Now );
// 		Attributes.Set( "PostRenderScale", _rain.RenderScale );
// 		Attributes.Set( "B3GridOrigin", _rain.GridLoPhys );
// 		Attributes.Set( "B3CellSize", _rain.CellSizePhys );
// 		Attributes.Set( "B3GridX", _rain.GridX );
// 		Attributes.Set( "B3GridY", _rain.GridY );
// 		Attributes.Set( "B3GridZ", _rain.GridZ );
// 		Attributes.Set( "B3CellCap", 64 );
// 		Attributes.Set( "ImpRingCap", RingCap );
// 		Attributes.Set( "PostRippleStrength", ImpactRipples ? RippleStrength : 0.0f );
// 		Attributes.Set( "PostRippleSpeed", RippleSpeed );
// 		Attributes.Set( "PostRippleThickness", RippleThickness );
// 		Attributes.Set( "PostRippleRange", RippleRange );
// 		Attributes.Set( "PostRippleLife", RippleLife );
// 		Attributes.Set( "PostAoStrength", ContactShadows ? ShadowStrength : 0.0f );
// 		Attributes.Set( "PostHazeStrength", SpeedHaze ? HazeStrength : 0.0f );
// 		Attributes.Set( "PostHazeMinSpeed", HazeMinSpeed );
//
// 		if ( Camera.IsValid() )
// 		{
// 			Attributes.Set( "CamRight", Camera.WorldRotation.Right );
// 			Attributes.Set( "CamUp", Camera.WorldRotation.Up );
// 		}
//
// 		var blit = BlitMode.WithBackbuffer( _material, Stage.BeforePostProcess, 4550, false );
// 		Blit( blit, "B3PhysicsPost" );
// 	}
//
// 	protected override void OnDisabled()
// 	{
// 		_ready = false;
// 		_ring?.Dispose();
// 		_ring = null;
// 		_meta?.Dispose();
// 		_meta = null;
// 	}
// }
