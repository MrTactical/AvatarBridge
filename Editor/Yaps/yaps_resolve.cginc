// YAPS socket resolution, for ChilloutVR.
//
// Where the socket comes from. The deform does not care, it takes a
// frame and bends toward it, so all the platform-specific awkwardness
// lives here.
//
// ---------------------------------------------------------------------
// THE ORDER, AND WHY IT IS THIS ORDER
// ---------------------------------------------------------------------
//
// Spike testing settled this the hard way, so the reasoning is worth
// keeping next to the code.
//
// ENGAGEMENT is decided by the discrete channel ALONE, contacts, through
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
//   3. CVR's own per-player position globals as the floor. Approximate , 
//      hip position with no rotation anywhere in the API, but set with
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
//
// Read for ONE purpose only: telling a wearer's own socket lights from
// everybody else's (YapsSameBodyAs), so a plug does not bend into the hip
// it grows from. Never as a target. See the note before the resolver.
float4 _CVR_PlayerHipPositions[255];
float4 CVRGlobalParams1;

struct YapsSocket
{
    float3 position;
    float3 forward;
    float3 up;
    float engaged;
    float isHole;
    // Who decided this answer: 0 nobody, 1 the contact channel, 2 a marker
    // light. Diagnostic, not behaviour, it exists because a plug bending
    // near a socket does not say WHO bent it, and a stray light reads
    // exactly like a working channel until the two are coloured apart. A
    // day was lost to precisely that.
    float tier;
};

// --- protocol lights -------------------------------------------------
//
// A socket emits a black, shadowless, vertex-only point light whose RANGE
// encodes what it is. Unity hands back attenuation rather than range, and
// range is recovered as 5/sqrt(atten).
//
// The stock DPS ordering puts fronts at 0.45 and roots at 0.41, and since
// Unity ranks vertex lights by range, every front outranks its own root
// and evicts it, with twelve sockets the four slots filled with fronts,
// which are a direction with no origin. So YAPS authors its own ordering,
// root above front.
//
// Picking the digits is more constrained than it looks. Legacy already
// speaks for almost all of them, 1 and 3 hole, 2 and 4 ring, 5 and 6
// front, 8 and 9 plug tip, and the decoder only ever looks at the second
// decimal, so 0.31 reads as a hole exactly like 0.41 does. That leaves
// precisely two free digits, 0 and 7, and root must sit above front:
//
//     root  0.4706   digit 7
//     front 0.4006   digit 0
//
// A first attempt used 0.4106 for the front, which is digit 1, legacy's
// hole root. Both YAPS lights then decoded as roots, no front was ever
// paired, and the socket had a position but no axis: the plug tracked the
// socket around but ignored its rotation entirely.
//
// The legacy values are still DECODED, so a plug still reacts to the DPS
// content already on the platform. Legacy plugs will not react to YAPS
// sockets, since 7 and 0 mean nothing to them, the price of roots that
// win their slots, and the reason emitting a legacy set as well is a
// separate opt-in.

// A root is a root however it was authored, but the legacy digits also say
// what KIND of socket it is, and that is worth keeping: a hole closes
// around the plug and stops it, a ring lets it pass straight through. The
// YAPS encoding cannot say, 0 and 7 were the only free digits and both are
// spent, so for converted sockets the kind travels on the contact channel
// instead. Whoever resolved the position decides the kind.
#define YAPS_LIGHT_NONE  0
#define YAPS_LIGHT_ROOT  1   // a root, kind unknown
#define YAPS_LIGHT_FRONT 2
#define YAPS_LIGHT_HOLE  3   // a root, and legacy says it is a hole
#define YAPS_LIGHT_RING  4   // a root, and legacy says it is a ring

inline bool YapsIsRoot(int kind)
{
    return kind == YAPS_LIGHT_ROOT || kind == YAPS_LIGHT_HOLE || kind == YAPS_LIGHT_RING;
}

inline float YapsLightRange(uint slot)
{
    float atten = unity_4LightAtten0[slot];
    return atten <= 1e-6 ? 1e6 : 5.0 * rsqrt(max(atten, 1e-8));
}

inline float3 YapsLightPosition(uint slot)
{
    return float3(unity_4LightPosX0[slot], unity_4LightPosY0[slot], unity_4LightPosZ0[slot]);
}

// Whose body is this light on, and is it the same one the plug is on?
//
// ChilloutVR publishes every player's hip position to every shader, so the
// question can simply be asked: find the player nearest the plug, find the
// player nearest the light, and see whether they are the same person. No
// identity is transmitted and nothing is spent, the answer was already
// sitting in a global array.
//
// It is a judgement rather than a fact, and it is built to fail SAFE: it
// returns "not mine" whenever it cannot tell. Discarding a light that was
// somebody else's costs a socket that should have worked; keeping one that
// was its own costs a plug bent into its wearer, which the deform recovers
// from as soon as anything better resolves. Doubt therefore keeps the light.
bool YapsSameBodyAs(float3 plugOrigin, uint slot)
{
    // No early-out for a lone player. Alone in an instance the wearer IS
    // the only body, and the inboard test below is exactly what separates
    // their own socket from a prop they are holding, bailing out here with
    // "same body" would blank every socket in the world for anyone testing
    // by themselves.
    int count = min((int) round(CVRGlobalParams1.y), 255);

    float3 lightAt = YapsLightPosition(slot);
    int nearPlug = -1, nearLight = -1;
    float bestPlug = 1e9, bestLight = 1e9;

    [loop]
    for (int i = 0; i < count; i++)
    {
        float3 hip = _CVR_PlayerHipPositions[i].xyz;
        if (dot(hip, hip) < 1e-6) continue;

        float toPlug = dot(hip - plugOrigin, hip - plugOrigin);
        if (toPlug < bestPlug) { bestPlug = toPlug; nearPlug = i; }

        float toLight = dot(hip - lightAt, hip - lightAt);
        if (toLight < bestLight) { bestLight = toLight; nearLight = i; }
    }

    // Nothing resolved: claim nothing, so a light is never discarded on the
    // strength of an answer that is not there.
    if (nearPlug < 0 || nearLight < 0)
    {
        return false;
    }
    if (nearPlug != nearLight)
    {
        return false;   // somebody else's body; plainly not ours
    }

    // Same body, but so is a prop held against your own hip, and skipping
    // those would make the whole test kit invisible to its own owner.
    //
    // What separates them is that a real socket on this body sits INBOARD
    // of the plug: a hip or a mouth is nearer that hip than the plug growing
    // out of it is. Something being pushed at the plug from outside is not.
    // So being nearest the same person is necessary and not sufficient.
    float plugToHip = dot(_CVR_PlayerHipPositions[nearPlug].xyz - plugOrigin,
                          _CVR_PlayerHipPositions[nearPlug].xyz - plugOrigin);
    return bestLight < plugToHip;
}

// The SOCKET side of the same question: is this plug's tracker light the
// wearer's OWN plug? It exists to stop a converted avatar's plug, sitting
// a hand's width from its own socket, reading as permanently inserted the
// moment the plug grew a tracker.
//
// Harder than the plug side, and this is honest about the limit. Same
// nearest-player is necessary but not sufficient: a stranger's plug that
// is inside this socket is ALSO nearest this hip, that is what inside
// means. What separates the two is where the BASE is. The wearer's plug
// base is at their crotch, a fixed short distance from their hip that does
// not change; a stranger's base, with the tip inside, is a plug length out
// on their side of the pair. So a base nearer this hip than the socket
// itself is ours; a base further out is theirs. Both bodies close together
// makes this a near thing, and doubt keeps the plug (returns false) , 
// a stranger's plug wrongly ignored is a missed effect, the wearer's plug
// wrongly kept is a socket that never closes.
bool YapsSocketOwnPlug(float3 socketWorld, uint slot)
{
    int count = min((int) round(CVRGlobalParams1.y), 255);
    float3 lightAt = YapsLightPosition(slot);
    int nearSocket = -1, nearLight = -1;
    float bestSocket = 1e9, bestLight = 1e9;

    [loop]
    for (int i = 0; i < count; i++)
    {
        float3 hip = _CVR_PlayerHipPositions[i].xyz;
        if (dot(hip, hip) < 1e-6) continue;
        float toSocket = dot(hip - socketWorld, hip - socketWorld);
        if (toSocket < bestSocket) { bestSocket = toSocket; nearSocket = i; }
        float toLight = dot(hip - lightAt, hip - lightAt);
        if (toLight < bestLight) { bestLight = toLight; nearLight = i; }
    }
    if (nearSocket < 0 || nearLight < 0) return false;
    if (nearSocket != nearLight) return false;
    return bestLight < bestSocket;
}

int YapsClassifyLight(uint slot, float3 plugOrigin)
{
    float range = YapsLightRange(slot);
    if (range >= 0.5) return YAPS_LIGHT_NONE;

    // A protocol light is authored black; anything carrying colour is
    // somebody's actual lighting.
    float4 colour = unity_LightColor[slot];
    if (any(colour.rgb > 0.0001) && colour.a > 0) return YAPS_LIGHT_NONE;

    // The wearer's own sockets, skipped before anything else. They are
    // permanently in reach and permanently nearest, so without this the
    // plug never looks at anyone else.
    //
    // Ownership is decided by the PLAYER POSITIONS alone. It used to be
    // decided by a digit stamped into the range's fourth decimal, and that
    // was built on precision nobody had measured: the spike verified the
    // SECOND decimal survives the round trip, and the range is not read
    // directly but reconstructed as 5·rsqrt(atten) from an attenuation
    // uniform. The fourth decimal does not come back reliably.
    //
    // It showed as a socket that worked or did not depending on which digit
    // its range happened to land on. A prop authored at 0.4206, fourth
    // decimal 6, was being skipped by a plug whose tag was 3, because the
    // number arriving in the shader was nearer 0.4203.
    //
    // So the digit is gone. _YAPS_SelfTag is now only a flag: zero or more
    // means "this plug is on an avatar that also has sockets, so check",
    // and -1 means there is nothing to check for.
    if (_YAPS_SelfTag >= 0 && YapsSameBodyAs(plugOrigin, slot))
    {
        return YAPS_LIGHT_NONE;
    }

    int digit = (int) round(fmod(range, 0.1) * 100.0);

    // Ours first: the two digits legacy never claimed.
    if (digit == 7) return YAPS_LIGHT_ROOT;
    if (digit == 0) return YAPS_LIGHT_FRONT;

    // Legacy DPS, so a plug reacts to content already on the platform , 
    // and legacy is more specific than YAPS can be, saying hole or ring in
    // the digit itself.
    if (digit == 1 || digit == 3) return YAPS_LIGHT_HOLE;
    if (digit == 2 || digit == 4) return YAPS_LIGHT_RING;
    if (digit == 5 || digit == 6) return YAPS_LIGHT_FRONT;   // front
    // 8 and 9 are a legacy plug's own tip light, not a socket. Ignoring
    // them stops one plug mistaking another plug for somewhere to go.
    return YAPS_LIGHT_NONE;
}

// Nearest root to the plug, with its front partner if one arrived. Unity
// may hand over a root without its front, so an unpaired root still yields a
// position and simply leaves the axis to the caller.
bool YapsFindLightSocket(float3 plugOrigin, float reach, out float3 position, out float3 forward,
                         out float holeHint)
{
    position = 0;
    forward = 0;
    holeHint = -1;   // the light did not say
    float bestDistanceSq = reach * reach;
    bool found = false;

    [unroll]
    for (uint i = 0; i < 4; i++)
    {
        int kind = YapsClassifyLight(i, plugOrigin);
        if (!YapsIsRoot(kind)) continue;
        float3 at = YapsLightPosition(i);
        float distanceSq = dot(at - plugOrigin, at - plugOrigin);
        if (distanceSq >= bestDistanceSq) continue;
        bestDistanceSq = distanceSq;
        position = at;
        found = true;
        holeHint = kind == YAPS_LIGHT_HOLE ? 1 : (kind == YAPS_LIGHT_RING ? 0 : -1);

        // Its front light, if present, sits about a centimetre away along
        // the socket axis. That is a very short baseline to derive a
        // direction from, so it is taken only when unambiguous and the
        // caller falls back to the approach direction otherwise.
        forward = 0;
        [unroll]
        for (uint j = 0; j < 4; j++)
        {
            if (YapsClassifyLight(j, plugOrigin) != YAPS_LIGHT_FRONT) continue;
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

// --- what the player globals are NOT for -------------------------------
//
// There used to be a fourth tier here: when nothing else resolved, aim the
// plug at the nearest player's body from ChilloutVR's per-player position
// arrays. It was removed on 2026-08-15, deliberately and permanently, and
// this comment exists so nobody rebuilds it as an "improvement".
//
// The plug bends only toward a socket it can resolve: a contact channel
// written by a socket, a marker light emitted by a socket. Nothing else. A
// body position is not a socket, nobody authored it and nothing on the
// wearer's side switches it off, so it is not a target here, and a plug
// with no socket in range stays exactly as it was.
//
// The one thing the player positions ARE still read for is YapsSameBodyAs
// above, and it goes the other way: it REJECTS a wearer's own socket lights
// so a plug does not bend into the hip it is growing out of. It never aims
// at anyone. Keep that distinction if this file is ever touched again.

// --- the resolution --------------------------------------------------

YapsSocket YapsResolveSocket(float3 plugOrigin, float3 plugForward, float3 plugUp, float worldLength)
{
    YapsSocket socket;

    // Engagement and flags: the discrete channel, always, alone.
    socket.engaged = saturate(_YAPS_SocketFlags.x);
    socket.isHole = _YAPS_SocketFlags.y;

    // TAG FILTER, from SPS. The channel may carry the socket's tag as a
    // small integer in the flags' z. A plug that refuses that tag, or
    // requires a different one, ignores the socket outright, engagement
    // goes to zero and the light refinement below has nothing to sharpen.
    // Zero on either knob means no filter; a socket carrying no tag (0)
    // is only ever refused by a REQUIRE, never by an exclude, since there
    // is nothing to match against. Marker lights carry no tag and cannot
    // be filtered, which is why this sits on the channel's answer alone.
    float socketTag = round(_YAPS_SocketFlags.z);
    float wantTag = round(_YAPS_TagInclude);
    float banTag = round(_YAPS_TagExclude);
    if ((banTag > 0.5 && socketTag > 0.5 && abs(socketTag - banTag) < 0.5)
        || (wantTag > 0.5 && abs(socketTag - wantTag) > 0.5))
    {
        socket.engaged = 0;
    }
    socket.position = _YAPS_SocketPos.xyz;
    socket.forward = _YAPS_SocketForward.xyz;
    socket.up = _YAPS_SocketUp.xyz;
    socket.tier = 0;

    // A zero position is NOT a socket at the world origin, however much it
    // looks like one to the maths. Track whether anything actually
    // resolved, or a plug on an avatar whose channel is quiet will spend
    // its life reaching for world zero.
    bool found = dot(socket.position, socket.position) > 1e-6;

    // ChilloutVR's contact channel speaks only in the receiver's own frame,
    // normalised per axis across its box, so a converted avatar sends the
    // socket's offset from the plug rather than a world position. Rebuild
    // it here, against the frame the deform has just recovered.
    //
    // That constraint turns out to suit the transport: what crosses the
    // wire is the gap between two bodies already touching, which barely
    // moves, rather than a world position that changes every time either
    // of them walks. A centre reading is a real reading, not silence, so
    // engagement decides whether anything arrived, the zero test above
    // cannot.
    if (_YAPS_ChannelSpace > 0.5)
    {
        found = socket.engaged > 0;
        // The trigger boxes ride the plug object, so a scaled plug has
        // scaled boxes; the decode scales with them.
        float3 offset = (_YAPS_SocketPos.xyz * 2 - 1) * _YAPS_ChannelExtents.xyz * max(_YAPS_BakeScale, 0.0001);
        float3 plugRight = cross(plugUp, plugForward);
        socket.position = plugOrigin + plugRight * offset.x
                                     + plugUp * offset.y
                                     + plugForward * offset.z;

        // The channel's engagement is a PROXIMITY reading taken across the
        // whole trigger sphere, and that sphere is 1.75 plug lengths, so
        // with the socket at the tip it reads about 0.4, where the light
        // path reads 1, and the plug bends roughly half as far for exactly
        // the same arrangement.
        //
        // So keep it as the thing it is good at, a gate, meaning something
        // is in range and the reconstruction below is real, and take the
        // curve itself from the position, by the same formula the light
        // path uses. Two routes to the same socket then cannot disagree
        // about how far in it is, which they have no business doing.
        if (socket.engaged > 0)
        {
            float channelGap = length(socket.position - plugOrigin);
            socket.engaged = 1 - smoothstep(worldLength, worldLength * 1.6, channelGap);

            // The remap doubles as the channel's own reality check, and it
            // has to feed back into "found" or it silently makes things
            // worse. A trigger reporting "in range" while its position axes
            // still sit at their default puts the socket in the CORNER of
            // the box, three plug lengths out, and engagement correctly
            // collapses to zero, but the position it computed on the way
            // there is what the light refinement below gets tested against.
            // Left saying "found", a half-delivered channel therefore
            // rejects the very light that would have rescued it, and the
            // plug ignores a socket it can plainly see.
            //
            // Proximity arriving without position is not a resolved socket.
            // Say nothing and let the other tiers answer.
            found = socket.engaged > 0;
        }

        // Which way the socket FACES, from the second point it already
        // publishes. Without this the channel hands over a bare point, the
        // deform can only aim at it, and the plug reaches the socket
        // instead of threading it, visibly worse than the light path,
        // which gets a root and a front and therefore an axis.
        //
        // Believed only when it is PLAUSIBLE. Both ecosystems put the
        // second point about a centimetre from the first, so a gap much
        // larger than that is not a socket's axis: it is the value sitting
        // at its default because no front was in range, or a front
        // belonging to a different socket than the root did. Either way the
        // honest answer is to say nothing and let the deform take its
        // direction from the approach, which is what a zero forward means.
        float3 frontOffset = (_YAPS_SocketFront.xyz * 2 - 1) * _YAPS_ChannelExtents.xyz * max(_YAPS_BakeScale, 0.0001);
        float3 frontAt = plugOrigin + plugRight * frontOffset.x
                                    + plugUp * frontOffset.y
                                    + plugForward * frontOffset.z;
        float3 axis = frontAt - socket.position;
        float axisLength = length(axis);
        if (axisLength > 1e-5 && axisLength < worldLength * 0.5)
        {
            socket.forward = axis / axisLength;
        }
    }

    // What the CHANNEL resolved. The light refinement below is allowed to
    // sharpen this answer but not to replace it with a different socket,
    // and telling those apart needs the original to compare against.
    bool channelFound = found;
    float3 channelPosition = socket.position;
    if (channelFound)
    {
        socket.tier = 1;
    }

    // There is deliberately NO floor here. If neither the channel nor a
    // light has named a socket, nothing has, and the plug stays as it is.
    // See "what the player globals are NOT for" above.

    // Refinement: a protocol light close to where the resolver already believes the
    // socket to be. It only sharpens the position, it can never switch
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
    float lightHoleHint;
    bool litRoot = YapsFindLightSocket(plugOrigin, worldLength * 1.6, lightPosition, lightForward,
                                       lightHoleHint);
    // A light refines the socket the channel already resolved. It does not
    // get to nominate a DIFFERENT one, and it used to: any lit root inside
    // the envelope replaced the channel's position outright. A contact-only
    // socket therefore appeared broken whenever a lit socket was anywhere
    // nearby, the channel engaged on the one being used, and the position
    // came from the one carrying lights. Blue and orange are the sockets
    // this ruins, because they emit no light of their own to win it back.
    //
    // So accept the light only when it is close enough to BE the socket the
    // channel is reporting. Rejecting a good light costs a little precision
    // and nothing else, since the channel's own position is already correct;
    // accepting a wrong one bends the plug at another socket entirely, which
    // is why the test is deliberately tight.
    if (litRoot && channelFound)
    {
        float3 delta = lightPosition - channelPosition;
        litRoot = dot(delta, delta) < worldLength * worldLength * 0.25;
    }

    if (litRoot)
    {
        socket.position = lightPosition;
        found = true;
        // A light that only SHARPENED the channel's answer leaves the tier
        // saying channel, the channel engaged it, the light polished it.
        // A light standing in for a silent channel owns the answer.
        if (!channelFound)
        {
            socket.tier = 2;
        }
        // Whoever resolved the position decides the kind. A legacy light
        // states outright whether it is a hole or a ring, and it is
        // describing the very socket now being aimed at, which the
        // channel's flag may not be, since the channel reports whatever
        // last entered the trigger box.
        if (lightHoleHint >= 0)
        {
            socket.isHole = lightHoleHint;
        }
        if (dot(lightForward, lightForward) > 1e-6)
        {
            socket.forward = lightForward;
        }
    }

    // Legacy content has no contacts to engage from. ChilloutVR's existing
    // DPS sockets, on avatars and on spawned props alike, announce
    // themselves with marker lights and nothing else, so a plug that
    // insisted on the contact channel would never react to any of it. That
    // is most of the platform's existing content, and refusing it is worse
    // than the compromise.
    //
    // The compromise: engage on distance to a light-resolved root, but only
    // once nothing else has engaged, and only within about a plug length.
    // Engagement decided from a light is engagement decided per camera,
    // which is the thing this file otherwise refuses to do, bounded here
    // because the divergence is a range effect. A light sitting centimetres
    // from the plug is inside any frustum already drawing the plug; one
    // across the room is not, and that is where mirrors and the direct view
    // disagreed in testing.
    if (socket.engaged <= 0 && litRoot)
    {
        // Measured against the TIP, not the root. The gap is from the
        // plug's base, and the tip is already a whole length out, so a
        // window of 0.9 to 1.3 lengths only ever engaged a socket within a
        // few centimetres of the tip, and standing just outside it gave
        // nothing at all while moving back and forth flickered in and out
        // of it. Full engagement once the socket is at the tip, fading over
        // a further half length, which is also where the light search stops
        // looking.
        float gap = length(socket.position - plugOrigin);
        socket.engaged = 1 - smoothstep(worldLength, worldLength * 1.6, gap);
        // Whoever provides the ENGAGEMENT owns the answer, whatever set the
        // position before this.
        socket.tier = 2;
    }

    // Only a socket clearly BEHIND the base is refused. Anything from
    // dead ahead to square beside it engages in full; past that it fades
    // out by about a hundred and twenty degrees. That is enough to stop
    // the plug folding back on itself, and it still lets one reach up.
    if (socket.engaged > 0)
    {
        float3 toSocket = socket.position - plugOrigin;
        float gapAhead = length(toSocket);
        if (gapAhead > 0.0001)
        {
            float ahead = dot(toSocket / gapAhead, plugForward);
            socket.engaged *= smoothstep(-0.5, 0.0, ahead);
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
