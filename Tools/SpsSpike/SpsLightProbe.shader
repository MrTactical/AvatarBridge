// Spike S2. Reads the four built-in vertex light slots and draws what it
// found, so the question "does ChilloutVR populate unity_4LightPos* for
// content shaders in a real world" gets a yes or no by looking at it.
//
// Every face carries the same readout: four rows, one per slot, top row
// is slot 0. Each row is a colour swatch (what the slot decoded to) and
// a ruler showing the recovered light range against the protocol marks.
//
// Border pulsing cyan = the shader is running. Border solid red = the
// vertex stage and the fragment stage disagree about slot 0, which is
// the thing that would make a deform ghost between passes.
Shader "AvatarBridge/SPS Light Probe"
{
    Properties
    {
        [Toggle] _FlipRows ("Slot 0 at bottom", Float) = 0
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
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float _FlipRows;

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
                float3 worldPos : TEXCOORD1;
                float vertexRange0 : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // 5 / sqrt(atten) recovers the authored range: Unity packs
            // 1/range^2 * 25 into the attenuation slot.
            float SlotRange(float atten)
            {
                return (atten <= 1e-6) ? 1e6 : 5.0 * rsqrt(max(atten, 1e-8));
            }

            // 0 hole, 1 ring, 2 front, 3 tip, 4 sub-half but unrecognised,
            // 5 ordinary light or empty slot.
            int SlotClass(float range, float4 colour)
            {
                if (range >= 0.5) return 5;
                // Protocol lights are authored black. Anything carrying
                // colour is somebody's actual lighting.
                if (any(colour.rgb > 0.0001) && colour.a > 0) return 5;
                int digit = (int) round(fmod(range, 0.1) * 100.0);
                if (digit == 1) return 0;
                if (digit == 2) return 1;
                if (digit == 5) return 2;
                if (digit == 9 || digit == 8) return 3;
                return 4;
            }

            float3 ClassColour(int c)
            {
                if (c == 0) return float3(1.0, 0.15, 0.15);   // hole
                if (c == 1) return float3(0.15, 1.0, 0.25);   // ring
                if (c == 2) return float3(0.25, 0.45, 1.0);   // front
                if (c == 3) return float3(1.0, 0.2, 1.0);     // tip
                if (c == 4) return float3(1.0, 0.65, 0.1);    // unrecognised
                return float3(0.85, 0.85, 0.85);              // ordinary
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                // The stage SPS actually deforms in. Carried across so the
                // fragment can call out a disagreement.
                o.vertexRange0 = SlotRange(unity_4LightAtten0[0]);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float fragRange0 = SlotRange(unity_4LightAtten0[0]);
                bool stagesDisagree = abs(fragRange0 - i.vertexRange0) > 0.001
                    && !(fragRange0 > 1e5 && i.vertexRange0 > 1e5);

                float border = 0.03;
                float2 uv = i.uv;
                if (uv.x < border || uv.x > 1.0 - border
                    || uv.y < border || uv.y > 1.0 - border)
                {
                    if (stagesDisagree) return fixed4(1, 0, 0, 1);
                    float pulse = 0.5 + 0.5 * sin(_Time.y * 3.0);
                    return fixed4(0, pulse * 0.9, pulse * 0.6, 1);
                }

                float2 t = (uv - border) / (1.0 - 2.0 * border);
                float rowAxis = (_FlipRows > 0.5) ? t.y : (1.0 - t.y);
                int row = (int) floor(rowAxis * 4.0);
                float withinRow = frac(rowAxis * 4.0);

                float3 col = float3(0.03, 0.03, 0.035);
                if (withinRow < 0.06) return fixed4(0.0, 0.0, 0.0, 1);

                [unroll]
                for (int k = 0; k < 4; k++)
                {
                    if (row != k) continue;

                    float atten = unity_4LightAtten0[k];
                    float4 lightColour = unity_LightColor[k];
                    bool empty = atten <= 1e-6;
                    float range = SlotRange(atten);
                    int cls = SlotClass(range, lightColour);

                    float3 lightPos = float3(unity_4LightPosX0[k],
                                             unity_4LightPosY0[k],
                                             unity_4LightPosZ0[k]);
                    float dist = distance(lightPos, i.worldPos);

                    if (t.x < 0.14)
                    {
                        // Swatch: hue says what it decoded to, brightness
                        // says how far away it claims to be. If the light
                        // is real, walking toward it brightens this.
                        float near = saturate(1.0 - dist / 3.0);
                        col = empty
                            ? float3(0.09, 0.09, 0.09)
                            : ClassColour(cls) * (0.2 + 0.8 * near);
                    }
                    else if (t.x > 0.16 && !empty)
                    {
                        float bar = (t.x - 0.16) / 0.84;

                        if (withinRow < 0.56)
                        {
                            // Ruler across 0.38 .. 0.52, where the protocol
                            // lives. Ticks are the authored values; the fat
                            // mark is what this slot actually decoded to.
                            col = float3(0.06, 0.06, 0.07);

                            float ticks[4] = { 0.41, 0.42, 0.45, 0.49 };
                            [unroll]
                            for (int m = 0; m < 4; m++)
                            {
                                float tickAt = (ticks[m] - 0.38) / 0.14;
                                if (abs(bar - tickAt) < 0.005) col = float3(0.4, 0.4, 0.45);
                            }

                            float mark = saturate((range - 0.38) / 0.14);
                            if (range >= 0.5) mark = 1.0;
                            if (abs(bar - mark) < 0.013) col = ClassColour(cls);
                        }
                        else if (withinRow > 0.62)
                        {
                            // How far away the light claims to be, 0..5 m,
                            // ticked every metre. This is what separates a
                            // light riding this same object from one on
                            // somebody else across the room — without it,
                            // "my own lights" and "their lights" look
                            // identical on the readout.
                            col = float3(0.04, 0.04, 0.05);
                            [unroll]
                            for (int n = 1; n < 5; n++)
                            {
                                if (abs(bar - n * 0.2) < 0.004) col = float3(0.3, 0.3, 0.34);
                            }
                            if (bar < saturate(dist / 5.0)) col = float3(0.6, 0.6, 0.66);
                        }
                    }
                }

                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
