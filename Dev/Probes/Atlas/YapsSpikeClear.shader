// Clears the atlas before anyone writes into it.
//
// Without this there is no way to tell an empty cell from an occupied one.
// A grab captures whatever the screen had at those pixels, the screen is
// opaque, so alpha reads 1 everywhere and every cell looks occupied. Spike 3
// measured exactly that: twenty-seven cells all reporting a hit, all decoding
// the floor.
//
// One quad covering the whole atlas rect, painting alpha 0. A socket's patch
// then writes alpha 1 over it and an empty cell keeps the 0. Costs one quad
// per atlas however many sockets exist.
//
// Queue is Overlay-200 against the writers' Overlay-100, so ordering comes
// from the QUEUE rather than from distance. That matters because the clear
// and the writers live on different avatars and Unity sorts within a queue by
// distance, which nobody controls. Across queues it is deterministic.
//
// If nobody in the room draws a clear, cells degrade to unreliable rather
// than to wrong, which is the right direction for a fallback transport.
Shader "YAPS/Spike Clear"
{
    Properties
    {
        _Corner ("Atlas corner, clip space", Vector) = (-0.95, 0.95, 0, 0)
        _CellPixels ("One cell, clip space", Float) = 0.02
        _Grid ("Cells across the atlas", Float) = 8
    }
    SubShader
    {
        Tags { "Queue" = "Overlay-200" "RenderType" = "Opaque" }
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            float4 _Corner;
            float _CellPixels;
            float _Grid;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float span = _CellPixels * max(_Grid, 1);
                float2 unit = v.vertex.xy + 0.5;
                float2 p = _Corner.xy + unit * span * float2(1, -1);
                o.pos = float4(p, UNITY_NEAR_CLIP_VALUE, 1);
                return o;
            }

            // Alpha 0 is "nothing here". The colour does not matter; only a
            // socket's patch writes alpha 1 over it.
            fixed4 frag (v2f i) : SV_Target { return fixed4(0, 0, 0, 0); }
            ENDCG
        }
    }
}
