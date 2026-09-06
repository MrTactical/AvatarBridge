// YAPS socket resolution, for ChilloutVR.
//
// Where the socket comes from. The deform takes a frame and bends toward
// it, so all the platform awkwardness lives here.
//
// ENGAGEMENT is decided by the discrete channel wherever there is one.
// A light engages only where no channel reached the plug at all, close in
// and as a last resort (see LIGHT FALLBACK at the end). Unity fills the
// vertex light slots PER CAMERA and ChilloutVR's mirrors zero the pixel
// light count, so anything decided from light presence bends one way in a
// mirror and another in reality.
//
// POSITION comes from the best source available, inside that engagement:
//
//   1. The channel. Exact, rotation-aware, identical on every camera,
//      since a material property is not per-camera state. Its ceiling is
//      10 Hz, so it carries a near-static offset.
//   2. Protocol lights, refining at contact range. Free, and bounded
//      close deliberately: the per-camera problem only shows at distance.
//   3. The screen atlas, for a socket on somebody else's avatar. Carries
//      its own facing and kind, so it is taken outright.
//
#ifndef YAPS_RESOLVE_INCLUDED
#define YAPS_RESOLVE_INCLUDED

#include "yaps_props.cginc"
#include "yaps_atlas.cginc"

// CVR publishes these for every player, on every client. Declared at the
// client's capacity: Unity locks an array's size at first bind.
//
// Read for ONE purpose: telling a wearer's own socket lights from
// everybody else's, so a plug does not bend into the hip it grows from.
// Never as a target. See the note before the resolver.
float4 _CVR_PlayerHipPositions[255];
float4 CVRGlobalParams1;

inline float3 YapsNormalizeOr(float3 v, float3 fallback)
{
    float lengthSq = dot(v, v);
    return lengthSq < 1e-12 ? fallback : v * rsqrt(lengthSq);
}

// --- the screen atlas ------------------------------------------------
//
// A LIST, NOT A WINNER. Picking the nearest socket makes a plug flip
// between two as whichever is closer to its ROOT changes, and lets one
// BEHIND it win, since distance to the root never asks which way a plug
// points.
//
// The list makes the plug a path: each socket claims a range of arc length
// along the shaft. A ring mid-shaft and a hole at the tip are two entries.
// Only the list is built here, the deform walks it.
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
    // Who decided this answer: 0 nobody, 1 the channel, 2 a marker light,
    // 3 the atlas. Diagnostic, not behaviour. A stray light reads exactly
    // like a working channel until the two are coloured apart.
    float tier;
    // How many atlas header taps said a cell held anything. Diagnostic,
    // and read as a LADDER: the step it stops on threw the socket away.
    float atlasHeaders;
    float atlasHits;
    // The WHOLE ordered chain, not only the link position names. A shaft
    // passing a ring on its way to a hole follows both, and the deform
    // picks a link per vertex by arc length. count is 0 for every source
    // but the atlas, and 0 means the fields above are the whole answer.
    YapsChain chain;
};

// --- protocol lights -------------------------------------------------
//
// A socket emits a black, shadowless, vertex-only point light whose RANGE
// says what it is. Unity hands back attenuation, so range is recovered as
// 5/sqrt(atten).
//
// WHAT A SOCKET EMITS IS STOCK DPS, BYTE FOR BYTE. 0.4130 for a hole root,
// 0.4230 for a ring root, 0.4530 for a front, and a light is only ever
// ADDED where one was missing. Change those and every DPS plug in
// ChilloutVR stops seeing YAPS sockets.
//
// Digits: 1 and 3 hole, 2 and 4 ring, 5 and 6 front, 8 and 9 a plug's own
// tip. Only the second decimal is read, so 0.31 is a hole like 0.41.
//
// 0 and 7 are unclaimed and still decoded, because a YAPS-only ordering
// was drafted around them: stock puts fronts ABOVE roots, Unity ranks
// vertex lights by range, so every front evicts its own root. Root 0.4706
// and front 0.4006 would fix that. NOT adopted, and never quietly: a
// legacy plug reads 7 and 0 as nothing, so those sockets go dark for every
// plug but this one. If revisited it emits BOTH sets, never a replacement.
//
// (A first draft used 0.4106 for the front, which is legacy's hole root.
// Both decoded as roots, no axis was ever paired, and the plug tracked the
// socket around ignoring its rotation. The digits are not free.)

// A root is a root however it was authored, but the legacy digits also say
// the KIND: a hole closes around the plug, a ring lets it pass through.
// Whoever resolved the position decides the kind, and where a light did
// not say, the kind travels on the channel instead.
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

// Whose body is this light on, and is it the plug's own?
//
// ChilloutVR publishes every player's hip to every shader, so ask it:
// nearest player to the plug, nearest to the light, same person or not.
// Nothing is transmitted.
//
// A judgement, not a fact, built to fail SAFE: "not mine" whenever it
// cannot tell. A discarded light costs a socket that should have worked, a
// kept one costs a plug bent into its wearer, which recovers as soon as
// anything better resolves. Doubt keeps the light.
//
// Takes a WORLD POSITION rather than a slot, so the atlas can ask the same
// question about a socket it read off the screen.
bool YapsSameBodyAt(float3 plugOrigin, float3 lightAt)
{
    // No early-out for a lone player. Alone, the wearer IS the only body,
    // and the inboard test below is what separates their own socket from a
    // prop they hold. Bailing here blanks every socket for a lone tester.
    int count = min((int) round(CVRGlobalParams1.y), 255);

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

    // Nothing resolved: claim nothing, so a light is never discarded on an
    // answer that is not there.
    if (nearPlug < 0 || nearLight < 0)
    {
        return false;
    }
    if (nearPlug != nearLight)
    {
        return false;   // somebody else's body; plainly not ours
    }

    // Same body, but so is a prop held against your own hip.
    //
    // A real socket on this body sits INBOARD of the plug, nearer the hip
    // than the plug growing out of it. Something pushed at the plug from
    // outside is not. Nearest the same person is necessary, not sufficient.
    float plugToHip = dot(_CVR_PlayerHipPositions[nearPlug].xyz - plugOrigin,
                          _CVR_PlayerHipPositions[nearPlug].xyz - plugOrigin);
    return bestLight < plugToHip;
}

inline bool YapsSameBodyAs(float3 plugOrigin, uint slot)
{
    return YapsSameBodyAt(plugOrigin, YapsLightPosition(slot));
}

// The SOCKET side of the same question: is this tracker light the wearer's
// OWN plug? Without it a converted avatar's plug, a hand's width from its
// own socket, reads as permanently inserted.
//
// The anchor is where the WEARER is, not where the socket is. A hand
// socket can sit in somebody else's lap, and judged from there a
// stranger's plug reads as their own and gets ignored, which is the one
// plug it exists for. The caller passes its mesh origin.
//
// Nearest player is necessary, not sufficient: a stranger's plug inside
// this socket is also nearest this hip. The BASE separates them. The
// wearer's base is a fixed short distance from their hip, a stranger's is
// a plug length out on their side. Doubt keeps the plug, since a
// stranger's plug wrongly ignored is only a missed effect.
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

    // A protocol light is authored black. Colour means real lighting.
    float4 colour = unity_LightColor[slot];
    if (any(colour.rgb > 0.0001) && colour.a > 0) return YAPS_LIGHT_NONE;

    // The wearer's own sockets, skipped before anything else. They are
    // permanently nearest, so without this the plug never looks elsewhere.
    //
    // Ownership goes by PLAYER POSITIONS alone. It used to be a digit in
    // the range's fourth decimal, built on precision nobody had measured:
    // only the SECOND decimal survives, since range is reconstructed as
    // 5*rsqrt(atten) rather than read. A prop authored at 0.4206 arrived
    // nearer 0.4203 and was skipped by a plug whose tag was 3.
    //
    // _YAPS_SelfTag is only a flag now. Zero or more means check ownership,
    // -1 means there is nothing to check for.
    if (_YAPS_SelfTag >= 0 && YapsSameBodyAs(plugOrigin, slot))
    {
        return YAPS_LIGHT_NONE;
    }

    int digit = (int) round(fmod(range, 0.1) * 100.0);

    // Ours first: the two digits legacy never claimed.
    if (digit == 7) return YAPS_LIGHT_ROOT;
    if (digit == 0) return YAPS_LIGHT_FRONT;

    // Legacy DPS, so a plug reacts to content already on the platform.
    // Legacy is the more specific, saying hole or ring in the digit.
    if (digit == 1 || digit == 3) return YAPS_LIGHT_HOLE;
    if (digit == 2 || digit == 4) return YAPS_LIGHT_RING;
    if (digit == 5 || digit == 6) return YAPS_LIGHT_FRONT;   // front
    // 8 and 9 are a plug's own tip light, not a socket. Ignoring them
    // stops one plug taking another for somewhere to go.
    return YAPS_LIGHT_NONE;
}

// Nearest root to the plug, with its front partner if one arrived. Unity
// may hand over a root without its front, so an unpaired root still yields
// a position and leaves the axis to the caller.
// preferNear: the socket the channel resolved, else the plug's own origin.
//
// Ranking by distance to the PLUG picked the wrong light on a prop with
// two sockets. A hole and a ring six centimetres apart are both inside the
// envelope, and which sits nearer the plug's origin flips as the plug
// moves in, so a plug inside the hole was handed the ring's kind and swept
// past. It changed with viewing distance, which reads as a socket that
// breaks when you look at it.
//
// The reach gate still measures from the plug, which is the engagement
// envelope. Only the ranking moved.
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

        // Its front light sits about a centimetre along the socket axis.
        // A very short baseline, so it is taken only when unambiguous.
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
// There used to be a fourth tier here: aim the plug at the nearest
// player's body when nothing else resolved. Removed 2026-08-15,
// deliberately and permanently. This note exists so nobody rebuilds it.
//
// The plug bends only toward a socket it can resolve: a channel written by
// a socket, a light emitted by a socket. Nothing else. A body position is
// not a socket, nobody authored it and nothing switches it off, so a plug
// with no socket in range stays exactly as it was.
//
// The player positions are read for YapsSameBodyAs alone, and it goes the
// other way: it REJECTS a wearer's own socket lights. It never aims at
// anyone. Keep that distinction.

// --- the resolution --------------------------------------------------


// The sort below moves whole entries by literal index.
#define YAPS_CH_MOVE(a, b) sockD[a] = sockD[b]; sockP[a] = sockP[b]; sockF[a] = sockF[b]; sockK[a] = sockK[b];
#define YAPS_CH_PUT(a) sockD[a] = d; sockP[a] = at; sockF[a] = fwd; sockK[a] = kind;

// Reads the neighbourhood of the shaft's MIDPOINT, not its root, so a
// radius-1 read covers the whole plug. No extra taps.
YapsChain YapsResolveChain(float3 root, float3 axis, float worldLength)
{
    // Zeroed in one go, so the compiler cannot read the early return below
    // as leaving the struct part-written.
    YapsChain chain = (YapsChain)0;

    float len = max(worldLength, 1e-4);

    // WHICH LEVEL. The atlas carries several cell sizes, each four times
    // the last, because one size cannot serve a 20 cm plug and a 20 m one.
    // Coverage wants about L/2r, so read the level nearest that. No extra
    // taps: the socket published to every level and this reads one.
    float wantCell = len / max(2.0 * YAPS_ATLAS_RADIUS, 1.0);
    int lvl = clamp(int(round(log2(wantCell / YAPS_ATLAS_CELL) * 0.5)), 0, YAPS_ATLAS_LEVELS - 1);
    float size = max(YAPS_ATLAS_CELL * pow(4.0, lvl), 1e-6);
    int total = YAPS_ATLAS_GRID * YAPS_ATLAS_GRID;

    // Where engagement reaches zero, and the inclusion test for the list.
    // It also rejects a hash collision from across the world, which decodes
    // to a plausible payload in an implausible place.
    float far = len * YAPS_ATLAS_REACH * 1.6;

    // Spelled out rather than looped, so the compiler sees every entry
    // written before one is read. 1e9 and kind -1 mean empty.
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

            // ONE tap for the header, whose alpha COUNTS the sockets in
            // this slot. Nearly every cell holds nothing and costs only
            // this, which is what makes eight buckets affordable. A count,
            // never a bitmask: bits add, so two sockets in one octant
            // unset the octant that exists and set one that does not.
            int held = int(round(YAPS_ATLAS_LOAD(cellX, cellY).a * 255.0));
            if (held > 0) chain.headers += 1;
            if (held == 0) break;   // home 2 cannot hold what home 1 does not
            bool got1 = false;
            [loop] for (int sub = 0; sub < 8; sub++)
            {
                int px = cellX + (1 + 2 * sub) * YAPS_ATLAS_SLOTPX;
                float4 got = YAPS_ATLAS_LOAD(px, cellY);
                if (got.a < 0.5) continue;
                // The header SUMS every cell sharing this slot, so it can
                // advertise somebody else's octants. The tag settles it,
                // and the protocol version rides in the tag.
                if (abs((got.a - 0.5) * 2 - tagWant) > 0.001) continue;
                got1 = true;
                chain.hits += 1;

                float3 at = (float3(c) + got.rgb) * size;
                float d = distance(at, root);

                float4 f4 = YAPS_ATLAS_LOAD(px + YAPS_ATLAS_SLOTPX, cellY);
                float3 fwd = normalize(f4.rgb * 2 - 1);
                float kind = round(f4.a * 16.0) - 1;

                // No facing test. There used to be one, rejecting a hole
                // whose forward pointed the way the plug was going. The
                // deform already flips the socket axis to meet the
                // approach, so a correctly aimed hole was thrown away
                // before the deform ever saw it. Range is what rejects.
                if (d > far) continue;

                // OWN BODY, the same question the lights ask. The atlas is
                // the transport for OTHER PEOPLE's sockets, and a wearer's
                // own is permanently nearest, so without this it takes link
                // 0 for ever and nobody else is ever seen.
                //
                // Nothing is lost. A wearer's own sockets already reach
                // this plug through the channel, exactly and locally.
                //
                // Tested here rather than after the sort, so a rejected
                // entry leaves no hole in the list. The scan inside is over
                // players, not taps, and only for a socket past range.
                if (_YAPS_SelfTag >= 0 && YapsSameBodyAt(root, at)) continue;

                // Insertion sort, nearest first. THE ORDER IS THE PATH:
                // socket one is the one the shaft meets first.
                //
                // Written out, never looped. These arrays only stay in
                // registers while every index is a compile-time constant,
                // and a loop breaking on a comparison cannot unroll. That
                // is the whole reason YAPS_CHAIN_MAX is not a knob.
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
            // Found in this home, so the other is not this cell's.
            if (got1) break;
        }
    }

    int count = 0;
    [unroll] for (int i2 = 0; i2 < YAPS_CHAIN_MAX; i2++)
        if (sockK[i2] > -0.5) count++;
    chain.count = count;

    // Chords rather than true cubic arc length, as the single-socket
    // path already approximates.
    chain.arc[0] = 0;
    float3 prev = root;
    [unroll] for (int i3 = 0; i3 < YAPS_CHAIN_MAX; i3++)
    {
        float3 seg = sockP[i3] - prev;
        float d3 = length(seg);
        // Every socket is turned to meet its approach, ring and hole
        // alike, the same law the deform applies to a lone socket.
        // Trusting the sign makes the path arrive backwards in a hairpin.
        // It used to be ring-only, when an aimed-away hole was supposed to
        // have been rejected first. A converter inherits the original
        // avatar's convention, so an aimed-away hole is usually correct.
        if (d3 > 1e-5 && dot(sockF[i3], seg) > 0)
            sockF[i3] = -sockF[i3];
        // Entries past the count get a range nothing falls inside rather
        // than a branch. A runtime-dependent continue stopped this loop
        // unrolling, and it must unroll to keep the indices constant.
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

    // The chain is filled by the atlas branch alone, so it is zeroed here
    // rather than left to the registers. count 0 tells the deform there is
    // no path to walk, and a stale count sends it walking one.
    socket.chain = (YapsChain)0;

    // Engagement and flags: the discrete channel, always, alone.
    socket.engaged = saturate(_YAPS_SocketFlags.x);
    socket.isHole = _YAPS_SocketFlags.y;

    // TAG FILTER, from SPS. The channel may carry the socket's tag in the
    // flags' z. Zero on either knob means no filter, and an untagged
    // socket is refused only by a REQUIRE. Marker lights carry no tag, so
    // this sits on the channel's answer alone.
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

    // A zero position is NOT a socket at the world origin. Track whether
    // anything resolved, or a quiet channel reaches for world zero.
    bool found = dot(socket.position, socket.position) > 1e-6;

    // The contact channel speaks only in the receiver's own frame,
    // normalised per axis, so the socket arrives as an offset from the
    // plug. Rebuild it against the frame the deform just recovered.
    //
    // The constraint suits the transport: what crosses the wire is the gap
    // between two bodies already touching, which barely moves. A centre
    // reading is a real reading, so engagement decides whether anything
    // arrived and the zero test above cannot.
    if (_YAPS_ChannelSpace > 0.5)
    {
        found = socket.engaged > 0;
        // The boxes ride the plug object, so a scaled plug has scaled
        // boxes and the decode scales with them.
        float3 offset = (_YAPS_SocketPos.xyz * 2 - 1) * _YAPS_ChannelExtents.xyz * max(_YAPS_BakeScale, 0.0001);

        // THE PER-VERTEX RECOVERED FRAME. The channel's offsets are
        // measured in the trigger boxes' frame, which rides the plug's
        // bones, and this is the only live estimate of it: for a SKINNED
        // mesh Unity skins into world space and unity_ObjectToWorld is
        // IDENTITY at draw time. A frame published in renderer object
        // space decodes unrotated, so a body imported with -90 on X bent
        // toward a socket a metre and a half behind it.
        //
        // At rest every vertex recovers the SAME frame. Posed, a plug
        // spanning many bones recovers slightly different ones, which is
        // the least wrong option and exact for every ordinary plug.
        float3 frameOrigin = plugOrigin;
        float3 frameForward = plugForward;
        float3 frameUp = plugUp;
        float3 plugRight = cross(frameUp, frameForward);
        socket.position = frameOrigin + plugRight * offset.x
                                      + frameUp * offset.y
                                      + frameForward * offset.z;

        // The channel's engagement is a PROXIMITY reading across the whole
        // trigger sphere, 1.75 plug lengths, so with the socket at the tip
        // it reads about 0.4 where the light path reads 1.
        //
        // So keep it as a gate, meaning something is in range, and take the
        // curve from the position by the light path's own formula. Two
        // routes to one socket then cannot disagree about how far in it is.
        if (socket.engaged > 0)
        {
            // From the CHANNEL'S frame, never the vertex's.
            //
            // Measured to plugOrigin, which is recovered per vertex, every
            // vertex computed its own gap and its own engagement, and the
            // mesh deformed in pieces. frameOrigin is the frame the offsets
            // were measured in, so every vertex gets the same gap.
            float channelGap = length(socket.position - frameOrigin);
            socket.engaged = 1 - smoothstep(worldLength, worldLength * 1.6, channelGap);

            // The remap is also the channel's reality check, and it has to
            // feed back into "found". A trigger reporting in range with its
            // position axes still at default puts the socket in the CORNER
            // of the box, engagement collapses to zero, and the position it
            // computed is what the light test below compares against. Left
            // saying "found", a half-delivered channel rejects the very
            // light that would have rescued it.
            //
            // Proximity without position is not a resolved socket.
            found = socket.engaged > 0;
        }

        // Which way the socket FACES, from the second point it publishes.
        // Without it the deform can only aim at a bare point, and the plug
        // reaches the socket instead of threading it.
        //
        // Believed only when PLAUSIBLE. Both ecosystems put the second
        // point about a centimetre out, so a much larger gap is not an
        // axis: it is a default, or a front belonging to another socket.
        // A zero forward tells the deform to take the approach direction.
        float3 frontOffset = (_YAPS_SocketFront.xyz * 2 - 1) * _YAPS_ChannelExtents.xyz * max(_YAPS_BakeScale, 0.0001);
        float3 frontAt = frameOrigin + plugRight * frontOffset.x
                                     + frameUp * frontOffset.y
                                     + frameForward * frontOffset.z;
        float3 axis = frontAt - socket.position;
        float axisLength = length(axis);
        // AN ABSOLUTE WINDOW, because the front point is at an absolute
        // distance. FrontOffset is 0.01 here, TPS_Orf_Norm and
        // SPSLL_Socket_Front the same, and none of them scale with a plug.
        //
        // This gate was worldLength * 0.5, which on a metre-and-a-half plug
        // accepts seventy-seven centimetres as an axis. Noise passed it and
        // became the facing, so the plug arrived along a direction nobody
        // sent. It only ever showed on long plugs.
        //
        // Two millimetres to five centimetres, and nothing beyond.
        if (axisLength > 0.002 && axisLength < 0.05)
        {
            socket.forward = axis / axisLength;
        }
    }

    // What the CHANNEL resolved. The refinement below may sharpen this
    // answer, never replace it with a different socket, and telling those
    // apart needs the original to compare against.
    bool channelFound = found;
    float3 channelPosition = socket.position;
    if (channelFound)
    {
        socket.tier = 1;
    }

    // There is deliberately NO floor here. If neither the channel nor a
    // light named a socket, nothing has, and the plug stays as it is.

    // Refinement: a protocol light close to where the resolver already
    // believes the socket to be. It sharpens the position only, never
    // switching the deform on or off, because light visibility is per
    // camera and engagement must not be.
    //
    // Searched across the whole envelope. Beyond 1.6 lengths engagement is
    // zero anyway, and a shorter reach makes the light path wake abruptly
    // on contact instead of easing in.
    float3 lightPosition;
    float3 lightForward;
    float lightHoleHint;
    bool litRoot = YapsFindLightSocket(plugOrigin,
                                       channelFound ? channelPosition : plugOrigin,
                                       worldLength * 1.6, lightPosition, lightForward,
                                       lightHoleHint);
    // A light refines the socket the channel resolved. It does not get to
    // nominate a DIFFERENT one, and it used to: any lit root in the
    // envelope replaced the channel's position outright, so a contact-only
    // socket broke whenever a lit one was nearby.
    //
    // So accept a light only when it is close enough to BE the socket the
    // channel reports. Rejecting a good light costs a little precision,
    // accepting a wrong one bends the plug at another socket entirely.
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
        // Engagement was measured from the CHANNEL's gap and the light
        // replaces the position afterwards, so the target went steady while
        // the strength kept shaking at the channel's own resolution. On a
        // socket carrying both the light appeared to do nothing.
        //
        // Same formula the light-only path uses, so whoever provides the
        // position provides the gap.
        if (channelFound)
        {
            socket.engaged = 1 - smoothstep(worldLength, worldLength * 1.6,
                                            length(socket.position - plugOrigin));
        }
        // A light that only SHARPENED the channel's answer leaves the tier
        // saying channel. One standing in for a silent channel owns it.
        if (!channelFound)
        {
            socket.tier = 2;
        }
        // Whoever resolved the position decides the kind. A legacy light
        // states hole or ring outright about the socket now being aimed at,
        // which the channel's flag may not be.
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
    // Legacy content has no contacts. ChilloutVR's DPS sockets announce
    // themselves with marker lights and nothing else, and that is most of
    // the platform's existing content.
    //
    // The compromise: engage on distance to a light-resolved root, but only
    // once nothing else has engaged, and only within about a plug length.
    // Engagement from a light is engagement per camera, bounded here
    // because the divergence is a range effect. A light centimetres from
    // the plug is inside any frustum already drawing it, one across the
    // room is not, and that is where a mirror and the direct view split.
    if (socket.engaged <= 0 && litRoot)
    {
        // Measured against the TIP, not the root. The gap is from the
        // base and the tip is already a length out, so a 0.9 to 1.3 window
        // only engaged within a few centimetres of the tip and flickered.
        // Full at the tip, fading over a further half length, which is
        // where the light search stops looking.
        float gap = length(socket.position - plugOrigin);
        socket.engaged = 1 - smoothstep(worldLength, worldLength * 1.6, gap);
        // Whoever provides the ENGAGEMENT owns the answer.
        socket.tier = 2;
    }

    // Nothing to bend toward. Say so, rather than bending toward nothing.
    if (!found)
    {
        socket.engaged = 0;
    }

    // THE ATLAS, on top of both.
    //
    // It answers where neither other can: a socket on somebody else's
    // avatar, with no contact receiver between them and no light slot
    // spent. It carries the socket's own facing and kind rather than
    // deriving them, so it takes the result outright.
    //
    // The first link fills the single-socket fields, so anything that
    // knows nothing of chains keeps working.
    //
    // YapsAtlasFits here, so a target too small to have been painted is
    // never read. Decoding it anyway hands back whatever the scene drew.
    if (_YAPS_UseAtlas > 0.5 && YapsAtlasFits())
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

    // ONLY A SOCKET A REAL WAY BEHIND THE BASE IS REFUSED. Dead ahead and
    // square beside both engage in full, fading over the last half length
    // behind the root. Enough to stop the plug folding back on itself.
    //
    // Asked as a DISTANCE along the shaft, never as a direction. It used to
    // normalise the root-to-socket vector and test its angle, which has no
    // answer where it matters: engagement is per VERTEX, and near the root
    // the scatter between origins is as large as the gap. Each vertex
    // normalised a different tiny vector and the mesh came out half
    // engaged. A signed distance passes smoothly through zero.
    //
    // LAST in this function, so it judges whatever answer won. It used to
    // sit above the atlas block, so a tier-3 socket skipped it entirely and
    // one decoded behind the plug's own root folded the shaft back.
    // Anything that decides engagement belongs above this.
    if (socket.engaged > 0)
    {
        float behind = dot(socket.position - plugOrigin, plugForward);
        socket.engaged *= smoothstep(-worldLength * 0.5, -worldLength * 0.05, behind);
    }

    return socket;
}

#endif
