// The named screen grab, and nothing else.
//
// A named GrabPass runs once per frame per name.
// One avatar wearing it serves every plug in the room.
//
// Queue Background-944, after the writers, before the scene.
// The scene covers those pixels, except where nothing draws: a camera
// clearing to a solid colour or to transparent showed the cells as spots.
// So the second pass puts the rect back as the clear's grab found it.
// Readers read the grab, never the screen.
Shader "YAPS/Atlas Grab"
{
    SubShader
    {
        Tags { "Queue" = "Background-944" "RenderType" = "Opaque" "IgnoreProjector" = "True" }

        GrabPass { "_YAPS_Atlas" }

        // A SubShader needs a pass or the object is never drawn.
        // No colour, no depth, one tiny triangle.
        Pass
        {
            ColorMask 0
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return 0; }
            ENDCG
        }

        // The rect back as it was before the clear. Painting it with one colour
        // sampled beside it left the spots wherever the background was not one
        // colour: a camera drawing over an earlier one's picture.
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "yaps_atlas.cginc"

            struct appdata { float4 vertex : POSITION; uint id : SV_VertexID; UNITY_VERTEX_INPUT_INSTANCE_ID };
            // Each eye its own slice: unlike the atlas, the pictures differ.
            #if defined(UNITY_STEREO_INSTANCING_ENABLED)
                Texture2DArray _YAPS_Under;
            #else
                Texture2D _YAPS_Under;
            #endif

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 px : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // The clear's rect, in pixels from the top left.
                float2 span = float2(YapsAtlasWidthPx(), YapsAtlasHeightPx()) + 4;
                // One triangle twice the rect each way covers it; frag cuts the rest.
                o.px = float2(v.id == 1 ? 2 : 0, v.id == 2 ? 2 : 0) * span;
                o.pos = YapsAtlasFits() ? float4(YapsAtlasToClip(o.px), UNITY_NEAR_CLIP_VALUE, 1) : YapsAtlasNowhere();
                return o;
            }

            // By the fragment's own pixel, never a row worked out from the top:
            // the grab is a copy of this target, so whichever way up it is, the
            // same pixel is the same address.
            float4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 span = float2(YapsAtlasWidthPx(), YapsAtlasHeightPx()) + 4;
                clip(span - i.px);
                int2 at = int2(i.pos.xy);
            #if defined(UNITY_STEREO_INSTANCING_ENABLED)
                return _YAPS_Under.Load(int4(at, unity_StereoEyeIndex, 0));
            #else
                return _YAPS_Under.Load(int3(at, 0));
            #endif
            }
            ENDCG
        }
    }
}
