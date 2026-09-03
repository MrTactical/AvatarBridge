// The screen atlas protocol: where a socket writes and where a plug looks.
//
// These are #defines rather than material properties on purpose. Every avatar
// in the instance shares them, because a socket hashes its world position
// with them and a plug addresses cells with them, so a property here is a
// slider somebody can drag and a dragged constant does not read as absence.
// It reads as a real socket a few centimetres from where it is, which is the
// phantom the tag exists to catch.
//
// Bump YAPS_ATLAS_VERSION whenever ANY of them changes. The version rides the
// tag hash, so a mismatch is dropped by the check the reader already runs.
#ifndef YAPS_ATLAS_INCLUDED
#define YAPS_ATLAS_INCLUDED

#define YAPS_ATLAS_VERSION 2

// 4096 cells. Sockets clash by hashing to a slot another cell owns, and the
// second home is the recovery, so what matters is clashing on BOTH. At 120
// sockets, an ordinary public instance, that is about one in twelve hundred.
// A grid of 16 loses one socket in seven.
#define YAPS_ATLAS_GRID    64

// COLUMNS, which is not the grid. The two were the same number for no reason,
// which made the rect a wide strip: 4096 cells at 17 pixels is 1088 px
// across, and a camera narrower than that clips the right-hand columns. A
// clipped slot does not read as absent, it reads as the opaque screen. Found
// in the editor as a bend dropping out every few centimetres and returning
// when the view was widened. 32 columns is the same cells in 552 x 520, which
// fits a 720p view, a mirror texture and an eye buffer.
#define YAPS_ATLAS_COLS    32

#define YAPS_ATLAS_SLOTPX  1
#define YAPS_ATLAS_ORIGIN  8

// Level 0 cell size in metres. Each level is four times the last, so four of
// them span two centimetres to one metre thirty.
#define YAPS_ATLAS_CELL    0.02
#define YAPS_ATLAS_LEVELS  4

// 17 slots to a cell: a header, then eight octants of position and facing.
#define YAPS_ATLAS_CELLSLOTS 17

int YapsAtlasHash(int3 c)
{
    int h = c.x * 73856093;
    h ^= c.y * 19349663;
    h ^= c.z * 83492791;
    return h;
}

// The second home. A bigger grid only makes a clash rarer; two independent
// slots make it one in N squared.
int YapsAtlasHash2(int3 c)
{
    int h = c.x * 12582917;
    h ^= c.y * 3145739;
    h ^= c.z * 6291469;
    return h;
}

// Who owns this payload. A cell that merely shares a grid slot hands back
// somebody else's socket decoded against the wrong cell, and an offset within
// a cell decodes into that cell whichever cell you decode it against, so the
// payload cannot reveal it alone.
float YapsAtlasTag(int3 c)
{
    int h = c.x * 19349663;
    h ^= c.y * 83492791;
    h ^= c.z * 73856093;
    h ^= YAPS_ATLAS_VERSION * 1566083941;
    h = h & 0x7FFFFF;
    return (h % 256) / 255.0;
}

// Where a cell's slots start, given its index within a level.
void YapsAtlasCellPixels(int idx, int level, out int cellPx, out int cellPy)
{
    int rowsPerLevel = max(YAPS_ATLAS_GRID * YAPS_ATLAS_GRID / YAPS_ATLAS_COLS, 1);
    int gx = idx % YAPS_ATLAS_COLS;
    int gy = idx / YAPS_ATLAS_COLS;
    cellPx = YAPS_ATLAS_ORIGIN + gx * YAPS_ATLAS_CELLSLOTS * YAPS_ATLAS_SLOTPX;
    cellPy = YAPS_ATLAS_ORIGIN + (level * rowsPerLevel + gy) * YAPS_ATLAS_SLOTPX;
}

// The rect the atlas occupies, which the clear has to cover exactly.
int YapsAtlasWidthPx()
{
    return YAPS_ATLAS_ORIGIN + YAPS_ATLAS_COLS * YAPS_ATLAS_CELLSLOTS * YAPS_ATLAS_SLOTPX;
}

int YapsAtlasHeightPx()
{
    int rowsPerLevel = max(YAPS_ATLAS_GRID * YAPS_ATLAS_GRID / YAPS_ATLAS_COLS, 1);
    return YAPS_ATLAS_ORIGIN + YAPS_ATLAS_LEVELS * rowsPerLevel * YAPS_ATLAS_SLOTPX;
}

// Pixels to clip space. Rows count down from the top, which is why y flips.
float2 YapsAtlasToClip(float2 atPx)
{
    float2 p = atPx / _ScreenParams.xy * 2.0 - 1.0;
    p.y = -p.y;
    return p;
}

#endif
