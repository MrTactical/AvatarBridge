// The skinning probe's material: the vertex stage reads what skinning wrote
// and does nothing else, so a skinned draw minus the same mesh unskinned is
// the skinning. Points behind the far plane draw no pixel; with _Check on,
// each vertex writes where it was skinned to, its id in alpha.
Shader "Hidden/DuplicatePerfSkin"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Check;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT; uint id : SV_VertexID; };
            struct v2f { float4 pos : SV_POSITION; float4 value : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                float3 w = mul(unity_ObjectToWorld, v.vertex).xyz;
                if (_Check > 0.5)
                {
                    float2 px = float2(v.id % 128, v.id / 128) + 0.5;
                    o.pos = float4(px / _ScreenParams.xy * 2 - 1, UNITY_NEAR_CLIP_VALUE, 1);
                    o.value = float4(w, 100 + v.id);
                }
                else
                {
                    o.pos = float4(dot(w + v.normal + v.tangent.xyz, 1) * 1e-9, 0, 2, 1);
                    o.value = 0;
                }
                return o;
            }

            float4 frag(v2f i) : SV_Target { return i.value; }
            ENDCG
        }
    }
}
