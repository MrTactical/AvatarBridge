// Spike 5: a socket that publishes into a bucketed, multi-resolution atlas.
//
// Two limits killed at once, because they are the same change: a cell that
// holds more than one thing.
//
// ---------------------------------------------------------------------
// BUCKETS: eight sockets per cell, and a HEADER so reading stays cheap
// ---------------------------------------------------------------------
//
// One socket per cell forced sockets a whole cell apart, which capped a plug
// at 2r-1 of them, and left the one clash double hashing cannot fix: two
// homes are a property of the CELL, so two sockets in one cell share both.
//
// A cell now holds eight, indexed by which OCTANT of the cell the socket sits
// in. That is deterministic, needs no negotiation, and separates anything
// more than half a cell apart in any axis. Two sockets in the same octant
// still collide, which is the genuinely ambiguous case.
//
// Reading all eight in every cell would multiply every tap by eight. So the
// cell carries a HEADER pixel first, and a reader takes one tap for it and
// only opens the cell if it says anything is there. Nearly every cell is
// empty, so nearly every cell still costs one tap, the same as before
// buckets existed.
//
// The header is a COUNT, built with ADDITIVE blending on the alpha channel
// alone: every socket adds 1/255 and the sum is exactly how many sockets sit
// in that slot. Alpha only, so it is invisible, and the clear pass zeroes it
// for free.
//
// It was a BITMASK of live octants first, which is strictly more information
// and does not survive contact with two sockets in one octant: 1+1 is 2, so
// the octant that IS occupied stops being advertised and a neighbouring one
// that is not starts being. With several sockets in a cell the carries
// cascade and the whole cell goes unreadable, which is worse than losing the
// pair that collided. Addition is only an OR while the bits differ.
//
// A count cannot carry. Zero still means empty, which is the case that has to
// be cheap because most cells are, and a cell that holds anything costs eight
// reads that nothing else was going to need.
//
// ---------------------------------------------------------------------
// LEVELS: several cell sizes at once
// ---------------------------------------------------------------------
//
// Cell size is a protocol constant, so one number has to serve a twenty
// centimetre plug and a twenty metre one. It cannot: coverage wants a cell
// about L/2r, and those two differ by a hundred times.
//
// So the atlas holds several, each level four times the last, each in its own
// band of rows. A socket publishes to ALL levels, which costs one more small
// draw each and nothing to read. A plug reads only the level that matches its
// own length, so read cost does not change at all.
//
// ---------------------------------------------------------------------
// LAYOUT
// ---------------------------------------------------------------------
//
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
        // 0 ring, 1 hole. A ring is a loop and can be entered from either
        // face; a hole has a front and a back. Sixteen kinds fit in the
        // facing pixel's alpha, which was only repeating the tag.
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

        // HOW MANY COLUMNS the atlas folds its slots into, and it is NOT the grid.
        // The two were the same number for no reason, which made the rect a wide
        // strip: 4096 slots at 17 pixels a cell is 1088 px across, and a camera
        // narrower than that clips the right-hand columns. A clipped slot does not
        // read as absent, it reads as the opaque screen, so a socket whose cell
        // hashes there vanishes until it moves to another cell. Found in the editor
        // 2026-09-03 as a bend dropping out every few centimetres and returning when
        // the Game view was widened.
        //
        // Folding is free: the same slots, a squarer block. 4096 at 32 columns is
        // 544 x 512 instead of 1088 x 256, which fits a 720p view, a mirror texture
        // and an eye buffer. C# derives it and sets it on both shaders, so there is
        // one derivation rather than two that have to agree.
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

        // THE ATLAS PROTOCOL VERSION. Grid, cell size, origin, slot size and level
        // count are constants EVERY avatar in the instance shares, because a socket
        // hashes its world position with them and a plug addresses cells with them.
        // So a socket written by a later version lands where this reader looks and
        // decodes against the wrong constants, which is not absence: it reads as a
        // real socket a few centimetres away, the same phantom a slot clash makes.
        //
        // It rides the TAG rather than a pixel of its own. The tag exists to reject
        // a payload decoded against the wrong cell, and a version mismatch is that
        // failure exactly, so the rejection is already written: no extra pixel, no
        // extra read, no extra branch. Unretrofittable, which is why it is here
        // before anything ships, and it must be bumped whenever ANY protocol
        // constant changes.
        #define YAPS_ATLAS_VERSION 2

        // Who owns this payload. A cell that merely shares a grid slot hands
        // back somebody else's socket decoded against the wrong cell, and an
        // offset within a cell decodes into that cell whichever cell you
        // decode it against, so the payload cannot reveal it alone.
        //
        // Must match CellTag in the plug shader, version included.
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

            // The octant, which is the whole of the bucket scheme: no
            // negotiation, and anything more than half a cell apart in any
            // axis lands somewhere else.
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

                // One count, added in. A half float resolves 1/255 to about
                // a hundred and thirty steps at these magnitudes, so the sum
                // survives the grab exactly.
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
                // The facing pixel's alpha carried a second copy of the tag,
                // which nothing read: it is only reached once the position
                // pixel's tag matched, and both are written by the same draw,
                // so it cannot disagree. It carries the KIND instead.
                o.other   = float4(fwd, (floor(_Kind) + 1) / 16.0);
                o.u = unit.x;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return i.u < 0.5 ? i.payload : i.other; }
            ENDCG
        }
    }
}
