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

**Textures.** Count, total VRAM, download size, and a table: resolution, format, crunched or not,
mip settings, and which materials use it. Most heavy avatars are heavy here and nowhere else.

VRAM is computed from the graphics format and the mip chain, NOT from `Profiler.
GetRuntimeMemorySizeLong`. That was the obvious source and it is wrong for this: in the editor
every texture also keeps a copy on the CPU side, so it answers exactly twice the truth for all of
them, which is worse than a rough number because it looks exact. Crunch does not reduce this
figure either — it shrinks the download and unpacks to plain DXT on upload.

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

### The card judges, it does not only count

Built 2026-08-18 in `Editor/Core/AvatarWeight.cs`. A "what to fix" section, ranked by the memory
each line gives back, then the advice that has no figure attached. It is blunt on purpose: a
number nobody acts on is a number wasted.

The texture rule is Phase 3's density calculation, brought forward because judging without it is
guesswork. Per material it sums world-space triangle area and UV area, applies the material's
tiling, takes the HIGHEST density among the materials using a texture, and reports the power of
two that meets the target. `TargetDensity` is 2000 texels per metre and the floor on any advice is
256: below that the map is looked at from ten centimetres away in VR and the memory saved is not
worth the argument. Twelve worst are named, the tail is one line with its total.

Read/Write Enabled on a texture is called out on its own — it doubles the cost for a readback
nothing on an avatar performs. Non-readable MESHES are read anyway; that flag only bars access at
runtime, and the editor holds the source data regardless.

Everything else is a threshold: contacts over 96 against the instance's 512, cloth over 24, tris
over 250k, unanimated blendshapes over 64, and a materials-to-shaders count that reveals per
material locked shader copies.

Measured against Abbess: 333.3 MB of texture across 229 maps, 93.4 MB of it recoverable without
anything visible changing.

## The survey — what the sweep becomes

The toggle sweep drives every parameter and watches what moves. That is the *verifier*, and it is
only half a tool: it can tell you something did not appear to happen, and never why. The other
half is a model of the avatar read straight out of the controller, the menu and the components,
and it is what Phase 1, the weight card and half the roadmap have all been quietly waiting for.

**What the model holds:**

- **Every parameter** — type, default, synced or local, and both directions: who WRITES it (clips,
  drivers, triggers, parameter streams, the menu, the game itself) and who READS it (transitions,
  blend trees, drivers).
- **Every layer** — mask, weight, blend mode, default state, the bindings its clips actually
  touch, and its timing: exit times, wait states, what a sequence is waiting for.
- **Every control** — toggles, sliders, dropdowns with their named options, joysticks, colours —
  and the parameter each one drives.
- **The graph joining them**: control to parameter to layer to binding to object, followed either
  way. A preset stops being mysterious the moment you can see one int driving twelve bools.

**What it can then say that nothing today can:**

- **Hidden features.** A parameter that drives real bindings but has no control on the menu. The
  author built it, never wired it, and the new owner has no idea their avatar can do it. This is
  the most valuable output and nothing on the platform does it.
- **Dead controls.** A menu entry whose parameter nothing reads: it will always look broken.
- **Conflicts.** Two layers writing one binding, in order, with the winner named. Half the toggle
  bugs this project has ever fixed were this.
- **Subordination.** "This toggle does nothing while that preset is active" — the Abbess case,
  stated instead of guessed.
- **Unreachable states**, and transition conditions that can never be true.
- **Plain words for every control**: *Hoodie switches 3 objects and 2 blendshapes.* That sentence
  is what a new owner actually wants, and it is derivable from the bindings.

**The sweep becomes the check on the model, not the source of truth.** The model predicts what a
parameter should change; the sweep moves it and looks. Where the two disagree, that disagreement
is itself a finding worth printing, because it means something is driving the avatar that reading
it did not reveal.

This absorbs four roadmap items — the conflict map, the weight audit, state-machine reachability,
and "explain a parameter" — into one pass. They were always the same tool seen from four angles.

## Phase 1 — the free wins

Nothing behavioural, everything provable, all of it already detectable:

- layers with zero states
- the placeholder clips generated for empty motion slots, one corpus avatar carries 75
- parameters nothing reads and nothing writes, proven by reading the controller

**The sweep is a hint, never the criterion.** It flips a parameter and watches for an observable
change, and there are three ordinary designs it reads as dead when they are not. Abbess carries
259 driver behaviours and 232 timed transitions, two of them parked at `exit=True@100`:

- **A preset overrides the individual toggle.** One synced int expands through drivers into a
  dozen local bools, which is how an avatar buys twelve toggles for eight bits and guarantees a
  coherent outfit. Toggle one of those while the preset asserts its own value and nothing moves.
  Subordinated, not dead.
- **A wait state defers the effect.** A driver fires on state entry, so sequencing anything needs
  a state that waits; the effect lands after the sweep has already looked. The same idiom
  debounces a toggle and lets a blend settle before the next write.
- **The parameter is internal.** Individual toggles are often local names driven only by a preset,
  so they never appear in the menu and look invisible from outside.

So removal is decided by STATIC analysis: no clip writes it, no transition reads it, no driver
writes it, no menu entry names it, no trigger or parameter stream touches it. That is provable
from the controller. "Nothing appeared to happen" is not, and an avatar like Abbess is exactly
where it would do damage. The sweep's job is to rank what a human looks at first, and the report
should say WHY something looks dead: overridden by a driver, behind a wait, or genuinely unread.

### Built 2026-08-18, in the Toolkit

`Editor/Toolkit/FreeWins.cs`, reached from the **Free wins** card. Toolkit first because the
converter already removes both of these on the way through — Abbess converts with zero empty
layers and zero unused parameters — so the avatars that carry them are the native ones the
Toolkit exists for. It writes a tidied COPY of the controller and repoints the avatar at it.

**The placeholder clips are NOT removed, and this list was wrong to call them a free win.** Unity
crashes when it builds a playable graph containing an empty motion slot, which is the only reason
`FillEmptyMotionSlots` writes them. They are reported instead, because what they really mean is
that a motion the author intended never arrived. Abbess carries 99.

Two guards, both learned the hard way in `Dev/Probes/FreeWinsProbe.cs`:

- A parameter the GAME writes is kept even when nothing on the avatar touches it, and said so in
  the report. `CvrParameterNames.IsGameDriven` decides, so the list stays in one place.
- The avatar runs an override controller WRAPPING the base. Assigning the tidied controller to the
  Animator would throw that override away with every clip mapping in it, so the swap happens one
  level down. The probe now asserts the shared override still points where it did, because the
  first version of the probe edited that shared asset and then deleted what it pointed at.

## Phase 2 — capped resources

Contacts are the only thing here with a hard platform limit and silent failure past it. 28
pointers an avatar is four a socket: SPS root, SPS front, TPS root, TPS norm. Two questions worth
answering with a spike rather than an argument: whether every plug family needs its own pair, and
whether pointers should be capped the way marker lights now are.

### Measured 2026-08-19: the first question is answered

The weight card breaks pointers down by family. One corpus avatar carries **174 pointers across
40 families**, and the shape is not 28:

```
SPSLL_Socket_Front 14   SPSLL_Socket_Front_SelfNotOnHips 10
SPSLL_Socket_Root  14   SPSLL_Socket_Root_SelfNotOnHips  10
SPSLL_Socket_Ring  10   SPSLL_Socket_Ring_SelfNotOnHips   8
TPS_Orf_Root       14   TPS_Orf_Root_SelfNotOnHips       10
TPS_Orf_Norm       14   TPS_Orf_Norm_SelfNotOnHips       10
```

One socket, described five times over for five decoders, then doubled for a self-exclusion rule:
**58 of the 174 are `_SelfNotOnHips` duplicates** of a pointer the avatar already carries.

So no, every family does not need its own pair. What remains to decide is which of them any
ChilloutVR-side consumer actually reads, since YAPS resolves through its own channel and marker
lights. A family nothing reads is pure budget. That is the second question, and it wants the
same treatment: measure who reads, then cap.

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

**Measurement built 2026-08-19.** The card counts simulated TRANSFORMS, not components: one
corpus avatar runs 164 solvers over 1,070 of them.

**Collider pruning is off the table, and the plan was wrong to list it.** "Colliders that cannot
reach any particle can go" assumes a collider only serves the cloth on its own avatar. It does
not: MagicaCloth colliders are used against REMOTE players' cloth, so a collider in no local
cloth's list is what lets somebody else's hair collide with this body. There is no local
evidence that can prove one unused, and deleting one breaks something that only appears with
another person in the room.

Merging is reported, not performed: solvers crowded under one parent are named, since one solver
can hold many roots. A toggle that switches them apart is what makes a merge wrong, and that is
the thing to check first.

18 cloth solvers an avatar at 90 Hz is the largest silent CPU cost in a converted avatar. Chains
that share a preset and a bone parent can merge; colliders that cannot reach any particle can go;
the toe and finger skips already exist as settings. Wants measurement first: particles, not
component count, is the number that matters.

## Phase 5 — report only

**Built 2026-08-19.** Atlas candidates are named on the card: materials sharing a shader that
nothing animates apart, checked against object-reference swaps and material property curves.

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
2. **The survey.** Everything after this leans on it, and its hidden-features output is worth
   shipping on its own merits, whatever happens to the rest of this document.
3. **Textures.** The biggest real win at the lowest risk, and the one users feel immediately.
4. **Free wins.** Provable, no behaviour change, but they need the survey to be safe and the card
   to be believable.
5. **Contacts, then physics.** Both want a spike before a setting.
6. **Atlas candidates.** Last, and only as a report.

This is a feature cycle. It leaves the bug-fix-only posture held since 3.7.4 deliberately, and
the standing rules still apply inside it: docs move with the change, the corpus runs before any
release, and a user hitting a bug still comes first.
