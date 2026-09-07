// YAPS, the SOCKET side. A socket reacts to what has entered it, in the
// vertex shader, with no synced parameters.
//
// The animator route is not replaced. A contact drives a "#" local
// parameter, every client computes it, nothing crosses the wire.
//
// This adds the two things that route cannot do:
//   1. It works against content with NO CONTACTS. DPS is marker lights.
//   2. It knows WHERE the plug is, not merely how deep.
//
// A plug announces itself with ONE black vertex light:
//   range     second decimal 8 or 9
//   position  the plug's BASE
//   intensity the plug's LENGTH, in metres
//
// Intensity is a length, never a brightness. Raliv ships 0.354 for a
// 0.354 m model, and depth = plugLength - distance(socket, plugBase)
// only means anything because the light sits at the BASE.

#ifndef YAPS_SOCKET_INCLUDED
#define YAPS_SOCKET_INCLUDED

// Brings the bake reader, YapsSafeNormalize and the light decode.
// One bake format and one protocol serve both ends.
#include "yaps_deform.cginc"

// Depth from the contact channel, as a fraction of plug length.
// Negative when the channel has nothing to say.
float _YAPS_SocketDepth;

// The socket's position in the mesh's local space. Zero for a dedicated
// socket mesh, set at bake time for a body mesh. Depth measures from here.
// Ownership does not: whose-plug stays on the mesh origin.
float4 _YAPS_SocketOrigin;

// Skip the self-exclusion below. Zero keeps it, as older bakes have.
// The baker sets it when no plug of the wearer's rests within reach,
// where the test can only get it wrong.
float _YAPS_SocketNoSelfExclude;

// Where each shape starts and how long it takes, in fractions of plug
// length. Up to sixteen, so several can open together.
float4 _YAPS_SocketShapeStart, _YAPS_SocketShapeStart2, _YAPS_SocketShapeStart3, _YAPS_SocketShapeStart4;
float4 _YAPS_SocketShapeFade, _YAPS_SocketShapeFade2, _YAPS_SocketShapeFade3, _YAPS_SocketShapeFade4;

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

// How much of the baked shapes to apply. Zero is off, as older bakes are.
float _YAPS_SocketPower;

// How far a shaft has travelled past this socket, as a fraction of its
// own length. Zero when nothing has arrived. The plug's length rides in
// the light colour's alpha, where Unity keeps vertex-light intensity.
//
// THE DEEPEST WINS, never the nearest. Depth is (length - distance) /
// length, so a long plug standing off beats a short one close in. The
// maximum also lets TWO plugs share a socket instead of one passing
// through closed mesh.
//
// Distance measures from the socket, whose-plug from the owner anchor,
// since a socket on a hand can sit in somebody else's lap.
float YapsPlugDepth(float3 socketAt, float3 ownerAnchor)
{
    float depth = 0;

    [unroll]
    for (uint i = 0; i < 4; i++)
    {
        // Black, or it is real lighting rather than protocol.
        if (dot(unity_LightColor[i].rgb, unity_LightColor[i].rgb) > 0.0001) continue;

        float range = YapsLightRange(i);
        if (range >= 0.5) continue;
        int digit = (int) round(fmod(range, 0.1) * 100);
        if (digit != 8 && digit != 9) continue;

        // The wearer's own plug must not open the wearer's own socket.
        // Its tracker sits a hand's width off permanently, so the socket
        // read as always full. Gated, because ownership goes by nearest
        // hip and a socket on a hand can be nearer a stranger's.
        if (_YAPS_SocketNoSelfExclude <= 0 && YapsSocketOwnPlug(ownerAnchor, i)) continue;

        float plugLength = unity_LightColor[i].a;
        if (plugLength <= 0.0001) continue;

        // Raliv's formula. The light has to be at the base.
        float through = plugLength - distance(socketAt, YapsLightPosition(i));
        depth = max(depth, saturate(through / plugLength));
    }

    // The channel wins when it has an answer, having measured it.
    // Negative means nothing to say, which is not zero.
    if (_YAPS_SocketDepth >= 0)
    {
        depth = max(depth, saturate(_YAPS_SocketDepth));
    }
    return depth;
}

// The deform. Baked shape deltas, staged by depth.
// No bezier, no frame recovery, no walk. A socket does not follow
// anything, it opens. The author's blendshapes decided the geometry.
void YapsSocketDeform(inout float3 position, inout float3 normal, inout float3 tangent,
                      uint vertexId)
{
    if (_YAPS_SocketPower <= 0) return;

    uint total = (uint) max(_YAPS_VertexCount, 0);
    uint shapeCount = (uint) max(_YAPS_ShapeCount, 0);
    if (vertexId >= total || shapeCount == 0) return;

    // Two positions, two questions. The mesh origin says whose socket
    // this is, the baked offset where it is. Equal on a socket mesh.
    float3 ownerAnchor = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
    float3 socketAt = mul(unity_ObjectToWorld, float4(_YAPS_SocketOrigin.xyz, 1)).xyz;
    float depth = YapsPlugDepth(socketAt, ownerAnchor);
    if (depth <= 0) return;

    // Same block layout as the plug bake, so one baker serves both.
    // A header float, ten floats a vertex, then nine a vertex per shape.
    uint block = 1 + total * 10;

    [loop]
    for (uint s = 0; s < min(shapeCount, 16u); s++)
    {
        float start = YapsSocketStageStart(s);
        float fade = max(YapsSocketStageFade(s), 0.0001);

        // CUMULATIVE, as DPS is: an arrived shape stays and going deeper
        // stacks the next on top. Never a travelling band, which would
        // empty the part already occupied.
        //
        // Cosine eased, DPS's touch. A linear ramp pops at the corners.
        float weight = saturate((depth - start) / fade);
        weight = -cos(weight * 3.14159265) * 0.5 + 0.5;
        weight *= _YAPS_SocketPower;
        if (weight <= 0.0001) continue;

        uint at = block + s * total * 9 + vertexId * 9;
        position += YapsReadFloat3(at) * weight;
        normal += YapsReadFloat3(at + 3) * weight;
        tangent += YapsReadFloat3(at + 6) * weight;
    }

    normal = YapsSafeNormalize(normal, float3(0, 0, 1));
    tangent = YapsSafeNormalize(tangent, float3(1, 0, 0));
}

#endif
