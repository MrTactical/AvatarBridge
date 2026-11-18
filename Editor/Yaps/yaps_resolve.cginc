// YAPS socket resolution, for ChilloutVR. Where the socket comes from; the deform
// bends toward it. Whichever route answers owns engagement and position alike:
//   1. The screen atlas: every frame, 0.1 mm, facing, kind and a whole path.
//   2. Protocol lights, where the atlas cannot answer (view too small, legacy content).
//      Bounded close: Unity fills vertex lights PER CAMERA, so a distant light bends
//      one way in a mirror and another in reality.
// The contact channel is not read; see YapsResolveSocket.
//
#ifndef YAPS_RESOLVE_INCLUDED
#define YAPS_RESOLVE_INCLUDED

#include "yaps_props.cginc"
#include "yaps_atlas.cginc"

// CVR publishes these for every player. Declared at the client's capacity: Unity
// locks an array's size at first bind. Used only to tell the wearer's own sockets
// from others', never as a target.
cbuffer YapsHips {
float4 _CVR_PlayerHipPositions[255];
float4 CVRGlobalParams1;
};

// --- the screen atlas ------------------------------------------------
//
// A list, not a winner: the nearest flips between two sockets and lets one behind
// the plug win. Each socket claims a range of arc length (a ring mid-shaft, a hole
// at the tip); the deform walks it.
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
    // Nearest tag-refused socket, so the light tier cannot answer it. 1e9: none.
    float3 refusedAt;
    float  refusedD;
    // Light answer is an own socket not ticked. Apart from refusedD: the hip
    // socket is always nearest and would crowd a tag refusal out.
    float  lightUnticked;
    // owner << 4 | number of the hole the path ends in; 0 none or unknown.
    int    portal;
};

struct YapsSocket
{
    float3 position;
    float3 forward;
    float engaged;
    float isHole;
    // Diagnostic: 0 nobody, 1 editor preview, 2 marker light, 3 atlas.
    float tier;
    // Diagnostic ladder: the step it stops on threw the socket away.
    float atlasHeaders;
    float atlasHits;
    // The whole ordered chain; the deform picks a link per vertex by arc length.
    // count is 0 for every source but the atlas.
    YapsChain chain;
};

// --- protocol lights -------------------------------------------------
//
// A socket emits a black, shadowless, vertex-only point light whose RANGE says what
// it is, recovered as 5/sqrt(atten). Emitted ranges are stock DPS byte for byte (0.4106
// hole root, 0.4206 ring root, 0.4506 front) or DPS plugs stop seeing YAPS sockets.
// Second decimal only: 1/3 hole, 2/4 ring, 5/6 front, 8/9 a plug's tip.
// 7 (root) and 0 (front) are decoded but not emitted: Unity ranks vertex lights by
// range, so stock fronts evict their roots, but legacy plugs read 7 and 0 as nothing.
// If ever emitted, emit both sets. Using 0.4106 for a front once paired no axis at all.

// Legacy digits also say the kind; a light that did not say is a ring, which never
// stops a plug.
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

// Second decimal of the range, or -1 when too long to be protocol. One decode for all.
inline int YapsLightDigit(uint slot)
{
    float range = YapsLightRange(slot);
    return range >= 0.5 ? -1 : (int) round(fmod(range, 0.1) * 100.0);
}

// Black, or 1/255 grey in some kits, scaled by intensity (a tip carries its length
// there), so colour is judged relative to it: grey reads 3e-4, real light 0.1 or more.
// A fixed bound misread grey at 0.354 (1.07e-4) and at 31.85 (0.0097).
inline bool YapsLightIsMarker(uint slot)
{
    float4 colour = unity_LightColor[slot];
    return all(colour.rgb <= 0.005 * colour.a);
}

// Only the atlas asks: the one route that carries tags.
inline bool YapsTagsRefuse(int socketWord)
{
    // Present when every bit of its pattern is lit; an OR keeps each pattern whole.
    bool wanted = false;
    bool matched = false;
    bool lane1 = _YAPS_PlugLane > 0.5;
    [loop] for (int i = 0; i < 4; i++)
    {
        int deny = (int) round(_YAPS_TagExclude[i]);
        if (deny != 0 && (socketWord & deny) == deny) return true;

        int want = (int) round(lane1 ? _YAPS_TagInclude2[i] : _YAPS_TagInclude[i]);
        if (want == 0) continue;
        wanted = true;
        if ((socketWord & want) == want) matched = true;
    }
    return wanted && !matched;
}

// Is this socket on the plug's own body? Nearest player hip to the plug vs to the
// light. Fails safe to "not mine": a kept light at worst bends the plug into its
// wearer until something better resolves. Takes a world position so the atlas can
// ask too; owner 0 when unknown.
bool YapsSameBodyOwned(float3 plugOrigin, float3 lightAt, int owner)
{
    // Both ids known: a fact. The hip vote fails when two bodies overlap.
    int mine = YapsOwnerOf(_YAPS_Owner);
    bool known = mine != 0 && owner != 0;
    if (known && owner != mine) return false;

    // No early-out when alone: the inboard test still separates own sockets
    // from a held prop.
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

    // Nothing resolved: claim nothing.
    if (nearPlug < 0 || nearLight < 0)
    {
        return false;
    }
    if (!known && nearPlug != nearLight)
    {
        return false;   // somebody else's body; plainly not ours
    }

    // Own sockets sit inboard of the plug, nearer the hip; a held prop does not.
    // So own hip sockets are refused, hands and mouth are not, as SPS behaves.
    // Measured from the hip nearest the plug: the socket's can be a partner's.
    float3 hip = _CVR_PlayerHipPositions[nearPlug].xyz;
    return dot(hip - lightAt, hip - lightAt) < dot(hip - plugOrigin, hip - plugOrigin);
}

// One of the wearer's own, known by id, that carries a number.
bool YapsOwnNumbered(int owner, int index)
{
    int mine = YapsOwnerOf(_YAPS_Owner);
    return mine != 0 && owner == mine && index > 0;
}

// Numbered own sockets: the plug's bit list. Anything else: the body test.
bool YapsSelfRefuses(float3 root, float3 at, int owner, int index)
{
    if (_YAPS_SelfTag < 0 || YAPS_SELF_ALLOW >= 0.5) return false;
    if (YapsOwnNumbered(owner, index))
        return (((int) round(_YAPS_SelfSockets) >> (index - 1)) & 1) == 0;
    return YapsSameBodyOwned(root, at, owner);
}

// Chosen by name, so the tag test is skipped: tags are for other people.
bool YapsSelfChosen(int owner, int index)
{
    return YapsOwnNumbered(owner, index)
        && (((int) round(_YAPS_SelfChosen) >> (index - 1)) & 1) != 0;
}

// Own socket lights are always nearest, so the plug would never look elsewhere.
// By player positions alone: only the range's second decimal survives
// 5*rsqrt(atten) (0.4206 arrives near 0.4203). _YAPS_SelfTag -1: no check.
bool YapsLightOwn(float3 plugOrigin, uint slot)
{
    if (_YAPS_SelfTag < 0) return false;
    return YapsSameBodyOwned(plugOrigin, YapsLightPosition(slot), 0);
}

// Socket side: is this tracker light the wearer's own plug? Else it reads as always
// inserted. The anchor is the wearer (mesh origin), not the socket, which can sit in
// someone's lap. A stranger's plug inside is also nearest this hip, but its base is a
// plug length out; the wearer's is close. Doubt keeps the plug.
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

// What a light's range says it is. Whose it is stays with the caller: that
// loops over every player, and most lights in the slots are not sockets.
int YapsClassifyLight(uint slot)
{
    int digit = YapsLightDigit(slot);
    if (digit < 0) return YAPS_LIGHT_NONE;

    if (!YapsLightIsMarker(slot)) return YAPS_LIGHT_NONE;

    // YAPS first: the two digits legacy never claimed.
    if (digit == 7) return YAPS_LIGHT_ROOT;
    if (digit == 0) return YAPS_LIGHT_FRONT;

    // Legacy DPS.
    if (digit == 1 || digit == 3) return YAPS_LIGHT_HOLE;
    if (digit == 2 || digit == 4) return YAPS_LIGHT_RING;
    if (digit == 5 || digit == 6) return YAPS_LIGHT_FRONT;   // front
    // 8 and 9: another plug's tip, not somewhere to go.
    return YAPS_LIGHT_NONE;
}

// Nearest root, with its front if one arrived; else the axis is the caller's.
// Distance ranking swaps two sockets 6 cm apart on a prop; the atlas answers those
// first. Ownership (a loop over players) is asked only of a light about to be taken.
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

    // Looped: unrolled four-light scans were half or more of every plug's compile.
    [loop]
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

        // The NEAREST front within 10 cm (about 1 cm along the axis); the last
        // one found gave a root its neighbour's front and a sideways axis.
        forward = 0;
        float frontSq = 0.01;
        [loop]
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

// --- the light relay, both ends ------------------------------------------
//
// See the protocol in yaps_atlas.cginc. The writer is YAPS/Atlas Relay.

// The first two roots in the slots, slot order being Unity's ranking, each
// paired with its nearest front within ten centimetres as above. Unpaired
// fronts of an owned socket are not filtered: the relay cannot tell whose
// they are, and a stranger's root with the wearer's front nearer than its
// own is the only case that differs.
int YapsRelayRoots(out float3 at[YAPS_RELAY_ROOTS], out float3 fwd[YAPS_RELAY_ROOTS],
                   out int kind[YAPS_RELAY_ROOTS])
{
    int4 kinds = int4(YapsClassifyLight(0), YapsClassifyLight(1),
                      YapsClassifyLight(2), YapsClassifyLight(3));
    int count = 0;
    [unroll] for (int k = 0; k < YAPS_RELAY_ROOTS; k++) { at[k] = 0; fwd[k] = 0; kind[k] = YAPS_LIGHT_NONE; }
    [unroll] for (uint i = 0; i < 4; i++)
    {
        if (count >= YAPS_RELAY_ROOTS || !YapsIsRoot(kinds[i])) continue;
        float3 p = YapsLightPosition(i);
        float3 f = 0;
        float frontSq = 0.01;
        [unroll] for (uint j = 0; j < 4; j++)
        {
            if (kinds[j] != YAPS_LIGHT_FRONT) continue;
            float3 offset = YapsLightPosition(j) - p;
            float offsetSq = dot(offset, offset);
            if (!(offsetSq > 1e-8 && offsetSq < frontSq)) continue;
            frontSq = offsetSq;
            f = normalize(offset);
        }
        at[count] = p;
        fwd[count] = f;
        kind[count] = kinds[i];
        count++;
    }
    return count;
}

// The writer's pixel i of the entry.
float4 YapsRelayPixel(int i, int owner, int n, float3 at[YAPS_RELAY_ROOTS],
                      float3 fwd[YAPS_RELAY_ROOTS], int kind[YAPS_RELAY_ROOTS])
{
    if (i == 0) return float4(n / (float) YAPS_ATLAS_OWNERMAX, 0, 0, 0.5 + 0.5 * YapsRelayTag(owner, n));
    if (i == 1) return YapsOwnerEncode(owner);
    int k = (i - 2) / 3;
    float3 s = at[k] / YAPS_RELAY_CELL;
    int3 c = int3(floor(s)) & 0xFF;
    int part = (i - 2) % 3;
    return part == 0 ? float4(frac(s), kind[k] / (float) YAPS_ATLAS_OWNERMAX)
         : part == 1 ? float4(fwd[k] * 0.5 + 0.5, dot(fwd[k], fwd[k]) > 0 ? 1 : 0)
         : YapsOwnerEncode(c.x | (c.y << 8) | (c.z << 16));
}

// What the relay saw a frame ago, for passes Unity gives no lights. Picks as
// YapsFindLightSocket does. Tag, number and owner must match, so a clash fails safe.
bool YapsFindRelaySocket(float3 plugOrigin, float reach,
                         out float3 position, out float3 forward,
                         out float holeHint)
{
    position = 0;
    forward = 0;
    holeHint = -1;
    bool found = false;
    int owner = YapsOwnerOf(_YAPS_Owner);
    int n = (int) round(_YAPS_PlugIndex);
    if (owner != 0 && n >= 1 && n <= YAPS_ATLAS_SOCKETMAX)
    {
        float tagWant = YapsRelayTag(owner, n);
        int py = YapsAtlasRow(YapsRelayRow(owner, n, 0));
        float4 p0 = YAPS_ATLAS_LOAD(0, py);
        bool ok = YapsDirTagged(p0, tagWant);
        if (!ok)
        {
            py = YapsAtlasRow(YapsRelayRow(owner, n, 1));
            p0 = YAPS_ATLAS_LOAD(0, py);
            ok = YapsDirTagged(p0, tagWant);
        }
        ok = ok && (int) round(p0.r * YAPS_ATLAS_OWNERMAX) == n
                && YapsOwnerDecode(YAPS_ATLAS_LOAD(1, py)) == owner;

        float bestSq = reach * reach;
        int3 near = int3(floor(plugOrigin / YAPS_RELAY_CELL));
        // Unrolled: ChilloutVR's Shader Filtering gives a [loop] after a heavy
        // one a single pass, which would drop the second root.
        [unroll]
        for (int k = 0; k < YAPS_RELAY_ROOTS; k++)
        {
            float4 a = YAPS_ATLAS_LOAD(2 + 3 * k, py);
            int kind = (int) round(a.a * YAPS_ATLAS_OWNERMAX);
            if (!ok || !YapsIsRoot(kind)) continue;
            int c = YapsOwnerDecode(YAPS_ATLAS_LOAD(4 + 3 * k, py));
            int3 cell = near + ((((int3(c, c >> 8, c >> 16) - near) & 0xFF) ^ 0x80) - 0x80);
            float3 at = (float3(cell) + a.rgb) * YAPS_RELAY_CELL;
            float fromPlugSq = dot(at - plugOrigin, at - plugOrigin);
            if (fromPlugSq >= bestSq) continue;
            if (_YAPS_SelfTag >= 0 && YapsSameBodyOwned(plugOrigin, at, 0)) continue;
            bestSq = fromPlugSq;
            position = at;
            found = true;
            holeHint = kind == YAPS_LIGHT_HOLE ? 1 : (kind == YAPS_LIGHT_RING ? 0 : -1);
            float4 f = YAPS_ATLAS_LOAD(3 + 3 * k, py);
            float3 v = f.rgb * 2 - 1;
            forward = f.a >= 0.5 && dot(v, v) >= 0.25 ? normalize(v) : 0;
        }
    }
    return found;
}

// --- what the player globals are NOT for -------------------------------
//
// Never aim a plug at a player's body. The plug bends only toward a socket it
// resolves (atlas cell or socket light): a body is not authored and nothing switches
// it off. Player positions only REJECT own sockets. Removed on purpose; do not rebuild.

// --- the resolution --------------------------------------------------


// The sort below moves whole entries by literal index.
#define YAPS_CH_MOVE(a, b) sockD[a] = sockD[b]; sockP[a] = sockP[b]; sockF[a] = sockF[b]; sockK[a] = sockK[b];
#define YAPS_CH_PUT(a) sockD[a] = d; sockP[a] = at; sockF[a] = fwd; sockK[a] = kind;
#define YAPS_CH_SAME(a) (sockD[a] < 1e8 && distance(sockP[a], at) < same)
#define YAPS_CH_REFINE(a) if (sockD[a] < 1e8 && all(sockP[a] == refineAt)) { sockP[a] = refined; sockD[a] = distance(refined, root); }

// Reads the neighbourhood of the shaft's MIDPOINT, not its root, so a
// radius-1 read covers the whole plug. No extra taps. lightAt is the light
// tier's answer, far away when it had none.
YapsChain YapsResolveChain(float3 root, float3 axis, float worldLength, float3 lightAt)
{
    // Zeroed whole, or the compiler warns the struct may be part-written.
    YapsChain chain = (YapsChain)0;
    chain.refusedD = 1e9;   // zero would read as a refusal at the origin

    float len = max(worldLength, 1e-4);

    // Cell sizes grow 4x a level so one atlas serves 20 cm and 20 m plugs. Coverage
    // level: the smallest cell of YAPS_ATLAS_COVER lengths / r, so the block round the
    // base holds every socket that fully bends the plug wherever the grid falls. (Nearest
    // to L/2 left 23 of 50 places round a 0.649 m tip dead.)
    float cover = len * YAPS_ATLAS_COVER / max((float) YAPS_ATLAS_RADIUS, 1.0);
    int lvl = clamp(int(ceil(log2(cover / YAPS_ATLAS_CELL) * 0.5 - 1e-4)), 0, YAPS_ATLAS_LEVELS - 1);
    // Fine first, round the shaft's middle: the coverage level's octants are big
    // enough that two sockets a hand apart share one. Coarse then adds the rest.
    int fine = clamp(int(round(log2(len * 0.5 / YAPS_ATLAS_CELL) * 0.5)), 0, lvl);
    // Caller already checked YapsAtlasFits.
    YapsAtlasLayout layout = YapsAtlasLayoutNow();
    int total = max(layout.cells, 1);

    // Engagement's zero and the list's inclusion test; also rejects far-off hash collisions.
    float far = len * YAPS_ATLAS_REACH * 1.6;

    // Spelled out so the compiler sees every entry written. 1e9 / kind -1: empty.
    float3 sockP[YAPS_CHAIN_MAX] = { (float3)0, (float3)0, (float3)0, (float3)0 };
    float3 sockF[YAPS_CHAIN_MAX] = { float3(0, 0, 1), float3(0, 0, 1), float3(0, 0, 1), float3(0, 0, 1) };
    float  sockD[YAPS_CHAIN_MAX] = { 1e9, 1e9, 1e9, 1e9 };
    float  sockK[YAPS_CHAIN_MAX] = { -1, -1, -1, -1 };

    // A socket found by the coarse read alone: float noise moves the fine block a cell
    // per vertex, so neighbours read it at different precision, 0.5 mm apart, and the
    // plug opened along UV seams. The nearest is re-read from its own fine cell by a
    // third pass (cheaper to compile than a separate loop).
    float3 refineAt = (float3)0;
    float refineD = 1e9;
    float coarseSame = YapsAtlasCellSize(lvl) / 128.0;
    float3 refined = (float3)0;
    float refinedGap = 1e9;

    [loop] for (int scan = 0; scan < 3; scan++)
    {
        int lv = scan == 1 ? lvl : fine;
        if (scan == 1 && lv == fine) break;
        if (scan == 2 && refineD > 1e8) break;
        float size = YapsAtlasCellSize(lv);
        // Coverage round the base, where engagement is measured from.
        int3 mine = int3(floor((scan == 0 ? root + axis * (len * 0.5) : (scan == 1 ? root : refineAt)) / size));
        // Dedupe window against the fine read.
        float same = size / 128.0;

        // Cells, homes and octants as ONE loop, 16 steps a cell: nested, they
        // were about two thirds of the plug's compile time.
        const int side = 2 * YAPS_ATLAS_RADIUS + 1;
        int3 c = mine;
        float tagWant = 0;
        int idxB = 0, cellX = 0, cellY = 0;
        bool got1 = false;
        [loop] for (int k = 0; k < side * side * side * 16; k++)
        {
            int sub = k & 7;
            if (sub == 0)
            {
                int use;
                if ((k & 8) == 0)
                {
                    int cell = k >> 4;
                    c = mine + int3(cell / (side * side), (cell / side) % side, cell % side) - YAPS_ATLAS_RADIUS;
                    // The third pass asks only the cells the coarse position could be in.
                    if (scan == 2 && any(abs(refineAt - (float3(c) + 0.5) * size) > size * 0.5 + coarseSame)) { k |= 15; continue; }
                    tagWant = YapsAtlasTag(c);
                    YapsAtlasHomes(c, total, use, idxB);
                    got1 = false;
                }
                else
                {
                    // Found in the first home, so the second is not this cell's.
                    if (got1) { k |= 15; continue; }
                    use = idxB;
                }
                int fromTop;
                YapsAtlasCellPixels(use, lv, layout, cellX, fromTop);
                cellY = YapsAtlasRow(fromTop);

                // One header tap; its alpha COUNTS sockets in the slot. Most cells
                // cost only this. A count, not a bitmask: blended bits carry.
                int held = int(round(YAPS_ATLAS_LOAD(cellX, cellY).a * 255.0));
                if (held > 0) chain.headers += 1;
                // Home 2 cannot hold what home 1 does not.
                if (held == 0) { k |= 15; continue; }
            }

            int px = cellX + (1 + YAPS_ATLAS_OCTPX * sub) * YAPS_ATLAS_SLOTPX;
            float4 got = YAPS_ATLAS_LOAD(px, cellY);
            if (got.a < 0.5) continue;
            // The header sums every cell sharing the slot; the tag (with the
            // version in it) settles whose it is.
            if (abs((got.a - 0.5) * 2 - tagWant) > 0.001) continue;
            got1 = true;
            chain.hits += 1;

            float3 at = (float3(c) + got.rgb) * size;
            // Third pass: position only, already judged.
            if (scan == 2)
            {
                float gap = distance(at, refineAt);
                if (gap < min(refinedGap, coarseSame)) { refinedGap = gap; refined = at; }
                continue;
            }
            float d = distance(at, root);

            float4 f4 = YAPS_ATLAS_LOAD(px + YAPS_ATLAS_SLOTPX, cellY);
            // No facing gives zero, never normalize(0)'s NaN. Tested on length:
            // "none" decodes to about 0.004 per axis on an 8-bit target.
            float3 fwdRaw = f4.rgb * 2 - 1;
            float3 fwd = dot(fwdRaw, fwdRaw) > 0.25 ? normalize(fwdRaw) : (float3)0;
            float kind;
            int ownIndex;
            bool oneWay;
            YapsFacingDecode(f4.a, kind, ownIndex, oneWay);
            int owner = YapsOwnerDecode(
                YAPS_ATLAS_LOAD(px + 3 * YAPS_ATLAS_SLOTPX, cellY));

            // Tags. Untagged sockets pass a plug with no include list, or legacy
            // content goes dark. Own sockets chosen by name skip this, as SPS does.
            if (!YapsSelfChosen(owner, ownIndex) && YapsTagsRefuse(YapsTagsDecode(
                    YAPS_ATLAS_LOAD(px + 2 * YAPS_ATLAS_SLOTPX, cellY))))
            {
                // Remembered, so the light tier cannot answer it. Within reach only.
                if (d <= far && d < chain.refusedD)
                {
                    chain.refusedD = d;
                    chain.refusedAt = at;
                }
                continue;
            }

            // No facing test: the deform flips the axis to meet the approach, so
            // one threw away correctly aimed holes. Range rejects.
            if (d > far) continue;

            // One-way ring: judged from the base, which never passes through, so
            // the answer holds all the way in.
            if (oneWay && dot(fwd, root - at) < 0) continue;

            // Own body: the hip socket is always nearest and would take link 0 for
            // ever. Before the sort, so a rejection leaves no hole in the list.
            if (YapsSelfRefuses(root, at, owner, ownIndex))
            {
                // Its lights are still up and the light tier only refuses hip
                // sockets. A tenth of a length, as for tags, so a neighbour survives.
                if (YapsOwnNumbered(owner, ownIndex) && distance(at, lightAt) < len * 0.1)
                    chain.lightUnticked = 1;
                continue;
            }

            // Behind the base past the fade's zero: admitted, it took link 0 from
            // a socket ahead and switched the plug off.
            if (dot(at - root, axis) < -len * 0.5) continue;

            if (scan == 1 && (YAPS_CH_SAME(0) || YAPS_CH_SAME(1) || YAPS_CH_SAME(2) || YAPS_CH_SAME(3)))
                continue;
            if (scan == 1 && d < sockD[3] && d < refineD) { refineD = d; refineAt = at; }

            // A hole's portal key rides in its kind's bits (a float >= 2), so the
            // sort moves it with no array of its own. 0 when unknown.
            if (kind > 0.5)
                kind = asfloat(0x40000000 + (owner != 0 && ownIndex > 0 ? owner << 4 | ownIndex : 0));

            // Insertion sort, nearest first: the order is the path. Written out:
            // arrays stay in registers only with constant indices, which is why
            // YAPS_CHAIN_MAX is not a knob.
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
    }
    if (refinedGap < 1e8) { YAPS_CH_REFINE(0) YAPS_CH_REFINE(1) YAPS_CH_REFINE(2) YAPS_CH_REFINE(3) }

    // A hole ends the path; otherwise the shaft threads it and carries on to what is
    // behind. A flag, not a break: the loop must unroll to keep indices constant.
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
        // Every socket turned to meet its approach, as the deform does for one:
        // trusting the sign hairpins the path, and converted avatars keep their
        // own (often aimed-away) convention. Along the approach, since the
        // deform's handle sits at socket - forward.
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

    // Only the atlas fills it; a stale count would send the deform walking.
    socket.chain = (YapsChain)0;

    // The contact channel does not bend a plug: a trigger clears only on exit, so a
    // socket deleted or switched off inside it held the plug bent. The atlas clears
    // every frame and a light that goes out is gone.
    socket.engaged = 0;
    socket.isHole = 0;
    socket.position = 0;
    socket.forward = 0;
    socket.tier = 0;
    socket.atlasHeaders = 0;
    socket.atlasHits = 0;
    bool found = false;

    // The editor preview, world space, from a block that never ships. Channel-space
    // values (_YAPS_ChannelSpace) are ignored.
    if (_YAPS_ChannelSpace < 0.5 && _YAPS_SocketFlags.x > 0)
    {
        socket.engaged = saturate(_YAPS_SocketFlags.x);
        socket.isHole = _YAPS_SocketFlags.y;
        socket.position = _YAPS_SocketPos.xyz;
        socket.forward = _YAPS_SocketForward.xyz;
        socket.tier = 1;
        found = dot(socket.position, socket.position) > 1e-6;
    }

    // Marker light: the fallback, and legacy DPS's only route. Per camera, so
    // bounded close: a near light is in every frustum drawing the plug, a far
    // one splits mirror from direct view.
    float3 lightPosition;
    float3 lightForward;
    float lightHoleHint;
#if defined(UNITY_PASS_SHADOWCASTER)
    // A caster gets no vertex lights; the search was a sixth of its program.
    bool lit = false;
#else
    bool lit = !found && YapsFindLightSocket(plugOrigin, worldLength * 1.6,
                                             lightPosition, lightForward, lightHoleHint);
#endif
    // Other passes (the depth caster above all) ask the relay.
#if !defined(UNITY_PASS_FORWARDBASE)
    if (!found && !lit)
        lit = YapsFindRelaySocket(plugOrigin, worldLength * 1.6, lightPosition, lightForward, lightHoleHint);
#endif
    if (lit)
    {
        socket.position = lightPosition;
        found = true;
        // Full at the tip's distance, fading over a further 0.6 length where the
        // search stops. A 0.9 to 1.3 window engaged only near the tip and flickered.
        socket.engaged = 1 - smoothstep(worldLength, worldLength * 1.6,
                                        length(lightPosition - plugOrigin));
        socket.tier = 2;
        // A legacy light states hole or ring outright.
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

    // The atlas, on top of both: it carries facing and kind, so it wins outright.
    // Link 0 fills the single-socket fields. A target too small to be painted is
    // never read; it holds whatever the scene drew.
    if (_YAPS_UseAtlas > 0.5 && YapsAtlasFits())
    {
        YapsChain chain = YapsResolveChain(plugOrigin, plugForward, worldLength,
            socket.tier == 2 ? socket.position : float3(1e9, 1e9, 1e9));
        socket.atlasHeaders = chain.headers;
        socket.atlasHits = chain.hits;
        // Kept even when empty: count still gates consumers, and refusedD matters.
        socket.chain = chain;
        if (chain.count > 0)
        {
            socket.position = chain.position[0];
            socket.forward = chain.forward[0];
            socket.isHole = chain.kind[0];
            socket.engaged = chain.engaged;
            socket.tier = 3;
        }

        // A refused socket is not an unknown one: undo the light tier when it
        // answered a socket the atlas read and refused (tags or unticked own).
        // Within a tenth of a length, so it hits the same socket, not a neighbour.
        if (socket.tier == 2 && (chain.lightUnticked > 0.5 || (chain.refusedD < 1e8
            && distance(socket.position, chain.refusedAt) < worldLength * 0.1)))
        {
            socket.engaged = 0;
            socket.tier = 0;
        }
    }

    // Only a socket well behind the base is refused, fading over the last half
    // length, so the plug cannot fold back. A signed distance, not an angle: per
    // vertex, near the root, the angle of a tiny vector scatters and the mesh came
    // out half engaged. LAST, so it judges whichever answer won.
    if (socket.engaged > 0)
    {
        float behind = dot(socket.position - plugOrigin, plugForward);
        socket.engaged *= smoothstep(-worldLength * 0.5, -worldLength * 0.05, behind);
    }

    return socket;
}

#endif
