// Spike 5: a socket that publishes into a bucketed, multi-resolution atlas.
//
// BUCKETS: eight sockets per cell, by octant (no negotiation; separates
// anything over half a cell apart; one per cell made same-cell sockets share both homes).
// A header pixel counts sockets (additive alpha, 1/255 each), so empty cells
// cost one tap. A count, not an octant bitmask: 1+1 carries into the wrong bit
// and cascades until the cell goes dark.
// LEVELS: cell ~ L/2r differs 100x between 20 cm and 20 m plugs, so levels
// step 4x in their own row bands. Sockets publish to all; plugs read one.
//
// LAYOUT
//   cell x  = origin + gx * (1 + 2*8) * slot        17 slots to a cell
//   cell y  = origin + (level * grid + gy) * slot
//
//   header      at cell x
//   octant s    position at cell x + (1 + 2s) * slot
//               facing   at cell x + (2 + 2s) * slot
//
// The mesh is one quad per (level, home) pair; the two passes below read the
// same quads and place them differently.
Shader "YAPS/Spike Socket"
{
    Properties
    {
        _Grid ("Cells across a level", Float) = 16
        _Cols ("Columns the slots fold into", Float) = 32
        _SlotPx ("One slot, in pixels", Float) = 1
        _OriginPx ("Atlas origin, pixels from the top left", Float) = 8
        _CellSize ("Cell size of level 0, metres", Float) = 0.02
        _Levels ("How many levels", Float) = 6
        // A ring enters from either face, a hole only the front. 16 kinds fit the facing alpha.
        [Enum(Ring,0,Hole,1)] _Kind ("Socket kind", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Overlay-100" "RenderType" = "Opaque" }
        ZTest Always ZWrite Off Cull Off

        CGINCLUDE
        #pragma target 4.0
        #include "UnityCG.cginc"

        struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

        float _Grid, _SlotPx, _OriginPx, _CellSize, _Levels, _Kind;

        // Columns, not the grid: a 1088 px row was clipped by narrower cameras,
        // and a clipped slot reads the opaque screen, so sockets vanished.
        // 4096 at 32 columns is 544 x 512, fitting 720p, mirrors and eye buffers.
        // C# derives it for both shaders.
        float _Cols;

        // Must match HashCell in the plug shader and SpikeCell.Hash in C#.
        int HashCell(int3 c)
        {
            int h = c.x * 73856093;
            h ^= c.y * 19349663;
            h ^= c.z * 83492791;
            return h;
        }

        // The second home. A bigger grid only makes a clash rarer; two
        // independent slots make it 1/N squared.
        int HashCell2(int3 c)
        {
            int h = c.x * 12582917;
            h ^= c.y * 3145739;
            h ^= c.z * 6291469;
            return h;
        }

        // Bump on ANY protocol constant change (grid, cell size, origin, slot
        // size, levels): a mismatched socket decodes as a phantom a few cm away.
        // Rides the tag, which already rejects wrong-cell payloads: no extra read.
        #define YAPS_ATLAS_VERSION 2

        // Identifies the owning cell: a slot-sharing cell's payload decodes
        // plausibly against any cell. Must match the plug's CellTag, version included.
        float CellTag(int3 c)
        {
            int h = c.x * 19349663;
            h ^= c.y * 83492791;
            h ^= c.z * 73856093;
            h ^= YAPS_ATLAS_VERSION * 1566083941;
            h = h & 0x7FFFFF;
            return (h % 256) / 255.0;
        }

        // Everything a quad needs to know about where it belongs.
        void Place(float4 vertex, out int cellPx, out int cellPy,
                   out int sub, out float3 payload, out float3 facing, out float tag)
        {
            int levels = max(int(_Levels), 1);
            int q = int(vertex.z + 0.5);
            int level = min(q / 2, levels - 1);
            int home = q % 2;

            // Each level is four times the last, so six of them span a
            // factor of a thousand: two centimetres to twenty metres.
            float size = _CellSize * pow(4.0, level);
            float3 world = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
            float3 scaled = world / max(size, 1e-6);
            int3 cell = int3(floor(scaled));
            float3 f = frac(scaled);

            int grid = max(int(_Grid), 1);
            int total = grid * grid;
            int idx = HashCell(cell) % total;
            if (idx < 0) idx += total;
            if (home == 1)
            {
                int step = HashCell2(cell) % max(total - 1, 1);
                if (step < 0) step += max(total - 1, 1);
                idx = (idx + step + 1) % total;
            }
            int cols = max(int(_Cols), 1);
            int gx = idx % cols, gy = idx / cols;
            int rowsPerLevel = max(total / cols, 1);

            int slot = max(int(_SlotPx), 1);
            cellPx = int(_OriginPx) + gx * 17 * slot;
            cellPy = int(_OriginPx) + (level * rowsPerLevel + gy) * slot;

            // The octant is the bucket.
            sub = (f.x > 0.5 ? 4 : 0) + (f.y > 0.5 ? 2 : 0) + (f.z > 0.5 ? 1 : 0);

            payload = f;
            facing = normalize(mul((float3x3)unity_ObjectToWorld, float3(0, 0, 1))) * 0.5 + 0.5;
            tag = 0.5 + 0.5 * CellTag(cell);
        }

        float2 ToClip(float2 atPx)
        {
            float2 p = atPx / _ScreenParams.xy * 2.0 - 1.0;
            p.y = -p.y;                       // pixel rows count down from the top
            return p;
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
                Place(v.vertex, cx, cy, sub, pay, fwd, tag);

                float slot = max(_SlotPx, 1);
                float2 unit = v.vertex.xy + 0.5;
                o.pos = float4(ToClip(float2(cx, cy) + unit * slot), UNITY_NEAR_CLIP_VALUE, 1);

                // A half float holds 1/255 steps exactly at these magnitudes.
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
                Place(v.vertex, cx, cy, sub, pay, fwd, tag);

                float slot = max(_SlotPx, 1);
                float2 unit = v.vertex.xy + 0.5;
                float2 atPx;
                atPx.x = cx + (1 + 2 * sub) * slot + unit.x * 2 * slot;
                atPx.y = cy + unit.y * slot;
                o.pos = float4(ToClip(atPx), UNITY_NEAR_CLIP_VALUE, 1);

                o.payload = float4(pay, tag);
                // Alpha carries the kind: a second tag copy could never disagree (same draw).
                o.other   = float4(fwd, (floor(_Kind) + 1) / 16.0);
                o.u = unit.x;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return i.u < 0.5 ? i.payload : i.other; }
            ENDCG
        }
    }
}
