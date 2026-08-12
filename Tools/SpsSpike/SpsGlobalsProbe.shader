// Spike S1. The redesign leans on CVR's per-player position globals for
// all per-frame tracking, and nobody has ever checked they resolve in
// game, let alone how accurate they are.
//
// This draws a small marker AT each player's hip, chest and head, taken
// straight from the globals, in world space — the object's own transform
// is deliberately ignored. So the test is simply: do the markers sit on
// people's bodies, and do they stay there when everyone moves?
//
//   red   hip     green  chest    blue  head
//   brighter marker = player index 0, which should be you
//
// The mirror question matters as much as the accuracy: these are set with
// Shader.SetGlobalVectorArray, so they should look IDENTICAL in a mirror
// and in the direct view. Lights did not. If the markers agree, the
// backbone of the design is sound.
Shader "AvatarBridge/SPS Globals Probe"
{
    Properties
    {
        _Size ("Marker size", Float) = 0.06
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Cull Off
            ZWrite On

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
                if (channel == 0)      { slot = _CVR_PlayerHipPositions[player];   hue = float3(1.0, 0.2, 0.2); }
                else if (channel == 1) { slot = _CVR_PlayerChestPositions[player]; hue = float3(0.2, 1.0, 0.3); }
                else                   { slot = _CVR_PlayerHeadPositions[player];  hue = float3(0.3, 0.5, 1.0); }

                // Beyond the reported player count, or an unwritten slot,
                // collapse the marker so it draws nothing at all.
                int count = (int) round(CVRGlobalParams1.y);
                bool live = (player < count) && any(abs(slot.xyz) > 0.0001);

                float3 world = slot.xyz + v.vertex.xyz * (live ? _Size : 0.0);
                o.pos = UnityWorldToClipPos(world);

                // Index 0 is meant to be the local player. Brightening it
                // makes "am I index 0" answerable at a glance.
                o.tint = hue * (player == 0 ? 1.0 : 0.45);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return fixed4(i.tint, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
