// YAPS deform core, for ChilloutVR.
//
// Inspired by VRCFury's SPS, which invented this technique for VRChat.
// Written from a description of the behaviour, not from their source; no
// SPS code appears here. See Tools/SpsSpike/LICENSE-POSTURE.md.
//
// ---------------------------------------------------------------------
// WHAT THIS DOES, in words, so the maths below is readable
// ---------------------------------------------------------------------
//
// The plug is baked in its own local space with +Z running from base to
// tip. The trick is to stop thinking of a vertex as a point in space and
// start thinking of it as a point on a rod:
//
//     * its Z is how far ALONG the rod it sits
//     * its X and Y are how far OFF the rod's centre line it sits
//
// Bending the plug is then just: replace the straight rod with a curved
// one, walk Z metres along the curve, and re-hang the X/Y offset off the
// curve's local frame at that point. Nothing else about the mesh changes,
// which is why this survives arbitrary topology.
//
// The curve is one cubic bezier from the plug's root to the socket:
//
//     p0 = plug root position          p3 = socket position
//     p1 = p0 + plugForward * handle   p2 = p3 - socketForward * handle
//
// The handle length is what makes it feel physical. When the socket is
// far away, the handles are stretched enormously, which drags the curve
// almost straight along the plug's own forward — so a distant socket does
// not visibly bend anything. As the socket approaches, the handles
// shorten toward half the gap, and the curve becomes a real S-bend that
// meets the socket head-on along its axis. Approach, engagement and
// alignment all fall out of that one interpolation.
//
// Engagement range is measured in plug lengths, not metres, so a small
// plug engages at a small distance: full bend within ~1.2 lengths, faded
// to nothing by ~1.6.
//
// Walking the curve needs ARC LENGTH, not the bezier parameter t — they
// are not proportional, and using t directly would bunch the mesh up
// where the curve bends hardest. So the curve is sampled in fixed steps,
// chord lengths are accumulated, and we stop at the step that passes the
// distance we want, interpolating inside it.
//
// The frame at each sample needs an up-vector, and the honest way to get
// one is parallel transport: take the previous up, project out any
// component along the new forward, renormalise. Recomputing up from a
// fixed world axis instead would make the plug spin on its own axis as it
// bends, which reads as a twist that nobody authored.
//
// Two behaviours at the end of the curve:
//
//   * OVERRUN — a vertex whose Z is longer than the curve keeps going in
//     a straight line along the final forward. Without it, a plug longer
//     than the gap would pile up at the socket.
//   * HOLE COLLAPSE — when the socket is a hole rather than a ring, the
//     part of the plug past the socket has its X/Y offsets scaled toward
//     zero, so the tip tapers to a point instead of poking out the far
//     side. The taper happens over the last 5-10% of the plug's length.
//
// Finally the whole thing is a lerp against the original vertex, scaled
// by the per-vertex active weight from the bake. That weight is a MASK
// FALLOFF and is genuinely fractional near the base — it must be
// multiplied, never thresholded, or the mesh tears away from the body
// instead of feathering into it.
//
#ifndef YAPS_DEFORM_INCLUDED
#define YAPS_DEFORM_INCLUDED

#include "yaps_props.cginc"

#define YAPS_WALK_STEPS 48

struct YapsFrame
{
    float3 position;
    float3 forward;
    float3 up;
};

struct YapsVertex
{
    float3 position;
    float3 normal;
    float3 tangent;
    float active;
};

// --- small helpers ---------------------------------------------------

inline bool YapsIsZero(float3 v)
{
    return dot(v, v) < 1e-12;
}

inline float3 YapsSafeNormalize(float3 v, float3 fallback)
{
    float lengthSq = dot(v, v);
    return lengthSq < 1e-12 ? fallback : v * rsqrt(lengthSq);
}

// Parallel transport: the part of `up` that is perpendicular to
// `forward`. Keeps the frame from rolling as the curve turns.
inline float3 YapsPerpendicular(float3 forward, float3 up)
{
    float3 flattened = up - forward * dot(up, forward);
    if (YapsIsZero(flattened))
    {
        // Degenerate only when up and forward are parallel; any
        // perpendicular will do, so take the least-aligned world axis.
        float3 axis = abs(forward.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0);
        flattened = axis - forward * dot(axis, forward);
    }
    return YapsSafeNormalize(flattened, float3(0, 1, 0));
}

inline float YapsRamp(float value, float from, float to)
{
    return saturate((value - from) / max(to - from, 1e-6));
}

// --- reading the bake ------------------------------------------------

// Each float was stored as the four bytes of one RGBA32 pixel, red
// holding the least significant byte. Load() gives them back as 0..1, so
// scale to bytes and reassemble. This must be an exact integer load —
// any filtering or sRGB conversion would corrupt the bit pattern.
inline uint YapsPackToUint(float4 rgba)
{
    uint4 bytes = (uint4) round(saturate(rgba) * 255.0);
    return bytes.r | (bytes.g << 8) | (bytes.b << 16) | (bytes.a << 24);
}

inline float YapsReadFloat(uint index)
{
    uint width = (uint) round(abs(1.0 / _YAPS_Bake_TexelSize.x));
    uint2 texel = uint2(index % width, index / width);
    return asfloat(YapsPackToUint(_YAPS_Bake.Load(int3(texel, 0))));
}

inline float3 YapsReadFloat3(uint index)
{
    return float3(YapsReadFloat(index), YapsReadFloat(index + 1), YapsReadFloat(index + 2));
}

YapsVertex YapsReadBaked(uint vertexId)
{
    YapsVertex baked;
    // One header float, then ten per vertex.
    uint at = 1 + vertexId * 10;
    baked.position = YapsReadFloat3(at);
    baked.normal = YapsReadFloat3(at + 3);
    baked.tangent = YapsReadFloat3(at + 6);
    baked.active = YapsReadFloat(at + 9);

    // Baked vectors live in plug-local space, so their length is the
    // plug's scale rather than 1. Measured across a real corpus this
    // ranges from 0.38 to 30, so assuming unit vectors is not survivable.
    float inverseScale = 1.0 / max(_YAPS_BakeScale, 1e-6);
    baked.normal *= inverseScale;
    baked.tangent *= inverseScale;

    // A vertex behind the base is not part of the shaft.
    if (baked.position.z < 0) baked.active = 0;
    return baked;
}

// --- the curve -------------------------------------------------------

inline float3 YapsBezier(float3 p0, float3 p1, float3 p2, float3 p3, float t)
{
    float u = 1 - t;
    return u * u * u * p0
         + 3 * u * u * t * p1
         + 3 * u * t * t * p2
         + t * t * t * p3;
}

inline float3 YapsBezierTangent(float3 p0, float3 p1, float3 p2, float3 p3, float t)
{
    float u = 1 - t;
    return 3 * u * u * (p1 - p0)
         + 6 * u * t * (p2 - p1)
         + 3 * t * t * (p3 - p2);
}

// Walk `distance` metres of arc length along the curve. Returns the frame
// there, and how much distance was left over when the curve ran out.
YapsFrame YapsWalk(float3 p0, float3 p1, float3 p2, float3 p3,
                   float wantedLength, float3 startUp, out float leftOver)
{
    YapsFrame frame;
    frame.position = p0;
    frame.forward = YapsSafeNormalize(YapsBezierTangent(p0, p1, p2, p3, 0), float3(0, 0, 1));
    frame.up = YapsPerpendicular(frame.forward, startUp);
    leftOver = 0;

    if (wantedLength <= 0)
    {
        return frame;
    }

    float travelled = 0;
    float3 previousPoint = p0;
    float previousT = 0;
    float previousTravelled = 0;
    float3 carriedUp = frame.up;

    [loop]
    for (int step = 1; step <= YAPS_WALK_STEPS; step++)
    {
        float t = (float) step / YAPS_WALK_STEPS;
        // Not "point": HLSL reserves it as a geometry-shader primitive type.
        float3 curvePoint = YapsBezier(p0, p1, p2, p3, t);
        float3 forward = YapsSafeNormalize(YapsBezierTangent(p0, p1, p2, p3, t), frame.forward);
        float3 up = YapsPerpendicular(forward, carriedUp);
        travelled += length(curvePoint - previousPoint);

        if (wantedLength <= travelled)
        {
            // Land between this sample and the last one. Interpolating t
            // by the distance fraction is an approximation, but across a
            // step this small the curve is effectively straight.
            float fraction = saturate((wantedLength - previousTravelled)
                / max(travelled - previousTravelled, 1e-6));
            float landedT = lerp(previousT, t, fraction);
            frame.position = YapsBezier(p0, p1, p2, p3, landedT);
            frame.forward = YapsSafeNormalize(
                YapsBezierTangent(p0, p1, p2, p3, landedT), forward);
            frame.up = YapsPerpendicular(frame.forward, lerp(carriedUp, up, fraction));
            return frame;
        }

        previousT = t;
        previousTravelled = travelled;
        previousPoint = curvePoint;
        carriedUp = up;
    }

    // Ran off the end: report the shortfall so the caller can extend
    // straight ahead, or collapse the tip if the socket is a hole.
    leftOver = max(wantedLength - travelled, 0);
    frame.position = p3;
    frame.forward = YapsSafeNormalize(YapsBezierTangent(p0, p1, p2, p3, 1), frame.forward);
    frame.up = YapsPerpendicular(frame.forward, carriedUp);
    return frame;
}

// --- diagnostics -----------------------------------------------------

// Reports what the deform is actually seeing, so a plug that refuses to
// move can say why instead of being guessed at. Returns:
//   x  the baked active weight  (0 here means the texture read failed
//      or this vertex is masked out)
//   y  engagement 0..1          (0 means the socket is out of range)
//   z  the final blend          (0 means no deform will be applied)
//   w  baked Z in metres        (0 everywhere means the bake is not
//      being read at all — the single most useful signal)
float4 YapsDebug(uint vertexId)
{
    if (vertexId >= (uint) max(_YAPS_VertexCount, 0)) return float4(0, 0, 0, 0);
    YapsVertex baked = YapsReadBaked(vertexId);

    float3 rootWorld = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
    float worldLength = _YAPS_Length * _YAPS_BakeScale;
    float gap = length(_YAPS_SocketPos.xyz - rootWorld);
    float engage = 1 - YapsRamp(gap, worldLength * 1.2, worldLength * 1.6);
    float enabled = saturate(_YAPS_Enabled) * saturate(_YAPS_SocketFlags.x);
    float blend = YapsRamp(engage, 0, 0.2) * baked.active * enabled;
    return float4(baked.active, engage, blend, baked.position.z);
}

// --- the deform ------------------------------------------------------

void YapsDeform(inout float3 position, inout float3 normal, inout float3 tangent, uint vertexId)
{
    float enabled = saturate(_YAPS_Enabled) * saturate(_YAPS_SocketFlags.x);
    if (enabled <= 0) return;
    if (vertexId >= (uint) max(_YAPS_VertexCount, 0)) return;

    YapsVertex baked = YapsReadBaked(vertexId);
    if (baked.active <= 0) return;

    float3 originalPosition = position;
    float3 originalNormal = normal;
    float3 originalTangent = tangent;

    // The rod's start: where the plug is, pointing where it points.
    float3 rootWorld = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
    float3 rootForward = YapsSafeNormalize(
        mul((float3x3) unity_ObjectToWorld, float3(0, 0, 1)), float3(0, 0, 1));
    float3 rootUp = YapsPerpendicular(rootForward,
        mul((float3x3) unity_ObjectToWorld, float3(0, 1, 0)));

    float3 socketWorld = _YAPS_SocketPos.xyz;
    float3 socketForward = YapsSafeNormalize(_YAPS_SocketForward.xyz, rootForward);

    float worldLength = _YAPS_Length * _YAPS_BakeScale;
    float3 toSocket = socketWorld - rootWorld;
    float gap = length(toSocket);

    // Handles: stretched far out when the socket is distant, which drags
    // the curve straight; shortened to half the gap when it is close,
    // which turns it into a real bend that arrives along the socket axis.
    float engage = 1 - YapsRamp(gap, worldLength * 1.2, worldLength * 1.6);
    float handle = lerp(worldLength * 5, gap * 0.5, engage);

    float3 p0 = rootWorld;
    float3 p1 = rootWorld + rootForward * handle;
    float3 p2 = socketWorld - socketForward * handle;
    float3 p3 = socketWorld;

    float leftOver;
    YapsFrame frame = YapsWalk(p0, p1, p2, p3, max(baked.position.z, 0), rootUp, leftOver);

    // Past the end of the curve. A hole swallows the remainder and tapers
    // the tip to a point; a ring lets it carry straight on through.
    float radius = 1;
    bool isHole = _YAPS_SocketFlags.y > 0.5;
    if (leftOver > 0 && isHole)
    {
        float taperFrom = worldLength * 0.05;
        float taperTo = worldLength * 0.10;
        radius = 1 - YapsRamp(leftOver, taperFrom, taperTo);
        if (_YAPS_Overrun < 0.5) leftOver = 0;
    }
    frame.position += frame.forward * leftOver;

    float3 right = cross(frame.up, frame.forward);

    // Re-hang the vertex off the curve's frame, and blend in by how
    // engaged the bend is and how much this vertex belongs to the shaft.
    float blend = YapsRamp(engage, 0, 0.2) * baked.active * enabled;

    float3 deformed = frame.position
        + right * (baked.position.x * radius)
        + frame.up * (baked.position.y * radius);
    position = lerp(originalPosition, mul(unity_WorldToObject, float4(deformed, 1)).xyz, blend);

    if (!YapsIsZero(baked.normal))
    {
        float3 worldNormal = right * baked.normal.x
                           + frame.up * baked.normal.y
                           + frame.forward * baked.normal.z;
        normal = lerp(originalNormal,
            mul((float3x3) unity_WorldToObject, worldNormal), blend);
    }
    if (!YapsIsZero(baked.tangent))
    {
        float3 worldTangent = right * baked.tangent.x
                            + frame.up * baked.tangent.y
                            + frame.forward * baked.tangent.z;
        tangent = lerp(originalTangent,
            mul((float3x3) unity_WorldToObject, worldTangent), blend);
    }
}

#endif
