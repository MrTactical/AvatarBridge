// What the atlas read costs per vertex. Pass 0 runs the shipped resolve for
// every vertex, as a plug's vertex stage does; pass 1 runs nothing and is the
// floor to subtract. Every point lands behind the far plane, so nothing
// rasterises, but its position depends on the answer, so the read cannot be
// compiled out. Lives in Assets/AvatarBridge/Editor/DevTools when deployed,
// hence the relative include.
Shader "Hidden/YapsResolvePerf"
{
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        CGINCLUDE
        #pragma target 5.0
        #include "UnityCG.cginc"
        #include "../Yaps/yaps_resolve.cginc"

        float3 _PerfRoot;
        float3 _PerfAxis;
        float _PerfLength;

        struct v2f { float4 pos : SV_POSITION; };

        v2f vertResolve(uint id : SV_VertexID)
        {
            YapsChain c = YapsResolveChain(_PerfRoot, _PerfAxis, _PerfLength, float3(1e6, 1e6, 1e6));
            v2f o;
            o.pos = float4((c.headers + c.hits + c.position[0].x) * 1e-9 + id * 1e-12, 0, 2, 1);
            return o;
        }

        v2f vertFloor(uint id : SV_VertexID)
        {
            v2f o;
            o.pos = float4(id * 1e-12, 0, 2, 1);
            return o;
        }

        fixed4 frag(v2f i) : SV_Target { return 0; }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma vertex vertResolve
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
