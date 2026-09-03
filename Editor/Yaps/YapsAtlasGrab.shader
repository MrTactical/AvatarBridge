// The named screen grab, and nothing else.
//
// A GrabPass with a NAME writes a texture every shader in the room can
// sample, and Unity runs it once per frame per name however many objects
// carry it. So one avatar wearing this serves every plug present, including
// plugs on avatars carrying none, and a second copy in the room is free.
//
// Queue Overlay, after the sockets, so the grab contains what they wrote. A
// plug drawing at Geometry therefore reads the PREVIOUS frame, about 11 ms
// at 90 Hz.
Shader "YAPS/Atlas Grab"
{
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Opaque" "IgnoreProjector" = "True" }

        GrabPass { "_YAPS_Atlas" }

        // The grab is the whole point; this pass exists because a SubShader
        // needs one for the object to be drawn at all. No colour, no depth,
        // one triangle a millimetre across.
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
