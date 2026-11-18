// YAPS deform core, for ChilloutVR.
// Inspired by VRCFury's SPS, written from a description of the behaviour,
// never their source. See docs/YAPS-CLEAN-ROOM.md.
//
// Baked in plug space, +Z base to tip: Z is distance ALONG the rod, X/Y the
// offset from it. Bending walks Z along a curve and re-hangs X/Y off the
// frame there, so any topology survives.
// One cubic bezier, root to socket:
//     p0 = plug root                   p3 = socket
//     p1 = p0 + plugForward * handle   p2 = p3 - socketForward * handle
// Long handles far off keep the plug straight; near, they shrink toward half
// the gap and the curve meets the socket head-on. Range in plug lengths:
// full bend by 1.2, gone by 1.6.
// Walk ARC LENGTH, never bezier t (t bunches the mesh at the bend). Up comes
// from parallel transport; a fixed world axis spins the plug as it bends.
// Past the curve: OVERRUN carries straight on (else it piles up at the
// socket); past a hole the X/Y offsets collapse so the tip tapers.
// The active weight is a fractional MASK FALLOFF: multiply, never threshold,
// or the mesh tears off the body.
#ifndef YAPS_DEFORM_INCLUDED
#define YAPS_DEFORM_INCLUDED

#include "yaps_props.cginc"
#include "yaps_resolve.cginc"

#define YAPS_WALK_STEPS 48

// Attribution switch for the fxc budget: 0 none, 1 tracker, 2 and lookup,
// 3 and cut (the spike).
#ifndef PORTAL_STAGE
#define PORTAL_STAGE 3
#endif

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
    baked.position = YapsReadFloat3(at);
    baked.normal = YapsReadFloat3(at + 3);
    baked.tangent = YapsReadFloat3(at + 6);
    baked.active = YapsReadFloat(at + 9);

    // The incoming vertex has blendshapes applied but the bake is the REST
    // pose, so add each shape back. Nine floats a vertex per block after the
    // base block, hence the vertex count on the material.
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

    // Baked vectors carry plug scale (0.38 to 30 seen). Only direction is
    // wanted, and dividing by a factor fails on non-uniform scale.
    baked.normal = YapsSafeNormalize(baked.normal, float3(0, 0, 1));
    baked.tangent = YapsSafeNormalize(baked.tangent, float3(1, 0, 0));

    // A vertex behind the base is not part of the shaft.
    if (baked.position.z < 0) baked.active = 0;
    return baked;
}

// --- recovering the plug's own frame ---------------------------------
// On a skinned avatar the renderer's transform is the avatar root, not the
// plug. The skinned normal and tangent against the baked ones pin the
// rotation, the position the translation. No uniforms, follows the bone.
// Weak only on multi-bone vertices at the base, where the mask fades anyway.

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

// Shared with the readout overlay so it reports the very frame the deform
// uses. Returns baked scaled into world units.
YapsRoot YapsRecoverRoot(inout YapsVertex baked, float3 position, float3 normal, float3 tangent)
{
    YapsRoot root;
    root.recovered = 0;

    // Bone scale, already in the skinned vertex but not the bake. Before
    // the recovery, so a scaled vertex lands on its own root.
    baked.position *= float3(max(_YAPS_BakeGirth, 0.0001), max(_YAPS_BakeGirth, 0.0001), max(_YAPS_BakeScale, 0.0001));

    float3 rootLocal = float3(0, 0, 0);
    float3 forwardLocal = float3(0, 0, 1);
    float3 upLocal = float3(0, 1, 0);

    if (_YAPS_FrameFromVertex > 0.5)
    {
        YapsBasis basis = YapsBuildBasis(baked.normal, baked.tangent, normal, tangent);
        root.recovered = basis.valid ? 2 : 1;
        if (basis.valid)
        {
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

    // The drawn scale: the bake is in renderer units, compared below against
    // world distances. Unhandled, a 2x avatar engaged at the wrong distance
    // and wore a half-thickness plug.
    // NOT PROVEN FOR A SKINNED PLUG: its draw matrix is not the renderer's
    // transform, and whether it carries root scale was never checked in game.
    // After the recovery, which needs the numbers as baked.
    float yapsScale = length(mul((float3x3) unity_ObjectToWorld, float3(0, 0, 1)));
    baked.position *= yapsScale;
    root.worldLength = _YAPS_Length * _YAPS_BakeScale * yapsScale;
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

// Mean of chord and control net: they bracket the length, within about 1%.
// A walk per segment per vertex just to pick one is the wrong trade.
inline float YapsCurveLength(float3 p0, float3 p1, float3 p2, float3 p3)
{
    float net = length(p1 - p0) + length(p2 - p1) + length(p3 - p2);
    return (length(p3 - p0) + net) * 0.5;
}

// Walk wantedLength of arc; leftOver is what remained past the end.
// startForward covers a socket on the root, where every tangent is zero:
// a world-forward fallback flattened the plug into a line through a ring.
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
            // Lerping t by distance is fine across a step this small.
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

    leftOver = max(wantedLength - travelled, 0);
    frame.position = p3;
    frame.forward = YapsSafeNormalize(YapsBezierTangent(p0, p1, p2, p3, 1), frame.forward);
    frame.up = YapsPerpendicular(frame.forward, carriedUp);
    return frame;
}

// --- the shape at rest ----------------------------------------------

// In plug frame, world units. idle: 1 with no socket, 0 fully engaged.
void YapsRestShape(inout YapsVertex v, float idle, float worldLength)
{
    // IDLE SHRINK, from TPS. On the baked position before the walk, so
    // taper and overrun scale with it. Driven by approach, so it eases in.
    v.position.z *= lerp(1, max(_YAPS_IdleLength, 0.01), idle);
    v.position.xy *= lerp(1, max(_YAPS_IdleWidth, 0.01), idle);

    // CURVATURE and RECURVATURE, from DPS. A rotation about x growing along
    // the shaft: a bend, not a shear, so length holds and normals turn.
    // Recurvature squares the fraction to gather at the tip. Before the walk,
    // so it composes with everything after.
    if (abs(_YAPS_Curvature) > 1e-4 || abs(_YAPS_ReCurvature) > 1e-4)
    {
        float t = saturate(v.position.z / max(worldLength, 0.0001));
        float turn = _YAPS_Curvature * t - _YAPS_ReCurvature * t * t;
        float s, c;
        sincos(turn, s, c);
        // Directions turn with the position or the lighting lies.
        float2 yz = v.position.yz;
        v.position.yz = float2(yz.x * c - yz.y * s, yz.x * s + yz.y * c);
        float2 nyz = v.normal.yz;
        v.normal.yz = float2(nyz.x * c - nyz.y * s, nyz.x * s + nyz.y * c);
        float2 tyz = v.tangent.yz;
        v.tangent.yz = float2(tyz.x * c - tyz.y * s, tyz.x * s + tyz.y * c);
    }

    // WRIGGLE, from DPS, idle only so it never fights the pumping. Scaled
    // along the shaft, so the base stays welded to the body.
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
// --- the second copy (DUPLICATE SPIKE) -------------------------------

// The copy is built on ONE point (the bake origin, on the root's bone, root
// axes as tangent and normal), so every test answers once for the whole
// copy. Weighted as their originals, a bent plug spread the roots by up to
// 119 mm and triangles hid one corner at a time. Without the deform: a point.

// Hidden: every vertex to one point a metre behind the eye, one normal and
// tangent, so no area and any host offset opens it behind the camera. A
// point near the plug failed: outline hosts opened it in view.
void YapsCopyHide(inout float3 position, inout float3 normal, inout float3 tangent)
{
    float3 back = UNITY_MATRIX_V[2].xyz;
    position = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos + back, 1)).xyz;
    normal = mul((float3x3) unity_WorldToObject, back);
    tangent = mul((float3x3) unity_WorldToObject, UNITY_MATRIX_V[0].xyz);
}

// The chain past the first path's hole, as its own path. Atlas only: no
// other tier gives more than one socket.
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

#ifdef YAPS_DUP_DUMP
// The socket as resolved, before the second copy's shift, and the recovered
// root, for the spike's capture.
static YapsSocket yapsDump = (YapsSocket)0;
static float3 yapsDumpRoot = float3(0, 0, 0);
#endif
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
    // Off before any read: a socket material carries a disabled plug half
    // over active vertices, which all ran the full resolve for nothing.
#ifdef YAPS_DUPLICATE
    // Hidden through a fade too, so a fading toggle is one plug.
    if (copy2 && _YAPS_Enabled < 1) { YapsCopyHide(position, normal, tangent); return; }
#endif
    if (_YAPS_Enabled <= 0 && _YAPS_Debug < 0.5) return;
    if (vertexId >= (uint) max(_YAPS_VertexCount, 0)) return;

    YapsVertex baked = YapsReadBaked(vertexId);
#ifdef YAPS_DUPLICATE
    // An inactive copy vertex must hide with the copy, or a triangle runs
    // from the base to the eye.
    if (baked.active <= 0 && !copy2) return;
#else
    if (baked.active <= 0) return;
#endif

    float3 originalPosition = position;
    float3 originalNormal = normal;
    float3 originalTangent = tangent;

    YapsRoot root = YapsRecoverRoot(baked, originalPosition, originalNormal, originalTangent);
    // No frame, no deform: falling back to the renderer's frame bent from
    // the world origin. A host with no tangent input gives zero here.
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
#ifdef YAPS_DUP_DUMP
    yapsDumpRoot = rootWorld;
#endif
#endif

    // The recovered frame, never the renderer's: a bone carries the plug.
    YapsSocket socket = YapsResolveSocket(rootWorld, rootForward, rootUp, worldLength);

    // DEBUG VIEWS, answered as plug LENGTH (only the vertex stage is
    // patched, so no colour). Resolved by: 1/4 nobody, 1/2 editor preview,
    // 3/4 marker light, full atlas. Straight and unbent, so length is the
    // only signal. Before the enabled test: "nobody" is an answer.
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
            // The CAMERA, since view, mirror and self portrait differ.
            // 0.1 target too small; 0.4 grab size differs (another screen);
            // 0.7 right target, no cell reported (addressing mismatch);
            // full: transport works here.
            float2 grabPx = _YAPS_Atlas_TexelSize.zw;
            bool sameTarget = all(abs(grabPx - _ScreenParams.xy) < 1.5);
            shown = !YapsAtlasFits() ? 0.10
                  : (!sameTarget ? 0.40
                  : (socket.atlasHeaders < 0.5 ? 0.70 : 1.0));
        }
        else if (_YAPS_Debug >= 4.5)
        {
            // Raw atlas taps, since "nothing resolved" has opposite causes.
            // Steps, never a ratio: one socket is 1 cell of 27, so a ratio
            // put working and dead 3% apart.
            // 0.1 no cell reported (nothing in reach, empty grab, addressing);
            // 0.33 no payload matched its tag; 0.66 matched but kind or reach
            // threw it away (geometry, not transport); full: a socket.
            shown = socket.atlasHeaders < 0.5 ? 0.10
                  : (socket.atlasHits < 0.5 ? 0.33
                  : (socket.tier < 2.5 ? 0.66 : 1.0));
        }
        else if (_YAPS_Debug >= 3.5)
        {
            // Socket facing vs plug forward: full same way, about half
            // square across, 0.1 straight back (a floor, as 0 looked like a
            // collapse). Per vertex frame, so a multi-bone plug can give
            // several answers. No forward sits mid scale.
            float3 face = socket.forward;
            float facing = dot(face, face) > 1e-6
                ? saturate(dot(normalize(face), rootForward) * 0.5 + 0.5)
                : 0.5;   // nothing believed, and it sits deliberately mid
            shown = lerp(0.1, 1.0, facing);
        }
        else if (_YAPS_Debug >= 2.5)
        {
            // ENGAGEMENT: it can collapse while tier and gap hold steady,
            // which is what a pop looks like. 0.1 floor keeps a stub.
            shown = lerp(0.1, 1.0, saturate(socket.engaged));
        }
        else
        {
            // GAP to the socket in plug lengths: a JUMP shows two same-tier
            // lights swapping. Full when unresolved; zero would mean "on
            // the root".
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
#ifdef YAPS_DUP_DUMP
    yapsDump = socket;
#endif
    // Nested: HLSL does not short-circuit, and the inout call on the first
    // copy sent it down the second's path.
    if (copy2)
    {
        if (!YapsCopyPath(socket, rootWorld, rootForward, worldLength))
        {
            YapsCopyHide(position, normal, tangent);
            return;
        }
    }
#endif

    // THE SHAPE AT REST, before the no-socket return, or the knobs show
    // only between 1.2 and 1.6 lengths. A DELTA on the drawn vertex, so the
    // defaults leave it as drawn and the bend fades into this.
    YapsVertex rest = baked;
    YapsRestShape(rest, 1, worldLength);
    float restWeight = baked.active * saturate(_YAPS_Enabled);
#ifdef YAPS_DUPLICATE
    // The copy rests on its point and grows out of the root as it engages.
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

    float enabled = saturate(_YAPS_Enabled) * socket.engaged;
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

    // MINIMUM SOCKET DISTANCE, from TPS: a socket on the root hairpins.
    float minimumGap = max(_YAPS_MinimumSocketDistance, 0) * worldLength;
    if (gap < minimumGap && gap > 1e-5)
    {
        socketWorld = rootWorld + toSocket * (minimumGap / gap);
        toSocket = socketWorld - rootWorld;
        gap = minimumGap;
    }

    // A root light can arrive without its front: aim along the approach.
    float3 socketForward = dot(socket.forward, socket.forward) > 1e-6
        ? normalize(socket.forward)
        : YapsSafeNormalize(toSocket, rootForward);

    // Flip to meet the approach, or a socket facing away hairpins. Converted
    // avatars keep their original convention, so never depend on it.
    if (dot(socketForward, toSocket) < 0)
    {
        socketForward = -socketForward;
    }

    float engage = 1 - YapsRamp(gap, worldLength * 1.2, worldLength * 1.6);
#ifdef YAPS_DUPLICATE
    // Unengaged, an outline host would open the copy's point in view.
    if (copy2 && engage <= 0)
    {
        YapsCopyHide(position, normal, tangent);
        return;
    }
#endif

    // THE SWEEP. Short of full engagement the target turns back toward the
    // plug's forward by the unengaged part, so the bend grows evenly. A five
    // length root handle failed: the tip whipped up to 18x as far as the
    // socket moved, just inside 1.3 lengths.
    float3 aimAt = YapsSafeNormalize(toSocket, rootForward);
    float3 sweepAxis = cross(aimAt, rootForward);
    float sweepSin = length(sweepAxis);
    sweepAxis = sweepSin > 1e-5 ? sweepAxis / sweepSin : rootUp;
    float sweep = acos(clamp(dot(aimAt, rootForward), -1, 1)) * (1 - engage);
    socketWorld = rootWorld + YapsTurn(socketWorld - rootWorld, sweepAxis, sweep);
    socketForward = YapsTurn(socketForward, sweepAxis, sweep);
    toSocket = socketWorld - rootWorld;

    YapsRestShape(baked, 1 - engage, worldLength);

    // PUMPING, from TPS. Engaged only, so it never fights the wriggle;
    // scaled along the shaft so the base stays welded.
    float along = saturate(baked.position.z / max(worldLength, 0.0001));

    if (_YAPS_PumpStrength > 0)
    {
        float t = _Time.y * max(_YAPS_PumpSpeed, 0);
        // along^(1/width): 1 linear, 0.25 quartic, throwing only the tip.
        float width = clamp(_YAPS_PumpWidth, 0.05, 1);
        float share = pow(along, 1 / width);
        baked.position.z += sin(t) * _YAPS_PumpStrength * worldLength * share * engage;
    }

    // Neither handle is stretched: the sweep holds it straight. Stretched
    // handles turned a 0.6 m plug into a 1.8 m loop aimed into space.
    // BEZIER SMOOTHNESS, from TPS: below 1 sharper, above 1 wider.
    float smoothness = max(_YAPS_BezierSmoothness, 0.05);

    float zAlong = max(baked.position.z, 0);

    float approachHandle = gap * 0.5 * smoothness;
    float rootHandle = approachHandle;

    // ENTRANCE STIFFNESS, from DPS: 0 bends evenly, 1 holds a full length.
    rootHandle = max(rootHandle, saturate(_YAPS_EntranceStiffness) * worldLength * engage);

    float3 p0 = rootWorld;
    float3 p1 = rootWorld + rootForward * rootHandle;
    float3 p2 = socketWorld - socketForward * approachHandle;
    float3 p3 = socketWorld;

    // Here so the chain measures link 0 on the curve the walk uses.
    float straightStart = saturate(_YAPS_BezierStart) * worldLength;
    float3 startWorld = rootWorld + rootForward * straightStart;
    float3 sp1 = startWorld + rootForward * max(rootHandle - straightStart, worldLength * 0.1);

    // Squeeze and bulge measure from this vertex's OWN socket on a chain.
    float gripAt = gap;
    float linkKind = socket.isHole;
    int link = 0;

    // THE CHAIN: each socket owns a stretch, one cubic per vertex, leaving
    // the previous socket along its forward so joints stay smooth.
    // Remeasured on the CURVE: the resolver's chord is shorter, which tore a
    // ring of mesh open at every joint. Root-owned handles (pullout, straight
    // start, stiffness) stay on segment 0.
    if (socket.chain.count > 1)
    {
        float3 fromWorld = rootWorld;
        float3 fromForward = rootForward;
        float travelled = 0;

        // A cascade, not a search: literal subscripts keep the arrays in
        // registers.
        [unroll] for (int c = 0; c < YAPS_CHAIN_MAX; c++)
        {
            // Link 0 as settled above (minimum gap, guarded and flipped axis),
            // not rebuilt from the raw entry.
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

            // Dead entries get an unreachable range rather than a branch.
            bool live = c < socket.chain.count;
            // Measured as walked (straight run, then startWorld..q3), or the
            // joint steps.
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

    // BEZIER START and SMOOTH START, from TPS: the curve starts past the
    // straight run; vertices inside it are not walked.
    float straight = link > 0 ? 0 : straightStart;
    float ease = max(_YAPS_SmoothStart, 0) * worldLength;
    float leftOver;
    YapsFrame frame;
    if (straight > 1e-5)
    {
        if (zAlong <= straight)
        {
            frame.position = rootWorld + rootForward * zAlong;
            frame.forward = rootForward;
            frame.up = rootUp;
            leftOver = 0;
        }
        else
        {
            frame = YapsWalk(startWorld, sp1, p2, p3, zAlong - straight, rootUp, rootForward, leftOver);
            // Ease the join so it bends, not breaks.
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

    // THE PORTAL (spike). A hole with a published exit cuts the shaft; a
    // band either side collapses to a point, the rest comes out the exit.
    // Every gate term is plug-wide, so no triangle spans the room. Nested:
    // HLSL runs both sides of &&, and the read belongs here alone.
    bool portalled = false;
    bool collapsed = false;
#if PORTAL_STAGE == 1
    // Attribution only: the tracker, kept alive by a consumer that costs nothing.
    if (socket.chain.portal != 0) leftOver += 1e-7;
#endif
#if PORTAL_STAGE >= 2
    if (leftOver > 0 && socket.chain.portal != 0 && link == socket.chain.count - 1
        && _YAPS_Enabled >= 1 && socket.engaged * engage >= 0.999 && _YAPS_PortalSpan > 0)
    {
        float3 exitAt = p3;
        float3 exitFwd = frame.forward;
        if (YapsPortalExit(socket.chain.portal, exitAt, exitFwd))
        {
#if PORTAL_STAGE == 2
            // Attribution only: the cut replaced by a token use.
            frame.position += (exitAt + exitFwd) * 1e-7;
#else
            // Margin covers the pump (up to its strength in lengths).
            float w = _YAPS_PortalSpan * worldLength * (1 + max(_YAPS_PumpStrength, 0));
            float3 PA = p3 + frame.forward * w;
            float3 PB = exitAt - exitFwd * w;
            collapsed = leftOver < 2 * w;
            portalled = !collapsed;
            // The band: a point on AB, normal and tangent along AB, so host
            // offsets stay on the line and it has no area.
            frame.position = collapsed ? (leftOver < w ? PA : PB) : exitAt;
            frame.forward = collapsed ? YapsSafeNormalize(PB - PA, exitFwd) : exitFwd;
            // Roll from the arrival, approximate.
            frame.up = YapsPerpendicular(frame.forward, frame.up);
            leftOver = collapsed ? 0 : leftOver - 2 * w;
            if (collapsed)
            {
                baked.position.xy = 0;
                baked.normal = float3(0, 0, 1);
                baked.tangent = float3(0, 0, 1);
            }
#endif
        }
    }
#endif

    float radius = 1;
    // This vertex's link, not socket.isHole. Only the last leaves remainder.
    bool isHole = linkKind > 0.5;
    if (leftOver > 0 && isHole && !portalled)
    {
        float taperFrom = worldLength * _YAPS_TaperStart;
        float taperTo = worldLength * max(_YAPS_TaperEnd, _YAPS_TaperStart + 0.001);

        // CLAMP FIRST, or vertices past a near socket fly forward at zero
        // radius and render as a flat twisted ribbon.
        leftOver = min(leftOver, taperTo);
        radius = 1 - YapsRamp(leftOver, taperFrom, taperTo);
    }

    // OVERRUN applies to BOTH kinds, rings above all: a ring has no clamp,
    // so one near the base threw every vertex past it into a flat ribbon.
    if (_YAPS_Overrun < 0.5 && !portalled)
    {
        leftOver = 0;
    }
    // SQUEEZE and BULGE from the entry: negative outside, positive through.
    // Faded by engagement so an idle plug is never pinched. Out of a portal
    // the grip is the exit's mouth.
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
        // Outside only: swelling inside reads as the shaft growing in there.
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
    // Assigned on the band: lerp(a, b, 1) can be an ulp off, a sliver.
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
