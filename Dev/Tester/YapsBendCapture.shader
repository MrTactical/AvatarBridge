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
            // ForwardBase with the vertex-light variant, as the hosts are:
            // marker lights reach a shader only as unity_4Light*, which Unity
            // fills in this pass alone. Without it no light socket is ever found.
            Tags { "LightMode" = "ForwardBase" }
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "../Yaps/yaps_deform.cginc"
            #include "../Yaps/yaps_socket.cginc"

            RWStructuredBuffer<float4> _YapsCapture : register(u1);
            float _YapsCaptureOn;
            // The atlas list itself, for one vertex the tester names, from the
            // rest root it measured, written past the vertices at _YapsProbeAt.
            float _YapsProbeVertex;
            float _YapsProbeAt;
            float4 _YapsProbeRoot;
            float4 _YapsProbeForward;
            float _YapsProbeLength;
            // Two vertices' recovered roots, written at _YapsTwinAt: a seam's twins share a point
            // and a normal, so a root that differs between them is what parts them in a bend.
            float _YapsTwinA;
            float _YapsTwinB;
            float _YapsTwinAt;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                uint id : SV_VertexID;
            };
            struct v2f { float4 pos : SV_POSITION; float3 nrm : TEXCOORD0; };

            v2f vert(appdata v)
            {
                float3 p = v.vertex.xyz, n = v.normal, t = v.tangent.xyz;
                YapsDeform(p, n, t, v.id);
                float3 deformed = p;
                YapsSocketDeform(p, n, t, v.id);
                if (_YapsCaptureOn > 0.5)
                    _YapsCapture[v.id] = float4(mul(unity_ObjectToWorld, float4(p, 1)).xyz, 1);
                if (_YapsCaptureOn > 0.5 && _YapsTwinAt > 0.5 && (v.id == (uint) _YapsTwinA || v.id == (uint) _YapsTwinB))
                {
                    YapsVertex b = YapsReadBaked(v.id);
                    YapsRoot r = YapsRecoverRoot(b, v.vertex.xyz, v.normal, v.tangent.xyz);
                    uint at = (uint) _YapsTwinAt + (v.id == (uint) _YapsTwinA ? 0 : 16);
                    _YapsCapture[at] = float4(r.position, r.recovered);
                    _YapsCapture[at + 1] = float4(r.forward, r.worldLength);
                    _YapsCapture[at + 2] = float4(r.up, 0);
                    _YapsCapture[at + 3] = float4(b.normal, b.active);
                    _YapsCapture[at + 4] = float4(b.tangent, 0);
                    _YapsCapture[at + 5] = float4(v.tangent.xyz, v.tangent.w);
                    // Where the plug's deform left it, where the socket deform after it did, and
                    // the socket this vertex resolved from its own root.
                    YapsSocket sk = YapsResolveSocket(r.position, r.forward, r.up, r.worldLength);
                    _YapsCapture[at + 6] = float4(mul(unity_ObjectToWorld, float4(deformed, 1)).xyz, 0);
                    _YapsCapture[at + 7] = float4(mul(unity_ObjectToWorld, float4(p, 1)).xyz, 0);
                    _YapsCapture[at + 8] = float4(sk.position, sk.engaged);
                    _YapsCapture[at + 9] = float4(sk.forward, sk.tier);
                    // The chain this vertex read, as the single-vertex probe below writes it.
                    YapsChain tc = YapsResolveChain(r.position, r.forward, r.worldLength, float3(1e9, 1e9, 1e9));
                    _YapsCapture[at + 10] = float4(tc.count, tc.engaged, tc.headers, tc.hits);
                    [unroll] for (int j = 0; j < YAPS_CHAIN_MAX; j++)
                        _YapsCapture[at + 11 + j] = float4(tc.position[j], tc.kind[j]);
                    _YapsCapture[at + 11 + YAPS_CHAIN_MAX] = float4(r.worldLength, 0, 0, 0);
                }
                if (_YapsCaptureOn > 0.5 && _YapsProbeAt > 0.5 && v.id == (uint) _YapsProbeVertex)
                {
                    YapsChain c = YapsResolveChain(_YapsProbeRoot.xyz, _YapsProbeForward.xyz, _YapsProbeLength,
                                                   float3(1e9, 1e9, 1e9));
                    uint at = (uint) _YapsProbeAt;
                    _YapsCapture[at] = float4(c.count, c.engaged, c.headers, c.hits);
                    [unroll] for (int i = 0; i < YAPS_CHAIN_MAX; i++)
                        _YapsCapture[at + 1 + i] = float4(c.position[i], c.kind[i]);
                    _YapsCapture[at + 1 + YAPS_CHAIN_MAX] = float4(c.refusedAt, c.refusedD);
                }
                v2f o;
                o.pos = UnityObjectToClipPos(float4(p, 1));
                o.nrm = UnityObjectToWorldNormal(n);
                return o;
            }

            // Shaded from the bent normals, for the tester's pictures.
            fixed4 frag(v2f i) : SV_Target
            {
                float lit = 0.3 + 0.7 * saturate(dot(normalize(i.nrm), normalize(float3(0.3, 1, 0.2))));
                return fixed4(0.85, 0.72, 0.68, 1) * lit;
            }
            ENDCG
        }
    }
}
