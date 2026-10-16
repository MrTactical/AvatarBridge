# Unfinished

The single list. Every open piece of work lives here and nowhere else. Check this document
before starting on a new idea, in case something similar is already on it, and whenever the
question is "what's next" or "what is left to do". An item leaves this file by shipping, or
by a decision recorded in `archive/`. The standing rule still outranks everything on it: a bug
somebody hits while wearing an avatar comes first.

Transport facts and their build order live in `YAPS5.md`; solver maths in `SolverCalibration.md`;
what SPS code may be looked at in `YAPS-CLEAN-ROOM.md`. Finished records are in `archive/`.

## Next up

**Decided 2026-10-15 (Joe): one big release, 5.0.0.** 4.8.0 never ships; everything on dev goes
out as 5.0.0. Headlines: GoGoLoco poses, SPS2 in the converter (detected and carried from the
property names a finished SPS2 bake leaves on a material, never from SPS2's code), a check before
upload that proves the conversion, and the animator merger made visible (built). Also in it: the first
squash build made fast, pairs found by bone name scaled to their peak like painted regions, the
corpus building a squash, and the grouping branch (`physbone-grouping-2`, issue #7) merged after
physics A/B probes and a corpus run. Decisions on the queue's open questions, same day:
- Always Animate on avatars with toggles: measure the cost first, then decide.
- Menu names with `.`, `!`, `&`, `+` or non-ASCII: left alone.
- MagicaCloth2's gravity cap: find another lever (stiffness) and measure it against the original.
- Several plugs on one mesh share one Deform toggle: documented, not built.
- A plug with a size slider: baked at the slider's menu default, so editor and game agree.
- The socket readout showing the wearer's own plug: left as it is.
- Renderers only a child Animator switches on: the survey walks child Animators (one re-baseline).
  (built: `AvatarWeight.SwitchedPaths` reads what each child Animator's clips switch, rebased onto
  the avatar. Its layers stay out of the survey's, since they never compete with the avatar's, so
  `Layer.Bindings` counts do not move; only a renderer that was wrongly Dead changes.)
- Issues #6, #7 and #8: replies drafted, Joe posts them. GrabbyBones: a PR upstream first, drafted.
- Corpus: subsets whenever needed, three full runs authorised for this job.
- Squash shadows on layers with Unity's own caster or a Fallback-only shader: patch the casters.
  (built: the opaque-Fallback case; Unity's own caster was already wrapped)
- Thry's "Unlock all materials" taking YAPS materials back to a plain shader: detect it and offer
  a re-bake. (built: plug and socket inspectors say so beside Re-bake and Build, the upload check
  lists every material by its saved `_YAPS_Enabled`; PreflightTest covers the check and passes,
  2026-10-16. Its setup had used `SetFloat`, which never saves a property the shader lacks, so it
  now writes the leftover entry the way a shader swap leaves it)
- PhysBone-side squish (Bone Spring collision): dropped, the shader squash covers it.
- Physics A/B videos for Discord: yes, test avatars may be shown, captions name none.
- No player globals with the cameras content filter off: documented, no fallback.
- Toy mods: an honest-length switch per plug, off by default, tested by Joe with a toy.
  (built: `YapsPlug.toyModLength`. From the mod's source: it climbs from the tip light to the first
  object with a mesh beneath, takes max vertex Z times that object's forward scale as the length,
  and measures from the mesh OBJECT's position, so off, even a current build measures from the body
  or a hat. On, an inactive 100-vertex mesh sits under the tracker light at the base. Needs Joe's
  toy test in game.)
- A per-plug menu toggle for answering its wearer's own sockets: built (one synced bool a plug).
- A prop case in the corpus: added.
- A slider whose two children animate different properties: covered by the restore pass.
- Also in 5.0.0, Joe's picks from the rest of the queue: the remote twin, a diff between two
  conversions for users, fast reconversion (skipped passes proven equal by the corpus), and YAPS
  portal and duplicate. (twin and diff built; fast reconversion logs each pass's time, the next full
  run says which are worth skipping; portal and duplicate designed in the YAPS 5 record, NOT built:
  a portal is protocol 10, so that is Joe's call)
- No user reports since 4.7.2.
- Joe wears it in ChilloutVR before 5.0.0 ships, with the full in-game checklist (all of it,
  ordered by risk, paced over days), and the release still needs his word.

**Bug panel over the 5.0.0 diff, 2026-10-15: 14 findings, 13 kept and FIXED on dev, 1 refuted.**
Readers per area, each finding then checked by a judge trying to refute it. Fixed, by area:
- Squash: a Rebuild left an alternate look's copy out of the new providers, so that layer stopped
  squashing while its toggle was on (SwapTest in `SquashTestBuilder` now rebuilds; passes 2026-10-16);
  the own-arm capsule push ignored `_YAPS_SquashHands.x`, so animating own arms off left the push;
  the regions tool refused every other body after the last pair was removed.
- Soft bodies: pressing the Toolkit card twice said "No two soft bodies sit close enough to meet".
- Physics: a soft body with Multi Child Type Ignore was reported as split per branch when it is not
  (README now says so); a local-space relay's fixed copy was parented inside a simulated chain, where
  MagicaCloth2 takes every transform under a root as a particle, so the copy swung. The bone's own
  parent stands in now, its offset measured from the pose, for one full-weight source only. This may
  be part of the 4.6 cm left in the record below; unmeasured. A humanoid bone counted as never
  rotating, since muscle curves bind at the root.
- YAPS: every shader whose caster got wrapped was told it takes no tangent; Remove's swap-key
  repointing had no undo.
- Upload check: a prefab instance's own bookkeeping was listed as "not saved" (PreflightTest has a
  prefab-instance case; passes 2026-10-16); a curve binding with no type could throw.
- Conversion diff: a version-only change was headlined "only the counts moved", and a native setup's
  diff pointed at `ConversionReport.md`.

**Second review over the changes since the panel, 2026-10-16: 11 findings, 9 kept and FIXED on dev,
2 judged not defects (one fixed anyway).** Same shape: readers per area, a judge refuting each.
- Physics: a relay whose bone's parent stands in for its source also raised "relayed a bone from
  another chain", contradicting its own Converted line, since the stand-in's parent is the
  grandparent. A relay whose offset could not be measured (inactive, several sources, partial
  weight) was told its source rotates and that a fixed copy was tried; neither was true.
- GoGo: its flight scale layer reads no `Go/` value at all, so the clip test never ran on it, and it
  kept a scale feed alive. A layer reading no `Go/` value now goes when every clip it plays is
  GoGo's own; one borrowed `go_` clip among the creator's keeps it (`GogoLayerStripTest`, both
  cases). `CollectParameterRefs` skipped sub-machine exits, so a parameter read only there could be
  deleted; it is shared by the strip, the nativizer and the face-tracking prune.
- YAPS: a shader's own shadow caster without a tangent still set the "stays straight" note. The
  note is now decided from the passes people see, per function, include functions included.
- Upload check: an avatar under another scene object had that parent listed as outside the avatar.
- Native setup: `Diagnostics.md` sent the reader to `ConversionReport.md`, which a setup never writes.
- Docs: README's *Your call* sentence named two toggles of three; the depth-sync comment said two
  parameters per socket; the fit comment said four speed limits of five (judged a comment-only
  miscount, fixed anyway). Not acted on: a report line saying "Physics options", a wording nit.

**SPS's own menu options, 2026-10-16: both left out, the plan's default applied.** SPS adds
*Legacy Compatibility* and *Stealth Mode* to the menu, as `VF80_legacy` and `VF80_stealth` in every
one of the 30 controllers of the GoGo subset. Read from that subset's 29 outputs and their 242
rebuilt sockets: none has a Light under `WorldSpace/Lights`, so the first switches nothing, and 188
carry an atlas writer outside the tree Stealth's clip switches, so the second hides lights and
contacts while YAPS plugs still find the socket, and "invisible to others" would be false. Both
names are stripped exactly (`VF80_` also prefixes the depth parameters, so never the prefix), a
Skipped line says so, and README's SPS section explains. If Stealth is ever wanted, its clip would
have to switch the atlas writer too.

**Squash costs minutes per avatar, 2026-10-16. OPEN: first fixes on dev (d96bd83), not yet measured.**
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
- *YAPS shader patcher:* a pass's patched text is memoised by a hash of its input, and the source
  read once per run. Each sub-step is timed in a `YAPS shader timing (ms)` log line.
- *Stereo patcher:* plans judged once, regexes cached, verdicts cached by content, and a copy whose
  bytes did not change skips its write and refresh. An equivalence check over 1934 shader units
  found no change in output and halved planning, 261 s to 137 s. Logs `Stereo shader times (ms)`.
  The verify after a copy still runs in every folder, because an include left as text can resolve
  differently beside a different copy.
- *Batched writes:* self-contained clips and masks, restore clips, and rehomed meshes and materials
  each go in one batch with one delete for the stale files, where each file cost two refreshes.
- *Smaller:* report step timings, MeshGrowth reads each bone's matrix once instead of per vertex,
  and YapsRename's remap is memoised once settled.

Before 5.0.0: a corpus subset after 417 finishes, with identical digests to 417 required, and its
timings against 417's. Then `TextureRehomeTest` extended to a mesh and a second material slot, and
run, since the rehome batch is the one change no test reaches. Still to do, in order of size:
- Compiling a material's passes together was written and pulled: compiling the shadow caster with
  the rest can change which shaders are refused, and waiting for it spun. It comes back only after
  a probe of what the error check costs per pass, alone and in parallel.
- The shared content-addressed folder above, the fix for the cross-conversion cost, which no memo
  inside one run can reach.
- The per-slot `.mat` saves in the squash pass (about 35 s), batched: unprobed, because the importer
  and GUID may not exist until the batch stops.
- `RestorePartialOffStates` and `AssertOwnedBindingsEverywhere` still claim, delete and create one
  clip per state.
- Stereo copies written in one batch, a verified marker that survives a restart, and judging in
  parallel.

**Decided 2026-09-23 (Joe):** ship the next release first (one default corpus run authorised,
release on his word), then the two items below. **It is 4.6.4** (Joe, same day): 4.6.3 is skipped,
its number spent on test builds, and the socket readout goes in with it. 4.6.4 shipped that day,
4.6.5 on 2026-09-26 and 4.6.6 on 2026-10-02, so the two items below are next.

**Corpus 412 (2026-10-02, full, throttled, against 410): 6 of 83 changed, all explained.** Five
lose a false "may not render correctly in VR" warning: locked Poiyomi 7.3 includes its own files as
`#include "/CGI_..."`, and the old diagnostics reader sent that leading slash to the drive root and
never saw the stereo macros inside; it reads through `ShaderSpiPatcher.ReadUnit` now. The sixth also
lost a Weight line because `NativeToggleProbe` had converted it in the corpus project with default
settings and slimmed six of its textures; put back with `AvatarSlimmer.Revert`. Lesson: a probe that
converts in the corpus project must not slim, or must revert. Accepted as the YAPS baseline on Joe's
word for the night (410 kept in `Baseline-pre-412-20261002`). The log is named 411 by mistake: that
label was already 2026-09-24's run. Avatar H's digest in it carried the slimmed texture; a one-avatar
YAPS-on subset after the revert brought the Weight line back and nothing else, and that digest
replaced it in the baseline.

**Corpus 413 (2026-10-02, YAPS off, full, throttled): accepted as the opt-out baseline.** Its
baseline dated from 4.5.0 (2026-09-09), so all 83 changed against it. Explained by comparing each
avatar with the accepted YAPS-on run instead: 53 are identical but for the `convertYapsSystems`
stamp, and all 30 that differ carry a penetration system the two profiles treat differently (SPS
and TPS layers stripped instead of converted, so layers renumber and pointers and triggers go; DPS
or TPS marker lights reported as kept, one Converted line, measured on Avatar E by converting it
in both profiles). Every other line matches, so the opt-out run's drift since 4.5.0 is the drift
each YAPS-on baseline already accepted. 4.5.0's kept in `Baseline-pre-413-20261002`.

**A Physics card in the CCK Animator Tester (now the Avatar Tester's Interaction lab, below). BUILT 2026-10-02, unreleased: Joe tries it first.**
Asked for by Joe (motion presets, grab and fling, overlay; not the side-by-side with VRChat). A
card of its own, `Editor/CckAnimatorTester.Physics.cs`, behind `AVATARBRIDGE_MAGICA`, wired through
partial methods so the tester builds without MagicaCloth2. Play mode only. *Walk*, *Run* (circles
1.5 and 3 m across, so there is never an about-turn the cloth would read as a teleport), *Turn*,
*Jump* (Grounded off in the air), *Shake*, *Sit*, *Stop*, driving the avatar root from
`Application.onBeforeRender` with the locomotion parameters alongside. *Grab chains in the Scene
view* pulls the cloth component nearest the cursor toward the mouse with MagicaCloth's `AddForce`
and flings it on release, so it moves a whole component, not one bone. *Draw every chain* sets
MagicaCloth's own `GizmoSerializeData.always` and `clothDebugSettings.enable` on every chain and
puts them back when switched off; the card adds names and each bone's swing bound, measured from
the rest pose captured as Play starts. Newer MagicaCloth2 members are reached by name.
`Dev/Probes/PhysicsTesterProbe` runs it headless in Play mode on a converted avatar: a push moves a
chain 5 mm, walking moves the avatar 1.43 m and swings chains 16.6 degrees against 0.0 standing,
Stop puts it back, the overlay goes on and back to how each chain was. Only 2 of that avatar's 12
cloths were running at rest; why the other 10 were not was not looked into. Not yet seen by eye:
the Scene view drawing and the mouse grab, which a batch run cannot show *(Joe used both on
2026-10-04, round two below; since 2026-10-15 the button smoke probe draws and grabs in batch too)*. **Compile-check gap found
on the way:** `compile-check.sh` never defines `AVATARBRIDGE_MAGICA`, and `check-defines.sh`
compiles the deployed copy in a project, not the repo, so code behind both CCK and MagicaCloth is
compiled by neither until it is deployed; run `check-defines.sh` against the project holding it.
**Gap closed 2026-10-03:** `compile-check.sh` now also compiles `CVR_CCK_EXISTS` with
`AVATARBRIDGE_MAGICA` and no SDK, and `check-defines.sh` compiles the repo (Editor, Runtime, and the
Dev harness by name), not the deployed copy, so the workaround above is no longer needed.

**The Interaction lab: squash, plugs and sockets tested beside the motion. BUILT 2026-10-14 on dev,
compiled in every combination, seen in offscreen renders, NOT YET OPENED by a person.** Joe 2026-10-14: put the squash in the
tester's physics zone, rename it to something universal, and move plug and socket testing there
too. Designed by a five-agent workflow and critiqued; names approved by Joe. The window is now the
**Avatar Tester** (display strings, menu path, Toolkit buttons, report wording; the class and file
keep their names, since deploy copies and never deletes and a renamed file would leave a duplicate
type), and the Physics card is the **Interaction lab** (the old Remember key kept). The public
package names nothing of the add-on: a public `TesterSections` registry, filled from
`YapsTesterSections` by name through reflection, so the add-on installed alone still builds. Its
sections are nested folds: **Squash** (the component's own preview controls, extracted into
`YapsSquashPreview.Controls`; in Play mode Squeeze and Bounce hide, since the cloth is real, and
only the test hand is written) and **Plugs and sockets** (Preview and Drop a test plug per socket,
Into a hole and Through a ring per baked plug, the avatar's and any outside an avatar). Every
defect the critique found is fixed: Preview in Play mode sets the socket's play-mode flag for that
session (it did nothing before); one test-socket path (`YapsPreview.DropTestSocket(kind)`, used by
the window's Test it page too, so every test socket has the one name and any surface can remove
it); a test plug in Play mode only from the prop prefab (one built there leaked its mesh, material
and bake), with the button disabled when there is none; sections get a rebuild call and add a
fingerprint (sockets, plugs, preview state) so changes made elsewhere rebuild the window; the
window rebuilds a frame after a play-mode change, so the squash preview has stopped before the
card reads it; the hand handle draws from `SceneView.duringSceneGui`, kept in the avatar's space so
it walks with the avatar; Stop clears the four player arrays as well as the count; the registry is
public with explicit binding flags, logs an error if the tester is there without `Add`, and the
smoke test asserts both sections registered (none without the add-on); registration rebuilds
open windows a frame later and replaces by name; the lab's header lists its sections, not only
the physics summary; sections append after `SafeCard`, so a physics failure leaves them working;
`compile-check.sh` gains `addon:`, the add-on's own file list compiled alone (the list moved to
`Dev/Build/yaps-files.sh`, shared with `build-package.sh`); it passes. Open: open it in the GUI,
edit mode and Play mode, on a converted avatar with a squash and a socket, and on a prop; that a
Play-mode Preview stops on exit; that the hand follows Walk and Jump; the public package's tester
files still name nothing of the add-on (grep is clean now).
Seen the same day in offscreen renders (edit mode and Play, a painted-squash scene and a socket
scene; `UiOffscreenProbe` now takes `-captureInspectors <type>` and runs the panel's binding phase
before painting, or bound sliders draw their low end). Fixed from looking: **Tune it** beside
**Preview**; no "(s)" in the lab or the squash panel (`BridgeElements.Count`); the Animator layers
legend shows a chip only for a kind present, since a red **conflict** chip on an avatar with none
read as an error to fix. The renders also caught a stale `YapsSquashRegions.cs` in the test project,
left by the rename (deploy copies, never deletes): it owned the settings' GUID, every deploy put the
repo's meta back and Unity handed the real script a fresh random GUID, so a scene saved meanwhile
pointed at a GUID that no longer exists, the knobs read as absent and the film's anime shots matched
the real ones. A package import matches by GUID, so users cannot meet this; the film now warns when
it finds no settings.
**Round two 2026-10-04, after Joe tried it** ("pulling from a bone but not the correct one"; wanted
the solver's settings overlaid, and to select, edit live and keep). The grab took the nearest bone
in screen space, roots included, and a MagicaCloth root never moves, so the pull anchored there at
full force forever: roots are now skipped (drawn as rings) unless a chain has nothing else, and
near-ties go to the bone nearer the camera. DynamicBone joins MagicaCloth (card title "Physics",
each solver behind its own define, all four combinations compile): its chains, radius and
colliders are drawn, and a grab drives `m_Force` on top of the converter's gravity, put back after.
*Draw every chain* labels each chain with its solver, key settings and a health colour (amber off,
not running, or stretched past 1.5x; red NaN or 2 chain lengths from rest); a label click, or
Ctrl-click a bone, selects the component. Inspector edits to selected chains are detected by a
4 Hz hash of `EditorJsonUtility.ToJson` (MagicaCloth's `SerializeData` only, so the overlay's gizmo
switches never count) and refreshed with `SetParameterChange`/`UpdateParameters` by name; the card
lists them with *Keep* / *Keep all*, recorded by GlobalObjectId (instance id fallback for an unsaved
scene) and written back on `EnteredEditMode` with Undo, any reference the record lost restored
from the scene. Not yet seen in the GUI: all of it, and whether references survive the Play
boundary in the JSON at all (the restore covers it either way). The probe still covers round one.
Review repairs the same day: the near-tie window drifted with the running pick (a chain seen
end-on handed the grab to its tip), now measured from the nearest bone; `m_Enabled` is stripped
from the hash and the record, since toggles animate it on chain components and a Keep wrote the
Play-mode on/off state over the scene default; label rects are per Scene view; a bone in reach
beats a label over it; labels place worst first and a red one is never dropped; the avatar's
chains are watched as well as the selection (a locked Inspector); kept records live in
`SessionState` with a static `EnteredEditMode` hook, so closing the tester first loses nothing.

**A mesh riding a plug stayed rigid through every rebuild. FIXED ON DEV 2026-09-26, found by Joe on
his own avatar.** A whole-avatar plug rooted at the armature carries a second skinned mesh, a
collar, as a rider. The rider's generated copy sat in its slot back on the mesh's own shader, with
`_YAPS_Enabled` 0 and no `bakedSlots` record. Every Build took `PatchExtra`'s already-ours branch,
where `IsStale` answered true for an unpatched material (it is its own source, so it names the patch
it would get) and `Refresh`, with no original in the shader to patch from, did nothing; the `else`
that assigns the patched shader was never reached. A rider's `_YAPS_Enabled` was never written
either, where the primary is set to 1 on every bake. So it stayed rigid, and with no `_YAPS_Bake`
on its shader the setup window stopped listing it as part of the plug. `IsStale` now answers only
for patched materials (every other caller already filtered to those), and a rider is switched on as
the primary is. `Dev/Probes/RiderProbe` (armature-rooted plug, body plus collar, the copy reverted
by hand) failed on the old code and passes. The primary and a mirrored slot already handled the
same state (`oursAlready`, and `MirrorToSlots`' own branch); the rider path was the third door and
had none. How the copy got reverted is not known. **Withdrawn, same day:** it was not Remove, which
leaves a rider slot it has no record of alone; an earlier bake or removal of that mesh as a plug of
its own is the likelier history.

**A shader drew into one eye with the stereo patch on. FIXED ON DEV 2026-09-26, found by Joe, not
verified in game.** An eye shader keeps its vertex and fragment functions in an include named from
the project root, `#include "Assets/<vendor>//Shaders/.../Base.cginc"`, doubled slash and all.
`ShaderSpiPatcher.ReadUnit` looked for an include only beside the file naming it, never read that
one, found no vertex function and refused the shader ("vertex function signature not recognised"),
so it kept drawing into one eye. The same beside-only lookup sat in both patchers' include
repointing and in a separate reader `BridgeDiagnostics` kept for the one-eye warning. All now go
through `ShaderSpiPatcher.ResolveInclude` (beside the file, else from the project root through
`FileUtil.GetPhysicalPath`), and the warning reads through `ReadUnit` itself so the two cannot
disagree. A consequence: a `Packages/...` include now resolves too, so a shader including a
package's file gets that file copied into `RehomedAssets` with the rest, where before its include
was left pointing at the package. Patching it then showed a second fault: the header is
`Shader"name"`, no space, which ShaderLab accepts and the rename pattern `Shader\s+"` did not, so the
patched copy came out under the original's name. Both patchers rename through
`ShaderSpiPatcher.Rename` now, anchored to a line start so a display name ending in "Shader" is never
taken. `Dev/Probes/SpiIncludeProbe` (a synthetic shader with both traits, and `-spiShader` for a real
one) passes; the real shader, copied into a test project, came out patched, renamed and compiling,
where before the rename fix it came out patched under its own name. The YAPS rename shares the
helper and was not probed separately. Both eyes can only be checked in game.

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
  **First part BUILT 2026-10-02**, rule 7 of `YapsBendTester`: a hole comes in from a seeded
  direction (0 to 75 degrees off the shaft, any turn) to a seeded depth (0.2 to 0.8L), sweeps 20 to
  50 degrees across the tip there, and withdraws to 1.8L, with no bend out of reach, the atlas
  finding it at its deepest, no seam past 0.5 mm, under 1% of edges crushed outside the opening,
  no stretch past 6x and no mesh step past ten times the socket's, checked at every step.
  `-yapsSeed` and `-yapsScenarios` (default 1 and 6) pick them; a failure prints the
  `-yapsSeed n -yapsScenario i` line that replays it alone, in place of a saved scene, since the
  capture materials are not assets a scene could keep. At its deepest each scenario must also be
  found from the 8-bit camera and a 256-pixel one, a small view as a mirror gives. One generator is
  drawn in order: a generator per scenario off neighbouring seeds stepped the directions 3 degrees
  at a time. On Alexa: 12 of 12 hold on seed 1, 124 to 178 steps each; it has not yet been seen to
  fail. Still to do: locomotion with physics on, toggle flips, a second avatar.
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
- **Corpus 408 (2026-09-23), not clean: plugs sharing a mesh. FIXED, SHIPPED IN 4.6.5.** Two avatars carry
  more than one plug on one renderer (3 and 2). Readouts were built one plug at a time, so the
  next plug measured the last one's readout quads as part of itself and patched its readout slot
  into a copy of the plug: the 3-plug avatar drew its plug three times over, one working readout,
  and the weigh pass flagged the copies as a shared-shader set (that is how it surfaced). Now each
  plug records what its readout is built from (`readoutSource`, `readoutAnchor`, `readoutTip`) and
  `YapsDebugOverlayBuilder.Build` builds every readout a mesh carries together, on the mesh from
  before any of them, one submesh each; `Restore(renderer)` takes all of them off before any bake,
  whichever plug made them. The converter records during the plug loop and builds after it; the
  toolkit rebuilds the whole mesh on each bake. A plug whose material a later plug took over (the
  same slot: only the last bake bends it) gives up its readout rather than showing a bake that no
  longer drives anything. `ReadoutProbe` adds a second plug on the first plug's mesh, in another
  slot and in the same material. Its first run caught one more: the mesh under the readouts was
  kept only on the plug that built the last readout, so deleting that plug left the next bake on
  the readout mesh; every record on the renderer now keeps it. A review of the callers found three
  more, all fixed: Remove rebuilt the other plugs' readouts before putting its own materials back,
  and its baked-slot finder took any material with a bake texture, readouts included, for a plug
  slot to restore (it now skips readouts, and the rebuild comes last); a toolkit bake that stopped
  short left every readout on the mesh off (it now builds them back); and *Update every YAPS
  shader in this project* counted every readout as a failed update, having no source to patch
  from. Probe 3 of 3 (one test mesh exercises two slots: 2 readouts, 4 submeshes), tester 29 of 29
  x3, held shapes, smoke 34 of 34, `check-defines.sh` clean.
  - **The two-avatar conversion then found a pre-existing one, worse: a plug that never bent.**
    Avatar I has two meshes of the same name, each with `SPS/BakedSpsPlug`, and the bake file was
    named for mesh, parent and plug only, so the second bake overwrote the first and the first
    plug's material was left with no bake at all (408 outputs: only this avatar). Readout files had
    the same flaw, by mesh name, and the readout materials by plug name, which is `BakedSpsPlug` for
    every converted plug. All three now carry `YapsBaker.PlaceKey`, six hex digits of where the
    objects sit by sibling index: unique, and stable across reconverts so a reconvert still
    overwrites its own file.
  - Found beside it: plugs baking the same slot, so only the last one's bake reaches the material.
    **FIXED 2026-09-23 (Joe: "fix it fully"), shipped in 4.6.5, not in game.** The 3-plug avatar is one shaft
    with three plug components (last bake winning is right there); the 2-plug avatar is two ears on
    one accessory mesh, one slot, so one ear never bent. `YapsSlotSplit`: when a plug's slot already
    holds a bake from a plug on another shaft (vertex masks overlapping under half), its triangles
    move to a new submesh on a copy of the mesh (`YAPS <mesh> <key> split.asset`), with a slot of
    its own on the slot's original material, and it bakes that. The bake is indexed by vertex, so
    neither bake changes. Both builders: the converter per conversion (`SplitShared`), the toolkit
    on the bake that finds the other plug's material in its slot. `Dev/Probes/SharedSlotProbe.cs`
    (synthetic two-shaft mesh, toolkit door) found three bugs in the first cut before it passed:
    the slots a plug mirrors into were counted before the split, so the second bake mirrored back
    over the first plug's slot; the new slot first held a copy of the first plug's material, whose
    bake texture the re-bake path then deleted as "the one it replaces" (it now takes the slot's
    recorded original, a fresh bake with its own record for Remove); and writing `materialSlot`
    would have switched off multi-slot and multi-mesh baking on every later re-bake. It also showed
    Remove un-baking EVERY plug on a mesh (pre-existing, for plugs in different slots too): it took
    every baked slot, the mesh's toggle and its size wiring. Remove now leaves any slot another plug
    on the mesh still bends with (handing it the original if the record was the removed plug's),
    and the toggle and wiring while any plug is left; the plug that split puts back the mesh from
    before (`YapsPlug.splitFrom`) while its split is still the last one made on it, so removing
    every plug leaves the avatar on its own mesh, not on a generated copy. Split files are keyed by
    renderer and plug, so a third plug's split never deletes the mesh the second one goes back to.
    Probe: split, a bake each, re-bakes split nothing more, a third plug on the first shaft still
    takes its slot over, Remove one at a time down to the original mesh and material.
    - **The subset then found the converter half still broken**: the split slot's generated
      material is keyed by source material and renderer (`YapsBaker.Tail`), the same for both ears,
      so the second bake loaded the first one's material by path and took it over again.
      `YapsBaker.Apply` takes a key now, the plug's `PlaceKey`, for a split slot only. The probe
      had missed it because Standard cannot be patched and every bake fell back to a Simple Lit
      copy of its own; it now runs three shaders: a plain one it writes itself (foreign, patched),
      the project's Poiyomi (already patched in place) and YAPS Simple Lit (native, baked in place).
      The native case needed a copy with no bake yet, since nothing records an original, and
      Remove now also keeps a slot whose triangles draw a remaining plug's shaft (a slot taken over
      in place has neither a record nor a readout source).
    - **And a pre-existing one, shipped: a shaft with several plug components lost its look.** The
      second component re-patched the first one's patched material, the patcher refused it, and it
      fell back to YAPS Simple Lit. On the 3-plug avatar the shaft rendered Simple Lit instead of
      its Poiyomi material; on the 2-ear avatar the whole accessory mesh did, which is also why four
      of its toggles (dissolving accessory tiles) were stripped as animating nothing. The converter
      now patches such a slot from what it replaced, and records that original for the swap
      repointing. Subset: both ears on their own material and bake (5 submeshes for 5 slots, 2
      readouts), the four toggles back, the 3-plug shaft back on Poiyomi, two "wears YAPS Simple
      Lit now" approximations gone.
    - The weigh pass no longer offers two baked plug materials as a "share a shader" merge
      (`YapsMarks.IsBakedMaterial`): the split exists because they cannot be one material.
    Battery and probe green on all of it. **Corpus 410 (2026-09-24, full, against the 409
    baseline): 8 of 84 changed, all explained.** Six avatars lose a "share a shader" set (baked
    plug materials no longer offered as a merge), the 3-plug avatar loses its two Simple Lit
    approximations, and the 2-ear avatar shows the split and its four toggles back. Nothing else
    moved. Accepted as the YAPS baseline on Joe's word the same morning (409 kept in
    `Baseline-pre-410-20260924`); corpus 411, throttled, runs against it. Still unproven in game:
    both ears bending at once.
  - **Known limit, not fixed:** plugs on one mesh share ONE deform toggle. `material._YAPS_Enabled`
    reaches every slot of a renderer and `material[n]._X` binds nothing, so there is no per-slot
    switch without material swaps; the entry is named for whichever plug baked last. Same before
    the split for plugs in different slots.
  - ~~Also beside it: the sync check counts a parameter that STARTS under 3200 bits as fitting, so
    an avatar at 3230/3200 gets a warning, not the error.~~ **WITHDRAWN 2026-09-23: the client does
    the same.** `AvatarAnimatorManager.CreateParameterDefinition` (decompiled, beta client) adds a
    synced parameter when `AASBitUsage < 3200`, before adding its 32 bits, in the animator's
    parameter order, so a float starting at 3199 syncs and 3231 is the real ceiling. The one
    difference left: `IsSynced` also skips parameters an animation curve controls
    (`IsParameterControlledByCurve`), which the check still counts, so it can only overcount.
- **The readout is optional, Joe 2026-09-23.** An *In-game readout* tick on every plug (applies on
  re-bake) and socket (applies at once when built), in the See it work card. With nothing left
  carrying one, `Menu` now also drops the `YAPS/Readout` parameter: a controller parameter syncs
  whether the menu names it or not, so before this the bit outlived the toggle. `ReadoutProbe`
  turns every tick off (no readout, layer, parameter or row) and back on.

*4.6.6 shipped 2026-10-02, overnight on Joe's word: tag `v4.6.6`, merge `4213ee1`, both packages
published (not a draft) and extracted (public: no `Editor/Yaps`, `Runtime` or `Dev`; add-on: the
4.6.5 file list exactly, no converter). In it: native toggles kept in the menu (issue #8, reply
posted, left open for the reporter), Unity's controller warning no longer counted as a VRCFury
failure, the stereo patch reading root-written includes and renaming a no-space header, the one-eye
warning no longer flagging locked Poiyomi 7.3, and the rider re-patch. Corpus 412 clean and
accepted as the YAPS baseline. Not worn in game yet.*

*4.6.5 shipped 2026-09-26: tag `v4.6.5`, merge `a744e6c`, both packages published and extracted
(public: the 4.6.4 file list; add-on: the 4.6.4 file list plus `YapsSlotSplit.cs`, no converter).
In it: a slot of its own for each plug sharing a mesh, patching a re-patched slot from its
original instead of falling back to Simple Lit, Remove leaving the other plugs on a mesh alone,
the weigh pass leaving baked materials out, and the Toolkit socket toggles, plus a README tidy
against the window. Corpus 410 is the baseline and 411 (throttled, 2026-09-24) was clean, but
both predate the toggle fix, which was checked by `SocketToggleProbe`, the battery and Joe's own
avatar. Not worn in game yet.*

*4.6.4 shipped 2026-09-23: tag `v4.6.4`, merge `d3f891c`, both packages published and extracted
(public: no `Editor/Yaps`, `Runtime` or `Dev`; add-on: the 4.6.2 file list plus the socket readout
shader, no converter). Corpus 408 was not clean and was not rerun in full on Joe's word; the fixes
it led to were checked on the two avatars it named, the probe, the tester, held shapes, smoke and
`check-defines.sh`. Corpus 409 (2026-09-23, full, on the shipped code) was the owed run: every
change against the old baseline was a known 4.6.4 family, warnings and errors only went down, no
new stuck toggle, so it is the YAPS baseline now (the old one in `Baseline-pre-409-20260924`).*

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

   **Compiled 2026-10-15, both theories that need no headset are out.** Poiyomi 8.1, 9.3 and 10.0
   Toon, original and YAPS-patched, every pass's vertex stage in the plain and the
   `STEREO_INSTANCING_ON` variant for D3D11: all compile, so the VR copy is not magenta or
   fallen back. In 9.3 and 10.0 the patched EarlyZ, Base and Add vertex programs come out the same
   size to the byte (816058 plain, 816894 stereo) with one YAPS block each, so the prepass deforms
   exactly as the base pass does. EarlyZ also only exists unlocked or with *Early Z* on: it defaults
   off and `//ifex _RenderingEarlyZEnabled==0` strips it at lock. Fragment stages were not compiled
   (a minute per pass); YAPS does not edit them. What is left is runtime and only shows in a
   headset, so it needs the user: desktop and VR screenshots of the same plug, whether the material
   was locked before converting, and which Poiyomi features it uses (matcap, rim and specular all
   read the normal YAPS rewrites against a per-eye view direction).

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

   **The ceiling is CLOSED ON DEV 2026-10-15, at the detection point, not by a count.** A plug
   material that reaches `YapsLegacyMap.Detect` as SPS with no `_SPS_Enabled` on it can only mean
   VRCFury's deform got into the shader: the bake was asked not to patch, so this is an avatar
   VRCFury already built, a VRCFury that could not be asked, or a renamed class. Both builders now
   say so, once per material, from one constant (`YapsLegacyMap.SpsPrebuilt`): a warning in the
   converter, a note in the toolkit's upgrade. It catches the renamed class without counting
   anything. README Troubleshooting "a plug bends twice or stretches" quotes the title. None of 31
   SPS-baked materials in the corpus project carries `_SPS_Enabled`, so `SwitchOffLegacyDeform`'s
   comment now says the SPS switch is for older bakes. `Dev/Probes/SpsPrebuiltProbe.cs` checks the
   warning fires on a built copy and stays silent on the original. Passes 2026-10-16 against
   VRCFury 1.1408.0: one material on the built copy, warned once; the original silent, and VRCFury
   asked not to patch it. VRCFury logs a burst of its own `ErrorDialogBoundary` stack lines during
   the bake; the copy builds anyway, so they are its noise. The properties that bake left on the
   material, read from the output: `_SPS_Configured`, `_SPS_IdLow`/`_SPS_IdHigh`,
   `_SPS_PlayerIdLow`/`_SPS_PlayerIdHigh`, `_SPS_BlendshapeVertCount`, `_SPS_BlendshapeCount`,
   `_SPS_Blendshape0` to `15`, `_SPS_Bake`, `_SPS_Overrun`, `_SPS_DisableShadows`,
   `_SPS_DisableDepth`. Not `_SPS_Enabled`.

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
   Avatar J's two variants (16 and 15) and Avatar I (13). The ten extra since the entry was written
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
    the next advisor run: Base applied on 8 avatars, down from 29 (the 5 Avatar D scenes and the 3
    mixed Avatar K/Avatar L).
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

## An avatar floated, and folded up after full-body calibration. FIXED ON DEV 2026-10-11, works in game

Reconverted and reported working by Joe, 2026-10-14. Desktop and FBT were not confirmed separately.

Field report 2026-10-11: feet off the floor on desktop, the body collapsing after FBT calibration,
the hips looking wrong; on the floor in edit mode, floating in editor Play mode too. The armature
was scaled about 9x and moved in the scene after the FBX's humanoid rig was made, so the rig's
recorded skeleton put the hips at a different height from the body. `RigSkeletonSync` (before
`JawUnmapper`) compares the recorded hips position, composed through the skeleton entries of
every object above the hips, with the scene's; past 1% of the hips height it rewrites those
entries and the hips position from the scene and rebuilds with `BuildHumanAvatar`, saved as
`<rig>_Matched.asset`. Limb rotations keep the recorded T-pose.
Test: reconvert, press Play (feet on the floor), then desktop and FBT in game. The source rig
was not on this machine, so the report's numbers are the first reading of how far off it was.
Open: a rig asset that the jaw pass also rebuilds leaves the `_Matched` copy unused in the output.

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
else; the three unchanged had nothing to switch off (Avatar M's scene has all seven Animators at
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

**2026-10-14 (overnight), branch `physbone-grouping-2`**, the old branch merged onto dev and
reviewed twice. Also held out now: a chain whose own object, root or PhysBone the animator
switches by itself (a cloth has one enabled flag, so one strand's toggle stopped the whole
group), and any chain whose bones differ in length by more than 5% each (gravity and the swing
bound are solved from the path one cloth bends, and the merge took them from the longest
strand). Fixed with it: the rider check before asserting a grouped cloth off reads every
strand's riders, the mirrored-off count counts the cloth once, the pose-ratio grounding asks
whether ANY strand of a cloth is animated, the store description counts chains, not cloths.
A group shared one collision radius (the mesh fit pooled every strand), so strands of the same
bones but different mesh thickness collided at one size. Held out since (below).

**Untested against the avatar that reported it.** The variant to hand is the Quest one, 8 chains,
four of them anchored volumes, none groupable. The reporter has been asked which avatar it is.
Merge to `dev` only once the PC avatar converts and the result is checked in game.

**2026-10-14 (overnight): branch `physbone-grouping-2`**, the old branch merged onto dev,
reviewed twice and fixed (its own Unfinished record lists what). New hold-outs: a chain the
animator switches by itself (one cloth has one enabled flag), and bones differing in length by
more than 5% each (gravity and the swing bound are solved from one strand's path). Measured, not
worn:
- Corpus subset of four avatars against dev: cloths 112 to 71 on the one built a strand at a
  time, 148 to 146, 50 to 48 and 49 to 48 on the others (mirrored pairs: ears, long hair, hat
  ornaments). Toggle sweep identical on all four; warnings identical; converted and approximated
  item counts drop because a grouped cloth reports once.
- Physics A/B on the 112-chain avatar: single strands unchanged; grouped strands' free motion a
  wash (mean peak error 33.80 to 33.65, sag 0.92 to 0.90), collision level or better (press
  angle 58.7 to 53.1, recover 0.547 to 0.534). The first A/B said collision got worse; that was
  the probe pressing every strand of a grouped cloth at once, fixed in the probe the same night.
- **Mesh thickness, 2026-10-15.** Each strand's own mesh radius is measured before the merge, and
  a strand groups only with a head within a quarter of its size (no mesh only with no mesh). A
  trace on the two avatars it changed found real spread: one hairstyle's strands measured 3.8 to
  15.6 mm, and a mirrored pair 9.5 and 22.7 mm, which the pooled fit had given one size.
  Cloths 71 to 84 on the strand-at-a-time avatar (dev 112), 48 to 49 on one other, the rest
  unchanged. Physics A/B: the split strands return to dev's per-strand radius, so press angle on
  them moves 53.1 (pooled) to 57.0, dev 58.7; free motion a wash; singles unchanged. The pooled
  radius pressed closer on this avatar by luck of the average, not because grouping knew better:
  the fit's own accuracy per strand is the mesh fit's question, not the merge's. The spacing cap
  needs no hold-out, since the 5% segment rule already keeps grouped strands' spacing within 5%.
  Its own commit, so it can be dropped if the cloth count matters more than the radius.
  Open from it: a mirrored pair measuring 2.4x apart is worth a look at what the fit samples there.
- **Merged into dev 2026-10-16**, held local until the full corpus run that follows it is read:
  that run is the corpus run Joe's 5.0.0 decision asks for. One conflict, the Multi Child report
  line, which now carries both the group's label and the soft-body wording. Still not worn, and
  still not checked against the reporter's PC avatar.

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

## PhysBone parity, round two: the overnight of 2026-10-06. ON DEV, measured, not in game

**Result (run n6 against the 4.7.2 baseline, median error copy vs original):** walk swing 1.04 to
0.30, turn 1.38 to 0.40, head turn 1.40 to 0.40, jump 1.78 to 0.42, lying hang 2.7 to 1.1 degrees,
rest sag 0.42 to 0.19. Worse: quick shake 0.52 to 0.77 (scatter, little bias). "stretch" turned out
to be the root-to-tip distance, which a curved chain straightening also raises, so it cannot judge
squish; only the press test on squishing chains can, and the test set has two.

**A regression the measurements caught and fixed:** removing the restoration floor (fix 3) was right
for hanging chains and wrong for soft bodies, whose PhysBones pull weakly because they hold the body
in other ways: copies swung 100 to 180 degrees on a walk against 8 in the original, which is the
flailing and clipping Joe saw live. Soft bodies keep the preset's root restoration at both ends (a
per-end taper still left 50 degrees); now 21 against 8. Multi-child re-rooting skips soft bodies
(their root is sprung, not pinned). Squish touches only squishing sources: rigid bone length for
everyone swung chains measurably less (distance stiffness also resists bending).

**Soft-body collision option:** first version (spheres at full volume) made things worse, two
neighbours overlapping at rest and shoving every frame; spheres sized to the gap were then all
skipped, because the bodies' own volume radii already overlap. Final: with the option on, each soft
body is shrunk to 45% of the way to its neighbour and its sphere matches; measured neutral to
slightly better (soft walk 20.7 to 18.8, sag 0.19 to 0.27). Off by default as decided.

**Corpus (the one authorised run, label physics-1007): PARTIAL, not accepted.** Unity died natively
(SIGSEGV in `Animator.Rebind`, called from ToggleSweep.Drive) on avatar 14 of 87, Avatar G. The same
avatar alone, through the corpus settings and the sweep (`Dev/Probes/SweepCrashProbe.cs`), converts
and sweeps cleanly with the baseline's result (12 stuck + 5 refused), so it is not that avatar and not
deterministic; likely state accumulated over a long session. The 13 digests it wrote differ from the
baseline only as expected: the new setting line, report counts, hand capsules turned spheres, Multi
Child Ignore roots listed as each branch's first bone, and one fewer stuck toggle on one avatar's three scenes.
**A full run needs Joe's word again** (one authorisation, one run).

**Soft-body squish, 2026-10-07 (Joe asked: do they actually squish, like marshmallow PB?).** Not
yet. The press test shows originals giving way 15 to 48 degrees or shortening where the copies barely
react: a Bone Spring collides on one bone per side only, and squish is not mapped for soft bodies.
Marshmallow PB could not be measured: both avatars carrying it in the corpus project (Avatar N and
another) have VRCFury setups referencing Dismay PCS generated files that project lacks, so Fury
refuses to build them and marshmallow's physics never exists there, original or converted. Needs a
project where they build. Two fixes found on the way: `ModularAvatarBaker` passed NDMF a null platform,
which resolves to NDMF's ambient default (Generic, not VRChat), now asks for VRChat by name; the
probe's originals were being rebuilt by VRCFury's play-mode build (it builds every avatar on Play,
NDMF plugins included), now skipped with Av3Emulator's "(ShadowClone)" name marker, and the probe
bakes the original before converting.

Measured since (2026-10-07, `-abForceSquish 0.5` on Avatar B and Avatar A, original and copy both
squishing): **Bone Cloth for a squishing soft body is WITHDRAWN.** It shortens about right (a butt at
0.55 of its length against the original's 0.59) but bends 70 to 140 degrees away from a press where
the original bends 18: median press-angle error 59 degrees, against 10 as Bone Spring. Bone Spring
stays. What a real fix needs: Bone Spring collision on more than one bone, or Bone Cloth held far
stiffer near rest than the restoration floor manages; neither tried.

Marshmallow PB, once built (`-abStripBrokenFury` removes the one broken PCS component on the open
scene): it is a stack of 18 PhysBones per avatar (PhysBone, Inertia, Gravity, Buffer, Squish,
Gravity_Squish and Collision chains per side, Max Squish 0.3 to 1) under `cake_PB_Setup/Dummy(for PB
count)`, and in a VRCFury test copy that whole tree is still disabled with nothing on the breast
bones. Unknown whether an upload wires it further; Joe is checking in Play mode. If it is layered
chains combined through constraints, converting each layer as its own cloth cannot reproduce it:
it would need recognising and converting as a whole. Also: after a VRCFury build Debug.Log is
silent for the rest of that call, so the probe writes `bake.txt` and `physbones.txt` instead.

**Soft-body root slide, FIXED 2026-10-07.** Bone Spring lets the root bone slide within
`springConstraint.limitDistance`; the preset's 5 cm moved a breast root by a third of its length on
a walk (new probe metric `slide`, root position in the parent's space over chain length), dragging
straps weighted to it off the body (Joe's screenshot). A quarter-radius cap left 8.5%; 0 leaves 2%
and the swing angles matched as well or better. Now 0.

**Marshmallow stand-in, 2026-10-07 (Joe: "the squish is less obvious on magicacloth").** Measured
with the original baked (the probe now renames its baked copy, which the converter's own VRCFury
bake used to destroy) and a squeeze phase (two palm-sized balls pushing the pair together): marshmallow
moves the bone itself (root slide 1.5 chain lengths) and bends it 10 degrees, the stand-in pinned its
root and bent 31 to 104. Stand-ins (`PhysBoneChainData.StandIn`, set by HelperRigCleanup) now keep the
root slide, widened to the body's length, collide at the root too, and hold restoration at 1. Butt.L:
pressed bend 40 to 12 (marshmallow 5.5), squeeze bend 31 to 14 (10), jump 104 to 35 (12). Still
swingier than marshmallow on jumps and shakes; marshmallow's contact-driven squish (animator
parameters moving its colliders) needs the VRChat layers running, which batch Play mode does not.
Also fixed in the probe: press colliders sat on the scaled ball, so both solvers saw them far
smaller than drawn; every press result before 2026-10-07 used undersized balls.

**Marshmallow rig kept, chain by chain: TRIED 2026-10-07, STOPPED, non-deterministic.** Converting
each cake_PB helper chain to its own cloth and keeping its constraints (dev switch, now removed; the
whole experiment is `Regression/KeptRigExperiment-2026-10-07.patch`). Found and FIXED on the way:
`AlignLocalSpaceRelays` moved Neutral_L1 (a local-space relay carrying every later stage) out of the
Gravity chain, putting the butt 13 cm out of place at rest; a bone with children under a simulated
parent now stays put and a fixed copy of a static source drives it (4.6 cm). Then: the same build run
three times put Butt.L 1.0, 3.3 and 11.7 cm out of place at rest, so no fix could be told from noise.
Likely cause, unproven: MagicaCloth2 solves cloths in parallel with no order, and the cascade feeds
each stage from the last. Wrong guesses this round, withdrawn: Buffer held by inside-bounds spheres
(its limits are ordinary colliders), Buffer resting pressed into them (it is not). Also in the patch,
unmeasured: inside-bounds colliders as a distance bound. Next attempt starts from the variance.

**Still open: a belly flailing.** Avatar B's belly peaks at 160 to 170 degrees on the walk where the
original moves 3, and slides 0.36 even at limitDistance 0. Not investigated.

**GrabbyBones:** research and a fork plan, private in `Regression/GrabbyBonesForkPlan.md` (it leans on
the client decompile). GPL-3.0; the author has pose on his own roadmap and merges outside PRs, so
the plan recommends an upstream PR first and a fork only if declined.

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

**Stretch past 3%, three routes measured (2026-10-10, `Dev/Probes/Perf/SoftBodyPerf`, RTX 4080 /
9800X3D, batch, throttled; nothing here run in game).** MagicaCloth2 itself does shorten and
lengthen bones (BoneCloth writes each moving bone's local position, `VirtualMeshManager.cs:1185`),
but its tether caps stretch at a compiled-in 3% (`TetherStretchLimit`), which no avatar can change.

- **Shader dent: free.** Four marker-slot dents per vertex measured below the noise (floor 0.052 ns a
  vertex, dent +0.00). Under 3 us GPU for a 60k-vertex body in two passes and a mirror. Its cost is
  the shader patch and the transport, not the math. Dents only where a marker is.
- **GPU feedback (camera to 1x1 texture to parser): dead on cost.** 76 to 79 us CPU per camera at 1
  to 10, 216 us each at 40 (8.6 ms a frame). It is also gated by each viewer's Cameras filter.
  The parser itself is cheap: one double-buffered AsyncGPUReadback per texture per frame, about 6 us
  to request (client decompile), 8 bits a channel.
- **PhysX joints: open and cheap.** Rigidbody and ConfigurableJoint are on the avatar whitelist.
  Five chains of three an avatar at 90 Hz: 0.21 ms step for one avatar, 0.79 ms for forty (about
  15 us each past the first). p99 spiked to 5.9 ms, unexplained (throttled run). Bones genuinely
  ranged 0.54 to 1.60 of rest. Unknown in game: ChilloutVR's fixed step, which layers avatar
  bodies collide with, a teleport snapping the joints, whether a content filter strips them, and
  how a body follows an animated parent (a dynamic body under a moving bone is teleported with
  it). Next is an in-game spike: one chain on one avatar.
- **Joint spike built (`Dev/Probes/JointSpike.cs`, menu Tools > AvatarBridge Dev > Joint spike).** Each
  bone follows its own body through a parent constraint; the bodies sit under the avatar root, no
  colliders. Self-test (batch, script-stepped at 90 Hz): under the avatar root the tip strays 5.1 cm
  walking and 12.1 cm on a jump from its settled sag, 0.2 cm after a 100 m teleport, bones 0.87 to
  1.46 of rest. Under the hips the same chain strays 0.2 cm everywhere: a dynamic body under a moving
  parent is teleported with it, confirmed. Settled sag 6.9 cm, untuned. The client decompile shows
  Rigidbody and joints pass the avatar filter untouched; only colliders are processed (content
  filters, layers, a kinematic body added). Awaiting Joe's upload.
- **Layered on MagicaCloth2: works in batch Play mode, 2026-10-10.** The first build drove a copy of
  the chain with the cloth and had the real bones follow it. Dead by design: MC2 restores its bones
  to rest in EarlyUpdate, before physics and constraints, so nothing outside MC2 ever read the
  simulated pose. (It also left BoneSpring's `collisionBones` on the real bones, so the copy collided
  nothing.) Now MC2 stays on the real bones at Animation Pose Ratio 1; each bone below the root is
  position-constrained to a lag body (sphere collider, no gravity) chasing a kinematic target at its
  rest place under the chain's parent. Constraints run before MC2 reads the pose, so MC2 settles to
  the pushed pose and adds its swing on top. Probe, a soft chest chain against its plain-MC2 twin,
  press ratio 0.85 vs 0.92 (PhysBones 0.75), squeeze bend 23° vs 8° (51°), stretch 1.12 vs 1.05.
  Self-test without the cloth: tip body pushed 30% of a bone holds it at 0.90, rebounds to 1.02.
  Open: the squash needs a **physics** collider to reach the bodies; whether ChilloutVR gives hands
  one that touches an avatar's bodies is the in-game question. `Longest` follows the deepest branch,
  so a nested chain (a piercing) can take the bodies instead of the soft body's own tip. Also fixed:
  bodies now never sleep, since a kinematic target moved by its transform does not wake them.
  Sizing the bodies to the flesh (tried 2026-10-10) moved each part as one block and stretched it
  1.4 to 1.6 of rest; reverted to half a bone. Squish is the shader's job, stretch the joints'.
- **Three layers, 2026-10-11: MagicaCloth2 swings, joints stretch, the YAPS dent squishes.** Add-on
  only (squish is 18+ content, Joe's call). `Editor/Yaps/yaps_dent.cginc`, patched into every YAPS
  shader, gated by `_YAPS_DentPower` (default 0, so nothing changes until something turns it on):
  - Up to four spheres push skin out onto their surface, stopping 20% of the depth short so a top
    and the skin under it keep their order (pushed onto one surface they z-fought in stripes).
  - The displaced flesh rises in a ring round each press, its slope capped at 0.5: steeper, the
    skin under a top rose past it and showed through.
  - A soft pair pressed together gives only on the facing side: a band four press depths deep is
    pushed back to the plane where the two meet, and the skin round the seam spills out away from
    the line between the parts, at most a press, fading in over half a reach of that line
    (`_YAPS_DentSpill`, in press depths, default 1). The far side never moves. Spilled along each
    vertex's own facing instead, skin and the top over it faced slightly apart, spilled apart by
    centimetres at a deep press, and the skin showed through the top in blotches.
  - Which part a vertex belongs to is its skin weight to that part's bones, baked per renderer
    into a point texture read by vertex id (`_YAPS_DentOwn`). Guessed from position and facing,
    neighbours went different ways and tore. Every renderer over the skin (the renderer carrying
    the most of the parts) takes the share of the nearest skin vertex within 3 cm instead of its
    own: a top weighted less to the breasts than the skin squashed less, and the skin came through.
  - Each part's centre moves with every bone carrying it, by weight. On its main bone alone, a part
    carried mostly by a chain root the joints never move stood still.
  - How far each part reaches toward its partner is the 98th percentile of its own vertices along
    the line between them, every frame. The covariance estimate said 15 cm where breasts touching
    at rest reached 21, and the squash barely began (1.2 cm of press, now 11).
  - Withdrawn on the way, kept so nobody retries them: carving the overlap away (two crescents,
    visibly losing volume); squashing the whole part toward its centre (the far side moved in, the
    pair still slid through); growing the cross-section by one over the root of the compression,
    about the line between the parts (a broad inner face sits far from that line and was thrown two
    to three times its distance, the blow-up of 2026-10-11).
  - Probe only: flesh-sized frictionless spheres on each partner's lag body, 75% of the reach,
    colliding with nothing but each other. They halve how far a squeeze drives the bodies through
    each other (23 cm to 9); two kinematic presses still force them. `-abSqueezeDepth` sets how far
    the probe's squeeze goes (1 drives the pair a whole gap into each other, past any hand).
  - Verified in batch Play mode on one avatar (`PhysicsAbProbe -abDent -abNoSpheres
    -abSqueezeDepth 0.5`): the faces flatten against each other and spill. The original's
    PhysBones under the same squeeze pass one breast entirely in front of the other, which ours
    no longer does; the drift that is left comes from the probe's press geometry. Not seen in game.
  Open: the transport. The probe sets world-space globals; in game the spheres must come from hands
  and other avatars (vertex lights, the atlas, or a trigger position), and the pair plane from the
  wearer's own flesh centres, reaches and press (the probe computes them in C# from the skin). The
  hand dent is off in the probe (`-abNoSpheres`): a probe-sized ball carved cups, a palm-sized one is
  untried. Also: own-body spheres need a mask, and the patcher's new properties
  change every patched shader's name hash, so existing YAPS materials re-patch on the next convert.

- **Bone-scale squash, 2026-10-11: supersedes the shader pair squash above.** The soft pair is
  squashed by scaling bones, not by moving vertices, so skin and every layer over it share one
  deformation and cannot tear or show through by construction. No shader transport needed.
  - Each carrying bone's slot in every renderer's bone list is swapped for a skin bone in a stack:
    follow (part centre) > aim (at the partner) > scale > counter (undoes the aim) > skin bones.
    The stack is identity at rest, so bind poses are untouched.
  - The scale flattens along the pair line and overfills across it, the far side held still by a
    pivot shift: along = clamp(1 - press / (2 reach), 0.6, 1), across = min(1 + 2.33 (1 - along),
    1.5). The overfill reads as full rather than shrunk (`-abOverfill`, `-abFlattest`).
  - Built from constraints only, so it can ship to ChilloutVR: the follow is a ParentConstraint on
    the part's TOP carrying bone (on the main bone, a chain root's skin was dragged 16 cm by its
    child's swing), an AimConstraint, a RotationConstraint counter, and per skin bone a
    RotationConstraint to its bone plus a PositionConstraint below the top (all-static offsets
    lost the joint stretch, 16.7 cm off). Constraints solve in world space, so the parent's squash
    cannot reach a child's moving offset: measured 6.5 cm off the exact stack at full squash on
    one avatar, 7.9 cm on another, under 0.3 cm where the press is small. Looks the same on film.
  - Probe: `PhysicsAbProbe -abSquash`. Verified in batch Play mode on three avatars, breasts and
    butt. Not seen in game.
  - Driver, built and run in the probe (`-abDriver`): a CCK box trigger on one part's aim
    (SetFromPosition, sampled on -Z, exit task writes 0) reads a pointer on the partner's
    follow, so the box's z runs down the line between them. One synced float per pair: 0 where
    the surfaces meet at rest, 1 with the centres together. A generated 1D blend tree keyed at
    the squash's bends (6 keys) sets both parts' scale and pivot, so no C# touches the squash;
    only the client's box read is emulated. Matches the C#-driven squash on film (flattest 0.85
    against 0.86). The press comes from rest reaches, so it reads up to 9 cm off the press
    measured from the skin each frame; not visible on film.
  - In game, 2026-10-11, driven by the trigger: another player pushing the pair together
    squashed it and looked right; the menu slider at 30 looked right, at 100 (centres together,
    the maximum) looked wrong with no partner pressing. But it cost a synced float per pair,
    and it only knew pushed-together: no touch, no squash against the body, no gravity.
  - Zero-sync reader, built and run in the probe (`-abReader`), replaces the trigger: a camera on
    one part's aim (orthographic, vertex lit so no shadow maps, near plane 0.00123) renders a
    quad on the partner's follow into a 4x4 linear texture. The quad's shader draws only for
    that camera (orthographic, that near plane, facing it within 25 degrees) and writes both
    parts' along and across into one pixel's four channels; a `CVRTexturePropertyParser` sets
    each scale node's localScale and pivot from them (4 tasks per part). Every client measures
    its own copy against its own cloth, so nothing syncs. Probe emulates the client's parser
    from its decompile (8-bit read, lerp, reflection): within 0.0017 of the exact squash (one
    8-bit step) on Avatar A, breasts and butt, and the film matches the trigger version frame for
    frame. The client reads a frame or two late; the probe reads the same frame.
  - In-game test rig: `Dev/Probes/SquashTestBuilder.cs` (menu Tools/YAPS/Dev/Add the squash
    test, or batch) puts the reader squash on a converted avatar, measured at rest in edit mode,
    assets in `Assets/AvatarBridgeOutput/SquashTest`. A 4 cm cube in front of the chest shows the
    texture: black at rest, toward yellow when squeezed (magenta/green in the first upload). Pairs are found by name: skinned bones
    naming a soft part AND a side, so a shared root both parts hang from is skipped.
  - In game, 2026-10-11, the reader WORKS for the wearer: the cube went magenta at rest and green
    squeezed, and the pair squashed, with nothing synced. Once the cube showed black pixels, which
    the shader cannot write: something else in the camera's box drew after the quad. The box was
    far too big (half height = the press start, far plane 20 times it); now a quarter of the start
    by 2.5 times it, and the quad draws in the last queue (Overlay+1000).
    Joe could also turn the whole cube black. Black read as fully flat and no wider, a visible
    glitch, so the encoding is inverted: black is rest, and whatever draws over the pixel black
    now reads as no squash. Re-run in the probe: within 0.0017 of the exact squash on all four
    parts.
  - Camera-dependent failure in game: the squash works in first person and in a far third-person
    camera, and stops (readout black, so the reader saw no quad) at a middle third-person
    distance; the mirror follows the third-person state, and remote viewers saw it change with
    distance. The client switches the local avatar's head-hidden clone per camera
    (`AvatarClone`, `PlayerCloneDistanceDisabler`: within 0.8 m times the play-space scale of
    the head), a suspect but not shown: the head stays visible in both states, and MeshRenderers
    like the quad are not cloned. A diagnostic build ("Add the squash test (diagnostic)") draws
    the quad for any 4x4 target and fills the readout with the reader's checks (orthographic,
    near plane, facing, a blink showing the camera renders, the distance), the clear colour
    blue; the squash still reads pixel (0,0).
  - Reader faults found reading the code (2026-10-11): part b's widening rides alpha, which the
    readout cube cannot show and which any opaque draw sets to 1, so "black cube, one part
    swollen" is a foreign write, not rest. The camera's cullingMask is Everything, so the world,
    other players and the wearer's arms in the box draw into the texture on any frame the quad
    misses (facing over 26 degrees, or b outside the box while the aim lags). Fixes: no value in
    alpha, one row per part (r flattening, g widening), and a cullingMask of the avatar layers.
    Also suspected: only the lower skin bone is pinned, so the squash slides each part's base
    14-15 cm instead of shaping it about its joint; one editor render (scale b set by hand)
    decides it before changing anything.
  - Diagnostic results in game (2026-10-11): every readable cube showed all checks green, the
    distance mid grey and the value at rest, so the reader works when it runs. The failure is
    view-dependent, not distance: with the client's Self Portrait on, some views turn the readout
    all black (no blue, no checks, no blink, so something opaque covered the whole texture after
    the clear) while one part swells, which is b's widening reading the opaque alpha. Turning Self
    Portrait off changes it. Self Portrait is a plain camera half a metre in front of the head,
    turned back at it, clear colour transparent black, depth texture on. Fix 1 done: the value is
    off alpha, pixel (0,0) a and (1,0) b, r flattening and g widening, filling the top and bottom
    rows so a flipped target still reads; diagnostic cells moved to the middle rows. The probe
    gains `-abPortrait` (a Self Portrait stand-in swept 90 degrees either side) and counts reads
    with any alpha as foreign draws. Run on Avatar A: within 0.0017 of the exact squash on all four parts,
    and 0 foreign draws in 6302 reads both without and with the stand-in (logged as rendering on
    the head). So a camera alone does not reproduce it; what Self Portrait changes in game is still
    unknown, and needs the new diagnostic build in game.
  - Second round in game: fix 1 holds (no swell when the cube goes dark), but Self Portrait at
    some angles still swells one part. The diagnostic cube showed the reader's own cells at rest
    (checks green, distance mid grey, top value row black) while the bottom value row, the one
    the parser reads, held pure green in a's cells: full widening with no flattening, which the
    shader cannot produce. So something draws over the texture after the quad. Queue 5000 and
    ZTest Always did not guarantee last: transparent draws sort by sorting layer and order in
    layer before queue, and the camera's mask was Everything, so UI crossing the reader's box
    lands on top. Fix 2 done, verified in game 2026-10-11 (Self Portrait no longer swells, the far angle reads): the quad's order in layer is the maximum, the
    camera renders the player layers only (8 local, 10 remote), and diagnostic value cells carry
    blue so a foreign write shows as pure green. Unproven which object it was. Not covered: a
    later sorting layer, or a command buffer drawing after the camera.  - Target architecture (design study 2026-10-11, all costs ASSUMED until measured): the whole
    Marshmallow set, zero sync. No always-on camera: a CVRRenderController running a
    CVRMaterialDataProvider (bone matrices) into a self-feedback CVRBlitter computes pair
    contact, own arms and thighs as capsules, other players' hands from the client's
    `_CVR_Player*Positions` shader globals, and a spring with velocity kept across frames. Two
    small depth views (chest, pelvis) through CVRCameraHelper replacement shaders sit disabled
    and are switched on by a parser writing `Camera.enabled` only when something foreign is
    near or the posture says floor or ledge. The Animator's DistanceTo gives a LOD (off past
    about 12 m, faded to rest on the GPU first). Encoding rule: everything that must be 0 at
    rest rides one channel that every no-data case (fresh texture, the parser's opaque black,
    error magenta) decodes to rest. Gates before building: the controller and provider must run
    on remote copies in stable (in-game test), and a replacement shader must draw every body
    shader at its skinned pose (batch probe). Gravity sag stays with MagicaCloth2.
  - Measurement probe, first slice (SquashMeasureProbe, batch Play mode, editor timings, 2026-10-11):
    B2 one tiny camera costs a median 71-90 us at 500-5000 renderers, p90 135-165 us; an empty-layer
    mask saves 10-15%. Under the 100 us gate, so K = 4 holds if the client is no worse than the editor.
    B3 controller work 12 us per copy (gate 25): the 12 m Mid radius holds. B4 the client's parser loop
    replayed: 2 frames from render to applied value 79%, 3 frames 21%, one 13-frame hitch in 300. B8
    decode: fresh texture and the parser's black read rest; the error shader's magenta reads FULL
    flattening (r is 1), white fallbacks read full on both, Standard 0.04. So r is not a safe channel:
    only g survives magenta, and nothing survives white. The reader in game still puts flattening on
    r. B12 FAILS: a LateUpdate blit sees unity_DeltaTime as a constant 0.005, while a camera draw sees
    the real dt (from about 5 ms up). A blitter spring cannot take dt from the shader.
  - Measurement probe, second slice (same day):
    B1 PASS: the nearness replacement draws Standard, Unlit, transparent, cutout, locked Poiyomi
    8.1 and unlocked Poiyomi Pro at the exact depth, and a skinned mesh at its posed depth, not the
    bind pose. No lilToon material in the project, so lilToon is untested. B10: a vertex-displacing
    shader draws undisplaced (A7 holds), lines and TextMesh draw at their real depth, the emitted
    particle did not draw at all (probably the probe, not settled).
    B4 setter 0.05 us per set through object (gate 1 us). B7 PASS: the sandwich at rest is off by
    0.0001 mm at most over 20000 byte pairs.
    B9 PASS once the clear is compensated: the project is linear and a camera's clear colour is
    gamma, so (1/255, 0, 0, 0) as written clears to 0 and the handshake floor vanishes. Written as
    LinearToGammaSpace(1.5/255) (17.6/255) it clears to exactly 1/255. Each camera's clear stays in
    its own rect, and a camera left off reads 0 in its rect the next frame.
    B5 FAILS: 600 Rotation and Position constraints on 20 rigs cost 2.2 ms a frame in
    PreLateUpdate.ConstraintManagerUpdate (25 us with them disabled); 400 would be about 1.5 ms,
    over the 1 ms redesign line even at half for the editor. The Far state disabling them is not
    optional, and the skin chain needs a cheaper form before 20 wearers are realistic.
    Split by form on 20 rigs: cost is linear in components and the same per type, about 3.5 us
    each in the editor (Rotation 1442 us for 400, Parent 1460 us for 400, Rotation 591 for 200,
    282 for 100; none enabled 24). So only the count matters: a ParentConstraint in place of a
    Rotation plus Position pair saves a third, and the 300 us gate for 20 wearers means about 5
    components a wearer, which the current chain is far over.
    B6: the plain child follow and the skin under it have no error at the end of the frame with
    the chest moved 10 cm a frame after every other LateUpdate (gate passes). The ParentConstraint
    follow showed no lag either, against the one-frame lag the design expected, so constraints run
    again somewhere after LateUpdate; measured at end of frame, not at render.
    B11: off screen under CullUpdateTransforms (and CullCompletely) neither a GameObject-active
    nor a Behaviour-enabled curve applies; AlwaysAnimate applies both. So A9 is false: on remote
    copies a toggle written while the wearer is off screen waits until they are seen.
    B12 FAILS outright: in a LateUpdate blit _Time is 0 and unity_DeltaTime the default 0.005, even
    right after a camera rendered; a camera's own draw sees both. Time for the spring has to come
    from elsewhere: a looping linear clip on the Flags transform the provider already reads would
    give a clock (one frame old), else a fixed dt.
    The reader in game moved off r: one value per column on g (a flattening, a widening, b
    flattening, b widening), so the error shader's magenta reads rest. Verified in game 2026-10-11: still squashes, rest unchanged.
    Not run: B13 (the bone-table fallback is only designed if the controller fails G1).
  - Skin chain replaced by the shader, 2026-10-11 (B5 failed: about 3.5 us per constraint, two
    per skin bone). The patched body shader applies the stack itself: each part's counter
    local-to-world after its follow's world-to-local, as one map, weighted by the baked shares
    (`_YAPS_SquashTo`, `_YAPS_SquashFrom` in `yaps_dent.cginc`). In game a
    `CVRMaterialDataProvider` would write both arrays every LateUpdate, locally: no parser, no
    sync, no skin bones. Why this is not the old shader route: the dents moved each vertex by its
    own facing and collapsed layers onto one surface; a map of position alone moves every layer
    at a point together, which is what kept the bones clean. Probe `-abSquash -abAffine`, three
    avatars: Avatar A and Avatar B match the bone films (mean pixel difference at most 1.6 and 0.2 of 255),
    and the bones' 7 to 17 cm stack error is gone. Avatar C (a top over the skin): no skin through
    the top. WITHDRAWN: "the top passes through itself at the cleavage". The slits read as that
    are stitched tears in the top, there at rest. The probe's "passed through" figure is capped
    by its 2 cm search cell (every run reads 2.0), so it measured nothing either.
  - Contact plane on top of the map: each part's skin that crosses the plane where the pair meets
    is eased back onto it (from a tenth of the reach before it, keeping a fifth of the slope so
    layers keep their order) and what is pushed back spills away from the line between the parts
    (`_YAPS_SquashPlane`). The map already moves the facing apex back by the press, so the plane
    adds no press of its own; it only catches what reaches past the reach. Measured as how far
    the skin crosses the plane, both sides added, no radius cap: Avatar A 8.5 cm with the map alone,
    2.3 cm with the plane (`-abNoContact` for the former). On Avatar A's dark shader the film shows
    little; a lighter avatar with a deep press is the visual check still owed.
  - Follows only, 2026-10-11: the shader works the whole squash out from where the parts are.
    `_YAPS_SquashFollow[4]` holds each part's follow (a plain child of its top carrying bone, at
    its centre); three vectors baked per material at build (`_YAPS_SquashReach`,
    `_YAPS_SquashPair`, `_YAPS_SquashShape`) give the rest. The gap gives the press, the press the
    map and the contact plane. `_YAPS_SquashTo`, `_YAPS_SquashFrom` and `_YAPS_SquashPlane` above
    are gone, and no camera, parser, stack or constraint is left in this route. The press is read
    in the first part's units, so both faces meet on one plane even when the two top bones are
    scaled differently. Avatar C, probe feeding the follows: same deepest press as before
    (12.9 cm), skin across the plane by 1.5 cm at most, the top stays over the skin.
    Build for the in-game test: Tools > YAPS > Dev > Add the squash test (shader). It puts a
    `CVRMaterialDataProvider` (TransformLocalToWorld) and its `CVRRenderController` on a child of
    the root (the client finds the wearer from the parent up), patched copies of every layer's
    materials with a share texture per renderer, and strips either earlier build. Batch-built on
    Avatar C: 2 pairs, 9 materials, a built-in shader left alone. The CCK's `LateEventsManager`
    also runs in editor Play mode, so the real provider can be measured in batch (not done yet).
    In game, to answer: does the provider run on remote copies (A2); does the client's material
    take the array (if the client instances renderer materials, the provider writes the asset and
    nothing moves); does it look right.
    Resolved by decompile (beta client): the client instances the provider's materials and the
    renderers' through one per-copy cache (`SharedFilter.ProcessRenderer`,
    `HandleRenderTextureForMaterialDataProvider`), so the provider reaches that copy's renderers
    and two wearers of one avatar never share a material. The four-reach guard stays as a net.
    Next, in order: own hands and forearms as capsules through the same provider; other players'
    hands as spheres from the `_CVR_PlayerLeftHandPositions` / `_CVR_PlayerRightHandPositions`
    globals (slots 1 and up, set every LateUpdate, no camera, no sync); sag from the follow's
    orientation. Depth camera and GPU soft-body only after those hold.
    First upload (Avatar A), 2026-10-11: no squash in game at all. Likely cause, from the build log
    and the probe: the follows sat on the chain roots (Breast_L carries 43%, Breast1_L 55%), and
    the cloth holds a chain root still, so the gap between follows never closed. The probe hid it
    by moving the parts kinematically. Follows now go on the heaviest carrier below the root; the
    diagnostic build adds `_YAPS_SquashPulse`: the pair squeezes and lets go every few seconds
    with nobody moving it, which needs the provider writing the follows.
    VERIFIED in game 2026-10-11 (Avatar A, diagnostic build): it pulses on the wearer's view AND remote
    users see it. A2 is closed: `CVRMaterialDataProvider` runs on remote copies, the client's
    per-copy material instances take the matrices, and the patched shader deforms in the client.
    What is left is the press: nothing in normal play pushes the follows together reliably.
    Press sources, 2026-10-11, NOT YET TESTED in editor or game. `_YAPS_SquashFollow` grows to
    16 slots: 0-3 follows, 4-7 rests (a plain node per part on the bone above its chain, at the
    part's centre, which the cloth never moves), 8-15 the humanoid upper arm, lower arm, hand
    and middle finger per side; empty slots hold a node scaled to zero. Bone push: follow minus
    rest, downward left out so settled sag is not a press, ignored below a share of the reach,
    pressed from the push side and summed with the pair gap into one press per part.
    `_YAPS_SquashPush` holds gain, deadzone, capsule edge and arms on, tweakable live. Own arms:
    a capsule per bone, radius measured from the skin the bone carries
    (`_YAPS_SquashLimbL`/`R`, share of the bone's length); skin inside is eased out radially
    and tops out at half the radius, since a hand meets no resistance and following it scooped
    a tunnel. The old sphere and pair dents, `_YAPS_DentSoftness` and `_YAPS_DentBulge` are gone
    from the shader; the probe's dent and sphere modes now drive nothing and its `-abBulge` sets
    a property that no longer exists. The builder logs any layer whose shadow pass is Unity's
    own caster, which stays unsquashed. Compiled: fxc vs_5_0 (about 1180 slots, most behind
    the early exit for vertices outside the parts) and the editor assembly through Unity's
    Roslyn with the project's references.
    Play check, 2026-10-11 (`Dev/Probes/SquashPlayProbe.cs`, Avatar A, batch): THE PROVIDER NEVER
    SEES THE CLOTH. MagicaCloth2 puts its bones back to rest at EarlyUpdate and simulates after
    the scripts' LateUpdate (`UpdateLocation.AfterLateUpdate`, the default, and the client's
    Assembly-CSharp never changes it); the CCK and the client both run the provider from a plain
    script LateUpdate (`LateEventsManager`). Measured: follow off its rest 0.00 cm every frame
    at script time, up to 0.10 cm at the end of the same frames. So the pair gap and the bone
    push only ever read the animated pose: neither can be driven by cloth through the provider.
    The pulse worked because it is a timer. This is also the likely whole story of the first
    upload showing nothing. Own arms do reach the shader (arms on against off changes the skin
    around the hand and forearm only), but capped at half the radius the hand still sinks in:
    it reads as a carve, not a squash. Next: hand contact as a press into the part's map, so
    the part flattens under the hand. Cloth as a source would need a reader that runs after
    the cloth, e.g. the provider's skinned vertex buffer mode on a helper mesh weighted to the
    follows (one frame late, unproven). Also unknown: whether the client's IK has moved the arm
    bones before the provider reads them.
    Both fixed the same day, editor-verified, NOT YET IN GAME:
    - Skin reader: a helper SkinnedMeshRenderer under the host, one vertex per slot weighted
      fully to it (an empty slot to the host), triangles with no area, `updateWhenOffscreen`,
      read by a second provider in `SkinnedMeshRendererVertexBuffer` mode into
      `ByteAddressBuffer _YAPS_SquashSkin` (shaders targeting 4.5 and up; `_YAPS_SquashHands.w`
      is the stride, 12, and 0 falls back to the matrices). Measured: all 16 slots in the buffer
      equal the bones' end-of-frame world positions, worst 0.00 cm, so the slots now arrive after
      the cloth (and after the IK, in game) one skinning pass late, in world space. The pair gap
      and the bone push can see the cloth again. Open: whether the client runs this mode on
      avatars and remote copies.
    - Hands press the part: each arm's deepest bone (upper arm, forearm or hand) and every other
      player's hands (`_CVR_PlayerLeft/RightHandPositions`, `CVRGlobalParams1.y` players, capped
      at 64, a ball of `_YAPS_SquashHands.y` metres, any hand within 1.5 radii of an own hand
      skipped as the wearer's) press the part toward its side by how far they reach into it,
      summed into the one press. Avatar A, wrist sunk 0.6 reach into the front: the part flattens and
      bulges around the arm instead of the hand sinking in a groove; strong at that depth (the
      pair's shape constants, flattest 0.6 and fullest 1.5, cap it). Gains in
      `_YAPS_SquashHands` x (own) and z (others), live. Two hands from opposite sides cancel in
      the one press vector: a known limit.
    In game 2026-10-11: NOTHING moved, wearer or remote copy, while editor Play mode worked. The
    client's `CVRRenderController.DoRenderLoop` disposes every buffer `GetVertexBuffer()` handed
    out right after `Execute` binds it, before anything draws; the CCK's editor loop never
    disposes. Suspected: the binding dies with it, the shader reads zeros, every slot sits at the
    world origin and every part skips itself as too far away. Changed the same day, NOT YET IN
    GAME: the skin provider uses `SkinnedMeshRendererPreviousVertexBuffer` (the client copies
    into two buffers it owns and binds those, two frames old), and the shader checks the first
    rest slot against its matrix once per vertex and reads the matrices when the buffer is empty
    or wrong. The diagnostic build reports which: the pulse on both pairs means the skinned
    buffer arrives, the first pair alone that it reads empty, the second alone that it reads
    wrong.
    WORKS the same day (Joe): the diagnostic build pulsed in editor Play mode, then in game on
    the wearer. Not yet read off it: whether both pairs pulsed in game (the skinned buffer) or
    the first alone (the matrix fallback, which carries no cloth).
    Viewers with the Cameras content filter off see no squash at all (decompile, `AssetFilter`):
    the filter destroys every Camera, CVRBlitter, CVRRenderController, CVRMaterialDataProvider,
    Projector, texture parser and custom render texture updater on the avatar, so the shader gets
    no slots and reads rest. No way around it inside the whitelist; the bone stack would be the
    only route that survives it.
    Joe: "the squish is extreme in game". Causes, from the shader: the diagnostic pulse; the
    overfill (across = 1 + 2.33(1 - k), capped 1.5) GREW a squashed part by 27 to 68% of its
    volume; the bone push read every swing of the cloth as a press; the reach was one radius, so
    a hand at the side met a ball as big as the part's length. Refined 2026-10-11 (Joe: bend
    before it squashes, no part may get bigger, check the volume), NOT YET IN EDITOR OR GAME:
    - Volume kept: across = k^(-1/2), so flattening times widening squared is 1. A Python mirror
      of the shader (closed meshes, signed volume) reads 1.000 on every case (side hands 2, 6
      and 12 cm in, front 8 cm, two opposite hands, lying back, standing) against 1.04 to 1.68
      for the old shape, and 0.998 for the pair with its contact plane. `_YAPS_SquashShape.y` is
      now the share of volume kept (1 all).
    - Ellipsoid reach: six reaches per part (toward the partner, away, out of the body, into it,
      either way across), measured at rest as the 98th percentile of the part's skin, in a frame
      from slots no cloth moves: the rests and a new back node per pair (slots 16 and 17, on the
      rest bone behind the pair's middle, level when standing). A part with any reach missing
      falls back to the one reach.
    - Squeeze, not cancel: presses sum as S = sum v and M = sum v v^T / |v|; the squash runs
      along M's main axis (power iteration from S), each side moving in by its own press, so two
      opposite hands squeeze.
    - Bend first: hand and arm presses across the body swing the part away about where it meets
      the body (a shear, so volume stays), giving up to `Tune.z` of the reach out and saturating;
      what the bend took comes off the squash.
    - Sag: lying back presses the part toward the body by `Tune.x` of its reach out, faded in from
      30% tilt. Hanging stretch is MagicaCloth2's.
    - Bone push off by default (`_YAPS_SquashPush.x` 0), `Tune.y` scales every press.
    - A layer whose shader cannot be patched, over 5% of a part's skin: the pair holds still
      (its reaches baked 0) and the build log names the layer, since squashing the skin under a
      layer that cannot follow pushes the skin through it.
    Heavy, not a water balloon (Joe, same day: "they need mass, lots of mass ... better indent?
    and heft"), NOT YET IN EDITOR OR GAME. A uniform volume-kept squash is what a water balloon
    does: one finger flattens the whole part. Hands now DENT first: up to `Tune.w` (0.35) of the
    reach toward the hand, eased in, as a bowl as wide as the hand plus the depth with a rim of
    (1 - s^2) e^(-s^2) around it, moved along the line from the part's centre (on a ball that
    keeps the volume exactly: area in chord distance is 2 pi c dc and the profile integrates to
    0). Only what the dent cannot take squashes the whole part; past both the hand goes in and
    the capsules carry the skin, so nothing squashes without limit. The bend is taken off each
    hand before its dent. Hands are kept as four slots: both own arms and the two deepest other
    hands. Python mirror (closed meshes, 2562 vertices): volume 1.000 to 0.997 on every case,
    0.982 at a 14 cm press; an 8 cm front press dents 4.5 cm, rims 16 mm and moves the far side
    11 mm, where the water-balloon form moves it 31 mm; no folds. Along the hand's line instead
    of the centre's, the rim slid round the sides unseen and a deep dent lost 6%. The dent's
    normal checked against finite differences (2000 points, 0.0000 degrees).
    Shadows: a ShadowCaster pass on Unity's own `vertShadowCaster` is now wrapped, not skipped.
    The wrapper's signature is read from the editor's `UnityStandardShadow.cginc` (2022.3
    returns a `VertexOutput`, older versions an out position), takes `SV_VertexID` beside the
    input, runs the same deform calls (one `DeformCalls` list for both routes) and calls
    Unity's. Compiled with fxc against 2022.3's CGIncludes in five variants (depth, cube,
    cutout, parallax, stereo instancing). Avatar A's Poiyomi 7 caster is its own, already patched.
    Still unsquashed: a caster from anywhere else outside the shader, and shaders with no caster
    that lean on a Fallback.
    BUILT 2026-10-15: a shader with no caster of its own and an opaque `Fallback` gets Unity's
    standard caster written into each SubShader (`AddStandardCaster`), which the wrapper above then
    bends. Not when the Fallback cuts alpha (the caster would shadow the cut parts solid), not
    beside a `CGINCLUDE`/`HLSLINCLUDE` (it can declare what UnityStandardShadow declares), not for
    surface shaders. The patcher is shared, so a Fallback-only plug shader's shadow bends too.
    `Dev/Tests/StandardCasterTest.cs` compiles the caster pass by hand (`ShaderUtil.CompilePass`),
    since the caster variant otherwise compiles only when a shadow is drawn. Passes 2026-10-16, once
    its input declared `SV_VertexID`: the patcher refuses any vertex input without one. Still
    unsquashed: casters from other shaders (`UsePass`), cutout Fallbacks.
    Strap bending at the shoulder (Joe, editor Play mode, same day). WITHDRAWN as a squash bug:
    the builder's grid did hold only skin WITH a share (every skin vertex goes in now, as the A/B
    probe's copy always did), but Joe's rebuild with that fix still bent. Read from the source
    FBX: skin and bra both carry up to 0.33 to 0.35 weight on the breast bones from the chest up
    to the shoulder joint's height, and the strap over the shoulder's top 0.09 where the skin
    has none. The breast cloth's root is pinned (not a stand-in), so the strap bends with the
    breast as it would under the PhysBone in VRChat. The source paint, not the conversion;
    less swing shrinks it.
    Test: the plain build in editor Play mode (hands in, lying back), then in game; which pairs
    pulse in the diagnostic build is still unread.
    Marshmallow PB read from its prefabs (2026-10-11, `Assets/marshmallow_PB`, ver2 stack; read
    only, not run). The visible breast bone is no PhysBone's output: constraints drive it.
    Position follows the end of a Buffer chain (pull 0.5, spring 0, stiffness 0, hinge 45,
    collider-floored): a damped follow with no spring, so it cannot bounce. Rotation is 0.5 to
    0.7 an Inertia chain (spring 0.9, pull 0.05, angle limit 20/40) and the rest a look-at: the
    lively lag is in rotation only. A Gravity chain (immobile 1, gravity 0.5, hinge) gauges tilt
    and, through a time-driven clip, rotates the parent of the whole stack between Sag, Neutral
    and Squish targets: lying back tilts the rest pose. The squish is a scale clip on the bone
    driven by a gauge PhysBone's _Squish: at full squish 0.85 deep and 1.5 wide, at stretch 1.2
    long and 0.85 wide, under a Scale constraint at weight 0.6. That GROWS the part (1.5 x 1.5
    x 0.85 = 1.9 times the volume, about 1.5 if the weight blends from rest), the opposite of
    Joe's no-bigger rule. Setup defaults: pull 0.1, momentum 0.5, stiffness 0.25, gravity 0.02,
    immobile 0.5, limit 40, collision radius 4 cm, max squish 0.6. So the heft to copy into the
    MagicaCloth2 stand-in is: position heavily damped with no spring overshoot, swing lag on
    rotation only, rest pose tilted by orientation. Needs the A/B probe (batch, GUI closed).
    Next: the stand-in's damping from those, other players' hands from the globals.
    Heft sweep DONE 2026-10-12 (PhysicsAbProbe on the marshmallow avatar, `-abTune`, both butt
    chains; peaks in degrees, marshmallow / stand-in before / after): walk 2.0 / 11.0 / 4.3, jump
    12.5 / 35.0 / 8.6, shake 10.1 / 37.3 / 15.7, lie 7.3 / 22.1 / 8.4, turn 8.7 / 8.4 / 3.4. Stand-ins
    now get world inertia 0.4: the mean peak error fell from 1.27 to 0.79 (1.0 is twice or half).
    0.5 matched the jump, 0.3 the shake and lie; 0.4 scored best of 0.3, 0.4, 0.5. Damping is the
    wrong lever: 0.9 RAISED walk to 16 and lie to 30, because MagicaCloth2's damping slows the
    return to rest as well. The prefab reading's "cannot bounce" is position only: measured, the
    original swings back 0.86 of its jump peak (stand-in 0.3) and settles in 0.77 s (0.33 s).
    Press and squeeze DONE 2026-10-12: stand-ins get spring 0.03 and a 10 degree angle limit, error
    0.79 to 0.63. Press 12.4 to 8.5 (add-on 5.5 to 7.2), squeeze 14.1 to 12.0 (9.7 to 10.8), shake
    15.7 to 10.1 (10.1), lie 8.4 to 7.0 (7.3); jump fell 8.6 to 5.9 (12.5), walk 4.3 to 3.7 (2.0).
    A 6 degree cap or a spring of 0.015 lost the swing. FitToPhysBone switched every angle limit
    off AFTER the stand-in set it, so the first converter run scored 0.70; a no-op `-abTune *=1`
    logged useAngleLimit 0 and found it. Out of MagicaCloth2's reach: turn 3.2 against 8.7, slide
    0.78 against 1.5 (spring 0.015 reaches it and kills the swing; limitDistance, centrifugal and
    normalLimitRatio did nothing), and the rest of press and squeeze. The add-on's split of a
    position follow from a rotation lag has no MagicaCloth2 equivalent.
    One avatar, one add-on version. The only other marshmallow avatar found, the BBL prefab, fails
    to build: its setup plugin throws on a "marshmallow_PB" child that is already there.
    Open: the globals in stable; the probe moving parts by hand instead of through the cloth.
    Other players' hands, 2026-10-14. In game (Joe): own hands squash, other players' hands never
    did. The client's globals were right (decompile: each player's hand bone in world space,
    `CVRGlobalParams1.y` = network players + 1, set every frame); the shader was not. It took
    another player's hand as a 5 cm ball at the WRIST, which is the hand bone's origin, a palm's
    length out from the skin while the palm is on it. SquashPlayProbe `-spOther` feeds a fake
    player through the same globals: wrist 7 cm past the surface, nothing moved (797 samples
    changed against rest, the same as no press at all); a 30 cm ball at the same wrist did move
    it, so the globals reach the shader. Changed the same day, NOT YET IN GAME: the hand runs on
    from the wrist, away from that player's chest (`_CVR_PlayerChestPositions`), for 0.11 of
    their viewpoint height (`_CVR_PlayerRootPositions` w, 1.6 when missing), its radius scaled
    from a 1.6 m viewpoint. Same press: 9582 samples changed. The line from the chest stands in
    for the forearm, which the globals do not carry. The test avatar needs `-squashShader` on
    SquashTestBuilder, or it builds the old bone-scale test.
    Joe, same day: "denting it shouldn't move it that badly ... a dent can have a bit of
    compression", and the volume should read as kept. A press bends the part (up to bend give
    0.35 of its out reach), dents it (up to 0.35 of its reach toward the hand) and moves the whole
    part by whatever is left. Probe A/B, same press: bend 0.10 with dent 0.60 kept the part near
    its rest outline with the dent showing; bend 0 with dent 0.90 nearer still. Not chosen yet.
    Volume: the dent's rim holds exactly what the bowl moved, but peaks at 13% of the depth,
    1.4 bowl widths out, so it never shows and a dent reads as lost volume. A lobe below the
    hand in the front renders was the part itself, hidden at rest by the hanging arm: it is
    there with the shader off.
    Rebuilt on how tissue behaves, same day, NOT YET IN GAME. Breast tissue and fat are nearly
    incompressible (Poisson's ratio about 0.49), so a dent's volume has to show somewhere; skin
    is about ten times stiffer than the tissue under it, so it lies on the hand and rounds the
    edge rather than creasing; tissue stiffens fast with strain, so a light touch stays local
    and only a firm one moves the whole part; the part is held at its base and slides over the
    chest, so it swings about the base. Two changes follow. The dent comes FIRST and the bend
    takes only what the dent leaves (it used to bend first, so a 2 cm touch from the side swung
    the whole part 1.5 cm). And the dent's profile: inside, the skin lies on a ball of the hand's
    radius pressed that deep, `1 - t`; round its edge, the same volume rises into a shoulder a
    third of the depth high at 1.35 contact radii, back to rest by 1.86, instead of the old
    rim out to three widths. Volume-neutral on a ball (cap area is pi times the chord squared,
    so a profile neutral in t = chord^2 / width^2 is exact). Python mirror of the part (scratch,
    the test avatar's measured reaches), shipped vs new: volume 1.000 vs 1.000 to 1.004 in every
    case; rim 0.08 to 0.49 cm vs 0.32 to 1.17 cm; side press 2 cm: whole part moved 0.8 vs 0.2
    cm, bend 1.5 vs 0.4 cm; from below 3 cm: 1.0 vs 0.3 cm; no folded faces in either. Probe on
    the rebuilt test avatar: the old dent showed as a small dimple with the outline shifted; the
    new one as a hand-sized dent with a visible lip, outline at rest. The lip's edge, where the
    curvature turns, catches a highlight ring on a glossy material: that is where skin leaves
    the hand, but say if it reads too sharp. Left out on purpose: sending part of the volume
    to the far side of the part when the tissue under the hand is thick, which is what physics
    says. Spread that thin, it is invisible, which is the complaint.
    Idea, Joe same day: other players' FINGERS. The client gives shaders only wrist, chest, hip,
    head, feet and root per player, so finger bones never reach another avatar's shader. But the
    decompile (`BetterPlayerShaderPositionsProvider.UpdatePlayerPositions`, and `PlayerSetup` for
    the local player at index 0) puts each player's GESTURE in the hand's w, the same number the
    animator's GestureLeft/Right gets and synced like it: -1 open, 0 to 1 fist, 2 thumbs up, 3
    finger gun, 4 point, 5 victory, 6 rock and roll (`CVRXRModule`). Done the same day, NOT YET
    IN GAME, for every player, patched or not: a fist ends at the knuckles (0.06 of viewpoint
    height from the wrist instead of 0.11); gestures 3 to 6 add a finger capsule from the
    knuckles to the fingertips at a fifth of the hand's radius (victory: a third, two fingers as
    one, since the palm's facing is not carried); the deeper of hand and finger is the press.
    Probe, same wrist 8.7 cm out: open hand 4217 samples changed against rest, fist 107 (falls
    short), point 3185 and victory 3350 (a narrow poke), fist with the wrist at the target 5051.
    Exact fingers would still need the patched-to-patched route: the presser's hand shader
    writing finger segments into the cross-avatar GrabPass atlas (protocol 7, proven in game),
    about ten slots a player. Not started.
    A third player, Joe same day: does someone watching see A press B? By construction yes,
    nothing synced beyond what the client already sends. Every client fills the arrays itself:
    index 0 its own player, then EVERY network player with a puppet master, from that remote
    avatar's bones after net IK (`NetIKController.UpdateNetIk`, human avatars with IK applied).
    So on the watcher's machine A's hands are in the arrays, B's part runs the shader on B's
    remote copy (its own slots come through the material data provider, which works on remote
    copies), and B's own hands are skipped by distance from B's own slots. Probe `third_palm`
    lays the arrays out as the watcher has them (its own player far off at 0, the presser at 1,
    the wearer's own hands at 2, count 3): identical to the two-player shot, 0 samples differ.
    Needs three clients to prove in game.
    Squeezed together, Joe 2026-10-14 after the first squish video: "the test pulse needs to
    bring them together and the test hands squish too". Two shader gaps. (1) A hand on the outer
    side bends the part toward its partner, but the contact plane only switched on when the
    FOLLOWS were inside `start`, so two bent parts passed through each other. Now the plane
    eases in with the bend toward the partner (full at a tenth of the reach) and sits midway
    across the gap when the pair is apart, so a squeeze from outside makes them meet and
    flatten against each other. A bend away from the partner never turns it on, and at rest
    nothing moves. Ceiling: each part sees only its own bend, so one squeezed alone flattens
    against its partner's rest surface without pushing the partner. (2) The test pulse added
    to the pair press, which flattened each part where it stood and read as no contact. It is
    now a squeeze from the outer side, through the same bend and flatten as a hand, so the pair
    swings in and meets; still a share of the start, still on the same pairs the diagnostic
    reads. ON DEV, editor-proven only (`-spVideo`: both the squeeze and the pulse close the
    cleavage into a flat seam and let go), NOT YET IN GAME. Re-make the diagnostic build to see
    it; a shipped build's pulse is 0.
    Snaps and the seam, Joe 2026-10-14: "if you move the avatar arm over, the breast snaps
    violently, is there other instances of this occuring?", then "a nicer looking cleavage
    squish". FIXED ON DEV, editor-measured, NOT YET IN GAME. `SquashPlayProbe -spJump` sweeps the
    own arm past the pair on five paths (360 frames each, over the top among them) and reads every
    vertex's move per frame; a snap is a move far past the arm's own step, about 7 mm a frame. It
    reads the same sweep again from copies of the shader with one stage cut out, and with the
    press or the bend read out in place of the move, so the copy that loses a snap names it. What
    was found and what replaced it:
    - An arm's press points from the part's centre to the arm's line. An arm passing near the
      centre turned it right over in about six frames while it was at its deepest (260 mm), so the
      whole part flipped from pressed above to pressed below, 130 mm in a frame. The press is now
      never more than the line's distance from the centre, which holds the turn to the arm's own
      speed. A fade toward the centre (smoothstep over half the extent) only halved it.
    - The own-arm capsule push went opposite ways either side of the bone and flicked skin across
      as the bone swept by; it fades to nothing at the bone. Most of the jitter on the other paths.
    - One squeeze line swung the whole flattening over as one press overtook another: now three
      square lines, the second solved exactly in the plane across the first (iterated, it grew
      from rounding noise under a single press and threw the part a hand's width), the volume
      kept across all three, normals by the inverse scale.
    - The first line was found by iterating from the sum of the presses, and presses from either
      side cancel in the sum: on one frame it lay square to the strongest line (their dot about
      1 mm), the iteration never found it, and the whole part flattened along a wrong line for
      that frame, the farthest vertex out and back by 39 mm. Traced with readout copies of the
      lines, their strengths and the shifts, logged for one vertex (`-spJumpVertex`). Started
      from M's longest column instead, it jumped wherever two squeezes were nearly level (78 to
      72, so six steps barely moved off the start) and another column grew longer: 37 mm on the
      over-the-top path. Now solved exactly: the largest eigenvalue in closed form, its line as
      the longest cross of two rows of M less it; checked against long power iteration on 20,000
      random press sets in Python. Which way a line points never mattered, since flattening along
      its reverse swaps front and back and the shift cancels exactly.
    - Presses merge, the deeper leading by depth to the fourth, instead of the deepest alone: per
      arm across its bones, and other players' hands into slots 2 and 3. An own hand eases out of
      the others' list instead of cutting, the plug's push eases out over the last fifth of its
      length, the pair's contact eases in over the last tenth of the gap.
    Worst move per frame on each path, committed shader then now: 77/14, 53/21, 64/29, 133/36,
    62/19 mm. Over the top is now a ramp over eight frames, not a flip. Left out of those: the
    sweep's first frame, 39 mm in the committed shader as well, which is the arm switching on.
    Ceiling, measured: where two squeezes come nearly level the exact lines still swing round
    each other fast (80 degrees over five frames, 25 at the most, strengths 78 and 69), and the
    flatten changed with them, since each line read its own strength and its own width of the
    part. FIXED ON DEV 2026-10-14 (overnight): toward level (the weaker within 30 % of the
    stronger) the pair shares one strength and one width, wholly when exactly level, which no turn
    of the lines in their plane can change; each line keeps how far off centre the part reaches.
    Sweep: the one frame it reaches, on the fourth path, 36.9 to 24.9 mm, now spread over two
    frames (at 60 %, 23.3: not worth twice the zone where unequal presses are averaged); every
    other frame of every path within 1 mm, and the four `-spLook` shots pixel-identical. That
    path's worst is now 36 mm at another frame, an arm-contact frame that stays with the flatten
    cut out. WRONG, withdrawn: "29 mm in one frame of a five-frame ramp" from the flatten. The
    third path's 29 mm is 28 mm with the flatten cut out, so it is not the flatten, and the
    blend leaves it as it was. Needs Add squash again (the include is inlined). Not seen in game.
    Cleavage: `-spLook` renders the pair lit over a lightened texture (a black top hid the shape)
    from the front, above and close, at rest and squeezed. The plane where the two meet let a
    fifth of the overshoot through, so both sides crossed into each other lying nearly flat. Now
    each side stops just short and deeper layers stack behind. With the give at a tenth of the
    reach that opened a slot 1 to 2 cm wide down the middle; at 3% the two meet in one thin line.
    The jagged seam Joe saw in game never showed in these renders, so whether this is its fix is
    open. Ceiling: an arm right through the middle now lets the part go rather than flip, as a
    shader keeps no memory of the side it came from. Open: in game, after **Add squash** again.
    Idea, Joe 2026-10-14: MESH-BASED SELECTION. PROPOSED, not started, Joe picks the tier. Today
    parts are found by bone name (breast, boob, butt, booty, glute), at most two pairs, the share
    per vertex is the skin weight to those bones, and only the dev test builder sets any of it
    up: no bones, no squash; no belly, thigh or cheek; sloppy weights bleed it into the chest.
    The selection is the user path the converter step below needs. Tier 1: Scene view tools
    (click to grow by surface distance, brush with falloff, grow, shrink, smooth, X-mirror that
    makes the pair) with seeds from bone weights, from shape key deltas (what a size key moves,
    its delta as the falloff) and from a bulge finder (what sticks out of a heavily smoothed copy
    of the body); always feathered, since hard per-vertex guesses tore the skin; baked into the
    existing share texture, four regions, no shader change; a region with no bone gets a generated
    anchor under its dominant bone, one with a physics bone follows its tip. Tier 2: any number of
    regions (top two per vertex, id and weight in one texel, so the cost stays two per vertex),
    region settings in a small texture because material arrays are never saved and an avatar
    runs no script, single regions and rests-on contacts beside pairs, flesh presets, weights
    carried to every clothing layer by nearest surface (or the layers part and the skin comes
    through in stripes, the old bug), and poking it with the mouse in the Scene view through the
    same player globals. Tier 3: the press per region out through the texture parser to drive
    reactions (parser proven on the wearer, remote copies unproven), a stateless pressure tint
    (a fading print needs memory an avatar has not got), rigid islands for jewellery, and the
    same tool painting YAPS plug and socket masks, the override the masks entry above says is
    missing. Tier 4: jiggle anywhere, a generated bone and cloth per region on a re-skinned mesh
    copy. Unknowns: how many slots the material data provider carries, and keying by vertex index
    (store the selection by position and UV, re-bake at build, refuse a mismatch). Both builders
    need it.
    Joe 2026-10-14, "go ahead" on Tier 1, and: make squash a FEATURE the user opts into, a toggle
    in the conversion and in the YAPS tool, either what the converter would have done (parts by
    bone name) or custom (the selection). Off unless switched on. Step 1 DONE: the setup moved out
    of the dev test builder into the add-on (`YapsSquash.Add(avatar, folder, report, pulse)`,
    `YapsSquash.Strip`), the test builder now a thin caller; rebuilt the test avatar through it and
    every share texture came out byte-identical to the build before. Copies are marked on their
    importer (`squash:` before the source's id) so Strip finds them in any folder. Found on the way:
    the video probe's pulse shot set the pulse on the material ASSETS and left 0.006 on the build it
    filmed; it now puts the build's own value back.
    Steps 2 to 4 DONE, 2026-10-14, ON DEV, editor-proven only, NOT YET IN GAME. The converter
    opt-in (`squashSoftParts`, off; one pass, "Squash soft parts", after "Point material swaps at
    the baked material" so YAPS finds its own bakes on the mesh and the copies are of what the
    avatar ends up wearing). The YAPS tool's Squash card (Add, Paint regions, Remove). Swaps
    followed (`YapsSquash.FollowSwaps`): a clip putting the original back points at the squashed
    copy, an alternate gets a copy of its own with the slot's squash values and joins both
    providers' material lists; Strip with clips points every key back. The region tool (Tools >
    YAPS > Squash regions, `YapsSquashRegions` on the avatar, renamed `YapsSquashSettings` 2026-10-14, bytes per vertex, refused when the
    mesh's vertex count or position fingerprint changed): a brush that spreads over the welded
    surface (Dijkstra, so it never crosses a gap), Ctrl erases, strokes never paint past their
    strength, grow, shrink, smooth, X-mirror, and seeds from the bone names, a selected bone's
    subtree and a shape key's delta split down the middle; seam copies take one weight. A
    painted part rides the most weighted bone of its own that hangs from another (the cloth holds
    a chain's root still), its rest on what the chain hangs from; with no bone of its own, follow
    and rest share the dominant bone, so only a press moves it. Proof, Avatar A in batch: the name path
    rebuilt byte-identical (shares and materials); the converter's opt-in made the same textures;
    regions seeded from the names gave identical materials and body shares, and clothing differs
    only off the skin, where the painted path copies nothing past 3 cm and the name path keeps the
    layer's own bone weights (README says so); a synthetic swap clip (original back, plus an
    alternate) followed and stripped cleanly. Not built from Tier 1: the bulge finder (the shape
    key seed covers the case it was for) and a separate click-to-grow (the brush is it).
    Each rebuild left the previous build's copies in the folder. FIXED ON DEV 2026-10-15: a
    rebuild writes over the assets the last one made (see the story record below).
    Open: the Scene view brush and overlay have only compiled, nobody has painted with them;
    a clothing toggle while squashing, in game; whether the client's per-copy material cache
    feeds a material that arrives by animation (the alternates' copies depend on it).
    Idea, Joe 2026-10-14: SQUASH MEETS A PLUG. Today it does not: the squash reads only hands (the
    wearer's arms through the provider, other players' through CVR's player globals), the cloth and
    the partner, so a plug passes straight through a part, and one bent into a socket between a pair
    leaves the pair as it was. Routes, cheapest first: (1) the wearer's own plug through the provider
    slots like the arms, a capsule per baked segment; (2) another player's plug needs a transport,
    and the atlas is the one already crossing avatars, but today only sockets write it, so plugs
    writing their shaft is a new kind and a protocol bump (mixed builds blind to each other); (3) a
    plug already announces its base and length in a protocol light, which is how a socket knows its
    depth, so a socket placed between the pair gives the shaft's line (base to socket) and the pair's
    shader reading the same light could part them along it with no new transport; only where such a
    socket sits, and the four light slots are shared.
    Joe 2026-10-14, "all of it". Route 3 DONE, ON DEV, editor-proven only, NOT YET IN GAME: two
    more provider slots (18 and 19), each pair's socket when a `YapsSocket` sits in the gap the
    pair parts around (within the smaller reach of the line from the back node to the pair's
    middle; distance to the middle alone missed a socket at the skin between large parts, 25.7 cm
    off with 22 cm reach, and could have taken a mouth socket above them). The shader takes every
    black light whose range digit is 8 or 9, base at the light, length in its alpha, and presses the
    pair with a capsule from the base through the socket for the plug's length, only once the
    socket is within that length; the capsule competes in the hand slots, so it wins only where it
    presses hardest. Proof, Avatar A in batch Play mode (`SquashPlayProbe -spPlug`, each shot rendered
    twice in one frame, light on and off, so the cloth cannot differ): 5 cm short of the socket, 0
    pixels change; a third and two thirds through, about 4400 pixels at the meeting line, front
    and side, nothing torn. A shaft run up the chest wall behind the parts changed nothing, as it
    should; one aimed into a part pressed it (9230 pixels), which is how the light path itself was
    proven. Route 1 needs no build of its own: the wearer's own plug light reaches their own body,
    so route 3 covers it wherever a socket is. Route 2 (the atlas, a protocol bump) stays queued
    for a plug that touches a part with no socket between the pair.
    Thickness: the light carries none, so the shaft's radius is a tenth of its length. Capped at
    3.5 cm at first, which drew any plug past 35 cm thinner than proportional; 6 cm from
    2026-10-14. Real girth would need a channel: the tracker light is DPS's, so its colour must
    stay black, and the atlas route (2) could carry it.
    Open: in game, a plug through a socket between the pair, own and another player's; that the
    four light slots hold with a plug, the socket's own lights and a world's lights together.
    Idea, Joe 2026-10-14: FORCE AND BOUNCE. A press that arrives fast should land as an impact: a
    wave out from where a hand (or a plug, or another part) hit, the part bouncing a little after
    a hard push, on any part pressed by any other, with sockets and plugs. NOT STARTED. The squash
    is stateless (every frame is a function of where things are now), so it cannot ring after the
    hand leaves; motion that outlives its cause needs memory. Routes: (1) a CustomRenderTexture,
    double buffered, holding a damped spring per part (offset and velocity) and the last positions
    of every press source, updated on the GPU with no script, the hand's speed from its last
    position, the impulse its speed into the part; the squash reads the spring as one more move,
    and a wave is the impact point and its age in the same texture, a ring travelling out over the
    part's ellipsoid and fading. (2) The cloth itself, which already bounces: an impulse through
    the texture parser into a synced float that animates a pose offset, which costs sync and a
    frame of lag. (3) MagicaCloth2 colliders, which only ever see the wearer's own body. Route 1
    is the one that reaches other players' hands and costs nothing synced, but nothing proves a
    CustomRenderTexture updates on a ChilloutVR avatar at all, on remote copies, or per copy (two
    wearers of one avatar would share one texture, the same trap as the shared materials). Spike
    first: a CRT counting frames on an avatar, read by its own material, watched by a second
    client. A plug's base velocity from its protocol light and a socket's depth velocity are the
    impulses for sockets and plugs once the spike holds.
    Researched 2026-10-14 (decompile of Joe's client, Unity's player loop, prior art): ROUTE 1 AS
    WRITTEN IS REFUTED, and a blitter replaces it. The client does clone every RenderTexture per
    avatar copy, a CRT included, so per-copy state is possible; but it clones the CRT and not the
    CRT's update material, and the material data providers write only to cloned materials, so a
    CRT update pass sees its own buffer, the global shader values and shared constants, never the
    slots or the skin. It cannot know where the part is, so it cannot feel a press. Recommended
    instead: a 5x4 float RenderTexture (point filtered, linear) updated by a CVRBlitter blitting it
    onto itself, inside the avatar's existing render controller after the two providers. A blitter's
    material and textures ARE cloned per copy, a provider can write into that material, and a
    blitter with `_Time` already ran in game on this repo, remote copies included. The drive is the
    per-frame change of each part's summed press, so players joining or leaving do not tear it;
    the step is the exact damped-oscillator solution with a clamped dt; the body reads about six
    texels per owned vertex. Plug and socket impulses cannot ride it (marker lights exist only when
    drawing), so their bounce needs another feed. Nobody has shipped per-part spring state on an
    avatar on either platform. Unknown and only answerable in game: self-feedback on a cloned float
    RT, `_Time` and the slots inside that blit, and whether a Realtime CRT ticks on a CVR avatar at
    all (VRChat's CRTs freeze after an avatar switch). Next: one spike upload in stages (a counter,
    the slots read in the blit, self-feedback, a remote viewer, a switch-and-back) before any build.
    STAGE 1 BUILT 2026-10-14, editor-proven, NOT YET UPLOADED: `Dev/Probes/BounceSpikeBuilder`
    (`Build`, or `Verify -bounceOut <dir>`) makes `Assets/AvatarBridge_Bounce/Bounce_Stage1.prefab`
    in the dev project, with a README and a preview scene. Two blitters each rewrite a 2x1
    ARGBFloat texture from itself (origin == destination, the client's own temp-and-copy route,
    read in the decompile): one in a CVRRenderController, the production placement, one off the
    camera as the control. Texel 0 holds sums decayed by 0.99 a run (runs, unity_DeltaTime,
    _Time.y's steps between runs, the last _Time.y), so runs over time is the tick rate and frame
    times over time passed is runs per frame; texel 1 wraps a counter for a sweep. A billboard
    readout (single-pass instanced macros, ZTest Always) shows both rows. So the one upload
    covers the counter, self-feedback, `_Time` and `unity_DeltaTime` inside the blit, and the
    remote viewer and switch-and-back as things to look at; stage 2, the providers' slots read in
    the blit, is what remains. Batch Play at a fixed 60 fps, 600 frames: both rows 60 runs a
    second, 1.0000 runs per frame, readout rendered as designed. The editor cannot show the
    client's per-copy clones, so the readout's textures are DECLARED properties: the client
    re-points an instanced material's render textures by walking `GetTexturePropertyNameIDs`,
    and an undeclared one would read the uncloned asset, which nothing writes.
    IN GAME 2026-10-14, the wearer's own view (Joe, screenshot): both rows written and healthy,
    runs per frame at 1 (green, at the middle mark) on both, rate bar full (180 or more a second,
    so an uncapped desktop frame rate; the bar's scale tops out there). So on the wearer's client a
    float texture rewritten from itself inside a render controller keeps its state, ticks once a
    frame, and sees valid `_Time` and `unity_DeltaTime`, and the readout reads the copy's clone.
    The sweep moved (Joe, same session), so it was live, not a texture frozen after its start.
    Still open: a remote viewer, switch-and-back, both eyes in VR.
    COST, measured 2026-10-14, batch Unity with graphics, one 1920x1080 render plus a one-pixel
    readback so the GPU finishes, 35 renders kept, median ms (fastest), this machine's GPU:
    one avatar: none 0.22 (0.19), squash shader off 0.74 (0.58), on 0.99 (0.84), with 16 players
    out of reach 0.96 (0.84), 64 out of reach 1.17 (1.01), 64 hands all on one part 1.87 (1.69).
    Ten copies: none 0.23, off 1.67, on 2.15, 16 far 2.19, 64 far 2.43, 64 hands on one 3.09.
    So in a crowd the squash adds about 0.05 ms per avatar on top of about 0.1 for drawing it, a
    full room of 64 players adds a quarter of a millisecond for the loop over their hands, and the
    worst case (every hand in the room on one part) is under a millisecond. ponytail: one view, no
    shadows; VR draws each eye's vertices and a shadow-casting light draws them again, so expect
    two to three times this in game. CPU side (the providers' per-frame matrix and skin copies) is
    the client's and was not measured here.
    Joe 2026-10-14: A TUNING PANEL, "similar to how sockets and plugs work", for the converted
    squash and the native one alike. DONE ON DEV, compiled in every combination, inspector NOT
    YET OPENED by anyone. `YapsSquashRegions` became `YapsSquashSettings` (git mv, the meta's GUID
    kept, so painted regions on avatars survive), menu YAPS/YAPS Squash, the regions hidden and the
    knobs in four [Header] folds (how it gives, the pair, in motion, what presses it), defaults
    equal to the old hard-coded values. The build adds the component when the avatar has none and
    `YapsSquash.Apply` writes the knobs to every material both providers list, alternates included,
    so a knob needs no rebuild; `WriteKnobs` keeps the skin stride in the hands' w. The converter's
    clone carries a component set on the VRChat avatar. The painter's Remove clears the regions and
    keeps the knobs. `YapsSquashEditor`: See it work (Preview, then Squeeze, Bounce, Test hand),
    Parts (painted or by name, Paint regions), How it feels, Build (Build/Rebuild, Remove), Animate
    it. The preview writes `_YAPS_SquashFollow` itself each editor update from the provider's
    transforms (arrays are not serialised; Stop writes zeros back): Squeeze moves the follows toward
    each other, Bounce moves the rests the opposite way so the part stays where its skin is, Test
    hand is player 1's open left hand through CVR's globals with a scene handle, and
    CVRGlobalParams1 is put back on Stop, Play and reload. The Setup card's Add squash selects the
    component when it builds. Open: open the inspector and run the preview on a real avatar; that
    edit mode draws the squash at all with the skin buffer unbound (it should fall back to the
    matrices, state 2).
    User suggestion via Joe 2026-10-14: SQUASH AND STRETCH IN MOTION. "a bone chain can't really
    recreate the squish/stretch structured fat sacks have while in motion", with the slider "At low
    end, maximum realism. High end, maximum Gainax." DONE ON DEV, shader only, the cloth untouched.
    The swing is the follow minus its rest, which for a spring is the push the motion gives it, so
    it rings as long as the physics do and needs no memory. Each rest node is turned so its up axis
    points toward where the part hangs from (the chain root by name, the top of the loose chain
    when painted) plus the avatar's up, so a drop stretches it as well as a pull away from the
    body; the shader reads that axis from the rest's matrix. Along the swing the part stretches by
    -dot(swing, hang) per reach, about its side toward where it hangs, and thins across by
    rsqrt(stretch), so the volume holds; normals take the inverse-transpose. Motion Style lerps
    the gain 0.6 to 3.5, the swing ignored 0.1 to 0.03 of the reach, the longest 1.12 to 1.8.
    ponytail: the ignored swing stands in for the sag the physics settle into, which the edit-mode
    rest never saw; a slow average of the swing would need memory (the CRT route). Needs a rebuild
    for the rest rotation. Plug press gain is a knob too (`_YAPS_SquashPlug`), and the others'
    hands loop now skips a hand out of reach before pressing (3 reaches plus a hand plus its
    radius). Seen in batch Play mode 2026-10-14 on the socket test avatar, hopping 12 cm: stretch
    on against off changes 8600 pixels at Motion Style 0 and 66100 at 1, side view. Open: in game,
    whether real physics swing a part enough to see at 0 and too much at 1; whether the "plus up"
    hang is right for butt pairs.
  Open: remote copies,
  the measurement probe (camera cost, controller
  cost, readback latency, replacement universality), then the staged build above, tuning, and
  the converter (add-on only).
## MagicaCloth2 settings: measured against the originals and fixed, 2026-10-06. ON DEV, not in game

**Measured.** `Dev/Probes/PhysicsAbProbe.cs` converts a scene with a new user's defaults, keeps the
source beside the copy, turns every Animator off (cullingMode AlwaysAnimate first: MC2's default
culling follows the Animator, and nothing is on screen in batch, so it culled every cloth) and drives
both through one 90 fps timeline: walk, turn, a Head-bone turn, jump, shake, lie down and hold. Per
chain it compares rest sag, swing peak, settle, overshoot, mid-chain share and where it hangs lying
down; `-abGravityScale` sweeps gravity on the copy. Five avatars, 172 chains: Avatar D (112, all
PhysBone 1.1), Avatar B, Avatar A, Avatar C, Avatar F (mostly 1.0). Scripts and runs in the session
scratchpad (`physab/`: `ab.sh`, `compare.py`, `gravfit.py`).

Median error, copy against original (peaks in log2, 1.0 = twice or half; hang in degrees):

| metric | before | after |
|---|---|---|
| walk swing | 1.04 (copy swung half as far) | 0.30 |
| turn | 1.38 | 0.41 |
| head turn | 1.40 | 0.43 |
| jump | 1.78 | 0.42 |
| walk overshoot | 0.27 (copy bounced back) | 0.08 |
| lying-down hang | 2.7 (15 to 30 at times in between) | 2.1 |
| rest sag | 0.42 | 0.19 |
| shake | 0.52 | 0.79, worse: more scatter, little bias |
| stretch | +6% | +8% |

What it took, each measured on its own: the swing bound squared over depth and per joint (small,
all closer); preset angle limit off and particle limit raised (large); damping divided by MC2's 0.2
and no restoration floor (the big one: the floor made loose hair about nine times stiffer); gravity
converted per PhysBone version, falloff included, with an empirical load factor (fitted from the
sweep, `docs/SolverCalibration.md`); inertia on one control per Immobile type, depth inertia off
(head turns and jumps closer, walks a little further); Edge collision (neutral on these metrics,
kept because it matches VRChat's capsule); soft-body report, saved spring power, piercing and
"Pec"/"Hip Jiggle" names.

Open: shake scatter and the slight over-swing (copies now lean about 1.2x the original's swing);
PhysBone 1.0's stiffness algebra (renormalised, about 1/(1+k)) is still read as 1.1's; gravity
1.0 on a stiff chain cannot reach VRChat's full hang (G caps at 20); the load factor is fitted on
two avatars' worth of gravity chains. Everything here is editor-measured: **Joe checks in game.**

### The review that started it, kept for the findings not yet acted on

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
6. **Collision.** Point mode (`:184`, `MagicaColliderAutoAssign.cs:82`) where VRChat collides the bone
   as a capsule with lerped radius, so chains pass between particles; MC2's Edge mode matches. The
   author's radius is replaced by a mesh thickness; collider fitting sizes spheres from their narrow
   ends and never re-centres.
7. **Inertia split** (weakened by its skeptic): all three of world, local and anchor take `1-immobile`
   (`:670-697`). Walking ends up over-held; head turns slightly over-held. Needs the harness.
8. **Structure.** `ignoreOtherPhysBones` never read, so a nested chain is driven by two cloths.
   Curves keep only endpoints though MC2 samples 16 points; the Immobile curve is dropped. PhysBone
   `version` never read (v1.0 stiffness algebra differs). One cloth per PhysBone (issue #7): the
   "asked which avatar" blocker is stale, the reporter sent it on Discord. Distance culling unused.

## Loose ends, small but real

*4.7.2 shipped 2026-10-06 on Joe's word (tag `v4.7.2`, merge `7f6e9b8`): the Opt-ins gate and the audited README, no reconvert needed.
4.7.1 shipped 2026-10-04 on Joe's word: tag `v4.7.1`, merge `526b92e`, both packages published and
extracted (public: no `Editor/Yaps`, `Runtime`, `Dev` or internal files; add-on: no converter). It is
everything since 4.6.6: the audit and its corpus-414 fixes, the UI redesign, the tester's Physics card
(DynamicBone, hover labels, Simple and Advanced settings, Keep) and the stereo patcher rework. A 4.7.0
draft was built first, then deleted unpublished when two tester changes landed after it; its tag
`v4.7.0` stays in history and that number was never shipped. Corpus 415 is the accepted YAPS baseline.
Not verified in ChilloutVR; the records below say what only the game can settle.*

### Every button pressed headless, 2026-10-15: one dead, one noisy. FIXED ON DEV

Joe found **Tune it** did nothing. It and **Open material** now select the material, ping it and
open it in a property editor (`BridgeElements.Reveal`); selection alone showed nothing with the
Inspector locked or closed.

`Dev/Probes/ButtonSmokeProbe.cs` with `button-smoke.sh` presses every button and toggle in every
window and inspector, a fresh scene load per state, and judges each press by what changed: window
text and state, scene hash, materials, selection, clipboard, notifications, and a before and after
picture through an offscreen Scene view, so overlays a press turns on are in it. In Play mode it
grabs chains with mouse events through the tester's own handler: four of four followed the drag and
swung back. Pressing the converter windows converts per press and takes over an hour; run them alone.

**Weigh it and Fix it flooded the console** (18 errors, 146 warnings, none from the avatar). The
slimmer's "used elsewhere" check loaded every material in the project and read its textures, and
the first read builds a material's property sheet, which logs every fault in it. It reads an
asset-database dependency index now and loads only the materials that name the texture. Same answer
as before on all 4471 textures it can ask about (two Unity built-ins have no importer and are never
asked), in 3.5 s where the old sweep took 147 s cold. A texture inside a font or a controller counts:
the index adds each file's own path, which `GetDependencies` leaves out. The shader errors that
showed under **Patch shaders for VR stereo** were the same sweep's leftovers: a fresh process
patching alone logs none, and the rerun has none. Toolkit after the fix: 65 OK, 0 errors; the one
message left is a missing third-party drawer on the avatar's own material.

**Repeated test buttons made to agree, the same day.** Joe asked whether the Toolkit and the Avatar
Tester duplicate each other. They do not: the Toolkit writes, the tester plays, and the survey's
contested bindings skip humanoid muscles, which is exactly what the tester's finger chip checks.
The YAPS test buttons did repeat, in up to four places, each calling the one `YapsPreview`
function, so only labels and states had drifted. The tester now uses the inspectors' labels
(**Preview** stays Preview while lit, **Drop a test plug here**, **Preview into a hole** and
**through a ring**, **Take the test socket away**); the socket inspector greys **Drop a test plug
here** in Play mode with no plug prop prefab, as the tester did; the setup window's row **preview**
chips (a third copy: hole only, no baked check) are gone. The hand-layer name check had a copy in
the tester; both now read `AvatarFeatureDetect.IsHandLayer`, since the merger needs the VRChat SDK
and the tester must build without it.

**Blank result boxes in the setup window, shipped 4.7.0 to 4.7.2. FIXED ON DEV.** The last DEAD row,
**Verify prop** after a tab switch, was the window, not the probe. Picking a new target cleared the
add, prop and tidy result notices with `VisualElement.Clear()`, which removes the box's icon, body
and label; every later **Make selected object a prop**, **Verify prop**, add or tidy result then
coloured an empty box. Picking happens on the first selection, so it hit every session. They are
hidden now. The probe missed the text because the notice line alone changed (2 lines where text
makes 4); a DEAD row now lists the notices the probe sees.

**Make selected object a prop** on an avatar root added a collider and a spawnable to the avatar
(seen in the smoke's plain pass on a socket avatar). FIXED ON DEV: `MakeProp` refuses an avatar or
anything under a `CVRAvatar`, for all four callers.

Open: **Merge animators / clear** is disabled with no tooltip saying why. The paint stroke and the
squash depth are not yet gestured.

### A user's story played headless, 2026-10-15: three squash bugs and a false report line. FIXED ON DEV

Joe had not used 4.8.0's new features by hand and asked for them used the way a user would: a
custom jiggle on the thighs. `-bsStory thighs` (`Dev/Probes/ButtonSmokeProbe.Story.cs`) does that
through the real windows on a VRChat avatar with jiggle PhysBones on its thighs: Analyse, turn on
**Squash**, **Soft bodies push each other apart** and **Keep the converted avatar a prefab**,
Convert; then **Squash regions**: **The bone names**, a second pair painted from the jiggle bones
with **The selected bone**, **Smooth**, one brush stroke; the squash inspector's **Rebuild**,
**Motion Style**, **Preview**, **Squeeze**, **Test hand** and **Bounce**; then Play mode through
the tester: **Walk**, **Jump** and **Shake** filmed from the hips with the jiggle tip measured,
and the lab's squash **Preview** and **Test hand**. It backs up and restores the converter's
EditorPrefs, so it starts from a new user's defaults.

**The squash measured its parts on clothing.** Build took the skin with the most part vertices and
the region tool took the largest mesh. A dense piece of clothing over the seat won both, and since
every layer copies its share from the nearest skin vertex, the chest pair came out empty. Now
Build uses, of the skins carrying the most parts, the one weighted to the most bones
(`YapsSquash.Body`), and the tool starts on the same skin (`NamedBody`). The same conversion built
43 squashed materials before and 52 after, and the chest pair went from 0 to 1048 vertices a side.

**A region painted from a jiggle bone was dropped at Build.** The jiggle bones carried the thigh at
14.5% at most (1350 vertices weighted, none at 0.25). Build measures a part on the skin painted at
half or more and drops a pair with fewer than 20 such vertices ("too little skin"). The shader
moves each vertex by its share, so even a kept pair would have dented at a seventh. **The selected
bone** now scales to its peak and doubles, as **The shape key** always did, and it refuses a bone
that carries none of the body instead of leaving an empty side that fails only at Build. The
story counts the skin at half or more after painting, and fails **Rebuild** on a dropped pair.

**Test hand pressed only the first pair.** The hand aimed its palm at the first part wherever it
was dragged, so on a thigh it faced up the leg toward the chest and never pressed in (0.1% of the
picture moved). It reaches for the part nearest it now, in the inspector and in the tester's lab:
laid mid-thigh, it moved 3.3% of the legs in the editor, all of it on that thigh, and in Play mode
7.1% against 2.6% for the cloth's own sway over the same wait.

**"No mesh vertices are weighted to these bones" was false** for the same chain. The particle
radius samples only vertices that carry 20% or more on one bone (`MinBoneWeight`), so a lightly
weighted chain had nothing to measure. The report now says that.

Measured after the fixes: the thigh jiggle became two MagicaCloth chains and swung its tip 5 to
10 cm on Walk, Jump and Shake (a 32 cm chain); the converted avatar stayed linked to its prefab and
**Apply All** carried the painted regions and Motion Style onto it; the thighs painted 903 vertices
a side at half or more, **Rebuild** built both pairs, and **Squeeze** and **Bounce** moved 13% of
the thigh picture. The story lays the hand mid-thigh, since the chain's root sits in the hip
crease where the region is barely painted, logs how far it is from each part, and judges the lab's
press against the sway without it.

Left open by the story, all four done the same night, editor-measured:
- **Done 2026-10-15: a first squash build took 20 to 30 minutes in a large project.** Each
  patched shader paid a full `AssetDatabase.Refresh()` and its own import, and every import runs
  every installed package's import hooks. Now the patcher writes every copy first and imports
  them in two batches, includes and then shaders (`YapsShaderPatcher.Prewrite`, called from
  `Add` and from `FollowSwaps`), and refreshes only for a folder it made on disk itself.
  **Rebuild** goes back to the folder the last build used, a conversion's included. Measured on
  the thighs story into a fresh folder: 24 shaders patched, 12 includes in one import and 19
  shaders in a second, and 113 s of refresh across the whole run, against 130 s before for a run
  that reused a folder and patched one.
- **Done 2026-10-15: named pairs squash at their raw weights.** The bone-name path now scales
  each part to its peak on the body, as a painted region is (`Peak`), so a part its bones carry
  at 0.67 at most dents whole where they carry most. The body is picked before scaling, which
  lifts every skin and could change the pick.
- **Done 2026-10-15: the corpus never builds a squash.** `CorpusSettings.squashSoftParts` is on.
- **Done 2026-10-15: a rebuild numbered a duplicate of every copy.** `Asset` took
  `GenerateUniqueAssetPath`, so each **Rebuild** added a " 1" set beside the last, and with the
  rebuild now going back into the conversion's folder they would have piled up there. A rebuild
  now writes over the assets the squash had on the avatar before it started (`Owned`, then
  `CopySerialized` onto the old asset), so anything still pointing at them, a prefab not applied
  since, follows. Only those: a same-named avatar's copies in a shared folder are never touched.
  The story's rebuild: 58 distinct squash assets against 124, no numbered shares, and the same
  Squeeze, Bounce and hand numbers.
- **Done 2026-10-15:** `UiOffscreenProbe.FindSource()` picked either of two VRChat avatars in one
  scene from run to run. It now takes an active one first, then the first in the hierarchy.

### "Avoid unpacking the converted prefab": a user's request, 2026-10-14. A ON DEV, B PARKED

The asker's answer: they meant B, so that re-exporting the FBX reaches the converted avatar, and
said not to worry about it (their avatar uses VRCFury, whose bake flattens it anyway). Parked until
someone asks again. Worth telling the next asker that edits to an existing mesh already arrive:
the copy points at the FBX's own mesh, which a reimport updates in place. New objects, bones,
renames and anything the converter wrote into its own copy do not.

Nothing unpacks explicitly. The link is lost three ways: the clone is `Object.Instantiate`, which
never keeps one; a VRCFury or NDMF bake hands back a plain copy; and the output prefab was saved
with `SaveAsPrefabAsset`, which leaves the scene copy unconnected.

- **A, ON DEV:** *Keep the converted avatar a prefab* (Manual ▸ Menu & extras, off) saves with
  `SaveAsPrefabAssetAndConnect`. Batch check on one avatar: the copy is an instance of the output
  prefab with no overrides at all, so nothing after the save touches it. Off is unchanged.
  Reconverting overwrites the prefab, so an older linked copy follows; that is why it is off.
- **B, parked:** the converted avatar as a Variant of the user's ORIGINAL prefab. Never possible on a
  VRCFury or Modular Avatar avatar (their bakes flatten). Needs a link-keeping duplicate (the Ctrl+D
  route, not Instantiate), and Unity refuses to reparent inside a prefab instance: at least
  `ConstraintConverter.cs:668` and `FuryHeadFlattener.cs:78` move existing bones. Probe which passes
  refuse before building. A later VRChat component added to the original lands unconverted.
- **C, not wanted:** nested prefabs inside the avatar (outfits, props) staying prefab instances in the
  converted copy. Same Instantiate and bake limits as B.

### "Kinda converted but didn't": a false sync Error, 2026-10-14. FIXED ON DEV, not in game

A user's output folder: the conversion completed, and its one Error said 65 parameters were past
the 3200-bit cap (4856 counted). 63 of them were floats a clip drives (classID 95 curves). The
client never spends a bit on those: `CreateParameterDefinition` allocates only when `IsSynced`,
which is false for `isControlledByCurve` (read from the client 2026-10-14). The real tally was
2840, so nothing was dropped in game. Two causes, both fixed:

- The compressor strip's restore took every animator parameter, clip-driven floats included. It
  takes expression parameters only now, and never a clip-driven one (`CompressorSlotTest`, 3 new
  cases, all 8 pass in batch).
- Our budget check counted clip-driven floats, and so does the CCK's. `CheckSyncBudget` and the
  YAPS socket tally skip them now (`CvrParameterNames.CurveDriven`); the report's CCK-count line
  says its number includes them, and the README has a troubleshooting entry.

Two smaller findings from the same folder, both FIXED ON DEV 2026-10-14 (overnight):

- **A 59 MB VRCFury FX controller was rehomed.** "Nothing references it" was WRONG, withdrawn:
  four rehomed materials point at textures stored inside it. That bake kept four textures (two
  4096 and two 2048, DXT1, not readable) as sub-assets of its FX controller, and the texture
  rescue copied a texture's whole container, so the controller came along; most of the 59 MB is
  those textures as hex text, the rest the controller's own layers and 305 clips. Now a container
  of nothing but textures is still copied whole, and a 2D texture inside anything else is cloned
  on its own; other kinds (cubemaps, arrays) still take the whole copy, since only a 2D clone has
  been measured. The 2026-09 rescue avoided cloning on the grounds that a non-readable texture
  would clone blank; measured, it does not: `TextureRehomeTest` reloads both copies from disk and
  compares them with the originals on the GPU, 0 of 4096 pixels differ, for a texture reloaded
  from disk and for a live one made unreadable in the same session and never reloaded; format,
  mip count, filter and wrap match too, and the clone stays not readable.
- **The survey listed the scaler's own `#AvatarScaler/One` and `#AvatarScaler/StepSize` as dead
  features.** The survey's scaffolding pattern now takes the `#AvatarScaler/` prefix, with the
  `#` required, so an avatar's own `AvatarScaler/...` parameter is still surveyed.

### README audited against the code, 2026-10-06. Both loose ends FIXED the same day

Every section was checked against the code by one auditor each, plus sweeps for labels, defaults,
feature coverage and links, and every proposed edit was re-checked by a skeptic before it went in
(246 edits proposed, 4 refuted, then 42 wrap and wording fixes from a final review of the diff). Wrong
labels (13), defaults, features that changed, avatar-specific details and outdated version history were
corrected, and what 4.7.x added (the tester's Physics card, Find a setting, Open in the CCK Animator
Tester) is now described. The header screenshot is a fresh 4.7.1 render, `docs/images/window-471.png`,
made by UiOffscreenProbe with `-captureRename "Generic Avatar" -captureTile 4000`.

Found and fixed on Joe's word:
- **Keep the OGB / PCS haptics contacts was shown in the public window, where it could never act**
  (without the add-on the converter forces *Remove*). Its sibling *Keep OGB haptics synced* was already
  inside `#if AVATARBRIDGE_YAPS`; now the whole *Opt-ins* section is, since every tick in it acts on a
  converted penetration system. Shipped in 4.7.2; README rows say "(YAPS add-on)".
- **The 4.7.1 release notes said "press Build once on each plug and socket".** No control has that
  name. The published notes now say *Tools ▸ YAPS ▸ Setup*, **Bake every plug and verify**, which bakes
  every plug and builds every socket.

### The UI redesign, 2026-10-04: seen in offscreen renders, not yet used by a person

Every window moved onto one kit (Editor/UI): tokens for spacing, type and colour per skin, cards with
nested and accent variants, sentence-case section headings, notices in place of HelpBoxes, segmented
controls (gestures, stances, motion presets), strong / secondary / danger buttons, and a fixed label
column. The tester's Physics card got a summary (solvers, health), a selected-chain panel with live
fields and Keep, and empty states. Four rounds of rendered review (Dev/Probes/UiOffscreenProbe.cs, dark
and the editor's real light theme, edit and Play mode) fixed what looked wrong, and every README-quoted
label stayed verbatim. *(Withdrawn 2026-10-06: the README audit below found 13 quoted labels that did
not match, several older than the redesign.)*

What a render cannot show, so is unverified: hover and press feel, keyboard focus, the selected-chain
fields' live values (the offscreen panel does not run Unity's binding, so they read 0 there), and
anything that only happens in a real Play session. Nits the last review left: pale light-skin chips,
one row in the tester's Avatar menu card out of the label column, info notices using a "!" icon, YAPS
steps 2 and 3 nearly the same orange, and the footer reflowing after a conversion.

### Corpus 414 after the audit: four regressions found and fixed on dev, 2026-10-04

Run 414 (YAPS profile) changed all 83 digests against 412, as a batch that size must. Every changed
signature was accounted for. Most trace to intended fixes: the height scaler's parameters became local
`#AvatarScaler/...` (the sweep's parameter count drops by exactly those three), false blend-tree
warnings went, texture-cost wording changed, VRCFury's parameter-math layer carries the no-muscles mask,
and rigid accessory chains that used to be deleted as helper rigs now get a cloth.

Four were not intended, and each is fixed and verified on a subset (414s, 414b):
- **"clip(s) LOST paths that existed before conversion"** appeared on 18 avatars. Every lost path sat
  under a system the converter deletes on purpose (the world-scale detector of an SPS setup). The audit
  had started seeing those paths correctly; deliberate deletions are now recorded
  (`BridgeContext.RemovedPaths`, `SystemStripper.Delete`) and neither the clip audit, the path repair
  (which could otherwise move such a curve onto an unrelated object of the same name), the constraint
  report nor the contact report counts them.
- **A contact toggle stuck its colliders off.** The new collider-curve mirror fed collider hosts into
  the zone balance pass, which stripped the one lower-layer write that turned them back on. A rest-only
  write is now kept when every layer that moves the binding sits above it.
- **Numbered hand layers lost their AnyState self-restart guard** (and the other toggle passes) as a
  side effect of the finger-mask fix. `IsProtectedLayer` is exact-name again; the finger fix lives in
  the hand-pose audit and holds.
- **Unskinned grab-and-stretch handles were deleted as helper rigs.** A chain that moves no rendered
  vertex and drives no constraint is left alone again, and the helper-rig report no longer claims
  constraints where none were removed.

Also: the toggle sweep threw on 11 avatars in every run, so their toggles were never compared; it now
sweeps them, and an avatar with nothing to sweep says so. Still open: one avatar's sweep now refuses
`ScaleFactorInverse` where it refused `EyeHeightAsMeters`; both are game-driven, and the scaler change
explains the swap, so it reads as expected but has not been traced line by line.

### Corpus 415: tree toggles whose "off" child animates only dead paths. The five FIXED ON DEV 2026-10-14, one shape open

Avatar E swept for the first time (it threw before) and reported 9 stuck. None is a regression: its
controller matches the baseline except for the no-muscles mask on three renamed toggle layers, whose
clips carry only `m_IsActive` on paths already missing in VRChat and no object-reference curve.
- **Three were the sweep's own misreading** (VRCEmote, Collar, DPS). A 1D tree resting at 0.5 between
  children that animate different blendshapes re-blends against the defaults each Drive's rebind
  captures, so the shapes crept 50, 75, 87.5... and every drive charged the creep to its parameter.
  The sweep now makes two no-op drives first and leaves whatever still moves out of every verdict; the
  digest prints it as `unstable at rest:`. Rerun to confirm.
- **Five are real and pre-existing.** 1D toggle trees whose off child was authored for another body and
  animates only dead paths. `IsCurveless` sees curves, so `CollectToggleTrees` never offers that child
  to `FillEmptyTreeSlotsWithRestoreClips`, and in CVR the shape stays where the on child left it.
  Candidate fix: count a child as empty for that pass when no binding resolves on the target.
  **FIXED ON DEV 2026-10-14 (overnight).** For the restore pass, `CollectToggleTrees` counts an
  off child as empty when every binding is dead in VRChat: missing on the avatar (by animation
  path, `FindByAnimationPath`, so slash-named objects count as present) and missing in the source
  too, or removed with a stripped system, the test `AuditClipBindings` uses. A path the
  conversion renamed waits for `RepairClipPaths`, and an object whose component a converter
  replaced (a PhysBone's enable curve) for `RewirePhysicsToggles`, so neither counts; nor do
  hashed paths or Animator bindings. By path, never by reading values: a live material property
  reads as missing through the value API, and treating that half as empty would replace a
  working off. The hoist pass keeps the old test. `TreeToggleRestoreTest` covers three: an off
  half on dead paths gets the current value; one on a renamed path, and one on a live material
  property, are left alone. Subset runs 416v and 416w (after the review fixes) on
  Avatar E: stuck 5 to 0 (five body toggles), responded 9 to 10, the five
  off slots now restore clips, nothing else in the controller moved but the number on one
  existing restore clip. Not worn in ChilloutVR.
- **One is the other shape** (now reported as unstable at rest): a slider's two children each animate a
  property the other does not. No pass covers it, and the restore passes exempt tree-driven bindings
  by design, so it needs a decision first.

### Corpus 416, the first full run since 4.6.6: three side effects fixed on dev, 2026-10-15

Run 416 (YAPS profile, dev 8ffe549) against the 4.6.6 baseline. The batch Unity crashed at avatar 74
of 83, a native SIGSEGV in the allocator while a shader package's import hook searched the asset
database during one of the converter's `CreateAsset` calls, at about 24 GB of process memory. The same
avatar converts in a fresh process, so the eleven it never reached ran as a subset (416r).

Every other changed shape traces to a commit on dev:
- `#` on parameters only the animator reads, after the parameter compressor's layer is stripped
  (0e633f8); contact parameters keep their sync through `ContactParameters`. The `_cvr` clip clones
  follow from it, and so does one avatar losing its scale feed layer: the compressor had declared the
  scale family, nothing read it (the feed's own driver was the only reference), and the restore no
  longer keeps an animator parameter the menu does not list.
- Cloth roots on each branch's first bone (1598869), the extra per-chain approximation lines (f9b7815),
  the new settings lines, plural wording (04ac9df), the scaler survey warning gone, and sweep lines that
  move where a parameter went local.
- VRCFury's play-mode hook throws "The Playable is null" from every graph it walks; 415 had 4934 and
  416 has 4887. It is editor state, and the sweep's "threw while driving" lines on four avatars come
  with it while their counts stay the same.

Three were side effects, all fixed and measured on subsets (416t, 416y):
- **The Additive and Gesture layers vanished when the humanoid rig was rebuilt.** Two avatars lost the
  stock additive idle, and one its `CVRParameterStream` with it (TrackingType was its only reader). A
  probe (`LayerBakeProbe`) showed VRCFury's bake hands back all five layers; a per-pass trace showed
  "Humanoid rig skeleton" (eb39f19) leave three. Both rig rebuilds set `animator.avatar = null` before
  `BuildHumanAvatar`, and the descriptor's inspector (`EnforceAnimLayerSetup`) deletes the human-only
  layers from an avatar with no humanoid rig, then re-adds them empty once it has one. The jaw unmap
  has detached the same way since 4.0.3, so a released conversion that unmapped a jaw could lose both
  layers too. `JawUnmapper.Detach`/`Reattach` keep the layers across the gap for both. After the fix,
  all three test avatars keep five layers and the two that lost the idle match the baseline's layer
  list, stream and sweep. The trace is gone; the probe stays.
- **Texture costs counted slots the shader does not have.** 0a656ae read a material's textures by name
  to avoid a crash, and `GetTexturePropertyNameIDs` lists every slot the material ever saved, under
  any shader it had before or a shader that is missing from the project. Nineteen avatars' download
  figure rose, one from 224 to 378 MB, and twenty dead renderers on a material whose shader is missing
  went from "frees nothing" to "34.3 MB to reclaim". `TexturesOf` now skips ids the current shader
  lacks (`HasTexture`), which keeps the crash fix. Every texture figure on the four retested avatars
  is back to the baseline's.
- **The YAPS readout warned it could not carry the squash block** ("could not carry 18 ...
  `_YAPS_DentPower`") on fifteen avatars. The patch adds those properties to every shader and no
  readout cell reads them, so `YapsDebugOverlayBuilder.Copy` drops them from the missing list. Gone on
  the retested avatars, nothing else moved.

An idea it raised, not measured: a material's leftover slots may still ship in the build, since a
build follows every saved reference. If a build does carry them, the weight report undercounts and
could offer clearing them as a saving. Measure with a real CCK build before claiming either way.

**The crash answered with chunking, 2026-10-15. ON DEV.** `run-corpus.sh --per-process N` (default
30, 0 for one Unity) caps each Unity's conversions; the runner exits 4 at the cap, and the launcher
relaunches with `AVATARBRIDGE_RESUME=1`, which keeps `Current/` and reads back the digests already
written. A Unity that dies after making progress is relaunched the same way, so the avatar it died
on gets a fresh process; one that makes no progress stops the run. Parts append to the one log and
the last writes the summary. Tested on four avatars at `--per-process 2` with a deliberate kill
after the first digest: killed, resumed, stopped at its cap, finished in three processes with one
summary and the partial marker cleared. Avatar O, converted in the resumed process, matches 416t exactly;
the other three differ only by db7ade2's texture-slot fix, against digests made before it.

### Corpus 417, the 5.0.0 run: two regressions, one fixed on a branch, 2026-10-16

Run 417 (YAPS profile, dev 045f987, parts of 30, unthrottled) against 416. Read so far: 60 of 88; the
rest once the run ends. `CorpusSettings` turns squash on for the first time (e8ec36f), so every squash
line in it is new from the flip, not from squash code. Explained by commits: three new settings lines,
two more Converted entries on every avatar (the changes list and the upload check), the squash
components, textures and 20-triangle helper mesh where it built, GoGo's flight scale layer gone, fewer
cloth solvers from grouping, and SPS's option rows out.

Regressions:
- **C1: SPS's option rows took the whole SPS layer family.** 045f987 strips `VF80_legacy` and
  `VF80_stealth` by name, and the fury-id harvest took id 80 from them, so every `[VF80]` layer went:
  the creator's depth animations, auto mode (and its AAS entry), Init Behaviours, Auto Socket
  Comparison, Socket Exclusivity, and on five avatars the shared FrameTime Counter that face-gesture
  smoothing needs. Seventeen avatars; the restore filler's warning moving on them (C2) is the same
  cause. **FIXED on branch `fix-417` (97d6b6a), not compiled**: the harvest skips the option rows,
  `SpsOptionStripTest` pins both sides. Merges after the speed subset matches 417, since the subset
  holds an SPS fixture.
- **C5: the texture advice shrinks when squash is on.** Squash copies a material per renderer slot.
  `AvatarWeight.Densities` keys area by material and keeps a texture's lowest density with that one
  copy's area, so the "across X m²" figure covers one share and the reclaim advice falls (one avatar
  6.07 to 2.61 m², another's reclaim 29.4 to 22.4 MB). Nothing on the avatar changes; the card and the
  report misstate it. Fix: merge the area of materials with the same textures and scale before the
  density, in Core, since `Editor/Yaps` is pruned from the public package. Open, after the gate.

Time: across the first 60 conversions squash took 81% of all pass time (22,718 of about 28,150 s;
median conversion 291 s, longest 2,559 s). Compiling its shaders together is the lever above.

### The audit of 2026-10-03: about 210 findings fixed on dev, not verified in game

**What was done.** A read of the whole codebase against its own claims, in four waves: 177 findings
(166 fixed, 6 partly fixed, 5 skipped), a review of those fixes whose real points were repaired, the
items left for an author decision decided and built, and a last review of that round. By area: the
animator merger 23, YAPS 55 (converter 21, setup tool 27, shaders 7), physics 14, analysis and the
cost card 17, the window and the Toolkit 16, the conversion pipeline 11, assets 9, face tracking 9,
docs 5, build scripts and the Dev harness 18. README updated in the same pass. Compiled, not run in
ChilloutVR and not through the corpus.

**What the open items became.** All fixed on dev, compiled, none worn:

- **YC1-05, plug bounds.** A skinned mesh that is all plug gets a box fitted to its rest pose plus a
  plug length round the base, `updateWhenOffscreen` off; a body mesh keeps its box, grown only to
  the reach. ChilloutVR: several own-body sockets plus a partner's DPS socket, and a plug bending at
  the screen edge.
- **YC1-01, toggle-swapped variants.** Each copies every `_YAPS_` value from its slot's finished
  material, is recorded on the plug (`YapsPlug.variants`) and follows later knob writes, in both
  builders.
- **YS1-07, an adopted plug pinned to one slot.** The converter adopts with slot -1, marks the plug
  `converted` and records every slot and mesh it baked, primary first; re-bake and mirroring walk
  those records, extra meshes take the plug-wide tag, self and atlas values, and the Setup row shows
  each plug's own material. Needs a reconvert.
- **PHY-14, animated parent-constraint offsets.** Carried 1:1 (names and units measured), weight and
  offset curves remapped to the Unity source index, a curve on a skipped source dropped and
  reported, and the `ConstraintScaleRelay` guard in the same change.
- **YS2-02, depth-channel naming.** Toolkit Build keeps the synced name and also writes each name the
  author's layers still read, on one trigger; Clear trims back. ChilloutVR: whether two
  `SetFromPosition` stay tasks on one trigger both fire.
- **YC1-06, Remove and the bounds copy.** The original mesh is recorded (`YapsPlug.boundsFrom`) and
  Remove puts it back, keeping the copy while another plug shares it. Toolkit bakes only: plugs baked
  earlier and every converted plug keep the copy (the converter half is under Still open).
- **FT-04, one face-tracking rule.** `FaceTrackingParameters.cs` ships in `YAPS_CORE`,
  `AvatarFeatureDetect` delegates to it, and `FtPlainNames` is gone.
- **AM2-04, paths dead in VRChat too.** Repairs only paths in a source snapshot taken after
  `PrepareTarget`, and the broken-paths audit reads the same snapshot, so baked and in-place runs
  report lost paths as lost. Accepted cost: a bone renamed after its animation was authored is no
  longer repaired.
- **YC1-03, untraceable SPS owners.** Still no route back for a socket merged into a bone; an
  untraced plug now gets its own report line too. Rare and left: a merged socket under a bone
  carrying its own SPS socket takes that socket's tags.
- **UI-02, the folder-name rule.** The converter calls `CvrSetup.PrepareOutputFolder`, the settings
  key lives in `CvrSetup`, and the Convert card offers *Put the textures back* from the folder's
  record once the report is gone.
- **ANA-002, the dead-renderer test.** The survey reads clip-mode CCK controls' clips and the clips a
  shipped override controller swaps in.
- **DOC-04, the CCK's stock controller.** One resolver (`AvatarSurvey.ShippedController`) and one
  guard (`CvrSetup.SharedController`): YAPS layers and Free wins never write into it and say so, and
  Free wins swaps its copy in everywhere the avatar wires the original.

**Still open**, each with why:

- **PHY-03, native mode's ancestor rule.** A contact or collider host outside the switched-off object
  it belongs under still converts live in *CVR Native Targets*; dropping that needs the contact's
  enabled state recorded beside `ctx.ContactHosts`. Whether a switched-off host stops a DynamicBone
  collider needs ChilloutVR.
- **ANA-016, MeshCloth cost.** Counted as 0 transforms; needs a particle estimate and wording of its
  own. New work.
- **A native plug whose Root Bone is an empty under a bone** keeps the one-slot re-bake YS1-07 fixed
  for converted plugs. Not touched.
- **PHY-14b, animated offsets and rest values on position, scale, aim and look-at constraints** are
  dropped (reported). The SDK's reverse map in `AvatarDynamicsSetup.cs` gives their names. A rotation
  constraint's offset is measured from the live pose, so its animated offset stays dropped unless
  checked in ChilloutVR.
- **FT-02, face shapes after Face Tracking goes off.** The rig's off states are Write Defaults on with
  no motion, and ChilloutVR restores no defaults, so the last tracked shapes may stay. A zero clip
  would fight the native blink `EyeTrakingOFF` hands back and override the avatar's expressions.
  ChilloutVR: turn Face Tracking off mid-expression and watch the face.
- **FT-05, eye gaze on a rolled head bone.** The gaze empties hang off `EyeTracking.Frame`, turned to
  the avatar's facing, so the clips' angles should hold on a head bone that is not axis-aligned.
  Compiled only. Play mode or ChilloutVR: such an avatar looking down and left.
- **ASSET-FURYHEAD-REFS, skins bound to VRCFury's second head.** `FuryHeadFlattener.RepointReferences`
  moves anything naming `vrcfAlwaysVisibleHead` onto the head bone, but nobody has seen VRCFury bind
  bones to the copy. Convert such an avatar, read the report's count, check the skin keeps its pose.
- **YSH-13, the protocol-light black test differs between plug and socket.** The plug's
  `YapsClassifyLight` rejects `any(rgb > 1e-4) && a > 0`; `YapsPlugDepth` and the readout reject
  `dot(rgb,rgb) > 1e-4`, which accepts a near-black third-party light the plug refuses. The digit
  decode is shared now (`YapsLightDigit`), the colour test is not. Settle in game which one
  third-party lights need, then fold it into the helper.
- **The socket readout's plug cell shows the wearer's own plug** where the socket would exclude it.
  Author decision: an own-plug state in the cell, or a per-socket readout material copying
  `_YAPS_SocketNoSelfExclude`.
- **YSH-07, a skinned plug and the height slider.** `yapsScale` reads `unity_ObjectToWorld`, identity
  on a skinned mesh, so the bend may not follow the slider. Game first: a skinned plug in a socket at
  2x and 0.5x, Gap or Resolved-by view. The fix would be an avatar-scale property mirrored next to
  the slider, used in the per-vertex recovery too.
- **YSH-18, shadow and depth passes** resolve with different light and atlas state from the visible
  pass. Game: a plug bent through a marker light and one through the atlas, under a shadow-casting
  light. If confirmed, decide per pass, and add a README Known limitations line.
- **Remove leaves the slot's material-swap keys on the bake.** `YapsRemover.RemoveSocket` and the
  plug restore put the original material back but never repoint the swap keys, so a toggle that
  swaps that slot brings the removed bake back. Unbake already reverses them with
  `YapsSwapFollow.Follow(renderer, slot, baked, original)`; Remove should too. DONE 2026-10-15:
  socket, plug and the plug's other meshes all repoint the swap keys as they restore.
- **YC1-06b, a converted plain-mesh plug keeps the bounds copy after Remove.** `boundsFrom` has one
  writer, `YapsNativeBuilder.cs:73`. The converter bakes through `YapsBaker.Bake`
  (`YapsConverter.cs:201`), which makes the same `bounds.asset` copy, and `AdoptPlug` never sets the
  record; a later toolkit re-bake then reads the copy as the original. Fix: take
  `filter.sharedMesh` before that bake and set it on the adopted plug when the bake moved the
  filter, as the toolkit does. README "Taking things out again" says converted plugs keep the copy;
  change it in the same commit. Needs a reconvert. DONE 2026-10-15, as described.
- **The *Fit the preset to the PhysBone* tooltip and settings comment lag the code.**
  `FitToPhysBone` (`MagicaClothWriter.cs:622`) also raises the preset's four speed limits, and the
  README says so. The window tooltip (`AvatarBridgeWindow.cs:648`) names gravity, immobile and wind
  but not the speed limits; the `fitToPhysBone` comment (`BridgeSettings.cs:72`) names neither wind
  nor the speed limits. Code-only text change. DONE 2026-10-15.
- **Animators nested on child objects** are not walked by the survey, so a renderer only such an
  Animator switches on can read Dead and be stripped by *Fix it*. Author decision: it changes every
  `Layer.Bindings` count. FIXED 2026-10-15 without touching the counts: the Dead check reads what
  child Animators switch, see Next up.
- **AM1-10, the menu-name rename rule.** Whether `RenamePass` should trigger on
  `IllegalMenuNameChars` (`[^a-zA-Z0-9/\-_#]`) instead of its own list, leaving
  `VerifyMenuParameterNames` to assert. Author decision: it changes the names users get for menu
  parameters holding `.`, `!`, `&`, `+` or non-ASCII letters.
- **AM1-14, PlayAudio paths with a slash.** Whether VRChat plays a PlayAudio whose AudioSource object
  has `/` in its name is untested; one VRChat test before `ConvertPlayAudio` moves to
  `FindByAnimationPath`.
- **YS2-22, the tag-chooser key.** `YapsTagMenu.Parameter` and `YapsRemover.ChooserKey` hash the
  renderer path with `string.GetHashCode`, stable on Mono and randomised on CoreCLR. Before any
  runtime switch, move both to a fixed hash and keep the legacy key while saved values use it, since
  ChilloutVR restores by parameter name. Low priority.
- **Docs outside the README.** `YAPS-CLEAN-ROOM.md` still rests YAPS's independence on "no screen
  atlas, a contact channel", both since reversed: rewrite it around what YAPS does today (its own
  atlas protocol, DPS marker lights). `YAPS5.md` names `DefaultMaxLightEmittingSockets` and
  `YapsAtlas.Enabled`, both gone. The `ParameterMenuConverter` header still says its entries exist
  so ChilloutVR syncs the values.
- **Optional, no bug behind them:** an early out in `YapsDeform` before the full baked read (YC1-10;
  a compact layout would be a protocol change), one `PatchOnSimpleLit` for both builders (YC1-08),
  sharing the clip walk in `ConstraintScaleRelay` (PHY-13), a weights-based helper-rig rule shared
  with `ConstraintConverter.SkinningBones` (PHY-19, a behaviour decision), and the Toolkit's *Mesh
  bounds* button re-applying the avatar envelope to a separate plug mesh after a native bake.

### The stereo shader patcher, read end to end on 2026-10-03. FIXED ON DEV the same day, not in game

**What changed.** One analysis, `PlanStereo`, finds every vertex and fragment function named by a
pragma, in every pass and file, and the exact edits each needs; whether a shader needs patching
(`DeclaresStereo`), the one-eye warning (`StereoProblems`) and the patch itself all read it, so they
cannot disagree. Edits are planned on the text as read and applied from the end of each file back.
Two facts it rests on were measured first (`Dev/Probes/SpiFactsProbe`): Unity's generated
surface-shader code carries all four macros in every pass, and a duplicated instancing member
compiles in the default variant with `ShaderHasError` false and fails only with
`STEREO_INSTANCING_ON`. So the copy is now also compiled outright in the single-pass instanced
variant, every pass, through `ShaderData.Pass.CompileVariant`. That check found a bug old and new
code shared: the stereo member went FIRST in the struct, and a fragment taking its own system value
(a `VFACE`) failed in exactly the variant ChilloutVR draws; members now go last, as Unity places
them. `Dev/Probes/SpiPatcherProbe` builds a shader per fault and passes 32 of 32.

**One item deliberately not done:** a shader naming all four macros is not patched for a plain depth
read. `Dev/Probes/SpiSweepProbe` puts every shader of the corpus project (1092) through old and new
builds; counting depth sent nearly every locked Poiyomi Pro material to be copied, for optional
features that declare a plain depth texture. Depth reads are rewritten only in shaders patched
anyway, and only when every read takes the depth value alone (`DecodeFloatRG` of two channels would
compile and read wrong); otherwise they are left and the report says so.

The record as found:

A read of `ShaderSpiPatcher` with every claim checked against the code. From the code, none of it
measured yet:

1. **Multi-pass shaders get half a patch.** Only the first `#pragma vertex/fragment` is used and the
   setup lines go into the first matching vertex function, so an outline or ForwardAdd pass with its
   own vertex function stays one-eyed while the shader is reported patched.
2. **Copies overwrite each other.** Copies are named `<stem>_SPI` with names deduplicated only within
   one shader, so two shaders sharing a file or include name write the same file in `RehomedAssets`,
   and a later failed patch deletes a file an earlier good copy includes.
3. **Macros added twice.** A shader holding some of the four (instancing ID for GPU instancing) gets a
   second copy, a duplicate struct member.
4. **The toggle pass drops lines.** Swap-only shaders that are already correct or limited by a
   screen grab are collected and never reported.
5. **`.failed.txt` keeps only the main file** while the report says the failing line is in it.
6. **Unreadable reads as correct:** `DeclaresStereo` catches a throw and returns true.
7. **Depth reads: two exact spellings only**, and never for a shader that already names all four
   macros, which can still read a stereo depth array as a plain texture.
8. **`#include_with_pragmas` is never followed**, checked or copied.
9. **Surface shaders are refused and flagged as one-eyed**, when Unity generates their passes; a
   surface shader with its own vert/frag pass (an outline) is refused whole. To be measured.
10. **The compile check proves the default variant only**; nothing compiles `STEREO_INSTANCING_ON`.
11. **Docs and comments out of step:** the README and the diagnostics warning put the tickbox "in
   Advanced" (it is in Manual options, under Shaders); the README says screen reads are rewritten
   (depth only) and that an edited shader's recipe "is refused rather than guessed at" (a mismatch
   only adds a note); two header comments say locked shaders and shared-include structs are refused.
   *The recipe claim is moot, done 2026-10-03: the recipe machinery held no recipes and is removed
   (`ShaderFixRecipes.cs` deleted, its branches gone from `ShaderSpiPatcher`), and the README no
   longer mentions recipes.*
12. **The advisor counts renderers only**, so swap-only one-eyed shaders never raise its suggestion.

### Toggle style "CVR Native Targets" lost its toggles (issue #8). FIXED ON DEV 2026-10-01

Reported on 4.6.1 as every toggle dead in game. `ToggleNativizer` hands a plain object toggle to the
CCK: the objects go into the menu entry's `gameObjectTargets`, the layer is deleted, the parameter is
kept. `PruneDeadMenuEntries` then saw a declared parameter no layer read and deleted the entry, so
every toggle handed over left the menu; the report listed them as "Menu entry ... removed" in the
Animator section and as "toggled natively by CVR" under Native toggles, and nobody read the two
together. The survey made the same mistake for the ones a driver kept alive ("control that does
nothing"). Both now treat an entry with targets as read, through `AvatarSurvey.DrivesTargets`.
`Dev/Probes/NativeToggleProbe` converts one avatar with the native style: on the old build 8 toggles
handed over and all 8 pruned; with the fix 25 of 25 checks. The corpus profile uses Animator Layers,
so the corpus never ran this path. The report's other lead was a red herring: "VRCFury reported 2
error(s)" quoted Unity's own message about a transition testing a parameter its controller never
declares (the string is in `Unity.exe`), counted as Fury's because Fury was on the stack. It is a
Warning of its own now and no longer says not to upload; that branch is compile-checked, not probed.

### A chain's tip bone lands 100 times too far on a scale-100 armature. WITHDRAWN 2026-10-01: VRChat does the same

**Withdrawn, measured.** `Dev/Probes/EndpointScaleProbe` asks VRChat's own simulation, in Play mode:
two chains under a parent at scale 100, Endpoint Position 0.1, one with a sphere collider 5 m out
that only a tip in scaled units could reach, one with a collider just past the bone. The far collider
turned its chain 3.03 degrees, the angle a 10 m tip needs to clear it, and the near one 0.89 degrees,
where a 0.1 m tip would have needed about 11. So PhysBone reads Endpoint Position in the leaf's scaled
units, as the `_End` bone does, and the 7.7 m leash over a 10 m tip is the same 45 degrees. The
authored values were simply long in VRChat too. What remains is the other path: `DynamicBoneWriter`
passes the raw value to `m_EndOffset`, which DynamicBone reads in metres, so on a scale-100
armature its tips come out 100 times shorter than VRChat's. Nobody has reported it and DynamicBone
is rarely the target. **FIXED ON WIP 2026-10-02 (Joe's go-ahead overnight):** the value is scaled
by the chain root's lossy scale, so the tip's length matches; its direction still follows
DynamicBone's own transform, not each leaf's, which no single offset can match. Compile-checked in
the corpus project; not run, since DynamicBone's tip is read from its source (line 619,
`TransformDirection`) rather than measured. The original record follows.


Found in a user's report (4.6.4). `MagicaClothWriter` realises a PhysBone's Endpoint Position as a
`<leaf>_End` child with `localPosition = EndpointPosition`, so the value is read in the leaf's own
units. On an armature exported from Blender at scale 100 without applied transforms, each tip sat
100 times too far out: 0.1 became 10 m, 0.06 6 m, 0.05 5 m, 0.02 2 m, measured from the converted
prefab. `ApplyMotionLeash` measures the chain in world metres through those tips, so the swing bound
came out at 7.7 m over a 10.075 m chain and never binds; the chains swing further than the source
allowed and the long tips stiffen them. `DynamicBoneWriter` hands the same value to `m_EndOffset`,
which DynamicBone applies through `transform.TransformDirection` (rotation only, so metres), so the
two physics paths already disagree on the same avatar. The authored values only make sense as
metres. Not yet settled: how VRChat's PhysBone reads Endpoint Position on a scaled bone. Probe that
before changing anything; if it is metres, the tip's local position is the value over the leaf's
lossy scale, and the leash follows. The same report's Play-mode collapse, meshes pulled into lines
while edit mode looked right, was the user's own setup and not this.

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
- Side finding: the weigh pass advised shrinking the bake texture ("8192x280 ... 1024x1024 is the
  same picture"). It is data, not a picture. Nothing acted on the bake itself (the texture pass only
  touches textures with an importer), but the same suggestion drives the texture pass, so an
  imported data texture (point filtered, no mipmaps) could have been resized on convert.
  **FIXED ON DEV 2026-09-23:** a data texture gets no density and no suggestion, in the advice and
  the texture pass alike. `Dev/Probes/WeighDataProbe.cs` on the reporter's avatar. Not in corpus
  408 (deployed before), and the corpus runs with the texture pass off.

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

**BUILT ON DEV 2026-10-15, editor only, NOT YET IN GAME** (Joe's call, line 25: bake at the
slider's menu default). `YapsRestPose.Apply` builds a bare transform copy of the avatar, drives
the shipped controller on it with every parameter at its default for a second, and sets each
size bone to the scale it lands at, with Undo. Both builders call it before `LargestSize`; the
converter reports the move, the toolkit adds a note. No clip is touched and nothing of the
avatar's runs. `BakeScaleCheck` gained four cases (motion time, a 1D tree, a bool left on in the
scene, a resting state that animates nothing); all pass, 2026-10-16. What remains, read from code, not
measured: away from rest the tracker light's intensity and the tip pointers still carry the rest
length; the ratio is exact only for a uniform scale on the chain root; an IsLocal-gated size layer
is sampled as a remote copy would load; a CVR profile restores a saved size by parameter name.

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
frames rather than a second shaft. It cannot be two different lengths. **"It costs nothing" was
wrong** (corrected 2026-10-15): one vertex has one position per draw, so the second frame needs a
second copy of every pass the plug's material has, shadow caster included. That is the plug
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
  5.0 wearer would be blind to each other until both update.
- **The light tier cannot carry it at all**: one socket per light path is the whole budget.
  Portals are atlas-only, and the README would have to say so.
- **Authoring:** a socket field naming its exit socket, both on the same avatar, written by the
  avatar's own atlas writer, which can see both bones. A portal between two people is out: neither
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

**A remote twin.** *BUILT ON DEV 2026-10-15 (`CckAnimatorTester.Remote.cs`), compiled, NOT YET RUN
in Play mode.* Remote view's *Show a copy as others see it* instantiates the live avatar beside
itself, removes its parameter streams, sets `IsLocal` off, and every editor tick copies only what
syncs: `BridgeDiagnostics.BudgetOrder`, now the one walk both the sync-budget diagnostic and the
copy read, plus the game's core parameters bar the per-viewer three. Not built: contacts and
`CVRAnimatorDriver` do nothing in the editor on either copy, so the driver clobbering remote
copies is not shown; the copy's YAPS lights sit in the same scene as the wearer's sockets, so a
socket within reach of the copy's plug can answer it; and a Play-mode probe for the copy.
The strongest idea on this page. Spawn a second copy of the converted
avatar beside the first, driven *only* by parameters that actually sync, and let the tester
walk between them. Every wearer-only bug becomes visible in the editor: the socket shapes
that moved for nobody, the toggle that never left the wearer's machine, the smoothing rig
that froze at its defaults. The Animator Tester already snaps `#` locals to their defaults
for its Remote view; this is that idea given a body.

**A conversion diff.** *BUILT ON DEV 2026-10-15 (`ConversionDiff`).* The regression harness writes a digest per avatar and compares runs.
Users get the same tool: convert, change a setting, convert again, and read what moved.
"Reconverting on a new release" stops being an act of faith.
What was built: every conversion writes `.last-conversion.txt` into its output folder (settings,
version, status counts, every non-Converted report subject, layers with states, weight, blending
and mask, parameters, menu entries, component counts) and the next one compares, with one
*Changes* row and a *Since the last conversion* section. Converted subjects and all details are
left out, since their wording churns; proven by `Dev/Tests/ConversionDiffTest.cs`. Not built:
any per-object detail (which renderer, which clip), and the HTML report shows only the row.

**A toggle sweep, shipped.** `Dev/Corpus/ToggleSweep.cs` flips every menu toggle on and off
and reports anything that did not come back. It found real bugs. It should be a button in
the Toolkit rather than a thing only the harness runs.

**A budget planner.** The sync tally exists in the report; make it interactive. Tick the
features you want, watch the bits, and let it name the cheapest thing to drop. Most avatars
that go over do it by accident, in a menu they never counted.

**An upload preflight.** *BUILT ON DEV 2026-10-15 (`UploadPreflight`), NOT YET TRIED AGAINST A
REAL UPLOAD.* One list, run before uploading: these diagnostics, the CCK's own
validators, the texture flags, parameters past the cap, missing metas. Everything that
currently fails at the last moment, in an order that says which to fix first.
What was built: the five rules that hide the CCK panel's Upload button (missing scripts, no root
Animator, max size above 8192, mipmaps without streaming, sound not loading in the background),
each gathering exactly what the CCK gathers, run at the end of every conversion and first in the
Toolkit's card, now *Check before upload*. "Missing metas" became the class that actually loses
work: references to objects that are neither saved assets nor inside the avatar, which the
prefab the upload saves drops (`Dev/Tests/PreflightTest.cs` saves one and looks). The parameter
cap was already in the diagnostics. Left: the CCK's non-blocking warnings (viewpoint at zero,
generic rig, legacy blendshape normals), and a Fix button of our own, since the panel has one.

**Advanced Tagging, inferred.** Content tags are currently listed as "not converted, set
them yourself". An avatar carrying YAPS knows what it is. Offer the tags rather than
assuming them, and nobody uploads an NSFW avatar untagged by accident.

**Incremental reconversion.** Whole-avatar every time today. Keyed on source hashes, the
passes whose inputs did not change could be skipped. Worth it only once the corpus can prove
a skipped pass and a run pass produce the same output.
Step one, 2026-10-15: every conversion logs `[AvatarBridge] Pass times (ms): ...` and `After the
passes (ms): save N, report N` (log only, so digests do not move). The next full corpus run says
which passes are worth skipping; nothing is skipped until then.

---

## GoGoLoco's poses, through ChilloutVR's own front door
*Status: BUILT ON DEV 2026-10-15, editor and corpus only, NOT YET IN GAME.*

**What was built.** `LocomotionGrafter.PlaceGogoEmotes` runs last in the merger, with every clip
name final, and `FillGogoEmotes` writes the pairs into the override controller before
`SaveOverride` copies them. Up to eight: the creator's own clips from GoGo's Action layers (a
`VRCEmote` Equals condition, not stock, not `go_`, humanoid, on at most two values, which is what
tells them from GoGo's placeholders), then a fixed eight of the poses GoGo ships and nothing in
GoGo plays (`go_sit_crossed_leg`, `go_seiza`, `go_side_sit`, `go_long_sitting`, `go_kneeling`,
`go_squat`, `go_laydown_side`, `go_laydown_chill_idle_long`), found beside the avatar's own
`go_stand` so a project with several GoGo installs takes the right one. Copies keep only
`Animator` curves (ChilloutVR never restores Write Defaults); a label shared with a locomotion clip
gets " Pose" added; a loop or a pose of 0.1 s or less loses the condition-less exit-time
transition on its `EmoteN` state, so it holds until movement cancels it. Setting `gogoEmotes`, on,
offered only while GoGo is removed. The Action transplant now skips GoGo layers that the strip
removes: with Action ticked it had moved GoGo's pose states into ChilloutVR's locomotion layer
(`[AB] Prepare Emote`, `[AB] Hip Drift`, `[AB] Puppet`), where nothing drove them.

**Corrections to the record below, found while building (withdrawn, not deleted):**
- WITHDRAWN: "Sixteen, not fifty" and the toggle slots for held stances. The converted controller
  keeps only `Locomotion/Emotes` and the two hand layers, so there is no Toggles layer, and the
  CCK's Toggles mask moves no body part anyway. Eight emote slots; a pose is held by removing the
  state's exit-time transition instead.
- WITHDRAWN: "a user pressing Create Controller would wipe their locomotion". The CCK copies
  `baseOverrideController` and re-points it, so the overrides survive.

Open:
- In game: the eight labels show in the wheel; a sit holds until the stick moves, on desktop and
  in VR; hips on the floor; hands do not fight the pose; a second client sees it start and end;
  after the CCK's Create Controller and an upload the labels survive.
- Left behind on GoGo avatars by the strip: `[FX] [VF#] Flying`, checked 2026-10-15 on avatar
  Q's digest, present in the baseline too. It plays `go_flying_*` clips on `Go/VRCEmote` 120 to
  124 and `IsLocal`; with GoGo's menu gone nothing drives it, so it idles in its default state.
  Not caught because the name has no GoGo hint, `[VF#]` is not a numbered Fury id, and GoGo's
  parameters are under the 0.6 share the FX test needs. Not yet known: what the idle clip
  animates, and whether the strip sees `Go/VRCEmote` with or without its `#`. Instrument before
  widening the FX test, which a creator's own layer reading a `Go/` parameter would also match.
  **Answered and FIXED, 2026-10-16:** the strip runs before the local rename, so it sees bare
  `Go/` names; the layer scores 2 of 6 references (the rest are `IsLocal`, `InStation`, `AFK`,
  `VRMode`). Its clips animate GoGo's flight prefab (`Beyond Prefab/Fly`, a RotationConstraint,
  a collider centre, `Chairs` active), whose stations and colliders are already gone from the
  output, leaving bare transforms the idle layer holds. Every clip it plays at strip time is
  `go_*` (the two "restore" clips are the merge's own placeholders, added later). New rule in
  `RemoveLayers`: a layer reading a stripped `Go/` parameter whose clips are at least 0.6 `go_*`
  goes too. `GogoLayerStripTest` pins it, with a creator layer reading the same `Go/` value
  through clips of its own that must survive. Not yet run in batch; the empty `Beyond Prefab`
  transforms stay, harmless.
  Corpus subset `gogo1`, 2026-10-16, 29 of 29 changed against the 2026-10-04 baseline, every
  change explained: GoGo layers, states and `go_*` clips gone, `EmoteN` slots named, the new
  settings lines, local `#` renames, MagicaCloth roots split per branch, squash components and
  warnings, report wording. One digest gained "threw while driving, writes lost:
  ArgumentNullException": the stack is VRCFury's editor-only Harmony patch on
  `Animator.SetInteger` (`FixAnimatorPreviewBreakingInPlayModeHook`) throwing when the CCK's
  AnimatorDriver sets an integer during the sweep. Not AvatarBridge and not in game, but it
  makes that avatar's sweep lose writes; the sweep should drop the patch or skip the avatar.
  **WITHDRAWN, the same day:** `[Additive] Idle` is VRChat's stock additive layer (`proxy_idle`,
  `TrackingType`), not GoGo's.
- Which eight library poses, and their order: a choice, open to Joe.
- **NOT BUILT: the names are not sanitised** (constraint 2 below), found 2026-10-16 while writing
  the in-game checklist. A creator's clip keeps its own name as the label (`PlaceGogoEmotes`, the
  `picks` query), so one named "Dance" or "Wave Emote" lands as is and would mute both hand layers
  while it plays. The library labels are safe. Fix: rename a label containing "Emote" or equal to
  one of the stock eight, before the " Pose" loop so the result stays unique, and say so in the
  report. It changes output, so it waits until the speed-merge subset matches corpus 417. Checklist
  item S3.4 tests it in game.

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

## The animator merger: what is left after 5.0.0
*Status: open. The rest of this record is in `archive/Closed-2026-10.md`.*

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
