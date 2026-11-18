// Screen atlas protocol. Sockets write, plugs read.
// Defines, not properties: every avatar must agree.
#ifndef YAPS_ATLAS_INCLUDED
#define YAPS_ATLAS_INCLUDED

// Bump when an older reader would misread what a
// newer writer draws. It rides the tag.
#define YAPS_ATLAS_VERSION 9

// 4096 cells. Two homes, so a clash needs both.
#define YAPS_ATLAS_GRID    64

// Columns, not the grid. A rect wider than
// the view clips slots, and they read as opaque.
// 28 keeps the rect inside 1024 square, which is
// what a mirror capped at 1024 renders into. 32
// was 1064 wide once the owner pixel arrived.
#define YAPS_ATLAS_COLS    28

#define YAPS_ATLAS_SLOTPX  1
// The margin before the cells. No cell draws or
// reads it; its top rows are the portal directory.
#define YAPS_ATLAS_ORIGIN  8

// Level 0 cell size. Each level is four times.
#define YAPS_ATLAS_CELL    0.02
#define YAPS_ATLAS_LEVELS  4

// One header pixel, then FOUR per octant: position, facing, tags, owner.
// Same pixel count can still be a different protocol (an untagged socket's alpha 1 reads as
// five tag bits set), so the version refuses it. The tag pixel spends all four channels,
// hence a 20-bit folded word.
#define YAPS_ATLAS_OCTPX     4
#define YAPS_ATLAS_CELLSLOTS (1 + 8 * YAPS_ATLAS_OCTPX)

// Owner: low 24 bits of the wearer's user id, six a channel (not eight, for the tags' reason:
// a wrong owner is silent). Zero is UNKNOWN, never a match (prop, world socket, unsynced
// remote copy); the reader falls back to geometry.
#define YAPS_ATLAS_OWNERBITS 6
#define YAPS_ATLAS_OWNERMAX  63

int YapsOwnerDecode(float4 rgba)
{
    int a = (int) round(saturate(rgba.r) * YAPS_ATLAS_OWNERMAX);
    int b = (int) round(saturate(rgba.g) * YAPS_ATLAS_OWNERMAX);
    int c = (int) round(saturate(rgba.b) * YAPS_ATLAS_OWNERMAX);
    int d = (int) round(saturate(rgba.a) * YAPS_ATLAS_OWNERMAX);
    return a | (b << YAPS_ATLAS_OWNERBITS) | (c << (2 * YAPS_ATLAS_OWNERBITS))
             | (d << (3 * YAPS_ATLAS_OWNERBITS));
}

float4 YapsOwnerEncode(int owner)
{
    return float4(
        (owner & YAPS_ATLAS_OWNERMAX) / (float) YAPS_ATLAS_OWNERMAX,
        ((owner >> YAPS_ATLAS_OWNERBITS) & YAPS_ATLAS_OWNERMAX) / (float) YAPS_ATLAS_OWNERMAX,
        ((owner >> (2 * YAPS_ATLAS_OWNERBITS)) & YAPS_ATLAS_OWNERMAX) / (float) YAPS_ATLAS_OWNERMAX,
        ((owner >> (3 * YAPS_ATLAS_OWNERBITS)) & YAPS_ATLAS_OWNERMAX) / (float) YAPS_ATLAS_OWNERMAX);
}

// A float holds integers below 2^24 exactly, hence 24 bits.
int YapsOwnerOf(float animated)
{
    return (int) round(animated) & 0xFFFFFF;
}

// Facing alpha: kind (0 ring, 1 hole, 2 one-way ring) plus the socket's number among its
// wearer's own, 1 to 15, 0 none. Only the number crosses the screen; plugs keep their choices.
// Offset by one so an unwritten pixel reads as no kind. Kind times three fits six bits
// (max 48), where a separate one-way bit needed 64.
#define YAPS_ATLAS_SOCKETMAX 15
#define YAPS_KIND_RING    0
#define YAPS_KIND_HOLE    1
#define YAPS_KIND_ONEWAY  2

float YapsFacingEncode(int kind, int index)
{
    return (1 + clamp(kind, 0, 2) + 3 * clamp(index, 0, YAPS_ATLAS_SOCKETMAX)) / (float) YAPS_ATLAS_OWNERMAX;
}

// kind comes back as 1 for a hole and 0 for either ring, -1 for nothing;
// oneWay says which ring.
void YapsFacingDecode(float a, out float kind, out int index, out bool oneWay)
{
    int code = (int) round(saturate(a) * YAPS_ATLAS_OWNERMAX) - 1;
    int k = code < 0 ? -1 : code % 3;
    kind = k < 0 ? -1 : (k == YAPS_KIND_HOLE ? 1 : 0);
    oneWay = k == YAPS_KIND_ONEWAY;
    index = code < 0 ? 0 : code / 3;
}

// Tag word, 20 bits at five a channel: the grab is half float, and a wrong decode misroutes
// silently. A fold, not SPS's eight slots, which would take the rect past 2600 wide
// (see Runtime/Yaps/YapsTags.cs).
#define YAPS_ATLAS_TAGBITS 5
#define YAPS_ATLAS_TAGMAX  31

int YapsTagsDecode(float4 rgba)
{
    int a = (int) round(saturate(rgba.r) * YAPS_ATLAS_TAGMAX);
    int b = (int) round(saturate(rgba.g) * YAPS_ATLAS_TAGMAX);
    int c = (int) round(saturate(rgba.b) * YAPS_ATLAS_TAGMAX);
    int d = (int) round(saturate(rgba.a) * YAPS_ATLAS_TAGMAX);
    return a | (b << YAPS_ATLAS_TAGBITS) | (c << (2 * YAPS_ATLAS_TAGBITS))
             | (d << (3 * YAPS_ATLAS_TAGBITS));
}

float4 YapsTagsEncode(int tags)
{
    return float4(
        (tags & YAPS_ATLAS_TAGMAX) / (float) YAPS_ATLAS_TAGMAX,
        ((tags >> YAPS_ATLAS_TAGBITS) & YAPS_ATLAS_TAGMAX) / (float) YAPS_ATLAS_TAGMAX,
        ((tags >> (2 * YAPS_ATLAS_TAGBITS)) & YAPS_ATLAS_TAGMAX) / (float) YAPS_ATLAS_TAGMAX,
        ((tags >> (3 * YAPS_ATLAS_TAGBITS)) & YAPS_ATLAS_TAGMAX) / (float) YAPS_ATLAS_TAGMAX);
}

// Slot, second home and tag are all cut from this. Added then mixed (lowbias32): multiply
// and XOR per axis made h(-x, y, -z) == h(x, y, z) for odd x and z, so a socket also answered
// from its mirror across the world's vertical axis (protocol 9).
uint YapsAtlasMix(int3 c, uint salt)
{
    uint h = asuint(c.x) * 0x8DA6B343u + asuint(c.y) * 0xD8163841u + asuint(c.z) * 0xCB1AB31Fu + salt;
    h ^= h >> 16;
    h *= 0x7FEB352Du;
    h ^= h >> 15;
    h *= 0x846CA68Bu;
    h ^= h >> 16;
    return h;
}

int YapsAtlasHash(int3 c)
{
    return (int) (YapsAtlasMix(c, 0x9E3779B9u) & 0x7FFFFFFFu);
}

// The second home. Two slots square the odds.
int YapsAtlasHash2(int3 c)
{
    return (int) (YapsAtlasMix(c, 0x85EBCA6Bu) & 0x7FFFFFFFu);
}

// A cell's two slots. Writer, reader and readout must address them bit for
// bit alike. Both hashes are masked positive, so neither needs a sign fix;
// total is at least 1.
void YapsAtlasHomes(int3 cell, int total, out int a, out int b)
{
    a = YapsAtlasHash(cell) % total;
    b = (a + YapsAtlasHash2(cell) % max(total - 1, 1) + 1) % total;
}

// A level's cell edge, in metres.
float YapsAtlasCellSize(int level)
{
    return YAPS_ATLAS_CELL * pow(4.0, level);
}

// Who owns the payload. Seven bits, odd numerators: the writer stores 0.5 + tag / 2 =
// (128 + k) / 255, exact on an 8-bit target. k / 255 read even k 1/255 off and killed half the
// cells on non-HDR cameras (protocol 8). A clash passes 1 in 128; the range test catches most.
float YapsAtlasTag(int3 c)
{
    uint h = YapsAtlasMix(c, 0xC2B2AE35u + YAPS_ATLAS_VERSION * 0x27D4EB2Fu);
    return (1 + 2 * (h >> 25)) / 255.0;
}

// The layout follows the target. The full rect is 932 x 596; a smaller view (small mirror,
// low-res camera, self portrait) gets as many cells as fit. Writer and reader share the target,
// so both derive it from _ScreenParams. Fewer cells mean more clashes, still better than no
// atlas. The floor is about a tenth of the full grid; a 256 square target holds 434 cells.
#define YAPS_ATLAS_MINCELLS 384

struct YapsAtlasLayout { int cols; int cells; int rows; };

YapsAtlasLayout YapsAtlasLayoutNow()
{
    YapsAtlasLayout l;
    int w = (int) _ScreenParams.x;
    int h = (int) _ScreenParams.y;
    l.cols = clamp((w - YAPS_ATLAS_ORIGIN) / (YAPS_ATLAS_CELLSLOTS * YAPS_ATLAS_SLOTPX), 0, YAPS_ATLAS_COLS);
    int rowsFree = max((h - YAPS_ATLAS_ORIGIN) / (YAPS_ATLAS_LEVELS * YAPS_ATLAS_SLOTPX), 0);
    l.cells = min(YAPS_ATLAS_GRID * YAPS_ATLAS_GRID, l.cols * rowsFree);
    // Rounded UP. Columns need not divide the cells, and a floor here
    // put a level's last row on top of the next level's first.
    l.rows = max((l.cells + l.cols - 1) / max(l.cols, 1), 1);
    return l;
}

// Where a cell's slots start.
void YapsAtlasCellPixels(int idx, int level, YapsAtlasLayout l, out int cellPx, out int cellPy)
{
    int gx = idx % max(l.cols, 1);
    int gy = idx / max(l.cols, 1);
    cellPx = YAPS_ATLAS_ORIGIN + gx * YAPS_ATLAS_CELLSLOTS * YAPS_ATLAS_SLOTPX;
    cellPy = YAPS_ATLAS_ORIGIN + (level * l.rows + gy) * YAPS_ATLAS_SLOTPX;
}


// --- reading ---------------------------------------------------------
//
// .Load, never a sample. Filtering blends neighbouring cells.
// Stereo instancing grabs an array. Both slices match.
#if defined(UNITY_STEREO_INSTANCING_ENABLED)
    Texture2DArray _YAPS_Atlas;
    #define YAPS_ATLAS_LOAD(px, py) _YAPS_Atlas.Load(int4(px, py, 0, 0))
#else
    Texture2D _YAPS_Atlas;
    #define YAPS_ATLAS_LOAD(px, py) _YAPS_Atlas.Load(int3(px, py, 0))
#endif
cbuffer YapsAtlas { float4 _YAPS_Atlas_TexelSize; };

// Compile-time fact, never _TexelSize.y's sign.
// That sign is for UV samples. Wrong here.
int YapsAtlasRow(int fromTop)
{
#if UNITY_UV_STARTS_AT_TOP
    return fromTop;
#else
    return int(_YAPS_Atlas_TexelSize.w) - 1 - fromTop;
#endif
}

// The radius caps sockets per shaft, not the list.
// Three cells, so three sockets. Two costs 125 reads.
#define YAPS_ATLAS_RADIUS 1
#define YAPS_ATLAS_REACH  1.6
// How far from the base, in lengths, the scan is sure to see a socket:
// the deform's full-engagement distance. Reader only.
#define YAPS_ATLAS_COVER  1.2

// The rect. The clear must cover it exactly.
int YapsAtlasWidthPx()
{
    return YAPS_ATLAS_ORIGIN + YapsAtlasLayoutNow().cols * YAPS_ATLAS_CELLSLOTS * YAPS_ATLAS_SLOTPX;
}

int YapsAtlasHeightPx()
{
    return YAPS_ATLAS_ORIGIN + YAPS_ATLAS_LEVELS * YapsAtlasLayoutNow().rows * YAPS_ATLAS_SLOTPX;
}

// Can this target hold a layout worth reading? Painting a rect bigger
// than the target erased the self portrait; the layout never is.
bool YapsAtlasFits()
{
    return YapsAtlasLayoutNow().cells >= YAPS_ATLAS_MINCELLS;
}

// --- the margin directory -----------------------------------------------
//
// Rows 0 to 7, unused by cells, so the cell protocol stays 9. A portal exit publishes its pose
// keyed by owner and entrance number. Four pixels an entry (position fraction and tag, facing
// and number, cell index, owner): the floor, as key 28 bits + pose 72 = 100 > three pixels' 96.
#define YAPS_ATLAS_DIR_VERSION 1
#define YAPS_ATLAS_DIR_ROWS    8
#define YAPS_ATLAS_DIR_PX      4
// The exit's cell edge for an 8-bit cell index per axis: an exit within
// +-41 m of its entrance, unwrapped there, so anywhere in the world.
#define YAPS_ATLAS_DIR_CELL    0.32

// Two homes, B read only on a miss; slots by multiply and shift, never a divide. GPU time
// cannot tell one home from two. With 20 live, one home loses 0.5% at 1864 slots and 2.0% at
// 472, two homes 0.02% and 0.23% (simulated).

int3 YapsDirKey(int owner, int n)
{
    return int3(owner & 0xFFF, owner >> 12, n);
}

// As wide as the rect, so the clear already covers it. Unsigned throughout:
// a signed divide is a longer sequence and a warning on every host.
uint YapsDirPerRow()
{
    return max((uint) YapsAtlasWidthPx() / YAPS_ATLAS_DIR_PX, 1u);
}

uint YapsDirWord(int owner, int n)
{
    return YapsAtlasMix(YapsDirKey(owner, n), 0x1B56C4E9u + YAPS_ATLAS_DIR_VERSION * 0x27D4EB2Fu);
}

// Odd numerators, for the 8-bit reason YapsAtlasTag gives.
float YapsDirTag(int owner, int n)
{
    return (1 + 2 * (YapsDirWord(owner, n) >> 25)) / 255.0;
}

// Where home 0 or 1 of (owner, n) starts: column in pixels, row from the top.
// Writer and reader must address it bit for bit alike.
void YapsDirHome(int owner, int n, int home, out uint px, out uint row)
{
    uint perRow = YapsDirPerRow();
    // 16 bits of column against at most 233 slots a row, so the multiply
    // stays inside 32 bits and every slot is as likely as another.
    uint h = home == 0 ? YapsDirWord(owner, n) : YapsAtlasMix(YapsDirKey(owner, n), 0xB5297A4Du);
    px = (((h & 0xFFFF) * perRow) >> 16) * YAPS_ATLAS_DIR_PX;
    row = (h >> 16) & (YAPS_ATLAS_DIR_ROWS - 1);
}

// Is this directory pixel the entry for (owner, n)? Alpha 0 is empty, and the
// margin's rgb is never cleared, so the alpha gate comes first.
bool YapsDirTagged(float4 p0, float tagWant)
{
    return p0.a >= 0.5 && abs((p0.a - 0.5) * 2 - tagWant) <= 0.001;
}

// The writer's pixel i of the entry. The cell index keeps only its low byte
// per axis; the reader puts back the rest from the entrance.
float4 YapsDirEncode(int owner, int n, float3 at, float3 fwd, int i)
{
    float3 s = at / YAPS_ATLAS_DIR_CELL;
    int3 c = int3(floor(s)) & 0xFF;
    return i == 0 ? float4(frac(s), 0.5 + 0.5 * YapsDirTag(owner, n))
         : i == 1 ? float4(fwd * 0.5 + 0.5, (1 + n) / (float) YAPS_ATLAS_OWNERMAX)
         : i == 2 ? YapsOwnerEncode(c.x | (c.y << 8) | (c.z << 16))
         : YapsOwnerEncode(owner);
}

// The three pixels after a tagged one. Number and owner must match too.
// The cell is the one nearest the entrance with that low byte, so the reach
// is +-41 m from the entrance, anywhere in the world.
bool YapsDirRead(uint px, int py, int owner, int n, float4 p0, inout float3 exitAt, inout float3 exitFwd)
{
    float4 p1 = YAPS_ATLAS_LOAD(px + 1, py);
    float3 f = p1.rgb * 2 - 1;
    // No facing, no exit: a shaft leaving along nothing is a NaN.
    bool ok = abs(p1.a * YAPS_ATLAS_OWNERMAX - (1 + n)) <= 0.5 && dot(f, f) >= 0.25;
    if (ok) ok = YapsOwnerDecode(YAPS_ATLAS_LOAD(px + 3, py)) == owner;
    if (ok)
    {
        int c = YapsOwnerDecode(YAPS_ATLAS_LOAD(px + 2, py));
        int3 near = int3(floor(exitAt / YAPS_ATLAS_DIR_CELL));
        int3 cell = near + ((((int3(c, c >> 8, c >> 16) - near) & 0xFF) ^ 0x80) - 0x80);
        exitAt = (float3(cell) + p0.rgb) * YAPS_ATLAS_DIR_CELL;
        exitFwd = normalize(f);
    }
    return ok;
}

// Exit of the hole keyed (owner << 4 | number). Tag, number and owner must all match, so a
// clash fails safe to a plain hole. exitAt comes in as the entrance; written only on a hit.
// One return: fxc warns on early ones as maybe-unset values.
bool YapsPortalExit(int key, inout float3 exitAt, inout float3 exitFwd)
{
    int owner = key >> 4;
    int n = key & 15;
    float tagWant = YapsDirTag(owner, n);
    uint px, row;
    YapsDirHome(owner, n, 0, px, row);
    float4 p0 = YAPS_ATLAS_LOAD(px, YapsAtlasRow(row));
    bool ok = YapsDirTagged(p0, tagWant);
    if (!ok)
    {
        YapsDirHome(owner, n, 1, px, row);
        p0 = YAPS_ATLAS_LOAD(px, YapsAtlasRow(row));
        ok = YapsDirTagged(p0, tagWant);
    }
    if (ok) ok = YapsDirRead(px, YapsAtlasRow(row), owner, n, p0, exitAt, exitFwd);
    return ok;
}

// --- the light relay ------------------------------------------------------
//
// Unity fills vertex lights for ForwardBase alone, so caster, add and outline passes drew
// the plug straight (a ghost in the depth texture). A relay renderer per plug, drawn before
// the grab with the plug's bounds, writes the sockets its lights name here; those passes read
// it a frame late. A light's shadow map cannot: it binds the texture the atlas name held at
// frame start. Columns 0 to 7 under the directory, so the cell protocol stays 9.
// Eight pixels an entry: tag and number, owner, then per root (two: four slots hold two
// sockets with fronts) position fraction and kind, facing, cell index. Ownership is the
// reader's, which alone knows where the plug starts.
#define YAPS_RELAY_VERSION 1
#define YAPS_RELAY_PX      8
#define YAPS_RELAY_ROOTS   2
#define YAPS_RELAY_SLOTS   512
// 8 bits per axis at 2 cm is +-2.56 m, beyond any marker light's range (under 0.5 m);
// the fraction keeps 1/12 mm.
#define YAPS_RELAY_CELL    0.02

uint YapsRelayWord(int owner, int n)
{
    return YapsAtlasMix(int3(owner & 0xFFF, owner >> 12, 16 + n), 0x6A09E667u + YAPS_RELAY_VERSION * 0x27D4EB2Fu);
}

float YapsRelayTag(int owner, int n)
{
    return (1 + 2 * (YapsRelayWord(owner, n) >> 25)) / 255.0;
}

// Row from the top of home 0 or 1, both under the directory.
uint YapsRelayRow(int owner, int n, int home)
{
    uint h = home == 0 ? YapsRelayWord(owner, n) : YapsAtlasMix(int3(owner & 0xFFF, owner >> 12, 16 + n), 0xBB67AE85u);
    return YAPS_ATLAS_DIR_ROWS + (((h & 0xFFFF) * YAPS_RELAY_SLOTS) >> 16);
}

// The writer's side: only where the strip lies inside the cleared rect, or its
// pixels would stay on screen.
bool YapsRelayFits()
{
    return YapsAtlasFits() && YapsAtlasHeightPx() >= YAPS_ATLAS_DIR_ROWS + YAPS_RELAY_SLOTS;
}

// Outside the cube, so the clipper drops it.
// z only has to suit both depth conventions.
float4 YapsAtlasNowhere()
{
    return float4(2, 2, 0.5, 1);
}

// Pixels to clip space. Rows count from the top.
// Never times _ProjectionParams.x. Tried; broke the view.
float2 YapsAtlasToClip(float2 atPx)
{
    float2 p = atPx / _ScreenParams.xy * 2.0 - 1.0;
    p.y = -p.y;
    return p;
}

#endif
