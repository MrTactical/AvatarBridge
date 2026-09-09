// A readout for a plug that will not behave, drawn beside it in game.
//
// The plug's own debug view answers in LENGTH, because the patcher edits
// a host shader's vertex stage and there is no fragment of ours to paint
// in. That view can only say one thing at a time, and it straightens the
// plug to say it, so the bend and the reason for the bend can never be
// seen together. This is our own shader end to end, so it paints, and it
// leaves the plug alone.
//
// Six cells, left to right. Each is a colour, never a fraction:
//   1  who resolved it      grey nobody, red channel, amber light, green atlas
//   2  engaged              red no, green yes
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

        // MIRRORS THE PATCHED PLUG'S BLOCK. The builder copies the values
        // across and reports any name it could not, so a property added to
        // YapsShaderPatcher.PropertyBlock and forgotten here is a warning
        // in the report rather than a cell that quietly reads zero.
        _YAPS_Length ("YAPS length", Float) = 0
        _YAPS_BakeScale ("YAPS bake scale", Float) = 1
        _YAPS_BakeGirth ("YAPS bake girth", Float) = 1
        _YAPS_Enabled ("YAPS enabled", Float) = 1
        _YAPS_SelfTag ("YAPS self tag", Float) = -1
        _YAPS_SelfAllow ("YAPS self allow", Float) = 0
        _YAPS_UseAtlas ("YAPS use atlas", Float) = 0
        _YAPS_TagInclude ("YAPS tag include", Vector) = (0,0,0,0)
        _YAPS_TagExclude ("YAPS tag exclude", Vector) = (0,0,0,0)
        _YAPS_SocketPos ("YAPS socket pos", Vector) = (0,0,0,0)
        _YAPS_SocketForward ("YAPS socket forward", Vector) = (0,0,0,0)
        _YAPS_SocketUp ("YAPS socket up", Vector) = (0,0,0,0)
        _YAPS_SocketFlags ("YAPS socket flags", Vector) = (0,0,0,0)
        _YAPS_SocketFront ("YAPS socket front", Vector) = (0,0,0,0)
        _YAPS_ChannelOrigin ("YAPS channel origin", Vector) = (0,0,0,0)
        _YAPS_ChannelForward ("YAPS channel forward", Vector) = (0,0,0,0)
        _YAPS_ChannelUp ("YAPS channel up", Vector) = (0,0,0,0)
        _YAPS_ChannelSpace ("YAPS channel space", Float) = 0
        _YAPS_ChannelExtents ("YAPS channel extents", Vector) = (0,0,0,0)
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
                // The whole answer, resolved ONCE. Every vertex would
                // resolve the same thing: the origin is the object's, not
                // the vertex's, and 27 atlas cells per pixel is not a
                // diagnostic, it is a frame rate bug.
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

                // The plug's frame, from this object's transform. The
                // builder parents this quad to the bone the bake measured
                // from and orients it to the bake's own rotation, so the
                // matrix here IS the frame the deform recovers per vertex.
                float3 rootWorld = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float3 rootForward = YapsSafeNormalize(
                    mul((float3x3) unity_ObjectToWorld, float3(0, 0, 1)), float3(0, 0, 1));
                float3 rootUp = YapsPerpendicular(rootForward,
                    mul((float3x3) unity_ObjectToWorld, float3(0, 1, 0)));

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

                // A VIEW-SPACE BILLBOARD, so it faces every eye, every
                // mirror and the portrait camera without a script. The
                // view matrix is per-eye, so single-pass instanced gets
                // this right for free; a world-space facing would not.
                float3 originView = UnityObjectToViewPos(float3(0, 0, 0));
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
                    c = tier < 0.5 ? YAPS_NONE
                      : (tier < 1.5 ? YAPS_BAD
                      : (tier < 2.5 ? YAPS_HALF : YAPS_GOOD));
                }
                else if (cell == 1)
                {
                    c = engaged < 0.5 ? YAPS_BAD : YAPS_GOOD;
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
