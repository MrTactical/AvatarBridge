// Where the scratch deform puts each vertex of a mesh, for PortalBandProbe.
// Dev only. Beside the scratch includes, hence the bare include; the probe
// swaps that line for the copies it was pointed at, inlined.
//
// Pass 0 draws a mesh as points, each vertex to its own pixel: its world
// position, and its id plus 100 in alpha, so the probe never depends on
// which way a readback runs. Pass 1 writes what the resolve and the lookup
// answered for the plug's own root, five pixels on the bottom row:
//   alpha 10+hit  tier, links, portal key
//   alpha 20      the exit as decoded
//   alpha 30      its facing
//   alpha 40      the hole as resolved
//   alpha 50      headers and hits the scan read, and whether the atlas fits
// The target is the atlas's size, so the deform reads the layout written.
Shader "Hidden/PortalBandCapture"
{
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        CGINCLUDE
        #pragma target 5.0
        #include "UnityCG.cginc"
        #include "yaps_deform.cginc"

        struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT; uint id : SV_VertexID; };
        struct v2c { float4 pos : SV_POSITION; float4 value : TEXCOORD0; };

        float4 Pixel(uint i)
        {
            uint w = (uint) _ScreenParams.x;
            return float4(YapsAtlasToClip(float2(i % w, i / w) + 0.5), UNITY_NEAR_CLIP_VALUE, 1);
        }

        v2c vertCapture(appdata v)
        {
            float3 p = v.vertex.xyz, n = v.normal, t = v.tangent.xyz;
            YapsDeform(p, n, t, v.id);
            v2c o;
            o.pos = Pixel(v.id);
            o.value = float4(mul(unity_ObjectToWorld, float4(p, 1)).xyz, 100 + v.id);
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
            float hit = 0;
            float3 at = s.position, f = 0;
            if (s.chain.portal != 0) hit = YapsPortalExit(s.chain.portal, at, f) ? 1 : 0;
            v2c o;
            uint w = (uint) _ScreenParams.x, h = (uint) _ScreenParams.y;
            o.pos = Pixel(w * (h - 1) + id);
            o.value = id == 0 ? float4(s.tier, s.chain.count, s.chain.portal != 0 ? 1 : 0, 10 + hit)
                    : id == 1 ? float4(at, 20) : id == 2 ? float4(f, 30) : id == 3 ? float4(s.position, 40)
                    : float4(s.atlasHeaders, s.atlasHits, YapsAtlasFits() ? 1 : 0, 50);
            return o;
        }

        float4 frag(v2c i) : SV_Target { return i.value; }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma vertex vertCapture
            #pragma fragment frag
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vertSummary
            #pragma fragment frag
            ENDCG
        }
    }
}
