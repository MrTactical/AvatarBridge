// A readout for a plug that will not behave, drawn on the plug itself.
//
// The plug's own debug view answers in LENGTH, because the patcher edits
// a host shader's vertex stage and there is no fragment of ours to paint
// in. That view can only say one thing at a time, and it straightens the
// plug to say it, so the bend and the reason for the bend can never be
// seen together. This is our own shader end to end, so it paints, and it
// leaves the plug alone.
//
// IT RUNS ON THE PLUG'S RENDERER, as its own submesh and material slot,
// and that is not a packaging detail. A separate renderer cannot see the
// contact channel or the enabled flag, which arrive as animated material
// properties in this renderer's block alone, and it cannot recover the
// frame the deform recovers, which comes from this renderer's skinned
// vertices. The first version was a separate object and reported nobody
// while the plug was bent round a socket. The four vertices here are
// skinned exactly like one plug vertex, the anchor, so what arrives is
// where the anchor is, and the bake says where it was: the same recovery
// the deform runs, on the same numbers.
//
// Six cells, left to right. Each is a colour, never a fraction:
//   1  who resolved it      grey nobody, cyan channel, amber light, green atlas
//   2  bending              red not engaged, amber engaged but switched off, green bending
//   3  gap to the socket    a bar, full width at a plug length away
//   4  what the atlas read  black nothing, red not ours, amber thrown out, green socket
//   5  atlas on this camera black too small, red wrong screen, amber empty, green live
//   6  atlas asked for      red off for this plug, green on
//
// Queue Overlay, long after the atlas finishes. The whole atlas
// transaction runs in Background: clear at -946, writers at -945, grab at
// -944. Drawing here cannot land in the grab and cannot corrupt what it
// is reporting on. Do not move this queue forward.
Shader "YAPS/Debug Overlay"
{
    Properties
    {
        _YAPS_OverlaySize ("Overlay size", Float) = 0.12
        _YAPS_OverlayLift ("Overlay lift", Float) = 0.15
        _YAPS_AnchorVertex ("Anchor vertex", Float) = -1

        // MIRRORS THE PATCHED PLUG'S BLOCK, the whole of it. The builder
        // copies the values across by name and reports any it could not,
        // so a property added to YapsShaderPatcher.PropertyBlock and
        // forgotten here is a warning in the report rather than a cell that
        // quietly reads zero.
        _YAPS_Bake ("YAPS baked data", 2D) = "black" {}
        _YAPS_VertexCount ("YAPS vertex count", Float) = 0
        _YAPS_Enabled ("YAPS enabled", Range(0,1)) = 1
        _YAPS_Length ("YAPS plug length", Float) = 1
        _YAPS_Overrun ("YAPS allow overrun", Range(0,1)) = 1
        _YAPS_BakeScale ("YAPS bake scale", Float) = 1
        _YAPS_BakeGirth ("YAPS bake girth", Float) = 1
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
            // Always visible: a readout hidden by the body it sits on is
            // no readout. Never written to depth, so nothing downstream
            // sorts against it.
            ZTest Always
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"
            // The whole library, not yaps_resolve alone: the resolver's own
            // helpers are declared in the deform unit after it includes the
            // resolver, so taking the resolver by itself leaves them missing.
            #include "yaps_deform.cginc"

            float _YAPS_OverlaySize;
            float _YAPS_OverlayLift;
            float _YAPS_AnchorVertex;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                // The whole answer, resolved ONCE per vertex. All four
                // vertices are the anchor, so all four resolve the same
                // thing; 27 atlas cells per pixel is not a diagnostic, it
                // is a frame rate bug.
                float4 read : TEXCOORD1;   // tier, engaged, gap fraction, headers
                float2 read2 : TEXCOORD2;  // hits, atlas-on-this-camera step
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // THE DEFORM'S OWN FRAME RECOVERY, step for step. This
                // vertex is skinned like the anchor, so it arrives where
                // the anchor arrives; the bake holds where the anchor was.
                // The steps and their order are YapsDeform's, and a change
                // there that is not made here is a readout that lies.
                YapsVertex baked = YapsReadBaked((uint) max(_YAPS_AnchorVertex, 0));
                baked.position *= float3(max(_YAPS_BakeGirth, 0.0001), max(_YAPS_BakeGirth, 0.0001),
                                         max(_YAPS_BakeScale, 0.0001));

                float3 rootLocal = float3(0, 0, 0);
                float3 forwardLocal = float3(0, 0, 1);
                float3 upLocal = float3(0, 1, 0);
                if (_YAPS_FrameFromVertex > 0.5)
                {
                    YapsBasis basis = YapsBuildBasis(baked.normal, baked.tangent,
                                                     v.normal, v.tangent.xyz);
                    if (basis.valid)
                    {
                        rootLocal = v.vertex.xyz - YapsRotate(basis, baked.position);
                        forwardLocal = YapsRotate(basis, float3(0, 0, 1));
                        upLocal = YapsRotate(basis, float3(0, 1, 0));
                    }
                }

                float3 rootWorld = mul(unity_ObjectToWorld, float4(rootLocal, 1)).xyz;
                float3 rootForward = YapsSafeNormalize(
                    mul((float3x3) unity_ObjectToWorld, forwardLocal), float3(0, 0, 1));
                float3 rootUp = YapsPerpendicular(rootForward,
                    mul((float3x3) unity_ObjectToWorld, upLocal));

                float yapsScale = length(mul((float3x3) unity_ObjectToWorld, float3(0, 0, 1)));
                float worldLength = _YAPS_Length * _YAPS_BakeScale * yapsScale;

                YapsSocket socket = YapsResolveSocket(rootWorld, rootForward, rootUp, worldLength);

                float gap = socket.tier < 0.5
                    ? 1.0
                    : saturate(length(socket.position - rootWorld) / max(worldLength, 1e-4));

                // WHICH TARGET this camera draws into. The same plug
                // answers differently in the view, a mirror and the self
                // portrait, and nothing else in here can say so.
                float2 grabPx = _YAPS_Atlas_TexelSize.zw;
                bool sameTarget = all(abs(grabPx - _ScreenParams.xy) < 1.5);
                float target = !YapsAtlasFits() ? 0.0
                             : (!sameTarget ? 1.0
                             : (socket.atlasHeaders < 0.5 ? 2.0 : 3.0));

                o.read = float4(socket.tier, socket.engaged, gap, socket.atlasHeaders);
                o.read2 = float2(socket.atlasHits, target);

                // A VIEW-SPACE BILLBOARD off the recovered root, so it
                // faces every eye, every mirror and the portrait camera
                // without a script. The view matrix is per-eye, so
                // single-pass instanced gets this right for free; a
                // world-space facing would not.
                float3 originView = mul(UNITY_MATRIX_V, float4(rootWorld, 1)).xyz;
                float2 off = (v.uv - 0.5) * float2(_YAPS_OverlaySize * 6.0, _YAPS_OverlaySize);
                originView.y += _YAPS_OverlayLift;
                o.pos = mul(UNITY_MATRIX_P, float4(originView + float3(off, 0), 1));
                o.uv = v.uv;
                return o;
            }

            // Four steps, never a ratio. One socket occupies one cell of
            // the twenty-seven read, so a ratio put a working transport a
            // few percent from a dead one.
            static const fixed3 YAPS_DEAD  = fixed3(0.10, 0.10, 0.10);
            static const fixed3 YAPS_NONE  = fixed3(0.45, 0.45, 0.45);
            static const fixed3 YAPS_BAD   = fixed3(0.85, 0.15, 0.10);
            static const fixed3 YAPS_HALF  = fixed3(0.95, 0.70, 0.10);
            static const fixed3 YAPS_GOOD  = fixed3(0.15, 0.80, 0.25);

            // The contact channel answering is a WORKING state, and it was
            // painted in this readout's own fault colour. Every healthy
            // channel read as a failure, which is worse than no readout at
            // all. Red belongs to faults only.
            static const fixed3 YAPS_CHAN  = fixed3(0.20, 0.75, 0.85);

            fixed4 frag(v2f i) : SV_Target
            {
                float cellF = i.uv.x * 6.0;
                int cell = (int) floor(cellF);
                float withinX = frac(cellF);

                // A dark gutter, so six cells read as six and not as one
                // gradient. Without it two neighbouring greens merge.
                if (withinX < 0.06 || withinX > 0.94 || i.uv.y < 0.08 || i.uv.y > 0.92)
                {
                    return fixed4(0, 0, 0, 1);
                }

                float tier = i.read.x;
                float engaged = i.read.y;
                float gap = i.read.z;
                float headers = i.read.w;
                float hits = i.read2.x;
                float target = i.read2.y;

                fixed3 c = YAPS_NONE;

                if (cell == 0)
                {
                    // Three working transports, three tellable colours.
                    // Grey is the only nothing here: a plug with no socket
                    // in reach is not broken.
                    c = tier < 0.5 ? YAPS_NONE
                      : (tier < 1.5 ? YAPS_CHAN
                      : (tier < 2.5 ? YAPS_HALF : YAPS_GOOD));
                }
                else if (cell == 1)
                {
                    // Engaged is the resolver's verdict; bending needs the
                    // enabled flag as well, and that flag only exists in
                    // this renderer's block. Amber is a plug that found its
                    // socket while its toggle holds it off, which from the
                    // outside looks exactly like nobody resolving it.
                    c = engaged < 0.5 ? YAPS_BAD
                      : (_YAPS_Enabled < 0.5 ? YAPS_HALF : YAPS_GOOD);
                }
                else if (cell == 2)
                {
                    // A BAR, not a colour. Distance is the one value here
                    // that is genuinely continuous, and a jump in it means
                    // a different socket was handed over on that frame.
                    c = withinX < saturate(gap) ? fixed3(0.20, 0.45, 0.95) : YAPS_DEAD;
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
                else
                {
                    c = _YAPS_UseAtlas > 0.5 ? YAPS_GOOD : YAPS_BAD;
                }

                return fixed4(c, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
