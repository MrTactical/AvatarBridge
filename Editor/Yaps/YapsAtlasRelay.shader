// A plug's light relay: the sockets its marker lights name, written into the
// atlas strip for the passes Unity hands no lights. See yaps_atlas.cginc.
//
// ForwardBase, so it is handed vertex lights at all, and drawn on the plug's
// bounds, so they are the plug's four. The socket writers' queue, before the
// grab, so the reading passes find it a frame later.
//
// One quad per home, flagged by z, corners in UV0 and every position zero, as
// the exit's quads and for the same reason.
Shader "YAPS/Atlas Relay"
{
    Properties
    {
        _YAPS_Owner ("Owner id", Float) = 0
        _YAPS_PlugIndex ("Plug number", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Background-945" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ VERTEXLIGHT_ON
            #include "UnityCG.cginc"
            #include "yaps_resolve.cginc"

            struct appdata { float4 vertex : POSITION; float3 corner : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float u : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                int owner = YapsOwnerOf(_YAPS_Owner);
                int n = (int) round(_YAPS_PlugIndex);
                float3 at[YAPS_RELAY_ROOTS], fwd[YAPS_RELAY_ROOTS];
                int kind[YAPS_RELAY_ROOTS];
                // Nothing to say is nothing drawn, so an idle plug holds no slot.
                bool live = YapsRelayFits() && owner != 0 && n >= 1 && n <= YAPS_ATLAS_SOCKETMAX
                            && YapsRelayRoots(at, fwd, kind) > 0;
                uint row = YapsRelayRow(owner, n, v.corner.z > 0.5 ? 1 : 0);
                float2 unit = v.corner.xy + 0.5;
                o.pos = live
                    ? float4(YapsAtlasToClip(float2(0, row) + unit * float2(YAPS_RELAY_PX, 1)), UNITY_NEAR_CLIP_VALUE, 1)
                    : YapsAtlasNowhere();
                o.u = unit.x;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 at[YAPS_RELAY_ROOTS], fwd[YAPS_RELAY_ROOTS];
                int kind[YAPS_RELAY_ROOTS];
                YapsRelayRoots(at, fwd, kind);
                int px = min((int) (i.u * YAPS_RELAY_PX), YAPS_RELAY_PX - 1);
                return YapsRelayPixel(px, YapsOwnerOf(_YAPS_Owner), (int) round(_YAPS_PlugIndex), at, fwd, kind);
            }
            ENDCG
        }
    }
}
