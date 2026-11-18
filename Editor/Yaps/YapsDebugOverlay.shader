// A readout for a plug that will not behave, drawn on the plug itself.
//
// The patched plug's debug view can only answer in LENGTH (vertex stage
// only) and straightens the plug to say it. This shader paints, so the bend
// and its reason show together.
//
// IT RUNS ON THE PLUG'S RENDERER, as its own submesh: the preview and
// enabled flag live in this renderer's property block alone, and the frame
// comes from its skinned vertices. A separate object reported nobody while
// the plug was bent. The four vertices skin like the anchor vertex, so the
// recovery is the deform's own, on the same numbers.
//
// Fourteen cells in two rows, top the resolve, bottom the plug's body.
// Each is a colour, never a fraction:
//   top 1  who resolved it      grey nobody, cyan preview, amber light, green atlas
//   top 2  bending              red not engaged, amber engaged but switched off, green bending
//   top 3  gap to the socket    a bar, full width at a plug length away
//   top 4  what the atlas read  black nothing, red not ours, amber thrown out, green socket
//   top 5  atlas on this camera black too small, red wrong screen, amber empty, green live
//   top 6  atlas asked for      red off for this plug, green on
//   top 7  portal               dark no hole at the end, amber hole with no key,
//                               grey no exit published, green exit found,
//                               red exit found and the plug cannot come out of it
//   bot 1  frame recovery       grey taken from the object, red refused, green recovered
//   bot 2  anchor vertex        red outside the bake, green inside it
//   bot 3  bake read            black no bake, red the row read nothing, green data
//   bot 4  own body allowed     amber refusing its wearer's sockets, green allowing
//   bot 5  chain links          dark one socket, otherwise a bar of the chain
//   bot 6  refused by tags      dark nothing refused, amber refused, red refused and nearer
//   bot 7  always dark, kept so the rows stay the same width
//
// TWO MARKERS do what no cell can: one vertex only sees world space, which
// moves when the avatar turns. White is where the tip would be at bake pose,
// from the anchor's frame; magenta is where a real tip vertex arrives.
// Apart means the BONES moved (cloth, animation, constraint).
//
// Queue Overlay, after the atlas (clear -946, writers -945, grab -944), so
// it never lands in the grab. Do not move it forward.
Shader "YAPS/Debug Overlay"
{
    Properties
    {
        _YAPS_OverlaySize ("Overlay size", Float) = 0.12
        _YAPS_OverlayLift ("Overlay lift", Float) = 0.15
        _YAPS_AnchorVertex ("Anchor vertex", Float) = -1
        // Off unless the avatar's menu animates it on. Built on every plug,
        // so off has to cost nothing but the draw.
        _YAPS_ReadoutOn ("Readout shown", Range(0,1)) = 0

        // MIRRORS YapsShaderPatcher.PropertyBlock in full. The builder copies
        // by name and reports any it could not, so a forgotten one warns
        // instead of reading zero.
        _YAPS_Bake ("YAPS baked data", 2D) = "black" {}
        _YAPS_VertexCount ("YAPS vertex count", Float) = 0
        _YAPS_Enabled ("YAPS enabled", Range(0,1)) = 1
        _YAPS_Length ("YAPS plug length", Float) = 1
        _YAPS_Overrun ("YAPS allow overrun", Range(0,1)) = 1
        _YAPS_BakeScale ("YAPS bake scale", Float) = 1
        _YAPS_BakeGirth ("YAPS bake girth", Float) = 1
        [HideInInspector] _YAPS_PlugLane ("YAPS plug lane", Float) = 0
        _YAPS_BakeScale2 ("YAPS bake scale, second plug", Float) = 1
        _YAPS_BakeGirth2 ("YAPS bake girth, second plug", Float) = 1
        _YAPS_Enabled2 ("YAPS enabled, second plug", Range(0,1)) = 1
        _YAPS_SelfAllow2 ("YAPS own body allowed, second plug", Range(0,1)) = 0
        _YAPS_BakeSkinScale ("YAPS bake skin scale", Float) = 0
        _YAPS_PortalSpan ("YAPS portal band (-1 too coarse)", Float) = 0
        _YAPS_CopyStart ("YAPS second copy's first vertex", Float) = 0
        _YAPS_FrameFromVertex ("YAPS frame from vertex", Range(0,1)) = 0
        _YAPS_SocketPos ("YAPS socket position", Vector) = (0,0,0,0)
        _YAPS_SocketForward ("YAPS socket forward", Vector) = (0,0,0,0)
        _YAPS_SocketUp ("YAPS socket up", Vector) = (0,0,0,0)
        _YAPS_SocketFlags ("YAPS socket flags", Vector) = (0,0,0,0)
        _YAPS_SocketFront ("YAPS socket front", Vector) = (0,0,0,0)
        _YAPS_ChannelSpace ("YAPS channel space", Range(0,1)) = 0
        _YAPS_ChannelOrigin ("YAPS channel origin", Vector) = (0,0,0,0)
        _YAPS_ChannelForward ("YAPS channel forward", Vector) = (0,0,0,0)
        _YAPS_ChannelUp ("YAPS channel up", Vector) = (0,0,0,0)
        _YAPS_ChannelExtents ("YAPS channel extents", Vector) = (1,1,1,0)
        _YAPS_SelfTag ("YAPS self tag", Float) = -1
        _YAPS_SelfAllow ("YAPS own body allowed", Range(0,1)) = 0
        _YAPS_Owner ("YAPS owner id", Float) = 0
        _YAPS_SelfSockets ("YAPS own sockets it may enter", Float) = 0
        _YAPS_SelfChosen ("YAPS own sockets past its tags", Float) = 0
        _YAPS_UseAtlas ("YAPS read the screen atlas", Range(0,1)) = 0
        _YAPS_Debug ("YAPS view", Float) = 0
        _YAPS_TaperStart ("YAPS hole taper start", Range(0,1)) = 0.10
        _YAPS_TaperEnd ("YAPS hole taper end", Range(0,1)) = 0.30
        _YAPS_IdleLength ("YAPS idle length", Range(0.1,1)) = 1
        _YAPS_IdleWidth ("YAPS idle width", Range(0.1,1)) = 1
        _YAPS_Squeeze ("YAPS squeeze", Range(0,1)) = 0
        _YAPS_SqueezeDistance ("YAPS squeeze reach", Range(0.01,1)) = 0.15
        _YAPS_Bulge ("YAPS bulge", Range(0,1)) = 0
        _YAPS_BulgeDistance ("YAPS bulge reach", Range(0.01,1)) = 0.2
        _YAPS_BulgeFalloff ("YAPS bulge falloff", Range(0,0.5)) = 0
        _YAPS_PumpStrength ("YAPS pumping", Range(0,0.5)) = 0
        _YAPS_PumpSpeed ("YAPS pumping speed", Range(0,20)) = 6
        _YAPS_PumpWidth ("YAPS pumping width", Range(0.05,1)) = 1
        _YAPS_WriggleStrength ("YAPS wriggle", Range(0,0.5)) = 0
        _YAPS_WriggleSpeed ("YAPS wriggle speed", Range(0,20)) = 2
        _YAPS_Curvature ("YAPS curvature", Range(-1.5,1.5)) = 0
        _YAPS_ReCurvature ("YAPS recurvature", Range(-1.5,1.5)) = 0
        _YAPS_EntranceStiffness ("YAPS entrance stiffness", Range(0,1)) = 0
        _YAPS_BezierSmoothness ("YAPS bezier smoothness", Range(0.2,3)) = 1
        _YAPS_BezierStart ("YAPS straight before bend", Range(0,0.8)) = 0
        _YAPS_SmoothStart ("YAPS ease into bend", Range(0,0.5)) = 0
        _YAPS_MinimumSocketDistance ("YAPS minimum socket distance", Range(0,1)) = 0
        _YAPS_TagInclude ("YAPS only sockets tagged", Vector) = (0,0,0,0)
        _YAPS_TagInclude2 ("YAPS only sockets tagged, second plug", Vector) = (0,0,0,0)
        _YAPS_TagExclude ("YAPS never sockets tagged", Vector) = (0,0,0,0)
        _YAPS_ShapeCount ("YAPS shape count", Float) = 0
        _YAPS_ShapeWeights ("YAPS shape weights 0-3", Vector) = (0,0,0,0)
        _YAPS_ShapeWeights2 ("YAPS shape weights 4-7", Vector) = (0,0,0,0)
        _YAPS_ShapeWeights3 ("YAPS shape weights 8-11", Vector) = (0,0,0,0)
        _YAPS_ShapeWeights4 ("YAPS shape weights 12-15", Vector) = (0,0,0,0)
        _YAPS_SocketPower ("YAPS socket power", Range(0,1)) = 0
        _YAPS_SocketDepth ("YAPS socket depth (-1 none)", Range(-1,1)) = -1
        _YAPS_SocketOrigin ("YAPS socket origin, mesh local", Vector) = (0,0,0,0)
        _YAPS_SocketNoSelfExclude ("YAPS socket takes the wearer's own plug", Range(0,1)) = 0
        _YAPS_SocketShapeStart ("YAPS socket shape starts 0-3", Vector) = (0, 0.25, 0.5, 0.75)
        _YAPS_SocketShapeStart2 ("YAPS socket shape starts 4-7", Vector) = (0, 0, 0, 0)
        _YAPS_SocketShapeStart3 ("YAPS socket shape starts 8-11", Vector) = (0, 0, 0, 0)
        _YAPS_SocketShapeStart4 ("YAPS socket shape starts 12-15", Vector) = (0, 0, 0, 0)
        _YAPS_SocketShapeFade ("YAPS socket shape fades 0-3", Vector) = (0.3, 0.3, 0.3, 0.3)
        _YAPS_SocketShapeFade2 ("YAPS socket shape fades 4-7", Vector) = (0.3, 0.3, 0.3, 0.3)
        _YAPS_SocketShapeFade3 ("YAPS socket shape fades 8-11", Vector) = (0.3, 0.3, 0.3, 0.3)
        _YAPS_SocketShapeFade4 ("YAPS socket shape fades 12-15", Vector) = (0.3, 0.3, 0.3, 0.3)
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Overlay" "IgnoreProjector" = "True" }

        Pass
        {
            // ForwardBase: unity_4Light* is filled here alone. Untagged, it
            // read the last forward draw's lights.
            Tags { "LightMode" = "ForwardBase" }
            // Always visible, never written to depth.
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
            // The whole library: the resolver's helpers are declared in the
            // deform unit, after it includes the resolver.
            #include "yaps_deform.cginc"

            float _YAPS_OverlaySize;
            float _YAPS_OverlayLift;
            float _YAPS_AnchorVertex;
            float _YAPS_ReadoutOn;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                // uv0.xy the corner, uv0.z WHICH quad: 0 strip, 1 rest
                // marker, 2 live marker. Nothing else tells them apart.
                float4 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                // Resolved ONCE per vertex: 27 atlas cells per pixel would
                // be a frame rate bug.
                float4 read : TEXCOORD1;   // tier, engaged, gap fraction, headers
                float4 read2 : TEXCOORD2;  // hits, atlas-on-this-camera step, frame recovery, refused distance
                float4 read3 : TEXCOORD3;  // chain links, which quad, bake read, portal
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // Hidden: every corner at one point, so nothing is shaded.
                if (_YAPS_ReadoutOn < 0.5)
                {
                    return o;
                }

                float quad = v.uv.z;

                // The same YapsRecoverRoot call YapsDeform makes, so this is
                // the frame the plug really bends from.
                YapsVertex baked = YapsReadBaked((uint) max(_YAPS_AnchorVertex, 0));
                // The raw base normal: YapsReadBaked normalises with a fallback, so its
                // normal is never zero even when nothing was read.
                float bakeRead = length(YapsReadFloat3(1 + (uint) max(_YAPS_AnchorVertex, 0) * 10 + 3)) > 0.001 ? 1.0 : 0.0;
                YapsRoot root = YapsRecoverRoot(baked, v.vertex.xyz, v.normal, v.tangent.xyz);
                // 0 the object's axes (plain mesh, by design), 1 refused (a
                // fault: the deform leaves the vertex alone), 2 recovered.
                float recovered = root.recovered;
                float3 rootWorld = root.position;
                float3 rootForward = root.forward;
                float3 rootUp = root.up;
                float worldLength = root.worldLength;

                // ONLY THE STRIP RESOLVES: the 27-cell scan is wasted on markers.
                YapsSocket socket = (YapsSocket) 0;
                float gap = 1.0;
                float target = 0.0;
                float refused = 0.0;
                float portal = 0.0;
                if (quad < 0.5)
                {
                    socket = YapsResolveSocket(rootWorld, rootForward, rootUp, worldLength);
                    gap = socket.tier < 0.5
                        ? 1.0
                        : saturate(length(socket.position - rootWorld) / max(worldLength, 1e-4));

                    // The same plug answers differently in the view, a mirror
                    // and the self portrait.
                    float2 grabPx = _YAPS_Atlas_TexelSize.zw;
                    bool sameTarget = all(abs(grabPx - _ScreenParams.xy) < 1.5);
                    target = !YapsAtlasFits() ? 0.0
                           : (!sameTarget ? 1.0
                           : (socket.atlasHeaders < 0.5 ? 2.0 : 3.0));

                    // Refused by tags: 0 none, 1 refused, 2 refused and nearer
                    // than whatever answered. Zero distance means the chain
                    // never ran (atlas off or too small); ungated, this cell
                    // was red on every plug that resolved nothing.
                    float refusedD = socket.chain.refusedD;
                    float taken = socket.tier < 0.5 ? 1e9 : length(socket.position - rootWorld);
                    refused = (refusedD > 1e8 || refusedD <= 0.0)
                        ? 0.0 : (refusedD < taken ? 2.0 : 1.0);

                    // The deform's own question, asked of the hole the path
                    // ends in. Literal indices: a dynamic one spills the array.
                    // 0 no hole, 1 a hole with no owner or number, 2 keyed
                    // and no exit in the margin, 3 an exit found.
                    int links = socket.chain.count;
                    float lastKind = links > 3 ? socket.chain.kind[3]
                                   : (links > 2 ? socket.chain.kind[2]
                                   : (links > 1 ? socket.chain.kind[1]
                                   : (links > 0 ? socket.chain.kind[0] : 0.0)));
                    if (lastKind > 0.5)
                    {
                        portal = 1.0;
                        float3 exitAt = rootWorld;
                        float3 exitFwd = rootForward;
                        if (socket.chain.portal != 0)
                            portal = YapsPortalExit(socket.chain.portal, exitAt, exitFwd) ? 3.0 : 2.0;
                    }
                }

                o.read = float4(socket.tier, socket.engaged, gap, socket.atlasHeaders);
                o.read2 = float4(socket.atlasHits, target, recovered, refused);
                o.read3 = float4(socket.chain.count, quad, bakeRead, portal);

                // VIEW-SPACE BILLBOARD: faces every eye, mirror and portrait
                // camera, per-eye under single-pass instanced, which a world
                // facing would not. Markers billboard with no lift: they must
                // sit where they mean.
                float3 centre = quad < 0.5
                    ? rootWorld
                    : (quad < 1.5 ? rootWorld + rootForward * worldLength
                                  : mul(unity_ObjectToWorld, v.vertex).xyz);
                float3 centreView = mul(UNITY_MATRIX_V, float4(centre, 1)).xyz;
                if (quad < 0.5)
                {
                    centreView.y += _YAPS_OverlayLift;
                }
                float2 span = quad < 0.5
                    ? float2(_YAPS_OverlaySize * 7.0, _YAPS_OverlaySize * 2.0)
                    : float2(_YAPS_OverlaySize * 0.45, _YAPS_OverlaySize * 0.45);
                float2 off = (v.uv.xy - 0.5) * span;
                o.pos = mul(UNITY_MATRIX_P, float4(centreView + float3(off, 0), 1));
                o.uv = v.uv.xy;
                return o;
            }

            // Steps, never a ratio: one socket fills one of 27 cells, so a
            // ratio put a working transport a few percent from a dead one.
            static const fixed3 YAPS_DEAD  = fixed3(0.10, 0.10, 0.10);
            static const fixed3 YAPS_NONE  = fixed3(0.45, 0.45, 0.45);
            static const fixed3 YAPS_BAD   = fixed3(0.85, 0.15, 0.10);
            static const fixed3 YAPS_HALF  = fixed3(0.95, 0.70, 0.10);
            static const fixed3 YAPS_GOOD  = fixed3(0.15, 0.80, 0.25);

            // Tier 1 (the editor preview) is a WORKING state. Red is for
            // faults only.
            static const fixed3 YAPS_CHAN  = fixed3(0.20, 0.75, 0.85);

            // For the cells whose value is genuinely continuous.
            static const fixed3 YAPS_BAR   = fixed3(0.20, 0.45, 0.95);

            fixed4 frag(v2f i) : SV_Target
            {
                float quad = i.read3.y;
                if (quad > 0.5)
                {
                    // THE TWO MARKERS, see the header.
                    fixed3 m = quad < 1.5 ? fixed3(0.95, 0.95, 0.95) : fixed3(0.95, 0.15, 0.85);
                    float edge = max(abs(i.uv.x - 0.5), abs(i.uv.y - 0.5));
                    return fixed4(edge > 0.38 ? fixed3(0, 0, 0) : m, 1);
                }

                float cellF = i.uv.x * 7.0;
                int column = (int) floor(cellF);
                float withinX = frac(cellF);
                float rowF = i.uv.y * 2.0;
                int row = (int) floor(rowF);
                float withinY = frac(rowF);

                // A dark gutter, or neighbouring greens merge.
                if (withinX < 0.06 || withinX > 0.94 || withinY < 0.10 || withinY > 0.90)
                {
                    return fixed4(0, 0, 0, 1);
                }

                // Cell order is fixed so old screenshots stay readable.
                int cell = column == 6 ? (row == 1 ? 12 : 13) : (row == 1 ? column : column + 6);

                float tier = i.read.x;
                float engaged = i.read.y;
                float gap = i.read.z;
                float headers = i.read.w;
                float hits = i.read2.x;
                float target = i.read2.y;
                float recovered = i.read2.z;
                float refused = i.read2.w;
                float links = i.read3.x;
                float bakeRead = i.read3.z;
                float portal = i.read3.w;

                fixed3 c = YAPS_NONE;

                if (cell == 0)
                {
                    // Grey is nothing in reach, not a fault.
                    c = tier < 0.5 ? YAPS_NONE
                      : (tier < 1.5 ? YAPS_CHAN
                      : (tier < 2.5 ? YAPS_HALF : YAPS_GOOD));
                }
                else if (cell == 1)
                {
                    // Bending also needs the enabled flag. Amber: found a
                    // socket while its toggle holds it off, which otherwise
                    // looks like nobody resolving it.
                    c = engaged < 0.5 ? YAPS_BAD
                      : (YAPS_ENABLED < 0.5 ? YAPS_HALF : YAPS_GOOD);
                }
                else if (cell == 2)
                {
                    // A bar: a jump means a different socket took over.
                    c = withinX < saturate(gap) ? YAPS_BAR : YAPS_DEAD;
                }
                else if (cell == 3)
                {
                    c = headers < 0.5 ? YAPS_DEAD
                      : (hits < 0.5 ? YAPS_BAD
                      : (tier < 2.5 ? YAPS_HALF : YAPS_GOOD));
                }
                else if (cell == 4)
                {
                    c = target < 0.5 ? YAPS_DEAD
                      : (target < 1.5 ? YAPS_BAD
                      : (target < 2.5 ? YAPS_HALF : YAPS_GOOD));
                }
                else if (cell == 5)
                {
                    c = _YAPS_UseAtlas > 0.5 ? YAPS_GOOD : YAPS_BAD;
                }
                else if (cell == 6)
                {
                    // Red is silent everywhere else: the deform keeps running
                    // around an axis that is not the plug's.
                    c = recovered < 0.5 ? YAPS_NONE
                      : (recovered < 1.5 ? YAPS_BAD : YAPS_GOOD);
                }
                else if (cell == 7)
                {
                    // Red: a re-bake moved the anchor or the bake is another
                    // mesh's, so every other cell is wrong too.
                    c = (_YAPS_AnchorVertex < 0 || _YAPS_AnchorVertex >= _YAPS_VertexCount)
                        ? YAPS_BAD : YAPS_GOOD;
                }
                else if (cell == 8)
                {
                    // Red: the bake texture did not ship or import as written.
                    c = _YAPS_VertexCount < 0.5 ? YAPS_DEAD
                      : (bakeRead < 0.5 ? YAPS_BAD : YAPS_GOOD);
                }
                else if (cell == 9)
                {
                    // The quietest reason a plug finds nothing.
                    c = YAPS_SELF_ALLOW > 0.5 ? YAPS_GOOD : YAPS_HALF;
                }
                else if (cell == 10)
                {
                    // Only the atlas builds a chain.
                    c = links < 0.5 ? YAPS_DEAD
                      : (withinX < saturate(links / 4.0) ? YAPS_GOOD : YAPS_DEAD);
                }
                else if (cell == 12)
                {
                    // Red: the plug cannot come out of the exit (too coarse,
                    // no second copy, or baked before portals).
                    c = portal < 0.5 ? YAPS_DEAD
                      : (portal < 1.5 ? YAPS_HALF
                      : (portal < 2.5 ? YAPS_NONE
                      : (_YAPS_PortalSpan > 0 && _YAPS_CopyStart > 0 ? YAPS_GOOD : YAPS_BAD)));
                }
                else if (cell == 13)
                {
                    c = YAPS_DEAD;
                }
                else
                {
                    // Red: refused AND nearer than whatever answered, which
                    // looks like no socket at all.
                    c = refused < 0.5 ? YAPS_DEAD
                      : (refused < 1.5 ? YAPS_HALF : YAPS_BAD);
                }

                return fixed4(c, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
