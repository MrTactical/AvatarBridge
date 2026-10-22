// The named screen grab, and nothing else.
//
// A named GrabPass runs once per frame per name.
// One avatar wearing it serves every plug in the room.
//
// Queue Background-944, after the writers, before the scene.
// The scene covers those pixels, except where nothing draws: a camera
// clearing to a solid colour or to transparent showed the cells as spots.
// So the second pass paints the rect back with the background, read from
// the grab just taken. Readers read the grab, never the screen.
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

        // The rect back to the background. Before the scene draws, the rest of
        // the screen is still the camera's clear, so three pixels outside the
        // rect that agree are it. When they disagree (a camera drawing over an
        // earlier one's picture) nothing is painted: there is no one colour.
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
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 px : TEXCOORD0;
                nointerpolation float4 back : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // The clear's rect, in pixels from the top left.
                float2 span = float2(YapsAtlasWidthPx(), YapsAtlasHeightPx()) + 4;
                int sx = min((int) span.x + 1, (int) _ScreenParams.x - 1);
                int sy = min((int) span.y + 1, (int) _ScreenParams.y - 1);
                float4 a = YAPS_ATLAS_LOAD(sx, YapsAtlasRow(0));
                float4 b = YAPS_ATLAS_LOAD(0, YapsAtlasRow(sy));
                float4 c = YAPS_ATLAS_LOAD(sx, YapsAtlasRow(sy));
                float4 d = abs(a - b) + abs(a - c);
                bool one = max(max(d.r, d.g), max(d.b, d.a)) <= 0.002;
                // One triangle twice the rect each way covers it; frag cuts the rest.
                o.px = float2(v.id == 1 ? 2 : 0, v.id == 2 ? 2 : 0) * span;
                o.back = a;
                o.pos = YapsAtlasFits() && one ? float4(YapsAtlasToClip(o.px), UNITY_NEAR_CLIP_VALUE, 1) : YapsAtlasNowhere();
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 span = float2(YapsAtlasWidthPx(), YapsAtlasHeightPx()) + 4;
                clip(span - i.px);
                return i.back;
            }
            ENDCG
        }
    }
}
