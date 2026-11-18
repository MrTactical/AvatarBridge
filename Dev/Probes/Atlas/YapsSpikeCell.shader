// Spike 3: publish into a cell derived from WHERE THIS OBJECT IS.
//
// Plug and socket derive the same cell from world position, like a
// broadphase spatial hash, so they never negotiate.
//
//   cell    = floor(worldPos / cellSize)          a 3D integer coordinate
//   index   = hash(cell) % (N*N)                  a square on the screen
//   payload = frac(worldPos / cellSize)           where it sits INSIDE the cell
//
// Precision = cellSize x half-float step, ~0.24 mm at 0.5 m; range comes from
// the cell count (spike 2: a bigger box trades precision for range).
// Integer hash, not sin/frac: C# (double) and GPU (float) must agree exactly.
// Worth target 4.0.
Shader "YAPS/Spike Cell"
{
    Properties
    {
        _Corner ("Atlas corner, clip space", Vector) = (-0.95, 0.95, 0, 0)
        _CellPixels ("One cell, clip space", Float) = 0.02
        _CellPx ("One cell in PIXELS, 0 to use clip space", Float) = 0
        _Grid ("Cells across the atlas", Float) = 8
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
                float4 payload : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _Corner;
            float _CellPixels;
            float _CellPx;
            float _Grid;
            float _CellSize;

            // Must match SpikeCell.Hash in C# exactly, including the wrap.
            int HashCell(int3 c)
            {
                int h = c.x * 73856093;
                h ^= c.y * 19349663;
                h ^= c.z * 83492791;
                return h;
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
                if (idx < 0) idx += total;           // C# and HLSL both truncate toward zero
                int cx = idx % grid;
                int cy = idx / grid;

                // The patch goes where the HASH says, not where the object is.
                float2 unit = v.vertex.xy + 0.5;
                float2 p;
                if (_CellPx > 0.5)
                {
                    // Snapped to whole pixels: in clip units a 0.002 cell is
                    // 1.92 px at 1920 and drift accumulates with the index
                    // (measured: (0,4) fine, (6,7) read a neighbour).
                    float2 originPx = float2(8, 8);
                    float2 cellPx = float2(_CellPx, _CellPx);
                    float2 atPx = originPx + float2(cx, cy) * cellPx + unit * cellPx;
                    p = atPx / _ScreenParams.xy * 2.0 - 1.0;
                    p.y = -p.y;                       // pixel rows count down from the top
                }
                else
                {
                    float2 at = _Corner.xy + float2(cx, -cy) * _CellPixels;
                    p = at + unit * _CellPixels * float2(1, -1);
                }
                o.pos = float4(p, UNITY_NEAR_CLIP_VALUE, 1);

                // Where it sits inside its own cell, already 0..1.
                o.payload = float4(frac(scaled), 1);
                return o;
            }

            // Alpha 1 = occupied. Whether it survives the grab is under test.
            fixed4 frag (v2f i) : SV_Target { return i.payload; }
            ENDCG
        }
    }
}
