// YAPS deform core, for ChilloutVR.
//
// Inspired by VRCFury's SPS. Written from a description of the behaviour,
// never their source. See docs/YAPS-CLEAN-ROOM.md.
//
// The plug is baked in its own local space, +Z base to tip. A vertex is
// not a point in space, it is a point on a rod:
//
//     * Z is how far ALONG the rod it sits
//     * X and Y are how far OFF the centre line
//
// So bending is: swap the straight rod for a curved one, walk Z metres
// along it, re-hang the X/Y offset off the frame there. Nothing else
// changes, which is why this survives any topology.
//
// The curve is one cubic bezier, root to socket:
//
//     p0 = plug root                   p3 = socket
//     p1 = p0 + plugForward * handle   p2 = p3 - socketForward * handle
//
// The handle length is what makes it feel physical. Far off, the handles
// stretch enormously and drag the curve straight along the plug's own
// forward. As the socket nears they shorten toward half the gap and the
// curve becomes an S-bend meeting it head-on. Approach, engagement and
// alignment all fall out of that one interpolation.
//
// Range is in plug lengths, not metres: full bend by 1.2, gone by 1.6.
//
// The walk needs ARC LENGTH, never the bezier t. They are not
// proportional, and t bunches the mesh where the curve bends hardest.
//
// The frame's up comes from parallel transport. Recomputing it from a
// fixed world axis makes the plug spin on its own axis as it bends.
//
// Two behaviours at the end of the curve:
//
//   * OVERRUN, a vertex past the curve carries straight on. Without it a
//     plug longer than the gap piles up at the socket.
//   * HOLE COLLAPSE, past a hole the X/Y offsets scale toward zero, so
//     the tip tapers instead of poking out the far side.
//
// The result is a lerp against the original vertex by the baked active
// weight. That weight is a MASK FALLOFF, genuinely fractional near the
// base. Multiply it, never threshold it, or the mesh tears off the body.
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
        // Only when up and forward are parallel. Any perpendicular will
        // do, so take the least-aligned world axis.
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

// Each float was four bytes of one RGBA32 pixel, red the least
// significant. An exact integer load: filtering or sRGB corrupts it.
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

    // Blendshapes. The vertex arriving here has them applied, but the
    // bake is the REST pose, so on a plug with a length or girth slider
    // the two describe different meshes. Add back what each contributed.
    //
    // Blocks of nine floats a vertex follow the base block, which is why
    // the vertex count has to be on the material.
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

    // Baked vectors carry the plug's own scale, seen anywhere from 0.38
    // to 30 in real content. Nothing here wants their length, only their
    // direction, so normalise and stop tracking scale. Dividing by a
    // recorded factor only works while that scale is uniform.
    baked.normal = YapsSafeNormalize(baked.normal, float3(0, 0, 1));
    baked.tangent = YapsSafeNormalize(baked.tangent, float3(1, 0, 0));

    // A vertex behind the base is not part of the shaft.
    if (baked.position.z < 0) baked.active = 0;
    return baked;
}

// --- recovering the plug's own frame ---------------------------------
//
// The curve starts at the plug. Where the plug is its own object,
// unity_ObjectToWorld IS that frame. On a real avatar it is not: the
// renderer's transform is the avatar root and a bone carries the plug.
//
// So recover the frame from the vertex. Skinning hands over a position,
// normal and tangent already in renderer space, and the bake holds the
// same three in plug space. Two independent directions pin a rotation
// completely, and the position gives the translation.
//
// No extra uniforms, and it follows the bone for free. Its limit is
// vertices blended across several bones, which is the base of the shaft,
// where the mask weight is already feathering the deform out.

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

    // Parallel normal and tangent carry one direction, which cannot pin
    // a rotation. Bail rather than invent one.
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

// Curve length as the mean of chord and control net. The two bracket the
// true length, and the average is within about a percent here.
//
// A walk would be exact, but the chain needs a length for EVERY segment on
// EVERY vertex just to pick one. Four walks to choose one walk is the
// wrong trade.
inline float YapsCurveLength(float3 p0, float3 p1, float3 p2, float3 p3)
{
    float net = length(p1 - p0) + length(p2 - p1) + length(p3 - p2);
    return (length(p3 - p0) + net) * 0.5;
}

// Walk `distance` metres of arc along the curve. Returns the frame there
// and what distance was left when the curve ran out.
//
// startForward: where the PLUG points, for a curve with no direction of
// its own. A socket pushed onto the root collapses all four control
// points, so every tangent is zero. The fallback then ran out at WORLD
// forward, and a ring, which never clamps the remainder, extended every
// vertex along that axis. The plug flattened into a line.
YapsFrame YapsWalk(float3 p0, float3 p1, float3 p2, float3 p3,
                   float wantedLength, float3 startUp, float3 startForward,
                   out float leftOver)
{
    YapsFrame frame;
    frame.position = p0;
    frame.forward = YapsSafeNormalize(YapsBezierTangent(p0, p1, p2, p3, 0), startForward);
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
            // Land between this sample and the last. Interpolating t by
            // the distance fraction is fine across a step this small.
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

// What the deform is actually seeing, so a plug that refuses to move can
// say why. Returns:
//   x  baked active weight  (0 means the read failed, or it is masked out)
//   y  engagement 0..1      (0 means the socket is out of range)
//   z  the final blend      (0 means no deform will be applied)
//   w  baked Z in metres    (0 everywhere means the bake is not read)
float4 YapsDebug(uint vertexId)
{
    if (vertexId >= (uint) max(_YAPS_VertexCount, 0)) return float4(0, 0, 0, 0);
    YapsVertex baked = YapsReadBaked(vertexId);

    float3 rootWorld = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
    // Same scale correction as the real deform, so this reports the
    // engagement the plug is using rather than the one at 1x.
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

// WHO resolved the socket, for the "Resolved by" view. x is the tier
// (0 nobody, 1 the channel, 2 a marker light, 3 the screen atlas), y the
// engagement, so the view can dim an answer that resolved but did not
// engage.
//
// A plug bending near a socket does not say who bent it, and a stray
// marker light reads exactly like a working channel.
//
// Uses the renderer's transform, like YapsDebug. For a skinned plug the
// tier does not depend on the frame, only the ranges do.
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

    // The plug scaled by its bones, which the skinned vertex already is
    // and the bake is not. 1 when nothing scales it. Before the recovery,
    // so a scaled vertex still lands on its own root.
    baked.position *= float3(max(_YAPS_BakeGirth, 0.0001), max(_YAPS_BakeGirth, 0.0001), max(_YAPS_BakeScale, 0.0001));

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
        // ask the vertex where its bone has put the plug.
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
    // distances. The bake is in renderer units, so the two agree only at
    // 1x, and the height slider animates m_LocalScale on the avatar ROOT
    // from 0.25x to 4x, which lands in this matrix.
    //
    // Left unhandled, a scaled avatar engaged at the wrong distance and
    // kept its baked GIRTH while its length followed the body, since the
    // offsets below are added to unit world vectors. A 2x avatar wore a
    // half-thickness plug.
    //
    // Read from the matrix rather than published, so it follows the
    // slider, a world's own scaling or an author's transform.
    //
    // AFTER the frame recovery above, which works in object space and
    // needs the baked numbers exactly as baked.
    float yapsScale = length(mul((float3x3) unity_ObjectToWorld, float3(0, 0, 1)));
    baked.position *= yapsScale;

    float worldLength = _YAPS_Length * _YAPS_BakeScale * yapsScale;

    // Everything platform-specific happens in here. A plug only ever
    // bends toward a SOCKET, never toward a body.
    //
    // The recovered frame, never the renderer's: a bone carries the plug,
    // and the channel reports the socket in the PLUG's frame.
    YapsSocket socket = YapsResolveSocket(rootWorld, rootForward, rootUp, worldLength);

    // THE "RESOLVED BY" VIEW.
    //
    // A plug bending near a socket does not say WHO bent it, and a stray
    // marker light reads exactly like a working contact channel.
    //
    // It cannot be a colour. The patcher edits a host shader's VERTEX
    // stage and nothing else, so there is no fragment left to paint in.
    // Every host shader shares where the vertices go, so the answer is
    // given as LENGTH:
    //
    //     a quarter    nobody resolved it
    //     a half       the contact channel
    //     three parts  a marker light
    //     full         the screen atlas
    //
    // Straight, unbent and unengaged, so length is the only thing moving.
    // Before the enabled test on purpose: "nobody" is an answer, and an
    // early return would hide the most important case.
    if (_YAPS_Debug >= 0.5)
    {
        float shown;
        if (_YAPS_Debug < 1.5)
        {
            shown = socket.tier < 0.5 ? 0.25
                  : (socket.tier < 1.5 ? 0.50
                  : (socket.tier < 2.5 ? 0.75 : 1.0));
        }
        else if (_YAPS_Debug >= 5.5)
        {
            // WHICH TARGET this camera draws into, and whether the atlas
            // could have been on it. Every other view describes the plug.
            // This one describes the CAMERA, because the same plug answers
            // differently in the view, a mirror and the self portrait.
            //
            // A tenth: the target is too small, so nothing was painted.
            // Four tenths: the grab is a different size from this target,
            // so the plug is reading somebody else's screen.
            // Seven tenths: right target, and not one cell reported.
            // Writer and reader address different pixels.
            // Full: the transport is on this camera.
            float2 grabPx = _YAPS_Atlas_TexelSize.zw;
            bool sameTarget = all(abs(grabPx - _ScreenParams.xy) < 1.5);
            shown = !YapsAtlasFits() ? 0.10
                  : (!sameTarget ? 0.40
                  : (socket.atlasHeaders < 0.5 ? 0.70 : 1.0));
        }
        else if (_YAPS_Debug >= 4.5)
        {
            // WHAT THE ATLAS READ, before anything was decoded from it.
            // Every other view describes an answer. This one describes the
            // raw material, because "nothing resolved" has two opposite
            // causes needing opposite fixes.
            //
            // FOUR STEPS, never a ratio. One socket occupies one cell of
            // the twenty-seven read, so a ratio put a working transport
            // three percent of plug length from a dead one.
            //
            // A tenth: no cell reported. Nothing published within reach,
            // the grab is empty, or reader and writer address different
            // pixels.
            // A third: a cell reported but no payload matched its tag. The
            // slot was somebody else's, or the tag does not survive.
            // Two thirds: a payload matched and the kind test or reach
            // threw it away. The transport is healthy, the geometry is not.
            // Full: a socket came back.
            shown = socket.atlasHeaders < 0.5 ? 0.10
                  : (socket.atlasHits < 0.5 ? 0.33
                  : (socket.tier < 2.5 ? 0.66 : 1.0));
        }
        else if (_YAPS_Debug >= 3.5)
        {
            // THE SOCKET'S FACING, against the plug's own forward. Full
            // means it faces the way the plug points, half is square
            // across, nothing is straight back. The channel sends no
            // rotation, only a second point, so this value exists on the
            // channel route alone.
            //
            // Measured against the CHANNEL'S frame, never the vertex's.
            // The first version used rootForward, which is recovered per
            // vertex, so a plug spanning a skeleton showed thousands of
            // answers at once and read as a flat half. That is an average,
            // not a measurement.
            float3 reference = rootForward;
            if (dot(_YAPS_ChannelForward.xyz, _YAPS_ChannelForward.xyz) > 1e-8)
            {
                reference = YapsSafeNormalize(
                    mul((float3x3) unity_ObjectToWorld, _YAPS_ChannelForward.xyz), rootForward);
            }
            // A zero forward is an honest "nothing believed", and reads as
            // exactly half so it cannot be taken for either end.
            // A floor, because zero is a correct reading that looked like a
            // catastrophe: a socket facing straight back scores 0 and the
            // plug collapsed to nothing.
            float3 face = socket.forward;
            float facing = dot(face, face) > 1e-6
                ? saturate(dot(normalize(face), reference) * 0.5 + 0.5)
                : 0.5;   // nothing believed, and it sits deliberately mid
            shown = lerp(0.1, 1.0, facing);
        }
        else if (_YAPS_Debug >= 2.5)
        {
            // ENGAGEMENT, the switch itself. Who found the socket and how
            // far off it is can both be steady while THIS collapses, and
            // then the deform stops dead and the plug springs back, which
            // is the whole appearance of a pop.
            //
            // A tenth at zero, so "not engaged" is a visible stub.
            shown = lerp(0.1, 1.0, saturate(socket.engaged));
        }
        else
        {
            // GAP TO THE SOCKET, as a fraction of plug length. It answers
            // whether the socket being aimed at MOVES: two lights of the
            // same tier swapping places are invisible to the view above,
            // since both read as a marker light.
            //
            // Smooth shortening is the geometry behaving. A JUMP is a
            // different answer, and the frame it jumps on is the one to
            // explain.
            //
            // Full length when nobody resolved. Zero would read as "the
            // socket is exactly on the root", the opposite of nothing.
            shown = socket.tier < 0.5
                ? 1.0
                : saturate(length(socket.position - rootWorld) / max(worldLength, 1e-4));
        }
        float3 right = cross(rootUp, rootForward);
        float3 at = rootWorld
                  + right * baked.position.x
                  + rootUp * baked.position.y
                  + rootForward * (baked.position.z * shown);
        position = mul(unity_WorldToObject, float4(at, 1)).xyz;
        return;
    }

    float enabled = saturate(_YAPS_Enabled) * socket.engaged;
    if (enabled <= 0) return;

    float3 socketWorld = socket.position;
    float3 toSocket = socketWorld - rootWorld;
    float gap = length(toSocket);

    // MINIMUM SOCKET DISTANCE, from TPS. A socket against the root asks
    // the curve to reach behind where the shaft begins, and the bezier
    // answers with a hairpin. Hold it off at a floor instead.
    float minimumGap = max(_YAPS_MinimumSocketDistance, 0) * worldLength;
    if (gap < minimumGap && gap > 1e-5)
    {
        socketWorld = rootWorld + toSocket * (minimumGap / gap);
        toSocket = socketWorld - rootWorld;
        gap = minimumGap;
    }

    // A resolved socket may arrive without an axis: a root light can turn
    // up without its front. Aiming along the approach is the honest
    // fallback, a straight arrival rather than an invented direction.
    float3 socketForward = dot(socket.forward, socket.forward) > 1e-6
        ? normalize(socket.forward)
        : YapsSafeNormalize(toSocket, rootForward);

    // You enter a hole from the side you are standing on. A socket facing
    // away would make the curve loop round and arrive from behind, a
    // visible hairpin, so the axis is flipped to meet the approach. A
    // converter inherits the original avatar's convention and cannot
    // dictate one, so the deform must not depend on it.
    if (dot(socketForward, toSocket) < 0)
    {
        socketForward = -socketForward;
    }

    float engage = 1 - YapsRamp(gap, worldLength * 1.2, worldLength * 1.6);

    // IDLE SHRINK, from TPS. A plug nobody is using goes soft. Without it
    // a plug is at full mast the entire time nothing is happening.
    //
    // Applied to the BAKED position, before the walk, so length and girth
    // shrink together and the taper and overrun scale with it.
    //
    // Both default to 1, so older conversions are unchanged. Driven by the
    // SHAPE of the approach, not the channel's flag, so it eases in.
    float idle = 1 - engage;
    baked.position.z *= lerp(1, max(_YAPS_IdleLength, 0.01), idle);
    baked.position.xy *= lerp(1, max(_YAPS_IdleWidth, 0.01), idle);

    // CURVATURE and RECURVATURE, from DPS. A bend along the whole length,
    // and a second of the opposite sign near the tip, so a shaft can sweep
    // up and then hook.
    //
    // A rotation about the plug's x axis by an angle growing along the
    // shaft: a bend, not a shear, so the shaft keeps its length and its
    // normals turn with it. The knob is total turn at the tip, in radians.
    // Recurvature squares the fraction so it gathers at the tip.
    //
    // BEFORE the walk, so the curve carries a shaft that already has its
    // resting shape. That is why it composes with everything after it.
    if (abs(_YAPS_Curvature) > 1e-4 || abs(_YAPS_ReCurvature) > 1e-4)
    {
        float t = saturate(baked.position.z / max(worldLength, 0.0001));
        float turn = _YAPS_Curvature * t - _YAPS_ReCurvature * t * t;
        float s, c;
        sincos(turn, s, c);
        // Rotate in the y/z plane, x is the bend axis. Position and both
        // directions turn together or the lighting lies about the shape.
        float2 yz = baked.position.yz;
        baked.position.yz = float2(yz.x * c - yz.y * s, yz.x * s + yz.y * c);
        float2 nyz = baked.normal.yz;
        baked.normal.yz = float2(nyz.x * c - nyz.y * s, nyz.x * s + nyz.y * c);
        float2 tyz = baked.tangent.yz;
        baked.tangent.yz = float2(tyz.x * c - tyz.y * s, tyz.x * s + tyz.y * c);
    }

    // PUMPING and WRIGGLE, from TPS and DPS. Motion the plug makes alone,
    // so it is not a rigid prop between events.
    //
    // Both scale with how far ALONG the shaft a vertex sits, so the base
    // stays welded to the body. A uniform offset slides the whole plug out
    // of its owner.
    //
    // Pumping runs only while ENGAGED, wriggle only while IDLE, so the two
    // can never fight.
    float along = saturate(baked.position.z / max(worldLength, 0.0001));

    if (_YAPS_PumpStrength > 0)
    {
        float t = _Time.y * max(_YAPS_PumpSpeed, 0);
        // How much of the shaft takes part. Width 1 is one stroke, still
        // zero at the base so it stays welded. Small widths throw only the
        // tip. A power of `along` shapes it: 1 linear, 0.25 quartic.
        float width = clamp(_YAPS_PumpWidth, 0.05, 1);
        float share = pow(along, 1 / width);
        baked.position.z += sin(t) * _YAPS_PumpStrength * worldLength * share * engage;
    }

    if (_YAPS_WriggleStrength > 0)
    {
        float t = _Time.y * max(_YAPS_WriggleSpeed, 0);
        // Two axes out of phase, so it wanders rather than swinging flat.
        baked.position.x += sin(t) * _YAPS_WriggleStrength * worldLength * along * idle;
        baked.position.y += sin(t * 0.7 + 1.3) * _YAPS_WriggleStrength * worldLength * along * idle;
    }

    // Only the PLUG's handle gets the pullout. Stretching it along the
    // plug's own forward is what holds the shaft straight while the socket
    // is out of range.
    //
    // The socket's handle must NOT be stretched with it. Both ends pulled
    // out inflate the curve into a loop far longer than the plug, and the
    // shaft follows its opening arc, nowhere near the socket. A 0.6 m plug
    // made a 1.8 m curve and aimed off into space.
    //
    // BEZIER SMOOTHNESS, from TPS. Scales both handles: below 1 sharper,
    // above 1 a wider arc. 1 is the law the deform has always used.
    float smoothness = max(_YAPS_BezierSmoothness, 0.05);

    float zAlong = max(baked.position.z, 0);

    float approachHandle = gap * 0.5 * smoothness;
    float rootHandle = lerp(worldLength * 5, approachHandle, engage);

    // ENTRANCE STIFFNESS, from DPS. The root handle is held longer along
    // the plug's own forward, so the base stays put and only the far part
    // turns. 0 bends evenly from the root, 1 holds a full length rigid.
    rootHandle = max(rootHandle, saturate(_YAPS_EntranceStiffness) * worldLength * engage);

    float3 p0 = rootWorld;
    float3 p1 = rootWorld + rootForward * rootHandle;
    float3 p2 = socketWorld - socketForward * approachHandle;
    float3 p3 = socketWorld;

    // Where along the shaft the socket has hold of this vertex. Squeeze
    // and bulge measure from it, and on a chain it is this vertex's OWN
    // socket, not the first on the path.
    float gripAt = gap;
    float linkKind = socket.isHole;
    int link = 0;

    // THE CHAIN. A plug resolved through the atlas gets an ORDERED LIST
    // of sockets rather than a winner, each owning a stretch of the shaft.
    // The shaft passes through all of them: a ring mid-shaft and a hole at
    // the tip is one path with two bends, not a choice, and picking a
    // winner is what made a plug flip between them.
    //
    // A vertex is only ever inside ONE link, so the walk stays a single
    // cubic. Its segment LEAVES the previous socket along that socket's
    // own forward, which keeps the joint smooth instead of kinked.
    //
    // MEASURED ON THE CURVE. The resolver orders by CHORD, the right thing
    // to sort by and the wrong thing to hand a shaft: a curve is longer
    // than its chord, so a vertex given the chord stops short while the
    // next segment's first vertex starts on the socket. That is a step, a
    // ring of mesh torn open at every joint. Each segment is remeasured
    // here, where the handles that shape it are known.
    //
    // Everything the root owns stays on segment 0: the pullout handle, the
    // straight start, the stiffness. Past the first socket the shaft is
    // carried by sockets, so both ends get the same plain handle.
    if (socket.chain.count > 1)
    {
        float3 fromWorld = rootWorld;
        float3 fromForward = rootForward;
        float travelled = 0;

        // A cascade, not a search, and no index computed from data: the
        // arrays only stay in registers while every subscript is a
        // literal. Same rule that shaped the resolver's sort.
        [unroll] for (int c = 0; c < YAPS_CHAIN_MAX; c++)
        {
            float3 toWorld = socket.chain.position[c];
            float3 toForward = socket.chain.forward[c];
            float approach = length(toWorld - fromWorld) * 0.5 * smoothness;
            float3 q0 = fromWorld;
            float3 q1 = fromWorld + fromForward * (c == 0 ? rootHandle : approach);
            float3 q2 = toWorld - toForward * approach;
            float3 q3 = toWorld;

            // Dead entries get a range nothing falls inside rather than a
            // branch, for the unrolling reason above.
            bool live = c < socket.chain.count;
            float segment = live ? YapsCurveLength(q0, q1, q2, q3) : 1e6;
            bool last = c == socket.chain.count - 1;
            bool inside = live && zAlong >= travelled
                       && (last || zAlong < travelled + segment);
            if (inside)
            {
                link = c;
                p0 = q0; p1 = q1; p2 = q2; p3 = q3;
                socketWorld = toWorld;
                socketForward = toForward;
                linkKind = socket.chain.kind[c];
                gripAt = travelled + segment;
                zAlong -= travelled;
            }

            travelled += segment;
            fromWorld = toWorld;
            fromForward = toForward;
        }
    }

    // BEZIER START and SMOOTH START, from TPS. The first fraction of the
    // shaft is held straight before any bend, and SmoothStart eases the
    // join rather than letting it kink. The curve's START moves along the
    // plug's forward, and the walk is asked for the distance PAST it. A
    // vertex inside the straight part is not walked at all.
    float straight = link > 0 ? 0 : saturate(_YAPS_BezierStart) * worldLength;
    float ease = max(_YAPS_SmoothStart, 0) * worldLength;
    float leftOver;
    YapsFrame frame;
    if (straight > 1e-5)
    {
        float3 startWorld = rootWorld + rootForward * straight;
        float3 sp1 = startWorld + rootForward * max(rootHandle - straight, worldLength * 0.1);
        if (zAlong <= straight)
        {
            // In the straight part. On the plug's own axis, no walk.
            frame.position = rootWorld + rootForward * zAlong;
            frame.forward = rootForward;
            frame.up = rootUp;
            leftOver = 0;
        }
        else
        {
            frame = YapsWalk(startWorld, sp1, p2, p3, zAlong - straight, rootUp, rootForward, leftOver);
            // The join: blend the walked frame back toward the straight
            // one over the ease length, so the shaft bends into the curve
            // instead of breaking at a point.
            if (ease > 1e-5)
            {
                float k = saturate((zAlong - straight) / ease);
                k = k * k * (3 - 2 * k);
                float3 straightPos = rootWorld + rootForward * zAlong;
                frame.position = lerp(straightPos, frame.position, k);
                frame.forward = YapsSafeNormalize(lerp(rootForward, frame.forward, k), rootForward);
                frame.up = YapsPerpendicular(frame.forward, lerp(rootUp, frame.up, k));
            }
        }
    }
    else
    {
        frame = YapsWalk(p0, p1, p2, p3, zAlong, rootUp, rootForward, leftOver);
    }

    // Past the end of the curve. A hole swallows the remainder and tapers
    // the tip, a ring lets it carry straight on through.
    float radius = 1;
    // The link this vertex is inside, which past the first is not the one
    // socket.isHole names. Only the last link can leave anything over.
    bool isHole = linkKind > 0.5;
    if (leftOver > 0 && isHole)
    {
        // How far past the hole a vertex travels before narrowing, and
        // before it closes. Fractions of plug length, so big and small
        // plugs taper the same proportion of themselves.
        float taperFrom = worldLength * _YAPS_TaperStart;
        float taperTo = worldLength * max(_YAPS_TaperEnd, _YAPS_TaperStart + 0.001);

        // CLAMP FIRST. Without it a socket closer than the plug is long
        // sends every vertex past it flying forward by its own excess,
        // and with the radius already at zero they collapse onto a line
        // and render as a flat twisted ribbon. Nothing goes more than a
        // tenth of a length past a hole.
        leftOver = min(leftOver, taperTo);
        radius = 1 - YapsRamp(leftOver, taperFrom, taperTo);
    }

    // OVERRUN, and it belongs to BOTH kinds. Its own tooltip is about
    // rings, and it was read only inside the hole branch above, so a ring
    // always carried on and the switch did nothing for the case it names.
    //
    // A ring gets no clamp either, since the clamp is the hole's taper
    // distance. So a ring near the base sent every vertex past it its full
    // remaining length at once, the same flat ribbon by another door.
    if (_YAPS_Overrun < 0.5)
    {
        leftOver = 0;
    }
    // SQUEEZE and BULGE, both measured from the entry, the point level
    // with the socket. Baked z == gap is in the opening, less is outside,
    // more is through.
    //
    // Squeeze narrows where the socket grips, bulge swells just short of
    // the opening. Both make an entry read as tight rather than as a rod
    // sliding through a hoop.
    //
    // Only while engaged, and faded by engagement, so an idle plug is
    // never pinched by a socket that is not there.
    float entry = baked.position.z - gripAt;
    float grip = engage * step(0.5, enabled);

    if (_YAPS_Squeeze > 0)
    {
        // Deepest AT the opening, easing off either side.
        float reach = max(_YAPS_SqueezeDistance, 0.001) * worldLength;
        float near = 1 - saturate(abs(entry) / reach);
        radius *= 1 - _YAPS_Squeeze * near * grip;
    }

    if (_YAPS_Bulge > 0)
    {
        // Just OUTSIDE the opening: entry < 0. Swelling on the far side
        // is the shaft growing inside what it entered.
        float reach = max(_YAPS_BulgeDistance, 0.001) * worldLength;
        float before = saturate(-entry / reach);
        float shape = before * (1 - before) * 4;   // a hump, zero at both ends
        radius *= 1 + _YAPS_Bulge * shape * grip;
    }

    frame.position += frame.forward * leftOver;

    float3 right = cross(frame.up, frame.forward);

    // Re-hang the vertex off the curve's frame, blended by how engaged
    // the bend is and how much this vertex belongs to the shaft.
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
