HEADER
{
    Description = "Box3D GPU port. Modes 0-2: integration (velocities with Pade damping and gyroscopic Newton-Raphson, positions with speed clamps, per-step finalize). Modes 3-6: broadphase (fat AABB build, uniform grid clear, grid rasterize, candidate pair generation with home-cell dedupe). Mode 7: narrowphase (sphere-sphere, sphere-hull GJK, hull-hull SAT with clipping and reduction). Modes 8-11: Soft Step contact solver driven by CPU color lists. Modes 12-21: zero-readback pipeline (indirect args, GPU greedy coloring with atomic claim and rollback, warm-start persistence via GPU hash table, per-color-slot indirect solve).";
    DevShader = true;
    Version = 16;
}

MODES
{
    Default();
}

CS
{
    #include "system.fxc"

    RWStructuredBuffer<float> Bodies < Attribute( "B3Bodies" ); >;
    RWStructuredBuffer<float> Aabbs < Attribute( "B3Aabbs" ); >;
    RWStructuredBuffer<uint> GridCount < Attribute( "B3GridCount" ); >;
    RWStructuredBuffer<uint> GridCells < Attribute( "B3GridCells" ); >;
    RWStructuredBuffer<uint> Pairs < Attribute( "B3Pairs" ); >;
    RWStructuredBuffer<uint> BpCounters < Attribute( "B3BpCounters" ); >;
    RWStructuredBuffer<float> Manifolds < Attribute( "B3Manifolds" ); >;
    RWStructuredBuffer<float> Constraints < Attribute( "B3Constraints" ); >;
    RWStructuredBuffer<uint> ColorList < Attribute( "B3ColorList" ); >;
    RWStructuredBuffer<float> WarmImpulses < Attribute( "B3WarmImpulses" ); >;
    RWStructuredBuffer<uint> BodyColorMask < Attribute( "B3BodyColorMask" ); >;
    RWStructuredBuffer<uint> HashKeys < Attribute( "B3HashKeys" ); >;
    RWStructuredBuffer<float> HashData < Attribute( "B3HashData" ); >;

    RWStructuredBuffer<float> TriData < Attribute( "B3TriData" ); >;
    int TriCount < Attribute( "B3TriCount" ); Default( 0 ); >;
    int MeshBody < Attribute( "B3MeshBody" ); Default( 1 ); >;

    #define TRI_FLAG 0x80000000u

    struct RenderInstance
    {
        float4 Row0;
        float4 Row1;
        float4 Row2;
        float Alpha;
        uint Tint;
        uint VertexCacheOffset;
        uint BlendWeightCount;
    };

    RWStructuredBuffer<RenderInstance> RenderInstances < Attribute( "B3RenderInstances" ); >;
    RWStructuredBuffer<RenderInstance> RenderInstancesBox < Attribute( "B3RenderInstancesBox" ); >;
    float3 BoxRenderScale < Attribute( "B3BoxRenderScale" ); >;
    int BodyCount < Attribute( "B3BodyCount" ); Default( 0 ); >;
    int Mode < Attribute( "B3Mode" ); Default( 0 ); >;
    float3 Gravity < Attribute( "B3Gravity" ); >;
    float H < Attribute( "B3H" ); Default( 0.0 ); >;
    float MaxLinearSpeed < Attribute( "B3MaxLinearSpeed" ); Default( 400.0 ); >;
    float MaxAngularSpeed < Attribute( "B3MaxAngularSpeed" ); Default( 0.0 ); >;
    float3 GridOrigin < Attribute( "B3GridOrigin" ); >;
    float CellSize < Attribute( "B3CellSize" ); Default( 1.0 ); >;
    int GridX < Attribute( "B3GridX" ); Default( 1 ); >;
    int GridY < Attribute( "B3GridY" ); Default( 1 ); >;
    int GridZ < Attribute( "B3GridZ" ); Default( 1 ); >;
    int CellCap < Attribute( "B3CellCap" ); Default( 64 ); >;
    int PairCap < Attribute( "B3PairCap" ); Default( 0 ); >;
    float AabbInflate < Attribute( "B3AabbInflate" ); Default( 0.0 ); >;
    int UseBias < Attribute( "B3UseBias" ); Default( 0 ); >;
    int ColorOffset < Attribute( "B3ColorOffset" ); Default( 0 ); >;
    int ColorCount < Attribute( "B3ColorCount" ); Default( 0 ); >;
    int ColorSlot < Attribute( "B3ColorSlot" ); Default( 0 ); >;
    int SegCap < Attribute( "B3SegCap" ); Default( 0 ); >;
    int HashCap < Attribute( "B3HashCap" ); Default( 0 ); >;
    float RenderScale < Attribute( "B3RenderScale" ); Default( 39.37 ); >;
    float RenderModelScale < Attribute( "B3RenderModelScale" ); Default( 1.0 ); >;
    int RenderFirstBody < Attribute( "B3RenderFirstBody" ); Default( 5 ); >;
    int RenderCount < Attribute( "B3RenderCount" ); Default( 0 ); >;
    int LargeCount < Attribute( "B3LargeCount" ); Default( 0 ); >;
    int Large0 < Attribute( "B3Large0" ); Default( -1 ); >;
    int Large1 < Attribute( "B3Large1" ); Default( -1 ); >;
    int Large2 < Attribute( "B3Large2" ); Default( -1 ); >;
    int Large3 < Attribute( "B3Large3" ); Default( -1 ); >;

    bool IsLargeBody( uint b )
    {
        if ( LargeCount > 0 && (int)b == Large0 )
            return true;
        if ( LargeCount > 1 && (int)b == Large1 )
            return true;
        if ( LargeCount > 2 && (int)b == Large2 )
            return true;
        if ( LargeCount > 3 && (int)b == Large3 )
            return true;
        return false;
    }
    int SleepEnable < Attribute( "B3SleepEnable" ); Default( 0 ); >;
    int SleepSteps < Attribute( "B3SleepSteps" ); Default( 30 ); >;
    float SleepVel < Attribute( "B3SleepVel" ); Default( 0.08 ); >;
    float3 WakeLo < Attribute( "B3WakeLo" ); >;
    float3 WakeHi < Attribute( "B3WakeHi" ); >;

    bool IsAsleep( uint b )
    {
        return SleepEnable != 0 && Bodies[b * 64 + 63] >= (float)SleepSteps;
    }

    float StepDt < Attribute( "B3StepDt" ); Default( 0.01666667 ); >;
    float InvH < Attribute( "B3InvH" ); Default( 0.0 ); >;
    float ContactSpeed < Attribute( "B3ContactSpeed" ); Default( 3.0 ); >;
    float RestitutionThreshold < Attribute( "B3RestitutionThreshold" ); Default( 1.0 ); >;
    float3 ContactSoft < Attribute( "B3ContactSoft" ); >;
    float3 JointSoft < Attribute( "B3JointSoft" ); >;
    float JointBreak < Attribute( "B3JointBreak" ); Default( 0.0 ); >;
    int MaxRagdolls < Attribute( "B3MaxRagdolls" ); Default( 0 ); >;
    int RagdollFirst < Attribute( "B3RagdollFirst" ); Default( 0 ); >;
    int RagdollBodies < Attribute( "B3RagdollBodies" ); Default( 0 ); >;
    int DebrisFirst < Attribute( "B3DebrisFirst" ); Default( 0 ); >;
    int DebrisBodies < Attribute( "B3DebrisBodies" ); Default( 0 ); >;
    int RewindSlot < Attribute( "B3RewindSlot" ); Default( 0 ); >;
    int RewindSlotB < Attribute( "B3RewindSlotB" ); Default( 0 ); >;
    float RewindLerp < Attribute( "B3RewindLerp" ); Default( 0.0 ); >;
    int RewindFirst < Attribute( "B3RewindFirst" ); Default( 0 ); >;
    int RewindCount < Attribute( "B3RewindCount" ); Default( 0 ); >;
    int VoxOn < Attribute( "B3VoxOn" ); Default( 0 ); >;
    float3 VoxOrigin < Attribute( "B3VoxOrigin" ); >;
    float VoxSize < Attribute( "B3VoxSize" ); Default( 0.17 ); >;
    int VoxDimX < Attribute( "B3VoxDimX" ); Default( 1 ); >;
    int VoxDimY < Attribute( "B3VoxDimY" ); Default( 1 ); >;
    int VoxDimZ < Attribute( "B3VoxDimZ" ); Default( 1 ); >;
    float3 StaticSoft < Attribute( "B3StaticSoft" ); >;
    int PortalOn < Attribute( "B3PortalOn" ); Default( 0 ); >;
    float3 PortalAPos < Attribute( "B3PortalAPos" ); >;
    float3 PortalAN < Attribute( "B3PortalAN" ); >;
    float3 PortalAR < Attribute( "B3PortalAR" ); >;
    float3 PortalAU < Attribute( "B3PortalAU" ); >;
    float3 PortalBPos < Attribute( "B3PortalBPos" ); >;
    float3 PortalBN < Attribute( "B3PortalBN" ); >;
    float3 PortalBR < Attribute( "B3PortalBR" ); >;
    float3 PortalBU < Attribute( "B3PortalBU" ); >;
    float3 PortalHalf < Attribute( "B3PortalHalf" ); >;

    #define B3_STRIDE 64
    #define B3_FLT_MIN 1.17549435e-38
    #define CON_STRIDE 96
    #define WARM_STRIDE 12

    struct M3
    {
        float3 cx;
        float3 cy;
        float3 cz;
    };

    float3 LoadV3( uint b, uint o )
    {
        uint k = b * B3_STRIDE + o;
        return float3( Bodies[k], Bodies[k + 1], Bodies[k + 2] );
    }

    void StoreV3( uint b, uint o, float3 v )
    {
        uint k = b * B3_STRIDE + o;
        Bodies[k] = v.x;
        Bodies[k + 1] = v.y;
        Bodies[k + 2] = v.z;
    }

    float4 LoadQ( uint b, uint o )
    {
        uint k = b * B3_STRIDE + o;
        return float4( Bodies[k], Bodies[k + 1], Bodies[k + 2], Bodies[k + 3] );
    }

    void StoreQ( uint b, uint o, float4 q )
    {
        uint k = b * B3_STRIDE + o;
        Bodies[k] = q.x;
        Bodies[k + 1] = q.y;
        Bodies[k + 2] = q.z;
        Bodies[k + 3] = q.w;
    }

    M3 LoadM3( uint b, uint o )
    {
        M3 m;
        m.cx = LoadV3( b, o );
        m.cy = LoadV3( b, o + 3 );
        m.cz = LoadV3( b, o + 6 );
        return m;
    }

    void StoreM3( uint b, uint o, M3 m )
    {
        StoreV3( b, o, m.cx );
        StoreV3( b, o + 3, m.cy );
        StoreV3( b, o + 6, m.cz );
    }

    float3 B3RotateVector( float4 q, float3 v )
    {
        float3 t1 = cross( q.xyz, v );
        float3 t2 = t1 + q.w * v;
        float3 t3 = cross( q.xyz, t2 );
        return v + 2.0 * t3;
    }

    float3 B3InvRotateVector( float4 q, float3 v )
    {
        float3 t1 = cross( q.xyz, v );
        float3 t2 = t1 - q.w * v;
        float3 t3 = cross( q.xyz, t2 );
        return v + 2.0 * t3;
    }

    float4 B3MulQuat( float4 q1, float4 q2 )
    {
        float3 t1 = cross( q1.xyz, q2.xyz );
        float3 t2 = t1 + q1.w * q2.xyz;
        float3 t3 = t2 + q2.w * q1.xyz;
        return float4( t3, q1.w * q2.w - dot( q1.xyz, q2.xyz ) );
    }

    float4 B3InvMulQuat( float4 q1, float4 q2 )
    {
        float3 t1 = cross( q2.xyz, q1.xyz );
        float3 t2 = t1 + q1.w * q2.xyz;
        float3 t3 = t2 - q2.w * q1.xyz;
        return float4( t3, q1.w * q2.w + dot( q1.xyz, q2.xyz ) );
    }

    float4 B3NormalizeQuat( float4 q )
    {
        float lengthSq = dot( q, q );
        if ( lengthSq > 1000.0 * B3_FLT_MIN )
        {
            float s = 1.0 / sqrt( lengthSq );
            return s * q;
        }

        return float4( 0.0, 0.0, 0.0, 1.0 );
    }

    float4 B3IntegrateRotation( float4 q1, float3 dr )
    {
        float4 qd = float4( 0.5 * dr, 0.0 );
        qd = B3MulQuat( qd, q1 );
        return B3NormalizeQuat( q1 + qd );
    }

    float B3Det( M3 m )
    {
        return dot( m.cx, cross( m.cy, m.cz ) );
    }

    float3 B3MulMV( M3 m, float3 a )
    {
        return m.cx * a.x + m.cy * a.y + m.cz * a.z;
    }

    M3 B3MulMM( M3 a, M3 b )
    {
        M3 o;
        o.cx = B3MulMV( a, b.cx );
        o.cy = B3MulMV( a, b.cy );
        o.cz = B3MulMV( a, b.cz );
        return o;
    }

    M3 B3Transpose( M3 m )
    {
        M3 o;
        o.cx = float3( m.cx.x, m.cy.x, m.cz.x );
        o.cy = float3( m.cx.y, m.cy.y, m.cz.y );
        o.cz = float3( m.cx.z, m.cy.z, m.cz.z );
        return o;
    }

    M3 B3InvertMatrix( M3 m )
    {
        M3 o;
        o.cx = float3( 0.0, 0.0, 0.0 );
        o.cy = float3( 0.0, 0.0, 0.0 );
        o.cz = float3( 0.0, 0.0, 0.0 );

        float det = B3Det( m );
        if ( abs( det ) > 1000.0 * B3_FLT_MIN )
        {
            float invDet = 1.0 / det;
            o.cx = invDet * cross( m.cy, m.cz );
            o.cy = invDet * cross( m.cz, m.cx );
            o.cz = invDet * cross( m.cx, m.cy );
            o = B3Transpose( o );
        }

        return o;
    }

    float3 B3Solve3( M3 m, float3 a )
    {
        float det = B3Det( m );
        if ( abs( det ) > 1000.0 * B3_FLT_MIN )
        {
            float invDet = 1.0 / det;
            float3 sx = cross( m.cy, m.cz );
            float3 sy = cross( m.cz, m.cx );
            float3 sz = cross( m.cx, m.cy );
            return float3( invDet * dot( sx, a ), invDet * dot( sy, a ), invDet * dot( sz, a ) );
        }

        return float3( 0.0, 0.0, 0.0 );
    }

    M3 B3MakeMatrixFromQuat( float4 q )
    {
        float xx = q.x * q.x;
        float yy = q.y * q.y;
        float zz = q.z * q.z;
        float xy = q.x * q.y;
        float xz = q.x * q.z;
        float xw = q.x * q.w;
        float yz = q.y * q.z;
        float yw = q.y * q.w;
        float zw = q.z * q.w;

        M3 m;
        m.cx = float3( 1.0 - 2.0 * ( yy + zz ), 2.0 * ( xy + zw ), 2.0 * ( xz - yw ) );
        m.cy = float3( 2.0 * ( xy - zw ), 1.0 - 2.0 * ( xx + zz ), 2.0 * ( yz + xw ) );
        m.cz = float3( 2.0 * ( xz + yw ), 2.0 * ( yz - xw ), 1.0 - 2.0 * ( xx + yy ) );
        return m;
    }

    void IntegrateVelocities( uint b )
    {
        float3 v = LoadV3( b, 7 );
        float3 w = LoadV3( b, 10 );

        float invMass = Bodies[b * B3_STRIDE + 20];
        float gravityScaleRaw = Bodies[b * B3_STRIDE + 21];
        float linDampCoef = Bodies[b * B3_STRIDE + 22];
        float angDampCoef = Bodies[b * B3_STRIDE + 23];

        float linearDamping = 1.0 / ( 1.0 + H * linDampCoef );
        float angularDamping = 1.0 / ( 1.0 + H * angDampCoef );
        float gravityScale = invMass > 0.0 ? gravityScaleRaw : 0.0;

        float3 force = LoadV3( b, 45 );
        float3 torque = LoadV3( b, 48 );
        M3 invInertiaWorld = LoadM3( b, 36 );

        float3 linearVelocityDelta = ( H * invMass ) * force + ( H * gravityScale ) * Gravity;
        v = linearVelocityDelta + linearDamping * v;

        float3 angularVelocityDelta = H * B3MulMV( invInertiaWorld, torque );
        w = angularVelocityDelta + angularDamping * w;

        float4 q0 = LoadQ( b, 3 );
        float4 q = B3MulQuat( LoadQ( b, 16 ), q0 );

        M3 inertiaLocal = B3InvertMatrix( LoadM3( b, 27 ) );

        float3 omega1 = B3InvRotateVector( q, w );
        float3 omega2 = omega1;

        float i00 = inertiaLocal.cx.x;
        float i01 = inertiaLocal.cy.x;
        float i02 = inertiaLocal.cz.x;
        float i11 = inertiaLocal.cy.y;
        float i12 = inertiaLocal.cz.y;
        float i22 = inertiaLocal.cz.z;

        [loop]
        for ( int gyroIteration = 0; gyroIteration < 1; ++gyroIteration )
        {
            float w1 = omega2.x;
            float w2 = omega2.y;
            float w3 = omega2.z;

            float Iw1 = i00 * w1 + i01 * w2 + i02 * w3;
            float Iw2 = i01 * w1 + i11 * w2 + i12 * w3;
            float Iw3 = i02 * w1 + i12 * w2 + i22 * w3;

            float3 dw = omega2 - omega1;
            float3 bres = float3(
                i00 * dw.x + i01 * dw.y + i02 * dw.z + H * ( w2 * Iw3 - w3 * Iw2 ),
                i01 * dw.x + i11 * dw.y + i12 * dw.z + H * ( w3 * Iw1 - w1 * Iw3 ),
                i02 * dw.x + i12 * dw.y + i22 * dw.z + H * ( w1 * Iw2 - w2 * Iw1 ) );

            M3 J;
            J.cx = float3( i00 + H * ( w2 * i02 - w3 * i01 ), i01 + H * ( w3 * i00 - w1 * i02 - Iw3 ),
                i02 + H * ( w1 * i01 - w2 * i00 + Iw2 ) );
            J.cy = float3( i01 + H * ( w2 * i12 - w3 * i11 + Iw3 ), i11 + H * ( w3 * i01 - w1 * i12 ),
                i12 + H * ( w1 * i11 - w2 * i01 - Iw1 ) );
            J.cz = float3( i02 + H * ( w2 * i22 - w3 * i12 - Iw2 ), i12 + H * ( w3 * i02 - w1 * i22 + Iw1 ),
                i22 + H * ( w1 * i12 - w2 * i02 ) );

            omega2 = omega2 - B3Solve3( J, bres );
        }

        w = B3RotateVector( q, omega2 );

        StoreV3( b, 7, v );
        StoreV3( b, 10, w );
    }

    void IntegratePositions( uint b )
    {
        float3 v = LoadV3( b, 7 );
        float3 w = LoadV3( b, 10 );

        float maxLinearSpeedSquared = MaxLinearSpeed * MaxLinearSpeed;
        float maxAngularSpeedSquared = MaxAngularSpeed * MaxAngularSpeed;

        if ( dot( v, v ) > maxLinearSpeedSquared )
        {
            float ratio = MaxLinearSpeed / sqrt( dot( v, v ) );
            v = ratio * v;
        }

        if ( dot( w, w ) > maxAngularSpeedSquared )
        {
            float ratio = MaxAngularSpeed / sqrt( dot( w, w ) );
            w = ratio * w;
        }

        StoreV3( b, 7, v );
        StoreV3( b, 10, w );
        StoreV3( b, 13, LoadV3( b, 13 ) + H * v );
        StoreQ( b, 16, B3IntegrateRotation( LoadQ( b, 16 ), H * w ) );
    }

    void FinalizeBody( uint b )
    {
        float3 center = LoadV3( b, 0 );
        float4 q = LoadQ( b, 3 );
        float3 deltaPos = LoadV3( b, 13 );
        float4 deltaRot = LoadQ( b, 16 );
        float3 localCenter = LoadV3( b, 24 );

        center = center + deltaPos;
        q = B3NormalizeQuat( B3MulQuat( deltaRot, q ) );

        StoreV3( b, 0, center );
        StoreQ( b, 3, q );
        StoreV3( b, 13, float3( 0.0, 0.0, 0.0 ) );
        StoreQ( b, 16, float4( 0.0, 0.0, 0.0, 1.0 ) );
        StoreV3( b, 53, center - B3RotateVector( q, localCenter ) );
        StoreV3( b, 45, float3( 0.0, 0.0, 0.0 ) );
        StoreV3( b, 48, float3( 0.0, 0.0, 0.0 ) );

        M3 rotationMatrix = B3MakeMatrixFromQuat( q );
        StoreM3( b, 36, B3MulMM( B3MulMM( rotationMatrix, LoadM3( b, 27 ) ), B3Transpose( rotationMatrix ) ) );
    }

    int3 CellOf( float3 v )
    {
        int3 g = (int3)floor( ( v - GridOrigin ) / CellSize );
        return clamp( g, int3( 0, 0, 0 ), int3( GridX - 1, GridY - 1, GridZ - 1 ) );
    }

    uint CellIndex( int3 g )
    {
        return (uint)( g.x + g.y * GridX + g.z * GridX * GridY );
    }

    void BuildAabb( uint b )
    {
        if ( Bodies[b * B3_STRIDE + 52] < 0.0 )
            return;

        float3 p = LoadV3( b, 53 );
        float4 q = LoadQ( b, 3 );
        float shapeKind = Bodies[b * B3_STRIDE + 56];

        float3 extent;
        if ( shapeKind == 2.0 )
        {
            float hx = Bodies[b * B3_STRIDE + 57];
            float hy = Bodies[b * B3_STRIDE + 58];
            float hz = Bodies[b * B3_STRIDE + 59];
            M3 r = B3MakeMatrixFromQuat( q );
            extent = float3(
                abs( r.cx.x ) * hx + abs( r.cy.x ) * hy + abs( r.cz.x ) * hz,
                abs( r.cx.y ) * hx + abs( r.cy.y ) * hy + abs( r.cz.y ) * hz,
                abs( r.cx.z ) * hx + abs( r.cy.z ) * hy + abs( r.cz.z ) * hz );
        }
        else if ( shapeKind == 1.0 )
        {
            float cr = Bodies[b * B3_STRIDE + 57];
            float ch = Bodies[b * B3_STRIDE + 58];
            float3 axis = B3RotateVector( q, float3( 0.0, ch, 0.0 ) );
            extent = abs( axis ) + cr;
        }
        else
        {
            float radius = Bodies[b * B3_STRIDE + 57];
            extent = float3( radius, radius, radius );
        }

        extent += AabbInflate;

        float3 lo = p - extent;
        float3 hi = p + extent;
        if ( Bodies[b * B3_STRIDE + 52] == 2.0 )
        {
            float3 travel = LoadV3( b, 7 ) * StepDt;
            lo += min( travel, float3( 0.0, 0.0, 0.0 ) );
            hi += max( travel, float3( 0.0, 0.0, 0.0 ) );
        }

        uint k = b * 6;
        Aabbs[k] = lo.x;
        Aabbs[k + 1] = lo.y;
        Aabbs[k + 2] = lo.z;
        Aabbs[k + 3] = hi.x;
        Aabbs[k + 4] = hi.y;
        Aabbs[k + 5] = hi.z;
    }

    void RasterizeBody( uint b )
    {
        if ( Bodies[b * B3_STRIDE + 52] < 0.0 )
            return;

        if ( IsLargeBody( b ) )
            return;

        uint k = b * 6;
        float3 lo = float3( Aabbs[k], Aabbs[k + 1], Aabbs[k + 2] );
        float3 hi = float3( Aabbs[k + 3], Aabbs[k + 4], Aabbs[k + 5] );

        int3 c0 = CellOf( lo );
        int3 c1 = CellOf( hi );

        [loop]
        for ( int z = c0.z; z <= c1.z; ++z )
        {
            [loop]
            for ( int y = c0.y; y <= c1.y; ++y )
            {
                [loop]
                for ( int x = c0.x; x <= c1.x; ++x )
                {
                    uint cell = CellIndex( int3( x, y, z ) );
                    uint slot;
                    InterlockedAdd( GridCount[cell], 1u, slot );
                    if ( slot < (uint)CellCap )
                        GridCells[cell * (uint)CellCap + slot] = b;
                    else
                        InterlockedOr( BpCounters[1], 1u );
                }
            }
        }
    }

    void GeneratePairs( uint i )
    {
        if ( Bodies[i * B3_STRIDE + 52] < 0.0 )
            return;

        if ( IsLargeBody( i ) )
            return;

        uint ki = i * 6;
        float3 loI = float3( Aabbs[ki], Aabbs[ki + 1], Aabbs[ki + 2] );
        float3 hiI = float3( Aabbs[ki + 3], Aabbs[ki + 4], Aabbs[ki + 5] );
        float typeI = Bodies[i * B3_STRIDE + 52];
        bool sleepI = IsAsleep( i );

        [loop]
        for ( int lk = 0; lk < LargeCount; ++lk )
        {
            if ( sleepI )
                break;

            int L = lk == 0 ? Large0 : ( lk == 1 ? Large1 : ( lk == 2 ? Large2 : Large3 ) );
            if ( L < 0 || (uint)L == i )
                continue;

            float typeL = Bodies[(uint)L * B3_STRIDE + 52];
            if ( typeI != 2.0 && typeL != 2.0 )
                continue;

            uint kl = (uint)L * 6;
            float3 loL = float3( Aabbs[kl], Aabbs[kl + 1], Aabbs[kl + 2] );
            float3 hiL = float3( Aabbs[kl + 3], Aabbs[kl + 4], Aabbs[kl + 5] );

            if ( loI.x > hiL.x || loL.x > hiI.x || loI.y > hiL.y || loL.y > hiI.y || loI.z > hiL.z || loL.z > hiI.z )
                continue;

            uint slotL;
            InterlockedAdd( BpCounters[0], 1u, slotL );
            if ( slotL < (uint)PairCap )
            {
                Pairs[slotL * 2] = min( (uint)L, i );
                Pairs[slotL * 2 + 1] = max( (uint)L, i );
            }
            else
            {
                InterlockedOr( BpCounters[2], 1u );
            }
        }

        int3 c0 = CellOf( loI );
        int3 c1 = CellOf( hiI );

        bool sawTriangle = false;

        [loop]
        for ( int z = c0.z; z <= c1.z; ++z )
        {
            [loop]
            for ( int y = c0.y; y <= c1.y; ++y )
            {
                [loop]
                for ( int x = c0.x; x <= c1.x; ++x )
                {
                    uint cell = CellIndex( int3( x, y, z ) );
                    uint n = min( GridCount[cell], (uint)CellCap );

                    [loop]
                    for ( uint s = 0; s < n; ++s )
                    {
                        uint j = GridCells[cell * (uint)CellCap + s];
                        if ( ( j & TRI_FLAG ) != 0 )
                        {
                            sawTriangle = true;
                            continue;
                        }

                        if ( j <= i )
                            continue;

                        float typeJ = Bodies[j * B3_STRIDE + 52];
                        if ( typeI != 2.0 && typeJ != 2.0 )
                            continue;

                        if ( sleepI && IsAsleep( j ) )
                            continue;

                        uint fi = ColorList[i];
                        uint fj = ColorList[j];
                        if ( fi != 0xFFFFFFFFu && fj != 0xFFFFFFFFu && ( fi >> 16 ) == ( fj >> 16 ) )
                            continue;

                        uint kj = j * 6;
                        float3 loJ = float3( Aabbs[kj], Aabbs[kj + 1], Aabbs[kj + 2] );
                        float3 hiJ = float3( Aabbs[kj + 3], Aabbs[kj + 4], Aabbs[kj + 5] );

                        if ( loI.x > hiJ.x || loJ.x > hiI.x || loI.y > hiJ.y || loJ.y > hiI.y || loI.z > hiJ.z || loJ.z > hiI.z )
                            continue;

                        int3 home = CellOf( max( loI, loJ ) );
                        if ( home.x != x || home.y != y || home.z != z )
                            continue;

                        uint slot;
                        InterlockedAdd( BpCounters[0], 1u, slot );
                        if ( slot < (uint)PairCap )
                        {
                            Pairs[slot * 2] = i;
                            Pairs[slot * 2 + 1] = j;
                        }
                        else
                        {
                            InterlockedOr( BpCounters[2], 1u );
                        }
                    }
                }
            }
        }

        if ( ( ( sawTriangle && TriCount > 0 ) || VoxOn != 0 ) && typeI == 2.0 && !sleepI && (int)i != MeshBody && (int)i != MeshBody + 1 )
        {
            int mbCount = VoxOn != 0 ? 1 : 2;

            [loop]
            for ( int mb = 0; mb < mbCount; ++mb )
            {
                uint meshIdx = (uint)( MeshBody + mb );
                uint slotM;
                InterlockedAdd( BpCounters[0], 1u, slotM );
                if ( slotM < (uint)PairCap )
                {
                    Pairs[slotM * 2] = min( meshIdx, i );
                    Pairs[slotM * 2 + 1] = max( meshIdx, i );
                }
                else
                {
                    InterlockedOr( BpCounters[2], 1u );
                }
            }
        }
    }

    #define MANIFOLD_STRIDE 24
    #define B3_FLT_EPSILON 1.19209290e-7

    float3 B3NormalizeV( float3 a )
    {
        float lengthSquared = a.x * a.x + a.y * a.y + a.z * a.z;
        if ( lengthSquared > 1000.0 * B3_FLT_MIN )
        {
            float s = 1.0 / sqrt( lengthSquared );
            return float3( s * a.x, s * a.y, s * a.z );
        }

        return float3( 0.0, 0.0, 0.0 );
    }

    float B3Stp( float3 a, float3 b, float3 c )
    {
        return dot( a, cross( b, c ) );
    }

    float3 B3BaryEdge( float3 a, float3 b )
    {
        float3 ab = b - a;
        return float3( dot( b, ab ), -dot( a, ab ), dot( ab, ab ) );
    }

    float4 B3BaryTri( float3 a, float3 b, float3 c )
    {
        float3 ab = b - a;
        float3 ac = c - a;
        float3 bXC = cross( b, c );
        float3 cXA = cross( c, a );
        float3 aXB = cross( a, b );
        float3 abXAc = cross( ab, ac );
        return float4( dot( bXC, abXAc ), dot( cXA, abXAc ), dot( aXB, abXAc ), dot( abXAc, abXAc ) );
    }

    struct SxV
    {
        float3 wA;
        float3 wB;
        float3 w;
        float a;
        int indexA;
        int indexB;
    };

    struct Sx
    {
        SxV v[4];
        int count;
    };

    bool B3SolveSimplex2( inout Sx s )
    {
        float3 a = s.v[0].w;
        float3 b = s.v[1].w;
        float3 ab = b - a;
        float divisor = dot( ab, ab );
        float u = dot( b, ab );
        float v = -dot( a, ab );

        if ( v <= 0.0 )
        {
            s.count = 1;
            s.v[0].a = 1.0;
            return true;
        }

        if ( u <= 0.0 )
        {
            s.count = 1;
            s.v[0] = s.v[1];
            s.v[0].a = 1.0;
            return true;
        }

        if ( divisor <= 0.0 )
            return false;

        float denominator = 1.0 / divisor;
        s.v[0].a = denominator * u;
        s.v[1].a = denominator * v;
        return true;
    }

    bool B3SolveSimplex3( inout Sx s )
    {
        SxV v1 = s.v[0];
        SxV v2 = s.v[1];
        SxV v3 = s.v[2];

        float3 wAB = B3BaryEdge( v1.w, v2.w );
        float3 wBC = B3BaryEdge( v2.w, v3.w );
        float3 wCA = B3BaryEdge( v3.w, v1.w );

        if ( wAB.y <= 0.0 && wCA.x <= 0.0 )
        {
            s.count = 1;
            s.v[0] = v1;
            s.v[0].a = 1.0;
            return true;
        }

        if ( wBC.y <= 0.0 && wAB.x <= 0.0 )
        {
            s.count = 1;
            s.v[0] = v2;
            s.v[0].a = 1.0;
            return true;
        }

        if ( wCA.y <= 0.0 && wBC.x <= 0.0 )
        {
            s.count = 1;
            s.v[0] = v3;
            s.v[0].a = 1.0;
            return true;
        }

        float4 wABC = B3BaryTri( v1.w, v2.w, v3.w );

        if ( wABC.z <= 0.0 && wAB.x > 0.0 && wAB.y > 0.0 )
        {
            s.count = 2;
            s.v[0] = v1;
            s.v[1] = v2;

            float divisor = wAB.z;
            if ( divisor <= 0.0 )
                return false;

            s.v[0].a = wAB.x / divisor;
            s.v[1].a = wAB.y / divisor;
            return true;
        }

        if ( wABC.x <= 0.0 && wBC.x > 0.0 && wBC.y > 0.0 )
        {
            s.count = 2;
            s.v[0] = v2;
            s.v[1] = v3;

            float divisor = wBC.z;
            if ( divisor <= 0.0 )
                return false;

            s.v[0].a = wBC.x / divisor;
            s.v[1].a = wBC.y / divisor;
            return true;
        }

        if ( wABC.y <= 0.0 && wCA.x > 0.0 && wCA.y > 0.0 )
        {
            s.count = 2;
            s.v[0] = v3;
            s.v[1] = v1;

            float divisor = wCA.z;
            if ( divisor <= 0.0 )
                return false;

            s.v[0].a = wCA.x / divisor;
            s.v[1].a = wCA.y / divisor;
            return true;
        }

        float divisor = wABC.w;
        if ( divisor <= 0.0 )
            return false;

        s.v[0].a = wABC.x / divisor;
        s.v[1].a = wABC.y / divisor;
        s.v[2].a = wABC.z / divisor;
        return true;
    }

    bool B3SolveSimplex4( inout Sx s )
    {
        SxV vertexA = s.v[0];
        SxV vertexB = s.v[1];
        SxV vertexC = s.v[2];
        SxV vertexD = s.v[3];

        float3 wAB = B3BaryEdge( vertexA.w, vertexB.w );
        float3 wAC = B3BaryEdge( vertexA.w, vertexC.w );
        float3 wAD = B3BaryEdge( vertexA.w, vertexD.w );
        float3 wBC = B3BaryEdge( vertexB.w, vertexC.w );
        float3 wCD = B3BaryEdge( vertexC.w, vertexD.w );
        float3 wDB = B3BaryEdge( vertexD.w, vertexB.w );

        if ( wAB.y <= 0.0 && wAC.y <= 0.0 && wAD.y <= 0.0 )
        {
            s.count = 1;
            s.v[0] = vertexA;
            s.v[0].a = 1.0;
            return true;
        }

        if ( wAB.x <= 0.0 && wDB.x <= 0.0 && wBC.y <= 0.0 )
        {
            s.count = 1;
            s.v[0] = vertexB;
            s.v[0].a = 1.0;
            return true;
        }

        if ( wAC.x <= 0.0 && wBC.x <= 0.0 && wCD.y <= 0.0 )
        {
            s.count = 1;
            s.v[0] = vertexC;
            s.v[0].a = 1.0;
            return true;
        }

        if ( wAD.x <= 0.0 && wCD.x <= 0.0 && wDB.y <= 0.0 )
        {
            s.count = 1;
            s.v[0] = vertexD;
            s.v[0].a = 1.0;
            return true;
        }

        float4 wACB = B3BaryTri( vertexA.w, vertexC.w, vertexB.w );
        float4 wABD = B3BaryTri( vertexA.w, vertexB.w, vertexD.w );
        float4 wADC = B3BaryTri( vertexA.w, vertexD.w, vertexC.w );
        float4 wBCD = B3BaryTri( vertexB.w, vertexC.w, vertexD.w );

        if ( wABD.z <= 0.0 && wACB.y <= 0.0 && wAB.x > 0.0 && wAB.y > 0.0 )
        {
            s.count = 2;
            s.v[0] = vertexA;
            s.v[1] = vertexB;

            float divisor = wAB.z;
            if ( divisor <= 0.0 )
                return false;

            s.v[0].a = wAB.x / divisor;
            s.v[1].a = wAB.y / divisor;
            return true;
        }

        if ( wACB.z <= 0.0 && wADC.y <= 0.0 && wAC.x > 0.0 && wAC.y > 0.0 )
        {
            s.count = 2;
            s.v[0] = vertexA;
            s.v[1] = vertexC;

            float divisor = wAC.z;
            if ( divisor <= 0.0 )
                return false;

            s.v[0].a = wAC.x / divisor;
            s.v[1].a = wAC.y / divisor;
            return true;
        }

        if ( wADC.z <= 0.0 && wABD.y <= 0.0 && wAD.x > 0.0 && wAD.y > 0.0 )
        {
            s.count = 2;
            s.v[0] = vertexA;
            s.v[1] = vertexD;

            float divisor = wAD.z;
            if ( divisor <= 0.0 )
                return false;

            s.v[0].a = wAD.x / divisor;
            s.v[1].a = wAD.y / divisor;
            return true;
        }

        if ( wACB.x <= 0.0 && wBCD.z <= 0.0 && wBC.x > 0.0 && wBC.y > 0.0 )
        {
            s.count = 2;
            s.v[0] = vertexB;
            s.v[1] = vertexC;

            float divisor = wBC.z;
            if ( divisor <= 0.0 )
                return false;

            s.v[0].a = wBC.x / divisor;
            s.v[1].a = wBC.y / divisor;
            return true;
        }

        if ( wADC.x <= 0.0 && wBCD.x <= 0.0 && wCD.x > 0.0 && wCD.y > 0.0 )
        {
            s.count = 2;
            s.v[0] = vertexC;
            s.v[1] = vertexD;

            float divisor = wCD.z;
            if ( divisor <= 0.0 )
                return false;

            s.v[0].a = wCD.x / divisor;
            s.v[1].a = wCD.y / divisor;
            return true;
        }

        if ( wABD.x <= 0.0 && wBCD.y <= 0.0 && wDB.x > 0.0 && wDB.y > 0.0 )
        {
            s.count = 2;
            s.v[0] = vertexD;
            s.v[1] = vertexB;

            float divisor = wDB.z;
            if ( divisor <= 0.0 )
                return false;

            s.v[0].a = wDB.x / divisor;
            s.v[1].a = wDB.y / divisor;
            return true;
        }

        float wABCD[5];
        {
            float3 a = vertexA.w;
            float3 b = vertexB.w;
            float3 c = vertexC.w;
            float3 d = vertexD.w;
            float3 ab = b - a;
            float3 ac = c - a;
            float3 ad = d - a;
            float divisor = B3Stp( ab, ac, ad );
            float sgn = divisor < 0.0 ? -1.0 : 1.0;
            wABCD[0] = sgn * B3Stp( b, c, d );
            wABCD[1] = sgn * B3Stp( a, d, c );
            wABCD[2] = sgn * B3Stp( a, b, d );
            wABCD[3] = sgn * B3Stp( a, c, b );
            wABCD[4] = sgn * divisor;
        }

        if ( wABCD[3] < 0.0 && wACB.x > 0.0 && wACB.y > 0.0 && wACB.z > 0.0 )
        {
            s.count = 3;
            s.v[0] = vertexA;
            s.v[1] = vertexC;
            s.v[2] = vertexB;

            float divisor = wACB.w;
            if ( divisor <= 0.0 )
                return false;

            s.v[0].a = wACB.x / divisor;
            s.v[1].a = wACB.y / divisor;
            s.v[2].a = wACB.z / divisor;
            return true;
        }

        if ( wABCD[2] < 0.0 && wABD.x > 0.0 && wABD.y > 0.0 && wABD.z > 0.0 )
        {
            s.count = 3;
            s.v[0] = vertexA;
            s.v[1] = vertexB;
            s.v[2] = vertexD;

            float divisor = wABD.w;
            if ( divisor <= 0.0 )
                return false;

            s.v[0].a = wABD.x / divisor;
            s.v[1].a = wABD.y / divisor;
            s.v[2].a = wABD.z / divisor;
            return true;
        }

        if ( wABCD[1] < 0.0 && wADC.x > 0.0 && wADC.y > 0.0 && wADC.z > 0.0 )
        {
            s.count = 3;
            s.v[0] = vertexA;
            s.v[1] = vertexD;
            s.v[2] = vertexC;

            float divisor = wADC.w;
            if ( divisor <= 0.0 )
                return false;

            s.v[0].a = wADC.x / divisor;
            s.v[1].a = wADC.y / divisor;
            s.v[2].a = wADC.z / divisor;
            return true;
        }

        if ( wABCD[0] < 0.0 && wBCD.x > 0.0 && wBCD.y > 0.0 && wBCD.z > 0.0 )
        {
            s.count = 3;
            s.v[0] = vertexB;
            s.v[1] = vertexC;
            s.v[2] = vertexD;

            float divisor = wBCD.w;
            if ( divisor <= 0.0 )
                return false;

            s.v[0].a = wBCD.x / divisor;
            s.v[1].a = wBCD.y / divisor;
            s.v[2].a = wBCD.z / divisor;
            return true;
        }

        float divisor4 = wABCD[4];
        if ( divisor4 <= 0.0 )
            return false;

        s.v[0].a = wABCD[0] / divisor4;
        s.v[1].a = wABCD[1] / divisor4;
        s.v[2].a = wABCD[2] / divisor4;
        s.v[3].a = wABCD[3] / divisor4;
        return true;
    }

    float3 B3Blend2V( float s, float3 a, float t, float3 b )
    {
        return float3( s * a.x + t * b.x, s * a.y + t * b.y, s * a.z + t * b.z );
    }

    float3 B3Blend3V( float s, float3 a, float t, float3 b, float u, float3 c )
    {
        return float3( s * a.x + t * b.x + u * c.x, s * a.y + t * b.y + u * c.y, s * a.z + t * b.z + u * c.z );
    }

    void B3Witness( Sx s, out float3 pointA, out float3 pointB )
    {
        if ( s.count == 1 )
        {
            pointA = s.v[0].wA;
            pointB = s.v[0].wB;
        }
        else if ( s.count == 2 )
        {
            pointA = B3Blend2V( s.v[0].a, s.v[0].wA, s.v[1].a, s.v[1].wA );
            pointB = B3Blend2V( s.v[0].a, s.v[0].wB, s.v[1].a, s.v[1].wB );
        }
        else if ( s.count == 3 )
        {
            pointA = B3Blend3V( s.v[0].a, s.v[0].wA, s.v[1].a, s.v[1].wA, s.v[2].a, s.v[2].wA );
            pointB = B3Blend3V( s.v[0].a, s.v[0].wB, s.v[1].a, s.v[1].wB, s.v[2].a, s.v[2].wB );
        }
        else
        {
            float3 sum = B3Blend2V( s.v[0].a, s.v[0].wA, s.v[1].a, s.v[1].wA ) +
                B3Blend2V( s.v[2].a, s.v[2].wA, s.v[3].a, s.v[3].wA );
            pointA = sum;
            pointB = sum;
        }
    }

    int BoxSupport( float3 hv[8], float3 axis )
    {
        float3 origin = hv[0];
        int maxIndex = 0;
        float maxProjection = 0.0;

        [loop]
        for ( int index = 1; index < 8; ++index )
        {
            float projection = dot( axis, hv[index] - origin );
            if ( projection > maxProjection )
            {
                maxIndex = index;
                maxProjection = projection;
            }
        }

        return maxIndex;
    }

    void B3PointHullGjk( float3 hv[8], float3 center, out float3 pointA, out float3 pointB, out float dist )
    {
        Sx s;
        s.count = 1;
        s.v[0].indexA = 0;
        s.v[0].indexB = 0;
        s.v[0].wA = hv[0];
        s.v[0].wB = center;
        s.v[0].w = center - hv[0];
        s.v[0].a = 0.0;
        s.v[1] = s.v[0];
        s.v[2] = s.v[0];
        s.v[3] = s.v[0];

        Sx backup = s;
        float distanceSq = 3.402823466e38;
        float3 normal = float3( 0.0, 0.0, 0.0 );

        pointA = float3( 0.0, 0.0, 0.0 );
        pointB = float3( 0.0, 0.0, 0.0 );
        dist = 0.0;

        [loop]
        for ( int iteration = 0; iteration < 32; ++iteration )
        {
            bool solved = false;
            if ( s.count == 1 )
            {
                s.v[0].a = 1.0;
                solved = true;
            }
            else if ( s.count == 2 )
                solved = B3SolveSimplex2( s );
            else if ( s.count == 3 )
                solved = B3SolveSimplex3( s );
            else
                solved = B3SolveSimplex4( s );

            if ( !solved )
            {
                s = backup;
                break;
            }

            if ( s.count == 4 )
            {
                B3Witness( s, pointA, pointB );
                return;
            }

            float oldDistanceSq = distanceSq;
            float3 closestPoint = float3( 0.0, 0.0, 0.0 );
            if ( s.count == 1 )
                closestPoint = s.v[0].w;
            else if ( s.count == 2 )
                closestPoint = B3Blend2V( s.v[0].a, s.v[0].w, s.v[1].a, s.v[1].w );
            else
                closestPoint = B3Blend3V( s.v[0].a, s.v[0].w, s.v[1].a, s.v[1].w, s.v[2].a, s.v[2].w );

            distanceSq = dot( closestPoint, closestPoint );
            if ( distanceSq >= oldDistanceSq )
            {
                s = backup;
                break;
            }

            float3 searchDirection = float3( 0.0, 0.0, 0.0 );
            if ( s.count == 1 )
            {
                searchDirection = -s.v[0].w;
            }
            else if ( s.count == 2 )
            {
                float3 ab = s.v[1].w - s.v[0].w;
                searchDirection = cross( cross( ab, -s.v[0].w ), ab );
            }
            else
            {
                float3 ab = s.v[1].w - s.v[0].w;
                float3 ac = s.v[2].w - s.v[0].w;
                float3 n = cross( ab, ac );
                searchDirection = dot( n, s.v[0].w ) < 0.0 ? n : -n;
            }

            if ( dot( searchDirection, searchDirection ) < 1000.0 * B3_FLT_MIN )
            {
                B3Witness( s, pointA, pointB );
                return;
            }

            normal = -searchDirection;

            int indexA = BoxSupport( hv, -searchDirection );
            float3 supportA = hv[indexA];
            int indexB = 0;
            float3 supportB = center;

            backup = s;

            bool duplicate = false;
            [loop]
            for ( int i = 0; i < s.count; ++i )
            {
                if ( s.v[i].indexA == indexA && s.v[i].indexB == indexB )
                {
                    duplicate = true;
                    break;
                }
            }

            if ( duplicate )
                break;

            s.v[s.count].indexA = indexA;
            s.v[s.count].indexB = indexB;
            s.v[s.count].wA = supportA;
            s.v[s.count].wB = supportB;
            s.v[s.count].w = supportB - supportA;
            s.count += 1;
        }

        normal = B3NormalizeV( normal );
        if ( !( abs( 1.0 - dot( normal, normal ) ) < 100.0 * B3_FLT_EPSILON ) )
        {
            pointA = float3( 0.0, 0.0, 0.0 );
            pointB = float3( 0.0, 0.0, 0.0 );
            dist = 0.0;
            return;
        }

        B3Witness( s, pointA, pointB );
        float3 d = pointB - pointA;
        dist = sqrt( dot( d, d ) );
    }

    void WriteManifold1( uint mo, float3 normalLocal, float3 pointLocal, float separation, float4 qA )
    {
        M3 matrixA = B3MakeMatrixFromQuat( qA );
        float3 normalWorld = B3MulMV( matrixA, normalLocal );
        float3 anchorA = B3MulMV( matrixA, pointLocal );

        Manifolds[mo] = normalWorld.x;
        Manifolds[mo + 1] = normalWorld.y;
        Manifolds[mo + 2] = normalWorld.z;
        Manifolds[mo + 3] = 1.0;
        Manifolds[mo + 4] = anchorA.x;
        Manifolds[mo + 5] = anchorA.y;
        Manifolds[mo + 6] = anchorA.z;
        Manifolds[mo + 7] = separation;
        Manifolds[mo + 8] = 0.0;
    }

    void CollideSphereSphere( uint mo, uint a, uint b )
    {
        float3 pA = LoadV3( a, 53 );
        float4 qA = LoadQ( a, 3 );
        float3 pB = LoadV3( b, 53 );
        float4 qB = LoadQ( b, 3 );
        float radiusA = Bodies[a * B3_STRIDE + 57];
        float radiusB = Bodies[b * B3_STRIDE + 57];

        float3 btp = B3InvRotateVector( qA, pB - pA );

        float3 center1 = float3( 0.0, 0.0, 0.0 );
        float3 center2 = btp;

        float totalRadius = radiusA + radiusB;
        float3 offset = center2 - center1;
        float distanceSq = dot( offset, offset );

        if ( distanceSq > totalRadius * totalRadius )
        {
            Manifolds[mo + 3] = 0.0;
            return;
        }

        float3 normal = float3( 0.0, 1.0, 0.0 );
        float distance = sqrt( distanceSq );
        if ( distance * distance > 1000.0 * B3_FLT_MIN )
            normal = ( 1.0 / distance ) * offset;

        float3 point = 0.5 * ( ( ( center1 + radiusA * normal ) + center2 ) - radiusB * normal );
        float separation = distance - totalRadius;

        WriteManifold1( mo, normal, point, separation, qA );
    }

    void CollideHullSphere( uint mo, uint a, uint b )
    {
        float3 pA = LoadV3( a, 53 );
        float4 qA = LoadQ( a, 3 );
        float3 pB = LoadV3( b, 53 );
        float4 qB = LoadQ( b, 3 );
        float radiusB = Bodies[b * B3_STRIDE + 57];

        float minH = 0.2 * 0.005;
        float3 h = max( float3( minH, minH, minH ), float3( Bodies[a * B3_STRIDE + 57], Bodies[a * B3_STRIDE + 58], Bodies[a * B3_STRIDE + 59] ) );

        float3 hv[8];
        hv[0] = float3( h.x, h.y, h.z );
        hv[1] = float3( -h.x, h.y, h.z );
        hv[2] = float3( -h.x, -h.y, h.z );
        hv[3] = float3( h.x, -h.y, h.z );
        hv[4] = float3( h.x, h.y, -h.z );
        hv[5] = float3( -h.x, h.y, -h.z );
        hv[6] = float3( -h.x, -h.y, -h.z );
        hv[7] = float3( h.x, -h.y, -h.z );

        float3 btp = B3InvRotateVector( qA, pB - pA );
        float3 center = btp;

        float3 pointA;
        float3 pointB;
        float dist;
        B3PointHullGjk( hv, center, pointA, pointB, dist );

        float speculativeDistance = 4.0 * 0.005;
        float radius = 0.0 + radiusB;

        if ( dist > radius + speculativeDistance )
        {
            Manifolds[mo + 3] = 0.0;
            return;
        }

        if ( dist > 100.0 * B3_FLT_EPSILON )
        {
            float3 normal = B3NormalizeV( pointB - pointA );
            float3 cA = center + ( 0.0 - dot( center - pointA, normal ) ) * normal;
            float3 cB = center - radiusB * normal;
            float3 point = float3(
                0.5 * cA.x + 0.5 * cB.x,
                0.5 * cA.y + 0.5 * cB.y,
                0.5 * cA.z + 0.5 * cB.z );
            float separation = dist - radius;
            WriteManifold1( mo, normal, point, separation, qA );
        }
        else
        {
            float3 pn[6];
            float po[6];
            pn[0] = float3( -1.0, 0.0, 0.0 );
            po[0] = h.x;
            pn[1] = float3( 1.0, 0.0, 0.0 );
            po[1] = h.x;
            pn[2] = float3( 0.0, -1.0, 0.0 );
            po[2] = h.y;
            pn[3] = float3( 0.0, 1.0, 0.0 );
            po[3] = h.y;
            pn[4] = float3( 0.0, 0.0, -1.0 );
            po[4] = h.z;
            pn[5] = float3( 0.0, 0.0, 1.0 );
            po[5] = h.z;

            int bestIndex = -1;
            float bestDistance = -3.402823466e38;

            [loop]
            for ( int index = 0; index < 6; ++index )
            {
                float distance = dot( pn[index], center ) - po[index];
                if ( distance > bestDistance )
                {
                    bestIndex = index;
                    bestDistance = distance;
                }
            }

            float3 normal = pn[bestIndex];
            float3 cA = center + ( 0.0 - dot( center - pointA, normal ) ) * normal;
            float3 cB = center - radiusB * normal;
            float3 point = float3(
                0.5 * cA.x + 0.5 * cB.x,
                0.5 * cA.y + 0.5 * cB.y,
                0.5 * cA.z + 0.5 * cB.z );
            float separation = bestDistance - radius;
            WriteManifold1( mo, normal, point, separation, qA );
        }
    }

    void SegSegClosest( float3 p1, float3 q1, float3 p2, float3 q2, out float3 c1, out float3 c2 )
    {
        float3 d1 = q1 - p1;
        float3 d2 = q2 - p2;
        float3 r = p1 - p2;
        float a = dot( d1, d1 );
        float e = dot( d2, d2 );
        float f = dot( d2, r );
        float s = 0.0;
        float t = 0.0;

        if ( a <= 1e-9 && e <= 1e-9 )
        {
            c1 = p1;
            c2 = p2;
            return;
        }

        if ( a <= 1e-9 )
        {
            t = clamp( f / e, 0.0, 1.0 );
        }
        else
        {
            float c = dot( d1, r );
            if ( e <= 1e-9 )
            {
                s = clamp( -c / a, 0.0, 1.0 );
            }
            else
            {
                float b = dot( d1, d2 );
                float denom = a * e - b * b;
                if ( denom > 1e-9 )
                    s = clamp( ( b * f - c * e ) / denom, 0.0, 1.0 );

                t = ( b * s + f ) / e;
                if ( t < 0.0 )
                {
                    t = 0.0;
                    s = clamp( -c / a, 0.0, 1.0 );
                }
                else if ( t > 1.0 )
                {
                    t = 1.0;
                    s = clamp( ( b - c ) / a, 0.0, 1.0 );
                }
            }
        }

        c1 = p1 + d1 * s;
        c2 = p2 + d2 * t;
    }

    void CollideCapsuleSphere( uint mo, uint a, uint b )
    {
        float3 pA = LoadV3( a, 53 );
        float4 qA = LoadQ( a, 3 );
        float3 pB = LoadV3( b, 53 );
        float radiusA = Bodies[a * B3_STRIDE + 57];
        float hA = Bodies[a * B3_STRIDE + 58];
        float radiusB = Bodies[b * B3_STRIDE + 57];

        float3 btp = B3InvRotateVector( qA, pB - pA );
        float3 onSeg = float3( 0.0, clamp( btp.y, -hA, hA ), 0.0 );

        float totalRadius = radiusA + radiusB;
        float spec = 4.0 * 0.005;
        float3 offset = btp - onSeg;
        float distanceSq = dot( offset, offset );

        if ( distanceSq > ( totalRadius + spec ) * ( totalRadius + spec ) )
        {
            Manifolds[mo + 3] = 0.0;
            return;
        }

        float3 normal = float3( 1.0, 0.0, 0.0 );
        float distance = sqrt( distanceSq );
        if ( distance * distance > 1000.0 * B3_FLT_MIN )
            normal = ( 1.0 / distance ) * offset;

        float3 point = 0.5 * ( ( onSeg + radiusA * normal ) + ( btp - radiusB * normal ) );
        WriteManifold1( mo, normal, point, distance - totalRadius, qA );
    }

    void CollideCapsuleCapsule( uint mo, uint a, uint b )
    {
        float3 pA = LoadV3( a, 53 );
        float4 qA = LoadQ( a, 3 );
        float3 pB = LoadV3( b, 53 );
        float4 qB = LoadQ( b, 3 );
        float radiusA = Bodies[a * B3_STRIDE + 57];
        float hA = Bodies[a * B3_STRIDE + 58];
        float radiusB = Bodies[b * B3_STRIDE + 57];
        float hB = Bodies[b * B3_STRIDE + 58];

        float3 btp = B3InvRotateVector( qA, pB - pA );
        float4 btq = B3InvMulQuat( qA, qB );
        float3 axisB = B3RotateVector( btq, float3( 0.0, hB, 0.0 ) );

        float3 c1;
        float3 c2;
        SegSegClosest( float3( 0.0, -hA, 0.0 ), float3( 0.0, hA, 0.0 ), btp - axisB, btp + axisB, c1, c2 );

        float totalRadius = radiusA + radiusB;
        float spec = 4.0 * 0.005;
        float3 offset = c2 - c1;
        float distanceSq = dot( offset, offset );

        if ( distanceSq > ( totalRadius + spec ) * ( totalRadius + spec ) )
        {
            Manifolds[mo + 3] = 0.0;
            return;
        }

        float3 normal = float3( 1.0, 0.0, 0.0 );
        float distance = sqrt( distanceSq );
        if ( distance * distance > 1000.0 * B3_FLT_MIN )
            normal = ( 1.0 / distance ) * offset;

        float3 point = 0.5 * ( ( c1 + radiusA * normal ) + ( c2 - radiusB * normal ) );
        WriteManifold1( mo, normal, point, distance - totalRadius, qA );
    }

    void CollideHullCapsule( uint mo, uint a, uint b )
    {
        float3 pA = LoadV3( a, 53 );
        float4 qA = LoadQ( a, 3 );
        float3 pB = LoadV3( b, 53 );
        float4 qB = LoadQ( b, 3 );
        float radiusB = Bodies[b * B3_STRIDE + 57];
        float hB = Bodies[b * B3_STRIDE + 58];

        float minH = 0.2 * 0.005;
        float3 h = max( float3( minH, minH, minH ), float3( Bodies[a * B3_STRIDE + 57], Bodies[a * B3_STRIDE + 58], Bodies[a * B3_STRIDE + 59] ) );

        float3 hv[8];
        hv[0] = float3( h.x, h.y, h.z );
        hv[1] = float3( -h.x, h.y, h.z );
        hv[2] = float3( -h.x, -h.y, h.z );
        hv[3] = float3( h.x, -h.y, h.z );
        hv[4] = float3( h.x, h.y, -h.z );
        hv[5] = float3( -h.x, h.y, -h.z );
        hv[6] = float3( -h.x, -h.y, -h.z );
        hv[7] = float3( h.x, -h.y, -h.z );

        float3 btp = B3InvRotateVector( qA, pB - pA );
        float4 btq = B3InvMulQuat( qA, qB );
        float3 axisB = B3RotateVector( btq, float3( 0.0, hB, 0.0 ) );

        float spec = 4.0 * 0.005;

        float3 samples[3];
        samples[0] = btp - axisB;
        samples[1] = btp;
        samples[2] = btp + axisB;

        float3 nrm[3];
        float3 pts[3];
        float seps[3];
        bool ok[3];

        [loop]
        for ( int k = 0; k < 3; ++k )
        {
            ok[k] = false;
            float3 center = samples[k];

            float3 pointA;
            float3 pointB;
            float dist;
            B3PointHullGjk( hv, center, pointA, pointB, dist );

            if ( dist > radiusB + spec )
                continue;

            float3 normal;
            if ( dist > 100.0 * B3_FLT_EPSILON )
            {
                normal = B3NormalizeV( pointB - pointA );
                float3 cA = center + ( 0.0 - dot( center - pointA, normal ) ) * normal;
                float3 cB = center - radiusB * normal;
                pts[k] = 0.5 * ( cA + cB );
                seps[k] = dist - radiusB;
                nrm[k] = normal;
                ok[k] = true;
            }
            else
            {
                float3 pn[6];
                float po[6];
                pn[0] = float3( -1.0, 0.0, 0.0 );
                po[0] = h.x;
                pn[1] = float3( 1.0, 0.0, 0.0 );
                po[1] = h.x;
                pn[2] = float3( 0.0, -1.0, 0.0 );
                po[2] = h.y;
                pn[3] = float3( 0.0, 1.0, 0.0 );
                po[3] = h.y;
                pn[4] = float3( 0.0, 0.0, -1.0 );
                po[4] = h.z;
                pn[5] = float3( 0.0, 0.0, 1.0 );
                po[5] = h.z;

                int bestIndex = 0;
                float bestDistance = -3.402823466e38;

                [loop]
                for ( int f = 0; f < 6; ++f )
                {
                    float d2 = dot( pn[f], center ) - po[f];
                    if ( d2 > bestDistance )
                    {
                        bestIndex = f;
                        bestDistance = d2;
                    }
                }

                normal = pn[bestIndex];
                float3 cB = center - radiusB * normal;
                pts[k] = cB;
                seps[k] = bestDistance - radiusB;
                nrm[k] = normal;
                ok[k] = true;
            }
        }

        int deep = -1;
        float deepSep = 1e9;

        [unroll]
        for ( int k1 = 0; k1 < 3; ++k1 )
        {
            if ( ok[k1] && seps[k1] < deepSep )
            {
                deepSep = seps[k1];
                deep = k1;
            }
        }

        if ( deep < 0 )
        {
            Manifolds[mo + 3] = 0.0;
            return;
        }

        M3 matrixA = B3MakeMatrixFromQuat( qA );
        float3 normalWorld = B3MulMV( matrixA, nrm[deep] );

        Manifolds[mo] = normalWorld.x;
        Manifolds[mo + 1] = normalWorld.y;
        Manifolds[mo + 2] = normalWorld.z;

        int count = 0;

        [loop]
        for ( int k2 = 0; k2 < 3 && count < 2; ++k2 )
        {
            if ( !ok[k2] )
                continue;
            if ( k2 != deep && dot( nrm[k2], nrm[deep] ) < 0.9 )
                continue;
            if ( k2 == 1 && deep != 1 )
                continue;

            float3 anchor = B3MulMV( matrixA, pts[k2] );
            uint ko = mo + 4 + (uint)count * 5;
            Manifolds[ko] = anchor.x;
            Manifolds[ko + 1] = anchor.y;
            Manifolds[ko + 2] = anchor.z;
            Manifolds[ko + 3] = seps[k2];
            Manifolds[ko + 4] = asfloat( (uint)k2 );
            count++;
        }

        Manifolds[mo + 3] = (float)count;
    }

    static const uint4 BOX_EDGES[24] =
    {
        uint4( 2, 1, 2, 0 ), uint4( 17, 0, 1, 5 ), uint4( 4, 3, 1, 0 ), uint4( 20, 2, 5, 3 ),
        uint4( 6, 5, 5, 0 ), uint4( 23, 4, 6, 4 ), uint4( 0, 7, 6, 0 ), uint4( 18, 6, 2, 2 ),
        uint4( 10, 9, 0, 1 ), uint4( 21, 8, 3, 5 ), uint4( 12, 11, 3, 1 ), uint4( 16, 10, 7, 2 ),
        uint4( 14, 13, 7, 1 ), uint4( 19, 12, 4, 4 ), uint4( 8, 15, 4, 1 ), uint4( 22, 14, 0, 3 ),
        uint4( 7, 17, 3, 2 ), uint4( 9, 16, 2, 5 ), uint4( 11, 19, 6, 2 ), uint4( 5, 18, 7, 4 ),
        uint4( 15, 21, 1, 3 ), uint4( 1, 20, 0, 5 ), uint4( 3, 23, 4, 3 ), uint4( 13, 22, 5, 4 )
    };

    static const uint BOX_VERT_EDGE[8] = { 8, 1, 0, 9, 13, 3, 5, 11 };
    static const uint BOX_FACE_EDGE[6] = { 0, 8, 16, 20, 19, 21 };

    struct HullBox
    {
        float3 v[8];
        float3 pn[6];
        float po[6];
    };

    HullBox MakeHullBox( uint b )
    {
        float minH = 0.2 * 0.005;
        float3 h = max( float3( minH, minH, minH ), float3( Bodies[b * B3_STRIDE + 57], Bodies[b * B3_STRIDE + 58], Bodies[b * B3_STRIDE + 59] ) );

        HullBox hb;
        hb.v[0] = float3( h.x, h.y, h.z );
        hb.v[1] = float3( -h.x, h.y, h.z );
        hb.v[2] = float3( -h.x, -h.y, h.z );
        hb.v[3] = float3( h.x, -h.y, h.z );
        hb.v[4] = float3( h.x, h.y, -h.z );
        hb.v[5] = float3( -h.x, h.y, -h.z );
        hb.v[6] = float3( -h.x, -h.y, -h.z );
        hb.v[7] = float3( h.x, -h.y, -h.z );
        hb.pn[0] = float3( -1.0, 0.0, 0.0 );
        hb.po[0] = h.x;
        hb.pn[1] = float3( 1.0, 0.0, 0.0 );
        hb.po[1] = h.x;
        hb.pn[2] = float3( 0.0, -1.0, 0.0 );
        hb.po[2] = h.y;
        hb.pn[3] = float3( 0.0, 1.0, 0.0 );
        hb.po[3] = h.y;
        hb.pn[4] = float3( 0.0, 0.0, -1.0 );
        hb.po[4] = h.z;
        hb.pn[5] = float3( 0.0, 0.0, 1.0 );
        hb.po[5] = h.z;
        return hb;
    }

    float3 HullExtents( HullBox hb )
    {
        return hb.v[0];
    }

    struct CV
    {
        float3 position;
        float separation;
        uint pair;
    };

    struct LMPoint
    {
        float3 point;
        float separation;
        uint pair;
    };

    struct LManifold
    {
        float3 normal;
        int count;
        LMPoint pts[4];
    };

    uint FlipPairU( uint p )
    {
        uint o1 = ( p >> 24 ) & 255u;
        uint i1 = ( p >> 16 ) & 255u;
        uint o2 = ( p >> 8 ) & 255u;
        uint i2 = p & 255u;
        return ( ( 1u - o2 ) << 24 ) | ( i2 << 16 ) | ( ( 1u - o1 ) << 8 ) | i1;
    }

    int HullSupportBiased( HullBox hb, float3 direction, float bias, out float support )
    {
        int minIndex = 0;
        float minValue = 1.0e9;

        [loop]
        for ( int i = 0; i < 8; ++i )
        {
            float d = direction.z * hb.v[i].z + ( direction.y * hb.v[i].y + direction.x * hb.v[i].x );
            float value = bias - d;
            if ( value < minValue )
            {
                minIndex = i;
                minValue = value;
            }
        }

        support = direction.x * hb.v[minIndex].x + direction.y * hb.v[minIndex].y + direction.z * hb.v[minIndex].z;
        return minIndex;
    }

    int FindIncidentFace( HullBox inc, float3 refNormal, int vertexIndex )
    {
        uint startEdge = BOX_VERT_EDGE[vertexIndex];
        int minEdgeIndex = 0;
        float minEdgeProjection = 3.402823466e38;
        float3 edgeOrigin = inc.v[vertexIndex];

        uint e = startEdge;

        [loop]
        for ( int guard = 0; guard < 8; ++guard )
        {
            uint twinIdx = BOX_EDGES[e].y;
            float3 twinOrigin = inc.v[BOX_EDGES[twinIdx].z];
            float3 axis = B3NormalizeV( twinOrigin - edgeOrigin );
            float edgeProjection = abs( dot( axis, refNormal ) );
            if ( edgeProjection < minEdgeProjection )
            {
                minEdgeIndex = (int)e;
                minEdgeProjection = edgeProjection;
            }

            e = BOX_EDGES[twinIdx].x;
            if ( e == startEdge )
                break;
        }

        uint f1 = BOX_EDGES[minEdgeIndex].w;
        uint f2 = BOX_EDGES[BOX_EDGES[minEdgeIndex].y].w;
        return dot( inc.pn[f1], refNormal ) < dot( inc.pn[f2], refNormal ) ? (int)f1 : (int)f2;
    }

    void LineDistance( float3 p1, float3 d1, float3 p2, float3 d2, out float3 point1, out float3 point2, out float fraction1, out float fraction2 )
    {
        float a11 = dot( d1, d1 );
        float a12 = -dot( d1, d2 );
        float a21 = dot( d2, d1 );
        float a22 = -dot( d2, d2 );

        float3 w = p1 - p2;
        float b1 = -dot( d1, w );
        float b2 = -dot( d2, w );

        float det = a11 * a22 - a12 * a21;
        if ( det * det < 1000.0 * B3_FLT_MIN )
        {
            float s1p = dot( p2 - p1, d1 ) / dot( d1, d1 );
            point1 = p1 + s1p * d1;
            fraction1 = s1p;
            point2 = p2;
            fraction2 = 0.0;
            return;
        }

        float s1 = ( a22 * b1 - a12 * b2 ) / det;
        float s2 = ( a11 * b2 - a21 * b1 ) / det;
        point1 = p1 + s1 * d1;
        fraction1 = s1;
        point2 = p2 + s2 * d2;
        fraction2 = s2;
    }

    float3 B3ArbitraryPerp( float3 v )
    {
        float3 p;
        if ( v.x < -0.5 || 0.5 < v.x )
        {
            float a = 0.67;
            float b = -0.42;
            p = float3( a * v.y + b * v.z, -a * v.x, -b * v.x );
        }
        else if ( v.y < -0.5 || 0.5 < v.y )
        {
            float a = 0.67;
            float c = -0.42;
            p = float3( a * v.y, -a * v.x + c * v.z, -c * v.y );
        }
        else
        {
            float a = 0.67;
            float b = -0.42;
            p = float3( a * v.z, b * v.z, -a * v.x - b * v.y );
        }

        return B3NormalizeV( p );
    }

    void ReduceManifold( inout LManifold m, inout LMPoint points[16], int count )
    {
        if ( count <= 4 )
        {
            [loop]
            for ( int i = 0; i < count; ++i )
                m.pts[i] = points[i];
            m.count = count;
            return;
        }

        float3 normal = m.normal;
        float speculativeDistance = 4.0 * 0.005;
        float tolSqr = speculativeDistance * speculativeDistance;
        float bias = 0.95;

        int bestIndex = -1;
        float bestScore = -3.402823466e38;

        float3 searchDirection = B3ArbitraryPerp( normal );

        [loop]
        for ( int i0 = 0; i0 < count; ++i0 )
        {
            if ( points[i0].separation > speculativeDistance )
                continue;

            float score = -points[i0].separation + dot( searchDirection, points[i0].point );
            if ( bias * score > bestScore )
            {
                bestIndex = i0;
                bestScore = score;
            }
        }

        if ( bestIndex < 0 )
        {
            m.count = 0;
            return;
        }

        m.pts[0] = points[bestIndex];
        m.count = 1;
        points[bestIndex] = points[count - 1];
        count -= 1;

        float3 a = m.pts[0].point;

        bestScore = 0.0;
        bestIndex = -1;

        [loop]
        for ( int i1 = 0; i1 < count; ++i1 )
        {
            float3 d = points[i1].point - a;
            float3 vv = d - dot( d, normal ) * normal;
            float distanceSquared = dot( vv, vv );
            float separation = max( 0.0, -points[i1].separation );
            float score = distanceSquared + 4.0 * separation * separation;
            if ( bias * score > bestScore )
            {
                bestScore = score;
                bestIndex = i1;
            }
        }

        if ( bestScore < tolSqr )
            return;

        m.pts[1] = points[bestIndex];
        m.count = 2;
        points[bestIndex] = points[count - 1];
        count -= 1;

        float3 b = m.pts[1].point;

        bestScore = tolSqr;
        bestIndex = -1;
        float bestSignedArea = 0.0;
        float3 ba = b - a;

        [loop]
        for ( int i2 = 0; i2 < count; ++i2 )
        {
            float signedArea = dot( normal, cross( ba, points[i2].point - a ) );
            float score = abs( signedArea );
            if ( bias * score >= bestScore )
            {
                bestScore = score;
                bestIndex = i2;
                bestSignedArea = signedArea;
            }
        }

        if ( bestIndex < 0 )
            return;

        m.pts[2] = points[bestIndex];
        m.count = 3;
        points[bestIndex] = points[count - 1];
        count -= 1;

        float3 c = m.pts[2].point;

        bestScore = tolSqr;
        bestIndex = -1;
        float sgn = bestSignedArea < 0.0 ? -1.0 : 1.0;

        [loop]
        for ( int i3 = 0; i3 < count; ++i3 )
        {
            float3 pp = points[i3].point;
            float u1 = sgn * dot( normal, cross( pp - a, ba ) );
            float u2 = sgn * dot( normal, cross( pp - b, c - b ) );
            float u3 = sgn * dot( normal, cross( pp - c, a - c ) );
            float score = max( u1, max( u2, u3 ) );

            if ( bias * score > bestScore )
            {
                bestScore = score;
                bestIndex = i3;
            }
        }

        if ( bestIndex >= 0 )
        {
            m.pts[m.count] = points[bestIndex];
            m.count += 1;
        }
    }

    bool BuildFaceContact( out LManifold m, HullBox refH, HullBox incH, float4 xfq, float3 xfp, int refFace, int incVertex, out float minSeparation )
    {
        m.normal = float3( 0.0, 0.0, 0.0 );
        m.count = 0;
        LMPoint zeroPt;
        zeroPt.point = float3( 0.0, 0.0, 0.0 );
        zeroPt.separation = 0.0;
        zeroPt.pair = 0u;
        m.pts[0] = zeroPt;
        m.pts[1] = zeroPt;
        m.pts[2] = zeroPt;
        m.pts[3] = zeroPt;
        minSeparation = 0.0;

        float3 refN = refH.pn[refFace];
        float refO = refH.po[refFace];

        float3 refNormalInB = B3InvRotateVector( xfq, refN );
        int incFace = FindIncidentFace( incH, refNormalInB, incVertex );

        CV buf1[16];
        CV buf2[16];

        M3 matrix = B3MakeMatrixFromQuat( xfq );
        uint startInc = BOX_FACE_EDGE[incFace];
        uint e = startInc;
        int pointCount = 0;

        [loop]
        for ( int guard = 0; guard < 8; ++guard )
        {
            uint nextE = BOX_EDGES[e].x;
            float3 pos = B3MulMV( matrix, incH.v[BOX_EDGES[nextE].z] ) + xfp;
            buf1[pointCount].position = pos;
            buf1[pointCount].separation = dot( refN, pos ) - refO;
            buf1[pointCount].pair = ( 1u << 24 ) | ( e << 16 ) | ( 1u << 8 ) | nextE;
            pointCount += 1;
            e = nextE;
            if ( e == startInc )
                break;
        }

        uint startRef = BOX_FACE_EDGE[refFace];
        uint edgeIndex = startRef;

        [loop]
        for ( int side = 0; side < 8; ++side )
        {
            uint nextEdgeIndex = BOX_EDGES[edgeIndex].x;
            float3 vertex1 = refH.v[BOX_EDGES[edgeIndex].z];
            float3 vertex2 = refH.v[BOX_EDGES[nextEdgeIndex].z];
            float3 tangent = B3NormalizeV( vertex2 - vertex1 );
            float3 binormal = cross( tangent, refN );
            float clipO = dot( binormal, vertex1 );

            CV vertexP = buf1[pointCount - 1];
            float distance1 = dot( binormal, vertexP.position ) - clipO;
            int outCount = 0;

            [loop]
            for ( int index = 0; index < pointCount; ++index )
            {
                CV vertex2c = buf1[index];
                float distance2 = dot( binormal, vertex2c.position ) - clipO;

                if ( distance1 <= 0.0 && distance2 <= 0.0 )
                {
                    buf2[outCount] = vertex2c;
                    outCount += 1;
                }
                else if ( distance1 <= 0.0 && distance2 > 0.0 )
                {
                    float fraction = distance1 / ( distance1 - distance2 );
                    float3 position = vertexP.position + fraction * ( vertex2c.position - vertexP.position );
                    CV vtx;
                    vtx.position = position;
                    vtx.separation = dot( refN, position ) - refO;
                    vtx.pair = ( vertex2c.pair & 0xFFFF0000u ) | edgeIndex;
                    buf2[outCount] = vtx;
                    outCount += 1;
                }
                else if ( distance2 <= 0.0 && distance1 > 0.0 )
                {
                    float fraction = distance1 / ( distance1 - distance2 );
                    float3 position = vertexP.position + fraction * ( vertex2c.position - vertexP.position );
                    CV vtx;
                    vtx.position = position;
                    vtx.separation = dot( refN, position ) - refO;
                    vtx.pair = ( edgeIndex << 16 ) | ( vertexP.pair & 0x0000FFFFu );
                    buf2[outCount] = vtx;
                    outCount += 1;

                    buf2[outCount] = vertex2c;
                    outCount += 1;
                }

                vertexP = vertex2c;
                distance1 = distance2;
            }

            pointCount = outCount;

            [loop]
            for ( int k = 0; k < pointCount; ++k )
                buf1[k] = buf2[k];

            if ( pointCount < 3 )
                return false;

            edgeIndex = nextEdgeIndex;
            if ( edgeIndex == startRef )
                break;
        }

        LMPoint points[16];
        float minSep = 3.402823466e38;

        m.normal = refN;

        [loop]
        for ( int i = 0; i < pointCount; ++i )
        {
            points[i].point = buf1[i].position - ( 0.5 * buf1[i].separation ) * refN;
            points[i].separation = buf1[i].separation;
            points[i].pair = buf1[i].pair;
            minSep = min( minSep, buf1[i].separation );
        }

        if ( minSep >= 4.0 * 0.005 )
            return false;

        ReduceManifold( m, points, pointCount );

        minSeparation = minSep;
        return true;
    }

    bool BuildEdgeContact( out LManifold m, HullBox hA, HullBox hB, float4 btq, float3 btp, float3 axisNormal, int indexA, int indexB )
    {
        m.normal = float3( 0.0, 0.0, 0.0 );
        m.count = 0;
        LMPoint zeroPt;
        zeroPt.point = float3( 0.0, 0.0, 0.0 );
        zeroPt.separation = 0.0;
        zeroPt.pair = 0u;
        m.pts[0] = zeroPt;
        m.pts[1] = zeroPt;
        m.pts[2] = zeroPt;
        m.pts[3] = zeroPt;

        float3 pA = hA.v[BOX_EDGES[indexA].z];
        float3 qA = hA.v[BOX_EDGES[BOX_EDGES[indexA].y].z];
        float3 eA = qA - pA;

        float3 pB = B3RotateVector( btq, hB.v[BOX_EDGES[indexB].z] ) + btp;
        float3 qB = B3RotateVector( btq, hB.v[BOX_EDGES[BOX_EDGES[indexB].y].z] ) + btp;
        float3 eB = qB - pB;

        float3 point1;
        float3 point2;
        float fraction1;
        float fraction2;
        LineDistance( pA, eA, pB, eB, point1, point2, fraction1, fraction2 );

        if ( !( 0.0 <= fraction1 && fraction1 <= 1.0 && 0.0 <= fraction2 && fraction2 <= 1.0 ) )
            return false;

        float separation = dot( axisNormal, point2 - point1 );
        float3 point = 0.5 * ( point1 + point2 );

        m.normal = axisNormal;
        m.count = 1;
        m.pts[0].point = point;
        m.pts[0].separation = separation;
        m.pts[0].pair = ( (uint)indexA << 16 ) | ( 1u << 8 ) | (uint)indexB;
        return true;
    }

    void CollideHullHull( uint mo, uint a, uint b )
    {
        float3 pAw = LoadV3( a, 53 );
        float4 qAw = LoadQ( a, 3 );
        float3 pBw = LoadV3( b, 53 );
        float4 qBw = LoadQ( b, 3 );

        HullBox hA = MakeHullBox( a );
        HullBox hB = MakeHullBox( b );

        float4 btq = B3InvMulQuat( qAw, qBw );
        float3 btp = B3InvRotateVector( qAw, pBw - pAw );

        M3 R = B3MakeMatrixFromQuat( btq );
        M3 invR = B3Transpose( R );

        float speculativeDistance = 4.0 * 0.005;
        float linearSlop = 0.005;

        float faceASep = -3.402823466e38;
        int faceAIndexA = -1;
        int faceAIndexB = -1;
        float3 faceANormal = float3( 0.0, 0.0, 0.0 );

        float faceBSep = -3.402823466e38;
        int faceBIndexA = -1;
        int faceBIndexB = -1;
        float3 faceBNormal = float3( 0.0, 0.0, 0.0 );

        float edgeSep = -3.402823466e38;
        int edgeIndexA = -1;
        int edgeIndexB = -1;
        float3 edgeNormal = float3( 0.0, 0.0, 0.0 );

        float3 hBExt = HullExtents( hB );
        float3 cB = float3( 0.0, 0.0, 0.0 );

        [loop]
        for ( int fa = 0; fa < 6; ++fa )
        {
            float3 n = hA.pn[fa];
            float3 direction = -B3MulMV( invR, n );
            float planeSeparation = dot( n, btp ) - hA.po[fa];
            float biasB = dot( direction, cB ) + 1.0625 * dot( abs( direction ), hBExt );
            float support;
            int vertexIndex = HullSupportBiased( hB, direction, biasB, support );
            float separation = planeSeparation - support;
            if ( separation > faceASep )
            {
                faceANormal = n;
                faceASep = separation;
                faceAIndexA = fa;
                faceAIndexB = vertexIndex;
                if ( separation > speculativeDistance )
                {
                    Manifolds[mo + 3] = 0.0;
                    return;
                }
            }
        }

        float3 hAExt = HullExtents( hA );
        float3 cA = float3( 0.0, 0.0, 0.0 );

        [loop]
        for ( int fb = 0; fb < 6; ++fb )
        {
            float3 n = hB.pn[fb];
            float3 direction = -B3MulMV( R, n );
            float planeSeparation = dot( direction, btp ) - hB.po[fb];
            float biasA = dot( direction, cA ) + 1.0625 * dot( abs( direction ), hAExt );
            float support;
            int vertexIndex = HullSupportBiased( hA, direction, biasA, support );
            float separation = planeSeparation - support;
            if ( separation > faceBSep )
            {
                faceBNormal = direction;
                faceBSep = separation;
                faceBIndexA = vertexIndex;
                faceBIndexB = fb;
                if ( separation > speculativeDistance )
                {
                    Manifolds[mo + 3] = 0.0;
                    return;
                }
            }
        }

        float3 bFN[6];
        [loop]
        for ( int f = 0; f < 6; ++f )
            bFN[f] = -B3MulMV( R, hB.pn[f] );

        float3 bW[8];
        [loop]
        for ( int vtx = 0; vtx < 8; ++vtx )
            bW[vtx] = -( B3MulMV( R, hB.v[vtx] ) + btp );

        float EPS = -0.0001;
        float squaredTol = 0.005 * 0.005;

        [loop]
        for ( int jj = 0; jj < 12; ++jj )
        {
            uint be = (uint)( 2 * jj );
            uint bt = BOX_EDGES[be].y;
            float3 C = bFN[BOX_EDGES[be].w];
            float3 D = bFN[BOX_EDGES[bt].w];
            float3 bv0 = bW[BOX_EDGES[be].z];
            float3 DC = bW[BOX_EDGES[bt].z] - bW[BOX_EDGES[be].z];

            [loop]
            for ( int ii = 0; ii < 12; ++ii )
            {
                uint ae = (uint)( 2 * ii );
                uint at = BOX_EDGES[ae].y;
                float3 aN0 = hA.pn[BOX_EDGES[ae].w];
                float3 aN1 = hA.pn[BOX_EDGES[at].w];
                float3 av0 = hA.v[BOX_EDGES[ae].z];
                float3 aD = hA.v[BOX_EDGES[at].z] - av0;
                float aTol = squaredTol * ( aD.x * aD.x + aD.y * aD.y + aD.z * aD.z );

                float CBA = C.x * aD.x + ( C.y * aD.y + C.z * aD.z );
                float DBA = D.x * aD.x + ( D.y * aD.y + D.z * aD.z );
                if ( CBA * DBA >= EPS )
                    continue;

                float ADC = aN0.x * DC.x + ( aN0.y * DC.y + aN0.z * DC.z );
                float BDC = aN1.x * DC.x + ( aN1.y * DC.y + aN1.z * DC.z );
                if ( ADC * BDC >= EPS || CBA * BDC >= EPS )
                    continue;

                float maxCD = max( CBA * CBA, DBA * DBA );
                if ( maxCD <= aTol )
                    continue;

                float t = -CBA / ( DBA - CBA );

                float nx = C.x + t * ( D.x - C.x );
                float ny = C.y + t * ( D.y - C.y );
                float nz = C.z + t * ( D.z - C.z );
                float len2 = nx * nx + ( ny * ny + nz * nz );
                float inv = 1.0 / sqrt( len2 );
                nx *= inv;
                ny *= inv;
                nz *= inv;

                float sx = av0.x + bv0.x;
                float sy = av0.y + bv0.y;
                float sz = av0.z + bv0.z;

                float separation = -( sx * nx + ( sy * ny + sz * nz ) );
                if ( separation > edgeSep )
                {
                    edgeNormal = float3( nx, ny, nz );
                    edgeSep = separation;
                    edgeIndexA = 2 * ii;
                    edgeIndexB = 2 * jj;

                    if ( separation > speculativeDistance )
                    {
                        Manifolds[mo + 3] = 0.0;
                        return;
                    }
                }
            }
        }

        LManifold m;
        float clipSeparation = 0.0;
        bool touching = false;

        if ( faceASep > faceBSep )
        {
            touching = BuildFaceContact( m, hA, hB, btq, btp, faceAIndexA, faceAIndexB, clipSeparation );
        }
        else
        {
            float4 atbq = float4( -btq.x, -btq.y, -btq.z, btq.w );
            float3 atbp = B3InvRotateVector( btq, -btp );
            touching = BuildFaceContact( m, hB, hA, atbq, atbp, faceBIndexB, faceBIndexA, clipSeparation );

            if ( touching )
            {
                M3 matBtoA = B3MakeMatrixFromQuat( btq );
                m.normal = -B3MulMV( matBtoA, m.normal );

                [loop]
                for ( int i = 0; i < m.count; ++i )
                {
                    m.pts[i].point = B3MulMV( matBtoA, m.pts[i].point ) + btp;
                    m.pts[i].pair = FlipPairU( m.pts[i].pair );
                }
            }
        }

        if ( !touching )
        {
            m.count = 0;
            clipSeparation = 0.0;
        }

        if ( edgeIndexA >= 0 && ( m.count == 0 || edgeSep > clipSeparation + linearSlop ) )
        {
            LManifold em;
            bool edgeTouching = BuildEdgeContact( em, hA, hB, btq, btp, edgeNormal, edgeIndexA, edgeIndexB );
            if ( edgeTouching && em.count == 1 )
                m = em;
        }

        if ( m.count == 0 )
        {
            Manifolds[mo + 3] = 0.0;
            return;
        }

        M3 matrixA = B3MakeMatrixFromQuat( qAw );
        float3 normalWorld = B3MulMV( matrixA, m.normal );

        Manifolds[mo] = normalWorld.x;
        Manifolds[mo + 1] = normalWorld.y;
        Manifolds[mo + 2] = normalWorld.z;
        Manifolds[mo + 3] = (float)m.count;

        [loop]
        for ( int i = 0; i < m.count; ++i )
        {
            float3 anchorA = B3MulMV( matrixA, m.pts[i].point );
            uint k = mo + 4 + (uint)i * 5;
            Manifolds[k] = anchorA.x;
            Manifolds[k + 1] = anchorA.y;
            Manifolds[k + 2] = anchorA.z;
            Manifolds[k + 3] = m.pts[i].separation;
            Manifolds[k + 4] = asfloat( m.pts[i].pair );
        }
    }

    float3 ClosestPtPointTriangle( float3 p, float3 a, float3 b, float3 c )
    {
        float3 ab = b - a;
        float3 ac = c - a;
        float3 ap = p - a;
        float d1 = dot( ab, ap );
        float d2 = dot( ac, ap );
        if ( d1 <= 0.0 && d2 <= 0.0 )
            return a;

        float3 bp = p - b;
        float d3 = dot( ab, bp );
        float d4 = dot( ac, bp );
        if ( d3 >= 0.0 && d4 <= d3 )
            return b;

        float vc = d1 * d4 - d3 * d2;
        if ( vc <= 0.0 && d1 >= 0.0 && d3 <= 0.0 )
            return a + ( d1 / ( d1 - d3 ) ) * ab;

        float3 cp = p - c;
        float d5 = dot( ab, cp );
        float d6 = dot( ac, cp );
        if ( d6 >= 0.0 && d5 <= d6 )
            return c;

        float vb = d5 * d2 - d1 * d6;
        if ( vb <= 0.0 && d2 >= 0.0 && d6 <= 0.0 )
            return a + ( d2 / ( d2 - d6 ) ) * ac;

        float va = d3 * d6 - d5 * d4;
        if ( va <= 0.0 && ( d4 - d3 ) >= 0.0 && ( d5 - d6 ) >= 0.0 )
            return b + ( ( d4 - d3 ) / ( ( d4 - d3 ) + ( d5 - d6 ) ) ) * ( c - b );

        float denom = 1.0 / ( va + vb + vc );
        float v = vb * denom;
        float w = vc * denom;
        return a + v * ab + w * ac;
    }

    void CollideMeshSphere( uint mo, uint b, float3 excludeN, float excludeOn )
    {
        float3 pB = LoadV3( b, 53 );
        float radius = Bodies[b * B3_STRIDE + 57];
        float speculativeDistance = 4.0 * 0.005;
        float3 vel = LoadV3( b, 7 );
        float window = speculativeDistance + min( sqrt( dot( vel, vel ) ) * StepDt, 6.0 );

        uint kb = b * 6;
        float3 lo = float3( Aabbs[kb], Aabbs[kb + 1], Aabbs[kb + 2] );
        float3 hi = float3( Aabbs[kb + 3], Aabbs[kb + 4], Aabbs[kb + 5] );
        int3 c0 = CellOf( lo );
        int3 c1 = CellOf( hi );

        float bestSep = window;
        float3 bestPoint = float3( 0.0, 0.0, 0.0 );
        float3 bestNormal = float3( 0.0, 1.0, 0.0 );
        uint bestTri = 0u;
        bool found = false;

        [loop]
        for ( int z = c0.z; z <= c1.z; ++z )
        {
            [loop]
            for ( int y = c0.y; y <= c1.y; ++y )
            {
                [loop]
                for ( int x = c0.x; x <= c1.x; ++x )
                {
                    uint cell = CellIndex( int3( x, y, z ) );
                    uint n = min( GridCount[cell], (uint)CellCap );

                    [loop]
                    for ( uint s = 0; s < n; ++s )
                    {
                        uint e = GridCells[cell * (uint)CellCap + s];
                        if ( ( e & TRI_FLAG ) == 0 )
                            continue;

                        uint t = e & ~TRI_FLAG;
                        uint to = t * 9u;
                        float3 ta = float3( TriData[to], TriData[to + 1], TriData[to + 2] );
                        float3 tb = float3( TriData[to + 3], TriData[to + 4], TriData[to + 5] );
                        float3 tc = float3( TriData[to + 6], TriData[to + 7], TriData[to + 8] );

                        float3 cp = ClosestPtPointTriangle( pB, ta, tb, tc );
                        float3 d = pB - cp;
                        float dist = sqrt( dot( d, d ) );
                        float sep = dist - radius;
                        if ( sep >= bestSep )
                            continue;

                        float3 nrm;
                        if ( dist > 1000.0 * B3_FLT_MIN )
                            nrm = ( 1.0 / dist ) * d;
                        else
                            nrm = B3NormalizeV( cross( tb - ta, tc - ta ) );

                        if ( excludeOn > 0.0 && dot( nrm, excludeN ) >= 0.9 )
                            continue;

                        bestSep = sep;
                        bestPoint = cp;
                        bestNormal = nrm;
                        bestTri = t;
                        found = true;
                    }
                }
            }
        }

        if ( !found )
        {
            Manifolds[mo + 3] = 0.0;
            return;
        }

        Manifolds[mo] = bestNormal.x;
        Manifolds[mo + 1] = bestNormal.y;
        Manifolds[mo + 2] = bestNormal.z;
        Manifolds[mo + 3] = 1.0;
        Manifolds[mo + 4] = bestPoint.x;
        Manifolds[mo + 5] = bestPoint.y;
        Manifolds[mo + 6] = bestPoint.z;
        Manifolds[mo + 7] = bestSep;
        Manifolds[mo + 8] = asfloat( bestTri );
    }

    void CollideMeshHull( uint mo, uint b, float3 excludeN, float excludeOn )
    {
        float3 pB = LoadV3( b, 53 );
        float4 qB = LoadQ( b, 3 );
        M3 rB = B3MakeMatrixFromQuat( qB );
        float minH = 0.2 * 0.005;
        float3 h = max( float3( minH, minH, minH ), float3( Bodies[b * B3_STRIDE + 57], Bodies[b * B3_STRIDE + 58], Bodies[b * B3_STRIDE + 59] ) );
        float speculativeDistance = 4.0 * 0.005;
        float3 vel = LoadV3( b, 7 );
        float3 omega = LoadV3( b, 10 );
        float sweep = sqrt( dot( vel, vel ) ) + sqrt( dot( omega, omega ) ) * sqrt( dot( h, h ) );
        float window = speculativeDistance + min( sweep * StepDt, 6.0 );

        float cornerRadius = min( 0.08, 0.3 * min( h.x, min( h.y, h.z ) ) );
        float3 hc = h - cornerRadius;

        float3 corners[8];
        corners[0] = pB + B3MulMV( rB, float3( hc.x, hc.y, hc.z ) );
        corners[1] = pB + B3MulMV( rB, float3( -hc.x, hc.y, hc.z ) );
        corners[2] = pB + B3MulMV( rB, float3( hc.x, -hc.y, hc.z ) );
        corners[3] = pB + B3MulMV( rB, float3( -hc.x, -hc.y, hc.z ) );
        corners[4] = pB + B3MulMV( rB, float3( hc.x, hc.y, -hc.z ) );
        corners[5] = pB + B3MulMV( rB, float3( -hc.x, hc.y, -hc.z ) );
        corners[6] = pB + B3MulMV( rB, float3( hc.x, -hc.y, -hc.z ) );
        corners[7] = pB + B3MulMV( rB, float3( -hc.x, -hc.y, -hc.z ) );

        float bestSep[8];
        float3 bestPt[8];
        float3 bestN[8];
        uint bestT[8];
        bool found[8];

        [unroll]
        for ( int k0 = 0; k0 < 8; ++k0 )
        {
            bestSep[k0] = window;
            bestPt[k0] = float3( 0.0, 0.0, 0.0 );
            bestN[k0] = float3( 0.0, 1.0, 0.0 );
            bestT[k0] = 0u;
            found[k0] = false;
        }

        uint kb = b * 6;
        float3 lo = float3( Aabbs[kb], Aabbs[kb + 1], Aabbs[kb + 2] );
        float3 hi = float3( Aabbs[kb + 3], Aabbs[kb + 4], Aabbs[kb + 5] );
        int3 c0 = CellOf( lo );
        int3 c1 = CellOf( hi );

        [loop]
        for ( int z = c0.z; z <= c1.z; ++z )
        {
            [loop]
            for ( int y = c0.y; y <= c1.y; ++y )
            {
                [loop]
                for ( int x = c0.x; x <= c1.x; ++x )
                {
                    uint cell = CellIndex( int3( x, y, z ) );
                    uint n = min( GridCount[cell], (uint)CellCap );

                    [loop]
                    for ( uint s = 0; s < n; ++s )
                    {
                        uint e = GridCells[cell * (uint)CellCap + s];
                        if ( ( e & TRI_FLAG ) == 0 )
                            continue;

                        uint t = e & ~TRI_FLAG;
                        uint to = t * 9u;
                        float3 ta = float3( TriData[to], TriData[to + 1], TriData[to + 2] );
                        float3 tb = float3( TriData[to + 3], TriData[to + 4], TriData[to + 5] );
                        float3 tc = float3( TriData[to + 6], TriData[to + 7], TriData[to + 8] );

                        [loop]
                        for ( int k = 0; k < 8; ++k )
                        {
                            float3 cp = ClosestPtPointTriangle( corners[k], ta, tb, tc );
                            float3 d = corners[k] - cp;
                            float dist = sqrt( dot( d, d ) );
                            float sep = dist - cornerRadius;
                            if ( sep >= bestSep[k] )
                                continue;

                            float3 nrm;
                            if ( dist > 1000.0 * B3_FLT_MIN )
                                nrm = ( 1.0 / dist ) * d;
                            else
                                nrm = B3NormalizeV( cross( tb - ta, tc - ta ) );

                            if ( excludeOn > 0.0 && dot( nrm, excludeN ) >= 0.9 )
                                continue;

                            bestSep[k] = sep;
                            bestPt[k] = cp;
                            bestN[k] = nrm;
                            bestT[k] = t;
                            found[k] = true;
                        }
                    }
                }
            }
        }

        int deep = -1;
        float deepSep = window;
        [unroll]
        for ( int k1 = 0; k1 < 8; ++k1 )
        {
            if ( found[k1] && bestSep[k1] < deepSep )
            {
                deepSep = bestSep[k1];
                deep = k1;
            }
        }

        if ( deep < 0 )
        {
            Manifolds[mo + 3] = 0.0;
            return;
        }

        float3 normal = bestN[deep];
        Manifolds[mo] = normal.x;
        Manifolds[mo + 1] = normal.y;
        Manifolds[mo + 2] = normal.z;

        int count = 0;
        [loop]
        for ( int k2 = 0; k2 < 8 && count < 4; ++k2 )
        {
            if ( !found[k2] )
                continue;
            if ( k2 != deep && dot( bestN[k2], normal ) < 0.9 )
                continue;

            uint ko = mo + 4 + (uint)count * 5;
            Manifolds[ko] = bestPt[k2].x;
            Manifolds[ko + 1] = bestPt[k2].y;
            Manifolds[ko + 2] = bestPt[k2].z;
            Manifolds[ko + 3] = bestSep[k2];
            Manifolds[ko + 4] = asfloat( ( bestT[k2] << 3 ) | (uint)k2 );
            count++;
        }

        Manifolds[mo + 3] = (float)count;
    }

    bool VoxGet( int3 c )
    {
        if ( c.x < 0 || c.y < 0 || c.z < 0 || c.x >= VoxDimX || c.y >= VoxDimY || c.z >= VoxDimZ )
            return false;

        uint idx = (uint)( ( c.z * VoxDimY + c.y ) * VoxDimX + c.x );
        return ( asuint( TriData[idx >> 5] ) & ( 1u << ( idx & 31u ) ) ) != 0u;
    }

    bool InPortalHole( float3 p )
    {
        if ( PortalOn == 0 )
            return false;

        float3 relA = p - PortalAPos;
        float ar = dot( relA, PortalAR ) / PortalHalf.x;
        float au = dot( relA, PortalAU ) / PortalHalf.y;
        if ( abs( dot( relA, PortalAN ) ) < PortalHalf.z && ar * ar + au * au < 1.0 )
            return true;

        float3 relB = p - PortalBPos;
        float br = dot( relB, PortalBR ) / PortalHalf.x;
        float bu = dot( relB, PortalBU ) / PortalHalf.y;
        if ( abs( dot( relB, PortalBN ) ) < PortalHalf.z && br * br + bu * bu < 1.0 )
            return true;

        return false;
    }

    void VoxSampleContact( float3 p, float radius, float window, inout float bestSep, inout float3 bestPoint, inout float3 bestNormal, inout bool found )
    {
        float3 lo = p - ( radius + window );
        float3 hi = p + ( radius + window );
        int3 c0 = int3( floor( ( lo - VoxOrigin ) / VoxSize ) );
        int3 c1 = int3( floor( ( hi - VoxOrigin ) / VoxSize ) );

        [loop]
        for ( int z = c0.z; z <= c1.z; ++z )
        {
            [loop]
            for ( int y = c0.y; y <= c1.y; ++y )
            {
                [loop]
                for ( int x = c0.x; x <= c1.x; ++x )
                {
                    int3 c = int3( x, y, z );
                    if ( !VoxGet( c ) )
                        continue;

                    if ( PortalOn != 0 && InPortalHole( VoxOrigin + ( float3( c ) + 0.5 ) * VoxSize ) )
                        continue;

                    float3 vlo = VoxOrigin + float3( c ) * VoxSize;
                    float3 vhi = vlo + VoxSize;
                    float3 cp = clamp( p, vlo, vhi );
                    float3 d = p - cp;
                    float dist2 = dot( d, d );

                    float sep;
                    float3 nrm;
                    float3 pt;

                    if ( dist2 > 1e-10 )
                    {
                        float dist = sqrt( dist2 );
                        sep = dist - radius;
                        nrm = d / dist;
                        pt = cp;
                    }
                    else
                    {
                        float3 center = ( vlo + vhi ) * 0.5;
                        float3 q = p - center;
                        float3 h = ( vhi - vlo ) * 0.5;
                        float3 pen = h - abs( q );
                        if ( pen.x <= pen.y && pen.x <= pen.z )
                        {
                            nrm = float3( q.x >= 0.0 ? 1.0 : -1.0, 0.0, 0.0 );
                            sep = -pen.x - radius;
                        }
                        else if ( pen.y <= pen.z )
                        {
                            nrm = float3( 0.0, q.y >= 0.0 ? 1.0 : -1.0, 0.0 );
                            sep = -pen.y - radius;
                        }
                        else
                        {
                            nrm = float3( 0.0, 0.0, q.z >= 0.0 ? 1.0 : -1.0 );
                            sep = -pen.z - radius;
                        }

                        pt = p - nrm * ( sep + radius );
                    }

                    if ( VoxGet( c + int3( nrm.x >= 0.5 ? 1 : ( nrm.x <= -0.5 ? -1 : 0 ),
                                           nrm.y >= 0.5 ? 1 : ( nrm.y <= -0.5 ? -1 : 0 ),
                                           nrm.z >= 0.5 ? 1 : ( nrm.z <= -0.5 ? -1 : 0 ) ) ) )
                        continue;

                    if ( sep < bestSep )
                    {
                        bestSep = sep;
                        bestPoint = pt;
                        bestNormal = nrm;
                        found = true;
                    }
                }
            }
        }
    }

    void CollideVoxSphere( uint mo, uint b )
    {
        float3 pB = LoadV3( b, 53 );
        float radius = Bodies[b * B3_STRIDE + 57];
        float3 vel = LoadV3( b, 7 );
        float window = 4.0 * 0.005 + min( sqrt( dot( vel, vel ) ) * StepDt, 1.0 );

        float bestSep = window;
        float3 bestPoint = float3( 0.0, 0.0, 0.0 );
        float3 bestNormal = float3( 0.0, 1.0, 0.0 );
        bool found = false;

        VoxSampleContact( pB, radius, window, bestSep, bestPoint, bestNormal, found );

        if ( !found )
        {
            Manifolds[mo + 3] = 0.0;
            return;
        }

        Manifolds[mo] = bestNormal.x;
        Manifolds[mo + 1] = bestNormal.y;
        Manifolds[mo + 2] = bestNormal.z;
        Manifolds[mo + 3] = 1.0;
        Manifolds[mo + 4] = bestPoint.x;
        Manifolds[mo + 5] = bestPoint.y;
        Manifolds[mo + 6] = bestPoint.z;
        Manifolds[mo + 7] = bestSep;
        Manifolds[mo + 8] = 0.0;
    }

    void CollideVoxHull( uint mo, uint b )
    {
        float3 pB = LoadV3( b, 53 );
        float4 qB = LoadQ( b, 3 );
        M3 rB = B3MakeMatrixFromQuat( qB );
        float minH = 0.2 * 0.005;
        float3 h = max( float3( minH, minH, minH ), float3( Bodies[b * B3_STRIDE + 57], Bodies[b * B3_STRIDE + 58], Bodies[b * B3_STRIDE + 59] ) );
        float3 vel = LoadV3( b, 7 );
        float3 omega = LoadV3( b, 10 );
        float sweep = sqrt( dot( vel, vel ) ) + sqrt( dot( omega, omega ) ) * sqrt( dot( h, h ) );
        float window = 4.0 * 0.005 + min( sweep * StepDt, 1.0 );

        float cornerRadius = min( 0.08, 0.3 * min( h.x, min( h.y, h.z ) ) );
        float3 hc = h - cornerRadius;

        float3 corners[8];
        corners[0] = pB + B3MulMV( rB, float3( hc.x, hc.y, hc.z ) );
        corners[1] = pB + B3MulMV( rB, float3( -hc.x, hc.y, hc.z ) );
        corners[2] = pB + B3MulMV( rB, float3( hc.x, -hc.y, hc.z ) );
        corners[3] = pB + B3MulMV( rB, float3( -hc.x, -hc.y, hc.z ) );
        corners[4] = pB + B3MulMV( rB, float3( hc.x, hc.y, -hc.z ) );
        corners[5] = pB + B3MulMV( rB, float3( -hc.x, hc.y, -hc.z ) );
        corners[6] = pB + B3MulMV( rB, float3( hc.x, -hc.y, -hc.z ) );
        corners[7] = pB + B3MulMV( rB, float3( -hc.x, -hc.y, -hc.z ) );

        float bestSep[8];
        float3 bestPt[8];
        float3 bestN[8];
        bool found[8];

        [loop]
        for ( int k = 0; k < 8; ++k )
        {
            bestSep[k] = window;
            bestPt[k] = float3( 0.0, 0.0, 0.0 );
            bestN[k] = float3( 0.0, 1.0, 0.0 );
            found[k] = false;
            VoxSampleContact( corners[k], cornerRadius, window, bestSep[k], bestPt[k], bestN[k], found[k] );
        }

        int deep = -1;
        float deepSep = window;

        [unroll]
        for ( int k1 = 0; k1 < 8; ++k1 )
        {
            if ( found[k1] && bestSep[k1] < deepSep )
            {
                deepSep = bestSep[k1];
                deep = k1;
            }
        }

        if ( deep < 0 )
        {
            Manifolds[mo + 3] = 0.0;
            return;
        }

        float3 normal = bestN[deep];
        Manifolds[mo] = normal.x;
        Manifolds[mo + 1] = normal.y;
        Manifolds[mo + 2] = normal.z;

        int count = 0;

        [loop]
        for ( int k2 = 0; k2 < 8 && count < 4; ++k2 )
        {
            if ( !found[k2] )
                continue;
            if ( k2 != deep && dot( bestN[k2], normal ) < 0.9 )
                continue;

            uint ko = mo + 4 + (uint)count * 5;
            Manifolds[ko] = bestPt[k2].x;
            Manifolds[ko + 1] = bestPt[k2].y;
            Manifolds[ko + 2] = bestPt[k2].z;
            Manifolds[ko + 3] = bestSep[k2];
            Manifolds[ko + 4] = asfloat( (uint)k2 );
            count++;
        }

        Manifolds[mo + 3] = (float)count;
    }

    void CollideVoxCapsule( uint mo, uint b )
    {
        float3 pB = LoadV3( b, 53 );
        float4 qB = LoadQ( b, 3 );
        float radius = Bodies[b * B3_STRIDE + 57];
        float hB = Bodies[b * B3_STRIDE + 58];
        float3 axis = B3RotateVector( qB, float3( 0.0, hB, 0.0 ) );
        float3 vel = LoadV3( b, 7 );
        float3 omega = LoadV3( b, 10 );
        float sweep = sqrt( dot( vel, vel ) ) + sqrt( dot( omega, omega ) ) * ( hB + radius );
        float window = 4.0 * 0.005 + min( sweep * StepDt, 1.0 );

        float3 samples[5];
        samples[0] = pB - axis;
        samples[1] = pB - 0.5 * axis;
        samples[2] = pB;
        samples[3] = pB + 0.5 * axis;
        samples[4] = pB + axis;

        float bestSep[5];
        float3 bestPt[5];
        float3 bestN[5];
        bool found[5];

        [loop]
        for ( int k = 0; k < 5; ++k )
        {
            bestSep[k] = window;
            bestPt[k] = float3( 0.0, 0.0, 0.0 );
            bestN[k] = float3( 0.0, 1.0, 0.0 );
            found[k] = false;
            VoxSampleContact( samples[k], radius, window, bestSep[k], bestPt[k], bestN[k], found[k] );
        }

        int deep = -1;
        float deepSep = window;

        [unroll]
        for ( int k1 = 0; k1 < 5; ++k1 )
        {
            if ( found[k1] && bestSep[k1] < deepSep )
            {
                deepSep = bestSep[k1];
                deep = k1;
            }
        }

        if ( deep < 0 )
        {
            Manifolds[mo + 3] = 0.0;
            return;
        }

        float3 normal = bestN[deep];
        Manifolds[mo] = normal.x;
        Manifolds[mo + 1] = normal.y;
        Manifolds[mo + 2] = normal.z;

        int count = 0;

        [loop]
        for ( int k2 = 0; k2 < 5 && count < 4; ++k2 )
        {
            if ( !found[k2] )
                continue;
            if ( k2 != deep && dot( bestN[k2], normal ) < 0.9 )
                continue;

            uint ko = mo + 4 + (uint)count * 5;
            Manifolds[ko] = bestPt[k2].x;
            Manifolds[ko + 1] = bestPt[k2].y;
            Manifolds[ko + 2] = bestPt[k2].z;
            Manifolds[ko + 3] = bestSep[k2];
            Manifolds[ko + 4] = asfloat( (uint)k2 );
            count++;
        }

        Manifolds[mo + 3] = (float)count;
    }

    void CollideMeshCapsule( uint mo, uint b, float3 excludeN, float excludeOn )
    {
        float3 pB = LoadV3( b, 53 );
        float4 qB = LoadQ( b, 3 );
        float radius = Bodies[b * B3_STRIDE + 57];
        float hB = Bodies[b * B3_STRIDE + 58];
        float3 axis = B3RotateVector( qB, float3( 0.0, hB, 0.0 ) );

        float speculativeDistance = 4.0 * 0.005;
        float3 vel = LoadV3( b, 7 );
        float3 omega = LoadV3( b, 10 );
        float sweep = sqrt( dot( vel, vel ) ) + sqrt( dot( omega, omega ) ) * ( hB + radius );
        float window = speculativeDistance + min( sweep * StepDt, 6.0 );

        float3 samples[5];
        samples[0] = pB - axis;
        samples[1] = pB - 0.5 * axis;
        samples[2] = pB;
        samples[3] = pB + 0.5 * axis;
        samples[4] = pB + axis;

        float bestSep[5];
        float3 bestPt[5];
        float3 bestN[5];
        uint bestT[5];
        bool found[5];

        [unroll]
        for ( int k0 = 0; k0 < 5; ++k0 )
        {
            bestSep[k0] = window;
            bestPt[k0] = float3( 0.0, 0.0, 0.0 );
            bestN[k0] = float3( 0.0, 1.0, 0.0 );
            bestT[k0] = 0u;
            found[k0] = false;
        }

        uint kb = b * 6;
        float3 lo = float3( Aabbs[kb], Aabbs[kb + 1], Aabbs[kb + 2] );
        float3 hi = float3( Aabbs[kb + 3], Aabbs[kb + 4], Aabbs[kb + 5] );
        int3 c0 = CellOf( lo );
        int3 c1 = CellOf( hi );

        [loop]
        for ( int z = c0.z; z <= c1.z; ++z )
        {
            [loop]
            for ( int y = c0.y; y <= c1.y; ++y )
            {
                [loop]
                for ( int x = c0.x; x <= c1.x; ++x )
                {
                    uint cell = CellIndex( int3( x, y, z ) );
                    uint n = min( GridCount[cell], (uint)CellCap );

                    [loop]
                    for ( uint s = 0; s < n; ++s )
                    {
                        uint e = GridCells[cell * (uint)CellCap + s];
                        if ( ( e & TRI_FLAG ) == 0 )
                            continue;

                        uint t = e & ~TRI_FLAG;
                        uint to = t * 9u;
                        float3 ta = float3( TriData[to], TriData[to + 1], TriData[to + 2] );
                        float3 tb = float3( TriData[to + 3], TriData[to + 4], TriData[to + 5] );
                        float3 tc = float3( TriData[to + 6], TriData[to + 7], TriData[to + 8] );

                        [loop]
                        for ( int k = 0; k < 5; ++k )
                        {
                            float3 cp = ClosestPtPointTriangle( samples[k], ta, tb, tc );
                            float3 d = samples[k] - cp;
                            float dist = sqrt( dot( d, d ) );
                            float sep = dist - radius;
                            if ( sep >= bestSep[k] )
                                continue;

                            float3 nrm;
                            if ( dist > 1000.0 * B3_FLT_MIN )
                                nrm = ( 1.0 / dist ) * d;
                            else
                                nrm = B3NormalizeV( cross( tb - ta, tc - ta ) );

                            if ( excludeOn > 0.0 && dot( nrm, excludeN ) >= 0.9 )
                                continue;

                            bestSep[k] = sep;
                            bestPt[k] = cp;
                            bestN[k] = nrm;
                            bestT[k] = t;
                            found[k] = true;
                        }
                    }
                }
            }
        }

        int deep = -1;
        float deepSep = window;

        [unroll]
        for ( int k1 = 0; k1 < 5; ++k1 )
        {
            if ( found[k1] && bestSep[k1] < deepSep )
            {
                deepSep = bestSep[k1];
                deep = k1;
            }
        }

        if ( deep < 0 )
        {
            Manifolds[mo + 3] = 0.0;
            return;
        }

        float3 normal = bestN[deep];
        Manifolds[mo] = normal.x;
        Manifolds[mo + 1] = normal.y;
        Manifolds[mo + 2] = normal.z;

        int count = 0;

        [loop]
        for ( int k2 = 0; k2 < 5 && count < 4; ++k2 )
        {
            if ( !found[k2] )
                continue;
            if ( k2 != deep && dot( bestN[k2], normal ) < 0.9 )
                continue;

            uint ko = mo + 4 + (uint)count * 5;
            Manifolds[ko] = bestPt[k2].x;
            Manifolds[ko + 1] = bestPt[k2].y;
            Manifolds[ko + 2] = bestPt[k2].z;
            Manifolds[ko + 3] = bestSep[k2];
            Manifolds[ko + 4] = asfloat( ( bestT[k2] << 3 ) | (uint)k2 );
            count++;
        }

        Manifolds[mo + 3] = (float)count;
    }

    void FlipManifoldAB( uint mo, float3 delta )
    {
        int count = (int)Manifolds[mo + 3];
        if ( count < 1 )
            return;

        Manifolds[mo] = -Manifolds[mo];
        Manifolds[mo + 1] = -Manifolds[mo + 1];
        Manifolds[mo + 2] = -Manifolds[mo + 2];

        [loop]
        for ( int k = 0; k < count; ++k )
        {
            uint ko = mo + 4 + (uint)k * 5;
            Manifolds[ko] += delta.x;
            Manifolds[ko + 1] += delta.y;
            Manifolds[ko + 2] += delta.z;
        }
    }

    void CollidePair( uint p )
    {
        Constraints[p * CON_STRIDE + 63] = -1.0;

        uint i = Pairs[p * 2];
        uint j = Pairs[p * 2 + 1];

        uint mo = p * MANIFOLD_STRIDE;
        float kindI = Bodies[i * B3_STRIDE + 56];
        float kindJ = Bodies[j * B3_STRIDE + 56];

        if ( kindI == 0.0 && kindJ == 0.0 )
        {
            CollideSphereSphere( mo, i, j );
            return;
        }

        if ( kindI == 2.0 && kindJ == 0.0 )
        {
            CollideHullSphere( mo, i, j );
            return;
        }

        if ( kindI == 0.0 && kindJ == 2.0 )
        {
            CollideHullSphere( mo, j, i );
            FlipManifoldAB( mo, LoadV3( j, 53 ) - LoadV3( i, 53 ) );
            return;
        }

        if ( kindI == 2.0 && kindJ == 2.0 )
        {
            CollideHullHull( mo, i, j );
            return;
        }

        if ( kindI == 3.0 && kindJ == 0.0 )
        {
            CollideMeshSphere( mo, j, float3( 0.0, 0.0, 0.0 ), 0.0 );
            if ( (int)i != MeshBody && (int)Manifolds[mo + 3] >= 1 )
            {
                float3 n0 = float3( Manifolds[mo], Manifolds[mo + 1], Manifolds[mo + 2] );
                CollideMeshSphere( mo, j, n0, 1.0 );
            }
            return;
        }

        if ( kindI == 3.0 && kindJ == 2.0 )
        {
            CollideMeshHull( mo, j, float3( 0.0, 0.0, 0.0 ), 0.0 );
            if ( (int)i != MeshBody && (int)Manifolds[mo + 3] >= 1 )
            {
                float3 n0 = float3( Manifolds[mo], Manifolds[mo + 1], Manifolds[mo + 2] );
                CollideMeshHull( mo, j, n0, 1.0 );
            }
            return;
        }

        if ( kindI == 1.0 && kindJ == 1.0 )
        {
            CollideCapsuleCapsule( mo, i, j );
            return;
        }

        if ( kindI == 1.0 && kindJ == 0.0 )
        {
            CollideCapsuleSphere( mo, i, j );
            return;
        }

        if ( kindI == 0.0 && kindJ == 1.0 )
        {
            CollideCapsuleSphere( mo, j, i );
            FlipManifoldAB( mo, LoadV3( j, 53 ) - LoadV3( i, 53 ) );
            return;
        }

        if ( kindI == 2.0 && kindJ == 1.0 )
        {
            CollideHullCapsule( mo, i, j );
            return;
        }

        if ( kindI == 1.0 && kindJ == 2.0 )
        {
            CollideHullCapsule( mo, j, i );
            FlipManifoldAB( mo, LoadV3( j, 53 ) - LoadV3( i, 53 ) );
            return;
        }

        if ( kindI == 3.0 && kindJ == 1.0 )
        {
            CollideMeshCapsule( mo, j, float3( 0.0, 0.0, 0.0 ), 0.0 );
            if ( (int)i != MeshBody && (int)Manifolds[mo + 3] >= 1 )
            {
                float3 n0 = float3( Manifolds[mo], Manifolds[mo + 1], Manifolds[mo + 2] );
                CollideMeshCapsule( mo, j, n0, 1.0 );
            }
            return;
        }

        if ( kindI == 4.0 )
        {
            if ( kindJ == 0.0 )
                CollideVoxSphere( mo, j );
            else if ( kindJ == 1.0 )
                CollideVoxCapsule( mo, j );
            else if ( kindJ == 2.0 )
                CollideVoxHull( mo, j );
            else
                Manifolds[mo + 3] = 0.0;
            return;
        }

        Manifolds[mo + 3] = -1.0;
    }

    void GetSolverBody( uint b, out float mass, out M3 inertia, out float3 v, out float3 w, out bool dynamic )
    {
        dynamic = Bodies[b * B3_STRIDE + 52] == 2.0 && !IsAsleep( b );
        if ( dynamic )
        {
            mass = Bodies[b * B3_STRIDE + 20];
            inertia = LoadM3( b, 36 );
            v = LoadV3( b, 7 );
            w = LoadV3( b, 10 );
        }
        else
        {
            mass = 0.0;
            inertia.cx = float3( 0.0, 0.0, 0.0 );
            inertia.cy = float3( 0.0, 0.0, 0.0 );
            inertia.cz = float3( 0.0, 0.0, 0.0 );
            v = float3( 0.0, 0.0, 0.0 );
            w = float3( 0.0, 0.0, 0.0 );
        }
    }

    M3 AddM3( M3 a, M3 b )
    {
        M3 o;
        o.cx = a.cx + b.cx;
        o.cy = a.cy + b.cy;
        o.cz = a.cz + b.cz;
        return o;
    }

    float3 B3PerpV( float3 a )
    {
        float3 p;
        if ( a.x < -0.5 || 0.5 < a.x )
            p = float3( a.y, -a.x, 0.0 );
        else
            p = float3( 0.0, a.z, -a.y );

        return B3NormalizeV( p );
    }

    void StoreV3Raw( uint k, float3 v )
    {
        Constraints[k] = v.x;
        Constraints[k + 1] = v.y;
        Constraints[k + 2] = v.z;
    }

    void SolveJoint( uint jo )
    {
        uint a = (uint)HashData[jo];
        uint b = (uint)HashData[jo + 1];

        float mA;
        M3 iA;
        float3 vA;
        float3 wA;
        bool dynA;
        GetSolverBody( a, mA, iA, vA, wA, dynA );

        float mB;
        M3 iB;
        float3 vB;
        float3 wB;
        bool dynB;
        GetSolverBody( b, mB, iB, vB, wB, dynB );

        if ( !dynA && !dynB )
            return;

        float4 qA0 = LoadQ( a, 3 );
        float4 qB0 = LoadQ( b, 3 );
        float4 dqA = dynA ? LoadQ( a, 16 ) : float4( 0.0, 0.0, 0.0, 1.0 );
        float4 dqB = dynB ? LoadQ( b, 16 ) : float4( 0.0, 0.0, 0.0, 1.0 );
        float3 dpA = dynA ? LoadV3( a, 13 ) : float3( 0.0, 0.0, 0.0 );
        float3 dpB = dynB ? LoadV3( b, 13 ) : float3( 0.0, 0.0, 0.0 );

        float3 lA = float3( HashData[jo + 2], HashData[jo + 3], HashData[jo + 4] );
        float3 lB = float3( HashData[jo + 5], HashData[jo + 6], HashData[jo + 7] );

        float3 rA = B3RotateVector( dqA, B3RotateVector( qA0, lA ) );
        float3 rB = B3RotateVector( dqB, B3RotateVector( qB0, lB ) );

        float3 pA0 = LoadV3( a, 0 );
        float3 pB0 = LoadV3( b, 0 );
        float3 C = ( pB0 + dpB + rB ) - ( pA0 + dpA + rA );

        if ( JointBreak > 0.0 && HashData[jo + 21] > 0.5 )
        {
            float3 sep = ( vB + cross( wB, rB ) ) - ( vA + cross( wA, rA ) );
            if ( dot( sep, sep ) > JointBreak * JointBreak || dot( C, C ) > 0.25 )
            {
                HashData[jo + 20] = 0.0;
                return;
            }
        }

        float3 ex = float3( 1.0, 0.0, 0.0 );
        float3 ey = float3( 0.0, 1.0, 0.0 );
        float3 ez = float3( 0.0, 0.0, 1.0 );

        M3 K;
        K.cx = ( mA + mB ) * ex - cross( rA, B3MulMV( iA, cross( rA, ex ) ) ) - cross( rB, B3MulMV( iB, cross( rB, ex ) ) );
        K.cy = ( mA + mB ) * ey - cross( rA, B3MulMV( iA, cross( rA, ey ) ) ) - cross( rB, B3MulMV( iB, cross( rB, ey ) ) );
        K.cz = ( mA + mB ) * ez - cross( rA, B3MulMV( iA, cross( rA, ez ) ) ) - cross( rB, B3MulMV( iB, cross( rB, ez ) ) );
        M3 Kinv = B3InvertMatrix( K );

        float3 Cdot = ( vB + cross( wB, rB ) ) - ( vA + cross( wA, rA ) );
        float3 impulse = -B3MulMV( Kinv, JointSoft.y * Cdot + JointSoft.y * JointSoft.x * C );

        vA -= mA * impulse;
        wA -= B3MulMV( iA, cross( rA, impulse ) );
        vB += mB * impulse;
        wB += B3MulMV( iB, cross( rB, impulse ) );

        float swingLimit = HashData[jo + 16];
        float twistEnable = HashData[jo + 19];

        if ( swingLimit > 0.0 || twistEnable > 0.5 )
        {
            float4 fqA = float4( HashData[jo + 8], HashData[jo + 9], HashData[jo + 10], HashData[jo + 11] );
            float4 fqB = float4( HashData[jo + 12], HashData[jo + 13], HashData[jo + 14], HashData[jo + 15] );
            float4 qtA = B3MulQuat( dqA, B3MulQuat( qA0, fqA ) );
            float4 qtB = B3MulQuat( dqB, B3MulQuat( qB0, fqB ) );
            float3 axisA = B3RotateVector( qtA, ex );
            float3 axisB = B3RotateVector( qtB, ex );

            if ( swingLimit > 0.0 )
            {
                float cosAng = clamp( dot( axisA, axisB ), -1.0, 1.0 );
                float ang = acos( cosAng );
                if ( ang > swingLimit )
                {
                    float3 u = cross( axisA, axisB );
                    float ul = length( u );
                    if ( ul > 1e-6 )
                    {
                        u /= ul;
                        float km = dot( u, B3MulMV( iA, u ) ) + dot( u, B3MulMV( iB, u ) );
                        if ( km > 1e-9 )
                        {
                            float cd = dot( wB - wA, u );
                            float lambda = -( JointSoft.y * cd + JointSoft.y * JointSoft.x * ( ang - swingLimit ) ) / km;
                            lambda = min( lambda, 0.0 );
                            wA -= B3MulMV( iA, u * lambda );
                            wB += B3MulMV( iB, u * lambda );
                        }
                    }
                }
            }

            if ( twistEnable > 0.5 )
            {
                float4 qRel = B3InvMulQuat( qtA, qtB );
                float twist = 2.0 * atan2( qRel.x, qRel.w );
                float tMin = HashData[jo + 17];
                float tMax = HashData[jo + 18];
                float km = dot( axisA, B3MulMV( iA, axisA ) ) + dot( axisA, B3MulMV( iB, axisA ) );
                if ( km > 1e-9 )
                {
                    if ( twist > tMax )
                    {
                        float cd = dot( wB - wA, axisA );
                        float lambda = -( JointSoft.y * cd + JointSoft.y * JointSoft.x * ( twist - tMax ) ) / km;
                        lambda = min( lambda, 0.0 );
                        wA -= B3MulMV( iA, axisA * lambda );
                        wB += B3MulMV( iB, axisA * lambda );
                    }
                    else if ( twist < tMin )
                    {
                        float cd = dot( wB - wA, axisA );
                        float lambda = -( JointSoft.y * cd + JointSoft.y * JointSoft.x * ( twist - tMin ) ) / km;
                        lambda = max( lambda, 0.0 );
                        wA -= B3MulMV( iA, axisA * lambda );
                        wB += B3MulMV( iB, axisA * lambda );
                    }
                }
            }
        }

        if ( dynA )
        {
            StoreV3( a, 7, vA );
            StoreV3( a, 10, wA );
        }

        if ( dynB )
        {
            StoreV3( b, 7, vB );
            StoreV3( b, 10, wB );
        }
    }

    void PrepareConstraint( uint p )
    {
        uint a = Pairs[p * 2];
        uint b = Pairs[p * 2 + 1];
        uint mo = p * MANIFOLD_STRIDE;
        uint co = p * CON_STRIDE;
        uint wo = p * WARM_STRIDE;

        float mA;
        M3 iA;
        float3 vA;
        float3 wA;
        bool dynA;
        GetSolverBody( a, mA, iA, vA, wA, dynA );

        float mB;
        M3 iB;
        float3 vB;
        float3 wB;
        bool dynB;
        GetSolverBody( b, mB, iB, vB, wB, dynB );

        int pointCount = (int)Manifolds[mo + 3];
        float3 normal = float3( Manifolds[mo], Manifolds[mo + 1], Manifolds[mo + 2] );
        float3 tangent1 = B3PerpV( normal );
        float3 tangent2 = cross( tangent1, normal );

        float fA = Bodies[a * B3_STRIDE + 61];
        float fB = Bodies[b * B3_STRIDE + 61];
        float rA0 = Bodies[a * B3_STRIDE + 62];
        float rB0 = Bodies[b * B3_STRIDE + 62];

        Constraints[co] = (float)a;
        Constraints[co + 1] = (float)b;
        Constraints[co + 2] = ( dynA && dynB ) ? 0.0 : 1.0;
        Constraints[co + 3] = (float)pointCount;
        StoreV3Raw( co + 4, normal );
        StoreV3Raw( co + 7, tangent1 );
        StoreV3Raw( co + 10, tangent2 );
        Constraints[co + 33] = sqrt( fA * fB );
        Constraints[co + 34] = max( rA0, rB0 );

        M3 sumI = AddM3( iA, iB );
        M3 rollingMass = B3InvertMatrix( sumI );
        Constraints[co + 24] = rollingMass.cx.x;
        Constraints[co + 25] = rollingMass.cx.y;
        Constraints[co + 26] = rollingMass.cx.z;
        Constraints[co + 27] = rollingMass.cy.x;
        Constraints[co + 28] = rollingMass.cy.y;
        Constraints[co + 29] = rollingMass.cy.z;
        Constraints[co + 30] = rollingMass.cz.x;
        Constraints[co + 31] = rollingMass.cz.y;
        Constraints[co + 32] = rollingMass.cz.z;

        float3 pA = LoadV3( a, 53 );
        float3 pB = LoadV3( b, 53 );

        float invTau = 1.0 / ( 4.0 * 0.005 );
        float3 centerA = float3( 0.0, 0.0, 0.0 );
        float3 centerB = float3( 0.0, 0.0, 0.0 );
        float totalFrictionWeight = 0.0;

        [loop]
        for ( int pt = 0; pt < pointCount; ++pt )
        {
            uint ko = mo + 4 + (uint)pt * 5;
            uint po = co + 41 + (uint)pt * 12;

            float3 anchorA = float3( Manifolds[ko], Manifolds[ko + 1], Manifolds[ko + 2] );
            float s = Manifolds[ko + 3];
            float3 anchorB = anchorA + ( pA - pB );

            StoreV3Raw( po, anchorA );
            StoreV3Raw( po + 3, anchorB );
            Constraints[po + 6] = s - dot( anchorB - anchorA, normal );
            Constraints[po + 9] = WarmImpulses[wo + (uint)pt];
            Constraints[po + 10] = 0.0;

            float3 rnA = cross( anchorA, normal );
            float3 rnB = cross( anchorB, normal );
            float kNormal = mA + mB + dot( rnA, B3MulMV( iA, rnA ) ) + dot( rnB, B3MulMV( iB, rnB ) );
            Constraints[po + 7] = kNormal > 0.0 ? 1.0 / kNormal : 0.0;

            float3 vrA = vA + cross( wA, anchorA );
            float3 vrB = vB + cross( wB, anchorB );
            Constraints[po + 8] = dot( normal, vrB - vrA );

            float weight = clamp( 2.0 - s * invTau, 1e-10, 1.0 );
            centerA = centerA + weight * anchorA;
            centerB = centerB + weight * anchorB;
            totalFrictionWeight += weight;
        }

        float invWeight = 1.0 / totalFrictionWeight;
        centerA = invWeight * centerA;
        centerB = invWeight * centerB;
        StoreV3Raw( co + 13, centerA );
        StoreV3Raw( co + 16, centerB );

        [loop]
        for ( int pt2 = 0; pt2 < pointCount; ++pt2 )
        {
            uint po = co + 41 + (uint)pt2 * 12;
            float3 anchorA = float3( Constraints[po], Constraints[po + 1], Constraints[po + 2] );
            float3 d = anchorA - centerA;
            Constraints[po + 11] = sqrt( dot( d, d ) );
        }

        float3 rtA1 = cross( centerA, tangent1 );
        float3 rtA2 = cross( centerA, tangent2 );
        float3 rtB1 = cross( centerB, tangent1 );
        float3 rtB2 = cross( centerB, tangent2 );

        float kxx = mA + mB + dot( rtA1, B3MulMV( iA, rtA1 ) ) + dot( rtB1, B3MulMV( iB, rtB1 ) );
        float kyy = mA + mB + dot( rtA2, B3MulMV( iA, rtA2 ) ) + dot( rtB2, B3MulMV( iB, rtB2 ) );
        float kxy = dot( rtA1, B3MulMV( iA, rtA2 ) ) + dot( rtB1, B3MulMV( iB, rtB2 ) );

        float det2 = kxx * kyy - kxy * kxy;
        float4 tangentMass = float4( 0.0, 0.0, 0.0, 0.0 );
        if ( abs( det2 ) > 1000.0 * B3_FLT_MIN )
        {
            float invDet = 1.0 / det2;
            tangentMass = float4( invDet * kyy, -invDet * kxy, -invDet * kxy, invDet * kxx );
        }

        Constraints[co + 19] = tangentMass.x;
        Constraints[co + 20] = tangentMass.y;
        Constraints[co + 21] = tangentMass.z;
        Constraints[co + 22] = tangentMass.w;

        float3 warmFriction = float3( WarmImpulses[wo + 4], WarmImpulses[wo + 5], WarmImpulses[wo + 6] );
        Constraints[co + 35] = dot( warmFriction, tangent1 );
        Constraints[co + 36] = dot( warmFriction, tangent2 );

        float kTwist = dot( normal, B3MulMV( sumI, normal ) );
        Constraints[co + 23] = kTwist > 0.0 ? 1.0 / kTwist : 0.0;
        Constraints[co + 37] = WarmImpulses[wo + 7];
        Constraints[co + 38] = WarmImpulses[wo + 8];
        Constraints[co + 39] = WarmImpulses[wo + 9];
        Constraints[co + 40] = WarmImpulses[wo + 10];
    }

    void WarmStartConstraint( uint p )
    {
        uint co = p * CON_STRIDE;
        uint a = (uint)Constraints[co];
        uint b = (uint)Constraints[co + 1];

        float mA;
        M3 iA;
        float3 vA;
        float3 wA;
        bool dynA;
        GetSolverBody( a, mA, iA, vA, wA, dynA );

        float mB;
        M3 iB;
        float3 vB;
        float3 wB;
        bool dynB;
        GetSolverBody( b, mB, iB, vB, wB, dynB );

        int pointCount = (int)Constraints[co + 3];
        float3 normal = float3( Constraints[co + 4], Constraints[co + 5], Constraints[co + 6] );
        float3 tangent1 = float3( Constraints[co + 7], Constraints[co + 8], Constraints[co + 9] );
        float3 tangent2 = float3( Constraints[co + 10], Constraints[co + 11], Constraints[co + 12] );

        [loop]
        for ( int pt = 0; pt < pointCount; ++pt )
        {
            uint po = co + 41 + (uint)pt * 12;
            float3 rA = float3( Constraints[po], Constraints[po + 1], Constraints[po + 2] );
            float3 rB = float3( Constraints[po + 3], Constraints[po + 4], Constraints[po + 5] );
            float3 impulse = Constraints[po + 9] * normal;
            wA = wA - B3MulMV( iA, cross( rA, impulse ) );
            vA = vA - mA * impulse;
            wB = wB + B3MulMV( iB, cross( rB, impulse ) );
            vB = vB + mB * impulse;
        }

        {
            float3 rA = float3( Constraints[co + 13], Constraints[co + 14], Constraints[co + 15] );
            float3 rB = float3( Constraints[co + 16], Constraints[co + 17], Constraints[co + 18] );
            float3 impulse = Constraints[co + 35] * tangent1 + Constraints[co + 36] * tangent2;
            wA = wA - B3MulMV( iA, cross( rA, impulse ) );
            vA = vA - mA * impulse;
            wB = wB + B3MulMV( iB, cross( rB, impulse ) );
            vB = vB + mB * impulse;
        }

        {
            float3 impulse = Constraints[co + 37] * normal;
            wA = wA - B3MulMV( iA, impulse );
            wB = wB + B3MulMV( iB, impulse );
        }

        {
            float3 impulse = float3( Constraints[co + 38], Constraints[co + 39], Constraints[co + 40] );
            wA = wA - B3MulMV( iA, impulse );
            wB = wB + B3MulMV( iB, impulse );
        }

        if ( dynA )
        {
            StoreV3( a, 7, vA );
            StoreV3( a, 10, wA );
        }

        if ( dynB )
        {
            StoreV3( b, 7, vB );
            StoreV3( b, 10, wB );
        }
    }

    void SolveConstraint( uint p, bool useBias )
    {
        uint co = p * CON_STRIDE;
        uint a = (uint)Constraints[co];
        uint b = (uint)Constraints[co + 1];

        float mA;
        M3 iA;
        float3 vA;
        float3 wA;
        bool dynA;
        GetSolverBody( a, mA, iA, vA, wA, dynA );

        float mB;
        M3 iB;
        float3 vB;
        float3 wB;
        bool dynB;
        GetSolverBody( b, mB, iB, vB, wB, dynB );

        float4 dqA = dynA ? LoadQ( a, 16 ) : float4( 0.0, 0.0, 0.0, 1.0 );
        float4 dqB = dynB ? LoadQ( b, 16 ) : float4( 0.0, 0.0, 0.0, 1.0 );
        float3 dpA = dynA ? LoadV3( a, 13 ) : float3( 0.0, 0.0, 0.0 );
        float3 dpB = dynB ? LoadV3( b, 13 ) : float3( 0.0, 0.0, 0.0 );
        float3 dp = dpB - dpA;

        float3 soft = Constraints[co + 2] != 0.0 ? StaticSoft : ContactSoft;
        float friction = Constraints[co + 33];

        int pointCount = (int)Constraints[co + 3];
        float3 normal = float3( Constraints[co + 4], Constraints[co + 5], Constraints[co + 6] );

        float totalNormalImpulse = 0.0;
        float totalTwistLimit = 0.0;

        [loop]
        for ( int pt = 0; pt < pointCount; ++pt )
        {
            uint po = co + 41 + (uint)pt * 12;
            float3 rA = float3( Constraints[po], Constraints[po + 1], Constraints[po + 2] );
            float3 rB = float3( Constraints[po + 3], Constraints[po + 4], Constraints[po + 5] );

            float3 ds = dp + ( B3RotateVector( dqB, rB ) - B3RotateVector( dqA, rA ) );
            float s = dot( ds, normal ) + Constraints[po + 6];

            float velocityBias = 0.0;
            float massScale = 1.0;
            float impulseScale = 0.0;
            if ( s > 0.0 )
            {
                velocityBias = s * InvH;
            }
            else if ( useBias )
            {
                velocityBias = max( soft.y * soft.x * s, -ContactSpeed );
                massScale = soft.y;
                impulseScale = soft.z;
            }

            float3 vrA = vA + cross( wA, rA );
            float3 vrB = vB + cross( wB, rB );
            float vn = dot( vrB - vrA, normal );

            float normalImpulse = Constraints[po + 9];
            float deltaImpulse = -Constraints[po + 7] * ( massScale * vn + velocityBias ) - impulseScale * normalImpulse;

            float newImpulse = max( normalImpulse + deltaImpulse, 0.0 );
            deltaImpulse = newImpulse - normalImpulse;
            Constraints[po + 9] = newImpulse;
            Constraints[po + 10] += newImpulse;

            totalNormalImpulse += newImpulse;
            totalTwistLimit += Constraints[po + 11] * newImpulse;

            float3 P = deltaImpulse * normal;
            vA = vA - mA * P;
            wA = wA - B3MulMV( iA, cross( rA, P ) );
            vB = vB + mB * P;
            wB = wB + B3MulMV( iB, cross( rB, P ) );
        }

        if ( !useBias )
        {
            {
                float twistSpeed = dot( normal, wB - wA );
                float maxImpulse = friction * totalTwistLimit;
                float deltaImpulse = -Constraints[co + 23] * twistSpeed;
                float oldImpulse = Constraints[co + 37];
                float newTwist = clamp( oldImpulse + deltaImpulse, -maxImpulse, maxImpulse );
                Constraints[co + 37] = newTwist;
                deltaImpulse = newTwist - oldImpulse;

                wA = wA - B3MulMV( iA, deltaImpulse * normal );
                wB = wB + B3MulMV( iB, deltaImpulse * normal );
            }

            {
                float3 tangent1 = float3( Constraints[co + 7], Constraints[co + 8], Constraints[co + 9] );
                float3 tangent2 = float3( Constraints[co + 10], Constraints[co + 11], Constraints[co + 12] );

                float3 rA = float3( Constraints[co + 13], Constraints[co + 14], Constraints[co + 15] );
                float3 rB = float3( Constraints[co + 16], Constraints[co + 17], Constraints[co + 18] );

                float3 vrA = vA + cross( wA, rA );
                float3 vrB = vB + cross( wB, rB );
                float3 vr = vrB - vrA;
                float2 vt = float2( dot( vr, tangent1 ), dot( vr, tangent2 ) );

                float2 tm = float2(
                    Constraints[co + 19] * vt.x + Constraints[co + 21] * vt.y,
                    Constraints[co + 20] * vt.x + Constraints[co + 22] * vt.y );
                float2 deltaImpulse = float2( -tm.x, -tm.y );
                float2 oldFriction = float2( Constraints[co + 35], Constraints[co + 36] );
                float2 newImpulse = oldFriction + deltaImpulse;

                float maxImpulse = friction * totalNormalImpulse;

                float lengthSquared = dot( newImpulse, newImpulse );
                if ( lengthSquared > maxImpulse * maxImpulse )
                {
                    float scale = maxImpulse / sqrt( lengthSquared );
                    newImpulse.x *= scale;
                    newImpulse.y *= scale;
                }

                deltaImpulse = newImpulse - oldFriction;
                Constraints[co + 35] = newImpulse.x;
                Constraints[co + 36] = newImpulse.y;

                float3 P = float3(
                    deltaImpulse.x * tangent1.x + deltaImpulse.y * tangent2.x,
                    deltaImpulse.x * tangent1.y + deltaImpulse.y * tangent2.y,
                    deltaImpulse.x * tangent1.z + deltaImpulse.y * tangent2.z );
                vA = vA - mA * P;
                wA = wA - B3MulMV( iA, cross( rA, P ) );
                vB = vB + mB * P;
                wB = wB + B3MulMV( iB, cross( rB, P ) );
            }
        }

        if ( dynA )
        {
            StoreV3( a, 7, vA );
            StoreV3( a, 10, wA );
        }

        if ( dynB )
        {
            StoreV3( b, 7, vB );
            StoreV3( b, 10, wB );
        }
    }

    void RestitutionConstraint( uint p )
    {
        uint co = p * CON_STRIDE;
        float restitution = Constraints[co + 34];
        if ( restitution == 0.0 )
            return;

        uint a = (uint)Constraints[co];
        uint b = (uint)Constraints[co + 1];

        float mA;
        M3 iA;
        float3 vA;
        float3 wA;
        bool dynA;
        GetSolverBody( a, mA, iA, vA, wA, dynA );

        float mB;
        M3 iB;
        float3 vB;
        float3 wB;
        bool dynB;
        GetSolverBody( b, mB, iB, vB, wB, dynB );

        int pointCount = (int)Constraints[co + 3];
        float3 normal = float3( Constraints[co + 4], Constraints[co + 5], Constraints[co + 6] );

        [loop]
        for ( int pt = 0; pt < pointCount; ++pt )
        {
            uint po = co + 41 + (uint)pt * 12;

            if ( Constraints[po + 8] > -RestitutionThreshold || Constraints[po + 10] == 0.0 )
                continue;

            float3 rA = float3( Constraints[po], Constraints[po + 1], Constraints[po + 2] );
            float3 rB = float3( Constraints[po + 3], Constraints[po + 4], Constraints[po + 5] );

            float3 vrB = vB + cross( wB, rB );
            float3 vrA = vA + cross( wA, rA );
            float vn = dot( vrB - vrA, normal );

            float normalImpulse = Constraints[po + 9];
            float impulse = -Constraints[po + 7] * ( vn + restitution * Constraints[po + 8] );

            float newImpulse = max( normalImpulse + impulse, 0.0 );
            impulse = newImpulse - normalImpulse;
            Constraints[po + 9] = newImpulse;
            Constraints[po + 10] += impulse;

            float3 P = impulse * normal;
            vA = vA - mA * P;
            wA = wA - B3MulMV( iA, cross( rA, P ) );
            vB = vB + mB * P;
            wB = wB + B3MulMV( iB, cross( rB, P ) );
        }

        if ( dynA )
        {
            StoreV3( a, 7, vA );
            StoreV3( a, 10, wA );
        }

        if ( dynB )
        {
            StoreV3( b, 7, vB );
            StoreV3( b, 10, wB );
        }
    }

    #define COLOR_SLOTS 16
    #define HASH_EMPTY 0xFFFFFFFFu
    #define HASH_PROBES 16

    uint PairHashKey( uint a, uint b )
    {
        return ( a * 2654435761u ) ^ ( b * 40503u ) ^ 0x9E3779B9u;
    }

    void ColoringRound( uint p )
    {
        uint co = p * CON_STRIDE;
        if ( (int)Manifolds[p * MANIFOLD_STRIDE + 3] < 1 )
            return;

        if ( Constraints[co + 63] >= 0.0 )
            return;

        uint a = Pairs[p * 2];
        uint b = Pairs[p * 2 + 1];
        bool dynA = Bodies[a * B3_STRIDE + 52] == 2.0;
        bool dynB = Bodies[b * B3_STRIDE + 52] == 2.0;

        uint used = 0;
        if ( dynA )
            used |= BodyColorMask[a];
        if ( dynB )
            used |= BodyColorMask[b];

        int c = 0;
        while ( c < COLOR_SLOTS && ( used & ( 1u << c ) ) != 0 )
            c++;

        if ( c >= COLOR_SLOTS )
        {
            InterlockedOr( BpCounters[4], 1u );
            return;
        }

        uint bit = 1u << c;

        if ( dynA )
        {
            uint oldA;
            InterlockedOr( BodyColorMask[a], bit, oldA );
            if ( ( oldA & bit ) != 0 )
                return;
        }

        if ( dynB )
        {
            uint oldB;
            InterlockedOr( BodyColorMask[b], bit, oldB );
            if ( ( oldB & bit ) != 0 )
            {
                if ( dynA )
                    InterlockedAnd( BodyColorMask[a], ~bit );
                return;
            }
        }

        Constraints[co + 63] = (float)c;
        uint slot;
        InterlockedAdd( BpCounters[8 + (uint)c], 1u, slot );
        ColorList[(uint)c * (uint)SegCap + slot] = p;
    }

    void HashLookupWarm( uint p )
    {
        uint a = Pairs[p * 2];
        uint b = Pairs[p * 2 + 1];
        uint key = PairHashKey( a, b );
        uint mask = (uint)HashCap - 1u;
        uint wo = p * WARM_STRIDE;

        [loop]
        for ( int k = 0; k < WARM_STRIDE; ++k )
            WarmImpulses[wo + (uint)k] = 0.0;

        uint h = key & mask;

        [loop]
        for ( int probe = 0; probe < HASH_PROBES; ++probe )
        {
            uint slot = ( h + (uint)probe ) & mask;
            uint stored = HashKeys[slot];
            if ( stored == HASH_EMPTY )
                return;

            if ( stored == key )
            {
                uint ho = slot * 16u;
                int mo = (int)( p * MANIFOLD_STRIDE );
                int gpuCount = (int)Manifolds[mo + 3];
                int prevCount = (int)HashData[ho + 15];

                [loop]
                for ( int gk = 0; gk < gpuCount; ++gk )
                {
                    uint fid = asuint( Manifolds[mo + 4 + gk * 5 + 4] );

                    [loop]
                    for ( int op = 0; op < prevCount; ++op )
                    {
                        if ( asuint( HashData[ho + (uint)op] ) == fid )
                        {
                            WarmImpulses[wo + (uint)gk] = HashData[ho + 4u + (uint)op];
                            break;
                        }
                    }
                }

                WarmImpulses[wo + 4] = HashData[ho + 8];
                WarmImpulses[wo + 5] = HashData[ho + 9];
                WarmImpulses[wo + 6] = HashData[ho + 10];
                WarmImpulses[wo + 7] = HashData[ho + 11];
                WarmImpulses[wo + 8] = HashData[ho + 12];
                WarmImpulses[wo + 9] = HashData[ho + 13];
                WarmImpulses[wo + 10] = HashData[ho + 14];
                return;
            }
        }
    }

    void HashStore( uint p )
    {
        uint a = Pairs[p * 2];
        uint b = Pairs[p * 2 + 1];
        uint key = PairHashKey( a, b );
        uint mask = (uint)HashCap - 1u;
        uint co = p * CON_STRIDE;
        int mo = (int)( p * MANIFOLD_STRIDE );
        int count = (int)Constraints[co + 3];

        uint h = key & mask;

        [loop]
        for ( int probe = 0; probe < HASH_PROBES; ++probe )
        {
            uint slot = ( h + (uint)probe ) & mask;
            uint old;
            InterlockedCompareExchange( HashKeys[slot], HASH_EMPTY, key, old );
            if ( old == HASH_EMPTY || old == key )
            {
                uint ho = slot * 16u;

                [loop]
                for ( int k = 0; k < 4; ++k )
                {
                    if ( k < count )
                    {
                        HashData[ho + (uint)k] = Manifolds[mo + 4 + k * 5 + 4];
                        HashData[ho + 4u + (uint)k] = Constraints[co + 41 + (uint)k * 12 + 9];
                    }
                    else
                    {
                        HashData[ho + (uint)k] = asfloat( HASH_EMPTY );
                        HashData[ho + 4u + (uint)k] = 0.0;
                    }
                }

                float3 t1 = float3( Constraints[co + 7], Constraints[co + 8], Constraints[co + 9] );
                float3 t2 = float3( Constraints[co + 10], Constraints[co + 11], Constraints[co + 12] );
                float fx = Constraints[co + 35];
                float fy = Constraints[co + 36];
                HashData[ho + 8] = fx * t1.x + fy * t2.x;
                HashData[ho + 9] = fx * t1.y + fy * t2.y;
                HashData[ho + 10] = fx * t1.z + fy * t2.z;
                HashData[ho + 11] = Constraints[co + 37];
                HashData[ho + 12] = Constraints[co + 38];
                HashData[ho + 13] = Constraints[co + 39];
                HashData[ho + 14] = Constraints[co + 40];
                HashData[ho + 15] = (float)count;
                return;
            }
        }
    }

    #define JC_STRIDE 12
    #define BODY_LIST_CAP 32

    void BuildBodyList( uint p )
    {
        if ( (int)Manifolds[p * MANIFOLD_STRIDE + 3] < 1 )
            return;

        uint a = Pairs[p * 2];
        uint b = Pairs[p * 2 + 1];

        if ( Bodies[a * B3_STRIDE + 52] == 2.0 && !IsAsleep( a ) )
        {
            uint sa;
            InterlockedAdd( BodyColorMask[a], 1u, sa );
            if ( sa < BODY_LIST_CAP )
                ColorList[a * BODY_LIST_CAP + sa] = p;
            else
                InterlockedOr( BpCounters[6], 1u );
        }

        if ( Bodies[b * B3_STRIDE + 52] == 2.0 && !IsAsleep( b ) )
        {
            uint sb;
            InterlockedAdd( BodyColorMask[b], 1u, sb );
            if ( sb < BODY_LIST_CAP )
                ColorList[b * BODY_LIST_CAP + sb] = p;
            else
                InterlockedOr( BpCounters[6], 1u );
        }
    }

    void GetSplitBody( uint b, out float mass, out M3 inertia, out float3 v, out float3 w, out bool dynamic )
    {
        GetSolverBody( b, mass, inertia, v, w, dynamic );
        if ( dynamic )
        {
            float deg = max( 1.0, (float)BodyColorMask[b] );
            mass *= deg;
            inertia.cx *= deg;
            inertia.cy *= deg;
            inertia.cz *= deg;
        }
    }

    void PrepareJacobi( uint p )
    {
        uint a = Pairs[p * 2];
        uint b = Pairs[p * 2 + 1];
        uint mo = p * MANIFOLD_STRIDE;
        uint co = p * CON_STRIDE;
        uint wo = p * WARM_STRIDE;

        float mA;
        M3 iA;
        float3 vA;
        float3 wA;
        bool dynA;
        GetSplitBody( a, mA, iA, vA, wA, dynA );

        float mB;
        M3 iB;
        float3 vB;
        float3 wB;
        bool dynB;
        GetSplitBody( b, mB, iB, vB, wB, dynB );

        int pointCount = (int)Manifolds[mo + 3];
        float3 normal = float3( Manifolds[mo], Manifolds[mo + 1], Manifolds[mo + 2] );
        float3 tangent1 = B3PerpV( normal );
        float3 tangent2 = cross( tangent1, normal );

        float fA = Bodies[a * B3_STRIDE + 61];
        float fB = Bodies[b * B3_STRIDE + 61];
        float rA0 = Bodies[a * B3_STRIDE + 62];
        float rB0 = Bodies[b * B3_STRIDE + 62];

        Constraints[co] = (float)a;
        Constraints[co + 1] = (float)b;
        Constraints[co + 2] = ( dynA && dynB ) ? 0.0 : 1.0;
        Constraints[co + 3] = (float)pointCount;
        StoreV3Raw( co + 4, normal );
        StoreV3Raw( co + 7, tangent1 );
        StoreV3Raw( co + 10, tangent2 );
        Constraints[co + 33] = sqrt( fA * fB );
        Constraints[co + 34] = max( rA0, rB0 );

        M3 sumI = AddM3( iA, iB );
        M3 rollingMass = B3InvertMatrix( sumI );
        Constraints[co + 24] = rollingMass.cx.x;
        Constraints[co + 25] = rollingMass.cx.y;
        Constraints[co + 26] = rollingMass.cx.z;
        Constraints[co + 27] = rollingMass.cy.x;
        Constraints[co + 28] = rollingMass.cy.y;
        Constraints[co + 29] = rollingMass.cy.z;
        Constraints[co + 30] = rollingMass.cz.x;
        Constraints[co + 31] = rollingMass.cz.y;
        Constraints[co + 32] = rollingMass.cz.z;

        float3 pA = LoadV3( a, 53 );
        float3 pB = LoadV3( b, 53 );

        float invTau = 1.0 / ( 4.0 * 0.005 );
        float3 centerA = float3( 0.0, 0.0, 0.0 );
        float3 centerB = float3( 0.0, 0.0, 0.0 );
        float totalFrictionWeight = 0.0;

        [loop]
        for ( int pt = 0; pt < pointCount; ++pt )
        {
            uint ko = mo + 4 + (uint)pt * 5;
            uint po = co + 41 + (uint)pt * 12;

            float3 anchorA = float3( Manifolds[ko], Manifolds[ko + 1], Manifolds[ko + 2] );
            float s = Manifolds[ko + 3];
            float3 anchorB = anchorA + ( pA - pB );

            StoreV3Raw( po, anchorA );
            StoreV3Raw( po + 3, anchorB );
            Constraints[po + 6] = s - dot( anchorB - anchorA, normal );
            Constraints[po + 9] = WarmImpulses[wo + (uint)pt];
            Constraints[po + 10] = 0.0;

            float3 rnA = cross( anchorA, normal );
            float3 rnB = cross( anchorB, normal );
            float kNormal = mA + mB + dot( rnA, B3MulMV( iA, rnA ) ) + dot( rnB, B3MulMV( iB, rnB ) );
            Constraints[po + 7] = kNormal > 0.0 ? 1.0 / kNormal : 0.0;

            float3 vrA = vA + cross( wA, anchorA );
            float3 vrB = vB + cross( wB, anchorB );
            Constraints[po + 8] = dot( normal, vrB - vrA );

            float weight = clamp( 2.0 - s * invTau, 1e-10, 1.0 );
            centerA = centerA + weight * anchorA;
            centerB = centerB + weight * anchorB;
            totalFrictionWeight += weight;
        }

        float invWeight = 1.0 / totalFrictionWeight;
        centerA = invWeight * centerA;
        centerB = invWeight * centerB;
        StoreV3Raw( co + 13, centerA );
        StoreV3Raw( co + 16, centerB );

        [loop]
        for ( int pt2 = 0; pt2 < pointCount; ++pt2 )
        {
            uint po = co + 41 + (uint)pt2 * 12;
            float3 anchorA = float3( Constraints[po], Constraints[po + 1], Constraints[po + 2] );
            float3 d = anchorA - centerA;
            Constraints[po + 11] = sqrt( dot( d, d ) );
        }

        float3 rtA1 = cross( centerA, tangent1 );
        float3 rtA2 = cross( centerA, tangent2 );
        float3 rtB1 = cross( centerB, tangent1 );
        float3 rtB2 = cross( centerB, tangent2 );

        float kxx = mA + mB + dot( rtA1, B3MulMV( iA, rtA1 ) ) + dot( rtB1, B3MulMV( iB, rtB1 ) );
        float kyy = mA + mB + dot( rtA2, B3MulMV( iA, rtA2 ) ) + dot( rtB2, B3MulMV( iB, rtB2 ) );
        float kxy = dot( rtA1, B3MulMV( iA, rtA2 ) ) + dot( rtB1, B3MulMV( iB, rtB2 ) );

        float det2 = kxx * kyy - kxy * kxy;
        float4 tangentMass = float4( 0.0, 0.0, 0.0, 0.0 );
        if ( abs( det2 ) > 1000.0 * B3_FLT_MIN )
        {
            float invDet = 1.0 / det2;
            tangentMass = float4( invDet * kyy, -invDet * kxy, -invDet * kxy, invDet * kxx );
        }

        Constraints[co + 19] = tangentMass.x;
        Constraints[co + 20] = tangentMass.y;
        Constraints[co + 21] = tangentMass.z;
        Constraints[co + 22] = tangentMass.w;

        float3 warmFriction = float3( WarmImpulses[wo + 4], WarmImpulses[wo + 5], WarmImpulses[wo + 6] );
        Constraints[co + 35] = dot( warmFriction, tangent1 );
        Constraints[co + 36] = dot( warmFriction, tangent2 );

        float kTwist = dot( normal, B3MulMV( sumI, normal ) );
        Constraints[co + 23] = kTwist > 0.0 ? 1.0 / kTwist : 0.0;
        Constraints[co + 37] = WarmImpulses[wo + 7];
        Constraints[co + 38] = WarmImpulses[wo + 8];
        Constraints[co + 39] = WarmImpulses[wo + 9];
        Constraints[co + 40] = WarmImpulses[wo + 10];
    }

    void EmitContrib( uint p, float3 la, float3 aa, float3 lb, float3 ab )
    {
        uint jo = p * JC_STRIDE;
        WarmImpulses[jo] = la.x;
        WarmImpulses[jo + 1] = la.y;
        WarmImpulses[jo + 2] = la.z;
        WarmImpulses[jo + 3] = aa.x;
        WarmImpulses[jo + 4] = aa.y;
        WarmImpulses[jo + 5] = aa.z;
        WarmImpulses[jo + 6] = lb.x;
        WarmImpulses[jo + 7] = lb.y;
        WarmImpulses[jo + 8] = lb.z;
        WarmImpulses[jo + 9] = ab.x;
        WarmImpulses[jo + 10] = ab.y;
        WarmImpulses[jo + 11] = ab.z;
    }

    void WarmEmitJacobi( uint p )
    {
        uint co = p * CON_STRIDE;
        uint a = (uint)Constraints[co];
        uint b = (uint)Constraints[co + 1];

        int pointCount = (int)Constraints[co + 3];
        float3 normal = float3( Constraints[co + 4], Constraints[co + 5], Constraints[co + 6] );
        float3 tangent1 = float3( Constraints[co + 7], Constraints[co + 8], Constraints[co + 9] );
        float3 tangent2 = float3( Constraints[co + 10], Constraints[co + 11], Constraints[co + 12] );

        float3 la = float3( 0.0, 0.0, 0.0 );
        float3 aa = float3( 0.0, 0.0, 0.0 );
        float3 lb = float3( 0.0, 0.0, 0.0 );
        float3 ab = float3( 0.0, 0.0, 0.0 );

        [loop]
        for ( int pt = 0; pt < pointCount; ++pt )
        {
            uint po = co + 41 + (uint)pt * 12;
            float3 rA = float3( Constraints[po], Constraints[po + 1], Constraints[po + 2] );
            float3 rB = float3( Constraints[po + 3], Constraints[po + 4], Constraints[po + 5] );
            float3 impulse = Constraints[po + 9] * normal;
            la -= impulse;
            aa -= cross( rA, impulse );
            lb += impulse;
            ab += cross( rB, impulse );
        }

        {
            float3 rA = float3( Constraints[co + 13], Constraints[co + 14], Constraints[co + 15] );
            float3 rB = float3( Constraints[co + 16], Constraints[co + 17], Constraints[co + 18] );
            float3 impulse = Constraints[co + 35] * tangent1 + Constraints[co + 36] * tangent2;
            la -= impulse;
            aa -= cross( rA, impulse );
            lb += impulse;
            ab += cross( rB, impulse );
        }

        {
            float3 impulse = Constraints[co + 37] * normal;
            aa -= impulse;
            ab += impulse;
        }

        {
            float3 impulse = float3( Constraints[co + 38], Constraints[co + 39], Constraints[co + 40] );
            aa -= impulse;
            ab += impulse;
        }

        EmitContrib( p, la, aa, lb, ab );
    }

    void SolveJacobi( uint p, bool useBias )
    {
        uint co = p * CON_STRIDE;
        uint a = (uint)Constraints[co];
        uint b = (uint)Constraints[co + 1];

        float mA;
        M3 iA;
        float3 vA;
        float3 wA;
        bool dynA;
        GetSplitBody( a, mA, iA, vA, wA, dynA );

        float mB;
        M3 iB;
        float3 vB;
        float3 wB;
        bool dynB;
        GetSplitBody( b, mB, iB, vB, wB, dynB );

        float4 dqA = dynA ? LoadQ( a, 16 ) : float4( 0.0, 0.0, 0.0, 1.0 );
        float4 dqB = dynB ? LoadQ( b, 16 ) : float4( 0.0, 0.0, 0.0, 1.0 );
        float3 dpA = dynA ? LoadV3( a, 13 ) : float3( 0.0, 0.0, 0.0 );
        float3 dpB = dynB ? LoadV3( b, 13 ) : float3( 0.0, 0.0, 0.0 );
        float3 dp = dpB - dpA;

        float3 soft = Constraints[co + 2] != 0.0 ? StaticSoft : ContactSoft;
        float friction = Constraints[co + 33];

        int pointCount = (int)Constraints[co + 3];
        float3 normal = float3( Constraints[co + 4], Constraints[co + 5], Constraints[co + 6] );

        float3 la = float3( 0.0, 0.0, 0.0 );
        float3 aa = float3( 0.0, 0.0, 0.0 );
        float3 lb = float3( 0.0, 0.0, 0.0 );
        float3 ab = float3( 0.0, 0.0, 0.0 );

        float totalNormalImpulse = 0.0;
        float totalTwistLimit = 0.0;

        [loop]
        for ( int pt = 0; pt < pointCount; ++pt )
        {
            uint po = co + 41 + (uint)pt * 12;
            float3 rA = float3( Constraints[po], Constraints[po + 1], Constraints[po + 2] );
            float3 rB = float3( Constraints[po + 3], Constraints[po + 4], Constraints[po + 5] );

            float3 ds = dp + ( B3RotateVector( dqB, rB ) - B3RotateVector( dqA, rA ) );
            float s = dot( ds, normal ) + Constraints[po + 6];

            float velocityBias = 0.0;
            float massScale = 1.0;
            float impulseScale = 0.0;
            if ( s > 0.0 )
            {
                velocityBias = s * InvH;
            }
            else if ( useBias )
            {
                velocityBias = max( soft.y * soft.x * s, -ContactSpeed );
                massScale = soft.y;
                impulseScale = soft.z;
            }

            float3 vrA = vA + cross( wA, rA );
            float3 vrB = vB + cross( wB, rB );
            float vn = dot( vrB - vrA, normal );

            float normalImpulse = Constraints[po + 9];
            float deltaImpulse = -Constraints[po + 7] * ( massScale * vn + velocityBias ) - impulseScale * normalImpulse;

            float newImpulse = max( normalImpulse + deltaImpulse, 0.0 );
            deltaImpulse = newImpulse - normalImpulse;
            Constraints[po + 9] = newImpulse;
            Constraints[po + 10] += newImpulse;

            totalNormalImpulse += newImpulse;
            totalTwistLimit += Constraints[po + 11] * newImpulse;

            float3 P = deltaImpulse * normal;
            vA = vA - mA * P;
            wA = wA - B3MulMV( iA, cross( rA, P ) );
            vB = vB + mB * P;
            wB = wB + B3MulMV( iB, cross( rB, P ) );
            la -= P;
            aa -= cross( rA, P );
            lb += P;
            ab += cross( rB, P );
        }

        if ( !useBias )
        {
            {
                float twistSpeed = dot( normal, wB - wA );
                float maxImpulse = friction * totalTwistLimit;
                float deltaImpulse = -Constraints[co + 23] * twistSpeed;
                float oldImpulse = Constraints[co + 37];
                float newTwist = clamp( oldImpulse + deltaImpulse, -maxImpulse, maxImpulse );
                Constraints[co + 37] = newTwist;
                deltaImpulse = newTwist - oldImpulse;

                wA = wA - B3MulMV( iA, deltaImpulse * normal );
                wB = wB + B3MulMV( iB, deltaImpulse * normal );
                aa -= deltaImpulse * normal;
                ab += deltaImpulse * normal;
            }

            {
                float3 tangent1 = float3( Constraints[co + 7], Constraints[co + 8], Constraints[co + 9] );
                float3 tangent2 = float3( Constraints[co + 10], Constraints[co + 11], Constraints[co + 12] );

                float3 rA = float3( Constraints[co + 13], Constraints[co + 14], Constraints[co + 15] );
                float3 rB = float3( Constraints[co + 16], Constraints[co + 17], Constraints[co + 18] );

                float3 vrA = vA + cross( wA, rA );
                float3 vrB = vB + cross( wB, rB );
                float3 vr = vrB - vrA;
                float2 vt = float2( dot( vr, tangent1 ), dot( vr, tangent2 ) );

                float2 tm = float2(
                    Constraints[co + 19] * vt.x + Constraints[co + 21] * vt.y,
                    Constraints[co + 20] * vt.x + Constraints[co + 22] * vt.y );
                float2 deltaImpulse = float2( -tm.x, -tm.y );
                float2 oldFriction = float2( Constraints[co + 35], Constraints[co + 36] );
                float2 newImpulse = oldFriction + deltaImpulse;

                float maxImpulse = friction * totalNormalImpulse;

                float lengthSquared = dot( newImpulse, newImpulse );
                if ( lengthSquared > maxImpulse * maxImpulse )
                {
                    float scale = maxImpulse / sqrt( lengthSquared );
                    newImpulse.x *= scale;
                    newImpulse.y *= scale;
                }

                deltaImpulse = newImpulse - oldFriction;
                Constraints[co + 35] = newImpulse.x;
                Constraints[co + 36] = newImpulse.y;

                float3 P = float3(
                    deltaImpulse.x * tangent1.x + deltaImpulse.y * tangent2.x,
                    deltaImpulse.x * tangent1.y + deltaImpulse.y * tangent2.y,
                    deltaImpulse.x * tangent1.z + deltaImpulse.y * tangent2.z );
                vA = vA - mA * P;
                wA = wA - B3MulMV( iA, cross( rA, P ) );
                vB = vB + mB * P;
                wB = wB + B3MulMV( iB, cross( rB, P ) );
                la -= P;
                aa -= cross( rA, P );
                lb += P;
                ab += cross( rB, P );
            }
        }

        EmitContrib( p, la, aa, lb, ab );
    }

    void RestitutionJacobi( uint p )
    {
        uint co = p * CON_STRIDE;
        float restitution = Constraints[co + 34];

        float3 la = float3( 0.0, 0.0, 0.0 );
        float3 aa = float3( 0.0, 0.0, 0.0 );
        float3 lb = float3( 0.0, 0.0, 0.0 );
        float3 ab = float3( 0.0, 0.0, 0.0 );

        if ( restitution == 0.0 )
        {
            EmitContrib( p, la, aa, lb, ab );
            return;
        }

        uint a = (uint)Constraints[co];
        uint b = (uint)Constraints[co + 1];

        float mA;
        M3 iA;
        float3 vA;
        float3 wA;
        bool dynA;
        GetSplitBody( a, mA, iA, vA, wA, dynA );

        float mB;
        M3 iB;
        float3 vB;
        float3 wB;
        bool dynB;
        GetSplitBody( b, mB, iB, vB, wB, dynB );

        int pointCount = (int)Constraints[co + 3];
        float3 normal = float3( Constraints[co + 4], Constraints[co + 5], Constraints[co + 6] );

        [loop]
        for ( int pt = 0; pt < pointCount; ++pt )
        {
            uint po = co + 41 + (uint)pt * 12;

            if ( Constraints[po + 8] > -RestitutionThreshold || Constraints[po + 10] == 0.0 )
                continue;

            float3 rA = float3( Constraints[po], Constraints[po + 1], Constraints[po + 2] );
            float3 rB = float3( Constraints[po + 3], Constraints[po + 4], Constraints[po + 5] );

            float3 vrB = vB + cross( wB, rB );
            float3 vrA = vA + cross( wA, rA );
            float vn = dot( vrB - vrA, normal );

            float normalImpulse = Constraints[po + 9];
            float impulse = -Constraints[po + 7] * ( vn + restitution * Constraints[po + 8] );

            float newImpulse = max( normalImpulse + impulse, 0.0 );
            impulse = newImpulse - normalImpulse;
            Constraints[po + 9] = newImpulse;
            Constraints[po + 10] += impulse;

            float3 P = impulse * normal;
            vA = vA - mA * P;
            wA = wA - B3MulMV( iA, cross( rA, P ) );
            vB = vB + mB * P;
            wB = wB + B3MulMV( iB, cross( rB, P ) );
            la -= P;
            aa -= cross( rA, P );
            lb += P;
            ab += cross( rB, P );
        }

        EmitContrib( p, la, aa, lb, ab );
    }

    void ApplyContrib( uint b )
    {
        if ( Bodies[b * B3_STRIDE + 52] != 2.0 )
            return;

        uint deg = min( BodyColorMask[b], (uint)BODY_LIST_CAP );
        if ( deg == 0u )
            return;

        float mass = Bodies[b * B3_STRIDE + 20];
        M3 iW = LoadM3( b, 36 );
        float3 v = LoadV3( b, 7 );
        float3 w = LoadV3( b, 10 );

        [loop]
        for ( uint k = 0; k < deg; ++k )
        {
            uint c = ColorList[b * BODY_LIST_CAP + k];
            uint jo = c * JC_STRIDE;
            bool isA = Pairs[c * 2] == b;
            float3 lin = isA
                ? float3( WarmImpulses[jo], WarmImpulses[jo + 1], WarmImpulses[jo + 2] )
                : float3( WarmImpulses[jo + 6], WarmImpulses[jo + 7], WarmImpulses[jo + 8] );
            float3 ang = isA
                ? float3( WarmImpulses[jo + 3], WarmImpulses[jo + 4], WarmImpulses[jo + 5] )
                : float3( WarmImpulses[jo + 9], WarmImpulses[jo + 10], WarmImpulses[jo + 11] );

            v += mass * lin;
            w += B3MulMV( iW, ang );
        }

        StoreV3( b, 7, v );
        StoreV3( b, 10, w );
    }

    [numthreads( 64, 1, 1 )]
    void MainCs( uint3 id : SV_DispatchThreadID )
    {
        if ( Mode == 22 )
        {
            if ( id.x < min( BpCounters[0], (uint)PairCap ) )
            {
                uint co = id.x * CON_STRIDE;
                float cf = Constraints[co + 63];
                if ( (int)Manifolds[id.x * MANIFOLD_STRIDE + 3] >= 1 && cf >= 0.0 )
                {
                    uint bit = 1u << (uint)cf;
                    uint a = Pairs[id.x * 2];
                    uint b = Pairs[id.x * 2 + 1];
                    if ( Bodies[a * B3_STRIDE + 52] == 2.0 )
                    {
                        uint oldA;
                        InterlockedOr( BodyColorMask[a], bit, oldA );
                        if ( ( oldA & bit ) != 0 )
                            InterlockedAdd( BpCounters[5], 1u );
                    }

                    if ( Bodies[b * B3_STRIDE + 52] == 2.0 )
                    {
                        uint oldB;
                        InterlockedOr( BodyColorMask[b], bit, oldB );
                        if ( ( oldB & bit ) != 0 )
                            InterlockedAdd( BpCounters[5], 1u );
                    }
                }
            }
            return;
        }

        if ( Mode == 23 )
        {
            if ( id.x < (uint)BodyCount )
                BodyColorMask[id.x] = 0u;
            return;
        }

        if ( Mode == 24 )
        {
            if ( id.x < (uint)( RenderCount + RagdollBodies + DebrisBodies ) )
            {
                uint b;
                if ( id.x < (uint)RenderCount )
                    b = (uint)RenderFirstBody + id.x;
                else if ( id.x < (uint)( RenderCount + RagdollBodies ) )
                    b = (uint)RagdollFirst + ( id.x - (uint)RenderCount );
                else
                    b = (uint)DebrisFirst + ( id.x - (uint)( RenderCount + RagdollBodies ) );
                float3 p = LoadV3( b, 53 ) * RenderScale;
                float4 q = LoadQ( b, 3 );
                M3 r = B3MakeMatrixFromQuat( q );
                bool active = Bodies[b * B3_STRIDE + 52] == 2.0 && Bodies[b * B3_STRIDE + 60] < 0.5;
                float kind = Bodies[b * B3_STRIDE + 56];

                RenderInstance inst;
                inst.Alpha = 1.0;

                float3 c;
                if ( id.x >= (uint)( RenderCount + RagdollBodies ) )
                {
                    float cj = frac( sin( id.x * 12.9898 ) * 43758.5453 );
                    c = float3( 0.70, 0.61, 0.49 ) * ( 0.82 + 0.3 * cj );
                }
                else
                {
                    float hue = frac( id.x * 0.6180339887 );
                    c = saturate( abs( frac( hue + float3( 0.0, 0.333, 0.667 ) ) * 6.0 - 3.0 ) - 1.0 );
                    c = lerp( float3( 0.65, 0.65, 0.65 ), c, 0.5 );
                }
                uint cr = (uint)( c.x * 255.0 );
                uint cg = (uint)( c.y * 255.0 );
                uint cb = (uint)( c.z * 255.0 );
                inst.Tint = cr | ( cg << 8 ) | ( cb << 16 );
                inst.VertexCacheOffset = 0u;
                inst.BlendWeightCount = 0u;

                float s = ( active && kind == 0.0 ) ? Bodies[b * B3_STRIDE + 57] * RenderModelScale : 0.0;
                inst.Row0 = float4( s * r.cx.x, s * r.cz.x, s * r.cy.x, p.x );
                inst.Row1 = float4( s * r.cx.z, s * r.cz.z, s * r.cy.z, p.z );
                inst.Row2 = float4( s * r.cx.y, s * r.cz.y, s * r.cy.y, p.y );
                RenderInstances[id.x] = inst;

                float sx = 0.0;
                float sy = 0.0;
                float sz = 0.0;
                if ( active && kind == 2.0 )
                {
                    sx = Bodies[b * B3_STRIDE + 57] * BoxRenderScale.x;
                    sy = Bodies[b * B3_STRIDE + 59] * BoxRenderScale.y;
                    sz = Bodies[b * B3_STRIDE + 58] * BoxRenderScale.z;
                }
                else if ( active && kind == 1.0 )
                {
                    float cr = Bodies[b * B3_STRIDE + 57];
                    float chh = Bodies[b * B3_STRIDE + 58];
                    sx = cr * BoxRenderScale.x;
                    sy = cr * BoxRenderScale.y;
                    sz = ( chh + cr ) * BoxRenderScale.z;
                }

                inst.Row0 = float4( sx * r.cx.x, sy * r.cz.x, sz * r.cy.x, p.x );
                inst.Row1 = float4( sx * r.cx.z, sy * r.cz.z, sz * r.cy.z, p.z );
                inst.Row2 = float4( sx * r.cx.y, sy * r.cz.y, sz * r.cy.y, p.y );
                RenderInstancesBox[id.x] = inst;
            }

            return;
        }

        if ( Mode == 13 )
        {
            if ( id.x < (uint)BodyCount )
                BodyColorMask[id.x] = 0u;
            if ( id.x < (uint)COLOR_SLOTS )
                BpCounters[8 + id.x] = 0u;
            if ( id.x == 0u )
            {
                BpCounters[0] = 0u;
                BpCounters[1] = 0u;
                BpCounters[2] = 0u;
                BpCounters[4] = 0u;
            }
            return;
        }

        if ( Mode == 14 )
        {
            if ( id.x < (uint)HashCap )
                HashKeys[id.x] = HASH_EMPTY;
            return;
        }

        if ( Mode == 15 )
        {
            if ( id.x < min( BpCounters[0], (uint)PairCap ) )
                ColoringRound( id.x );
            return;
        }

        if ( Mode == 34 )
        {
            if ( id.x == 0u )
            {
                uint pc = min( BpCounters[0], (uint)PairCap );
                Aabbs[0] = asfloat( ( pc + 63u ) / 64u );
                Aabbs[1] = asfloat( 1u );
                Aabbs[2] = asfloat( 1u );
            }
            return;
        }

        if ( Mode == 37 )
        {
            if ( id.x < (uint)RewindCount )
            {
                uint b = (uint)RewindFirst + id.x;
                uint bo = b * B3_STRIDE;
                uint ro = ( (uint)RewindSlot * (uint)RewindCount + id.x ) * 16u;

                HashData[ro] = Bodies[bo + 53];
                HashData[ro + 1] = Bodies[bo + 54];
                HashData[ro + 2] = Bodies[bo + 55];
                HashData[ro + 3] = Bodies[bo + 3];
                HashData[ro + 4] = Bodies[bo + 4];
                HashData[ro + 5] = Bodies[bo + 5];
                HashData[ro + 6] = Bodies[bo + 6];
                HashData[ro + 7] = Bodies[bo + 7];
                HashData[ro + 8] = Bodies[bo + 8];
                HashData[ro + 9] = Bodies[bo + 9];
                HashData[ro + 10] = Bodies[bo + 10];
                HashData[ro + 11] = Bodies[bo + 11];
                HashData[ro + 12] = Bodies[bo + 12];
                HashData[ro + 13] = Bodies[bo + 52];
                HashData[ro + 14] = 0.0;
                HashData[ro + 15] = 0.0;
            }
            return;
        }

        if ( Mode == 38 )
        {
            if ( id.x < (uint)RewindCount )
            {
                uint b = (uint)RewindFirst + id.x;
                uint bo = b * B3_STRIDE;
                uint roA = ( (uint)RewindSlot * (uint)RewindCount + id.x ) * 16u;
                uint roB = ( (uint)RewindSlotB * (uint)RewindCount + id.x ) * 16u;

                float typeA = HashData[roA + 13];
                float typeB = HashData[roB + 13];

                if ( typeA != 2.0 || typeB != 2.0 )
                {
                    Bodies[bo + 52] = typeA;
                    if ( typeA != 2.0 )
                    {
                        Bodies[bo] = 0.0;
                        Bodies[bo + 1] = -70.0;
                        Bodies[bo + 2] = 0.0;
                        Bodies[bo + 53] = 0.0;
                        Bodies[bo + 54] = -70.0;
                        Bodies[bo + 55] = 0.0;
                        StoreV3( b, 7, float3( 0.0, 0.0, 0.0 ) );
                        StoreV3( b, 10, float3( 0.0, 0.0, 0.0 ) );
                    }
                    return;
                }

                float t = RewindLerp;
                float3 pA = float3( HashData[roA], HashData[roA + 1], HashData[roA + 2] );
                float3 pB = float3( HashData[roB], HashData[roB + 1], HashData[roB + 2] );
                float4 qA = float4( HashData[roA + 3], HashData[roA + 4], HashData[roA + 5], HashData[roA + 6] );
                float4 qB = float4( HashData[roB + 3], HashData[roB + 4], HashData[roB + 5], HashData[roB + 6] );
                if ( dot( qA, qB ) < 0.0 )
                    qB = -qB;

                float3 p = lerp( pA, pB, t );
                float4 q = normalize( lerp( qA, qB, t ) );
                float3 lv = lerp(
                    float3( HashData[roA + 7], HashData[roA + 8], HashData[roA + 9] ),
                    float3( HashData[roB + 7], HashData[roB + 8], HashData[roB + 9] ), t );
                float3 av = lerp(
                    float3( HashData[roA + 10], HashData[roA + 11], HashData[roA + 12] ),
                    float3( HashData[roB + 10], HashData[roB + 11], HashData[roB + 12] ), t );

                Bodies[bo] = p.x;
                Bodies[bo + 1] = p.y;
                Bodies[bo + 2] = p.z;
                Bodies[bo + 53] = p.x;
                Bodies[bo + 54] = p.y;
                Bodies[bo + 55] = p.z;
                Bodies[bo + 3] = q.x;
                Bodies[bo + 4] = q.y;
                Bodies[bo + 5] = q.z;
                Bodies[bo + 6] = q.w;
                StoreV3( b, 7, lv );
                StoreV3( b, 10, av );
                StoreV3( b, 13, float3( 0.0, 0.0, 0.0 ) );
                Bodies[bo + 16] = 0.0;
                Bodies[bo + 17] = 0.0;
                Bodies[bo + 18] = 0.0;
                Bodies[bo + 19] = 1.0;
                Bodies[bo + 45] = 0.0;
                Bodies[bo + 46] = 0.0;
                Bodies[bo + 47] = 0.0;
                Bodies[bo + 48] = 0.0;
                Bodies[bo + 49] = 0.0;
                Bodies[bo + 50] = 0.0;
                Bodies[bo + 52] = 2.0;
                Bodies[bo + 63] = 0.0;
            }
            return;
        }

        if ( Mode == 36 )
        {
            if ( id.x < (uint)MaxRagdolls )
            {
                uint jStart = HashKeys[id.x * 2];
                uint jCount = HashKeys[id.x * 2 + 1];

                [loop]
                for ( uint jj = 0; jj < jCount; ++jj )
                {
                    uint jo = ( jStart + jj ) * 32u;
                    if ( HashData[jo + 20] > 0.5 )
                        SolveJoint( jo );
                }
            }
            return;
        }

        if ( Mode == 35 )
        {
            if ( id.x < (uint)BodyCount && Bodies[id.x * B3_STRIDE + 52] == 2.0 )
            {
                float3 p = LoadV3( id.x, 53 );
                if ( p.x >= WakeLo.x && p.y >= WakeLo.y && p.z >= WakeLo.z &&
                     p.x <= WakeHi.x && p.y <= WakeHi.y && p.z <= WakeHi.z )
                    Bodies[id.x * B3_STRIDE + 63] = 0.0;
            }
            return;
        }

        if ( Mode == 28 )
        {
            if ( id.x < min( BpCounters[0], (uint)PairCap ) )
                BuildBodyList( id.x );
            return;
        }

        if ( Mode == 29 )
        {
            if ( id.x < min( BpCounters[0], (uint)PairCap ) )
            {
                if ( (int)Manifolds[id.x * MANIFOLD_STRIDE + 3] >= 1 )
                {
                    HashLookupWarm( id.x );
                    PrepareJacobi( id.x );
                }
            }
            return;
        }

        if ( Mode == 30 )
        {
            if ( id.x < min( BpCounters[0], (uint)PairCap ) )
            {
                if ( (int)Manifolds[id.x * MANIFOLD_STRIDE + 3] >= 1 )
                    SolveJacobi( id.x, UseBias != 0 );
            }
            return;
        }

        if ( Mode == 31 )
        {
            if ( id.x < (uint)BodyCount )
                ApplyContrib( id.x );
            return;
        }

        if ( Mode == 32 )
        {
            if ( id.x < min( BpCounters[0], (uint)PairCap ) )
            {
                if ( (int)Manifolds[id.x * MANIFOLD_STRIDE + 3] >= 1 )
                    WarmEmitJacobi( id.x );
            }
            return;
        }

        if ( Mode == 33 )
        {
            if ( id.x < min( BpCounters[0], (uint)PairCap ) )
            {
                if ( (int)Manifolds[id.x * MANIFOLD_STRIDE + 3] >= 1 )
                    RestitutionJacobi( id.x );
            }
            return;
        }

        if ( Mode == 17 )
        {
            if ( id.x < min( BpCounters[0], (uint)PairCap ) )
            {
                if ( (int)Manifolds[id.x * MANIFOLD_STRIDE + 3] >= 1 )
                {
                    HashLookupWarm( id.x );
                    PrepareConstraint( id.x );
                }
            }
            return;
        }

        if ( Mode == 18 )
        {
            if ( id.x < min( BpCounters[0], (uint)PairCap ) )
            {
                if ( (int)Manifolds[id.x * MANIFOLD_STRIDE + 3] >= 1 )
                    HashStore( id.x );
            }
            return;
        }

        if ( Mode == 19 )
        {
            if ( id.x < BpCounters[8 + (uint)ColorSlot] )
                WarmStartConstraint( ColorList[(uint)ColorSlot * (uint)SegCap + id.x] );
            return;
        }

        if ( Mode == 20 )
        {
            if ( id.x < BpCounters[8 + (uint)ColorSlot] )
                SolveConstraint( ColorList[(uint)ColorSlot * (uint)SegCap + id.x], UseBias != 0 );
            return;
        }

        if ( Mode == 21 )
        {
            if ( id.x < BpCounters[8 + (uint)ColorSlot] )
                RestitutionConstraint( ColorList[(uint)ColorSlot * (uint)SegCap + id.x] );
            return;
        }

        if ( Mode == 8 )
        {
            if ( id.x < (uint)ColorCount )
                PrepareConstraint( ColorList[id.x] );
            return;
        }

        if ( Mode == 9 )
        {
            if ( id.x < (uint)ColorCount )
                WarmStartConstraint( ColorList[(uint)ColorOffset + id.x] );
            return;
        }

        if ( Mode == 10 )
        {
            if ( id.x < (uint)ColorCount )
                SolveConstraint( ColorList[(uint)ColorOffset + id.x], UseBias != 0 );
            return;
        }

        if ( Mode == 11 )
        {
            if ( id.x < (uint)ColorCount )
                RestitutionConstraint( ColorList[(uint)ColorOffset + id.x] );
            return;
        }

        if ( Mode == 7 )
        {
            uint pairCount = min( BpCounters[0], (uint)PairCap );
            if ( id.x < pairCount )
            {
                CollidePair( id.x );

                if ( SleepEnable != 0 && (int)Manifolds[id.x * MANIFOLD_STRIDE + 3] >= 1 )
                {
                    uint pa = Pairs[id.x * 2];
                    uint pb = Pairs[id.x * 2 + 1];
                    float wakeTol = 9.0 * SleepVel * SleepVel;

                    if ( IsAsleep( pa ) && !IsAsleep( pb ) )
                    {
                        float3 vb = LoadV3( pb, 7 );
                        if ( dot( vb, vb ) > wakeTol )
                            Bodies[pa * B3_STRIDE + 63] = 0.0;
                    }

                    if ( IsAsleep( pb ) && !IsAsleep( pa ) )
                    {
                        float3 va = LoadV3( pa, 7 );
                        if ( dot( va, va ) > wakeTol )
                            Bodies[pb * B3_STRIDE + 63] = 0.0;
                    }

                    float minSep = 0.0;
                    int mc = (int)Manifolds[id.x * MANIFOLD_STRIDE + 3];

                    [loop]
                    for ( int mk = 0; mk < mc; ++mk )
                        minSep = min( minSep, Manifolds[id.x * MANIFOLD_STRIDE + 4 + (uint)mk * 5 + 3] );

                    if ( minSep < -0.15 )
                    {
                        if ( Bodies[pa * B3_STRIDE + 52] == 2.0 )
                            Bodies[pa * B3_STRIDE + 63] = 0.0;
                        if ( Bodies[pb * B3_STRIDE + 52] == 2.0 )
                            Bodies[pb * B3_STRIDE + 63] = 0.0;
                    }
                }
            }
            return;
        }

        if ( Mode == 4 )
        {
            if ( id.x < (uint)( GridX * GridY * GridZ ) )
                GridCount[id.x] = 0u;
            return;
        }

        if ( Mode == 26 )
        {
            if ( id.x < (uint)TriCount )
            {
                uint to = id.x * 9u;
                float3 ta = float3( TriData[to], TriData[to + 1], TriData[to + 2] );
                float3 tb = float3( TriData[to + 3], TriData[to + 4], TriData[to + 5] );
                float3 tc = float3( TriData[to + 6], TriData[to + 7], TriData[to + 8] );
                float3 e1 = tb - ta;
                float3 e2 = tc - ta;
                if ( dot( e1, e1 ) + dot( e2, e2 ) < 1e-10 )
                    return;

                int3 t0 = CellOf( min( ta, min( tb, tc ) ) );
                int3 t1 = CellOf( max( ta, max( tb, tc ) ) );

                [loop]
                for ( int z = t0.z; z <= t1.z; ++z )
                {
                    [loop]
                    for ( int y = t0.y; y <= t1.y; ++y )
                    {
                        [loop]
                        for ( int x = t0.x; x <= t1.x; ++x )
                        {
                            uint cell = CellIndex( int3( x, y, z ) );
                            uint slot;
                            InterlockedAdd( GridCount[cell], 1u, slot );
                            if ( slot < (uint)CellCap )
                                GridCells[cell * (uint)CellCap + slot] = TRI_FLAG | id.x;
                            else
                                InterlockedOr( BpCounters[1], 1u );
                        }
                    }
                }
            }
            return;
        }

        if ( id.x >= (uint)BodyCount )
            return;

        if ( Mode == 3 )
        {
            BuildAabb( id.x );
            return;
        }

        if ( Mode == 5 )
        {
            RasterizeBody( id.x );
            return;
        }

        if ( Mode == 27 )
        {
            BuildAabb( id.x );
            RasterizeBody( id.x );
            return;
        }

        if ( Mode == 6 )
        {
            GeneratePairs( id.x );
            return;
        }

        if ( Bodies[id.x * B3_STRIDE + 52] != 2.0 )
            return;

        if ( IsAsleep( id.x ) )
            return;

        if ( Mode == 0 )
        {
            IntegrateVelocities( id.x );
            return;
        }

        if ( Mode == 1 )
        {
            IntegratePositions( id.x );
            return;
        }

        if ( Mode == 2 )
        {
            FinalizeBody( id.x );

            if ( SleepEnable != 0 )
            {
                float3 sv = LoadV3( id.x, 7 );
                float3 sw = LoadV3( id.x, 10 );
                float tol = SleepVel * SleepVel;
                if ( dot( sv, sv ) < tol && dot( sw, sw ) < 4.0 * tol )
                {
                    float c = Bodies[id.x * B3_STRIDE + 63] + 1.0;
                    Bodies[id.x * B3_STRIDE + 63] = c;
                    if ( c >= (float)SleepSteps )
                    {
                        StoreV3( id.x, 7, float3( 0.0, 0.0, 0.0 ) );
                        StoreV3( id.x, 10, float3( 0.0, 0.0, 0.0 ) );
                    }
                }
                else
                {
                    Bodies[id.x * B3_STRIDE + 63] = 0.0;
                }
            }

            return;
        }
    }
}
