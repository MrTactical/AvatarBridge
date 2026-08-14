// Harness shader for the SOCKET side, the mirror of YapsTestPlug.
//
// Minimal and unlit, so a socket's opening can be developed against a real
// baked mesh in the editor with no patcher, no conversion and no upload —
// exactly how the plug deform was built.
//
// Shades by world normal, so a fold or a pinch in the opening shows up
// immediately instead of hiding under lighting.
Shader "AvatarBridge/YAPS Test Socket"
{
    Properties
    {
        _YAPS_Bake ("Baked data", 2D) = "black" {}
        _YAPS_VertexCount ("Vertex count", Float) = 0

        // How much of the baked shapes to apply. ZERO by default: a socket
        // that has not been set up deliberately should not move.
        _YAPS_SocketPower ("Socket power", Range(0,1)) = 0

        // Depth from the contact channel, as a fraction of plug length.
        // NEGATIVE means "the channel has nothing to say", which is not the
        // same as zero — zero is a plug present and not yet in.
        _YAPS_SocketDepth ("Socket depth (-1 = no channel)", Range(-1,1)) = -1

        // Where each shape starts, and how long it takes to arrive, in
        // fractions of the plug's length. Four: an entry-open plus three
        // staged, the arrangement DPS settled on.
        _YAPS_SocketShapeStart ("Shape starts", Vector) = (0, 0.2, 0.5, 0.75)
        _YAPS_SocketShapeFade ("Shape fades", Vector) = (0.15, 0.2, 0.2, 0.2)

        _YAPS_ShapeCount ("Shape count", Float) = 0

        // The plug deform's own properties come along with the include and
        // are unused here, but a shader must declare what it references.
        _YAPS_Enabled ("Enabled", Range(0,1)) = 0
        _YAPS_Length ("Plug length (local)", Float) = 1
        _YAPS_Overrun ("Allow overrun", Range(0,1)) = 1
        _YAPS_BakeScale ("Bake scale", Float) = 1
        _YAPS_FrameFromVertex ("Frame from vertex", Range(0,1)) = 0
        _YAPS_SocketPos ("Socket position", Vector) = (0,0,0,0)
        _YAPS_SocketForward ("Socket forward", Vector) = (0,0,0,0)
        _YAPS_SocketUp ("Socket up", Vector) = (0,0,0,0)
        _YAPS_SocketFlags ("Socket flags", Vector) = (0,0,0,0)
        _YAPS_SocketFront ("Socket front", Vector) = (0,0,0,0)
        _YAPS_ChannelSpace ("Channel space", Range(0,1)) = 0
        _YAPS_ChannelExtents ("Channel extents", Vector) = (1,1,1,0)
        _YAPS_SelfTag ("Self tag", Float) = -1
        _YAPS_TaperStart ("Hole taper start", Range(0,1)) = 0.05
        _YAPS_TaperEnd ("Hole taper end", Range(0,1)) = 0.20
        _YAPS_IdleLength ("Idle length", Range(0.1,1)) = 1
        _YAPS_IdleWidth ("Idle width", Range(0.1,1)) = 1
        _YAPS_Squeeze ("Squeeze", Range(0,1)) = 0
        _YAPS_SqueezeDistance ("Squeeze reach", Range(0.01,1)) = 0.15
        _YAPS_Bulge ("Bulge", Range(0,1)) = 0
        _YAPS_BulgeDistance ("Bulge reach", Range(0.01,1)) = 0.2
        _YAPS_PumpStrength ("Pumping", Range(0,0.5)) = 0
        _YAPS_PumpSpeed ("Pumping speed", Range(0,20)) = 6
        _YAPS_WriggleStrength ("Wriggle", Range(0,0.5)) = 0
        _YAPS_WriggleSpeed ("Wriggle speed", Range(0,20)) = 2
        _YAPS_ShapeWeights ("Shape weights 0-3", Vector) = (0,0,0,0)
        _YAPS_ShapeWeights2 ("Shape weights 4-7", Vector) = (0,0,0,0)
        _YAPS_ShapeWeights3 ("Shape weights 8-11", Vector) = (0,0,0,0)
        _YAPS_ShapeWeights4 ("Shape weights 12-15", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            // Project-absolute so the harness resolves wherever it sits.
            #include "Assets/AvatarBridge/Editor/Yaps/yaps_socket.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                uint vertexId : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
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

                float3 position = v.vertex.xyz;
                float3 normal = v.normal;
                float3 tangent = v.tangent.xyz;
                YapsSocketDeform(position, normal, tangent, v.vertexId);

                o.pos = UnityObjectToClipPos(float4(position, 1));
                o.worldNormal = UnityObjectToWorldNormal(normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.worldNormal);
                return fixed4(n * 0.5 + 0.5, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
