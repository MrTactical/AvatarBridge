// The control. Identical to the writer in every way except that it has no
// GrabPass anywhere in the file.
//
// A magenta quad in game means a shader failed, and "ChilloutVR rejects
// GrabPass" and "custom shaders do not survive this upload at all" look
// exactly the same from the outside. This one separates them: if the control
// draws and the reader is pink, the GrabPass is the reason.
Shader "YAPS/Spike Control"
{
    Properties
    {
        _SpikeValue ("Value to stamp", Vector) = (0, 1, 0, 1)
        _Corner ("Corner of the patch, clip space", Vector) = (-1, 1, 0, 0)
        _Size ("Patch size, clip space", Float) = 0.06
    }
    SubShader
    {
        Tags { "Queue" = "Overlay-100" "RenderType" = "Opaque" }
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

            float4 _SpikeValue;
            float4 _Corner;
            float _Size;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float2 unit = v.vertex.xy + 0.5;
                // Third patch along, so all three sit side by side.
                float2 p = _Corner.xy + float2(_Size * 2.4, 0) + unit * _Size * float2(1, -1);
                o.pos = float4(p, UNITY_NEAR_CLIP_VALUE, 1);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return _SpikeValue; }
            ENDCG
        }
    }
}
