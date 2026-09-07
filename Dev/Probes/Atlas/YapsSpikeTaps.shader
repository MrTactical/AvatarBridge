// What do the neighbourhood taps actually cost?
//
// One grab per camera turned out to be below the noise floor. The cost that
// has never been measured is the other half: a plug's VERTEX shader reading
// the atlas once per neighbouring cell, for every vertex. A radius-1
// neighbourhood is twenty-seven cells, and a three thousand vertex plug is
// then eighty-one thousand texture reads a frame, per plug, per camera.
// That is a different order of thing from a single full-screen copy.
//
// _Taps is the count, so the same mesh can be measured at 0, 8 and 27 and
// the slope read off directly.
//
// The accumulator matters: a compiler will delete a sample whose result is
// never used, and a tap test that optimises itself away measures nothing.
// Every read is folded into the vertex position, by an amount too small to
// see but too real to remove.
//
// Caveat worth carrying: this samples through a plain sampler2D with
// tex2Dlod, because sampling in a vertex shader needs an explicit LOD and
// the stereo macro has no vertex-stage form. Under single-pass instanced the
// real thing reads a texture array, which may not cost exactly the same. The
// SHAPE of the answer, flat, linear, or ruinous, is what this is for.
Shader "YAPS/Spike Taps"
{
    Properties
    {
        _Taps ("Taps per vertex", Range(0, 64)) = 27
        _Colour ("Colour", Color) = (0.2, 0.6, 0.9, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            sampler2D _YAPS_SpikeAtlas;
            float _Taps;
            fixed4 _Colour;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // Read the atlas once per neighbouring cell, the way a plug
                // would. The UVs walk a small patch so the reads are not all
                // the same texel, which is what a real neighbourhood does to
                // the cache.
                float3 acc = 0;
                int taps = (int)_Taps;
                [loop] for (int i = 0; i < taps; i++)
                {
                    float2 uv = float2(0.02 + (i % 8) * 0.02, 0.95 - (i / 8) * 0.02);
                    acc += tex2Dlod(_YAPS_SpikeAtlas, float4(uv, 0, 0)).rgb;
                }

                // Folded in, so nothing can be optimised away, but scaled to
                // nothing so the mesh does not visibly move.
                float4 pos = v.vertex;
                pos.xyz += acc * 1e-6;
                o.pos = UnityObjectToClipPos(pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return _Colour; }
            ENDCG
        }
    }
}
