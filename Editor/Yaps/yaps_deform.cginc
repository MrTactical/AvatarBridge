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
#include "yaps_resolve.cginc"

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

    // Blendshapes. The vertex arriving here already has them applied, but
    // this bake is the REST pose — so on a plug with a length or girth
    // slider the two describe different meshes, and every number taken
    // from the bake is measured against a shape the vertex has left.
    // Rebuild the rest pose the vertex actually came from by adding back
    // what each slider contributed.
    //
    // Blocks of nine floats a vertex follow the base block, which is why
    // the vertex count has to be on the material: block s starts at
    // 1 + count*10 + s*count*9.
    uint shapeCount = (uint) max(_YAPS_ShapeCount, 0);
    if (shapeCount > 0)
    {
        uint total = (uint) max(_YAPS_VertexCount, 0);
        uint block = 1 + total * 10;
        [loop]
        for (uint s = 0; s < shapeCount; s++)
        {
            float weight = YapsShapeWeight(s);
            if (abs(weight) > 0.0001)
            {
                uint shapeAt = block + s * total * 9 + vertexId * 9;
                baked.position += YapsReadFloat3(shapeAt) * weight;
                baked.normal += YapsReadFloat3(shapeAt + 3) * weight;
                baked.tangent += YapsReadFloat3(shapeAt + 6) * weight;
            }
        }
    }

    // Baked vectors carry whatever length the plug's own scale gave them
    // — measured across a real corpus, anywhere from 0.38 to 30. Nothing
    // here cares about their length: they serve as directions, both for
    // recovering the plug's frame and for rewriting the output normal. So
    // normalise and stop tracking the scale at all. Trying to divide by a
    // recorded scale factor only works while the scale is uniform, and a
    // non-uniformly scaled plug makes the magnitude direction-dependent.
    baked.normal = YapsSafeNormalize(baked.normal, float3(0, 0, 1));
    baked.tangent = YapsSafeNormalize(baked.tangent, float3(1, 0, 0));

    // A vertex behind the base is not part of the shaft.
    if (baked.position.z < 0) baked.active = 0;
    return baked;
}

// --- recovering the plug's own frame ---------------------------------
//
// The curve starts at the plug. On the test rig the plug is its own
// object, so `unity_ObjectToWorld` IS that frame. On a real avatar it is
// not: the plug is part of a SkinnedMeshRenderer whose transform is the
// avatar root, while the plug itself is carried by a bone. SPS solves
// this by parenting a marker to the bone and publishing its transform
// through the screen atlas — a route we deliberately do not have.
//
// So recover the frame from the vertex itself. Skinning hands the vertex
// shader a position, a normal and a tangent already moved into renderer
// space; the bake holds the same three in plug space. Two independent
// directions are enough to pin a rotation completely — build an
// orthonormal basis from each pair and the rotation is the one that maps
// one onto the other. The position then gives the translation.
//
// This needs no extra uniforms and follows the bone for free, because it
// reads the result of the very skinning that moved the bone. Its limit is
// vertices blended across several bones, where the mapping is no longer a
// single rigid transform — but that is the base of the shaft, where the
// mask weight is already feathering the deform out anyway.

struct YapsBasis
{
    float3 a1; float3 a2; float3 a3;   // from the bake
    float3 b1; float3 b2; float3 b3;   // from the skinned vertex
    bool valid;
};

YapsBasis YapsBuildBasis(float3 bakedNormal, float3 bakedTangent,
                         float3 skinnedNormal, float3 skinnedTangent)
{
    YapsBasis basis;
    basis.a1 = 0; basis.a2 = 0; basis.a3 = 0;
    basis.b1 = 0; basis.b2 = 0; basis.b3 = 0;
    basis.valid = false;

    if (YapsIsZero(bakedNormal) || YapsIsZero(bakedTangent)) return basis;
    if (YapsIsZero(skinnedNormal) || YapsIsZero(skinnedTangent)) return basis;

    basis.a1 = normalize(bakedNormal);
    float3 aFlat = bakedTangent - basis.a1 * dot(bakedTangent, basis.a1);
    basis.b1 = normalize(skinnedNormal);
    float3 bFlat = skinnedTangent - basis.b1 * dot(skinnedTangent, basis.b1);

    // Normal and tangent parallel means the pair carries only one
    // direction, which cannot pin a rotation. Bail rather than invent one.
    if (dot(aFlat, aFlat) < 1e-8 || dot(bFlat, bFlat) < 1e-8) return basis;

    basis.a2 = normalize(aFlat);
    basis.a3 = cross(basis.a1, basis.a2);
    basis.b2 = normalize(bFlat);
    basis.b3 = cross(basis.b1, basis.b2);
    basis.valid = true;
    return basis;
}

// Rotate a plug-space direction into renderer space.
inline float3 YapsRotate(YapsBasis basis, float3 v)
{
    float3 c = float3(dot(basis.a1, v), dot(basis.a2, v), dot(basis.a3, v));
    return basis.b1 * c.x + basis.b2 * c.y + basis.b3 * c.z;
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
    // Same scale correction as the real deform, so the debug view reports
    // the engagement the plug is actually using rather than the one it
    // would use at 1x.
    float worldLength = _YAPS_Length * _YAPS_BakeScale
        * length(mul((float3x3) unity_ObjectToWorld, float3(0, 0, 1)));
    YapsSocket socket = YapsResolveSocket(rootWorld,
        YapsSafeNormalize(mul((float3x3) unity_ObjectToWorld, float3(0, 0, 1)), float3(0, 0, 1)),
        YapsSafeNormalize(mul((float3x3) unity_ObjectToWorld, float3(0, 1, 0)), float3(0, 1, 0)),
        worldLength);
    float gap = length(socket.position - rootWorld);
    float engage = 1 - YapsRamp(gap, worldLength * 1.2, worldLength * 1.6);
    float enabled = saturate(_YAPS_Enabled) * socket.engaged;
    float blend = YapsRamp(engage, 0, 0.2) * baked.active * enabled;
    return float4(baked.active, engage, blend, baked.position.z);
}

// WHO resolved the socket, for the "Resolved by" debug view: x is the tier
// (0 nobody, 1 the contact channel, 2 a marker light, 3 the player-globals
// floor), y the engagement, so the view can dim an answer that resolved but
// did not engage.
//
// The distinction this buys: a plug bending near a socket does not say who
// bent it, and a stray marker light — a lit socket nearby, the holder's own
// avatar wearing one — reads exactly like a working contact channel. A full
// day went to a channel that had never worked because the lights kept
// covering for it; one glance at this view would have named the truth.
//
// Uses the renderer's transform for the frame, like YapsDebug above — for a
// prop that IS the frame, and for a skinned plug the tier does not depend
// on the frame anyway, only the ranges do, slightly.
float2 YapsDebugTier()
{
    float3 rootWorld = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
    float worldLength = _YAPS_Length * _YAPS_BakeScale
        * length(mul((float3x3) unity_ObjectToWorld, float3(0, 0, 1)));
    YapsSocket socket = YapsResolveSocket(rootWorld,
        YapsSafeNormalize(mul((float3x3) unity_ObjectToWorld, float3(0, 0, 1)), float3(0, 0, 1)),
        YapsSafeNormalize(mul((float3x3) unity_ObjectToWorld, float3(0, 1, 0)), float3(0, 1, 0)),
        worldLength);
    return float2(socket.tier, socket.engaged);
}

// --- the deform ------------------------------------------------------

void YapsDeform(inout float3 position, inout float3 normal, inout float3 tangent, uint vertexId)
{
    if (vertexId >= (uint) max(_YAPS_VertexCount, 0)) return;

    YapsVertex baked = YapsReadBaked(vertexId);
    if (baked.active <= 0) return;

    float3 originalPosition = position;
    float3 originalNormal = normal;
    float3 originalTangent = tangent;

    // The rod's start: where the plug is, pointing where it points.
    float3 rootLocal = float3(0, 0, 0);
    float3 forwardLocal = float3(0, 0, 1);
    float3 upLocal = float3(0, 1, 0);

    if (_YAPS_FrameFromVertex > 0.5)
    {
        // Skinned mesh: the renderer's transform is the avatar root, so
        // ask the vertex where its bone has actually put the plug.
        YapsBasis basis = YapsBuildBasis(baked.normal, baked.tangent,
                                         originalNormal, originalTangent);
        if (basis.valid)
        {
            rootLocal = originalPosition - YapsRotate(basis, baked.position);
            forwardLocal = YapsRotate(basis, float3(0, 0, 1));
            upLocal = YapsRotate(basis, float3(0, 1, 0));
        }
    }

    float3 rootWorld = mul(unity_ObjectToWorld, float4(rootLocal, 1)).xyz;
    float3 rootForward = YapsSafeNormalize(
        mul((float3x3) unity_ObjectToWorld, forwardLocal), float3(0, 0, 1));
    float3 rootUp = YapsPerpendicular(rootForward,
        mul((float3x3) unity_ObjectToWorld, upLocal));

    // THE SCALE THE PLUG IS ACTUALLY DRAWN AT.
    //
    // Everything below compares baked measurements against WORLD
    // distances: the gap to a socket, the engagement envelope, the taper,
    // squeeze and bulge reach. The bake is in the renderer's own units, so
    // the two agree only while that renderer sits at 1x — and AvatarBridge
    // ships a height slider, ON BY DEFAULT, that animates m_LocalScale on
    // the avatar ROOT from 0.25x to 4x. That scale lands in this matrix.
    //
    // Left unhandled, a scaled avatar got a plug that engaged at the wrong
    // distance and, worse, kept its baked GIRTH while its length followed
    // the body — because the offsets below are added to unit world vectors,
    // so baked units were being spent as metres. A 2x avatar wore a
    // half-thickness plug. It never showed because the slider had not been
    // moved during any test.
    //
    // Read from the matrix rather than published as a property, so it
    // follows the slider, a world's own scaling, or an author's transform
    // without anyone having to send it anywhere.
    //
    // Applied AFTER the frame recovery above, which works in object space
    // and needs the baked numbers exactly as baked.
    float yapsScale = length(mul((float3x3) unity_ObjectToWorld, float3(0, 0, 1)));
    baked.position *= yapsScale;

    float worldLength = _YAPS_Length * _YAPS_BakeScale * yapsScale;

    // Everything platform-specific happens in here: the discrete channel,
    // protocol lights at contact range, the player globals as a floor.
    // The recovered frame, not the renderer's: on a skinned mesh the
    // renderer sits at the avatar root while a bone carries the plug, and
    // the channel reports the socket in the PLUG's frame.
    YapsSocket socket = YapsResolveSocket(rootWorld, rootForward, rootUp, worldLength);

    float enabled = saturate(_YAPS_Enabled) * socket.engaged;
    if (enabled <= 0) return;

    float3 socketWorld = socket.position;
    float3 toSocket = socketWorld - rootWorld;
    float gap = length(toSocket);

    // A resolved socket may arrive without an axis — the globals have no
    // rotation at all, and a root light can turn up without its front.
    // Aiming along the approach is the honest fallback: it produces a
    // straight arrival rather than an invented direction.
    float3 socketForward = dot(socket.forward, socket.forward) > 1e-6
        ? normalize(socket.forward)
        : YapsSafeNormalize(toSocket, rootForward);

    // You enter a hole from the side you are standing on. A socket whose
    // forward points away from the plug would otherwise make the curve
    // loop around to arrive from behind — a visible hairpin fold — so the
    // axis is flipped to face the approach. This makes the deform
    // independent of which way an author happened to aim the socket,
    // which matters because a converter inherits whatever convention the
    // original avatar used and cannot dictate one.
    if (dot(socketForward, toSocket) < 0)
    {
        socketForward = -socketForward;
    }

    float engage = 1 - YapsRamp(gap, worldLength * 1.2, worldLength * 1.6);

    // IDLE SHRINK, from TPS. A plug nobody is using goes soft, and comes
    // back as a socket approaches. No amount of bend maths substitutes for
    // it: without it a plug is permanently at full mast, which reads wrong
    // the entire time nothing is happening — which is most of the time.
    //
    // Applied to the BAKED position, before the curve walk, so length and
    // girth shrink together and everything downstream follows: the walk
    // measures a shorter rod, and the taper and overrun scale with it.
    //
    // Both default to 1, so an avatar converted before this existed
    // behaves exactly as it did. The engagement used is the SHAPE of the
    // approach rather than the channel's flag, so it eases in with the
    // bend instead of snapping at the moment a socket is detected.
    float idle = 1 - engage;
    baked.position.z *= lerp(1, max(_YAPS_IdleLength, 0.01), idle);
    baked.position.xy *= lerp(1, max(_YAPS_IdleWidth, 0.01), idle);

    // PUMPING and WRIGGLE, from TPS and DPS. Motion the plug makes on its
    // own, so it is not a rigid prop between events.
    //
    // Both scale with how far ALONG the shaft a vertex sits, so the base
    // stays welded to the body and the tip moves most. A uniform offset
    // would slide the whole plug out of its owner.
    //
    // Pumping runs only while ENGAGED — a plug thrusting at nothing is
    // absurd — and wriggle only while IDLE, where it reads as the thing
    // being alive rather than as a stiff rod. They never overlap, so they
    // cannot fight each other.
    float along = saturate(baked.position.z / max(worldLength, 0.0001));

    if (_YAPS_PumpStrength > 0)
    {
        float t = _Time.y * max(_YAPS_PumpSpeed, 0);
        baked.position.z += sin(t) * _YAPS_PumpStrength * worldLength * along * engage;
    }

    if (_YAPS_WriggleStrength > 0)
    {
        float t = _Time.y * max(_YAPS_WriggleSpeed, 0);
        // Two axes out of phase, so it wanders rather than swinging flat.
        baked.position.x += sin(t) * _YAPS_WriggleStrength * worldLength * along * idle;
        baked.position.y += sin(t * 0.7 + 1.3) * _YAPS_WriggleStrength * worldLength * along * idle;
    }

    // Only the PLUG's handle gets the pullout. Stretching it far along the
    // plug's own forward is what holds the shaft straight while the socket
    // is still out of range — the curve's first stretch is then dominated
    // by that one direction.
    //
    // The socket's handle must NOT be stretched with it. Pulling both ends
    // out inflates the whole curve into a loop far longer than the plug,
    // and the plug ends up following the opening arc of that loop, which
    // points nowhere near the socket. Measured on a 0.6 m plug at partial
    // engagement: a 1.8 m curve, and a shaft aimed off into space.
    float approachHandle = gap * 0.5;
    float rootHandle = lerp(worldLength * 5, approachHandle, engage);

    float3 p0 = rootWorld;
    float3 p1 = rootWorld + rootForward * rootHandle;
    float3 p2 = socketWorld - socketForward * approachHandle;
    float3 p3 = socketWorld;

    float leftOver;
    YapsFrame frame = YapsWalk(p0, p1, p2, p3, max(baked.position.z, 0), rootUp, leftOver);

    // Past the end of the curve. A hole swallows the remainder and tapers
    // the tip to a point; a ring lets it carry straight on through.
    float radius = 1;
    bool isHole = socket.isHole > 0.5;
    if (leftOver > 0 && isHole)
    {
        // How far past the hole a vertex may travel before it starts
        // narrowing, and how far before it has closed to nothing. As
        // fractions of plug length, so a big plug and a small one taper
        // over the same proportion of themselves. Exposed because how
        // abruptly a hole closes is taste, not physics.
        float taperFrom = worldLength * _YAPS_TaperStart;
        float taperTo = worldLength * max(_YAPS_TaperEnd, _YAPS_TaperStart + 0.001);

        // CLAMP FIRST. Without this, a socket closer than the plug is long
        // sends every vertex past it flying forward by its own excess —
        // and since the taper has already pulled the radius to zero, they
        // collapse onto a line and render as a flat twisted ribbon. A hole
        // is a hole: nothing goes more than a tenth of a plug-length past
        // it, and everything beyond simply piles up there and closes off.
        leftOver = min(leftOver, taperTo);
        radius = 1 - YapsRamp(leftOver, taperFrom, taperTo);
        if (_YAPS_Overrun < 0.5) leftOver = 0;
    }
    // SQUEEZE and BULGE, both measured from the entry — the point along
    // the shaft that is level with the socket. A vertex at baked z == gap
    // is exactly in the opening; less than that is still outside, more is
    // through.
    //
    // Squeeze narrows the shaft where the socket grips it, bulge swells it
    // just short of the opening where the flesh piles up. Both are DPS and
    // TPS ideas and both are what makes an entry read as tight rather than
    // as a rod sliding through a hoop.
    //
    // Only while engaged, and faded by engagement, so an idle plug is
    // never pinched by a socket that is not there.
    float entry = baked.position.z - gap;
    float grip = engage * step(0.5, enabled);

    if (_YAPS_Squeeze > 0)
    {
        // Deepest AT the opening, easing off either side of it.
        float reach = max(_YAPS_SqueezeDistance, 0.001) * worldLength;
        float near = 1 - saturate(abs(entry) / reach);
        radius *= 1 - _YAPS_Squeeze * near * grip;
    }

    if (_YAPS_Bulge > 0)
    {
        // Just OUTSIDE the opening only: entry < 0. Swelling on the far
        // side would be the shaft growing inside whatever it entered.
        float reach = max(_YAPS_BulgeDistance, 0.001) * worldLength;
        float before = saturate(-entry / reach);
        float shape = before * (1 - before) * 4;   // a hump, zero at both ends
        radius *= 1 + _YAPS_Bulge * shape * grip;
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
