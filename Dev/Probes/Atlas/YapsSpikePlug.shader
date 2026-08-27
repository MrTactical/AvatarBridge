// Spike 4: a plug that threads through every socket it can see.
//
// A LIST, NOT A WINNER. The first version picked the nearest socket and threw
// the rest of the neighbourhood away, which is what yaps_resolve.cginc does
// today. Two sockets in range then made the plug flip between them as
// whichever was momentarily closer to its ROOT changed, and a socket behind
// it could beat one in front, because distance-to-root never asks which way
// a plug points.
//
// So the sockets are kept as an ORDERED LIST and the plug becomes a path
// rather than an aim. Each socket claims a range of ARC LENGTH along the
// shaft, and the shaft is a chain of cubics: leave the root along the plug's
// own axis, arrive at socket one against its facing, leave socket one out the
// far side, arrive at socket two. Position and tangent are continuous at
// every join because the segments either side share them.
//
// Three wanted features are then the same change: a ring mid-shaft and a hole
// at the tip are two entries; a portal is two entries with a GAP between
// their ranges; a duplicate is one range mapped twice. Only the first is
// built here.
//
// A radius-1 read covers one cell either side, so the plug hashes the
// MIDPOINT of its shaft rather than the root, which halves the radius the
// same coverage needs. Cell size is a protocol constant, not a per-plug
// setting, since sockets hash with it too: small buys precision and costs
// reach.
//
// HOW MANY SOCKETS. Not the length of the list. A plug spanning L has to see
// cells covering L/2 either side of its midpoint, so cell >= L/2r; and one
// cell holds one socket, so sockets sit at least a cell apart. Divide the
// one by the other and the answer is 2r+1, whatever the list length: three
// at radius 1, five at radius 2 for 125 reads instead of 27. Raising
// YAPS_MAX past that buys nothing. The way out is a cell holding SEVERAL
// sockets, which decouples spacing from cell size and is not built here.
//
// No lights, no contacts, no animator parameters, no sync. The socket draws
// two pixels; the plug hashes its own world position, reads the twenty-seven
// cells around it, and deforms. Nothing is wired between them and nothing
// tells either side the other exists.
//
// This is the demonstration, not the shipping deform. The real one in
// yaps_deform.cginc works off a bake: a measured frame per vertex, an
// arc-length parameterisation, tip and root behaviour. This takes a plain
// cylinder along local +Z and bends it along a cubic, which is enough to
// show that the TRANSPORT carries a usable socket and cheap enough that a
// failure is obviously the transport's rather than the deform's.
//
// Reading the atlas:
//
//   .Load, not tex2Dlod. A cell is one pixel and any filtering blends it
//   with its neighbours, which is exactly the failure spike 3 measured. A
//   point read at integer coordinates is the only correct primitive here,
//   and it is what the shipped deform already uses on _YAPS_Bake.
//
//   Row order is a COMPILE-TIME platform fact, not the runtime sign of
//   _TexelSize.y. That sign is the right signal for a UV sample and the
//   wrong one here, which cost a session to find: with it, all twenty-seven
//   cells read the opaque screen, every one reported alpha 1, and the plug
//   locked onto whichever piece of floor decoded nearest. Measured, not
//   argued: the debug colours came back white with it and green without.
//
//   The writer places its patch in pixels from the TOP of the render
//   target and .Load indexes memory rows directly, so the only question is
//   which end of memory the top is, and UNITY_UV_STARTS_AT_TOP answers it.
//
//   Under single-pass instanced the grab is a texture ARRAY. The patch is
//   written in clip space ignoring the eye, so both slices carry identical
//   content and slice 0 is always right.
Shader "YAPS/Spike Plug"
{
    Properties
    {
        _Grid ("Cells across the atlas", Float) = 8
        _SlotPx ("One slot, in pixels", Float) = 1
        _OriginPx ("Atlas origin, pixels from the top left", Float) = 8
        _CellSize ("Cell size of level 0, metres", Float) = 0.02
        _Levels ("How many levels", Float) = 6
        _MeshLength ("Shaft length in the mesh, local units", Float) = 1
        _Reach ("How far it reaches, in plug lengths", Range(0.5, 4)) = 1.6
        // How many cells out to read. This is what actually caps how many
        // sockets a plug can thread: cells one step either way along the
        // shaft is three cells, so three sockets, and no list length changes
        // that. Two costs 125 reads against 27.
        _Radius ("Neighbourhood radius", Range(0, 3)) = 2
        _Colour ("Colour", Color) = (0.85, 0.6, 0.62, 1)
        _Miss ("Colour when it finds nothing", Color) = (0.45, 0.45, 0.48, 1)
        // 0 normal. 1 paints what the resolve DECIDED rather than what it
        // found, because a plug that bends at nothing looks the same as a
        // plug reading the wrong row and only one of those is a row problem.
        _Debug ("Debug colours", Float) = 0
        // 0 takes the platform default, 1 forces top-down, 2 forces
        // bottom-up. Kept past the fix because the platform default is
        // right on D3D by construction and unverified in game.
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

            // How many sockets one plug can thread. Eight, so the list is
            // never the binding constraint: the neighbourhood caps it at
            // 2r+1 long before this does, seven even at radius 3. Each entry
            // costs about nine floats of vertex registers.
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

            // The grab, declared for .Load rather than for sampling. A
            // sampler2D has no Load, and UNITY_DECLARE_SCREENSPACE_TEXTURE
            // expands to one outside stereo instancing.
            #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                Texture2DArray _YAPS_SpikeAtlas;
                #define YAPS_LOAD(px, py) _YAPS_SpikeAtlas.Load(int4(px, py, 0, 0))
            #else
                Texture2D _YAPS_SpikeAtlas;
                #define YAPS_LOAD(px, py) _YAPS_SpikeAtlas.Load(int3(px, py, 0))
            #endif
            float4 _YAPS_SpikeAtlas_TexelSize;

            float _Grid, _SlotPx, _OriginPx, _CellSize, _MeshLength, _Reach;
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

            // Must match CellTag in the socket shader. See the note there:
            // without it a radius-2 read aliases into itself.
            float CellTag(int3 c)
            {
                int h = c.x * 19349663;
                h ^= c.y * 83492791;
                h ^= c.z * 73856093;
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

            // Minimal rotation carrying "from" onto "to", applied to v.
            // Used instead of rebuilding a frame from a reference up vector,
            // which twists as the tangent swings past the reference.
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
                //
                // WHICH LEVEL. The atlas carries several cell sizes at once,
                // each four times the last, because one cell size cannot
                // serve a twenty centimetre plug and a twenty metre one.
                // Coverage wants a cell of about L/2r, so pick the level
                // nearest that and read only there. Costs no extra taps: the
                // socket published to every level, this reads one.
                int levels = max(int(_Levels), 1);
                int R = clamp(int(_Radius), 0, 3);
                float wantCell = len / max(2.0 * R, 1.0);
                int lvl = clamp(int(round(log2(wantCell / max(_CellSize, 1e-6)) * 0.5)), 0, levels - 1);
                float size  = max(_CellSize * pow(4.0, lvl), 1e-6);
                int   grid  = max(int(_Grid), 1);
                int   total = grid * grid;
                int   slot  = max(int(_SlotPx), 1);
                int   mid   = slot / 2;
                int   texH  = int(_YAPS_SpikeAtlas_TexelSize.w);
                #if UNITY_UV_STARTS_AT_TOP
                    bool platformFlip = false;   // memory row 0 is the top
                #else
                    bool platformFlip = true;
                #endif
                bool flip = _ForceRow > 0.5 ? (_ForceRow > 1.5) : platformFlip;
                // How many of the twenty-seven claimed to hold something. If
                // the clear is not running this is 27, and every one of them
                // is a piece of scene colour decoded as a socket.
                int hits = 0;

                // Where engagement reaches zero. Also the inclusion test
                // for the list: a socket the plug cannot reach is not part
                // of the path it takes.
                float far = len * _Reach * 1.6;

                // Hashed on the MIDPOINT of the shaft rather than the root,
                // so a radius-1 read covers the whole plug instead of only
                // the half nearest the body. One line, no extra taps.
                int3 mine = int3(floor((root + axis * (len * 0.5)) / size));

                float3 sockP[YAPS_MAX];
                float  sockD[YAPS_MAX];
                int    sockX[YAPS_MAX], sockY[YAPS_MAX];
                [unroll] for (int i = 0; i < YAPS_MAX; i++)
                {
                    sockP[i] = 0; sockD[i] = 1e9; sockX[i] = -1; sockY[i] = 0;
                }

                // [loop], not [unroll]: the bound is a property now, and the
                // whole point of it being one is being able to measure the
                // cost of widening it rather than arguing about it.
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
                        int gx = use % grid, gy = use / grid;
                        int cellX = int(_OriginPx) + gx * 17 * slot;
                        int fromTop = int(_OriginPx) + (lvl * grid + gy) * slot;
                        int cellY = flip ? (texH - 1 - fromTop) : fromTop;

                        // ONE tap for the header, whose alpha is a bitmask of
                        // which octants of this cell hold anything. An empty
                        // cell costs exactly this and nothing more, which is
                        // what makes eight buckets affordable: reading all
                        // eight unconditionally would multiply every tap by
                        // eight and cost more than the buckets are worth.
                        int mask = int(round(YAPS_LOAD(cellX, cellY).a * 255.0));
                        if (mask == 0) break;      // nothing here, and home 1
                                                   // is always written, so
                                                   // home 2 cannot hold it
                        bool got1 = false;
                        [loop] for (int sub = 0; sub < 8; sub++)
                        {
                            if ((mask & (1 << sub)) == 0) continue;
                            int px = cellX + (1 + 2 * sub) * slot;
                            float4 got = YAPS_LOAD(px, cellY);
                            if (got.a < 0.5) continue;
                            // The header is a SUM across every cell sharing
                            // this slot, so it can advertise octants that
                            // belong to somebody else. The tag is what
                            // settles it.
                            if (abs((got.a - 0.5) * 2 - tagWant) > 0.001) continue;
                            got1 = true;
                            hits++;

                            float3 at = (float3(c) + got.rgb) * size;
                            float d = distance(at, root);
                            // A hash collision from across the world decodes
                            // to a plausible payload in an implausible place,
                            // and so does a socket too far to reach. Both are
                            // the same test.
                            if (d > far) continue;

                            // Insertion sort, nearest first. Sorted here
                            // rather than later because the ORDER IS THE
                            // PATH: socket one is the one the shaft meets
                            // first.
                            [unroll] for (int k = 0; k < YAPS_MAX; k++)
                            {
                                if (d >= sockD[k]) continue;
                                [unroll] for (int m = YAPS_MAX - 1; m > k; m--)
                                {
                                    sockD[m] = sockD[m - 1]; sockP[m] = sockP[m - 1];
                                    sockX[m] = sockX[m - 1]; sockY[m] = sockY[m - 1];
                                }
                                sockD[k] = d; sockP[k] = at; sockX[k] = px; sockY[k] = py;
                                break;
                            }
                        }
                        // Found in this home, so the other one is not this
                        // cell's. Only a cell whose first home was taken by
                        // somebody else falls through to look in the second.
                        if (got1) break;
                    }
                }

                // The facings, one extra read each, and only for sockets that
                // made the list rather than for all twenty-seven cells.
                int count = 0;
                float3 sockF[YAPS_MAX];
                [unroll] for (int i2 = 0; i2 < YAPS_MAX; i2++)
                {
                    sockF[i2] = float3(0, 0, 1);
                    if (sockX[i2] < 0) continue;
                    count++;
                    sockF[i2] = normalize(YAPS_LOAD(sockX[i2] + slot, sockY[i2]).rgb * 2 - 1);
                }

                // The arc length at which each socket sits, measured along
                // the chain. Chords rather than true cubic arc length, the
                // same approximation the single-socket version made.
                float arc[YAPS_MAX + 1];
                arc[0] = 0;
                float3 prev = root;
                [unroll] for (int i3 = 0; i3 < YAPS_MAX; i3++)
                {
                    if (i3 >= count) { arc[i3 + 1] = arc[i3] + 1e6; continue; }
                    float3 step = sockP[i3] - prev;
                    float d3 = length(step);
                    // TURNED TO MEET THE APPROACH. A socket facing the same
                    // way the shaft is travelling means the curve has to
                    // arrive going backwards, and a cubic asked to do that
                    // ties itself in a hairpin and piles the vertices up.
                    //
                    // A ring is enterable from either face, so which of the
                    // two the author happened to point it is not information.
                    // A one-sided socket, a hole, would want the sign
                    // respected instead, and nothing here is one-sided.
                    if (d3 > 1e-5 && dot(sockF[i3], step) > 0) sockF[i3] = -sockF[i3];
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
                        // Where this vertex sits along the shaft, and how far
                        // off the axis. Taken in WORLD space so the object's
                        // scale and rotation come along for free.
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
                            // Leaves along whatever the previous link exited
                            // on and arrives against this socket's facing, so
                            // position AND tangent match at every join. That
                            // continuity is the only reason a chain of cubics
                            // reads as one shaft rather than as kinked parts.
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
                // Colour says whether the atlas read worked, which cannot be
                // seen from the shape alone when the plug happens to be
                // pointing at the socket already.
                if (_Debug > 0.5) return fixed4(i.dbg, 1);
                fixed4 c = lerp(_Miss, _Colour, saturate(i.engaged * 2));
                return fixed4(c.rgb * l, 1);
            }
            ENDCG
        }
    }
}
