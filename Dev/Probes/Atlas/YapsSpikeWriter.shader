// Stamps a known value into a fixed corner of the screen.
//
// The quad ignores its own transform and its own eye: the vertex shader
// writes clip space directly, so the patch lands on the same pixels no
// matter where the object is, whether it faces the camera, or which eye is
// being drawn. Under single-pass instanced that means both slices of the
// eye texture array carry identical content, which is the whole reason this
// is simpler here than on VRChat's double-wide.
Shader "YAPS/Spike Writer"
{
    Properties
    {
        _SpikeValue ("Value to stamp", Vector) = (0.25, 0.5, 0.75, 1)
        _Corner ("Corner of the patch, clip space", Vector) = (-1, 1, 0, 0)
        _Size ("Patch size, clip space", Float) = 0.06
    }
    SubShader
    {
        // Before anything that wants to read it.
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
                // A Unity Quad spans -0.5..0.5, so this maps it into a
                // _Size patch hanging off _Corner, downward and rightward.
                float2 unit = v.vertex.xy + 0.5;
                float2 p = _Corner.xy + unit * _Size * float2(1, -1);
                o.pos = float4(p, UNITY_NEAR_CLIP_VALUE, 1);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return _SpikeValue; }
            ENDCG
        }
    }
}
