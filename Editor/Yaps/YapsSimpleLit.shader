// A plain lit shader the YAPS patcher can patch, for meshes whose own
// shader cannot be, and for the test plug. It carries no YAPS itself.
// Metallic workflow through Unity's BRDF; each pass keeps its vertex
// function in its own program block.
Shader "YAPS/Simple Lit"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Float) = 1
        [Gamma] _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 200

        CGINCLUDE
        #include "UnityCG.cginc"
        #include "Lighting.cginc"
        #include "AutoLight.cginc"
        #include "UnityPBSLighting.cginc"

        sampler2D _MainTex; float4 _MainTex_ST;
        sampler2D _BumpMap; half _BumpScale;
        fixed4 _Color; half _Metallic; half _Glossiness; half4 _EmissionColor;

        struct appdata
        {
            float4 vertex : POSITION;
            float3 normal : NORMAL;
            float4 tangent : TANGENT;
            float2 uv : TEXCOORD0;
            uint vertexId : SV_VertexID;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
            float4 tspace0 : TEXCOORD1;   // tangent-to-world rows; w carries world position
            float4 tspace1 : TEXCOORD2;
            float4 tspace2 : TEXCOORD3;
            half3 ambient : TEXCOORD4;    // SH and vertex lights, base pass only
            UNITY_LIGHTING_COORDS(5, 6)
            UNITY_FOG_COORDS(7)
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        // Shared interpolators. Called after the vertex functions set the position.
        v2f Interpolate(appdata v)
        {
            v2f o;
            UNITY_INITIALIZE_OUTPUT(v2f, o);
            UNITY_TRANSFER_INSTANCE_ID(v, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = TRANSFORM_TEX(v.uv, _MainTex);
            half3 wn = UnityObjectToWorldNormal(v.normal);
            half3 wt = UnityObjectToWorldDir(v.tangent.xyz);
            half tsign = v.tangent.w * unity_WorldTransformParams.w;
            half3 wb = cross(wn, wt) * tsign;
            o.tspace0 = float4(wt.x, wb.x, wn.x, worldPos.x);
            o.tspace1 = float4(wt.y, wb.y, wn.y, worldPos.y);
            o.tspace2 = float4(wt.z, wb.z, wn.z, worldPos.z);
            o.ambient = 0;
            UNITY_TRANSFER_LIGHTING(o, v.uv);
            UNITY_TRANSFER_FOG(o, o.pos);
            return o;
        }

        half4 Lit(v2f i, half3 lightColor, half3 lightDir, half atten, bool basePass)
        {
            half4 albedo = tex2D(_MainTex, i.uv) * _Color;
            half3 tn = UnpackScaleNormal(tex2D(_BumpMap, i.uv), _BumpScale);
            half3 n = normalize(half3(dot(i.tspace0.xyz, tn), dot(i.tspace1.xyz, tn), dot(i.tspace2.xyz, tn)));
            float3 worldPos = float3(i.tspace0.w, i.tspace1.w, i.tspace2.w);
            half3 viewDir = normalize(UnityWorldSpaceViewDir(worldPos));

            half3 specColor;
            half oneMinusReflectivity;
            half3 diffColor = DiffuseAndSpecularFromMetallic(albedo.rgb, _Metallic, specColor, oneMinusReflectivity);

            UnityLight light;
            light.color = lightColor * atten;
            light.dir = lightDir;
            light.ndotl = saturate(dot(n, lightDir));

            UnityIndirect indirect;
            indirect.diffuse = 0;
            indirect.specular = 0;
            if (basePass)
            {
                indirect.diffuse = i.ambient;
                Unity_GlossyEnvironmentData g = UnityGlossyEnvironmentSetup(_Glossiness, viewDir, n, specColor);
                indirect.specular = Unity_GlossyEnvironment(UNITY_PASS_TEXCUBE(unity_SpecCube0), unity_SpecCube0_HDR, g);
            }

            half4 c = UNITY_BRDF_PBS(diffColor, specColor, oneMinusReflectivity, _Glossiness, n, viewDir, light, indirect);
            if (basePass) c.rgb += _EmissionColor.rgb;
            c.a = 1;
            return c;
        }
        ENDCG

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }
            Cull [_Cull]

            CGPROGRAM
            #pragma target 4.0
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma vertex vertBase
            #pragma fragment fragBase

            v2f vertBase(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o = Interpolate(v);
                half3 wn = half3(o.tspace0.z, o.tspace1.z, o.tspace2.z);
                float3 worldPos = float3(o.tspace0.w, o.tspace1.w, o.tspace2.w);
                o.ambient = ShadeSH9(half4(wn, 1));
                #ifdef VERTEXLIGHT_ON
                // Marker lights are black and add nothing.
                o.ambient += Shade4PointLights(
                    unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0,
                    unity_LightColor[0].rgb, unity_LightColor[1].rgb, unity_LightColor[2].rgb, unity_LightColor[3].rgb,
                    unity_4LightAtten0, worldPos, wn);
                #endif
                return o;
            }

            fixed4 fragBase(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 worldPos = float3(i.tspace0.w, i.tspace1.w, i.tspace2.w);
                UNITY_LIGHT_ATTENUATION(atten, i, worldPos);
                half4 c = Lit(i, _LightColor0.rgb, normalize(_WorldSpaceLightPos0.xyz), atten, true);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }

        Pass
        {
            Name "FORWARD_DELTA"
            Tags { "LightMode" = "ForwardAdd" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Fog { Color (0,0,0,0) }
            Cull [_Cull]

            CGPROGRAM
            #pragma target 4.0
            #pragma multi_compile_fwdadd_fullshadows
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma vertex vertAdd
            #pragma fragment fragAdd

            v2f vertAdd(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                return Interpolate(v);
            }

            fixed4 fragAdd(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 worldPos = float3(i.tspace0.w, i.tspace1.w, i.tspace2.w);
                UNITY_LIGHT_ATTENUATION(atten, i, worldPos);
                half3 lightDir = normalize(UnityWorldSpaceLightDir(worldPos));
                half4 c = Lit(i, _LightColor0.rgb, lightDir, atten, false);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, fixed4(0, 0, 0, 0));
                return c;
            }
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            Cull [_Cull]

            CGPROGRAM
            #pragma target 4.0
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            #pragma vertex vertShadow
            #pragma fragment fragShadow

            struct v2fShadow
            {
                V2F_SHADOW_CASTER;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2fShadow vertShadow(appdata v)
            {
                v2fShadow o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 fragShadow(v2fShadow i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    FallBack Off
}
