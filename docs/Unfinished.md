# Unfinished

The single list. Every open piece of work lives here and nowhere else. Check this document
before starting on a new idea, in case something similar is already on it, and whenever the
question is "what's next" or "what do we have to do". An item leaves this file by shipping, or
by a decision recorded in `archive/`. The standing rule still outranks everything on it: a bug
somebody hits while wearing an avatar comes first.

Transport facts and their build order live in `YAPS5.md`; solver maths in `SolverCalibration.md`;
what SPS code may be looked at in `YAPS-CLEAN-ROOM.md`. Finished records are in `archive/`.

## Next up

*4.3.0 shipped 2026-08-23: the rebuild, the lighthouse and the light budget, after corpus runs 385,
386 and 387 passed and a tester confirmed a rebuilt mouth socket with a DPS prop in game. The
hold on 4.2.0 ended there; that number was spent on a tester build and never released.*

1. **Watch the 4.3.1 reports.** The classes that reached users before are in the corpus now, but
   the first week of a release is its real corpus. A report wearing a `test-rebuild` build is
   pre-release; ask for the file name.

   *4.3.1 shipped 2026-08-24, a hotfix: a tester with **DynamicBone and no MagicaCloth2** could
   not compile the tool at all. `DynamicBoneWriter` called two helpers that lived in
   `MagicaClothWriter`, and the two files sit behind different defines, so the callee vanished
   and the caller broke. The helpers were plain `Transform.Find` code and moved to
   `PhysBoneConverter`, which both writers already depend on and which compiles unconditionally.
   Swept the codebase after: no other cross-define reference exists.*

   **The gap this exposed is CLOSED 2026-08-24: `Dev/Build/check-defines.sh`.** There are four
   install combinations (Magica ±, DynamicBone ±) and every other gate — corpus, test project,
   the editor sitting open — runs the one with both installed, so a define mistake shipped
   invisibly and killed the tool outright for whoever hit it.

   No Unity launch and no domain reload: Unity leaves the exact compile arguments for
   `Assembly-CSharp-Editor` in a response file under `Library/Bee/artifacts/*.dag/`, so the
   script reuses them — all 323 references, the source list, the language version — and only
   swaps the two `-define:` lines. Four real compiles of the real assembly, seconds each.
   Errors outside AvatarBridge are counted and ignored, because a project full of other assets'
   editor scripts is not this gate's business.

   **Self-tested against the bug it exists for**: reintroducing the `MagicaClothWriter` call in
   `DynamicBoneWriter` makes it fail on MAGICA=0 DYNBONE=1 with `CS0103` at lines 43 and 44 —
   the same file, lines and columns the tester's log carried. Run it before any release that
   touches physics or the defines.
2. **The sweep's 131 carried toggle failures** — triaged as avatar-side, no tool signature; the
   prediction that Fury's wired socket toggles flip to "responded" is worth checking in the next
   digests.
3. **The body-mesh fallback: ANSWERED 2026-08-24, and the answer is no** (section below). The
   split is built and shipped, but a skinned body mesh receives no vertex lights, so the shader
   can never compute the depth this was meant to make free. The 32 bits stay. What is left on
   this axis is the **dedicated-mesh ownership bug** the work exposed: a hand socket's own mesh
   sits at the socket, so a hand in somebody's lap resolves ownership to THEIR hip and ignores
   their plug. `_YAPS_SocketOrigin` is the foundation for the fix; it needs an owner anchor
   baked for dedicated meshes and its own test pass.
4. **The GPU bridge** (`YAPS5.md`, candidate 4) — blit or RT camera into a texture parser gives
   per-client audio for zero sync and zero contacts. Local Play mode first, then an upload.

## Loose ends, small but real

### Four found wearing the avatar, 2026-08-25/26

Joe baked a whole avatar as one plug and four separate faults fell out of it. Recorded here
because they were found in a session, not in a report, and the queue is the only thing that
outlives scrollback.

1. **The bake made a new material every time — FIXED 2026-08-26.** Both material sites asked
   `AssetDatabase.GenerateUniqueAssetPath`, so a plug removed and baked again left `Fur_YAPS_`,
   `Fur_YAPS_ 1`, `Fur_YAPS_ 2` behind it, one full material per click, in a user's project.
   `YapsBaker.Generated` now derives a path that does not move and re-derives the values from the
   source onto whatever is already there. The file keeps its GUID, so anything already pointing at
   it stays pointed at the right thing, and what was there is never trusted — only its identity —
   so an asset left by an older version cannot carry stale settings forward. One helper serves
   both sites; the primary and the mirrored slots had drifted into two different conventions.

2. **Additional meshes under the armature do not bend — FIXED 2026-08-26, UNTESTED IN UNITY.**
   A plug whose root bone is the Armature patched ONE renderer's materials; every other skinned
   mesh weighted to the same bones kept its own shader and stayed rigid, which is the seam the
   multi-material work closes, one level up. `MirrorToRenderers` now bakes each of them, and the
   bake is per mesh because the texture is indexed by mesh-global vertex id — what they share is
   the primary's FRAME and LENGTH, via a new `shareFrameWith` on `YapsBaker.Bake`, so they bend
   as one object rather than each measuring its own idea of where the shaft is. Scoped to the
   `CVRAvatar`, not the scene root, or two avatars under one container lend each other meshes.
   Skipped entirely when the author names a material slot: that says "this mesh, this slot".

   **`BakedSlot` gained a `renderer`**, because a plug spanning meshes has a slot 0 on each of
   them. Matching on the number alone would have handed one mesh's material to another on Remove
   — the "IT BROKE, my fur!!!" failure of 2026-08-25 exactly, one level up.

   *Compile-checked only.* `check-defines.sh` links the project's compiled Runtime assembly, so
   a Runtime change cannot be validated there — the documented blind spot. Needs a real Unity
   compile and a bake on an avatar with a second weighted mesh.

3. **Poiyomi's auto-lock — ANSWERED 2026-08-26, and the pink fix had already closed it.**
   Read out of `ShaderOptimizer.SetLockedForAllMaterials` rather than guessed. The sweep takes
   every material whose shader uses the optimizer and is not already locked, and its test for
   "already locked" is `shader.name.StartsWith("Hidden/Locked/")` — the exact prefix the pink fix
   gave us. So auto-lock-on-upload skips our materials, and has been skipping them since that fix.

   Worth knowing what it would have done: locking resolves properties to constants, and OUR
   properties are the ones animation drives (`_YAPS_Enabled`, `_YAPS_BakeScale`, the knobs). A
   locked YAPS material would have frozen at whatever it happened to hold — a plug that never
   toggles and never resizes, in game only.

   **One gap closed with it.** `IsShaderUsingThryOptimizer` keys on the `ThryShaderOptimizerLockButton`
   attribute, while `PatchedName` keyed only on `shader_is_using_thry_editor`. A shader carrying
   the lock button without the editor marker would have been named plainly and swept in. Both
   markers now.

   **Still open, and left alone on purpose:** an explicit "Unlock all materials" grabs ours too,
   and tries to restore a `TAG_ORIGINAL_SHADER` we never wrote. The result is a broken material,
   recovered by re-baking. Guarding it means writing Thry's own lock records, which is
   impersonating another tool's bookkeeping to survive a button the user deliberately pressed.

4. **Parallel paths — AUDITED 2026-08-26, two closed and one recorded.**

   *Window vs inspector, CLOSED.* Three doors bake a single plug and only one went through
   `BakeAndRefreshMenu`. The window's row button and its make-this-a-plug flow called the bare
   `Bake`, so they left the contact channel holding a previous build's frames and the menu
   animator unrefreshed — the same divergence as yesterday's, in two more places. Both now go
   through the same door as the inspector. `BuildAll` keeps the bare `Bake` deliberately: it does
   the menu and the channel once, for the whole avatar, which is the point of a batch.

   *Remove vs Sweep, CLOSED.* Both clear the channel and refresh the menu animator. The rest of
   the difference is real: Remove undoes one plug, Sweep collects orphans. They are not two doors
   to one job.

   *Native builder vs converter, OPEN and the interesting one.* **The converter has its own bake
   path** (`YapsConverter` lines 116 and 211) and patches ONE material slot on ONE renderer. It
   never calls `MirrorToSlots`, and now never calls `MirrorToRenderers` either — so both the
   multi-material fix of 2026-08-25 and the multi-renderer fix of 2026-08-26 apply to the native
   toolkit ONLY, and a CONVERTED avatar still tears along the seam.

   Not fixed here, for two honest reasons: the mirroring takes a `YapsPlug` and the converter
   holds a VRCFury plug, so closing it is a refactor to pass values rather than the component;
   and it changes conversion output, so it needs a corpus run to land. Low impact in practice — a
   VRCFury plug is normally one dedicated mesh with one material, and multi-material plugs are the
   whole-avatar case, which is a native-toolkit experiment. Worth doing, not worth rushing.

4. **Parallel paths that disagree.** Two doors to the same job diverged on the same day: the
   window's Build wired the contact channel and the inspector's Bake did not, and Remove cleared
   channels where Sweep did the leftovers. Both were fixed one at a time. That is a class, not
   two bugs — every pair of entry points into one operation wants auditing against each other:
   window Build vs inspector Bake, Remove vs Sweep, native builder vs converter.

### A stale material is invisible, and it wasted three readings

2026-08-26. Three times in one afternoon a test result was wrong because the material was still
running the previous shader, and nothing said so. The tell each time was a debug view answering
a question the shader it was running had never been asked.

**DONE 2026-08-26, in the two places a person actually looks.** The material's own YAPS panel
gets a warning under the banner, because that is where somebody reads a value and believes it —
which is exactly how all three readings were lost. And the Setup window's row goes amber with
"its shader is older than the toolkit — Build refreshes it", because there the fix is one click
away and the row already had a mechanism for saying so.

Still open below: the project-wide sweep. A warning only reaches a material somebody happens to
select or an avatar somebody happens to scan, and a prop prefab is neither.

### Nothing revisits a prop, so a shader fix never reaches it

Found 2026-08-26, after the two-socket prop fix. A patched shader's name hashes the emitted
source, so `IsStale` knows when a material has fallen behind and both bake paths re-patch it —
but only when something bakes. An avatar gets baked because its scene is open and Build is
pressed. **A prop is a prefab sitting in the project, and nothing ever opens it**, so every
shader-level fix reaches everyone except the props, and the only cure today is to make a new one.

The fix is not a prop button. It is one project-wide sweep: walk the materials carrying
`_YAPS_Bake`, ask `IsStale`, `Refresh` the ones that answer yes. That catches props, avatars in
scenes nobody has opened, and anything imported from someone else's package — the same blind
spot in three shapes. `YapsShaderPatcher` already has every piece; what is missing is the caller
and a line in the window saying it exists.

Joe's question is the right one: it should just work. A user has no way to know a shader moved,
and "make a new one" is not an answer for a prop somebody has positioned and tuned.

- **External audit 2026-08-25, the five deferred findings.** An audit by another agent
  (`AUDIT-external-2026-08-25.md`, kept in the repo). Nine of twenty were verified in source and
  fixed the same day; two were wrong (F1's consequence, F17's premise); these five are real,
  deferred on purpose, and each changes behaviour, so each wants evidence before it moves:
  - **F3, strafe folding: FIXED 2026-08-25, fixture first.** `DirectionOf` folded east into west
    and the replacement wrote one clip to both sides, so an author's distinct left-strafe clip was
    lost. Checked against the CCK's own `AvatarAnimator.controller` before touching anything: it
    has separate children at `x: -0.5` and `x: +0.5` and points BOTH at clip `004085e0…`, so CVR
    ships mirrored strafe — but the slots are real and can hold different clips.

    **The corpus could not judge it, so the corpus was given the shape it lacked.**
    `Fixture_AsymmetricStrafe` (built by `FixtureBuilder.RunStrafeOnly`, so the two existing
    fixtures are not rebuilt from a source scene that has been hand-edited since) carries a
    velocity tree with a different clip on each side of every sideways direction. Run against the
    unfixed tool it lost three clips — `(1,0)` played `Fix_StrafeL`, and both diagonals the same —
    which is the point of building it before the fix rather than after.

    `Slot` still describes the direction PAIR, because CVR's slot set is pair-shaped; a `Side`
    now rides beside it, picks are keyed `(Slot, Side)`, and replacement is child-driven so each
    CCK position asks for its own side. A source with one clip for both sides still fills both,
    by falling back to the opposite side — which is what every symmetric avatar relies on.
    After: `(-1,0)=>Fix_StrafeL (1,0)=>Fix_StrafeR`, both diagonals likewise.
  - **F11, slider hole-drop degrades to hole-filling.** `kept.Count >= 2` sends a slider tree with
    one surviving child to the generic filler, which inserts the placeholder clip the report text
    beside it promises sliders never get.
  - **F12, gesture-hand promotion by substring.** `layerName.Contains("left")` on a lowercased
    name: "Copyright pose" contains "right". The tempting fix is wrong — `\bleft\b` cannot match
    `GestureLeft`, there is no boundary inside one run of word characters, and lowercasing has
    already flattened the camel hump. The right fix is to promote only layers that actually WRITE
    a gesture parameter.
  - **F18, owner-rule shortcut skips the humanoid guard.** `FindPlugRenderer` returns the parent's
    SkinnedMeshRenderer before `chainLevel` is ever set, so `HumanoidBoneName(null)` returns null
    and the "your chain is the body" refusal never runs. Silent bypass, not a crash. A guard here
    risks refusing legitimate plugs, so it wants the corpus.
  - **F19/F20, the shader surface.** The patcher's regexes are unanchored and comment-blind, and
    the socket decoder admits a coloured light whose alpha is zero (`&& colour.a > 0`), which also
    defeats the self-exclusion test that consumes its verdict. Held back because a shader change
    cannot be judged from this machine — see the body-mesh section for what that cost on 2026-08-24.

- **A rebuild refreshes the shader now, not just the bake — FIXED 2026-08-24.** Every shader-level
  fix used to reach nobody who had already converted. `BakeSocket` and `Bake` took a refresh
  branch whenever the material carried `_YAPS_Bake` and never called `Patch` again, and the patch
  was named `Hash(sourcePath + Revision + unit.Count)` — a hand-written `Revision` constant that
  did not move when the emitted code did. Found by adding a property and watching it never arrive:
  the material's shader had nine copies of a property added that morning and none of one added
  that evening, because the morning's landed on a FIRST patch and the evening's needed a second.
  The constant was forgotten twice in one day, which is the argument against constants like it.
  Now the name hashes the emitted source itself (`EmittedVersion`), so it moves exactly when the
  code moves, and both bake paths ask `IsStale` and re-patch when it has fallen behind. A material
  keeps its values across a shader swap, and a property the old code never had arrives at its
  declared default, which the bake then sets.

  **The original is recovered from the PATCH, not from the component.** The first version asked
  `socket.bakedFrom`, which only the tool's own bake ever fills in — a CONVERTED avatar adopts its
  components and leaves it null, so the check would have done nothing for the majority of avatars
  and looked like it worked. `_YAPS_SourceShader` already carried the source shader's name in a
  hidden property's description, so `OriginalShaderOf` reads it back and `Refresh` re-patches
  through a stand-in material. Verified in the editor: a socket material's shader moved from
  `56f6502ef13e` to `253145a5a99b` on a rebuild and gained the property it had been missing.

- **Corpus classes: CLOSED 2026-08-22** — Fixture_DeformSocket and Fixture_HeadTransplant are in
  the corpus and its baseline; the transplant fixture came out on the real Head with zero errors.
- **Pointer capping: CLOSED 2026-08-24, declined with the measurement.** Two questions, both
  answered without a run. *Which families does anything read?* A census over the 93 corpus files
  carrying contact receivers, senders split from receivers: `TPS_Orf_Root`/`SPSLL_Socket_Root`
  heard in 4 files, `SPSLL_Socket_Hole` 3, `Ring` 2, twins alongside — and the front pair,
  `TPS_Orf_Norm`/`SPSLL_Socket_Front`, heard by NOTHING. That looked like four dead pointers a
  socket until `YapsPropBuilder.FrontTypes` turned out to be exactly that pair: it is the prop
  channel's FX/FY/FZ front axis. No corpus avatar hears it because no corpus avatar carries a
  prop, and the corpus enumerates scenes while props are spawnables. Our own system is the
  consumer; capping the family would silently cost props their axis.
  *Should the COUNT be capped like marker lights?* No, the limits are not alike. Lights are four
  slots a MESH and a fifth evicts the lowest range, which is why the lighthouse had to exist.
  Pointers are 512 overlapping PAIRS instance-wide, and a pointer costs nothing until it is
  inside a receiver — 174 idle pointers standing in a room spend none of the budget. A count cap
  would break sockets in the common case to save a resource nobody is spending. If pair
  exhaustion ever appears in the wild, the lever is the overlap, not the socket's description of
  itself.
- **DPS range offset, ON ICE 2026-08-23** — the +0.003 offset makes YAPS sockets and plugs
  invisible to every mod decoding at 0.001, sound mods included: NAK's PlapPlapForAll needs
  `RoundToInt(Repeat(range*500+500,50)+200)` to hit 205/210/225/245, and our 0.4130/0.4230/0.4530/
  0.4930 all land on x.5 and read Invalid. No range separates a sound mod from a toy mod, so it is
  one choice for both. **Parked until CVRGoesBrrr is fixed or rejected** — if that mod stops
  reading strangers' lights without consent, or the modding group turns it down, the reason for
  the offset is gone and exact VRCFury ranges (0.4106/0.4206/0.4506/0.4906) restore every mod at
  once. The alternative, if it drags: a wearer-facing setting defaulting to today's behaviour.
- **Consolidation remainder**: items after 5 in `archive/Consolidation.md`'s order, minus 6,
  which was skipped on purpose.

---

---

## Read it, strip it, rebuild it in YAPS
*Status: SHIPPED in 4.3.0 (2026-08-23). Corpus 385/386/387 same-shape and clean; a tester confirmed a rebuilt mouth socket with a DPS prop in game. Kept here as the record of what the conversion does; the table below is history.*

**The single highest-value thing on this list. Not a feature: it removes the seam every
penetration bug of the last three sittings came out of.**

Conversion used to keep VRCFury's baked rig and adapt it in place. So an avatar ended up with
a socket that was *Fury's socket, retuned* — while the YAPS tool built a different thing entirely
from the same description. Two shapes, one name, and every difference between them is somewhere
a bug can live. All of these did:

| what was wrong | where it lived |
|---|---|
| no `_SelfNotOnHips` twins | tool-built sockets only |
| socket parked on `vrcfAlwaysVisibleHead`, switched off | converted only |
| `Original Object` switched off between them | converted only |
| receivers switched off by `OverlappingContactsFixService` | converted only |
| socket exclusivity merged at weight 0, animating sockets off | converted only |
| every socket's lights defaulting to lit, 22 fighting for 4 slots | converted only |

Five of six are things a native socket cannot have. Each was found by somebody wearing an avatar
in game, none by any check here, and each fix was a patch on Fury's scaffolding rather than a
removal of it.

**What the conversion now does — built 2026-08-22, in `YapsSocketRebuilder.cs`:**

1. **Read** every DPS, TPS and SPS socket and plug: transform, kind, the mesh it deforms, the
   shapes it stages, the radius measured from that mesh, the toggle that owns it.
2. **Strip** the legacy rig completely — no baked objects, no services, no exclusivity layers,
   nothing of Fury's left to switch off.
3. **Rebuild** through the same code the YAPS tool uses, at the transforms that were read.
4. **Repoint** the author's reaction layers onto the rebuilt socket's depth parameter — when the
   socket read exactly one; several are kept as authored, and the report says so.

Then a converted socket IS a native socket. Odessa becomes a valid test for every avatar, the
scanner and the preview see one shape, and this whole table stops being possible.

Two thirds of the machinery already exists: plugs are re-baked today rather than adapted, and the
toggles are rebuilt by Bake and Verify, which was the objection that looked hardest until Joe
pointed out it is already solved.

**Step 4 was the one the spike found, and it was not optional.** Before the rebuild the two paths
did not name the depth parameter alike:

| | how depth is named |
|---|---|
| native, `YapsSocketReactions` | `YAPS/<label>/Depth` |
| converted | Fury's own layers, e.g. `[FX] [VF80] Pussy - Depth Animations - 2 - Action` |

The converter never called `YapsSocketReactions`; it kept Fury's layers and made them local. So
a socket rebuilt natively would have published one name while the author's clips read another,
and every reaction would have gone silent. Measured across the corpus: **72 depth-animation layer mentions, 6 empty**,
so roughly sixty-six carry real animation an author built. Not droppable.

The repoint itself is a known job — `AnimatorMerger` already renames parameter references across
a whole controller. The trap it must avoid is the one that has bitten here before: **renaming a
layer's parameter references never touches its clips.** The clips animate blendshapes and stay
exactly as they are; only the layer's read of the depth value moves. Backwards, that silences a
channel or resizes a mesh.

**Then a corpus, and an avatar with an always-visible head added to it.** There were zero of
those in 84 — which is exactly why this class kept reaching users — until Fixture_HeadTransplant
and Fixture_DeformSocket joined the corpus on 2026-08-22.

*Supersedes the earlier entry here, which proposed waking everything Fury left switched off. That
treats the symptom. There is no residue to wake if there is no residue.*

---


## The preview tells the truth about the game
*Status: partial. The honest window shipped in 4.3.0; the game-accurate resolver is unstarted.*

The setup window's preview bends a plug toward a socket by reading transforms. The GAME needs the
socket's pointers, marker lights and receivers switched on. So a socket can preview perfectly and
do nothing in game, and it did: pointers visible in CVR's own debug view, no bend, and the tool
saying the socket was fine throughout.

4.3.0 makes the window HONEST — each socket row says which of the three is dark and what it
costs. That closes the lie. It does not close the gap.

The gap worth closing is a preview that resolves a socket the way the game does: pointer and
trigger overlap by geometry, the enabled and active state of both, the depth it would publish,
the parameters it would drive. Then "it previews" and "it works" are the same sentence, and this
entire class of report — works in editor, dead in game — stops existing.

Wants measuring first: how much of CVR's contact resolution has to be reproduced before the
answer is trustworthy. A preview that is right most of the time is worse than one that is
honest about being a preview.

**One member of this class is FIXED 2026-08-25: the squish.** Joe wore a plug in game and was
compressed hard; the editor showed it correct, and scaling the plug made the mismatch worse.
`_YAPS_BakeScale` is written as `1` at Bake, meaning "the size it was baked at", but
`MirrorBoneScale` copied the bone's `m_LocalScale` curve across as an ABSOLUTE number. The two
agree only for a bone that happened to sit at exactly 1 when it was baked; on any other rig the
bone's scale was applied a second time, on top of the skinning that had already applied it. The
editor hid it because no animator runs there, so the material's static `1` stood. Mirrored as a
ratio to the bake pose now, tangents divided with the values. The same change gives each bone its
own along-axis, which a child further down the chain never shared with its root.

**The scaled-bone check is in: `Dev/Probes/BakeScaleCheck.cs`.** Every plug in the corpus sits at
scale 1, the one value where the old code was right, so 87 avatars passed green for weeks while
this was live. Not a corpus scene: the digest records no curve values, so no baseline would have
moved even with a scaled bone in it. Three cases asserted directly instead, in seconds, with no
avatar and no baseline: a bone baked at 0.4 that reads 1 at the bake pose and 3 at the top of its
slider, the halfway point that only comes out right if the tangents were divided too, a bone at 1
that must still pass through untouched, and a turned child whose length axis is not its root's.

Still open in the same class, because a ratio only fixes the reference: a plug whose size layer
holds a different value in game than the scene pose does in the editor is still two different
plugs.

---

## The body-mesh fallback is our line, not ChilloutVR's

*Opened 2026-08-23. Not a transport — a limit we imposed on ourselves that costs sync bits.*

A socket whose shapes live on the BODY mesh cannot use the shader deform, so its reactions go
through the animator: a depth trigger, a parameter, a layer. That parameter is wearer-only unless
synced, which is the 32-bits-a-socket tickbox. A socket with its own mesh pays none of that — its
deform runs in the shader, on every client, free.

The whole difference is one line, `yaps_socket.cginc`:

```hlsl
float3 socketWorld = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
float depth = YapsPlugDepth(socketWorld);
```

**The socket's position is taken as the mesh's object origin.** For a body mesh that pivot is the
avatar root, so the shader looks for a plug down at the hips and finds nothing. Hence
`MeshIsTheSocket`, which is just `distance(renderer.position, socket.position) < 0.03f`.

**It cannot distort geometry.** The deform is baked shape deltas blended by weight, with no frame
recovery and no walk, and `depth <= 0` returns before any of it. Get `socketWorld` wrong and the
socket simply does not open. The frame-packing worry that makes this file dangerous belongs to the
PLUG deform, not this one. There is a second net as well: `_YAPS_SocketDepth` overrides the
computed depth whenever it is >= 0 (`yaps_socket.cginc:147`), so the animator channel still wins
where it is driven.

**But it is not only where we look for a plug — corrected 2026-08-23.** `socketWorld` reaches
further down than the depth maths:

```
YapsSocketDeform  ->  YapsPlugDepth  ->  YapsFindPlug  ->  YapsSocketOwnPlug   (yaps_resolve.cginc:215)
```

`YapsSocketOwnPlug` measures `socketWorld` against EVERY player's hip to decide whose plug to
ignore. So the value also answers "whose socket is this". On a body mesh it sits at the avatar
root, right beside the wearer's own hip, and the self filter passes by accident. Move it to the
real socket and that stops being free: a socket out on a hand, near somebody else, can resolve to
THEIR hip. Any candidate has to be checked against the hand case, not just the jaw case.

**The hard part is not the line, it is what to put in it.** Candidates:

1. **A baked offset**, `_YAPS_SocketOrigin` in the renderer's local space, set at bake time from
   the socket's transform. Correct whenever the socket does not move relative to the renderer —
   and wrong the moment a bone does, since a body mesh's transform is the avatar root while the
   socket rides a bone. Fine for a chest, wrong for a jaw.
2. **The socket's own marker light.** It sits exactly at the socket and tracks its bone for free,
   and the shader already reads lights. But the lighthouse lights one socket at a time, so this
   only works for the lit one.
3. **The vertex's own world position.** Tracks skinning perfectly and costs nothing, but depth
   then varies across the shape by a few centimetres out of the reach — a gradient where the
   stages want one uniform weight. May be invisible on a bulge and visible on a staged sequence.
   Worse since the ownership finding above: one position per VERTEX means the nearest hip is
   resolved per vertex too, so vertices can disagree about whose plug it is. Candidates 1 and 2
   keep one position for the whole socket and stay coherent.

None is free of a catch, which is why this is a spike and not a patch. Worth it: it would move
every body-mesh socket onto the free path and retire the sync tickbox for most avatars.

**The hand case is measured now, and it closes the question — 2026-08-24.** Grepping the
baseline digests: 79 avatars, 19 with sockets, and all 19 carry hand or foot sockets
("Handjob L/R" on nearly every one, "Footjob"/"Steppies" on about twelve). Far-from-root
sockets are not an edge case, they are the standard loadout. So no single position can be put
in the line: depth wants the socket, ownership wants the wearer, and every candidate above
failed by making one value answer both.

**The answer is a split, not a choice.** `YapsSocketOwnPlug` keeps reading the renderer's
object origin, which for a body mesh is the avatar root beside the wearer's hip — the accident
that worked, now on purpose. Depth reads the origin plus a baked `_YAPS_SocketOrigin` offset.
Both default to zero, so a dedicated socket mesh, where the two coincide, is bit-for-bit
unchanged. Candidate 1's jaw catch still stands but now costs only depth accuracy, never
ownership; a socket that rides a far bone keeps the animator channel, which still overrides.

One consequence worth its own line: the split also shows the SHIPPED behaviour is wrong for
dedicated hand-socket meshes — their origin IS the socket, so ownership already resolves
against the hand, and a hand in somebody's lap can claim their hip and ignore their plug.

**Fixed 2026-08-24, by deleting the question rather than answering it.** The obvious repair was
a baked owner anchor pointing at the avatar root, and it does not work: a hand socket's mesh
rides the hand bone, so any offset baked in the rest pose is wrong the moment the arm moves.
There is no static point on a hand that tracks the wearer's hips.

So ask what the test is FOR. Its own comment says: an avatar carrying both a plug and a socket
has the plug's tracker a hand's width from its crotch socket, permanently within a plug length,
and the socket reads as always full. That is a crotch socket beside the wearer's own plug — and
it is the one case where the nearest-hip heuristic is reliable, because the socket really is at
the wearer's hip. Everywhere else the test is not merely unreliable, it is unwanted: a handjob
socket SHOULD react to the wearer's own plug.

`_YAPS_SocketNoSelfExclude`, baked. Zero keeps today's behaviour, which is what every material
baked before this reads, so nothing shipped changes until it is rebuilt. The baker sets it when
no plug of the avatar's rests within its own length of the socket — measured off the tracker
light, because that is the point the shader itself measures from and its intensity carries the
plug's length. The converter asks the same question of `ctx.YapsPlugs`, which needs no lights to
exist yet.

**What is proven**: it compiles in all four define combinations, and the reasoning above.
**What is not**: the payoff is a hand socket reacting to a STRANGER's plug, which needs two
clients in one instance. Nothing in the editor or the corpus can see it — a second client run by
Joe is the only test that would.

### Built and measured 2026-08-24 — and the sync saving is NOT there

The split is written: `_YAPS_SocketOrigin` in `yaps_socket.cginc`, baked by `BakeSocket`,
defaulting to zero so a dedicated socket mesh is bit-for-bit unchanged. Subset run of the 19
socketed avatars plus a no-socket control came back 20/20 identical, so nothing regressed.
Tested by hand on a real body-mesh socket (Sootie, "Pussy hole" on `Base`, two staged shapes):

- **The bake is right.** `_YAPS_SocketOrigin` came out (0, -0.0197, 0.6863) — the socket's true
  pelvis position in the mesh's own space, X on the midline. `_YAPS_SocketPower` 1,
  `_YAPS_SocketDepth` -1.
- **The deform is right.** Driving `_YAPS_SocketDepth` by hand opens both shapes in order.
- **The socket never sees the plug.** With the light channel as the only depth source it stays
  shut. Probed the generated shader directly, with every filter removed and no distance gate,
  forcing depth to 1 on the mere existence of ANY vertex light in ANY of the four slots: still
  nothing. `unity_4LightAtten0` is zero on that renderer.

Vertex lights are not broken in general — the PLUG bends in the same scene, and that needs a
plug mesh to read a socket's markers. The difference is the renderer: a small prop mesh gets its
four slots, a 34k-vertex skinned body gets none. Unity ranks a renderer's candidate vertex
lights by influence, and a marker light is BLACK, so it brings no luminance to rank with.

**So the prize this section was chasing does not exist.** The reason to move a body-mesh socket
onto the shader was to retire the 32-bit synced depth parameter; the shader can only compute
depth from a light it never receives. The contact route is not a fallback for body meshes, it is
the only route, and the tickbox stays. The opposite of what was assumed when this was opened.

What the work still buys, and why it is kept: a socket is no longer measured from the wrong
point when its mesh is not its own, and `_YAPS_SocketOrigin` is the correct foundation for the
dedicated-mesh ownership fix parked above. Do not reopen this as a sync-saving idea without
first re-measuring the light delivery — that is the thing that killed it.

**Also on the same axis:** the GPU bridge (`YAPS5.md`, candidate 4) can write
`Transform.localPosition` and `localScale` per client for nothing. So a reaction rigged to BONES
rather than blendshapes is already free today. Not retrofittable onto an author's existing shapes,
but it is what YAPS should prefer for anything it builds itself. Blendshape weights are behind
`SetBlendShapeWeight`, a method, and no reflection-based trick reaches them.

---

## YAPS 5: the plug follows a path, not a point
*Status: planned, unstarted.*

*The other half of YAPS 5 — how a plug FINDS a socket — lives in `YAPS5.md`, which gathers every
transport, every measured limit, and the order they get built in. This section is what the plug
does once found.*

Today `yaps_resolve.cginc` holds exactly one socket — one position, one forward, one
engagement, chosen as the single best candidate. Give it an **ordered list of sockets with
arc-length ranges** and three of the four things on the wishlist fall out of one change:

| want | how it falls out |
|---|---|
| a ring mid-shaft *and* a hole at the tip | two entries in the list |
| portal: in at one socket, out of another | two entries with a gap between their ranges |
| duplicate: the shaft showing in two places | the same range mapped twice |

A vertex shader cannot create geometry, so a "duplicate" is the existing shaft drawn at two
frames rather than a second shaft. It costs nothing and it cannot be two different lengths.

**Multiple plugs per socket is a separate, much smaller job.** The bending already works:
every plug resolves sockets in its own shader, so two plugs into one socket needs no change
at all. What breaks is the socket's *shapes* — a ChilloutVR trigger writes from whichever
sender touched it last, so two plugs fight over the depth instead of the deepest winning.
Socket-side, no extra sync, and it can ship on its own.

**What the platform gives and charges for.** Unity hands a mesh four vertex-light slots and a
socket takes two — but the third slot is spoken for by the tracker light of whatever enters the
socket, and that tracker lives on a prop or on the other person, so it can never be counted at
build time. **The light path therefore carries exactly ONE socket, not two.** Two lit sockets
plus a tracker is five lights for four slots, and Unity fills by range, so the casualty is
always the lowest range in the protocol: the hole root at 0.4130, behind a ring root at 0.4230
and two fronts at 0.4530, with the tracker on top at 0.4930. An avatar over the budget loses its
holes and keeps its rings, which is exactly how this reached us. A portal pair costs two sockets
and so cannot ride the light path at all; it needs contacts or a script.

The contact channel is where bits get expensive: eight values per socket, 96–288 bits per plug
by tier, doubled by a second socket. Give socket two its own lower tier (engagement and
position, orientation dropped first).

Socket depth stays one parameter per socket, local by default and synced at the wearer's
choice. That is deliberate: sharing a slot
between sockets assumes one is engaged at a time, which is exactly the assumption
multi-socket and portal exist to break.

**Order to build it in:** deepest-plug-wins first (small, self-contained), then the socket
list with socket two on contacts (the light path carries one socket, see above), then portal and
duplicate as ranges on top. The converter repointing an author's reactions onto YAPS's own
depth, once last on this list, shipped in 4.3.0 with the rebuild.

---

## The converter: prove it before the instance does
*Status: planned, unstarted.*

Everything here comes from the same observation: the expensive bugs are the ones nobody can
see in the editor, and every one this month was found by a person wearing the avatar.

**A bench, so "optimised" stops being a claim.** Spawn the avatar N times in a fixed scene,
let it settle, sample frame timings over a few hundred frames, and report CPU and GPU
milliseconds. The absolute number means nothing; the delta between two runs on one machine
means everything, and nothing in the project can currently produce one.

It would settle questions the tool answers by assertion today. Does Free wins cost less to
run or only read tidier? Do 164 cloth solvers over 1,070 transforms actually hurt? The texture
pass is the interesting case, because the honest expectation is **~0 ms**: shrinking a map
buys memory and load, not frame time, and a bench that reports zero there is doing its job.

**It has to answer in VR terms, or it answers the wrong question.** Almost everyone wearing
these avatars is in a headset, and the two halves of the cost scale differently:

- **CPU transfers nearly one to one.** Cloth, contacts, animator evaluation and skinning run
  once a frame however many eyes are drawn. What changes is the budget: 90 Hz allows 11.1 ms
  against a desktop 60 Hz frame's 16.7, so the same 2 ms of cloth is 12% of one and 18% of the
  other. Report the share, not the milliseconds.
- **GPU transfers not at all.** Stereo roughly doubles it and per-eye resolution dwarfs an
  editor viewport, so a bench rendering into the game view reports a number nobody will
  experience. Render single-pass instanced, which is what ChilloutVR uses, at real per-eye
  resolution.

And it wants calibrating once against a real session in a headset, because play mode has no
compositor and no reprojection. Measure one avatar both ways, keep the ratio, apply it.

Belongs with the twin below: both exist so a bug or a cost shows up in the editor rather than
in somebody's instance.

**A remote twin.** The strongest idea on this page. Spawn a second copy of the converted
avatar beside the first, driven *only* by parameters that actually sync, and let the tester
walk between them. Every wearer-only bug becomes visible in the editor: the socket shapes
that moved for nobody, the toggle that never left the wearer's machine, the smoothing rig
that froze at its defaults. The Animator Tester already snaps `#` locals to their defaults
for its Remote view; this is that idea given a body.

**A conversion diff.** The regression harness writes a digest per avatar and compares runs.
Users get the same tool: convert, change a setting, convert again, and read what moved.
"Reconverting on a new release" stops being an act of faith.

**A toggle sweep, shipped.** `Dev/Corpus/ToggleSweep.cs` flips every menu toggle on and off
and reports anything that did not come back. It found real bugs. It should be a button in
the Toolkit rather than a thing only the harness runs.

**A budget planner.** The sync tally exists in the report; make it interactive. Tick the
features you want, watch the bits, and let it name the cheapest thing to drop. Most avatars
that go over do it by accident, in a menu they never counted.

**An upload preflight.** One list, run before uploading: our diagnostics, the CCK's own
validators, the texture flags, parameters past the cap, missing metas. Everything that
currently fails at the last moment, in an order that says which to fix first.

**Advanced Tagging, inferred.** Content tags are currently listed as "not converted, set
them yourself". An avatar carrying YAPS knows what it is. Offer the tags rather than
assuming them, and nobody uploads an NSFW avatar untagged by accident.

**Incremental reconversion.** Whole-avatar every time today. Keyed on source hashes, the
passes whose inputs did not change could be skipped. Worth it only once the corpus can prove
a skipped pass and a run pass produce the same output.

---

## GoGoLoco's poses, through ChilloutVR's own front door
*Status: planned, unstarted.*

The most visible thing a converted avatar loses. GoGo is stripped because its layers assert body
poses while ChilloutVR's locomotion is asserting them too, and two writers on one body is the
bicycle pose everybody has seen. Merging it harder does not help; the fight is the problem.

**ChilloutVR already has the door.** The CCK ships a full set of override slots, and the client
plays them from its own state machine, so nothing competes:

| slots | what they would hold | where it shows up |
|---|---|---|
| `Emote1`–`Emote8` | the popular poses, one each | the quick-menu emote wheel |
| `ToggleDefault`, `ToggleState1`–`7` | stances meant to be held | the toggle list |
| `LocSitting` | the avatar's sit pose | automatically, in chairs |
| `LocCrouch* / LocProne* / LocFlying / LocSwimming*` | stance art | CVR's own states |
| `LocIdle / LocWalking* / LocRunning*` | already done, by LocomotionGrafter | — |

Locomotion stays with the grafter, and the reason is measured rather than preferred: the CCK
reuses eleven clips, every one of them a Right variant (`LocWalkingStrafeRight`,
`LocRunningStrafeRight*`, `LocCrouchRight`, `LocProneRight`), at both the left and right
positions with the tree mirroring them. An override is one clip per ASSET, so it would put a
mirrored right strafe on the left and throw away the author's real left clip; the grafter
matches by POSITION and keeps it. Overrides would also mean the avatar has to run an override
controller, which the CCK's "Create Controller" regenerates, so a user pressing that button
would wipe their locomotion.

**And the client names the menu from the clip.** `AvatarAnimatorManager.FindLegacyEmotesAndToggles`
switches on the ORIGINAL slot name and takes the OVERRIDE clip's name as the label:

```csharp
case "Emote1": _legacyOverrideEmoteNames[0] = originalOverride.Value.name;
case "ToggleDefault": _legacyToggleNames[0] = originalOverride.Value.name;
```

So a clip named "Sit Cross-Legged" in `Emote1` is what the wearer reads in the wheel. No AAS
entry, no parameter, no sync bits, and it works on every avatar this tool converts today,
because grafting CVR's locomotion is what puts those slots on the controller in the first place.

**Three constraints that shape the feature:**

1. **Sixteen, not fifty.** Eight emotes and eight toggles. GoGo ships far more, plus puppet-style
   fine positioning with no equivalent here. This carries the popular poses, not the system.
2. **Names are load-bearing twice.** A clip whose name contains "Emote", or matches the stock
   eight (Wave, Bow, Die, Backflip, Point, Sad, Salute, Dance), makes the client mute both hand
   layers while it plays. The harvested names must be sanitised even though they are also the
   labels the wearer sees.
3. **An emote ends when you move.** Right for a dance, wrong for a sit somebody wants to hold:
   those belong in the toggle slots, which is also how to tell the two apart when harvesting.

**Order to build it in:** identify GoGo's pose clips and classify each as held or momentary;
fill the toggle slots from the held ones and the emote slots from the rest, best-known first;
sanitise the names; write the override controller. Report what landed where and what did not
fit, because "which of my poses survived" is the first question anyone will ask.

## The animator merger: make the invisible visible
*Status: planned, unstarted.*

Ten thousand lines, the most fragile thing in the package, and the place where a bug is
hardest to see because nothing throws — the avatar simply behaves slightly wrongly.

**A conflict map.** Which layers write the same binding, in what order, at what weight, and
who wins. Half the toggle failures this project has fixed are two layers writing one
property and nobody knowing. It would also have caught the strength bug on sight: a layer at
weight 0.2 writing a blendshape *no other layer writes* creeps to full, because the value it
blends from is its own last frame. That is a rule a map can check.

**A weight audit.** Flag every partial-weight layer whose properties nothing else writes.
That single check is worth writing on its own.

**State-machine reachability.** States nothing can enter, transitions whose conditions can
never be true, parameters read but never written. The pieces exist across the diagnostics;
gathering them into one pass would name a class of bug that currently only shows up as "the
toggle does nothing".

**Explain a parameter.** Pick one and get its whole life: where it came from, what renamed
it, whether it syncs and why, which layers read it, which clips write it. Debugging today
means reading three report sections and inferring the join.

**Deterministic ordering, stated.** Layer order decides who wins. The merger has rules for
it; they should be visible in the report, so a surprising result can be traced to a rule
rather than to luck.

---

## Marker lights are a broadcast, and the room is listening
*Status: reference — measured facts, kept for design work; the distilled limits are in `YAPS5.md`.*

Added 2026-08-17, after a user reported their controllers vibrating whenever they came within
two metres of a converted avatar. Everything here was learned from source, and it changes what a
socket is allowed to be.

**A marker light is the only thing an avatar does that reaches into someone else's.** Its range
is a message — every decoder reads `range % 0.1` and matches 0.01 hole, 0.02 ring, 0.05 front,
0.09 plug tip — and anything on the platform may listen. Raliv's shader matches within 0.005; a
toy mod reading the same protocol in C# matches within 0.001. VRChat authors +0.0006, inside
both, which is why a stock converted avatar drove a stranger's toy across a room. YAPS authors
+0.003, which DPS reads and that mod does not.

**Toy integration cannot be made safe from the socket side, and that is not our bug.** The mod
computes reach as `1 - distance / giver.Length`, and estimates `Length` from whichever renderer
sits first under the avatar root rather than reading the length DPS states in the tip light's
intensity. A socket is only ever the target of somebody else's number. So:

- **plug side, one day: yes.** A plug can declare its own length honestly, by giving the tracker
  light a sibling whose mesh states the measured length. The mod climbs to the first renderer
  under the light, so an inactive one on the marker object is enough, and then it engages within
  a plug length rather than a room. That is a switch that risks only the wearer.
- **socket side: never.** Any "let toy mods read my sockets" control hands strangers the right
  to decide how far away they can reach you.

Both are moot if [ddakebono/CVRGoesBrrr#2](https://github.com/ddakebono/CVRGoesBrrr/pull/2)
merges, since it bounds the estimate with the stated length. Old builds stay broken either way,
so the quiet offset remains the default long after any merge.

**A tempting idea that does not work: shrinking the ranges.** Since only `range % 0.1` is read,
0.0106 decodes exactly like 0.4106 and reaches a fortieth as far. It would ease vertex-slot
pressure — but Unity ranks the four per-object light slots BY RANGE, so a tiny light is the
first evicted by every stock DPS avatar in the room. It would work alone and fail in company,
which is the opposite of what a compatibility feature needs. Worth keeping only as a "YAPS talks
to YAPS and nothing else" mode, where our own decoder sets the rules.

---

## Two walls the transports hit, and whether a shader goes round them
*Status: the light-slot fix landed, and the authority-gate wall is DISPROVEN in game 2026-08-22 — cross-avatar prop writes work, see `YAPS5.md`. The 512-pair cap stands.*

Read out of the client 2026-08-22 while costing a contact-based replacement for the marker-light
channel. Both of these are platform behaviour, not our code, and both bound what any redesign can
achieve.

**A prop can only be driven by your own avatar.** Every contact write into a `CVRSpawnable` value
goes through `TriggerToContact.HasProbableAuthorityToApplySync`. It allows a sender that is
another prop (if either is synced by you), a sender that is *your own* avatar, or the world. There
is **no branch for another player's avatar**, so a remote sender falls through and returns false
and the value is silently never written. This is very likely a client bug rather than policy: the
prop branch falls back to `IsSyncedByMe()`, and that fallback is simply missing for avatar
senders, so even the prop's own syncer is refused. It predicts the split we have been chasing —
avatar-to-avatar works (the gate only runs for spawnable triggers), old DPS props work (lights,
never this path), YAPS props work on your own sockets and never on someone else's. **Test before
building anything around it, and report it upstream if it holds.**

**512 overlapping contact pairs, instance-wide.** `CollectPairsJob` caps `pairs` at 512 and re-adds
*previous* pairs first, so an established interaction is sticky and cannot be evicted, but in a
saturated instance a new one may never register — "works once I am in it, will not start in a
crowd". The broadphase itself is brute force over every sender for every receiver, but Burst and
parallel with a squared-distance test, so it is sub-millisecond and not the constraint. Rejections
on tags, `contentType` and owner happen *before* a pair is written, so **tags are the lever on the
512, not volume count.**

### So: is there shader magic?

Two routes, and the cheap one is much more interesting than the famous one.

**The light colour is free, and nobody is using it.** A vertex light hands the shader
`unity_LightColor` alongside its position and range. Our markers are black with non-zero intensity
(zero intensity drops a light from the per-object list entirely), so three channels per light are
sitting unused. If a root light's colour carried the socket's axis, a YAPS-native socket would
need **one** light instead of two — and with the tracker holding a slot, that is three sockets in
the budget instead of one. Legacy plugs still need the root+front pair, so this is a YAPS-native
mode alongside the compatibility pair, not a replacement. Cheap to spike, and the open questions
are small: whether CVR's asset filter clamps light colour or intensity on avatars, and how much
precision survives the intensity multiply.

**The screen-space atlas is real, and we would write it better than SPS — but do not build it
yet.** Sockets render a small quad encoding their world position into a reserved screen region;
the plug samples it back through a named GrabPass. It is the only channel that dodges *both* walls
above: no light slots, no contact pairs, no parameters, so no authority gate and no sync bits, and
no cap on socket count. And the reason SPS's does not survive conversion is one we could simply
not have — theirs is written for VRChat's double-wide, ours would be instanced-native from the
first line (`UNITY_DECLARE_SCREENSPACE_TEXTURE`, the same family the shader patcher already
applies). Against it: a named GrabPass is a real per-frame cost; the atlas quads must escape
frustum culling; and the unsolved one is **cell collisions between avatars**, because two avatars
cannot negotiate which screen cell they own without scripting, and a collision is a wrong socket
position rather than a missing one. It also reintroduces the screen dependency that made the
contact route attractive in the first place.

**Order of work:** the light-slot fix first (done), the gate test (done, disproven), then spike light
colour. The atlas stays designed and unbuilt until something forces it.

---

## The WASM route, and what it would make of all this
*Status: blocked — access declined 2026-08-19.*

Read from Joe's client on 2026-08-17. **His install is the `public-scripting` beta**, and the
bridge's initialiser sits behind `#if WASM_SCRIPTING_ENABLED`, which a ChilloutVR dev confirmed is
defined only on that branch. So none of this is in stable, and nothing here can be built yet.
Written down because it changes what YAPS 5 should aim at.

**What a script actually gets.** Not the thin, movement-shaped API the `CVR_*` binding names
suggest. Underneath is a generated binder, `WasmBinder.Links.UnityEngine`, mapping **107 Unity
types** including `Material`, `MaterialPropertyBlock`, `Renderer`, `SkinnedMeshRenderer`,
`Shader` and `Transform`. `MaterialLink` alone exports **276 functions**, `SetFloat`, `SetVector`
and `SetTexture` among them. Plus `CVR_Avatar_GetAllAvatars`, avatar root transforms, and a
`Networking` binding set that is not the AAS budget.

**And the permission model is the right shape.** Reading a transform has no access check;
*writing* one, and every material setter, demands `CVRScriptScopeContext.Self`. A script may look
at everything in the instance and touch only its own avatar.

That is the entire YAPS resolver, in ordinary code:

| fought today | under a script |
|---|---|
| contacts, 512 overlapping pairs a frame for the whole instance | read transforms directly |
| marker lights, four vertex slots a mesh | write the material directly |
| the socket's kind encoded in a light's RANGE, two free digits | any data, any shape |
| one socket per plug | a list, arc ranges, portals, whatever the code says |
| 3200 AAS bits | its own networking |

**The backwards-compatibility prize is bigger than the feature.** A script does not need anyone
else to cooperate: it can walk another avatar's hierarchy and read their sockets *whatever
protocol they speak* — Raliv's lights, TPS and SPS pointers, YAPS markers — because it reads the
components rather than waiting for Unity to hand it four light slots or for a contact pair to
survive the budget. Everything on the platform becomes findable, including content that will
never be converted and whose authors are long gone. That is what "force everything over to YAPS"
can actually mean: not converting other people's avatars, but understanding them.

**It stays a tier, not a replacement.** Our sockets must keep emitting lights and pointers, since
that is the only way somebody else's plug finds us, and a script cannot run on stable or for a
wearer who refuses it. So: script when it is there, contacts next, lights last, which is the
tiering the resolver already has.

### What YAPS becomes with a script

Three features, in the order they are worth building.

**Bones, not vertices.** A script may write `Transform.position` on its own avatar, and that is
the whole of what YAPS does today expressed as ordinary code: pose a bone chain toward the socket
rather than displace vertices in a shader. Everything the vertex path fights disappears with it.
No shader patching, so a plug works on a shader that refuses to be patched and on whatever
Poiyomi does next. No bake texture, which is megabytes of VRAM per plug and the reason a re-bake
is needed after every update. Correct shadows and depth, because the mesh really is where it
looks. Cloth and colliders can interact with it. And a bone chain is what most plugs already have.

**A shader fallback, kept forever.** A plug modelled as a single rigid mesh with no bone chain
cannot be posed, and neither can a plug on a client with no script. So the vertex deform stays as
the floor, and the rule is the same shape as the socket resolver's: **bones if the mesh has them
and the script runs, the shader otherwise.** The toolkit already knows which it is: the bake
records `_YAPS_FrameFromVertex`, and the survey knows a mesh's bone chain.

**A detector for everything already out there.** A script can walk any avatar in the instance and
read its sockets whatever protocol they speak, because it reads components rather than waiting
for a light slot or a contact pair. Old Raliv DPS lights, TPS and SPS pointers, YAPS markers, all
of it, with no cost to the four vertex slots and none to the 512-pair budget. Content whose
authors left the platform years ago becomes usable, and that is worth more than any new feature.

**Access: asked, and declined, 2026-08-19.** ChilloutVR gates the scripting CCK. The request was
made and the answer was no, for reasons that matter more than the refusal:

- **They are building WORLD scripting.** Avatar and prop scripting is not what they are working
  on, so feedback on it is not useful to them yet. Everything below is designed against an
  implementation that does not exist and may not for a long time.
- **They asked that AI tools not be used to analyse their scripting implementation.** Joining the
  testing group means agreeing to that. It is their call and worth respecting past the letter of
  it: no AI-assisted digging into the scripting side, and nothing here waits on doing so.

**So this waits for public release rather than for an invitation.** When scripting ships to
everyone, writing against a documented API is ordinary work and none of the above applies. Until
then this section is a design sketch, not a plan, and nothing else in the project depends on it.

**The authoring half exists.** An experimental CCK with WASM components is in closed testing.
So this is moving rather than hypothetical, and Joe already runs the scripting branch, which
means the week it reaches everybody a prototype is possible: one plug, one socket, a resolver
that reads transforms and writes `_YAPS_SocketPos`, and the answer to whether any of the rest is
worth designing.

**Unknowns to settle before building:** what approving a script costs a wearer socially, whether
open read access survives to release, what a per-frame resolver costs in a full instance, and
whether a scripted avatar can still be found by everyone else's plugs (it must: the lights and
pointers stay either way).

## Two constraints worth remembering before designing anything

**ChilloutVR runs an avatar's triggers on the wearer's machine alone.** Anything
contact-driven that the room must see has to be a synced parameter. This is not VRChat's
model and it invalidates the instinct to make contact parameters local because they are
"free everywhere".

**An animator-driven parameter can never sync.** `IsReadOnly` is true for anything
controlled by a curve, and `IsSynced` requires `!IsReadOnly`. Any design that computes a
value in the animator and expects the room to see it is already wrong; sync the inputs, or
compute it on every client from something that does sync.
