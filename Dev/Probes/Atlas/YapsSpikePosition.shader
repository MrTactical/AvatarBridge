// Spike 2: publish this object's own WORLD POSITION into the atlas.
//
// Spike 1 round-tripped a constant, which proves the pipe and nothing about
// what fits through it. This encodes a real transform, so it moves when the
// object moves and the error can be measured in metres rather than in
// colour channels.
//
// The payload comes from the object's OWN matrix, not from a C# property,
// because that is what a socket would have to do in game: there is no script
// running on someone else's avatar to set a material value. A plain
// MeshRenderer has a real unity_ObjectToWorld; a SkinnedMeshRenderer does
// NOT, since Unity skins into world space and hands the shader identity, so
// a socket marker for this has to be an ordinary mesh.
//
// Encoding here is deliberately naive: normalised across a fixed box around
// the world origin. That is the WRONG scheme for shipping and the spike
// should show why — a half float carries about three decimal digits of
// relative precision, so this is excellent near the origin and poor far from
// it. The fix is to encode the offset within a spatial-hash cell instead,
// where the payload is never more than half a cell. Measure the naive one
// first so the number is real rather than asserted.
Shader "YAPS/Spike Position"
{
    Properties
    {
        _Corner ("Corner of the patch, clip space", Vector) = (-0.95, -0.45, 0, 0)
        _Size ("Patch size, clip space", Float) = 0.02
        _Extent ("Half the box the position is normalised across, metres", Float) = 8
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
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 payload : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _Corner;
            float _Size;
            float _Extent;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // The patch sits at a fixed place on screen, in clip space,
                // ignoring this object's transform and the eye. Both slices
                // of the eye texture array then carry identical content.
                float2 unit = v.vertex.xy + 0.5;
                float2 p = _Corner.xy + unit * _Size * float2(1, -1);
                o.pos = float4(p, UNITY_NEAR_CLIP_VALUE, 1);

                // The payload is where this object actually IS.
                float3 world = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                o.payload = saturate(world / (2.0 * max(_Extent, 0.0001)) + 0.5);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return fixed4(i.payload, 1); }
            ENDCG
        }
    }
}
