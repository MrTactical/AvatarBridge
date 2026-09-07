// A socket publishing its world position into the screen atlas.
//
// Two passes and no visible geometry. The mesh is one unit quad per (level,
// home) pair, flagged by z; both passes read the same quads and place them
// in different pixels.
//
// The HEADER pixel is a COUNT, built with additive blending on alpha alone:
// every socket adds 1/255 and the sum is how many sit in that slot. A reader
// takes one tap for it and only opens the cell if it says anything is there,
// so a nearly empty atlas costs one tap a cell. It was a bitmask of live
// octants first, which does not survive two sockets in one octant: 1+1 is 2,
// so the occupied octant stops being advertised and a neighbour that is not
// starts. A count cannot carry.
//
// The PAYLOAD is the socket's position within its cell and the direction it
// faces, in the octant bucket it occupies. Eight to a cell, indexed by which
// octant of the cell the socket sits in, which needs no negotiation and
// separates anything more than half a cell apart in any axis.
//
// A socket publishes to every level. That costs one small draw each and
// nothing to read, because a plug reads only the level matching its own
// length.
Shader "YAPS/Atlas Socket"
{
    Properties
    {
        // 0 ring, 1 hole. A ring is a loop and can be entered from either
        // face; a hole has a front and a back. Sixteen kinds fit in the
        // facing pixel's alpha.
        [Enum(Ring,0,Hole,1)] _YAPS_Kind ("Socket kind", Float) = 0
    }
    SubShader
    {
        // Before the grab at Background-944, so the grab contains this, and
        // before the scene, so the scene covers the pixels it wrote.
        Tags { "Queue" = "Background-945" "RenderType" = "Opaque" }
        ZTest Always ZWrite Off Cull Off

        CGINCLUDE
        #pragma target 4.0
        #include "UnityCG.cginc"
        #include "yaps_atlas.cginc"

        // Corner and quad index in UV0, not POSITION: see YapsAtlas.LevelQuads.
        // Every position is zero so a replacement shader draws nothing.
        struct appdata { float4 vertex : POSITION; float3 corner : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

        float _YAPS_Kind;

        // Everything a quad needs to know about where it belongs.
        void Place(float3 corner, out int cellPx, out int cellPy,
                   out int sub, out float3 payload, out float3 facing, out float tag)
        {
            int q = int(corner.z + 0.5);
            int level = min(q / 2, YAPS_ATLAS_LEVELS - 1);
            int home = q % 2;

            float size = YAPS_ATLAS_CELL * pow(4.0, level);
            float3 world = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
            float3 scaled = world / max(size, 1e-6);
            int3 cell = int3(floor(scaled));
            float3 f = frac(scaled);

            int total = YAPS_ATLAS_GRID * YAPS_ATLAS_GRID;
            int idx = YapsAtlasHash(cell) % total;
            if (idx < 0) idx += total;
            if (home == 1)
            {
                int step = YapsAtlasHash2(cell) % max(total - 1, 1);
                if (step < 0) step += max(total - 1, 1);
                idx = (idx + step + 1) % total;
            }
            YapsAtlasCellPixels(idx, level, cellPx, cellPy);

            sub = (f.x > 0.5 ? 4 : 0) + (f.y > 0.5 ? 2 : 0) + (f.z > 0.5 ? 1 : 0);
            payload = f;
            facing = normalize(mul((float3x3)unity_ObjectToWorld, float3(0, 0, 1))) * 0.5 + 0.5;
            tag = 0.5 + 0.5 * YapsAtlasTag(cell);
        }
        ENDCG

        // ---- the header: one pixel, alpha counts what is in the cell ----
        Pass
        {
            Blend One One
            ColorMask A

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct v2f { float4 pos : SV_POSITION; float bit : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                int cx, cy, sub; float3 pay, fwd; float tag;
                Place(v.corner, cx, cy, sub, pay, fwd, tag);

                float2 unit = v.corner.xy + 0.5;
                o.pos = YapsAtlasFits()
                    ? float4(YapsAtlasToClip(float2(cx, cy) + unit * YAPS_ATLAS_SLOTPX),
                             UNITY_NEAR_CLIP_VALUE, 1)
                    : YapsAtlasNowhere();

                // A half float resolves 1/255 to about a hundred and thirty
                // steps at these magnitudes, so the sum survives the grab.
                o.bit = 1.0 / 255.0;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return fixed4(0, 0, 0, i.bit); }
            ENDCG
        }

        // ---- the payload: this octant's position and facing ----
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 payload : TEXCOORD0;
                float4 other   : TEXCOORD1;
                float  u       : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                int cx, cy, sub; float3 pay, fwd; float tag;
                Place(v.corner, cx, cy, sub, pay, fwd, tag);

                float2 unit = v.corner.xy + 0.5;
                float2 atPx;
                atPx.x = cx + (1 + 2 * sub) * YAPS_ATLAS_SLOTPX + unit.x * 2 * YAPS_ATLAS_SLOTPX;
                atPx.y = cy + unit.y * YAPS_ATLAS_SLOTPX;
                o.pos = YapsAtlasFits()
                    ? float4(YapsAtlasToClip(atPx), UNITY_NEAR_CLIP_VALUE, 1)
                    : YapsAtlasNowhere();

                o.payload = float4(pay, tag);
                // The facing pixel's alpha carried a second copy of the tag,
                // which nothing read: it is only reached once the position
                // pixel's tag matched, and the same draw writes both, so it
                // cannot disagree. It carries the KIND instead.
                o.other   = float4(fwd, (floor(_YAPS_Kind) + 1) / 16.0);
                o.u = unit.x;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return i.u < 0.5 ? i.payload : i.other; }
            ENDCG
        }
    }
}
