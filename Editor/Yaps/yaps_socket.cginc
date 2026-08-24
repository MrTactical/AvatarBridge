// YAPS, the SOCKET side. A socket that reacts to what has gone into it,
// in the vertex shader, without spending a single synced parameter.
//
// ---------------------------------------------------------------------
// WHY THIS EXISTS WHEN THE ANIMATOR ROUTE ALREADY WORKS
// ---------------------------------------------------------------------
//
// A converted avatar already plays its socket's own blendshapes from a
// contact, and on ChilloutVR that is FREE: a contact drives a "#" local
// parameter, every client computes it independently, nothing crosses the
// wire. That route is not being replaced. It handles the author's own
// shapes and it should stay.
//
// This adds the two things it cannot do:
//
//   1. It works against content with NO CONTACTS AT ALL. Raliv's DPS is
//      marker lights and nothing else, and it is most of the penetration
//      content ChilloutVR already has. A socket driven only by contacts
//      is inert against every bit of it.
//   2. It knows WHERE the plug is, not merely how deep. An animator
//      parameter is one scalar; this reads the plug's actual position and
//      length, so the shapes stage against real geometry.
//
// ---------------------------------------------------------------------
// HOW A SOCKET FINDS A PLUG
// ---------------------------------------------------------------------
//
// The mirror of the plug's own search, using the protocol's remaining
// digits. A penetrator announces itself with ONE black vertex light:
//
//   range     second decimal 8 or 9    it is a plug's tracker
//   position  the plug's BASE
//   intensity the plug's LENGTH, in metres
//
// That last one is not a brightness and reading it as one is the mistake
// this file exists to avoid repeating. Raliv's own penetrator ships
// intensity 0.354 for a 0.354 m model, and the orifice does:
//
//   depth = max(0, plugLength - distance(socket, plugBase))
//
// which only means anything because the light sits at the BASE. A light
// at the tip makes that subtraction nonsense.
//
// The contact channel can override the answer when it has one, so an
// avatar with contacts gets the exact figure and one without still works.
// Both, not either.

#ifndef YAPS_SOCKET_INCLUDED
#define YAPS_SOCKET_INCLUDED

// Brings the bake reader and YapsSafeNormalize, and through it the light
// decode in yaps_resolve. One bake format and one light protocol serve
// both ends, which is the point: a socket reading a plug and a plug
// reading a socket are the same problem seen from opposite sides.
#include "yaps_deform.cginc"

// Depth published by the contact channel, as a fraction of the plug's
// length, or negative when the channel has nothing to say. Lets a socket
// that HAS contacts use them and one that does not fall back to lights.
float _YAPS_SocketDepth;

// The socket's position in the mesh's local space. Zero for a dedicated
// socket mesh, whose origin IS the socket; set at bake time for a body
// mesh, whose origin is the avatar root. Depth is measured from here.
// Ownership is not: the whose-plug question stays on the mesh origin,
// which for a body mesh is the root beside the wearer's own hip.
float4 _YAPS_SocketOrigin;

// Skip the self-exclusion below. Zero keeps it, which is what every
// material baked before this existed does. The baker sets it when no
// plug on the avatar rests within reach of this socket, because then
// there is nothing to exclude and the test can only get it wrong.
float _YAPS_SocketNoSelfExclude;

// Where each shape starts and how far it takes to arrive, in fractions of
// the plug's length. Up to sixteen, each with its own depth, so several can
// open together; the toolkit bakes the socket's named shapes in the order
// the author staged them, the converter its own picks.
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

// How much of the baked shapes to apply at all. Zero is off, so a socket
// converted before this existed behaves exactly as it did.
float _YAPS_SocketPower;

// A plug's tracker light: black, vertex-only, second decimal 8 or 9.
// Returns false when nothing in the four slots is one. Distance is
// measured from the socket; whose-plug is judged from the owner anchor,
// because a socket on a hand can sit in somebody else's lap while its
// wearer's root stays put.
bool YapsFindPlug(float3 socketAt, float3 ownerAnchor, out float3 plugAt, out float plugLength)
{
    plugAt = 0;
    plugLength = 0;
    bool found = false;
    float nearest = 1e9;

    [unroll]
    for (uint i = 0; i < 4; i++)
    {
        // Black, or it is somebody's actual lighting rather than protocol.
        if (dot(unity_LightColor[i].rgb, unity_LightColor[i].rgb) > 0.0001) continue;

        float range = YapsLightRange(i);
        if (range >= 0.5) continue;
        int digit = (int) round(fmod(range, 0.1) * 100);
        if (digit != 8 && digit != 9) continue;

        // The wearer's own plug does not open the wearer's own socket. A
        // converted avatar carrying both has its plug's tracker a hand's
        // width from its own socket, permanently within a plug length, and
        // without this the socket read as always full.
        // Only where a plug of the wearer's actually rests on top of this
        // socket. Elsewhere the test is both unnecessary and unreliable:
        // it decides ownership by which player's hip is nearest, and a
        // socket on a hand can be nearer a stranger's hip than its own
        // wearer's, which ignores the one plug it exists for.
        if (_YAPS_SocketNoSelfExclude <= 0 && YapsSocketOwnPlug(ownerAnchor, i)) continue;

        float3 at = YapsLightPosition(i);
        float away = distance(at, socketAt);
        if (away >= nearest) continue;

        nearest = away;
        plugAt = at;
        // The LENGTH, carried in the alpha of the light colour. Unity puts
        // a vertex light's intensity there.
        plugLength = unity_LightColor[i].a;
        found = true;
    }
    return found;
}

// How far the shaft has travelled past this socket, as a fraction of its
// own length. Zero when nothing has arrived.
float YapsPlugDepth(float3 socketAt, float3 ownerAnchor)
{
    float depth = 0;

    float3 plugAt;
    float plugLength;
    if (YapsFindPlug(socketAt, ownerAnchor, plugAt, plugLength) && plugLength > 0.0001)
    {
        // Raliv's formula, and the reason the light has to be at the base.
        float through = plugLength - distance(socketAt, plugAt);
        depth = saturate(through / plugLength);
    }

    // The channel wins when it has an answer, because it measured the
    // thing rather than inferring it from a distance between two points.
    // Negative means "nothing to say", which is not the same as zero.
    if (_YAPS_SocketDepth >= 0)
    {
        depth = max(depth, saturate(_YAPS_SocketDepth));
    }
    return depth;
}

// The deform. Baked shape deltas, staged by depth.
//
// Note what is NOT here: no bezier, no frame recovery, no walk. A socket
// does not follow anything, it opens. All the geometry was decided by
// whoever authored the blendshapes; this only chooses how much of each to
// apply and when.
void YapsSocketDeform(inout float3 position, inout float3 normal, inout float3 tangent,
                      uint vertexId)
{
    if (_YAPS_SocketPower <= 0) return;

    uint total = (uint) max(_YAPS_VertexCount, 0);
    uint shapeCount = (uint) max(_YAPS_ShapeCount, 0);
    if (vertexId >= total || shapeCount == 0) return;

    // Two positions, two questions. The mesh origin answers whose socket
    // this is; the baked offset answers where it is. On a dedicated socket
    // mesh the offset is zero and they are the same point.
    float3 ownerAnchor = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
    float3 socketAt = mul(unity_ObjectToWorld, float4(_YAPS_SocketOrigin.xyz, 1)).xyz;
    float depth = YapsPlugDepth(socketAt, ownerAnchor);
    if (depth <= 0) return;

    // Same block layout the plug bake uses, so one baker serves both:
    // a header float, ten floats a vertex, then nine a vertex per shape.
    uint block = 1 + total * 10;

    [loop]
    for (uint s = 0; s < min(shapeCount, 16u); s++)
    {
        float start = YapsSocketStageStart(s);
        float fade = max(YapsSocketStageFade(s), 0.0001);

        // CUMULATIVE, as DPS is: once a shape has arrived it stays, and
        // going deeper stacks the next one on top. Not a travelling band , 
        // a tube being filled progressively is what this describes, and a
        // band would empty the part already occupied.
        //
        // Eased with a cosine rather than left linear, which is DPS's
        // touch and worth copying: a linear ramp starts and stops with a
        // visible corner, and on a shape this large the corner reads as a
        // pop.
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
