# Making converted avatars light

VRChat avatars are heavy, and a converter that carries their weight across without comment makes
ChilloutVR heavier one upload at a time. This is the plan for not doing that.

Everything here is measured rather than assumed. The numbers below come from the 84-avatar
regression corpus as it stood after 4.0.4.

| measure | across 84 avatars |
|---|---|
| menu parameters that change nothing when toggled | **1,596 of 2,977 (54%)** |
| CVRPointers, each one a contact | 2,373 — **28 an avatar** |
| MagicaCloth solvers, each at 90 Hz | 1,549 — **18 an avatar** |
| contact triggers | 697 — 8 an avatar |
| animator layers | average **49**, worst **263** |
| animator parameters | average 104, worst **424** |

The 54% is the headline and it wants a caveat: the toggle sweep flips every menu parameter and
watches whether anything observable changes, so a parameter driving audio, particles or a
combination the sweep cannot reach reads as dead when it is not. Halve it for false negatives and
a quarter of the animator is still doing nothing.

## Three rules for the whole cycle

**Measure before touching.** Every phase after the first is an opinion until the numbers are on
screen. That is why the weight card ships first, and it pays for itself even if nothing else gets
built.

**Never touch the source project.** Optimised assets are written into the output folder and the
CONVERTED avatar is repointed at them. Re-running with a different target rewrites the copies.
Someone who dislikes the result deletes a folder; they do not restore their textures from backup.

**Report what cannot safely be automated.** A converter has no business decimating somebody else's
mesh. It has every business telling them the mesh is 400k triangles.

## Phase 0 — the weight card

One card, in the converter's report and in the ChilloutVR Toolkit, so it works on avatars that
were never converted. Each number is shown against the platform limit it spends.

**Textures.** Count, total VRAM (`Profiler.GetRuntimeMemorySizeLong`, which knows what each format
actually costs), download size, and a table: resolution, format, crunched or not, mip settings,
and which materials use it. Most heavy avatars are heavy here and nowhere else.

**Meshes.** Renderers, triangles, submeshes, bones, skinned against static, blendshape count and
how many of those are ever animated.

**Materials.** Unique count, shaders in use, and duplicates that differ only in a property value.

**Animator.** Layers, states, parameters, synced bits against the 3200 cap, layers with no states,
placeholder clips written for empty motion slots, and the sweep's list of parameters that did
nothing.

**Contacts.** Pointers and triggers, against the 512 overlapping pairs a frame ChilloutVR budgets
for the whole instance.

**Physics.** Cloth solvers, total particles, colliders.

**The rest.** Audio sources against the cap of 100. Lights, marker and real, against four vertex
slots a mesh. Particle systems and their maximums.

## Phase 1 — the free wins

Nothing behavioural, everything provable, all of it already detectable:

- parameters and layers the sweep shows do nothing, listed with a way to remove them
- layers with zero states
- the placeholder clips generated for empty motion slots, one corpus avatar carries 75
- parameters no layer reads, which the sweep already reports as refused

## Phase 2 — capped resources

Contacts are the only thing here with a hard platform limit and silent failure past it. 28
pointers an avatar is four a socket: SPS root, SPS front, TPS root, TPS norm. Two questions worth
answering with a spike rather than an argument: whether every plug family needs its own pair, and
whether pointers should be capped the way marker lights now are.

## Phase 3 — textures, by density rather than by guess

The metric is **texel density**: how many texture pixels land on how much real surface.

For each material slot, sum the world-space area of the triangles using it and the UV area they
cover, then

```
texels  = resolution^2 * uvArea
density = sqrt(texels / worldArea)      # texels per metre
```

A ring two centimetres across has perhaps 0.001 m2 of surface. An 8K map covering five per cent
of UV space puts 3.3 million texels on it: about **57,000 texels per metre**, where a face at
conversational distance is well served by 1,000 to 2,000. The tool does not need a rule saying
"rings get 256". It computes 256 as the size that meets the target density, and shows the
arithmetic that got there.

**Caveats that decide whether this is safe:**

- a texture used by several meshes takes the HIGHEST density among them, never the average
- tiling multiplies coverage and must be read from the material, not assumed
- a texture used outside the avatar is copied, never resized in place
- normal and mask maps follow their albedo's density; they are not judged independently
- the target is a setting with a sane default, not a constant buried in code

**Format, in the same pass.** VRAM and download size are reported separately, because crunch
compression shrinks the download and not the memory, and people conflate the two constantly.

## Phase 4 — physics

18 cloth solvers an avatar at 90 Hz is the largest silent CPU cost in a converted avatar. Chains
that share a preset and a bone parent can merge; colliders that cannot reach any particle can go;
the toe and finger skips already exist as settings. Wants measurement first: particles, not
component count, is the number that matters.

## Phase 5 — report only

Meshes, materials, draw calls, blendshapes, and **atlas candidates**: sets of materials sharing a
shader, never animated separately, that would merge cleanly.

**Atlasing is reported and not performed.** It rewrites UVs and merges materials, which breaks
anything animating a material property per slot, and a converted avatar is full of exactly that,
including YAPS's own `material._YAPS_Enabled` toggles and every material-swap toggle carried over
from VRChat. Prior art exists and is a large, subtle project. Naming the candidates is useful and
cannot break anybody's avatar; performing the merge is a later cycle, if ever.

## What this deliberately will not do

Mesh decimation. Texture re-authoring. Automatic atlasing. Anything that changes an asset in the
source project. Anything whose result cannot be undone by deleting the output folder.

## Order, and why

1. **The card.** It makes every later decision visible before anything changes, and it lets the
   numbers be checked against avatars already known to be heavy.
2. **Textures.** The biggest real win at the lowest risk, and the one users feel immediately.
3. **Free wins.** Provable, no behaviour change, but they need the card to be believable.
4. **Contacts, then physics.** Both want a spike before a setting.
5. **Atlas candidates.** Last, and only as a report.

This is a feature cycle. It leaves the bug-fix-only posture held since 3.7.4 deliberately, and
the standing rules still apply inside it: docs move with the change, the corpus runs before any
release, and a user hitting a bug still comes first.
