// YAPS socket resolution, for ChilloutVR. All platform awkwardness lives
// here; the deform just bends toward the frame. Two routes:
//   1. The screen atlas: every frame, 0.1 mm, carries facing, kind and a
//      whole path. Taken outright.
//   2. Protocol lights, where the atlas cannot answer (small view, legacy
//      content). Bounded close: Unity fills vertex light slots PER CAMERA,
//      so a distant light bends one way in a mirror and another in reality.
#ifndef YAPS_RESOLVE_INCLUDED
#define YAPS_RESOLVE_INCLUDED

#include "yaps_props.cginc"
#include "yaps_atlas.cginc"

// CVR publishes these for every player. Declared at the client's capacity:
// Unity locks an array's size at first bind. Used only to tell the wearer's
// own sockets apart, never as a target (see the note before the resolver).
float4 _CVR_PlayerHipPositions[255];
float4 CVRGlobalParams1;

// --- the screen atlas ------------------------------------------------
// A LIST, NOT A WINNER. Nearest-to-root flips between two sockets and lets
// one behind win. Each socket claims a range of arc length; the deform
// walks the list.
#define YAPS_CHAIN_MAX 4

struct YapsChain
{
    float3 position[YAPS_CHAIN_MAX];
    float3 forward[YAPS_CHAIN_MAX];
    float  kind[YAPS_CHAIN_MAX];      // 0 ring, 1 hole
    int    count;
    float  engaged;
    float  headers;   // cells whose header said something was there
    float  hits;      // payloads that then matched the cell's tag
    // Nearest socket the tags REFUSED, so the light tier cannot answer it
    // anyway. 1e9 means none.
    float3 refusedAt;
    float  refusedD;
    // Light tier hit an own socket not ticked. Apart from refusedD, which
    // the always-nearest hip socket would crowd out.
    float  lightUnticked;
    // PORTAL SPIKE. owner << 4 | number of the ending hole, 0 for none or
    // unknown.
    int    portal;
};

struct YapsSocket
{
    float3 position;
    float3 forward;
    float engaged;
    float isHole;
    // Diagnostic: 0 nobody, 1 editor preview, 2 marker light, 3 atlas.
    // A stray light looks like a working atlas until coloured apart.
    float tier;
    // Diagnostic ladder: the step it stops on threw the socket away.
    float atlasHeaders;
    float atlasHits;
    // The whole ordered chain; the deform picks a link per vertex by arc
    // length. count 0 (every source but the atlas): the fields above suffice.
    YapsChain chain;
};

// --- protocol lights -------------------------------------------------
// A black, shadowless, vertex-only point light whose RANGE says what it is,
// recovered as 5/sqrt(atten).
// Sockets emit STOCK DPS byte for byte (0.4106 hole root, 0.4206 ring root,
// 0.4506 front), only added where missing, or every DPS plug in ChilloutVR
// stops seeing them.
// Digits: 1 and 3 hole, 2 and 4 ring, 5 and 6 front, 8 and 9 a plug's tip.
// Only the second decimal survives.
// 0 and 7 are decoded for a drafted YAPS ordering (root 0.4706, front
// 0.4006) that stops fronts evicting their roots, since Unity ranks by
// range. Not adopted: legacy plugs read 7 and 0 as nothing. If revisited,
// emit BOTH sets. A front at 0.4106 failed: it decoded as a hole root.

// A light that did not say hole or ring stays a ring, which never stops
// a plug.
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

// The digit in the range's second decimal, -1 when too long to be protocol.
// The black test stays with each caller: plug and socket test colour
// differently, and which is right only shows in game.
inline int YapsLightDigit(uint slot)
{
    float range = YapsLightRange(slot);
    return range >= 0.5 ? -1 : (int) round(fmod(range, 0.1) * 100.0);
}

// Only the atlas carries tags, so only it asks.
inline bool YapsTagsRefuse(int socketWord)
{
    // Present when every bit of its pattern is lit: an OR keeps each whole.
    bool wanted = false;
    bool matched = false;
    [unroll] for (int i = 0; i < 4; i++)
    {
        int deny = (int) round(_YAPS_TagExclude[i]);
        if (deny != 0 && (socketWord & deny) == deny) return true;

        int want = (int) round(_YAPS_TagInclude[i]);
        if (want == 0) continue;
        wanted = true;
        if ((socketWord & want) == want) matched = true;
    }
    return wanted && !matched;
}

// Is this socket on the plug's own body? Nearest player hip to the plug vs
// to the socket. Fails SAFE to "not mine": a kept own light bends the plug
// until something better resolves, a discarded one loses a socket.
// A world position, so the atlas can ask too; owner 0 when unknown.
bool YapsSameBodyOwned(float3 plugOrigin, float3 lightAt, int owner)
{
    // Both ids known: a fact. The hip vote fails when bodies overlap, refusing
    // a partner's socket mid-insertion.
    int mine = YapsOwnerOf(_YAPS_Owner);
    bool known = mine != 0 && owner != 0;
    if (known && owner != mine) return false;

    // No early-out for a lone player: the inboard test below still separates
    // their own socket from a held prop.
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

    if (nearPlug < 0 || nearLight < 0)
    {
        return false;
    }
    if (!known && nearPlug != nearLight)
    {
        return false;   // somebody else's body; plainly not ours
    }

    // Same body is not enough: a held prop is too. An own socket sits
    // INBOARD, nearer the hip than the plug. So hip sockets are refused,
    // hands and mouth are not, as SPS behaves. Uses the hip nearest the
    // plug; the socket's nearest can be a partner's.
    float3 hip = _CVR_PlayerHipPositions[nearPlug].xyz;
    return dot(hip - lightAt, hip - lightAt) < dot(hip - plugOrigin, hip - plugOrigin);
}

// One of the wearer's own, known by id, that carries a number.
bool YapsOwnNumbered(int owner, int index)
{
    int mine = YapsOwnerOf(_YAPS_Owner);
    return mine != 0 && owner == mine && index > 0;
}

// A numbered own socket: the plug's tick list decides. Else the body test.
bool YapsSelfRefuses(float3 root, float3 at, int owner, int index)
{
    if (_YAPS_SelfTag < 0 || _YAPS_SelfAllow >= 0.5) return false;
    if (YapsOwnNumbered(owner, index))
        return (((int) round(_YAPS_SelfSockets) >> (index - 1)) & 1) == 0;
    return YapsSameBodyOwned(root, at, owner);
}

// Chosen by name skips the tags: tags are for other people's sockets.
bool YapsSelfChosen(int owner, int index)
{
    return YapsOwnNumbered(owner, index)
        && (((int) round(_YAPS_SelfChosen) >> (index - 1)) & 1) != 0;
}

// The wearer's own lights are permanently nearest. Ownership goes by player
// positions: a digit in the range's fourth decimal failed, as 0.4206
// arrives near 0.4203 through 5*rsqrt(atten).
bool YapsLightOwn(float3 plugOrigin, uint slot)
{
    if (_YAPS_SelfTag < 0) return false;
    return YapsSameBodyOwned(plugOrigin, YapsLightPosition(slot), 0);
}

// Socket side: is this tracker light the wearer's OWN plug? Else a plug a
// hand from its own socket reads as permanently inserted.
// Anchored on the wearer (mesh origin), not the socket: a hand socket in a
// stranger's lap would ignore exactly the plug it is for. A stranger's plug
// inside is nearest this hip too; the base separates them (the wearer's is
// near their hip). Doubt keeps the plug.
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

// Whose it is stays with the caller: that loops every player, and most
// lights are not sockets.
int YapsClassifyLight(uint slot)
{
    int digit = YapsLightDigit(slot);
    if (digit < 0) return YAPS_LIGHT_NONE;

    // A protocol light is authored black. Colour means real lighting.
    float4 colour = unity_LightColor[slot];
    if (any(colour.rgb > 0.0001) && colour.a > 0) return YAPS_LIGHT_NONE;

    // YAPS first: the two digits legacy never claimed.
    if (digit == 7) return YAPS_LIGHT_ROOT;
    if (digit == 0) return YAPS_LIGHT_FRONT;

    // Legacy DPS states hole or ring in the digit.
    if (digit == 1 || digit == 3) return YAPS_LIGHT_HOLE;
    if (digit == 2 || digit == 4) return YAPS_LIGHT_RING;
    if (digit == 5 || digit == 6) return YAPS_LIGHT_FRONT;   // front
    // 8 and 9: a plug's tip light, never somewhere to go.
    return YAPS_LIGHT_NONE;
}

// Nearest root, with its front if Unity handed one over; else the axis is
// the caller's. Distance ranking swaps two sockets 6 cm apart as the plug
// moves in; the atlas answers such props first.
// Ownership (a loop over every player) is asked only of a light that would
// otherwise be taken.
bool YapsFindLightSocket(float3 plugOrigin, float reach,
                         out float3 position, out float3 forward,
                         out float holeHint)
{
    position = 0;
    forward = 0;
    holeHint = -1;   // the light did not say
    // The reach gate and the best so far in one: nearer than both or skipped.
    float bestSq = reach * reach;
    bool found = false;

    int4 kinds = int4(YapsClassifyLight(0), YapsClassifyLight(1),
                      YapsClassifyLight(2), YapsClassifyLight(3));

    [unroll]
    for (uint i = 0; i < 4; i++)
    {
        int kind = kinds[i];
        if (!YapsIsRoot(kind)) continue;
        float3 at = YapsLightPosition(i);
        float fromPlugSq = dot(at - plugOrigin, at - plugOrigin);
        if (fromPlugSq >= bestSq) continue;
        if (YapsLightOwn(plugOrigin, i)) continue;
        bestSq = fromPlugSq;
        position = at;
        found = true;
        holeHint = kind == YAPS_LIGHT_HOLE ? 1 : (kind == YAPS_LIGHT_RING ? 0 : -1);

        // The NEAREST front within 10 cm. The last one in the gate gave a
        // root its neighbour's front, and the axis pointed sideways.
        forward = 0;
        float frontSq = 0.01;
        [unroll]
        for (uint j = 0; j < 4; j++)
        {
            if (kinds[j] != YAPS_LIGHT_FRONT) continue;
            float3 offset = YapsLightPosition(j) - at;
            float offsetSq = dot(offset, offset);
            if (!(offsetSq > 1e-8 && offsetSq < frontSq)) continue;
            if (YapsLightOwn(plugOrigin, j)) continue;
            frontSq = offsetSq;
            forward = normalize(offset);
        }
    }
    return found;
}

// --- what the player globals are NOT for -------------------------------
// Never aim a plug at a player's body. Removed on purpose: a body is not a
// socket, nobody authored it and nothing switches it off. Player positions
// only REJECT the wearer's own sockets. Do not rebuild it.

// --- the resolution --------------------------------------------------


// The sort below moves whole entries by literal index.
#define YAPS_CH_MOVE(a, b) sockD[a] = sockD[b]; sockP[a] = sockP[b]; sockF[a] = sockF[b]; sockK[a] = sockK[b];
#define YAPS_CH_PUT(a) sockD[a] = d; sockP[a] = at; sockF[a] = fwd; sockK[a] = kind;
#define YAPS_CH_SAME(a) (sockD[a] < 1e8 && distance(sockP[a], at) < same)

// Reads the neighbourhood of the shaft's MIDPOINT, not its root, so a
// radius-1 read covers the whole plug. No extra taps. lightAt is the light
// tier's answer, far away when it had none.
YapsChain YapsResolveChain(float3 root, float3 axis, float worldLength, float3 lightAt)
{
    // Zeroed whole, or the compiler sees it part-written.
    YapsChain chain = (YapsChain)0;
    chain.refusedD = 1e9;   // zero would read as a refusal at the origin

    float len = max(worldLength, 1e-4);

    // Levels serve 20 cm to 20 m plugs. Coverage is guaranteed: a block r
    // cells round the base holds every point within r cells, so the smallest
    // cell of YAPS_ATLAS_COVER lengths / r sees every socket that bends the
    // plug fully. Nearest-to-L/2 alone left 23 of 50 places round a 0.649 m
    // tip dead, on every camera.
    float cover = len * YAPS_ATLAS_COVER / max((float) YAPS_ATLAS_RADIUS, 1.0);
    int lvl = clamp(int(ceil(log2(cover / YAPS_ATLAS_CELL) * 0.5 - 1e-4)), 0, YAPS_ATLAS_LEVELS - 1);
    // FINE, THEN COARSE. Coverage octants are big enough that two sockets a
    // hand apart share one and one hides. So the level nearest L/2 is read
    // first round the middle, then coverage adds the rest. Twice the header
    // taps unless both levels match.
    int fine = clamp(int(round(log2(len * 0.5 / YAPS_ATLAS_CELL) * 0.5)), 0, lvl);
    // The caller already checked YapsAtlasFits.
    YapsAtlasLayout layout = YapsAtlasLayoutNow();
    int total = max(layout.cells, 1);

    // Engagement reaches zero here. Also rejects a hash collision decoded
    // from across the world.
    float far = len * YAPS_ATLAS_REACH * 1.6;

    // Spelled out so the compiler sees every entry written. 1e9, -1: empty.
    float3 sockP[YAPS_CHAIN_MAX] = { (float3)0, (float3)0, (float3)0, (float3)0 };
    float3 sockF[YAPS_CHAIN_MAX] = { float3(0, 0, 1), float3(0, 0, 1), float3(0, 0, 1), float3(0, 0, 1) };
    float  sockD[YAPS_CHAIN_MAX] = { 1e9, 1e9, 1e9, 1e9 };
    float  sockK[YAPS_CHAIN_MAX] = { -1, -1, -1, -1 };

    [loop] for (int scan = 0; scan < 2; scan++)
    {
        int lv = scan == 0 ? fine : lvl;
        if (scan == 1 && lv == fine) break;
        float size = YapsAtlasCellSize(lv);
        // Coverage round the base: engagement is measured from there.
        int3 mine = int3(floor((scan == 0 ? root + axis * (len * 0.5) : root) / size));
        // A fine-read socket returns from the coarse read within a step or two.
        float same = size / 128.0;

        [loop] for (int dx = -YAPS_ATLAS_RADIUS; dx <= YAPS_ATLAS_RADIUS; dx++)
        [loop] for (int dy = -YAPS_ATLAS_RADIUS; dy <= YAPS_ATLAS_RADIUS; dy++)
        [loop] for (int dz = -YAPS_ATLAS_RADIUS; dz <= YAPS_ATLAS_RADIUS; dz++)
        {
            int3 c = mine + int3(dx, dy, dz);
            float tagWant = YapsAtlasTag(c);

            int idx, idxB;
            YapsAtlasHomes(c, total, idx, idxB);

            [loop] for (int home = 0; home < 2; home++)
            {
                int use = home == 0 ? idx : idxB;
                int cellX, fromTop;
                YapsAtlasCellPixels(use, lv, layout, cellX, fromTop);
                int cellY = YapsAtlasRow(fromTop);

                // One header tap; alpha COUNTS sockets, so empty cells cost
                // one read. Never a bitmask: blending adds, so two sockets in
                // one octant would set the wrong bit.
                int held = int(round(YAPS_ATLAS_LOAD(cellX, cellY).a * 255.0));
                if (held > 0) chain.headers += 1;
                if (held == 0) break;   // home 2 cannot hold what home 1 does not
                bool got1 = false;
                [loop] for (int sub = 0; sub < 8; sub++)
                {
                    int px = cellX + (1 + YAPS_ATLAS_OCTPX * sub) * YAPS_ATLAS_SLOTPX;
                    float4 got = YAPS_ATLAS_LOAD(px, cellY);
                    if (got.a < 0.5) continue;
                    // The header sums every cell sharing the slot; the tag
                    // (with the version in it) settles whose.
                    if (abs((got.a - 0.5) * 2 - tagWant) > 0.001) continue;
                    got1 = true;
                    chain.hits += 1;

                    float3 at = (float3(c) + got.rgb) * size;
                    float d = distance(at, root);

                    float4 f4 = YAPS_ATLAS_LOAD(px + YAPS_ATLAS_SLOTPX, cellY);
                    // Never normalize(0): its NaN reaches the curve. "No
                    // facing" decodes to about 0.004 per axis on 8 bits, so
                    // test length, not exact zero.
                    float3 fwdRaw = f4.rgb * 2 - 1;
                    float3 fwd = dot(fwdRaw, fwdRaw) > 0.25 ? normalize(fwdRaw) : (float3)0;
                    float kind;
                    int ownIndex;
                    bool oneWay;
                    YapsFacingDecode(f4.a, kind, ownIndex, oneWay);
                    int owner = YapsOwnerDecode(
                        YAPS_ATLAS_LOAD(px + 3 * YAPS_ATLAS_SLOTPX, cellY));

                    // TAGS, the third pixel. An untagged socket must pass a
                    // plug with no include list, or every legacy socket goes
                    // dark. An own socket chosen by name skips them, as SPS's
                    // Self rules do.
                    if (!YapsSelfChosen(owner, ownIndex) && YapsTagsRefuse(YapsTagsDecode(
                            YAPS_ATLAS_LOAD(px + 2 * YAPS_ATLAS_SLOTPX, cellY))))
                    {
                        // Remember it, or the light tier answers it later.
                        // Within reach only.
                        if (d <= far && d < chain.refusedD)
                        {
                            chain.refusedD = d;
                            chain.refusedAt = at;
                        }
                        continue;
                    }

                    // No facing test: the deform flips the axis to meet the
                    // approach, and a facing test threw away correct holes.
                    if (d > far) continue;

                    // One-way ring: front only. Judged by the base, which never
                    // passes through, so it holds all the way in.
                    if (oneWay && dot(fwd, root - at) < 0) continue;

                    // OWN BODY: the hip socket is always nearest and would take
                    // link 0 for ever. Owner id from the fourth pixel. Tested
                    // before the sort, so a rejection leaves no gap.
                    if (YapsSelfRefuses(root, at, owner, ownIndex))
                    {
                        // Its lights are still up and the hip vote only refuses
                        // hip sockets. A tenth of a length, so a neighbour survives.
                        if (YapsOwnNumbered(owner, ownIndex) && distance(at, lightAt) < len * 0.1)
                            chain.lightUnticked = 1;
                        continue;
                    }

                    // Behind the base, past where the final fade is zero.
                    // Admitted, it took link 0 from a socket ahead and the
                    // fade switched the whole plug off.
                    if (dot(at - root, axis) < -len * 0.5) continue;

                    if (scan == 1 && (YAPS_CH_SAME(0) || YAPS_CH_SAME(1) || YAPS_CH_SAME(2) || YAPS_CH_SAME(3)))
                        continue;

                    // A hole's portal key rides in kind's bits (a float >= 2),
                    // so the sort carries it with no extra array. 0: unknown.
                    if (kind > 0.5)
                        kind = asfloat(0x40000000 + (owner != 0 && ownIndex > 0 ? owner << 4 | ownIndex : 0));

                    // Insertion sort, nearest first: the order is the path.
                    // Written out: the arrays stay in registers only with
                    // constant indices. Why YAPS_CHAIN_MAX is not a knob.
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
    }

    // A HOLE ENDS THE PATH, or the shaft threads it and carries on to
    // whatever is behind. A flag, not a break, so the loop unrolls.
    int count = 0;
    bool ended = false;
    float lastK = 0;
    [unroll] for (int i2 = 0; i2 < YAPS_CHAIN_MAX; i2++)
    {
        bool live = sockK[i2] > -0.5 && !ended;
        if (live) { count++; lastK = sockK[i2]; }
        ended = ended || (live && sockK[i2] > 0.5);
    }
    chain.count = count;
    // Ended means the last live entry is a hole.
    chain.portal = ended ? asint(lastK) - 0x40000000 : 0;

    float3 prev = root;
    [unroll] for (int i3 = 0; i3 < YAPS_CHAIN_MAX; i3++)
    {
        float3 seg = sockP[i3] - prev;
        float d3 = length(seg);
        // Every socket, ring or hole, turned to point ALONG its approach,
        // as the deform does: trusting the sign hairpins the path. Converted
        // holes often point away, and that is usually correct.
        if (d3 > 1e-5 && dot(sockF[i3], seg) < 0)
            sockF[i3] = -sockF[i3];
        prev = sockP[i3];

        chain.position[i3] = sockP[i3];
        chain.forward[i3] = sockF[i3];
        chain.kind[i3] = min(sockK[i3], 1);
    }

    chain.engaged = 1 - smoothstep(len * YAPS_ATLAS_REACH, far, sockD[0]);
    return chain;
}

YapsSocket YapsResolveSocket(float3 plugOrigin, float3 plugForward, float3 plugUp, float worldLength)
{
    YapsSocket socket;

    // Only the atlas fills it; a stale count sends the deform walking.
    socket.chain = (YapsChain)0;

    // No contact channel: a trigger clears only on exit, so a socket deleted
    // inside it held the plug bent for ever. Atlas and lights cannot.
    socket.engaged = 0;
    socket.isHole = 0;
    socket.position = 0;
    socket.forward = 0;
    socket.tier = 0;
    socket.atlasHeaders = 0;
    socket.atlasHits = 0;
    bool found = false;

    // The editor preview, world space; channel-space leftovers are ignored.
    if (_YAPS_ChannelSpace < 0.5 && _YAPS_SocketFlags.x > 0)
    {
        socket.engaged = saturate(_YAPS_SocketFlags.x);
        socket.isHole = _YAPS_SocketFlags.y;
        socket.position = _YAPS_SocketPos.xyz;
        socket.forward = _YAPS_SocketForward.xyz;
        socket.tier = 1;
        found = dot(socket.position, socket.position) > 1e-6;
    }

    // A marker light: the fallback, and legacy DPS's only route. Bounded
    // because lights are per camera: a near one is in any frustum drawing
    // the plug, a far one splits mirror and direct view.
    float3 lightPosition;
    float3 lightForward;
    float lightHoleHint;
    if (!found && YapsFindLightSocket(plugOrigin, worldLength * 1.6,
                                      lightPosition, lightForward, lightHoleHint))
    {
        socket.position = lightPosition;
        found = true;
        // Full at the tip, fading over a further 0.6 length. A 0.9 to 1.3
        // window engaged only within cm of the tip and flickered.
        socket.engaged = 1 - smoothstep(worldLength, worldLength * 1.6,
                                        length(lightPosition - plugOrigin));
        socket.tier = 2;
        if (lightHoleHint >= 0)
        {
            socket.isHole = lightHoleHint;
        }
        if (dot(lightForward, lightForward) > 1e-6)
        {
            socket.forward = lightForward;
        }
    }

    if (!found)
    {
        socket.engaged = 0;
    }

    // THE ATLAS, on top of both: it carries facing and kind, so it wins
    // outright. Link 0 fills the single-socket fields for chain-unaware
    // code. YapsAtlasFits, or an unpainted target decodes the scene.
    if (_YAPS_UseAtlas > 0.5 && YapsAtlasFits())
    {
        YapsChain chain = YapsResolveChain(plugOrigin, plugForward, worldLength,
            socket.tier == 2 ? socket.position : float3(1e9, 1e9, 1e9));
        socket.atlasHeaders = chain.headers;
        socket.atlasHits = chain.hits;
        // Kept even when nothing was accepted, so refusedD survives.
        socket.chain = chain;
        if (chain.count > 0)
        {
            socket.position = chain.position[0];
            socket.forward = chain.forward[0];
            socket.isHole = chain.kind[0];
            socket.engaged = chain.engaged;
            socket.tier = 3;
        }

        // A REFUSED SOCKET IS NOT AN UNKNOWN ONE: undo a light-tier answer
        // the atlas already refused (tags, or an own socket not ticked).
        // A tenth of a length, so two sockets a hand apart stay two.
        if (socket.tier == 2 && (chain.lightUnticked > 0.5 || (chain.refusedD < 1e8
            && distance(socket.position, chain.refusedAt) < worldLength * 0.1)))
        {
            socket.engaged = 0;
            socket.tier = 0;
        }
    }

    // Only a socket well behind the base is refused, fading over half a length,
    // so the plug cannot fold back. A signed DISTANCE, not an angle: near the
    // root each vertex normalised a different tiny vector and the mesh came
    // out half engaged. LAST, so it judges whichever tier won; anything that
    // decides engagement belongs above.
    if (socket.engaged > 0)
    {
        float behind = dot(socket.position - plugOrigin, plugForward);
        socket.engaged *= smoothstep(-worldLength * 0.5, -worldLength * 0.05, behind);
    }

    return socket;
}

#endif
