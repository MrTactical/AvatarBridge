// Spike S1. The redesign leans on CVR's per-player position globals for
// all per-frame tracking, and nobody had checked they resolve in game.
//
// Draws a marker AT each player's hip, chest and head, taken straight from
// the globals, in world space — the object's own transform is deliberately
// ignored. So the test is simply: do the markers sit on people's bodies,
// and do they stay there when everyone moves?
//
//   red   hip     green  chest    blue  head
//
// Drawn as an OVERLAY: always on top, never depth-tested, so a marker
// buried inside a body is still readable. Wireframe rather than solid so
// it reads as a gizmo and does not hide the person it is measuring.
// Markers hold a minimum apparent size, so a player across the room stays
// visible, and the local player's set is drawn small so it does not fill
// your own view from the inside.
//
// The mirror question matters as much as the accuracy: these come from
// Shader.SetGlobalVectorArray, so they should look IDENTICAL in a mirror
// and in the direct view. Lights did not. If the markers agree, the
// backbone of the redesign is sound.
Shader "AvatarBridge/SPS Globals Probe"
{
    Properties
    {
        _Size ("Marker size (m)", Float) = 0.07
        _MinApparent ("Minimum apparent size", Float) = 0.025
        _LocalScale ("Local player marker scale", Float) = 0.35
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Overlay" "IgnoreProjector" = "True" }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Cull Off
            ZWrite Off
            ZTest Always

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            // Declared at the client's capacity. Unity locks an array's
            // size at first bind, so matching it avoids a silent mismatch.
            float4 _CVR_PlayerHipPositions[255];
            float4 _CVR_PlayerChestPositions[255];
            float4 _CVR_PlayerHeadPositions[255];
            float4 CVRGlobalParams1;

            float _Size;
            float _MinApparent;
            float _LocalScale;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;   // x = player index, y = channel
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 tint : TEXCOORD0;
                float3 local : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                int player = (int) round(v.uv.x);
                int channel = (int) round(v.uv.y);

                float4 slot;
                float3 hue;
                if (channel == 0)      { slot = _CVR_PlayerHipPositions[player];   hue = float3(1.0, 0.25, 0.25); }
                else if (channel == 1) { slot = _CVR_PlayerChestPositions[player]; hue = float3(0.25, 1.0, 0.35); }
                else                   { slot = _CVR_PlayerHeadPositions[player];  hue = float3(0.35, 0.6, 1.0); }

                // Beyond the reported player count, or an unwritten slot,
                // collapse the marker so it draws nothing at all.
                int count = (int) round(CVRGlobalParams1.y);
                bool live = (player < count) && any(abs(slot.xyz) > 0.0001);

                // Hold a minimum apparent size so a marker across the room
                // does not shrink to a pixel, and shrink your own set so it
                // does not fill the view from the inside.
                float toCamera = distance(_WorldSpaceCameraPos, slot.xyz);
                float size = max(_Size, toCamera * _MinApparent);
                if (player == 0) size *= _LocalScale;

                float3 world = slot.xyz + v.vertex.xyz * (live ? size : 0.0);
                o.pos = UnityWorldToClipPos(world);
                o.local = v.vertex.xyz;
                o.tint = hue;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                // Wireframe: on a cube face two of the three axes are at
                // their extreme along an edge, so counting them draws the
                // outline without needing a texture.
                float3 axis = abs(i.local) * 2.0;
                int atExtreme = (axis.x > 0.86 ? 1 : 0)
                              + (axis.y > 0.86 ? 1 : 0)
                              + (axis.z > 0.86 ? 1 : 0);

                if (atExtreme >= 2)
                {
                    return fixed4(i.tint, 1);
                }
                // Faces kept dim so the marker is locatable at distance but
                // never hides the body part it is measuring.
                return fixed4(i.tint * 0.18, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
