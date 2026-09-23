# Which atlas read hands back a socket in the wrong place. Replays the writer
# (YapsAtlasSocket.shader: every level, both homes) and the reader
# (yaps_resolve.cginc: fine level round the shaft's middle, coverage level
# round the base) in slot-pixel space with HLSL's 32-bit int arithmetic, and
# lists every read that lands on a written pixel and passes the cell tag
# for a cell that is not the writer's. The tester found one on a 0.330 m
# plug: the socket at 45 degrees also decoded two fine cells behind itself,
# its mirror across the vertical axis through the world's origin, because
# the protocol 8 hash gave h(-x, y, -z) == h(x, y, z) for odd x and z.
# Protocol 9 mixes one word per cell; both are kept here to compare.
import math
import random
import sys


GRID, COLS, SLOTPX, ORIGIN, CELL, LEVELS, CELLSLOTS = 64, 28, 1, 8, 0.02, 4, 1 + 8 * 4
PROTOCOL = 9
RADIUS, COVER = 1, 1.2

def i32(v):
    v &= 0xFFFFFFFF
    return v - (1 << 32) if v & 0x80000000 else v

def cmod(a, b):
    # HLSL % truncates toward zero, like C.
    r = abs(a) % abs(b)
    return -r if a < 0 else r

def mix(c, salt):
    M = 0xFFFFFFFF
    h = ((c[0] & M) * 0x8DA6B343 + (c[1] & M) * 0xD8163841 + (c[2] & M) * 0xCB1AB31F + salt) & M
    h ^= h >> 16
    h = (h * 0x7FEB352D) & M
    h ^= h >> 15
    h = (h * 0x846CA68B) & M
    h ^= h >> 16
    return h

def hash1(c):
    if PROTOCOL == 8:
        return i32(i32(i32(c[0] * 73856093) ^ i32(c[1] * 19349663)) ^ i32(c[2] * 83492791))
    return mix(c, 0x9E3779B9) & 0x7FFFFFFF

def hash2(c):
    if PROTOCOL == 8:
        return i32(i32(i32(c[0] * 12582917) ^ i32(c[1] * 3145739)) ^ i32(c[2] * 6291469))
    return mix(c, 0x85EBCA6B) & 0x7FFFFFFF

def tag(c):
    if PROTOCOL == 8:
        h = i32(i32(i32(i32(c[0] * 19349663) ^ i32(c[1] * 83492791)) ^ i32(c[2] * 73856093)) ^ i32(8 * 1566083941))
        h &= 0x7FFFFF
        return (1 + 2 * (h % 128)) / 255.0
    return (1 + 2 * (mix(c, (0xC2B2AE35 + PROTOCOL * 0x27D4EB2F) & 0xFFFFFFFF) >> 25)) / 255.0

def layout(w, h):
    cols = max(0, min((w - ORIGIN) // (CELLSLOTS * SLOTPX), COLS))
    rows_free = max((h - ORIGIN) // (LEVELS * SLOTPX), 0)
    cells = min(GRID * GRID, cols * rows_free)
    rows = max((cells + cols - 1) // max(cols, 1), 1)
    return cols, rows, cells

def slot(c, home, total):
    idx = cmod(hash1(c), total)
    if idx < 0:
        idx += total
    if home == 1:
        step = cmod(hash2(c), max(total - 1, 1))
        if step < 0:
            step += max(total - 1, 1)
        idx = (idx + step + 1) % total
    return idx

def pixel(idx, level, lay):
    cols, rows, _ = lay
    return ORIGIN + (idx % cols) * CELLSLOTS * SLOTPX, ORIGIN + (level * rows + idx // cols) * SLOTPX

def floor3(p, size):
    return tuple(int(math.floor(x / size)) for x in p)

def phantoms(root, fwd, length, socket, screen=(1024, 1024)):
    lay = layout(*screen)
    total = max(lay[2], 1)
    written = {}
    for level in range(LEVELS):
        size = CELL * 4 ** level
        cw = floor3(socket, size)
        for home in (0, 1):
            written[pixel(slot(cw, home, total), level, lay)] = (level, home, cw)
    cover = length * COVER / RADIUS
    lvl = max(0, min(int(math.ceil(math.log2(cover / CELL) * 0.5 - 1e-4)), LEVELS - 1))
    fine = max(0, min(int(round(math.log2(length * 0.5 / CELL) * 0.5)), lvl))
    found = []
    for scan, lv in enumerate((fine, lvl)):
        if scan == 1 and lv == fine:
            break
        size = CELL * 4 ** lv
        centre = [r + f * length * 0.5 for r, f in zip(root, fwd)] if scan == 0 else root
        mine = floor3(centre, size)
        for dx in range(-RADIUS, RADIUS + 1):
            for dy in range(-RADIUS, RADIUS + 1):
                for dz in range(-RADIUS, RADIUS + 1):
                    c = (mine[0] + dx, mine[1] + dy, mine[2] + dz)
                    for home in (0, 1):
                        px = pixel(slot(c, home, total), lv, lay)
                        if px not in written:
                            continue
                        wl, wh, cw = written[px]
                        if (wl, cw) == (lv, c):
                            continue   # the socket's own cell: a true read
                        wsize = CELL * 4 ** wl
                        f = [s / wsize - math.floor(s / wsize) for s in socket]
                        at = [(ci + fi) * size for ci, fi in zip(c, f)]
                        ok = abs(tag(c) - tag(cw)) <= 0.001
                        found.append((scan, lv, c, home, wl, wh, cw, ok, at))
    return found

def survey(protocol, trials=3000, seed=7):
    # Plugs of 0.15 to 1 m stood anywhere within 3 m of the origin, a socket
    # anywhere within 1.2 lengths: how many placements decode a phantom
    # that passes the tag and sits within reach (2.56 lengths).
    global PROTOCOL
    PROTOCOL = protocol
    rng = random.Random(seed)
    bad = 0
    for _ in range(trials):
        length = rng.uniform(0.15, 1.0)
        root = [rng.uniform(-3, 3), rng.uniform(0, 2), rng.uniform(-3, 3)]
        fwd = [rng.gauss(0, 1) for _ in range(3)]
        n = math.sqrt(sum(f * f for f in fwd))
        fwd = [f / n for f in fwd]
        d = [rng.gauss(0, 1) for _ in range(3)]
        n = math.sqrt(sum(x * x for x in d))
        r = rng.uniform(0.1, 1.2) * length
        socket = [a + x / n * r for a, x in zip(root, d)]
        for hit in phantoms(root, fwd, length, socket):
            at = hit[-1]
            if hit[7] and math.dist(at, root) <= 2.56 * length:
                bad += 1
                break
    return bad, trials

if __name__ == "__main__":
    if "--survey" in sys.argv:
        for protocol in (8, 9):
            bad, trials = survey(protocol)
            print(f"protocol {protocol}: {bad} of {trials} placements decode a phantom in reach")
        bad9, trials = survey(9)
        # Random clashes remain at 1 in 128 per shared slot; a systematic
        # one would show in the hundreds.
        assert bad9 <= trials * 0.005, bad9
        sys.exit(0)
    PROTOCOL = 8
    # The tester's frame at the jump on the 0.330 m plug.
    root, fwd, length = (0.0, 0.77775, 0.13621), (-0.00001, 0.09337, 0.99563), 0.32974
    socket = (0.32083, 0.80771, 0.45564)
    hits = phantoms(root, fwd, length, socket)
    for scan, lv, c, home, wl, wh, cw, ok, at in hits:
        off = [a - r for a, r in zip(at, root)]
        d = math.sqrt(sum(o * o for o in off))
        ang = math.degrees(math.acos(max(-1, min(1, sum(o * f for o, f in zip(off, fwd)) / d))))
        print(f"read {'fine' if scan == 0 else 'coverage'} level {lv} cell {c} home {home} lands on "
              f"level {wl} home {wh} cell {cw}; tag {'PASSES' if ok else 'fails'}; "
              f"decodes {d / length:.3f}L at {ang:.0f} deg")
    if not hits:
        print("no read lands on a written pixel for another cell")
