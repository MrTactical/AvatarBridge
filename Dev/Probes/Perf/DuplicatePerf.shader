// What the duplicate's second copy costs a plug's vertex stage, resolve and
// deform together. As written it compiles on the shipped includes, which have
// no copy; the probe swaps the include line below for the spike's copies,
// inlined, with YAPS_DUPLICATE defined or not.
//
// Pass 0 runs the deform for every vertex, pass 1 nothing and is the floor.
// Every point lands behind the far plane, so nothing rasterises, but its
// position depends on the answer, so the work cannot be compiled out. Pass 2
// writes each deformed vertex to its own pixel and pass 3 what the resolve
// and the copy's path answered, so the probe can say which path it timed.
// Pass 4 fills the margin directory.
//
// Lives in Assets/AvatarBridge/Editor/DevTools when deployed, hence the
// relative include.
Shader "Hidden/DuplicatePerf"
{
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        CGINCLUDE
        #pragma target 5.0
        #include "UnityCG.cginc"
        #include "../Yaps/yaps_deform.cginc"

        // The first copy vertex, as _YAPS_CopyStart, which the shipped
        // includes do not declare.
        float _PerfCopyStart;

        struct v2f { float4 pos : SV_POSITION; };
        struct v2c { float4 pos : SV_POSITION; float4 value : TEXCOORD0; };

        // PortalDeformPerf's inputs and vertex count are constants the
        // compiler can fold through the deform; a mesh's never are. Defined
        // for one comparison variant only.
#ifdef PERF_CONSTANT_INPUTS
        #define PERF_COUNT 6433u
#else
        #define PERF_COUNT ((uint) _YAPS_VertexCount)
#endif

        // The renderer's own frame (_YAPS_FrameFromVertex 0), so an original's
        // inputs only seed the rest pose the bend replaces. A copy arrives as
        // the builder anchors it: on the bake origin, its normal and tangent
        // the root's up and forward.
        float3 PerfDeform(uint v, out float3 n, out float3 t)
        {
#ifdef PERF_CONSTANT_INPUTS
            float3 p = float3(0, 0, v * 1e-4);
            n = float3(1, 0, 0);
            t = float3(0, 1, 0);
#else
            bool copy = _PerfCopyStart > 0 && v >= (uint) _PerfCopyStart;
            float3 p = copy ? float3(0, 0, 0) : float3(0, 0, v * 1e-4);
            n = copy ? float3(0, 1, 0) : float3(1, 0, 0);
            t = copy ? float3(0, 0, 1) : float3(0, 1, 0);
#endif
            YapsDeform(p, n, t, v);
            return p;
        }

        float4 PerfPixel(float2 px)
        {
            return float4(YapsAtlasToClip(px + 0.5), UNITY_NEAR_CLIP_VALUE, 1);
        }

        v2f vertDeform(uint id : SV_VertexID)
        {
            uint v = id % PERF_COUNT;
            float3 n, t;
            float3 p = PerfDeform(v, n, t);
            v2f o;
            o.pos = float4(dot(p + n + t, 1) * 1e-9 + v * 1e-12, 0, 2, 1);
            return o;
        }

        v2f vertFloor(uint id : SV_VertexID)
        {
            v2f o;
            o.pos = float4((id % PERF_COUNT) * 1e-12, 0, 2, 1);
            return o;
        }

        v2c vertCheck(uint id : SV_VertexID)
        {
            float3 n, t;
            float3 p = PerfDeform(id, n, t);
            v2c o;
            o.pos = PerfPixel(float2(id % 128, id / 128));
            // The vertex id in alpha, so the probe never depends on which way a readback runs.
            o.value = float4(mul(unity_ObjectToWorld, float4(p, 1)).xyz, 100 + id);
            return o;
        }

        // The plug's root and axis as the deform recovers them for a plug that
        // is its own object, and where the copy's path goes from there. Row
        // 1000, clear of the check's rows for twice the plug.
        v2c vertSummary(uint id : SV_VertexID)
        {
            float3 root = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
            float3 fwd = normalize(mul((float3x3) unity_ObjectToWorld, float3(0, 0, 1)));
            float3 up = normalize(mul((float3x3) unity_ObjectToWorld, float3(0, 1, 0)));
            YapsSocket s = YapsResolveSocket(root, fwd, up, _YAPS_Length);
            float copied = 0;
            YapsSocket c = s;
#ifdef YAPS_DUPLICATE
            copied = YapsCopyPath(c, root, fwd, _YAPS_Length) ? 1 : 0;
#endif
            v2c o;
            o.pos = PerfPixel(float2(id, 1000));
            o.value = id == 0 ? float4(s.tier, s.chain.count, copied, 10)
                    : id == 1 ? float4(s.position, 20)
                    : id == 2 ? float4(c.chain.count, c.engaged, 0, 30)
                    : float4(c.position, 40);
            return o;
        }

        // Rows 0-7 across the rect, as entries whose tag nothing asks for.
        v2c vertDirFill(uint id : SV_VertexID)
        {
            uint c = id < 3 ? id : id - 2;
            v2c o;
            o.pos = float4(YapsAtlasToClip(float2((c & 1) != 0 ? YapsAtlasWidthPx() : 0,
                                                  (c & 2) != 0 ? YAPS_ATLAS_ORIGIN : 0)), UNITY_NEAR_CLIP_VALUE, 1);
            o.value = float4(0.3, 0.3, 0.3, 0.5);
            return o;
        }

        fixed4 frag(v2f i) : SV_Target { return 0; }
        float4 fragValue(v2c i) : SV_Target { return i.value; }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma vertex vertDeform
            #pragma fragment frag
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vertFloor
            #pragma fragment frag
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vertCheck
            #pragma fragment fragValue
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vertSummary
            #pragma fragment fragValue
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vertDirFill
            #pragma fragment fragValue
            ENDCG
        }
    }
}
