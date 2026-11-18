// YAPS deform core, for ChilloutVR.
//
// Inspired by VRCFury's SPS. Written from a description of the behaviour,
// never their source. See docs/YAPS-CLEAN-ROOM.md.
//
// Baked in plug space, +Z base to tip: Z is how far along the rod, X/Y how far off the
// centre line. Bending walks Z along a curved rod and re-hangs X/Y off the frame there,
// so it survives any topology.
//
// The curve is one cubic bezier, root to socket:
//     p0 = plug root                   p3 = socket
//     p1 = p0 + plugForward * handle   p2 = p3 - socketForward * handle
// Far off the handles stretch and drag the curve along the plug; near, they shorten
// toward half the gap into an S-bend meeting the socket head-on. Range in plug lengths:
// full bend by 1.2, gone by 1.6.
//
// Walk by ARC LENGTH, never bezier t, which bunches the mesh at the tightest bend. Up
// comes from parallel transport; a fixed world axis spins the plug as it bends.
// Past the curve: OVERRUN carries straight on (else a long plug piles up), and past a
// hole X/Y collapse so the tip tapers instead of poking through.
//
// The baked active weight is a fractional mask falloff near the base: multiply, never
// threshold, or the mesh tears off the body.
//
#ifndef YAPS_DEFORM_INCLUDED
#define YAPS_DEFORM_INCLUDED

#include "yaps_props.cginc"
#include "yaps_resolve.cginc"

#define YAPS_WALK_STEPS 8

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

// v turned about a unit axis by an angle, right-handed (Rodrigues).
inline float3 YapsTurn(float3 v, float3 axis, float angle)
{
    float s, c;
    sincos(angle, s, c);
    return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1 - c);
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
    baked.position = 0;
    baked.normal = 0;
    baked.tangent = 0;
    baked.active = YapsReadFloat(at + 9);

    // The vertex has blendshapes applied but the bake is rest pose, so add each shape's
    // part back. Nine floats a vertex per shape block. The base block is the loop's
    // first pass (weight 1): read apart, its reads compiled twice per variant.
    int shapeCount = (int) max(_YAPS_ShapeCount, 0);
    uint total = (uint) max(_YAPS_VertexCount, 0);
    uint block = 1 + total * 10;
    // The second plug fills the packs from the top, so neither plug's slots
    // move when the other's shape count does.
    uint flip = _YAPS_PlugLane > 0.5 ? 15u : 0u;
    [loop]
    for (int s = -1; s < shapeCount; s++)
    {
        float weight = 1;
        if (s >= 0)
        {
            weight = YapsShapeWeight(flip ^ (uint) s);
            if (abs(weight) <= 0.0001) continue;
            at = block + (uint) s * total * 9 + vertexId * 9;
        }
        baked.position += YapsReadFloat3(at) * weight;
        baked.normal += YapsReadFloat3(at + 3) * weight;
        baked.tangent += YapsReadFloat3(at + 6) * weight;
    }

    // Baked vectors carry the plug's scale (0.38 to 30 seen); only direction matters.
    baked.normal = YapsSafeNormalize(baked.normal, float3(0, 0, 1));
    baked.tangent = YapsSafeNormalize(baked.tangent, float3(1, 0, 0));

    // A vertex behind the base is not part of the shaft.
    if (baked.position.z < 0) baked.active = 0;
    return baked;
}

// --- recovering the plug's own frame ---------------------------------
//
// On an avatar the renderer's transform is the root, not the plug, so the frame is
// recovered from the vertex: skinned normal and tangent vs baked ones pin the rotation,
// position the translation. Follows the bone for free; weak only where vertices blend
// across bones, at the base, where the mask already feathers the deform out.

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

struct YapsRoot
{
    float3 position;   // world
    float3 forward;
    float3 up;
    float worldLength;
    // 0 the object's own axes, 1 the recovery refused, 2 recovered.
    float recovered;
};

// The rod's start. Shared with the readout overlay so it reports the real frame.
// Hands baked back scaled into world units.
YapsRoot YapsRecoverRoot(inout YapsVertex baked, float3 position, float3 normal, float3 tangent)
{
    YapsRoot root;
    root.recovered = 0;

    // Bone scale, which the skinned vertex has and the bake lacks. Before recovery.
    float bakeScale = _YAPS_PlugLane > 0.5 ? _YAPS_BakeScale2 : _YAPS_BakeScale;
    float bakeGirth = _YAPS_PlugLane > 0.5 ? _YAPS_BakeGirth2 : _YAPS_BakeGirth;
    baked.position *= float3(max(bakeGirth, 0.0001), max(bakeGirth, 0.0001), max(bakeScale, 0.0001));

    float3 rootLocal = float3(0, 0, 0);
    float3 forwardLocal = float3(0, 0, 1);
    float3 upLocal = float3(0, 1, 0);
    float skinScale = 1;

    if (_YAPS_FrameFromVertex > 0.5)
    {
        YapsBasis basis = YapsBuildBasis(baked.normal, baked.tangent, normal, tangent);
        root.recovered = basis.valid ? 2 : 1;
        if (basis.valid)
        {
            // Avatar scale, invisible in a skinned mesh's matrix: skinning scales the
            // normal without renormalising (measured), so its length over the bake's,
            // less bone scale, is the root's scale. 0 on old bakes: baked size.
            if (_YAPS_BakeSkinScale > 0)
            {
                float3 boned = basis.a1 * float3(max(bakeGirth, 0.0001), max(bakeGirth, 0.0001), max(bakeScale, 0.0001));
                skinScale = length(normal) / max(_YAPS_BakeSkinScale * length(boned), 1e-6);
                baked.position *= skinScale;
            }
            rootLocal = position - YapsRotate(basis, baked.position);
            forwardLocal = YapsRotate(basis, float3(0, 0, 1));
            upLocal = YapsRotate(basis, float3(0, 1, 0));
        }
    }

    root.position = mul(unity_ObjectToWorld, float4(rootLocal, 1)).xyz;
    root.forward = YapsSafeNormalize(
        mul((float3x3) unity_ObjectToWorld, forwardLocal), float3(0, 0, 1));
    root.up = YapsPerpendicular(root.forward,
        mul((float3x3) unity_ObjectToWorld, upLocal));

    // The scale the plug is drawn at, from the matrix (height slider, world scaling):
    // baked numbers are renderer units and everything after compares world distances.
    // Unhandled, a 2x avatar wore a half-thickness plug engaging at the wrong distance.
    // Reads 1 for skinned meshes, where skinScale carries it. After recovery, which
    // needs the baked numbers as baked.
    float yapsScale = length(mul((float3x3) unity_ObjectToWorld, float3(0, 0, 1)));
    baked.position *= yapsScale;
    root.worldLength = _YAPS_Length * bakeScale * skinScale * yapsScale;
    return root;
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

// Mean of chord and control net, within about 1%. A walk is exact but the chain needs
// every segment's length per vertex just to pick one.
inline float YapsCurveLength(float3 p0, float3 p1, float3 p2, float3 p3)
{
    float net = length(p1 - p0) + length(p2 - p1) + length(p3 - p2);
    return (length(p3 - p0) + net) * 0.5;
}

// Walks arc length; returns the frame and the distance left when the curve ran out.
// startForward covers a collapsed curve (socket on the root, all tangents zero); a world
// fallback flattened the plug into a line through a ring.
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
    float previousT = 0;
    float previousTravelled = 0;
    float previousSpeed = length(YapsBezierTangent(p0, p1, p2, p3, 0));
    float3 carriedUp = frame.up;
    const float dt = 1.0 / YAPS_WALK_STEPS;

    // Unrolled: CVR's Shader Filtering gives a late [loop] one pass, flattening the plug
    // into a strip. Eight Simpson steps on the speed plus one Newton step land within
    // 0.03% on 99 in 100 tries (0.26% worst, near a cusp); eight chords were off 2.6%.
    // The landing is written once below, not per unrolled step.
    bool landed = false;
    float landT = 0, landSpan = 0;
    float3 landForward = frame.forward, landUp = frame.up;
    [unroll]
    for (int step = 1; step <= YAPS_WALK_STEPS; step++)
    {
        float t = step * dt;
        float3 tangent = YapsBezierTangent(p0, p1, p2, p3, t);
        float speed = length(tangent);
        float3 forward = YapsSafeNormalize(tangent, frame.forward);
        float3 up = YapsPerpendicular(forward, carriedUp);
        float span = dt / 6 * (previousSpeed + 4 * length(YapsBezierTangent(p0, p1, p2, p3, t - dt * 0.5)) + speed);
        travelled += span;

        if (wantedLength <= travelled)
        {
            landed = true;
            landT = t;
            landSpan = span;
            landForward = forward;
            landUp = up;
            break;
        }

        previousT = t;
        previousTravelled = travelled;
        previousSpeed = speed;
        carriedUp = up;
    }

    if (landed)
    {
        float landedT = lerp(previousT, landT, saturate((wantedLength - previousTravelled) / max(landSpan, 1e-6)));
        // The speed floored at half the step's mean: near a cusp it falls
        // toward 0 and one step flew to the end of the curve.
        float landedSpeed = length(YapsBezierTangent(p0, p1, p2, p3, landedT));
        float along = previousTravelled + (landedT - previousT) / 6 * (previousSpeed
            + 4 * length(YapsBezierTangent(p0, p1, p2, p3, (previousT + landedT) * 0.5)) + landedSpeed);
        landedT = clamp(landedT + (wantedLength - along) / max(landedSpeed, max(0.5 * landSpan / dt, 1e-6)), previousT, landT);
        frame.position = YapsBezier(p0, p1, p2, p3, landedT);
        frame.forward = YapsSafeNormalize(
            YapsBezierTangent(p0, p1, p2, p3, landedT), landForward);
        frame.up = YapsPerpendicular(frame.forward, lerp(carriedUp, landUp, (landedT - previousT) / dt));
        return frame;
    }

    // Ran off the end: report the shortfall so the caller can extend
    // straight ahead, or collapse the tip if the socket is a hole.
    leftOver = max(wantedLength - travelled, 0);
    frame.position = p3;
    frame.forward = YapsSafeNormalize(YapsBezierTangent(p0, p1, p2, p3, 1), frame.forward);
    frame.up = YapsPerpendicular(frame.forward, carriedUp);
    return frame;
}

// --- the shape at rest ----------------------------------------------

// What the plug does on its own, in its frame, world units. idle is how far
// it is from engaged: 1 with no socket, 0 fully engaged.
void YapsRestShape(inout YapsVertex v, float idle, float worldLength)
{
    // Idle shrink, from TPS. On the baked position before the walk, so taper and overrun
    // scale with it. Driven by the approach's shape, so it eases in.
    v.position.z *= lerp(1, max(_YAPS_IdleLength, 0.01), idle);
    v.position.xy *= lerp(1, max(_YAPS_IdleWidth, 0.01), idle);

    // Curvature and recurvature, from DPS: radians of turn at the tip in the y/z plane,
    // recurvature squared so it gathers at the tip. Each cross-section rides the bent
    // centreline, keeping length; turning about the root grew the shaft 23% at 1.3.
    // Before the walk, so it composes with everything after.
    if (abs(_YAPS_Curvature) > 1e-4 || abs(_YAPS_ReCurvature) > 1e-4)
    {
        float shaft = max(worldLength, 0.0001);
        float along = clamp(v.position.z, 0, shaft);
        // The centreline, by midpoint steps: closed form only without
        // recurvature, and eight steps hold the length to well under 1%.
        float2 centre = 0;
        float stride = along / 8;
        [unroll] for (int i = 0; i < 8; i++)
        {
            float m = (i + 0.5) * stride / shaft;
            float sm, cm;
            sincos(_YAPS_Curvature * m - _YAPS_ReCurvature * m * m, sm, cm);
            centre += float2(-sm, cm) * stride;
        }
        float u = along / shaft;
        float turn = _YAPS_Curvature * u - _YAPS_ReCurvature * u * u;
        float s, c;
        sincos(turn, s, c);
        // Past the tip the shaft runs on straight along the tip's direction;
        // behind the root it is untouched, since there the turn is zero.
        centre += float2(-s, c) * (v.position.z - along);
        // Positive turn sends +z toward -y. Position and both directions turn
        // together or the lighting lies about the shape.
        v.position.yz = centre + v.position.y * float2(c, s);
        float2 nyz = v.normal.yz;
        v.normal.yz = float2(nyz.x * c - nyz.y * s, nyz.x * s + nyz.y * c);
        float2 tyz = v.tangent.yz;
        v.tangent.yz = float2(tyz.x * c - tyz.y * s, tyz.x * s + tyz.y * c);
    }

    // Wriggle, from DPS, idle only so it never fights pumping. Scaled along the shaft
    // so the base stays on the body.
    if (_YAPS_WriggleStrength > 0)
    {
        float along = saturate(v.position.z / max(worldLength, 0.0001));
        float t = _Time.y * max(_YAPS_WriggleSpeed, 0);
        // Two axes out of phase, so it wanders rather than swinging flat.
        v.position.x += sin(t) * _YAPS_WriggleStrength * worldLength * along * idle;
        v.position.y += sin(t * 0.7 + 1.3) * _YAPS_WriggleStrength * worldLength * along * idle;
    }
}

#ifdef YAPS_DUPLICATE
// --- the second copy -------------------------------------------------

// Tests answer per vertex from its recovered root; weighted as originals, roots spread
// up to 119 mm and triangles hid a corner at a time. So the copy sits on ONE point (the
// bake origin, on the root bone, root axes as tangent and normal): every copy vertex
// reads the same root bit for bit. Without the deform a viewer sees a point.

// Hidden: one point a metre behind the eye, so zero-area triangles that a host's
// outline widening opens anyway open behind the camera.
void YapsCopyHide(inout float3 position, inout float3 normal, inout float3 tangent)
{
    float3 back = UNITY_MATRIX_V[2].xyz;
    position = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos + back, 1)).xyz;
    normal = mul((float3x3) unity_WorldToObject, back);
    tangent = mul((float3x3) unity_WorldToObject, UNITY_MATRIX_V[0].xyz);
}

// The chain past the hole that ends the first copy's path, as a path of its
// own. Atlas only: no other tier hands over more than one socket. False when
// nothing live is left or it is out of reach.
bool YapsCopyPath(inout YapsSocket socket, float3 root, float3 forward, float len)
{
    YapsChain c = socket.chain;
    int k = c.count;
    if (socket.tier < 2.5 || k < 1 || k >= YAPS_CHAIN_MAX) return false;
    // One place at a time, so every subscript stays a literal.
    [unroll] for (int s = 0; s < YAPS_CHAIN_MAX - 1; s++)
    {
        if (s < k)
        {
            c.position[0] = c.position[1]; c.position[1] = c.position[2]; c.position[2] = c.position[3];
            c.forward[0] = c.forward[1]; c.forward[1] = c.forward[2]; c.forward[2] = c.forward[3];
            c.kind[0] = c.kind[1]; c.kind[1] = c.kind[2]; c.kind[2] = c.kind[3]; c.kind[3] = -1;
        }
    }
    // To the next hole, as the resolver counts the first path.
    int n = 0;
    bool ended = false;
    [unroll] for (int i = 0; i < YAPS_CHAIN_MAX; i++)
    {
        bool live = c.kind[i] > -0.5 && !ended;
        if (live) n++;
        ended = ended || (live && c.kind[i] > 0.5);
    }
    if (n == 0) return false;
    c.count = n;
    // The key rode in the kind's bits, which the chain no longer carries.
    c.portal = 0;
    // The resolver's engagement and behind-the-base fade, for this socket.
    c.engaged = 1 - smoothstep(len * YAPS_ATLAS_REACH, len * YAPS_ATLAS_REACH * 1.6,
                               distance(c.position[0], root));
    socket.chain = c;
    socket.position = c.position[0];
    socket.forward = c.forward[0];
    socket.isHole = c.kind[0];
    socket.engaged = c.engaged * smoothstep(-len * 0.5, -len * 0.05, dot(c.position[0] - root, forward));
    return socket.engaged > 0;
}

#define YAPS_COPY_HIDE if (copy2) YapsCopyHide(position, normal, tangent);

#else
#define YAPS_COPY_HIDE
#endif

// --- the deform ------------------------------------------------------

void YapsDeform(inout float3 position, inout float3 normal, inout float3 tangent, uint vertexId)
{
#ifdef YAPS_DUPLICATE
    // Bounded above as well: the readout's quads come after the copies.
    bool copy2 = _YAPS_CopyStart > 0 && vertexId >= (uint) _YAPS_CopyStart
              && vertexId < (uint) max(_YAPS_VertexCount, 0);
#endif
    // Off is identity: answer before reading (socket materials carry the plug half off
    // over active vertices). Debug views still answer.
#ifdef YAPS_DUPLICATE
    // Hidden through a fade too, so a toggle on its way out is one plug.
    if (copy2 && YAPS_ENABLED < 1) { YapsCopyHide(position, normal, tangent); return; }
#endif
    if (YAPS_ENABLED <= 0 && _YAPS_Debug < 0.5) return;
    if (vertexId >= (uint) max(_YAPS_VertexCount, 0)) return;

    YapsVertex baked = YapsReadBaked(vertexId);
#ifdef YAPS_DUPLICATE
    // Inactive copy vertices wait for the copy's answer, or one stayed while the
    // copy hid: a triangle from base to eye.
    if (baked.active <= 0 && !copy2) return;
#else
    if (baked.active <= 0) return;
#endif

    float3 originalPosition = position;
    float3 originalNormal = normal;
    float3 originalTangent = tangent;

    YapsRoot root = YapsRecoverRoot(baked, originalPosition, originalNormal, originalTangent);
    // No frame, no deform: the renderer's frame bent from the world origin. A host
    // with no tangent input hands zero here; for the copy this holds host-wide.
    if (root.recovered > 0.5 && root.recovered < 1.5) { YAPS_COPY_HIDE return; }

    float3 rootWorld = root.position;
    float3 rootForward = root.forward;
    float3 rootUp = root.up;
    float worldLength = root.worldLength;
#ifdef YAPS_DUPLICATE
    // The copy's vertex IS its root, its tangent and normal the root's axes.
    if (copy2)
    {
        rootWorld = mul(unity_ObjectToWorld, float4(originalPosition, 1)).xyz;
        rootForward = YapsSafeNormalize(mul((float3x3) unity_ObjectToWorld, originalTangent), float3(0, 0, 1));
        rootUp = YapsPerpendicular(rootForward, mul((float3x3) unity_ObjectToWorld, originalNormal));
    }
#endif

    // From the recovered frame: a bone carries the plug.
    YapsSocket socket = YapsResolveSocket(rootWorld, rootForward, rootUp, worldLength);

    // Debug views answer as plug LENGTH: only the vertex stage is patched, so there
    // is no colour. Straight and unengaged so length is all that moves.
    // "Resolved by": 0.25 nobody, 0.5 editor preview, 0.75 marker light, 1 atlas.
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
            // The camera's target: 0.1 too small to paint, 0.4 grab is another
            // size (someone else's screen), 0.7 right target but no cell reported
            // (addressing mismatch), 1 atlas live on this camera.
            float2 grabPx = _YAPS_Atlas_TexelSize.zw;
            bool sameTarget = all(abs(grabPx - _ScreenParams.xy) < 1.5);
            shown = !YapsAtlasFits() ? 0.10
                  : (!sameTarget ? 0.40
                  : (socket.atlasHeaders < 0.5 ? 0.70 : 1.0));
        }
        else if (_YAPS_Debug >= 4.5)
        {
            // Raw atlas taps, in steps not a ratio (one cell of 27 is 3%): 0.1 no
            // cell reported, 0.33 no payload matched its tag, 0.66 matched but
            // rejected by kind or reach, 1 a socket came back.
            shown = socket.atlasHeaders < 0.5 ? 0.10
                  : (socket.atlasHits < 0.5 ? 0.33
                  : (socket.tier < 2.5 ? 0.66 : 1.0));
        }
        else if (_YAPS_Debug >= 3.5)
        {
            // Socket facing vs this vertex's recovered forward: 1 same way, ~0.55
            // square, 0.1 straight back (floored so it is not read as a collapse).
            // No facing sits at mid scale.
            float3 face = socket.forward;
            float facing = dot(face, face) > 1e-6
                ? saturate(dot(normalize(face), rootForward) * 0.5 + 0.5)
                : 0.5;   // nothing believed, and it sits deliberately mid
            shown = lerp(0.1, 1.0, facing);
        }
        else if (_YAPS_Debug >= 2.5)
        {
            // Engagement, which can collapse while tier and gap hold steady (a pop).
            // 0.1 at zero, a visible stub.
            shown = lerp(0.1, 1.0, saturate(socket.engaged));
        }
        else
        {
            // Gap to the socket in plug lengths: a jump means a different socket
            // (two lights of one tier swapping). Full when nobody resolved.
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
        // The views describe the first copy.
        YAPS_COPY_HIDE
        return;
    }

#ifdef YAPS_DUPLICATE
    // A path ending in a portal gives the second copy to the exit. Only a hole with a
    // published exit counts; every hole has a key, and keying on it hid the copy at
    // every hole. Unwrapped round the entrance: round the origin, past 41 m it was 82 m off.
    float3 portalAt = socket.position, portalFwd = rootForward;
    bool viaPortal = false;
    if (copy2 && socket.chain.portal != 0) viaPortal = YapsPortalExit(socket.chain.portal, portalAt, portalFwd);
    // Nested: HLSL does not short-circuit, so && ran YapsCopyPath's inout on the first copy.
    if (copy2 && !viaPortal)
    {
        if (!YapsCopyPath(socket, rootWorld, rootForward, worldLength))
        {
            YapsCopyHide(position, normal, tangent);
            return;
        }
    }
#endif

    // The rest shape holds with no socket in reach. A delta on the drawn vertex, so
    // defaults draw exactly as modelled and the bend fades into this, not the mesh.
    YapsVertex rest = baked;
    YapsRestShape(rest, 1, worldLength);
    float restWeight = baked.active * saturate(YAPS_ENABLED);
#ifdef YAPS_DUPLICATE
    // The copy rests on its point, so it grows out of the root as it engages
    // rather than standing up beside the first as a second straight shaft.
    if (copy2) restWeight = 0;
#endif
    float3 restRight = cross(rootUp, rootForward);
    float3 moved = rest.position - baked.position;
    float3 restPosition = originalPosition + mul((float3x3) unity_WorldToObject,
        restRight * moved.x + rootUp * moved.y + rootForward * moved.z) * restWeight;
    float3 restNormal = originalNormal;
    float3 restTangent = originalTangent;
    if (abs(_YAPS_Curvature) > 1e-4 || abs(_YAPS_ReCurvature) > 1e-4)
    {
        // Only the bend turns a direction; shrink and wriggle leave them be.
        restNormal = lerp(originalNormal, mul((float3x3) unity_WorldToObject,
            restRight * rest.normal.x + rootUp * rest.normal.y + rootForward * rest.normal.z), restWeight);
        restTangent = lerp(originalTangent, mul((float3x3) unity_WorldToObject,
            restRight * rest.tangent.x + rootUp * rest.tangent.y + rootForward * rest.tangent.z), restWeight);
    }

    float enabled = saturate(YAPS_ENABLED) * socket.engaged;
    if (enabled <= 0)
    {
        position = restPosition;
        normal = restNormal;
        tangent = restTangent;
        YAPS_COPY_HIDE
        return;
    }

    float3 socketWorld = socket.position;
    float3 toSocket = socketWorld - rootWorld;
    float gap = length(toSocket);

    // Minimum socket distance, from TPS: a socket on the root makes a hairpin.
    float minimumGap = max(_YAPS_MinimumSocketDistance, 0) * worldLength;
    if (gap < minimumGap && gap > 1e-5)
    {
        socketWorld = rootWorld + toSocket * (minimumGap / gap);
        toSocket = socketWorld - rootWorld;
        gap = minimumGap;
    }

    // No axis (a root light without its front): arrive along the approach.
    float3 socketForward = dot(socket.forward, socket.forward) > 1e-6
        ? normalize(socket.forward)
        : YapsSafeNormalize(toSocket, rootForward);

    // Flipped to meet the approach, or a socket facing away makes a hairpin. Converted
    // avatars keep their own convention, so the deform must not depend on it.
    if (dot(socketForward, toSocket) < 0)
    {
        socketForward = -socketForward;
    }

    float engage = 1 - YapsRamp(gap, worldLength * 1.2, worldLength * 1.6);
#ifdef YAPS_DUPLICATE
    // Unengaged the copy sits on its point, which a host widening outlines
    // from vertex colour opens in view.
    if (copy2 && engage <= 0)
    {
        YapsCopyHide(position, normal, tangent);
        return;
    }
#endif

    // The sweep: short of full engagement the target turns back toward the plug's forward
    // by the unengaged part, so the bend grows evenly. A long root handle instead whipped
    // the tip up to 18x the socket's motion near 1.3 lengths. The chain turns too.
    float3 aimAt = YapsSafeNormalize(toSocket, rootForward);
    float3 sweepAxis = cross(aimAt, rootForward);
    float sweepSin = length(sweepAxis);
    sweepAxis = sweepSin > 1e-5 ? sweepAxis / sweepSin : rootUp;
    float sweep = acos(clamp(dot(aimAt, rootForward), -1, 1)) * (1 - engage);
    socketWorld = rootWorld + YapsTurn(socketWorld - rootWorld, sweepAxis, sweep);
    socketForward = YapsTurn(socketForward, sweepAxis, sweep);
    toSocket = socketWorld - rootWorld;

    // Idle shrink, the resting bend and wriggle, eased out as it engages.
    YapsRestShape(baked, 1 - engage, worldLength);

    // Pumping, from TPS: engaged only, scaled along the shaft so the base stays put.
    float along = saturate(baked.position.z / max(worldLength, 0.0001));

    if (_YAPS_PumpStrength > 0)
    {
        float t = _Time.y * max(_YAPS_PumpSpeed, 0);
        // along^(1/width): 1 linear, 0.25 quartic (only the tip moves).
        float width = clamp(_YAPS_PumpWidth, 0.05, 1);
        float share = pow(along, 1 / width);
        baked.position.z += sin(t) * _YAPS_PumpStrength * worldLength * share * engage;
    }

    // Handles are not stretched: that made a 0.6 m plug a 1.8 m loop. The sweep keeps it
    // straight out of range. Smoothness from TPS scales both.
    float smoothness = max(_YAPS_BezierSmoothness, 0.05);

    float zAlong = max(baked.position.z, 0);

    float approachHandle = gap * 0.5 * smoothness;
    float rootHandle = approachHandle;

    // Entrance stiffness, from DPS: a longer root handle keeps the base put.
    rootHandle = max(rootHandle, saturate(_YAPS_EntranceStiffness) * worldLength * engage);

    float3 p0 = rootWorld;
    float3 p1 = rootWorld + rootForward * rootHandle;
    float3 p2 = socketWorld - socketForward * approachHandle;
    float3 p3 = socketWorld;

    // Here so the chain measures link 0 on the curve its vertices walk.
    float straightStart = saturate(_YAPS_BezierStart) * worldLength;
    float3 startWorld = rootWorld + rootForward * straightStart;
    float3 sp1 = startWorld + rootForward * max(rootHandle - straightStart, worldLength * 0.1);

    // Where this vertex's own socket grips; squeeze and bulge measure from it.
    float gripAt = gap;
    float linkKind = socket.isHole;
    int link = 0;

    // The chain: each socket owns a stretch of shaft; a vertex is in ONE link, so the
    // walk stays one cubic, leaving the previous socket along its forward (no kink).
    // Segments remeasured on the curve, not the resolver's chord, or every joint tore
    // open. Root-owned handles (pullout, straight start, stiffness) stay on link 0.
    if (socket.chain.count > 1)
    {
        float3 fromWorld = rootWorld;
        float3 fromForward = rootForward;
        float travelled = 0;

        // A cascade: arrays stay in registers only with literal subscripts.
        [unroll] for (int c = 0; c < YAPS_CHAIN_MAX; c++)
        {
            // Link 0 as settled above (min gap, guarded axis, turned to the approach).
            float3 toWorld = c == 0 ? socketWorld
                : rootWorld + YapsTurn(socket.chain.position[c] - rootWorld, sweepAxis, sweep);
            float3 toForward = c == 0 ? socketForward
                : YapsTurn(socket.chain.forward[c], sweepAxis, sweep);
            // No facing believed: arrive along the approach, as a lone socket does.
            toForward = YapsSafeNormalize(toForward, YapsSafeNormalize(toWorld - fromWorld, fromForward));
            float approach = length(toWorld - fromWorld) * 0.5 * smoothness;
            float3 q0 = fromWorld;
            float3 q1 = fromWorld + fromForward * (c == 0 ? rootHandle : approach);
            float3 q2 = toWorld - toForward * approach;
            float3 q3 = toWorld;

            // Dead entries get an empty range, not a branch.
            bool live = c < socket.chain.count;
            // Link 0 with a straight start is measured as it is walked, or the joint stepped.
            float segment = !live ? 1e6
                : (c == 0 && straightStart > 1e-5
                    ? straightStart + YapsCurveLength(startWorld, sp1, q2, q3)
                    : YapsCurveLength(q0, q1, q2, q3));
            bool last = c == socket.chain.count - 1;
            bool inside = live && zAlong >= travelled
                       && (last || zAlong < travelled + segment);
            if (inside)
            {
                link = c;
                p0 = q0; p1 = q1; p2 = q2; p3 = q3;
                linkKind = socket.chain.kind[c];
                gripAt = travelled + segment;
                zAlong -= travelled;
            }

            travelled += segment;
            fromWorld = toWorld;
            fromForward = toForward;
        }
    }

    // Bezier start and smooth start, from TPS: the first fraction stays straight (not
    // walked), the curve starts past it, and the join is eased.
    float straight = link > 0 ? 0 : straightStart;
    float ease = max(_YAPS_SmoothStart, 0) * worldLength;
    float leftOver;
    YapsFrame frame;
    bool held = straight > 1e-5;
    if (held && zAlong <= straight)
    {
        frame.position = rootWorld + rootForward * zAlong;
        frame.forward = rootForward;
        frame.up = rootUp;
        leftOver = 0;
    }
    else
    {
        // One call for both curves, or it compiles twice per variant.
        frame = YapsWalk(held ? startWorld : p0, held ? sp1 : p1, p2, p3, held ? zAlong - straight : zAlong,
                         rootUp, rootForward, leftOver);
        // Eased join, so the shaft bends into the curve instead of breaking.
        if (held && ease > 1e-5)
        {
            float k = saturate((zAlong - straight) / ease);
            k = k * k * (3 - 2 * k);
            float3 straightPos = rootWorld + rootForward * zAlong;
            frame.position = lerp(straightPos, frame.position, k);
            frame.forward = YapsSafeNormalize(lerp(rootForward, frame.forward, k), rootForward);
            frame.up = YapsPerpendicular(frame.forward, lerp(rootUp, frame.up, k));
        }
    }

    // The portal, drawn by the second copy: the first enters the hole, the second leaves
    // the exit past a cut a band beyond the hole; the rest waits on a point inside the exit.
    // No triangle joins the two (one copy across the room let outlines draw a line between).
    // Every gate term is plug-wide, so the copy takes one branch.
    bool portalled = false;
    bool collapsed = false;
#ifdef YAPS_DUPLICATE
    if (viaPortal)
    {
        // The exit as read where the portal was decided.
        float3 exitAt = portalAt;
        float3 exitFwd = portalFwd;
        bool open = YAPS_ENABLED >= 1 && socket.engaged * engage >= 0.999 && _YAPS_PortalSpan > 0;
        // Engaging or fading: the first copy is the whole plug.
        if (!open)
        {
            YapsCopyHide(position, normal, tangent);
            return;
        }
        // Margin covers the pump's travel.
        float w = _YAPS_PortalSpan * worldLength * (1 + max(_YAPS_PumpStrength, 0));
        portalled = link == socket.chain.count - 1 && leftOver >= 2 * w;
        collapsed = !portalled;
        // Short of the cut: a zero-area point inside the exit, normal and tangent along it.
        frame.position = portalled ? exitAt : exitAt - exitFwd * w;
        frame.forward = exitFwd;
        // Roll from the arrival, approximate.
        frame.up = YapsPerpendicular(exitFwd, frame.up);
        leftOver = portalled ? leftOver - 2 * w : 0;
        if (collapsed)
        {
            baked.position.xy = 0;
            baked.normal = float3(0, 0, 1);
            baked.tangent = float3(0, 0, 1);
        }
    }
#endif

    // Past the curve: a hole swallows and tapers, a ring carries on.
    float radius = 1;
    // This vertex's link, not socket.isHole. Only the last link has leftover.
    bool isHole = linkKind > 0.5;
    if (leftOver > 0 && isHole && !portalled)
    {
        float taperFrom = worldLength * _YAPS_TaperStart;
        float taperTo = worldLength * max(_YAPS_TaperEnd, _YAPS_TaperStart + 0.001);

        // Clamp first, or vertices fly forward by their excess and, at zero radius,
        // render as a flat twisted ribbon.
        leftOver = min(leftOver, taperTo);
        radius = 1 - YapsRamp(leftOver, taperFrom, taperTo);
    }

    // Overrun applies to both kinds; rings get no taper clamp, so off it stops the
    // flat ribbon past a ring near the base.
    if (_YAPS_Overrun < 0.5 && !portalled)
    {
        leftOver = 0;
    }
    // Squeeze and bulge, from the entry (z == gap is in the opening, less outside).
    // Faded by engagement so an idle plug is never pinched. Out of a portal, the exit's mouth.
    float entry = portalled ? leftOver : baked.position.z - gripAt;
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
        // Outside the opening only (entry < 0).
        float reach = max(_YAPS_BulgeDistance, 0.001) * worldLength;
        float shape;
        if (_YAPS_BulgeFalloff > 0)
        {
            // TPS's shape: full a falloff short of the opening, gone at it,
            // rising over the reach behind the peak.
            float peak = _YAPS_BulgeFalloff * worldLength;
            shape = -entry < peak
                ? smoothstep(0, peak, -entry)
                : 1 - smoothstep(peak, peak + reach, -entry);
        }
        else
        {
            float before = saturate(-entry / reach);
            shape = before * (1 - before) * 4;   // a hump, zero at both ends
        }
        radius *= 1 + _YAPS_Bulge * shape * grip;
    }

    frame.position += frame.forward * leftOver;

    float3 right = cross(frame.up, frame.forward);

    float blend = YapsRamp(engage, 0, 0.2) * baked.active * enabled;

    float3 deformed = frame.position
        + right * (baked.position.x * radius)
        + frame.up * (baked.position.y * radius);
    float3 drawn = mul(unity_WorldToObject, float4(deformed, 1)).xyz;
    // Assigned on the copy's point: lerp(a, b, 1) can be an ulp off, a sliver.
    position = collapsed ? drawn : lerp(restPosition, drawn, blend);
    if (collapsed) blend = 1;

    if (!YapsIsZero(baked.normal))
    {
        float3 worldNormal = right * baked.normal.x
                           + frame.up * baked.normal.y
                           + frame.forward * baked.normal.z;
        normal = lerp(restNormal,
            mul((float3x3) unity_WorldToObject, worldNormal), blend);
    }
    if (!YapsIsZero(baked.tangent))
    {
        float3 worldTangent = right * baked.tangent.x
                            + frame.up * baked.tangent.y
                            + frame.forward * baked.tangent.z;
        tangent = lerp(restTangent,
            mul((float3x3) unity_WorldToObject, worldTangent), blend);
    }
}

#endif
