// YAPS — the SOCKET side. A socket that reacts to what has gone into it,
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

// Where each shape starts and how far it takes to arrive, in fractions of
// the plug's length. Four shapes: an entry-open plus three staged ones,
// the arrangement DPS settled on and the one socket authors already build
// their meshes around.
float4 _YAPS_SocketShapeStart;
float4 _YAPS_SocketShapeFade;

// How much of the baked shapes to apply at all. Zero is off, so a socket
// converted before this existed behaves exactly as it did.
float _YAPS_SocketPower;

// A plug's tracker light: black, vertex-only, second decimal 8 or 9.
// Returns false when nothing in the four slots is one.
bool YapsFindPlug(float3 socketWorld, out float3 plugAt, out float plugLength)
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

        float3 at = YapsLightPosition(i);
        float away = distance(at, socketWorld);
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
float YapsPlugDepth(float3 socketWorld)
{
    float depth = 0;

    float3 plugAt;
    float plugLength;
    if (YapsFindPlug(socketWorld, plugAt, plugLength) && plugLength > 0.0001)
    {
        // Raliv's formula, and the reason the light has to be at the base.
        float through = plugLength - distance(socketWorld, plugAt);
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
// does not follow anything — it opens. All the geometry was decided by
// whoever authored the blendshapes; this only chooses how much of each to
// apply and when.
void YapsSocketDeform(inout float3 position, inout float3 normal, inout float3 tangent,
                      uint vertexId)
{
    if (_YAPS_SocketPower <= 0) return;

    uint total = (uint) max(_YAPS_VertexCount, 0);
    uint shapeCount = (uint) max(_YAPS_ShapeCount, 0);
    if (vertexId >= total || shapeCount == 0) return;

    float3 socketWorld = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
    float depth = YapsPlugDepth(socketWorld);
    if (depth <= 0) return;

    // Same block layout the plug bake uses, so one baker serves both:
    // a header float, ten floats a vertex, then nine a vertex per shape.
    uint block = 1 + total * 10;

    [loop]
    for (uint s = 0; s < min(shapeCount, 4u); s++)
    {
        float start = _YAPS_SocketShapeStart[s];
        float fade = max(_YAPS_SocketShapeFade[s], 0.0001);
        float weight = saturate((depth - start) / fade) * _YAPS_SocketPower;
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
