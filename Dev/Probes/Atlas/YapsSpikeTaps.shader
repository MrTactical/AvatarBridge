// What do the neighbourhood taps actually cost?
//
// The grab is below the noise floor; this measures the vertex taps: 27 per
// vertex at radius 1, so a 3000-vertex plug does 81,000 reads per camera.
// Set _Taps to 0, 8, 27 and read the slope. Reads fold into the position, or
// the compiler deletes them.
// Plain sampler2D + tex2Dlod (the stereo macro has no vertex form); the real
// texture array may cost differently. This gives the shape, not the number.
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

                // UVs walk a patch so reads hit different texels, as a real neighbourhood does.
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
