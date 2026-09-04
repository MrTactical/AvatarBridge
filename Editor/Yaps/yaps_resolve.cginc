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
// ENGAGEMENT is decided by the discrete channel wherever there is one:
// contacts, through animator parameters, through CVRMaterialDriver, into
// material vectors. A light engages only where no channel reached the
// plug at all, close in and as a last resort, because a socket with no
// channel would otherwise be findable by nothing (see LIGHT FALLBACK at
// the end of this file). Prefer the channel for this reason: Unity fills
// the vertex light slots
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
#include "yaps_atlas.cginc"

// CVR publishes these for every player in the instance, on every client.
// Declared at the client's capacity: Unity locks an array's size at first
// bind, so matching it avoids a silent mismatch.
//
// Read for ONE purpose only: telling a wearer's own socket lights from
// everybody else's (YapsSameBodyAs), so a plug does not bend into the hip
// it grows from. Never as a target. See the note before the resolver.
float4 _CVR_PlayerHipPositions[255];
float4 CVRGlobalParams1;

inline float3 YapsNormalizeOr(float3 v, float3 fallback)
{
    float lengthSq = dot(v, v);
    return lengthSq < 1e-12 ? fallback : v * rsqrt(lengthSq);
}

// --- the screen atlas ------------------------------------------------
//
// A LIST, NOT A WINNER. The rest of this file picks the nearest socket and
// throws the neighbourhood away, which makes a plug flip between two sockets
// as whichever is momentarily closer to its ROOT changes, and lets a socket
// BEHIND it beat one in front, because distance-to-root never asks which way
// a plug points.
//
// The atlas hands back an ordered list instead, and the plug becomes a path
// rather than an aim: each socket claims a range of arc length along the
// shaft. A ring mid-shaft and a hole at the tip are then two entries, a
// portal is two entries with a gap between their ranges, and a duplicate is
// one range mapped twice. Only the list and the ranges are built here; what
// walks them belongs to the deform.
#define YAPS_CHAIN_MAX 4

struct YapsChain
{
    float3 position[YAPS_CHAIN_MAX];
    float3 forward[YAPS_CHAIN_MAX];
    float  kind[YAPS_CHAIN_MAX];      // 0 ring, 1 hole
    float  arc[YAPS_CHAIN_MAX + 1];   // where each socket sits along the shaft
    int    count;
    float  engaged;
    float  headers;   // cells whose header said something was there
    float  hits;      // payloads that then matched the cell's tag
};

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
    // How many of the atlas's 27 header taps said a cell held anything.
    // Diagnostic only, and read as a LADDER rather than a count: each step
    // is a different half of the search, and the step it stops on is the
    // stage that threw the socket away.
    float atlasHeaders;
    float atlasHits;
    // The WHOLE ordered chain, not only the link this struct's position
    // names. A shaft that passes a ring on its way to a hole has to follow
    // both, and the deform picks a link per vertex by arc length. count is
    // 0 for every source but the atlas, and 0 means the fields above are
    // the only answer there is.
    YapsChain chain;
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
// The anchor is where the WEARER is, not where the socket is. The two
// differ on purpose: a hand socket can sit in somebody else's lap, and
// judged from there the nearest hip is theirs and their plug reads as
// their own and gets ignored, which is the one plug it exists for. The
// caller passes its mesh origin, which for a body mesh is the avatar
// root beside the wearer's hip.
//
// nearest-player is necessary but not sufficient: a stranger's plug that
// is inside this socket is ALSO nearest this hip, that is what inside
// means. What separates the two is where the BASE is. The wearer's plug
// base is at their crotch, a fixed short distance from their hip that does
// not change; a stranger's base, with the tip inside, is a plug length out
// on their side of the pair. So a base nearer this hip than the anchor
// is ours; a base further out is theirs. Both bodies close together
// makes this a near thing, and doubt keeps the plug (returns false) ,
// a stranger's plug wrongly ignored is a missed effect, the wearer's plug
// wrongly kept is a socket that never closes.
bool YapsSocketOwnPlug(float3 ownerAnchor, uint slot)
{
    int count = min((int) round(CVRGlobalParams1.y), 255);
    float3 lightAt = YapsLightPosition(slot);
    int nearAnchor = -1, nearLight = -1;
    float bestAnchor = 1e9, bestLight = 1e9;

    [loop]
    for (int i = 0; i < count; i++)
    {
        float3 hip = _CVR_PlayerHipPositions[i].xyz;
        if (dot(hip, hip) < 1e-6) continue;
        float toAnchor = dot(hip - ownerAnchor, hip - ownerAnchor);
        if (toAnchor < bestAnchor) { bestAnchor = toAnchor; nearAnchor = i; }
        float toLight = dot(hip - lightAt, hip - lightAt);
        if (toLight < bestLight) { bestLight = toLight; nearLight = i; }
    }
    if (nearAnchor < 0 || nearLight < 0) return false;
    if (nearAnchor != nearLight) return false;
    return bestLight < bestAnchor;
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
// preferNear: the socket the channel already resolved, when it resolved
// one, else the plug's own origin.
//
// Ranking by distance to the PLUG picked the wrong light on a prop
// carrying two sockets. A hole and a ring six centimetres apart are both
// inside the envelope, and which one sits nearer the plug's origin flips
// as the plug moves in — so a plug genuinely inside the hole was handed
// the ring's light, took its kind, and swept past instead of entering.
// It changed with viewing distance because the geometry and Unity's
// four-slot light list both change as you approach, which reads as a
// socket that breaks when you look at it.
//
// The reach gate still measures from the plug, because that is the
// engagement envelope. Only the ranking moved. The caller's guard below
// stays, demoted from the only defence to a sanity check.
bool YapsFindLightSocket(float3 plugOrigin, float3 preferNear, float reach,
                         out float3 position, out float3 forward,
                         out float holeHint)
{
    position = 0;
    forward = 0;
    holeHint = -1;   // the light did not say
    float bestRankSq = 1e30;
    bool found = false;

    [unroll]
    for (uint i = 0; i < 4; i++)
    {
        int kind = YapsClassifyLight(i, plugOrigin);
        if (!YapsIsRoot(kind)) continue;
        float3 at = YapsLightPosition(i);
        float fromPlugSq = dot(at - plugOrigin, at - plugOrigin);
        if (fromPlugSq >= reach * reach) continue;
        float rankSq = dot(at - preferNear, at - preferNear);
        if (rankSq >= bestRankSq) continue;
        bestRankSq = rankSq;
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


// The sort below moves and places whole entries by literal index.
#define YAPS_CH_MOVE(a, b) sockD[a] = sockD[b]; sockP[a] = sockP[b]; sockF[a] = sockF[b]; sockK[a] = sockK[b];
#define YAPS_CH_PUT(a) sockD[a] = d; sockP[a] = at; sockF[a] = fwd; sockK[a] = kind;

// Reads the neighbourhood of the shaft's MIDPOINT rather than its root, so a
// radius-1 read covers the whole plug instead of only the half nearest the
// body. One line, no extra taps.
YapsChain YapsResolveChain(float3 root, float3 axis, float worldLength)
{
    // Zeroed in one go rather than field by field: shorter, and it stops the
    // compiler reading the early return below as leaving the struct
    // part-written. Nothing reads an entry past count, so the zeroed forward
    // never reaches anybody.
    YapsChain chain = (YapsChain)0;

    float len = max(worldLength, 1e-4);

    // WHICH LEVEL. The atlas carries several cell sizes at once, each four
    // times the last, because one cell size cannot serve a twenty centimetre
    // plug and a twenty metre one. Coverage wants a cell of about L/2r, so
    // read only the level nearest that. Costs no extra taps: the socket
    // published to every level and this reads one.
    float wantCell = len / max(2.0 * YAPS_ATLAS_RADIUS, 1.0);
    int lvl = clamp(int(round(log2(wantCell / YAPS_ATLAS_CELL) * 0.5)), 0, YAPS_ATLAS_LEVELS - 1);
    float size = max(YAPS_ATLAS_CELL * pow(4.0, lvl), 1e-6);
    int total = YAPS_ATLAS_GRID * YAPS_ATLAS_GRID;

    // Where engagement reaches zero, and the inclusion test for the list: a
    // socket the plug cannot reach is not part of the path it takes. It is
    // also what rejects a hash collision from across the world, which decodes
    // to a plausible payload in an implausible place.
    float far = len * YAPS_ATLAS_REACH * 1.6;

    // Spelled out rather than looped, so the compiler can see every entry is
    // written before anything reads one. A distance of 1e9 and a kind of -1
    // are what "empty" means below.
    float3 sockP[YAPS_CHAIN_MAX] = { (float3)0, (float3)0, (float3)0, (float3)0 };
    float3 sockF[YAPS_CHAIN_MAX] = { float3(0, 0, 1), float3(0, 0, 1), float3(0, 0, 1), float3(0, 0, 1) };
    float  sockD[YAPS_CHAIN_MAX] = { 1e9, 1e9, 1e9, 1e9 };
    float  sockK[YAPS_CHAIN_MAX] = { -1, -1, -1, -1 };

    int3 mine = int3(floor((root + axis * (len * 0.5)) / size));

    [loop] for (int dx = -YAPS_ATLAS_RADIUS; dx <= YAPS_ATLAS_RADIUS; dx++)
    [loop] for (int dy = -YAPS_ATLAS_RADIUS; dy <= YAPS_ATLAS_RADIUS; dy++)
    [loop] for (int dz = -YAPS_ATLAS_RADIUS; dz <= YAPS_ATLAS_RADIUS; dz++)
    {
        int3 c = mine + int3(dx, dy, dz);
        float tagWant = YapsAtlasTag(c);

        int idx = YapsAtlasHash(c) % total;
        if (idx < 0) idx += total;
        int step = YapsAtlasHash2(c) % max(total - 1, 1);
        if (step < 0) step += max(total - 1, 1);
        int idxB = (idx + step + 1) % total;

        [loop] for (int home = 0; home < 2; home++)
        {
            int use = home == 0 ? idx : idxB;
            int cellX, fromTop;
            YapsAtlasCellPixels(use, lvl, cellX, fromTop);
            int cellY = YapsAtlasRow(fromTop);

            // ONE tap for the header, whose alpha COUNTS how many sockets sit
            // in this slot. Nearly every cell holds nothing and that case
            // costs exactly this, which is what makes eight buckets
            // affordable. A count rather than a bitmask of live octants: a
            // mask is built by adding bits and 1+1 is 2, so two sockets in
            // one octant unset the octant that exists and set one that does
            // not.
            int held = int(round(YAPS_ATLAS_LOAD(cellX, cellY).a * 255.0));
            if (held > 0) chain.headers += 1;
            if (held == 0) break;   // home 1 is always written, so home 2
                                    // cannot hold what home 1 does not
            bool got1 = false;
            [loop] for (int sub = 0; sub < 8; sub++)
            {
                int px = cellX + (1 + 2 * sub) * YAPS_ATLAS_SLOTPX;
                float4 got = YAPS_ATLAS_LOAD(px, cellY);
                if (got.a < 0.5) continue;
                // The header is a SUM across every cell sharing this slot, so
                // it can advertise octants belonging to somebody else. The
                // tag is what settles it, and the protocol version rides in
                // it, so a socket from another version fails here.
                if (abs((got.a - 0.5) * 2 - tagWant) > 0.001) continue;
                got1 = true;
                chain.hits += 1;

                float3 at = (float3(c) + got.rgb) * size;
                float d = distance(at, root);

                float4 f4 = YAPS_ATLAS_LOAD(px + YAPS_ATLAS_SLOTPX, cellY);
                float3 fwd = normalize(f4.rgb * 2 - 1);
                float kind = round(f4.a * 16.0) - 1;

                // No facing test. There used to be one here, rejecting a
                // hole whose forward pointed the way the plug was going, and
                // it was wrong twice over: the deform already flips the
                // socket axis to meet the approach precisely because a
                // converter inherits whatever convention the original avatar
                // used, and the two halves disagreeing meant a correctly
                // aimed hole was thrown away before the deform ever saw it.
                // Range is what says a socket is not this plug's business.
                if (d > far) continue;

                // Insertion sort, nearest first. Sorted here rather than
                // later because THE ORDER IS THE PATH: socket one is the one
                // the shaft meets first.
                //
                // Written out rather than looped. These four arrays only stay
                // in registers while every index is a compile-time constant,
                // and a loop that breaks on a comparison leaves its counter
                // data-dependent, so the compiler cannot unroll it and then
                // refuses the write it has turned into a dynamic one. Four
                // entries is few enough to spell, and this is the whole
                // reason YAPS_CHAIN_MAX is not a knob.
                if (d < sockD[0])
                {
                    YAPS_CH_MOVE(3, 2) YAPS_CH_MOVE(2, 1) YAPS_CH_MOVE(1, 0) YAPS_CH_PUT(0)
                }
                else if (d < sockD[1])
                {
                    YAPS_CH_MOVE(3, 2) YAPS_CH_MOVE(2, 1) YAPS_CH_PUT(1)
                }
                else if (d < sockD[2])
                {
                    YAPS_CH_MOVE(3, 2) YAPS_CH_PUT(2)
                }
                else if (d < sockD[3])
                {
                    YAPS_CH_PUT(3)
                }
            }
            // Found in this home, so the other is not this cell's. Only a
            // cell whose first home was taken by somebody else falls through.
            if (got1) break;
        }
    }

    int count = 0;
    [unroll] for (int i2 = 0; i2 < YAPS_CHAIN_MAX; i2++)
        if (sockK[i2] > -0.5) count++;
    chain.count = count;

    // Chords rather than true cubic arc length, the same approximation the
    // single-socket path makes.
    chain.arc[0] = 0;
    float3 prev = root;
    [unroll] for (int i3 = 0; i3 < YAPS_CHAIN_MAX; i3++)
    {
        float3 seg = sockP[i3] - prev;
        float d3 = length(seg);
        // Every socket is turned to meet its approach, ring and hole alike,
        // which is the same law the deform applies to a lone socket. Trusting
        // the sign makes the path arrive travelling backwards and tie itself
        // in a hairpin. It used to be ring-only, because a hole aimed away
        // was supposed to have been rejected before reaching here, and that
        // rejection is gone: a converter inherits whatever convention the
        // original avatar used, so an aimed-away hole is usually a correctly
        // placed one with the other convention.
        if (d3 > 1e-5 && dot(sockF[i3], seg) > 0)
            sockF[i3] = -sockF[i3];
        // Entries past the count get a range nothing can fall inside rather
        // than a branch that skips them. A runtime-dependent continue is what
        // stopped this loop unrolling, and it has to unroll for the indices to
        // stay constant, same rule as the sort above.
        chain.arc[i3 + 1] = chain.arc[i3] + (i3 < count ? d3 : 1e6);
        prev = sockP[i3];

        chain.position[i3] = sockP[i3];
        chain.forward[i3] = sockF[i3];
        chain.kind[i3] = sockK[i3];
    }

    chain.engaged = 1 - smoothstep(len * YAPS_ATLAS_REACH, far, sockD[0]);
    return chain;
}

YapsSocket YapsResolveSocket(float3 plugOrigin, float3 plugForward, float3 plugUp, float worldLength)
{
    YapsSocket socket;

    // The chain is filled by the atlas branch at the end and by nothing
    // else, so it is zeroed here rather than left to whatever was in the
    // registers. count 0 is what tells the deform there is no path to walk,
    // and reading a stale count would send it walking one.
    socket.chain = (YapsChain)0;

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
    socket.atlasHeaders = 0;
    socket.atlasHits = 0;

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

        // THE PER-VERTEX RECOVERED FRAME, and this is a decision with a
        // history. The channel's offsets are measured in the trigger boxes'
        // frame, which rides the plug's bones — and the per-vertex recovery
        // is the shader's only live estimate of that frame, because for a
        // SKINNED mesh Unity skins into world space and unity_ObjectToWorld
        // is IDENTITY at draw time. A frame published in the renderer's
        // object space therefore decodes unrotated: on a Blender-imported
        // body carrying -90 on X, "up" arrived pointing forward and the
        // avatar bent toward a socket a metre and a half behind it. Joe
        // found it by zeroing the rotation and watching the avatar stand up.
        //
        // At rest pose every vertex recovers the SAME frame, so the editor
        // and an unposed avatar are exact. Posed, a plug spanning many bones
        // recovers slightly different frames per vertex — each vertex bends
        // toward the socket as its own bones perceive it, which is the least
        // wrong option available and exact for every normal plug.
        float3 frameOrigin = plugOrigin;
        float3 frameForward = plugForward;
        float3 frameUp = plugUp;
        float3 plugRight = cross(frameUp, frameForward);
        socket.position = frameOrigin + plugRight * offset.x
                                      + frameUp * offset.y
                                      + frameForward * offset.z;

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
            // From the CHANNEL'S frame, not the vertex's.
            //
            // This measured to plugOrigin, which is recovered per vertex, so
            // on a plug spanning more than a bone every vertex computed its
            // own gap and therefore its own engagement: vertices near the
            // socket fully engaged, vertices at the far end not at all, and
            // the mesh deformed in pieces. The world route has no remap and
            // so never showed it, which is exactly the difference between
            // the two that this was chased through.
            //
            // frameOrigin is the frame the offsets were measured in, so
            // every vertex gets the same gap and the same engagement.
            float channelGap = length(socket.position - frameOrigin);
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
        float3 frontAt = frameOrigin + plugRight * frontOffset.x
                                     + frameUp * frontOffset.y
                                     + frameForward * frontOffset.z;
        float3 axis = frontAt - socket.position;
        float axisLength = length(axis);
        // AN ABSOLUTE WINDOW, because the front point is at an absolute
        // distance. Both ecosystems put it about a centimetre out —
        // FrontOffset is 0.01 here, TPS_Orf_Norm and SPSLL_Socket_Front are
        // the same — and that never scales with the plug.
        //
        // This gate was worldLength * 0.5, which on a plug measuring a metre
        // and a half accepts anything up to seventy-seven centimetres as a
        // socket's axis. A noisy or half-delivered front then passed it and
        // became the facing, and the plug arrived along a direction nobody
        // sent. On an ordinary plug the same gate is a few centimetres and
        // rejects the same noise, which is why this only ever showed on long
        // ones. Joe's read square across the shaft when the socket was
        // pointing straight back at it.
        //
        // Two millimetres to five centimetres: generous either side of a
        // centimetre, and nothing beyond it is a front point.
        if (axisLength > 0.002 && axisLength < 0.05)
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
    bool litRoot = YapsFindLightSocket(plugOrigin,
                                       channelFound ? channelPosition : plugOrigin,
                                       worldLength * 1.6, lightPosition, lightForward,
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

        // ENGAGEMENT follows the position, or a lit socket still trembles.
        //
        // Engagement was measured from the CHANNEL's decoded gap further up
        // and the light replaces the position here, afterwards, so the
        // target went steady while the strength kept shaking at the
        // channel's own resolution, about a millimetre arriving ten times a
        // second. On a socket carrying both, which is most of them, the plug
        // twitched exactly as hard as one carrying contacts alone, and the
        // light appeared to be doing nothing.
        //
        // Same formula the light-only path below uses, so whoever provides
        // the position provides the gap this is measured from.
        if (channelFound)
        {
            socket.engaged = 1 - smoothstep(worldLength, worldLength * 1.6,
                                            length(socket.position - plugOrigin));
        }
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

    // LIGHT FALLBACK, the exception the header points at.
    //
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

    // ONLY A SOCKET A REAL WAY BEHIND THE BASE IS REFUSED. Dead ahead and
    // square beside both engage in full; it fades out over the last half of
    // a plug length behind the root. That is enough to stop the plug
    // folding back on itself and still lets one reach up.
    //
    // Asked as a DISTANCE along the shaft, never as a direction.
    //
    // It used to normalise the vector from the root to the socket and test
    // its angle, and that had no answer at the one place it mattered.
    // Engagement is computed per VERTEX — a skinned plug recovers its frame
    // from each vertex's own normal and tangent, so every vertex brings its
    // own plugOrigin — and near the root the scatter between those origins
    // is as large as the gap itself. Each vertex normalised a different
    // tiny vector, got a different answer, and the mesh came out half
    // engaged: some vertices swallowed, some hanging out. Joe called it "a
    // weird mixture of engagement and not", which is exactly what a
    // per-vertex singularity looks like from outside.
    //
    // A signed distance passes smoothly through zero instead of being
    // undefined there, so every vertex near the root agrees.
    if (socket.engaged > 0)
    {
        float behind = dot(socket.position - plugOrigin, plugForward);
        socket.engaged *= smoothstep(-worldLength * 0.5, -worldLength * 0.05, behind);
    }

    // Nothing to bend toward. Say so, rather than bending toward nothing.
    if (!found)
    {
        socket.engaged = 0;
    }

    // THE ATLAS, on top of both, and off by default.
    //
    // It answers where neither of the others can: a socket on somebody
    // else's avatar, with no contact receiver between them and no light
    // slot spent. Where it answers at all it is also the better answer,
    // because it carries the socket's own facing and kind rather than
    // deriving them, so it takes the result outright rather than blending.
    //
    // The first link fills the single-socket fields, so everything that
    // reads a socket and knows nothing of chains keeps working. The whole
    // chain rides along beside it for the deform to walk.
    if (_YAPS_UseAtlas > 0.5)
    {
        YapsChain chain = YapsResolveChain(plugOrigin, plugForward, worldLength);
        socket.atlasHeaders = chain.headers;
        socket.atlasHits = chain.hits;
        if (chain.count > 0)
        {
            socket.chain = chain;
            socket.position = chain.position[0];
            socket.forward = chain.forward[0];
            socket.isHole = chain.kind[0];
            socket.engaged = chain.engaged;
            socket.tier = 3;
        }
    }

    return socket;
}

#endif
