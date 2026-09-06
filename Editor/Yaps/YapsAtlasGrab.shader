// The named screen grab, and nothing else.
//
// A named GrabPass runs once per frame per name.
// One avatar wearing it serves every plug in the room.
//
// Queue Background-944, after the writers, before the scene.
// The scene covers those pixels, so nothing shows.
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
    }
}
