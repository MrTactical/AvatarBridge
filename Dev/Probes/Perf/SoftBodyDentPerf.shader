// What a soft-body dent costs per vertex. Pass 0 pushes every vertex in from up to
// four pressing points, each read from a marker-light slot and checked against the
// range tag first, as a patched body's vertex stage would. Pass 1 is the floor to
// subtract. Points land behind the far plane, so nothing rasterises, but the position
// depends on the dent, so it cannot be compiled out.
Shader "Hidden/SoftBodyDentPerf"
{
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        CGINCLUDE
        #pragma target 5.0
        #include "UnityCG.cginc"

        float4 _Points[4];   // xyz, radius
        float4 _Atten;       // per slot, as unity_4LightAtten0 arrives

        struct v2f { float4 pos : SV_POSITION; };

        float3 Vertex(uint id) { return float3(id * 1e-6, frac(id * 0.37), frac(id * 0.11)); }
        float3 Normal(uint id) { return normalize(float3(frac(id * 0.13) - 0.5, 0.3, frac(id * 0.71) - 0.5)); }

        v2f vertDent(uint id : SV_VertexID)
        {
            float3 p = Vertex(id);
            float3 n = Normal(id);
            [unroll] for (int i = 0; i < 4; i++)
            {
                // Range back from Unity's attenuation, then the protocol's tag on range % 0.1.
                float range = 5.0 * rsqrt(max(_Atten[i], 1e-6));
                float tagged = step(abs(frac(range * 10.0) - 0.5), 0.01);
                float r = _Points[i].w;
                float d = length(p - _Points[i].xyz);
                float push = saturate(1.0 - d / r);
                p -= n * (push * push * r * tagged);
            }
            v2f o;
            o.pos = float4(dot(p, 1) * 1e-9 + id * 1e-12, 0, 2, 1);
            return o;
        }

        v2f vertFloor(uint id : SV_VertexID)
        {
            float3 p = Vertex(id) + Normal(id) * 1e-3;
            v2f o;
            o.pos = float4(dot(p, 1) * 1e-9 + id * 1e-12, 0, 2, 1);
            return o;
        }

        fixed4 frag(v2f i) : SV_Target { return 0; }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma vertex vertDent
            #pragma fragment frag
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vertFloor
            #pragma fragment frag
            ENDCG
        }
    }
}
