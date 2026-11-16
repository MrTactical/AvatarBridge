// What the portal costs on the whole plug path, resolve and deform, per
// vertex. As written it is the baseline on the shipped includes; the probe
// swaps the include line below for the portal spike's copies, inlined.
//
// Pass 0 runs the deform for every vertex, pass 1 nothing and is the floor.
// Every point lands behind the far plane, so nothing rasterises, but its
// position depends on the answer, so the work cannot be compiled out. Pass 2
// writes each deformed vertex to its own pixel and pass 3 what the resolve
// and the lookup answered, and where the hole decoded, so the probe can say
// which path it timed. Passes
// 4 and 5 fill and write the margin directory.
//
// Lives in Assets/AvatarBridge/Editor/DevTools when deployed, hence the
// relative include.
Shader "Hidden/PortalDeformPerf"
{
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        CGINCLUDE
        #pragma target 5.0
        #include "UnityCG.cginc"
        #include "../Yaps/yaps_deform.cginc"

        #define PERF_VERTICES 6433

        float3 _PerfExitAt;
        float3 _PerfExitFwd;
        float _PerfOwner;
        float _PerfNumber;
        float _PerfHome;

        struct v2f { float4 pos : SV_POSITION; };
        struct v2c { float4 pos : SV_POSITION; float4 value : TEXCOORD0; };

        // The renderer's own frame (_YAPS_FrameFromVertex 0), so the inputs
        // only seed the rest pose the bend replaces.
        float3 PerfDeform(uint v, out float3 n, out float3 t)
        {
            float3 p = float3(0, 0, v * 1e-4);
            n = float3(1, 0, 0);
            t = float3(0, 1, 0);
            YapsDeform(p, n, t, v);
            return p;
        }

        float4 PerfPixel(float2 px)
        {
            return float4(YapsAtlasToClip(px + 0.5), UNITY_NEAR_CLIP_VALUE, 1);
        }

        v2f vertDeform(uint id : SV_VertexID)
        {
            uint v = id % PERF_VERTICES;
            float3 n, t;
            float3 p = PerfDeform(v, n, t);
            v2f o;
            o.pos = float4(dot(p + n + t, 1) * 1e-9 + v * 1e-12, 0, 2, 1);
            return o;
        }

        v2f vertFloor(uint id : SV_VertexID)
        {
            v2f o;
            o.pos = float4((id % PERF_VERTICES) * 1e-12, 0, 2, 1);
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
        // is its own object.
        v2c vertSummary(uint id : SV_VertexID)
        {
            float3 root = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
            float3 fwd = normalize(mul((float3x3) unity_ObjectToWorld, float3(0, 0, 1)));
            float3 up = normalize(mul((float3x3) unity_ObjectToWorld, float3(0, 1, 0)));
            YapsSocket s = YapsResolveSocket(root, fwd, up, _YAPS_Length);
            float portal = 0, hit = 0;
            float3 at = s.position, f = 0;
#ifdef YAPS_ATLAS_DIR_VERSION
            portal = s.chain.portal != 0 ? 1 : 0;
            if (s.chain.portal != 0) hit = YapsPortalExit(s.chain.portal, at, f) ? 1 : 0;
#endif
            v2c o;
            o.pos = PerfPixel(float2(id, 60));
            o.value = id == 0 ? float4(s.tier, s.chain.count, portal, 10 + hit)
                    : id == 1 ? float4(at, 20) : id == 2 ? float4(f, 30) : float4(s.position, 40);
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

        v2c vertDirWrite(uint id : SV_VertexID)
        {
            v2c o;
#ifdef YAPS_ATLAS_DIR_VERSION
            int owner = (int) round(_PerfOwner), n = (int) round(_PerfNumber);
            uint px, row;
            YapsDirHome(owner, n, (int) round(_PerfHome), px, row);
            o.pos = PerfPixel(float2(px + id, row));
            o.value = YapsDirEncode(owner, n, _PerfExitAt, normalize(_PerfExitFwd), id);
#else
            o.pos = YapsAtlasNowhere();
            o.value = 0;
#endif
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
        Pass
        {
            CGPROGRAM
            #pragma vertex vertDirWrite
            #pragma fragment fragValue
            ENDCG
        }
    }
}
