// Phase 1d harness. Minimal unlit shader carrying the YAPS deform, so
// the bend can be developed against a real baked plug in the editor with
// no patcher, no conversion and no upload.
//
// Shades by world normal so the bend, the twist of the frame and the
// tip taper are all visible at a glance.
Shader "AvatarBridge/YAPS Test Plug"
{
    Properties
    {
        _YAPS_Bake ("Baked data", 2D) = "black" {}
        _YAPS_VertexCount ("Vertex count", Float) = 0
        _YAPS_Enabled ("Enabled", Range(0,1)) = 1
        _YAPS_Length ("Plug length (local)", Float) = 1
        _YAPS_Overrun ("Allow overrun", Range(0,1)) = 1
        _YAPS_BakeScale ("Bake scale", Float) = 1
        _YAPS_SocketPos ("Socket position", Vector) = (0,0,0,0)
        // ZERO, and it matters. Zero means "nobody said which way this
        // socket faces", which is what makes the deform derive the
        // direction from the approach and meet a plug from any side. The
        // contact channel publishes position and never orientation, so a
        // non-zero default here is not a fallback — it is a fixed socket
        // frame asserted forever, and the plug enters along the same axis
        // however the socket is turned.
        _YAPS_SocketForward ("Socket forward", Vector) = (0,0,0,0)
        _YAPS_SocketUp ("Socket up", Vector) = (0,0,0,0)
        // Engaged defaults OFF. Starting at 1 bends the plug at whatever
        // the other defaults describe until something writes otherwise.
        _YAPS_SocketFlags ("Socket flags (x engaged, y hole)", Vector) = (0,0,0,0)
        _YAPS_SocketFront ("Socket front (channel space)", Vector) = (0,0,0,0)

        // These must be DECLARED here, not merely set from code. A uniform
        // the Properties block does not name has no per-material value to
        // serialize, so SetFloat on it is forgotten by the time the prop is
        // built — the shader then ran with channel space off and read the
        // channel's normalised box coordinates as a world position.
        //
        // The list matches what YapsShaderPatcher injects into a converted
        // avatar's shader, and has to keep matching it: the same include
        // decodes both, and it cannot tell a prop from an avatar.
        _YAPS_FrameFromVertex ("Frame from vertex", Range(0,1)) = 0
        _YAPS_ChannelSpace ("Channel space", Range(0,1)) = 0
        _YAPS_ChannelExtents ("Channel extents", Vector) = (1,1,1,0)
        _YAPS_SelfTag ("Self tag", Float) = -1
        _YAPS_TaperStart ("Hole taper start", Range(0,1)) = 0.05
        _YAPS_TaperEnd ("Hole taper end", Range(0,1)) = 0.10
        _YAPS_ShapeCount ("Shape count", Float) = 0
        _YAPS_ShapeWeights ("Shape weights 0-3", Vector) = (0,0,0,0)
        _YAPS_ShapeWeights2 ("Shape weights 4-7", Vector) = (0,0,0,0)
        [Enum(Deform,0,Active weight,1,Engagement,2,Blend,3,Baked Z,4)]
        _YAPS_Debug ("Debug view", Float) = 0
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
            // Project-absolute so the harness resolves wherever it sits;
            // this is where AvatarBridge deploys the YAPS includes.
            #include "Assets/AvatarBridge/Editor/Yaps/yaps_deform.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                uint vertexId : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float _YAPS_Debug;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float4 debug : TEXCOORD1;
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
                o.debug = YapsDebug(v.vertexId);
                YapsDeform(position, normal, tangent, v.vertexId);

                o.pos = UnityObjectToClipPos(float4(position, 1));
                o.worldNormal = UnityObjectToWorldNormal(normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // Debug views answer "why is nothing happening" directly.
                // Black in any of them names the culprit: no active weight
                // means the bake is not being read, no engagement means the
                // socket is out of range or not being written.
                if (_YAPS_Debug > 0.5)
                {
                    if (_YAPS_Debug < 1.5) return fixed4(i.debug.xxx, 1);          // active
                    if (_YAPS_Debug < 2.5) return fixed4(i.debug.yyy, 1);          // engagement
                    if (_YAPS_Debug < 3.5) return fixed4(i.debug.zzz, 1);          // blend
                    return fixed4(frac(i.debug.www * 5).xxx, 1);                   // baked Z, banded
                }

                float3 n = normalize(i.worldNormal);
                // Normal-as-colour: any fold, pinch or twist in the bend
                // shows up immediately instead of hiding under lighting.
                return fixed4(n * 0.5 + 0.5, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
