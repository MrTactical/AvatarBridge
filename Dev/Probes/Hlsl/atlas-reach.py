# Which sockets the atlas scan can see, from the shader's own arithmetic.
# Mirrors the level choice and the 3x3x3 block in YapsResolveAtlas ("cover" is the shipped
# rule from 2026-09-23; "round" what shipped before, "ceil" a tried half-way)
# (Editor/Yaps/yaps_resolve.cginc) and the writer's cell in
# YapsAtlasSocket.shader; change both together.
#
# For each plug length it drops the plug at random places and turns, puts a
# socket at a set distance past the tip (or beside it), and counts how often
# the socket's cell falls outside the block the plug reads. Anything above
# zero is a dead zone that moves as the plug moves.
# Run: python Dev/Probes/Hlsl/atlas-reach.py
import math, random

CELL, LEVELS, RADIUS = 0.02, 4, 1


COVER = 1.2   # YAPS_ATLAS_COVER


def level(length, rule):
    if rule == "cover":
        x = math.log2(length * COVER / max(RADIUS, 1) / CELL) * 0.5
        return max(0, min(LEVELS - 1, math.ceil(x - 1e-4)))
    want = length / max(2.0 * RADIUS, 1.0)
    x = math.log2(want / CELL) * 0.5
    lvl = round(x) if rule == "round" else math.ceil(x - 1e-9)
    return max(0, min(LEVELS - 1, int(lvl)))


def seen(root, axis, length, socket, rule):
    size = CELL * 4 ** level(length, rule)
    # "cover" scans round the base; the old rules round the middle.
    half = 0.0 if rule == "cover" else 0.5
    mid = [root[i] + axis[i] * length * half for i in range(3)]
    mine = [math.floor(mid[i] / size) for i in range(3)]
    cell = [math.floor(socket[i] / size) for i in range(3)]
    return all(abs(cell[i] - mine[i]) <= RADIUS for i in range(3))


def unit():
    while True:
        v = [random.uniform(-1, 1) for _ in range(3)]
        n = math.sqrt(sum(c * c for c in v))
        if 0.1 < n <= 1:
            return [c / n for c in v]


def side_of(axis):
    s = unit()
    d = sum(s[i] * axis[i] for i in range(3))
    s = [s[i] - axis[i] * d for i in range(3)]
    n = math.sqrt(sum(c * c for c in s))
    return [c / n for c in s]


def missed(length, where, rule, trials=20000):
    random.seed(1)
    miss = 0
    for _ in range(trials):
        root = [random.uniform(-5, 5) for _ in range(3)]
        axis = unit()
        tip = [root[i] + axis[i] * length for i in range(3)]
        if where[0] == "ball":
            # anywhere within that many lengths of the base
            d = unit(); r = random.random() ** (1 / 3) * where[1] * length
            p = [root[i] + d[i] * r for i in range(3)]
        elif where[0] == "past":
            p = [tip[i] + axis[i] * where[1] * length for i in range(3)]
        else:
            s = side_of(axis)
            p = [tip[i] + s[i] * where[1] * length for i in range(3)]
        miss += not seen(root, axis, length, p, rule)
    return miss / trials


cases = [("past", 0.0), ("past", 0.1), ("past", 0.2), ("side", 0.2), ("side", 0.4), ("ball", 1.2)]
for rule in ("round", "ceil", "cover"):
    print(f"level rule: {rule}")
    print("  length  cell   " + "  ".join(f"{w}{d:+.1f}L" for w, d in cases))
    for length in (0.10, 0.15, 0.20, 0.25, 0.30, 0.40, 0.586, 0.80, 1.20):
        size = CELL * 4 ** level(length, rule)
        row = "  ".join(f"{missed(length, c, rule) * 100:8.0f}%" for c in cases)
        print(f"  {length:5.3f}  {size:5.2f}  {row}")
