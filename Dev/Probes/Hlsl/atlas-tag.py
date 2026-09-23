# Does every atlas cell tag survive the target it is grabbed from?
# Mirrors YapsAtlasTag (Editor/Yaps/yaps_atlas.cginc), the writer's
# 0.5 + tag / 2 and the reader's 0.001 window; change them together.
# An 8-bit target stores round(a * 255) / 255, a half-float one keeps
# 11 bits of mantissa. Every tag has to come back inside the window on both.
# Run: python Dev/Probes/Hlsl/atlas-tag.py
import struct

WINDOW = 0.001


def unorm8(a):
    return round(a * 255) / 255


def half(a):
    return struct.unpack("<e", struct.pack("<e", a))[0]


def dead(tags, store):
    return sum(abs((store(0.5 + 0.5 * t) - 0.5) * 2 - t) > WINDOW for t in tags)


old = [k / 255 for k in range(256)]
new = [(1 + 2 * k) / 255 for k in range(128)]
print(f"old tag, 8-bit: {dead(old, unorm8)} of {len(old)} dead, half: {dead(old, half)}")
print(f"new tag, 8-bit: {dead(new, unorm8)} of {len(new)} dead, half: {dead(new, half)}")
assert dead(old, unorm8) > 0, "the old tag should fail on 8-bit, or this probe is not modelling it"
assert dead(new, unorm8) == 0 and dead(new, half) == 0
print("ok")
