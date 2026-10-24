# Unfinished

The single list. Every open piece of work lives here and nowhere else. Check this document
before starting on a new idea, in case something similar is already on it, and whenever the
question is "what's next" or "what is left to do". An item leaves this file by shipping, or
by a decision recorded in `archive/`. The standing rule still outranks everything on it: a bug
somebody hits while wearing an avatar comes first.

Transport facts and their build order live in `YAPS5.md`; solver maths in `SolverCalibration.md`;
what SPS code may be looked at in `YAPS-CLEAN-ROOM.md`. Finished records are in `archive/`.

Laid out by when: what 5.0.0 waits for, what 5.0.1 takes, then the ideas.

## Before 5.0.0

5.0.0 ships when Joe has worn it through the in-game checklist (`Dev/Release/ingame-checklist-5.0.0.md`,
local) and says so. SPS2 tags (S23), the portal (S24) and the duplicate (S25) must pass in game, and
both packages ship together. Joe, 2026-10-21: the items below are fixed first. Records of work done
on dev moved to `archive/Closed-2026-10.md` the same day; their in-game checks are in the checklist.

Joe's own: post the drafted replies to issues #6, #7 and #8 (`Dev/Release/issue-replies.md`), and the
GrabbyBones upstream PR (plan in `Regression/GrabbyBonesForkPlan.md`).

### 0. User reports from stable, 2026-08-27: a bug a user hit, so first

Both from a user on the shipped build. Neither is reproduced here yet, and
neither should be closed on the reasoning below alone.

#### Duplicate SPS toggles after conversion

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

**Found 2026-10-21, on the window's route, not the converter's.** `DuplicateToggleCheck.RunNativeBatch`
converts each avatar, then runs Build all on it as the YAPS window would. Across 12 SPS avatars,
every converted plug's rows read the same: the label came from the plug's root, which the
conversion always names `YAPS Plug`, so two plugs gave two `YAPS Plug plug own sockets` rows nobody
could tell apart. Plugs on one mesh were a second case: a row writes the whole renderer, so they
share it, and each build renamed it to whichever plug went last. **Fixed:** the label climbs past
the conversion's and VRCFury's wrappers to the author's object, two plugs of one name are told
apart by the first wrapper the other lacks, and a row shared by plugs on one mesh names them all
(`MenuLabelFor`). `Dev/Tests/PlugLabelTest.cs`.

**Still open, and not this:** the user's report was an inert author toggle beside a working one.
The probe marked no entry dead on either route across the 12, but dead there means a clip path that
no longer resolves; an entry that resolves and still does nothing in game would not show. Leave the
record open until the user confirms on the 5.0.0 build, and ask for their avatar's SPS setup if it
persists.

### 5. Cloth on a plug's own bones, unmeasured


A cloth chain that moves bones inside a plug would break the per-vertex frame in play. Ruled out
as the cause of the 4.6.4 tear (archived, "Blendshapes a plug renderer holds came undone"), never
measured in game.

### 6. Conversions got slower between corpus 418 and 419: the squash's compile, fixed on dev


419 took 5 h 52 min, 418 5 h 12 min, though 419 ran unthrottled for its second half [M]. Summed over
the YAPS avatars [M]:
- penetration pass 16 to 24 min, squash 223 to 261 min, the prewrite import 7.3 to 12.6 min;
- the heaviest avatar's passes 808 to 1206 s;
- squash's slot loop 202 min over 66 avatars (top 1113 s), yet `ShaderFor` and `Save` together take
  about 1 s an avatar, so most of the loop sits outside every lap;
- 0 Poiyomi lock calls in 419.
- Suspects [R, unmeasured]: bigger patched shaders from the portal and Two At Once (with the copy
  3903 fxc lines against 3546), and dev's own commits since 418 (8821f2a, e1667a3, ba7b338).
- Next: lap the slot loop's imports, then an A/B subset of the slowest avatars, `dev` before the
  merge against `portal-spike`. A subset run needs Joe's word.
- **Located 2026-10-21, subset 423 (`ab5-subset.txt`, dev 3a8f9e1 + laps, Joe's word) [M]:** the
  slot loop's missing time is `YapsLock.Basis`, the locked stand-in the copy is made from: 1188 s of
  the heaviest avatar's 1190 s (33 copies), then 137 s, 61 s, 0.07 s; the material assignment is 0.
  `Basis` now laps keying apart from loading the stand-in, and full run 424 reports the split.
  Suspect [R]: `Existing` loading each stand-in, and with it its own locked shader.
  **WITHDRAWN 2026-10-21 (run 424):** not `Basis`. Its key and load laps read 0 on all 24 squash
  avatars while the lap around them read up to 1011 s: the time is in the same line's
  `new Material(...) { shader = patched }`, which that lap also wrapped.
- **A tester's report 2026-10-21 [user]:** the squash "takes ages for everything to compile" on an
  avatar whose materials were locked before it built. `Basis` returns at its first test for a locked
  material (`Wants`), so this one is not the stand-in time above: suspect [R] the patched copies'
  own shaders compiling. Measure it on a locked-first avatar before calling item 6 done.
  **Same cause, measured 2026-10-21 in the Poiyomi probe project [M]:** the first Material made
  with a fresh YAPS patch waits 82 to 280 s; Poiyomi's own locked shader under a fresh name waits
  0.12 to 0.17 s, and so does the YAPS copy once one material has used it. Locked or unlocked makes
  no difference: three patches in one build, timed in either order, cost 280 s, 85 s and 0.15 s in
  that order, so they compile side by side and the first material waits for its own.
  `ShaderHasError` and `isSupported` return at once and trigger none of it. The patch is 844 KB
  against the locked shader's 342 KB, and the compiler logs show only its Base, Add and
  ShadowCaster default variants compiling. Suspect [R]: YAPS code the compiler unrolls, the
  atlas scan first. Next: a fresh YAPS Simple Lit timed the same way, which says whether the YAPS
  code alone costs it, then switch features off one at a time (atlas, squash, portal, the second
  copy) to find which.
- **Located and fixed on dev 2026-10-21 [M]:** the squash. A fresh YAPS Simple Lit costs it too
  (203 s against Poiyomi's 215 s), and without the squash call the same patches take 15 s and 20 s.
  The dent's four-part loop was unrolled around unrolled arm, hand and plug-light loops, so the
  body was copied many times over. Those five loops are `[loop]` now: 27 s and 33 s, salted fresh
  shaders in the probe project, contended by a corpus run. A/B against the unrolled dent in batch
  Play mode, with the arm and 64 players' hands: 0.0005 mm worst over 1800 frames and 4928
  vertices, render cost level. Not exercised: the plug-light loop (no vertex lights in that scene).
  Still open: a corpus run on the fix to confirm the squash pass time fell, and it needs Joe's word.
- **Roles and the four-light loops, on dev 2026-10-21 [M]:** each patch now carries only what its
  material does (plug, socket, squash, or a union of them; `_YAPS_Roles` records which, absent means
  all), and the four-light scans loop instead of unrolling. First compile, salted fresh copies in
  the Poiyomi probe project, the includes before the squash fix against now: Poiyomi 354.7 s to
  24.5 s with every role, 10.7 s squash alone; Simple Lit 126.6 s to 14.5 s, 7.9 s squash alone.
  `YapsRolesTest` PASS; a bend A/B on a fresh bake identical, the light route 50 of 50 both ways.
- **Confirmed by run 427 (dev e5608e1, full, default profile) [M]:** the squash pass summed over 84
  avatars took 223.4 min in 418, 261.1 in 419, 242.5 in 424, 29.4 in 425 and 14.2 in 427; every pass
  together 286.3, 321.4, 287.2, 62.8 and 36.8 min. The run took about an hour in four Unity
  processes. 427 followed 425 and 426 on the same machine, so part of its lead over 425 may be
  Unity's shader cache holding copies those runs compiled.
- **Run 429 (dev 2373354, full, default profile) read against 427, 2026-10-24 [M]:** 10 of 85
  digests moved, one class: which layers the squash copies. 7c0c6fe gives a layer the skin's share
  as a distance-weighted blend out to 4 cm instead of the nearest skin vertex within 3 cm, and any
  layer with one share that stores above zero (1/510) is copied. So layers 3 to 4 cm off a part now
  carry a faint share and a copy (a prop, a hair mesh, and a plug on three variants of one avatar;
  a plug on another was already copied in 427). Two hair layers lost theirs, read from the output
  folders; that they only grazed a part's faded edge and now average below storable is inferred,
  not measured. The `converted=` counts move by the copies,
  the Weight "sets of materials share a shader" counts and one avatar's texture total (0.1 MB, a
  share texture fewer) with them. Components, AAS, controllers, physics and the toggle sweep (2798
  parameters, 1624 responded, 47 stuck) are identical. Not promoted. Pass time is not comparable:
  every pass 51.1 min against 36.8, squash 20.1 against 14.2, but the animator merge (7.4 to 14.3)
  and clip self-containment (7.1 to 12.2), which compile nothing, slowed more than the squash, and
  probes shared the cores during the run.
  **Decided 2026-10-24 (Joe) and fixed [M]:** a copy needs a stored share of 4/255 (about 1.5% of
  the squash) on some vertex of the slot (`YapsSquash.MinShare`), and a material carrying a plug
  bake never takes one. Measured over the 506 share textures in the corpus output: the prop (220 on
  3 vertices) and the hair mesh (median 207 on 91) are strong and keep their copies; the faint ones
  are the plug (1 on all three variants), a hood (1), a belt (1) and a socket mesh (3), which the
  cutoff drops, while a stocking at 4 keeps its copy. Of those three plugs only one variant's is
  baked; the rest, and a plug-shaped mesh on another avatar at 52, are plain meshes and follow the
  cutoff like any layer. `SquashSlotTest` PASS.
  Not yet seen in a corpus run, and neither rule in ChilloutVR.
- **Run 424 (dev b9fb21d, before the loop fix) [M]:** 73 of 85 digests moved against baseline 422,
  every class explained: the count line; rest clips for sliders (90b277a); squash reported not
  built where every shader under the part refused (a1b3812: 9 avatars on Unity's Standard or a
  shader in error had a provider that moved nothing); the locomotion warning gone where the
  baseline predates e1667a3's FX masks. Seven avatars' Weight lines are void: a probe converted
  them with default settings that morning and resized their source textures (reverted from each
  `TextureSizes.undo`). So 424 is not promoted.

### 7. YAPS against every popular shader: done
- **Squash on Unity's Standard, 2026-10-21 [M]:** the plug and socket move a Standard material to
  YAPS Simple Lit; the squash only patches, refuses Standard ("no source file on disk") and is not
  built. Run 424 has nine such avatars. Same Simple Lit fallback, or say so in the setup card.
  **Written on dev, not yet compiled (Joe in VR):** `YapsSquash.ShaderFor` moves a Standard layer to
  Simple Lit only when it is opaque and uses nothing Simple Lit lacks (metallic, occlusion, emission,
  height or detail maps, smoothness from albedo alpha, an emission colour with the keyword off), since
  squash patches the body's own skin and a changed look there is worse than holding still. Anything
  else keeps the refusal, with the reason. `SquashRefusedTest` gains a plain Standard case (built, on
  a patched Simple Lit copy) and a metallic-map case (refused, names the map). Then: the nine
  avatars from run 424 in a subset.
  **Tested 2026-10-21 [M]:** old code 4 WRONG (both new cases). New code first read 2 WRONG on the
  metallic case: the test set `Texture2D.whiteTexture`, which does not survive the material being
  saved, so the material had no map. With a saved texture asset: 8/8 PASS, the refusal reads "YAPS
  Simple Lit has no metallic map". The nine avatars are checked in the full run instead of a subset.
  **Run 427 [M]:** one avatar gains squash on a Standard layer (Converted "Deform patched into YAPS
  Simple Lit", 14 squash materials where 13 were). The nine stay unbuilt, each now naming why:
  Standard with a metallic map (three), Unity's surface shaders (two), shaders in error that read
  as `Hidden/InternalErrorShader` (four, one avatar's scene variants). Correct by the rule above; the fallback simply finds
  little to take in this corpus.
  **YAPS Standard, on dev 2026-10-24 [M] (Joe: "try that"):** `Editor/Yaps/YapsStandard.shader` is
  Unity's Standard rebuilt from its published source (MIT, notice in the file): the same properties,
  passes and pragmas, Unity's own vertex functions called through thin wrappers that the patcher can
  take. A Standard material on a plug, socket or squash layer now moves to it with every setting
  carried over (`YapsNativeBuilder.OnFallback`); every other refused shader still goes to Simple Lit,
  which never stands in on a squash layer. `SquashRefusedTest` old code 5 WRONG, new code PASS: a
  plain Standard skin is built on a patched YAPS Standard copy, and a transparent one with a metallic
  map keeps its map, keyword, blend and render queue. SquashPartsTest, StandardCasterTest and
  IncludeVertexTest PASS. Full shader sweep RESULT PASS, 54 of 54: YAPS Standard plain, every map
  and cutout patched, 0 px over 2/255 against the unpatched shader at rest and off, Bend and Open 0
  stray with full cover in view and shadow; built-in Standard and Standard every map refused, drawn
  on YAPS Standard, 0 px against built-in Standard. Run 427's three metallic-map avatars should now
  build; the next full run checks them.
  **Found on the way, pre-existing, fixed for YAPS [M]:** Unity drops a material's own render queue
  when its shader changes. A QueueProbe read 2000 for a transparent 3000 on every route that swaps
  the shader (Standard to the patched copy, Standard to Simple Lit, Poiyomi to YAPS Standard), so
  every patched copy of a transparent Standard or Poiyomi material drew in the opaque queue.
  `YapsShaderPatcher.Wear` keeps the queue across the swap and every YAPS site uses it.
  **Still open:** the same swap without the queue kept in `Editor/Core/ShaderSpiPatcher.cs:1370`
  (`copy.shader = patchedShader`, the SPS patcher) and `SceneAssetRehomer.cs:265`. Unmeasured there;
  a failing test first.


Joe, on the K2 options: "not every shader is built the same ... we would need to test YAPS plugs and
sockets against every known popular shader, mochie, liltoon, poi, autodesk interactive, etc". The
lilToon finding below is the proof: a host can patch without a word and never bend.
- What the corpus avatars use, from the corpus project's source materials (generated output excluded,
  2194 materials) [M]: Poiyomi 1079; Standard and other built-ins 469, plus 116 Standard-like on missing
  shaders; VRChat Mobile 150; Mochie about 134 (fingerprinted, shader not installed there); UTS 26;
  lilToon 16 (not installed there). Not in the corpus but popular: Silent's Filamented and CrossTone,
  Xiexe XSToon, ORL toon, Sunao, arktoon, Poiyomi Pro, Autodesk Interactive.
- Per shader and per mode that offsets vertices (outlines above all), with the shipped patcher:
  1. It patches, or refuses with a reason. Never patched-but-straight like lilToon.
  2. The plug bends in every pass that draws (forward, add, shadow caster, outline), against Simple Lit.
  3. Sockets: the dent and squash patch on the same host.
  4. Two At Once: the copy draws, and hides, in every pass.
  5. Portal: the corridor test, which decides whether the band (K2 options 2 to 4) is safe anywhere.
- Harness: PortalCopyProbe's host list and its per-host captures, generalised. Shaders not on this
  machine need Joe's yes per download.
- It answers the K2 call: the band's safety rests on hosts offsetting along the normal, and this is
  the survey of that.
- Installed in the Poiyomi lock project with Joe's yes: Mochie 1.77.1 Free, ORL 7.3.1 (with its
  generator and inspector), XSToon 3.7.1 (its fur add-on left out; 15 GUIDs it shares with an older
  partial Xiexe copy there were given new ones), Filamented 1.4.0, arktoon 1.0.2.6, UTS 2.0.7 and
  Silent's Cel Shading. Already there: Poiyomi 9.3, lilToon 1.7.3, the VRChat Mobile shaders. Sunao
  1.6.2 added 2026-10-24, fetched by Joe.
- `Dev/Probes/Portal/ShaderSweepProbe.cs` covers checks 1 to 3: 37 setups across 12 families, every outline mode that offsets by its own rule
  (mask, vertex colour, baked normal, position, geometry-shader outlines). Per host: patched or
  refused with a reason; YAPS off draws the unpatched original to 2/255; a plug bend and a socket
  shape move, each judged against Simple Lit drawing the same move, in the view (forward and add
  passes, outlines) and in a floor shadow (the caster). A pass the patch missed draws where only the
  unmoved tube was. The dent rides the same injected call as the socket move and is not drawn
  separately. It also counts the deform calls in each patched copy, and `-sweepDump` keeps the copies.
- **Checks 1 to 3, run 2026-10-19 (Poiyomi lock project, four runs) [M]:**
  - Patched and moving in every pass, 0 stray pixels in view and shadow, outlines drawn: Simple Lit,
    Poiyomi 9.3 (plain, outline, vertex-colour normals local and world, drop shadow), XSToon 3.7.1
    (plain, outline, vertex-colour normals). Off equals the original to 0 px over 2/255 on all.
  - Refused with a reason and drawn on Simple Lit, as designed: Standard and Autodesk Interactive
    (no `vertAdd`), VRChat Mobile Toon Lit and Standard Lite (surface shader), Mochie Standard and
    Filamented (no `SV_VertexID`), Crosstone, Lightramp and arktoon (no POSITION struct found), ORL's
    three (generated by its importer, no source on disk).
  - **Patched yet never moved, no warning (fixed, below):** Mochie Uber 1.77.1 (plain and both outlines)
    and UTS 2.0.7 with outline (three outline modes): 0 deform calls in their copies. lilToon (plain and
    outline): its drawing passes are borrowed.
  - Probe fix on the way: a refused host was failed for drawing no outline. Simple Lit has none.
- **Sunao 1.6.2, checks 1 to 5, 2026-10-24 (Poiyomi lock project) [M]:** plain, outline (normal
  times a mask), stencil outline, transparent outline and fur all patch natively, five vertex passes
  in the first subshader, 3 deform calls in 18 files (fur 20). Off and nothing in reach both 0 px
  over 2/255; bend and open 0 stray pixels, view and shadow fully covered, outlines drawn (+29.2%,
  fur +19.7%). A skinned plug stays straight on none of them. Check 4: 0 corridor pixels on all five,
  HDR and 8-bit. Check 5: 0 corridor pixels on all five, so no new line under the band.
- **Fixed on `portal-spike`, 2026-10-19: a shader whose vertex function sits in an include was
  "patched" with nothing in it.** Passes are patched last first, so the shadow caster went first. It
  marked the include done before its checks, failed on the missing `SV_VertexID`, was skipped as
  cosmetic, and every later pass returned "already done". It is marked only once injected now, so
  Mochie Uber and UTS refuse with the real reason and fall back to Simple Lit (sweep 3: 30 of 32,
  lilToon the two left; sweep 4 with the `UsePass` refusal below: 32 of 32 PASS). `Dev/Tests/IncludeVertexTest`
  failed before, passes after, and covers the borrowed-pass refusal (a borrowed caster still patches).
- **Not run in the corpus.** Squash patches body materials through the same patcher, so the next
  authorised run should change digests on avatars wearing UTS or a shader that borrows passes: a
  refusal where a silent straight patch was. That is the fix, not a regression.
  **Run 427 carried the fix (9c52bfb is in e5608e1), checked 2026-10-24 [M]:** its log names every
  refusal the corpus has (no source file on disk 13, no metallic map 5, surface shader 3,
  `InternalErrorShader` 14) and none of the new kinds (a borrowed pass, no `SV_VertexID`, made on
  import), so no corpus avatar wears such a shader under a plug, socket or squash part and no digest
  moved for it. Nothing left to run. Checks 4 and 5 passed on 2026-10-19 (below).
- ~~ORL's refusal says the shader "lives inside a compiled package", which is wrong for a shader its
  importer generates.~~ **Fixed on `dev` 2026-10-19:** a shader with a project path but no `.shader`
  text is refused as "made on import from <file>, which is not shader source". `GeneratedShaderTest`
  (its own scripted importer, no ORL needed) 2 WRONG before, PASS after. Still open from the sweep: a host with `SV_VertexID` and one vertex function name
  per pass in different files (or several under `#if`) would get only the first patched; no tested
  host has both, so it is latent [R]. The `UsePass` refusal runs before the vertex checks and
  excepts only caster and meta passes, so Filamented now refuses for borrowing `Standard/DEFERRED`
  rather than for its missing `SV_VertexID` (since the lender patch, because `Standard` cannot be patched). Same outcome there; a shader whose only borrowed pass is
  a deferred one would be refused though a forward client never draws it [R].
- Checks 4 and 5: `-portalSweep` on PortalCopyProbe (the copy and its corridor, shipped includes)
  and on PortalBandProbe (the band, spike includes swapped in) adds the same hosts beyond the
  families those probes already draw. Refused hosts draw on Simple Lit, already a host there.
  - **Check 4, 2026-10-19 (shipped includes at `9c52bfb`): PASS** [M]. 51 PASS, 0 FAIL: 0 corridor
    pixels on every host, HDR and 8-bit, outlines drawn; exit vertices within 0.0213 mm (HDR) and
    0.1617 mm (8-bit) of the decoded exit; 0 plain or first-copy vertices moved by the exit. The new
    hosts that patch are XSToon's three; every other sweep host refused with the reasons above.
  - **Check 5, 2026-10-19 (the band, spike includes swapped in, restored after): no new line** [M].
    The only failures are S3's two known setups, Poiyomi's local vertex-colour normals (39098 and
    37181 corridor pixels, HDR and 8-bit) and lilToon's vector from vertex colour (18217 and
    17456). XSToon plain, outline and vertex-colour normals draw 0, outlines drawn. The K2 table
    stands: of the hosts that patch today, only those two draw the line under the band. A real
    lilToon patch would bring its vector mode back into reach. **It now does** (lilToon record below):
    a lilToon plug whose outline takes its vector from vertex colour would draw the line under
    options 2 to 4, as the Poiyomi local mode would. Joe kept the fallback (2026-10-19), so neither does.

### 8. The MagicaCloth2 review: findings not yet acted on


Joe called the MagicaCloth2 output "really bad". Six reviewers read the writer against the MC2 source
and the decompiled VRC.Dynamics (scratchpad copy of `PhysBoneManager.cs`), each challenged by a
skeptic. Items 1 to 7 below are now fixed as above unless noted; item 8 is open.

The core 60/90 Hz calibration, the 0.2 restoration scale and 3 iterations, zero wind, raised
movement limits, the inertia anchor, humanoid exclusions and `_End` tips all held up. What did not,
ranked by how visible it should be:

1. **The swing bound pins the base and middle of every limited chain.** MC2 squares depth before it
   reads maxDistance (`MotionConstraint.cs:194`, also in CVR's copy); the writer's comment
   (`MagicaClothWriter.cs:724-729`) assumes linear. One chord for the whole chain, from
   `Max(MaxAngleX, MaxAngleZ)` (`:707`) where VRChat reads only X for Angle/Hinge and Z defaults to 45;
   hinge plane and limit rotation ignored. Found independently by two reviewers.
2. **Presets leak values the derivation never overwrites.** `angleLimitConstraint` (the constraint
   3.7.0 removed for vibrating; the README says it is off) stays on from LongHair 75, Skirt 60, Cape
   45, FrontHair 15 and others, even for a source with no limit. `velocityAttenuation` 0.55-0.8 stays,
   so only part of each restoring move becomes momentum. `particleSpeedLimit` 2-4 m/s stays (hunch).
3. **Damping lands at a fifth.** MC2 scales inspector damping by 0.2 (`ClothSerializeDataFunction.cs:181`)
   and `PhysBoneSolverMap.Damping` never divides it back, unlike restoration. The 2026-08-06 preset
   floors cover for it, and the restoration floor reads the preset's ROOT value and flattens the
   taper (`MagicaClothWriter.cs:560-586`): floored hair and tail tips 5-10x stiffer than the preset.
   Items 2 and 3 pull bounce in opposite directions; only measuring says which wins.
4. **Gravity never converts.** Only zero and negative are handled (`:646-664`); positive source
   gravity gets the preset's 0-7, so tails, earrings and charms on Tail/Accessory presets never hang.
   Gravity falloff unread (DynamicBone only). PhysBone v1.1 gravity bends the target pose; MC2's is an
   acceleration, so even a converted value needs calibrating.
5. **Soft bodies (BoneSpring).** MC2 forces gravity 0 and turns the swing bound off for BoneSpring
   (`MotionConstraint.cs:118,122`), yet the report says "Swing bounded" for every limited soft body.
   The 0.06 power floor overrides a user's saved preset (`:1267,1280`) despite Presets/README.md.
   Jewellery under a breast bone becomes a soft body through the ancestor walk
   (`MagicaPresetLibrary.cs:107-114`); "Pec" and "Hip Jiggle" are missed. A shared parent root springs
   both sides as one block.
   - 2026-10-24, the angle limit 86a563f gave soft bodies is a trade, not a fix. Physics A/B on
     Sootie with it and without: breasts much nearer their PhysBones with it (walk peak error 22
     against 68, lying 52 against 192), thighs worse (walk 34 against 9; lying they hang 20 degrees
     off where without it they hang exactly). It is what grew the thighs' swing in the squash story
     from 5 to 20 cm since 2026-10-15, and it deadens the tester's hand on them in Play mode: the
     thigh story's lab Test hand moves 3.7% of the legs against 2.4% swaying (DEAD) with it, 7.3%
     against 2.5% without, every other row the same. Undecided: keep it per class, or find why an
     MC2 angle limit on a BoneSpring swings a part further. `D:/Claude/scratch/curveab/{new,nolimit}`.
6. **Collision.** Point mode (`:184`, `MagicaColliderAutoAssign.cs:82`) where VRChat collides the bone
   as a capsule with lerped radius, so chains pass between particles; MC2's Edge mode matches. The
   author's radius is replaced by a mesh thickness; collider fitting sizes spheres from their narrow
   ends and never re-centres.
7. **Inertia split** (weakened by its skeptic): all three of world, local and anchor take `1-immobile`
   (`:670-697`). Walking ends up over-held; head turns slightly over-held. Needs the harness.
8. **Structure.** `ignoreOtherPhysBones` is now read (`PhysBoneData.NestedExclusions`, matching
   `VRCPhysBoneBase.cs:714`: any child that is another PhysBone's root, the whole hierarchy's
   components, disabled ones too). DynamicBone excludes them exactly (`NestedPhysBoneTest`: old code
   WRONG, exclusions empty; new PASS, Front and Bead, empty with the flag off. 2026-10-21 [M]).
   **MC2 still open:** it only reports "Also moves", because rooting around a nested root pins the
   first joint of every sibling branch, and partway down a strand it would pin the path bones too.
   Measure with the Physics A/B probe on an avatar with one, rooted around vs two cloths on one
   bone, before choosing.
   **Measured 2026-10-21 [M], decided: keep the report.** A census over the corpus scenes finds
   nested roots under Ignore Other on 3 avatars only (one at depth 1, one at 2, one at depth 3 down
   a strand). Physics A/B probe on all three, today's two-cloths-on-one-bone against a variant that
   adds the nested roots to the ignores (so `WriteRootsExcluding` roots around them), summing
   |copy - source| over sag, lie, lie hold, the walk/turn/jump/shake peaks, settle, overshoot, press
   and stretch for the outer and nested chains: depth 3, 1094 to 1002 (one side nearer, the rest
   unchanged); depth 1, 310 to 2356 (chains that held still swing 100° and more once rooted around);
   depth 2, 195 to 251. Rooting around is worse where it changes anything big, so the "Also moves"
   report stays. Both are far from the source on these soft parts either way, which is items 3 and 5's
   ground, not this one. Curves keep only endpoints though MC2 samples 16 points; the Immobile
   curve is dropped. ~~PhysBone `version` never read~~ withdrawn: `IsVersion10` is read and used
   (`MagicaClothWriter.cs:631,633,731,894`). One cloth per PhysBone (issue #7): the "asked which
   avatar" blocker is stale, the reporter sent it on Discord. Distance culling unused.
   **Run 427 [M]:** the "Also moves" line appears on four avatars (two scene variants of one, and
   two more; Approximated up by 1 or 2). On one of them it replaced the chain's "Multi Child Type
   'Ignore'" line: `PhysBoneData` counts a root's children without the nested roots, so that root
   no longer branches, as VRChat sees it with Ignore Other on. The `[physics]` block, which records
   each cloth's root bones, is byte-identical on all four against run 424, so no cloth moved.
   **Curves, fixed on dev 2026-10-24 [M]:** pull, spring and stiffness curves were read at the two
   ends only, so a chain loose in the middle converted as held all along. A census of the corpus
   sources (2935 PhysBones) finds 45 pull, 50 spring and 66 stiffness curves shaped more than 0.1
   away from a straight line, in 13 to 18 files. The derived damping and restoration are sampled at
   MagicaCloth2's own 16 depths now; a result that is a straight line keeps the old two-end write.
   `PhysBoneCurveTest`: old code 2 WRONG (a dip read 0.678 all along where the PhysBone wants 0.054
   mid-chain; a ramp's middle 8% off), new PASS (0.061, and 0.0001 off). A straight PhysBone curve
   is not a straight restoration (the mapping bends it), so linear-curve chains change too. Not
   measured with the Physics A/B probe yet, not in game. Left as they are, each small: the Immobile
   curve (MC2 inertia has no depth curve), gravity curves (49 shaped; MC2 gravity is one number),
   distance culling.

### 9. The audit of 2026-10-03: what is still open

**Still open**, each with why:

- **PHY-03, native mode's ancestor rule.** A contact or collider host outside the switched-off object
  it belongs under still converts live in *CVR Native Targets*; dropping that needs the contact's
  enabled state recorded beside `ctx.ContactHosts`. Whether a switched-off host stops a DynamicBone
  collider needs ChilloutVR.
- **FT-02, face shapes after Face Tracking goes off.** The rig's off states are Write Defaults on with
  no motion, and ChilloutVR restores no defaults, so the last tracked shapes may stay. A zero clip
  would fight the native blink `EyeTrakingOFF` hands back and override the avatar's expressions.
  ChilloutVR: turn Face Tracking off mid-expression and watch the face.
- **FT-05, eye gaze on a rolled head bone.** The gaze empties hang off `EyeTracking.Frame`, turned to
  the avatar's facing, so the clips' angles should hold on a head bone that is not axis-aligned.
  Compiled only. Play mode or ChilloutVR: such an avatar looking down and left.
  **Proven in Play mode 2026-10-21 [M]:** `Dev/Tests/GazeRolledHeadTest` builds the rig on a head
  rolled (-90, 0, 37) and on a straight one, both under an avatar turned 30°, keys the empties
  down 20 and left 20 and reads the eyes after the constraints run: both heads, both eyes, down
  0.342 and left 0.321 in the avatar's frame (sin 20° = 0.342). Real eye-tracking input stays on the
  in-game checklist (S6.8).
- **ASSET-FURYHEAD-REFS, skins bound to VRCFury's second head.** `FuryHeadFlattener.RepointReferences`
  moves anything naming `vrcfAlwaysVisibleHead` onto the head bone, but nobody has seen VRCFury bind
  bones to the copy. Convert such an avatar, read the report's count, check the skin keeps its pose.
  **Read 2026-10-21, closed as defensive:** VRCFury 1.1408.0 makes the copy in `FakeHeadService`
  for one caller, Show In First Person, which links the OBJECT under it (`useObj`, not `useBone`), so
  the object's own bones move with it and nothing binds to the copy itself. With an SDK that has
  head chop (`VRCSDK_HAS_HEAD_CHOP`) the copy is not made at all. The corpus's one copy carries a
  socket child, which the flattener moves. `RepointReferences` stays as a guard for older Fury
  builds; there is no avatar to convert that exercises it.
- **YSH-13, the protocol-light black test differs between plug and socket.** The plug's
  `YapsClassifyLight` rejects `any(rgb > 1e-4) && a > 0`; `YapsPlugDepth` and the readout reject
  `dot(rgb,rgb) > 1e-4`, which accepts a near-black third-party light the plug refuses. The digit
  decode is shared now (`YapsLightDigit`), the colour test is not. Settle in game which one
  third-party lights need, then fold it into the helper.
- **YSH-07, a skinned plug and the height slider.** `yapsScale` reads `unity_ObjectToWorld`, identity
  on a skinned mesh, so the bend may not follow the slider. Game first: a skinned plug in a socket at
  2x and 0.5x, Gap or Resolved-by view. The fix would be an avatar-scale property mirrored next to
  the slider, used in the per-vertex recovery too.
  **Covered by item 21.2's fix (f588654), read 2026-10-24:** the slider animates the root's
  `m_LocalScale` (`AvatarScaler/Anim_AvatarScale_Slider_*.anim`, empty path), which is what the
  tester's `-yapsScale` does, and a skinned plug now reads that size from its own skinned normals
  (`_YAPS_BakeSkinScale`). Tester: 0.2, 0.5 and 1 pass; what fails past 1.6 is the atlas's reach,
  not the scale. Needs a re-bake; ChilloutVR still to see, with the slider at both ends.
- **YSH-18, shadow and depth passes** resolve with different light and atlas state from the visible
  pass. Game: a plug bent through a marker light and one through the atlas, under a shadow-casting
  light. If confirmed, decide per pass, and add a README Known limitations line.
  **Measured 2026-10-22 [M], Joe's "ghost" report** (a faint plug shape through the receiving
  skin in game). `Dev/Probes/PlugGhostProbe.cs`: a converted avatar's real skinned plug on its
  patched material, a hole 0.7L out at 35° and 60°, camera depth texture read back. Atlas only, and
  atlas with lights: 0 depth pixels where the colour has no plug, and the floor shadow bends (20875
  to 24140 px off the unbent plug's). Marker lights only: 21724 and 26216 depth pixels where the
  colour has no plug, every one of them on the UNBENT plug, and the shadow identical to the unbent
  plug's (0 px differ). With an opaque box behind the entrance as the body, those same pixels are
  depth where the colour shows only body. Cause: Unity sets the four vertex light slots for
  ForwardBase only, so a ShadowCaster pass (shadow maps and the camera depth texture that depth of
  field, fog and ambient occlusion read) sees no marker light and draws the plug straight. The
  atlas is a global texture and reaches every pass. Marker lights decide whenever the atlas cannot:
  a DPS-only socket, a view too small for the atlas, mixed YAPS builds (protocol 7). README Known
  limitations line added; checklist S17.6.
  **No shipped fix: the caster has no way to read the lights.** Options for Joe: (a) leave it,
  documented; (b) the plug's forward pass writes the socket it found by light into the atlas, so the
  next frame's caster (and every plug) reads it there: an atlas protocol change, real work; (c) the
  plug left out of the camera depth texture (not shadow maps) when no atlas socket resolves, which
  removes the ghost but lets fog and depth of field treat every idle plug as the wall behind it.
  **Decided 2026-10-22 (Joe): (a), leave it documented for 5.0.0.** The README line and the release
  notes' YAPS line stand. `PlugGhostProbe` keeps failing on the marker-light cases by design; that
  is the expected result, not a regression. What is still open is the game check (S17.9), which
  confirms whether Joe's ghost was this cause at all.
  **Seen again in game 2026-10-24 (Joe, screenshot):** a very faint straight plug shape across the
  floor under a bent plug. The other player could not say whether their avatar went through the
  YAPS upgrade, so the route is unknown. Next: S17.9's Resolved by view (`_YAPS_Debug` 1) on the
  plug while it shows: three quarters (marker light) is this cause, full (atlas) would be a new bug
  that needs the world and both builds.
  **Measured again 2026-10-24 [M]: the sun's shadow cannot read the atlas either.** In a fresh
  project the floor shadow stayed straight on every route, the atlas included. A debug buffer written
  from a ShadowCaster pass showed why: a light's shadow map is drawn after the frame's screen grabs
  but bound to the texture `_YAPS_Atlas` named when the frame began, and Unity's temporary-texture
  pool trades same-size grab textures between names every frame (instance ids logged alternating
  render to render). The light's pass read a flat (0,0,0,1) picture, the clear's `_YAPS_Under` grab,
  while the camera depth pass, drawn before the grabs, read the real atlas. 7d5d696 (the exact
  put-back) added that second grab; with it commented out the 10-22 numbers come back (20875 and
  24148 px bent), but one unrelated named grab at the transparent queue, as water or glass in a
  world, breaks the single-grab build the same way (the light's pass read the floor's colour). So
  the 10-22 "atlas bends the floor shadow" held only in a scene with no other grab, and 7d5d696
  stays. README line corrected: a bent plug casts a straight shadow on every route, and depth
  effects follow the atlas. Release notes' YAPS shadow line and S17.9 corrected to match. Joe's
  10-24 sighting is one of two: a cast shadow sits off the plug along the sun and moves with it,
  the depth ghost sits exactly behind the plug from the eye. He will say which next time.
- **AM1-10, the menu-name rename rule.** Whether `RenamePass` should trigger on
  `IllegalMenuNameChars` (`[^a-zA-Z0-9/\-_#]`) instead of its own list, leaving
  `VerifyMenuParameterNames` to assert. Author decision: it changes the names users get for menu
  parameters holding `.`, `!`, `&`, `+` or non-ASCII letters.
  **Measured 2026-10-21 [M], decided: no change.** Every name the regex catches and `RenamePass`
  misses is already renamed later by `VerifyMenuParameterNames`, as a Warning, so the baseline
  digests list them all: 43 renames on 12 of 86 avatars, 11 distinct (`at||Sweater||29` to
  `atSweater29` and five like it on six avatars, `~Soup_FaceLock~`, `Oiled~`,
  `PullEars&TailToggle`, `VF#_thine.gg`, one all non-ASCII name to `Param`). Every result is
  legal and synced today. Moving the trigger into `RenamePass` would only swap those names for
  label-derived ones, and ChilloutVR profiles restore saved values by name, so users who already
  converted would lose their saved values for nothing they can see.
- **AM1-14, PlayAudio paths with a slash:** moved to 5.0.1, item 15 (Joe, 2026-10-22).
- **Docs outside the README: done on dev 2026-10-21.** `YAPS-CLEAN-ROOM.md` now rests on YAPS's own
  atlas protocol and the DPS marker lights, and no longer claims a contact channel. `YAPS5.md` stays
  a record, with a note at the top naming the three code names since removed and what replaced
  them. The `ParameterMenuConverter` header says the entries give menu controls and profile saves,
  and that sync comes from the animator's parameters.
- **Optional, no bug behind them:** an early out in `YapsDeform` before the full baked read (YC1-10;
  a compact layout would be a protocol change), one `PatchOnSimpleLit` for both builders (YC1-08),
  sharing the clip walk in `ConstraintScaleRelay` (PHY-13), a weights-based helper-rig rule shared
  with `ConstraintConverter.SkinningBones` (PHY-19, a behaviour decision; **measured 2026-10-21 [M],
  decided: no change.** A census over the corpus sources: a weights rule would call 97 of 3710
  chains helper rigs that today's listed-bones rule converts, on 14 scenes (32 distinct chains,
  hat brims, shaft segments, small two-bone chains), and none the other way, since a weighted bone
  is always listed. 72 of the 97 are grabbable and none holds content. They move no vertex, so
  converting them costs a little and shows nothing; flipping them hands them to the cleanup pass,
  the route that deleted grab handles before (item 3, archived), for no fix anyone could see), and the Toolkit's *Mesh
  bounds* button re-applying the avatar envelope to a separate plug mesh after a native bake.

### 12. The bend tester's seam checks fail on a fresh bake
Found 2026-10-21 [M]: on a freshly converted SPS avatar the tester's seams open 38 to 102 mm and 6
scenarios fail, with the includes before and after the roles change alike, so not that change.
Read what the seam check measures before calling it a bake fault.
- **Read 2026-10-21:** it pairs every captured plug vertex that sits within 0.01 mm of another at
  rest, with the current blendshape weights applied, and fails a pair that parts by more than
  0.5 mm bent. That catches true seams (one point, two UVs or normals) and also vertices a shape
  collapses together without being one point, which YAPS is right to move apart. The edge check
  already leaves collapsed parts out; the seam check does not.
- **Instrument written, not yet run:** a failing seam check now logs its worst pair, how many
  vertices share that rest point, how far apart the two sit in the bind mesh and how far apart
  their normals are. Bind distance 0 means a real seam, a bake or deform fault; above 0 with a
  cluster bigger than two means the shape collapse, and the check needs the edge check's rule.
- **Run 2026-10-21 [M]** (a converted SPS avatar's plug, 6618 vertices, 155 seam pairs; the capture
  shader `Dev/Tester/YapsBendCapture.shader` has to sit in DevTools beside the tester, deploy does
  not add it). Every failing walk names the same pair: vertices 2924 and 6614, 5 vertices at that
  rest point, 0.00000 apart in the bind mesh, normals 0° apart; bent, one moves 87.7 to 236.4 mm and
  the other 0.0 mm. So a TRUE seam: one copy of a point bends and its twin is not deformed at all.
  A bake or deform fault, not the collapse case. Next: what sets 6614 apart (its baked frame, its
  weights, whether it falls outside the plug's bake set).
- **Found and fixed (tester only), 2026-10-21 [M]:** neither. Vertices 6606 to 6617 are the debug
  overlay's readout, a second submesh on the plug's renderer (`YAPS/Debug Overlay`), all twelve
  past `_YAPS_VertexCount` (6606) and four of them collapsed onto plug vertex 2924. The tester took
  that slot for a plug slot, since it carries the plug's YAPS block, and paired the readout's
  undeformed corners with the bending tip. `IsPlug` now skips a material with `_YAPS_AnchorVertex`.
  Rerun: 1 slot, 143 seam pairs, seams open 0.00 to 0.05 mm on all four walks, every seeded
  scenario passes. The bake and the deform were never at fault.
- **Still failing, separate:** the 90° walk stretches an edge 6.31x at 0.20L (3.97 to 25.03 mm) and
  crushes 57 edges outside the opening, against the 6x floor set on 2026-09-23 from another plug
  (5.84x, socket beside the base). It failed in the first run too, before the slot fix. A socket a
  fifth of a length out at right angles bends the shaft 90° in a few centimetres, so a large
  stretch there is expected; whether 6x is the right bound needs the floor's own avatar (not in
  the corpus project) measured again. Tester only, not a 5.0.0 blocker.
- **Run 2026-10-24 [M]** on Non Corpus Zone's converted Alexa plug (6445 vertices, 307 seam pairs):
  PASS. Seams open 0.17 mm at most; the 90° walk stretches 4.65x at 0.22L, under the floor. The
  6.31x plug and the floor's own avatar are still the two to measure.
- **Run 2026-10-24 [M]** on another converted SPS avatar's plug (117 seam pairs), dev and the
  light-relay branch alike, line for line: 35 checks pass, one fails. The 45° YAPS hole walk opens a
  seam 0.96 mm at 1.36L (vertices 77 and 553, one rest point, normals 0° apart, both moved about
  275 mm), a true seam parting deep in a hard bend. The 90° walk passes there (3.31x). Not
  separated yet.
- **Separated 2026-10-25 [M]: the atlas resolve, not the bake or the deform.** A twin probe in the
  capture shader (`_YapsTwinA/B/At`) reads each twin's recovered root, its resolved socket and its
  whole chain. The twins' tangents are 77° apart and mirrored (a UV seam), yet their roots agree to
  0.000 mm and 0.000°. Their sockets do not: 0.494 mm apart, same facing, both engaged, both tier 3.
  Vertex 77's chain took 2 header taps and 2 hits, the fine read and the coarse one (deduped);
  vertex 553's 1 and 1, the coarse read alone. The fine block is centred on `floor((root + axis *
  len/2) / size)`, so float noise in a vertex's own root can move it one cell, and a socket at the
  block's edge is then read at the coarse level's precision (a half-float fraction of a larger
  cell, 0.47 mm off here). The plug deform doubles that at the tip: 0.956 mm. So any two vertices,
  not only seam twins, can resolve one socket at two precisions; as the plug moves it should read
  as a faint shimmer or a one-frame crack along UV seams.
  **Proposed fix, not done (Joe's call):** an entry only the coarse read found is looked up again
  in its own fine cell, `floor(at / fineSize)`, and takes the fine position when the same socket is
  there. Precision then follows the socket, not the block. It costs a header tap and up to eight
  octant reads per coarse-only entry, in the shipped resolver, so compile time and CVR's shader
  filter need measuring. Tester: the 45° walk on this plug is the repro.

### 16. Tester reports on the rc builds, 2026-10-22

Six reports from Joe's testers on the latest test packages.
- **Squash: leave chosen materials alone.** Done. `YapsSquashSettings.leaveAlone`; a listed
  squashed copy resolves to its original. The rebuild hash covers the list. SquashPartsTest passes.
- **Squash parts from picked bones (cheeks, thighs).** Done for pairs: `partBones`, two to a pair,
  two pairs at most; painted regions still win. A single centred part (belly) does not fit the
  pair design; painted regions cover it. SquashPartsTest passes.
- **Atlas spots on a user's camera.** Fixed. The clear zeroed the rect's alpha and the writers'
  pixels stayed in the picture. The grab shader's second pass now paints the rect back with the
  background wherever the background is one colour. AtlasSpotsProbe: opaque and transparent
  backgrounds both went from FAIL (561,600 px; 32 spots) to 0 px, plug bend unchanged. PlugGhost
  rerun: the atlas routes still show 0 depth-only px. Left open: a camera drawing over another
  camera's picture with detail in that corner. Closed 2026-10-24 by item 20.
- **Poiyomi 10 coming back as 9.3.** Two Poiyomi installs share the shader name
  ".poiyomi/Poiyomi Toon" (the 9.3 folder in Joe's project carries 10.0.12, the 10.0 folder
  10.0.11), and `Shader.Find` picks one. The lock and Thry's Unlock keep the right file
  (PoiyomiYapsProbe: both go by GUID). The name lookups were ours: `OriginalShaderOf` (re-patch,
  roles, staleness) and the Remove fallback. A patch now records its source's GUID and resolves by
  file first. TwinSourceTest failed on the old code (B led back to A) and passes now. Patches made
  before this keep the name lookup until rebuilt.
- **Poiyomi panel not working after the patch.** Not reproduced. The patch is marked locked so the
  upload leaves it alone, and Thry's panel reads that as a locked material: its settings are baked
  into the copy and edits belong on the original, then a rebuild. PoiyomiYapsProbe's panel
  captures came back blank for the plain source too, so UiOffscreenProbe cannot draw a material
  inspector; that is the probe, not evidence. Needs the tester's steps (which material, what
  "not working" looks like, whether they pressed Unlock).
  **Screenshots 2026-10-23:** a locked patch ("Hidden/Locked/YAPS/...") shows the YAPS sections,
  then Poiyomi's "POIYOMI SHADER UI FAILED TO LOAD" text. Those lines are `[Header]` decorators
  on `_ShaderUIWarning0/1` that only Unity's default drawing shows; Thry never draws them. So
  `YapsShaderGUI.OriginalEditor` returned null and the fallback drew the raw properties: the
  recorded class (`Thry.ShaderEditor`) did not exist in that project. In Attempt Conversion it
  resolves on all 734 patched materials (scratch probe), so ours works where Thry compiles. Most
  likely Poiyomi's own scripts failed to compile in the tester's project (Poiyomi's text says the
  same). To decide: does an unpatched Poiyomi material there show the same text, and what is the
  console's first error.
- **Slower CVR uploads.** Not measured. The editor hooks are cheap; the suspects are first-upload
  compiles of patched shaders and an uber shader shipped when the lock fails. Next: time
  `BuildAssetBundles` of one avatar with and without YAPS and squash.
  **Measured 2026-10-22 [M]: the first upload compiles every variant of every YAPS patch.**
  `Dev/Probes/Perf/UploadCostProbe` runs the CCK's own pipeline (LocalTest, no upload) stage by
  stage, cold then warm. Clone, build processors, save and validation take under 5 s on every
  avatar; the bundle build is the rest. Cold, then warm:
  - 3-plug-patch avatar 92 s then 4 s: 79 s of it compiling 3 YAPS patches (568 variants).
  - 36-patch avatar 851 s then 45 s: YAPS shaders 723 s wall, 9234 s CPU, 4154 variants.
  - 25-patch avatar 683 s then 34 s: YAPS 586 s of 648 s compiling.
  - An avatar with no YAPS: 56 s then 11 s.
  No material was locked by Poiyomi's upload hook. Warm builds hit the shader cache for every
  variant, so the cost lands on the first upload after a conversion or rebuild that changed a
  patch, which is what testers see.
  Per variant, the YAPS vertex stage costs 3 to 4 s CPU against 0.05 s for the same host
  unpatched. `Dev/Probes/Perf/YapsCompileBisect` (one vertex variant, salted): no YAPS 0.02 s,
  plug 2.8 s, squash 1.5 s, socket 0.13 s, all three 4.9 s.
  `Dev/Probes/Perf/YapsVariantLog` (`-ycbLogVariants`): every YAPS variant is built three
  times, mono, `STEREO_INSTANCING_ON` and `UNITY_SINGLE_PASS_STEREO` (85 of 255 vertex variants
  each). ChilloutVR forces single-pass instanced, so the double-wide third is never drawn.
  Options, not built: (a) an `IPreprocessShaders` stripper in the add-on dropping
  `UNITY_SINGLE_PASS_STEREO` from YAPS patches only, a third off the first upload; (b) cut the
  plug and squash code's per-variant compile cost, the bigger lever but real shader work (the
  walk and atlas loops are already `[loop]`). Also seen: the plug's debug overlay and socket
  readout shaders ship in the bundle (48 variants).
  **Bundle split per avatar 2026-10-22 [M]** (`UploadCostProbe -upSplit`, warm bundles rebuilt
  by kind of asset): YAPS patches are 1.60 of 10.3 MB, 16.4 of 239, 9.9 of 159, none of 69 (no
  YAPS). Textures and meshes are the rest of every big bundle. Speed is the compile, not bytes.
  **(a) done:** `Editor/Yaps/YapsStereoStrip` drops `UNITY_SINGLE_PASS_STEREO` from patches under
  `Assets/YAPS/Generated/Shaders` only. Variant log after it: 0 double-wide patch variants (the
  small atlas, overlay and readout shaders keep theirs). 3-patch avatar bundle 10.27 to 9.88 MB.
  **(b) done:** compile cost cut without changing the result, measured by direct fxc (one vertex
  variant, Unity's flags): plug 1.76 to 1.58 s, squash 1.07 to 0.90 s, all three 4.40 to 3.86 s,
  bytecode 151 to 132 KB. One plug walk instead of two, the baked base read inside the shape
  loop, one press loop over arms, hands and plugs, reciprocal reaches. Checks: `YapsDiffProbe`
  (old and new includes on 400 random squash states, 4096 vertices each, 2 seeds) worst 0.002 mm
  and 0.0005 in a normal, rounding; control old against old exactly 0. YapsBendTester on the
  plug: output identical line for line before and after (its one FAIL, the 45° seam at 0.95 mm,
  is old). Tried and worse: scalarised socket arrays, unrolled atlas scan, skipping optimisation
  (2x instructions). The six-deep atlas scan is still most of the plug's cost. Together about
  0.6x the old first-upload compile; existing patches keep the old code until rebuilt.
  Not done: `YapsCompileBisect` end to end on a rebuilt patch; a test in game.
  **Bisect end to end 2026-10-24 [M]**, on the YAPS block as the patcher inlines it at three
  commits, one vertex variant each, run while corpus 429 had the cores (so ±0.3 s on identical
  bytecode). Squash: e2ae6d3 1.29 s / 63.9 KB, 2373354 1.36 s / 65.1 KB, 43aef09 (item 22's
  partner pass) 1.55-2.69 s over four runs, median 1.80 s / 71.8 KB. Plug and socket unchanged;
  all three together 5.4-5.7 s at every commit, so a single 7.1 s reading was noise. Tried:
  the partner pass's hand loop as `[loop]` instead of `[unroll]`, identical output
  (`YapsDiffProbe` both seeds, 0 of 400 scenes differ, 0.000000 mm), squash median 1.56 s over
  four runs and 69.2 KB. Not applied: a quarter of a second at best, while a dynamic index puts the hand arrays in
  indexed registers every frame (unmeasured) and ChilloutVR's shader filter treats each heavy
  `[loop]` on its own. The `plug chain1` case is gone: the chain is a fixed four by design.

### 17. Joe's squash and setup UI review, 2026-10-23 (screenshots image2-7)

All six done 2026-10-23 (Joe: "do all 6", "do both" for item 1). SquashPartsTest gained three
checks: on the old code the scan listed 2 plugs and the overlay kept an undone stroke (red 1.00),
FAIL 2; on the new code PASS (Non Corpus Zone, headless). compile-check all ok. Not seen in the
GUI yet: the new layouts want one look from Joe.
**Rendered 2026-10-24 [M]** (UiOffscreenProbe on AlexaSquash in Non Corpus Zone, dev 43aef09): the
YAPS window's squash card (one **Set up squash**, Remove under it), the component and the Avatar
Tester read as this item and item 19 say. The two renamed toggles are in the window and README
word for word. Joe's own look in the GUI is still wanted.

1. **Two toggles read as one feature.** Done, both ways: renamed to "Soft body physics: keep parts
   from passing through" (Physics ▸ Your call, `softBodiesCollide`) and "Soft body shape: squash
   where parts press together" (Opt-ins ▸ Soft parts, `squashSoftParts`), and each carries a hint
   line naming the other ("use both"). The physics hint shows only with the add-on. Field names
   unchanged, so saved settings keep.
2. **Clothing listed as plugs (bug).** Fixed: `YapsScanner.Scan` skips a component-less YAPS
   plug material that is not `YapsMarks.IsBakedMaterial`; squash copies carry the plug's
   properties with no bake. Test: 2 listed, then 0.
3. **Painting has no undo.** It had one: each stroke's `Save` records the component, and Ctrl+Z
   restored the weights. The overlay did not follow: `Load` kept `_colours` of the same length and
   `Bake` redrew them, so the undone stroke stayed drawn. `Load` now drops them. Test: red 1.00
   after undo, then 0.00. The brush hint now says Ctrl+Z.
4. **The brush overlay is cluttered.** Now: hint, side, radius, strength, one row (Soften edge,
   Copy to other side, Stop), the legend. Wider and Narrower are gone (they read as the radius),
   Clear this side moved to the window.
5. **The Squash regions window reads like a cockpit.** Paint card: pair, the paint button, add or
   remove a pair. Name, side, fill from a bone or shape key and Clear sit in a closed fold "More for
   this pair"; Start over is a closed fold under the build.
6. **The squash card's buttons open different places.** The Setup card has one primary button,
   **Set up squash**, which opens the YAPS Squash component (adding it if missing, no build), plus
   Remove. The component's cards run as numbered steps: Parts, Build, See it work, How it feels.

### 18. Squash in game, reported by other users, 2026-10-23 (image8, image9, a clip)

All fixed on dev 2026-10-23. Existing squash builds keep the old shader code until rebuilt,
because patched shaders inline the include. No fix has been seen in game yet.

1. **Turning in place pressed the pair's middle (image8).** It looked like cloth swinging. The
   cause was in the decompiled client instead. `AvatarClone` builds the wearer's first-person copy:
   it turns off offscreen skinning on the wearer's renderers and sets `forceRenderingOff` on any
   renderer that casts no shadow. The slot skin casts none, so it never skinned in the wearer's own
   view. `SkinnedMeshRendererPreviousVertexBuffer` then served a frozen buffer: the contact plane
   stayed wherever a mirror or camera last saw it, and turning sliced the pair along that plane.
   Renderers named with "[FPR]" are skipped by the copy, so the helper is now `skin [FPR]` (Owned
   finds it by component, so older builds still clean up). SquashPartsTest: FAIL on the old name,
   PASS on the new.
2. **The squashed mesh exploded for other players (image9, the clip).** What spoils the buffer on a
   remote copy is NOT proven. It may be a stable client binding a disposed or reused buffer, or
   NetIK order. The shader now checks every skinned slot against its matrix (0.5 m, 1.5 m for arm
   slots) and treats NaN or infinity by its bits, because fxc folds a float NaN test away. If any
   slot is off, all slots are read from the matrices, and limb radii come from the matrices' bone
   lengths. `YapsDiffProbe -ydWild` (new) spoils the buffer four ways. Old code moved vertices by
   garbage 27.2, huge 56035, NaN 2.7 reaches, with 52471 NaN vertices. New code: 0, 0, 0, and no NaN.
   The sane diff, seeds 1 and 2 with 400 scenes each, is still 0.000000 mm. compile-check and
   check-defines are ok.
   Still open: the buffer is two skinning passes old by design, so a very fast turn can still
   shift the contact a little (a LastFrame provider could compensate). What decides the remote
   cause: the viewer's client branch (stable or beta), and whether it happens with that one avatar alone.
3. **Tessellated Poiyomi 10 read the wrong vertex (Joe's lead, 2026-10-23: "tessellation and loop
   limits").** Poiyomi 10's Pro Tessellated shaders keep one `appdata` with `vertexId : TEXCOORD6`
   under `POI_TESSELLATED` and `: SV_VertexID` otherwise. The patcher's struct reader does not follow
   `#if`, so in the tessellated passes the deform read `v.vertexId`, a mesh UV channel (zero when the
   mesh has none): every vertex took one vertex's squash share, plug frame and socket weights.
   Poiyomi 9.3's tessellated shaders use two structs and were right. Fix: in a program with
   `#pragma hull`, a vertex id field the struct declares twice is not trusted, and the entry (a real
   vertex stage) takes `SV_VertexID` as a parameter. `IncludeVertexTest`: WRONG on the old code, PASS
   on the new. On the real Poiyomi 10.0 Pro Tessellated (copied into Non Corpus Zone under a probe
   name): old, all four passes read `v . vertexId`; new, all four read `yapsVertexId` and the patch
   compiles. Not proven to be the testers' explosion: ask whether the exploding material is on
   Poiyomi 10 Tessellated. Loop limits ruled out by reading: DX11 puts no cap on a `[loop]` count;
   fxc's limit is on unrolling, which fails the compile rather than moving vertices.
4. **Shader Filtering made a part fly apart (Joe's report on one avatar, 2026-10-23: one breast
   and one butt cheek break, only sometimes, and toggling Settings ▸ Experimental ▸ Enable Shader
   Filtering changes it).** The filter is the client's `DxbcShaderFilter.dll`, a native hook on
   the device's Create*Shader that rewrites the bytecode. Read from its output on our compiled
   shaders: one counter for the whole program; each loop pass adds its body's instruction count;
   at the end of every pass and at every `continue`, the loop breaks once the counter reaches the
   Loop Limit (128 to 2048); and a loop whose counter is already past 8192 at its top does not run.
   The squash part loop costs 2022 a pass, so at every limit it ends after its first pass. A vertex
   of parts 1 to 3 left at part 0's `continue`, and the meet loop then read its own part's plane,
   axis and contact from registers the loop never wrote: `held = shares x garbage`, long streaks.
   Fix (`yaps_dent.cginc`): the five per-part arrays are cleared before the loop; the loop starts
   at the vertex's strongest part, so the one pass that survives is its own; normals are blended
   after the loop in part order (the lerp chain is order dependent, and the reorder alone flipped
   normals on vertices two parts share); the slot check reads the matrices if the filter cut it
   short (unrolling it cost 22% more compile per variant, this costs 4%).
   Why every earlier editor try came out clean: **the editor's hook never sees an imported
   shader**, only one compiled at run time (canary drawn from source 0.0315 at limit 128, the same
   shader imported as an asset 0.6323, even with the plugin loaded at startup). So
   `ShaderFilterProbe -sfRuntime` recompiles the squash materials' shaders from source in Play
   mode. With it, on the old code, part 1 at limit 128 streaks at rest and pressed; on the new code
   rest is identical to the filter off (YAVG 0.001) at 128 and 2048, and a press by another
   player's hand is lost, as expected (the hand loop gets one pass, the own upper arm). `YapsDiffProbe`
   old vs new, filter off: 0 of 400 scenes differ on seeds 1 and 2 (0.0006 mm, normals 0.00001);
   `-ydWild` still passes. README notes the lost presses.
   Same filter, not fixed: **plugs**. The bake's shape-key loop and the walk along the curve are
   loops, so with the filter on a plug with active shape keys reads its rest shape short of them,
   and the walk stops after a pass or two and lands on the curve's end. Not measured in the editor
   yet (`-sfRuntime` on a plug scene would show it). Unrolling the walk costs compile time on every
   plug variant, so it waits on Joe.

### 19. Joe's squash UI review, round 2, 2026-10-23

Fixed on dev the same day, except where marked open.

1. **The Avatar Tester's tab still read "CCK Animator Tester".** The title was set only when opened
   from the menu; a window restored from a saved layout kept the old one. Set in `OnEnable` now.
2. **The squash component moved to the parts' bone, like a plug or socket** (Joe: "consistency is
   key"). `YapsSquash.HomeFor` is the common parent of the found or picked part bones (the chest
   for one pair, the hips for two), the avatar root while none is found. **Set up squash**
   (`YapsSquash.Ensure`) adds it there, or moves one an older build left on the avatar root, keeping
   its values, then selects it and fronts the Inspector (`YapsSquash.Show`), as the plug and socket
   rows' customise chip does. The Avatar Tester's **Tune it** does the same. SquashPartsTest: five
   new checks, PASS.
3. **Tune it opened a new window on every press, and the windows broke.** It called
   `EditorUtility.OpenPropertyEditor`, one window per call; a property window on a scene object
   outlives Play mode's scene reload, and Joe's Editor.log has the `MissingReferenceException` from
   `YapsSquashEditor.Rebuild` it threw. No window is opened now (item 2), and a stale inspector
   returns early. "Broke the preview button" was not reproduced beyond this.
4. **Squeeze pushed the pair apart.** It moved the follows toward each other but not the skin, so
   the shader read an overlap and, since 7c0c6fe, swung each part away from its partner. Squeeze is
   now another player's two open hands pressing each pair from outside, through the client's own
   hand globals (players 0 and 2, one per pair; the test hand stays player 1), touching at 0 and at
   the part's centre at 1, in Play mode too. "Other players' hands" scales it. Rendered on Kar with
   `SquashPlayProbe -spLook -spPreview` (new flag), Meet off and on: the pair goes together, sides
   flatten, and with Meet on the middle closes on a seam. Images:
   `D:/Claude/scratch/squashlook/kar/prev2_<off|on>/small.jpg` (rows Squeeze 0, 0.5, 1). Pushed past
   the centre, the bottoms pinched into a bridge.
5. **"How it feels" did nothing outside Play mode.** The knobs wrote, but nothing pressed, so nothing
   showed. Preview and knobs are one card now, **See and tune it** (steps 1-3 of 3), and Preview starts
   at Squeeze 0.5 so a knob shows the moment it moves.
6. **The painter came inline, and the build went last** (Joe: "similar to all other UIs where the
   last thing you do is bake the squash at the bottom"). The component reads top to bottom: 1 Which
   parts squash (Part bones, Materials left alone), 2 Paint them (the region painter, drawn in the
   inspector, which owns and destroys it; "Paint regions" starts it when nothing is painted yet),
   3 Tune how it feels (Preview, the knob folds, Animate it as a closed fold), 4 Build or Rebuild,
   with Remove under it. *Tools ▸ YAPS ▸ Squash regions* now selects the component. Fill from a bone
   takes a **Bone** picker instead of the Hierarchy selection, since selecting a bone closes the
   inspector. The region colours hide while the preview runs, unless painting. SquashPartsTest PASS
   (the painter still mounts alone for tests and probes); the inspector rendered with
   UiOffscreenProbe on AlexaSquash in Non Corpus Zone. ButtonSmokeProbe's story now sets the bone
   field; not rerun: it converts, so it needs the corpus project, free once run 429 ends.
7. **Round 3, 2026-10-24 (Joe: "can we refine the squash UI still?", a screenshot on Kar, then "do
   all five"). Steps 1 and 2 contradicted each other:** step 1 said the painted pairs win and still
   showed two empty lists, while step 2's subtitle said "only if the bones miss a part", false once
   the painter had started from the bone names. Now three steps: 1 **Which parts squash** names the
   source first (painted, picked bones, or bone names) and holds the painter; **Paint them** opens it
   when nothing is painted. **Part bones** shows only while it holds bones or after **Pick bones**,
   with a line saying the painting wins when both are set. **Materials left alone** is a closed fold
   whose header counts them. The painter says so when picked bones are in use, and at two pairs a
   line under the greyed **Add a pair** says why. The "stripped at upload" notice is shared by every
   YAPS inspector and stays. **Meet is on by default** (Joe: should it stay off? "nah"), in the
   component and the patched shader's `_YAPS_SquashShape.w`; a component saved before keeps its
   value. compile-check 7/7, check-defines 8/8. **Rendered 2026-10-24 [M]:** three steps, Part
   bones hidden while empty, Materials left alone closed with "none", Add a pair greyed with its
   line, Rebuild last. Not seen in the GUI yet.
8. **The painted colours only while painting, 2026-10-24** (Joe, a screenshot on Kar: "I cannot
   test the squish when the intrusive overlay is on top of the avatar"). The region overlay drew
   whenever the component was inspected with pairs painted, hidden only under the inspector's
   Preview, so the test hand and Play mode pressed a part under the colours. It now draws only while
   **Paint in the Scene view** is on. Fill this side from and Copy to other side change the weights
   with no colours in sight until painting resumes. compile-check 7/7, check-defines 8/8.

### 20. The atlas paints dots on viewers' screens. Joe, 2026-10-24, fixed on dev, Joe to check in game

Joe, in game: "the dots in the right hand corner, atlas is leaking to user screens, did we fix
that?" ~~Not fixed.~~ **WITHDRAWN 2026-10-24: it was half fixed, in 04da441 (2026-10-22, the
"Atlas spots" line under item 16), which this record missed.** The payload pass writes colour, so
every occupied cell paints a pixel or a few,
noted 2026-09-03 (`docs/YAPS5.md` "One cosmetic item is now visible") and never queued. The writers
draw at `Background-945`, ahead of the scene, so the dots hide wherever the world draws over them
and show wherever it does not: a void or no skybox, and a camera clearing to transparent
(`YAPS5.md`, the size gate's second job). Every socket avatar in view writes into every viewer's
frame, so the dots reach other people.

- First, confirm the screenshot's dots are the atlas: the same spot with no socket avatar in view.
- Fix candidates, unmeasured: (a) grab the frame before the payload writes and copy those pixels
  back after the plugs' grab at -943: one more full-screen grab each frame a socket is in view;
  (b) carry the payload in alpha alone, as the clear already does (`ColorMask A`): invisible, but
  about four pixels a cell and a protocol bump, so mixed builds cannot see each other.
- **(b) rejected, 2026-10-24.** Four alpha pixels per payload pixel, stacked down so the width
  holds, cut the cells a view holds: 720p 4096 to 1634, a 1024 mirror 4096 to 1860, a 512 one 1890
  to 450, and the atlas off below about 480 px square (was 240). Header reads stay one tap, a hit
  costs 16 taps not 4. Fewer cells means more sockets lost to clashes (about 0.5% at 1634 cells
  with 120 in view, 6% at 450). Joe picked (a).
- **(a) done, 2026-10-24.** 04da441 painted the rect back with one colour sampled from three
  pixels beside it, and painted nothing where they disagreed, so the one case left was a camera
  drawing over an earlier camera's picture. Now the clear grabs the screen first (`_YAPS_Under`,
  before its own pass at Background-946) and the grab's second pass copies that back over the
  rect by the fragment's own pixel, each eye from its own slice. Any background, any number of
  old-build avatars in view: one new-build avatar puts the whole rect back. AtlasSpotsProbe gained
  that case (a picture blitted under a depth-only camera): FAIL, 561,600 px (the whole rect, alpha
  zeroed plus the payload), to 0 px; opaque and transparent stay 0; plug bend 27,747 px unchanged
  in all three. Clear and grab compile with and without STEREO_INSTANCING_ON. Cost: a second
  full-screen copy per camera per frame while a YAPS avatar is in view, unmeasured in game.
- Still to do in game: confirm Joe's dots were the atlas (and which build showed them: an avatar
  uploaded before 2026-10-22 has no paint-back at all, though one current avatar in view now
  clears it), and look at the corner in a void with a current build. The right-hand corner is
  odd: the rect sits top LEFT of each eye.

### 21. ChilloutVR's October 2026 status report: what to check. Joe, 2026-10-24

From <https://chilloutvr.net/blog/status_report_october_2026>, read 2026-10-24. Game Server 2 is
due on the Nightly branch within days; none of this is in stable yet.

1. **Local test avatars show as the default avatar, labelled "Local Test", to other players** under
   GS2. Two-client checks need uploaded (private) avatars, not Build & Test.
2. **Universal Scaling**: any avatar resized from 20 cm to 5 m, set up for it or not, with gestures.
   Check plug reach and bend, sockets, the atlas, squash reach and hand size, contacts and cloth at
   both ends.
   **A skinned plug keeps its baked length, measured 2026-10-24 [M]** (`YapsBendTester -yapsScale`,
   which scales the avatar's root and every test place with it; the test plug, 0.649 m). Scale 1
   passes. At 2 the plug is 1.298 m but the shader still calibrates 0.649 m: the atlas misses
   every socket at 0.8L, the 0° walk stretches an edge 59.9x and steps 16x the socket's move, 24
   checks fail. At 0.5 (0.324 m) it bends 45 to 423 mm toward sockets 1.8 lengths away, 11 fail.
   Why: Unity skins into world space, so `unity_ObjectToWorld` is identity on a skinned plug, the
   frame recovered from the normal and tangent carries rotation only, and `_YAPS_BakeScale`
   follows animated bone scale, not the root's. Assumes ChilloutVR scales the root, unconfirmed
   until the Nightly. The neighbour checks fail at both scales too, not yet separated from this.
   To decide: where the shader can learn the avatar's scale (a parameter the client drives, or
   measured from the skin), then a fix and a rerun at 0.2, 0.5, 2 and 5.
   **The skin carries it, measured 2026-10-24 [M]** (`Dev/Probes/SkinScaleProbe` with
   `Hidden/SkinScaleCapture`, which records what the vertex stage receives on the plug's slots;
   D3D11 GPU skinning). Root 1, 2, 0.5: every plug vertex's normal and tangent arrive 1.0000,
   2.0000, 0.5000 long, the plug spans 1.28, 2.57, 0.64 m, and `unity_ObjectToWorld` scales by
   1.0000 at all three. So skinning does not renormalise, and |normal| over the bake's normal is the
   scale, per vertex. A fix has two halves: the recovery subtracts the baked offset unscaled, so at
   2x each vertex finds its root displaced by its own offset (the 59.9x stretch with seams shut),
   and the length. Bone scale the bake already follows (`_YAPS_BakeGirth`, `_YAPS_BakeScale`)
   lengthens the normal too, so it divides out through the baked normal's direction. Unmeasured:
   CPU skinning, a blendshape's normal deltas at weights other than the bake's, and ChilloutVR.
   **Fixed for the plug, measured 2026-10-24 [M].** Bake records the shaft's median skinned normal
   length as `_YAPS_BakeSkinScale` (1.0000 at root 1, 2.0000 at root 2); the shader divides the
   live normal by it, with the bone scale taken out, scales the baked offset before the recovery
   and the length after. 0, an older bake, is the old path: the tester at scale 1 without it
   matches the old shader line for line. With it (`-yapsSkinScale 1` standing in for a fresh bake):
   0.2, 0.5 and 1 pass every check (0.5 was 11 fails), 1.6 fails 3, 2 fails 14 (was 24), 5 fails 17.
   What is left is the atlas, not the scale: its top cell is 1.28 m and the scan covers 1.2 plug
   lengths, so a plug past 1.07 m long misses sockets at the far edge (1.6, a 1.04 m plug, finds
   all 50 everywhere; 2 misses 1). The 1.6 leftovers, the 6 cm neighbour pair (a fixed gap, read
   through cells sized for a plug 1.6 times as long), one 90° walk-in and one scenario, are not yet
   separated from that. Existing plugs need a re-bake. Still open: sockets and squash reach under
   scaling, and all of it in ChilloutVR.
   **A skinned socket mesh opens at its baked size, measured 2026-10-24 [M]**
   (`Dev/Probes/SocketScaleProbe`, one scale per Unity process: re-scaling the root inside one left
   the skin at the first size. `-sockMaterial` put an orphaned body-socket bake back on its body,
   34453 vertices, 2 shapes, the contact channel's depth forced to 1). The mesh reads 1.6061 avatar
   units across at 1, 2 and 0.5, so the root's scale reached the skin; the widest opening reads
   0.0098, 0.0049 and 0.0195 avatar units, the same 9.8 mm in the world at every size. A quarter turn
   moves every vertex's opening by 0.0° from its unturned move turned. ~~So the opening does turn
   with the avatar (the shader comment saying the deltas keep the bake pose is wrong).~~ **Withdrawn
   the same day:** a batch run skins the GPU mesh once and never again (blendshape weights and scale
   set later never reach it; the closed mesh still turned exactly), so the turn came from the draw
   matrix carrying the rig's rotation, which says nothing about ChilloutVR's GPU skinning, where
   the matrix was read as identity (memory, not re-measured). Whether a turned avatar's opening
   turns in game is open; the shader comment stays until it is measured there.
   **Fixed, measured 2026-10-24 [M].** A skinned socket bake records `_YAPS_BakeSkinScale`, the
   median skinned normal length of the vertices its shapes move (it took the plug's shaft rule
   before, meaningless for a socket); the shader scales the position offsets by the live normal's
   length over it and the normal and tangent offsets, stored per unit length, by the live lengths.
   0, an older bake or a plain mesh, is as before, as is a host with no normal input. The probe now
   bakes the socket again (`-sockShapes`) and checks it against the stage shapes themselves, skinned
   by Unity on the CPU (which lands 0.0000 from the GPU's closed mesh): before, the opening was
   0.500 of the shapes' length at 2 and 1.999 at 0.5; after, 1.000 at 0.5, 1 and 2, 0.0° off their
   direction, the widest 0.999 of the scale 1 run's; baked at 2 and shown at 1 (skin scale 2.0000), 1.000 too. Sockets need building again. Plain
   socket meshes take their scale from the matrix and were not measured. Beside it, by reading only: a DPS plug's length rides its
   marker light's intensity, which no transform scale reaches, so a resized plug opens a light-read
   socket as if it were its baked length. Squash reaches are kept in each follow node's units, so
   they should scale; not measured.
3. **Prop partial sync**: the server hands out control per part of a prop, and Disallow Theft is now
   enforced. `YapsPropBuilder` already leaves it off on fresh props, but who writes a prop's synced
   value changes: retest the prop contact channel on the Nightly.
4. **Avatar settings send only what changed after the first full set**, and distant avatars get
   fewer updates. Retest the synced YAPS channels (owner id, contact-driven parameters) on the Nightly.
5. **Unity 6.3 LTS** is being evaluated after GS2. Nothing until a 6.3 CCK exists; then compile and
   run the corpus against it.
6. Watch only: Lookalikes (baked stand-ins; view-based shader effects are not processed, so the
   atlas should simply be absent from them), server-side SPI patching of pre-CCK 3.4 content (not
   ours), and a URP proof of concept that keeps grab passes (no move planned).

### 22. A spin flattens the pair down a seam. Joe, 2026-10-24 (two screenshots, Kar in game)

Joe: "if I spin still, it flattens the boob like they are pushing together but they are not ... if
one boob is pushing against another boob, you'd expect both boobs to move together to the right";
"meet should only occur when it detects or sees that both boobs are being pushed against each
other. And then it can do like a marshmallow style spillage in the middle and not just a weird like
plane collider"; and take a playbook from Poiyomi's glass squish.

1. **WITHDRAWN 2026-10-24: the shunt is gone, see 5.** One part swung into a still partner
   shunted it. Fixed on dev, never seen in game; did not hold on Kar, see 4. The
   overlap one follow drove further toward the other than the other drove back moves the other
   aside (swung about where it meets the body, as a bend), and only the rest presses the two
   together. Read as the difference of the two swings: in batch Play mode on Alexa each follow sat
   0.95 reaches off its edit-mode rest with nothing pushing, 0.30 of it inward, so a first version
   measuring each swing alone read the still part as pushing back and never shunted.
   - Evidence: `SquashPlayProbe -spLook -spSwing -spMeet` (new flag) on Non Corpus Zone's
     AlexaSquash, old include against new: one bone turned until the pair overlaps by 0.14 and 0.25
     reaches, then both by 0.59. One-sided, the old shader pressed a flat face down the middle and
     the new one moves the still part out of the way; both-sided, the two renders match.
     Images `D:/Claude/scratch/squashlook/swing/cmp_*.jpg`. Squash builds need a rebuild to get it.
2. **WITHDRAWN 2026-10-24: the shunt is gone, see 5.** A hand pushing one part into the other
   shunted it too, never seen in game. Joe, the same day: "do those". Each part also runs its partner's hands, when that
   partner could reach it, and keeps how far they bend the partner its way; what one part's hands
   bent it further than the other's bent back shunts the other, as a swing does, and the plane
   sits where the fronts end up. Seen from the pushed part alone, the other was never pushed and
   both pressed flat down the middle. The partner's pass doubles the hand loop for those vertices;
   the shader filter can cut it, which falls back to the old plane.
   - Evidence: `-spHand` (new flag): the pair swung to touching, then another player's open hand
     on the first part's outer side, 0.4 and 0.8 reaches in. Old, the pushed part flattens at the
     middle and the other stays; new, both move over together, the seam with them. `cmp_hand_*.jpg`.
3. **The meet swells instead of a rim. Done on dev, Joe to judge in game.** Poiyomi's glass squish
   turned out to be a plane too: it projects the skin onto it, starting a centimetre short, and
   spreads it a few millimetres. What reads as marshmallow is volume coming out round the contact,
   so what the meet pushes back now swells out round the patch, most at the plane and dying away
   over the rounding width, sized to hold the cap the overlap pushed back (a patch of radius
   sqrt(2 reach deep)); the normals follow the swell. The old spill went half the push sideways
   inside the patch, where a flat face sliding shows nothing, and a rim half that size a width out.
   Spill keeps its range; 1 now means the volume kept.
   - Evidence: `-spSwing` gains a both-sided 1.2-reach shot. At 0.6 the swell is a few
     millimetres on Alexa and the renders barely differ; at 1.2 the zebra shows the sides bulging
     round the seam where the old ones creased into it, and the top V partly fills.
     `cmp_meet_both_deep*.jpg`, `crop_meet_deep.jpg`. Rest renders identical old and new.
4. **On Kar a part turned into its partner tore the pair open. Fixed on dev by 5, Joe to judge in
   game.** Joe, 2026-10-24
   (screenshots of the tester's bone drag, then "its still folding into a void? when I have meet on
   enabled?"). Measured on his 12:44 conversion: one breast's bone turned 0.58 reach into the still
   partner. The skinning alone presses the two together cleanly. With the squash, both parts end up
   further apart than at rest, and the band between them, half each part's share, stretches into a
   tube. Meet adds a fold at the top of the cleavage that reads as a dark hole; with Meet off the
   fold goes and the tube stays. Every include from before 7c0c6fe to now draws the same tube, so
   none of the last two days' changes made it, and 1 above never fixed it here: it was measured on
   Alexa only. Kar's pair rests exactly touching, which Alexa's was not shown to.
   - Joe, on seeing it: "its the flat plane itself I have an issue with". The dark fold is the
     meet's flat face, turned away from the light; the meet should not press to a plane at all.
   - Evidence: `SquashPlayProbe -spLook -spSwing` on a scratch scene holding only the converted
     prefab (`AvatarBridgeOutput/SquashTest/KarMeetDrag.unity`, to delete once fixed), with new
     shots: `_raw` (the skinning, the squash left out) and `-spNoMeet`. Images
     `D:/Claude/scratch/meetdrag/kar-drag-meet.jpg`, the includes `meetcmp/sheet.jpg`.
5. **The pair wraps round instead of pressing flat or pushing apart. Done on dev, Joe to judge in
   game.** Joe, 2026-10-24, after "You're stretching the boobs apart ... You have to push them
   together", chose *Wrap round*: the part that moved keeps its round front and the still one takes
   a rounded hollow its shape, flesh swelling round the edge; pushed alike from both sides, both
   give and the seam curves softly. Nothing moves either part away from the other any more.
   - How: the overlap goes to the part driven less (the lead is the difference of the two swings,
     the hands' bends included, over their sum). The seam is the leader's turned shape bowed by the
     lead, read where the line through the vertex leaves it, carrying on away from the part pressed
     past the leader's outline. The leader gives only where it is inside the other, as far as it
     leads.
   - The turn is about where the part really hangs from: the converter writes it per part
     (`_YAPS_SquashHangToward/Out/Across`, off the rest, in the rest's units) and the shader turns
     the frame about it. `SquashPlayProbe -spPivot` (new) fits the pivot from Play-mode physics: on
     Kar and Alexa, the chain root, where the written point sits. Written in follow units the point
     shrank on Alexa, whose follow plays at 0.556 of its build scale.
   - The leader first came out pointed top and bottom with shards, on Alexa (whose pair rests
     apart) and on Kar's soft swing and soft hand shots: past its fitted outline the seam curved back
     into it, and inside it the press stepped off at its edge. Found by switching stages off one at
     a time: contact off was clean, stretch or swell off was not.
   - Evidence: `-spLook -spSwing -spMeet` and `-spHand` on AlexaSquash and KarMeetDrag, rebuilt in
     place by `SquashTestBuilder.Rebuild` (new). Images `D:/Claude/scratch/alexa_blend.jpg`,
     `kar_blend.jpg`, `hand_cmp.jpg`. Squash builds need a rebuild for the new properties.
   - Pushed together hard from both sides (1.08 reaches each), the bottoms met in a ridge along the
     seam on Alexa. The swell did it, not the press: off, the bottoms met in a small notch. It
     peaked at the seam and fell away from it at once, so the two sides met at an angle. Now it is
     level at the seam and dies away over two widths, holding the same volume; the ridge and the
     point at the top of the seam are gone (`D:/Claude/scratch/keel2.jpg`). On Kar the small point
     at the bottom of the seam rounds off too, one-sided unchanged (`kar_ridge.jpg`).

### 23. Squash turned swapped-in materials pink. A user, 2026-10-24 (two screenshots, via Joe)

The user built squash with the Toolkit on an avatar whose toggles swap materials for outfit and hair
colours. The build put copies on the hair and clothing layers it moves and pointed the swaps at
copies, as designed. In game the hair and clothes drew pink, the skin did not. The user then deleted
the YAPS Squash component instead of pressing Remove, so the swaps stayed on the copies.

- **Remove blind after a hand deletion: fixed on dev [M].** Both Remove buttons asked only whether
  the `YAPS squash (shader)` object was there. Deleted by hand, the window said "Nothing to remove"
  while every layer and swap stayed on its copy. `YapsSquash.Worn` counts the slots and swap keys on
  copies; both buttons and the card use it. `SquashTestBuilder -squashByHand` (build, key a swap,
  delete the objects and component, read the card, Remove) failed on the card before, passes after.
- **The pink itself: explained [user].** The user then deleted every generated YAPS material by hand.
  The swap clips still named them, so each swap put a missing material in the slot, which draws pink.
  Nothing can undo that now: Remove finds each original through the copy's own importer mark, and the
  copy is gone. Two leads checked against it and left untested, still worth a test: the shader sweep
  (item 7) never covered the squash patch, and `FollowSwaps` keys copies into the clips before `Flush`
  writes them while the swap test reads the clip in memory only.
- **B, clips remember their originals: done on dev (Joe's call, 2026-10-25) [M].** Every key
  `RepointInClips` moves off a user's material records that material in the clip's importer userData
  (`yaps-swap-originals`, one line per key: clip local id, path, property, time, the original's
  GlobalObjectId); a move back onto a user's material forgets it, copy to copy keeps the first.
  "User's material" = not in a folder named YAPS and not carrying the squash mark. A clip whose
  importer already holds someone else's userData is not recorded. `MissingKeys` counts remembered
  keys whose material is gone; the squash card warns on them and counts them toward Remove;
  Remove squash and Clean up leftovers (`RestoreMissing`) put them back. Renderer slots are not
  covered: they show in the Inspector. Keys pointed before this have no record, and a rebuild
  over them makes none (copy to copy): Remove, then build.
  `SquashTestBuilder -squashDeleted` (build, key a swap reachable through an Animator, follow,
  reimport the clip, delete the copies, card, then Remove squash, and again with Clean up
  leftovers) failed record, card and put back on both before, passes after.
- **A, repoint only at upload: planned for 5.1 (Joe, 2026-10-25).** Leave user clips untouched and
  repoint on the upload copy through `CCK_BuildUtility.PreAvatarBundleEvent`, the way VRCFury does.
  Covers the plug and socket bakes as well as squash.

### 24. "Converting drops the face-tracking blendshapes." A user, 2026-10-24 (via Joe, their output folder)

A paid avatar with a paid face-tracking add-on. The add-on (BlendShare data, Modular Avatar
prefabs) works in the editor: its installer writes a generated mesh carrying about 70 tracking
shapes (`JawOpen`, `MouthClosed`, `EyeLookUpLeft` and the rest) onto the avatar, or onto a copy,
renames it `<name>-FaceTracking` and adds its tracking prefabs.

- **Read from the user's output (4.7.2) [M]:** the converted Body wears the original FBX mesh, the
  one the add-on's data names as its origin; no tracking object, layer or `v2/` parameter came
  across, though the Modular Avatar bake ran (its own layers are there), and the avatar is named
  without `-FaceTracking`. The report: "No face-tracking blendshapes detected", Body matching 4
  names against the 12 needed. The add-on's names are ones the detector knows. So the avatar that
  was converted never had the add-on on it: most likely the original next to the copy the
  installer made. Asked of the user: convert the `-FaceTracking` avatar, and check the report's
  Face tracking section.
- **Repro with the same packages, run headless the way the add-on's button runs [M]:** Body 372
  shapes as imported, 457 on the add-on's mesh after it, 457 on that mesh converted (dev, Native
  mode: 51 mapped, the add-on's rig of 9 layers removed and reported). The scene saved and reopened,
  then baked on NDMF's Generic platform as 4.7.2 did: still 457 on that mesh. The add-on records its
  mesh change through Undo, so a reload keeps it. 4.7.2 strips and reports any tracking rig in Native
  mode whether or not it detects shapes, and the user's report has no such line: nothing to strip.
  The user says the window's card found 51 shapes; that card reads the add-on's mesh only, so it
  and the output came from different avatars or attempts. Asked for the report from converting the
  avatar the card was on, and whether "lost" means the output or in game.
- **What the user wanted [M]:** the add-on's animator rig, the smoothed layers driving the shapes,
  which Native replaces by design. Both bake platforms carry that rig intact (FX 22 layers, 275
  tracking parameters, the same on Generic and VRChat), so *Keep the avatar's own rig* converts it
  on 4.7.2 as well. Their output's Body still points at the FBX's own mesh: the 4.7.2 window converts
  the avatar its card analysed (a new pick clears the card) and clones with `Object.Instantiate`, so
  the avatar converted was wearing the stock mesh.
- **Diagnostics said "Modular Avatar: not installed" on every machine: fixed on dev [M].** The row
  looked up `ModularAvatarInformation`, a type no release has (checked against 1.17.1). It reads
  `ModularAvatarMergeAnimator` now. `Dev/Tests/DiagnosticsPackagesTest` (each loaded package's row
  must read as installed) failed on it before, passes after.
- **The analysis and the report now say it, on dev (Joe, 2026-10-24) [M]:** an avatar with no
  tracking shapes names a scene root wearing the same mesh (renderer name and vertex count) with them,
  or else a face-tracking add-on installed in the project and not run on it (one known installer
  type, read from the package). The report's line turns into a Warning when it does. An avatar with
  the shapes AND its own rig (face-tracking parameters in its expression parameters or in any
  controller a component hands over, merge components included, so before the bake) gets a *your
  call* card offering Keep; with Keep chosen, Native is no longer recommended over it.
  `Dev/Tests/FaceTrackingAdviceTest` failed 4 checks before, passes after.

## 5.0.1

Speed, held back from 5.0.0 (Joe, 2026-10-21).

### 15. Moved out of 5.0.0 (Joe, 2026-10-22)

- **One avatar's texture estimate grew [M]** (from item 14, archived). "About 268.0 MB of texture to
  download" in every run from 410 to 422 (276.2 once, in 416), 274.0 in 425 and 427, with "1 set
  of materials shares a shader" becoming "2 sets": four materials on one locked YAPS copy and two
  on another. 424 cannot say (a probe had resized this avatar's textures that morning, reverted at
  14:40 from its `TextureSizes.undo`). Candidates: the per-role patches (65a4466), which put
  materials on shared copies by role, or the revert not restoring every importer exactly. Only the
  report's estimate moved; meshes, clips and the `[physics]` block are unchanged.
- **AM1-14, PlayAudio paths with a slash** (from item 9). Whether VRChat plays a PlayAudio whose
  AudioSource object has `/` in its name is untested; one VRChat test before `ConvertPlayAudio`
  moves to `FindByAnimationPath`. Checklist S6.9 is optional.

### 10. Squash, stereo, contacts and the lock: what is left of the speed work

**Squash costs minutes per avatar, 2026-10-16. Fixes on dev and measured (subsets `speed1`, `speed2`, then full run 418: squash 27,468 s to 13,402 s, all passes 34,587 s to 17,181 s).**
The first corpus run with squash on (`CorpusSettings` forces it; a user's default is off) spends
25 minutes in *Squash soft parts* on each of the five fixture avatars and 7 to 10 on the next two,
against under 2 for every other pass combined. During it all 16 shader compilers are busy with
`compileSnippet` on `YAPS/Squash/Hidden/Locked/YAPS/<hash>` passes of about 250 KB each: the
synchronous import compiles every patched pass. The five fixtures share one body and still pay full
price each time, so nothing is reused between conversions; the patched shaders live under each
output folder, and import results are keyed by asset, not content. The same run's report step takes
16 to 50 seconds against 10 without squash. Next: time the pass's sub-steps before choosing a fix.
A candidate is one content-addressed folder of patched shaders shared by every conversion, which
would also make a reconvert skip the compile. A user who opts in waits this long today, so it
belongs in 5.0.0 if the fix is small, and in the release notes if not.

The same log shows the rest of a conversion's overhead: about 400 asset refreshes per avatar at a
quarter second each, every one running every package's postprocessors, and the controller imported
35 to 105 times. Landed on dev at d96bd83, compiled in every SDK and define combination, and none
of it run in Unity yet:

- Compiling a material's passes together was written and pulled: compiling the shadow caster with
  the rest can change which shaders are refused, and waiting for it spun. It comes back only after
  a probe of what the error check costs per pass, alone and in parallel. **Probed 2026-10-16
  (`ShaderVerifyCostProbe`, three patched shaders out of one squash output): not worth bringing
  back.** `ShaderHasError` itself takes 1 ms; the compile happens in the asset database, and every
  pass of all three queued at once with `CompilePass` (16 compiler processes) took 146 s against
  147 s for the path the pass takes now, so Unity already compiles in parallel. The vertex stage
  alone through `CompileVariant`, the cheaper verify that was considered, took 387 s, so that is out
  too. The check does catch a real fault: an HLSL error in the default variant reports as an error
  with its message, while one only a keyword reaches does not, which is the known gap the stereo
  and caster checks already cover.
- The shared content-addressed folder above, the fix for the cross-conversion cost, which no memo
  inside one run can reach. The same probe measured it: the identical text in a second folder
  compiled again in full, about 140 s for the three, where the same folder reused is free (the
  reconvert case, squash 319 s to 15 s in the speed subset).
  **DONE on dev 2026-10-16, ~~not yet run in a conversion~~ run from 418 on (0452e5a predates its
  start, 23:04 that night; squash 27,468 s to 13,402 s, see above):** every YAPS patch, converted or built,
  goes to `Assets/YAPS/Generated/Shaders` (`YapsShaderPatcher.SharedFolder`); the name already
  hashed the source path, its text and the YAPS version, so nothing else had to change, and the
  output-folder argument is gone from `Patch`, `Prewrite` and `Refresh`. The project sweep no longer
  writes beside the old copy either. Corpus digests that list output paths change once. Follow-ups:
  ~~the copies are never pruned, so each YAPS version leaves the last set behind~~ **DONE on dev
  2026-10-17:** *Remove unused patched shaders* (Setup, Props and prefabs card, and the Tools ▸ YAPS
  menu; `YapsShaderPatcher.PruneUnused`) trashes every tagged set whose shader no material or scene
  depends on directly and no loaded material uses, asking first; `Dev/Tests/PruneShadersTest` 6/6 in
  a folder of its own. Generated/Locked is not covered. Not done: deleting
  the folder turns every YAPS avatar in the project magenta until each is converted again (README
  Troubleshooting says so). Writing each copy's `.meta` with a GUID derived from its hash would let
  one reconvert relink the rest; left out until somebody hits it. The stereo patcher's copies could
  share the same way, but their verify depends on the folder (see above), so not without a probe.

- Measured cold, subset `speed2` (2026-10-16, throttled), against 417 (the first run with squash,
  unthrottled), the same eight avatars: 1240 s to 728 s, squash 319 to 148 s, YAPS 440 to 354 s. The
  shared folder started empty, so this is the first conversion after updating, which compiles each
  shader once; `speed1`'s 234 s was the warm case, reusing 417's per-folder copies. On the avatar
  with the most layers the squash patched 16 materials where it patched 26. Digests: four identical
  to 417, and the four SPS avatars changed only by the `fix-417` merge, their Auto Mode rows and
  Auto Socket layers back to 416's counts exactly, plus the C5 texture figure.

- **Next squash target, unlocated:** in 418 the cold squash time outside every import refresh is
  12,505 s of 13,402 s (94%) and tracks the number of NEW patched shaders (r 0.991), about 52.6 s
  each, not the copies saved. Untimed blocks in `YapsSquash.Add`: Shares, Prewrite as a whole, the
  slot loop, Flush, Apply, SaveAssets, FollowSwaps. Timers there (log only), then one cold one-avatar
  run, would find the call. Warm 42-copy avatars also spend ~13 s outside refreshes, same timers.
  **Located 2026-10-17, A/B subset `ab5`** (timers in `YapsSquash.Add` and its pass, log only).
  On the heaviest of the five, 47.7 s: Add 42.2 s, of which Shares 16.9, Prewrite 11.2, Slots 5.5
  (ShaderFor 1.0, Save 0.02, 33 copies), SaveAssets 5.3, Flush 1.8; FollowSwaps 5.1 outside Add.
  Shares and Prewrite are the targets now, on every squash avatar (Shares 1.8 to 3.8 s on the
  small ones). After 5.0.0.
- `RestorePartialOffStates` and `AssertOwnedBindingsEverywhere` still claim, delete and create one
  clip per state. **Measured 2026-10-17 from 418, the biggest cost in Animator merge:** the 418
  digests hold about 2,679 Assert clips (`{layer} {state} restore`) on 59 avatars, and each write
  costs one asset refresh (median 0.183 s, many with no "Start importing" line), so Assert is about
  496 s of the 1,086.5 s merge total (inferred seconds, measured counts). RestorePartialOffStates is
  at most about 12 s. Fix: claim each name with a listing instead of a delete, drop the
  `LoadAssetAtPath`/`DeleteAsset` guards, and write both through the fillers' `CreateRestoreClips`
  batch; about 465 s a full run, 27 of 33 s on the most toggle-heavy avatar. A/B subset: the four
  heaviest Assert avatars plus one squash avatar as control, back to back (names in the commit).
  **DONE on dev 2026-10-17, A/B measured.** Both write through `CreateRestoreClips`, claiming each
  name against a throwaway listing. Subset `ab5` (the four heaviest Assert avatars plus a squash
  control), run A on the old code and B on the new, back to back: **all five digests identical**,
  Animator merge 291.7 s to 85.8 s (159.9 to 44.9, 66.6 to 13.5, 37.0 to 10.0, 16.4 to 6.6, 11.8
  to 10.8). The merge's own timers (log only) now name what is left: `WriteRestoreClips` 14.6 and
  10.4 s on the two heaviest, `RehomeVolatileAssets` 12.3 s on one, GoGo's `PlaceGogoEmotes` 6.0 s,
  `AnimatorAssetSaver.Save` to 4.6 s. After 5.0.0.
- **Latent, not hit in 418:** `WriteRestoreClip` registers its names only in `writtenPaths`, never
  where `OutputAssetPaths.Claim` looks, so an Assert name `{A} {B} restore` equal to a filler's
  `{A B} restore` is claimed as free and the filler's fresh file deleted, leaving its states with no
  motion. Fix: Claim skips `writtenPaths`; an EditMode test in the style of TreeToggleRestoreTest.
  **FIXED on dev 2026-10-17.** `WriteRestoreClip` also reserves each name through
  `OutputAssetPaths.Reserve`, so a later Claim of the same spelling numbers past it.
  `Dev/Tests/RestoreClipBatchTest.cs` builds the collision (a filler's "Hat Off restore" against
  the assertion copy of layer Hat, state Off), plus a partial-off topping, and checks four saved
  files, values at conversion time, and the same four after a reconvert: PASS, and
  TreeToggleRestoreTest still PASS.
- Stereo copies written in one batch, a verified marker that survives a restart, and judging in
  parallel. **DONE on dev 2026-10-17:** every shader a pass, the swap pass or the report check
  is about to judge is planned at once on the thread pool (`ShaderSpiPatcher.Prejudge`; reading stays
  on the main thread), and Judge takes the plan made for its unit, so nothing plans twice (~~nothing~~:
  Stage plans again a shader whose verdict an earlier avatar cached). Each copy's
  includes import in one batch. After a restart a byte-identical copy is verified where it lies and
  rewritten only if that fails, in place of the in-memory marker; no file marker. Same A/B: planning
  3.3 s to 2.5 s, report check 1.4 s to 0.9 s, digest identical; SpiPatcherProbe 34/34 and
  SpiIncludeProbe 5/5. Then the write batch across shaders (`ShaderSpiPatcher.Prepare`): every
  shader a renderer pass or the swap pass meets is staged first (planned, named, written), every
  include of every copy imports in one batch, then every shader in a second, then each is verified
  and reported in the order met, so names and the report match one at a time; failed copies are
  deleted in one call. SpiPatcherProbe 34/34 (collisions and a failing copy among them) and
  SpiIncludeProbe 5/5 again. Saves per-import overhead only; the compile is paid either way.
  A copy that cannot be written (a locked or read-only file) puts the disk back as it found it,
  deleting what it made and restoring what it overwrote, and is refused with the reason; the rest
  of the batch still imports and reports. SpiIncludeProbe checks it fresh and on a reconvert.
  Timed on the old code over three avatars with a warm cache: the copies took 52, 39 and 173 ms
  in all, 35, 31 and 160 of it verifying, so the batch saves at most 8 to 17 ms an avatar there.
  The time is in planning (0.07, 10.2 and 4.2 s, ~~every shader's text already seen~~ **WITHDRAWN:**
  Prejudge stores each verdict before StereoProblems asks, so "by text seen before" equals "judged"
  by construction; these were cold plans) and the report check (to 5.9 s). SpiPatcherProbe 34/34,
  SpiIncludeProbe 15/15.
- **Stereo planning, read 2026-10-17.** The time is PlanStereo's own whole-text regex work, not
  Unity's. The report check's cold text is mostly YAPS shader copies, written after the stereo pass,
  so it is the only judge that sees them: in 418 the 68 YAPS avatars spent 166.8 s of its 167.3 s.
  Where the text really was seen, it is about 4 ms a shader (read and SHA-256, twice per path).
  Ranked, all exact by the code and identical on all 1,415 project shader units in a Python port:
  (1) judge with a verdict-only plan (the fragment eye-index search and depth flags feed only the
  patch; Verdict reads Passes, SurfaceOnly, Adds, Left), Stage plans fully; (2) literal guards before
  the costly regexes (`_renderers` before OffPcPrograms, the function name before each signature
  scan). Together 59-68% of planning in the port, about 208-240 s a full run (Mono unmeasured);
  small next to squash, but 14-24% of a small avatar's whole conversion. (3) The advisor plans one
  shader at a time inline when an avatar is selected, untimed, on the DEFAULT path (patching off):
  Prejudge there first. (4) The report check reuses the units Prejudge read, not a second read.
  (5) Log plans made, not cache hits. Proof: SpiPatcherProbe, SpiIncludeProbe, SpiSweepProbe lines
  byte-identical before and after, and a subset whose `[Shaders]` and stereo diagnostics lines hold.
  **(1), (2), (3) and (5) DONE on dev 2026-10-17; (4) not done.** The judge plans verdict-only
  (`PlanStereo(forPatch: false)`, a `Full` flag so a verdict plan never reaches the patch);
  Prejudge plans a full one beside it only where the verdict sends the shader to the patch; literal
  guards before OffPcPrograms and each signature scan; the advisor Prejudges before its loop; the
  log counts verdict and full plans. SpiPatcherProbe 34/34, SpiIncludeProbe 15/15, SpiSweepProbe's
  843 lines byte-identical before and after. Subset `ab5`: digests identical, Shader SPI patch
  12.4 s to 8.3 s over the five.
- **Contacts: zones grown is the biggest single cost left, found 2026-10-17 by the new timers**
  (`ContactsConverter.Run`, log only). On one avatar with 212 contacts, 282.8 s of the pass's
  283.0 s is growing zones; hosts 11 ms, rest states 4 ms. 112 contacts elsewhere took 1.2 s, so it
  is not linear in contacts: read what one zone's growth costs before guessing.
  **Fixed for 5.0.0 on dev 2026-10-21 [M]:** the cost was attributing each animated shape to each
  zone, a binary search per sample and a sort of lists that were mostly zeros. Only the pushes above
  zero are kept now, the percentile offset by the zeros, and a big zone walks the moved list once.
  The per-mesh shape list is cached for the conversion. Same avatar, default settings: 212 s to
  53.6 s; every zone's push and per-shape reach byte-identical, and the reconvert's report says
  nothing changed. **Then 53.6 s to 12.9 s, same day [M]:** each mesh is placed once per bone
  state into a coarse grid and a zone reads only nearby cells (exact distance test kept); each
  vertex lists the shapes that move it, so a zone walks its own samples, not every shape's
  moved list; and zones sharing a centre and capture reuse the answer while every mesh is placed
  and named the same (206 of 336 asks on that avatar: a socket's receivers sit on one point).
  Zone probe digests identical on 59 zones, report identical, clips identical but for item 11.
  What is left is the samples themselves, 3.3 s scan and 7.0 s attribution.
- **The converter washover, fixed for 5.0.0 on dev 2026-10-21 [M]:** the same avatar, every pass
  timed, original code against now: 474.9 s to 127.8 s, then 88.9 s after the second round.
  - *Rename YAPS objects* 76.8 s to 0.8 s. Its masks list 556,568 transforms and each
    `SetTransformPath` was a whole-mask update: one serialized pass now. Its clips moved a binding at
    a time, two clip syncs each over 3197 bindings: one `SetEditorCurves` call per clip now. Every
    curve and mask entry identical before and after, build ids folded.
  - *Texture sizes* (on by default, off in `CorpusSettings`, so the corpus never timed it) 83 s to
    29.7 s with nothing to change. The project's material index asked the database for each
    material's dependencies, 4 to 46 s; each `.mat` file's own GUIDs are a superset, about 3 s, with
    the database for anything not text. The content check decodes without a mip chain, 11% faster
    and the same verdict on 291 files. **Then the decode, same day [M]:** verdicts are kept in
    `Library/AvatarBridge/TextureContent-1.txt`, keyed by `GetAssetDependencyHash` (file and import
    settings, no extra read), decoded verdicts only. 26.6 s cold to 7.4 s on a reconvert.
  - Laps left in the log: `Texture sizes steps`.
  - *Animator merge*, 19 s, not changed. The guess that its curve writes cost it was wrong:
    timed, every per-binding write in `WriteRestoreClips` comes to 0.3 s. The time is files:
    `RehomeVolatileAssets` 5 s is `SceneAssetRehomer` cloning six textures Fury left inside its
    FX controller, each `CreateAsset` outside any batch (0.7 s each), and `CreateRestoreClips`
    3 s is writing about 485 clips, already batched. Batching the texture clones means a rescued
    material naming a texture created in the same batch; untested, and that path broke before
    (particles drawn as white squares). Open, [M] timings, fix [R].
  - Whole conversion, original code against now, same avatar: public passes 351.4 s to 82.3 s on
    a first convert (4.3x) and 60.4 s on a reconvert (5.8x), the add-on's 123.4 s to 6.6 s. The
    reconvert's report differs from the first only in which 40 of the same 848 contested bindings
    it samples, the order Fury writes its defaults in.

- Probes first, in the copy project: P0 stopwatches over each squash step against `shadercompiler-*.log`;
  P1 whether `new Material`/`CopyPropertiesFromMaterial`/`CopySerialized` carry override tags; P2 lock the
  same stand-ins in two calls and other company, diff the text; P3 how many compiles a line-1-blind
  `NameHash` would save over the 21 probe avatars (build it only if at least one per avatar); P4 Thry
  Unlock on a copy sharing a patch, tag kept vs cleared; P5 a forced Thry exception, does the
  StartAssetEditing block stay open.

- *P0, where the cold convert goes:* not the patcher (about 8 s) and not Thry (5.6 s for one stand-in,
  9.65 s for six in one call). It is Unity compiling each fresh patched shader's default passes, Base, Add
  and ShadowCaster, synchronously in batch, on the FIRST material created on it: `new Material(shader)`
  took 89.9 s in the trigger probe and every later step milliseconds; the squash's first copy 445 s, the
  second 71 s, the rest about 200 ms. *The GUI editor waits the same:* one patch (the 4 MB unlocked-Poiyomi
  one), same probe, throttled alike: `new Material(shader)` 20.1 s in the GUI against 22.8 s in batch, and
  setting it on a copy 8.2 s against 10.1 s. Asynchronous shader compilation does not cover it, so a user
  converting in the window sees the same first-convert wait. **Open:** whether anything avoids it short
  of fewer patches (one shader shared by more materials).

### 13. What the speed work missed, read from run 425 against 417. 2026-10-21

Per avatar, all passes plus save and report, the 83 avatars in both runs. The commit that queued
this names the avatars.

- **One heavy avatar sped up 1.2×** (third slowest public before, 4 min 33 s to 3 min 44 s). Its
  *Animator merge* did not move (104 s to 107 s), so it is not the toggle-heavy shape the merge work
  targeted, and its report step got slower (31 s to 51 s). Next: what its controllers look like, and
  where the report's 20 s went.
- **A light avatar got slower,** 36 s to 43 s: *Animator merge* 22 s to 34 s.
- **Two small passes grew** across the 83: *Point material swaps at the baked material* 1 s to 15 s,
  *Name grafted emote clips* 59 s to 68 s.
- **No cold number for the stereo step.** Run 425 found the stereo copies earlier runs made and
  skipped them: *Shader SPI patch* fell about 95% on every avatar, unique shaders included. So the
  published overall figures (3.6× average, 10.9× heaviest) are converting-again figures. Putting the
  old step time back brackets a first conversion at 2.5× to 2.8× average and 5.7× to 6.8× heaviest.
  Measuring it takes the stereo copies and `Library/AvatarBridge` cleared, then one run, which needs
  Joe's order.

## Major Ideas

The work Joe means to take on after 5.0.0.

### AvatarBridge Chambers: a home world with a test wing. Joe's idea 2026-10-20. THE MAIN FOCUS AFTER 5.0.0, NOT STARTED

Joe, 2026-10-20: a ChilloutVR world under the AvatarBridge name. One side is a home world, fully
built, to hang out and relax in. The other side is a run of test chambers with a clinical
test-facility mood, entirely original in look, story and names. Walk in wearing any avatar and test
anything: whatever the CCK does, whatever the converter changed, whatever YAPS adds. Its own project
apart from the converter, and the main focus once 5.0.0 ships.

**Why a world.** Physics, shaders and sync are only falsifiable in ChilloutVR. Today that proof is a
checklist and a friend with time. A chamber that sets each check up makes it repeatable, for anyone.

**What a chamber can set up that the editor cannot.** A first pass, from the in-game checklist:
- Physics: a drop shaft and jump pads (landing, overshoot, the landing spin), a walkway with stop
  lines (sway and settle), a turntable (body turns), a bed (lying down), a teleport pair and a
  respawn point (cloth after a jump in position), wind if a world can push avatar cloth.
- Mirrors and light: mirrors at several distances, a bay that cycles baked, realtime, point lights
  and none (toon fallbacks, outlines, both eyes in VR), a floor grid with height marks (floating or
  sinking).
- Interaction: seats and beds (GoGo poses, emotes), world pointers aimed at an avatar's triggers,
  props to grab, a viewer's mark for the tests that need a second player.
- YAPS: socket and plug fixtures with marker lights, a portal pair, the glass wall from
  "Squash against the world" above. Where these live is decision 2.
- Readouts: what a world can show about the avatar in it (parameters, contact hits) needs world
  scripting, which is not in stable. Until then a chamber sets up the situation and the player reads
  the result.

**Known constraints.**
- ChilloutVR declined scripting access (2026-08-19) and asked for no AI analysis of their scripting
  implementation. World scripting gets used through its public documentation only, once it ships.
- The clean-room rules carry over unchanged.
- Original look and names. No borrowed franchise, in the world or in anything written about it.

**Decisions for Joe.**
1. Where it lives: a new repository under the AvatarBridge name (recommended), or a GitHub fork of
   this one. A world shares no code with the converter, and a fork of a public repository carries
   its whole history and stays public.
2. The YAPS chambers: a separate adult world (recommended, it matches the package split), an adult
   wing behind a gate in the same world, or none.

**First step, after 5.0.0:** a spike. One lounge room and one chamber in a fresh CCK world project,
uploaded privately, to learn the world CCK's limits before designing the rest.

### A VRChat accessory onto a ChilloutVR avatar: find and attach. Joe's idea 2026-10-24. PLAN ONLY, after 5.0.0

Joe, 2026-10-24: "what if we can also throw them through the convertor? like lets say, a piece of
clothing that I want to put on an already converted avatar, but attach it myself manually after?
lets say, a "plug" thats vrc dependent but I can convert its logic to the avatar? like a find and
attach system??" Recorded as a brand new idea at his word.

**The gap.** Today an accessory converts only on a VRChat avatar: put it on the original and
reconvert, and everything comes across (physics, contacts, SPS, VRCFury toggles). An avatar with no
VRChat original has no route: a ChilloutVR-native avatar, or a converted one edited by hand since.
That is the value: VRChat's accessory market (clothing, props, plugs) opens to ChilloutVR-native
avatars. When the target did come out of the converter and its source is still in the project, the
tool should say that reconverting is the better route.

**Two modes, as Joe put them.**
1. Convert only: the accessory comes out converted, and the user attaches it by hand.
2. Find and attach: the tool also matches it onto the avatar.

**What already exists.**
- Bone matching: MA Merge Armature and VRCFury Armature Link define it. When the accessory carries
  one, read its settings (offset, prefix, bone map) rather than guess. `YapsConverter` and
  `YapsScanner` already read names through Fury's ArmatureLink ids (YapsConverter.cs:954, 1733;
  YapsScanner.cs:309).
- Component conversion: the passes in `BridgeConverter.ContentPasses` (PhysBones, Contacts,
  Constraints, Misc components, Shader SPI patch, YAPS penetration system).
- Logic merge: the Toolkit's Merge animators (`Editor/Toolkit/AnimatorMergeTool.cs`) copies layers
  and parameters into a target controller and names clashes. AAS entries and the 3200-bit check are
  not in it.
- The plug is closest: YAPS's upgrade converts an SPS plug in place. Whether it works on a
  ChilloutVR avatar with no descriptor is unchecked.

**Why "run only the component passes" is not a clean cut.** Read from the pass list, not measured:
- `BridgeConverter.Convert` takes a `VRCAvatarDescriptor`, and `VRCFuryBaker.TryBake` bakes from
  one: Fury builds an accessory only as part of an avatar.
- The component passes lean on the avatar ones: contacts drive parameters made by "Parameters and
  menu", and the later clip passes repoint constraint, contact and collider enable curves inside
  the merged controller.
- A prefab asset can reference only its own objects, so its PhysBones collide with colliders it
  carries. VRChat's player colliders (hands, fingers) come from the descriptor and have to be made,
  or found, on the ChilloutVR avatar.

**Candidate route for the first spike: a stand-in avatar.** Build a throwaway VRChat avatar from a
copy of the target (its armature, a bare descriptor), put the accessory on it, run the normal
pipeline unchanged, then carry back only what is new: the accessory's converted subtree, and its
layers, parameters and menu entries merged into the target's own controller and AAS. It reuses every
pass instead of forking a third path (the converter and the Toolkit already diverge: five of six
audit findings were one-path-only), and Armature Link resolves against the target's real bones.
Unknowns: what the pipeline does with the target's own ChilloutVR components on the copy, and
telling new from touched on the way back.

**Cut.**
1. MVP: convert and attach, no logic. The report names every toggle not carried.
2. Then the logic: layers, parameters, AAS entries, the 3200-bit check, name clashes with the
   avatar's own parameters.
3. A record on the attached accessory, so it re-attaches in one press after the avatar changes.

**Gates only ChilloutVR can settle:** the physics and toggles of the attached accessory, as for any
conversion.

### Squash pair contact: the flat seam is off, a natural squish is open. 2026-10-21

Joe, 2026-10-21, in game on the first upload since 58518d7: pushed together standing still, the
pair pressed into one hard vertical plane down the cleavage, "its doing the behaviour I kinda
wanted? but man, its terrible", and "I would rather let them either use YAPS squish to look
really good or clip through each other". The upload before it was built from a squash made two
hours before 58518d7, so this was its first time in game; that commit had already noted the
flat seam in the editor renders.

- **DONE ON DEV:** **Meet** is off by default (`YapsSquashSettings.meet`, and the patched
  shader's `_YAPS_SquashShape.w` default). The whole-part flatten still runs; what still
  overlaps passes through. A component saved with Meet on keeps it, so an avatar built before
  this needs it unticked or a fresh component.
- **OPEN, the squish that looks good [design]:** why it reads as a knife cut: everything past
  the plane is eased onto it with a give of 3% of the reach (`YAPS_SQUASH_MEET`), so the overlap
  of each part is planed flat and the spill goes sideways. Candidate shape, from parts the shader
  already has: the pair swings apart first; only the true overlap flattens, its edge rounded over
  a width that grows with the overlap depth; the volume pressed back rises in a rim up and out of
  the cleavage, as the hand dent's rim does. Judged only in ChilloutVR; the `-spLook` renders
  never showed the seam Joe saw. Meet stays off until one passes in game.
- **BUILT ON DEV 2026-10-23, waits on the game (Joe: "I agree with the fix", plus "the squash
  deform in general can look a shit-ton better, the way the vertices smooth out"):**
  - Shader (`yaps_dent.cginc`): the partner's press now feeds the bend, so a pair pushed together
    swings apart first, Meet on or off. With Meet on, half of what the swing leaves flattens the
    whole part (`YAPS_SQUASH_WHOLE`) and the rest is the overlap the contact presses back. The
    contact maps v before the stop to KEEP v + (1 - KEEP) v (1 - e^(v / width)), width = the
    overlap depth (`YAPS_SQUASH_ROUND`), so the flat patch is level with the plane at its edge
    and rounds into the part; strictly monotone, so layers keep their order. A rim, -x e^(1 + x),
    pushes the skin just outside the patch away from the pair's line. Normals from the real slope.
  - Bake (`YapsSquash.cs`): named shares get two passes of neighbour averaging over the welded
    mesh (`Soften`), and `Peak` eases its 2x clamp with a smoothstep (it stopped dead at half the
    peak and at the last weighted vertex). Layers take a Gaussian (1 cm) average of the skin's
    shares within 3 cm instead of the nearest vertex's, easing into their own weights from 3 to
    4 cm (a top bridging the cleavage stepped along the 3 cm line). Painted regions keep their paint.
  - Evidence (Non Corpus Zone, Alexa, `SquashPlayProbe -spLook [-spMeet]`, zebra stripes): Meet
    on, old = a dead-straight crease down the top and the skin and a pinched V at the top's edge;
    new = a rounded cleavage, no pinch. Meet off still crosses through in the middle, as before.
    Alexa's skin weights are mostly solid, so the share smoothing showed no visible change there.
    `YapsDiffProbe -ydWild` PASS, 0 NaN; sane scenes 297/400 differ by design, 0 NaN, farthest
    move 8.19 reaches against 8.34. Compile per squash variant 1.28 to 1.35 s (fxc). SquashParts,
    SquashRefused, SquashSlot and SquashStatus tests PASS. Squash builds need a rebuild to get it.
  - Joe, 2026-10-24: Meet on by default (queue item 19.7), ahead of the game check.
  - Kar rendered 2026-10-23 (Attempt Conversion, squash rebuilt, `-spLook [-spMeet]`, old shader =
    `7c0c6fe^`'s include swapped in on the new bake). Meet on: the old kink where the two bottoms
    meet is gone in the side view, the crease down the middle stays straight; mean pixel change
    3.5 grey levels at the hard squeeze, 0.3 at rest. Meet off: the parts still cross through, and
    the swing now tilts them so the crossing reads as a V at the top and a notch below instead of a
    straight line (0.7). Shares: no visible change at rest. The look probe's shader includes the
    DEPLOYED `yaps_dent.cginc`, so an A/B needs the old include swapped in, not an old scene.

### Show it on the avatar: an in-scene overlay for every tool. Joe's idea 2026-10-20. PLAN ONLY, after 5.0.0

Joe, 2026-10-20, after an hour lost in the squash windows: the tools say what they did in
reports and inspectors far from the avatar, and the reader has to translate. He wants the scene
itself to show what AvatarBridge and YAPS are doing to the avatar and what can be changed: our own
overlay system and UI, custom camera angles, animations, possibly the Game view, "so visually
impressive and incredibly intuitive" that nobody needs the README. Strict planning only; he will
expand on it. Same repository unless it turns out to need its own; say so if it does.

What it would cover, every tool: the converter's report, the Avatar Tester, YAPS plugs and
sockets, the squash and its painter, physics, contacts.

Method to work out first, before any code:
1. **One workflow per tool, written down.** What the user is trying to learn or change, in order,
   and which on-avatar picture answers each step. The squash failed exactly here: four windows,
   each pointing at another.
2. **Where it draws.** Unity's Scene view overlays (`UnityEditor.Overlays`, the transient kind
   now used by the squash brush) for controls; Handles and mesh overlays on the avatar for state;
   a dedicated camera rig for framed shots (the part being explained, from the angle that shows
   it). The Game view is an option for a presentation mode; it costs Play mode.
3. **One visual language.** Colours, labels and callouts shared across tools, so a reader learns
   it once. The two-platform colours of the window are the start.
4. **Measured, never decorative.** Every picture reads the real state (the bake, the slots, the
   report), so it can never claim something that is not there.

The squash is the pilot: the brush panel in the Scene view (shipped on dev 2026-10-20) is the
first piece. Open from the same day, for the pilot:
- Hold a pose in Play mode: grab a part, lock it there, and see the squash at that angle. Today
  only Pause holds it, and only the test hand moves while paused (the hint says so; not tried by
  Joe yet). Edit mode's Squeeze holds the pair, but only toward each other.
- The fold hints under How it feels name how to see each group; a live picture per knob would
  answer it better.

### Squash anywhere: any bone, no two-pair cap, each other, slaps. Joe, 2026-10-24. PLAN ONLY, for 5.1.0

Joe, 2026-10-24, on reading this plan: "keep those as plans for now. We'll do it in 5.1.0 ... Get
everything functional for 5.0, and then we can move on to the new projects and new ideas."

Joe, 2026-10-24: "fuck the limit": keep the name detection for breasts and butt, but let a user put
the squash on any bone (thighs, face cheeks), weights from that bone's skin weights with the brush
only for touch-ups, and warn past a point that performance degrades. Then avatars squashing each
other, walls and world objects squashing the avatar, and a slap that ripples. Thinking so far,
nothing built:

**1. No hard cap.** Four parts is a storage layout, not a GPU ceiling: `_YAPS_DentOwn` holds four
shares per vertex in RGBA, every per-part number is one component of a `float4`
(`_YAPS_SquashReach` and its five siblings, `_YAPS_SquashPair`), and the 20 provider slots are laid
out for two pairs. Reworked:
- Per vertex, the two strongest parts as index and share (`r` index, `g` share, `b` index, `a`
  share): up to 255 parts, and the vertex loop drops from four passes to two, cheaper than today
  and a smaller bill under Shader Filtering's loop limit.
- Per-part numbers in a small part table texture, read with `Load`. Not material arrays: Unity does
  not save arrays in the material asset, so they would not survive an upload.
- Two provider slots per part (follow and rest) plus one helper skin vertex each; the arm, back
  and socket slots move after them.
- A part is a pair (its partner presses it, as now) or single: a belly, a tail, one cheek, with no
  partner press, only hands, plugs and later the world. The belly was turned away (queue item 16)
  only because the design was pairs.
- UI: name detection keeps finding the pairs; **Add a part** takes a bone (and an optional partner)
  and fills its weights from that bone's skin weights, which **Fill this side from The bone**
  already does; the brush is for touch-ups. The warning comes from measurement, not a guess: cost
  grows with the skin on the squash path (GPU, per vertex) and the slots (CPU, every frame), not
  with the shader's size, so measure frame time against squashed vertex count on the corpus before
  picking a threshold.

**2. Each other.** Tier 1 is nearly free: the client already gives every shader each player's chest
and root positions (`_CVR_PlayerChestPositions`, `_CVR_PlayerRootPositions`, `w` their viewpoint
height) beside the hands the squash reads today. A torso capsule per player from chest to root,
scaled by height, presses the parts like a big hand: a hug flattens a chest, a lap presses a
butt. No sync, no atlas, every client. Tier 2 is real shapes: each part published into the atlas as
sockets are (centre, axis, reaches), so another avatar's squash sees it and both sides flatten
against each other, each deforming itself against the other. That bumps the atlas protocol (mixed
builds blind to each other) and waits on queue item 20 (the atlas dots on viewers' screens).

**3. Walls and world objects.** Planned in the next record, "Squash against the world": a lag body
per part on a spring, pushed back by world colliders, its offset read as a press. Its gate 4 (all
20 slots used) is what point 1 removes. Its gates 1 to 3 (avatar collider layers for the wearer and
for remote copies, the player capsule, content filters) need a look at the client first.

**4. A slap that ripples.** The squash shader has no memory, and a ripple needs time. The bounce
spike (`Dev/Probes/BounceSpikeBuilder.cs`, memory note on CVRBlitter self-update) proved the memory
in game on 2026-10-14: a blitter writing a render texture into itself once a frame. Per part, a few
texels hold the hands' last positions, so a hand's speed at contact is known; a fast hit stores the
impact point, time and strength, and the vertex shader adds a damped wave spreading from it over
the part plus a wobble of the whole part. Own hands and other players' hands both work, from
positions the shader already has. Open: the blitter on remote copies, its cost, both VR eyes.

**Order proposed:** 1 first (the biggest ask; every piece is already proven in game), then 2 tier 1
(reuses the hand code), then 4 (half spiked), then 3 (client read first), then 2 tier 2 (after item
20). Not in 5.0.0, which is in its release gate.

### Squash against the world: an idea, 2026-10-17. NOT STARTED, after 5.0.0

Joe's idea: a soft part pressed flat against a world surface, a pane of glass for example, from any
world object with a plain Unity collider. He says Poiyomi already has a shader-only version; this
would reach further. Thinking so far, nothing built:
- PhysX is the only way an avatar can feel a world collider without a script. Rigidbody,
  ConfigurableJoint and colliders are on the avatar whitelist. Contacts and pointers never see a
  world collider, and the hands the squash reads today come from the client's shader globals, which
  carry players only.
- Shape: one lag body per soft part (a sphere collider, no gravity, continuous speculative
  collision) on a spring to a kinematic target at the part's rest place, the layering the PhysBone
  parity record ("Layered on MagicaCloth2") built and measured in batch Play mode. A wall stops the
  body where the part would go through it, so the body's offset from its target is the contact, a
  normal and a depth. That goes to the shader as one more provider slot per part, and the shader
  flattens every vertex past the plane onto it and spreads the volume sideways, as the pair squash
  does.
- Nothing to sync: every client simulates its own copy against the same world colliders.
- Gates, in order: (1) which layer the client puts avatar colliders on, and whether that layer
  collides with world geometry, for the wearer and for remote copies (the client processes avatar
  colliders: content filters, layers, an added kinematic body; read that code first); (2) whether
  the body fights the wearer's own player capsule, which it sits inside; (3) content filters
  removing avatar colliders for some viewers; (4) slots: all 20 `_YAPS_SquashFollow` slots are in
  use; (5) invisible world colliders (boundary walls, ramp hulls) would flatten the part against
  nothing, so it wants a menu toggle; (6) glass usually has a collider, and rarely writes depth,
  which is why this route and not a depth-buffer one (depth is also per viewer's camera).
- Cost: the joint spike measured about 15 us a chain per avatar past the first, at 90 Hz.
- Not in 5.0.0. It starts with a read-only look at gates 1 to 3 in the client.

### Three plans from Joe's ideas of 2026-10-17. NOT STARTED, after 5.0.0

Joe, 2026-10-17: grabbed clothing driving a blendshape ("_Stretch is the one we need to fix. Everything
else can be the GrabbyBones mod"), Blender's avatar edits inside Unity for the Toolkit (blendshapes,
visemes, a smooth brush "mesh fixer"), and the Toolkit as its own download for people already on
ChilloutVR. His rule for all three: adopt a modern tool that already does it, build only what nothing
covers. Each plan ends in a Decisions table.

**Decided 2026-10-17 (Joe): the recommended default on every row of all three tables.** For grab, the
rows that left it to him take the precedent: converter default **off** (as `syncSocketDepthForOthers`),
package **public**, release behaviour after the spike. Toolkit row 7 stands: after 5.0.0. All three
wait until 5.0.0 ships.

### PhysBone `_Stretch` on ChilloutVR: grab clothing to drive a blendshape. PLAN 2026-10-17, NOT STARTED, after 5.0.0

Joe's brief: `_Stretch` is the one to fix. GrabbyBones covers the rest. Tags: **[M]** measured, or read in code or a record. **[R]** reasoned. **[GAME]** only settled in ChilloutVR.

**Existing tools, checked first.** Nothing found ships `_Stretch`, `_Squish` or `_IsPosed` on ChilloutVR [M, as far as searched].
- GrabbyBones exposes `_IsGrabbed` and `_Angle` only, plus their `#` local twins. Its README lists pose as "Waiting on the modding network". In `GrabbyBones/Data.cs`, `GrabState` has `// Pose,` commented out and defines only the `_Angle` and `_IsGrabbed` suffixes (https://github.com/kafeijao/Kafe_CVR_Mods/tree/master/GrabbyBones, https://raw.githubusercontent.com/kafeijao/Kafe_CVR_Mods/master/GrabbyBones/Data.cs) [M].
- No match turned up in NAK's mods, vrc3cvr, CVR-Conversion-Tools, Intermediate-Avatar or the CCK docs. A web search on 2026-10-17 also found no CVR mod reading grab-stretch [M].
- CVR has no stock avatar-part grab component. The core parameter page lists 24 parameters, matching `CvrParameterNames.Core` (`Editor/Core/CvrParameterNames.cs:77-84`), and none is grab, touch or stretch (https://docs.chilloutvr.net/cck/avatar/animator-core-parameters/) [M]. A native grab *rig* can be built, an aim driven by the `grab` tag, at about 97 bits per chain, and Joe rejected it (`docs/Unfinished.md:1559-1562`, from a decompile of the beta client, "unverified unless noted", `:1558`) [M].
- So this is built here. It reuses the stock trigger, not a new transport.

#### 1. What each source parameter becomes

| Source | On CVR | Who sees it | Sync | Stock, no mod? |
|---|---|---|---|---|
| `_IsGrabbed` | GrabbyBones, as today (`Editor/Core/Physics/GrabbyBonesSupport.cs:22-35`, preserved at `:32-33`) [M] | Everyone, when the wearer runs the mod [M] | 1 bit | No |
| `_Angle` | GrabbyBones, as today [M]. The fork plan says upstream measures the parent bone, only while held, and 0 on release (`Regression/GrabbyBonesForkPlan.md:74`). The repo's own comment says "bend of the end bone" (`GrabbyBonesSupport.cs:9`). The two disagree | Same | 32 | No |
| `_Squish`, `_IsPosed` | Proposed for a GrabbyBones upstream PR (`ForkPlan.md:72,76-77`; `Unfinished.md:1528-1530`) [M] | Modded only | 0 to 33 | No |
| **`_Stretch`** | **Ranked routes below** | | | |

**`_Stretch` is not only a grab output.** In VRChat it reads "how close the bones are to maximum stretch length", and **Stretch Motion** lets movement change bone length. Only at 0 do bones stretch purely "from grabbing or collisions" (https://creators.vrchat.com/common-components/physbones/) [M]. Gauge chains read `_Stretch`/`_Squish` from motion: Marshmallow PB drives a scale clip from a gauge PB, with stretch at 1.2 long (`Unfinished.md:2004-2010`; `memory/marshmallow-pb-mechanism.md`) [M]. A hand-position trigger is the wrong writer for those [R]. Every route below targets **grab-driven** `_Stretch` only.

**`_Stretch` routes, ranked**

1. **Stock trigger reading the hand's position along the pull axis. Recommended.**
   - Build: a `CVRAdvancedAvatarSettingsTrigger` box with a `SetFromPosition` stay task writing `<P>_Stretch`. This is the build in `YapsSocketReactions.EnsureTrigger` (`Editor/Yaps/Setup/YapsSocketReactions.cs:627-674`): `sampleDirection = ZNegative`, min 0 / max 1, an exit task writing 0, and local and network interaction both forced on [M].
   - The value is `(penetration.axis + 1) / 2` across the **whole** box: 0 at one face, 0.5 at the centre, 1 at the other. It is read in box-local space only for a Box, never a Sphere. A trigger with no Collider defaults to Box (`memory/cvr-trigger-reports-a-position.md`) [M].
   - It runs on the wearer's machine only (`memory/cvr-contacts-are-wearer-only.md`) [M]. Strangers' hand pointers reach receivers (README 812-822). `ContactsConverter.cs:225-226` maps `allowSelf`/`allowOthers` onto `isLocalInteractable`/`isNetworkInteractable` [M].
   - The wearer's client writes a synced float, so remote copies receive it. The YAPS doc puts the rate at "ten times a second" (`docs/YAPS5.md:27-28`). That is a doc statement, not a measurement here [M]. The "~1 mm" figure in that doc describes the YAPS channel's scale. For a 32-bit float the trigger record says "no quantisation worry" (`memory/cvr-trigger-reports-a-position.md`), so no step size is claimed.
   - Cost: 32 bits per chain out of 3200 [M].
   - The bone itself does not move. The blendshape is the only effect.
   - Fits the grab-handle build. `PhysBoneConverter.cs:551-554` records grab-and-stretch handles that skin no mesh and whose stretch "drives the animator" [M]. One sourced tutorial uses a chain of empty objects under Spine, with Grab Movement 1 and Max Stretch 1, and Motion Time on `<P>_Stretch` to scrub a shape key. The author flags it as partly outdated (https://note.com/marenatu/n/n0f8dd8193926) [M]. Calling that "the common build" is [R].
   - Limits: one volume samples one axis (`YAPS5.md:44-46`) [M]. VRChat stretch follows the hand in any direction, and the hand can grab anywhere on the chain, so this is Approximated [R].
2. **GrabbyBones upstream stretch.** After FABRIK, the fork plan scales bone offsets by `clamp(dist/reach, 1, 1+maxStretch)` (`ForkPlan.md:70`). The plan reports `_Stretch` as chain length against rest, normalised by maxStretch (`:75`) [M].
   - The bone really moves, but only for modded clients.
   - The plan recommends `_Stretch` as `#`-local, which costs 0 bits and is seen by modded clients only. Synced, it costs 32 bits and needs the wearer to run the mod (`ForkPlan.md:84-87`) [M].
   - It depends on Kafe's timeline. It complements route 1 and does not replace it [R].
3. **Shader or texture-parser route through squash. Last.**
   - Squash is vertex displacement in the shader. The hand press is `YapsHandsPress` (`Editor/Yaps/yaps_dent.cginc:255-323`) [M], and a shader writes no animator parameter [R].
   - Blendshape weights are unreachable from the parser. The chain from parser to `CVRAnimatorDriver` to parameter (`D1Pump.anim`) was "not yet run in game" (`YAPS5.md:205-211`) [M].
   - Viewers with the Cameras filter off lose it (`Unfinished.md:1928-1931`) [M].
   - The shader is stateless, so it cannot know a grab started without a blitter self-update loop (`memory/cvr-blitter-self-update.md`) [R].
   - It lives in the 18+ add-on, while clothing pull is a general feature [R].
   - Joe is right that it "technically works" for a push look. It is the wrong tool for a pull that drives the animator [R].
4. **Others, ruled out for now.**
   - A `CVRParameterStream` reading bone length would read near 0, because MC2's tether caps stretch at a compiled-in 3% (`Unfinished.md:1566-1569`) [M][R].
   - PhysX joints stretch bones for real: 1.12 against plain MC2's 1.05 in batch, built for the add-on (`Unfinished.md:1576-1610`) [M]. What stays open is whether CVR gives hands a physics collider that touches avatar bodies (`:1603-1604`) [GAME]. A push is not a pull, so a joint gives no grab even if it does [R].
   - The native `grab`-tag aim rig costs about 97 bits per chain and Joe rejected it (`Unfinished.md:1559-1562`) [M].

#### 2. Converter side

**Today** [M]
- `<P>_Stretch` is not in `PreserveParameters`. `AnimatorMerger` renames it `#<P>_Stretch` ("not synced in VRChat, so kept local") when `preserveParameterSyncState` is on, which is the default (`AnimatorMerger.cs:2124-2127,2150-2159`; `BridgeSettings.cs:61`). Nothing writes it, so it sits at its default.
- A chain whose only job was stretch or squish gets no cloth (`PhysBoneConverter.cs:540-576`).
- Report lines: MagicaCloth2 gives an Approximated line ("_Stretch/_Squish/_IsPosed have no equivalent"), or "no CVR equivalent" with GrabbyBones off (`MagicaClothWriter.cs:584-590`). DynamicBone gives Approximated, or **Skipped** with GrabbyBones off, plus "Max Stretch is not converted" (`DynamicBoneWriter.cs:200-217`). `ReportGrabbableChains` says chains "could be grabbed in VRChat; {target} can't be … Only players with the GrabbyBones mod can grab them" (`PhysBoneConverter.cs:450-478`).
- The survey files an unwritten `_Stretch` that drives bindings as "stopped working when converted" (`AvatarSurvey.cs:670-676,840-852`). The `Scaffolding` regex then hides it from the user's findings (`AvatarSurvey.cs:902-905,912-921`). The user is not told that a reveal broke. The survey already counts trigger stay tasks as writers (`AvatarSurvey.cs:434-445`).
- Order: `PhysBones` → `Contacts` → `Animator merge` → `Hold rewired contacts` (`BridgeConverter.cs:172-177`). PhysBones are destroyed at the end of the PhysBone pass when `deleteConvertedPhysBones` is on, which is the default (`PhysBoneConverter.cs:431-447`; `BridgeSettings.cs:69`).

**Would do** [R]
1. **Pick the chains.** For each PhysBone with parameter `P`, act only when a source layer reads `<P>_Stretch`, and when the chain is grab-driven: `allowGrabbing != False`, read as today in `PhysBoneConverter.cs:457`. Exclude motion-driven gauges such as Stretch Motion > 0 with gravity or pull, the Marshmallow case. Report those instead. The exact discriminator is open, and a corpus sweep should count both kinds before it is fixed.
   - The reference check must run on the **source** controllers. The merger's `CollectReferencedParameters` (`AnimatorMerger.cs:5008`, private, used by `PruneOrphanedParameters` at `:4980`) reads the merged master, which does not exist at PhysBone time.
2. **Read the grab filter fields** (`grabFilter`, plus `allowPosing`/`poseFilter` for section 4). `PhysBoneData.cs:86-120` reads none of them, though `allowGrabbing` is already read directly at `PhysBoneConverter.cs:457` [M]. The field names come from the fork plan (`ForkPlan.md:12,98`) and still need a compile check (`ForkPlan.md:106`).
3. **Capture in the PhysBone pass, before the PhysBones are destroyed.** Add `<P>_Stretch` to `PreserveParameters` (or `ContactParameters`, which the merger also preserves and prune keeps, at `AnimatorMerger.cs:2153` and `:4991`) so the merger keeps the synced name. Build the trigger then or in the Contacts pass.
4. **Build one box per chain**, each on its own child object, since one trigger per object. Parent it to the chain root's parent so it follows the body, not the chain.
   - Near face at the rest tip.
   - Depth: rest length × `maxStretch`. VRChat defines Max Stretch as a multiple of the original bone length [M].
   - Axis: root to tip at rest. `sampleDirection` chosen so the near face reads 0, as `ZNegative` does in YAPS.
   - Lateral size: generous, a setting. A hand entering from the side mid-depth jumps straight to a partial value.
   - Exit task: decided by section 4.
   - `isLocalInteractable` and `isNetworkInteractable` mirror the PhysBone's grab filter.
5. **Budget.** `SyncRoom` (`YapsSocketReactions.cs:225-240`) only appends a warning to a report and never skips. The real tally runs after the merge (`BridgeConverter.cs:379-400`; `BridgeDiagnostics.cs:1076`). Bits go in declaration order with a check before each add, so the client itself drops past the cap (`memory/cvr-aas-sync-requires-menu-entry.md`). "Skip if short" needs a new pre-merge estimate, or it becomes a post-merge report line.
6. The avatar's layers keep reading the same name, so no layer is rewritten. A threshold-transition use (`_Stretch` at 0.5 and 0.2) also works.
7. **Survey.** Once written, `_Stretch` stops being "stopped working" through the trigger writer (`AvatarSurvey.cs:434-445`). Taking `_Stretch` out of `Scaffolding` would also surface every unwritten gauge `_Stretch` as a user finding. Decide whether that is wanted. Update the `LostItsDriver` wording (`:844-852`).
8. **Report.** Replace the "no equivalent" lines in both writers, and update `ReportGrabbableChains` (`PhysBoneConverter.cs:474-478`). New wording: Approximated, one axis, the wearer computes, synced.

The shared trigger helper belongs in Core. `EnsureTrigger` is add-on only (`Editor/Yaps`), so a generalised copy in Core can serve both this pass and YAPS. A **new Core file must be added to `YAPS_CORE` in `Dev/Build/yaps-files.sh:16-20`**. The add-on ships a hand-written Core subset plus all of `Editor/Toolkit`, and a missing type is how 4.1.0 and 4.1.1 shipped broken (`Dev/Build/build-package.sh:119-123`) [M]. The second builder is the Toolkit's "PhysBones to cloth" card: `PhysBonesInPlace` runs `PhysBoneConverter.Run` in place (`Editor/Core/Physics/PhysBonesInPlace.cs:5-14,85`). It runs after no merger, so it needs its own handling of preserve and rename (`memory/avatarbridge-two-builders-diverge.md`).

#### 3. Toolkit authoring tool (ships in both packages)

One card in `ToolkitPanel.Build` (`Editor/Toolkit/ToolkitWindow.cs:81-131`; the card list is at `:111-124`), built with the `Tool()` helper (`:292-329`). Its `option` slot can hold the pickers [M]. The window gates on `CVR_CCK_EXISTS` only (`ToolkitWindow.cs:4`; `memory/toolkit-stands-alone.md`) [M].

1. **Pick the clothing.** `BridgeElements.ObjectPicker` for the SkinnedMeshRenderer (`Editor/UI/BridgeElements.cs:683`) [M].
2. **Pick the blendshape.** Version 1 picks an existing shape only. "Make a shape" waits for the mesh kit's pose-to-shape step: BakeMesh with weights zeroed, minus rest, then `AddBlendShapeFrame` on an `.asset` copy saved through `OutputAssetPaths.Claim` (`OutputAssetPaths.cs:40`). No pose-to-shape code exists today. The pieces sit in the add-on only: `AddBlendShapeFrame` in `YapsDebugOverlayBuilder.cs:553` and `BakeMesh` in `YapsSquashPainter.cs:445` [M].
3. **Place the grab volume.** Create a child GameObject holding the trigger. The user moves and scales it with Unity's own tools, and the CCK draws the box. A custom handle waits for need [R].
4. **Write the layer.**
   - Synced float.
   - 1D blend tree over `blendShape.<name>` curves. This is the pattern in `YapsSocketReactions.cs:494-584`: authored-weight floor, `writeDefaultValues = true`, embedded through `AnimatorAssetSaver.EmbedLayer` [M]. Copy it into Core. Do not reference it from the add-on.
   - Target the controller CVR ships. `AvatarSurvey.ShippedController` (`Editor/Core/AvatarSurvey.cs:178-187`, overrides then base then Animator slot) and `CvrSetup.SharedController` (`Editor/Core/CvrSetup.cs:384`) already exist in Core and in `YAPS_CORE` [M]. A tool-built channel once went into a controller CVR never uploads and did nothing in game (`YAPS5.md:33-36`; `memory/cvr-which-controller-ships.md`) [M].
5. **One option, "Stays open when let go"**, whose default comes from section 4.

#### 4. Lift and hold

- A stay task writes only while a pointer is inside. The converter adds an exit task because "the parameter keeps its last reading after" (`ContactsConverter.cs:254-256`) [M].
- In VRChat, Grab Movement 0 reaches the grab point through pull and spring, and 1 moves there at once. Pulling the trigger while holding poses the bones, gated by Allow Posing (VRChat PhysBones docs) [M]. Springing back on an unposed release is [R]: the docs do not state it for avatars.
- **Out of the box.** The hand must stay inside the box. The box must contain the whole pull path, which is why it is sized by `maxStretch`. The hand must also grab near the tip, where in VRChat any bone would do [R].
- **Release with exit = 0.** The garment snaps shut when the hand leaves. This matches an unposed VRChat release only when the hand leaves at release time [R].
- **Release with no exit task.** The value holds at the last sample. Leaving sideways holds partway. Leaving back through the near face reads near 0, so it closes naturally. Leaving through the far face holds at 1 [R].
  - Closing needs a push back through the near face, or a menu toggle.
  - The source's `allowPosing` is the natural default for "Stays open when let go" [R].
  - `HoldUnlatchedContacts` (`ContactsConverter.cs:405-439`) is **not** a precedent. It turns an OnEnter pulse into "on while touched, off when it leaves", the opposite of holding after release [M].
- **Grip gating. The sources disagree.** The CCK docs call `grab` a **legacy** pointer, "Active on the hand that is currently grabbing an object" (https://docs.chilloutvr.net/cck/avatar/autogenerated-pointers/) [M]. The beta decompile says the tag "starts on at load until the player's first fist" (`Unfinished.md:1564`) [M]. Whether a bare fist with no pickup counts is [GAME].
  - If `grab` switches off when the fist opens inside the box, the trigger may never see it leave, and the value holds [GAME]. The same mechanism held a plug bent (`YAPS5.md:19-21`) [M].
  - Holding on release may come for free. It might also be unreliable [GAME].
- **Passing hands.** With `allowedTypes` limited to `grab`, a hand passing through without gripping does nothing [R][GAME]. With `Hand`, `LeftHand` or `RightHand`, any touch opens it. Converted `Hand` receivers already also listen for `grab` (README 824-828) [M].
- **Interplay with cloth.** On a chain that also keeps an MC2 cloth, the hand inside the box also pushes that cloth, since CVR adds hand colliders to every cloth (README 784-788) [R].

#### 5. Gates only ChilloutVR can settle

| # | Question | Why it matters |
|---|---|---|
| G1 | Does a box with `SetFromPosition` follow another player's hand smoothly with `isNetworkInteractable` on? [GAME] | Route 1 for others |
| G2 | Is the `grab` pointer present for a bare fist, only while gripping, or only while holding a pickup? [GAME] | Gating, and the hold default |
| G3 | When `grab` switches off inside the box, does the exit task fire? [GAME] | Snap or hold |
| G4 | Are latency and update rate acceptable to the person pulling? The path is their hand, then the wearer, then AAS, then back to them [GAME] | Feel |
| G5 | Does the wearer's own grab pointer fire it with `isLocalInteractable` on? [GAME] | Self-pull |
| G6 | Does the stable client behave like Joe's beta? The decompile notes come from the beta [M] | Shipping claim |
| G7 | Crowds: 512 sticky pairs, so a new touch can fail silently (`memory/cvr-trigger-reports-a-position.md`) [M][GAME] | Known limitation text |

#### Decisions for Joe

| Decision | Options | Recommendation |
|---|---|---|
| `_Stretch` route | Stock trigger / GrabbyBones only / squash | Stock trigger now. GrabbyBones stretch later, for modded bone motion |
| Which chains | Grab-driven only / every reader of `_Stretch` | Grab-driven only. Gauge chains such as Marshmallow get a report line, not a box |
| Converter default | On / off, new setting | Your call. Precedent: `syncSocketDepthForOthers`, also 32 synced bits per contact-driven value, defaults **off** (`BridgeSettings.cs:177-179`). "On" spends bits only on parameters a grab-driven chain's layers already read |
| Release behaviour | Snap shut / hold / follow G3 / mirror `allowPosing` | Decide after the spike |
| Pointer types | `grab` / any hand | `grab`, if G2 holds for a bare fist |
| Who can pull | Mirror the PhysBone grab filter / wearer only / setting | Mirror the PhysBone, as `ContactsConverter.cs:225-226` does for receivers |
| Package | Public / 18+ add-on | Public. Clothing pull is not penetration, but a reveal may read as adult, so this is your call |
| Name ownership if GrabbyBones adds stretch | Trigger owns synced `<P>_Stretch`, mod writes `#` / mod owns it | Trigger owns the synced name. The fork plan already recommends `#` for the mod's `_Stretch` (`ForkPlan.md:85`) |
| `_Squish` by reversed box | Now / later / GrabbyBones only | GrabbyBones only, per the brief |
| Toolkit version 1 scope | Pick shape / also make shape | Pick only. Make shape lands with the mesh kit |

#### First spike

- **Build.** One test avatar, built by hand, with no converter changes. The build is one box trigger, a Box with no Collider, under a chest bone:
  - `allowedTypes = grab`;
  - `SetFromPosition`, `sampleDirection` Z with 0 at the near face, 0 to 1;
  - no exit task;
  - writes a synced float `Spike_Stretch`;
  - a 1D blend tree drives one existing clothing blendshape, in the controller `ShippedController` returns.
- **Variant B.** Same build with `allowedTypes = Hand` and an exit task writing 0.
- **Test.** Joe in game, with a second client he runs as the remote player (`memory/no-ai-driven-cvr-client.md`). The remote player makes a bare fist and pulls along Z, then repeats while holding a pickup. They open the fist inside the box, then leave sideways and through the near face. The wearer pulls their own.
- **Read off:** G1 to G5, the value on the wearer against a remote copy, and whether a passing open hand opens it.
- **Done when** G1 to G5 each have an answer. The release decision and the converter pass follow from it.

**Docs to move with the converter pass.**
- README:
  - Not converted (2049).
  - The parameter table (357).
  - The GrabbyBones section (777-810), whose line "That's as far as any converter can go: **grabbing is a client mod**" (796-799) becomes untrue for stretch.
  - Options and Known limitations.
- Window text: `AvatarBridgeWindow.cs:611-615`, plus any new setting.
- `BridgeSettings` comment.
- Report lines: `MagicaClothWriter.cs:584-590`, `DynamicBoneWriter.cs:200-217`, `PhysBoneConverter.cs:474-478`.
- Survey wording: `AvatarSurvey.cs:844-852`.
- Build list: `Dev/Build/yaps-files.sh` `YAPS_CORE` for any new Core file.
- `docs/Unfinished.md` record.

---

#### Judge's corrections

1. **Missed: `_Stretch` is also motion-driven.** VRChat's Stretch Motion makes bones stretch from movement (VRChat PhysBones docs). Marshmallow PB reads a gauge PB's `_Squish`/`_Stretch` from motion (`Unfinished.md:2004-2010`). The original step 1 ("act when the animator reads `<P>_Stretch`") would have put grab boxes and 32 bits on gauge chains. Added a grab-driven filter, a decision row and a survey caveat.
2. **Wrong order: the merger's reference set does not exist when it is needed.** PhysBones runs before the merge (`BridgeConverter.cs:172-174`) and destroys the PhysBones (`PhysBoneConverter.cs:431-447`). `AnimatorMerger.cs:4980-4994` is `PruneOrphanedParameters` working on the merged master. The helper is `CollectReferencedParameters` at `:5008`. Rewrote steps 1 and 3: check the source controllers, and capture the data before the PhysBones are destroyed.
3. **"Grab fields are not read today" was half wrong.** `allowGrabbing` is already read at `PhysBoneConverter.cs:457`. Only `PhysBoneData` skips it. Also corrected the `PhysBoneData.cs:72-76` citation, which points at the MaxStretch/Parameter fields, not a reader.
4. **`HoldUnlatchedContacts` is not a hold-after-release pattern.** It makes a pulse "on while touched, off when it leaves" (`ContactsConverter.cs:399-439`). Corrected.
5. **`SyncRoom` never skips.** It only appends a report warning (`YapsSocketReactions.cs:225-240`), and the true tally exists only after the merge. Reworded the budget step.
6. **Missed: the add-on package's Core list.** A new Core helper must be added to `YAPS_CORE` (`Dev/Build/yaps-files.sh:16-20`; `build-package.sh:119-123`), or the add-on fails to compile on its own, as in 4.1.0 and 4.1.1.
7. **Missed: the second builder.** `PhysBonesInPlace` (the Toolkit "PhysBones to cloth" card) runs `PhysBoneConverter.Run` with no merger, so it needs its own preserve handling.
8. **Missed: existing Core helpers for the shipped controller.** `AvatarSurvey.ShippedController` and `CvrSetup.SharedController` already exist in Core and in `YAPS_CORE`.
9. **The `grab` pointer is contested.** The CCK docs call it legacy and active "on the hand that is currently grabbing an object". The beta decompile says "until the player's first fist". Widened G2 and added a pickup case to the spike.
10. **Dropped the "~1 mm steps".** That figure describes the YAPS channel. The trigger record says 32-bit floats carry "no quantisation worry". The 10 Hz figure is kept and labelled a doc statement.
11. **Core parameter count is 24, not 22**, per both the docs page and `CvrParameterNames.cs:77-84`.
12. **Data.cs claim re-sourced.** The cited tree URL does not show `Data.cs`. The raw file confirms `// Pose,` in `GrabState`. Also noted that `_Angle` is described two different ways (`GrabbyBonesSupport.cs:9` against `ForkPlan.md:74`).
13. **Report surfaces were incomplete.** Added `ReportGrabbableChains` (`PhysBoneConverter.cs:474-478`), DynamicBone's **Skipped** line with GrabbyBones off and "Max Stretch is not converted", and the README line saying grabbing is mod-only.
14. **Survey detail.** Stay tasks already count as writers (`AvatarSurvey.cs:434-445`), so a written `_Stretch` clears the finding on its own. Removing `_Stretch` from `Scaffolding` would also surface gauge chains. Flagged.
15. **The PhysX-joint route was understated.** Joints really stretch bones (add-on, `Unfinished.md:1576-1610`). What stays open is a hand physics collider, which is [GAME], not "nothing grabs". It stays ruled out for pull.
16. **Converter default precedent.** `syncSocketDepthForOthers`, the same 32-bit contact-to-synced trade, defaults off. The "On" recommendation is now Joe's call, with that precedent shown.
17. **Smaller fixes.**
    - The trigger value spans the whole box, from face to face, Box shape only.
    - `EnsureTrigger` uses `ZNegative` and forces interaction on, so mirroring the filter is new code.
    - Corrected line ranges: `GrabbyBonesSupport` path, `EnsureTrigger :627-674`, `YAPS5 :19-21, :33-36`, `AvatarSurvey :902-905`, `BridgeElements` path.
    - The note.com article is flagged by its author as outdated, so "common build" is [R].
    - VRChat "springs back on release" is not stated in the docs, so it is [R].
    - Added `allowPosing` as the natural default for "Stays open when let go".

Sources:
- [GrabbyBones](https://github.com/kafeijao/Kafe_CVR_Mods/tree/master/GrabbyBones)
- [GrabbyBones Data.cs](https://raw.githubusercontent.com/kafeijao/Kafe_CVR_Mods/master/GrabbyBones/Data.cs)
- [CVR animator core parameters](https://docs.chilloutvr.net/cck/avatar/animator-core-parameters/)
- [CVR autogenerated pointers](https://docs.chilloutvr.net/cck/avatar/autogenerated-pointers/)
- [VRChat PhysBones](https://creators.vrchat.com/common-components/physbones/)
- [Marenatu tutorial](https://note.com/marenatu/n/n0f8dd8193926)
- [VRChat PhysBones (search result)](https://creators.vrchat.com/avatars/avatar-dynamics/physbones)
- [_Stretch reactive on teleport](https://feedback.vrchat.com/avatar-30/p/physbones-stretch-parameter-is-extremely-reactive-when-the-avatar-teleports-or-r)
- [ShockOSC](https://github.com/OpenShock/ShockOSC)

### Mesh tools in the Toolkit, so common edits never need Blender. PLANNED 2026-10-17, NOT STARTED, after 5.0.0

Joe's request, 2026-10-17: Blender-style mesh edits inside Unity for avatars already on ChilloutVR. The list is pose to blendshape, a sculpt/move brush, a smooth/relax "mesh fixer" brush for lumps (in the style of the squash region painter), split/combine blendshapes, and a viseme maker. His rule: if a modern tool already does it easier and better, use that, and build only what nothing covers.

Tags: [M] means sourced to a file:line or a URL. [R] means reasoned or inferred, not yet measured. "Not found" means not found in the searches made on 2026-10-17. It does not prove absence.

#### Build or adopt, per feature

| Feature | Best existing tool | Verdict |
|---|---|---|
| Pose to blendshape | No in-Unity tool found that turns a bone pose into a shape (searches 2026-10-17) [M]. **PoseMesh** (¥880, V2.3 2026-10-13, tested on 2022.3.22f1) is a corrective sculpt tool: you brush in a pose, in Edit or Play mode, and it saves a new mesh with the corrective shape. It does not convert a pose, it saves no bone pose, and it states no licence (https://booth.pm/en/items/8385426) [M]. Cats has Pose to Shape Key, but in Blender (https://github.com/absolute-quantum/cats-blender-plugin, MIT) [M]. | **Build.** It is a gap [M, searches]. Smallest feature. Its core is shared by every later one [R]. |
| Sculpt/move brush | **VRCMeshDeformer**, ¥250, verified on 2022.3. Move, Push/Pull, Smooth, X mirror, works on SkinnedMeshRenderer, "Save as BlendShape", states no VRChat SDK dependency. Redistribution and bundling are prohibited (https://booth.pm/en/items/8588415) [M]. **Easy Mesh Editor**, ¥1,990, Unity 2019.4 to 6. Pull, Smooth, Flatten, Gather and Inflate brushes, sculpts new or existing shape keys, makes mirrored keys (https://booth.pm/en/items/8616338) [M]. **Lattice Deformation Tool**, free, MIT. Brush with Move and Smooth, lattice, BlendShape output. Its package.json depends only on NDMF >=1.9.0, with no VRChat SDK (https://github.com/32ba/VRC-lattice-deformation-tool, package.json) [M]. On CVR, NDMF runs through Chillaxins at upload, and Chillaxins lists only Starmesh, FaceTra Shape Creator and Prefabulous Universal as known working (https://docs.hai-vr.dev/docs/products/chillaxins) [M]. **Polybrush** is Unity's own free package, with Sculpt and Smooth modes. It is deprecated from 6000.3, and its skinned-mesh support is undocumented beyond "Fix compatibility issues with SkinnedMeshRenderer" in 1.0.0-preview.9 (https://docs.unity3d.com/Packages/com.unity.polybrush@1.2/manual/faq.html, https://docs.unity3d.com/Packages/com.unity.polybrush@1.1/changelog/CHANGELOG.html) [M]. | **Adopt by default.** Cheap or free tools cover it and need no SDK [M]. None is tested on CVR here [R]. Build last, and only if Joe wants it free and in the Toolkit. |
| Smooth/relax brush | Every tool in the row above has Smooth, and PoseMesh smooths in a pose [M]. The free options are Lattice (NDMF, CVR untested) and Polybrush (skinned support unknown) [M]. | **Build, if Joe wants the painter feel.** It reuses the painter's welded graph and geodesic dab (see Rules) [M for those existing, R for the reuse]. Otherwise adopt, as for sculpt. Worth one manual check of Polybrush on a posed skinned body before building [R]. |
| Split/combine | **BlendShapeEditor**, free, 2022.3.62f2: rename, merge (合成), concatenate, delete, left/right split, masking. No licence stated (https://booth.pm/ja/items/4662982) [M]. "SmoothRange" is not on the page [M]. **Blend Shape Modifier**, MIT, NDMF >=1.13.0, no VRChat SDK in package.json: merge, filter, intermediate frames (https://github.com/nekobako/BlendShapeModifier, Packages/net.nekobako.blend-shape-modifier/package.json) [M]. Easy Mesh Editor makes mirrored keys [M]. | **Adopt by default, or build small (a Decision).** A free in-Unity tool covers it, and an unstated licence only blocks bundling, not users using it [M for the tool, R for the reading]. If built, it is about 100 lines on the ShapeWriter that pose capture needs anyway [R]. |
| Viseme maker | **LipSincGenerator** (free) only copies existing shapes into VRChat slots. It prohibits redistribution (https://booth.pm/en/items/8798437) [M]. **MorphMixer** (€18.40, v1.2.0 2022-02-11, Unity 2019.4.34 listed) mixes shapes into new ones according to a search summary; its store page lists no features (https://assetstore.unity.com/packages/tools/animation/morphmixer-111278) [M for the page, R for the feature]. **CVR has its own fallbacks**: the lip sync modes `Visemes`, `SingleBlendshape` and `JawBone` (CCK CVRAvatar.cs:20, default `Visemes` at :192) [M]. An avatar with only a mouth-open shape or a jaw bone already lip-syncs without 15 visemes [R]. | **Build, but only for avatars with a few mouth shapes and no visemes.** It turns 3 shapes into 15. The README should point jaw-only and single-shape avatars at the CCK's own modes [R]. 15 named slots: CVRAvatar.cs:194, `string[15]` [M]. |
| Grab clothing to drive a shape | Every tool found needs PhysBones and the VRChat SDK. On CVR only GrabbyBones mod users see a grab (README.md:798-803) [M]. | **Do not build.** Out of scope [R]. |

#### Rules every card follows

- **Source assets are never edited.** The card copies the mesh with `Object.Instantiate`, as in `SceneAssetRehomer.CopyMesh` (Editor/Core/SceneAssetRehomer.cs:177-183) [M]. The copy is saved under the Toolkit's avatar folder (`WriteFolder`, Editor/Toolkit/ToolkitWindow.cs:253-258; `AvatarFolder`, :209-236) [M].
- **The first save and re-runs need different routes.**
  - A first save may take a name through `OutputAssetPaths.Claim` (Editor/Core/OutputAssetPaths.cs:40-69). The entry point must call `OutputAssetPaths.Reset()` first (:31-36) [M].
  - **A re-run must not use Claim.** Claim deletes whatever sits at the path, and the comment there says "The GUID changes" (:53-66) [M].
  - A re-run `CopySerialized`s into the existing asset instead: `SaveInPlace`, Editor/Yaps/YapsBaker.cs:1289-1305 [M]. That method is private and in the add-on, so it is rewritten in the Toolkit, about 15 lines [R].
- **A second capture edits the copy, not the source again.** When the renderer already wears a mesh that is a main `.asset` of its own, it is edited in place. That is the test at YapsBaker.cs:1205-1206 ("A mesh asset of its own keeps the edit") [M]. Re-copying from the FBX would drop the first captured shape [R].
- **FBX sub-assets cannot hold edits.** A model's sub-asset "drops the edit on the next restart" (YapsBaker.cs:1200-1201) [M].
- **Only the chosen renderer changes.** It gets the copy under `Undo.RecordObject(renderer)`. Other renderers sharing the source mesh keep it [R].
- **Put back needs a small change to `Tool()`.** The `putBack` slot exists (ToolkitWindow.cs:292-329), but its label is fixed to "Put the textures back" (:322). `canPutBack` is called unguarded whenever `putBack` is set (:309, :323) [M]. So `Tool()` needs a label argument, and the mesh card must pass `canPutBack` [R].
- **Shape names and order are kept.**
  - Clips bind `blendShape.<name>` (Editor/Core/AvatarWeight.cs:156, :184) [M], and CVR visemes are stored by name (CVRAvatar.cs:194) [M].
  - The renderer stores weights by index [R].
  - New shapes are appended at the end, so existing indices and animations stay valid [R].
- **No add-on dependency.** Everything lives in Editor/Toolkit [M].
  - Editor/Toolkit ships in both packages: the public build prunes only Editor/Yaps and Runtime (Dev/Build/build-package.sh:237-250), and the add-on takes Editor/Toolkit whole (Dev/Build/yaps-files.sh:26) [M].
  - **The add-on carries only the Core files listed in `YAPS_CORE`** (yaps-files.sh:16-21) [M]. Any Core file a card calls must be on that list, or the add-on package stops compiling.
  - Every callee named below is on the list: OutputAssetPaths, CvrSetup, AvatarFeatureDetect, UnifiedBlendshapes, MouthLocator and AvatarWeight [M].
  - New shared code goes in Editor/Toolkit, so no list edit is needed [R].
- **No VRChat SDK dependency.** The Toolkit is `#if CVR_CCK_EXISTS` only (ToolkitWindow.cs:4) [M].
  - So it must not call `MeshGrowth` (`VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS`, Editor/Core/MeshGrowth.cs:1) or `SceneAssetRehomer` (same gate, SceneAssetRehomer.cs:1) [M]. Their patterns are copied, not called [R].
  - `OutputAssetPaths` is ungated [M]. `CvrSetup`, `AvatarFeatureDetect`, `UnifiedBlendshapes`, `MouthLocator` and `AvatarWeight` are gated on `CVR_CCK_EXISTS` only (line 1 of each, AvatarWeight.cs:11) [M].
- **Add-on code is reimplemented, not moved.** That covers `YapsSquashPainter` (Editor/Yaps/Setup/YapsSquashPainter.cs), `YapsSquash.Welds`/`Fingerprint` (Editor/Yaps/YapsSquash.cs:188-207), `YapsDebugOverlayBuilder.WithReadout` (Editor/Yaps/Setup/YapsDebugOverlayBuilder.cs:397) and `YapsShapeSim` (Editor/Yaps/Setup/YapsComponentEditors.cs:536) [M].
  - The add-on painter could later call the Toolkit brush core. Both packages contain Editor/Toolkit, and the repo has no asmdef, so the code compiles together in either install [M for both facts, R for the conclusion].

#### Shared core, built once

1. **`SkinFrame`.** For each vertex, M = Σ wᵢ · boneᵢ.localToWorld · bindposeᵢ, plus its inverse [R].
   - The once-per-bone `bones[b].localToWorldMatrix * binds[b]` pattern is MeshGrowth.cs:121-130 [M].
   - Weights come from `GetAllBoneWeights`/`GetBonesPerVertex`, not `mesh.boneWeights`. The legacy view keeps only four influences per vertex (Editor/Core/AnimatorMerger.Physics.cs:749-753) [M]. Whether BakeMesh honours the renderer's bone-count quality is unverified, so the test includes a vertex with more than four influences [R].
   - **M must be evaluated in the pose the shape will play in** (the rest pose after reset), not the captured pose. Linear skinning gives posed = M·(v + d), so d = M_rest⁻¹·P − v [R].
   - BakeMesh skins on the CPU, and its result is relative to the renderer transform. `useScale` defaults to false, which bakes "without using the scale value" (https://docs.unity3d.com/2022.3/Documentation/ScriptReference/SkinnedMeshRenderer.BakeMesh.html) [M].
   - **The repo's two BakeMesh callers disagree on the flag.** The painter uses `BakeMesh(_baked, true)` and maps to the world with `TRS(position, rotation, one)` (YapsSquashPainter.cs:445-453). The probe uses `BakeMesh(baked, false)` with `position + rotation * v` (Dev/Probes/HeldShapeFrameProbe.cs:186-193) [M]. At most one of them can be right on a scaled renderer [R]. The spike settles it.
   - This is the main technical risk, and the first spike tests it.
2. **`ShapeWriter`.**
   - **Append** a new shape directly with `AddBlendShapeFrame` [R].
   - **Replace** or reorder a shape by rewriting the set: copy every frame with `GetBlendShapeFrameVertices`, `ClearBlendShapes`, then re-add [R].
   - The rules behind this: frames go only on a new shape or the last one, in increasing weight order, and delta arrays must equal `vertexCount`. A name that already exists is not created anew (https://docs.unity3d.com/ScriptReference/Mesh.AddBlendShapeFrame.html) [M].
   - So a clashing name is refused up front, or the frames land on the existing shape [R].
   - The template is the frame-copy loop at Editor/Yaps/Setup/YapsDebugOverlayBuilder.cs:546-556 [M]. That loop extends the arrays for added vertices, which the Toolkit does not need [M].
   - `deltaNormals` and `deltaTangents` may be null (same URL) [M].
3. **`Welds`.** Groups vertices on a 1e-5 grid of mesh-space position (the copies a UV seam splits a point into), after YapsSquash.cs:201-207 [M].
   - The painter already builds the welded one-ring adjacency from it (`_rep`, `_near`, YapsSquashPainter.cs:210-222) [M]. That is the smoothing graph.
   - Rounding to a grid can leave two near-identical points in neighbouring cells unwelded [R].

#### Order, smallest useful first

1. **Pose to blendshape card.** Pick a renderer, pose bones in the Scene view, name the shape, press Capture.
   - Zero all blendshape weights, BakeMesh, then restore the weights. That is the pattern at HeldShapeFrameProbe.cs:186-197 [M].
   - Delta = SkinFrame_rest⁻¹(P) − v [R].
   - Normals: transform with M_rest⁻¹'s 3x3 and renormalise before subtracting the unit rest normal. Tangents are handled the same way, keeping w [R].
   - Why the renormalising matters: a normal delta left at import scale against unit rest normals was the 4.6.2-4.6.4 torn-plug bug. `DirectionDelta` divides by the rest vector's transformed length (YapsBaker.cs:928-932) [M].
   - Skinned normals come from BakeMesh, so seams stay consistent with no recalculation [R].
   - Then reset the pose, recorded as one undo group [R].
   - A target pose other than rest (the shape plays while a bone also moves) is a Decision.
2. **Viseme maker card.** Choose three existing mouth shapes (A, O, CH), or capture them with step 1 from a posed jaw. Build the missing slots as linear mixes.
   - Cats' table takes those three inputs. Each viseme is one or two of them at fixed ratios, created with `from_mix`, and an intensity scales every ratio (cats-blender-plugin tools/viseme.py:97-99, 103-180, 258-284; MIT) [M]. Porting the ratios is [R].
   - **Always write new shapes, never point a slot at an input shape.** The CVR docs warn visemes do not work if an animation uses the same blendshapes (https://docs.chilloutvr.net/cck/components/cvr-avatar/) [M].
   - Name them `v_<slot>`. Both lookups find that name: `DetectVisemes`' candidate list, case-insensitive and exact (Editor/Core/AvatarFeatureDetect.cs:189), and the CCK's Auto Select, a substring match (project CCK, Assets/CVR.CCK/Scripts/Editor/GUI/Components/CCK_CVRAvatarEditor/CCK_CVRAvatarEditor.cs:461-477) [M].
   - Fill the slots in CVR order (sil, PP, FF, TH, DD, kk, CH, SS, nn, RR, aa, E, ih, oh, ou: AvatarFeatureDetect.cs:162-166; the CCK's own list at CCK_CVRAvatarEditor.cs:34-35) [M].
   - **`CvrSetup.WireFace` (Editor/Core/CvrSetup.cs:275-308) does more than fill slots.** It may change `bodyMesh` through `PickFaceMesh` (:312-326) and rewires blink (`WireBlink`) [M]. The Toolkit's existing "Face: visemes and blink" card already calls it (ToolkitWindow.cs:488-490) [M]. So either call it and say so in the rows, or write `visemeBlendshapes` directly [R].
   - **`visemeMode` must be checked.** WireFace sets `useVisemeLipsync` and never writes `visemeMode` (CvrSetup.cs:297-298) [M]. On an avatar set to `SingleBlendshape` or `JawBone`, the new visemes would be ignored. So the card sets `Visemes`, or warns [R].
   - Shapes go on the mesh that ends up as `bodyMesh` [R].
   - Edit-mode preview reuses the hold-and-release idea of `YapsShapeSim` (YapsComponentEditors.cs:536-620), reimplemented [M]. That includes releasing on assembly reload, on leaving Edit mode, and on `sceneSaving` (:549-556). Without the last, a save would keep the preview weights as the author's [M].
3. **Split/combine card** (if built).
   - **Split** multiplies each frame's deltas by a smoothstep across a blend width on avatar-local x. The side test is the painter's: `root.InverseTransformPoint(baked world position).x < 0` is left (YapsSquashPainter.cs:427, positions from :445-447) [M]. It needs the posed bake, not SkinFrame [R].
   - **Combine** sums deltas frame by frame. Shapes with differing frame weights are refused rather than resampled [R].
   - L/R names use a plain Left/Right suffix [R].
   - `UnifiedBlendshapes`' table cannot be reused without a Core edit. It is private (Editor/Core/UnifiedBlendshapes.cs:34), only `BuildPlan` is public (:123), and it holds face-tracking names only [M].
4. **Smooth/relax brush.** A Scene-view window reimplementing the painter's mechanics [M]:
   - Control id, ray, `DrawWireDisc`, hotControl stroke (YapsSquashPainter.cs:489-522).
   - **A geodesic dab**: Dijkstra over the welded graph within the radius, "never across a gap", so an arm resting on the side is not caught (:524-555).
   - Quartic falloff `(1-x²)²` (:560).
   - Overlay through `DrawMeshNow` with `Hidden/Internal-Colored` (:476-487).
   - Each dab: welded Laplacian, `p += s·f·(avg(neighbours) − p)`, worked out in posed space and turned into a bind-space delta through SkinFrame⁻¹ [R].
   - The posed positions and the overlay are rebaked after every dab. The painter bakes once per stroke because its edits never move vertices (:500) [M for the painter, R for the need].
   - Target: the base mesh, or one shape's deltas (Decision).
   - Raycasts stay brute force, Möller-Trumbore over every triangle, as the painter does (:566-595) [M]. A BVH and a `// ponytail:` mark come only if a measured large body is slow [R].
5. **Move/sculpt brush.** Only if Joe declines adopting. Same window with Move, Push/Pull and Inflate, and the same bind-space conversion [R].

#### Hard parts

- **Bind vs posed.** The scene pose is rarely the bind pose. Armatures scaled 100x, import-scaled renderers (lossy 0.07 has occurred, per the torn-plug fix) and rotated renderers are common [R]. SkinFrame⁻¹ evaluated at the play pose handles all of it, or nothing works. That is why it is spike one [R].
- **Held weights.** Every capture zeroes held blendshape weights first and restores them after (HeldShapeFrameProbe.cs:186-197) [M]. The Unity docs do not mention blendshapes in BakeMesh [M].
- **Normals after a brush edit.** Recompute area-weighted normals on the welded graph for the touched region and its one-ring, before and after. Apply the change as a delta to the authored normal, so custom and hard normals survive [R]. Tangents get the minimal rotation from the old normal to the new one [R].
- **Seams.** Every weld group moves as one, and its shape deltas stay identical, or the mesh tears along UV seams [R]. Positions are compared after the edit in the test.
- **Mirror.** A 1 cm spatial hash in avatar-local space, taking the nearest match within 2 cm of flipped x (YapsSquashPainter.cs:304-332) [M]. The painter writes 0 to an unmatched vertex (:329) [M]. Here unmatched vertices are listed in the report rows instead [R].
- **Multi-frame shapes.** `ShapeWriter` keeps every frame. The add-on's last-frame-only fold (YapsBaker.cs:941-972, marked `ponytail:` at :941) is not copied [M].
- **Undo size.** `Undo.RegisterCompleteObjectUndo(mesh)` once per stroke, not per dab [R]. The memory cost on a large mesh is unmeasured [R].
- **Avatar cost.** `AvatarWeight` counts every shape and calls one "animated" only if a clip binds it (AvatarWeight.cs:149-184). It warns past 64 shapes nothing animates (:410-414) [M]. Corrective and viseme shapes are driven by no clip, so they count toward that warning [M for the rule, R for the effect].

#### Tests (Dev/Tests, headless, gated `CVR_CCK_EXISTS` only; fixtures built like Dev/Probes/FixtureBuilder.cs:368-369)

- **SkinFrame / pose capture.** Two-bone cylinder with a 100x armature, a rotated **and scaled** renderer, and one vertex with more than four influences. Pose, capture, reset the pose, set weight 100, BakeMesh. Posed positions must match within 1e-4 and normals within 1e-3, through whichever `useScale` mapping the test proves [R].
- **Non-destructive.** The source mesh's own hash of positions plus every shape frame is unchanged after every card. `YapsSquash.Fingerprint` covers positions only (YapsSquash.cs:188-196) [M], so a new hash is needed [R]. The swapped renderer restores with Put back [R].
- **Re-run identity.** A second capture keeps the asset's GUID and the first captured shape [R].
- **ShapeWriter.** Round trip of a multi-frame mesh: every frame's weight and deltas are identical. The new shape is last, existing indices are unchanged, and a clashing name is refused [R].
- **Split/combine.** L at 100 plus R at 100 equals the source. The centre line is half. Combine equals the sum [R].
- **Visemes.** All 15 slots are non-null in CVR order, and `visemeMode` is `Visemes`. `DetectVisemes` returns non-null once 7 of 15 match (AvatarFeatureDetect.cs:200), so non-null alone proves little [M for the threshold, R for the test].
- **Smooth.** A noisy plane with a duplicated seam. After a dab, weld groups have identical positions, Laplacian energy drops, and vertices outside the radius are bit-identical [R].
- **Packaging.** `compile-check.sh` passes all five combinations: with and without the VRChat SDK, each with and without the add-on, plus MagicaCloth2 without the SDK (Dev/Build/compile-check.sh:10-11) [M for the script]. The add-on build compiles from `YAPS_CORE` plus Editor/Toolkit [R].

#### Decisions for Joe

| Question | Options | Suggested |
|---|---|---|
| Sculpt/move brush | Adopt (README pointer to VRCMeshDeformer, Easy Mesh Editor or Lattice, no code) or build | Adopt |
| Smooth "mesh fixer" brush | Build (painter feel, free) or adopt like sculpt | Build, after cards 1-2. Check Polybrush on a posed skinned body first |
| Split/combine | Adopt (pointer to the free BlendShapeEditor) or build small | Adopt. Build only if it should sit in the Toolkit |
| What the smooth brush edits | Base mesh, one blendshape's deltas, or both | Both: base by default, with a shape picker |
| Pose capture target | Shape plays from rest pose, or relative to a held pose | Rest only |
| Viseme mix ratios | Port Cats' three-input table (MIT, credited) or derive our own | Port and credit |
| Viseme wiring | Call `WireFace` (may change face mesh and blink) or write the slots directly | Write directly. Leave the Face card for the rest |
| Where the cards live | Toolkit cards (both packages) or a separate menu window | Cards in the Toolkit, brushes in one Scene-view window it opens |
| Docs | README section "Mesh tools", plus pointers to the adopted tools and to CVR's JawBone/SingleBlendshape modes | Same commit as each card |

#### First spike

Build `SkinFrame` and the pose-capture test only, in Dev/Tests, with no UI.
- Fixture: a two-bone cylinder under a 100x scaled armature. The renderer is rotated 90° and scaled 0.07. One vertex has five influences, and a held blendshape sits at 50.
- Steps: pose bone two by 45°, capture, return to rest, set the new shape to 100, BakeMesh.
- Pass: positions within 1e-4 and normals within 1e-3 of the posed bake. The held shape is back at 50 afterwards.
- It also records which BakeMesh `useScale` mapping is right on a scaled renderer. The painter and the probe disagree today [M].
- If it passes, card 1 is UI over a proven core. If it fails, the inverse-skinning approach is wrong, and every later card needs a different frame [R].

The plan reuses repo code only where it compiles without the VRChat SDK and the 18+ add-on.
- **Called directly:** `OutputAssetPaths` (first save only), `AvatarFeatureDetect.VisemeOrder`/`DetectVisemes`, optionally `CvrSetup.WireFace`, and the Toolkit `Tool()` card (after a small label change).
- **Copied as patterns, not called:** `SceneAssetRehomer.CopyMesh`, `MeshGrowth`'s per-bone matrices, `YapsBaker.SaveInPlace`/`DirectionDelta`, `YapsSquashPainter`, `YapsSquash.Welds`, `YapsDebugOverlayBuilder` and `YapsShapeSim`.
  - SceneAssetRehomer and MeshGrowth need the SDK.
  - The rest are add-on code.


#### Judge's corrections

1. **Re-run GUID conflict.** The plan saved through `OutputAssetPaths.Claim` and also promised that re-runs keep the GUID. Claim deletes the file at the path, and its own comment says "The GUID changes" (OutputAssetPaths.cs:53-66). Claim is now first-save only, re-runs go through the SaveInPlace pattern, and the required `Reset()` call is added (:31-36).
2. **The add-on's file list was missing.** The add-on carries Editor/Toolkit but only the `YAPS_CORE` Core files (yaps-files.sh:16-21). Added the rule that every callee must be on that list, and checked that the named ones are.
3. **Wrong reason for the add-on calling the brush core.** "The add-on depends on the public package and never the reverse" is wrong: the add-on contains Editor/Toolkit itself (yaps-files.sh:26), and there is no asmdef. The conclusion stands, with the right reason.
4. **Put back is not ready to use.** `Tool()`'s putBack label is hard-coded to "Put the textures back" (ToolkitWindow.cs:322), and `canPutBack` is called unguarded (:309, :323). A small change to `Tool()` is needed.
5. **SkinFrame was underspecified, in four places:**
   - The matrix must be evaluated at the play (rest) pose. "SkinFrame⁻¹(posed)" was ambiguous.
   - Weights must come from `GetAllBoneWeights` (AnimatorMerger.Physics.cs:749-753).
   - The repo's two BakeMesh callers disagree on `useScale` (YapsSquashPainter.cs:445-453 vs HeldShapeFrameProbe.cs:186-193), so the spike fixture now includes a scaled renderer and a vertex with more than four influences.
   - Normal deltas must be renormalised against unit rest normals, the 4.6.2-4.6.4 torn-plug lesson (`DirectionDelta`, YapsBaker.cs:928-932). "Rotation × normal" alone is wrong under scale.
6. **Split/combine verdict reversed to adopt by default.** BlendShapeEditor (free) does merge, concatenate and L/R split. An unstated licence blocks bundling, not users using it. Joe's rule points to adopt. "SmoothRange" is not on its page, so that claim was dropped.
7. **Blend Shape Modifier corrected.** "NDMF-only" holds (NDMF >=1.13.0), and package.json has no VRChat SDK dependency. Its README does not list a split feature.
8. **More existing tools added.** Lattice's Move and Smooth brushes and BlendShape output, with its package.json showing no SDK. Polybrush (free, official, deprecated from 6000.3, skinned support unclear). VRCMeshDeformer's "Save as BlendShape" and its no-bundling terms. Easy Mesh Editor's mirrored keys. MorphMixer. Chillaxins' short known-working list. Starmesh was checked: CVR-tested, but it has no pose capture or split.
9. **PoseMesh restated.** It is a corrective sculpt in a pose, not a pose-to-shape converter. The "no pose capture" claim is now marked as search-based.
10. **The CVR viseme facts were incomplete:**
    - The CCK has `SingleBlendshape` and `JawBone` lip sync modes (CVRAvatar.cs:20), which are an existing fallback.
    - WireFace never writes `visemeMode` and can change `bodyMesh` and blink (CvrSetup.cs:297-298, 312-326).
    - The Toolkit already has a Face card that calls it (ToolkitWindow.cs:488-490).
    - The CVR docs warn that visemes fail if an animation uses the same shapes, so the card always writes new shapes.
    - `DetectVisemes` needs only 7 of 15 (:200), so the test now checks all 15.
11. **Cats details.** Its inputs are exactly A, O and CH. Each viseme is a one- or two-input linear mix scaled by an intensity (tools/viseme.py:97-99, 103-180, 258-284), not "up to three". Cats is MIT.
12. **UnifiedBlendshapes cannot offer L/R names without a Core edit.** Its table is private (:34), only `BuildPlan` is public (:123), and it holds face-tracking names only. Plain suffixes are used instead.
13. **Split simplified.** It uses the painter's posed bake (:427, :445-447). SkinFrame is not needed there.
14. **Painter mechanics completed.** The dab is geodesic Dijkstra over the welded graph (:524-555), which is the painter feel. The welded adjacency already exists (`_near`, :210-222), so it is [M], not [R]. A position brush must rebake per dab. Citations corrected: weld folding :210-222, OnScene :489-522.
15. **ShapeWriter simplified.** Appending needs no clear-and-rewrite; only replacing a shape does. A clashing name must be refused, because AddBlendShapeFrame does not create a new shape for an existing name.
16. **Path and line fixes.**
    - Paths: YapsDebugOverlayBuilder, YapsComponentEditors and YapsSquashPainter are under Editor/Yaps/Setup. CCK_CVRAvatarEditor.cs is in the project's CCK, not the repo.
    - Lines: the frame loop is :546-556; the held-weight pattern is :186-197; the GrabbyBones text is README.md:798-803; the last-frame fold is :941-972; WriteFolder is :253-258.
17. **Avatar cost note added.** AvatarWeight counts only clip-bound shapes as animated (:149-184) and warns past 64 unanimated ones (:410-414). Corrective and viseme shapes count toward that warning.
18. **Tests changed.**
    - Fingerprint: `YapsSquash.Fingerprint` hashes positions only, so the non-destructive test needs its own hash.
    - Added a re-run identity test.
    - Packaging: compile-check runs five combinations (:10-11), and a passing compile there now covers the add-on's file list.
    - Test gate: `CVR_CCK_EXISTS` only.
19. **Decisions added.** Two new rows: split/combine adopt-or-build, and how visemes are wired.

### ChilloutVR Toolkit as its own package: plan 2026-10-17. NOT STARTED, after 5.0.0 (Joe); its add-on defect FIXED ON DEV

Legend: **[M]** means I read it this round in a file, in a command's output or on a web page. **[R]** means it is reasoned or inferred and was not checked. Line numbers are against the `dev` working tree on 2026-10-17.

**Goal.** Ship the Toolkit as a download for anyone with a ChilloutVR avatar, whether or not the avatar came from VRChat. It sits beside the converter package and the 18+ add-on.

#### Existing tools first

- **The CCK's own validator** runs 13 checks, and six of them have auto-fixes. [M] https://docs.chilloutvr.net/cck/validations/
  - The six auto-fixes are Potential Depth Light, Requires Streaming Mipmaps, Unsupported Streaming Mipmaps, Texture Too Large, Non Legacy Blendshape Normals and Missing Scripts.
  - Loud Audio, Long Range Audio and Potentially Non SPI Shaders only warn and have no fix. The SPI check "does not block building".
  - Overlap with the Toolkit, and what that means:
    - "Audio limits" and "Stereo shaders" fix things the CCK only warns about. "The CCK warns, this card fixes it" is true for these two. [M same page, `ToolkitWindow.cs:465,493`]
    - "Check before upload" refuses missing scripts, textures over 8192 and mipmaps without streaming [M `UploadPreflight.cs:45-50`]. The CCK auto-fixes all three itself. The card adds nothing there beyond reporting them. [M]
    - The validator has no survey, weight, free-wins, height or face-wiring step. [M same page]
- **CCK Enhancement** (MIT) merges controllers into the CVR base controller and generates property clips and AAS entries. It overlaps only "Merge animators". [M] https://github.com/milchzocker/cck-enhancement
- **NDMF Merge** (MIT) merges outfits at build time. It needs NDMF and Chillaxins. It is a different job from every card. [M] https://github.com/milchzocker/ndmf-merge
- **NDMF Avatar Optimisation** (MIT, same author, for ChilloutVR) works non-destructively at build time on a clone. [M] https://github.com/MilchZocker/NDMF-Avatar-Optimization
  - It removes unused bones and blendshapes, merges vertices, atlases materials and compresses and deduplicates textures.
  - Its README calls mesh simplification "a provisional experiment".
  - It partly overlaps "What this avatar costs" (unused blendshapes, texture sizes) and "Free wins" [M `ToolkitWindow.cs:385-388,442`]. The approach differs: the Toolkit edits the avatar in place, with undo; NDMF works on an upload-time clone. [R]
  - It covers nothing for audio, bounds, stereo, face wiring, height or the description.
- **Chillaxins** (hai-vr) makes NDMF tools and the platform-agnostic Modular Avatar components work on ChilloutVR. It installs as a `.unitypackage` or via ALCOM, and its page says UPM and tarball installs are not supported. [M] https://docs.hai-vr.dev/docs/products/chillaxins
- **The CCK** installs as a `.unitypackage` into `Assets/`. The setup page does not mention UPM or VPM. [M] https://docs.chilloutvr.net/cck/setup/
- **d4rkAvatarOptimizer** is VRChat-only. Search found no evidence of ChilloutVR support. [M] https://github.com/d4rkc0d3r/d4rkAvatarOptimizer
- **Upshot:**
  - No tool found does the Toolkit's job as a whole, so the work is to package what exists, not to write a new tool. [R]
  - A ChilloutVR optimizer does exist. Card text and the README should name NDMF Avatar Optimisation as the build-time alternative for weight, rather than claim the field is empty. [R]
  - Card text should not repeat what the CCK already says or fixes. [R]

#### Does a third package need to exist?

- The public package already works in a ChilloutVR-only project.
  - `compile-check.sh:60` compiles the whole public tree, minus `Editor/Yaps` and `Runtime`, with `CVR_CCK_EXISTS` alone. [M `:60,124`]
  - `README.md:1756` already tells users so. [M]
- So the gap is not what the code can do. It is three things. [R]
  - Discoverability: a ChilloutVR-only user does not look for a VRChat converter.
  - Size: 69,920 lines of `Editor` C#, against 15,197 in the Toolkit closure. [M `wc -l`]
  - Wording: the package reads as a converter.
- **Option 0:** build nothing new. Give the Toolkit its own README section and a release line pointing to the public package. This is the cheapest option. It is decision 1. [R]

#### How three packages can share a project

Unity's importer adds and replaces files and never removes them [M `Dev/Build/README-yaps.md:43-45`]. Destination paths and GUIDs are shared, so installing two packages overlays files rather than duplicating them [M `yaps-files.sh:14-15`]. Whichever package is imported last wins a shared file. [R]

| Option | Verdict |
|---|---|
| A. A common core package, plus three thin ones | Four downloads, and nothing compiles without the core. It follows the same overlay rules as C and gains nothing over it. Rejected. [R] |
| B. One package with modules | Not possible across the 18+ line: the public package must hold no penetration [M `build-package.sh:237-251`]. That already means two downloads. Rejected. [R] |
| C. The Toolkit as a subset that every package carries, at the same paths and GUIDs under `Assets/AvatarBridge` | This is the model the add-on already uses, and the add-on already ships `Editor/Toolkit` whole [M `yaps-files.sh:26`]. Code import order does not matter while all three come from one commit. `README.md` is the exception: each package swaps in its own text at one GUID, so the last import decides what the project's README says. That is cosmetic, but `README-toolkit.md` should say it. [M `build-package.sh:226-229`, R] **Recommended.** |
| D. UPM or VPM install into `Packages/` | An update would replace the folder whole, so no stale files would be left behind [R]. **Blocked.** [M] The CCK at `Assets/CVR.CCK` has one asmdef, `OpenXR_Configurator` [M, local project]. So `ABI.CCK.Components` compiles into Assembly-CSharp [R], and a package asmdef cannot reference Assembly-CSharp [R, Unity rule]. The ChilloutVR UPM packages that do exist bear this out: NDMF Avatar Optimisation's asmdef references only NDMF and its own runtime, and it finds `ABI.CCK.Components.CVRAvatar` by reflection (`CVRAvatarOptimizerPlugin.cs:185`) [M, GitHub]. The Toolkit uses CCK types directly (`ToolkitWindow.cs:8`), so D means rewriting it around reflection [M]. The MagicaCloth2 deadlock in `avatarbridge-vcc-distribution.md` applies as well [M]. Dead unless the CCK ships asmdefs. |

**Version skew** is the remaining risk.

- An older package imported over a newer one reverts the shared files. The 4.1.0 and 4.1.1 add-ons shipped uncompilable through this kind of shared-subset drift. [M `build-package.sh:117-123`]
- Mitigation: one `BridgeDefines.Version` for all three packages (`BridgeDefines.cs:26`), built from one commit and released together. The rule "delete `Assets/AvatarBridge`, then reimport every package you use" covers all three. [R]

#### What the Toolkit package ships

- **The file list:** `toolkit_files()` = today's `yaps_files()` minus `Editor/Yaps` and `Runtime`, plus `Editor/Core/Physics/SoftBodyContact.cs` **and the `Editor/Core/Physics` folder entry**. [M emulated in scratch: 191 entries]
  - Without the folder entry, Unity would make that folder with a fresh GUID. A later import of the public package would then bring the real GUID for the same path. [R]
  - That leaves:
    - `Editor/Toolkit` and `Editor/UI`
    - the 31 `YAPS_CORE` files
    - `BridgeDefines.cs`, `BridgeLinks.cs` and `LICENSE.md`
    - `AvatarScaler/` (70 KB, which the Height card reaches through `AvatarScalerInjector`)
    - `FaceTracking/` (940 KB) [M `yaps-files.sh:16-29`, `du`]
  - `yaps_files()` then becomes `toolkit_files()` plus `Editor/Yaps` and `Runtime`. One list means the two cannot drift. [R]
- **README:** `README.md` is swapped for `Dev/Build/README-toolkit.md` at the same path and GUID, the same way yaps mode does it. [M `build-package.sh:226-229`]
- **FaceTracking:** the folder holds no code [M `find`]. `FaceTrackingPackages` loads it at run time, and only `FaceTrackingConverter` and `FaceTrackingInjector` reach that [M `FaceTrackingConverter.cs:59`, `FaceTrackingInjector.cs:57`].
  - Those two are called from `CvrSetup.Run` (`CvrSetup.cs:56-57`) and from the converter. `CvrSetup.Run`'s only caller is `AvatarBridgeWindow.cs:1047` [M `grep`].
  - So in a Toolkit-only package, and in the add-on today, the folder can never be loaded. Dropping it from `toolkit_files()` needs no `CvrSetup` split: the code compiles without the assets. Decision 3. [M call graph by grep; reflection callers not ruled out, R]
- **Avatar Tester:** 3 files, 3,455 lines.
  - Its textual closure against the Toolkit list is clean. [M emulated]
  - It has `AVATARBRIDGE_DYNBONE` blocks (`CckAnimatorTester.Physics.cs:32,55,…`), so shipping it needs a DynamicBone compile shape too. [M]
  - The Toolkit already shows its button only when the type exists. [M `ToolkitWindow.cs:164`] Decision 4.
- **Cut:**
  - `Editor/Core/Physics` except `SoftBodyContact.cs` (5,138 lines in all) [M]
  - `Editor/Yaps` and `Runtime`
  - `AvatarBridgeWindow.cs` and the converter pipeline and converters
  - `Presets/`, `Dev/`, `docs/` and `Regression/`
  - `CLAUDE.md`, `AGENTS.md` and `Review.md`

#### Menu, window, wording

- **Folder:** stays `Assets/AvatarBridge`. The overlay depends on that path. [M `ROOT_GUID` at `build-package.sh:32`] `README-toolkit.md` explains it, the way `README-yaps.md` does under "Why this folder says AvatarBridge" (`:29`). [M]
- **Menu:** today it is `Tools/Avatar Bridge/ChilloutVR Toolkit`. [M `ToolkitWindow.cs:689`]
  - A second `MenuItem` at a root without the AvatarBridge name would make it easier to find. Decision 5.
  - Renaming the existing path instead would break the smoke test's menu entry [M `Dev/Tests/AvatarBridgeSmokeTest.cs:492`] and any other probe or doc that names the path [R].
- **"Convert tab" hints** (`:518-522`) are on the PhysBones card.
  - That card exists only when `ConvertPhysBones` is set [M `:123`]. Only `PhysBonesInPlace`'s static constructor sets it [M `PhysBonesInPlace.cs:22`].
  - So a Toolkit-only install never shows them. [M]
- **"Also in this package":**
  - It already hides links whose menu is absent, and offers YAPS as an external link. [M `:145-167`]
  - It detects the converter by `AvatarBridge.AvatarBridgeWindow` [M `BridgeLinks.cs:27-29`], so its YAPS line and its converter check both read correctly in the Toolkit package. [R]
  - It has no "Get AvatarBridge" link when the converter is absent. Decision 6.

#### What needs the VRChat SDK

- Only "PhysBones to cloth". It is gated through the delegate, and `PhysBonesInPlace.cs:1` is `#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS`. [M]
- The Toolkit package does not ship that file, so the card stays absent even where the SDK is installed. That is accepted: converting PhysBones belongs to the converter. [R]
- Nothing else in the closure needs the SDK. [R; the `toolkit:` compile shape is the proof] Without the CCK, the Toolkit window compiles to nothing. [M `ToolkitWindow.cs:4`]

#### Build and verification changes

- **`build-package.sh --toolkit`:**
  - Sets `MODE=toolkit`. It cannot be combined with `--yaps` or `--dev`. `--test TAG` is allowed.
  - Output is `ChilloutVR-Toolkit-<v>-toolkit.unitypackage`, or another labelled name, because every mode carries a label. [M `:22`] The exact name is decision 2.
- **Closure check:** generalise `check_yaps_closure` into `check_closure` and run it on the toolkit list.
  - It is textual and ignores `#if` [M `:141-175`]. **On the toolkit list it aborts today on a false positive:** `UploadPreflight.cs:56` calls `YapsNativeBuilder.ReportLostShaders` inside `#if AVATARBRIDGE_YAPS`, and `Editor/Yaps` is absent. [M emulated in scratch]
  - Fix: in toolkit mode, leave `Editor/Yaps` out of the absent-type scan. Those references only compile where the add-on, which brings the types, defines `AVATARBRIDGE_YAPS`. [R]
  - The scan reads only `find Editor` (`:153`), so a reference to a `Runtime` type is never checked. The `toolkit:` compile shape is what covers it. [M]
  - The abort text says "YAPS package" (`:164,170-171`) and needs the mode's name. [M]
- **`verify_package` after packing:** one check per mode, over the staged `pathname` entries. [R]
  - Public: no `Editor/Yaps`, no `Runtime`.
  - Toolkit: no `Editor/Yaps`, no `Runtime`, no `Core/Physics` except `SoftBodyContact.cs`, no `AvatarBridgeWindow.cs`, no `Presets`. `ToolkitWindow.cs` must be present.
  - Add-on: no converter.
  - This turns the manual "extract both and check the split" step in CLAUDE.md into a hard failure.
- **New `compile-check.sh` shapes:**
  - `toolkit:CVR_CCK_EXISTS`
  - `toolkit:CVR_CCK_EXISTS;AVATARBRIDGE_MAGICA`
  - `toolkit:CVR_CCK_EXISTS;AVATARBRIDGE_MAGICA;AVATARBRIDGE_DYNBONE`, if the tester ships
  - `addon:CVR_CCK_EXISTS;AVATARBRIDGE_YAPS;AVATARBRIDGE_MAGICA`. No shape covers that combination today. [M `:60-65`]
  - Each `toolkit:` shape needs its prefix stripped beside `addon:` (`:67`) and its own file-list case beside `addon:*` (`:122`). [M]

#### README and release notes

- **`README.md`:**
  - Installation (`:243`): add a "Which package" table with the converter, the Toolkit and the 18+ add-on.
  - Toolkit section (`:1756`): "ships in the converter's own package" is already incomplete, since the add-on also carries it. It becomes "ships in every package, and on its own". [M]
  - Mention NDMF Avatar Optimisation, with Chillaxins, as the build-time alternative for weight. [R]
  - Run the anchor check after editing. [M CLAUDE.md]
- **`Dev/Build/README-toolkit.md`:** new file. Cover what is in it, why the folder is named AvatarBridge, the delete-then-reimport-everything rule, and that the README shown is whichever package was imported last. [R]
- **Release notes:** a release gains one asset. The public body gets a single line: "ChilloutVR-only? The Toolkit is its own download." The YAPS dropdown rule does not change. [R]
- **`bug_report.yml`:** has no package field. [M grep] A "which package" dropdown is optional. YAGNI until reports get mixed up. [R]

#### Order of work

1. ~~Fix the `SoftBodyContact` add-on defect (file and folder entry) and add the add-on plus Magica gate. This is needed with or without this plan, before 5.0.0.~~ DONE 2026-10-17.
2. Joe decides 1 to 7.
3. The first spike, below.
4. `toolkit_files`, `--toolkit`, `check_closure` with the `Editor/Yaps` exclusion, `verify_package` and the gates, in one commit.
5. `README-toolkit.md`, the README Installation and Toolkit sections, and this record moved to `docs/archive/` once shipped, all in the same commit as step 4 or the next one.
6. Ship it with 5.0.0 or a later minor, all three packages built from one commit.

#### Decisions for Joe

| # | Question | Default if unanswered |
|---|---|---|
| 1 | A third package, or Option 0: point ChilloutVR-only users at the public package? | A third package (option C) |
| 2 | Package file name | `ChilloutVR-Toolkit-<v>-toolkit.unitypackage` |
| 3 | Drop `FaceTracking/` (940 KB of assets nothing in the package can load) from the Toolkit list? No code split is needed. | Drop it |
| 4 | Include the Avatar Tester (3 files; adds a DynamicBone compile shape)? | Yes, it suits the same audience |
| 5 | Add a menu root without the AvatarBridge name, beside today's path? | No. Keep `Tools/Avatar Bridge/ChilloutVR Toolkit` |
| 6 | A "Get AvatarBridge" link in "Also in this package" when the converter is absent? | Yes, one external link, like YAPS |
| 7 | With 5.0.0, or after it? | After it. 5.0.0 only gets the defect fix. **Joe, 2026-10-17: after 5.0.0.** |

#### First spike

1. In a scratch branch, add `toolkit_files()` and the `toolkit:` shapes to `compile-check.sh`. Run only those shapes.
2. Run `build-package.sh --toolkit --test spike`. Extract it and list its `pathname` entries against the forbidden list.
3. In a fresh project with only CCK 4 installed, import it, open the window and press every card on one avatar.
4. Import the public package over it, then the add-on. Then repeat in the reverse order in a second fresh project.

**Passes when:**

- Every compile is clean.
- The closure check passes without the false positive.
- The forbidden list is empty.
- Every card runs.
- Neither import order logs CS0101 (duplicate type) or a GUID conflict, including on the `Editor/Core/Physics` folder.
- `BridgeDefines.Version` is the same in every case.

**Needs Joe's go-ahead:** it runs csc, a build and Unity.

---

#### Judge's corrections

1. **The claim that no ChilloutVR optimizer exists was wrong.** NDMF Merge's README points to NDMF Avatar Optimisation (MilchZocker, MIT, ChilloutVR, build-time, non-destructive). It partly overlaps the weight and free-wins cards. I also added Chillaxins, which brings NDMF and Modular Avatar to ChilloutVR. I added d4rkAvatarOptimizer as VRChat-only. Sources: github.com/MilchZocker/NDMF-Avatar-Optimization, docs.hai-vr.dev/docs/products/chillaxins, github.com/d4rkc0d3r/d4rkAvatarOptimizer.
2. **"The CCK warns, this card fixes it" was only partly true.** The CCK itself auto-fixes missing scripts, oversized textures and streaming mipmaps, which are three of the things "Check before upload" refuses (`UploadPreflight.cs:45-50`). The line holds only for audio and stereo shaders.
3. **The plan's closure check would abort the Toolkit build on a false positive.** `UploadPreflight.cs:56` names `YapsNativeBuilder` inside `#if AVATARBRIDGE_YAPS`. I confirmed this by running the check's logic on a copy in scratch, without running the build. The plan treated the check's blindness to `#if` as harmless. I added the fix (leave `Editor/Yaps` out of the absent-type scan in toolkit mode) and noted that the check never looks at `Runtime` types (`build-package.sh:153`).
4. **The SoftBodyContact defect is now shown, not just reasoned.** The same scratch run flags `SoftBodyContact` for the add-on list, and `git tag --contains 1598869` is empty. The fix also needs the `Editor/Core/Physics` folder entry. Otherwise that folder gets a GUID that clashes with the public package's (inferred).
5. **FaceTracking (decision 3) does not need `CvrSetup` split.** The folder has no code. Its only route in is `CvrSetup.Run`, whose only caller is `AvatarBridgeWindow.cs:1047`, which the Toolkit package cuts. So the default flips to "drop it". The add-on carries it today with no way to load it either.
6. **The Avatar Tester needs a DynamicBone shape.** It has `AVATARBRIDGE_DYNBONE` blocks, so I added a `toolkit:` shape for it. Its textual closure is clean, so it now counts as checked, not just a reader finding.
7. **Option D's blocker now has outside evidence.** The existing ChilloutVR UPM package references only NDMF in its asmdef and reaches `CVRAvatar` by reflection. The Toolkit uses `using ABI.CCK.Components` directly.
8. **"Import order does not matter" was not true for README.md.** Each package swaps in its own text at the same GUID, so the last import decides what the project shows. I added this to C and to the contents of `README-toolkit.md`.
9. **The menu-rename cite was wrong.** `:157-166` are other menus' `ExecuteMenuItem` strings. The Toolkit's own path is used at `Dev/Tests/AvatarBridgeSmokeTest.cs:492`.
10. **Several line numbers were off.** compile-check's CCK-only shape is `:60` (not `:61`). The 4.1.x history is `build-package.sh:117-123` (not `:123-127`). The README-yaps "never removes" text is `:43-45`. The `toolkit:` shapes also need edits at compile-check `:67` and `:122`.
11. **Some tags were upgraded from [R] or reader finding to [M].** The `ConvertPhysBones` setter (`PhysBonesInPlace.cs:22`), the `PhysBonesInPlace.cs:1` guard and the `HasAvatarBridge` check (`BridgeLinks.cs:27-29`) were all read directly. The line counts (69,920 and 15,197) were measured. The README's "ships in the converter's own package" was already incomplete, since the add-on carries the Toolkit too.

### The atlas: what has never been worn, and what phase D is for

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

*Withdrawn 2026-10-03: `AnotherSocketBaked` is gone, and a body-mesh socket no longer takes the
shader route at all, because a skinned mesh draws with an identity object matrix and the baked
origin lands near the world origin (`yaps_socket.cginc`); `ShapesByContact` sends every mesh but the
socket's own to the contact route. Several bakes per material only matters once the socket anchor
is recovered per vertex, as the plug's is. Until then the lever below is blocked.*

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

### YAPS 5: the plug follows a path, not a point
*Status: the ordered list is BUILT and shipped in 4.6.0, and so is deepest-plug-wins on the
shader route. Portal, duplicate and deepest-plug-wins on the animator route are not.*

*The other half of YAPS 5, how a plug FINDS a socket, lives in `YAPS5.md`, which gathers every
transport, every measured limit, and the order they get built in. This section is what the plug
does once found.*

**This entry said "planned, unstarted" until 2026-09-12, months after the thing it asks for
shipped.** The ask was an ordered list of sockets with arc-length ranges instead of one socket,
and that is `YapsChain` in `yaps_resolve.cginc`: `position`, `forward`, `kind` and a cumulative
`arc` *(WITHDRAWN 2026-10-17: there is no arc field, `yaps_resolve.cginc:45-63`)*, filled nearest-first, a hole ending the path, with `yaps_deform.cginc` selecting the link
per vertex by how far along the shaft it sits. Joe caught the stale status by knowing the feature
existed. A status line nobody rereads is worse than no status line, because it is the one thing
a planning read trusts.

| want | how it falls out | state |
|---|---|---|
| a ring mid-shaft *and* a hole at the tip | two entries in the list | BUILT, shipped 4.6.0, untested in game across two bodies |
| portal: in at one socket, out of another | two entries with a gap between their ranges | NOT built: the cascade is continuous, one Bézier per consecutive pair, and nothing can express a gap |
| duplicate: the shaft showing in two places | the same range mapped twice | NOT built |

A vertex shader cannot create geometry, so a "duplicate" is the existing shaft drawn at two
frames rather than a second shaft. It cannot be two different lengths. **"It costs nothing" was
wrong** (corrected 2026-10-15): one vertex has one position per draw, so the second frame needs a
second copy of every pass the plug's material has, shadow caster included *(only if the passes are
doubled inside the shader, 2026-10-17: a second draw of the shaft gets every pass from Unity)*. That is the plug
drawn twice, which is cheap for one plug and not nothing.

**Design, 2026-10-15, not built, needs Joe's decision.** Read off the shader as it stands:

- **The deform half is small.** A portal is a link whose segment has zero length: the cascade
  in `yaps_deform.cginc` (~798) already walks `fromWorld`/`fromForward` link to link, so a portal
  link starts the next segment at the exit with the exit's outward facing instead of the
  entrance. Triangles straddling the cut would stretch across the room, so the rings within a
  short span either side of the cut collapse to the socket point. A triangle with two corners on
  one point has no area and draws nothing. Inside the body anyway, so nothing visible is lost.
- **The resolve half is the cost.** The plug finds sockets by scanning the atlas cells around
  itself, about 1.2 plug lengths (`yaps-atlas-reach-is-the-grid`). An exit across the room is
  never in that scan, so the ENTRANCE has to carry where its exit is. Today an octant is four
  pixels (position, facing+kind, tags, owner) and every extra pixel per octant widens the rect by
  eight per column. An exit position and facing is two more: six pixels, a wider rect, and a
  protocol bump. **Protocol 9 shipped in 4.7.2**, so this is protocol 10, and a 4.7 wearer and a
  5.0 wearer would be blind to each other until both update. *(WITHDRAWN 2026-10-17, twice over:
  protocol 9 shipped in 4.6.4, read off the tags; and protocol 10 follows only if the exit rides
  in the octant. The plan below puts it in the atlas margin, which no reader draws or reads, and
  keeps protocol 9.)*
- **The light tier cannot carry it at all**: one socket per light path is the whole budget.
  Portals are atlas-only, and the README would have to say so.
- **Authoring:** a socket field naming its exit socket, both on the same avatar, written by the
  avatar's own atlas writer, which can see both bones *(at build time only: at draw time a
  writer knows its own matrix alone, `YapsAtlasSocket.shader:78`, so the exit needs a writer of its own)*. A portal between two people is out: neither
  writer knows the other's exit.

Questions for Joe: is a portal worth protocol 10 in 5.0.0 (the major is the cheapest moment for
the break)? Is duplicate worth a doubled plug draw? Neither can be judged without two clients in
ChilloutVR, so neither is built blind.

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

### Marker lights are a broadcast, and the room is listening
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

### Two walls the transports hit, and whether a shader goes round them
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

#### So: is there shader magic?

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

### The WASM route, and what it would make of all this
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

#### What YAPS becomes with a script

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

### Two constraints worth remembering before designing anything

**ChilloutVR runs an avatar's triggers on the wearer's machine alone.** Anything
contact-driven that the room must see has to be a synced parameter. This is not VRChat's
model and it invalidates the instinct to make contact parameters local because they are
"free everywhere".

**An animator-driven parameter can never sync.** `IsReadOnly` is true for anything
controlled by a curve, and `IsSynced` requires `!IsReadOnly`. Any design that computes a
value in the animator and expects the room to see it is already wrong; sync the inputs, or
compute it on every client from something that does sync.

### A mod-side GPU soft-body system: Joe's bigger idea, 2026-10-07

**Joe's bigger idea (2026-10-07, answered, not started):** a mod-side GPU soft-body system (draw a
blanket with the pen, turn a prop into a soft body). Feasible for mod users in about 2 ms with particles
against capsules (true mesh-to-mesh is not); non-mod viewers can only see pre-uploaded shader-simulated
prefabs. Its own project, much bigger than the GrabbyBones work; first step would be a local one-prop
prototype to prove the budget.

Joe's decisions before leaving it running: commit to dev and deploy to all 8 projects, **no release**
(main waits for him); **one** full throttled corpus run after the fixes; the soft-body collision
option ships **off**; GrabbyBones fork is **research and a written plan only**. He also wants the A/B
videos for Discord if the results hold up (captions name no avatar; the footage shows paid test
avatars, so posting is his call).

Found while he watched the live view: Avatar D's breasts clipped. Two causes, both fixed: the
soft-body spring floor had been bypassed for every `MC2_Preset_Bridge_*` preset (it was meant for
user-saved ones, but those share the shipped file's name, so the floor is back for all), and the
soft-body collision radius came from the chain measurement's narrowest cross-section, 2 mm on a
breast whose bones carry the front skin; it is now the median distance from the collision bone to
the mesh it carries.

Fixed tonight, each measured on its own (runs n0 to n5 in the scratchpad `physab/`): PhysBone 1.0
stiffness (`AsVersion11`, exact); Multi Child Type Ignore rooted at the children (no hinge at the
waistband); squish as bone-length stiffness (rigid without squish; presets had shipped it soft);
hand and finger colliders written as spheres (a capsule under a hand stops ChilloutVR adding the
hand collider that pushes other people's cloth, and on a finger gets it filtered out); the
soft-body collision option and its Toolkit card.

Gap research (decompile of Joe's beta client, so unverified unless noted): **pushing works natively**,
ChilloutVR adds players' hand colliders to every MagicaCloth2 cloth (Joe confirms he pushes cloth in
game all the time); a native grab is possible as an aim driven by the "grab" contact tag but costs
about 97 sync bits per chain, which Joe rejected (four chains would eat the budget), so grab, pose,
stretch and squish go to a **GrabbyBones fork** (plan in progress); `_Angle` is possible natively
with a constraint rig and a parameter stream (32 bits per chain); `_IsGrabbed` approximately with a
"grab"-tag trigger (1 bit; the tag starts on at load until the player's first fist); `_IsPosed` not.

## Minor Ideas

Small features, after 5.0.0, none started.

### Detect Thry's "Unlock all materials" taking YAPS materials back

- Thry's "Unlock all materials" taking YAPS materials back to a plain shader: detect it and offer
  a re-bake. (built: plug and socket inspectors say so beside Re-bake and Build, the upload check
  lists every material by its saved `_YAPS_Enabled`; PreflightTest covers the check and passes,
  2026-10-16. Its setup had used `SetFloat`, which never saves a property the shader lacks, so it
  now writes the leftover entry the way a shader swap leaves it)

### A per-plug menu choice for its own sockets

- A per-plug menu choice. `_YAPS_SelfSockets` is a plain float, so a dropdown could animate it
  between a few sets, at the cost of one synced parameter a plug so every viewer bends alike.

### An id for prop and world sockets

- Props and world sockets carry no id and fall back to the vote. `SeedInstance` would give a
  spawnable its own; nothing needs it while nothing refuses a prop as own.

### The animator merger: weight audit and a corpus fixture


- **The weight audit.** A partial-weight layer writing a property nothing else writes was meant to
  be flagged for creeping to full. Not built: whether it creeps in ChilloutVR, which restores no
  Write Defaults, has never been measured there. Measure in game first, then write the rule.
- **A fixture in the corpus.** The layer rules are proven on controllers built in memory
  (`Dev/Tests/LayerWinnerTest.cs`). An avatar scene with one known hidden toggle and one parked
  state would hold them in the corpus as well.
- **Sub-machines.** A layer with a sub-state machine counts every state reachable, so it is never
  reported. Following transitions into and out of sub-machines would close that.
- **Transitions that can never fire** are used to prune what is reachable, but not reported on
  their own.

---

### The converted avatar as a Variant of the original prefab (parked)

- **B, parked:** the converted avatar as a Variant of the user's ORIGINAL prefab. Never possible on a
  VRCFury or Modular Avatar avatar (their bakes flatten). Needs a link-keeping duplicate (the Ctrl+D
  route, not Instantiate), and Unity refuses to reparent inside a prefab instance: at least
  `ConstraintConverter.cs:668` and `FuryHeadFlattener.cs:78` move existing bones. Probe which passes
  refuse before building. A later VRChat component added to the original lands unconverted.

### A budget planner

**A budget planner.** The sync tally exists in the report; make it interactive. Tick the
features you want, watch the bits, and let it name the cheapest thing to drop. Most avatars
that go over do it by accident, in a menu they never counted.

### A bench, so "optimised" stops being a claim

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
