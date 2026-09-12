// using System;
// using System.Collections.Generic;
// using System.Linq;
// using Sandbox;
//
// namespace PhysicsAndShaders;
//
// [Title( "Gpu Ragdoll" )]
// [Category( "Physics" )]
// [Icon( "accessibility_new" )]
// public sealed class GpuRagdoll : Component
// {
// 	[Property] public SkinnedModelRenderer Renderer { get; set; }
// 	[Property] public bool RagdollOnStart { get; set; }
// 	[Property] public Vector3 LaunchVelocity { get; set; }
// 	[Property] public bool DebugShapes { get; set; }
// 	[Property] public bool JointLimits { get; set; } = true;
// 	[Property, Range( 0f, 2f )] public float LinearDamping { get; set; } = 0.15f;
// 	[Property, Range( 0f, 10f )] public float AngularDamping { get; set; } = 3.0f;
//
// 	const int Stride = 64;
//
// 	struct PartMap
// 	{
// 		public int BodySlot;
// 		public BoneCollection.Bone Bone;
// 		public Transform BodyToBone;
// 	}
//
// 	GpuPhysicsRain _rain;
// 	GpuVoxelRagdolls _voxelRag;
// 	bool _voxelMode;
// 	readonly List<GpuVoxelRagdolls.PartShape> _shapes = new();
// 	readonly List<PartMap> _parts = new();
// 	float[] _poseData;
// 	bool _active;
// 	int _bodyStart;
// 	int _partCount;
// 	int _jointStart;
// 	int _jointCount;
// 	bool _breakArmed;
// 	RealTimeSince _sinceSpawn;
//
// 	[Button( "Ragdoll Now" )]
// 	public void RagdollNow() => Ragdoll( LaunchVelocity );
//
// 	public bool HasBodies => _partCount > 0;
// 	public int BodyStart => _bodyStart;
// 	public int PartCount => _partCount;
//
// 	void OnEvicted()
// 	{
// 		_active = false;
// 		if ( _voxelMode )
// 			_voxelRag?.Detach( _bodyStart );
// 		if ( GameObject.IsValid() )
// 			GameObject.Destroy();
// 	}
//
// 	static Transform Mul( Transform a, Transform b )
// 		=> new( a.Position + a.Rotation * b.Position, a.Rotation * b.Rotation );
//
// 	static Transform InvMul( Transform a, Transform b )
// 		=> new( a.Rotation.Inverse * ( b.Position - a.Position ), a.Rotation.Inverse * b.Rotation );
//
// 	static Vector4 QuatToPhys( Rotation q )
// 		=> new( -q.x, -q.z, -q.y, q.w );
//
// 	static Rotation QuatToWorld( float x, float y, float z, float w )
// 		=> new( -x, -z, -y, w );
//
// 	static readonly Rotation FrameFix = Rotation.FromPitch( -90f );
//
// 	static Rotation AlignUp( Vector3 up )
// 	{
// 		var u = up.Normal;
// 		var refDir = MathF.Abs( u.z ) < 0.9f ? Vector3.Up : Vector3.Forward;
// 		var fwd = Vector3.Cross( u, refDir ).Normal;
// 		return Rotation.LookAt( fwd, u );
// 	}
//
// 	public void Ragdoll( Vector3 worldVelocity = default )
// 	{
// 		if ( _active )
// 			return;
//
// 		_rain ??= Scene.GetAllComponents<GpuPhysicsRain>().FirstOrDefault();
// 		Renderer ??= GetComponent<SkinnedModelRenderer>();
//
// 		if ( _rain is null || !_rain.RagdollsAvailable || !Renderer.IsValid() || !Renderer.Model.IsValid() )
// 		{
// 			Log.Warning( "[B3Ragdoll] missing rain system or renderer" );
// 			return;
// 		}
//
// 		var model = Renderer.Model;
// 		var desc = model.Physics;
// 		if ( desc is null || desc.Parts.Count == 0 )
// 		{
// 			Log.Warning( $"[B3Ragdoll] model {model.Name} has no physics parts" );
// 			return;
// 		}
//
// 		var parts = desc.Parts;
// 		var joints = desc.Joints;
// 		int count = parts.Count;
//
// 		if ( !_rain.TryAllocRagdoll( count, joints.Count, OnEvicted, out _bodyStart, out int jointStart, out int ragdollIndex ) )
// 		{
// 			Log.Warning( "[B3Ragdoll] ragdoll allocation failed" );
// 			return;
// 		}
//
// 		float scale = _rain.RenderScale;
//
// 		var parentOf = new int[count];
// 		for ( int i = 0; i < count; i++ )
// 			parentOf[i] = 0xFF;
//
// 		foreach ( var joint in joints )
// 		{
// 			if ( !joint.EnableCollision && joint.Body2 >= 0 && joint.Body2 < count )
// 				parentOf[joint.Body2] = joint.Body1;
// 		}
//
// 		var partWorld = new Transform[count];
// 		var bodyWorld = new Transform[count];
//
// 		_voxelRag = Scene.GetAllComponents<GpuVoxelRagdolls>().FirstOrDefault( c => c.Active );
// 		_voxelMode = _voxelRag is not null;
// 		float padPhys = _voxelMode ? _voxelRag.CubeSize * 0.5f / scale : 0f;
//
// 		_parts.Clear();
// 		_shapes.Clear();
// 		_partCount = count;
//
// 		for ( int i = 0; i < count; i++ )
// 		{
// 			var part = parts[i];
// 			var bone = Renderer.Model.Bones.AllBones.FirstOrDefault( x => x.Name == part.BoneName );
// 			Transform boneWorld = WorldTransform;
// 			if ( bone is not null )
// 				Renderer.TryGetBoneTransform( bone, out boneWorld );
//
// 			partWorld[i] = boneWorld;
//
// 			int kind;
// 			float[] pars;
// 			Vector3 bodyPos;
// 			Rotation bodyRot;
// 			float volume;
//
// 			if ( part.Capsules.Count > 0 )
// 			{
// 				var cap = part.Capsules[0].Capsule;
// 				var centerL = ( cap.CenterA + cap.CenterB ) * 0.5f;
// 				var axisL = cap.CenterB - cap.CenterA;
// 				float segHalf = axisL.Length * 0.5f;
// 				float r = cap.Radius;
//
// 				bodyPos = partWorld[i].Position + partWorld[i].Rotation * centerL;
// 				bodyRot = segHalf > 0.01f ? AlignUp( partWorld[i].Rotation * axisL.Normal ) : partWorld[i].Rotation;
//
// 				float rp = r / scale;
// 				float hp = MathF.Max( segHalf / scale, 0.01f );
// 				kind = 1;
// 				pars = new[] { rp, hp, 0f, DebugShapes ? 0f : 1f };
// 				volume = MathF.PI * rp * rp * ( 2.0f * hp ) + ( 4.0f / 3.0f ) * MathF.PI * rp * rp * rp;
// 			}
// 			else if ( part.Spheres.Count > 0 )
// 			{
// 				var sph = part.Spheres[0].Sphere;
// 				bodyPos = partWorld[i].Position + partWorld[i].Rotation * sph.Center;
// 				bodyRot = partWorld[i].Rotation;
// 				float rp = sph.Radius / scale;
// 				kind = 0;
// 				pars = new[] { rp, 0f, 0f, DebugShapes ? 0f : 1f };
// 				volume = ( 4.0f / 3.0f ) * MathF.PI * rp * rp * rp;
// 			}
// 			else if ( part.Hulls.Count > 0 )
// 			{
// 				var bounds = part.Hulls[0].Bounds;
// 				var centerL = bounds.Center;
// 				var halfL = bounds.Size * 0.5f;
// 				bodyPos = partWorld[i].Position + partWorld[i].Rotation * centerL;
// 				bodyRot = partWorld[i].Rotation;
//
// 				var hp = new Vector3(
// 					MathF.Max( halfL.x, 0.5f ) / scale,
// 					MathF.Max( halfL.z, 0.5f ) / scale,
// 					MathF.Max( halfL.y, 0.5f ) / scale );
// 				kind = 2;
// 				pars = new[] { hp.x, hp.y, hp.z, DebugShapes ? 0f : 1f };
// 				volume = 8.0f * hp.x * hp.y * hp.z;
// 			}
// 			else
// 			{
// 				bodyPos = partWorld[i].Position;
// 				bodyRot = partWorld[i].Rotation;
// 				kind = 0;
// 				pars = new[] { 2.0f / scale, 0f, 0f, DebugShapes ? 0f : 1f };
// 				volume = ( 4.0f / 3.0f ) * MathF.PI * pars[0] * pars[0] * pars[0];
// 			}
//
// 			_shapes.Add( new GpuVoxelRagdolls.PartShape( kind, pars[0], pars[1], pars[2], part.BoneName ) );
//
// 			if ( padPhys > 0f )
// 			{
// 				pars[0] += padPhys;
// 				if ( kind == 2 )
// 				{
// 					pars[1] += padPhys;
// 					pars[2] += padPhys;
// 				}
// 			}
//
// 			float density = part.Mass > 0.01f && volume > 1e-6f ? part.Mass / volume : 1.0f;
//
// 			bodyWorld[i] = new Transform( bodyPos, bodyRot );
//
// 			uint filter = (uint)( ( ( ragdollIndex & 0xFFFF ) << 16 ) | ( ( i & 0xFF ) << 8 ) | ( parentOf[i] & 0xFF ) );
//
// 			var physPos = new Vector3( bodyPos.x, bodyPos.z, bodyPos.y ) / scale;
// 			var physVel = new Vector3( worldVelocity.x, worldVelocity.z, worldVelocity.y ) / scale;
//
// 			_rain.WriteRagdollBody( _bodyStart + i, kind, pars, physPos, QuatToPhys( bodyRot ), physVel, density, filter,
// 				false, MathF.Max( part.LinearDamping, LinearDamping ), MathF.Max( part.AngularDamping, AngularDamping ) );
//
// 			_parts.Add( new PartMap
// 			{
// 				BodySlot = _bodyStart + i,
// 				Bone = bone,
// 				BodyToBone = InvMul( bodyWorld[i], boneWorld ),
// 			} );
// 		}
//
// 		int jointsWritten = 0;
// 		foreach ( var joint in joints )
// 		{
// 			int b1 = joint.Body1;
// 			int b2 = joint.Body2;
// 			if ( b1 < 0 || b1 >= count || b2 < 0 || b2 >= count )
// 				continue;
//
// 			var frame1World = Mul( partWorld[b1], joint.Frame1 );
// 			var frame2World = Mul( partWorld[b2], joint.Frame2 );
//
// 			var lA = bodyWorld[b1].Rotation.Inverse * ( frame1World.Position - bodyWorld[b1].Position );
// 			var lB = bodyWorld[b2].Rotation.Inverse * ( frame2World.Position - bodyWorld[b2].Position );
//
// 			var fA = bodyWorld[b1].Rotation.Inverse * ( frame1World.Rotation * FrameFix );
// 			var fB = bodyWorld[b2].Rotation.Inverse * ( frame2World.Rotation * FrameFix );
//
// 			float swingRad = 0f;
// 			float twistMin = 0f;
// 			float twistMax = 0f;
// 			float twistEnable = 0f;
//
// 			if ( joint.Type == PhysicsGroupDescription.JointType.Fixed || joint.Fixed )
// 			{
// 				swingRad = 0.03f;
// 				twistMin = -0.03f;
// 				twistMax = 0.03f;
// 				twistEnable = 1f;
// 			}
// 			else if ( joint.Type == PhysicsGroupDescription.JointType.Hinge )
// 			{
// 				swingRad = 0.03f;
// 				if ( JointLimits && joint.EnableTwistLimit )
// 				{
// 					twistMin = joint.TwistMin * MathF.PI / 180.0f;
// 					twistMax = joint.TwistMax * MathF.PI / 180.0f;
// 					twistEnable = 1f;
// 				}
// 			}
// 			else if ( JointLimits )
// 			{
// 				if ( joint.EnableSwingLimit )
// 					swingRad = MathF.Max( MathF.Abs( joint.SwingMin ), MathF.Abs( joint.SwingMax ) ) * MathF.PI / 180.0f;
//
// 				if ( joint.EnableTwistLimit )
// 				{
// 					twistMin = joint.TwistMin * MathF.PI / 180.0f;
// 					twistMax = joint.TwistMax * MathF.PI / 180.0f;
// 					twistEnable = 1f;
// 				}
// 			}
//
// 			var qA = QuatToPhys( fA );
// 			var qB = QuatToPhys( fB );
//
// 			var j = new float[32];
// 			j[0] = _bodyStart + b1;
// 			j[1] = _bodyStart + b2;
// 			j[2] = lA.x / scale;
// 			j[3] = lA.z / scale;
// 			j[4] = lA.y / scale;
// 			j[5] = lB.x / scale;
// 			j[6] = lB.z / scale;
// 			j[7] = lB.y / scale;
// 			j[8] = qA.x;
// 			j[9] = qA.y;
// 			j[10] = qA.z;
// 			j[11] = qA.w;
// 			j[12] = qB.x;
// 			j[13] = qB.y;
// 			j[14] = qB.z;
// 			j[15] = qB.w;
// 			j[16] = swingRad;
// 			j[17] = twistMin;
// 			j[18] = twistMax;
// 			j[19] = twistEnable;
// 			j[20] = 1f;
//
// 			_rain.WriteJoint( jointStart + jointsWritten, j );
// 			jointsWritten++;
// 		}
//
// 		_rain.CommitRagdoll( ragdollIndex, jointStart, jointsWritten );
//
// 		_jointStart = jointStart;
// 		_jointCount = jointsWritten;
// 		_breakArmed = false;
// 		_sinceSpawn = 0;
//
// 		if ( _voxelMode )
// 		{
// 			_voxelRag.Attach( model.Name, _bodyStart, _shapes );
// 			Renderer.Enabled = false;
// 		}
//
// 		var modelPhysics = GetComponent<ModelPhysics>();
// 		if ( modelPhysics.IsValid() )
// 			modelPhysics.Enabled = false;
//
// 		_poseData = new float[count * Stride];
// 		_active = true;
//
// 		Log.Info( $"[B3Ragdoll] {model.Name}: {count} bodies, {jointsWritten} joints on GPU (ragdoll {ragdollIndex})" );
// 	}
//
// 	protected override void OnStart()
// 	{
// 		if ( RagdollOnStart )
// 			Ragdoll( LaunchVelocity );
// 	}
//
// 	protected override void OnUpdate()
// 	{
// 		if ( !_active || _rain?.BodiesBuffer is null )
// 			return;
//
// 		if ( !_breakArmed && _sinceSpawn > 0.5f )
// 		{
// 			_breakArmed = true;
// 			_rain.ArmRagdollJoints( _jointStart, _jointCount );
// 		}
//
// 		if ( _voxelMode )
// 			return;
//
// 		_rain.CopyRagdollPoses( _bodyStart, _partCount, _poseData );
//
// 		float scale = _rain.RenderScale;
//
// 		for ( int i = 0; i < _parts.Count; i++ )
// 		{
// 			var part = _parts[i];
// 			if ( part.Bone is null )
// 				continue;
//
// 			int o = i * Stride;
// 			var pos = new Vector3( _poseData[o + 53], _poseData[o + 55], _poseData[o + 54] ) * scale;
// 			var rot = QuatToWorld( _poseData[o + 3], _poseData[o + 4], _poseData[o + 5], _poseData[o + 6] );
//
// 			if ( rot.x == 0f && rot.y == 0f && rot.z == 0f && rot.w == 0f )
// 				continue;
//
// 			var boneWorld = Mul( new Transform( pos, rot ), part.BodyToBone );
// 			Renderer.SetBoneTransform( part.Bone, InvMul( Renderer.WorldTransform, boneWorld ) );
// 		}
// 	}
//
// 	protected override void OnDisabled()
// 	{
// 		_active = false;
// 	}
// }
