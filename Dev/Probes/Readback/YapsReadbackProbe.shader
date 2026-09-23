// Can a vertex stage write every vertex it outputs into a buffer the CPU
// reads back? The runtime tester stands on this: the bend exists only in a
// vertex shader, and this is the one route that hands it over as numbers.
// Writes the world position per SV_VertexID, w = 1 so an unwritten entry
// (w = 0) is told from a vertex at the origin.
Shader "Hidden/YapsReadbackProbe"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #include "UnityCG.cginc"

            // u1: slot 0 is the colour target, and UAVs number after them.
            RWStructuredBuffer<float4> _YapsCapture : register(u1);

            struct appdata { float4 vertex : POSITION; uint id : SV_VertexID; };
            struct v2f { float4 pos : SV_POSITION; };

            v2f vert(appdata v)
            {
                v2f o;
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                _YapsCapture[v.id] = float4(world, 1);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return fixed4(1, 0, 1, 1); }
            ENDCG
        }
    }
}
