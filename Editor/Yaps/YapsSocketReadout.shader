// A readout for a socket that will not be found, drawn beside it.
//
// Its own renderer, a quad at the socket's origin next to the atlas writer,
// hidden until the avatar's "YAPS readout" toggle shows it. The owner layer
// animates its _YAPS_Owner like every renderer carrying the property, and
// Unity hands it the same vertex lights a renderer at the socket gets.
//
// Everything is read back, never assumed. The atlas cells are what a plug
// on this camera would read, found by the socket's own cell tag and its
// position within the cell, so an 8-bit target that rounds the tag wrong,
// a neighbour that took the bucket and a writer that never drew each show
// as what they are.
//
// Six cells in one row, each a colour:
//   1  atlas on this camera   black too small, red wrong screen, green live
//   2  own entry, per level   four stripes, small cells to large: black
//                             nothing there, red the slot holds others but
//                             not this socket, green read back as its own
//   3  owner id               grey unknown, amber known and no entry to
//                             compare, red the atlas carries another,
//                             green the atlas carries it
//   4  kind and number        left: green hole, cyan ring, blue one-way
//                             ring, black not found; right: a bar of the
//                             number out of fifteen, dark for none
//   5  marker light           green this socket's root light reaches here,
//                             grey not
//   6  plug                   grey no plug light, amber a plug's light
//                             reaches but it is not in, green a bar of depth
//
// Queue Overlay, after the atlas grab at Background-944, so it reads the
// frame it reports on.
Shader "YAPS/Socket Readout"
{
    Properties
    {
        // Off unless the avatar's menu animates it on.
        _YAPS_ReadoutOn ("Readout shown", Range(0,1)) = 0
        // Animated per renderer by the owner layer, as on the writer.
        _YAPS_Owner ("Owner id", Float) = 0
        _YAPS_OverlaySize ("Overlay size", Float) = 0.05
        _YAPS_OverlayLift ("Overlay lift", Float) = 0.08
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Overlay" "IgnoreProjector" = "True" }

        Pass
        {
            // ForwardBase with the vertex-light variant: marker lights reach
            // a shader only as unity_4Light*, which Unity fills here alone.
            Tags { "LightMode" = "ForwardBase" }
            ZTest Always
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            #pragma multi_compile_fwdbase
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"
            // The whole library, for the atlas layout and the light decode
            // the plugs use.
            #include "yaps_deform.cginc"

            float _YAPS_ReadoutOn;
            float _YAPS_OverlaySize;
            float _YAPS_OverlayLift;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 read : TEXCOORD1;     // target, owner state, kind, number
                float4 levels : TEXCOORD2;   // per level: 0 nothing, 1 others, 2 own
                float2 light : TEXCOORD3;    // own marker light, plug depth (-1 none)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // Hidden: every corner at one point, and nothing is read.
                if (_YAPS_ReadoutOn < 0.5)
                {
                    return o;
                }

                float3 at = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;

                bool sameTarget = all(abs(_YAPS_Atlas_TexelSize.zw - _ScreenParams.xy) < 1.5);
                float target = !YapsAtlasFits() ? 0.0 : (!sameTarget ? 1.0 : 3.0);

                // The writer's own arithmetic, cell by cell, then every bucket
                // of both homes: the bucket is the writer's choice, and a
                // reader finds a socket by its tag and position.
                float4 levels = 0;
                float kind = -1;
                float number = 0;
                int found = -1;
                if (target > 2.5)
                {
                    YapsAtlasLayout layout = YapsAtlasLayoutNow();
                    int total = max(layout.cells, 1);
                    [loop] for (int lv = 0; lv < YAPS_ATLAS_LEVELS; lv++)
                    {
                        float size = YAPS_ATLAS_CELL * pow(4.0, lv);
                        int3 cell = int3(floor(at / size));
                        float3 f = frac(at / size);
                        float tagWant = YapsAtlasTag(cell);
                        int idx = YapsAtlasHash(cell) % total;
                        if (idx < 0) idx += total;
                        int step = YapsAtlasHash2(cell) % max(total - 1, 1);
                        if (step < 0) step += max(total - 1, 1);
                        int idxB = (idx + step + 1) % total;
                        float state = 0;
                        [loop] for (int home = 0; home < 2; home++)
                        {
                            int cellX, fromTop;
                            YapsAtlasCellPixels(home == 0 ? idx : idxB, lv, layout, cellX, fromTop);
                            int cellY = YapsAtlasRow(fromTop);
                            if (YAPS_ATLAS_LOAD(cellX, cellY).a * 255.0 > 0.5) state = max(state, 1);
                            [loop] for (int sub = 0; sub < 8; sub++)
                            {
                                int px = cellX + (1 + YAPS_ATLAS_OCTPX * sub) * YAPS_ATLAS_SLOTPX;
                                float4 got = YAPS_ATLAS_LOAD(px, cellY);
                                if (got.a < 0.5) continue;
                                if (abs((got.a - 0.5) * 2 - tagWant) > 0.001) continue;
                                // Within a 128th of the cell: an 8-bit target
                                // rounds the position a 510th, a neighbour
                                // sitting there is the same socket to a plug.
                                if (distance(got.rgb, f) > 1.0 / 128.0) continue;
                                state = 2;
                                bool oneWay;
                                int index;
                                YapsFacingDecode(YAPS_ATLAS_LOAD(px + YAPS_ATLAS_SLOTPX, cellY).a, kind, index, oneWay);
                                kind = kind < 0 ? -1 : (oneWay ? 2 : kind);
                                number = index;
                                found = YapsOwnerDecode(YAPS_ATLAS_LOAD(px + 3 * YAPS_ATLAS_SLOTPX, cellY));
                            }
                        }
                        // A mask, not levels[lv]: d3d11 cannot index a
                        // vector inside a loop it does not unroll.
                        levels += state * float4(lv == 0, lv == 1, lv == 2, lv == 3);
                    }
                }

                int mine = YapsOwnerOf(_YAPS_Owner);
                float ownerState = mine == 0 ? 0.0 : (found < 0 ? 3.0 : (found == mine ? 1.0 : 2.0));

                // The protocol lights, black and short, as a plug reads
                // them: a root says 7, or 1 to 4 in legacy, a plug's tracker
                // 8 or 9.
                float ownLight = 0;
                float plugDepth = -1;
                [unroll] for (uint i = 0; i < 4; i++)
                {
                    if (dot(unity_LightColor[i].rgb, unity_LightColor[i].rgb) > 0.0001) continue;
                    float range = YapsLightRange(i);
                    if (range >= 0.5) continue;
                    int digit = (int) round(fmod(range, 0.1) * 100);
                    float3 lp = YapsLightPosition(i);
                    bool root = digit == 7 || (digit >= 1 && digit <= 4);
                    if (root && distance(lp, at) < 0.02) ownLight = 1;
                    if (digit == 8 || digit == 9)
                    {
                        float len = unity_LightColor[i].a;
                        plugDepth = max(plugDepth, len > 1e-4 ? saturate((len - distance(at, lp)) / len) : 0);
                    }
                }

                o.read = float4(target, ownerState, kind, number);
                o.levels = levels;
                o.light = float2(ownLight, plugDepth);

                // A view-space billboard above the socket, so it faces every
                // eye, mirror and portrait camera.
                float3 centreView = mul(UNITY_MATRIX_V, float4(at, 1)).xyz;
                centreView.y += _YAPS_OverlayLift;
                float2 off = (v.uv - 0.5) * float2(_YAPS_OverlaySize * 6.0, _YAPS_OverlaySize);
                o.pos = mul(UNITY_MATRIX_P, float4(centreView + float3(off, 0), 1));
                o.uv = v.uv;
                return o;
            }

            static const fixed3 YAPS_DEAD = fixed3(0.10, 0.10, 0.10);
            static const fixed3 YAPS_NONE = fixed3(0.45, 0.45, 0.45);
            static const fixed3 YAPS_BAD  = fixed3(0.85, 0.15, 0.10);
            static const fixed3 YAPS_HALF = fixed3(0.95, 0.70, 0.10);
            static const fixed3 YAPS_GOOD = fixed3(0.15, 0.80, 0.25);
            static const fixed3 YAPS_CHAN = fixed3(0.20, 0.75, 0.85);
            static const fixed3 YAPS_BAR  = fixed3(0.20, 0.45, 0.95);

            fixed4 frag(v2f i) : SV_Target
            {
                float cellF = i.uv.x * 6.0;
                int cell = (int) floor(cellF);
                float withinX = frac(cellF);
                // A dark gutter, so six cells read as six.
                if (withinX < 0.06 || withinX > 0.94 || i.uv.y < 0.12 || i.uv.y > 0.88)
                {
                    return fixed4(0, 0, 0, 1);
                }
                float target = i.read.x;
                float ownerState = i.read.y;
                float kind = i.read.z;
                float number = i.read.w;
                fixed3 c = YAPS_NONE;
                if (cell == 0)
                {
                    c = target < 0.5 ? YAPS_DEAD : (target < 1.5 ? YAPS_BAD : YAPS_GOOD);
                }
                else if (cell == 1)
                {
                    int lv = min((int) floor(withinX * 4.0), 3);
                    float s = dot(i.levels, float4(lv == 0, lv == 1, lv == 2, lv == 3));
                    float edge = frac(withinX * 4.0);
                    c = (edge < 0.1 || edge > 0.9) ? fixed3(0, 0, 0)
                      : (s < 0.5 ? YAPS_DEAD : (s < 1.5 ? YAPS_BAD : YAPS_GOOD));
                }
                else if (cell == 2)
                {
                    c = ownerState < 0.5 ? YAPS_NONE
                      : (ownerState < 1.5 ? YAPS_GOOD : (ownerState < 2.5 ? YAPS_BAD : YAPS_HALF));
                }
                else if (cell == 3)
                {
                    if (withinX < 0.5)
                        c = kind < -0.5 ? YAPS_DEAD : (kind < 0.5 ? YAPS_CHAN : (kind < 1.5 ? YAPS_GOOD : YAPS_BAR));
                    else
                        c = (number > 0.5 && (withinX - 0.5) * 2.0 < number / 15.0) ? YAPS_BAR : YAPS_DEAD;
                }
                else if (cell == 4)
                {
                    c = i.light.x > 0.5 ? YAPS_GOOD : YAPS_NONE;
                }
                else
                {
                    float depth = i.light.y;
                    c = depth < -0.5 ? YAPS_NONE
                      : (depth <= 0.0 ? YAPS_HALF : (withinX < depth ? YAPS_GOOD : YAPS_DEAD));
                }
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
