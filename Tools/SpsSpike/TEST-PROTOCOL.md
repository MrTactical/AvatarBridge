# Confirmation session — the last four questions, one upload

Everything below runs in a single session with a second person. Build the pieces from the
**AvatarBridge ▸ Spike** menu, upload, and work down the list.

## What to upload

| Piece | Menu item | Who wears/spawns it |
|---|---|---|
| Probe **avatar** (no lights) | *Build probe AVATAR* | your tester wears it |
| Stress lights, 12 sockets | *Build socket stress lights* | you park it on your avatar |
| Probe **prop** (no lights) | *Build probe cube only* | either, as a control |

Share the probe avatar with your tester so they can wear it. Everything else you already have.

## Reading the cube

Four rows, top row is slot 0. Per row: a **swatch** (what that slot decoded to), a **ruler**
showing where the range landed against ticks at 0.41 / 0.42 / 0.45 / 0.49, and a **distance
bar** underneath, 0–5 m ticked per metre.

Swatch colours: **red** hole · **green** ring · **blue** front · **magenta** tip ·
**amber** an ordinary world light took the slot · **dark grey** slot empty.
Border pulsing cyan = alive. Border solid red = the vertex and fragment stages disagree.

**New: the top-right corner square** is written only by the ForwardAdd pass —
- **black** — that pass never ran here (no pixel light on the cube)
- **dim blue only** — it ran but sees *no* protocol light ← this is the ghosting risk
- **blue tinted red/green/blue/magenta** — it ran and has the same lights ← safe

## Q1 — Avatar → avatar (the real topology)

Tester wears the probe avatar. You wear the lights. Stand a couple of metres apart.

- **Rows light up** → avatar-to-avatar works; the layer question is closed and the precision
  channel is proven in the exact shape the deform needs.
- **Stays dark while the prop version works** → it is a layer problem. Recoverable (the
  converter would widen the culling mask), but I need to know.

## Q2 — Close range, the distance that actually matters

Walk right up to each other, under half a metre, and **look straight at the probe** — not at
the person wearing the lights.

- **Rows stay lit** → frustum culling is cosmetic. At real working distance the socket is
  always in frame when the plug is, and the precision channel is solid.
- **Rows drop out** → culling bites even at contact range, and the deform must lean on the
  discrete channel's last-known position much harder. Worth knowing before I write the easing.

Note the **distance bars** here: they should read very short. That is the reading that proves
these are the near lights and not something across the room.

## Q3 — Slot contention with a realistic socket count

You wear the **stress rig** (12 sockets, 24 lights). Tester watches the probe while you move
around them.

Watch for: do the four slots hold a **matched root+front pair** (a red or green *and* a blue
at nearly the same distance) for whichever socket is nearest? Or is it a scatter of roots
with no fronts?

- **Matched pairs, nearest wins** → the decoder can pair by proximity and trust the nearest.
- **Scattered** → pairing must be defensive, and a lone root has to be discarded rather than
  guessed at. Either answer is usable; it changes about ten lines of the decoder.

## Q4 — The corner square (cross-pass ghosting)

Do this in a world with real pixel lights — a bright one, or stand under a lamp.

Watch the **top-right corner**. Any of the three states is a valid result; I need to know
which. If it goes dim blue with no colour, the deform has to be disabled in that pass, which
is a one-line change made now rather than a mystery later.

## Optional, if easy

- **In VR**, check both eyes show the same readout (single-pass instanced correctness).
- **In a mirror**, check whether the readout differs from the direct view.
- Have the tester set their **lights content filter** to block and confirm the cube goes
  dark rather than misbehaving.

## What to send back

A shot of each of Q1–Q4, and a note on anything that flickered. That closes the spike and
Phase 1 starts.
