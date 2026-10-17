# Directory availability model: E live entries drawn in a random order, each
# writing its home slot(s); later draws overwrite earlier ones. An entry is
# lost in a view when the reader cannot find it: lookup 1 reads one home;
# 0 and 2 read A, and B only when A carries another tag (a foreign entry
# with the same 7-bit tag at A also loses it, fail safe).
import random, sys
def run(lookup, slots, E=20, trials=200000, seed=1):
    rnd = random.Random(seed)
    lost = 0
    for _ in range(trials):
        homes, tags = [], []
        for e in range(E):
            a = rnd.randrange(slots)
            if lookup == 1: b = None
            elif lookup == 0: b = (a + rnd.randrange(slots - 1) + 1) % slots   # distinct
            else: b = rnd.randrange(slots)                                     # independent
            homes.append((a, b)); tags.append(rnd.randrange(128))
        order = list(range(E)); rnd.shuffle(order)
        owner = {}
        for e in order:
            a, b = homes[e]
            owner[a] = e
            if b is not None: owner[b] = e
        for e in range(E):
            a, b = homes[e]
            w = owner[a]
            if w == e: continue
            if b is None or tags[w] == tags[e]: lost += 1; continue
            if owner[b] != e: lost += 1
    return lost / (trials * E)
for slots in (1864, 472):
    for lookup in (1, 0, 2):
        print(f"slots {slots} lookup {lookup}: {run(lookup, slots) * 100:.3f}% of entries lost")
# Self-check: two entries on one slot of one, the later wins, so half are lost.
assert abs(run(1, 1, E=2, trials=20000) - 0.5) < 1e-9
