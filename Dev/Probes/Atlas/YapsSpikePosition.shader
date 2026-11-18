// Spike 2: publish this object's own WORLD POSITION into the atlas.
//
// From the object's own matrix: no script runs on someone else's avatar.
// Must be a plain MeshRenderer; skinned ones get an identity matrix.
// Deliberately naive encoding (a fixed box round the origin) to measure how a
// half float's ~3 digits degrade with distance; shipping encodes an offset
// within a hash cell.
Shader "YAPS/Spike Position"
{
    Properties
    {
        _Corner ("Corner of the patch, clip space", Vector) = (-0.95, -0.45, 0, 0)
        _Size ("Patch size, clip space", Float) = 0.02
        _Extent ("Half the box the position is normalised across, metres", Float) = 8
    }
    SubShader
    {
        Tags { "Queue" = "Overlay-100" "RenderType" = "Opaque" }
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 payload : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _Corner;
            float _Size;
            float _Extent;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // Fixed clip-space spot, so both eye slices carry the same content.
                float2 unit = v.vertex.xy + 0.5;
                float2 p = _Corner.xy + unit * _Size * float2(1, -1);
                o.pos = float4(p, UNITY_NEAR_CLIP_VALUE, 1);

                // The payload is where this object actually IS.
                float3 world = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                o.payload = saturate(world / (2.0 * max(_Extent, 0.0001)) + 0.5);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return fixed4(i.payload, 1); }
            ENDCG
        }
    }
}
