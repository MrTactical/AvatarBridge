# The atlas layout's arithmetic, checked across target sizes. Mirrors
# YapsAtlasLayoutNow in Editor/Yaps/yaps_atlas.cginc; change both together.
# Run: python Dev/Probes/Hlsl/atlas-layout.py
GRID, COLS, SLOTPX, ORIGIN, LEVELS, OCTPX, MINCELLS = 64, 28, 1, 8, 4, 4, 384
CELLSLOTS = 1 + 8 * OCTPX


def layout(w, h):
    cols = max(0, min((w - ORIGIN) // (CELLSLOTS * SLOTPX), COLS))
    rows_free = max((h - ORIGIN) // (LEVELS * SLOTPX), 0)
    cells = min(GRID * GRID, cols * rows_free)
    rows = max((cells + cols - 1) // max(cols, 1), 1)
    return cols, cells, rows


def rect(w, h):
    cols, cells, rows = layout(w, h)
    return ORIGIN + cols * CELLSLOTS * SLOTPX, ORIGIN + LEVELS * rows * SLOTPX


# Where the full rect fits, nothing changed: 28 columns, 4096 cells, 932 x 596.
for w, h in [(932, 596), (1024, 1024), (1920, 1080), (4096, 4096)]:
    assert layout(w, h) == (28, 4096, 147), (w, h, layout(w, h))
    assert rect(w, h) == (932, 596)

fits = 0
for w in range(1, 1400, 7):
    for h in range(1, 1400, 7):
        cols, cells, rows = layout(w, h)
        if cells < MINCELLS:
            continue
        fits += 1
        rw, rh = rect(w, h)
        assert rw <= w and rh <= h, (w, h, rw, rh)          # never paints past the target
        assert cols * rows >= cells, (w, h)                   # every cell has a place
        assert (cells - 1) // cols < rows, (w, h)             # the last cell stays in its level

assert layout(256, 256)[1] >= MINCELLS, layout(256, 256)
assert layout(200, 200)[1] < MINCELLS
print("ok:", fits, "sizes fit;", "256 square:", layout(256, 256), "512 square:", layout(512, 512))

# The portal directory (spike, Dev/Probes/Portal/yaps_atlas.cginc): rows 0 to 7,
# four pixels an entry, as many a row as the rect is wide. It must sit inside
# the clear's span (YapsAtlasClear: the rect plus 4) and above every cell,
# which starts at row ORIGIN.
DIR_ROWS, DIR_PX = 8, 4


def directory(w, h):
    per_row = max(rect(w, h)[0] // DIR_PX, 1)
    return per_row, per_row * DIR_ROWS, per_row * DIR_PX


checked = 0
for w in range(1, 2000, 3):
    for h in range(1, 1200, 3):
        if layout(w, h)[1] < MINCELLS:
            continue
        checked += 1
        rw, rh = rect(w, h)
        per_row, slots, span = directory(w, h)
        assert span <= rw <= w and DIR_ROWS <= min(ORIGIN, rh), (w, h)   # inside the clear, the target, above the cells
        assert (0xFFFF * per_row) >> 16 < per_row < 1 << 16                # lookup 2's column stays in the row and 32 bits

print("directory inside the clear and above the cells at", checked, "sizes; per size:")
for w, h in [(239, 239), (256, 256), (512, 512), (932, 596), (1024, 1024), (1920, 1080), (238, 238)]:
    if layout(w, h)[1] < MINCELLS:
        print(f"  {w}x{h}: below the floor, the writer draws nothing")
        continue
    rw, rh = rect(w, h)
    per_row, slots, span = directory(w, h)
    print(f"  {w}x{h}: rect {rw}x{rh}, clear {rw + 4}x{rh + 4}, {per_row} a row, {slots} slots, x 0-{span - 1} of rows 0-{DIR_ROWS - 1}")
