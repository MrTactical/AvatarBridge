// The runtime tester's stand-in for a patched plug shader: the same two
// calls the patcher injects (YapsShaderPatcher, "--- YAPS ---"), in the same
// order, on the same includes, then every bent vertex written to a buffer
// the tester reads back. The host shader's lighting is irrelevant to where
// vertices go, so it is left out. Lives in Assets/AvatarBridge/Editor/DevTools
// when deployed, hence the relative include.
Shader "Hidden/YapsBendCapture"
{
    Properties
    {
        // Declared so YapsOwnerStandIn recognises the slot and hands it the
        // avatar's stand-in id. Everything else is set by the tester by name.
        _YAPS_Owner ("YAPS owner id", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #include "UnityCG.cginc"
            #include "../Yaps/yaps_deform.cginc"
            #include "../Yaps/yaps_socket.cginc"

            RWStructuredBuffer<float4> _YapsCapture : register(u1);
            float _YapsCaptureOn;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                uint id : SV_VertexID;
            };
            struct v2f { float4 pos : SV_POSITION; };

            v2f vert(appdata v)
            {
                float3 p = v.vertex.xyz, n = v.normal, t = v.tangent.xyz;
                YapsDeform(p, n, t, v.id);
                YapsSocketDeform(p, n, t, v.id);
                if (_YapsCaptureOn > 0.5)
                    _YapsCapture[v.id] = float4(mul(unity_ObjectToWorld, float4(p, 1)).xyz, 1);
                v2f o;
                o.pos = UnityObjectToClipPos(float4(p, 1));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return fixed4(0.5, 0.5, 0.5, 1); }
            ENDCG
        }
    }
}
