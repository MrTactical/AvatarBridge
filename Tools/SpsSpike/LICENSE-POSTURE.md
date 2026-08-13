# Clean-room posture for the CVR-SPS deform

Read this before writing a line of `cvr_sps_*.cginc`. The original is sitting on disk in the
test project, which makes the wrong thing very easy to do by accident.

## The position

**We ship no VRCFury code.** Not a retarget of their patched shader, not their includes, not
transcribed functions. The deform is reimplemented from an understanding of *what it does*.

This is a deliberate choice, not a legal reading forced on us. VRCFury's licence permits
derivatives for personal use with the notice retained, and forbids them under its commercial
terms — where "commercial" is defined broadly enough to include **donations**. Rather than
argue about which applies to a converted avatar that a user made for themselves, we sidestep
it: nothing of theirs is redistributed, so no term of their licence is engaged at all.

It also happens to be the better engineering. Phase 0a measured the whole deform core at
**744 lines**, of which the genuinely novel maths is roughly 250. Retargeting their generated
767 KB shader would have meant anchored text surgery that breaks every time they change a
line. Ours breaks when *we* change a line.

## What is allowed

- **Reading their source to understand the algorithm.** Joe licensed VRCFury and it is
  installed in his project. Understanding how a bezier bend along a socket chain works is not
  something anyone owns.
- **Reading and writing the `_SPS_Bake` texture layout.** That texture is the *user's own mesh
  data* in a documented arrangement, produced by a tool they licensed and ran on their own
  avatar. Implementing a format is not copying an implementation — and Phase 0b proves we
  decode it from an independent reader rather than by borrowing their reader.
- **Using the DPS light range protocol** (`0.41` hole / `0.42` ring / `0.45` front). That is an
  interoperability wire format, predating SPS, and the entire point of using it is to talk to
  content that already exists. Facts about a protocol, not creative expression.

## What is not

- Pasting or lightly editing any of their HLSL or C#.
- Keeping their file open in a split view while writing ours. **If a line comes out identical
  to theirs, that is a signal, not a coincidence** — stop and re-derive it.
- Reusing their identifier names as a shortcut for thinking. Ours are `cvr_sps_*` and the
  structure is our own: our socket resolution is a single nearest socket over a two-stop chain,
  not their screen-atlas chain walk, so the shapes should not converge anyway.
- Monetising this feature in any form, including donations tied to it.

## Ongoing obligations

- **Credit SPS and VRCFury as prior art** in the README section for this feature. They invented
  the technique; we are making it work on another platform. Say so plainly.
- Keep AvatarBridge's SPS support unmonetized.
- Reaching out to Senky (VRCFury's author) is courtesy worth doing, not a blocker.

## Practical method for the implementer

1. Read the relevant original file once, for comprehension.
2. Write down, in prose, what it does and why — the maths, not the code.
3. Close it. Implement from the prose.
4. Only reopen to check a *behavioural* question ("does it clamp before or after the lerp?"),
   never to check syntax.

The prose written in step 2 belongs in the include's header comment. It is the thing that
makes the file maintainable anyway, so this costs nothing.
