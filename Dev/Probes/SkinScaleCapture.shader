// SkinScaleProbe's stand-in for the plug's shader: what the vertex stage receives, as received.
Shader "Hidden/SkinScaleCapture"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #include "UnityCG.cginc"

            RWStructuredBuffer<float4> _SkinScaleOut : register(u1);
            float _SkinScaleCount;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                uint id : SV_VertexID;
            };

            float4 vert(appdata v) : SV_POSITION
            {
                uint n = (uint) _SkinScaleCount;
                _SkinScaleOut[v.id] = float4(mul(unity_ObjectToWorld, v.vertex).xyz, length(v.normal));
                _SkinScaleOut[n + v.id] = float4(length(v.tangent.xyz),
                    length(mul((float3x3) unity_ObjectToWorld, float3(0, 0, 1))), 0, 1);
                return UnityObjectToClipPos(v.vertex);
            }

            fixed4 frag() : SV_Target { return 0; }
            ENDCG
        }
    }
}
