// Spike 4: a plug that bends into a socket it reads out of the screen.
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
        _CellSize ("World cell size, metres", Float) = 0.5
        _MeshLength ("Shaft length in the mesh, local units", Float) = 1
        _Reach ("How far it reaches, in plug lengths", Range(0.5, 4)) = 1.6
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
            float _Debug, _ForceRow;
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
                float size  = max(_CellSize, 0.0001);
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

                int3   mine  = int3(floor(root / size));
                float3 best  = 0;
                float  bestD = 1e9;
                int    bestPx = -1, bestPy = 0;

                [unroll] for (int dx = -1; dx <= 1; dx++)
                [unroll] for (int dy = -1; dy <= 1; dy++)
                [unroll] for (int dz = -1; dz <= 1; dz++)
                {
                    int3 c = mine + int3(dx, dy, dz);
                    int idx = HashCell(c) % total;
                    if (idx < 0) idx += total;
                    int gx = idx % grid, gy = idx / grid;

                    int px = int(_OriginPx) + gx * 2 * slot + mid;
                    int fromTop = int(_OriginPx) + gy * slot + mid;
                    int py = flip ? (texH - 1 - fromTop) : fromTop;

                    float4 got = YAPS_LOAD(px, py);
                    if (got.a < 0.5) continue;
                    hits++;

                    float3 at = (float3(c) + got.rgb) * size;
                    float d = distance(at, root);
                    // A hash collision from across the world decodes to a
                    // plausible payload in an implausible place. Anything
                    // further than the neighbourhood could reach is one.
                    if (d > size * 4) continue;
                    if (d < bestD) { bestD = d; best = at; bestPx = px; bestPy = py; }
                }

                float3 pos = wv, nrm = wn;
                float engaged = 0;

                if (bestPx >= 0)
                {
                    // One more read for the winner's orientation, rather
                    // than reading both slots for all twenty-seven.
                    float4 f = YAPS_LOAD(bestPx + slot, bestPy);
                    float3 sockFwd = normalize(f.rgb * 2 - 1);

                    engaged = 1 - smoothstep(len * _Reach, len * _Reach * 1.6, bestD);
                    if (engaged > 0)
                    {
                        // Leaves the root where it is, arrives along the
                        // socket's own axis. The handle length is a third of
                        // the gap, the usual cubic compromise between a lazy
                        // curve and an S bend.
                        float3 p0 = root;
                        float3 p3 = best;
                        float  gap = max(bestD, 1e-4);
                        float3 p1 = p0 + axis * (gap / 3);
                        float3 p2 = p3 + sockFwd * (gap / 3);

                        // Where this vertex sits along the shaft, and how far
                        // off the axis. Taken in WORLD space so the object's
                        // scale and rotation come along for free.
                        float s = dot(wv - root, axis);
                        float3 radial = wv - (root + axis * s);

                        float3 at, tangent;
                        if (s <= gap)
                        {
                            // ponytail: parameterised by u rather than by arc
                            // length, so the shaft stretches slightly through
                            // the bend. Fine for a spike; the shipped deform
                            // reparameterises off the bake.
                            float u = saturate(s / gap);
                            at = Bez(p0, p1, p2, p3, u);
                            tangent = normalize(BezT(p0, p1, p2, p3, u));
                        }
                        else
                        {
                            // Past the mouth: straight on into the socket.
                            // This is the whole of insertion depth here.
                            tangent = normalize(-sockFwd);
                            at = p3 + tangent * (s - gap);
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
                // red   how many cells claimed a socket, over 27
                // green found one at all
                // blue  which row order was used
                o.dbg = float3(saturate(hits / 27.0), bestPx >= 0 ? 1 : 0, flip ? 1 : 0);
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
