// YAPS, the SOCKET side: reacts in the vertex shader, with no synced parameters.
// Beside the animator's contact route (a local "#" parameter), this works against
// content with no contacts (DPS is marker lights) and knows where the plug is.
//
// A plug announces itself with ONE black vertex light:
//   range     second decimal 8 or 9
//   position  the plug's BASE
//   intensity the plug's LENGTH, in metres (DPS ships 0.354 for a 0.354 m model)
// depth = length - distance(socket, base) only works because the light is at the base.

#ifndef YAPS_SOCKET_INCLUDED
#define YAPS_SOCKET_INCLUDED

// One bake format and one light protocol serve both ends.
#include "yaps_deform.cginc"

// Contact channel depth as a fraction of plug length; negative when silent.
cbuffer YapsSocket {
float _YAPS_SocketDepth;

// Socket position in mesh space (zero for a socket mesh). Depth measures from
// here; ownership stays on the mesh origin.
float4 _YAPS_SocketOrigin;

// Set by the baker when no wearer plug rests within reach, where the
// self-exclusion test can only get it wrong.
float _YAPS_SocketNoSelfExclude;

// Per shape start and fade, in fractions of plug length. Up to sixteen.
float4 _YAPS_SocketShapeStart, _YAPS_SocketShapeStart2, _YAPS_SocketShapeStart3, _YAPS_SocketShapeStart4;
float4 _YAPS_SocketShapeFade, _YAPS_SocketShapeFade2, _YAPS_SocketShapeFade3, _YAPS_SocketShapeFade4;
// How much of the baked shapes to apply. Zero is off, as older bakes are.
float _YAPS_SocketPower;
};

float YapsSocketStageStart(uint s)
{
    uint pack = s >> 2, lane = s & 3;
    float4 v = pack == 0 ? _YAPS_SocketShapeStart : pack == 1 ? _YAPS_SocketShapeStart2
             : pack == 2 ? _YAPS_SocketShapeStart3 : _YAPS_SocketShapeStart4;
    return v[lane];
}

float YapsSocketStageFade(uint s)
{
    uint pack = s >> 2, lane = s & 3;
    float4 v = pack == 0 ? _YAPS_SocketShapeFade : pack == 1 ? _YAPS_SocketShapeFade2
             : pack == 2 ? _YAPS_SocketShapeFade3 : _YAPS_SocketShapeFade4;
    return v[lane];
}

// Depth past this socket as a fraction of plug length. Length rides in the light
// colour's alpha, where Unity keeps vertex-light intensity. The deepest wins, not
// the nearest, which also lets two plugs share a socket. Whose-plug is judged
// from the owner anchor, since a hand socket can sit in somebody else's lap.
float YapsPlugDepth(float3 socketAt, float3 ownerAnchor)
{
    float depth = 0;

    [loop]
    for (uint i = 0; i < 4; i++)
    {
        if (!YapsLightIsMarker(i)) continue;

        int digit = YapsLightDigit(i);
        if (digit != 8 && digit != 9) continue;

        // The wearer's own plug sits a hand's width off for good, so the socket
        // read as always full. Gated: ownership goes by nearest hip.
        if (_YAPS_SocketNoSelfExclude <= 0 && YapsSocketOwnPlug(ownerAnchor, i)) continue;

        float plugLength = unity_LightColor[i].a;
        if (plugLength <= 0.0001) continue;

        // DPS's formula. The light has to be at the base.
        float through = plugLength - distance(socketAt, YapsLightPosition(i));
        depth = max(depth, saturate(through / plugLength));
    }

    // Negative means no answer, which is not zero.
    if (_YAPS_SocketDepth >= 0)
    {
        depth = max(depth, saturate(_YAPS_SocketDepth));
    }
    return depth;
}

// Baked shape deltas, staged by depth. A socket does not follow anything, it opens.
void YapsSocketDeform(inout float3 position, inout float3 normal, inout float3 tangent,
                      uint vertexId)
{
    if (_YAPS_SocketPower <= 0) return;

    uint total = (uint) max(_YAPS_VertexCount, 0);
    uint shapeCount = (uint) max(_YAPS_ShapeCount, 0);
    if (vertexId >= total || shapeCount == 0) return;

    // Mesh origin says whose socket, baked offset says where. Plain meshes only:
    // skinned ones draw with an identity matrix, so both land near the world
    // origin and a skinned body must take the contact route.
    float3 ownerAnchor = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
    float3 socketAt = mul(unity_ObjectToWorld, float4(_YAPS_SocketOrigin.xyz, 1)).xyz;
    float depth = YapsPlugDepth(socketAt, ownerAnchor);
    if (depth <= 0) return;

    // Plug bake layout: a header float, ten floats a vertex, then nine a vertex per shape.
    uint block = 1 + total * 10;

    // Skinning scales the normal without renormalising, so its live length over the
    // bake's carries the avatar's scale to the offsets (measured). Normal and tangent
    // offsets are per unit of their own length. _YAPS_BakeSkinScale 0 (old bake,
    // plain mesh) or a zero normal: as baked.
    float positionScale = 1, normalScale = 1, tangentScale = 1;
    float liveScale = length(normal);
    if (_YAPS_BakeSkinScale > 0 && liveScale > 1e-6)
    {
        positionScale = liveScale / _YAPS_BakeSkinScale;
        normalScale = liveScale;
        tangentScale = length(tangent);
    }

    [loop]
    for (uint s = 0; s < min(shapeCount, 16u); s++)
    {
        float start = YapsSocketStageStart(s);
        float fade = max(YapsSocketStageFade(s), 0.0001);

        // Cumulative, as DPS: arrived shapes stay, deeper ones stack. Cosine eased,
        // as a linear ramp pops at the corners.
        float weight = saturate((depth - start) / fade);
        weight = -cos(weight * 3.14159265) * 0.5 + 0.5;
        weight *= _YAPS_SocketPower;
        if (weight <= 0.0001) continue;

        uint at = block + s * total * 9 + vertexId * 9;
        position += YapsReadFloat3(at) * (weight * positionScale);
        normal += YapsReadFloat3(at + 3) * (weight * normalScale);
        tangent += YapsReadFloat3(at + 6) * (weight * tangentScale);
    }

    normal = YapsSafeNormalize(normal, float3(0, 0, 1));
    tangent = YapsSafeNormalize(tangent, float3(1, 0, 0));
}

#endif
