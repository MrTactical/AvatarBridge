// Where the scratch deform puts each vertex of a skinned mesh, for
// DuplicateSpike. Dev only. The bare include is swapped by the probe for the
// project's copy, as PortalBandCapture's is.
//
// One pass, so a camera drawing a points-topology renderer through it draws
// nothing else: each vertex to its own pixel, its world position, and its id
// plus 100 in alpha. _DupDump swaps the position for what the resolver handed
// the first copy: 1 chain entry id % 4, 2 its kind with the count and tier,
// 3 engagement with the atlas hits and headers, 4 the socket aimed at, 5 the
// vertex's own recovered root. Without YAPS_DUPLICATE only positions.
Shader "Hidden/DuplicateCapture"
{
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #define YAPS_DUP_DUMP
            #include "yaps_deform.cginc"

            float _DupDump;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT; uint id : SV_VertexID; };
            struct v2c { float4 pos : SV_POSITION; float4 value : TEXCOORD0; };

            v2c vert(appdata v)
            {
                float3 p = v.vertex.xyz, n = v.normal, t = v.tangent.xyz;
                YapsDeform(p, n, t, v.id);
                uint w = (uint) _ScreenParams.x;
                v2c o;
                o.pos = float4(YapsAtlasToClip(float2(v.id % w, v.id / w) + 0.5), UNITY_NEAR_CLIP_VALUE, 1);
                float3 value = mul(unity_ObjectToWorld, float4(p, 1)).xyz;
#ifdef YAPS_DUPLICATE
                YapsChain c = yapsDump.chain;
                uint e = v.id % 4;
                if (_DupDump > 0.5 && _DupDump < 1.5)
                    value = e == 0 ? c.position[0] : e == 1 ? c.position[1] : e == 2 ? c.position[2] : c.position[3];
                else if (_DupDump < 2.5 && _DupDump > 0.5)
                    value = float3(e == 0 ? c.kind[0] : e == 1 ? c.kind[1] : e == 2 ? c.kind[2] : c.kind[3], c.count, yapsDump.tier);
                else if (_DupDump < 3.5 && _DupDump > 0.5)
                    value = float3(c.engaged, c.hits, c.headers);
                else if (_DupDump < 4.5 && _DupDump > 0.5)
                    value = yapsDump.position;
                else if (_DupDump > 0.5)
                    value = yapsDumpRoot;
#endif
                o.value = float4(value, 100 + v.id);
                return o;
            }

            float4 frag(v2c i) : SV_Target { return i.value; }
            ENDCG
        }
    }
}
