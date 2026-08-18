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
