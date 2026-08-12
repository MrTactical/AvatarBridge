# Round 2 — proving the new backbone

The first round demoted lights from carrying position. The redesign leans instead on CVR's
per-player position globals for per-frame tracking, and **that has never been tested at all** —
we jumped straight to lights. So this round is mostly about the globals, plus two quick
follow-ups that could hand lights a real job back.

## Priority 1 — the globals probe (this is the go/no-go now)

**AvatarBridge ▸ Spike ▸ Build GLOBALS probe (player position markers)** → spawn as a prop.

It draws small cubes straight onto every player's **hip (red)**, **chest (green)** and
**head (blue)**, taken from the globals, ignoring its own transform entirely. The bright set
is player index 0, which should be you.

Look for, in order of importance:

1. **Do markers appear on people at all?** If nothing shows, the globals do not resolve for
   uploaded content and the whole redesign needs rethinking. Everything else is moot.
2. **Do they sit on the right body parts?** A hip marker floating at the knee, or offset by
   half a metre, changes how much correction the design needs.
3. **Do they track smoothly when people walk, crouch, jump?** Any lag or stutter here is lag
   the deform inherits, since this is the per-frame source now.
4. **Is the bright set on you?** Confirms local player is index 0.
5. **THE MIRROR.** Look at a mirror. The markers **must** appear in exactly the same places as
   in the direct view. These are global uniforms, so unlike lights they should be identical
   everywhere. If the mirror agrees, the camera-dependence problem is genuinely solved. If it
   somehow does not, tell me immediately — that would be the most surprising result of the
   whole spike.
6. With Fluffy present: do their markers track *them* correctly, and does yours stay on you?

## Priority 2 — do roots win with the ranges inverted?

**Build INVERTED-encoding stress lights (roots win)** → wear it, read the light-probe cube.

Same 12 sockets, but roots at **0.49 / 0.48** and fronts at **0.41**, so roots now outrange
their fronts. The probe now shows the two root bands apart: **magenta** and **cyan**, with
fronts in red.

- **Slots fill with magenta/cyan** → the fix works, roots hold the slots, and lights become
  usable again as a contact-range refinement.
- **Still mostly red (fronts)** → range is not what Unity ranks by here, and lights stay
  purely legacy-interop.

Compare directly against the old stress rig, which filled with blue fronts.

## Priority 3 — lights at contact range, in a mirror

Hold the light-probe cube right against the avatar wearing lights, under half a metre, then
look at a **mirror**.

- **Mirror and direct view agree** → lights are stable at contact range exactly as predicted,
  and the refinement tier is safe to build.
- **They disagree even touching** → lights never carry position, only legacy interop.

## Optional if convenient

- The **top-right corner square** on the light probe, in a brightly lit world (it stayed black
  before, meaning that pass never ran).
- VR, both eyes, on the globals probe.

## What to send back

A shot of the globals probe with both of you in frame, one of the same thing in a mirror, and
the inverted stress readout. Priority 1 is the one that decides whether Phase 1 starts.
