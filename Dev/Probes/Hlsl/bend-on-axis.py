# Whether the bend thrashes when a socket sits on the plug's own axis. Mirrors the shipped
# yaps_deform.cginc path for one socket: the socket's axis flipped to meet the approach, the
# sweep (the aim turned back toward the plug's forward about the root by the part not engaged,
# the axis falling back to the plug's up below sin 1e-5), then the cubic walked by arc length.
# Gain is how far a point on the shaft moves per unit the socket moves sideways. An unstable
# direction on the axis would show as a gain far above the same socket's off the axis.
import math
import importlib.util, os

spec = importlib.util.spec_from_file_location("swing", os.path.join(os.path.dirname(__file__), "engage-swing.py"))
swing = importlib.util.module_from_spec(spec)
spec.loader.exec_module(swing)
bez, walk, ramp, L = swing.bez, swing.walk, swing.ramp, swing.L

def add(a, b): return [x + y for x, y in zip(a, b)]
def sub(a, b): return [x - y for x, y in zip(a, b)]
def mul(a, s): return [x * s for x in a]
def dot(a, b): return sum(x * y for x, y in zip(a, b))
def cross(a, b): return [a[1]*b[2] - a[2]*b[1], a[2]*b[0] - a[0]*b[2], a[0]*b[1] - a[1]*b[0]]
def norm(a, fallback):
    n = math.sqrt(dot(a, a))
    return mul(a, 1 / n) if n * n >= 1e-12 else list(fallback)

def turn(v, axis, angle):
    s, c = math.sin(angle), math.cos(angle)
    return add(add(mul(v, c), mul(cross(axis, v), s)), mul(axis, dot(axis, v) * (1 - c)))

FORWARD, UP = [0.0, 0.0, 1.0], [0.0, 1.0, 0.0]

def shaft(sock, facing):
    gap = math.sqrt(dot(sock, sock))
    f = norm(facing, norm(sock, FORWARD))
    if dot(f, sock) < 0: f = mul(f, -1)
    e = 1 - ramp(gap, 1.2 * L, 1.6 * L)
    aim = norm(sock, FORWARD)
    axis = cross(aim, FORWARD)
    s = math.sqrt(dot(axis, axis))
    axis = mul(axis, 1 / s) if s > 1e-5 else UP
    sweep = math.acos(max(-1, min(1, dot(aim, FORWARD)))) * (1 - e)
    at, f = turn(sock, axis, sweep), turn(f, axis, sweep)
    h = gap * 0.5
    p = [[0, 0, 0], mul(FORWARD, h), sub(at, mul(f, h)), at]
    return [walk(p, z * L) for z in (0.25, 0.5, 0.75, 1.0)]

def gain(centre, facing, nudge=0.0005, turns=16):
    rest = shaft(centre, facing)
    worst = 0.0
    side = norm(cross(norm(centre, FORWARD), UP), [1.0, 0.0, 0.0])
    if abs(dot(norm(centre, FORWARD), UP)) > 0.99: side = [1.0, 0.0, 0.0]
    other = cross(norm(centre, FORWARD), side)
    for k in range(turns):
        a = 2 * math.pi * k / turns
        moved = add(centre, mul(add(mul(side, math.cos(a)), mul(other, math.sin(a))), nudge))
        worst = max(worst, max(math.dist(x, y) for x, y in zip(shaft(moved, facing), rest)) / nudge)
    return worst

if __name__ == "__main__":
    rows = []
    for g in (0.3, 0.6, 0.9, 1.2, 1.3, 1.45):
        for tilt in (0, 30):
            facing = [math.sin(math.radians(tilt)), 0.0, -math.cos(math.radians(tilt))]
            on = gain([0.0, 0.0, g * L], facing)
            off = gain([g * L * math.sin(math.radians(20)), 0.0, g * L * math.cos(math.radians(20))], facing)
            rows.append((g, tilt, on, off))
            print(f"gap {g:4.2f}L, socket tilted {tilt:2d} deg: gain on the axis {on:5.2f}, 20 deg off it {off:5.2f}")
    # Ill-conditioned would mean the axis far worse than off it. A gain near 1 is the tip
    # following the socket, which any bend must do.
    assert all(on < 2 * max(off, 1.0) for _, _, on, off in rows), rows
