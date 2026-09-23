# Unfinished

The single list. Every open piece of work lives here and nowhere else. Check this document
before starting on a new idea, in case something similar is already on it, and whenever the
question is "what's next" or "what is left to do". An item leaves this file by shipping, or
by a decision recorded in `archive/`. The standing rule still outranks everything on it: a bug
somebody hits while wearing an avatar comes first.

Transport facts and their build order live in `YAPS5.md`; solver maths in `SolverCalibration.md`;
what SPS code may be looked at in `YAPS-CLEAN-ROOM.md`. Finished records are in `archive/`.

## Next up

**Decided 2026-09-23 (Joe):** ship the next release first (one default corpus run authorised,
release on his word), then the two items below. **It is 4.6.4** (Joe, same day): 4.6.3 is skipped,
its number spent on test builds, and the socket readout goes in with it.

**1. A runtime tester: the bend as data. PROPOSED, step one is a probe.** Every YAPS bug this week
(8-bit atlas holes, own socket in the editor, rigid accessories, the torn tip) was found by eye,
because the bend happens in the vertex shader and nothing on the CPU ever sees it; the corpus
digests the conversion only. Design:
- **Readback.** A test-only variant of the patched plug shader writes every bent vertex, and what
  it resolved, into a buffer the tester reads. **PROVEN 2026-09-23** by
  `Dev/Probes/Readback/YapsReadbackProbe`: D3D11 at feature level 11.1, a `RWStructuredBuffer` at
  `u1` written per `SV_VertexID` from the vertex stage, a skinned strip bent by a turned bone, 64 of
  64 vertices back and 0.000 mm from Unity's own `BakeMesh`, in batch with graphics on. So the
  bend can be read as numbers; the float-target fallback is not needed here.
- **Step two BUILT 2026-09-23:** `Dev/Tester/YapsBendTester` + `YapsBendCapture.shader`. A real
  converted avatar (Non Corpus Zone, Alexa, 0.649 m plug) with its plug slots on a capture shader
  making the patcher's two calls on the current includes; tier read through the shipped "Resolved
  by" view, calibrated per vertex from two known views (gap = full and Resolved by = a quarter with
  nothing resolved; an extent along the marker axis read a full length as 0.33 and was dropped).
  Rule 1, no socket no bend: **0.00 mm**, pass. Rule 2, an atlas-only hole at 0.8 and 1.1 lengths
  on 25 directions: **27 of 50 found, the same on HDR and 8-bit**. The first miss read atlas
  target 0.70 and taps 0.10: the camera was live and no cell was read, so REACH, not the tag.
  With the level taken by `ceil` (tried in the test project only): **50 of 50** on both.
  **FIXED ON DEV 2026-09-23, editor only**, see "The atlas scan leaves dead zones" below: a
  two-level read and numbered buckets. The tester now covers every socket kind a shader can see
  (YAPS hole, ring, one-way ring by atlas, atlas plus lights, DPS hole and ring by light alone;
  contact-only TPS has nothing a shader reads) on three plug lengths, 0.155, 0.330 and 0.649 m,
  and a neighbour pair 6 and 25 cm apart, numbered and unnumbered. DPS misses only where the root
  light's range (the protocol's 0.41) cannot meet the plug's renderer bounds, which Unity culls
  in game too; the tester counts those apart. **Tester trap:** creating the numbered writer
  materials as assets mid-run made every later capture read a full length; they are made before
  the first capture now.
- **Scenarios, seeded, run on their own.** Sockets approaching from a shell of directions, swept
  past the tip, withdrawn; locomotion with physics on; the Animator Tester's toggle flips; HDR,
  8-bit and small views; a second avatar for ownership. A failure saves a repro scene.
- **Invariants, one per bug met.** No socket in range, no bend. A socket anywhere round the tip
  resolves. No edge stretches past a bound, across renderers too. Hidden parts stay hidden bent.
  A small socket move is a small mesh change. Every camera gives the same answer.
- **AI on top later** (choosing scenarios, reviewing flagged frames), never the backbone.
- **Cannot prove** anything the CVR client owns: its HDR, its globals, sync. Still checked in game.

**2. In-game readouts, 4.6.4. BOTH HALVES ON DEV 2026-09-23, not in game yet.** The plug readout built
on EVERY plug, hidden, with a SYNCED bool toggle (Joe's call: a helper sees the user's plug as
their own client resolves it), the shader skipping all work while off; one material slot per plug.
A socket readout of its own: publishing on this camera, its own atlas cell read back as its own (a
direct 8-bit-tag detector), owner id known, number and kind, whether it holds the avatar's marker
light, a plug seen.
- **Plug half done.** Every plug under an avatar carries its readout (a plug on a prop has no menu
  and gets none); one row, **YAPS readout** (`YAPS/Readout`, bool, 1 bit, off), switches them all
  through a layer of its own, from both builders, and Remove and Clean up take it out with the last
  readout. `_YAPS_ReadoutOn` off returns before the bake read and the resolve, every corner on one
  point. The *Draw a debug readout on each plug* option and the plug's *Debug overlay* tickbox are
  gone. `Dev/Probes/ReadoutProbe.cs` bakes through the toolkit door on three avatars: row, layer,
  hidden 0 px against no slot at all, shown 3132 to 4146 px, and the menu parameter through a real
  Animator shows it and hides it again. It found the bug below on its first run.
- **Socket half done.** `YAPS/Socket Readout`, a quad of its own beside each socket's atlas writer
  (`YapsDebugOverlayBuilder.AddSocketReadout`, from `YapsSocketBuilder.Build`, so both builders),
  only under an avatar, on the same row. Six cells: atlas on this camera; the socket's own entry at
  each of the four levels, found by cell tag and position as a plug finds it (black nothing, red
  others but not this one, green its own); owner id; kind and number; its root light (digit 7 or
  legacy 1 to 4) within 2 cm; a plug tracker's depth. The menu layer now targets every renderer
  drawing a readout, so Remove and Clean up take the row out with the last one. The probe rebuilds
  every socket on three avatars (34 sockets) and reads the cells off a half-float render: atlas,
  smallest level and kind right on all 34; with the writer and root light off, nothing of its own
  on all 34; the owner green once the stand-in hands out an id; and the light cell never claims a
  light that is off, green on exactly the one socket per avatar the lighthouse lights. Two things
  it showed at once: the largest level of one socket read red (another socket holds that bucket,
  so a plug whose length picks level 3 misses it there; not chased), and d3d11 refuses
  `levels[lv]` inside a `[loop]`, so the shader masks.
- **Corpus 407 (2026-09-23), not clean, three fixes on dev.** Deployed before the socket readout,
  so it covers the plug half only. Expected and seen: 19 proxy-only Action layers dropped (two of
  them carried VRCFury tracking-control drivers, the same hand-offs), 15 readout layers and rows,
  the settings stamp without `yapsDebugOverlay`, no new stuck toggle, one avatar back under the
  sync cap. Not expected: every converted plug warned that its readout "could not carry" two
  settings, which were the patcher's markers (`_YAPS_OriginalEditor`, `_YAPS_SourceShader`), and
  every avatar with two or more readouts gained a "materials share a shader" set in the weigh
  pass, the readouts, which each hold their own plug's values and cannot merge. `Copy` skips the
  markers; `YapsMarks.IsReadoutMaterial` keeps readouts out of the weigh pass's atlas advice and,
  for a socket's own renderer, out of the survey's lift-off candidates, as the atlas writers are.
  The probe checks the first on a real patched plug and passes on three avatars; the survey half
  is unmeasured (407 had no socket readouts). A clean corpus run is still owed before release.

*4.6.0 shipped 2026-09-12: tag `v4.6.0`, merge `b51296e`, both packages published, 98 commits
since 4.5.1. The atlas carries tags, one-way rings, the per-plug own-sockets checklist and an
owner id per socket; the conversion resizes oversized textures; four converter bugs went with it.
Corpus run 399 accepted as the YAPS baseline (83 digests), all fifteen dev tests and the smoke
test at 34 of 34, and `check-defines.sh` clean on all four Magica/DynamicBone combinations. That
last one was run AFTER the release rather than before, having been missed: it is not in CLAUDE.md's
release list and the release touched the physics path. It passed, so nothing came of it this time.*

*Proven in game before release: cross-avatar resolve on protocol 7, own sockets in all four
states, a hand-set tick beating the plug's tags, and the rect at 932 by 596 in every view (desktop
window, VR both eyes under a simulated headset, world mirror, personal mirror, self portrait,
third person). **Shipped unverified in game, deliberately:** socket clips by depth, the TPS bulge
falloff, one-way rings, the tag dropdown, multi-mesh plugs, the Mesh-to-None cleanup.*

1. **Watch the release week.** The classes that reached users before are in the corpus now, but
   the first week of a release is its real corpus. Ask a reporter for three things: a photo of the
   debug readout, what the top-left cell showed, and **both people's versions**. That last one is
   new in 4.6.0 and it is the question that cost a day on 2026-09-12: the atlas refuses a mismatched
   protocol version, which reads from the inside exactly like a socket that chose not to answer.

2. **The two field reports**, issues #7 and #6, below. Both have been waiting on the reporter
   since 2026-09-07: #7 needs to know which PC avatar (the work is on `physbone-grouping`), #6
   needs the SDK version, which bone, and what the wrong result looks like.

3. **A shipped plug's colour changes in VR on Poiyomi 9, and does not on Poiyomi 8.** A user
   report, so it outranks everything else here. The shape is right and only the colour moves.
   Nothing in the shipped YAPS reads the eye, the camera or the screen; it rewrites position,
   normal and tangent in the vertex stage and stops. What Poi 9 adds that Poi 8 does not have in
   the same shader is two more `ForwardBase` passes: an `EarlyZ` depth prepass (`ZWrite On`,
   `ColorMask 0`, drawn first) and an outline pass, where Poi 8 ships both as separate shader
   files you opt into. Both carry a vertex program the patcher patches. A prepass whose deform
   disagrees with the base pass's by any amount depth-tests the plug against its own undeformed
   silhouette, which reads as colour going wrong while the shape stays right. **Unproven**: ask the
   user to turn Early Z and the outline off and reupload, or reproduce it locally against the
   Poiyomi 9.3 in `Fixing The Flexing` with MockHMD. Deferred 2026-09-07 for want of a live case;
   MockHMD turning out to be enough for the atlas walk weakens that excuse.

4. **D2: does a named `GrabPass` reach a `CVRBlitter`?** Built and waiting on a run,
   `Dev/Probes/D2PrefabBuilder.cs`, staged into `Non Corpus Zone` and on the menu. It decides
   whether a value can be computed by geometry on the avatar (cheap, one blit) or needs a camera
   rendering into a render texture (a lot more), and it picks the shape of everything after it, so
   nothing else on the GPU-bridge candidate is worth building until the answer is in.

   The transport itself is PROVEN in game, 2026-09-07: a value computed on the GPU reaches C# on a
   stock client with no contact anywhere, on every client including remote copies, for zero sync
   bits. Nothing is transmitted; each viewer computes its own. **What the proof constrains:** the
   shader may read only synced avatar state, meaning bone transforms and blendshapes driven by
   synced parameters. Local time, `_ScreenParams`, frame count and the viewer's camera each give a
   different answer per viewer, so a blit cannot be the source for anything other people must
   agree on. The source has to be a render of avatar geometry, which is the camera route. Two
   corrections came out of building it, both in `YAPS5.md`: every render texture in the chain has
   to be linear, and animator parameters are reachable through `CVRAnimatorDriver` after all,
   given a pump clip to make it flush.

5. **WITHDRAWN 2026-09-13: "the atlas payload is visible in game".** Added 2026-09-12 from the VR
   captures, which show small pink and white squares near a plug. **They are the debug readout's
   own two markers**, `YapsDebugOverlay.shader:327`: white where the tip would be with the bones at
   bake pose, magenta where the real tip is. The readout was on in every one of those captures,
   which is what the coloured strips in them were. The payload itself was dealt with long before:
   the writers draw at `Background-945`, render queue 55, with the clear at 54 and the grab at 56,
   all ahead of the scene, so the scene paints over them. Joe caught it by knowing the queue
   number. Kept rather than deleted so the same pair of squares is not rediscovered as a bug.

6. **SPS2: the risk was in the wrong place, and the silent part is GUARDED 2026-09-13.** This item
   said an SPS2 plug's new material properties would read as "no legacy system" and get deformed
   twice. That was written without an SPS2 avatar and it misses how the converter works: **it never
   lets VRCFury patch a plug's shader at all.** `YapsBakePrep` sets `enableSps = false` on every
   `VRCFuryHapticPlug` before Fury bakes, so the plain shader comes through and only YAPS deforms
   it. The patcher's `_SPS_Bake` refusal is a backstop behind that, not the defence.

   So the real exposure is that switch. If a VRCFury update renames or retypes `enableSps`, the
   reflection lookup returns null, and the loop used to `continue` in silence: Fury patches its
   own deform, YAPS patches over it unless the backstop still recognises what Fury wrote, and the
   report says nothing. **Checked against this machine's VRCFury 1.1408.0, by run 401's own report
   rather than its source:** the SPS test avatar reads "Asked VRCFury not to patch 1 plug
   shader(s)", so the switch exists today and nothing is double-deformed. **Now guarded:** a plug
   component with no such switch is collected and reported as a warning naming the plugs and asking
   for the VRCFury version. It needs nothing about SPS2 to work, so it covers any later rename too.
   Smoke test 34 of 34 with no false warning; the next YAPS corpus run is the proof it stays silent
   on the SPS avatars, since a false positive would show there.

   **Known ceiling:** the lookup is by class name `VRCFuryHapticPlug`. If that is renamed, nothing
   matches, nothing is suppressed and nothing is warned. Catching it would mean counting the plugs
   VRCFury baked (`BakedSpsPlug`) against the ones suppressed and warning on a mismatch.

   **A decision for Joe before going further:** a detection signature for SPS2, if one is ever
   wanted, would come from baking an SPS avatar on a newer VRCFury and reading the property names on
   the material it writes. That is observing a tool's output on an avatar, the way `_SPS_Bake`,
   `_TPS_PenetratorEnabled` and `_OrificeData` already entered `Detect`, and not reading VRCFury's
   source, which `YAPS-CLEAN-ROOM.md` rules out. Whether that sits inside the clean room is his to
   say. On 2026-09-13 a search for the term turned up VRCFury source files by name; none were
   opened.

7. **The sweep's carried toggle failures**: triaged as avatar-side, no tool signature. **141 in run
   401, 2026-09-12, across 35 avatars**, up from the 131 this entry was written against. Read the
   sweep line carefully: its "(35)" header counts AVATARS and each bracket beside a name is that
   avatar's failures, so the total has to be summed. The biggest carriers are "open me" (17),
   CowBotNSFW (16), CowBotSFW (15) and Umbreon Nsfw (13). The ten extra since the entry was written
   are unexplained and worth a diff of which avatars moved, before anyone assumes they are still
   avatar-side. The prediction that Fury's wired socket toggles flip to "responded" is still to
   check.

8. **Phase D, retiring what the atlas cannot carry.** D1 proves the texture-parser route to the
   animator, D2 moves socket shapes, depth and haptics onto it, D3 deleted `YapsChannel` and its
   triggers with 4.5.1, D4 keeps the TPS material import, which is a separate thing from the tag
   plumbing. D5 inverts the atlas so a socket finds a plug the same way a plug finds a socket.
   Detail, and which lever kills which cost, in the atlas section below.

9. **Three things owed in game, none of which this machine can answer.** Grouped because they all
   want a headset or a second person, and because each one gates work that is otherwise finished.

   - **The chain across two bodies.** Own-body answering resolves through the atlas, proven in
     game on 2026-09-07, and the two-body case passed in the editor: the wearer's own socket
     first, the other person's second. What has never run is that same chain with a real second
     player. The record of the spike is in `archive/Closed-2026-09.md`, including why the switch
     defaults off and why a hole has to end the chain.
   - **A socket on a limb, now that self-exclusion is dropped there.** The trade made on
     2026-09-08 is that somebody else's plug can open a hand socket, and so can the wearer's own
     if it rests against it. Both halves want looking at, and the second is the one that will
     read as a bug.
   - **The restored marker ranges.** Soba is taking a toy mod build carrying the CVRGoesBrrr
     merge against a rebuilt avatar. Until that reads back, the change is made on source rather
     than on evidence, and the ranges are baked into real lights, so an existing avatar has to be
     rebuilt before it proves anything.

   Plus what 4.6.0 shipped without anyone wearing it: socket clips playing by depth, the TPS
   bulge falloff, a one-way ring refused from behind, the tag dropdown narrowing a plug built for
   several places, a multi-mesh plug switched and resized, and the Mesh-to-None cleanup. The
   first four want a partner; the last two can be checked alone.

10. **An advisor profile for the corpus. BUILT 2026-09-13, first run (402) found a bad
    recommendation, fixed on dev, not yet re-run.**

    **Run 402, 2026-09-13:** 83 of 83 converted in 6644 s, nothing failed. No baseline, so the sweep
    labels every existing failure "WORSE than baseline (32)"; that is `TallySweep` given a null
    baseline, not a regression. Exceptions (33 `ArgumentNullException`, 3 VRCFury missing files)
    are the same kinds as run 401. Apply all turned Face tracking on for 61 avatars and Base /
    locomotion on for 29; it unticked nothing on picking and left nothing blocked.

    **The finding: of the 29, 8 had nothing in Base but VRChat's stock locomotion** (one layer,
    21 `proxy_*` clips, none authored), copied into the slot so the descriptor does not mark it
    default. `CustomLayer` only asks "not default, has layers", so the advisor called it the
    avatar's own walking and ticked the box: the configuration the spin report came from, with
    placeholder clips merged at weight 1 over ChilloutVR's locomotion. Three more were 18 proxy
    plus 4 authored, which is real content and still recommended. **Fixed:** the Base rule now
    checks `AnimatorMerger.IsProxyOnlyLayer` on every layer; stock with Base off gets no row, stock
    with Base on gets a Change row switching it off. The merge itself is untouched (that is where
    fix 2 was withdrawn). `AdvisorTest` gained four checks, 13/13. **Not verified:** a second
    advisor run showing those 8 drop out.

    **The other 13 of the 29, looked into 2026-09-19: every one is GoGo.** Their Base slot holds
    GoGo's own `GoLocoBase` controller (several variants, 900 to 1,200 `Go/` references each), whose
    layers are named `Locomotion`, `Poses` and `Smooth Float`. The conversion was right: with
    `stripGogoLoco` on, the stripper removes those layers by their `Go/` references, so nothing
    merges. The advice was wrong: its GoGo exemption tested layer NAMES only (`IsGogoLayerName`),
    which never matches GoGo's own controller, and it was also gated on `AvatarUsesGogo`, which reads
    the expression parameters rather than the layers. So Apply all ticked a box that merged nothing
    and told the user GoGo's walk was the avatar's own. **Fixed:** `SystemStripper.IsGogoLayer` is
    now the one test, name or any `Go/` reference, the same rule the stripper's locomotion check
    applies, and the advisor calls it with no `AvatarUsesGogo` gate. `AdvisorTest` 15/15. Expected in
    the next advisor run: Base applied on 8 avatars, down from 29 (the 5 Sootie scenes and the 3
    mixed Chimera/Rotormantid).
    **Advisor run 405 (2026-09-22, on 4.6.2, accepted as the first advisor baseline):** Base
    applied on 9, not 8. The ninth was a GoGo demo whose Base controller is GoGo's `Locomotion`
    (placeholders only) plus an EMPTY add-on layer. Probed in batch: the empty layer fails
    `IsProxyOnlyLayer` for having no clips, so "every layer is placeholders" never held and the
    stock layer passed as the avatar's own; the merge then dropped everything, so the tick merged
    nothing. **Fixed on dev 2026-09-22:** the placeholder test skips stateless layers
    (`AdvisorTest` 16/16). The next advisor run should differ from the baseline on that one avatar
    only, Base off. Exceptions (11) and errored avatars (4) same as run 402.
    `Dev/Corpus/run-corpus.sh --advisor` sets `AVATARBRIDGE_PROFILE=advisor` (and unsets it
    otherwise, so a stray value cannot turn a normal run into this one), and digests land under
    `Regression/Yaps/Advisor` or `Regression/Advisor`. Each avatar starts from `new BridgeSettings()`
    with only the clone, the output folder and texture resizing overridden, then takes the window's
    own two steps through the window's own two calls: `AvatarAdvisor.MatchOptionalLayers` (what
    picking the avatar unticks) and every `Analyse` row `AvatarAdvisor.IsRecommendation` accepts.
    **Both were private to the window and moved onto `AvatarAdvisor`**, with the window delegating,
    so there is one definition of what Apply all does rather than a copy in the runner. The digest
    gains an `[advisor]` section above the settings: what was unticked, what Apply all applied
    (with its kind, since a Blocked row carrying a fix is applied too), what stayed blocked with no
    fix, and what was left to the user. Advice is taken on the source before `Convert` clones it,
    which answers the "clone versus scene" worry below: the window does the same.

    Verified so far: all four define states compile, the runner compiles inside the corpus project,
    and `AdvisorTest` passes after the move. **Not verified:** a run. The first one has no baseline
    to compare against, so it will report every avatar as new; what it is for is reading the
    `[advisor]` sections and the report warnings on avatars converted the way users convert them.
    Does not compose with `AVATARBRIDGE_PHYSICS=DynamicBone`: the advisor chooses the physics
    target itself, so under the advisor profile that variable adds no folder, or the folder name
    would claim a solver the run never used.

    *As queued:* `CorpusSettings` is one fixed
    profile for every avatar: all five layers on, both physics options that invent physics on,
    the BETA shader patcher on. That is deliberate and should stay the default. A digest must only
    move when the code moves, and per-avatar settings would also move it whenever the advice
    changed, with no way to tell which did. Maximal settings also push each avatar through more
    code than any user would.

    **What it cannot answer is whether what we recommend works.** No run converts an avatar the
    way a user who presses *Apply all* gets it, so a recommendation that produces a broken avatar
    is invisible. The spin reported on 2026-09-12 came from a setting the report had flagged, which
    is the same family of question from the other side.

    **Shape, following what already exists:** `AVATARBRIDGE_PHYSICS=DynamicBone` already runs a
    second profile into `Regression/Yaps/DynamicBone`, and its comment says why the folders never
    compare against each other. An `AVATARBRIDGE_PROFILE=advisor` beside it would start from
    `new BridgeSettings()` (the user's defaults, not `CorpusSettings`), run `AvatarAdvisor` against
    each avatar, apply exactly what *Apply all* applies (Recommended only, never Your call), and
    digest into its own folder. Two things it must record per avatar that the fixed profile never
    needs: which settings the advisor changed, so a digest change can be traced to advice rather
    than code, and whether *Apply all* left anything Blocked. Settings a user can never reach from
    the window stay at their defaults, and `slimTexturesOnConvert` stays off for the same reason
    `CorpusSettings` gives.

    **Before building:** read how `AvatarAdvisor` is driven from the window, since its findings are
    measured off the avatar in the scene and the runner converts a clone. And it is another full
    run's worth of wall clock per use, so it is a profile to run before a release that touches
    advice, not on every run.

## The atlas: what has never been worn, and what phase D is for

Phases A, B and C closed with 4.5.0; pass 1 passed all four steps on 2026-09-03 (`YAPS5.md`) and
the step-by-step log is in `archive/Closed-2026-09.md`. What stays here is the code that has never
run against reality, and the design that is still design.

What `yaps_resolve.cginc` does now: lights, then the atlas, each overwriting the last where it
answers, `socket.tier` recording which one did, and the behind-the-base guard last so it judges
whichever answer survived. **What a pair sees when only one side has the atlas is answered:** C2
keeps sockets emitting stock DPS ranges byte for byte, so a plug that does not read the atlas
still finds them by light, and B3 proved that direction in game against a legacy avatar. What is
NOT symmetric is the atlas between two YAPS avatars of different versions: the header carries a
protocol number and the hash refuses any other, so a mismatched pair falls back to the lights and
silently loses tags, one-way rings and own sockets. Both READMEs say so since 2026-09-12.

**The size gate.** `YapsAtlasFits` is a camera test, so C1's rule that the blend must not depend
on presence is violated by design: a target too small for the rect drops to the light tier. The
rect has been 932 by 596 since protocol 5, and every view that matters was measured in game on
2026-09-12 and carries it, the self portrait and the personal mirror included. The fallback layout
that fits fewer cells into about 240 pixels square has therefore never been exercised by anything
real. Revisit only if a divergence turns up between two cameras that both fit.

- **B2's crowded instance is NOT a ship blocker, 2026-09-06.** Sockets hash by spatial cell into
  4096 cells per level with two homes each, so expected first-home collisions are about N squared
  over 8192: under two at a hundred sockets, about five at two hundred, and a first-home clash
  still reads from the second. The platform's real ceiling is nowhere near that: an instance
  rarely holds 28 people and most carry none of this, so a realistic crowd is a couple of dozen
  sockets and about 0.07 expected clashes. Even 28 people fully kitted is 112 sockets and under
  two. What the arithmetic does NOT cover is throughput: every avatar's socket writers run on the
  VIEWER's client, so a full instance is a hundred-odd sockets times eight quads drawn ahead of
  the scene every frame. Test it if a crowd ever turns up; do not hold a release for it.
- **Two plugs in one socket: built on one route, open on the other.** The socket's shader takes
  the deepest plug in reach rather than the nearest, `yaps_socket.cginc:102`,
  `depth = max(depth, ...)`. A socket whose shapes ride the ANIMATOR instead still takes whichever
  sender wrote last, because that is what a ChilloutVR trigger does, so two plugs fight over the
  depth there. Socket-side, no extra sync, and it can ship on its own. Untested in game either
  way: wants two people and one socket, and the shape to watch for is the second plug passing
  through mesh that did not open.
- **B5, a plug through TWO sockets.** The chain walk went in on the strength of an offline
  compile, and `YapsChain` has carried an ordered list with arc-length ranges since then, so this
  is built rather than planned. Nobody has built the avatar that exercises it, which is why the
  resolver sat half used for a day and why the chord-versus-curve tear was found by reading rather
  than by looking. What to watch for is a torn ring of mesh at the joint, and a shaft that picks
  one socket and ignores the other.
- **B6, a multi-material plug seen by SOMEBODY ELSE.** The original cause is gone: the per-slot
  driver tasks that only diverged remotely belonged to the contact channel, which 4.5.1 deleted.
  4.6.0 replaced the whole area by baking every mesh of a plug rather than the largest, so the
  question is now whether THAT looks right on another person's screen. The shape at risk is
  unchanged: half the mesh following the socket and half sitting still, on the other person's view
  only. Needs a second client, which means a second person.
- **B7, a plug whose shaft bone is rolled differently from the hub it hung off.** The shaft
  descent inherits the authored UP from whichever bone it lands on. Forward does not matter, that
  is measured off the vertices and only seeded by the authored one, but roll is taken as given. It
  reaches the authored bend direction and the wriggle phase, not the bend toward a socket, so the
  shape to watch for is a plug that curves the wrong way at rest and bends correctly once engaged.
- **B8, a camera that draws nothing over the atlas rect. A risk in theory, never seen.** Moving
  the writers before the scene, render queue 55, means the scene covers them, which is what makes
  the payload invisible. A camera rendering neither geometry nor sky in that corner would leave
  them showing. Every ChilloutVR world has a skybox, so the world view and the mirrors are covered.
  It was once written down as safe only because the self portrait failed the size gate, and the
  portrait passes it since the rect moved, so a transparent-background camera is the shape still
  worth a look. *Corrected 2026-09-13: this entry briefly said the payload was SEEN in the
  2026-09-12 captures. Those squares were the debug readout's markers; see Next up item 5.*

**WHICH LEVER KILLS WHICH COST, 2026-09-06.** These get conflated, so they are written down apart.

*Show the avatar's OWN depth animations to other players* costs 32 sync bits per socket and
nothing else. The trigger and the animator layer are local and exist either way; the bits buy the
room seeing the result. D5 below does not touch it. **D1 deletes it outright**: the reason the
value has to be sent is that ChilloutVR runs an avatar's triggers on the wearer's machine alone,
so only one client ever computes it. Every viewer's GPU can compute the same depth from the atlas,
and the texture parser hands it to that viewer's own animator, so every client arrives at the
answer independently and there is nothing left to transmit. The toggle stops being cheaper and
stops existing.

A second lever, for how many sockets can use the free shader route at all: **one material carries
one bake and one origin**, see `AnotherSocketBaked` in `YapsNativeBuilder`, so the second socket on
a mesh is pushed onto the animator whatever the transport does. A body mesh usually carries
several. Letting one material hold SEVERAL bakes and origins, with the socket deform looping over
blocks, would move most body-mesh sockets onto the shader route. Bigger than it sounds: the bake
texture grows and the deform gains a loop. Not a transport problem, which is why no amount of
atlas work reaches it.

**D5, THE OTHER DIRECTION, raised 2026-09-06.** The atlas carries socket to plug and nothing else,
so a plug resolves at range while the socket it entered still finds the plug the old way: a
tracker light in one of four vertex slots. The visible consequence is a plug that bends while the
socket stays shut, and it gets worse the more sockets and plugs are in the room, because the light
slots are contested.

The same machinery inverts. A plug writer quad hashes the plug's base and length into cells at
Background-945 beside the socket writers, one grab still serves both, and the socket's vertex
shader reads its neighbourhood the way a plug reads its own. Depth is
(plugLength - distance) / plugLength and both operands fit a payload exactly like a socket's
position and facing do. Roughly doubles cell occupancy, which the arithmetic above says is nowhere
near the budget.

What it CANNOT carry, and this is the part to keep straight: haptics, and the depth parameter that
drives the author's own animated bulges. Those have to arrive in animator space, because a toy mod
reads a parameter and not a texture, and the atlas never leaves the GPU. That crossing is D1's
texture-parser route. Atlas plus D1 is the whole story; atlas alone is two thirds of it.

Two things not to lose while doing it. The marker lights are not overhead to be deleted, they are
the INTEROP surface: emitting them is how a legacy plug sees a YAPS socket (C2) and decoding them
is how a YAPS plug sees a legacy socket (B3), both proven in game. The atlas can be primary for
YAPS to YAPS without either of those going anywhere. And the reader's RANGE GATE has to stay: a
plug already publishes its base and length as a tracker light, so nothing new is disclosed by
publishing to the atlas, but a socket across the room must not start reacting to a plug that never
came near it. Range is a decision in the shader, never a property of the transport.

## The atlas knows whose socket it is. SHIPPED IN 4.6.0, verified in game 2026-09-12

*Four of the seven checks below are done and hold their evidence. Still open: 1, 2, 3, the tail
of 6 and 7. Nothing on this list blocks anything; they are the cases a user could reach that
nobody has stood in front of yet.*

Protocol version 5. Every socket writes its wearer's owner id into a FOURTH atlas pixel per
octant, and a plug compares it with its own. Where both ids are known, whose socket it is is a
fact; the nearest-hip vote runs only where either is zero. The vote was what failed when two
bodies overlap: a partner's socket pressed against the wearer's hips voted as the wearer's and
was refused mid-insertion.

**Where the id comes from.** `CVRParameterStream` type `SeedOwner` (90000) is a CRC32 of the
wearer's user id. The stream's `Mod` with 16777216 keeps its low 24 bits as an exact integer,
because the client's Mod works on the integer when the source is one, and a float holds every
integer below 2^24. It lands in `YAPS/Owner`, a synced float, and a direct blend tree weighted by
that parameter over a clip setting `material._YAPS_Owner` to 1 carries it onto every renderer
whose material has the property: plugs, atlas writers, the readout. `YapsOwner.Wire` does all of
it and is idempotent. The converter calls it after the lighthouse; the toolkit calls it from the
channel build, the socket build and both removers, and it takes itself out when nothing carries
the property any more.

**Two traps, both handled in `Wire`.** The stream is a local component, destroyed on remote
copies, so the parameter has to sync; a remote copy reads 0 until its first sync, and 0 means
unknown. And a stream added from code serialises `referenceType: World`, because only the CCK
inspector switches it to Avatar, and only when it is opened. The client computes SeedOwner for an
Avatar reference alone, so without the explicit set the id would be 0 everywhere, silently.

**The self rule.** Known and different: never refused as own. Known and the same, and the
socket numbered: the plug's own list decides (below). Known and the same, unnumbered: refused
only when INBOARD, nearer the wearer's hip than the plug root is. Unknown: the old vote,
unchanged. `_YAPS_SelfAllow` still opens everything.

**Choosing per plug, protocol 6, same day.** The plug inspector has a **Your own sockets**
checklist. A self choice only ever involves one avatar's own plug and sockets, and the build sees
both, so the choice never crosses the screen: each writer publishes its socket's number on its
avatar, 1 to 15, in the facing pixel's alpha beside the kind, and the plug holds a bit per number
in `_YAPS_SelfSockets`. No new pixel, no sync, no menu rows. `YapsPlug.selfEnter`/`selfRefuse`
store changes against the default (off the hips ticked, on the hips clear, bone found by climbing
to the first humanoid bone), so a socket added later starts at its default. `YapsOwner.ApplySelf`
numbers by hierarchy path and sets every mask; it runs from `YapsSocketBuilder.Build`, `Wire`
and every tick, materials only. Converted plugs have no component and take the default. Writer
materials are now one per kind, tag set and number.

**The rect.** A fourth pixel at 32 columns is 1064 wide, which a mirror capped at 1024 cannot
hold. Columns went to 28 with the rows per level rounded up, which the old floor division got
wrong for any column count not dividing 4096: 932 by 596. It loses targets under 596 tall that
808 by 520 fitted, 960 by 540 among them, and no layout keeps those, since the slots alone
outnumber 960 by 540's pixels.

**VERIFIED IN GAME 2026-09-12, protocol 7, cross avatar.** A tester on the same 4.6.0 test build
resolved another person's socket through the atlas, read off the debug overlay: who resolved it
green (atlas, not lights), what the atlas read green (a socket), atlas on this camera green
(live at 932 by 596 in a desktop window about 1280 by 720). Both plugs in view read the same. So
the rect, the 28 columns, the owner pixel per octant and the grab all survive a real client with
two avatars writing into one screen, which is the first end-to-end proof of the protocol since
the layout moved. Own-body answering was OFF in that test (the own-sockets cell sat amber), so
nothing below about the wearer's own sockets is covered by it.

*Protocol version is the reason a 4.5.1 avatar and this one cannot see each other's sockets at
all: 4.5.1 writes version 2 and the header hash refuses anything but 7. Marker lights carry no
version and still cross. Every tester has to be on the same build for the atlas.*

**Unverified, in the order to check:**
1. Own avatar in game: a temporary slider on `YAPS/Owner` reads non-zero. Zero means the stream
   is not running: the reference type, the CCK version, or a type only the beta client has.
2. A second client: the remote copy's `YAPS/Owner` equals the wearer's, and its plug still leaves
   the wearer's own hip socket alone. *The cross-avatar half is proven, 2026-09-12: an owner id
   that refused everything would have shown as nobody resolving.*
3. Overlap: a partner's socket pressed into the wearer's hips is answered.
4. **DONE, in game 2026-09-12.** Every view carries the atlas at 932 by 596: the desktop window,
   VR both eyes under a simulated headset, a world mirror, the personal mirror, the self portrait
   and the third-person camera. The atlas cell read green in all of them, including the readout
   drawn INSIDE a mirror and inside the portrait, which is the case that says the grab followed
   the camera rather than the main view. Nothing to shrink the rect for, and the fallback layout
   at 240 square stays unexercised.

5. **DONE, in game 2026-09-12.** All four states on one avatar, read off the overlay rather than
   off the bend. Own hand ring switched on, "own sockets" ON: atlas, engaged, bending. Same
   socket with "own sockets" OFF and the tick clear: nobody resolved, not engaged. The socket
   itself switched off: nothing in the atlas at all, which is what says the other two were not
   luck. Then the tick set by hand, re-baked, with "own sockets" OFF: bending again.
6. **The first half is DONE, in game 2026-09-12**, and it is the whole of `_YAPS_SelfChosen`: the
   checklist started that ring CLEAR because the plug's carried tags refuse it, and a tick set by
   hand entered it anyway with the blanket toggle off. The bottom row's own-body cell stays AMBER
   throughout, which is right and will read as a contradiction to somebody one day: that cell
   shows `_YAPS_SelfAllow`, the blanket flag, and a chosen socket is entered past it. Still open:
   with the tag chooser on one tag, a default-ticked own socket that tag does not name is not
   entered.
7. An unticked own hand or mouth socket with its marker lights lit is not entered either.

**Follow-ups, not started.**
- DONE 2026-09-11: SPS plug rules keep their side on conversion. Others rules go to the tag test
  as before, Self rules and hip avoidance ride on the plug's material as override tags, by name,
  and `YapsOwner.ApplySelf` turns them into ticks on every build, so a renumbering follows.
  *Corrected the same day: that never ran for a real conversion.* `AdoptPlug` gives every
  converted plug a `YapsPlug`, and `ApplySelf` read the material's rules only for a renderer
  with no component, so a converted plug always took the hips default. The smoke test built
  exactly that component-less shape and passed. A component plug now starts from its material's
  rules, the checklist's default follows them, and the test adopts a component first.
- DONE 2026-09-11: an own socket chosen BY NAME skips the tag test. `_YAPS_SelfChosen` holds a
  bit per chosen socket: a tick set by hand on a toolkit plug, or what a converted plug's Self
  tag rules allow (hip avoidance alone chooses nothing). A default tick still answers to the
  tags. A second mask rather than the ticks replacing the tags, because the tag chooser animates
  `_YAPS_TagInclude` in game, and ticks that skipped the tags would have put the wearer's own
  sockets out of its reach. The checklist's default also starts clear on a socket the plug's
  baked tags refuse, so what it shows is what the plug does. Untested in game, and no avatar on
  this machine has an SPS rule with its sides set; the smoke test covers the tick maths and fxc
  the shader.
- DONE 2026-09-11: an unticked own socket was only turned down by the atlas. With its marker
  lights lit, the light tier answered it anyway, since its own-body test is the hip vote and
  refuses only inboard sockets. The chain now flags an own numbered socket refused by its tick
  when it sits within a tenth of a length of the light tier's answer, and that answer is undone
  as a tag refusal's is. A separate flag, not `refusedD`: the hip socket is always the nearest
  own socket, so it would have crowded the tag refusal out of the one slot, and lit the debug
  view's refusal cell on every avatar with one. The trade: a legacy partner socket, with no
  atlas writer, pressed within that tenth of your own unticked socket is ignored too.
- DONE 2026-09-12: a converted plug's tag rules survive a toolkit re-bake, and it gets a chooser.
  The conversion wrote the author's answers and refuses to the material as hashes and the adopted
  component kept empty lists, so the inspector showed no rules, `YapsTagMenu` saw nothing to
  choose between, and `YapsNativeBuilder.Bake` wrote `Patterns(empty)`, a zero vector, over the
  material the moment anyone pressed Build in the toolkit: every converted plug then answered
  every socket. Found by asking whether a converted avatar can use the toolkit at all, which it
  otherwise can. `AdoptPlug`'s caller now copies the words from `YapsBakePrep` onto the
  component, and the converter calls `YapsTagMenu.Build` beside the lighthouse. Two builders
  again, and a new shape of it: not a rule that disagreed, but a value only one path could
  produce and the other overwrote in silence. Worth a sweep for any other property the converter
  writes to a material that the toolkit writes from a component. Untested in game.
- DONE 2026-09-12: a socket whose mesh goes back to None undoes its own build. `BakeSocket` and
  `YapsSocketReactions.Build` both returned null on an empty shape list, so the reactions layer,
  the synced depth parameter, the depth contact and the baked material all stayed on an avatar
  that no longer opened anything. The depth ANIMATIONS list has always read an empty list as an
  instruction; the shapes list never did. Both now do. The contact and parameter stay while the
  depth animations or the author's own layers still read them, which on a converted avatar they
  usually do. Found alongside it: Remove restored the socket's material to slot 0 by assumption,
  so a socket baked into any other slot left its bake in place and painted its original over
  slot 0; the renderer and slot are recorded on the socket now (`bakedRenderer`, `bakedSlot`)
  and both paths use them. Smoke test covers the restore. Untested in game.
- A per-plug menu choice. `_YAPS_SelfSockets` is a plain float, so a dropdown could animate it
  between a few sets, at the cost of one synced parameter a plug so every viewer bends alike.
- Props and world sockets carry no id and fall back to the vote. `SeedInstance` would give a
  spawnable its own; nothing needs it while nothing refuses a prop as own.
- Stripping contacts from the transport, which is planned, removes the channel latch further
  down this file, and leaves the owner id as the one self guard that does not rest on geometry.

## An avatar spun on the spot after landing, in VR only. FIXED 2026-09-21 on dev, for 4.6.2

**The 4.6.1 fix did not work, and the diagnosis below it is WITHDRAWN.** A second report on
2026-09-21, wearing a 4.6.1 conversion of the same avatar family: prefab at `m_ApplyRootMotion: 0`,
so the root-motion change applied, and the spin survived it. The reporter then reconverted with
*Base / locomotion* unticked, nothing else changed, and the spin was gone. That is the measurement.

**What the merged stock Base layer actually did, read from the client (decompiled 2026-09-21,
`IKHandlerHalfBody.HandleRootAngle`, `BodySystem.SetHeadWeight`, FinalIK `Spine.Solve`):** its
placeholder clips were masked out, as the earlier record says. Its JumpAndFall states carry
VRChat's tracking controls, converted to Body Control: `HardLand` and `QuickLand` hand head, pelvis
and legs to animation (weight 0), `RestoreTracking` hands them back. `SetHeadWeight(0)` zeroes
`spine.rotationWeight`; the half-body VR handler then sets `maxRootAngle = 180`, and FinalIK's
`Spine.Solve` skips its root-follows-head rotation entirely at 180, so the body stops turning with
the headset. Weight back to 1 sets it to 25 and the root turns to catch up. The desktop handler has
no such branch. Why that becomes a continuous spin rather than one catch-up turn is not read off
the code; that it is the trigger is the reporter's test, and it explains VR-only on the first try
where the mouse-versus-headset story was a guess.

**The fix is the one withdrawn on 2026-09-13, without its guard.** `AnimatorMerger` now drops a
Base layer whose every clip is a `proxy_` placeholder, tracking controls included, reporting it the
way the hand-pose layers are reported and counting any parameter drivers that went with it. The
guard was the mistake: those thirteen behaviours were the cause, not something to protect. The
root-motion change stays as hygiene. Advisor side already done on 2026-09-13 (stock Base is
recommended off). **Corpus run 404 (2026-09-21, accepted as the Yaps baseline):** 20 stock-Base
avatars lost `[Base]` with the report line, the 8 authored ones kept it, no new exceptions.
Shipped in 4.6.2. Nobody has worn 4.6.2 yet.

**Action layer, same family. CHANGED ON DEV 2026-09-22, for 4.6.4, unreleased.** Stock Action
layers are proxy-only too, and their AFK and emote states carry the same Body Control hand-offs
(corpus digest of a stock avatar: `Afk Init` one Body Control, `BlendOut` five, every clip
`proxy_`). Unity runs state behaviours on a weight-0 layer, and the client feeds AFK from the
headset proximity sensor, so the states fire. A proxy-only Action layer is now dropped whole, the
same as Base. Only reaches users who tick *Action*, which is off by default and never applied by
the advisor. **Not measured:** nobody reported a problem here and nothing was tried in game; the
drop costs nothing visible because the layer has no clip that plays, which is why it went in on
analysis alone. Emote states differ from AFK: the client already sets `maxRootAngle` 180 during its
own emotes, so a hand-off there matches native behaviour. Corpus will show it on the next default
run (it forces Action on).

*The record below is kept as written on 2026-09-12. Its chain is right up to the mask; its
conclusion about root motion was wrong, and corpus run 401 verified only that the flag changed.*

### Superseded record, 2026-09-12

Reported wearing a 4.4.3 conversion, reproducible for the user in VR and not on desktop, and not
reproducible here on desktop or under MockHMD. The video showed a rigid yaw at a roughly constant
rate, the pose never distorting, starting after the avatar came down from an airborne pose. "Lands
and then spins" is what named the subsystem: a continuous symptom says almost nothing, a symptom
with a trigger says a state machine did something.

**The chain, every link out of the user's own artefacts.** `convertBaseLayer` was on, so
`[Base] Locomotion` merged in at layer 3, weight 1, above ChilloutVR's `Locomotion/Emotes` at
layer 0. Its ten states play VRChat `proxy_*` clips, `proxy_landing` and `proxy_land_quick` among
them, which the VRChat client swaps out at runtime and ChilloutVR plays literally. The
`AvatarBridge_NoMuscles` mask does block the muscles, which is what the report means by "blocked
from driving the body", but **a mask's body-part bits do not govern root motion at all**, and
`ListEveryTransform` sets every transform in the list active besides. The avatar's Animator had
`m_ApplyRootMotion: 1`, inherited from the source, and nothing in this tool had ever touched that
flag.

**The fix.** `AvatarHygiene.StopRootMotion` switches Apply Root Motion off on the converted avatar
and says so in the report: one property, and it neutralises every clip carrying root movement
rather than the proxies this avatar happened to have. **Corpus run 401 verified it, 2026-09-12:**
83 avatars, 80 changed, every one by exactly the report tally `converted=N` to `N+1` and nothing
else; the three unchanged had nothing to switch off (Filo's scene has all seven Animators at
`m_ApplyRootMotion: 0`; the other two keep their Animators in prefabs and were not read).

**WITHDRAWN 2026-09-13: a second fix, the proxy-only Base skip.** It shipped on dev in `228d08b`
and `6ea1830` and was reverted, and the claim is kept here so nobody reopens it from the same
evidence. It widened `IsProxyOnlyLayer` from `Gesture` to `Base`, then guarded Base with
`HasAnyBehaviour` so a placeholder layer carrying a parameter driver was not dropped in silence.
**The guard defeated the fix for the very avatar it was written for.** VRChat's stock
`vrc_AvatarV3LocomotionLayer` carries thirteen tracking-control behaviours, which is exactly the
"13 tracking/locomotion behaviours converted to Body Control" in the reporter's own report, so the
layer was always kept. It fired on zero of 83 corpus avatars, and the corpus converts Base ON, so
that is a real zero rather than an unexercised path. With root motion off and muscles already
masked, a merged proxy Base layer can no longer move the body, so the skip's remaining value was
close to nil. Narrowing the guard to parameter drivers alone would have started dropping those
tracking behaviours on the stock layer, an untested change, so it was taken out instead.

*This came out of a wrong statement worth recording: the corpus was described as running default
settings, with Base off, and therefore not exercising the skip. `CorpusSettings` forces all five
layers on. Reading it is what exposed the guard.*

**Why it was VR-only, and this part is reasoned rather than measured:** a desktop player's mouse
writes an absolute facing every frame, painting over the drift as fast as it accumulates. VR only
ever asks for a relative turn, so it adds up. It also explains MockHMD reproducing nothing, since
nothing there turns or lands.

**Three things worth keeping.**

1. **The report warned and the warning did not help.** It told them `[Base] Locomotion` overrides
   CVR's locomotion, in bold, with "THE FIX IS ONE CLICK". But it describes the symptom as the
   movement sliders and stances doing nothing, which is not what happened, so a careful reader had
   no way to match it. A warning that names the wrong symptom is a warning nobody can use.
2. **Proxies are refused in three places and merged in a fourth, and that stays.** Both hand
   layers and the locomotion graft detect an all-proxy layer and hand the slot back; the Base
   layer *merge* does not, and the graft's own report line calls those same clips "nothing to
   carry over" while the merged layer plays them. Closing it was tried and withdrawn (above),
   because the stock locomotion layer's tracking behaviours make it more than placeholders.
3. **No gate we own could have found it.** The corpus converts headlessly and never grounds an
   avatar; MockHMD renders stereo and never lands. Anything whose trigger is a locomotion state
   transition needs a person falling onto a floor, which makes it the second bug class this month
   that only a wearer can reach.

*Still to confirm: the user has not yet tested it. The ten-second check offered was to untick
Apply Root Motion on the converted prefab by hand, which proves or kills the mechanism without a
reconversion.*

## Two reports from the field, 2026-09-07

### One cloth per PhysBone, 94 of them on one avatar (issue #7)

An avatar built one PhysBone per hair strand converts to one MagicaCloth component per strand:
94 components, 35 of them a single strand of the same hairstyle. Faithful to the source and the
wrong shape for MagicaCloth2, which simulates any number of independent roots per component.

**Grouping is written and compiles, on the `physbone-grouping` branch, and is deliberately NOT
on `dev`.** It is untested against the avatar that reported it, so it must not reach a release
until it converts that avatar correctly. Chains that would have received identical settings are
folded into one multi-root cloth: same classified kind, same parent on both the bone and the
component's object, same colliders, same numbers. Held out are anchored volumes (measured and
given a collision bone per side), a chain with a PhysBone parameter (the holder name IS the
GrabbyBones parameter), an ignore list (expressed as a root set), and any per-chain curve
(sampled by normalised depth, so length changes what it reads).

**Untested against the avatar that reported it.** The variant to hand is the Quest one, 8 chains,
four of them anchored volumes, none groupable. The reporter has been asked which avatar it is.
Merge to `dev` only once the PC avatar converts and the result is checked in game.

### Freeze rotation axes, VRChat vs Unity (issue #6)

Reported as the axis flags behaving differently after conversion. **Measured, and they do not.**
`Dev/Probes/ConstraintParityProbe.cs` builds the same rig twice, once with each component, and
compares: 2688 configurations across all seven axis masks, rest and offset values, constraint
weight including 0, source weight, a rotated parent, a non-uniformly scaled parent, and one or
two sources. Zero disagreements. A three-link chain agrees too, including built leaf first, where
a solver with no dependency sort would fall behind.

Both solvers keep the driven bone's LIVE local value on a masked axis. The first theory here was
that Unity substitutes `rotationAtRest` and VRChat keeps the animation; that was wrong, and the
probe is what said so rather than more reading.

**One real hazard came out of it and is fixed.** The converter reads the axis flags by name and
its reflection helper falls back to `true`, because a constraint whose flags cannot be read still
has to drive something. So a renamed field would not fail: every constraint would convert as
affecting all three axes and a frozen axis would quietly start moving, which is exactly what the
report describes. Every name resolves on the SDK here, so this is not that reporter's bug unless
their SDK differs; a name that does not resolve now warns instead of being assumed.

**Still open, and not answerable in the editor:** ChilloutVR's constraint order against its own
IK. Needs the reporter's SDK version, which bone, and what the wrong result actually looks like.

## Loose ends, small but real

### Menu toggles did nothing on a plug whose material was not its mesh's first. FIXED ON DEV 2026-09-23, for 4.6.4, not verified in game

Found by the readout probe: the readout sits in slot 1 or 2, and through a real Animator it never
showed. Unity's list of what can be animated on the renderer (`GetAnimatableBindings`) holds
`material._X` and nothing else, and `material[2]._X` resolves to no type at all: it binds to
nothing. `material._X` writes the renderer's own property block, which every slot reads; written
that way the readout showed in slot 1 on two avatars and slot 2 on the third.

So the premise of f76b482 (2026-09-08), that `material._X` reaches the first material alone, was
wrong, and the fix made things worse. From 4.6.0, on a plug whose YAPS materials sit only in later
slots, these all wrote curves that bind to nothing: the toolkit's Deform and own-sockets toggles,
the tag chooser (both builders, indexed since it was written, 01ba6bf) and the size curves
(afdc29c, 2026-09-11, which also deleted the working slot-0 spelling as stale). A plug with a YAPS
material in slot 0 was never affected. Now `YapsToggles.Bound` has one spelling, a clip writes one
curve per renderer, and the size mirror drops the dead indexed curves it finds. The reading half
of f76b482 (`Bare`, `Writes`, the remover) stays, for clips built by 4.6.0 to 4.6.2.

The claim in the tag-set record further down is marked withdrawn. Whether the half-switched-off
plug f76b482 describes was ever seen, or reasoned from the same premise, is not recorded.

**Three follow-ups, same day.**
- **A curve that binds to nothing is now reported.** `YapsToggles.DeadBindings` asks Unity
  (`GetEditorCurveValueType`) about every `material._YAPS_` curve the avatar can play: the
  controller's clips and the menu entries' own. The converter runs it as the last pass, "Check YAPS
  curves bind", after the rename, and warns; the toolkit's build log lists them. The probe plants
  `material[1]._YAPS_Enabled` beside the plain and vector spellings and only the first is caught.
- **The editor hid every menu toggle on every plug.** A block set on one slot replaces the
  renderer's block for that slot, and the renderer's is where the Animator writes. The owner
  stand-in wrote a block on every slot carrying `_YAPS_Owner`, every half second, and the socket
  preview on every YAPS slot, so in the editor nothing animated on a plug's materials could show,
  menu toggles included. Measured: the readout shown by the menu, 4146 px, went to 0 once the
  stand-in ran. Both write the renderer's block now, as the owner layer does in game, and the
  stand-in clears the slot blocks an earlier build left. Editor only; the game has neither script.
- **A toolkit avatar with plugs and no sockets never got a tag chooser.** Only the socket step
  built it, so neither Build on such an avatar nor Bake on the plug's own inspector did. It moved
  to `YapsNativeChannel.Build`, where every door that bakes a plug ends, with the readout toggle
  and the curve check.

- **Toolkit layers could land in a controller ChilloutVR does not upload.** Once the CCK has
  generated Advanced Settings and its override is taken as the avatar's `overrides`
  (`AAS_BaseControllers.cs`), what ships is the generated `_aas.controller` and what survives the
  next generate is `baseController`: two files. The toolkit split its writes between them: the
  owner id, the readout toggle and the tag choosers into the uploaded one (lost at the next
  generate), the marker-lights chooser and the socket reactions into the base alone (not shipped
  until then, with a note asking for *Create Animator*), and the menu toggles into the base and the
  Animator's own slot, which da91ae5 showed can belong to another avatar. `YapsOwner.Targets` is
  now both, once each, and every avatar-wide writer loops over it; removal also reads `overrides`
  and, for old builds, the Animator's slot. Converted avatars were never split: all three
  references resolve to the one `_CVR.controller`. The probe copies the base as a generated
  controller, points `overrides` at it, strips the readout and tag layers from both, bakes, and
  finds them back in both, on two avatars.

`Dev/Probes/ReadoutProbe.cs` covers all four and passes on the three test avatars; the runtime
tester is still 87 of 87. **Its first version polluted the test project:** the two tags it gives a
plug for the chooser check were baked into the converted plug materials and the chooser layers
saved into the controllers, and the tester then failed 10 checks on untagged sockets refused. It
now puts the authored tags back and bakes again at the end, which removed both.

### Blendshapes a plug renderer holds came undone mid-bend. ROOT CAUSE FIXED ON DEV 2026-09-23, for 4.6.4

Field report on 4.6.2, with the output folder: near a socket the plug grew torn translucent pieces
and black shards, and a ring appeared at the tip; at rest it looked right. The plug renderer held
five blendshapes at 100 with no animation, among them toggles that hide parts of the plug mesh.

**Measured, not reasoned:** the shipped bake decoded clean (no NaN, every vertex inside the length,
blend weight 1), so the data was not the fault. The deform lerps from the skinned vertex to one
rebuilt from the bake, and the bake's rest pose is the raw mesh: baked shapes enter only through
`_YAPS_ShapeWeights`, which started at 0 and are written only by mirrored animation curves, and a
held shape outside the baked sixteen was nowhere. So whatever a held shape did was undone as the
bend engaged.

**Fix, in `YapsBaker` so both builders and the swap path get it:** a baked shape's held weight is
the material's starting weight (`Result.ShapeWeights`, written by `Apply`); an unbaked held shape
is folded into the rest pose with the same turn as a shape block. Plugs only: sockets bake with
`objectFrame` and stage their shapes by depth. `YapsHeldShapeTest` 7/7. **Not verified:** on the
reporter's avatar (no mesh here) or in game. A multi-frame shape is folded as its last frame
scaled, exact for one frame only. The frame and length are still measured without held shapes.

**2026-09-22, the test build did not fix it.** The report confirms the fix ran (5 held shapes, all
five in the baked sixteen, every current slot starting them at 1), and the plug still tears near a
native socket too: fine up to the socket, a cone and brown/gold sheets past it, and stretched
toward a socket a length and a half away. Measured and ruled out, each on the shipped files:
- **Physics on the plug's bones. WITHDRAWN as the cause.** Four cloth chains move bones inside
  the plug (one rooted just above it, gravity 1, no ignores), which WOULD break the per-vertex
  frame in play. But the screenshots were outside Play mode, and turning the four off changed
  nothing. Still a real risk in game, unmeasured.
- **Bake data**, decoded: no NaN, all inside the length, weight 1.
- **Bones scaled to zero** to hide parts: none in the prefab.
- **Held shapes collapsing a normal or tangent** into a bad frame: none below 0.2 length.
- **Length measured without held shapes**: 0.582 m worn against 0.586 m baked.
Open, and not reproducible here without the plug's mesh, which is not in the output folder.

**2026-09-22, the debug overlay on the reporter's plug:** resolve healthy in every shot (atlas
green, bending, frame recovered, bake read). Socket at the tip or mid-shaft, all green; no socket,
grey. So the finder is not it, and the 8-bit tag fix does not touch this. The cone past a test hole
floating in air runs about 0.27 of a length past it (140 of 520 px), against the hole taper's 0.10
to 0.30 by default: the part a real body hides. **Candidate, not proven:** the "cone" is the taper
seen with no body round the hole. Pending an A/B against a ring, which carries straight through with
no taper. The white and magenta markers sit slightly apart even at rest, so the bones are a little
off bake pose; small, unexplained.

**Also measured 2026-09-22, "some parts hidden, some not" (Joe's theory):** the plug is ONE renderer
with five slots (shaft, condom, an invisible slot, gold, leather), and all 14843 vertices are baked
fully on the plug: no partial weights, none left out. Its hidden parts are held shapes, baked at
their held weights, so baking "everything on" would bake the hidden parts IN. Four other renderers
share the plug's armature and are not baked (sheath, a ring, a harness, fluff), so any of them on the
shaft would stay straight while it bends: floating pieces, not tearing. Which of them sit on the
shaft needs their meshes, not in the output folder.

**2026-09-23, the same reporter on a 4.6.4 build, still torn** (gold and brown sheets past the
fist, editor screenshot). Joe's theory: the accessories are hidden by blendshapes, so they are not
their normal size when the plug is baked. Measured on the output folder:
- Five shapes held at 100 on the plug renderer (a shrink, and hides for a cock ring, the condom, a
  spiked choker, plates); the materials' starting `_YAPS_ShapeWeights` carry exactly five ones.
- The toggle clips mirror correctly: `blendShape.Disable dick ring` pairs with
  `material._YAPS_ShapeWeights2.z`, both 0 in the restore clip.
- The deform adds each baked shape's delta times its weight to position, normal and tangent
  BEFORE the per-vertex frame is solved, so a shrunken accessory recovers consistently while the
  weights agree. So the plain form of the theory is already handled; it needs the reproduction.
- **New: the output now carries the plug's mesh**, as the readout's copy (`YAPS Cock readout.asset`,
  shapes included), which the 4.6.2 folder lacked. A reproduction is possible for the first time:
  import the folder, run the bend tester on the plug, then A/B with the four hides released.
- **ROOT CAUSE, measured on the reporter's own plug (2026-09-23).** Imported the output folder into
  a test project and ran the bend tester on it: seams 725 mm, edges 70,000x, steps 300x the socket's
  move, sheets fanning off the shaft exactly as in the screenshot. With every shape released on both
  sides, edges 5.5x and steps under 6x: the held shapes are the cause (Joe's theory, right class).
  `Dev/Probes/HeldShapeFrameProbe.cs` repeats the deform's per-vertex frame recovery on the CPU
  against Unity's own skinning. Released, every vertex recovers the same root to 0.00 mm. As
  shipped, every vertex a held shape moves recovered a root 85 mm to 1 m off. Position deltas were
  exact (ratio 1.000); the normals were not: relative to its normal a held shape's delta is 1.40 in
  the mesh and 0.095 in the bake, the renderer's 0.07 scale. The baker keeps rest normals and
  tangents at UNIT length (`YapsBaker` line ~208, since the first baker) but turned shape normal and
  tangent deltas through the same scaled matrices without dividing by that length, so on a mesh
  imported at a unit conversion a shape that turns a normal 90 degrees turned the baked one 3, and
  the frame spun. Invisible at scale 1, and invisible until a shape turns normals hard: collapse to
  hide does. The 22 Sep fix held the weights correctly and so exposed it.
  **Fix:** `DirectionDelta` divides each normal and tangent delta by its rest vector's pre-normalise
  length, in both capture paths and `HoldShapes` (sockets too). Rebaked the reporter's plug with it:
  normals turned exactly as Unity turns them (88.9 against 88.9 degrees), every root 0.0 mm, and the
  bend tester's steps back under 6x with no sheets in any shot. **Left:** the tester's worst edge is
  a hidden part, 0.010 mm at rest drawn to 68 to 98 mm, zero width and invisible in the shots:
  collapsed vertices a held shape moves behind the base go inactive while their neighbours bend.
  And seams open 290 to 770 mm on this mesh with or without the shapes, so they are its own, not
  this bug. **Verified in game 2026-09-23:** the reporter reconverted on test build `sep23`
  (020e88b) and it functions as expected (Joe).
- Side finding, report only: the weigh pass advises shrinking the bake texture ("8192x280 ...
  1024x1024 is the same picture"). It is data, not a picture. Nothing acts on it (the texture pass
  and free wins only touch textures with an importer, and the bake has none), but the advice is
  wrong and should skip YAPS bakes.

### An accessory riding the shaft stayed rigid while the plug bent. FIXED ON DEV 2026-09-22, for 4.6.4

Joe, from the reporter: the torn part is one of the accessories. The converter folded another mesh
into the plug only when MORE than half of it rode the plug's bones, silently, so a harness whose
straps run elsewhere stayed rigid and the shaft bent through it. The toolkit took any mesh with any
weight on the chain, which would take a body touching the base and bake all of it.

**Fix, one rule for both builders:** `YapsBaker.RidesPlug`. A mesh joins when more than half of it
is on the chain, or when any vertex is held more than half by a bone PAST the root. The body meets
the root and goes no further, so it stays out, and the report now names each mesh left out.
`YapsRidesPlugTest` 5/5. **Verified in the editor 2026-09-22** by the reporter on sep22c: "the
accessories are not broken anymore". Not in game. A single-bone plug has no "past the root", so an
accessory on one still needs to be mostly plug.

### In the editor a plug enters a socket on its own shaft. FIXED ON DEV 2026-09-23, for 4.6.4, not verified

**Fix:** `YapsOwnerStandIn` (in `YapsOwner.cs`) gives each avatar in the scene a stand-in owner id,
every half second, in property blocks on every renderer whose material declares `_YAPS_Owner`; in
Play Mode it sets the `YAPS/Owner` parameter instead, since the owner layer animates the property
there. The editor then refuses own sockets as the game does: the reporter's plug has `SelfTag` 1,
`SelfAllow` 0 and an empty mask, so its own numbered sockets are all refused. **Correction:** this
does NOT stop Joe's hand ring. A socket off the hips starts ticked, so the game lets a plug into
its wearer's hand by default too, as SPS does; that one was behaving as designed.



Right after sep22c the reporter's tip "bulges and breaks" as a socket nears. Bulge and squeeze are
0 on every slot, so not a knob. The plug carries its own SPS hole (a fluid target) at the seventh
of its chain bones, and a socket icon sits on the tip in three of four shots. Own sockets are
refused by owner id, which comes from the player and is 0 in the editor, so there nothing is
refused; the tag fix likely just made its atlas cell readable. **Pending:** the same approach with
that socket switched off.

2026-09-23: "still borked", but the socket gizmo is still drawn on the tip, so it is unclear it was
off. In the worst shot the bent plug climbs, turns back down and ENDS on that socket's icons, which
sit where the straight tip would be: the bones are not drooping, the bend is going into its own
socket. The bake is straight (cross-section centre within 1.4 cm of the axis in every tenth of the
length), which rules out a plug baked mid-droop. Joe's own hand ring did the same in the editor. Proposed fix: a stand-in owner id per avatar in edit mode, so the
editor refuses own sockets the way the game does.

### Half of all atlas cells were dead on a camera without HDR. FIXED ON DEV 2026-09-22, for 4.6.4

The patches beside a plug where a socket never engaged, in the editor. The debug overlay put a
socket in one: top 4 red, so the atlas READ the socket's slot and threw it out on the cell tag,
while the preview found the same socket. Moved to half a length it went green.

The writer stores `0.5 + tag / 2` with `tag = k / 255`, and the reader allows 0.001. On an 8-bit
target every even k lands half a step off and reads back 1/255 wrong: 128 of 256 tags dead
(`Dev/Probes/Hlsl/atlas-tag.py`). Which cells die is fixed by the hash, and cells are 32 cm at a
half-metre plug, hence big fixed patches. Half-float keeps every tag, so in game it passed.

**Fix:** `YapsAtlasTag` returns `(1 + 2 * (h % 128)) / 255`, an exact 8-bit step; protocol 7 to 8.
Cost: a slot clash passes the tag 1 in 128 instead of 256, and range still rejects it; 4.6.2 and
4.6.4 are blind to each other through the atlas. **Verified in the editor 2026-09-22** (Joe,
deployed build): a socket beside the plug now resolves by atlas, overlay all green, and the patches
are "a lot better". **Not verified in game.** The atlas passed there before, which fits an HDR
camera; any camera in game without HDR would have had the same holes.

### The atlas scan leaves dead zones near the tip. MEASURED 2026-09-22, FIXED ON DEV 2026-09-23

**WITHDRAWN as the cause of Joe's dead zones**, which were the tag above: the overlay showed the
socket's slot read and rejected, not missed. The reach problem below was modelled first, then
OBSERVED by the runtime tester on 2026-09-23: 27 of 50 places round a 0.649 m tip.

**The fix, measured in the editor on 0.155, 0.330 and 0.649 m plugs, not yet in game:**
- **Two reads.** The level nearest L/2 round the shaft's middle, as before, then the smallest
  level whose cells are at least 1.2 lengths, round the base, adding only what the first did not
  hold. The second guarantees every socket within 1.2 lengths is scanned; the first keeps close
  sockets apart. Every YAPS kind 50 of 50 on HDR and 8-bit, all three plugs. Cover alone (the
  coarse read only) had pushed a 25 cm neighbour hidden or lost from 5 to 19 of 50 on the 0.649 m
  plug. Reader only; twice the header taps when the levels differ, not benchmarked.
- **Numbered buckets.** A socket used to pick its bucket by which octant of the cell it sat in,
  so two sockets a hand apart shared one at every level and the later draw hid the nearer (11 of
  50 at 6 cm). A numbered socket (every socket `ApplySelf` numbers, the first fifteen) now takes
  bucket `(number - 1 + owner) & 7`: a wearer's first eight never clash, and across wearers a clash
  is one in eight at any distance. Numbered pairs 50 of 50 at 6 and 25 cm on all three plugs.
  Writer only and NOT a protocol change: the reader opens all eight buckets and takes position
  from the payload, so old plugs read new sockets and the other way round. Needs the socket
  rebuilt, which the 4.6.4 reconvert already asks for.
- **Props, worlds and past eight, same day.** `YapsSocketBuilder.Build` numbered only sockets under
  an avatar; `YapsOwner.NumberWriters` now numbers every root's sockets, a root with no avatar
  starting from an offset its name picks. With no owner the writer turns the number by the octant,
  so one object's close sockets still part. Numbers 9 to 15 each take the free partner of 1 to 7
  they sit farthest from at build (`RulesMask` reads the numbers now, not list positions). Measured
  on all three plugs: another avatar's pair, one object's pair and two objects' pair, 6 and 25 cm,
  all 50 of 50. **Still open:** two copies of one prop side by side share names and numbers, so
  they fall back to the octant.
- **Cost, measured** (`Dev/Probes/Perf/YapsResolvePerf`, RTX 4080): the second read adds 2.5 us a
  pass to a 6433-vertex plug over an empty atlas, 21.6 us over a worst-case full one (1.9x and 3.1x
  the one-read cost). Negligible against an 11 ms frame even times eyes, shadows and mirrors.
- **The mesh walk** (tester step 4, a socket from 1.8 to 0.2 lengths on four lines): seams never
  open (0.00 mm, all three plugs). Edges crushed to nothing lie behind the socket's opening, the
  socket hiding what went in; a handful outside it at the bend. Stretch reaches 4.5 to 5.8x only
  with a socket right beside the base at 90 degrees. **The steep swing, FIXED ON DEV same day.**
  Off-axis round 1.25 to 1.29 lengths the tip moved 8 to 18 times as far as the socket. Cause: the
  root handle was `lerp(5L, gap / 2, engage)`, and a long handle keeps the shaft straight only by
  pushing the curve's turn past the tip, so the tip whipped round once the handle fell below about
  1.5L. `Dev/Probes/Hlsl/engage-swing.py` reproduced it and ruled out reshaping the handle
  (geometric and harmonic still peak at 7 to 13x). **The sweep:** the handle is always gap / 2 and
  the whole socket path, chain included, is turned about the root toward the plug's forward by
  `acos(aim . forward) * (1 - engage)`, so the bend grows evenly from 1.6 to 1.2 lengths and full
  engagement is unchanged. Tester: worst step 2.9 to 5.9x the socket's move (8.4x at a tenth of
  the stride), was 8 to 18x. Candidate for the reporter's "breaking when nearly in range"; not
  seen by them yet. Seams open up to 0.17 mm dead ahead (was 0.00): the sweep's axis is per-vertex
  noise at tiny angles. Under the 0.5 mm bound; watch it.
- **Phantom sockets mirrored through the world's origin, FIXED ON DEV, protocol 9.** The sweep
  exposed a jump on the 0.330 m plug at 45 degrees: engagement snapped 0 to 1 in 0.002 lengths.
  The test shader's probe of the atlas list showed two holes at the same distance, the real one
  at 45 degrees and one at 134, two fine cells behind it. `Dev/Probes/Hlsl/atlas-phantom.py`
  replays writer and reader: cell (-1, 2, -1) landed on (1, 2, 1)'s slot in BOTH homes and passed
  its tag. The hashes multiplied each axis by an odd constant and XORed, and negating an odd
  product flips every bit but the lowest on both sides, so h(-x, y, -z) == h(x, y, z) for odd x
  and z, for the slot, the second home and the tag at once. So a socket also answered from its
  mirror across the vertical axis through the world's origin whenever both fell in one scan: a
  socket on one side could bend a plug toward empty space on the other, or, as here, sort first
  and trip the behind-the-base gate. Latent since the atlas shipped, but the old single fine read
  only saw it within about half a metre of that axis; the coverage read's 1.28 m cells would have
  spread it over metres round every world's origin. Caught before release, because the tester
  stands its avatars at the origin. Now one lowbias32-mixed word per cell and salt feeds all
  three; 3000 random placements within 3 m of the origin: 12 phantoms in reach on protocol 8, 0
  on 9. Protocol 9, so test builds before this one are blind to it.
- **Tester steps 5 and 6.** A ring on the plug's own shaft, built under the avatar so it is
  numbered and the plug told: refused on all three plugs (SelfTag 1, SelfAllow 0, SelfSockets 0),
  the reporter's case, in the editor. Pictures with `-yapsShots <folder>`: through the capture
  shader, because the avatar's own materials carry the YAPS code from their conversion and showed
  the old deform. The step-4 limits are floors from these measurements now.
- **Tester trap, twice:** a material asset created after the capture materials exist leaves every
  later capture reading nothing. Every numbered writer material is made before them now.

Joe found patches beside a plug where a socket never engages. `Dev/Probes/Hlsl/atlas-reach.py`
mirrors the reader's level choice and 3x3x3 block: the level is `round`ed, so a plug just under a
level threshold reads cells a quarter of its length, and whether the block even reaches its own
tip depends on where the plug stands in the world. A socket AT the tip is missed in 31% of
placements at 0.25 m, 60% at 0.30 m, 59% at 1.2 m. Taking the level by `ceil` instead removes every
miss at the tip. Reader-only (writers publish every level), so no protocol bump. Cost: coarser
cells, so close sockets share octants more often. Not yet in game.

### "Reconverting stacks MagicaCloth" was never stacking. MEASURED 2026-09-16

Field report (KJoy, 2026-09-16, video `Downloads/Unity_kD7oYj2ziX.mp4`): reconverting the same avatar
"adds more stacks of magica", inside ONE avatar under `MagicaCloth Phys`, holders numbered up to
`... 5`, default settings. Their reason it matters: "people who like changing outfits a lot would be
under this".

**WITHDRAWN, both of my proposed causes.** I claimed the source had been contaminated, by a
conversion with `cloneAvatar` off or by *Overrides -> Apply All* on the converted avatar, and wrote
both into the README. `Dev/Tests/ClothStackRepro.cs` (synthetic avatar, seven routes, run in the
corpus project 2026-09-16) says otherwise:

- **A** convert the source twice, defaults: 1 holder in each output, source clean. No stacking.
- **B** convert the output again: impossible, the conversion strips the descriptor.
- **C** convert in place: the source does keep the collection, but its descriptor is gone with it, so
  it can never be converted a second time. Contamination cannot compound.
- **D** *Apply All*: impossible. `Object.Instantiate` on a prefab instance returns a plain
  GameObject, so the converted avatar has no prefab link back to the source.
- **E** outfit added between runs: 2 holders, one per chain. Correct.
- **F** four outfits whose chains share the bone name `L_Ear`, ONE conversion: 5 holders, named
  `MagicaCloth_Hair_root, MagicaCloth_L_Ear, MagicaCloth_L_Ear 2, MagicaCloth_L_Ear 3,
  MagicaCloth_L_Ear 4`.
- **G** three PhysBones on one root, ONE conversion: `MagicaCloth_Hair_root` plus `2` and `3`.

F and G reproduce the video's naming exactly with no reconversion. The numbering counts chains, not
conversions, and their avatar carries many outfits with same-named bones. README rewritten to say
that, and to give the check that would prove otherwise: convert twice unchanged and compare counts.

**Trap for next time:** the first two repro passes reported "0 holders" for every route. Not a
result: a chain no mesh is weighted to is classed as a helper rig and skipped
(`PhysBoneConverter.SkipHelperRigChain`), so nothing had converted at all. Reading those zeroes as
"no stacking" would have confirmed the answer by accident.

**Kept:** the inherited-collection warning in `PhysBoneConverter.Run`, reworded. Route C is real but
one-shot, and if KJoy's counts do grow on an unchanged avatar the warning is what will name it.
**Unanswered:** whether their count actually grows between two conversions of an unchanged avatar.
Asked; no answer yet.

### A slash no longer renames a menu parameter. CHANGED ON DEV 2026-09-14, unreleased

`RenamePass` rebuilt any menu parameter name containing a space, slash, bracket, quote or comma
from its menu label ("CCK-safe"). The slash was never shown to be unsafe: the name that broke
Create Controller in `b63c2a5` had spaces too. CCK 4.0.1 source says a slash is legal: the
inspector derives machine names with `[^a-zA-Z0-9/\-_#]` (`AAS_SettingsList.cs:249`), and
`CreateAASController` strips everything but `[a-zA-Z0-9_]` before a name reaches a file path, and
skips entries the base controller already declares. That regex is the same one
`VerifyMenuParameterNames` already used, so the two rules in the merger disagreed and the stricter
one ran first. 334 of 1728 distinct parameter names in the corpus project's expression parameter
assets have a slash as their only unsafe character (menu-bound ones are a subset).

**For the release notes:** reconverting gives those parameters back their VRChat names, so a
value ChilloutVR saved in a profile under the old label-derived name is lost once. **Not
verified:** a corpus run (digests will move where names changed), and the client accepting a
slash in a synced name in game, which rests on the CCK generating such names itself. No unit test:
the rename lives inside the merge, and the corpus is what covers it.

### Four found wearing the avatar, 2026-08-25/26

A whole avatar baked as one plug turned up four separate faults. Recorded here
because they were found in a session, not in a report, and the queue is the only thing that
outlives scrollback.

1. **The bake made a new material every time: FIXED 2026-08-26.** Both material sites asked
   `AssetDatabase.GenerateUniqueAssetPath`, so a plug removed and baked again left `Fur_YAPS_`,
   `Fur_YAPS_ 1`, `Fur_YAPS_ 2` behind it, one full material per click, in a user's project.
   `YapsBaker.Generated` now derives a path that does not move and re-derives the values from the
   source onto whatever is already there. The file keeps its GUID, so anything already pointing at
   it stays pointed at the right thing, and what was there is never trusted, only its identity,
   so an asset left by an older version cannot carry stale settings forward. One helper serves
   both sites; the primary and the mirrored slots had drifted into two different conventions.

   **The BAKE TEXTURE was the same bug and was missed: FIXED 2026-09-03.** Only the material
   sites were changed in August. The 10 MB bake asset still asked for a unique path, so a
   reconvert left `YAPS Body bake` and `YAPS Body bake 1` side by side, and the name it was
   carefully avoiding belonged to the same plug from the previous run. Named for the renderer and
   the plug now, and the existing one is deleted first. Ceiling: two plugs on one renderer whose
   roots share a name AND a parent name would still collide.

2. **Additional meshes under the armature do not bend: FIXED 2026-08-26, UNTESTED IN UNITY.**
   A plug whose root bone is the Armature patched ONE renderer's materials; every other skinned
   mesh weighted to the same bones kept its own shader and stayed rigid, which is the seam the
   multi-material work closes, one level up. `MirrorToRenderers` now bakes each of them, and the
   bake is per mesh because the texture is indexed by mesh-global vertex id: what they share is
   the primary's FRAME and LENGTH, via a new `shareFrameWith` on `YapsBaker.Bake`, so they bend
   as one object rather than each measuring its own idea of where the shaft is. Scoped to the
   `CVRAvatar`, not the scene root, or two avatars under one container lend each other meshes.
   Skipped entirely when the author names a material slot: that says "this mesh, this slot".

   **`BakedSlot` gained a `renderer`**, because a plug spanning meshes has a slot 0 on each of
   them. Matching on the number alone would have handed one mesh's material to another on Remove:
the "IT BROKE, my fur!!!" failure of 2026-08-25 exactly, one level up.

   *Compile-checked only.* `check-defines.sh` links the project's compiled Runtime assembly, so
   a Runtime change cannot be validated there: the documented blind spot. Needs a real Unity
   compile and a bake on an avatar with a second weighted mesh.

3. **Poiyomi's auto-lock: ANSWERED 2026-08-26, and the pink fix had already closed it.**
   Read out of `ShaderOptimizer.SetLockedForAllMaterials` rather than guessed. The sweep takes
   every material whose shader uses the optimizer and is not already locked, and its test for
   "already locked" is `shader.name.StartsWith("Hidden/Locked/")`: the exact prefix the pink fix
   gave it. So auto-lock-on-upload skips these materials, and has been skipping them since that fix.

   Worth knowing what it would have done: locking resolves properties to constants, and the YAPS
   properties are the ones animation drives (`_YAPS_Enabled`, `_YAPS_BakeScale`, the knobs). A
   locked YAPS material would have frozen at whatever it happened to hold: a plug that never
   toggles and never resizes, in game only.

   **One gap closed with it.** `IsShaderUsingThryOptimizer` keys on the `ThryShaderOptimizerLockButton`
   attribute, while `PatchedName` keyed only on `shader_is_using_thry_editor`. A shader carrying
   the lock button without the editor marker would have been named plainly and swept in. Both
   markers now.

   **Still open, and left alone on purpose:** an explicit "Unlock all materials" grabs these too,
   and tries to restore a `TAG_ORIGINAL_SHADER` that was never written. The result is a broken material,
   recovered by re-baking. Guarding it means writing Thry's own lock records, which is
   impersonating another tool's bookkeeping to survive a button the user deliberately pressed.

4. **Parallel paths: AUDITED 2026-08-26, two closed and one recorded.**

   *Window vs inspector, CLOSED.* Three doors bake a single plug and only one went through
   `BakeAndRefreshMenu`. The window's row button and its make-this-a-plug flow called the bare
   `Bake`, so they left the contact channel holding a previous build's frames and the menu
   animator unrefreshed: the same divergence as yesterday's, in two more places. Both now go
   through the same door as the inspector. `BuildAll` keeps the bare `Bake` deliberately: it does
   the menu and the channel once, for the whole avatar, which is the point of a batch.

   *Remove vs Sweep, CLOSED.* Both clear the channel and refresh the menu animator. The rest of
   the difference is real: Remove undoes one plug, Sweep collects orphans. They are not two doors
   to one job.

   *Native builder vs converter. The slot half is FIXED 2026-09-03, the renderer half is OPEN.*
   **The converter has its own bake path** (`YapsConverter`) and used to patch ONE material slot
   on ONE renderer, so the multi-material fix of 2026-08-25 and the multi-renderer fix of
   2026-08-26 reached the native toolkit only and a CONVERTED avatar tore along the seam.

   The slot half turned up in the wild on 2026-09-03: a plug whose tip was modelled on a second
   material bent along its shaft and left the tip hanging in the air, and nothing in the report
   said so, because `MaterialSlotOf` counted plug vertices per submesh and then returned the
   winner. `MaterialSlotsOf` now returns every submesh that carries plug vertices, biggest first;
   `PatchPlugSlot` patches each one off the same bake; the first that takes it is the slot the
   plug record and the authoring component use, and any slot that refuses says why in the report.

   The CHANNEL half of the same gap closed on 2026-09-04: the static settings reached every baked
   material, but the CVRMaterialDriver tasks that carry the live socket vectors were built against
   `plug.MaterialSlot` alone, so a two-material plug had half its mesh following the socket and
   half sitting on whatever the bake left, and only remotely, only in game. `plug.MaterialSlots`
   now records the slots beside the materials, and the channel builds a task and a driver layer
   per slot. `PlugMaterials` also stopped sweeping the renderer for anything with a bake, which
   put one plug's channel extents into another plug's material when two shared a mesh.

   ~~Still open: **one renderer**.~~ **DONE 2026-09-11 on dev, wants a corpus run to land.** A
   plug split across two meshes got one of them. No refactor was needed after all: the converter
   adopts a `YapsPlug` before the end of `ConvertPlug`, so the extra meshes go through the
   converter's own `PatchPlugSlot` (legacy deform off, Simple Lit fallback, tags, swaps) on a bake
   sharing the primary's frame and length, then take the adopted component's knobs through
   `WriteKnobs` and are recorded in its `bakedSlots`, which is how the own-socket ticks and Remove
   reach them. Their materials join the plug record's `Materials`, so they read the atlas too, and
   `MirrorShapeCurves` mirrors shapes and bone scale onto each.

   **Deliberately narrower than the toolkit:** a mesh joins only when MOST of its vertices ride
   the plug's bones. The toolkit takes any weight, which suits its case, a plug rooted at the
   Armature being the whole avatar. The converter refuses a chain that is the body's, so its extra
   meshes are plug parts, and the one that has a few vertices there anyway is the body touching
   the base, whose materials must not be patched.

   **Two gaps the toolkit has that the converter does not, found doing it:** `WireSize` mirrors
   size curves onto the PRIMARY renderer only, so a toolkit plug's extra meshes do not follow a
   size slider; and `EnsurePlugToggle` writes `_YAPS_Enabled` on `plug.Target` alone, so turning
   the deform off leaves the extra meshes bending. Both tear at the seam under the condition
   named. **FIXED the same day, with three more of the same shape found on the way:**

   - The own-sockets toggle and the tag chooser wrote the primary alone too, so the extra meshes
     kept answering sockets as built while the rest of the plug was told otherwise.
   - Remove handled `plug.Target` alone: the extra meshes kept their baked materials and went on
     bending after the plug was gone. They now go back on the originals recorded for their own
     slots, never the primary's, and lose their size wiring.
   - `YapsCurveMirror` wrote every size curve as `material._X`, which is slot 0's spelling alone,
     so a plug material in any other slot never heard a size slider, on either builder. It now
     writes each slot that declares the property, through the same `SlotsWith` the toggle clips
     use, and clears the old slot-0 curve where slot 0 is not the plug's.

   One rule behind all of it: `YapsToggles.MeshesOf(plug)` is the plug's renderer plus every
   renderer in `bakedSlots`, and everything that writes a plug's material properties walks it.
   An existing toggle's clips are rewritten in place on the next build, so a plug built before
   this picks the extra meshes up without its menu row changing. Untested in Unity and in game.

4. **Parallel paths that disagree.** Two doors to the same job diverged on the same day: the
   window's Build wired the contact channel and the inspector's Bake did not, and Remove cleared
   channels where Sweep did the leftovers. Both were fixed one at a time. That is a class, not
   two bugs: every pair of entry points into one operation wants auditing against each other:
   window Build vs inspector Bake, Remove vs Sweep, native builder vs converter.

### A stale material is invisible, and it wasted three readings

2026-08-26. Three times in one afternoon a test result was wrong because the material was still
running the previous shader, and nothing said so. The tell each time was a debug view answering
a question the shader it was running had never been asked.

**DONE 2026-08-26, in the two places a person actually looks.** The material's own YAPS panel
gets a warning under the banner, because that is where somebody reads a value and believes it:
which is exactly how all three readings were lost. And the Setup window's row goes amber with
"its shader is older than the toolkit: Build refreshes it", because there the fix is one click
away and the row already had a mechanism for saying so.

Still open below: the project-wide sweep. A warning only reaches a material somebody happens to
select or an avatar somebody happens to scan, and a prop prefab is neither.

### Nothing revisits a prop, so a shader fix never reaches it

Found 2026-08-26, after the two-socket prop fix. A patched shader's name hashes the emitted
source, so `IsStale` knows when a material has fallen behind and both bake paths re-patch it:
but only when something bakes. An avatar gets baked because its scene is open and Build is
pressed. **A prop is a prefab sitting in the project, and nothing ever opens it**, so every
shader-level fix reaches everyone except the props, and the only cure today is to make a new one.

**DONE 2026-09-08.** `YapsShaderPatcher.SweepProject` walks every material in the project, keeps
the ones carrying `_YAPS_Bake`, asks `IsStale` and refreshes what has fallen behind; the Props
card carries the button and says to upload the prop again afterwards, since the copy on the
platform is not the copy in the project. The property is the test rather than the shader's name,
so a material patched by an older version is still found. The new shader is written BESIDE the one
it replaces, so a prop's shader stays with the prop instead of moving into this tool's output
folder where a later cleanup would take it. A material whose source shader has left the project
cannot be rebuilt and is counted out loud rather than skipped quietly.

Not yet run against a project holding a stale prop, which is the only thing that proves it.

- **External audit 2026-08-25, the five deferred findings.** An audit by another agent
  (`AUDIT-external-2026-08-25.md`, kept in the repo). Nine of twenty were verified in source and
  fixed the same day; two were wrong (F1's consequence, F17's premise); these five are real,
  deferred on purpose, and each changes behaviour, so each wants evidence before it moves:
  - **F3, strafe folding: FIXED 2026-08-25, fixture first.** `DirectionOf` folded east into west
    and the replacement wrote one clip to both sides, so an author's distinct left-strafe clip was
    lost. Checked against the CCK's own `AvatarAnimator.controller` before touching anything: it
    has separate children at `x: -0.5` and `x: +0.5` and points BOTH at clip `004085e0…`, so CVR
    ships mirrored strafe, but the slots are real and can hold different clips.

    **The corpus could not judge it, so the corpus was given the shape it lacked.**
    `Fixture_AsymmetricStrafe` (built by `FixtureBuilder.RunStrafeOnly`, so the two existing
    fixtures are not rebuilt from a source scene that has been hand-edited since) carries a
    velocity tree with a different clip on each side of every sideways direction. Run against the
    unfixed tool it lost three clips, `(1,0)` played `Fix_StrafeL`, and both diagonals the same,
    which is the point of building it before the fix rather than after.

    `Slot` still describes the direction PAIR, because CVR's slot set is pair-shaped; a `Side`
    now rides beside it, picks are keyed `(Slot, Side)`, and replacement is child-driven so each
    CCK position asks for its own side. A source with one clip for both sides still fills both,
    by falling back to the opposite side, which is what every symmetric avatar relies on.
    After: `(-1,0)=>Fix_StrafeL (1,0)=>Fix_StrafeR`, both diagonals likewise.
  - **F11, slider hole-drop degrades to hole-filling. FIXED 2026-09-08.** `kept.Count >= 2` sent a
    slider tree with one surviving child to the generic filler, which inserted the placeholder clip
    the report text beside it promised sliders never get.

    Dropping every hole instead is not the fix and was the trap here: a 1D tree holding ONE child
    plays it at full weight wherever the slider sits, so a slider whose other clips had gone
    missing would have come out with the survivor permanently on. The threshold is what carries a
    slider's meaning and one child has no other end to travel to. So one placeholder is kept, at
    the far end from the survivor, and the rest still go: the slider fades the one real clip in
    across its whole travel instead of standing it on. The report says so rather than describing a
    rule the code no longer follows.
  - **F12, gesture-hand promotion by substring. FIXED 2026-09-08.** `layerName.Contains("left")` on
    a lowercased name: "Copyright pose" contains "right", so the whole layer went into CVR's
    RightHand slot and whatever it did to the rest of the body went with it.

    What the layer READS decides it now: a gesture layer driven by GestureLeft or GestureRight is
    that hand, read off blend tree parameters and transition conditions, and a layer reading both
    or neither is not promoted at all. The mask still wins where there is one. The NAME is last and
    no longer a substring: the word after something that is not a letter, or the capital in the
    middle of a name, which is the pair of spellings a boundary alone cannot cover, since there is
    no boundary inside "GestureRight" and lowercasing flattens the hump that would have shown one.
    Checked against both failures and the ordinary spellings: "Copyright pose" and "COPYRIGHT POSE"
    match neither hand, "GestureRight", "Right Hand", "hand_right" and "Gesture (Right)" all match
    right, "cleft palate" matches neither.
  - **F18, owner-rule shortcut skips the humanoid guard. FIXED 2026-09-08.** `FindPlugRenderer`
    returned the parent's SkinnedMeshRenderer before `chainLevel` was ever set, so the refusal
    below it asked `HumanoidBoneName(null)`, null is not a bone, and the "your chain is the body"
    check never ran on that path. Silent bypass, not a crash: a component put on the object
    carrying the body mesh baked the whole avatar as one plug and reported success.

    A new test was the wrong shape here, and bone counts or head-and-foot weights would have
    refused legitimate plugs, including the avatar deliberately built as a single mesh. The
    existing guard is right; it was only reached with nothing to judge. So the shortcut sets
    `chainLevel` to the object the component sits on before returning, and the guard reads it: a
    dedicated plug mesh object is not a humanoid bone and passes, and a body mesh sitting on the
    skeleton is exactly what the guard was written to refuse.

    Unchanged for legitimate plugs by construction rather than by measurement, so the corpus is
    still owed: a plug object is not in `skin.bones` and carries none beneath it, so the climb
    from `plugRoot` lands where it did before.
  - **F19/F20, the shader surface.** The patcher's regexes are unanchored and comment-blind, and
    the socket decoder admits a coloured light whose alpha is zero (`&& colour.a > 0`), which also
    defeats the self-exclusion test that consumes its verdict. Held back because a shader change
    cannot be judged from this machine; see the body-mesh section for what that cost on 2026-08-24.

- **A rebuild refreshes the shader now, not just the bake: FIXED 2026-08-24.** Every shader-level
  fix used to reach nobody who had already converted. `BakeSocket` and `Bake` took a refresh
  branch whenever the material carried `_YAPS_Bake` and never called `Patch` again, and the patch
  was named `Hash(sourcePath + Revision + unit.Count)`: a hand-written `Revision` constant that
  did not move when the emitted code did. Found by adding a property and watching it never arrive:
  the material's shader had nine copies of a property added that morning and none of one added
  that evening, because the morning's landed on a FIRST patch and the evening's needed a second.
  The constant was forgotten twice in one day, which is the argument against constants like it.
  Now the name hashes the emitted source itself (`EmittedVersion`), so it moves exactly when the
  code moves, and both bake paths ask `IsStale` and re-patch when it has fallen behind. A material
  keeps its values across a shader swap, and a property the old code never had arrives at its
  declared default, which the bake then sets.

  **The original is recovered from the PATCH, not from the component.** The first version asked
  `socket.bakedFrom`, which only the tool's own bake ever fills in: a CONVERTED avatar adopts its
  components and leaves it null, so the check would have done nothing for the majority of avatars
  and looked like it worked. `_YAPS_SourceShader` already carried the source shader's name in a
  hidden property's description, so `OriginalShaderOf` reads it back and `Refresh` re-patches
  through a stand-in material. Verified in the editor: a socket material's shader moved from
  `56f6502ef13e` to `253145a5a99b` on a rebuild and gained the property it had been missing.

- **Corpus classes: CLOSED 2026-08-22**: Fixture_DeformSocket and Fixture_HeadTransplant are in
  the corpus and its baseline; the transplant fixture came out on the real Head with zero errors.
- **Pointer capping: CLOSED 2026-08-24, declined with the measurement.** Two questions, both
  answered without a run. *Which families does anything read?* A census over the 93 corpus files
  carrying contact receivers, senders split from receivers: `TPS_Orf_Root`/`SPSLL_Socket_Root`
  heard in 4 files, `SPSLL_Socket_Hole` 3, `Ring` 2, twins alongside, and the front pair,
  `TPS_Orf_Norm`/`SPSLL_Socket_Front`, heard by NOTHING. That looked like four dead pointers a
  socket until `YapsPropBuilder.FrontTypes` turned out to be exactly that pair: it is the prop
  channel's FX/FY/FZ front axis. No corpus avatar hears it because no corpus avatar carries a
  prop, and the corpus enumerates scenes while props are spawnables. This tool's own system is the
  consumer; capping the family would silently cost props their axis.
  *Should the COUNT be capped like marker lights?* No, the limits are not alike. Lights are four
  slots a MESH and a fifth evicts the lowest range, which is why the lighthouse had to exist.
  Pointers are 512 overlapping PAIRS instance-wide, and a pointer costs nothing until it is
  inside a receiver: 174 idle pointers standing in a room spend none of the budget. A count cap
  would break sockets in the common case to save a resource nobody is spending. If pair
  exhaustion ever appears in the wild, the lever is the overlap, not the socket's description of
  itself.
- **DPS range offset, LIFTED 2026-09-08**: the +0.003 offset made YAPS sockets and plugs
  invisible to every mod decoding at 0.001, sound mods included: NAK's PlapPlapForAll needs
  `RoundToInt(Repeat(range*500+500,50)+200)` to hit 205/210/225/245, and the 0.4130/0.4230/0.4530/
  0.4930 all landed on x.5 and read Invalid. No range separates a sound mod from a toy mod, so it
  was one choice for both. The condition set on 2026-08-28 was "the PR merges AND users can take
  it"; CVRGoesBrrr's merge is in the latest build, so the ranges are now VRCFury's exact
  0.4106/0.4206/0.4506/0.4906. The bystander case the offset answered is what the merge closes,
  by bounding reach with the plug's stated length rather than a guess from the first renderer.
  `OwnPlugRestsOn` widened from an exact match to the protocol's own 0.001 in the same commit, so
  a wearer's plain DPS plug counts toward self-exclusion the way a YAPS one does.
  **NOT YET TESTED IN GAME.** Soba is taking the merged mod build against a rebuilt avatar; until
  that reads back, this is a change made on source, not on evidence. Anyone on a toy mod build
  from before mid-2026 keeps the old across-a-room reach, which is a property of the mod and not
  of the avatar.
- **Consolidation remainder**: items after 5 in `archive/Consolidation.md`'s order, minus 6,
  which was skipped on purpose.

---

## The preview tells the truth about the game
*Status: partial. The honest window shipped in 4.3.0; the game-accurate resolver is unstarted.*

The setup window's preview bends a plug toward a socket by reading transforms. The GAME needs the
socket's pointers, marker lights and receivers switched on. So a socket can preview perfectly and
do nothing in game, and it did: pointers visible in CVR's own debug view, no bend, and the tool
saying the socket was fine throughout.

4.3.0 makes the window HONEST: each socket row says which of the three is dark and what it
costs. That closes the lie. It does not close the gap.

The gap worth closing is a preview that resolves a socket the way the game does: pointer and
trigger overlap by geometry, the enabled and active state of both, the depth it would publish,
the parameters it would drive. Then "it previews" and "it works" are the same sentence, and this
entire class of report, works in editor, dead in game, stops existing.

Wants measuring first: how much of CVR's contact resolution has to be reproduced before the
answer is trustworthy. A preview that is right most of the time is worse than one that is
honest about being a preview.

**One member of this class is FIXED 2026-08-25: the squish.** A plug worn in game was
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

## YAPS 5: the plug follows a path, not a point
*Status: the ordered list is BUILT and shipped in 4.6.0, and so is deepest-plug-wins on the
shader route. Portal, duplicate and deepest-plug-wins on the animator route are not.*

*The other half of YAPS 5, how a plug FINDS a socket, lives in `YAPS5.md`, which gathers every
transport, every measured limit, and the order they get built in. This section is what the plug
does once found.*

**This entry said "planned, unstarted" until 2026-09-12, months after the thing it asks for
shipped.** The ask was an ordered list of sockets with arc-length ranges instead of one socket,
and that is `YapsChain` in `yaps_resolve.cginc`: `position`, `forward`, `kind` and a cumulative
`arc`, filled nearest-first, a hole ending the path, with `yaps_deform.cginc` selecting the link
per vertex by how far along the shaft it sits. Joe caught the stale status by knowing the feature
existed. A status line nobody rereads is worse than no status line, because it is the one thing
a planning read trusts.

| want | how it falls out | state |
|---|---|---|
| a ring mid-shaft *and* a hole at the tip | two entries in the list | BUILT, shipped 4.6.0, untested in game across two bodies |
| portal: in at one socket, out of another | two entries with a gap between their ranges | NOT built: the cascade is continuous, one Bézier per consecutive pair, and nothing can express a gap |
| duplicate: the shaft showing in two places | the same range mapped twice | NOT built |

A vertex shader cannot create geometry, so a "duplicate" is the existing shaft drawn at two
frames rather than a second shaft. It costs nothing and it cannot be two different lengths.

**Multiple plugs per socket is a separate, much smaller job.** The bending already works:
every plug resolves sockets in its own shader, so two plugs into one socket needs no change
at all. What breaks is the socket's *shapes*: a ChilloutVR trigger writes from whichever
sender touched it last, so two plugs fight over the depth instead of the deepest winning.
Socket-side, no extra sync, and it can ship on its own.

**What the platform gives and charges for.** Unity hands a mesh four vertex-light slots and a
socket takes two, but the third slot is spoken for by the tracker light of whatever enters the
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

**Order to build it in, with what has happened to it since:** deepest-plug-wins first (small,
self-contained) is done where the socket's shapes ride the shader, `yaps_socket.cginc:102`, and
still open where they ride the animator, because a ChilloutVR trigger takes the last sender rather
than the deepest. The socket list is built and shipped. Portal and duplicate as ranges on top
remain, and the cascade would have to learn to express a gap first. The converter repointing an
author's reactions onto YAPS's own depth, once last on this list, shipped in 4.3.0 with the
rebuild.

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

**An upload preflight.** One list, run before uploading: these diagnostics, the CCK's own
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
| `LocIdle / LocWalking* / LocRunning*` | already done, by LocomotionGrafter | - |

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
hardest to see because nothing throws: the avatar simply behaves slightly wrongly.

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
*Status: reference: measured facts, kept for design work; the distilled limits are in `YAPS5.md`.*

Added 2026-08-17, after a user reported their controllers vibrating whenever they came within
two metres of a converted avatar. Everything here was learned from source, and it changes what a
socket is allowed to be.

**A marker light is the only thing an avatar does that reaches into someone else's.** Its range
is a message: every decoder reads `range % 0.1` and matches 0.01 hole, 0.02 ring, 0.05 front,
0.09 plug tip, and anything on the platform may listen. Raliv's shader matches within 0.005; a
toy mod reading the same protocol in C# matches within 0.001. VRCFury authors +0.0006, inside
both, which is why a stock converted avatar drove a stranger's toy across a room. YAPS authored
+0.003 from 2026-08-23, which DPS read and that mod did not; that lifted on 2026-09-08, see the
work queue entry.

**Toy integration cannot be made safe from the socket side, and that is not a bug here.** The mod
computes reach as `1 - distance / giver.Length`, and estimates `Length` from whichever renderer
sits first under the avatar root rather than reading the length DPS states in the tip light's
intensity. A socket is only ever the target of somebody else's number. So:

- **plug side, one day: yes.** A plug can declare its own length honestly, by giving the tracker
  light a sibling whose mesh states the measured length. The mod climbs to the first renderer
  under the light, so an inactive one on the marker object is enough, and then it engages within
  a plug length rather than a room. That is a switch that risks only the wearer.
- **socket side: never.** Any "let toy mods read my sockets" control hands strangers the right
  to decide how far away they can reach you.

[ddakebono/CVRGoesBrrr#2](https://github.com/ddakebono/CVRGoesBrrr/pull/2) bounds the estimate
with the stated length, and it is in the mod's latest build, which is what allowed the offset to
lift on 2026-09-08. Both notes above still hold for the plug side: declaring length honestly is
the wearer's own risk, and a "let toy mods read my sockets" control still hands strangers the
decision, so there is none. Builds of the mod from before the merge stay broken either way.

**A tempting idea that does not work: shrinking the ranges.** Since only `range % 0.1` is read,
0.0106 decodes exactly like 0.4106 and reaches a fortieth as far. It would ease vertex-slot
pressure, but Unity ranks the four per-object light slots BY RANGE, so a tiny light is the
first evicted by every stock DPS avatar in the room. It would work alone and fail in company,
which is the opposite of what a compatibility feature needs. Worth keeping only as a "YAPS talks
to YAPS and nothing else" mode, where this decoder sets the rules.

---

## Two walls the transports hit, and whether a shader goes round them
*Status: the light-slot fix landed, and the authority-gate wall is DISPROVEN in game 2026-08-22: cross-avatar prop writes work, see `YAPS5.md`. The 512-pair cap stands.*

Read out of the client 2026-08-22 while costing a contact-based replacement for the marker-light
channel. Both of these are platform behaviour, not this tool's code, and both bound what any redesign can
achieve.

**A prop can only be driven by your own avatar.** Every contact write into a `CVRSpawnable` value
goes through `TriggerToContact.HasProbableAuthorityToApplySync`. It allows a sender that is
another prop (if either is synced by you), a sender that is *your own* avatar, or the world. There
is **no branch for another player's avatar**, so a remote sender falls through and returns false
and the value is silently never written. This is very likely a client bug rather than policy: the
prop branch falls back to `IsSyncedByMe()`, and that fallback is simply missing for avatar
senders, so even the prop's own syncer is refused. It predicts the split chased here:
avatar-to-avatar works (the gate only runs for spawnable triggers), old DPS props work (lights,
never this path), YAPS props work on your own sockets and never on someone else's. **Test before
building anything around it, and report it upstream if it holds.**

**512 overlapping contact pairs, instance-wide.** `CollectPairsJob` caps `pairs` at 512 and re-adds
*previous* pairs first, so an established interaction is sticky and cannot be evicted, but in a
saturated instance a new one may never register: "works once I am in it, will not start in a
crowd". The broadphase itself is brute force over every sender for every receiver, but Burst and
parallel with a squared-distance test, so it is sub-millisecond and not the constraint. Rejections
on tags, `contentType` and owner happen *before* a pair is written, so **tags are the lever on the
512, not volume count.**

### So: is there shader magic?

Two routes, and the cheap one is much more interesting than the famous one.

**The light colour is free, and nobody is using it.** A vertex light hands the shader
`unity_LightColor` alongside its position and range. These markers are black with non-zero intensity
(zero intensity drops a light from the per-object list entirely), so three channels per light are
sitting unused. If a root light's colour carried the socket's axis, a YAPS-native socket would
need **one** light instead of two, and with the tracker holding a slot, that is three sockets in
the budget instead of one. Legacy plugs still need the root+front pair, so this is a YAPS-native
mode alongside the compatibility pair, not a replacement. Cheap to spike, and the open questions
are small: whether CVR's asset filter clamps light colour or intensity on avatars, and how much
precision survives the intensity multiply.

**SPIKED 2026-08-27 IN PLAY MODE AND IT WORKS.** A quad writing a known value into a fixed corner
of clip space, a named `GrabPass`, and a second quad sampling it back: the value returns exactly.
The grab comes back **ARGBHalf**, 16 bits of float per channel, worst error measured at 0.00005,
which across a two metre range is about **0.1 mm**. That is twelve times finer than the contact
channel, arrives every frame rather than ten times a second, and needs no smoothing, so none of
the resolution-versus-lag trade above applies to it at all. `Assets/YapsSpike/` and
`Assets/Editor/SpikeAtlas.cs` in the Dracaionan project.

Writing the patch in clip space, ignoring the object's transform and the eye, makes it
stereo-proof by construction: both slices of the eye texture array get identical content, so
there is no double-wide layout maths to get wrong. This is the part that does not survive
conversion from VRChat, and it is simply not available here.

**Still unanswered, in order:** whether ChilloutVR's asset filter keeps a `GrabPass` through an
upload (only an upload can say); the per-frame cost of a named grab in VR; whether the patch
escapes frustum culling when the socket is off screen (scaling the quad enormously works, since
the vertex shader ignores its position and only the bounds change); and cell collisions.

**A possible answer to collisions, untested:** give each socket a build-time random id, hash it to
a cell, and write the id into the cell beside the position. A colliding cell then decodes to some
other socket's position, which is almost always metres away, and the engagement range gate already
rejects that. Collisions would degrade to "no socket found", falling back to lights or contacts,
rather than "wrong socket found".

**Original note, written before the spike:**

**The screen-space atlas is real, and it could be written better than SPS, but do not build it
yet.** Sockets render a small quad encoding their world position into a reserved screen region;
the plug samples it back through a named GrabPass. It is the only channel that dodges *both* walls
above: no light slots, no contact pairs, no parameters, so no authority gate and no sync bits, and
no cap on socket count. And the reason SPS's does not survive conversion is one that need not apply here: theirs is written for VRChat's double-wide, this would be instanced-native from the
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
*Status: blocked: access declined 2026-08-19.*

Read from the local client on 2026-08-17. **That install is the `public-scripting` beta**, and the
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
protocol they speak*, Raliv's lights, TPS and SPS pointers, YAPS markers, because it reads the
components rather than waiting for Unity to hand it four light slots or for a contact pair to
survive the budget. Everything on the platform becomes findable, including content that will
never be converted and whose authors are long gone. That is what "force everything over to YAPS"
can actually mean: not converting other people's avatars, but understanding them.

**It stays a tier, not a replacement.** These sockets must keep emitting lights and pointers, since
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
So this is moving rather than hypothetical, and the scripting branch is already installed here, which
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

## The socket preview stands down in Play Mode without saying so

`YapsSocket.PreviewTick` returns at `if (Application.isPlaying) return;`. That is correct:
in Play Mode the game systems own the property blocks and an editor preview writing over
them would fight whatever drives the material. But it stands down in total silence, and a
preview that writes nothing is indistinguishable from a feature that does not work.

This cost a full evening of diagnosis on 2026-08-26. Every reading taken in Play Mode showed
an empty block, which decodes to the far corner of the channel box, which looks exactly like
a bad encode. Out of Play Mode the same scene round-tripped to within 0 m.

**Fix:** grey the preview toggle out in Play Mode, or put a line under it reading that the
preview is inactive while playing. The user must not have to infer it.

## `YapsSocket.preview` does not survive a recompile

It is `[NonSerialized]`, so a domain reload drops it to false while the button can still read
as active until it repaints. Any script edit silently stops a running preview. Either serialise
it, or have the socket editor re-assert it after a reload.

## The bend direction is ill-conditioned when a socket lines up with the plug's axis

Noticed 2026-08-27 while testing the above. With the socket dead on the plug's own axis, X and Y
both at the centre of the channel box, a sub-millimetre wobble makes the deform thrash: there is
no preferred side to bend toward, so the direction swings. Move the socket off-axis and it is
steady, and at long range it does not show because the plug is nearly straight anyway.

No amount of smoothing the POSITION fixes an unstable DIRECTION, so this is separate from the
resolution limit above. How much it matters depends on how often a real socket sits within a
millimetre of the axis, which is probably rare. Worth remembering when someone reports that a
plug "freaks out sometimes" with no other pattern to it.

## User reports from stable, 2026-08-27

Both from a user on the shipped build. Neither is reproduced here yet, and
neither should be closed on the reasoning below alone.

### Duplicate SPS toggles after conversion

The conversion lists the avatar's SPS toggles, and converting to YAPS adds a SECOND set, so the
menu carries two of each and one of them is inert.

**The first hypothesis here was wrong and is corrected.** It said `ToggledBy` only recognises an
entry driving the object through `gameObjectTargets`, so a clip-driven SPS toggle would slip past.
Reading it properly: it checks `gameObjectTargets`, the entry's own on and off animation clips,
dropdown options by both routes, AND every clip in the avatar's controller for anything hiding the
target or any of its parents. Only clips under YAPS's own output folder are skipped, so a
converted toggle's clips in `RehomedAssets` stay visible. Ordering is not it either: `Parameters
and menu` and `Animator merge` are passes 128 and 131, and YAPS is 146, so the entries and clips
exist by the time it looks.

**What YAPS does is defer, not replace.** If anything already switches the object it adds no
toggle, and it removes one an earlier build added that turned out to be redundant. So a duplicate
means `ToggledBy` returned null on an object something demonstrably switches, and no reading of
the code so far explains why.

**Cause unknown.** `Dev/Probes/DuplicateToggleCheck.cs` finds every object under more than one menu
entry and labels each by route, targets or clip, across the whole corpus in one Unity session. Run
it and let the answer come from an avatar rather than from me reading. It cannot run while a
corpus run holds the same project.

**To confirm before fixing:** take an avatar whose SPS toggle is clip-driven, convert it, and look
at whether the duplicate entry appears and whether `ToggledBy` returned null. The corpus has SPS
avatars in it; a probe counting menu entries that target the same object would find this across
all of them at once, which is better than one reproduction.

## The prop channel is a second implementation, and fixes land in one of them

*RESOLVED BY REMOVAL in 4.5.1: the plug no longer reads the contact channel, and `YapsChannel` is emptied on dev.*

Found 2026-08-27 while auditing the README. `YapsChannel` builds the contact channel for avatars
and `YapsPropBuilder.BuildChannel` builds it for props, and they are separate code that does the
same job with different component types, `CVRAdvancedAvatarSettingsTrigger` against
`CVRSpawnableTrigger`. So a fix goes into whichever one was being debugged.

Three had never crossed:

- The trigger halving. `box * 0.5f` on the engagement and hole triggers, fixed for avatars in
  `53e293c` and still live on props, so a prop's engagement volume was half what it should be
  while its axis triggers beside it were full size. Exactly the mismatch avatars had.
- No exit task on the axis triggers, so a prop's reported position stuck at the edge of the box
  after a socket left.
- No ring trigger, so the hole flag could only ever be set: a ring arriving after a hole was
  treated as a hole for as long as the prop lived.

All three are across now, but the shape of the problem is not fixed. **Two implementations of one
protocol will keep drifting**, and nothing compares them. Worth either a shared builder that emits
whichever trigger type it is handed, or a test that builds both and asserts the same box sizes,
tasks and tag lists come out.

**And the corpus cannot catch it.** It converts avatars; nothing in it builds a prop, so the prop
path has no regression cover at all. That is a separate gap from "nothing revisits an existing
prop", already recorded above, and worse: this one means the code is untested, not just that
users hold stale copies.

## SPS2 shipped, and the converter cannot see it

Published 2026-09-03 at `https://vrcfury.com/sps/`. It transports deformation through grab
passes, two per world rather than two per item, and its own notes say deformation "no longer
depends on socket lights or plug contacts". Legacy compatibility keeps the lights so it can still
meet SPS1, DPS and TPS; depth animations and some haptics still ride contacts. Each plug and each
socket costs two extra material slots. Supported shaders are a wide list: Poiyomi 7/8+, lilToon,
UTS, Mochie, XSToon, Silent, Standard, `Unity/Color`, a mobile particle shader and some Shader
Graph. The page does not publish the encoding, so there is nothing to read from it about cell
layout, hashing, or what the second grab pass carries.

**The problem for the converter is the switch, not detection. CORRECTED 2026-09-13.** This said
`YapsLegacyMap.Detect` would miss SPS2's new material properties and nothing would switch SPS2's
deform off. The converter does not rely on detection for that: `YapsBakePrep` turns Fury's plug
deform off on the component before Fury bakes, so what matters is that the component's switch
still exists. It does on VRCFury 1.1408.0, and a missing switch now warns. Next up item 6 has the
evidence, the ceiling, and the clean-room question still open for a property signature.

Still wanted, if SPS2 ever needs carrying rather than just not fighting: a carry map for whatever
its knobs are called, which does need a real SPS2 bake to read.

One thing it confirms rather than threatens: "The plug mesh must be straight / fully extended in
the editor" is their constraint too. The bake measures a rest pose because the technique requires
it, not because this one is weak.

### The feature gap against SPS2, DPS and TPS, read 2026-09-08

SPS2 from `docs/60-sps/index.mdx` in the vrcfury.com repository; DPS and TPS from this repository,
since the GUI already tags each knob with the system it came from and `YapsLegacyMap` records what
the converter cannot carry. Nothing here is decided. It is a list to choose from on evidence
rather than on a memory of what these systems looked like a year ago.

**1. Tags, and this is the hole, not a nice-to-have. IN PROGRESS 2026-09-08.**

DPS had channels in its day: `_OrificeChannel`, so a plug only answered orifices on a matching
channel, and the converter records it as unmapped to this day. SPS2 has tags: up to two custom
tags on a socket plus a location set derived automatically (hips, head, chest, hand, handleft,
handright, foot, footleft, footright, hipsfront, hipsback), and up to two include and two exclude
tags on a plug, each side taking `Self` and `Others` modifiers, changeable by an animation.

**Correction, 2026-09-08.** This entry said flatly that YAPS has none, and that was wrong.
`_YAPS_TagInclude` and `_YAPS_TagExclude` have been declared and read since before the atlas: one
hashed integer a side, compared for equality, on the CHANNEL tier only. Nothing in C# ever wrote
them, so the effect was the same as having none, but the shape was already there and the
correction matters because the fix was smaller than the entry implied. Read the code before
writing down what a system lacks. The switch added on 2026-09-08 for whether a plug answers its wearer's own
sockets is a one-bit stand-in for it, not a small feature that might grow into it later.

It also settles how the per-socket allow list dropped the same morning should have been built. A
tag lives on the socket and costs nothing to sync, where a per-socket list costs a bit each and
fills the wearer's menu with rows nobody opens. If that idea is ever reopened, it is reopened as
tags.

**What is built, 2026-09-08.** Protocol version 3. A socket carries a 15-bit `YapsTags` set and
publishes it in a THIRD atlas pixel, five bits a channel over rgb: the facing pixel's alpha
already holds the kind and the position pixel's holds the owner tag the whole read is gated on,
so there was nowhere else for it. That takes a cell from 17 slots to 25 and the rect from 552 by
520 to 808 by 520.

**The rect is the risk in this, and it is not yet paid.** A target that cannot hold the rect
fails `YapsAtlasFits` and drops to the light tier silently, which is the exact failure mode that
made the mirror look broken for two days. The seven cameras A1 settled grid 64 against have to be
walked again at the new width: editor, desktop first and third person, desktop mirror, VR both
eyes, VR mirror, personal mirror. Until that is done the tag work must not ship, and a width that
fails is a reason to go back to a single tag index in the byte that was already free rather than
to shrink the grid.

*Protocol 5, 2026-09-11, moved the target again: an owner pixel per octant, 28 columns, 932 by
596. The walk above now has to be done at that size.*

The plug carries an include set and an exclude set, both plain floats so an animation can drive
them. One rule tests them, `YapsTagsRefuse`, called from the channel branch and the atlas branch
alike: refuse on any bit shared with the exclude set, require any bit shared with the include set,
and an empty include set means no opinion. Two tiers cannot drift apart because there is one
function.

**A marker light can never carry a tag** and that is not a gap to close: a light's RANGE is its
whole message and the digits are Raliv's. So a socket found by light reads as untagged, and
untagged is refused only by a REQUIRE. That is the honest reading of content older than tags,
and it is what SPS does with legacy content too.

**But a REFUSED socket is not an unknown one, and that half is fixed, 2026-09-08.** The light
fallback engages on distance once nothing else has, so a socket the atlas had just read and
turned down was picked straight back up by its own marker light, at contact range, which is the
one range a refuse list is written for. `YapsResolveChain` now carries the nearest refused socket
out with it, and a light answer within a tenth of a length of that position is undone: engagement
back to zero, tier back to nobody. Only the light tier, and only where the atlas actually read
the socket. A tenth of a length so it takes the same socket and not its neighbour; deliberately
tight, since too loose refuses something nobody named.

Untested in game. The failure mode if the distance is wrong in either direction is quiet: too
tight is the old behaviour, too loose is a neighbouring socket going dark.

**The inspectors and the menu went in the same day.** The socket's set sits in "What it is"
beside its kind; the plug's two sets open a fold of their own, chipped DPS and SPS since both
had the idea first. A plug listing two or more tags gets a dropdown and an animator layer, one
state per tag plus "As built" and "Anything", driving `material._YAPS_TagInclude`. Fifteen
toggles would have been fifteen rows and fifteen parameters for a question with one answer at a
time, which is the objection that killed the per-socket allow list.

*WITHDRAWN 2026-09-23: measured wrong. `material._X` reaches every slot and `material[1]._X`
binds to nothing, so the fix below broke the toggles it meant to mend; see "Menu toggles did
nothing on a plug whose material was not its mesh's first" under Loose ends.*
**A multi-slot plug needed care and turned up a real bug elsewhere, since fixed.** A curve on
`material._X` binds to the FIRST material only. The tag clips were written that way from the
start, but the deform toggle and the own-body toggle were not: a plug with its tip on a second
material had half its mesh switched off and the other half left running, in the row people
actually use. Fixed by moving the slot walk into `YapsToggles.Clip`, which all three now share,
so the next property to be animated cannot get it wrong again.

The matching half of the bug was in the reading. `material[1]._X` is a different string from
`material._X`, and every place hunting for a toggle compared against the second: the toolkit
could not find a toggle it had built on a second slot, so it built another, and Remove walked
past the curves and left them behind. `YapsToggles.Bare` strips the index and `Writes` compares
through it, and `YapsRemover` reads its wired list the same way.

Where no slot declares the property at all, slot 0 is still written, so a plug whose material is
assigned after the clip behaves as it did before.

**The channel tier does not filter, and that is now written down rather than broken.** The shader
read the socket's set out of `_YAPS_SocketFlags.z`, which nothing has ever written: every channel
socket read as untagged, so a plug with any include list refused the entire contact tier and a
plug tagged for one place stopped resolving at all on an avatar with no atlas. Fail-closed, from
a field with a default rather than from a branch.

Unknown is not untagged. The test is gone from that branch: a tier that cannot see the set can
prove neither a match nor a refusal, so it claims neither. Nothing was weakened by removing it,
since an exclude never fired there either, a set of zero matching no exclude.

**A "pointer suffix transport" was proposed here on 2026-09-08 and does not exist.** Read the
correction before acting on any memory of it. The reasoning was that `YapsScanner.IsSocketRootTag`
matches `SPSLL_Socket_Root_` by prefix, so SPS must put tags after that underscore. It does not.
`_SelfNotOnHips` is the only suffix SPS ever emits, `HapticUtils` declares the contact type
strings as four fixed constants, and nothing in the bake appends a tag to any of them.

**What SPS actually does, read out of `SpsConfigurer.cs` rather than inferred.** A tag is a
free-form string, lowercased and trimmed, hashed with FNV-1a to 32 bits, and written into MATERIAL
PROPERTIES: eight socket slots as `_SPS_SocketTag{1..8}Low`/`High`, sixteen bits each half, and
four plug slots as `_SPS_TagInclude{n}Low`/`High` carrying self and others flags. Two of the eight
socket slots are the author's, two are derived from the nearest humanoid bone, one is a shared
tag. The plug reads them through SPS's own shader data channel.

So tags never touch contacts in SPS either. A material property is the same CLASS of channel as
the YAPS atlas, and the contact tier not filtering is what SPS does too, rather than a shortfall.
That closes the question above: there is nothing to wire, and the current behaviour is right.

**The encoding is now SPS's, as of 2026-09-08.** A tag is a free-form string, trimmed and
lowercased, hashed FNV-1a to 32 bits with SPS's constants, so a name means the same thing on both
tools. The fifteen-value enum is gone and the four custom slots with it.

**What is NOT SPS's is where the hash goes, and that was forced.** SPS gives a socket eight slots
and writes each 32-bit hash whole into material properties. The atlas has no room: one pixel per
field per octant, every extra pixel costing 256 pixels of width, so eight slots take the rect from
808 to 2600 and no mirror resolves at all. Even one extra pixel makes it 1064, and 808 is already
the number the fit test is meant to settle.

So a socket's tags FOLD into the one pixel already there. Each tag lights three distinct bits of
twenty, chosen by its hash, and the socket publishes the OR; a plug carries up to four patterns a
side in two Vector4s and asks whether all three of a tag's bits are lit. The plug's side costs the
atlas nothing, being its own uniforms.

**The fold has a false-yes rate and the direction was chosen.** A socket wearing three tags lights
about eight bits of twenty, so an unrelated tag reads as present about one time in twenty. On the
answer list that answers a socket that was not asking, which is the permissiveness the feature
already documents everywhere but the atlas; on the refuse list it refuses one it need not have,
which errs toward not touching. Neither invents a socket.

**Two generations of the pattern generator were wrong and a review caught both.** The first took
successive remainders of one hash, which let a tag repeat a bit and light only two; "footleft" lit
two, both inside "handright", so a hand socket read as a foot socket. Distinct bits fixed that and
introduced the second: `h = h * 16777619 + 2166136261` is congruent to `3h + 1` modulo 4, so the
index alternates between two residue classes and only 200 of the 1140 three-bit patterns exist.
"tag1" and "tag13" were the same tag outright. A multiply-and-add is not its own avalanche;
murmur3's finaliser is, and reaches all 1140.

Worth recording the direction: fixing it made the eleven names' three-tag false rate go UP, from
about 1.5% to 5.4%. The old space was small enough to be lucky on eleven cherry-picked names while
being catastrophic on arbitrary ones. 5.4% is the honest number for a fold this size.

`Dev/Probes/YapsTagProbe.cs` checks the eleven hashes against values read out of VRCFury, that no
name reads as present on a socket wearing only another, that all 1140 patterns are reachable over
ten thousand arbitrary names, and that the three-tag union rate has not moved. Run it after
touching `YapsTags`.

**One normalisation, shared.** The bake dropped blanks and the menu also deduplicated, so
"hips, HIPS, head, hand, foot" made the bake spend a slot on the repeat and lose "foot" while the
menu kept it, and entering the controller's default state changed which sockets the plug answered
with nobody choosing anything. `YapsTags.Listed` is the one implementation now.

**The atlas protocol is version 4.** Same pixel count, same rect, different meaning in the third
pixel: an old untagged socket wrote alpha 1, which the new decoder reads as five bits set rather
than none, so a pattern living in those bits would match a socket that had no tags at all. Only
mixed builds of unreleased version-3 work are affected, and the version is what refuses them.
**The converter carries them now, 2026-09-08.** The words are read off the VRCFury components in
`YapsBakePrep`, beside the overrun flag and for the same reason: SPS writes tags as 32-bit hashes
into a generated animation clip, so the baked avatar has numbers and a hash cannot be turned back
into a word. Sockets travel to the rebuild in `Spec.Tags`, because the rebuild compiles without
the VRChat SDK and must not reference the prep; plugs are written straight into the patched
material beside the overrun.

The bone-derived names are derived here too, by CLIMBING to the bone a socket hangs off rather
than measuring to the nearest one: a distance answers wrongly the moment two bones sit close.
Hips front and back only when there are exactly two hip sockets, which is SPS's own rule; with
three, naming a front socket "hipsback" would make a plug refuse the wrong one.

**All three paths say when a list was too long, 2026-09-08.** Four on each list is what the bake
holds, and the fifth used to go without a word on two of the three ways in: the converter named
the words it carried and never the ones it did not, and the tag menu capped the list in `Listed`
and built four rows. Only the native builder said so. The same miss in three places is the shape
this codebase keeps producing: a rule gets a shared helper, and the SPEAKING about it stays local
to whichever path was open at the time. `YapsTags.Dropped` is now the one counter and each path
reports it. The inspector still accepts a fifth entry rather than refusing the keystroke, which is
deliberate: the author may be moving words around, and the build is where the truth is told.

**The toolkit had no such convention, and that was the hole, 2026-09-08.** Carrying the shared word
across from SPS keeps a converted plug working on a converted avatar, and says nothing about the
sockets this tool builds itself. Those defaulted to an empty tag list, which publishes an empty
word, and a plug with any answer list refuses a word that does not match. So every plug converted
from SPS, which is every one of them since it always carries the shared word, refused every socket
the toolkit ever built and every prop, on the atlas tier. The light and contact tiers never ask
about tags, which is what kept it from being obvious.

The fix is the convention, not the rule: `YapsSocket.tags` starts with the shared word, so a socket
built here says the same thing an SPS socket says. A CONVERTED socket is still whatever SPS said,
empty list included, because an author who turned the shared tag off there meant it: the rebuild
assigns the list always rather than only when it has something in it, or the component default
would quietly overrule them.

No migration: the tag field has never shipped, so no socket in anyone's project carries the old
empty default.

Third builder, same lesson. The converter and the native builder were the two that got compared;
props go through the socket builder and were not in the comparison at all.

**Carrying the shared tag is what makes it safe, and it was nearly missed.** Nearly every SPS
socket and plug carries the global tag, and a plug that has it answers everything regardless of
its own list. Carrying a plug's include list WITHOUT carrying that would have made every
converted plug with a rule refuse every socket on the avatar: a shipped regression out of a
feature meant to add fidelity. The shared tag is a fixed number in SPS rather than a hashed word;
it travels here as the word "shared", since a VRChat plug and a ChilloutVR socket never meet and
only the behaviour has to carry.

What does not survive: the per-tag self and others modifiers, since a plug has one answer for
both. Reported rather than silent.

**Distinct entries past four are now counted, not dropped in silence** (the residue of H2, which
a reviewer was right to call out as an overstated "all fixed"). `YapsTags.Dropped` counts them and
the plug build says how many it left out; the tooltips and the README say four is the room. The
inspector still accepts a fifth, which is the honest place to stop: a custom list drawer to
prevent typing one is a UI project, and the bake now tells the truth either way.

**Still owed**: the rect, above, which is the one that gates shipping. No converted avatar has
been through this yet: the corpus has not run since it landed, and nothing has been tested in
game.

**2. Not a gap, and worth saying before the rest.**

The SPS2 transport is a grab pass costing two per world rather than two per item, which is the
conclusion the screen atlas reached from the other side. Hole and ring, the overrun switch, an
animated toggle for the deform and the rest-pose requirement all have equivalents here. Their
guided path allows three stops with adjustable tangents; the chain holds four links. Same features,
different names.

From DPS and TPS, nearly everything is already carried: curvature, recurvature, entrance stiffness,
wriggle and the socket blendshape staging came from DPS; squeeze, bulge, idle shrink, pumping, the
bezier bend, smooth start and minimum socket distance came from TPS.

**3. Small and mechanical.**

- **Ring sidedness. DONE 2026-09-11 on dev, protocol 7.** A socket's **One way** tick: the atlas
  kind becomes ring, hole or one-way ring, and a plug whose base is behind a one-way ring's front
  passes it by. Lights cannot carry it. Not carried from TPS: `_TPS_TwoSidedRings` sits on the
  PLUG there, and a plug-side rule would be a second control for the same question.
- **Idle gravity. WITHDRAWN 2026-09-11: TPS has no such setting.** Poiyomi declares
  `_TPS_IdleGravity` as a `[Helpbox]` whose text tells the author to use PhysBone gravity for idle
  droop, which the converter already carries. All 1524 materials on this machine hold 0. The
  entry came from the property's name in the unmapped list, and the converter no longer lists it.
  Was: recorded as unmapped; there is idle shrink and there is wriggle, but nothing that hangs.
- **Radius offset. DECLINED 2026-09-11.** A YAPS socket is an object the author places, so moving
  it IS the offset; SPS2 needs a field because it snaps sockets to bones. Revisit only if a
  converter needs somewhere to put SPS2's own value. Was: a per-socket nudge so the opening sits
  on the surface rather than in it,
  which SPS2's notes single out as mattering most for hand sockets.
- **Auto-rig.** Bones and physbones added to a static plug mesh at build time. This tool converts
  what an avatar already has and has never built that.
- **Bulge falloff. DONE 2026-09-11 on dev.** TPS's bulge rises over its distance, peaks the
  falloff short of the opening and is gone at it; YAPS had one even hump over the reach.
  `_YAPS_BulgeFalloff` (plug **Bulge falloff**, 0 to 0.5 of length) gives the TPS shape, with
  smooth ramps where TPS has linear ones, and 0 keeps the even hump, so every plug built before
  it is unchanged. `_TPS_BuldgeFalloffDistance` carries straight across, usually 0.05, so a
  converted TPS plug's bulge moves nearer the opening. Untested in game.

One unmapped property is deliberately not on this list: `_BlendshapeBadScaleFix` from DPS, because
scale is read live here and there is nothing to fix.

**4. A feature in its own right: depth animations. BUILT 2026-09-11 on dev, on the contact.**

A socket's **Plays as a plug goes in** list: clips, each with its own depth range, in a layer
"YAPS <label> animations" (a direct tree of one 1D tree per clip, write defaults on), driven by the
same trigger and synced depth as the reactions layer. Remove takes it out. Untested in game. The
per-client GPU route below is still open, and still waits on the bridge.

How far a plug has entered a socket drives an animation. This is the mechanism behind the arousal
systems people ask about, where touching yourself changes a blendshape over time; those are built
by avatar authors on top of it and are not themselves part of SPS.

Their depth animations still run on contacts even in SPS2, which their own page says plainly. That
is the interesting sentence, because contacts are exactly what the GPU bridge removes: a depth
animation here could be per client and cost nothing, where theirs cannot. If any item on this list
is worth doing for its own sake rather than for parity, it is this one, and it wants the GPU bridge
machinery first.

**5. Masks: the source is fine, the recovery is missing.**

Read on 2026-09-08. SPS paints a mask texture saying which vertices belong to the shaft, with an
auto-mask from bone weights and an author override on top. YAPS derives the same number from the
rig instead: `WeightOnPlug` in `YapsBaker.cs` takes each vertex's skin weight on the plug's bone
chain and stores it as the bake's `active` channel, which the shader reads as the per-vertex blend.
That is the better source of the two, and it is not what needs work. A skin weight feathers at the
base for free, and because it is the same number the skinning itself uses, the mask cannot disagree
with the deformation the way a painted texture can.

The problem is what happens when that source is not available, and there are two such cases.

- **The climb was unbounded. Fixed 2026-09-08.** When no bone sits under the plug's root, the baker
  walks up ancestors until it finds a subtree with any weights at all and takes that whole subtree.
  A plug parented to Hips climbs to Hips, every vertex on the body reads a weight near 1, and the
  avatar bakes as one enormous shaft. It is what ten avatars did.

  The conversion has refused this since long before, by asking whether the level it climbed to is
  itself a humanoid bone. The baker did not, so a plug the converter turned away could still be
  baked into a whole-body shaft by hand from the toolkit. Another one-path-only divergence between
  the two builders, found by reading rather than by a report.

  The baker now refuses it too, and asks a narrower question than the converter does: whether the
  bones it captured include *this renderer's* head or feet. A shaft on its own renderer weighted
  only to Hips captures neither, so it still bakes, where the converter's rule turns it away.
  Vertex count cannot decide this on its own, which is what the entry first proposed: a dedicated
  shaft renderer legitimately captures every vertex it has. Socket bakes are exempt, since they
  route through the same function and a socket on a body mesh is meant to reach the body's bone.
  Nothing is claimed on a rig with no humanoid mapping.

  **Not yet exercised.** It is written against the failure and it compiles; no avatar has hit it
  since. The corpus is where that would show, and it has not been run for it.

- **A plain mesh has no mask at all.** On a non-skinned renderer the baker writes a flat weight of
  1 for every vertex. Correct when the mesh is only the plug, and wrong the moment it is not,
  with nothing to say so. The only guard today is that a vertex behind the base is dropped.

Across both, the gap against SPS is not where the mask comes from, it is that there is no override.
The plug's root bone field is the only lever, it cannot touch a plain mesh, and it cannot trim a
skinned one: a sheath weighted to the same chain as the shaft bends with it and the author has no
way to say otherwise. A painted mask is the general answer and it is a real feature, needing UV
sampling in the bake, import settings and a workflow for producing the texture. Neither the plain
mesh nor the sheath has a reported case yet, so the honest order is to bound the climb now and
leave the override until an avatar asks for it.

**What is deliberately not on the list.** SPS2's legacy compatibility switch, because reading the
legacy systems is this tool's whole job, and their material slot budget, because the atlas answers
that differently.
