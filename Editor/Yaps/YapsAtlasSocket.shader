// A socket publishing its world position into the screen atlas.
//
// No visible geometry: one unit quad per (level, home), flagged by z; both
// passes place the same quads in different pixels.
//
// HEADER pixel: a COUNT, additive on alpha, 1/255 per socket, so a reader
// skips an empty cell in one tap. A bitmask of octants failed: two sockets in
// one octant sum to 2 and advertise the wrong octant. A count cannot carry.
//
// PAYLOAD: position in the cell and facing, in one of eight buckets (by the
// socket's number, else its octant). Readers open every bucket, so the choice
// is the writer's alone.
//
// Every level is written (one small draw each); a plug reads only its own.
Shader "YAPS/Atlas Socket"
{
    Properties
    {
        // A ring is entered from either face unless one-way; a hole has a
        // front and back. Shares the facing pixel's alpha with the number.
        [Enum(Ring,0,Hole,1,OneWayRing,2)] _YAPS_Kind ("Socket kind", Float) = 0

        // 20 bits in a float. Zero is untagged: older sockets, sockets found
        // by light, and what a plug with no include list answers.
        _YAPS_SocketTags ("Socket tag word", Float) = 0

        // Animated per renderer from a synced parameter. Zero is "unknown",
        // right for a prop or a world socket.
        _YAPS_Owner ("Owner id", Float) = 0

        // 1 to 15, 0 for none. Set at build on a material of its own.
        _YAPS_SocketIndex ("Own socket number", Float) = 0
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
        float _YAPS_SocketTags;
        float _YAPS_Owner;
        float _YAPS_SocketIndex;

        // Everything a quad needs to know about where it belongs.
        void Place(float3 corner, out int cellPx, out int cellPy,
                   out int sub, out float3 payload, out float3 facing, out float tag)
        {
            int q = int(corner.z + 0.5);
            int level = min(q / 2, YAPS_ATLAS_LEVELS - 1);
            int home = q % 2;

            float size = YapsAtlasCellSize(level);
            float3 world = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
            float3 scaled = world / size;
            int3 cell = int3(floor(scaled));
            float3 f = frac(scaled);

            // Off target the quad goes nowhere, but the maths still runs.
            YapsAtlasLayout layout = YapsAtlasLayoutNow();
            int total = max(layout.cells, 1);
            int idxA, idxB;
            YapsAtlasHomes(cell, total, idxA, idxB);
            int idx = home == 1 ? idxB : idxA;
            YapsAtlasCellPixels(idx, level, layout, cellPx, cellPy);

            // Bucket by number, turned by owner: a wearer's own sockets (up to
            // eight) never share one. By octant alone, two sockets a hand apart
            // shared one and the later draw hid the nearer. Across wearers a
            // clash is one in eight. No owner: the octant turns it instead.
            int octant = (f.x > 0.5 ? 4 : 0) + (f.y > 0.5 ? 2 : 0) + (f.z > 0.5 ? 1 : 0);
            int number = (int) round(_YAPS_SocketIndex);
            int owner = YapsOwnerOf(_YAPS_Owner);
            sub = number > 0 ? (number - 1 + (owner != 0 ? owner : octant)) & 7 : octant;
            payload = f;
            // Zero Z scale: normalising gives NaN, read as -1 on 8 bits, so the
            // cross of X and Y is the facing. Zero on every axis writes 0.5,
            // stored as 128/255 on 8 bits, so readers treat a short vector as
            // no facing.
            float3x3 m = (float3x3)unity_ObjectToWorld;
            float3 z = mul(m, float3(0, 0, 1));
            if (dot(z, z) < 1e-12) z = cross(mul(m, float3(1, 0, 0)), mul(m, float3(0, 1, 0)));
            facing = (dot(z, z) < 1e-12 ? z : normalize(z)) * 0.5 + 0.5;
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
                float4 tags    : TEXCOORD3;
                float4 owner   : TEXCOORD4;
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
                atPx.x = cx + (1 + YAPS_ATLAS_OCTPX * sub) * YAPS_ATLAS_SLOTPX
                       + unit.x * YAPS_ATLAS_OCTPX * YAPS_ATLAS_SLOTPX;
                atPx.y = cy + unit.y * YAPS_ATLAS_SLOTPX;
                o.pos = YapsAtlasFits()
                    ? float4(YapsAtlasToClip(atPx), UNITY_NEAR_CLIP_VALUE, 1)
                    : YapsAtlasNowhere();

                o.payload = float4(pay, tag);
                // Alpha holds the kind and number, not a second tag: the same
                // draw writes both pixels, so a tag copy could never disagree.
                o.other   = float4(fwd, YapsFacingEncode((int) round(_YAPS_Kind),
                                                         (int) round(_YAPS_SocketIndex)));
                o.tags    = YapsTagsEncode((int) floor(_YAPS_SocketTags + 0.5));
                o.owner   = YapsOwnerEncode(YapsOwnerOf(_YAPS_Owner));
                o.u = unit.x;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float q = i.u * YAPS_ATLAS_OCTPX;
                return q < 1 ? i.payload : q < 2 ? i.other : q < 3 ? i.tags : i.owner;
            }
            ENDCG
        }
    }
}
