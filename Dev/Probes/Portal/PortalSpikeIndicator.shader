// A square at a portal exit that reads the margin directory back, in
// whatever view draws it. PORTAL SPIKE, for the in-game gate G2; beside the
// scratch yaps_atlas.cginc, hence the bare include.
//
//   green   the entry is there, pose within the S2 gate
//   red     the atlas fits this view, and the entry is missing or wrong
//   grey    the atlas does not fit this view (under about 240 px)
//   yellow  no owner yet: a remote copy before its first sync
//
// It sits beside YAPS/Atlas Exit Spike, same parent, identity local
// transform, so its matrix is the writer's: the pose the entry must decode
// to. The lookup is the scratch YapsPortalExit with the production
// YAPS_ATLAS_LOAD, slice 0 in stereo, which is what a plug reads in both eyes.
//
// Corners in UV0 and every position zero, as the atlas writers, so a viewer
// with custom shaders blocked draws nothing.
Shader "YAPS/Portal Spike Indicator"
{
    Properties
    {
        _YAPS_Owner ("Owner id", Float) = 0
        _YAPS_EntranceIndex ("Entrance number", Float) = 0
        _Size ("Width, metres", Float) = 0.06
    }
    SubShader
    {
        // After the grab at Background-944, so it reads this view's atlas,
        // and over everything, so a body never hides the answer.
        Tags { "Queue" = "Overlay" "RenderType" = "Overlay" "IgnoreProjector" = "True" "DisableBatching" = "True" }
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

            // The S2 gate: S/255 + 0.1 mm, and half a degree.
            #define PORTAL_SPIKE_POS 0.0013549
            #define PORTAL_SPIKE_COS 0.99996192

            struct appdata { float4 vertex : POSITION; float3 corner : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                nointerpolation float4 color : COLOR0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float _YAPS_Owner;
            float _YAPS_EntranceIndex;
            float _Size;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // The writer's pose, with its zero-scale guard.
                float3 at = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float3x3 m = (float3x3) unity_ObjectToWorld;
                float3 z = mul(m, float3(0, 0, 1));
                if (dot(z, z) < 1e-12) z = cross(mul(m, float3(1, 0, 0)), mul(m, float3(0, 1, 0)));
                float3 fwd = dot(z, z) < 1e-12 ? z : normalize(z);

                int owner = YapsOwnerOf(_YAPS_Owner);
                int n = (int) round(_YAPS_EntranceIndex);
                float3 gotAt = at, gotFwd = 0;
                bool hit = YapsPortalExit(owner << 4 | n, gotAt, gotFwd);
                bool good = hit && distance(gotAt, at) <= PORTAL_SPIKE_POS && dot(gotFwd, fwd) >= PORTAL_SPIKE_COS;
                o.color = !YapsAtlasFits() ? float4(0.5, 0.5, 0.5, 1)
                        : owner == 0 ? float4(1, 1, 0, 1)
                        : good ? float4(0, 1, 0, 1)
                        : float4(1, 0, 0, 1);

                // Faces every camera, mirrors and each eye included.
                float4 view = mul(UNITY_MATRIX_V, float4(at, 1));
                view.xy += v.corner.xy * _Size;
                o.pos = mul(UNITY_MATRIX_P, view);
                return o;
            }

            float4 frag (v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
