// Grabs the screen and reads back the corner the writer stamped.
//
// The named GrabPass is the whole question: it is the only way an avatar
// shader can see what another avatar rendered, and nobody has tested whether
// ChilloutVR's asset filter keeps it. If it survives an upload, there is a
// transport here that costs no light slots, no contact pairs and no sync
// bits, and that a viewer cannot switch off the way they can switch off
// avatar lights.
Shader "YAPS/Spike Reader"
{
    Properties
    {
        _Corner ("Corner of the patch, clip space", Vector) = (-1, 1, 0, 0)
        _Size ("Patch size, clip space", Float) = 0.06
        _SpikeValue ("What the writer stamped", Vector) = (0.25, 0.5, 0.75, 1)
    }
    SubShader
    {
        // After the writer, so the grab contains it.
        Tags { "Queue" = "Overlay" "RenderType" = "Opaque" }

        GrabPass { "_YAPS_SpikeAtlas" }

        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            UNITY_DECLARE_SCREENSPACE_TEXTURE(_YAPS_SpikeAtlas);
            float4 _YAPS_SpikeAtlas_TexelSize;
            float4 _Corner;
            float _Size;
            float4 _SpikeValue;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // Sits beside the writer's patch so both are on screen.
                float2 unit = v.vertex.xy + 0.5;
                float2 p = _Corner.xy + float2(_Size * 1.2, 0)
                         + unit * _Size * float2(1, -1);
                o.pos = float4(p, UNITY_NEAR_CLIP_VALUE, 1);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // Middle of the writer's patch, clip space to UV.
                float2 mid = _Corner.xy + float2(_Size * 0.5, -_Size * 0.5);
                float2 uv = mid * 0.5 + 0.5;
                // Some targets hand back a flipped grab.
                if (_YAPS_SpikeAtlas_TexelSize.y < 0) uv.y = 1 - uv.y;
                float4 got = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_YAPS_SpikeAtlas, uv);
                // Green means the value came back through the grab, red
                // means it did not. Two small patches cannot be compared by
                // eye, and in game there is no readback to fall back on.
                float err = max(abs(got.r - _SpikeValue.r),
                            max(abs(got.g - _SpikeValue.g), abs(got.b - _SpikeValue.b)));
                return err < 0.01 ? float4(0, 1, 0, 1) : float4(1, 0, 0, 1);
            }
            ENDCG
        }
    }
}
