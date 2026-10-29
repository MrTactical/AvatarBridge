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

### 7. What the shader sweep left open
The record is in `archive/Closed-2026-10.md`, "YAPS against every popular shader".
- **The render queue across a shader swap: fixed on dev 2026-10-25, for 5.0.1 [M].** The stereo
  patcher's copies (`ShaderSpiPatcher.Repoint`) and a material rescued with its shader out of
  VRCFury's temp folder (`SceneAssetRehomer.CopyMaterial`) both dropped a material's own queue:
  3000 read back 2000, so a transparent material drew in the opaque queue. The rule moved to
  `ShaderSpiPatcher.Wear`, which both use and `YapsShaderPatcher.Wear` now calls.
  `Dev/Tests/RenderQueueSwapTest` 2 WRONG before, PASS after; a queue taken from the shader still
  follows the new one.
- **Two latent patcher gaps [R], both fixed on dev 2026-10-25 [M] (Joe: "run them").** A vertex
  function defined in two include files that an `#if` chooses between was patched in the first
  file only, so the passes reading the other drew the plug straight with no refusal: every include
  defining it is patched now, each once. A shader whose only borrowed pass from an unpatchable
  lender is a deferred one was refused: DEFERRED joins the caster and lightmap passes that leave a
  lender alone. The client draws forward: `PlayerSetup.SetDefaultCamValues` sets
  `RenderingPath.Forward`, and `CVRTools.CopyToDestCam` copies no rendering path from a world's
  reference camera. `IncludeVertexTest`: the two-file case failed before, passes after
  ("both files carry the deform (2)"); the deferred case, a lender whose only pass is a DEFERRED
  one with no POSITION, WRONG before and PASS after, the copy compiling. 36 checks, PASS. The
  test's cleanup now refreshes once instead of once per file.
- **Run 427's three metallic-map avatars** should build on YAPS Standard now; the next full run
  checks them.

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
   - **velocityAttenuation fixed 2026-10-26 [M], Joe: "ship it".** Derived damping is 1 (MC2's
     ceiling) for nearly every chain, so the attenuation is the one free coefficient: it now puts
     MC2's slow eigenvalue on the PhysBone's rebased one, or matches the ringing frequency for a
     complex pair (`PhysBoneSolverMap.RestorationAttenuation`, docs/SolverCalibration.md). Stand-ins
     keep the preset's. `PhysBoneBounceTest`: old code WRONG on a loose chain (step error 0.103),
     new PASS (0.006, 0.004, 0.004). Physics A/B, five-avatar set, scale 1, non-soft chains: peak
     error 24.4 to 21.6, overshoot 0.125 to 0.103, settle 0.26 to 0.31 s (mixed, 263 nearer, 278
     further), a few head chains on one avatar now never settle in the window. Forcing 0 everywhere
     overshot nearer still but settled faster than the originals. Copies still overshoot less than
     the originals even at 0, so the rest of that gap is not here.
     `D:/Claude/scratch/bounceab/{scaleab/P1,attnab/s0,attnab/s0.4,fixab/sderived}`.
   - **The rest of the bounce gap, measured 2026-10-26 [M]: no setting the converter writes.** Same
     A/B, one variant each, head overshoot against the originals' 0.215 (copies 0.062): swing leash
     off 0.060; movement smoothing 0 (MC2 default 0.4, never written) 0.061, though jump peaks
     came nearer (error 33 to 26) and shake peaks went further (28 to 38); particle speed limit
     off 0.063; rotation speed limits off 0.061; world and local inertia 1 0.062. What is left is
     MC2's own fixed attenuation of constraint moves (distance 0.3, tether 0.7, `SystemDefine`),
     which ChilloutVR's copy of MC2 fixes too. Writing less damping makes up for it at a price:
     half the derived damping brings head and turn overshoot error to 0.098 and 0.095 (from 0.157
     and 0.110) but swings 2-5 degrees further and walks 5, mean relative error 1.006 of today's;
     a quarter 1.22, none 2.02. Left as it is: a trade, not a fix. `D:/Claude/scratch/bounceab/`
     holds the leash, inertia, rotation and damping runs too.
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
     **Decided 2026-10-25 (Joe): per class.** Breasts and belly keep it, thighs and butt are left
     without (the report says so). Belly was not in the question; it stays on the measured 86a563f
     result (unbounded 160 degrees, limited 34 against the PhysBone's 36). `SoftBodyLimitTest`:
     Thigh and Butt WRONG before, PASS after. Why the limit swings a BoneSpring further is not
     looked into.
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
   is not a straight restoration (the mapping bends it), so linear-curve chains change too.
   **Physics A/B, 2026-10-24 [M], recorded 2026-10-25:** the writer before 0894a9e against after,
   on two avatars with shaped curves and one without (4437 rows, 140 chains;
   `D:/Claude/scratch/curveab/{old,new}`, `ab.out`). A wash with a lean to better: shake peak 47
   chains nearer their PhysBone against 27 further, lie peak 37 against 26, lie 35 against 25, walk
   peak 25 against 17; jump and turn settle lean the other way (4 against 9, 1 against 7). Mean
   errors move 11% at most (shake settle, better), except slide (32 to 45), which is one pair of butt chains on the avatar
   without shaped curves, 2244 to 3170 root lengths on both sides; those chains read 180 degree
   peaks on their source PhysBones too, so the row says nothing about the curve. Not in game. Left as they are, each small: the Immobile
   curve (MC2 inertia has no depth curve), gravity curves (49 shaped; MC2 gravity is one number),
   distance culling.

### 9. The audit of 2026-10-03: what is left, all of it for the game

Joe, 2026-10-26: "work on those". The three with code behind them are fixed on dev; the rest wait
only on the in-game checklist. ASSET-FURYHEAD-REFS (closed as defensive), AM1-10 (decided: no
change) and the docs outside the README moved to `archive/Closed-2026-10.md` the same day.

- **PHY-03, native mode's ancestor rule. Fixed on dev 2026-10-26 [M], Joe to check in game.** A
  contact or collider rebuilt under a rootTransform outside its own object stayed live from load
  with *CVR Native Targets*, so one under an object that starts off, or that nothing ever turns on,
  worked anyway. Both now take the rule animator toggles already had, and the native toggle
  carries collider hosts as it did contact hosts. `ctx.DisabledHosts` records the hosts whose
  component shipped switched off, which the toggle must never wake. `NativeContactRestTest`: red
  before (both contacts under switched-off objects live), green after, collider cases included.
  Still unchecked [GAME]: whether a switched-off host stops a DynamicBone collider (README says
  so). Checklist R14.
- **FT-02, face shapes after Face Tracking goes off. Fixed on dev 2026-10-26 [M], Joe to check in
  game.** The DSR rig's off states were empty, so the last tracked expression stayed. Each empty
  state in a rig layer that drives face shapes now plays the face mesh's own rest weights for one
  frame (read from the mesh, not assumed 0), then hands over to an empty state with the same ways
  out. Held, those weights would sit over every expression sharing a shape, since the rig's layers
  are on top; for one frame they fight neither the expressions nor the native blink. Lives in
  `FaceTrackingInjector.Inject`, so both builders get it. `FaceTrackingOffResetTest`: red before
  (both off states empty), green after. Open [GAME]: Face Tracking off mid-expression lets the
  face go, and gesture expressions still play. Checklist R15.
- **YSH-13, the protocol-light black test. Fixed on dev 2026-10-26 [M], settled without the game.**
  A census of the projects' avatars (7808 files, 2033 lights shorter than 0.5 m) found protocol
  lights authored black and also 1/255 grey (90 plug tips, 25 sockets on digits 3 and 7); every
  coloured one is far brighter. Rendered for real (`MarkerLightColourTest`, linear colour space):
  grey reads 3e-4 of its intensity at any intensity, so a grey socket at 0.354 gave 1.07e-4 a
  channel, just over the plug's 1e-4 bound, and plugs ignored it; a grey tip 31.85 long gave
  0.0097, over the socket readers' 0.01 length. One helper, `YapsLightIsMarker`, now judges colour
  against the light's intensity (0.005 of it) for the plug, the socket, the squash and the
  readout; real light measured at 0.4 to 1 of its intensity. Red before (8 wrong), green after.
  The editor's `YapsScanner.IsProtocolLight` keeps its 0.02 bound, all of which the shader
  accepts. Patched shaders hash the include, so every patch goes stale and avatars need
  converting again. Checklist R16.
- **FT-05, eye gaze on a rolled head bone.** The gaze empties hang off `EyeTracking.Frame`, turned to
  the avatar's facing, so the clips' angles should hold on a head bone that is not axis-aligned.
  Compiled only. Play mode or ChilloutVR: such an avatar looking down and left.
  **Proven in Play mode 2026-10-21 [M]:** `Dev/Tests/GazeRolledHeadTest` builds the rig on a head
  rolled (-90, 0, 37) and on a straight one, both under an avatar turned 30°, keys the empties
  down 20 and left 20 and reads the eyes after the constraints run: both heads, both eyes, down
  0.342 and left 0.321 in the avatar's frame (sin 20° = 0.342). Real eye-tracking input stays on the
  in-game checklist (S6.8).
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
  **Fix (b) for depth effects, branch `plug-ghost-light-atlas`, 2026-10-24 [M], not merged.** Each
  plug gets a light relay: a boneless skinned renderer on the plug's own bounds (so Unity hands it
  the same four lights), drawn in ForwardBase before the grab, which writes the roots those lights
  name into a strip of the atlas: columns 0 to 7, rows 8 to 519, keyed by owner and a plug number
  1 to 15 (`_YAPS_PlugIndex`), two homes, tag, number and the 24-bit owner checked on read. Every
  pass but ForwardBase reads it when nothing else resolved. `PlugGhostProbe` (marker lights only):
  21724 and 26216 depth-only px to 0, ghost 0, on every route at 35° and 60°; the floor shadow
  stays straight (above), so the probe reports it and no longer fails on it. `AtlasSpotsProbe`:
  the relay writes its 16 px and 0 px stay on screen on all three backgrounds. Shader sweep:
  54 of 54 hosts pass, every one as before. Left out: owner 0 (a plug on a prop, someone else's copy before the owner id
  syncs), views too small for the atlas, a socket mesh's opening, one frame late, and the sun's
  shadow. The patch's property block changed, so every patched shader is stale and avatars need
  converting again. **Merged into dev for 5.0.0 (Joe, 2026-10-25)**, before the in-game check:
  S17.9, a plug in a DPS-only socket in a world with depth of field or fog, two clients, no straight
  plug through the skin for either.
  **Later, after 5.0.0 (option B, not started):** the plug's other passes read a per-avatar texture
  outside the grab pool, filled once a frame by a CVRBlitter copying the relay rows out of the atlas,
  which would reach a light's shadow too. Own sockets first; needs the blitter's timing proven in
  game.
- **AM1-14, PlayAudio paths with a slash:** moved to 5.0.1, item 15 (Joe, 2026-10-22).
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
   Same filter, not fixed: **plugs. MEASURED 2026-10-26, broken at every limit; README
   troubleshooting says so.** `ShaderFilterProbe.Plug` (PlugGhostProbe with the plug's shader
   compiled at run time, Sally's plug, sockets at 35 and 60 degrees) at limit 0 / 128 / 2048:
   - atlas route: bends 32k / 54k px off straight unfiltered, **0 px at 128 and 2048**, the plug
     straight past the socket;
   - marker-light route: bends at 2048 (and at 128 at 60 degrees), but the plug draws 60k px
     against 82k unfiltered: **a thin flat strip** to the socket (picture checked);
   - at 128 the forward and caster passes disagree (44k colour-only px), since their loops differ.
   Why, from the filtered bytecode of the same program (vertex, 61 KB): one counter for the whole
   program, loops in this order with their cost a pass: shape keys 121, the light slots 69 (18
   inside) and 68 (18 inside), the atlas scan 39 > 9 > 9 > 36 > home 37 > octant 351 (32 and 18
   inside), the walk 135. At 128 every loop after the shape loop's second pass gets one pass;
   at 2048 one occupied atlas cell's eight octants alone cost 2808, so the scan stops at about
   the first socket it meets and the walk, last, always gets one pass of 48 and lands on the
   curve's end. The socket's atlas writer was NOT filtered in this run (imported shaders never
   are in the editor), so in game it can only be worse.
   Options for Joe, none started: (a) leave it, the README line (done); (b) the walk without a
   loop (eight unrolled arc samples and an interpolated landing), which mends the marker-light
   route only and changes the walk everyone sees, so it needs the bend tester and Joe's eye; the
   atlas scan cannot fit any limit as a loop. Shape keys past the first are lost at 128 (bytecode
   only, not rendered).
   **(b) DONE on dev 2026-10-26, Joe: "do B, see what happens, if compile time explodes, we
   advise".** `YapsWalk` unrolled: eight steps measured by Simpson's rule on the curve's speed and
   one Newton step on the landing, its speed floored at half the step's mean. Offline against a
   4000-step walk on 400 random plug-to-socket curves: mean 0.0025% of the curve, 99th percentile
   0.027%, worst 0.26% (a near cusp); the old 48 chords 0.04% worst; eight chords alone were 2.6%
   worst, and one Newton step unfloored flew 2% at the cusp. Measured (`ShaderFilterProbe.Plug`,
   Sally's plug re-patched from the deployed include in the run): unfiltered every case within 4 px
   of the old walk's bend; at 2048 the marker-light route bends exactly as unfiltered (82,567 colour
   px, the strip gone), the atlas route still 0; at 128 one light case of two passes, either way
   round from run to run, since the four light loops get a pass each and only slot 0 is read.
   Shadows straight at every limit as before. Compile, one plug vertex variant: 2.8 s to 3.9 s,
   72 to 100 KB; all roles 11.5 to 13.7 s. Not an explosion; README now says the limit at 2048 or
   the filter off is the fix, and that older plugs need converting again. Patched shaders inline
   it: avatars see it on reconversion. Left: the bend tester for Joe's eye, then in game.

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
   **The 1.6 leftovers separated, 2026-10-26 [M]** (`YapsBendTester -yapsScale`, Alexa reconverted
   on dev first: its plug in Non Corpus Zone had come out of a 10-25 00:04 conversion unbaked,
   `_YAPS_VertexCount` 0 though that report said baked; a reconvert bakes it, so not reproduced).
   The neighbour pair's 6 cm was the one test place that did not scale with the avatar; scaled,
   10 cm at 1.6 is taken 50 of 50, so that was cell resolution against the plug's length, not
   scale. The other two are one cause: above the top level (1.28 m cells) the coverage block ends
   at 1.23 lengths for a 1.04 m plug while engagement is full to 1.6, so a socket walking out
   leaves the scan still fully engaged (the 90° walk jumps 1178 mm at 1.232L, scenario 3 966 mm at
   1.33L). Tried ending the fade inside the block (far 0.99 of the top cell): 1 and 0.5 still
   pass, but 1.6 fails 6, because the marker-light route takes over at the new edge on its own
   curve (300-500 mm jumps at 1.22L), and 2 and 5 fail 16 and 20, since a plug longer than the top
   cell has its tip out of reach. Reverted. The fix is a fifth level (5.12 m cells), which changes
   the atlas layout and protocol: Joe's call. Tester now: 1 and 0.5 pass, 1.6 fails 2 (was 3).
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
   socket as if it were its baked length.
   **Squash reach scales with the avatar, measured 2026-10-25 [M]** (`SquashPlayProbe -spLook
   -spSwing -spMeet -spRootScale`, a converted pair with Meet on, the swing shots one-sided and
   both). Reach in the world 21.381 cm at 1, 4.276 at 0.2 and 42.763 at 2, exactly the scale; the
   look sheets, framed in reaches, match the scale 1 run to 141 of 1.47 million pixels at 0.2 (the
   zebra stripes' edges) and to none at 2, with the squash and without. The scale has to sit on a
   parent: the avatar's own animator wrote its root back to 0.999 every frame, so a first run
   scaling the root measured nothing. Read, not measured: other players' hands are sized by their
   viewpoint height, own arms by their own bones. Physics under scaling (the cloth's gravity is in
   metres) and all of it in ChilloutVR are still open.
   **Cloth under scaling, measured 2026-10-26 [M]** (`PhysicsAbProbe -abScale` / `-abScaleLive`,
   the five-avatar set, 0.2, 1 and 5 through a parent). Every metric is an angle, a ratio or a
   time; the original PhysBones hang the same at every scale (rest sag 2.9 deg, lying 13.9). The
   cloth depends on WHEN the size changes. MagicaCloth2 multiplies gravity, wind and outside force
   by `scaleRatio`, the cloth's world scale over its scale when it STARTED. Resized after it
   starts (`-abScaleLive`, a second to settle, as Universal Scaling does in play): it holds. Short
   hair lying 8.5 / 8.1 / 7.8 deg at 0.2 / 1 / 5 against 10.2 for the original; what is left is long
   hair and loose chains a little heavier when shrunk (long hair rest sag 11 deg against 3 at 1).
   Started at the new size (`-abScale`, the cloth never sees a change): gravity stays in metres,
   so a small avatar's hair droops (long hair lying 72 deg against 43 at 1, rest sag 22 against 3)
   and a big one's goes stiff (13 lying). The converter is not the cause: `ConvertedGravity`
   already sizes gravity by each chain's world length, so an avatar AUTHORED small is right.
   Read, not measured: MagicaCloth2 takes its start scale from `initData` (written by its editor
   when a cloth is edited in the inspector) or Pre-Build data when either is valid, else from the
   transform at start; the converter writes neither. So which case a user sees turns on whether
   ChilloutVR applies a saved scale before an avatar's cloths start. In game: resize small with
   Universal Scaling and note the long hair's hang; then switch avatars and back with the size
   kept. Heavier the second time = ChilloutVR scales before the cloths start, and writing
   `initData` or Pre-Build at conversion (start scale pinned to the converted size) is the likely
   fix. The probe's press metrics are meaningless at live 0.2 (the press never reached).
   **Joe, 2026-10-26: ChilloutVR DOES apply a saved scale before the cloths start, known.** So a
   user with a saved small or large size gets the drooping or stiff cloth today. The fix, not
   started: pin each cloth's start scale to the converted size. MagicaCloth2's editor writes
   `initData` only as a side effect of drawing a cloth's gizmo or inspector
   (`ClothEditorManager.RegisterComponent`, then `ApplyInitData` once its edit mesh builds), so
   today a cloth has it or not by whether someone looked at it; batch conversions never do.
   Preferred: `initData`, built at conversion the way the editor builds it. When it does not
   validate, MagicaCloth2 warns and builds as now. Pre-Build (`PreBuildDataCreation`) also pins the
   scale, as `buildScale`, but needs an asset per avatar and fails outright when ChilloutVR's own
   MagicaCloth2 rejects the data's version. Failing check first: `PhysicsAbProbe -abScale 0.2`
   (long hair lying 72 deg against 43).
   **FIXED on dev 2026-10-26 [M]:** pass "Pin cloth start scale" (`MagicaClothWriter.PinStartScale`,
   after the last hierarchy change) writes every cloth's `initData` the way the editor does
   (BoneCloth and BoneSpring setups, `ClothInitSerializeData.Serialize`). Same five avatars, built
   at 0.2 / 1 / 5: long hair rest sag 3.14 / 3.13 / 3.13 deg (was 22.4 / 3.1 / 1.3), lying 43.1 /
   43.0 / 27.4 (was 72.5 / 43.0 / 13.1); every class alike. At 1 nothing moved (mean errors within
   0.3 deg of before). Report: "N cloth(s) keep their weight at any avatar size", or a warning
   naming any that could not record. Untested: ChilloutVR's own MagicaCloth2 accepting the record
   (it warns and builds as before if not); Joe in game, a saved small size, long hair.
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

### 24. Kar after reconverting, 2026-10-25: plug gone, a breast "cloned" (Joe, editor screenshots)
- **Plug: FIXED on dev [M].** Kar's plug wears a locked Poiyomi 7.3 Pro, which declares Raliv's
  orifice (`_OrificeData`, `_Shape1Depth`) beside its penetrator, and `YapsLegacyMap.Detect` checked
  the orifice first: the plug read as a DPS socket, so neither the converter nor the YAPS window saw
  it, and the plug Joe had made by hand went with every reconvert. Poiyomi's own `_PenetratorEnabled`
  decides first now. `LegacyDetectTest`: WRONG ("DPS Socket") before, PASS after. Locked, Poiyomi
  bakes the penetrator in (`if((1.0 /*_PenetratorEnabled*/))`), so it takes the DPS route to YAPS
  Simple Lit. Joe to reconvert and check the plug.
- **The clone: FIXED on dev [M]** (Joe: "go ahead with the squash push", then "go ahead with the
  bra strap and cup fix"). Probe (`SquashPlayProbe -spDrag`, Kar's own scene): at the 10:26 build
  the dragged breast slid over a partner that stayed put, two breasts where one should be (Joe
  circled it). With aa4a613 the partner swings aside, but the dragged one cut a jagged slit down
  the partner and the bra tore. Two causes, found with the new tear count (`tear.tsv`) and stages
  switched off one at a time (`-spLookStages`):
  - **Shares over one.** The build eases each part's share toward its peak (`Peak`, half the peak
    reads as a whole share), so a vertex between the two carried 0.7 to 0.9 of EACH and took most
    of both moves. The shader caps a vertex's shares at one in all.
  - **The seam had no sides.** The contact is a plane along the pair's line, so a halter strap over
    the chest, 25 cm off the line and past the plane, was pressed and swollen by up to 10 cm, and
    its rough weights (0.54 beside 0.10 on one edge) tore it. The contact now fades out between 1
    and 1.5 of the part's own breadth off the line. Tighter (0.8 to 1.2), the both-sides seam
    crossed at its ends; cut by the distance out of the part's shape instead, real skin past the
    shape tore.
  - Kar, edges stretched past 3x / triangles turned over, before -> after: one side dragged, bra
    44 -> 0, body 120 -> 34, body turned 820 -> 132; both sides, bra 20 -> 0, body 286 -> 12, body
    turned 884 -> 144. Alexa (AlexaSquash): tube top 95 -> 12 one side, 86 -> 54 both; her body
    unchanged and clean. Swings (`-spSwing`) and hand pushes (`-spHand`) on both avatars render as
    before. The rest of Alexa's both-sides count is a share step across her tube top's cleavage
    midline (0.5 beside 1.0, 3 mm apart): the build copies body shares onto a garment within 3 cm,
    and over the gap one vertex averages both sides while its neighbour sees one. Not visible at
    the probe's framing; a build-side smoothing would need a rebuild to reach anyone.
  - **WITHDRAWN: a flat seam.** First tried pressing both flat at a plane in place of the hollow.
    It cleared the drag's slit, but on a swing into a still partner it showed the flat face Joe
    asked never to see one-sided ("see? when you push, the flat is still happening"). The slit was
    the double shares, not the wrap-round: with the cap, the wrap-round drags clean. Reverted.
  - The tear count's first runs understated the body eightfold: `BakeMesh(m, true)` left a renderer
    at 0.12 scale unscaled. Baked without the flag now, as the film's body bake always was.
  - **My mistake, put right:** a `-spRaw` run set `_YAPS_DentPower` 0 on Kar's seven squash
    materials and the batch editor saved them on exit (16:19), so Kar's squash was off in Attempt
    Conversion until 16:34, when I set them back to 1. `-spRaw` now works on copies, and every run
    script checks the materials after.
  - Joe to rebuild Kar's squash (or reconvert) and look in game: a breast pushed into the other,
    the bra and any strap over the chest.
- **White plug: FIXED on dev [M].** Joe, after toggling the plug on: the plug glowed pure white.
  The DPS route moves it to YAPS Simple Lit, which took Poiyomi's `_EmissionColor` (white) but had
  no `_EmissionMap` (Kar's is black with one dot) and no switch. Simple Lit samples an emission map
  now, and `OnSimpleLit` honours `_EnableEmission`/`_UseEmission` and `_EmissionStrength`.
  `SimpleLitEmissionTest`: 3 WRONG before, PASS after. Kar's baked plug material still holds the
  map, so a re-bake picks it up. Joe to re-bake and look.
- **The converter upgrades DPS/TPS itself: DONE on dev [M]** (Joe's "yes to both", then "go
  ahead"). The window's Build moved into `YapsNativeBuilder.BuildAvatar`; the window calls it, and
  `YapsConverter.UpgradeLegacy` runs it after the last pass on an avatar with no SPS (it replaces
  `ReportLegacyContent`). A Poiyomi DPS plug keeps Poiyomi: `_PenetratorEnabled` off on the material,
  and the patcher turns a locked shader's `(1.0 /*_PenetratorEnabled*/)` to 0 in its copy. Simple Lit
  stand-ins name their bake after the material they stand in for (`YapsBaker.StandsInFor`), which is
  where Kar's three Peen materials came from (a new instance id per fresh bake). `LegacyUpgradeTest`:
  5 FAIL before, PASS after. Kar reconverted twice in batch with Joe's saved settings, scene unsaved:
  "Built 1 of 1 plug and 6 sockets", plug patched from Poiyomi 7.3 Pro, DPS switch 0, the copy's
  `CGI_PoiPenetration` at 0, no shader errors, the second run reused `Peen _YAPS_ 38c45deb9450`.
  ~~Open: an avatar with SPS AND leftover DPS/TPS parts still upgrades only the SPS ones.~~
  **Fixed on dev 2026-10-26 [M]:** `YapsConverter.Run` schedules the upgrade whether or not SPS
  was found, and beside SPS `BuildAvatar` bakes and builds only the parts the SPS route left
  (`only`), since its Build renames sockets and remakes reactions the SPS route keeps as authored.
  `LegacyUpgradeTest`'s new SPS case: red before (the whole avatar rebuilt), green after.
  Joe to look in game: Poiyomi plug, DPS bend off, sockets.
- **Sockets all on in game: FIXED on dev [M]** (Joe, Krystal screenshot: seven socket readouts
  showing). Socket toggles defaulted to the socket's own state (on) until 949789f, and
  `EnsureObjectToggle` kept an existing entry's default on every rebuild; Joe had to switch
  Krystal's seven off by hand. A rebuild now turns an existing one off. `SocketToggleDefaultTest`:
  WRONG (default True) before, PASS after. The readouts draw only while "YAPS readout" is on; it
  reads off by default in the scene, but Joe had switched things off before I read it, so whether
  it shipped on is unknown. Watch for it on the next upload.
- **592 duplicate files** from my probes moved out of the project to
  `D:/Claude/scratch/kar-cleanup-2026-10-25` (SquashTest, Kar 2, Generated/Squash Kar sets).

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
   game. One-sided pushes SUPERSEDED 2026-10-25 by 6: the partner swings aside again.** Joe, 2026-10-24, after "You're stretching the boobs apart ... You have to push them
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
6. **One-sided pushes the partner aside, flat only when both press. Done on dev, Joe to judge in
   game.** Joe, 2026-10-25, dragging one breast's bone into the other in the Scene view and
   the tester: "the same weird planar smush ... It's not moving the other tit the other way ... I
   want the planar to only occur when you're pushing both breasts together at once". This reverses
   the choice in 5 that nothing moves either part away.
   - Cause, measured: the lead was the difference of the two swings over their SUM. In Play mode
     each follow settles about 7.5 cm inward of its edit-mode rest on Alexa with nothing pushing, so a
     22 cm drag of one chain root read as lead -0.47 and both parts flattened most of the way to a
     middle plane. The sum carried the settling; the difference cancels it.
   - Now the lead is the difference over the overlap. What one part drove in further than the other
     swings the other away about its base, by all of it up to a reach, and the seam stays at the
     leader's front, so what swung away is pressed no more. Both driven alike, the lead is 0 and
     the two press flat at the middle as before. Read in C# from the slots: one-sided drag -1.00,
     both-sided -0.10 on Alexa; -1.00 and 0.00 on Kar.
   - The first version ran the shunt through the hand bend, capped by Bend Give: 0.35 of the
     outward reach, 3.5 cm on Kar, so on Kar the partner barely moved and the leader went through
     it (Joe: "you can see how broken that is"). Now the shunt is its own, uncapped by the give.
   - Evidence: `SquashPlayProbe -spDrag` and `-spDragBoth` (new): the first part's chain root moved
     22 cm into the other over two seconds, as the Move tool does, then held; both roots half as far
     each. AlexaSquash rebuilt in place before and after. Images and drag.tsv in the session
     scratchpad `squash-drag/` (before/after one and both). One-sided, the partner now slides away
     in the front view; both-sided, a flat middle seam, unchanged.
   - Kar, `-spLook -spSwing -spMeet` on KarMeetDrag (the look shader reads the deployed include, no
     rebuild): one-sided 0.6 reach, the partner now pushed aside with a curved contact where it was
     cut flat; zebra smooth; both-sided unchanged, a flat seam. Images `squash-drag/kar-shunt.jpg`.
   - Open: the tester's grab pushes a whole cloth component, so a pair on one component moves
     together and never overlaps; the Move tool on a chain root does. **WITHDRAWN 2026-10-25, see
     8:** the Move tool moves nothing in Play mode, and the probe's drag set the root at the start
     of Update, where no user can.
7. **The squash setup, one place each. Done on dev, Joe to judge.** Joe, 2026-10-25: two previews
   (the tester's, and the inspector's that undid it), test hand and player height in both places,
   no telling when to rebuild, and "Paint over them" / "Start empty" saying nothing of what they do
   ("the end user is really fucking stupid and we have to account for that").
   - The preview's controls are in the Avatar Tester alone; the inspector keeps the knobs and gains
     **Test in the Avatar Tester**, which opens the tester on the avatar with the preview running
     and its squash section open. The tester's **Tune it** is now **Settings**. A rebuild started
     with the preview on starts it again.
   - One status line (`YapsSquash.Status`) for the inspector, the tester and the Setup card, naming
     why a rebuild is needed: built by an older YAPS (any squash material `IsStale`), the painted
     body changed, or the parts or layers left alone changed. Rebuild is primary only then.
   - The parts step opens on what squashes: the pairs the bone names give, coloured on the body
     (shown, not kept), and one button, **Change these areas**, that starts painting from them;
     nothing found says so and offers **Paint a pair**. The colours hide while the preview or Play
     mode runs, instead of only showing while painting.
   - Knobs moved in Play mode stayed on the materials while the component went back: re-applied
     when Play ends.
   - Evidence: `UiOffscreenProbe` renders of the inspector and tester on KarMeetDrag (built before
     aa4a613, so it read "built by an older YAPS", as it should); the thighs story
     (`button-smoke.sh -bsStory thighs`) pressing Change these areas, Test in the Avatar Tester,
     the tester's Squeeze, Test hand and Bounce, and a knob moved while previewing.
8. **In Play mode nothing a user could do pushed one part into the other. Done on dev, Joe to
   judge.** Joe, 2026-10-25, after rebuilding Kar's squash: in Play mode he dragged the
   MagicaCloth_Breast_L object into the other side with the Move tool, "it's still not pushing the
   other boob the other way. It's still flattening", and "the squish you're seeing and the squish
   I'm seeing are completely different". The step's hint told him to "drag one part's bone into
   the other in Play mode".
   - Measured on Alexa (`SquashPlayProbe -spDrag` with the new `-spDragName`, `-spMoveTool`,
     `-spGrab`, `-spPush`; `drag.tsv` under the session scratchpad `alexa/`): the cloth component's
     object dragged 22 cm moved the follow 0 cm. The chain root set between frames, as the Move
     tool sets it, sat 22 cm off where it was set every frame and the follow never moved: MC2's
     `RestoreTransformJob` puts every cloth transform back to its pose, the fixed root included
     (`VirtualMeshProxy.cs:1605`), before it reads them. The tester's grab moved the follow 5 cm
     of a 22 cm pull, short of contact on Alexa. Only a root set at the start of Update, the
     probe's drag, moves the chain: what every earlier render showed and Joe could never make.
   - Now the tester's squash section has **Push one in** in Play mode: the first pair's first
     part moved toward its partner by its chain root, at the start of each frame's Update, by up
     to half the gap, kept in the root's parent's space and put back at 0 or when the preview
     stops. Squeeze starts at 0 in Play mode, since a one-sided push under a both-sided squeeze
     read as flat. The hints say the Move tool cannot push a cloth part in Play mode.
   - The paint step said "Painted: 2 pairs on "Kar", which win over any bones" and its colours
     vanished in Play mode with no word why. It now names the pairs ("Squashing Breast and Butt,
     painted on ..."), says where the colours show and that they hide while the squash is tested,
     and in Play mode greys out painting, since the regions live on the component and a Play-mode
     stroke would be lost when it ends.
   - Evidence: `-spDrag -spPush` drives the preview's own code. Alexa: the follow 7.6 to 22.0 cm
     off its rest, overlap 6.9 cm, lead -1.00 (one-sided), no compile error. Kar: 21.4 cm, overlap
     21.3 cm, lead -1.00. The probe's own drag on Joe's 18:48 rebuild tears as at 048465f (body 296
     edges opened past 1 cm, panties 0), so the rebuild carried the fix.
   - **Fixed: a butt cheek pushed into its partner tore the panties.** The first Push moved every
     pair's first part; on Kar the cheek's push opened 788 panty edges past 1 cm, widest 15.3 cm,
     and about 780 more on the body (`kar/push` in the session scratchpad). Cause: the shunt (the
     partner swung away) was laid on each vertex by its share, and a thong strip bridging the
     cleft copies its share from the skin either side, stepping 0, 0.5, 1 across about 1.5 cm.
     Now the build writes `_YAPS_DentSwing`, each renderer's own skin weight on each part's chain
     (root included), and the shader lays the shunt on by it, faded from the part's base to its
     tip as a swing. Vertices with no share but some swing weight now enter the part loop for the
     shunt alone (the seam and squash still weigh by share). Opened past 1 cm from the pre-drag
     pose, skin-only in brackets: Kar cheek at a quarter push, panties 204 to 0 (0); full push 966
     to 580 (196), widest 15.3 to 5.2 cm; Kar breast push, body 1118 to 1100 (1100), bra 251
     (360); Alexa's top 157 (143). Tried and dropped: the shunt rigid by swing weight (Kar breast
     body 1890, Alexa's top 13.6x, its own weights step 0 to 0.76 across one edge); capped by
     share (tore a layer where the share ends); the swing set without the chain root (the cheek
     swung 9% of its skin, so the shove all but vanished).
   - **Soft-body pair contact (Joe: "boob mesh to boob mesh squishy, genuine soft body").**
     `YAPS_SQUASH_BULK 0.6` of each part's overlap now goes into the whole-part squash (flatten
     toward the partner, grow the other ways, volume kept); the seam presses only the rest. Pressed
     from both sides at 0, the two met as halves of one ball cut down a plane; at 0.6 they round
     off into each other with a short seam. Kar squeezed both sides, body edges opened from start:
     264 at 0, 120 at 0.6 (skin alone 694). One-sided pushes are unchanged, as all of the overlap
     goes into the partner's swing there. At 0.9 the pair parted at the top of the cleavage.
     Still a fitted shape (six reaches), not the real mesh: a contact read from the mesh itself
     is the next step if this does not read as soft enough in game.
   - **Grab chains, measured (Joe: "it's just not moving the other titty").** Kar as converted
     2026-10-25 20:06 (soft body physics on), `-spDrag -spGrab -spLookStages slots`: the shader reads
     the grabbed part from the skinned helper (source 1), its follow 3.0 cm off rest exactly as the
     bone at frame end, the matrices 0.0. So the squash sees a grab. The grab itself moves little:
     3.0 cm on a 21 cm pull at one push a frame, 6.1 cm (skin 6.7 average, 8.4 most) at five
     pushes a frame toward 42 cm (`-spGrabTimes 5 -spDragScale 2`); the soft body spring and 50°
     limit hold it. At 6 cm the partner shoves about as far, hard to see on black latex. Joe's
     screenshot shows a far bigger move passing in front with no shove: not reproduced yet.
     ~~At rest the soft body spheres move nothing (both follows 0.00 cm off rest, on or off).~~
     **Withdrawn:** measured on the scene's own Kar, saved 19:58, which predates the 20:06
     conversion and has no spheres. The grab numbers above share that copy (same breast cloth
     but for radius and colliders).
   - **Soft body physics spreads the pair on entering Play (Joe, reproduced).** `-spPrefab` plays
     the converted prefab instead of the scene's copy. Kar 20:06 prefab, at rest: edit-mode gap
     42.79 cm with both follows on their rests; in Play 51.2 cm, follows 3.9 and 4.6 cm outward.
     Spheres taken out of every collider list before Play (`-spNoColliders SoftBodyContact`): 47.9
     cm, right follow still 4.2 cm out (the left root is pinned by the probe's drag), both breast
     roots turned 14 deg off edit pose. Cloth off (`-spNoCloth`): 0.0 deg, so the animator does not
     do it. The scene's 19:58 copy: 0.0 deg. Its breast cloth differs only in radius (0.191 vs
     0.140) and the collider list, gravity 0, spring 0.06, root rotation 1 in both. So the spheres
     add about 3 cm and something else in this conversion turns the roots 14 deg. Next: the prefab
     with radius put back to 0.191, then a diff of everything else between the two copies.
     **Fixed, same night.** The cause was the sphere's own object: `SoftBodyContact.Link` put each
     sphere on a child "SoftBodyContact" at the collision bone, and MagicaCloth2 simulates every
     transform under a chain's root, so it became a bone of no length (`-spDestroy SoftBodyContact`
     before Play: 0.0 deg, gap 42.74). The sphere now goes on the bone itself, told from an
     author's collider there by its size; a run on an older link moves each list's reference to it
     and deletes the child (`-spRelink` on the 20:06 prefab: gap 42.86 cm against 42.79 in edit
     mode, roots 2 deg). Measured with the fixed spheres under a strong grab (`-spGrabTimes 5
     -spDragScale 2`): the grabbed breast stops at 4.97 cm instead of 5.77 cm, and its partner does
     not move (0.07 cm, as at rest). So the option stops a part at the other's rest and never
     pushes the other aside; the window hint and README now say so.
     **Removed 2026-10-26 (Joe: "remove the top one").** With Soft body shape doing what can be
     seen, the physics option only stopped a part at the other's rest, under a second control
     that read like the first. The setting, its pass, the Toolkit's Soft bodies card and
     `SoftBodyContact.cs` are gone; an avatar converted with it keeps its spheres until converted
     again. Joe, the same day, of the squash on Kar: "FINALLY, BEAUTIFUL!!"

### 25. Mesh-to-mesh squash contact. Joe, 2026-10-25. PLAN, to start 2026-10-26

Joe: "boob mesh to boob mesh squishy, genuine soft body", then "plan that for tomorrow". Ask him
first: 5.0.0 or 5.1.0 (on 10-24 he put new squash ideas in 5.1.0). **Decided 2026-10-26 (Joe): 5.0.0.**

**The aim, in Joe's words (2026-10-26):** "when you push the boob into the other, even though it
pushes the boob, it still squishes a small amount, and can dynamically fix the problem about the
flat plane not being dynamic enough". So one contact against the partner's real surface replaces
the flat meeting plane, and that one contact's depth drives both answers: how far the partner
swings away and how much each part squishes, never one without the other. The current squash is
the baseline he approved ("FINALLY, BEAUTIFUL!!", 2026-10-25): every gate below compares
against it, and nothing it does well may get worse.

**Today.** Each part is an ellipsoid of six measured reaches in its own frame (a toward the
partner, o out of the body, u across): `YapsExtent`, `YapsInside` and the quadratic solves in the
seam pass (`leave`, `disc`) all read that ellipsoid. The pair meets where the two ellipsoids
overlap; the partner swings away (shunt, by swing weight, faded base to tip), 0.6 of the overlap
squashes each whole part, the seam presses the rest, bowed round the leader's front.

**What mesh to mesh can mean in a vertex shader.** A vertex sees only itself and what textures and
buffers carry. The partner's live vertices have no transport (40k positions a frame). Its *shape*
can travel instead: baked once from the mesh in the part's own frame, then turned and placed every
frame by the slots the shader already reads, cloth included through the skinned helper (measured
2026-10-25: source 1, exact to the bone). So: the real surface, moving as the part moves.

**Step 1, the real shape (build only).** Per part, a radial map: on a 32 x 16 grid of directions in
the part's frame, how far its skin reaches from the centre (the outermost skin in each direction's
cell, from vertices with share >= 0.5; empty cells filled from neighbours; one texel of smoothing).
All parts stacked in one RHalf texture, 32 x 64, `_YAPS_SquashShape`, saved non-readable with
streaming mips like the share textures (CCK upload check), carried by `Carry` and `Owned`.
**Gate 1, a probe, no shader change:** distance from every part vertex to the ellipsoid and to the
map surface, mean / 95th / max cm per part, on Kar, Alexa and three more squash avatars, plus a
picture of each surface drawn over the skin. Go on only if the map is clearly closer; where it
should be: a breast's underside and top, a cheek's side toward the cleft.

**Step 2, the shader reads it.** `YapsExtent(p, w)` becomes a lookup in the turned frame (bilinear,
wrapping in longitude, clamped at the poles); the ellipsoid stays as the fallback when no map was
baked (older builds, unframed parts). `YapsInside` and the seam's ray solves become a fixed
four-step secant on the map, unrolled, since ChilloutVR's shader filter cuts loops. Everything
that reads the shape gets the real surface at once: the contact, its bow round the leader's front,
the swing, the hands' dents, the swell.
**Gate 2:** the squash probes before and after (`-spSwing`, `-spDrag`, `-spDragBoth`, `-spPush`,
looks on the real materials of every layer, from-start tear counts no worse), the patched shader's
compile time, and the filtered path (`-sfRuntime`). Then Joe in game.

**Step 3, pressed along the partner's surface.** Where they meet, a vertex goes out of the other's
mapped shape along that surface's normal (from the map's gradient), not along the line between the
two centres; the lead still decides who keeps its shape (the leader keeps it, the partner takes the
hollow, alike both take half). The bulk share stays.

**Left out, said plainly:** each part sees the partner's rest shape turned and moved, not the
partner's own squash; a jiggle that rings on after the contact needs state kept between frames
(the CVRBlitter self-update route, its own spike); a shape a ray from the centre crosses twice keeps
only its outer surface. Cost: a 4 KB texture per avatar and a few texture reads per vertex per part.

**Gate 1: PASSED for breasts, measured 2026-10-26 [M]** (`Dev/Probes/SquashShapeProbe`, six
converted prefabs measured by the squash build itself through `YapsSquash.MeasuredForTest`, which
stops a build after measuring). The map is built from every other part vertex and judged on the
rest, so it is not graded on its own fit. Radial distance from the part's centre, mean / 95th cm,
ellipsoid against map 32:

| part | ellipsoid | map32 | worst region, ellipsoid / map32 (95th) |
|---|---|---|---|
| Kar breast | 1.6 / 5.2 | 0.33 / 0.84 | top 8.6 / 1.6 |
| Kar butt | 3.4 / 6.5 | 0.73 / 2.2 | underside 6.1 / 1.0 |
| Angela breast | 2.4 / 7.6 | 1.2 / 3.0 | toward partner 8.4 / 1.7 |
| Angela butt R | 4.9 / 10.7 | 1.8 / 4.3 | top 15.5 / 4.5 |
| Angela butt L | 5.8 / 14.0 | 3.5 / 11.6 | underside 18.4 / 15.7: both wrong |
| Momscarada breast | 1.6 / 3.2 | 0.63 / 1.5 | top 4.4 / 1.5 |
| Momscarada butt (41 vertices) | 1.7 / 3.3 | 1.8 / 3.0 | no better |
| Lady Adrina upper breast | 1.3 / 2.6 | 0.43 / 1.2 | underside 2.9 / 0.6 |
| Lady Adrina breast | 1.5 / 3.9 | 0.69 / 2.1 | underside 2.9 / 2.5, near even |
| Pheromosa breast | 7.6 / 16.1 | 1.4 / 3.9 | top 21.3 / 5.0 |
| Pheromosa butt | 5.4 / 12.3 | 1.6 / 4.1 | underside 10.7 / 4.2 |
| Alexa breast | 5.6 / 17.2 | 1.5 / 7.7 | top 24.2 / 6.5 |

The ellipsoid's signed error runs up to +13 cm (Pheromosa toward the partner): skin well outside
it, which is the flat-plane complaint in numbers. Map 16 is too coarse (worse than the ellipsoid on
two undersides), so 32. Angela's left cheek is not one surface seen from its centre (the section
picture crosses itself), and neither model fits it.
Decisions this sets, for Step 1: an octahedral 32 x 32 grid, folded on the side into the body
where a part has no skin of its own, so the skin's side is one continuous diamond with no poles
(replaces the 32 x 16 latitude grid above); and the bake keeps the ellipsoid for a part whose
map does not beat it on the held-out half, which covers the low-poly and folded parts above
without a vertex-count rule. Next: Step 1, the bake and texture, then Gate 2.

**Step 1 DONE on dev 2026-10-26 (build only, no shader reads it yet).** `YapsSquash.Surface` per
part: map from every other vertex judged on the rest; the map is kept when its mean is at most
0.75 of the ellipsoid's AND at least 2% of the mean reach better, then rebuilt from every vertex,
in follow units. `_YAPS_SquashSurface`, RHalf 32 x 128 (part m in rows 32m to 32m + 31, zero for a
part on the ellipsoid), point-sampled, saved non-readable with streaming mips, `<avatar>
surface.asset`, carried by `Carry` and listed by `Owned`; the patcher declares it (black), so a
material without one reads 0 and falls back. The build's Console line names each part's choice.
`SquashSurfaceTest`: an ellipsoid with exact reaches keeps the ellipsoid; a rounded box gets the
map, 0.12 cm off against 1.52, its radius read back within 3.9% on the skin's side, 1.1% at a
corner, and a follow scaled 2 halves every value. Note from it: with the build's 98th-percentile
reaches even an ellipsoid-shaped part sits 0.6 cm outside its own ellipsoid, so the map wins there
too, rightly. ~~The patcher declares the texture (black), so a material without one reads 0.~~
**Corrected in Step 2:** presence is a per-part flag, `_YAPS_SquashSurfaceOn`, not the texture.
A material patched before it never declared the texture, and what Unity binds for an unset one is
not known to be black; an unset vector reads 0.

**Step 2 DONE on dev 2026-10-26, Gate 2 PARTLY.** `yaps_dent.cginc`: `YapsSurfaceAt` (octahedral,
bilinear by four Loads per part block, the same grid as `YapsSquash.SurfaceCell`); `YapsExtent`
reads it where the part's flag is set, so the contact, the swing, the dents, the seam's pole, the
flattening's widths and the stretch all take the real shape (CORRECTED below: the flattening's
widths, the stretch's pivot, a hand's deepest and the lying-back press went back to the ellipsoid,
`YapsSpan`, for the compile time); `YapsLeave`, one radial step and three
secant steps, unrolled, from the ellipsoid's answer, eased back to it where the steps end off the
surface, gives `YapsInside` and the seam's line solve the real surface. The seam's normal stays the
ellipsoid's. Patched shaders INLINE this code: an avatar sees it only once its squash is rebuilt.
**Step 3, first half, Joe's aim:** `YAPS_SQUASH_GIVE` 0.3. Before it, a part pushed by its partner
swung away by all the overlap and its squash netted to exactly 0, and the leader's share was 0 too:
a one-sided push squashed nothing. Now the pushed part swings by 0.7 of the lead and both squash by
the 0.3 left, each its share; the seam still bows by the whole lead (a flat one-sided seam was
rejected 2026-10-25). README Squash paragraph says both. **Joe 2026-10-26, from the film
(`SquashPlayProbe -spVideo -spRebuild`, with a new one-sided push shot): looks right, 0.3 stays,
no knob.**
Gate 2 so far: `SquashPlayProbe -spLook -spSwing -spMeet` on Kar's prefab, new `-spSurface` (the
surfaces measured from a copy and set on the materials in memory, put back on quitting; Kar's
squash `.mat` md5s unchanged after every run). The look shader reads the deployed include, so these
are the new code; the real-material shots are not until a rebuild. Ellipsoid against surface alone:
one-sided pushes subtly rounder, both-sided unchanged (a flat seam is right for two parts driven
alike), zebra equally smooth. With GIVE: the hard one-sided push shows both parts pressed where the
pushed one used to slide away whole. Still to run: a rebuilt copy for real materials, drag tear
counts, `-spPush`, `-spCost`, the patched shader's compile time, the filtered path; then Joe in
game. Open for Step 3's second half: pressing along the partner's surface normal.

**Gate 2 DONE in the editor 2026-10-26**, Kar's prefab, the built materials (old) against a
`-spRebuild` copy (new), same frames:
- Compile, one vertex variant (`YapsCompileBisect -ycbCases`, twice, steady to 10%): squash only
  4.3 s old, 48-50 s with the map in every `YapsExtent`; squash+socket 4.7 s, 54 s. Bisected by
  call site on edited copies of the patched shader: the secant solve (40 inlined reads, outside the
  per-part loop) costs nothing; neither one filtered sample for the four Loads, nor read-then-select,
  nor `[branch]` helps; the cost is the four sites in the per-part loop that only SCALE something
  (a hand's deepest, the lying-back press, the flattening's six widths, the stretch's pivot). Those
  went back to the ellipsoid as `YapsSpan`: squash only 7.2-7.5 s, squash+socket 8.5 s. All three
  roles: 11.5 s old, 16 s new, the fix does not move it. Not chased further.
- Accuracy of the solve on Kar's real map (a Python copy of `YapsLeave`, 1500 lines a part, against
  a bisected exit): three secant steps and two agree (mean 0.01-0.05% of the reach); one step 0.05-
  0.16%, 95th percentile at most 0.3% (0.6 mm); the rare large misses are grazing lines that pick
  another crossing and do not shrink with more steps. Kept at three; one is there if cost matters.
- Tears, drag one part 21 cm: worst stretch 4.05x old, 3.77x new; seams opened over 1 cm 214, 262;
  over 2x 40, 62 (GIVE squashes both now, so more edges move). Drag both: worst 3.58x, 3.20x;
  opened 16, 0. Push hard and hand dents: the same to the eye, zebra smooth.
- GPU, the probe's frame (ms, one avatar): squash on alone 1.44 old, 1.70 new; 64 players' hands on
  one part 6.2, 8.6. Ten avatars: 3.19, 3.60; 8.9, 11.7.
- Shader Filtering at 128 against off, new code: a few small patches where the parts meet differ,
  at rest too; nothing breaks broadly. Old code under the filter not run.
- CORRECTION to "md5s unchanged after every run" above: true of the `-spSurface` runs; the look
  runs on the built materials (no `-spRebuild`) saved `_YAPS_SquashPulse` 0.6 into them on exit.
  Restored from the backup; the probe now puts the pulse and the meet flag back (84147d2), checked
  by md5 after a `-spPush -spLook -spMeet` run.
Next: Joe in game, on a reconverted avatar.

**Step 3, second half, DONE on dev 2026-10-26 [M].** The seam's normal comes from the leader's own
surface (`YapsSurfaceNormal`, four reads a texel and a half either side; the ellipsoid's gradient
where no map was baked), and the pressed skin goes out along it to the seam's tangent plane,
`seamN * dot(seamN, facing) * back`, not straight back along the line: the same where the seam is
flat, and off a bowed front it slides round it. Kar's prefab, `-spRebuild`, against the committed
include, same frames:
- Looks: one-sided swing and drag, the long crack down the meeting line is gone (the part Joe
  circled, 2026-10-26); both-sided swings within 0.3% of pixels, both-sided drag identical to the
  digit. Joe: "still being jagged": a short sawtooth sliver at the bottom of the contact on the
  deepest drag, and faint lumps near its top. Fixed below.
- Drag one part 22 cm: body edges turned over 365 to 119, over 2x 62 to 36, worst 3.77x to 3.59x,
  widest opening 3.9 to 3.2 cm, but opened over 1 cm 262 to 330; bra turned over 112 to 56, worst
  2.78x to 3.20x, over 3x 0 to 4.
- Compile, one vertex variant: squash 7.0 to 8.1 s, squash+socket 7.1 to 7.8 s, all roles 14.6 to
  15.5 s. GPU, the probe's frame: level (1.70 and 1.74 ms, squash on alone).
- Shader Filtering at 128 against off: 0.07-0.28% of pixels differ, as the old code's 0.12-0.22%.
- Alexa's deepest drag shows the partner through the pressed patch with the old code and the new,
  fainter now.
Tried and dropped, both on Joe's "needs some smoothing": the normal read three texels apart, and
the shading turned over twice the press's width. Each made the band along the meeting line darker
(the contact face is turned toward the partner, so that band is right; spreading it shows more of
it), and the wider read changed the drag only within noise.

**The jag, FIXED on dev 2026-10-26 [S].** Cause, instrumented: the leader gives only where it is
inside the other, `min(v, YapsInside(...))`, and that depth was read along the line between them.
Near the other's outline the line only grazes its surface, so the depth jumped from about 3 cm to
nothing between neighbouring vertices: body edges wholly on the leader stretched 2x in a band down
its front, a steep step that shaded dark and followed the triangles. Now the depth is read square
to the other's fitted shape (times the cosine of the line against the ellipsoid's normal where it
leaves), which cannot change faster than the skin does; at the middle of the contact the cosine is
1. How it was found, `SquashPlayProbe -spLookStages`, now drawn per layer too:
- `branch` colours the seam's choices; the sliver sat where the leader's depth won, not on a
  missed line or an eased surface solve.
- Pushing the pressed part's skin, then the leader's, a centimetre further back left the streak:
  not the two skins crossing. `nocut` removed it, and creased the leader's front everywhere else.
- Read off the stop margin (`inside + stop`) and against the partner's eased skin: thinner, not
  gone. The surface map's own normal in place of the ellipsoid's: the same within noise, at four
  more surface reads, so not taken.
Measured, Kar `-spRebuild`, against the committed include:
- Drag one part 22 cm: body over 2x 36 to 28, opened over 1 cm 330 to 282, squashed edges from the
  start 1068 to 1008; bra over 2x 28 to 6, over 3x 4 to 0, worst 3.20x to 2.19x, turned over 56 to
  25, opened over 1 cm 30 to 2. The sliver is a faint line, the lump near its top gone, and the bra
  over it no longer tears open along the meeting line.
- Both dragged alike: identical (the cut scales with the lead). Swing, push, hand: summaries
  identical; looks differ only near the one-sided meetings, no new crease.
- Compile, alternated twice each: squash 8.6/9.1 s to 9.0/8.2 s, all roles 18.3/19.3 s to
  18.8/20.6 s; 1% more bytes.
- Shader Filtering at 128 against off: 20 of 25 looks differ by a few thousand pixels, as before
  the change (20 of 25).
Next: Joe in game on the deepest one-sided press.

**Field seam, SPIKE 2026-10-26, not adopted [M].** Joe: "think outside the box". The literature:
implicit contact (Gascuel 1993, Desbrun and Gascuel 1995, Vaillant et al. 2013) and Brunel,
Bénard and Guennebaud 2021 (a shared contact surface between the two meshes by relative stiffness,
contact left where either surface turns past 80° from the push, a C1 falloff outside; its falloff
and bulge need mesh connectivity, so only the contact half carries to a vertex shader). Tried in
memory only (`SquashPlayProbe -spDent <file>`, the spike built from the live include by a script
kept outside the repo): the bow, plane and cut replaced by the other part as a shape, its depth read
along its own radius, this side pushed out along that radius by its share (all of it pressed by a
leader, half alike), the partner's field moved by the shunt and bulk its own part pass gave it.
Kar `-spRebuild`, against the committed seam:
- Drag 22 cm: leader-only bad body edges 51 to 0, the sliver gone, bra over 2x 6 to 0 and turned
  over 25 to 7; but body over 2x 28 to 150, opened over 1 cm 282 to 548, in the pressed part's
  hollow (hidden behind the leader from the front): pushed out along the leader's radius, its skin
  spreads sideways round it.
- Dragged alike: bra turned over 73 to 1; body opened over 1 cm 0 to 498.
- Swing, both deep: the line between the two is gone, the pair reads as one shape. Real flesh
  keeps that line; the seam's crease is what draws it.
Hybrid, tried next (Joe: "use both methods"): along the line where the other's surface faces the
push, turning to the radius as it turns past 50° to 80° from it. Bra better than the committed seam
(drag over 2x 6 to 0, turned over 25 to 6, opened 2 to 0; alike turned over 73 to 1), body alike
about level (opened 0 to 0, turned over 173 to 141), body led mixed (opened 282 to 190, but over 2x
28 to 52, over 3x 4 to 8, widest 3.2 to 4.5 cm). The line between two pressed alike is still gone,
same as the radius spike: the crease shows at the rim of the contact, and the rim is exactly where
the other's surface turns side-on, so the radius takes over there and pushes the skin out into the
groove. The line and the radius want opposite things at the one place the line is seen, so no
threshold keeps both. Not adopted; the committed seam keeps the line and its jag fix stands.

**Squeezing deleted mass, MEASURED 2026-10-26; the give-back WITHDRAWN the same day, see below.** Joe, on the side-by-side films: "boobs
suddenly delete mass the moment they squeeze together"; the swell at three and five times (wider
too) still read wrong, and the hybrid's clean merge read as the two clipping into each other.
`SquashPlayProbe -spSeamFilm` now counts at rest and mid hold of each of its six presses the
volume the squash adds, each part's own (`volume`) and as seen, counted once where skin overlaps
(`seen`), and how deep the seam cuts (`cut`). Kar, the live seam: at rest -1.0%; one dragged
-1.2%, both dragged -6.8%, one swung into a still partner -3.3%, both swung deep -8.1%, a hand
-2.1%, two hands squeezing -5.5% of the pair's volume. Seam off, the squash keeps it (-7 cm3 of
311). The swell is sized by the depth the part pass leaves (0.1 to 3 cm), but the seam cuts the
real skin 4 to 11 cm deep, 7 to 10 cm off the line between the pair: contact on a real pair
lands off that line, and a part bent or swung about its base sweeps its tip in further than its
centre. Three times the swell gave back 23 cm3 of 311. Next: size what comes back from the
press itself, calibrated on these six to keep volume, with the spill reaching marshmallow
(`marshmallow-pb` grows a squeezed part 1.5 to 1.9 times) at its top; exact needs the shader to
sample the part's baked surface round the contact.
Tried, in memory: the swell sized from the overlap and spread a share of the reach wide gave back
nothing, two reasons. Behind a bowed seam, the pressed part's skin all lies far behind it, so the
falloff by depth behind it gave it no swell. And near the seam the skin faces the other part, so
pushed out from the line between them it only slides along itself. What worked: the whole part
grows across the press (the part pass's flatten lines, scaled by how near square to the pair's
line), by the lens two balls of its reach overlap by, its share, over its own volume, the free
skin top, bottom and front taking it. At twice that lens, Kar's presses keep their volume within
2% (both swung deep 0.0%, two hands +0.2%, both dragged -1.6%, one swung -1.2%), and at rest -1.0%
as built; one dragged by its root overshoots +6% (the follows read a deep overlap the cloth never
made), and a hand's dent, its own mechanism, still loses 2%. Filmed at 2, 4 and 8 times for Joe.
Joe chose keeping volume and asked for the exact version too ("full send it"). Gated, the lens
growth tore the fold under the part, where Kar's weights step from 0.65 to 0.23 in one edge: grown
across the press (both dragged, body over 3x 4 to 34, worst 3.2x to 5.0x), and grown forward from
the deepest skin, as the fold under a hanging part lies well in front of the chest wall (one
dragged, over 3x 4 to 54). Not committed.
**Adopted, then WITHDRAWN (below): the give-back baked from the part's own skin.** `YapsSquash.Fill` runs the shader's seam
(the stop, the rounding, the swell; flat, and bowed round the partner's front) on the part's skin at
rest at 32 depths and sums the mesh volume it moves, over what the part gains grown by 1.
`YapsSquash.Base` fits the plane the part meets the body in (its skin a quarter to three quarters
on it), and the part grows out of that plane, square to the pair's line, so the ring where its
weights step off stays put and the press's front never moves; both ride under the surfaces in the
surface texture. The shader reads the table at the seam's own depth (after the swing-away and the
bulk), on the part flattened that far along the line, as bowed as the lead. Tears back at the
committed seam's (one dragged over 3x 4 to 4, worst 3.59x to 3.61x; both dragged 4 to 6, 3.20x to
3.34x; more edges over 2x, the part growing). Known answer `SquashFillTest`: on a ball, the growth's
volume does not move with its plane, and the tables match a separate numpy count within 0.03%.
Read back per part (`-spSeamFilm`'s `fill` line), the bake alone gave back 40%: the follows put the
seam 2 to 3 cm in where real skin lay 4 to 8 cm past it, carried ahead of the follow's bone by the
cloth. `YAPS_SQUASH_REFILL` 2.3 gives back that much more; Kar's six presses: at rest -0.1%, one
dragged +2.2% (was +6.2%), both dragged -1.6%, one swung +0.5%, both deep -1.9%, a hand +1.5%, two
hands +0.2%. One-sided presses want less (1.3 to 1.4), two-sided more (2.2 to 2.9). Gate: five
scenes, compile bisect, shader filter off and at 128 clean; vertex compile level with the
committed shader. Next: Joe in game; a follow on the chain's tip, which the skin's front rides,
could make the depth itself right and let the 2.3 go.
Probe notes: the client's filter DLL crashes the editor (SIGSEGV in `DoFilterShader`) when a freshly
generated shader is imported with it hooked, so run each case unfiltered first and the filtered run
reuses the import; `YapsCompileBisect -ycbDump` writes the vertex programs as DXBC for the
out-of-Unity `filt.py`, where both versions pass. A rebuild piles numbered material copies, so read
the newest, not `Kar 0`. Next: Joe in game.
**WITHDRAWN 2026-10-26: real tissue does not grow, it moves.** Joe, on the film: no visible
difference, the squeezed volume still goes "into the fold void, not bubble out the top", the
clothing should decide where it goes, and "remove the marshmallow effect"; build a real soft body
outside CVR's limits and film it side by side. `Dev/Probes/SoftBodyReference.cs`, filmed with
`SquashPlayProbe -spSeamFilm <file> -spReference`: XPBD on tetrahedra, the chest voxelised from the
skin at rest (1.35 cm cubes on Kar, about 35k particles, 109k tetrahedra), every tetrahedron held to
its volume (the tissue keeps it within 0.3 to 1.4% under the hands), held still behind each part's
`Base` plane and wherever the skin carries no part, the parts sliding on each other and landing on
the ribs and belly, the hands the shader's own capsules, and under clothing the tissue unable to go
out past the cloth along the line from its part's centre, so free only where the cloth is open.
Two traps on the way: Kar's body is two whole shells, the skin and an inside-out copy just under it
(462 and 445 litres), so the inside count read a hollow skin until pieces facing in were dropped;
and the cloth turned off lets its bones back to their own pose, so the reference is built after 30
frames with it off. What it shows, Joe: "the actual soft body simulation is completely different
to how we had it, that's what I expected". Squeezed, the whole part moves: the pair slides
together, closes the cleavage and pushes forward, the shape changing everywhere, where the shader
dents under the hand, flattens the seam and leaves the rest still; the volume moves to where body
and clothing leave room, it is never added. The growth came off dev on Joe's word. **Next, Joe's
go 2026-10-26: bake the soft body into the shader.** At conversion, run this sim on the avatar for a
few unit presses per part (from outside, from the partner, from the front, from below, from
above, the pair squeezed together) and store each one's move per skin vertex in a texture; in game
the shader projects each hand's and the partner's press onto those directions and sums the stored
moves (soft tissue is near linear for moderate presses), the dent staying for the hand's own
imprint. Joe: "we should feel GPU wise how heavy this is gonna be, we should not obliterate
someone's system": the sim is bake-time only; in game about six texture reads per skin vertex on a
part, the rest skipping it, measured against today's squash with the probe's cost mode before it
ships, replacing the seam's swell rather than adding to it.

**Measured 2026-10-26: the presses add up, and the bake takes seconds.** The probe's `-spBasis`
bakes eleven presses (outside, partner side, front, above, below for each part, and the pair
squeezed from both outsides) to a quarter and a half of the reach. The hands are then summed onto
them as a shader would: each capsule's depth along the line from the part's centre, shared over
the ways by cos², the deepest capsule each way (a palm and its fingers press one dent, summed
they counted it twice), and a squeeze where both outsides are pressed. Against the solved tissue,
skin rms:
- One hand: 1.18 cm of 11.5.
- Halfway squeeze: 2.11 cm of 13.1, mostly a high palm's share read as a press from above, with no
  partner contact in it.
- Deep squeeze: 4.94 cm of 17.8. The palm ends 2.3 cm from the part's centre, where one frame cannot
  say which way it came in.

That deep case is the no-collider one. Other players' hands push the MC2 bones when the toucher's
hand carries a MagicaCloth collider (Joe), so with one the hand sits shallow against the moved part
and the answers compose with the bones.

Deep presses are not reproducible anyway: the same solver in another constraint order lands 4 cm
rms apart at 0.8 of the reach (0.3 to 0.9 at a quarter and a half). So bake to half a reach and
limit beyond.

The bake went from 45 minutes on one core to **14.0 s**:
- 2.7 cm cubes (an eighth of the reach), 6 substeps, 15 frames in and 30 held.
- Each constraint kind coloured into batches that share no particle, run on every core.
- Contacts found once a frame.

Off the slow reference that costs 0.6 to 0.8 cm rms at a quarter and 0.8 to 1.8 at a half; four
substeps broke it (2.3 to 3.1). Users would run it at conversion, so it has to stay seconds.

**Refined 2026-10-26, Joe on the film: a crease where the pair met.** It came from the coarse
cubes, not the summing; the 1/12 lattice showed none. Four fixes:
- The lattice was rounded up from one side, so the split between the parts sat up to a cube off
  the middle and one part owned a column of its partner. It is centred now.
- Skin off its cube moves as the cube's nearest point does, instead of carried past its corners.
- The summed skin is smoothed, 10 passes over its neighbours. The steps were the skin following
  2.7 cm cubes beside the held breastbone. Averaging is linear, so the bake can smooth each answer.
- The seam and the capsule push-out run per skin vertex after the sum, as the shader's own stages
  do. Pressed flat in the tissue, the seam showed the cubes as a staircase.

The bake is now 11.5 s (the skin no longer redrawn every frame of it).

**How the hands are read, tuned offline on 11 presses.** The probe gained eight more shots: from
the front, below, above, out and front, out and high, one deep with the other light, a light
squeeze, and both lifted from below. `-spDump <dir>` writes the answers, the skin and each shot.
A Python copy of the summing reproduces the probe's numbers to the hundredth, a variant takes a
second, and a fitted best mix of the stored answers gives the floor each reading can reach.

The floor (0.3 to 1.5 cm rms, 2.4 for the deep squeeze) is about half of what the reading got, so
the answers are fine and the reading was not. The reading kept:
- Each capsule is checked for touching by its true distance.
- It is then read square to its own axis, as a palm presses along its normal. Measured to its
  nearest end, the forearm above a part read as a press from above.
- Each way's depth is taken on that way's own line through the centre, as its answer was baked.
- cos² shares over the five ways, the deepest capsule per way, and a squeeze where both outsides
  are pressed.

Against the solved tissue, cm rms of the most moved (best fit in brackets):
- one hand 0.99 of 11.6 (0.71); light squeeze 0.46 of 4.1 (0.30); halfway 1.86 of 12.8 (1.19);
- front 1.29 of 6.7 (0.71); below 0.81 of 7.6 (0.50); above 0.54 of 5.1 (0.46);
- out and front 1.52 of 8.7 (1.11); high 1.11 of 10.1 (0.71); deep and light 0.78 of 10.5 (0.65);
- both lifted 1.77 of 8.1 (1.51); deep squeeze 5.60 of 18.1 (2.44).

Tried and dropped:
- Squaring to the palm without the touch check: a palm under one part lifted its partner.
- Squaring only along the palm's length: brought the forearm back.
- Each way read on its own line with a falloff off it: an oblique palm counted on two ways (5 to 6
  cm).
- Dropping the squeeze answer.
- Pressing deeper by a factor: fitted to three poses; held back as overfitting.

**The bake in the build, uncommitted on dev 2026-10-27 [M].** `YapsSoftBody.cs` solves each pair
when the squash is built (about 11 s a pair on Kar) and the shader adds up its answers
(`YapsSoftMove`); a baked pair skips the old hand press. First in-game reading against the solved
tissue was 5 to 12 cm off from the sides. Three causes, each instrumented before it was fixed:
- **The tetrahedra leaned one way.** All six of a Kuhn cube share one long diagonal, so the
  tissue gave more one way than its mirror, and the answers changed with which way was up (the
  build's up points down on Kar). Five to a cube, alternating cube to cube, is its own mirror:
  up and down now agree (cos 0.94 to 1.00).
- **Shares as the shader reads them.** The bake read raw shares, which sum past one in the
  cleavage; the ring each part meets the body in came out tilted, and a deep press from the
  front answered the other way. It now reads them as the shader does: a byte each, scaled back to
  one in all.
- **Shape keys.** The skin was hand-skinned from the bare mesh, so a body holding a shape key
  (Kar's "Bra ON", up to 3.8 cm) baked tissue for a chest that was not drawn. `Shaped` adds each
  key at its weight first, as Unity does; this changes every squash measurement, not only the
  bake's.
- Withdrawn, not a cause: the probe's own reference merged both pairs' shares, so the other
  pair's ring tilted the truth; it reads pair 0 alone now.

After the three, the shader against the solved tissue, cm rms (skinning alone in brackets):
outside 0.85 (2.59), squeeze 0.91 (3.58), halfway 0.45 (1.78), front 0.66 (1.77), below 0.56
(2.32), above 0.39 (1.57), out and front 1.77 (2.82), held high 1.00 (2.57), deep and light 0.65
(2.27), light squeeze 0.34 (0.88), both lifted 1.86 (3.83). The two combined presses are where
summing single presses runs out.

**Answers by place, TRIED 2026-10-27 and NOT ADOPTED.** Joe: "try 1", then, on the numbers: "go
back to per vertex, we need to save as much memory as we can and keep this thing thin". In place
of the per-vertex table, each press's moves were kept on the tissue's cube corners (a box per
part, anchored at its centre) and read where the skin is drawn, so a shape key moved in game or a
layer over the skin would read the tissue where it is. Against the solved tissue it tied the
table to a few hundredths of a centimetre on all eleven presses (worst vertex better on three,
worse on two), at 20 MB of RGBAHalf against 2.6 MB, since the skin that moves fills most of the
box (a tighter crop measured offline still left 5 MB a pair). It could not have fixed a shape key
moved in game either way: a hand reads as touching from the surface measured at the build, so a
grown part takes a hand in before it presses. Making that surface follow the key is the lever,
if it is ever wanted. The code is kept outside the repository for the whole-skin idea (Squash
anywhere, point 5).

**Thinner, 2026-10-27 [M].** Joe: "save as much memory as we can". The table went from floats to
bytes: each answer's move about 127, scaled by a fourth byte's share of the part's reach, and its
normal's turn beside it, two texels where there was one. Offline on Kar's answers the moves land
under 0.4 mm from the floats, at a quarter of the size; Kar's table goes from about 2.8 MB to 1.4
MB. The per-vertex rows went from two floats to two 16-bit numbers (the row, then the second
pair's top bit over the share), lossless, half the size; a body with more than 65535 moving
vertices builds no soft body and says so.

**Cost, `-spCost` in the editor [M].** Measured in turns, three each, against the same build with
the soft body off (the old hand dent). First it was 46% heavier in a room of ten copies: a vertex
was cut only past twice the pair's span, so copies sharing a material, a metre off, read this
avatar's hands and pressed with them. Cut now as the parts' own test is, past four reaches of
both parts. After it, 64 players' hands on one part: 5.15 ms against 8.60 alone, 7.40 against
11.85 among ten; nobody near, 1.53 against 1.85 and 3.26 against 3.62. Lighter everywhere.

**Two knobs gone.** *Bend give* and *Deepest dent* shaped only the old hand dent, which a pair
built with its soft body skips. Joe: "remove the bend give and deepest dent". A pair no soft body
was built for keeps their old defaults (`YapsSquash.BendGive`, `DeepestDent`).

**Out of the hands.** The summed skin went up to 5 cm into a palm, where the solved tissue stays a
few millimetres off it. `YapsSoftMove` keeps the two capsules in deepest while it reads the hands
(not the own arms, which have their own push), and after the move eases the skin out of them to a
tenth of a radius off, over a fifth either side, the normal turning to the capsule's. Measured
[M]: no skin left in a palm (one vertex 1 mm in, once), and closer to the solved tissue on every
press: out and front 1.77 to 1.44 cm rms, the worst vertex 2.8 to 2.1 outside, 2.9 to 2.2 in the
squeeze, 3.0 to 2.1 from above, 4.9 to 4.3 both lifted.

**No jump at the first touch.** The side-by-side film showed the whole pair jump in the one frame a
hand first touched it on a slant (out and front, both lifted from below): each way read the hand
against its own flat plane, which a round hand crosses before it reaches the skin, so a way leaning
off the hand started at a sizeable press. Each way's press is now capped by the true contact over
its lean (`inside / c`), so it starts at nothing; a hand straight along a way reads as before.
Measured [M] (frame-to-frame change, the shader's column against the solved one): the six contact
jumps of 2.6 to 3.0 went to 0.2 to 0.4, the solved tissue's own 0.3; closer on the slanted presses,
out and front 1.44 to 1.31 cm rms, held high 0.99 to 0.95, both lifted 1.81 to 1.40 (worst 4.3 to
3.3); the straight presses unchanged. A one-frame blip of about 1.0 at each release is in the old
film too and moves the hair as much as the pair, so it is not the squash.

**Seen in the film, open.** Clothing over the pair: under an outside press the lower outer rim of
the clothing pokes out past the hand (before the push-out as well); from outside and in front the
hand shows through it; creases near the hand from below and from above; a ragged edge with both
lifted. Likely the clothing standing more than 3 cm off the skin, which fades off the tissue and
so neither follows it nor gets eased out of the hand in full. Not yet measured.

**Slanted presses baked.** Joe, 2026-10-27: "bake two slanted options". The slanted presses were
off the most because the tissue does not add up: a hand in front boxes the flesh in, so one under
it lifts it further than the two straight answers summed (the probe's own sum missed by as much as
the shader, so the reading was not at fault). Each part is now also pressed from outside in front
and from in front below, 15 ways instead of 11; below is toward the feet at the build, carried per
pair in `_YAPS_SoftBelow` as the first part's across runs either way. A hand reads a slanted way
alone when it lies on it: each slanted way takes from the two it lies between what the lesser
gives, the front's share split between its two. Measured [M]: out and front 1.31 to 0.66 cm rms
(worst 4.7 to 2.2), both lifted 1.40 to 0.92 (worst 3.3 to 2.6), held high 0.95 to 1.06, the
straight presses unchanged, no new jumps, no skin in a palm. The table 1.40 to 1.91 MB for Kar's
pair (+36%), the bake 15 presses in 14.6 s.

**The clothing, measured [M].** 572 of the bra's 4928 vertices stand 3 cm or more off the skin
(324 between 3 and 4 cm, easing off; 248 past 4 cm, which take none of the move). Joe circled the
result: lobes of the bra standing out past the hands in the squeeze while the skin under them is
pressed in.

**The clothing rides further out.** Joe: "try 1 and then 2 if 1 fails" (1: ride the skin's move
further out; 2: the bake answering for points off the skin). A layer takes the move of the skin
nearest it in full to 5 cm off and eases off to 6, where it was 3 to 4 as the shares are; the
grid's cells grew to 3 cm so the two either side reach 6. The bra's farthest vertex stands 4.5 cm
off, so all of it now rides. Seen [M]: the lobes standing out past the hands in the squeeze and
the outside press are gone; the skin's numbers unchanged. No memory. Option 2 not needed.

**The meet line at rest, gone.** Joe, 2026-10-27: "why does ours in the video still have the meet
line?" Not the soft body's: the part pass presses whatever the two overlap against the seam, each
side stopping 3% of the reach short, and the pair's mesh overlapped about 2 cm at rest, so it met
in a straight slot with nobody near. Of the two ways put to Joe (A: keep the overlap the mesh has
at rest; B: bake the two pressing each other into the soft body and retire the part pass) he took
A, "B if it looks wrong", judged by his eyes. The shader reads the rest overlap from what it
carries (each part's reach toward the other less the pair's start), takes it off the overlap, and
eases the contact in over it as over the last of a gap; a pair apart at rest reads as before. Kar
[M]: at rest 0.25 to 0.16 cm rms against the solved tissue (worst 2.1 to 0.8), a light squeeze
0.31 to 0.23, from below 0.55 to 0.48, the squeeze 0.86 to 0.89; no new jumps. Joe: "looks right
with A, i like it".

**Jagged under a hand, part way.** Joe: the control's skin under a hand is smooth, ours "slightly
jagged". It is the clothing: the bare skin's edge along the hand is smooth. Not the row each layer
vertex rides [M]: the bra's move is no rougher across it (0.07 cm rms against its neighbours) than
the skin's own (0.09). The push-out put everything inside a palm on one shell, so a layer standing
off the skin, deep in the palm, folded there; now what is deeper keeps a tenth of its order. [M]
outside and in front 42 to 16 flipped bra triangles, the edge along the hand smoother in the film;
the squeeze still 35, so most of the folding is something else. Open: where those edges are.

**Harder presses in the film** (Joe: "squish them harder"): squeezed hard together (0.55 of the
reach), one shoved hard into the other (0.6), a hand deep from the front (0.5), both pushed up hard
from below (0.45), both pressed hard from the front (0.45), one grabbed above and below (0.3).
[M] against the solved tissue: 0.97, 0.98, 1.68 (worst 7.1), 1.15 (worst 5.8), 1.36 (worst 6.3),
0.54 cm rms. The deep front presses fail: the hand goes through the bra, which tears round it,
and both pushed up lift less than the tissue does.

**One side pressed, the other mirrored** (Joe: "mirror and bake"). A pair whose skin has a twin
across the avatar's middle (95% of it within 1 cm) is pressed on its first side only: 8 presses
where there were 15, 7.8 s against 14.6 for Kar's breasts. The second side's skin within 3 mm of
its twin reads the twin's row with a flip bit, so the table holds 3461 rows where it held 6076
(2.18 MB on disk against 3.83). [M] presses on one part exactly as before; those loading both lost
0.03 to 0.2 cm and folded the bra more (35 to 52 flipped in the squeeze). Not twinless skin (8 of
6927 vertices) and not the pair's toward off the middle (0.3 degrees): the squeeze, pressed on both
sides at once, came out 0.93 cm lopsided at its deepest, so skin reading its twin's row disagreed
with its neighbour reading its own. Each now takes the mean of its own and its twin's mirrored: the
squeeze 0.83 to 0.69 cm rms and 56 to 37 flipped, squeezed hard 0.99 to 0.82 and 63 to 52.

**The palm grips.** The front press baked as chance: with the palm frictionless, a 1 mm nudge either
side moved the second part's deep front answer by 6.67 cm rms (15.5 worst) and the out press's by
1.13, flesh pressed square on its crown sliding off to whichever side it leaned. Skin under a palm
now moves with it across its face, in the bake and the control both; the same nudge moves them 0.49
and 0.14. [M] a hand deep from the front 1.68 to 0.45 cm rms (worst 7.1 to 1.7), both pressed hard
from the front 1.36 to 0.68 (6.3 to 2.8), shoved hard 0.98 to 0.81, a hand from outside 0.83 to
0.70. The hand no longer breaks through the bra on the deep front press. Joe: "yep that looks good".

Next:
1. B (Joe: "Go"): the two swung into each other baked, the part pass retired for a soft pair, the
   control taught to swing so it can be measured.
2. Where the bra's remaining folds are: 37 flipped in the squeeze, 38 outside and in front, 66 and
   103 on the deep front presses.
3. Both pushed up hard still lifts less than the tissue (1.01 cm rms, worst 5.1).
4. Nothing past 1.6 of the deeper depth is read, a cap set while deep presses baked as chance;
   with the palm gripping it may go further.

**B, where it stands (2026-10-27, uncommitted on dev).** The swing bake follows the bones: the
tissue a part's bones carry is now spread from the skin riding it (by the skin near a particle it
turned the tissue under the skin less than the skin), and on identical inputs the bake's tissue
gives what the control's does, partner 1.7/7.7 against 1.8/7.8 cm rms/most. In the film that is a
wash: one swung deep 1.63 to 1.90 cm rms, both deep 1.18 to 1.22, one light 0.74 to 0.89, both
light 0.86 to 0.80; the presses unchanged. Skin-on-skin friction was tried and dropped: it made the
two directions of a swing agree, and every hand press worse (one from outside 0.71 to 1.30), stick
and slip being chance again. Withdrawn: "the bake slides where the control pushes" was the carry,
not friction. Found on the hard presses (slides 19 and 20): a step at the top of the cleavage on
the inner edge of one part, in BEFORE and AFTER both, first seen between soft30 and soft42, when
skin began riding its own part's tissue by bone weight. Joe could not see B's difference with the
bra on; he wants a film without it.

**Rethought from the start, Joe 2026-10-27: "think outside the box, radically ... we are just
hitting body block after body block".** Every block so far has been a seam between canned answers
that a shader with no memory adds up: superposition, the way read off the centres, the mirror and
its evening, knife-edged bakes, embedding seams. Proposed: run the tissue in game, a lattice in a
render texture stepped by blitters, which deletes the table and every seam and brings dynamics.
Fallback if the client cannot carry it: a fitted map from contacts to a few baked shapes.

**The jelly spike, 2026-10-27 (Joe: "try it").** `Dev/Probes/JellySpikeBuilder.cs`: 3072 particles
(one pair's tissue) in a 128x48 float texture, two blitter passes a frame under a render
controller, each particle pulled toward its 3x3x3 neighbourhood's rigid fit (shape matching,
rotation by Mueller 2016) and the whole toward its rest on an anchor as a 3 Hz spring, every
player's hands pushing it as the squash reads them. A block 20 cm across in front of the chest.
- Cost, measured by the client's own route (temporary, blit, copy back), RTX 4080 [M]: 0.058 ms a
  pass with no players, 0.125 with 64 (the hands loop); a bare copy 0.018. 12288 particles cost
  0.062 and 0.145: the lattice's size is nearly free, the passes and the hands are what cost.
  About 0.12 to 0.25 ms an avatar a frame at two passes.
- Behaviour [M]: settles to 2.7 cm of sag (g over the spring's rate squared, as it should), a 60
  degree turn in a sixth of a second swings the front 9 cm past and back, still within half a
  second. The shape matching alone folded the block 42 cm under its own weight; the spring is what
  holds it.
- Editor Play mode with the CCK's own provider and render controller [M]: PASS, the back face exact
  on the anchor the provider handed.
- **Works in game, 2026-10-27 [GAME]** (Joe, on Kar): pink and wobbling, and both Joe and a
  remote user push it with their default hands, so the step runs every frame on the avatar, keeps
  its state, and reads every player's hands from the client's globals.
- **And the rest, same session [GAME]:** frame time "practically nothing"; the remote user saw it
  pink and wobbling on their own screen; switching avatar and back starts it again; both VR eyes
  alike. CVR's Shader Performance Viewer (instructions, texture fetches, surface instructions)
  draws the jelly's surface darkest on screen, cheaper than the body's own shader.
- Still open: the Cameras content filter off should show it red (viewers so set get no blitters,
  so a live squash needs a fallback for them).
- **Attached things, the next proof (built 2026-10-27, Joe to try):** a plain yellow cube on the
  front, its shader knowing nothing of the tissue, moved by a `CVRTexturePropertyParser` reading a
  2x1 texture one more blitter writes (the front centre's place and turn in the anchor's space,
  each between its task's bounds). Editor [M]: the texels decode to the front centre exactly, 20.8
  cm out. If it rides in game, anything on a bone can follow the tissue, a frame or two late.
- **The parser reads 8 bits a channel [M, client code]:** its manager downloads each texture
  async, once a frame, as RGBA32, whatever the texture's own format. So a value has 256 steps
  across its task's bounds: the cube's ±0.5 m and ±180° are ~4 mm and ~1.4° steps, which the
  editor check (full float) cannot see. For 5.1: tight bounds around rest (±5 cm, ±30° gives 0.4
  mm, 0.24°). The blitter itself never reads back: temp, blit, copy, all on the GPU.
- **A cost meter (2026-10-27, Joe to try):** a billboard over the jelly, frame time in ms, and a
  load of 32 more step passes on an undrawn lattice switched on and off every 5 s (every 0.5 s from the same evening, each side a running mean); the
  difference over 32 is one pass's own work on that machine. Editor [M]: the clock reads 11.10
  ms at a locked 90 fps.
- **In game, same day [GAME]:** Joe's own frame 4.3 ms, yellow 0.001 (CPU-bound, the work hid in
  GPU slack); remote viewers 32 ms / 0.079 and 18 ms / 0.062 a pass, jumping 0.001 to 0.1 (two
  5 s means differenced over 32 is noisy at 30 to 55 fps). The client never culls blitters
  (`CVRRenderController` runs them every frame while active, no distance or visibility check), so
  every viewer pays for every jelly; CVR's own Avatar Distance Disabling is the far-off answer.
- **Made cheaper, 2026-10-27 [M]:** the right half holds where each particle was, not its
  velocity, so half the texture does next to no work; the passes go into a twin texture and back
  instead of the client's temporary and copy; a near pass picks out up to eight hands in reach
  once a frame, unrolled, so the step reads eight however many players and Shader Filtering
  leaves it whole; the fit reads each neighbour once and takes 3 rotation steps, not 6; and above
  72 fps one pass steps, `_Stiff` now over a 180th of a second so one pass or two feel alike.
  Low end measured on the CPU's own Radeon (2 CUs, `-force-device-index 1`), a frame at 90 fps:
  0.246 to 0.107 ms empty, 0.646 to 0.092 with 64 players, 2.19 to 0.239 for 12288 particles
  with 64. The turn trace matches the old within about a centimetre, a touch more damped. The
  4080's figures need a rerun with the game closed (0.029 and 0.094 for the same code while it
  ran). Verify passes with the CCK's components.
- **The cheaper build in game, same evening [GAME]:** the meter's yellow (one pass's own work),
  four viewers on discrete GPUs: 0.000, 0.000, 0.001 to 0.002, 0.016 to 0.020 ms, against 0.062
  and 0.079 before. One viewer on a Ryzen 7840U's integrated graphics, no GPU: 0.116 ms a pass,
  so about 0.25 ms a frame below 72 fps where both passes step.
- **The jelly alone, as a prop [GAME]:** a viewer who lost about 20 fps around the avatar saw
  barely any cost from the prop; the loss was another player's avatar, and the meter's own load
  (32 passes every other half second) had been most of the rest. The meter is now opt-in (its own
  menu item), and a menu item makes the prop. From 2026-10-28 the prop can be grabbed (Joe): a
  pickup and a box round the jelly on the spawnable itself; let go, it hangs where it was left.
- **A slap jiggles it [GAME], Joe 2026-10-27:** a hand pushes the lattice and it wobbles back, the
  jiggle planned for the squash. Every player's hands do it with nothing on their avatar, where
  MagicaCloth needs a collider on the other player's hand.
- **Cheaper still, done overnight 2026-10-27 [M], Joe to try in game:** Joe: "do all of them".
  - The bench first: every row timed five times, the rows interleaved, a third of a second's
    warm-up, medians with their range; chains of plain copies for what a pass costs before it
    works (0.0025 ms on the 4080, 0.0073 on the 2-CU Radeon); the parser's readback timed apart,
    since thousands in flight inside a row timed Unity draining its queue.
  - Three blitters, not four. The first pass also looks for the hands in reach, into a spare row
    after the lattice; the second writes the rider's two texels there; a third copies those into
    the 2x1 texture the parser reads. Two passes was the plan, but the parser reads back a whole
    texture, and the whole state cost it 0.095 ms a frame against 0.008 for two texels.
  - Above 72 fps the first pass works out where each particle is headed and the second fits, so
    each neighbour is read once; below it each pass steps. The fit's sweep rewritten: one damping
    factor a pass, neighbour offsets the whole pass shares, sums signed by offset in place of the
    outer product, Place in floats. The turn traces at 90 and 60 fps match the old build's at
    their printed precision (46 values, none differ).
  - The hand scan passes over a player on where they stand, eight players a branch, and only the
    first pass carries the player arrays (a shader keyword). Reading every hand cost the 4080
    0.024 ms a pass with 64 players far off, and the old near pass paid that even alone.
  - Far switch, at Joe's 6 m: past it from the viewer's head (the client's first head position,
    the local player's) another player's copy hangs at rest, rigid, back within 5.5 m. No head, as
    with no humanoid avatar, counts as near; the wearer's own copy never switches.
  - Below 72 fps the first pass's own step pushes against the hands as they were the frame before;
    the second, which is drawn, against this frame's.
  - Tried and left: 1 or 2 rotation steps save 8% or 4% on the 2-CU Radeon but move the fast turn
    by up to 8 mm or 3 mm, so three stay.
  - A frame in ms, 3072 particles, before and after:

    | | 4080 | 2-CU Radeon |
    | --- | --- | --- |
    | Alone, 90 fps | 0.044 to 0.011 | 0.098 to 0.051 |
    | Alone, 60 fps | 0.051 to 0.012 | 0.140 to 0.072 |
    | 64 players far off | 0.045 to 0.017 | 0.099 to 0.059 |
    | 64 players, 8 hands in reach | 0.044 to 0.021 | 0.098 to 0.064 |
    | The viewer 20 m off | 0.044 to 0.011 | 0.098 to 0.036 |
    | 12288 particles, 64 players | 0.046 to 0.018 | 0.221 to 0.160 |

    CPU to submit a frame: 0.012 to 0.010 ms. Verify passes with the CCK's render controller, the
    rider exact, built twice with nothing missing.
  - Still open: the AMD readings (6700 XT 0.16 to 0.30, 7840U 0.116) do not fit the 2-CU part's
    numbers, most likely a GPU clocked down in a CPU-bound frame, so not real cost. Check the GPU
    clock and GPU-busy in game before optimising for them.
  - Ruled out: a double-buffered CustomRenderTexture (the client cannot drive one); a brick
    layout, dropping the right half by MRT, and a coarser lattice cost more than they save.
- **A wear build, far copies off, one pass benched, 2026-10-28 [M], Joe: "do 1 and bench 2 and do
  3":**
  - A third menu item builds it to wear: no test cube, so no third pass, readback or parser.
  - Far off, both passes now leave the state be, and the surface draws the rest shape off the far
    switch; a state that has not stepped for a tenth of a second starts again at rest. In the test
    build the third pass also writes whether the steps should run, and the parser hands that to
    both step blitters' `enabled` (a bool task; the client binds any public property), so a far
    copy runs only the 3x1 pass, back on within 5.5 m. That the client binds an inherited
    `Behaviour.enabled` is [GAME]: if it does not, the copy steps as before, still at rest.
  - A frame in ms, 3072 particles at 90 fps:

    | | 4080 | 2-CU Radeon |
    | --- | --- | --- |
    | Worn, 64 players, 8 hands in reach | 0.021 | 0.061 |
    | With the test cube | 0.023 | 0.066 |
    | One pass stepping in place, the client's temporary and copy back | 0.027 | 0.055 |
    | The viewer 20 m off, the steps running at rest | 0.011 to 0.016 | 0.037 |
    | The same, switched off by the parser | 0.006 | 0.004 |
    | The parser's readback, on top | 0.017 | 0.010 |

  - One pass: a wash, 0.006 ms dearer on the 4080 and 0.006 cheaper on the 2-CU part, and below
    72 fps it halves the steps. Two stay.
  - The parser's switch-off left out of the wear build: its readback and pass cost every copy, near
    or far, every frame (0.019 ms on the 4080, 0.015 on the 2-CU part), and save a far copy 0.005
    to 0.010 or 0.033. It never pays on the 4080; on the 2-CU part it pays once about half the
    copies are far, as in a crowd. Joe's call: a menu item of its own, below.
  - Verify passes on the test build, the bench on both GPUs; the wear build is benched, not played.
- **Far copies cheaper, far copies off when worn, a prop that sleeps, 2026-10-28 [M], Joe: "do 1 to
  3":**
  - Jelly spike: add to wear, far copies switched off: the third pass and the parser kept for the
    far switch alone, no rider. The bench builds both wear builds and counts their parts.
  - The far path, timed pass by pass against a light shader holding only the far test, and two
    plain copies (ms a frame; the Radeon is the 2-CU part):

    | | 4080 | Radeon |
    | --- | --- | --- |
    | A far copy, two passes | 0.008 | 0.032 |
    | The far test alone, in a light shader | 0.006 | 0.019 |
    | Two plain copies | 0.005 | 0.016 |

    The rest is the shader's size: without the hand scan the Radeon's first pass drops from 0.012
    to 0.009, and the fit's code is the remainder. Shedding either needs a third pass or a frame's
    lag on the hands. A discard in the second pass, writing nothing, takes 0.002 off the Radeon.
    A discard anywhere in the first, even one it never reaches, cost the 4080 0.003 to 0.004 a
    near frame with players, so it is compiled into the second pass's variant alone.
  - The prop sleeps: still for half a second (the rider's texels a frame apart, the front centre
    under 2 mm/s and its turn under a degree a second, the anchor under 2 mm/s), no hand in reach,
    the anchor within 0.1 mm of where it last stepped. Then both passes leave it be, the second
    stamping it so it reads as stepping, and it draws blue. The second pass decides a frame ahead,
    so a move or a hand wakes it the frame after, from where it lay. In the bench it fell asleep
    1.93 s after settling, with the same sag as an awake twin (2.64 cm against 2.64), woke to a
    1 mm move with the back face on the anchor the frame after, slept again, and woke to a hand.
    Ms a frame, alone and with 64 players:

    | | 4080 | Radeon |
    | --- | --- | --- |
    | Asleep | 0.017, 0.026 | 0.043, 0.048 |
    | Awake, as a prop | 0.018, 0.027 | 0.057, 0.064 |
    | Worn, no sleep compiled in | 0.018, 0.027 | 0.056, 0.063 |

    A quarter saved on the Radeon, nothing on the 4080, where the fixed cost and the hand scan are
    most of a frame. Worn builds leave it out (a keyword): the avatar's idle sway would keep it
    awake [R], and compiled in, the test costs the Radeon 2 to 4% alone.
  - Tried first: the stillness test in the first pass, beside the hand scan, cost the 4080 0.004 a
    frame with players (the old shader beside the new in one run). Now in the second pass.
  - With ChilloutVR open on desktop the 4080 reads about half as slow again with wide ranges, so the
    numbers above compare inside one run; across runs they do not. Verify passes, both benches pass.
    Blue in game, and waking on a real hand, are [GAME].
- **Many jellies, 2026-10-28 [M], Joe: "the main problem is numbers of avatars with it ... I want
  it to look as good as possible while remain as optimised as possible":**
  - Across players nothing can be shared: the client instantiates every render texture and
    material per avatar copy (`SharedFilter.GetInstancedRenderTexture`, from `AssetFilter`), so
    each copy steps private textures. That is also why two players in the same avatar never fight
    over one state.
  - Within one avatar, regions pack (Joe: "do a, prototype it in the spike now"): every region is a
    block of rows in one state texture, its side row last, its own anchor from the provider's
    transform list, so one pair of passes, one scan's player arrays and one texture serve them
    all. Jelly spike: add three to wear, packed (chest, behind the hips, above the head) and make a
    prop of three. Three regions, ms a frame at 90 fps, packed against apart:

    | | 4080 | Radeon |
    | --- | --- | --- |
    | Alone | 0.023 / 0.038 | 0.141 / 0.177 |
    | 64 players | 0.041 / 0.085 | 0.150 / 0.225 |
    | 8 hands on the first | 0.047 / 0.103 | 0.158 / 0.232 |

    Each packed region steps exactly as its lone twin (0.000 mm apart after 200 frames), the
    hands kept by the first alone. One surface a region, on its anchor, so the far rest shape
    stays right; the draw finds its region's switches from a second uv. ChilloutVR was open.
  - Skipping copies behind the viewer, researched (Joe: "research it"): the client hands shaders
    no camera direction, only player positions; the render controller steps in
    `LateEventsManager.OnPostLateUpdate`, before any camera renders; and the material data
    provider's UAV slot is bound globally, so a draw cannot flag "seen" per copy (the last copy
    bound wins). Nor are Unity's own camera values left over: a batch probe rendered two cameras
    and then blitted a shader writing `_WorldSpaceCameraPos`, `unity_CameraToWorld` and the view
    matrix, and every one read the origin and identity, before and after either camera. A blit
    resets them, and `CVRBlitter` blits. **Closed: content cannot know where a camera looks;** it
    would take the client handing one in.
  - Coarse lattice, smooth surface (Joe: "do it"): the surface is now finer than the particles, each
    of its points blended from the eight round it (one load where it sits on a particle), so the
    lattice is a setting. Jelly spike: coarse lattice, ticked by default, builds 8x6x8 (384
    particles, 2.86 cm apart); unticked, the 16x12x16 one. Settled sag 2.6 / 2.6 / 2.5 cm (16, 12,
    8 across), and the 60 degree turn within 0.2 cm of the fine lattice at 12x9x12 and 0.6 cm at
    8x6x8, a little more damped; the pictures side by side show the same shape. Ms a frame at
    90 fps:

    | | 4080 | Radeon |
    | --- | --- | --- |
    | 3072 alone / 64 players, 8 hands | 0.019 / 0.037 | 0.066 / 0.076 |
    | 1296 alone / 64 players, 8 hands | 0.021 / 0.039 | 0.042 / 0.053 |
    | 384 alone / 64 players, 8 hands | 0.020 / 0.035 | 0.032 / 0.043 |

    The 4080 does not see the particle count, only the passes; the Radeon pays half. The blend's
    own cost is lost in a camera's (0.12 to 0.34 ms to render the surface, the same within noise
    on every lattice). Verify passes on the coarse default. ChilloutVR was open. In game: dents
    under a finger are broader, [GAME].
- **Red and frozen in game, fixed 2026-10-28 [M]** (Joe, prop and avatar, screenshots): the
  surface red at its rest shape from the first frame while the yellow cube wobbled, so the
  tissue stepped and the surface never read it. The surface's `_Lattice` was set on its material
  but not declared by its shader, so the saved material (what is uploaded) never held it: in game
  the lattice read 0 and every lookup missed. Batch never saw it, the value still in memory from
  the same session. Now declared, and Verify reads the material file itself (it failed before the
  fix, passes after). Ruled out on the way: the client's texture parser and its far switch (its
  manager and tasks, emulated from the decompile on the real prefab, switch the steps off far and
  back on near), Joe's content filters, a client update, the load log. **Rebuilt, pink and
  wobbling in game the same morning [GAME]** (Joe, screenshot).
- **Never still, never asleep in game, fixed 2026-10-28 [M]** (Joe: "always very finely moving",
  blue "sometimes, but not for everyone"; the far switch works [GAME]). Where the tissue rests
  moves with the step (the shape pull grows with the step, gravity with its square), so stepping
  each frame by its own time shook it at every uneven frame. Measured through `unity_DeltaTime`,
  frames 40% uneven: 40 to 80 mm/s and never asleep, at 45 to 230 fps, any lattice, near the
  origin or 300 m out; at a steady frame time it settles to 0.000. Each viewer steps their own
  copy at their own pacing, hence blue for some. Now the first pass holds the step while the
  smoothed frame time stays within 15% of it (side-row texel 25): every case above asleep, the
  physics untouched (the turn traces at a fixed step match the last bench byte for byte). The
  bench checks it (40% uneven at 230 fps must sleep). Tried and left: both pulls as springs over
  the step squared, solved together, rests exactly at any step but nearly doubles the swing on a
  turn (the shape pull dilutes the chest spring).
- **A deep hand folded the coarse surface, 2026-10-28 [M]** (same report). The particles sit a
  cell apart, so a hand in deep dragged whole cells and the triangles between them folded. Now the
  surface itself is pushed out of every hand in reach too, by the same wrist-to-tip capsules the
  step reads: the dent follows the fingers and stays round (batch poke, before and after). The
  prop's box collider is now a trigger (Joe: still takes the grab, never pushes a player).
  Joe to check both in game.
- **The breasts on the jelly, started 2026-10-28 [M]** (Joe: "start the implementation of the
  titties for it"). Jelly spike: put the selected avatar's breasts on the jelly, on an avatar with
  the squash built:
  - One lattice over the pair, so one pushed into the other pushes it too, as the flesh between
    does. Fitted to the skin the squash gives the pair at least half of, with a centimetre of room
    and its back face half a centimetre into the chest wall, about 300 cells, standing on the chest.
  - The body's squash materials gain a new YAPS role, Jelly (`yaps_jelly.cginc`, dormant unless asked
    for by name). Each vertex moves as the eight particles round it have moved off their rest, by
    the squash's own share of the pair, so every layer at a point takes the same move. Its normal is
    turned by the move's gradient, and it is pushed out of the hands in reach, out to twice the
    radius and never past it, so layers keep their order. The squash leaves the pair to it
    (`_YAPS_JellyParts` masks their shares and their soft rows).
  - The step writes the anchor it stepped on into side-row texels 26 to 29, so a body's shader finds
    the lattice from the state alone, which the client points at each copy's own.
  - `_Lift` holds the rest above by the sag gravity gives (g (1 - (wh)^2) / w^2), so upright the
    tissue keeps the modelled shape.
  - The cloth or dynamic bones whose roots are on the pair's chains are switched off. A second menu
    item takes it all off again.
- **On Kar, batch Play mode with the CCK's render controller, the squash built on a copy [M]:**
  - A lattice of 12x7x7 (588 particles, 6.9 cm apart, 76 x 41 x 41 cm); 7 of 7 materials patched; the
    breast and piercing cloths switched off.
  - Settled 0.8 mm off the modelled shape (2.6 cm without the lift).
  - A finger 3 cm in moved the tissue 33 mm and 1.75% of the picture's pixels, against 0.05% frame to
    frame; a 50 degree turn swung it 9.3 cm.
  - The jelly as before is unchanged: Verify gives the same numbers, and the bench's turn traces match
    the last bench line for line.
- **Found on the way:** in the editor every jelly in a scene shares the step's materials (the client
  makes each copy its own, the editor does not), so a jelly prop in the same scene handed its anchor
  to the avatar's lattice. The check switches other jellies off first.
- **Open:** Kar's conversion has no squash now, so it needs reconverting with the squash ticked before
  the menu item [GAME]. In game:
  - the look at rest, the jiggle, the dents, the pair pushing each other;
  - whether the piercings, whose cloth went off with the pair's, still sit on the skin.
  Lying down, the lift, along the chest's up, holds the pair about 2.6 cm toward the head [R].
  Viewers with no blitters (the Cameras content filter) and far copies see the rest shape, with no
  squash either.
- **Kar reconverted with the squash, 2026-10-28:** with Kar's own settings (the window's saved ones
  came from a later conversion, so the first try took five settings it never had; moved aside).
  The report's "since the last conversion" names only the squash: 2 pairs, 7 materials.
- **Measured against the cloth and the squash, 2026-10-28.** The squash probe's film, jelly over
  squash on the same scripted presses, and per segment the picture's motion and how far the two
  differ:
  - The realistic-to-anime slider never reached the jelly (hops moved 2.08, 2.08, 2.19 against the
    squash's 1.79, 1.81, 2.38).
  - Nothing moved the breast as one piece: a hand from below lifted the squash's whole breast and
    only dented the jelly's; a hand between them spread the squash's apart.
  - Its dents were smooth pushes, no rim and no bulge, moving 0.76 to 0.86 of the squash's.
  - They never flattened on each other.

  The cause was the build: the squash was masked off the pair, and one lattice spanned both breasts
  and the air between. Against the breast cloth (`TuneBreasts`: a cloth copy and a jelly copy turned,
  hopped, walked and laid back alike), the cloth swings at about 1.25 Hz with a damping ratio near a
  quarter. The lattice's per-particle springs could not follow at any setting: soft enough to swing
  with it, they lagged 79 to 87 cm lying down against the cloth's 35, as the cloth stiffens with its
  angle limits. Walking, the cloth's chains, pointing forward and pushed back along their length,
  buckle 20 cm sideways; tissue should not copy that.
- **One system, the jelly, building 2026-10-28 [M]** (Joe: "the jelly system all the way, the most
  optimised path forward"; layering the squash over the jelly rejected as two systems a vertex):
  - A region a breast, packed into one texture and one pair of passes. Each a mass, side-row texels
    30 and 31, stepped once a frame in the first pass: verlet under gravity, a spring toward its rest
    with the sag lifted off, pushed by the hands in reach by `_Give` of their reach into it (the
    particles dent the rest), kept off its partner, its swing within `_Limit`, in and out within a
    tenth. Each particle's home rides the swing, more toward the front, so the back face stays on
    the chest wall. Stretch in motion is the squash's own rule from `_YAPS_SquashMotion`, so the
    conversion's slider drives it.
  - The weight from the breast's own volume (Joe: "base it on actual mass"): from the plane of its
    base ring out to its frontmost skin, so shells and inner faces do not count (a signed sum gave
    Kar's 1 litre, the chest bone's plane 35). Kar: 9.8 and 10.0 litres, 9.4 kg each, so by real
    tissue (about 2 Hz at half a kilogram, as one over the size) it swings at 0.75 Hz. Joe chose
    real physics over a gentler fantasy curve, with the inspector to tune it.
  - Skin past a region's sides takes the edge's move by its share, as easing it out tore skin off a
    swinging breast.
  - Hands on the film, 2026-10-28 (Joe: "moving whole titty by a slight hit, not deforming it"):
    the mass was pushed a share of the way out of each hand every frame, which within a few frames
    is all the way, so the whole breast left the hand and nothing dented. Now the hands move where
    the mass's spring holds it, a force. And the per-vertex hand push, lost when the shader went to a
    region a part, is back per region: the particles sit 4.6 cm apart, so a hand between them sank
    through the skin. On the film the skin now cups round a fist.
  - Squeezing together (Joe: "its not going as deep? ... fix the squeezing together"). Measured with
    the film's own squeeze in the verify, a hand each side 13 cm in: the outer side moved 35 mm,
    as particles pushed out of a hand's nearest side went round it and the hand passed between
    them. Now what a hand reaches goes on ahead of it toward the region's centre, in the lattice and
    the skin alike: 138 mm. Give from the tissue under a palm (about 2 kPa, 270 N/m) against the
    spring the breast hangs on (m w^2, 210 N/m for Kar): 0.56 of a held press moves the whole,
    the mass shifting the spring's target, so a quick hit barely does. `_Squish` 0.9, as flesh
    barely changes volume but gives easily in shear. The masses close 32 mm, the inner sides give
    45 mm, each mass bulges 9 mm forward. Open: the lattice loses 5% of its volume under the hard
    squeeze.
  - Weight, 2026-10-28 (Joe: "not holding weight or heft ... one hit and the whole wobbles madly",
    then "I want it like the softbody", then "get magicacloth for now to move the breast bones"):
    - The flesh is viscous: what a particle does apart from its home's own move is damped by
      `_Viscous` (25, about 0.7 of critical at 3 Hz), the home's move kept, so dents fill back
      without the 3 Hz jelly ring and nothing lags the swing.
    - Hands take the gesture the squash reads from each hand's w: a fist ends at the knuckles, a
      finger out is thin. A pointing finger had been a whole palm shoving the breast.
    - For now the pair's own MagicaCloth swings the breast bones and each region rides the bone
      carrying most of its breast's skin; the jelly's mass is off (`-jellyMass` puts it back, the
      cloth off). Hands dent, the pair squash by volume. The pair's overlap at rest is now fixed at
      the build (`_PairOffset`): taken from where the anchors are, two breasts swung together by
      their cloth read as resting and passed through.
    - With the mass (no cloth): give 0.1 and the whole damped at 0.6 of critical; a 5 cm slap moved
      it 1 mm. The pair's bodies hold the facing sides 17 mm further apart than without, measured
      by the film's squeeze twice, bodies off and on (an anchor pressed toward the other had shown
      only its mass overshooting).
    - Solid, not a balloon (Joe: "why do we have Pressure? they are meant to be solid, like actual
      breasts"). With the cloth carrying the bones, a squeeze moved the facing side 0.8 mm and lost
      8% of the volume, the homes' springs soaking up the push. A pressure (homes swollen evenly
      while volume was short) gave 16 mm and 0.99, but swelled the whole, into the hands too. Now
      once a frame the best linear map from a 4x4x4 of the particles' rest places to where they are
      is taken, its turn out and its volume made the rest's, and the homes take `_Solid` (0.7) of
      it, none at the chest wall: squeezed one way the whole bulges the others, and let go it
      comes back. The 13 cm squeeze: facing side 12.5 mm, volume 0.93, the whole 0.983 across,
      1.013 up, 1.005 out. A dent is not linear, so the map sees little of it. Open: more of the
      lost volume back near the dent, where flesh bulges round a hand.
  - The shake under a hard press, 2026-10-28 (Joe: "the latter squeezes still makes it wobble
    badly when pressed hard", "it shakes rapidly when pressed deep enough"). On film11 a held lift
    from below kept a 5 Hz shake the squash did not have. The verify now holds a palm 8 and 12 cm
    up under one breast and logs each particle's move a frame, at 90 or (`-jellyFps 60`) 60 frames
    a second: below 72 the step takes two whole steps a frame, the film's and a desktop player's
    way, and only there did the film's shake show. Three causes, all fixed:
    - The plow pushed a particle out of a hand toward the region's centre even from behind the
      hand's line, so one its spring had flung past the line went through the hand and out the
      far side, was flung back, and so on: at 60, a palm 12 cm up under a breast moved the
      lattice's bottom row up to 10 cm a frame. Sent out by the side it was on instead (0.05 mm),
      a withdrawing hand dragged the particles behind it out with it, 11 cm past their rest, then
      let go 82 mm in a frame: "sticky" on film15. Now a particle behind the hand's line is left
      be, as a hand pushes and never pulls.
    - The homes moved while each neighbourhood's fit held the plain rest, so every particle was
      pulled two ways (0.35 mm a frame at the worst). The fit now takes the homes' own stretch.
    - The pair's contact moved each side half the overlap it saw, and both saw all of it: a whole
      correction a frame, the edge of stability, so the two traded places every frame. A quarter
      each now. Held, the worst particle moves 0.06 mm a frame, 0.00 with the contact off: what is
      left is still the contact, too small to see. (SUPERSEDED 2026-10-28 by the seam, below: at a
      quarter a held squeeze passed through.)
  - The regions are no longer under the breast bones (Joe: hands with MagicaCloth or DynamicBone
    colliders are what most players have). Under a bone the cloth took each region for a bone of
    its own, as it takes anything under a chain's root, so a hand collider would have pushed the
    anchor off the skin. The step is handed the bone and each region's place on it
    (`_JellyOffsetAt`/`Turn`, saved in the step's materials); the region stays under the chest.
  - One mass (Joe on film15: "the whole breast is not moving like one mass, like the squash does
    it"). With the cloth on the bones nothing moved the whole for a hand, the jelly only dented. The
    mass is back in cloth mode as the whole's give alone (`_MassCarried`): moved on with the anchor
    each frame, so it adds no swing to the cloth's, no gravity of its own, 2.5 Hz at critical.
    At give 0.6 within 0.35 of its length (the squash's bend) the verify's 13 cm squeeze closed the
    masses 57.6 mm, the facing side 98.6 mm, volume 1.02, back in about 0.6 s; but on film16 the
    hands drove the pair together until the masses met and then up toward the chin. Now give 0.35
    within 0.2 (film17).
  - Stretch and breast against breast (Joe on film17: "all it needs now, is stretch in there? and
    titty to titty collision?"):
    - Stretch: carried, the mass is only a hand's give, so the stretch in motion read nothing. It
      now reads the cloth's swing, the bone's carry of the region's centre against a still copy of
      the region on the chest (`Jelly Hang 0/1`, the provider's third and fourth transforms), the
      squash's own rule from its follow off its rest. The film's stretch shots hand the squash's
      motion knobs on to the jelly's step, which took them once at build.
    - The pair passed through on film17's squeeze: each side went a quarter of the way out of the
      other as it had dented, and the springs pulled it straight back in. Now they meet at a seam:
      each home deeper in the partner's homes than it lay at rest goes half that way back out (half
      each meets halfway), never back toward the chest, and the springs take the tissue there. It
      reads the partner's homes, never its particles, so nothing trades places a frame apart. The
      seam curves with the two bodies. The masses keep apart by the distance they sat at as built,
      not where the cloth has the anchors.
    - Tried and taken out: particles past the seam put back on it. That laid a slab of the lattice
      flat on the seam, the skin and the garment over it on one plane, and on film18 and film19 the
      skin drew through the garment in ragged patches under the squeeze. Its first cut also read the
      lattice's particles outside the flesh by their distance from it and shoved them 6 cm at rest.
    - 60 fps, the 13 cm squeeze, with the particles put back: masses 52.5 mm closer, outer side
      139.5 mm, inner 85.6 mm, volume 0.956; settled 0.4 mm; a palm 12 cm up held, 0.17 mm a frame
      at most. Open: the volume lost under the squeeze, the lens between them pressed to half.
    - Now: the homes pushed half out of the partner's homes, and a particle the hands drive deeper
      into the partner than its home lies goes half the way back each pass, each to its own home's
      depth, so the layers keep their order. 60 fps: PASS, settled 0.4 mm, the bodies 42.0 mm, the
      squeeze's volume 0.947, a palm 12 cm up 0.20 mm a frame at most. Film20: the pair meet at one
      seam under the squeeze, no patches. The homes alone (no particle push) failed the verify, the
      bodies 1.9 mm: the hands' push reaches the facing side through the lattice past the homes.
  - **The cloth never reached the jelly (found 2026-10-28 on film20's hops).** Read on the film's
    hops: the region's bone 0.0 mm off its rest when the step is handed it, 38.7 mm at the frame's
    end. The kit's render controller runs its provider and blitters in a script LateUpdate, after the
    cloth has put its bones back to rest and before it writes them, so every cloth-mode run and film
    had the regions still on the chest: the whole moved only by a hand's give, and nothing swung
    or stretched in motion. The squash met the same and reads its follows through a helper mesh
    skinned one vertex to each (`_YAPS_SquashSkin`), as skinning runs after the cloth.
  - Fixed the squash's way (Joe: "enact your proposed fix"): a helper mesh, `Jelly skin [FPR]`,
    skinned four points to each region's bone (its place and a tenth along each axis) and four to
    its still copy on the chest, its buffer bound to the step by a second provider in the
    previous-buffer mode, after the anchors and before the passes. The step builds both frames from
    it and puts their difference, the cloth's swing a frame late, on the still copy as the chest is
    now, so the body's own move is not late; spoilt or empty it falls back to the bone at rest. The
    skin is still skinned to the bone, so the lattice must ride the swung bone, not the chest: a
    swing in the lattice on top of the skinning's would count twice. Read in batch, the skinned
    buffer is in the helper's own space, not the world's, so only the two frames' difference is
    used. Hops: the step now has the region 8 mm off its rest at the back face, 0.0 before; on
    film21 the jelly stretches in the anime hops (less than the squash's 1.8), and at rest looks as
    film20 did. 60 fps verify PASS: settled 0.4 mm, turning 86.7 mm (247 when the lattice sat at
    rest while the skin swung), the squeeze as before.
  - **What the jelly is for (Joe on film21, 2026-10-28):** a hand pushed in, the skin deforms
    round it; the cloth swings the bones; pushed together, a squish between the two, "not
    realism per se, but a squished together anime kind of vibe", never sticky, never clipping,
    the volume going up, down, front or back and never "into a plane void"; one may flow over
    the other. And weight: "these are massive ... a sense of, yes, those are massive". No
    MagicaCloth hands on the film: the jelly's own give moves the breasts without them, and
    they must smoosh and look good without them. The film: one realistic hop, no anime
    comparison; every press deeper (the lift from below 15 cm, top and bottom 17, one hand
    pushing one into the other 22, both sides 18).
    - The pair now meet at a plane between their masses, moving and tilting with them, no
      friction: each home past it eased back behind it by a soft minimum (layers keep their
      order, the face flat to a twelfth of the half width), and the whole grown across by the cap
      past the plane of a ball its half width, so the flattened volume goes up, down and
      forward. A particle driven past it goes half the way back to its home's side a pass. Only
      closer than they sat as built (`_PairReach`: each half width, the gap as built).
    - The mass is no longer carried on the bone: it lags the cloth's swing at the breast's own
      weight (0.75 Hz for 9.4 kg), critically damped, within 0.3 of its length; the hands' give
      stays 0.35. The stretch reads the cloth's swing and the mass's lag on it together.
      (REVERTED after film22, below: carried again at 2.5 Hz, within 0.2.)
  - **Joe on film22 (2026-10-28):** "we're getting there, very close". The plane contact stays.
    - The first hop drew both breasts out into long teardrops (the film's frame 12; the stretch
      readout stayed 1.000, so it was not the stretch in motion): the free mass trailing every hop
      at 0.75 Hz. Joe read it as anime: "put it back to realistic and leave it like that". Carried
      on the bone again; the weight is the cloth's, as the source physics have it.
    - **Motion Style removed** ("remove the anime slider for now"): the stretch in motion is real
      flesh's alone, (0.6, 0.1, 1.12) in `WriteKnobs`; README updated; the smoke story dials Spill
      instead. 5.0.0 is unreleased, so no shipped build carries it. Commit trap: the committed
      `SquashPlayProbe.cs` reads `motionStyle`, so the commit removing the field must carry a copy
      of it with only that taken out (the working file is the squash WIP, never committed). Done so.
    - "Deeper" meant nearer the body, over more of the breast, not further into it: the presses
      back at film21's depths, the side, top and bottom hands 7 cm nearer the chest, the lift 7.
    - Carried again, the verify failed (settled 13.6 mm): resting exactly as built, the plane's
      depth read a hair above 0 (the step's own readout: centres 0.3399 apart, 0.3399 as built), and
      a particle past it went all the way behind it however lightly they pressed. Now a particle
      goes no further back than its own home, which the plane eases by how hard they press. PASS
      at 60 fps: settled 0.4 mm, the contact 114.2 mm, the squeeze's volume 0.967. Film23 sent.
  - **Joe on film23 (2026-10-28): "the best-looking physics I've ever seen".** Committed 5b894b6.
    His order after it: the timing, the old bake out, the wearer's whole body as contacts, the
    finishing pass, a film, commit. Cubic sampling rejected (3 to 8 times the reads).
    - **Cost, Joe's bar:** "as low as humanly possible ... if it's going to go on multiple avatars
      with multiple pairs, with multiple players, we need it batched, instanced ... this will not
      increase overhead. Period." First measure, in the breast verify (the client's chain timed
      alone, RTX 4080): 0.029 ms a frame at rest for the two passes, 0.009 of it to submit; the
      same passes only copying 0.006 ms, so the step's own work is about 0.023. Seen in reading,
      not yet measured: every pixel of the state works out both regions' cloth anchors through the
      skinned helper (eight buffer loads, an inverse, three matrix products) and every particle
      works out the plane again, where once a region would do.
    - The distance-field bake (`Jelly_Bodies`, `Bodies()`, `_Shape`, `_ShapeBox`, `Body()`) taken
      out; nothing read it since the plane.
    - **The wearer's own body as contacts:** the client's arrays hold every player's hands, the
      wearer's included, never arms, so the film's own arm went through the breast. Now eight
      capsules bone to bone, upper arm, forearm, hand and thigh a side, the bones after the four
      anchors in the provider's transforms (`_JellyAnchor[16]`), each as thick as the median
      distance of the skin its bones carry. A bit each in the side row's texel 41 says which are in
      reach, so with the arms away a particle pays one read. They push the particles out the
      nearest way, never pulling (hands plow toward the centre, see below); only the own hands
      lean the mass (an arm resting at the side would shove the whole away); the
      draw presses them in per vertex with no rim.
      - Measured (the verify's own-arm stage, wrist taken to between the pair and held): every
        capsule in reach found, the provider handing 16 transforms, the particles moved up to
        213 mm. The radii as first taken (the median of the skin round the whole bone) were fat:
        upper arm 98 mm, forearm 73, hand 56, thigh 236 (Kar). Now the skin round the middle
        three fifths, its 30th percentile. Film24's own hand looked unchanged; the shot filmed
        alone (`-spOnly Own`, readout of texels 41 to 45) showed the step finding the arm and
        the breast riding up on the forearm while the squash's went through it.
      - Not yet contacts: the head, the belly and hips (the breasts rest on the belly, so it needs
        the rest-relative rule the pair's plane has), knees, shins, feet. Joe asked why not every
        part; cost grows with the contacts in reach, not with how many exist. The bounding test is
        a ball round the whole lattice (about 40 cm on Kar), so arms resting near count as in
        reach: 0.029 to 0.034 ms at rest after the contacts went in.
      - **Film25: the breast shook under the held arm (Joe: "why does the titty violently jiggle
        like that during the arm press").** The film's frame differences: the squash still while
        the arm held, the jelly moving every frame. The verify's trace: a held palm settles to
        0.000 mm a frame, the held arm kept 0.4 mm on average with one particle jumping 20 to 54
        mm a frame. Instrumented: the jumping ones were edge particles whose rest lay behind the
        forearm (84 mm behind its line, outside it). Plowed toward the centre as hands are, each
        slid round the arm toward its rest and, near the line, was thrown back over the top, a
        5 frame orbit. Turning the plow toward the particle's own side near the line halved it but
        left a 7 frame orbit at the upper arm and cut the hands' squeeze by 30%. Now the wearer's
        own capsules push out the nearest way, hands unchanged: the held arm settles to 0.000 mm a
        frame within half a second (worst 26.5 mm while it arrives), the hands' squeeze 136.3 and
        57.7 mm as before. Turning moved the tissue 107 mm against 124: the resting upper arm now
        stops the swing at its surface instead of plowing it back. The verify reports the held
        arm's shake. Film26 showed it: still under the held arm, as the squash.
      - **Lifted from below, it dents more than it rises (Joe on film26's new shot, both palms
        under: "why is it deforming the titty rather than lifting?").** Every press moves the
        whole by `_Give` 0.35 of its reach into the mass and no further than `_Limit` 0.2 of its
        length (41 mm on Kar), the rest left to dent: a palm 8 cm up lifts the whole 35.3 mm.
        LEFT AS IT IS, Joe 2026-10-28: "leave gravity and all that to Magicka ... people can deal
        with it later". What was tried:
        - Film27, the jelly lifting on its own: each lean's upward part as far as `_GiveUp` 0.8
          would, weighted by how nearly straight up it points (summed, two hands squeezing from
          either side, each a little low, took the pair 325 mm off rest), the whole allowed to
          0.45 of its length, and weight of its own once risen. A palm 8 cm up lifted it 72.8 mm.
          Out: "i don't want it to collide and double stack magica and my own gravity".
        - Film28, the cloth holding it: a MagicaCloth capsule on each film hand (the film probe's
          `-spMagicaHands`, as on a player whose hand carries one, which moves the wearer's bones
          in game), gravity the cloth's alone. Both rows lifted; Joe: "that just looks broken".
        The verify keeps a readout of how far the whole rose under each held palm.
      - **Film29: the breast stuck to the arm (Joe: "its sticking", the own hand shot).** Pushed out
        the nearest way on either side, an arm moving off dragged the tissue it had passed with it.
        Now a particle behind the line from the limb to the centre is left be, as hands do. The
        verify: the held arm settles to 0.008 mm a frame, the squeeze 136.8 and 64.3 mm as
        before. The shot filmed alone (`-spOnly Own`) against film29: Joe, "fixed".
      - **The skin jags at the sleeve for a frame or two as the arm comes and goes.** Joe's crop of
        film29 (held) was the sticking, fixed by never pulling. Tried and taken out, 2026-10-28: the
        skin held to its own thicker radius, the 90th percentile of every shown mesh round the bone
        between the tissue's and 1.4 times it (Kar: upper arm 118, forearm 91, hand 56, thigh 222
        mm; untamed, a thigh took in Kar's hips at 376 and a hand its other fingers at 97). Pushed
        out the nearest way on every side, it wrapped the arm in a tube of breast over the sleeve
        (film30, worse); on the centre's side alone, its edge stood outside the sleeve as notches
        all the while the arm was held, where the thinner radius hides the same fold inside it.
        Open: the frame or two of jags as the arm arrives and leaves.
        **Found 2026-10-29, the film's, not the jelly's:** the film probe drew each frame at the
        start of the frame, before that frame's step, so the jelly it drew was a step behind
        everything else. Measured at each render (`-spOnly Own`): the forearm end the draw presses
        with lay up to 12.0 mm off the wrist as drawn while the arm moved, 0 held; drawn after the
        step, as the client draws (`-spRenderLate`), 0.0 every frame. Every jelly film before this
        drew its presses a frame late on anything moving, other players' hands included (the
        squash works its hands out in the draw, so it was not late), so jelly films now pass
        `-spRenderLate`. Open [GAME]: in VR, whether VRIK moves the arms and chest before or after
        the step. Both run in a LateUpdate at the default order (the client's late events manager
        and FinalIK's solver), so the step may see the arms before IK places them: a forearm held
        under the breast and moved slowly should dent at the sleeve, not off it.
      - **A little more pillow (Joe):** the particles' pull to their rest `_Hz` 2.4 from 3.
      - **The avatar's own colliders press the jelly instead of moving the bones (Joe: "deform the
        mesh, not move the bone"), 2026-10-28.** The MagicaCloth spheres and capsules and the
        DynamicBone outside colliders in the breasts' cloth lists come off those lists and press as
        the own limbs do (eight at most, nearest the chest first), each handed as two child ends at
        its capsule's ends, anchors 16 to 31, side-row texels 64 to 79, mask bits 8 to 15. Taking
        the jelly off puts them back. One within a lattice cell of the breasts' skin as built stays
        in the cloth: the lattice reaches past the skin, so it would dent it for good (a 6 cm sphere
        in the cleavage, pressing, left the tissue 41 mm off its rest; left in the cloth, the cloth
        shoved the bones 3.5 cm apart, as any cloth would). The verify, with a capsule round each
        forearm and a 1.5 cm sphere in the cleavage: all three pressing, settled 0.8 mm, held arm,
        squeeze and lift as before. A cone presses as a cylinder of its larger end. Kar's breast
        cloths list no colliders, so on it this changes nothing.
      - **Optimisation pass, first round, 2026-10-28.** Every verify number unchanged. The step,
        timed as the client chains its two passes (each waits on the last): 0.064 ms a frame to
        0.022 at 90 fps on the 4080, 0.031 at 60 fps (two whole steps), two copies 0.005. What did
        it, measured one at a time:
        - Each region's rows padded to a whole number of eight with its side row. A GPU runs
          pixels in blocks of rows, and the block holding a side row held particles too, so it
          ran the side row's work and theirs one after the other. Most of the gain.
        - The rider's texels skipped where nothing reads them (worn: no parser pass, no sleep).
          They stepped a whole particle twice a region.
        - The scan's player arrays declared at the 64 it reads, not the client's 255: 16 kB of
          constants every first pass. The head array was already declared short.
        - Each pixel works out its own region's anchor alone (sixteen skinned reads a region).
        Tried and no gain: no discard in the second pass; the draw's side texels read before
        its first branch. Measured, a pixel class at a time: particles about 9 us, the side row
        3 us, a pass that returns at once 4.5 us (a copy 2.5).
        Open: **the draw now costs about what the step does**, 0.02 to 0.04 ms a camera render
        on Kar (`-jellyCost`), nearly all in the breast vertices' region work. Next: the own
        capsules' reach is a sphere round the lattice that takes in the thighs at rest (mask 265
        and 401 with the arm down), so every breast vertex and particle tests them; a box round
        the mass would leave them out. Then skin with a sliver of a share skipping the region.
        **Second round, 2026-10-28/29.** Reach is each breast's skin box (`_JellyBox0/1`) plus
        8 cm and the mass's swing, not a sphere round the lattice: at rest on Kar each breast
        keeps its own side's upper arm and thigh (the thighs stay in: the breasts hang low and
        the thigh capsule's round top sits up in the hip). `-jellyCost` now times a crowd: one
        avatar 0.025 ms, 16 avatars 0.243 (0.015 each), 20 m off 0.015 (16: 0.174), the far
        switch's parser pass alone 0.004, two copies 0.005; the squash's draw about 0.06 to 0.08
        a render on Kar's second pair, the jelly's little. **A coarser lattice saves nothing:**
        8x8x8 (512 particles, `-jellyCells 300`) costs 0.026 against 10x10x10's 0.025; the
        passes' own price is the cost, not the particles. Tried and no gain: the far decision
        ahead of the anchor. Checked: the squash moves no pixel the jelly draws (0.000% at rest,
        a finger, turning, the squeeze, the arm across), now part of the verify.
        **Far and out of view, switched off by the parser (Joe 2026-10-29: "eat that cost", far
        4 m not 6, and cull by camera):** the breasts' build now carries the third pass and the
        texture parser that switch both steps' blitters off, far from the viewer's head (4 m, back
        on within 3.5) or well behind their camera (more than 100 degrees off where it looks, less
        the pair's own angle there, ten more to go than to come back; never within 2 m, which keeps
        the wearer's own copy on). The camera is the client's `_CVRRenderingBuffer`, the local
        player's camera position and rotation a frame late; unbound, as in the editor, nothing
        counts as out of view. Off, a copy costs the third pass, 0.004 ms, and the parser's
        readback; near, the third pass adds 0.005 (0.031 against 0.026 at rest). The verify plays
        the parser by hand and turns a camera 3 m off away and back: the steps off, on again, the
        state restarted 1.2 mm off rest; every other number as before (run108). Open [GAME]:
        whether the stable client has the rendering buffer (Joe runs the beta), whether the parser
        runs on remote copies, the readback's cost; an avatar behind the viewer seen only in a
        mirror keeps its rest shape.
        **Resting, switched off too (Joe: the sleep "a little bit more aggressive").** The breasts
        never slept: the prop's sleep was compiled out of the worn build, and wanted the anchor
        still to 0.1 mm, which a worn chest never is. Now a worn region rests after half a second
        (`_Rest`) still against its anchor: no hand kept, the nine front particles under 1 cm/s
        against it (less the step of gravity the first pass's prediction carries, 1.2 mm a frame
        at 90, which first read as 10 cm/s), the wearer's capsules in reach under 2 cm/s against
        it. Still, not at rest, so an arm on it rests with its dent (Joe picked this over resting
        only untouched: Kar's upper arms overlap the breasts' boxes by 17 to 23 mm at rest). The
        third pass then switches the steps off and the draw keeps the state as it lies; it wakes
        them when the anchor moves 1 cm or turns 3 degrees from its last step, a capsule in reach
        moves 1 cm against it or one comes into reach, or a hand comes within 8 cm of either box
        (three points a hand: nine, over two regions and 64 players unrolled, would not compile).
        The third pass now takes the breasts' settings too: without their skin boxes it judged
        reach on the whole lattice and woke at once. The verify (run117): off at frame 60 with the
        arms down, on as the finger comes, off and on round each squeeze, every number as before,
        0.031 ms awake. While it rests and the body sways, the held shape can sit up to 1 cm off
        the body before it wakes (Joe's accepted trade). Open [GAME]: how often a worn avatar
        rests in VR.
        **Any pair, not only the breasts.** The worn build takes the first of the squash's pairs a
        name matches (a menu for the pair that is not the breasts). It hangs from the trunk bone
        the pair's skin's bones hang from, Hips for a seat, Chest for Kar's breasts as before, and
        a pair behind that bone is framed facing back, so the lattice's front is always out of the
        body. Its cloth and colliders are found by the pair's name; each collider's ends name the
        pair, so taking the jelly off puts it back in that pair's cloth. Kar's seat (run118) failed
        settled at 18.7 mm: both thighs as built reached the lattice and held it dented for good.
        Each of the wearer's capsules is now thinned to clear the pair's particles as built, half a
        centimetre of room, so it presses only past where it rests (thighs 159 to 96 and 84 mm on
        the seat; one arm 89 to 84 mm on the breasts). Seat (run119): PASS, settled 0.8 mm, a
        finger 147 mm and 1.86% of the picture, the contact 93 mm, rests at frame 60. Breasts
        (run120): every number as run117 bar turning, 109.0 from 109.2. The verify's camera now
        stands at the pair's own front. Next: both pairs at once (four regions); a pair whose
        outside is sideways, as thighs, still gets a front or back frame.
        **Painted pairs, and pairs with no cloth.** The worn build reads painted regions first,
        as the squash does, so a painted pair (thighs) goes on the jelly too. A pair no cloth of
        its own swings rides its most weighted bone, not one hanging from it (a thigh would have
        ridden its shin), and has no stretch in motion: its bone is the limb, and a seated thigh
        read as swung a quarter turn off where it hangs. Filmed for Joe 2026-10-29 against the
        squash alone, each pair rebuilt alone on Kar's squash so it is the one filmed: the seat
        (butt3: hops, pokes, slaps, squeezes, a hand between, from behind and below, never
        straight behind, where her tail hides it, or above, where her back does) and the thighs
        painted from the upper legs' weights (thigh1: sat down, knees turned in 12 and 22
        degrees, quickly in and out). Squeezed hard, the jelly's thighs meet flat at the pair's
        plane and spill outward; the squash's pass into each other. The film tool now takes
        the pair's own front, films late after the jelly's step on dev too, and has a slap shot
        and a seat mode. Open: the thighs' frame is still front or back, so the plane's
        spill is weighted toward the lattice's front.
        **Joe's notes on those films, and the tests remade (2026-10-29).** "The thighs look off,
        it's just clipping the shins through each other": the knees went 12 and 22 degrees in,
        and on Kar the lower legs meet at 15.5, so the hard shot drove them 6.5 degrees through.
        The seat's film now works its poses out on the mesh, skinning baked each step: the knees in
        till the lower legs come within 5 mm, feet 8 degrees apart so the calves never meet first;
        legs crossed right over left, the knee taken past the other's and lowered till the far half
        of its thigh meets the other, 4 cm on, its shin turned out till it clears (straight over the
        knee, a 30 cm thigh rode so high its shin stuck out sideways; on Kar a thigh rests on the
        other only lifted 36 to 64 degrees); a hand on a thigh, a slap, stood up, hops. The seat's
        film: hops, slaps side to side, both hands squeezing, grabbed from below, pokes, a leg
        lifted back and forward, both up as a squat; no plug, pulse, own hand or shots between a
        pair that never parts. "With the cheek squeeze, it's angling the butt down": the film tool
        took the first renderer wearing a squash material for the body, on the seat's build a strap
        round the hips, so every shot aimed at the strap and the squeeze pressed the slope above
        each cheek's middle. It now takes the squash's own body, and the squeeze aims at each
        cheek's widest outside, level with its crown, where the skin the squash gives it at least
        half of stands out furthest behind (y 1.24 against the bones' 1.375 on Kar). Films thigh6
        and butt9 sent. Open: Joe's verdict on them. **"The thighs look really bad" (thigh6,
        2026-10-29): the crossed thigh pressed into a blade.** As the crossed leg lifted off and
        swung back, the jelly flattened it into a sheet reaching down and out for a third of a
        second. Plane off, gone; the draw's guard alone off, still there: the step's plane did it,
        though it pressed only 3.9 cm, 0.15 of the half width. The plane is where a pair built side
        by side and turned alike meet; a thigh turned across the other (68 degrees between their
        frames at most, against 9 at rest and 28 knees together) lay past it with nothing to meet,
        and every home past it was laid flat. The plane, in the step and the draw's guard, now
        holds by the turn between the two frames, the partner's as its row last wrote it: all of
        it to 40 degrees, none from 55. thigh10: the blade gone, every frame under 40 degrees as
        before; breasts turned 3.5 and the seat 4.7 at most in their films, so theirs is as it
        was. The film tool logs the turn and the plane each frame (-spPlaneLog). Open: crossed thighs now meet nothing, so they can sink into each other; the
        proper contact is each lattice out of the other leg's capsule at its true width, which
        means skipping its own carrier's capsule (both thinned to 27 mm today only because each
        lattice also carried its own leg's). **Thighs parked (Joe, 2026-10-29):** "still not
        looking good on the thighs, due to thighs having a lot less loose skin, they aren't boobs
        or ass, ignore the thighs for now". The jelly is tuned as loose tissue; a thigh wants a
        firmer one, back at a later date. **One pass instead of two, tried and
        reverted 2026-10-29:** one avatar 0.017 against 0.025, but 16 avatars 0.243 either way
        (independent avatars' passes already overlap, so the saved fixed price is hidden in a
        crowd), and the look changed (a particle predicting from last frame's neighbours: the turn
        swung 157 mm against 109, the held shake 0.309 against 0.212). Not worth a film. Stepping every
        other frame was weighed and left: the skipped frame still pays the two passes, the
        stepped one the slower two-step path, about 20% off for a frame of lag.
        **The same breasts, squash against jelly (run106, one camera render on Kar):** the
        squash on them costs 0.047 ms a render (0.354 with the breasts handed back to it against
        0.307 with it on the second pair alone); the jelly's draw on them 0.021 a render, plus its
        step 0.015 to 0.025 a frame. Even at one render a frame; every further render (shadows, a
        mirror) the jelly wins by 0.026. Joe 2026-10-29: the squash never shipped, and if the
        jelly can do everything it does and costs less, the jelly replaces it. Not yet everything:
        the jelly carries breasts only, and parting a pair for a plug is the squash's.
    - **The finishing pass:** the draw already pressed other players' hands into every vertex
      (`YapsJellyPress`; said wrongly to Joe that nothing finished the skin). Added: the own
      capsules, and last a guard at the plane midway between the two skins (each breast's skin's
      reach toward the other baked as `_PairSkin`, moved on by half any overlap as built): skin past
      it eases back to within 4 mm, the deeper the further, so layers keep their order. The box's
      half width, which the particles' plane still uses as approved on film23, sits past the skin
      (7 cm into the other's box on Kar), so two such planes never meet.
  - **Idea, Joe 2026-10-28:** two avatars wearing the same YAPS could press each other's tissue
    through these contacts, body to body. Not started.
  - Check:
    the squash's skinned slots are tested against world matrices within half a metre, which in
    batch only holds with its host at the world's origin.
  - MagicaCloth colliders on the film's hands were tried and taken out again (Joe: "much more
    broken"). Found with them: the cloth-mode build switched the pair's cloth OFF (`on: !cloth`) while
    its report said "switched back on", so every cloth-mode run and film before 2026-10-28 13:00
    (film11 included) had the breasts held still on the chest, mass and cloth both off, the jelly
    only denting. On the film the squash's breasts were shoved together by the hand colliders and
    the jelly's never moved. Fixed; the numbers above from cloth mode predate it.
  - The pair squash each other, by volume, not on a plane (Joe: "a flat plane is what I want to
    avoid ... dynamic based volume preserving squish between the tits"; a plane only as the
    fallback). Each breast's body is baked as a signed distance field over its lattice box, 16 a
    side, from the volume's envelope. A particle inside its partner's box looks up how deep it is in
    the partner as the partner has swung and dented this frame (the partner's local move undone
    through eight of its particles), and if deeper than it lay at rest, goes a quarter of the way
    back out; the partner does the same. (SUPERSEDED 2026-10-28: the seam, above, from the
    partner's homes.) Each neighbourhood's fit keeps `_Squish` (0.9) of its own squash and
    stretch at constant volume (Mueller's linear match, normalised by the cube root of its
    determinant), so pressed flesh bulges.
  - Batch on Kar: the first steps landing particles metres off was a cleared mass texel reading as
    stepped at time 0, fixed (validity needs w above 0). Now PASS: settled 0.7 mm, a finger 35 mm,
    turning 409 mm. The other's anchor pressed 4 cm in: the side facing it pushed up to 15 mm
    with the bodies on, 0.7 mm with them off; the lattice's volume 0.996 of before. Next: the film
    again, the cost bench, the cloth comparison.
  - **Then a full optimisation pass (Joe 2026-10-28, "after all this is done"):** bench every
    piece the step now runs (mass, swing homes, the fit and its volume, the pair's contact) against
    the old lattice, cut what the numbers say, and settle `BreastCells`.
  - **Two scales, proposed 2026-10-28 (Joe: "is there a way to genuinely make it all look shit ton
    better, but keep it extremely optimized"), Joe to choose, after the bench:** small and fast
    per vertex with no state (finger and hand dents as the squash draws them, and the face the pair
    meet at: every vertex held behind the one shared plane, the cap it loses pushed out round it, so
    the two cannot cross); big and slow in a much coarser lattice (4 to 5 a side, not 10: the
    whole's give, the fill-back); the swing the cloth's. A breast nobody touches and that has
    settled stops stepping, as a settled prop does, and so does one far from the viewer. Further
    still, if that is not cheap enough: the few shapes a breast makes baked at build from the full
    lattice, and only their weights stepped.
- **Ideas queued with it, Joe 2026-10-28:**
  - **The inspector:** the squash's own component and inspector, not a new one (Joe: "do we steal
    that for this jelly?"); the jelly is the squash's engine. Knobs that already mean the same
    drive it: Meet (the pair's bodies), Spill or Volume kept (`_Squish`), In motion (`_Motion`, wired
    already), Others' hands and Hand size (the scan), Lying back (`_Gravity`). One new fold for what
    the squash has no word for: weight (measured kg shown, a multiplier over it), bounce, settle,
    give, swing limit. The Avatar Tester's test hand reaches it only in Play mode, as the step is a
    CCK blitter.
  - **Size blend shapes at run time:** the mass follows a size slider. Likely channel: a helper
    skinned mesh read in `SkinnedMeshRendererVertexBuffer` mode [R].
  - **Mesh-based bras:** the user names the bra mesh. Worn, it supports the mass (stiffer, damped,
    a smaller swing) and holds the tissue inside the cup from within, room per particle baked from
    the cup. Open: how the step learns the bra is on, since a toggle switches a GameObject; a marker
    transform the toggle's clip also moves is one channel [R].
    - Joe on film22, planned for 5.1 as item 27: found, not named. The build finds the garment over the skin by itself and
      bakes how covered each particle is; covered tissue holds, bare tissue (the cleavage, above
      the cup, under it) gives more, and what a squeeze pushes out of the cup goes to the bare
      side. Run time is one value a particle read with what the step already reads, so close to
      free; the bake is build time. The cup's edge is soft over one cell (4.6 cm on Kar).
  - **Remote fingers and anything else with a collider:** a small invisible MagicaCloth sensor net
    over each breast, pushed by other players' MagicaCloth colliders (which do push cloth in
    ChilloutVR), read through a helper mesh as above; each pushed sensor becomes a contact in the
    scan's slots. The wearer's own fingers need no sensor: the provider reads its own finger bones.
    Unknown: whether remote colliders push a mesh-less bone cloth, and DynamicBone colliders at all
    [GAME].
  - **The PhysBones conversion to 100%:**
    - Grab and pose: the wearer's own hands through the animator moving a marker transform from the
      gesture parameters; other players' need a grip signal the globals do not carry (look in the
      client) [R].
    - Stretch and squish: the mass's in and out from the source PhysBone's max stretch and squish.
    - The parameters a PhysBone drives (`_IsGrabbed`, `_IsPosed`, `_Angle`, `_Stretch`, `_Squish`),
      worked out in the step and carried to the animator by the texture parser, the GPU to AAS
      bridge proven in game. For chains left on MagicaCloth (hair, tails), a blit works them out from
      the bones through the helper mesh that sees cloth.
    - The source PhysBone's settings driving the mass (pull, momentum, stiffness, gravity, falloff,
      immobile, limits, radius), the volume's mass where a part has none.
- **Order, Joe 2026-10-27:** the squash shader working on MagicaCloth first and 5.0.0 shipped; the
  live tissue and the rest of his ideas after, in 5.1.
- The menu item rebuilt the assets with the preview scene, which replaced the open scene: it now
  only adds the prefab, building it without the scene if it is missing.
- **Cheapest full-body soft body, a design shoot-out (Joe, 2026-10-29).** Joe's brief: full body,
  any avatar, slaps that radiate, and "the most absolute cheapest in terms of performance, that is
  number 1, above all else". Five designs, two skeptics (cost, works in CVR), one judge ranking by
  cost first. All figures ESTIMATES on the 4080 basis from the measured jelly numbers; full text in
  the session's scratch (`shootout/all.md`).
  - **Winner: Impulse Skin.** No lattice: about 70 texels a copy (2 KiB against 192), one
    blitter pass, no parser. A damped jiggle point per region (off where MagicaCloth already swings
    the bones), the 8 nearest hands from the client's hand globals, 2 slap events. The draw adds the
    region's offset, presses hands in without state, and draws each slap as a damped ring travelling
    outward. About 0.05-0.06 ms a near avatar against 0.16 for today's breasts; a 30-player
    full-body instance 0.4-0.6 ms against about 4.9 for a full-body jelly.
  - **Grafted:** only a slap fires a ring (a hand entering faster than 0.5 m/s, from first-frame
    depth over dt); rings 5-8 cm long, slower than 22.5 wavelengths a second so they travel to 45 fps;
    no deform in shadow and depth passes; the 4 m far cut from the local head with a half-metre fade.
  - **Gives up:** the bulge under a squeeze and the slow refill, pair and own-limb contact; rings are
    drawn (circular, fixed speed, no reflections, two at a time). Biggest risk is the look (a ring
    reading as a decal), then what every design shares: the body's own draw reading the blitted state
    is not proven in game yet (above, [GAME] open).
  - **Joe's questions:** modal (DyRT) is about 2x cheaper than the jelly but not the cheapest (8
    shapes paid at every vertex in every render), and a travelling slap needs about 64 modes, so it
    draws a ring anyway. Refining the jelly is at its floor for a full body (passes and the draw
    dominate, particles are free). Runner-up Skinfield, a real wave equation on a surface field, is
    0.5-0.7 ms at 30 players with the riskiest setup.
  - **GrabPass for inputs, Joe's hybrid, rejected by its own designer:** every player's hands already
    reach a shader free through the client's globals, and the blitter steps before any camera
    renders, so it can never read this frame's grab; it would be a 0.067 ms a camera tax (measured,
    `GrabPassPerf`) for nothing. It stays a tier for other avatars' real shapes if the YAPS atlas
    already pays the grab.
  - **Spike, on Joe's go and not while he is in VR:** (1) draw only, a fake timed state on the breast
    vertices, passes at 0.010 ms a render at rest or less; (2) step, 0.010 ms a copy in a 16-avatar
    crowd, 0.005 at 20 m; (3) a film, a 3 m/s palm slap on the seat peaking 10 cm off at least 6
    frames after the impact point, a resting hand firing no ring, Joe judging flesh or decal; (4) one
    upload, a remote player slaps it. If (1) fails, Skinfield next.
  - **Spike, built 2026-10-29 behind `-jellyImpulse`** (`_YAPS_JellyImpulse.x` above 0 in the draw,
    `_Impulse` on the step; off by default, the lattice path untouched). Joe kept own-arm contact and
    the pair's push ("bruh" at dropping them) and asked for the squish between them, so the step
    still writes the own capsules, the pair plane and its spill growth (texel 59 z), and the draw
    grows the skin across the plane by it. Slaps are caught on the skin's box taken as an ellipsoid
    (texel 84 the nearest palm last frame, 80 to 83 two rings).
    - Measured (4080, `-jellyCost`): draw the same as the jelly's within noise; step 0.020 ms a copy
      against 0.031, 0.310 for 16 against 0.449 to 0.461, 0.021 at 20 m. Only 1.5x, not the 3x
      estimated. Returning early for every texel the impulse path does not use gained nothing, so
      the fixed cost looks like the big step shader's size (registers, occupancy) and the in-place
      copy. Next lever: a small step shader of its own.
    - **Open: the jelly's own step reads 1.8x its 28 Oct figure** (0.243 to 0.449 for 16, the far
      copies too), unexplained. Bisect before trusting either number.
    - The film: slaps fire (three in the slap shot, 1.15 cm at about 2.9 m/s), but the ring read
      as a shimmer in the highlights only, so the defaults grew: 1 cm a m/s to 3 cm, wavelength
      10 cm, fade 0.4 s. Joe: the jelly squeezes better and hands clip through on impulse. Cause:
      the draw's press only cleared the ball round the capsule's nearest point (skin beside a
      finger poked straight in stayed inside) and its rim was a fixed quarter radius (a deep hand
      sat in a hole of its own width), where the lattice drags a cell's width of skin in first. An
      exact exit from the capsule fixed the clipping and tore instead (Joe, second 16 of the film: a
      pointing finger): pushed point by point onto the hand's surface, the top and the skin under it
      met on one surface. Now the step measures each hand's dent once (texels 85 to 92, palm and
      finger) against the skin as it rests, an 8 by 8 octahedral table of its reach from the centre
      baked at build (`Jelly_Reach.asset`, a texture since a material keeps no array), and the draw
      spreads it as one smooth bump (`YapsJellyBump`) wide enough to stay outside the hand's curve.
      Also a bug of the spike's own: its lattice, rounded up from 8 cells, came out 59 cm deep for
      40 cm of skin, the centre 9 cm forward and the front at 0.4 of the depth, so hands pushed
      toward a point near the front and the whole-breast swing reached 40% at the skin. Now 3 a side.
      Joe: the jelly's two-hand squeeze still pushes the pair together and impulse's does not.
    - Joe asked whether impulse keeps weight, size and volume: weight, size and the swing yes (the
      measured litres set the mass's rate, as the jelly's), volume no, a dent only took it away. Now
      each hand gives the whole half its dent (the pair's spill fed by it too) and the step adds up
      the volume the dents took, against the skin as built (`_JellyVolume`; measured on the grown
      skin, the swell fed its own dent and hit its cap in a few frames), and grows the region about
      its mass by as much, across the way the hands push (texel 93). The cutout between a squeezed
      pair closes as the jelly's does, but the pair swells tall and square where the jelly's
      pillows, and a pointing finger tore a sliver again. Each piece of the lattice's job rebuilt in
      closed form brought its own artefact.
    - **Verdict so far: the saving does not pay for the look.** Same frame: draw equal, step 0.020
      ms an avatar against 0.031, about a tenth of the whole avatar's cost, and every piece added to
      look like the jelly adds step work. The jelly's own step reading 1.8x its 28 Oct figure may be
      worth more than impulse saves. Joe to choose.
    - **Binned, Joe 2026-10-29: "jelly main, work on its cost and bring the slap over".** Impulse
      is gone from the code (kept in f71ad5f for the record). A fallback for far avatars has no
      job: the far switch already turns the steps off there, cheaper than any stand-in. The slap
      came over: the step catches it in its first pass (texels 80 to 84, `_Slap`), the draw spreads
      it as a ring on top of the lattice (`YapsJellySlap`, `_YAPS_JellySlap`: how much, speed,
      wavelength, fade; 1, 1 m/s, 10 cm, 0.4 s). Filmed on Kar: three slaps at 2.9 cm, a ripple
      over the front for about six frames; a squeeze's hands arriving at 2 m/s fire one too. Next:
      the jelly step's 1.8x (bisect), then Joe on the ring's look and settings.
  - **The 1.8x, bisected 2026-10-29: no regression, the far switch's own pass.** The 28 Oct code
    on today's machine reads its old figures (0.025 one avatar, 0.243 for 16), so the code moved,
    not the machine; `801254b` fast, `db9ee21` slow, and that commit turned the far switch on for
    the breasts, a third pass. The crowd bench gave each avatar its own state and twin but mapped
    that pass's three texels onto the twin; now each has its own strip. Measured, 16 avatars: as
    built 0.418, without the far switch's pass 0.250, awake (its rest test off) 0.417, the anchor
    unskinned 0.411. So the pass costs 0.010 ms an avatar in a crowd whatever it computes: the
    fixed price of a pass (its target and shader changed per avatar), as the steps' own ~0.008
    each is mostly (the same passes only copying cost 0.008). Tried and reverted, no gain: the far
    test decided in texel 18 alone (HLSL's `?:` and `&&` work out both sides, so it needs a real
    branch), and the rest test returning before its walk of the players.
  - **Joe 2026-10-29: "64 all close is an even bigger problem... more avatars barely increases
    ms".** On the 4080 a crowd's cost is per-pass fixed price, so the floor with a blitter per
    avatar is about 0.016 ms an awake avatar (two passes) plus the switch's 0.010, and 0.014 to
    submit on the CPU; on a weak GPU it is the pixel work, which the far, view and rest switches
    take away. (2) Working the anchor out once a region: 1.5% (the unskinned row), dropped. (3)
    Half rate past a couple of metres by the parser switching the steps off every other frame:
    about 30% off a mid-distance copy on the 4080, more on a weak GPU; not built, Joe to choose.
    (4) One pass for every avatar through a shared screen texture, the YAPS atlas's transport: not
    viable for a state. A named grab runs per camera (each eye, the portrait, the CVR camera, every
    mirror, each its own size) and each overwrites the one texture, so nothing persists from one
    frame to the next; the atlas works because it writes and reads within one camera.
    **Joe also asked (2026-10-29) whether MagicaCloth's colliders could dent the jelly.** Already
    half done: the wearer's own colliders press it (texels 64 to 79) and the cloth-swung bones are
    its anchor. A shader cannot read a component, but MagicaCloth moves bones and bones reach the
    step through skinning, so a probe net (a dozen small cloth bones on the skin, collision on)
    pushed by any collider the cloth is given would read as dents, fingers included where the
    toucher's avatar has finger colliders. MagicaCloth ties colliders to each cloth's own list
    (`ColliderManager.Register` reads `colliderCollisionConstraint.colliderList`), so the client
    adds other players' at run time; Joe sees remote players' colliders push cloth in game every
    day, which settles that it works. Not started.

### 26. A fist closed with the grip read 0 in GestureLeftWeight. A user's report, 2026-10-26, fixed on dev, Joe to check in game

A user: a grip squeeze in VRChat became a trigger squeeze after conversion. The converter fed
GestureLeftWeight/RightWeight from the parameter stream's TriggerLeft/RightValue on every
controller. VRChat's weight follows whatever closes the fist: the grip on Quest and most
controllers, the trigger on Index (its docs say "analog trigger"; its feedback board says the grip
on most). ChilloutVR makes the same choice per controller for its own GestureLeft/Right, 0 to 1
while the fist closes (the client's `CVRXRModule.Update_Gestures`: the grip by default, the trigger
on Vive and Windows MR, finger curl from the trigger on Index), and its movement data carries that
float to remote copies.

Fixed: `FeedGestureWeights` works the weight out from GestureLeft/Right on every copy with an
Animator Driver: the squeeze in a fist, 1 in any other pose, 0 open, as VRChat gives it. The two
stream entries are gone, and the weights go local ("#") since every copy computes its own. The
known-answer test `GestureWeightFeedTest` runs the CCK's own task code: red before the fix (two
trigger entries, no driver), green after.

Open [GAME]: on a grip controller, a half squeeze half-closes a weight-driven fist, and a second
client sees the same; on Index the trigger still does it. In-game checklist R13.

## 5.0.1

Speed, held back from 5.0.0 (Joe, 2026-10-21).

### 23. Squash swaps: repoint at upload, planned for 5.1 (Joe, 2026-10-25)
The record is in `archive/Closed-2026-10.md`, "Squash turned swapped-in materials pink".
- **A: leave user clips untouched** and repoint on the upload copy through
  `CCK_BuildUtility.PreAvatarBundleEvent`, the way VRCFury does. Covers the plug and socket bakes as
  well as squash.
- **Two leads, untested:** the shader sweep (item 7) never covered the squash patch, and
  `FollowSwaps` keys copies into the clips before `Flush` writes them while the swap test reads the
  clip in memory only.

### 27. The bra holds, bare skin gives: planned for 5.1 (Joe, 2026-10-28)
Joe on film22: "a modular deform, so it detects the mesh over it and sees, oh yeah, that's a bra.
Bra holds more when the breast is in the bra and less than when the breast is not covered". Not
now: "I would plan it for 5.1 ... because I want this out at some point".
- **Bake:** at build, each jelly particle near the skin looks outward for a garment surface within
  a few millimetres (any renderer over the body in the region: the squash already lists them, "7 of
  7 squash materials read it") and keeps how covered it is, 0 to 1. One value a particle, packed
  beside what the step already reads.
- **Step:** covered particles keep more of their home (the cup holds the shape), bare ones (the
  cleavage, above the cup, under it) give more; what a squeeze or the pair's plane pushes out goes
  to the bare side first, so flesh spills over the cup's edge. The squash's per-vertex dent can take
  the same value later (a covered vertex dents less). The edge is soft over one cell, 4.6 cm on Kar.
- **On and off [R]:** Joe: "we have parameters in your animation states that tell you when the bra
  is on and off". The step cannot read an animator parameter; a marker transform the bra toggle's
  states also move is the likely channel (the provider already hands the step transforms). To
  evaluate first: the toggle states found from the bra's `m_IsActive` curves, copies of the clips
  (never the shared ones), and whether a turned-off GameObject's child still reaches the provider.
- **Cost:** build time for the bake; at run time one read and a multiply a particle.

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

**5. The soft body over the whole skin, the brush choosing what is soft.** Joe, 2026-10-27: "expand
that system to include the entire skin of the body, and the paint brush can determine which parts
actually move and squish, similar to the MagicaCloth paint tool". Item 25 bakes the tissue per pair
and keeps its answers per vertex. Its answers-by-place variant (a box of cube corners per part,
not adopted for its memory) would not care what a part is called, but costs about 5 MB a pair, so
a whole skin needs it sparser. Thinking so far, nothing built:
- The painted weight decides the tissue: free where painted, held where not, the way the shares
  hold the tissue behind each part today.
- Pairs become islands: each painted island gets its own presses (straight in and a few slanted
  ones round its surface) in place of the eleven ways a pair has, and the shader reads a hand
  against the island nearest it.
- Bake time is about a second a press, so a belly, thighs and arms add up to a minute or so. Each
  island's box is a few hundred KB to a few MB.
- Open: islands pressing each other (a thigh into the belly when sitting), which is the pair's
  partner logic made general; and how the shader picks islands without a loop over all of them.
- Open, Joe, 2026-10-27: how far it can be optimised, which decides it; local and remote players
  (the squash already runs on every viewer's own client from bone positions and the hands the
  client hands every shader, so no sync); two avatars' soft parts close together (point 2); and a
  pair on DynamicBone instead of MagicaCloth2: the squash reads only where the part bones are,
  but the colliders press option and the follow points under DynamicBone need checking.

**6. A whole bone physics system on the blitter. Joe, 2026-10-27: "we can make a
physbones/magicacloth system? all of it???" Answered, NOT PLANNED.** Possible in principle: the
blitter's self-update keeps state frame to frame, a material data provider feeds it the bones, and
the hands come from the client's globals. But it is draw-only: nothing writes a bone back, so
whatever hangs on a bone (props, lights, particles, contacts, a renderer whose shader is not
patched) stays where the bone rests, and every material on the moved skin needs the patch. A
viewer with the Cameras content filter off loses the components, so the hair would freeze; Shader
Filtering cuts the solver's loops; the blitter on remote copies is still unproven. MagicaCloth2 is
built into the client and moves real bones, and physics match the source. The part worth having,
soft tissue that answers everyone's hands without colliders on their side, is point 4 and item 25.

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

- **The file list:** `toolkit_files()` = today's `yaps_files()` minus `Editor/Yaps` and `Runtime`, plus `Editor/Core/Physics/SoftBodyContact.cs` **and the `Editor/Core/Physics` folder entry** (both gone 2026-10-26 with the soft body card: the Toolkit now reaches no Physics file). [M emulated in scratch: 191 entries]
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

**The grab's cost, measured in the editor 2026-10-29** (`Dev/Probes/Perf/GrabPassPerf.cs`, RTX
4080, D3D11, ARGBHalf, no MSAA, GPU held busy so the copy cannot hide): a named grab adds 0.067 ms a
camera at both VR eyes' size (4032x2240), 0.030 at 1440p, 0.016 or less at 1080p, and 8 readers of
one named grab cost what 1 does (+0.079 at VR size). An unnamed grab copies once per reader (8
readers +0.224 ms at 1440p). The 4080's 64 MB cache holds most of the screen it just drew; the
screen copy is about 144 MB of traffic at VR size, so a card with a small cache pays its full
bandwidth, roughly 0.2 ms on a 3080 and 0.6 on a 1070 (estimates, not measured). Still open: an
MSAA camera resolves before it grabs, and only CVR in game gives the real figure per camera (eyes,
portrait, CVR camera, mirrors).

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
