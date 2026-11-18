// Clears the atlas before anyone writes into it.
//
// Paints the rect alpha 0, or the opaque screen reads alpha 1 and every cell
// looks occupied (spike 3: all 27 cells decoded the floor).
// Overlay-200 vs the writers' Overlay-100: they sit on different avatars and
// Unity sorts within a queue by distance; across queues is deterministic.
// No clear in the room degrades cells to unreliable, not wrong.
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
            // Alpha only: painting RGB made the atlas a visible black square.
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
            float _SpanPxX;
            float _SpanPxY;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float2 unit = v.vertex.xy + 0.5;
                float2 p;
                if (_SpanPxX > 0.5)
                {
                    // Pixels, matching the snapped atlas; the clip-space path misses it.
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
