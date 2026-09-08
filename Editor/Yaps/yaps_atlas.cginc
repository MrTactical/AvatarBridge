// Screen atlas protocol. Sockets write, plugs read.
// Defines, not properties: every avatar must agree.
#ifndef YAPS_ATLAS_INCLUDED
#define YAPS_ATLAS_INCLUDED

// Bump on any change below. It rides the tag.
#define YAPS_ATLAS_VERSION 3

// 4096 cells. Two homes, so a clash needs both.
#define YAPS_ATLAS_GRID    64

// Columns, not the grid. A rect wider than
// the view clips slots, and they read as opaque.
#define YAPS_ATLAS_COLS    32

#define YAPS_ATLAS_SLOTPX  1
#define YAPS_ATLAS_ORIGIN  8

// Level 0 cell size. Each level is four times.
#define YAPS_ATLAS_CELL    0.02
#define YAPS_ATLAS_LEVELS  4

// A header, then eight octants.
// One header pixel, then THREE per octant: position, facing, tags.
// Version 3 added the third. A socket's tag set is 15 bits and there was
// nowhere left to put it: the facing pixel's alpha carries the kind, and
// the position pixel's carries the owner tag the whole read is gated on.
#define YAPS_ATLAS_CELLSLOTS 25

// The tag set, 15 bits over rgb at five bits a channel. Five, not eight:
// the grab is a half float and these are written once rather than summed,
// so eight would probably survive, but a tag that decodes wrong sends a
// plug to the wrong socket silently and 32 levels a channel cannot.
#define YAPS_ATLAS_TAGBITS 5
#define YAPS_ATLAS_TAGMAX  31

int YapsTagsDecode(float3 rgb)
{
    int lo = (int) round(saturate(rgb.r) * YAPS_ATLAS_TAGMAX);
    int mid = (int) round(saturate(rgb.g) * YAPS_ATLAS_TAGMAX);
    int hi = (int) round(saturate(rgb.b) * YAPS_ATLAS_TAGMAX);
    return lo | (mid << YAPS_ATLAS_TAGBITS) | (hi << (2 * YAPS_ATLAS_TAGBITS));
}

float3 YapsTagsEncode(int tags)
{
    return float3(
        (tags & YAPS_ATLAS_TAGMAX) / (float) YAPS_ATLAS_TAGMAX,
        ((tags >> YAPS_ATLAS_TAGBITS) & YAPS_ATLAS_TAGMAX) / (float) YAPS_ATLAS_TAGMAX,
        ((tags >> (2 * YAPS_ATLAS_TAGBITS)) & YAPS_ATLAS_TAGMAX) / (float) YAPS_ATLAS_TAGMAX);
}

int YapsAtlasHash(int3 c)
{
    int h = c.x * 73856093;
    h ^= c.y * 19349663;
    h ^= c.z * 83492791;
    return h;
}

// The second home. Two slots square the odds.
int YapsAtlasHash2(int3 c)
{
    int h = c.x * 12582917;
    h ^= c.y * 3145739;
    h ^= c.z * 6291469;
    return h;
}

// Who owns the payload. An offset alone cannot say.
float YapsAtlasTag(int3 c)
{
    int h = c.x * 19349663;
    h ^= c.y * 83492791;
    h ^= c.z * 73856093;
    h ^= YAPS_ATLAS_VERSION * 1566083941;
    h = h & 0x7FFFFF;
    return (h % 256) / 255.0;
}

// Where a cell's slots start.
void YapsAtlasCellPixels(int idx, int level, out int cellPx, out int cellPy)
{
    int rowsPerLevel = max(YAPS_ATLAS_GRID * YAPS_ATLAS_GRID / YAPS_ATLAS_COLS, 1);
    int gx = idx % YAPS_ATLAS_COLS;
    int gy = idx / YAPS_ATLAS_COLS;
    cellPx = YAPS_ATLAS_ORIGIN + gx * YAPS_ATLAS_CELLSLOTS * YAPS_ATLAS_SLOTPX;
    cellPy = YAPS_ATLAS_ORIGIN + (level * rowsPerLevel + gy) * YAPS_ATLAS_SLOTPX;
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
float4 _YAPS_Atlas_TexelSize;

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

// The rect. The clear must cover it exactly.
int YapsAtlasWidthPx()
{
    return YAPS_ATLAS_ORIGIN + YAPS_ATLAS_COLS * YAPS_ATLAS_CELLSLOTS * YAPS_ATLAS_SLOTPX;
}

int YapsAtlasHeightPx()
{
    int rowsPerLevel = max(YAPS_ATLAS_GRID * YAPS_ATLAS_GRID / YAPS_ATLAS_COLS, 1);
    return YAPS_ATLAS_ORIGIN + YAPS_ATLAS_LEVELS * rowsPerLevel * YAPS_ATLAS_SLOTPX;
}

// Can this target hold the rect at all?
// Painting a smaller one erased the self portrait.
bool YapsAtlasFits()
{
    return _ScreenParams.x >= YapsAtlasWidthPx() && _ScreenParams.y >= YapsAtlasHeightPx();
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
