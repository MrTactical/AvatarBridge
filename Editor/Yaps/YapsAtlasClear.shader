// Clears the atlas before the writers run.
// Without it every cell reads occupied.
//
// One quad, alpha 0. A socket writes over it.
//
// ALPHA ONLY. Colour too and the rect turns black on screen.
//
// Queue Background-946, ahead of the writers at -945.
// Order comes from the queue, never from distance.
//
// Before the scene. Painting last erased self portraits.
//
// With no clear in the room cells go unreliable, not wrong.
Shader "YAPS/Atlas Clear"
{
    SubShader
    {
        Tags { "Queue" = "Background-946" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
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

            // Corner in UV0, not POSITION: see YapsAtlas.LevelQuads.
            // Positions are all zero, so a replacement shader draws nothing.
            struct appdata { float4 vertex : POSITION; float3 corner : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // Pixels, matching the snapped atlas.
                // The object transform is ignored, so import scale cannot reach it.
                float2 unit = v.corner.xy + 0.5;
                float2 span = float2(YapsAtlasWidthPx(), YapsAtlasHeightPx()) + 4;
                o.pos = YapsAtlasFits()
                    ? float4(YapsAtlasToClip(unit * span), UNITY_NEAR_CLIP_VALUE, 1)
                    : YapsAtlasNowhere();
                return o;
            }

            // Alpha 0 means nothing here. Colour is ignored.
            fixed4 frag (v2f i) : SV_Target { return fixed4(0, 0, 0, 0); }
            ENDCG
        }
    }
}
