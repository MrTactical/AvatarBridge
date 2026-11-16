// A portal exit publishing its own pose into the margin directory.
//
// The entrance stays an ordinary protocol-9 hole; this writer draws only
// rows 0 to 7, which no cell draws or reads, so an older reader never sees it.
//
// One quad per home, flagged by z, corners in UV0 and every position zero, as
// the socket writer's quads and for the same reason. The matrix is the
// payload: its origin is the exit and its +Z the way the shaft leaves.
//
// The key is the owner and the ENTRANCE's number, not this socket's own: the
// entrance is what a plug resolves, so the entrance's number is what it asks
// for. Its own property, so numbering an exit as a socket never rewrites it.
Shader "YAPS/Atlas Exit"
{
    Properties
    {
        _YAPS_Owner ("Owner id", Float) = 0
        _YAPS_EntranceIndex ("Entrance number", Float) = 0
    }
    SubShader
    {
        // The socket writers' queue: before the grab at Background-944.
        Tags { "Queue" = "Background-945" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "yaps_atlas.cginc"

            struct appdata { float4 vertex : POSITION; float3 corner : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            // Not interpolated: the owner and cell index are integers in a
            // float, and the four pixels must be the ones the encoder made.
            struct v2f
            {
                float4 pos : SV_POSITION;
                nointerpolation float4 p0 : TEXCOORD0;
                nointerpolation float4 p1 : TEXCOORD1;
                nointerpolation float4 p2 : TEXCOORD2;
                nointerpolation float4 p3 : TEXCOORD3;
                float u : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float _YAPS_Owner;
            float _YAPS_EntranceIndex;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                int owner = YapsOwnerOf(_YAPS_Owner);
                int n = (int) round(_YAPS_EntranceIndex);
                float3 at = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;

                // The socket writer's zero-scale guard: flat along Z, the
                // cross of X and Y still faces the way +Z would. Flat on every
                // axis it writes no facing, which the reader refuses.
                float3x3 m = (float3x3) unity_ObjectToWorld;
                float3 z = mul(m, float3(0, 0, 1));
                if (dot(z, z) < 1e-12) z = cross(mul(m, float3(1, 0, 0)), mul(m, float3(0, 1, 0)));
                float3 fwd = dot(z, z) < 1e-12 ? z : normalize(z);

                uint px, row;
                YapsDirHome(owner, n, v.corner.z > 0.5 ? 1 : 0, px, row);

                // No owner is a prop or a copy not yet synced, and a number
                // outside 1 to 15 is an entrance that has none: both stay
                // plain holes, so nothing is published for them.
                bool live = YapsAtlasFits() && owner != 0 && n >= 1 && n <= YAPS_ATLAS_SOCKETMAX;
                float2 unit = v.corner.xy + 0.5;
                o.pos = live
                    ? float4(YapsAtlasToClip(float2(px, row) + unit * float2(YAPS_ATLAS_DIR_PX, 1)), UNITY_NEAR_CLIP_VALUE, 1)
                    : YapsAtlasNowhere();

                o.p0 = YapsDirEncode(owner, n, at, fwd, 0);
                o.p1 = YapsDirEncode(owner, n, at, fwd, 1);
                o.p2 = YapsDirEncode(owner, n, at, fwd, 2);
                o.p3 = YapsDirEncode(owner, n, at, fwd, 3);
                o.u = unit.x;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float q = i.u * YAPS_ATLAS_DIR_PX;
                return q < 1 ? i.p0 : q < 2 ? i.p1 : q < 3 ? i.p2 : i.p3;
            }
            ENDCG
        }
    }
}
