# How fast the shaft swings while a socket comes into range, for the root
# handle schedules the deform could use. Mirrors yaps_deform.cginc: a cubic
# from the root (handle along the plug's forward) to the socket (handle along
# the approach), walked by arc length, engagement 1 - a linear ramp from 1.2L to 1.6L (YapsRamp).
# Gain is how far a point on the shaft moves per unit the socket moves; the
# tester measured 8 to 18 off-axis round 1.25 to 1.29 lengths.
import math

L = 1.0

def bez(p, t):
    u = 1 - t
    return [u*u*u*p[0][i] + 3*u*u*t*p[1][i] + 3*u*t*t*p[2][i] + t*t*t*p[3][i] for i in range(3)]

def walk(p, s, steps=400):
    # Point at arc length s, carried straight on past the end.
    prev, run = bez(p, 0), 0.0
    for k in range(1, steps + 1):
        cur = bez(p, k / steps)
        d = math.dist(prev, cur)
        if run + d >= s:
            f = (s - run) / d if d > 0 else 0
            return [prev[i] + (cur[i] - prev[i]) * f for i in range(3)]
        run, prev = run + d, cur
    end, before = bez(p, 1), bez(p, 1 - 1 / steps)
    tan = [end[i] - before[i] for i in range(3)]
    n = math.sqrt(sum(c * c for c in tan)) or 1
    return [end[i] + tan[i] / n * (s - run) for i in range(3)]

def ramp(x, a, b):
    return min(max((x - a) / (b - a), 0), 1)

def slerp(a, b, t):
    dot = max(-1.0, min(1.0, sum(x * y for x, y in zip(a, b))))
    w = math.acos(dot)
    if w < 1e-6:
        return list(b)
    sa, sb = math.sin((1 - t) * w) / math.sin(w), math.sin(t * w) / math.sin(w)
    return [sa * x + sb * y for x, y in zip(a, b)]

# SWEEP: the handle stays the plain approach and the curve aims at a stand-in
# socket at the same distance, turned from straight ahead toward the real one
# by the engagement. At full engagement it is the real socket.
SCHEDULES = {
    "sweep":            None,
    "linear (shipped)": lambda far, near, e: far + (near - far) * e,
    "geometric":        lambda far, near, e: near * (far / near) ** (1 - e),
    "harmonic":         lambda far, near, e: 1 / (1 / far + (1 / near - 1 / far) * e),
}

def shaft(gap, angle, schedule):
    d = (math.sin(math.radians(angle)), 0.0, math.cos(math.radians(angle)))
    sock = [c * gap for c in d]
    e = 1 - ramp(gap, 1.2 * L, 1.6 * L)
    near = gap * 0.5
    if schedule is None:
        aim = slerp((0.0, 0.0, 1.0), d, e)
        at = [c * gap for c in aim]
        p = [[0, 0, 0], [0, 0, near], [at[i] - aim[i] * near for i in range(3)], at]
    else:
        h = schedule(5 * L, near, e)
        p = [[0, 0, 0], [0, 0, h], [sock[i] - d[i] * near for i in range(3)], sock]
    return [walk(p, z * L) for z in (0.25, 0.5, 0.75, 1.0)], e

def gain(angle, schedule, lo=1.1, hi=1.7, step=0.002):
    worst, at = 0.0, 0.0
    before, _ = shaft(hi, angle, schedule)
    g = hi - step
    while g >= lo:
        now, _ = shaft(g, angle, schedule)
        moved = max(math.dist(a, b) for a, b in zip(now, before))
        if moved / step > worst:
            worst, at = moved / step, g
        before, g = now, g - step
    return worst, at

if __name__ == "__main__":
    for name, s in SCHEDULES.items():
        row = []
        for angle in (45, 90, 135):
            w, at = gain(angle, s)
            row.append(f"{angle} deg: gain {w:4.1f} at {at:.3f}L")
        print(f"{name:18s} " + "   ".join(row))
    # The linear schedule must reproduce the tester's peak, or the model is wrong.
    w, at = gain(90, SCHEDULES["linear (shipped)"])
    assert w > 8 and 1.2 < at < 1.35, (w, at)
    # The sweep must end where the shipped deform ends, fully engaged.
    a, _ = shaft(1.2, 90, SCHEDULES["linear (shipped)"])
    b, _ = shaft(1.2, 90, None)
    assert max(math.dist(x, y) for x, y in zip(a, b)) < 1e-6
    # And must spread the swing.
    assert gain(90, None)[0] < 0.6 * w
