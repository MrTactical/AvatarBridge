// Spike 4: a plug that threads through every socket it can see.
//
// A list, not a winner: picking the nearest-to-root socket flipped between
// two in range and let one behind beat one in front. Sockets form an ordered
// list, each claiming an arc-length range, and the shaft is a chain of cubics
// sharing position and tangent at every join. Ring plus hole, portal (a gap
// between ranges) and duplicate all become list entries; only the first is built.
// Hashes the shaft MIDPOINT, halving the radius needed. Cell size is a
// protocol constant (sockets hash with it): small buys precision, costs reach.
// Sockets per plug are capped at 2r+1 by the neighbourhood (cell >= L/2r, one
// socket per cell), not by YAPS_MAX: 3 at radius 1, 5 at radius 2 (125 reads vs 27).
// Demonstration only (a cubic-bent cylinder); the shipping deform is yaps_deform.cginc.
//
// Reading the atlas:
//   .Load, not tex2Dlod: any filtering blends one-pixel cells (spike 3).
//   Row order is compile time (UNITY_UV_STARTS_AT_TOP), not the runtime sign
//   of _TexelSize.y: that read the opaque screen in all 27 cells (measured:
//   debug white with it, green without). The writer places from the top.
//   Under single-pass instanced the grab is an array; both slices match, so slice 0.
Shader "YAPS/Spike Plug"
{
    Properties
    {
        _Grid ("Cells across the atlas", Float) = 8
        _Cols ("Columns the slots fold into", Float) = 32
        _SlotPx ("One slot, in pixels", Float) = 1
        _OriginPx ("Atlas origin, pixels from the top left", Float) = 8
        _CellSize ("Cell size of level 0, metres", Float) = 0.02
        _Levels ("How many levels", Float) = 6
        _MeshLength ("Shaft length in the mesh, local units", Float) = 1
        _Reach ("How far it reaches, in plug lengths", Range(0.5, 4)) = 1.6
        // Caps sockets per plug at 2r+1. Radius 2 costs 125 reads vs 27.
        _Radius ("Neighbourhood radius", Range(0, 3)) = 2
        _Colour ("Colour", Color) = (0.85, 0.6, 0.62, 1)
        _Miss ("Colour when it finds nothing", Color) = (0.45, 0.45, 0.48, 1)
        // 1 paints what the resolve decided: tells nothing found from a wrong row.
        _Debug ("Debug colours", Float) = 0
        // Kept: the platform default is right on D3D but unverified in game.
        _ForceRow ("Row order: 0 auto, 1 top, 2 bottom", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            // Two-sided: the normals are authored outward by the generator,
            // so nothing depends on which way the triangles happen to wind.
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            // Never binding: the neighbourhood caps at 2r+1 (7 at radius 3). ~9 floats of registers each.
            #define YAPS_MAX 8

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 nrm : TEXCOORD0;
                float  engaged : TEXCOORD1;
                float3 dbg : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Declared for .Load: UNITY_DECLARE_SCREENSPACE_TEXTURE is a sampler2D outside stereo.
            #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                Texture2DArray _YAPS_Atlas;
                #define YAPS_LOAD(px, py) _YAPS_Atlas.Load(int4(px, py, 0, 0))
            #else
                Texture2D _YAPS_Atlas;
                #define YAPS_LOAD(px, py) _YAPS_Atlas.Load(int3(px, py, 0))
            #endif
            float4 _YAPS_Atlas_TexelSize;

            float _Grid, _SlotPx, _OriginPx, _CellSize, _MeshLength, _Reach;

            // Columns, not the grid: a 1088 px row was clipped by narrower cameras,
            // and a clipped slot reads the opaque screen, so sockets vanished.
            // 4096 at 32 columns is 544 x 512, fitting 720p, mirrors and eye buffers.
            // C# derives it for both shaders.
            float _Cols;
            float _Debug, _ForceRow, _Radius, _Levels;
            fixed4 _Colour, _Miss;

            // Must match HashCell in the socket shader and SpikeCell.Hash in
            // C#, wrap included.
            int HashCell(int3 c)
            {
                int h = c.x * 73856093;
                h ^= c.y * 19349663;
                h ^= c.z * 83492791;
                return h;
            }

            // Must match HashCell2 in the socket shader. Every socket lives
            // in two slots, so a clash on the first one is recoverable.
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

            // Must match CellTag in the socket shader, version included. See
            // the note there: without it a radius-2 read aliases into itself.
            float CellTag(int3 c)
            {
                int h = c.x * 19349663;
                h ^= c.y * 83492791;
                h ^= c.z * 73856093;
                h ^= YAPS_ATLAS_VERSION * 1566083941;
                h = h & 0x7FFFFF;
                return (h % 256) / 255.0;
            }

            float3 Bez(float3 a, float3 b, float3 c, float3 d, float u)
            {
                float3 ab = lerp(a, b, u), bc = lerp(b, c, u), cd = lerp(c, d, u);
                return lerp(lerp(ab, bc, u), lerp(bc, cd, u), u);
            }

            float3 BezT(float3 a, float3 b, float3 c, float3 d, float u)
            {
                float w = 1 - u;
                return 3 * w * w * (b - a) + 6 * w * u * (c - b) + 3 * u * u * (d - c);
            }

            // Minimal rotation: a frame from a reference up vector twists as the tangent swings past it.
            float3 RotateTo(float3 v, float3 from, float3 to)
            {
                float3 ax = cross(from, to);
                float s = length(ax);
                float c = dot(from, to);
                if (s < 1e-5) return c > 0 ? v : -v;
                ax /= s;
                float ang = atan2(s, c);
                float sa = sin(ang), ca = cos(ang);
                return v * ca + cross(ax, v) * sa + ax * dot(ax, v) * (1 - ca);
            }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 wv   = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 wn   = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                float3 root = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float3 axis = normalize(mul((float3x3)unity_ObjectToWorld, float3(0, 0, 1)));
                float  len  = length(mul((float3x3)unity_ObjectToWorld, float3(0, 0, _MeshLength)));

                // ---- find a socket, knowing nothing ----
                // Levels step 4x so one atlas serves 20 cm and 20 m plugs. Read only
                // the level nearest L/2r; sockets publish to all.
                int levels = max(int(_Levels), 1);
                int R = clamp(int(_Radius), 0, 3);
                float wantCell = len / max(2.0 * R, 1.0);
                int lvl = clamp(int(round(log2(wantCell / max(_CellSize, 1e-6)) * 0.5)), 0, levels - 1);
                float size  = max(_CellSize * pow(4.0, lvl), 1e-6);
                int   grid  = max(int(_Grid), 1);
                int   total = grid * grid;
                int   slot  = max(int(_SlotPx), 1);
                int   cols  = max(int(_Cols), 1);
                int   rowsPerLevel = max(total / cols, 1);
                int   mid   = slot / 2;
                int   texH  = int(_YAPS_Atlas_TexelSize.w);
                #if UNITY_UV_STARTS_AT_TOP
                    bool platformFlip = false;   // memory row 0 is the top
                #else
                    bool platformFlip = true;
                #endif
                bool flip = _ForceRow > 0.5 ? (_ForceRow > 1.5) : platformFlip;
                // 27 means the clear is not running.
                int hits = 0;

                // Engagement reaches zero here; also the list's inclusion test.
                float far = len * _Reach * 1.6;

                // Midpoint, so radius 1 covers the whole plug, not just the root half.
                int3 mine = int3(floor((root + axis * (len * 0.5)) / size));

                float3 sockP[YAPS_MAX];
                float3 sockF[YAPS_MAX];
                float  sockD[YAPS_MAX];
                int    sockK[YAPS_MAX];
                [unroll] for (int i = 0; i < YAPS_MAX; i++)
                {
                    sockP[i] = 0; sockF[i] = float3(0, 0, 1); sockD[i] = 1e9; sockK[i] = -1;
                }

                // [loop]: the bound is a property, so widening it can be measured.
                [loop] for (int dx = -R; dx <= R; dx++)
                [loop] for (int dy = -R; dy <= R; dy++)
                [loop] for (int dz = -R; dz <= R; dz++)
                {
                    int3 c = mine + int3(dx, dy, dz);
                    float tagWant = CellTag(c);

                    // Both homes of this cell, computed up front so the
                    // recovery path below is a lookup rather than a rebuild.
                    int idx = HashCell(c) % total;
                    if (idx < 0) idx += total;
                    int step = HashCell2(c) % max(total - 1, 1);
                    if (step < 0) step += max(total - 1, 1);
                    int idxB = (idx + step + 1) % total;

                    [loop] for (int home = 0; home < 2; home++)
                    {
                        int use = home == 0 ? idx : idxB;
                        int gx = use % cols, gy = use / cols;
                        int cellX = int(_OriginPx) + gx * 17 * slot;
                        int fromTop = int(_OriginPx) + (lvl * rowsPerLevel + gy) * slot;
                        int cellY = flip ? (texH - 1 - fromTop) : fromTop;

                        // One header tap (alpha = socket count); empty cells stop here,
                        // which makes eight buckets affordable. A count, not an octant
                        // bitmask: additive blending carries 1+1 into the wrong bit.
                        int held = int(round(YAPS_LOAD(cellX, cellY).a * 255.0));
                        if (held == 0) break;      // nothing here, and home 1
                                                   // is always written, so
                                                   // home 2 cannot hold it
                        bool got1 = false;
                        [loop] for (int sub = 0; sub < 8; sub++)
                        {
                            int px = cellX + (1 + 2 * sub) * slot;
                            float4 got = YAPS_LOAD(px, cellY);
                            if (got.a < 0.5) continue;
                            // The header sums every cell sharing the slot; the tag settles it.
                            if (abs((got.a - 0.5) * 2 - tagWant) > 0.001) continue;
                            got1 = true;
                            hits++;

                            float3 at = (float3(c) + got.rgb) * size;
                            float d = distance(at, root);

                            // Facing read per socket found; its kind is needed before the sort.
                            float4 f4 = YAPS_LOAD(px + slot, cellY);
                            float3 fwd = normalize(f4.rgb * 2 - 1);
                            int kind = int(round(f4.a * 16.0)) - 1;

                            // A hole is never entered from behind. A ring has no
                            // wrong side; it is turned to meet the plug below.
                            if (kind == 1 && dot(fwd, at - root) > 0) continue;
                            // Rejects both far hash collisions and unreachable sockets.
                            if (d > far) continue;

                            // Nearest first: the order is the path.
                            [unroll] for (int k = 0; k < YAPS_MAX; k++)
                            {
                                if (d >= sockD[k]) continue;
                                [unroll] for (int m = YAPS_MAX - 1; m > k; m--)
                                {
                                    sockD[m] = sockD[m - 1]; sockP[m] = sockP[m - 1];
                                    sockF[m] = sockF[m - 1]; sockK[m] = sockK[m - 1];
                                }
                                sockD[k] = d; sockP[k] = at; sockF[k] = fwd; sockK[k] = kind;
                                break;
                            }
                        }
                        // Only a cell whose first home was taken uses the second.
                        if (got1) break;
                    }
                }

                int count = 0;
                [unroll] for (int i2 = 0; i2 < YAPS_MAX; i2++)
                    if (sockK[i2] >= 0) count++;

                // Arc length per socket, by chords rather than true cubic length.
                float arc[YAPS_MAX + 1];
                arc[0] = 0;
                float3 prev = root;
                [unroll] for (int i3 = 0; i3 < YAPS_MAX; i3++)
                {
                    if (i3 >= count) { arc[i3 + 1] = arc[i3] + 1e6; continue; }
                    float3 step = sockP[i3] - prev;
                    float d3 = length(step);
                    // A ring turns to meet its approach (its authored facing is
                    // arbitrary; trusting it hairpins the cubic). A hole keeps its sign.
                    if (sockK[i3] == 0 && d3 > 1e-5 && dot(sockF[i3], step) > 0)
                        sockF[i3] = -sockF[i3];
                    arc[i3 + 1] = arc[i3] + d3;
                    prev = sockP[i3];
                }

                float3 pos = wv, nrm = wn;
                float engaged = 0;

                if (count > 0)
                {
                    engaged = 1 - smoothstep(len * _Reach, far, sockD[0]);
                    if (engaged > 0)
                    {
                        // World space, so object scale and rotation come for free.
                        float s = dot(wv - root, axis);
                        float3 radial = wv - (root + axis * s);

                        // Which link of the chain this arc length falls in.
                        int seg = 0;
                        [unroll] for (int i4 = 0; i4 < YAPS_MAX; i4++)
                            if (i4 < count && s > arc[i4 + 1]) seg = i4 + 1;

                        float3 at, tangent;
                        if (seg >= count)
                        {
                            // Past the last socket: straight on out of it.
                            // This is the whole of insertion depth here.
                            tangent = -sockF[count - 1];
                            at = sockP[count - 1] + tangent * (s - arc[count]);
                        }
                        else
                        {
                            // Position and tangent match at every join, or the shaft kinks.
                            float3 p0 = seg == 0 ? root : sockP[seg - 1];
                            float3 t0 = seg == 0 ? axis : -sockF[seg - 1];
                            float3 p3 = sockP[seg];
                            float3 t3 = sockF[seg];
                            float  L  = max(arc[seg + 1] - arc[seg], 1e-4);
                            // ponytail: parameterised by u rather than by arc
                            // length, so a link stretches slightly through
                            // its bend. The shipped deform reparameterises
                            // off the bake.
                            float u = saturate((s - arc[seg]) / L);
                            float3 p1 = p0 + t0 * (L / 3);
                            float3 p2 = p3 + t3 * (L / 3);
                            at = Bez(p0, p1, p2, p3, u);
                            tangent = normalize(BezT(p0, p1, p2, p3, u));
                        }

                        float3 bent  = at + RotateTo(radial, axis, tangent);
                        float3 bentN = RotateTo(wn, axis, tangent);
                        pos = lerp(wv, bent, engaged);
                        nrm = normalize(lerp(wn, bentN, engaged));
                    }
                }

                o.pos = mul(UNITY_MATRIX_VP, float4(pos, 1));
                o.nrm = nrm;
                o.engaged = engaged;
                // red   how many payloads matched, over eight
                // green how many made the LIST, over four
                // blue  which row order was used
                o.dbg = float3(saturate(hits / 8.0), count / (float)YAPS_MAX, flip ? 1 : 0);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // A fixed light, so the spike looks the same in any scene.
                float l = saturate(dot(normalize(i.nrm), normalize(float3(0.4, 0.8, -0.45)))) * 0.7 + 0.3;
                // Shape alone cannot show a working read when the plug already points at the socket.
                if (_Debug > 0.5) return fixed4(i.dbg, 1);
                fixed4 c = lerp(_Miss, _Colour, saturate(i.engaged * 2));
                return fixed4(c.rgb * l, 1);
            }
            ENDCG
        }
    }
}
