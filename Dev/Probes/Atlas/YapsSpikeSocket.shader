// Spike 4: a socket that publishes everything a plug needs to bend into it.
//
// Spike 3 published a POSITION and nothing else, which is enough to find a
// socket and not enough to enter one. A plug bending toward a bare point
// curves at it and stops; rotating the socket does nothing, because there is
// no axis to arrive along.
//
// So each socket takes TWO pixels rather than one:
//
//   slot 0    rgb = frac(worldPos / cellSize)      where it is
//   slot 1    rgb = forward * 0.5 + 0.5            which way it faces
//   both      a   = 1                              occupancy
//
// One quad covers both, and the FRAGMENT decides which payload it is from
// where it landed inside the quad. Two draws would work as well and cost
// twice the setup for nothing.
//
// Forward is the object's local +Z, matching the rest of YAPS: a plain mesh
// publishes its origin and its +Z, because that is the one convention that
// holds for a socket prop, a socket on an avatar, and the bake.
//
// The cell layout follows spike 3 exactly, pixel snapping included. A cell
// is now two slots wide, so the atlas is 2*grid by grid.
Shader "YAPS/Spike Socket"
{
    Properties
    {
        _Grid ("Cells across the atlas", Float) = 8
        _SlotPx ("One slot, in pixels", Float) = 1
        _OriginPx ("Atlas origin, pixels from the top left", Float) = 8
        _CellSize ("World cell size, metres", Float) = 0.5
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
            #pragma target 4.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 payload : TEXCOORD0;   // slot 0
                float4 other   : TEXCOORD1;   // slot 1
                float  u       : TEXCOORD2;   // which half of the quad
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float _Grid;
            float _SlotPx;
            float _OriginPx;
            float _CellSize;

            // Must match HashCell in YapsSpikeCell.shader and SpikeCell.Hash
            // in C#, wrap included. Integer, never a float hash: the two
            // sides have to agree bit for bit and a sin/frac hash evaluated
            // in double on one side does not have to.
            int HashCell(int3 c)
            {
                int h = c.x * 73856093;
                h ^= c.y * 19349663;
                h ^= c.z * 83492791;
                return h;
            }

            // A SECOND, independent hash, carried in alpha as a tag.
            //
            // The grid has 64 slots and a radius-2 read looks at 125 cells,
            // so by pigeonhole some of them land on a slot another cell
            // owns. The payload that comes back is a real socket's, decoded
            // against the WRONG cell, which puts a convincing phantom
            // somewhere in the neighbourhood. Nothing about the payload can
            // reveal that on its own: an offset within a cell always decodes
            // into that cell, whichever cell you decode it against.
            //
            // So the writer stamps who it is and the reader checks. 256
            // levels over the top half of alpha, which is four half-float
            // steps apart and therefore actually distinguishable, and the
            // bottom half stays free for "empty".
            float CellTag(int3 c)
            {
                int h = c.x * 19349663;
                h ^= c.y * 83492791;
                h ^= c.z * 73856093;
                h = h & 0x7FFFFF;
                return (h % 256) / 255.0;
            }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 world = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float size = max(_CellSize, 0.0001);
                float3 scaled = world / size;
                int3 cell = int3(floor(scaled));

                int grid = max(int(_Grid), 1);
                int total = grid * grid;
                int idx = HashCell(cell) % total;
                if (idx < 0) idx += total;
                int cx = idx % grid;
                int cy = idx / grid;

                // Whole pixels, because a cell is one or two pixels wide and
                // clip-space placement drifts a fraction further with every
                // grid index. Measured in spike 3: grid (0,4) read cleanly
                // while (6,7) read its neighbour.
                float slot = max(_SlotPx, 1);
                float2 unit = v.vertex.xy + 0.5;
                float2 atPx;
                atPx.x = _OriginPx + cx * 2 * slot + unit.x * 2 * slot;
                atPx.y = _OriginPx + cy * slot + unit.y * slot;
                float2 p = atPx / _ScreenParams.xy * 2.0 - 1.0;
                p.y = -p.y;                       // pixel rows count down from the top
                o.pos = float4(p, UNITY_NEAR_CLIP_VALUE, 1);

                float tag = 0.5 + 0.5 * CellTag(cell);
                o.payload = float4(frac(scaled), tag);

                // Local +Z in world. Normalised here rather than in the
                // fragment so a non-uniform scale cannot skew it, and mapped
                // into 0..1 because the grab has no signed format.
                float3 fwd = normalize(mul((float3x3)unity_ObjectToWorld, float3(0, 0, 1)));
                o.other = float4(fwd * 0.5 + 0.5, tag);

                o.u = unit.x;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return i.u < 0.5 ? i.payload : i.other;
            }
            ENDCG
        }
    }
}
