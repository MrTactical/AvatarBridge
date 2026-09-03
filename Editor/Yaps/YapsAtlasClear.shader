// Clears the atlas before anyone writes into it.
//
// Without this there is no way to tell an empty cell from an occupied one. A
// grab captures whatever the screen had at those pixels, the screen is
// opaque, so alpha reads 1 everywhere and every cell looks occupied.
//
// One quad over the whole atlas rect, painting alpha 0. A socket's patch then
// writes over it and an empty cell keeps the 0. Costs one quad however many
// sockets exist.
//
// ONLY the alpha channel. Painting colour as well makes the atlas a visible
// black rectangle: the clear covers the whole rect, so it is by far the most
// conspicuous part of the system. Masked off, the rect keeps whatever the
// scene drew there and still reads alpha 0, which is all occupancy needs.
//
// Queue Overlay-200, against the writers at Overlay-100 and the grab at
// Overlay. Ordering comes from the QUEUE, not from distance, which matters
// because these three live on different avatars and Unity sorts within a
// queue by distance, which nobody controls. Across queues it is deterministic.
//
// If nobody in the room draws a clear, cells degrade to unreliable rather
// than to wrong, which is the right direction for a fallback transport.
Shader "YAPS/Atlas Clear"
{
    SubShader
    {
        Tags { "Queue" = "Overlay-200" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            ColorMask A

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "yaps_atlas.cginc"

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // PIXELS, matching the snapped atlas. The object's own
                // transform is ignored, so import scale cannot reach this.
                float2 unit = v.vertex.xy + 0.5;
                float2 span = float2(YapsAtlasWidthPx(), YapsAtlasHeightPx()) + 4;
                o.pos = float4(YapsAtlasToClip(unit * span), UNITY_NEAR_CLIP_VALUE, 1);
                return o;
            }

            // Alpha 0 is "nothing here". Colour does not matter; only a
            // socket's patch writes alpha over it.
            fixed4 frag (v2f i) : SV_Target { return fixed4(0, 0, 0, 0); }
            ENDCG
        }
    }
}
