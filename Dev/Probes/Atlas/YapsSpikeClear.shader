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
        _SpanPxX ("Rect to clear, pixels across, 0 for clip space", Float) = 0
        _SpanPxY ("Rect to clear, pixels down", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Overlay-200" "RenderType" = "Opaque" }
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            // ONLY the alpha channel. Painting RGB as well is what made the
            // atlas a visible black square: the clear covers the whole rect,
            // so it is by far the most conspicuous part of the system. With
            // the colour masked off the rect keeps whatever the scene drew
            // there and still reads alpha 0, which is all occupancy needs.
            ColorMask A

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
                float2 unit = v.vertex.xy + 0.5;
                float2 p;
                if (_SpanPxX > 0.5)
                {
                    // PIXELS, matching the snapped atlas. The clip-space path
                    // below lands somewhere else entirely once the atlas is
                    // eight pixels wide, which would leave every cell reading
                    // the opaque screen and therefore occupied.
                    float2 atPx = unit * float2(_SpanPxX, _SpanPxY);
                    p = atPx / _ScreenParams.xy * 2.0 - 1.0;
                    p.y = -p.y;
                }
                else
                {
                    float span = _CellPixels * max(_Grid, 1);
                    p = _Corner.xy + unit * span * float2(1, -1);
                }
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
