#ifndef YAPS_STANDARD_INPUT_INCLUDED
#define YAPS_STANDARD_INPUT_INCLUDED

// The vertex input YAPS Standard's wrappers take. Unity's own struct drops the tangent in
// variants without a normal or height map, which leaves the patcher no frame to bend a skinned
// plug by, and it has no vertex id. Included after Unity's input, so its defines are known.
struct YapsStandardInput
{
    float4 vertex   : POSITION;
    float3 normal   : NORMAL;
    float4 tangent  : TANGENT;
    float2 uv0      : TEXCOORD0;
    float2 uv1      : TEXCOORD1;
#if defined(DYNAMICLIGHTMAP_ON) || defined(UNITY_PASS_META)
    float2 uv2      : TEXCOORD2;
#endif
    uint vertexId   : SV_VertexID;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

VertexInput YapsToUnity (YapsStandardInput i)
{
    VertexInput v = (VertexInput)0;
    v.vertex = i.vertex;
    v.normal = i.normal;
    v.uv0 = i.uv0;
#ifdef UNITY_STANDARD_SHADOW_INCLUDED
    // The caster's own struct: no second UV, and a tangent only for parallax.
    #if defined(UNITY_STANDARD_USE_SHADOW_UVS) && defined(_PARALLAXMAP)
        v.tangent = i.tangent;
    #endif
#else
    v.uv1 = i.uv1;
    #if defined(DYNAMICLIGHTMAP_ON) || defined(UNITY_PASS_META)
        v.uv2 = i.uv2;
    #endif
    #ifdef _TANGENT_TO_WORLD
        v.tangent = i.tangent;
    #endif
#endif
    UNITY_TRANSFER_INSTANCE_ID(i, v);
    return v;
}

// The caster's outputs go before the input, so the input stays the last parameter.
#if defined(UNITY_STANDARD_USE_SHADOW_OUTPUT_STRUCT) && defined(UNITY_STANDARD_USE_STEREO_SHADOW_OUTPUT_STRUCT)
    #define YAPS_CASTER_OUTPUTS out VertexOutputShadowCaster o, out VertexOutputStereoShadowCaster os,
#elif defined(UNITY_STANDARD_USE_SHADOW_OUTPUT_STRUCT)
    #define YAPS_CASTER_OUTPUTS out VertexOutputShadowCaster o,
#elif defined(UNITY_STANDARD_USE_STEREO_SHADOW_OUTPUT_STRUCT)
    #define YAPS_CASTER_OUTPUTS out VertexOutputStereoShadowCaster os,
#else
    #define YAPS_CASTER_OUTPUTS
#endif

#endif
