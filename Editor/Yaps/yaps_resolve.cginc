// YAPS socket resolution, for ChilloutVR.
//
// Where the socket comes from. The deform does not care — it takes a
// frame and bends toward it — so all the platform-specific awkwardness
// lives here.
//
// ---------------------------------------------------------------------
// THE ORDER, AND WHY IT IS THIS ORDER
// ---------------------------------------------------------------------
//
// Spike testing settled this the hard way, so the reasoning is worth
// keeping next to the code.
//
// ENGAGEMENT is decided by the discrete channel ALONE — contacts, through
// animator parameters, through CVRMaterialDriver, into material vectors.
// It is never derived from whether a light happens to be visible. This is
// the single most important rule here: Unity fills the vertex light slots
// PER CAMERA, and ChilloutVR's mirrors additionally zero the pixel light
// count while they render, so anything decided from light presence
// differs between the mirror, the third-person camera and the direct
// view. A deform that disagreed with itself per camera would bend one way
// in a mirror and another in reality.
//
// POSITION then comes from the best source available, but only ever
// within that already-decided engagement:
//
//   1. The discrete channel itself. Exact, rotation-aware, and identical
//      on every camera and every viewer because a material property is
//      not per-camera state. Its ceiling is 10 Hz, so it carries the
//      socket's near-static offset rather than a fast-moving world
//      position.
//   2. Protocol lights, as a refinement at contact range. Frame-accurate
//      and free, since the socket avatar already emits them. Bounded to
//      close range deliberately: the per-camera problem only expresses
//      itself at distance, because a light sitting centimetres from the
//      plug is inside any frustum that is drawing the plug at all.
//   3. CVR's own per-player position globals as the floor. Approximate —
//      hip position with no rotation anywhere in the API — but set with
//      SetGlobalVectorArray, so identical in every pass, every eye and
//      every camera, and refreshed each frame from the same interpolated
//      pose the remote avatar is rendered from. It cannot disagree with
//      what a viewer sees.
//
#ifndef YAPS_RESOLVE_INCLUDED
#define YAPS_RESOLVE_INCLUDED

#include "yaps_props.cginc"

// CVR publishes these for every player in the instance, on every client.
// Declared at the client's capacity: Unity locks an array's size at first
// bind, so matching it avoids a silent mismatch.
float4 _CVR_PlayerHipPositions[255];
float4 _CVR_PlayerChestPositions[255];
float4 CVRGlobalParams1;

struct YapsSocket
{
    float3 position;
    float3 forward;
    float3 up;
    float engaged;
    float isHole;
};

// --- protocol lights -------------------------------------------------
//
// A socket emits a black, shadowless, vertex-only point light whose RANGE
// encodes what it is. Unity hands back attenuation rather than range, and
// range is recovered as 5/sqrt(atten).
//
// We author the INVERTED encoding: roots at 0.4906/0.4806 and fronts at
// 0.4106. The stock DPS ordering puts fronts at 0.45 and roots at 0.41,
// and since Unity ranks vertex lights by range, every front outranked its
// own root and evicted it — with twelve sockets the four slots filled
// with fronts, which are a direction with no origin. Inverting the
// ordering makes roots win the slots they need to win.
//
// The legacy values are still DECODED, so a plug still reacts to the DPS
// content already on the platform.

#define YAPS_LIGHT_NONE  0
#define YAPS_LIGHT_ROOT  1
#define YAPS_LIGHT_FRONT 2

inline float YapsLightRange(uint slot)
{
    float atten = unity_4LightAtten0[slot];
    return atten <= 1e-6 ? 1e6 : 5.0 * rsqrt(max(atten, 1e-8));
}

inline float3 YapsLightPosition(uint slot)
{
    return float3(unity_4LightPosX0[slot], unity_4LightPosY0[slot], unity_4LightPosZ0[slot]);
}

int YapsClassifyLight(uint slot)
{
    float range = YapsLightRange(slot);
    if (range >= 0.5) return YAPS_LIGHT_NONE;

    // A protocol light is authored black; anything carrying colour is
    // somebody's actual lighting.
    float4 colour = unity_LightColor[slot];
    if (any(colour.rgb > 0.0001) && colour.a > 0) return YAPS_LIGHT_NONE;

    int digit = (int) round(fmod(range, 0.1) * 100.0);
    if (digit == 9 || digit == 8) return YAPS_LIGHT_ROOT;    // ours
    if (digit == 1 || digit == 2) return YAPS_LIGHT_ROOT;    // legacy hole/ring
    if (digit == 5) return YAPS_LIGHT_FRONT;                 // legacy front
    return YAPS_LIGHT_NONE;
}

// Nearest root to the plug, with its front partner if one arrived. Unity
// may hand us a root without its front, so an unpaired root still yields a
// position and simply leaves the axis to the caller.
bool YapsFindLightSocket(float3 plugOrigin, float reach, out float3 position, out float3 forward)
{
    position = 0;
    forward = 0;
    float bestDistanceSq = reach * reach;
    bool found = false;

    [unroll]
    for (uint i = 0; i < 4; i++)
    {
        if (YapsClassifyLight(i) != YAPS_LIGHT_ROOT) continue;
        float3 at = YapsLightPosition(i);
        float distanceSq = dot(at - plugOrigin, at - plugOrigin);
        if (distanceSq >= bestDistanceSq) continue;
        bestDistanceSq = distanceSq;
        position = at;
        found = true;

        // Its front light, if present, sits about a centimetre away along
        // the socket axis. That is a very short baseline to derive a
        // direction from, so it is taken only when unambiguous and the
        // caller falls back to the approach direction otherwise.
        forward = 0;
        [unroll]
        for (uint j = 0; j < 4; j++)
        {
            if (YapsClassifyLight(j) != YAPS_LIGHT_FRONT) continue;
            float3 front = YapsLightPosition(j);
            float3 offset = front - at;
            float offsetSq = dot(offset, offset);
            if (offsetSq > 1e-8 && offsetSq < 0.01)
            {
                forward = normalize(offset);
            }
        }
    }
    return found;
}

// --- player globals --------------------------------------------------

// Nearest player's hip, biased a little toward the chest so the target
// sits on the body rather than inside the pelvis. No rotation exists in
// the API, so the axis is left to the caller's approach direction.
bool YapsFindGlobalSocket(float3 plugOrigin, float reach, out float3 position)
{
    position = 0;
    float bestDistanceSq = reach * reach;
    bool found = false;
    int count = min((int) round(CVRGlobalParams1.y), 255);

    [loop]
    for (int i = 0; i < count; i++)
    {
        float3 hip = _CVR_PlayerHipPositions[i].xyz;
        if (dot(hip, hip) < 1e-6) continue;   // slot not written
        float3 chest = _CVR_PlayerChestPositions[i].xyz;
        float3 at = dot(chest, chest) > 1e-6 ? lerp(hip, chest, 0.15) : hip;

        float distanceSq = dot(at - plugOrigin, at - plugOrigin);
        if (distanceSq >= bestDistanceSq) continue;
        bestDistanceSq = distanceSq;
        position = at;
        found = true;
    }
    return found;
}

// --- the resolution --------------------------------------------------

YapsSocket YapsResolveSocket(float3 plugOrigin, float worldLength)
{
    YapsSocket socket;

    // Engagement and flags: the discrete channel, always, alone.
    socket.engaged = saturate(_YAPS_SocketFlags.x);
    socket.isHole = _YAPS_SocketFlags.y;
    socket.position = _YAPS_SocketPos.xyz;
    socket.forward = _YAPS_SocketForward.xyz;
    socket.up = _YAPS_SocketUp.xyz;

    // A zero position is NOT a socket at the world origin, however much it
    // looks like one to the maths. Track whether anything actually
    // resolved, or a plug on an avatar whose channel is quiet will spend
    // its life reaching for world zero.
    bool found = dot(socket.position, socket.position) > 1e-6;

    // Floor: if nothing has written a position, aim at the nearest body.
    if (!found)
    {
        float3 fromGlobals;
        if (YapsFindGlobalSocket(plugOrigin, worldLength * 2, fromGlobals))
        {
            socket.position = fromGlobals;
            socket.forward = 0;   // caller derives it from the approach
            found = true;
        }
    }

    // Refinement: a protocol light close to where we already believe the
    // socket to be. It only sharpens the position — it can never switch
    // the deform on or off, because light visibility is per camera and
    // engagement must not be.
    //
    // Searched across the whole engagement envelope rather than a tighter
    // "contact range". Anything beyond 1.6 plug lengths has zero
    // engagement anyway, so a shorter reach buys nothing and makes the
    // light path wake up abruptly on contact instead of easing in the way
    // the discrete path does.
    float3 lightPosition;
    float3 lightForward;
    if (YapsFindLightSocket(plugOrigin, worldLength * 1.6, lightPosition, lightForward))
    {
        socket.position = lightPosition;
        found = true;
        if (dot(lightForward, lightForward) > 1e-6)
        {
            socket.forward = lightForward;
        }
    }

    // Nothing to bend toward. Say so, rather than bending toward nothing.
    if (!found)
    {
        socket.engaged = 0;
    }

    return socket;
}

#endif
