# Unfinished

The single list. Every open piece of work lives here and nowhere else. Check this document
before starting on a new idea, in case something similar is already on it, and whenever the
question is "what's next" or "what is left to do". An item leaves this file by shipping, or
by a decision recorded in `archive/`. The standing rule still outranks everything on it: a bug
somebody hits while wearing an avatar comes first.

Transport facts and their build order live in `YAPS5.md`; solver maths in `SolverCalibration.md`;
what SPS code may be looked at in `YAPS-CLEAN-ROOM.md`. Finished records are in `archive/`.

AvatarBridge 5.0.0 was published on 2026-11-18 (tag `v5.0.0`). Everything recorded as done up to
that day shipped in it. An item marked SHIPPED stays only for what its heading says is still open;
its full record is in `archive/Closed-2026-11.md`.

Laid out by what an item waits for: the game, open work, 5.0.1, then the ideas. Item numbers are
the old ones, kept so commits, the archive and the in-game checklist still point at the right record.

## Waiting on the game

Shipped in 5.0.0 (or waiting on ChilloutVR's GS2), open only until somebody sees it in ChilloutVR.
Shortest first; the **Check** line under each heading is the thing to do. Steps are in
`Dev/Release/ingame-checklist-5.0.0.md` (local). Owed by Joe right now: items 20, 41, 42, 46 (his
call), 47, 50 and 51; tester fluffybat has rc23 for item 51.

### 56. In-game checks left by the items closed on 2026-11-18. Joe to check in game
**Check:** each line below once in ChilloutVR (or VRChat where it says so); a failure becomes a new item.

The code side of each item is done or decided and archived in `archive/Closed-2026-11.md` under
its number; only the look in game is left here.

- **(15) PlayAudio on a slash-named object, in VRChat.** Does VRChat play a source whose object
  name has a slash? If not, the converted avatar plays a sound the original did not (checklist S6.9).
- **(45) Editor, 2022.3.58f1:** press Squash and a row's Customise in the YAPS setup window; the
  Console stays free of the `SerializedObjectBindingToBaseField` NullReferenceException.
- **(32.12) Nested PhysBone chains on MagicaCloth 2:** an avatar with a PhysBone inside another's
  chain (a hair strand on a hair chain, a tail tip with its own bone): the inner chain swings by
  its own settings and the outer chain does not drag it stiffly.
- **(32.6) A user's own shader below target 4.5 on YAPS Standard:** the squash still moves with
  the cloth.
- **(32.1) Editor Play mode:** the Avatar Tester's remote copy starts from rest (`TwinRestTest` passes
  in batch; never looked at by hand).
- **(30.1) The wearer's own hand pressing once, not twice:** press your own live tissue with your
  own hand; the dent is one hand deep, not doubled.
- **(30.4) The press's lighting on a smooth-shaded avatar:** no ring round a dent with the slope
  on (only toon ramps have been looked at).
- **(31.6) Hands that carry only a MagicaCloth collider,** with Other Colliders on and a second player
  who shares colliders: turn on **Jelly probes (debug)**; a marker moved and red is a pass, moved and
  white means the step never read it, unmoved means the client gave the probes no collider.
- **(31.8) A top-and-bottom squeeze with the breast cloths' full-size balls,** seen by a viewer who shares
  colliders: the part does not ring on ("one touch answered twice").
- **(48) Shader Filtering on, a live tissue avatar:** the region draws at rest, not torn, when the
  filter cuts the lattice corners' rolled loop short.
- **(48) Point lights on an older Poiyomi avatar** (`VERTEXLIGHT_ON` now per pixel): they look as
  before.
- **(31.5) A converted chain whose PhysBone has a radius curve** (MagicaCloth 2): it collides like the
  original in VRChat, thinner toward the tip, not as a full-size ball along its length.
- **(40) Smaller Arms and Thickness on the plush, in game:** does either deform show, for you and for
  others? If neither does, try one upload without YAPS to tell the add-on from the avatar.
- **(49) Walking and running with live tissue:** a part nobody touches does not stretch; a hand, your
  own hand, a grip or the pair pressing wakes it; nothing at landing after fast flight.

### 35. A big part's lattice by its volume. Joe, 2026-11-10. SHIPPED in 5.0.0, Joe to judge in game
**Check:** a big part on the finer lattice feels and settles as tuned, not softer or slower.

The lattice aims at 2 cm cells, 600 up to 2400 (record and the cost table in
`archive/Closed-2026-11.md`); Kar's breasts went 10x10x10 at 4.6 cm to 15x14x15 at 3.0 cm.
- **Open, watch in game:** the step's fixed iterations spread a press fewer metres a frame on a
  finer lattice, so a big part may feel softer or settle slower than tuned.

### 34. A hard squeeze: the palm's dish and the flatten. Joe, 2026-11-10. SHIPPED in 5.0.0, Joe to judge in game
**Check:** two players squeezing: a palm dents as a dish with a ring and the pair closes in; slaps, pokes and grabs look right.

Built 2026-11-10 and approved on film (record in `archive/Closed-2026-11.md`): a palm dents as a
dish with a raised ring (`YapsJellyDish`), the whole flattens along the palms' line (`_Squeeze`
0.6), the pair closes in under a squeeze (`_PairKeep` 0.55). Since item 51 (2026-11-17) `_Squeeze`
is 0 on a part carried on cloth, so the flatten runs only on a part no cloth carries; the dish is
on every part.
- **Open:** Joe in game, two players squeezing. The slaps, poke, one hand pushing and grabs changed
  too (same palm path) and were judged only on film.
- The step's fresh fxc compile time was not measured after this change.
- The jelly prop's check limits may be keyed to the old dent.

### 36. The jellied body's compile and the awake step's cost: the looks it left. Joe, 2026-11-10/11. SHIPPED in 5.0.0, Joe to check in game
**Check:** a jellied body in a foggy world, at mid distance, and at 120 and 144 fps looks the same as near at 90.

Everything built is in 5.0.0 and recorded in `archive/Closed-2026-11.md`, with what was priced and
not built (the sparse lattice, one chain for every part, half-precision state) and what was
rejected outright, so none is proposed again. Left:
- **Fog in the pixel program:** a look in a foggy world (FogCutProbe matched 144 shader families to
  the pixel in the editor).
- **`_Mid` 45, the mid-distance rate:** Joe in game at mid distance. On film he said "looks
  basically the same, nice cost saving"; the squeeze was a little more uneven (1.14 against 1.03).
- **`_Cap` 72:** every other frame steps at 120 and 144 fps; on film only the slaps' ringing showed
  it (uneven 1.03 to 1.21). Not looked at in a headset at those rates.
- **Re-time the awake step on the RTX 4080 on a quiet machine:** the last cut read within noise
  with ChilloutVR open (iGPU 0.862 to 0.806 ms at 90 fps, 1.273 to 1.235 at 60).
- One chain for every part is worth reopening only if the CPU side (a provider and a parser
  readback a part in the client, which the probe does not see) proves dear.

### 26. A fist closed with the grip read 0 in GestureLeftWeight. A user's report, 2026-10-26. SHIPPED in 5.0.0, Joe to check in game
**Check:** on a grip controller a half squeeze half-closes a weight-driven fist and a second client sees it; on Index the trigger still does (R13).

A user: a grip squeeze in VRChat became a trigger squeeze after conversion. The converter fed
GestureLeftWeight/RightWeight from the parameter stream's TriggerLeft/RightValue on every
controller. VRChat's weight follows whatever closes the fist: the grip on Quest and most
controllers, the trigger on Index (its docs say "analog trigger"; its feedback board says the grip
on most). ChilloutVR makes the same choice per controller for its own
GestureLeft/Right, 0 to 1 while the fist closes (the client's `CVRXRModule.Update_Gestures`: the
grip by default, the trigger on Vive and Windows MR, finger curl from the trigger on Index), and
its movement data carries that float to remote copies.

Fixed (937a48cd, in 5.0.0): `FeedGestureWeights` (`Editor/Core/AnimatorMerger.cs`) works the weight
out from GestureLeft/Right on every copy with an Animator Driver: the squeeze in a fist, 1 in any
other pose, 0 open, as VRChat gives it. The two stream entries are gone, and the weights go local
("#") since every copy computes its own. `Dev/Tests/GestureWeightFeedTest` runs the CCK's own task
code: red before the fix (two trigger entries, no driver), green after.

Open [GAME], checklist R13: on a grip controller, a half squeeze half-closes a weight-driven fist,
and a second client sees the same; on Index the trigger still does it.

### 24. Kar after reconverting, 2026-10-25: plug gone, a breast "cloned". SHIPPED in 5.0.0, Joe to look in game
**Check:** reconvert Kar: the plug is still there and bends, is not white after a re-bake, and the sockets are not all on.

All five fixes are in 5.0.0; causes, tests and numbers are in `archive/Closed-2026-11.md`. No look
in game is recorded for any of them.

- **Plug kept across a reconvert.** A locked Poiyomi 7.3 Pro plug that also declares Raliv's orifice
  read as a DPS socket and went with every reconvert. Reconvert and check the plug is still there.
- **The converter upgrades DPS/TPS itself**, also beside SPS (only the parts the SPS route left).
  Look: the Poiyomi plug, DPS bend off, sockets.
- **White plug.** YAPS Simple Lit now samples an emission map and honours the emission switch and
  strength. Re-bake and look.
- **Sockets all on.** A rebuild turns an existing socket toggle off. Whether "YAPS readout" ever
  shipped on is unknown: watch for it on the next upload.
- **The "clone"** (a dragged breast sliding over its partner, the bra tearing) was the squash pair:
  shares over one and a seam with no sides, both fixed. Kar's pair is on the live tissue now
  (item 29), so this look is item 29's. Squash-only leftover, not visible at the probe's framing: a
  share step across a tube top's cleavage midline on another avatar (0.5 beside 1.0, 3 mm apart);
  a build-side smoothing would need a rebuild to reach anyone.

### 37. The 5.0.0 read-through's bugs: the in-game checks it left. SHIPPED in 5.0.0, Joe to check in game
**Check:** two plugs on one mesh each answer only their own slider and toggle; a "Tail_End" chain keeps its last segment; a stand-in cloth has no preset angle limit.

All fifteen bugs and every risk were fixed or withdrawn on dev 2026-11-12, a failing test first, and
shipped in 5.0.0; the record is in `archive/Closed-2026-11.md`. Only the game can tell these:
- **Two plugs on one mesh, each on its own lane** (risks 26, 28; `YapsSharedRendererLaneTest`). Each
  slider moves only its own plug (also with the other held part-way: the packs must agree), a shape
  moves both, a scale toggle on one, two choosers, shadows at each size, a lone-plug avatar
  unchanged, Shader Filtering clean, an old upload unchanged.
- **Each plug's own deform toggle** (`_YAPS_Enabled2`, `_YAPS_SelfAllow2`; `PlugOwnSwitchTest`).
  Each plug's toggle switches only its own plug, both ways, and each own-sockets row likewise.
- **A chain ending in a bone like "Tail_End" keeps its last segment on MagicaCloth2** (risk 22,
  `AuthoredEndTipTest`). Physics: confirm in game. Ceiling: an authored bone named exactly the
  parent's name plus "_End" is still skipped.
- **A stand-in cloth that is not a soft body no longer keeps the preset's angle limit** (risk 21,
  `StandInAngleLimitTest`). Physics: confirm in game.

Known ceilings, not work: a new toggle's clip file is not checked against an avatar that is only in
a prefab, off the scene (risk 29); the tester's DynamicBone grab fix (bug 6) has no automated test,
only `check-defines.sh`.

### 46. A Mini Me clone never squashes or jiggles. Joe in game, 2026-11-16. OPEN, waits for Joe's call (a feature, not a repair)
**Check:** Joe's call: see a Mini Me clone stay at rest in game, then say whether the clone deform gets built.

The clone (ChilloutVR's ContentCloneManager, as used by Mini Me) draws the host's skinned buffer with
the host's materials under its own `unity_ObjectToWorld`, somewhere else and smaller. The dent
(`yaps_dent.cginc` 807, 1397) and the jelly (`yaps_jelly.cginc` 402, 429) take a vertex to world
through that matrix and measure it against host-world data (the follow matrices, the skin buffer,
the lattice and anchors), so every clone vertex reads out of reach and keeps its rest shape. Not
proven in game; read from the code by the plush build session. Partly by design: 98d8aaaf and
96188335 leave another copy's skin alone on purpose, because the clone's chest and hips were
dragged across to the wearer as long bars. A fix makes the clone deform, a feature, not a repair.

Possible fix: deform in the host's frame when the draw is a displaced clone (its translation more
than about a centimetre off the host's), mapping back with the host's inverse so the clone's own
matrix then places and scales it. Needs the host's render matrix per material copy (the follow
array is full, so one more data provider per renderer) and the exact matrix Unity draws a skinned
renderer with. The host, mirrors and the first-person copy keep today's path.

Also seen on the same avatars: a jelly whose skin mesh shipped empty crashed the clone outright
("vertex buffer #0 is null") and broke the host. FilledMesh (089b0d74) writes the skin full at once.

### 50. Face tracking failing in every mode on a VRCFaceTracking add-on avatar. A user (Kneesox), 2026-11-17, with their own fixer script and a 4.7.2 Keep-mode output. SHIPPED in 5.0.0, not checked in game
**Check:** a VRCFaceTracking avatar converted on Native tracks face, tongue and eyes in game.

Their fixer (strip the rig, native component, built shapes) works for them; they gave permission to take from it.
Read against their output:
- **Native and DSR left five rig layers** (3d3c3280): a viseme layer holding every mouth shape at 0 behind
  `VisemesEnable` and `TrackingType`, and four tongue layers on `TongueControlActive`, `ForceTongueOut*`,
  `Tongue-x/y`. The strip now knows those switches (menu unchanged, Keep keeps them) and leaves `IsLocal`,
  `TrackingType`, `VRMode` and quantization out of a layer's count: it strips exactly the 12 their fixer did.
- **Keep cannot run a binary-synced rig** (4f587f66). VRCFaceTracking (master, `BaseParam`) does match
  `#FT/v2/JawOpen` and sends to that exact address, so the `#` alone is not it. But ChilloutVR lists every
  animator parameter where VRChat lists the expression parameters, so the input, its `Drive` and its
  `Generated` copy all end in `/v2/JawOpen` and the first match wins; and the 130 binary bits are floats in the
  animator, so the app's bool parameters never match them, and synced they cost 4160 of 3200 bits. The
  analyser steers such a rig to Native and drops the keep card; a kept one is a report warning.
- **Native builds the slots the CCK leaves empty** (cd36e8e3): sideless shapes split at the face's midline,
  ARKit names blended after VRCFaceTracking's weights, funnel and pucker halved, a two-step tongue. Eye look: an
  unset VRChat pose gets 25 degrees instead of pinning the eye.
- **Built shapes checked on 22 corpus scenes** (FaceTrackingShapesProbe, b6b7e97d): every left or right half
  moves on its own side and the halves match; slots mapped went up on 21 of 22 (27 to 58 on five, 10 to 70 from
  the CCK alone on one). **Open:** none of it is tested in game. Their fixer's blendshape eye look (EyeLook
  shapes, no eye bones) is not taken.

### 47. A remote hand held in live tissue made it jump and stretch up the arm. Joe in game on rc16, 2026-11-16. PART FIXED, SHIPPED in 5.0.0, never reproduced, Joe to retest in game
**Check:** reconverted on 5.0.0, a hand held still in a remote player's tissue: no jump, no stretch up the arm.

Joe, desktop, holding his hand still in a remote player's breast: "it just jumps and jitters up my
arm ... something to do with the grab". The stab harness (`-jellyHandIn` and the rest in
`JellySpikeBuilder`) put a generated remote hand in Kar's first breast for 300 frames.
- **Fixed (256c6653, 7a4d2163):** a gesture flickering between open and fist with the hand 6 cm in
  started a grip on every close and moved the part 32 mm in a frame; opening a fist inside moved
  it 47-62 mm; a deep grip letting go threw the tissue 36 mm. Now the slot's gesture eases at 5 a
  second (a flicker under ~0.15 s never grips) and a let-go hand's plow eases in over 0.2 s:
  4.3-9 mm a frame at worst. Full verify unchanged (grip pull 88.4 mm, ring 16.3 mm).
- **Not reproduced:** a steady open hand, 4 to 10 cm in, still or swaying 1 cm, sliding in and
  out, arm 60 degrees off, a smaller player, a second player's hand shifting the slots, uneven
  frame times, a MagicaCloth capsule on the hand with and without Other Colliders' probes, the hand
  fixed in the world while the avatar bobs 1-2 cm in 10 Hz packet steps or smoothly: all 0 to
  1.7 mm a frame, no grips. Desktop gestures change only on a keyboard combo
  (`CVRInputModule_Keyboard.Update_Emotes`), so his gesture was steady and the flicker fix is
  probably not his case.
- **Rechecked on the dent-only tissue (2026-11-18):** a steady hand at 6 to 10 cm, bobbing in 10 Hz packets, swaying,
  with a MagicaCloth capsule, or sweeping in and out moved the part 0.5 to 4.7 mm a frame; a flickering fist 7.1 mm.
  Still not reproduced. The dent-only change (item 51) took out the hand's push up the arm, the likeliest match.
- Next: Joe retests on 5.0.0, reconverted (it carries the rc18 fixes and item 51's dent-only hand).
  If it still jumps, record his own hand's values in game (the provider's
  `_CVR_PlayerLeftHandPositions[0]`, w the gesture, and the root's w, his avatar's height) and the
  slot texels 0 to 15 and 112 to 119 while it jumps.

### 29. The jelly on every part of the skin. Joe, 2026-10-31. SHIPPED in 5.0.0 as the live tissue (BETA), Joe to check in game
**Check:** seat, thighs (seated and crossed) and a belly: a squeeze and a poke dent the spot and do not sink the whole part (S26.5).

Built: any painted or found part goes on the live tissue, four parts an avatar (a fifth is refused
by name), one-sided parts too, a preset and eight knobs a part, the Avatar Tester's fist, slap and
twitch. Every batch check passes (run s5i, 2026-11-07: breasts, seat, thighs, thighs seated, belly,
eight random patches, the probe prop). Joe on the films, 2026-11-07: "they look good"; the presets
stay. The record is in `docs/archive/Closed-2026-11.md` ("The jelly on every part of the skin").

**Open: Joe in game.** Parts other than breasts and the presets have only been judged on film
(`Dev/Release/ingame-checklist-5.0.0.md` S26.5): the seat's squeeze and dent, the thighs seated and
crossed, a belly alone, and that a poke dents and no longer sinks the whole part.

**Known and left as built:**
- A poke past 7.9 cm on the seat still sinks: the skin holds a hand at a third of the tissue.
- A quick slap by another player's collider can pass between probes on a big part: 24 probes a
  part is the state row's room (texels 88 to 111).
- The homes reach about 0.9 of `_Solid` at 0.7, not all of it, less across than in depth.
- Thighs this thick ride 38 degrees up to cross in the film; the film's pose, not the tissue.

**Not done:**
- The painted fit reads weld-averaged shares (the one finding of the 2026-10-31 review left
  unfixed).
- The Avatar Tester's twitch against a running animator: the `PreLateUpdate` order is read from the
  CCK, not measured.

### 18. Squash in game, reported by other users, 2026-10-23. SHIPPED in 5.0.0, no fix seen in game yet
**Check:** turning in place does not press the pair's middle, nothing explodes for other players, and it holds with Shader Filtering on.

Four causes fixed on 2026-10-23/26; the record is in `archive/Closed-2026-11.md`. Builds from before
keep the old shader code until rebuilt (patched shaders inline the include). On humanoid avatars the
pairs are on the live tissue now (item 29), which carries the same `skin [FPR]` helper name.

In game, each still to be seen:
1. **Turning in place no longer presses the pair's middle** (the wearer's first-person copy froze the
   skinned buffer; the helper is `skin [FPR]` now). Still open by design: the buffer is two skinning
   passes old, so a very fast turn can shift the contact a little (a LastFrame provider could
   compensate).
2. **The squashed mesh no longer explodes for other players.** The cause on a remote copy is NOT
   proven (a client binding a disposed or reused buffer, or NetIK order); the shader now falls back to
   the matrices when any skinned slot is off or NaN. If it shows again, what decides the cause: the
   viewer's client branch, and whether it happens with that one avatar alone.
3. **Poiyomi 10 Pro Tessellated reads the right vertex.** Not proven to be the testers' explosion:
   ask whether the exploding material was on Poiyomi 10 Tessellated.
4. **Shader Filtering on.** Squash: a part no longer flies apart at any Loop Limit; a press by
   another player's hand is lost under the filter (README says so). Plugs: with the unrolled walk the
   marker-light route bends as unfiltered at 2048; at 128 one light case of two passes; **the atlas
   route is still straight at 128 and 2048** (the atlas scan cannot fit any limit as a loop), and
   shape keys past the first are lost at 128. README: the limit at 2048 or the filter off; older
   plugs need converting again. The editor never filters an imported shader, so in game it can only
   be worse than measured. Left: the bend tester for Joe's eye on the new walk, then in game.

### 43. ChilloutVR's Unity 6 + GS2 nightly (build 26689032, Unity 6000.3.26f1). Joe, 2026-11-16: "RED ALERT". Stable planned 2026-11-23. OPEN, Joe to test on the nightly
**Check:** on the nightly with a second player: no magenta, plug, tissue and squash work in a world and a mirror, toggles keep, a late joiner gets the state.

Uploads stay on Unity 2022.3 until a Unity 6 CCK ships; the risk is 2022-built avatars in the Unity 6
client. Decompiled and diffed against the scripting beta (2026-10-29) and GS2 nightly 26554805: nothing
we ship breaks in the client code. Most changes came with GS2, not Unity 6.
- **Sync budget: DONE, shipped in 5.0.0** (0ff28a92, `SyncBudgetTest`; record in
  `archive/Closed-2026-11.md`). The report counts the GS2 way; the stable client before GS2 still
  bills bools a bit each and stops at the first overflow.
- **Withdrawn: a phantom hand on the jelly from a hidden player.** The new client zeroes a hidden
  player's bones; the jelly scan, its wake check and the head press already skip a zero position.
- **Props: notes out of date.** GS2 gives each prop part its own owner: a value write no longer
  re-sends the prop's position, and Disallow Theft locks only while somebody holds it. The two reasons
  `YapsPropBuilder` dropped the contact channel and left theft allowed are absent from the new code.
  Revisit once Joe has tested a prop on the nightly.
- **IsAnimatorEnabled is written on remote copies now** (false while network LOD hides the avatar), so
  `CvrParameterNames.NeverFedNames` is only true for the wearer. No visible effect: a hidden copy draws
  nothing. Left as is.
- **Joe on the nightly, with a second player also on it:** YAPS and jelly avatars load with no magenta
  or fallback shaders (the engine side no decompile shows); plug, jelly and squash in a world and a
  personal mirror in VR (mirrors moved to pre-cull); toggles and sliders change once and keep, late
  join gets the state (GS2 sends changes only); EyeHeightAsMeters and ScaleFactor at 1x and scaled (the
  client measures height from the rest pose now); hair swing and a cloth toggled off and on (the
  client's MagicaCloth2 lacks `disableMode`); a material-swap toggle on a jellied part; Player.log has
  no `[ClipMaterialCrawler] ... disabled` line; the first-load hitch, re-measured.
- When GS2 reaches stable: item 33 (the bundled scaler's default) and the first-load re-measure of
  item 42 both wait on it.

### 20. The atlas paints dots on viewers' screens. Joe, 2026-10-24. SHIPPED in 5.0.0, Joe to check in game
**Check:** in a void on a current build there are no dots in the corner, and the spot is clean with no socket avatar in view.

Joe, in game: "the dots in the right hand corner, atlas is leaking to user screens, did we fix
that?" ~~Not fixed.~~ **WITHDRAWN 2026-10-24: it was half fixed, in 04da441 (2026-10-22), which this
record missed.**

- **Cause.** The payload pass writes colour, so every occupied cell paints a pixel or a few (noted
  2026-09-03 in `docs/YAPS5.md`, "One cosmetic item is now visible"). The writers draw at
  `Background-945`, ahead of the scene, so the dots show wherever the world does not draw over them:
  a void or no skybox, and a camera clearing to transparent. Every socket avatar in view writes into
  every viewer's frame.
- **Fix (a), 7d5d696, in 5.0.0.** The clear grabs the screen first (`_YAPS_Under`, before its own
  pass at Background-946) and the grab's second pass copies that back over the rect by the
  fragment's own pixel, each eye from its own slice. Any background, any number of old-build avatars
  in view: one new-build avatar puts the whole rect back. `AtlasSpotsProbe`: a picture under a
  depth-only camera 561,600 px to 0; opaque and transparent stay 0; plug bend 27,747 px unchanged in
  all three. 04da441 before it painted the rect with one colour sampled from three pixels beside it,
  and nothing where they disagreed.
- **(b) rejected, 2026-10-24:** the payload in alpha alone. Four alpha pixels per payload pixel cut
  the cells a view holds (720p 4096 to 1634, a 1024 mirror 4096 to 1860, a 512 one 1890 to 450, the
  atlas off below about 480 px square, was 240), header reads stay one tap but a hit costs 16 taps not 4, more sockets are lost to
  clashes (about 0.5% at 1634 cells with 120 in view, 6% at 450), and it is a protocol bump, so mixed
  builds could not see each other.
- Clear and grab compile with and without STEREO_INSTANCING_ON.
- **Cost, unmeasured in game:** a second full-screen copy per camera per frame while a YAPS avatar
  is in view.
- **In game:** confirm Joe's dots were the atlas (the same spot with no socket avatar in view), which
  build showed them (an avatar uploaded before 2026-10-22 has no paint-back, though one current
  avatar in view now clears it), and look at the corner in a void with a current build. The
  right-hand corner is odd: the rect sits top LEFT of each eye.

### 41. The live tissue under hitches, snap turns and bad numbers. Joe, 2026-11-14: "make it very stable". SHIPPED in 5.0.0, Joe to check in game
**Check:** a snap turn, a teleport, a hitch and under 30 fps leave the tissue whole; watch the belly's stop after a glide.

Each failure reproduced on purpose in batch (`VerifyBreasts -jellyStab`, a tsv a frame) on the code before
and after, 16 cases. Before: a 60 fps snap turn, teleport or hitch tore the tissue over a metre for a
frame (tear 37 spacings), shipped in every build with the jelly; a 90 ms hitch at 5 m/s flung it 134 mm
(62 mm is the glide's own trail); one bad particle hid a breast for 200 frames. After: 31 to 117 mm and
intact, 87 mm, and one frame with nothing visible. What fixed it:
- A snapped step runs one predicted pass. Two whole passes fit each particle to neighbours the jump left
  uncarried and pulled it most of the way back (the tear).
- The pair push skips a snapped step: it read the partner's mass from before the jump and shoved this
  one 6 cm, the real cause of the hitch fling. The old code hid it by snapping twice by accident.
- The step takes the time that really passed; a gap past 2.5 held steps (at least 1/30 s) is carried,
  so under 30 fps is no longer one long hitch.
- Velocity changes smoothed over 0.06 s on every copy alike (remote copies move in packet steps);
  landings still measured against the raw speed, or every speed-up rang.
- NaN guards by exponent bits (fxc folds `!(x < k)`), a watchdog that halves a runaway's own speed and
  resets it after eight hot steps, own speed capped at 4 m/s at the anchor's scale, a turn past 12 rad/s
  carried.
Costs: step variants +8 to 10% instructions, compile time unchanged. Trade-offs: a 6 m/s kick swings
124 mm instead of 142 (the cap); an 8 degree turn in one frame is carried; uneven frame rates settle
slower because the old clock ran 1.3x fast there. The speed patch from item 40 ships with it (Joe,
2026-11-15: "keep it"). Against the last commit at a 5 m/s glide: the breasts trail 123 to 62 mm and
stop dead 133 to 126 mm; the belly trails 80 to 95 mm and stops dead 54 to 138 mm, OPEN: the belly's
stop is worse, the patch's cost, not the guards' (the guards alone took it 179 to 138). Upside down the
step before the guards (with the speed patch) went NaN, eight checks failed; now 0.9 mm off rest. Unchanged within 0.5 mm: still,
60 fps, seated thighs, the probe prop. Open: Joe in game, the belly's stop, a remote-copy replay harness.
- **[Minor] A per-slot `_YAPS_JellyOn` scalar.** A toggle that animates the jelly off keys all four
  components of the lattice vector today (keying one zeroes the rest in the renderer's block and tears
  the skin). One float to switch it would make that toggle safe; the plush session would move its
  toggles onto it.

### 9. The audit of 2026-10-03: what is left, all of it for the game. SHIPPED in 5.0.0, Joe to check in game
**Check:** R14, R15, R16, S6.8 and S17.9 on the checklist, and a skinned plug with the height slider at both ends.

Every fix is in 5.0.0; the records (causes, tests, measurements) are in `archive/Closed-2026-11.md`.
None of the checks below is ticked in `Dev/Release/ingame-checklist-5.0.0.md` (local).

- **PHY-03, native mode's ancestor rule (R14).** With *CVR Native Targets*, a contact or collider on
  an outfit that starts off does nothing until the outfit is switched on, then works. Still unchecked
  [GAME]: whether a switched-off host stops a DynamicBone collider (the README says so).
- **FT-02, face shapes after Face Tracking goes off (R15).** DSR rig: Face Tracking off
  mid-expression lets the face go back to rest, and gesture expressions still play. Same for Eye
  Tracking and the lids.
- **YSH-13, near-black protocol lights (R16).** Settled without the game (`MarkerLightColourTest`);
  the check is a plug finding a DPS or SPS socket whose lights are 1/255 grey, and a long plug
  opening a YAPS socket. Reconvert first: every patched shader changed.
- **FT-05, eye gaze on a rolled head bone (S6.8).** Proven in Play mode (`GazeRolledHeadTest`); real
  eye-tracking input is the check. Low; skip without such an avatar.
- **YSH-07, a skinned plug and the height slider.** Covered by item 21.2's fix (f588654); needs a
  re-bake. ChilloutVR still to see, with the slider at both ends. Past 1.6x what fails is the
  atlas's reach, not the scale (item 21).
- **YSH-18, the plug "ghost" (S17.9).** A plug bent by marker lights drew straight in the depth
  passes; the light relay (8e5a288c) fixes depth effects. Check: a plug in a DPS-only socket in a
  world with depth of field or fog, two clients, no straight plug through the skin for either.
  - Not covered by the relay, by design: owner 0 (a plug on a prop, or someone else's copy before
    the owner id syncs), views too small for the atlas, a socket mesh's opening, one frame late.
  - A bent plug casts a straight shadow on every route (a light's shadow map cannot read the atlas);
    documented in the README, not fixed.
  - Joe's sightings of 2026-10-22 and 10-24 are not attributed yet: a cast shadow sits off the plug
    along the sun and moves with it, the depth ghost sits exactly behind the plug from the eye. If it
    shows again, the Resolved by view (`_YAPS_Debug` 1) on the plug: three quarters (marker light) is
    this cause, full (atlas) would be a new bug that needs the world and both builds.
  - Later, not started (option B): the plug's other passes read a per-avatar texture outside the grab
    pool, filled once a frame by a CVRBlitter copying the relay rows out of the atlas, which would
    reach a light's shadow too. Own sockets first; needs the blitter's timing proven in game.
- **Optional, no bug behind them:** an early out in `YapsDeform` before the full baked read (YC1-10;
  a compact layout would be a protocol change), one `PatchOnSimpleLit` for both builders (YC1-08),
  sharing the clip walk in `ConstraintScaleRelay` (PHY-13), and the Toolkit's *Mesh bounds* button
  re-applying the avatar envelope to a separate plug mesh after a native bake.
- AM1-14 (PlayAudio paths with a slash) is item 15.

### 21. ChilloutVR's October 2026 status report: what to check. Joe, 2026-10-24. PART SHIPPED in 5.0.0; the rest waits on Game Server 2 in game
**Check:** on GS2, at 20 cm and at 5 m: plug reach and bend, sockets, squash and tissue reach, contacts and cloth; then the prop contact channel and the synced YAPS channels.

From <https://chilloutvr.net/blog/status_report_october_2026>. GS2 is on the Nightly now and stable
is planned for 2026-11-23: item 43 holds the decompile and Joe's nightly list, item 33 the bundled
scaler. The scaling measurements and fixes are in `archive/Closed-2026-11.md`.

1. **Local test avatars show as the default avatar to other players** under GS2. Two-client checks
   need uploaded (private) avatars, not Build & Test.
2. **Universal Scaling** (20 cm to 5 m, any avatar). Fixed in 5.0.0, measured in the editor only: a
   skinned plug keeps its reach (needs a re-bake), a skinned socket mesh opens to scale (needs a
   rebuild), squash reach scales exactly, cloth keeps its weight at any size (start scale pinned).
   Open:
   - **The atlas's reach.** Its top cell is 1.28 m and the scan covers 1.2 plug lengths, so a plug
     past 1.07 m long misses sockets at the far edge. `YapsBendTester -yapsScale`: 0.2, 0.5 and 1
     pass, 1.6 fails 2, 2 fails 14, 5 fails 17. The fix is a fifth level (5.12 m cells), which
     changes the atlas layout and protocol: Joe's call. Tried and reverted: ending the fade inside
     the coverage block (far 0.99 of the top cell); 1.6 then failed 6, because the marker-light route
     takes over at the new edge on its own curve (300-500 mm jumps at 1.22L), and 2 and 5 failed 16
     and 20.
   - **Unmeasured:** plain (unskinned) socket meshes, which take their scale from the matrix; CPU
     skinning; a blendshape's normal deltas at weights other than the bake's. Read only: a DPS
     plug's length rides its marker light's intensity, which no transform scale reaches, so a
     resized plug opens a light-read socket as if it were its baked length; other players' hands are
     sized by their viewpoint height, own arms by their own bones.
   - **Whether a turned avatar's socket opening turns in game** is open (the editor result was
     withdrawn: a batch run skins the GPU mesh once).
   - **ChilloutVR's own MagicaCloth2 accepting the pinned `initData`** is untested; it warns and
     builds as before if not.
   - **In game:** plug reach and bend, sockets, the atlas, squash and tissue reach and hand size,
     contacts and cloth at both ends; a saved small size and long hair's hang.
3. **Prop partial sync.** Retest the prop contact channel on the Nightly. Item 43: GS2 gives each
   prop part its own owner, and the two reasons `YapsPropBuilder` dropped the contact channel and
   left theft allowed are absent from the new code.
4. **Avatar settings send only what changed after the first full set**, and distant avatars get
   fewer updates. Retest the synced YAPS channels (owner id, contact-driven parameters) on the
   Nightly; item 43's list has the toggle and late-join checks.
5. **Unity 6.** Overtaken by item 43: the client is on Unity 6 on the nightly while uploads stay on
   2022.3. Once a Unity 6 CCK exists, compile and run the corpus against it.
6. Watch only: Lookalikes (baked stand-ins; the atlas should simply be absent from them),
   server-side SPI patching of pre-CCK 3.4 content (not ours), and a URP proof of concept that keeps
   grab passes (no move planned).

### 42. The first-load freeze. Joe, 2026-11-15: "the freeze freezes everybody". SHIPPED in 5.0.0, Joe to check in game (with the GS2 nightly)
**Check:** first view of a reconverted avatar on the GS2 nightly: a shorter freeze, a plug that bends and tissue that moves.

A viewer's driver builds each shader at its first draw, on the thread the whole client waits on, so
everyone in the instance freezes. Measured on Kar with the tissue (scratch `d3d-load.py`, salted
first-draw timing, quiet machine): ~1.9 s of driver builds (step 0.95, plug 0.57, squash 0.23, rest
0.15) and ~0.3 s on the CPU. The step's bytes are the same on every avatar, so it is paid once per
viewer per YAPS version; plug and squash shaders are patched per avatar and paid each new avatar.
- Plug: the bend's walk landed in each of its eight steps, now once (-20% a pass, GPU stream-output
  test over 4,096 curves, worst 1.2e-7); a locked Poiyomi outline at width 0 is left unbent.
- Step: the first pass's side row (the scan, slaps, mass and the rest) is a blit of its own after
  the particles' first, and both passes' particles share one variant, so the driver builds it once.
  fxc: 281 + 143 KB to 170 + 145 KB. Stability gate: the VerifyBreasts set (seven cases) and the
  probe prop give every number as before, to the last digit.
- Probe net: **Other Colliders**, off by default, about 0.1 s of MagicaCloth setup a load saved.
- Re-measured on Kar reconverted on dev, old dump against new, same salt and load: the plug 843 ms to
  600, the step 1,037 + 325 ms to 602 (its particles' program reused by the second pass), Kar's whole
  first view 2.8 to 3.5 s down to 1.6 to 1.7 s. The cloth setup at wake 106 ms to 8 (no probe net).
- The animator's wake (`Animator.SetupControllerDataSet`, 160 to 300 ms on Kar, noisy run to run) is
  the materials: plain Standard on every renderer takes it to 3 ms, while plain on any one renderer
  moves nothing, so it scales with every renderer's shader properties together. Kar pays most of it
  without the tissue (its locked Poiyomi, ~470 properties a shader); the tissue's share is the ~124
  YAPS properties on a patched body shader, which must stay declared (undeclared values die on the
  next import). Left as is.
- Open: Joe in game, with the GS2 nightly.
- **A plush avatar still hitches once on first view (Joe in game, 2026-11-17).** Its built package dumped
  (`ShaderDumpProbe`, 108 programs) and timed cold with `d3d-load.py`, ChilloutVR running so the times
  are rough (3.3-3.6 s): the tissue step ~1.0 s (355 KB, once per viewer per version), the plug ~0.8 s,
  four tissue body shaders ~1.3 s. A plain locked Poiyomi material is 25-31 KB; patched, 206-250 KB,
  all of the growth in the vertex programs (~45 KB in each of four passes; fragments unchanged). The
  main and extra-light vertex programs of different materials are the SAME code: only the constant
  buffer offsets of the YAPS values differ, shifted by each material's own Poiyomi properties in `$Globals`.
  So the driver builds 12 distinct body programs (574 KB) where 4 would do (192 KB).
  **Done, 934e69b6, folded into 5.0.0 (Joe: "if it shows measurable improvement put it 5.0.0").** The YAPS
  values sit in named cbuffers of their own, one per include. The plush re-patched (`-dumpRefresh`): its
  four body materials share ONE vertex program per pass (27 distinct to 19), 917 KB to 535 KB of vertex
  bytecode, and the cold driver build of everything it draws 2.6-2.7 s to 1.8-1.9 s (game closed). A
  converted avatar picks it up on its next rebuild: the patch name hashes the include text, so the six
  patched materials read as stale. Bend tester PASS with one place of 50 in the two-socket 25 cm case now
  taking the nearer socket (twice); Kar's tissue numbers identical; fresh compile PASS. Leaving Eyes/Brows
  and Tails unpatched now saves nothing (they share the program), so dropped. No tissue in the extra-light
  and shadow passes: declined by Joe (a still shadow under a moving body). Open: ChilloutVR filling the
  named cbuffers, Joe in game (a plug that bends and tissue that moves prove it).

### 51. Live tissue drawn out into a cone along a hand held deep in it. A tester (fluffybat), 2026-11-17, rc21. SHIPPED in 5.0.0 (harness only); the tester has rc23, Joe to check in game
**Check:** a hand pushed in deep and held dents round the arm, no cone; the tester's answer on rc23.

"insane stretching when the hand in breast", "when I nudge it slowly, almost stretching past breaking": a breast drawn
2-3 times its length along the arm while a hand is pushed in deep and held. Their avatar: two 0.1 litre breasts on
10x11x9 lattices, a hand radius as big as the part, one MagicaCloth rooted on an unsided Boob_Root. Joe's rule for it:
the cloth's colliders move the bones, the tissue only dents the mesh; a hand never moves the whole.
Found (a five-agent read plus 10 harness runs, synthesis in the session scratch):
- **The hand moved and reshaped the whole.** `_Give` 0.525 leaned the mass 61 mm and `_Squeeze` flattened it along
  the hand: lattice 1.30x its depth outward, steady through a 2.5 s hold (str-arm45); both 0: 0.32 spacings, the
  dent kept. Now `CarriedGive` 0 and no squeeze on a carried part; Give, Swing and Spring back leave the feel UI.
  Joe would have kept some push if it could stay unstretched: the push without its outward part still reached
  15.2-15.3 of 14 spacings at 0.525 and 15.0-15.2 at 0.25 (bar 14.75), so it stays off.
- **The cloth was never recognised**: `PairCloth` took only roots on a sided bone, so a cloth on `Boob_Root` left the
  tissue riding the bone above the swung one. A root naming the part above the sided chains counts now.
- **The cloth no longer shrinks its balls**: other players' hands (added to the cloth at runtime) move the bones
  through it, as on the source; the tissue dents. The avatar's own colliders still leave the cloth's lists and only
  dent: left in, the own-forearm run threw the cloth about (furthest 577 mm, snap guard every frame). Old builds'
  marks still put the size back on a rebuild.
- **Nothing bounded the draw**: the clamp was twice the part's depth. The skin is now drawn within its depth of
  where the bones hold it, a quarter of that outward.
- **The tester's depth map was all 0xCD** in 8 of 8 of their outputs (rewritten about 4 minutes after converting; not
  reproduced here by any save, unload or reload, DepthSaveProbe). The step reads a value outside the box as no
  map; the map is written as a new texture copied over the old and saved at once, as the squash's are; the upload
  check reads it back.
- **Proof on e42bf610**, Kar with a hand at the tester's proportions (`-jellyHandTall 6.4 -jellyStabSet _Deepest=0.48`,
  slow push in four steps, each held 150 frames), lattice front in rest spacings of 14 (was on rc21):
  45 degree arm 14.32 max (18.26), outward 0.53 (4.26), dent -5.10 kept; hand collider 14.27 (outward 4.91 before);
  80 degree arm 14.52, one back particle 1.19 out under the hand; straight 13.98. Films: dented round the arm, no cone.
  Own forearm: tear 5.80, furthest 144 mm (5.77, 139.6). Walk, run and 25 fps: tear 1.00-1.01, nothing left over.
  Hand dents keep their depth (65.9 mm, 106.9 mm at 10 cm), tear up from 1.41 to 1.51 and 1.85 to 2.11: the whole
  no longer gives, so the particles take all of it. FreshCompileTimeTest PASS.
- **The wake jump, FIXED on dev (harness), not in game.** A part held on its bones and touched let go in one frame,
  so whatever was already in it moved the lattice at once: a hand arriving during a 4 m/s glide 77.7 mm in a frame
  (tear 5.53), the wearer's own forearm reaching in 80.5 mm (tear 5.80). Now it lets go over 0.12 s, eased at both
  ends (Ease negative while it does, the share still held home). Bar set before the runs: at most 15 mm between
  frames after the wake. Handrun 14.0 mm, tear 4.27. Own forearm: the wake itself spread over 11 frames (1.8 to 17.0
  mm a frame), the skin's film steady through it (pixels changed per frame 2160-2415, rc22 3063 at the wake); a
  27.9 mm step remains in the lattice two frames before the let go ends, as rc22's 22.5 mm one two frames after
  its wake, with no matching change on the skin, so the literal bar fails on own there. Tear 5.77 (the forearm
  lies through the lattice; the harness's IK puts it through from behind). Regressions unchanged to the first
  decimal (arm 45 and 80, collider, deep hand, dents 65.9 and 106.9 mm, walk, run, 25 fps).
- **Own forearm live/held at one depth:** the deep own run settles asleep with the arm inside (lattice still, 92.7
  mm, nothing on the skin); not a stretch.
- **Open:** in game with the tester: rc23 (the dent-only hand and the eased wake) sent 2026-11-18, answer pending. Nothing here reads the drawn skin, only the lattice; the films are the check.

### 25. Mesh-to-mesh squash contact, and the jelly spike that became the live tissue. Joe, 2026-10-25. SHIPPED in 5.0.0; what is left is in-game questions and unstarted ideas
**Check:** checklist S26: a forearm dents at the sleeve, an avatar out of view rests, the wearer's own fist grips, feedback and twitch reach remote copies, the prop's bars move.

Built and shipped: the squash reads each part's real surface, the pair give 0.3 to each other, hands
press a baked soft body; then the jelly spike became the live tissue (item 29), which replaced the
squash's pair contact on humanoid avatars. The whole record, with every measurement and what was
tried and dropped, is in `docs/archive/Closed-2026-11.md` ("Mesh-to-mesh squash contact, then the
jelly spike").

**Never seen in game** (the record marks each [GAME]; `Dev/Release/ingame-checklist-5.0.0.md` S26
has the steps):
1. **Whether VRIK places the arms before or after the tissue's step.** Both run in a LateUpdate at
   the default order. A forearm held under a breast and moved slowly should dent at the sleeve, not
   off it (S26.11).
2. **The out-of-view switch on the stable client.** It reads the client's `_CVRRenderingBuffer`;
   Joe runs the beta. Also: whether the parser runs on remote copies, and that an avatar behind the
   viewer seen only in a mirror keeps its rest shape (S26.8, S26.10).
3. **A viewer with the Cameras content filter off** gets no blitters: rest shape, no squash either.
   Read from the decompile only (S26.10).
4. **How often a worn avatar rests in VR.** Resting, the held shape can sit up to 1 cm off the body
   before it wakes (Joe's accepted trade).
5. **The wearer's own hands gripping.** They are not in the arrays' gesture, so whether a fist of
   the wearer's own grips is open (S26.2).
6. **Feedback and twitch on remote copies.** `#Jelly/<pair>/Squeeze`, `Grab`, `Slap` written by the
   `CVRAnimatorDriver` on the wearer and on remote copies (local, never synced; on another's avatar
   only with the Cameras filter on); the twitch toggle's kick on remote copies; the toggle a
   conversion adds against the passes after it (S26.6).
7. **The prop:** its strain, grip and slap bars through a spawnable's parser, and the twitch on
   other players' copies (S26.14).
8. **AMD readings that do not fit the bench** (6700 XT 0.16 to 0.30 ms, 7840U 0.116 ms a pass,
   2026-10-27): most likely a GPU clocked down in a CPU-bound frame. Check the GPU clock and
   GPU-busy in game before optimising for them (S26.9).

**Known limits, not work:**
- A grab holds the skin off the hand (a remote VR player saw about 5 cm; 11.1 cm on average in the
  batch study). WITHDRAWN 2026-11-15: no fix beat the 1 cm bar, since the client hands a shader no
  hand turn (even the hand's true way reads 6.3 cm).
- Other players' MagicaCloth colliders dent it only for a viewer whose Dynamic Bone Interaction
  settings take them in (all None by default); hands through the player arrays have no such filter.
- The steps run every frame near (`_Rate` 0): the client's parser reads its switch back late, so
  45 a second came in bursts. A step that skips frames by the clock itself, out of the parser's
  reach, would win the rate back. Not started.
- Content cannot know where a camera looks (a blit resets Unity's camera values); the view switch
  uses the client's buffer, a frame late.
- The parser reads 8 bits a channel, so anything riding the tissue through it wants tight bounds
  (±5 cm, ±30° gives 0.4 mm, 0.24°).
- The world probes (solid world geometry pressing the tissue) were removed 2026-11-14 (d63195a5);
  their in-game questions went with them.

**Left on the squash's baked soft body when the live tissue took over** (Kar, 2026-10-27; they
matter only where a pair stays on the squash alone):
- The bra still folds under hard presses: 37 flipped triangles in the squeeze, 38 outside and in
  front, 66 and 103 on the deep front presses. Where those edges are was never read.
- Both pushed up hard lifts less than the solved tissue (1.01 cm rms, worst 5.1).
- Nothing past 1.6 of the deeper depth is read, a cap set while deep presses baked as chance.
- A step at the top of the cleavage on one part's inner edge on the hard presses, first seen when
  skin began riding its own part's tissue by bone weight.
- The swing bake ("B") was a wash on film and never judged without the bra.

**Ideas from this item, none started:**
- Two avatars wearing YAPS pressing each other's tissue, body to body (Joe, 2026-10-28).
- A plug a hand can grab, opt-in "Hands can grab it" (Joe, 2026-10-31): stateless in the plug's
  draw, a fist round the shaft keeps its grip point; cost is a hand loop in its vertex shader,
  compile time to measure.
- The lattice parting for a plug: today the parting is drawn only. The socket's synced
  `YAPS/<socket>/Depth` moving a transform the step reads would part the lattice too.
- Size blend shapes at run time: the mass follows a size slider, likely through a helper skinned
  mesh [R].
- The PhysBones conversion to 100% on the tissue: grab and pose, stretch and squish from the source
  PhysBone, its parameters (`_IsGrabbed`, `_IsPosed`, `_Angle`, `_Stretch`, `_Squish`) worked out in
  the step and carried to the animator by the parser, the source's settings driving the mass.
- The bra holds, bare skin gives: item 27.

## Open bugs and work

Real work left, most urgent first: what somebody hits while wearing an avatar, then what waits on a
tester, then the unmeasured leftovers.

### 16. Tester reports on the rc builds, 2026-10-22. SHIPPED in 5.0.0; one report waits on the tester

Four of the six are fixed and closed; the record is in `archive/Closed-2026-11.md`.

- **Poiyomi panel not working after the patch. Not reproduced, waits on the tester.** The tester's
  screenshots show a locked patch drawing the YAPS sections and then Poiyomi's "POIYOMI SHADER UI
  FAILED TO LOAD" text: `YapsShaderGUI.OriginalEditor` returned null because the recorded class
  (`Thry.ShaderEditor`) did not exist in that project, so the fallback drew the raw properties. In
  Joe's project it resolves on all 734 patched materials. Most likely Poiyomi's own scripts failed
  to compile there. To decide: does an unpatched Poiyomi material in that project show the same
  text, and what is the console's first error. (`UiOffscreenProbe` cannot draw a material
  inspector, so blank captures prove nothing.)
- **Slower CVR uploads. Fixed, not timed on a real upload.** The first upload after a conversion
  compiles every variant of every YAPS patch (36-patch avatar 851 s cold, 45 s warm). Done: the
  double-wide stereo variants are stripped (`YapsStereoStrip`, a third off) and the plug and squash
  compile was cut, together about 0.6x the old first-upload compile; existing patches keep the old
  code until rebuilt. The six-deep atlas scan is still most of the plug's cost. Tried and worse:
  scalarised socket arrays, an unrolled atlas scan, skipping optimisation (2x instructions); the
  partner pass's hand loop as `[loop]` was identical but saved a quarter of a second at best and was
  not applied. Not done: a tester's own upload timed before and after.

### 6. Conversions got slower between corpus 418 and 419: the squash's compile. SHIPPED in 5.0.0, one check open

The slowdown is fixed and confirmed (squash pass 261 min in run 419, 14.2 min in run 427); the record
is in `archive/Closed-2026-11.md`. The live tissue's own compile cost is items 36 and 48 and
"Conversion speed: what is left".

Left, small: the squash's copy rule from 2026-10-24 (`YapsSquash.MinShare`: a layer is copied only
with a stored share of 4/255 on some vertex of the slot, and a material carrying a plug bake never
takes a copy).
- The plug half is seen in corpus output (2026-11-13 soft subset: Kar's soft parts made copies for
  bra, body, panties, piercings, shirt, shorts and tail, none for the baked plug).
- The faint-layer half is not checked against share textures: the layers the cutoff should drop read
  1 to 3 (a plug on three variants of one avatar, a hood, a belt, a socket mesh), a stocking at 4
  keeps its copy.
- Neither rule has been seen in ChilloutVR.
- The full corpus runs with soft parts off since 1d515e4, so this needs a `--soft` subset, on Joe's word.

### 7. What the shader sweep left open. SHIPPED in 5.0.0, one corpus read open

The three fixes (the false "may not render correctly in VR" on YAPS Standard, the render queue
across a shader swap, the two patcher gaps) are in 5.0.0; records in `archive/Closed-2026-11.md`.

Left: **run 427's three metallic-map avatars** should build on YAPS Standard since 8c5bcf5a
(2026-10-24). No full run has been read for them. The 2026-11-13 run named YAPS Standard copies on
4 corpus avatars (the false VR warning), so copies are being built [R: not matched to those three].
Next: read those three in the next full run's digests.

Known ceiling of the stereo fix: a pass the planner cannot read, in a shader that also includes
Unity's Standard core or shadow include, is trusted.

### Joe's own

The GrabbyBones upstream PR (plan in `Regression/GrabbyBonesForkPlan.md`, local). Not
recorded as sent.

## 5.0.1

What is planned for the next release: the window's Remove squash, the code rot review, the Update button, what the 5.0.0 review
left, the scaler's default once GS2 is on stable, then the small fixes and the speed leftovers.

### 53. "Remove squash" in the YAPS window left the YAPS Squash component. Joe, 2026-11-18. FIXED 2026-11-18, shipped in the 5.0.0 refresh (a9ff085a), Joe checked the window
The window's button strips the build and points every layer and toggle back at its own material, but leaves the
component (parts and painted regions) on the avatar, and its message does not say so. Harmless in game (setup data,
stripped at upload), but it reads as a failed remove. Decided (Joe): the window's button also deletes the component,
with Undo, and says Ctrl+Z brings it back. The component's own "Remove the squash" keeps unbuilding only, settings kept
for the next build.

### 54. Remove squash, then Rebuild in one editor session, gave a tissue that tore under the tester's Squeeze. Joe, 2026-11-18. OPEN, not reproduced
On the plush in Joe's project: the window's Remove squash, regions repainted, then the component's Rebuild, all in
one GUI session (Editor.log 06:46 to 07:04). In Play mode the Avatar Tester's Squeeze tore the body skin and the
clothes into spikes as soon as it moved, nothing in the Console. The batch build of the same avatar (q1) does not
tear at Squeeze 0, 0.05 or 0.3 (shots in Play mode, tissue on and off), and q1 uploaded feels right in game.
Suspect: generated assets remade at the same paths in one session leave stale references. Next: reproduce in a copy
of that project's state, then make a rebuild after a remove start from fresh assets.
2026-11-18 batch try on the plush build copy: Strip then Build at the same paths in one session, then
Squeeze 0, 0.05 and 0.3 in Play mode: no tear, but no dent at 0.3 either, so the tissue was most likely not stepping
(batch Play mode without a Game view). Inconclusive. A trustworthy repro needs the jelly harness's render-late
stepping, or Joe's own steps in the GUI with the repainted regions.

### 44. Update a converted avatar in place, keeping hand edits. Joe, 2026-11-16: "queue it", then "write it". PLAN, decided, NOT BUILT

Joe: put an already converted avatar in a second slot under the VRChat one, and the button becomes
**Update**, so a reconvert keeps the changes made by hand that do not conflict. Today every Convert
rebuilds the output from the source and writes over it; only the window's settings and the
*Since the last conversion* comparison carry over.

- **Proposed (not built): Unity's prefab Variant of the OUTPUT.** Convert writes a base prefab and
  makes a Variant beside it once; the user edits the Variant. Update rewrites the base; the Variant's
  overrides (added objects, moved bones, changed components) ride over it, and a value both set is
  the user's, shown as an override. No merge logic of ours.
- **Not kept:** hand edits inside generated assets (controller, menu, materials), rebuilt each time.
- **Differs from the parked Variant-of-the-ORIGINAL idea** under Minor Ideas: the base here is our
  own output, so VRCFury and Modular Avatar bakes and the passes that reparent bones do not block it.
- **Decided (Joe, 2026-11-16: "write it"):**
  - **The slot.** A Variant AvatarBridge made: Update rewrites its base. An older output with no
    Variant: the button reads **Update and keep a backup**, rebuilds, keeps the old prefab beside it,
    and makes the Variant from then on (hand edits on that copy cannot be told apart). Anything else
    (a VRChat avatar, another tool's output) is refused with a one-line reason.
  - **Edits on objects the new base dropped.** Unity keeps them on the Variant as unused overrides,
    so nothing is lost silently; the report lists them under *Edits that no longer apply* with each
    path, and the user decides whether to remove them. An object added under a parent that is gone
    is moved to the avatar root and listed, not dropped.
  - **The upload id** lives on the Variant, so every Update re-uploads to the same ChilloutVR avatar.
  - **The window.** One slot under the VRChat avatar, "Converted avatar (optional)"; filled, the
    button reads **Update**, with one line on what is replaced and what is kept. Rendered for Joe
    (UiOffscreenProbe) before building.

### 33. The avatar scaler once ChilloutVR scales every avatar itself. Joe, 2026-11-10. WAITS for GS2 on stable (planned 2026-11-23); `addAvatarScaler` still defaults to true in 5.0.0

ChilloutVR's Game Server 2 nightly (2026.3.0-nightly, build 26554805) adds universal avatar scaling,
20 cm to 5 m, for every avatar. Decompiled the same day (`PlayerSetup.ApplyAvatarScale`): the client
watches the avatar root's localScale each frame, and a change it did not make itself, outside the
first second after a load, switches it to legacy mode and universal scaling steps aside until the
user touches its slider again. Bone scale never trips it.

What that means for the bundled scaler (`addAvatarScaler`, on by default, `BridgeSettings.cs`):
- It defaults to exactly 1x, so wearing a converted avatar leaves universal scaling on.
- Moving its Height slider hands control to it; touching the universal slider hands it back. The
  two never break each other, but a converted avatar has two height controls that silently take
  turns, and ours covers less (0.25x to 4x).

To do when GS2 reaches stable, not before (stable has no universal scaling yet):
- Default `addAvatarScaler` to false; keep it as an option for avatars that want an avatar-side slider.
- README: the options table default, the scaler's section, and a line on the hand-over between the
  two sliders. The window tooltip and the settings comment say the same.
- Check S28.3 (`Dev/Release/ingame-checklist-5.0.0.md`, local) passed on the nightly first.

### 13. What the speed work missed, read from run 425 against 417. 2026-10-21. OPEN, nothing done; re-read against a current run first

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

The work Joe means to take on now that 5.0.0 is out (published 2026-11-18). None of it is started
unless its heading says so.

### AvatarBridge Chambers: a home world with a test wing. Joe's idea 2026-10-20. THE MAIN FOCUS NOW THAT 5.0.0 IS OUT (2026-11-18), NOT STARTED

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
  "Squash against the world".  Where these live is decision 2.
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

**First step (5.0.0 shipped 2026-11-18, so this is next):** a spike. One lounge room and one chamber in a fresh CCK world project,
uploaded privately, to learn the world CCK's limits before designing the rest.

### A VRChat accessory onto a ChilloutVR avatar: find and attach. Joe's idea 2026-10-24. ACCELERATED 2026-11-06: next after the jelly's final approval. NOT STARTED

*Nothing built. File and line cites are from 2026-10-24 and have drifted; the names still exist
(`BridgeConverter.ContentPasses`, `Editor/Toolkit/AnimatorMergeTool.cs`, checked 2026-11-18).
Related: item 44 (update a converted avatar in place, keeping hand edits).*

Joe, 2026-11-06: "accelerate timeline for idea, attaching VRCFury objects to already converted
avatars, and I mean any and all objects". So the scope is every VRCFury object, not clothing and
plugs alone: toggles, Full Controllers, Armature Links, SPS, gestures, whatever a prefab carries.
The stand-in route below covers that by design, since Fury bakes the accessory on the stand-in as
it would on any avatar, and the converter's passes then run unchanged. What it cannot cover is
whatever Fury itself refuses to bake there; the spike lists those.
Joe, the same day: "add on the idea of more then one object". Several accessories in one pass:
pick any number, all go onto the one stand-in and convert together, so their menus, parameters
and Fury features meet once, as they would on a VRChat avatar wearing them all. Clashes between
them (a parameter or menu path two accessories both use, the 3200-bit budget summed over all of
them) are reported before anything is written to the target.

A user, 2026-11-15 (relayed by Joe): "make avatar bridge setup outfits without baking". Read as Modular
Avatar's Setup Outfit: drop the outfit on the avatar, bones matched in one click, the outfit staying its
own editable object until upload, where the merge happens. The stand-in route below bakes first; this
asks for the non-destructive half on top, a merge at upload in the CCK's build.

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

### Show it on the avatar: an in-scene overlay for every tool. Joe's idea 2026-10-20. PLAN ONLY

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

The squash is the pilot: the brush panel in the Scene view (shipped in 5.0.0) is the
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
other, walls and world objects squashing the avatar, and a slap that ripples. Nothing built.

*State on 2026-11-18: 5.0.0 shipped. On humanoid avatars the live tissue (items 25 and 29, BETA in
5.0.0) took over the pair contact and "any part of the skin"; this plan is for the squash shader
itself. Before starting point 1, read item 29's record (archive): it lists the same
four-part limits and what the tissue did about each. Points 5 (soft body over the whole skin) and 6 (a whole bone
physics system on the blitter) are in the archive: 5 became item 29, 6 was answered and not planned.*

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
builds blind to each other) and waits on queue item 20 (the atlas dots on viewers' screens: fixed in 5.0.0, Joe to check in game).

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
20 is confirmed in game).

### Squash against the world: an idea, 2026-10-17. NOT STARTED

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
- Next step: a read-only look at gates 1 to 3 in the client. Gate 4 goes away with point 1 of
  "Squash anywhere" (no hard cap on parts).

### 23. Squash swaps: repoint at upload. Joe, 2026-10-25. PLAN ONLY, for 5.1
The record is in `archive/Closed-2026-10.md`, "Squash turned swapped-in materials pink".
- **A: leave user clips untouched** and repoint on the upload copy through
  `CCK_BuildUtility.PreAvatarBundleEvent`, the way VRCFury does. Covers the plug and socket bakes as
  well as squash.
- **Two leads, untested:** the shader sweep (item 7) never covered the squash patch, and
  `FollowSwaps` keys copies into the clips before `Flush` writes them while the swap test reads the
  clip in memory only.

### 27. The bra holds, bare skin gives. Joe, 2026-10-28. PLAN ONLY, for 5.1
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

### Three plans from Joe's ideas of 2026-10-17. DECIDED 2026-10-17, NOT STARTED

Joe, 2026-10-17: grabbed clothing driving a blendshape ("_Stretch is the one we need to fix. Everything
else can be the GrabbyBones mod"), Blender's avatar edits inside Unity for the Toolkit (blendshapes,
visemes, a smooth brush "mesh fixer"), and the Toolkit as its own download for people already on
ChilloutVR. His rule for all three: adopt a modern tool that already does it, build only what nothing
covers. Each plan ends in a Decisions table.

**Decided 2026-10-17 (Joe): the recommended default on every row of all three tables.** For grab, the
rows that left it to him take the precedent: converter default **off** (as `syncSocketDepthForOthers`),
package **public**, release behaviour after the spike. Toolkit row 7 stands: after 5.0.0. 5.0.0
shipped on 2026-11-18, so all three are free to start; none has.

The three plans are the next three records: `_Stretch`, Mesh tools, the Toolkit as its own package.

### PhysBone `_Stretch` on ChilloutVR: grab clothing to drive a blendshape. PLAN 2026-10-17, DECIDED (defaults), NOT STARTED. Next: the first spike, in game

*State on 2026-11-18: nothing built; `_Stretch` is still reported as "no equivalent"
(`MagicaClothWriter.cs`, now near line 628). Joe took the recommended default on every Decisions row
on 2026-10-17 (converter default off, package public, release behaviour after the spike). File and
line cites are against dev on 2026-10-17 and have drifted; re-find by name. The `Unfinished.md:NNNN`
cites point at records that have since moved to the archive: the gap research of
2026-10-07 (native grab rig at ~97 bits, the `grab` tag, `_Squish`/`_IsPosed` for the GrabbyBones PR),
in "The physics night of 2026-10-07"; and in `Closed-2026-10.md` the MC2 tether's 3%
(`TetherStretchLimit`), "Layered on MagicaCloth2" (the PhysX joint spike) and the
Marshmallow PB gauge. The review's 17 corrections are folded in below; the list itself is in the archive.*

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

### Mesh tools in the Toolkit, so common edits never need Blender. PLANNED 2026-10-17, DECIDED (suggested column), NOT STARTED. Next: the SkinFrame spike

*State on 2026-11-18: nothing built (no SkinFrame, ShapeWriter or mesh card in `Editor/Toolkit`). Joe took
the Suggested column of the Decisions table on 2026-10-17. File and line cites are against dev on
2026-10-17 and have drifted (`ToolkitWindow.cs` is 688 lines now; `YAPS_CORE` has grown); re-find by
name. The review's 19 corrections are folded in below; the list itself is in the archive.*

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

### ChilloutVR Toolkit as its own package: plan 2026-10-17. DECIDED (defaults), NOT STARTED. Next: the first spike (needs Joe's go-ahead)

*State on 2026-11-18: no `--toolkit` mode, no `toolkit_files()`, no `README-toolkit.md`. Joe took the
default on every Decisions row on 2026-10-17 (a third package, option C; after 5.0.0, which shipped
2026-11-18). His later note stands: the Toolkit should be independent, as YAPS is. The add-on defect
this plan found (order step 1) was fixed and is in the archive, as are the review's 11 corrections.
Line cites are against dev on 2026-10-17 and have drifted. The file counts below (191 entries, 31
`YAPS_CORE` files) are stale too: `SoftBodyContact.cs` is gone, `YAPS_CORE` is 32 names and now
names `Physics/MagicaClothKeepOut`, and the Toolkit menu is `ToolkitWindow.cs:667`. Re-run the
scratch emulation of the list before trusting any count.*

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

- **The file list:** `toolkit_files()` = today's `yaps_files()` minus `Editor/Yaps` and `Runtime`. The 2026-10-17 list also added `Editor/Core/Physics/SoftBodyContact.cs` and the `Editor/Core/Physics` folder entry; both went on 2026-10-26 with the soft body card. Check on the day whether any Toolkit file reaches a `Core/Physics` file (`YAPS_CORE` now carries `Physics/MagicaClothKeepOut`): if one does, the folder entry is needed again, for the reason below. [M emulated in scratch 2026-10-17: 191 entries; stale]
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
  - `Editor/Core/Physics` (5,138 lines on 2026-10-17), except whatever the check above finds the Toolkit reaches [M]
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
  - Toolkit: no `Editor/Yaps`, no `Runtime`, no `Core/Physics` beyond what the Toolkit is found to reach, no `AvatarBridgeWindow.cs`, no `Presets`. `ToolkitWindow.cs` must be present.
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

1. ~~Fix the `SoftBodyContact` add-on defect and add the add-on plus Magica gate.~~ DONE 2026-10-17 (archive).
2. ~~Joe decides 1 to 7.~~ DECIDED 2026-10-17: the default on every row.
3. The first spike, below.
4. `toolkit_files`, `--toolkit`, `check_closure` with the `Editor/Yaps` exclusion, `verify_package` and the gates, in one commit.
5. `README-toolkit.md`, the README Installation and Toolkit sections, and this record moved to `docs/archive/` once shipped, all in the same commit as step 4 or the next one.
6. Ship it with a release after 5.0.0, all three packages built from one commit.

#### Decisions (Joe, 2026-10-17: the default on every row)

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

### The atlas: what has never been worn, and what phase D is for. OPEN: in-game tests that need a second person; D1 and D5 are design

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
- **Portal and Two At Once: shipped in 5.0.0, their game gates not recorded here.** Both were built on
  the list above (`YapsPortalExit` in `yaps_atlas.cginc`, the exit in the atlas's top rows, protocol
  still 9; plan and gates in `archive/Closed-2026-10.md`, "Portal and duplicate: the plan", and
  `YAPS5.md`, "Portal directory" and "Two at once"). The checklist named two game-only gates, G2
  (S24.1) and DG1 (S25.1), plus S24 test 13 (two plugs in one entrance both come out, "not yet
  worn"). 5.0.0 was published on 2026-11-18 under the rule that they pass in game; this file holds no
  line saying they were worn. Joe to confirm, then strike this bullet.
- **The named grab's cost in game, never measured.** In the editor (2026-10-29,
  `Dev/Probes/Perf/GrabPassPerf.cs`, RTX 4080, D3D11, ARGBHalf, no MSAA, GPU held busy): +0.067 ms a
  camera at both VR eyes' size (4032x2240), 0.030 at 1440p, 0.016 or less at 1080p; 8 readers of one
  named grab cost what 1 does (+0.079 at VR size); an unnamed grab copies once per reader (8 readers
  +0.224 ms at 1440p). Estimates, not measured: about 0.2 ms on a 3080 and 0.6 on a 1070. Open: an MSAA
  camera resolves before it grabs, and only ChilloutVR gives the figure per camera (eyes, portrait,
  CVR camera, mirrors).

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

### The WASM route, and what it would make of all this. BLOCKED: access declined 2026-08-19, waits for scripting's public release

*A design sketch, not a plan. Nothing here may be built, and no AI-assisted reading of ChilloutVR's
scripting implementation is done, until scripting ships to everyone with public documentation. They
are building world scripting first; avatar and prop scripting may be a long way off.*

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

### Two constraints worth remembering before designing anything. REFERENCE, standing rules, not work

**ChilloutVR runs an avatar's triggers on the wearer's machine alone.** Anything
contact-driven that the room must see has to be a synced parameter. This is not VRChat's
model and it invalidates the instinct to make contact parameters local because they are
"free everywhere".

**An animator-driven parameter can never sync.** `IsReadOnly` is true for anything
controlled by a curve, and `IsSynced` requires `!IsReadOnly`. Any design that computes a
value in the animator and expects the room to see it is already wrong; sync the inputs, or
compute it on every client from something that does sync.

### A mod-side GPU soft-body system: Joe's bigger idea, 2026-10-07. ANSWERED, NOT STARTED

Joe, 2026-10-07: a mod-side GPU soft-body system (draw a blanket with the pen, turn a prop into a soft
body). Feasible for mod users in about 2 ms with particles against capsules (true mesh-to-mesh is not);
non-mod viewers can only see pre-uploaded shader-simulated prefabs. Its own project, much bigger than
the GrabbyBones work.

First step, if Joe ever wants it: a local one-prop prototype to prove the budget.

Since then the live tissue (5.0.0, BETA) put a GPU soft body on an avatar with no mod, through
ChilloutVR's own blitter. That is not this idea (no pen, no props turned soft), but its cost numbers
are the nearest measured thing; see items 25 and 29 in the archive.

## Minor Ideas

Small features and leftovers, after 5.0.0.

### Live tissue ideas from a VRChat skin-touch module (item 30's unbuilt points). Approved as ideas 2026-11-06, NOT STARTED
Read for ideas only (DocMe Skin Touch 1.1.0: its licence bars its code, or a rewrite of it, in ours).
Numbers are item 30's own; its record is in `archive/Closed-2026-11.md`.
- (3) A press pushing the far side of a thin part: the far side never moves (8 cm limb, 2.3 mm at
  3 cm in). Build the facing ease only if a limb's flank is seen to bulge in game.
- (5) A soft shadow as a hand nears: a darkening per vertex from the hand slots, multiplied into the
  host's colour. Needs a colour hook per shader family in `YapsShaderPatcher` (vertex stage only
  today). Large.
- (6) Feet pressing: the client hands feet, hips and chest positions but no foot direction, so only
  an ankle ball would press.
- (7) The Avatar Tester's hand put on the skin by a click in the Scene view, its controls in a Scene
  view panel.
- (8) Presets named by feel, not part (maybe Thin; the enum saves by index, so add after Firm), and a
  part's feel saved to a small file to share.
- (9) Every problem at the top of the component with its fix; a line saying what the build adds; a
  cost measured on the user's own card; a pile-up ring round a deep press; a dent whose rim grows
  with the press.

### Thin colliders between the tissue's probes (item 31 point 7). Measured 2026-11-06, NOT STARTED
A finger or a 15 mm sphere that slips between two cloth probes is felt but placed 9 to 19 mm outside the skin:
a probe on the skin slides round a thin collider, so its depth is not there to read (matrix `colliders3`, all
11 felt). Options: probes that cannot slide (a bone's tip held to its line), a second layer under the skin, or a
thin collider pressing at least its radius. Unmeasured: 128 probes a pair against 48 on a heavy scene. Only
matters for MagicaCloth colliders with Other Colliders on; players' hands dent through the arrays regardless.

### Three ideas from the plush avatar's reports (item 40). 2026-11-14, NOT STARTED
- A report line when a toggle's off clip, baked from the scene, equals its on clip: a toggle switched
  on before converting can never switch off.
- A way to leave parts out of the squash and jelly regions by blendshape, material or vertex group
  (the jelly crumpled a cloth tag).
- The squash's "built by an older YAPS" status naming which version built it and which would rebuild.

### Fold into the next intentional YAPS shader change. From the code rot review (item 52), 2026-11-18
Each changes the patched shader text, so on its own it would make every YAPS avatar read "Needs a rebuild"
for nothing a user can see. Ride along with the next change that needs a rebuild anyway:
- Drop `_YAPS_SocketUp`, `_YAPS_SocketFront`, `_YAPS_ChannelOrigin`, `_YAPS_ChannelForward`,
  `_YAPS_ChannelUp`, `_YAPS_ChannelExtents` from `YapsShaderPatcher`'s property block and
  `YapsDebugOverlay.shader`: no HLSL reads them, no C# writes them.
- `_YAPS_SquashPulse`: a probe's diagnostic (`SquashTestBuilder`) shipped in every squash material's
  vertex stage. Move it into the probe.
- `yaps_resolve.cginc`: the `plugUp` parameter nothing reads.
- The live tissue's hand, grip and own-hand loops run every frame adding zeros on a cloth-carried
  part (`_Give` 0): guard them with `[branch] if (_Give > 0)`, or remove the free-mass mode once
  the probes stop using `-jellyMass`.

### A per-plug menu choice for its own sockets. IDEA, not started

- A per-plug menu choice. `_YAPS_SelfSockets` is a plain float, so a dropdown could animate it
  between a few sets, at the cost of one synced parameter a plug so every viewer bends alike.

### Own sockets through the material data provider, no lights (after SPS3). QUEUED 2026-10-31, not wanted until self-use problems are reported

- VRCFury's SPS3 beta (1.1436.1-beta.9, read 2026-10-31) drops lights and the grab for a new VRChat
  SDK feature: a contact receiver with `writeToMaterials` writes each matched sender's world matrix
  into the plug's renderer, and the socket packs its type, guide stop and flags into the sender's
  world scale, held by a scale constraint. ChilloutVR has nothing that hands one avatar another's
  transforms, so the cross-avatar half does not carry over. The self half does: a
  `CVRMaterialDataProvider` already hands a material its own avatar's transforms, same frame, on
  remote copies too (the squash and the jelly run on it). A plug would find its own sockets with no
  light and no atlas cell: none a frame late, none lost in a view too small for the atlas, bent in
  the shadow and depth passes (the ghost, YSH-18, for own sockets), no four-slot eviction, and
  guide stops on own sockets (the path record, in `archive/Closed-2026-11.md`). Sockets keep their lights and atlas
  cell for everyone else's plugs. A socket on a part cloth swings reads at rest through the
  provider and needs the skinned helper, a frame or two late. Costs: another source in the plug's
  search, a provider wired a plug, every patched shader stale. Not wanted until self-use problems
  are reported (Joe, 2026-10-31: "yeah queue it", after the jelly into YAPS).

### An id for prop and world sockets. IDEA, nothing needs it yet

- Props and world sockets carry no id and fall back to the vote. `SeedInstance` would give a
  spawnable its own; nothing needs it while nothing refuses a prop as own.

### The animator merger: weight audit and a corpus fixture. OPEN, none built; the weight audit waits on an in-game measurement

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

### The converted avatar as a Variant of the original prefab. PARKED

- **B, parked:** the converted avatar as a Variant of the user's ORIGINAL prefab. Never possible on a
  VRCFury or Modular Avatar avatar (their bakes flatten). Needs a link-keeping duplicate (the Ctrl+D
  route, not Instantiate), and Unity refuses to reparent inside a prefab instance: at least
  `ConstraintConverter.cs:668` and `FuryHeadFlattener.cs:78` move existing bones. Probe which passes
  refuse before building. A later VRChat component added to the original lands unconverted.
Related: item 44 (update a converted avatar in place, keeping hand edits).

### A budget planner. IDEA, not started

**A budget planner.** The sync tally exists in the report; make it interactive. Tick the
features you want, watch the bits, and let it name the cheapest thing to drop. Most avatars
that go over do it by accident, in a menu they never counted.

### A bench, so "optimised" stops being a claim. IDEA, not started

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

*(The two bullets and the calibration paragraph had fallen out of the queue's copy, which stopped at
the colon; restored on 2026-11-18 from 4fd00a1f, `docs/Roadmap.md`.)*

Pieces that exist since: `Dev/Probes/Perf/` holds editor probes that time single costs (GrabPassPerf,
JellyCostProbe, LoadHitchProbe, UploadCostProbe and the compile probes). Whether any covers the bench's
job (N avatars, a frame share in VR terms) was not checked on 2026-11-18; look there first.

### Conversion speed: what is left (2026-11-16, updated 2026-11-18). OPEN, the done parts shipped in 5.0.0

Both speed passes and the 2026-11-17 follow-ups shipped in 5.0.0; their record is in the archive
("Conversion speed: the two passes and what 2026-11-17 added"). Times are read from each step's lines
in `Diagnostics.md`. Still open, largest first:

- **The soft bake's cache by input hash.** Not built. The presses run side by side now (16f1a73d), but
  no avatar in the corpus bakes any more (the jelly takes every pair), so it only matters for a squash
  with no jelly or a refused part. Cache the result by a hash of every input the bake reads.
- **The texture-size scan (~7 s in a large project, again in the window after Convert).** Not
  built: a cached material map would have to catch every material change, or a texture shared
  with another avatar gets shrunk; and the conversion itself writes materials, so a cache cleared
  on any material import saves nothing on the common path. Reuse the conversion's plan in the
  window instead.
- **The merged controller is written and reimported 4-8 times a conversion** (1-3 s, up to
  10-15 s on a large VRCFury controller).
- **Volatile assets rehomed one forced import at a time** (`RehomeReferencedAsset`). Part fixed
  (936bfcc8, archive). Left: about 310-350 ms a material in its textures, not yet split; texture
  clones stay immediate, since a batched material must not name a texture written in the same batch.
- **One digest move since the 5.0.0 release run, not yet accepted** (full corpus 2026-11-16 on
  7a4d2163, 7 of 88 changed, the release run 0). One avatar's rehomed clip reads "Blush" where the
  baseline read "Blush 1", the file still `Blush 1.anim`, so a name is now taken before the import
  that renames it. Cosmetic; check whether the speed work moved it before accepting a new baseline.
  (The other six, a tie in the texture warning order, are fixed: e5bea683, archive.)
- **Upload time: the levers left.** Measured cold on 2026-11-16 (archive): a bundle build of 100 s,
  85 s of it shader compiles; on a plush avatar 173 s of shaders, the plug shader 72 s (1010 s CPU)
  and each squash copy ~20 s (156 variants each). Levers, in order: the plug shader's compile, then
  the number and cost of squash body copies (each role set is its own copy), or a cheaper JellyStep
  fragment. Optimize Mesh Data is ruled out (on 174 s, off 187 s, same 18.8 MiB).
- **The plug shader's compile: step 2 done (307fbe9d), its proof and steps 3 and 4 open.**
  - Not yet done for step 2: a cold bundle build of a plug avatar before and after, and an in-game
    look with Shader Filtering on (the scan's loops are `[loop]`, and the walk's unroll exists
    because the filter cuts a late `[loop]`). Step 1's fxc figures were measured in the plush
    session and not re-run in this repo.
  - Tried and dropped, do not retry: the four nearest as named scalars with compare-swaps (slower,
    deform 3.04 s against 2.50 s before), and the three scan passes folded into the same loop (5%
    more, 84 more slots). `YAPS_CHAIN_MAX` below 4 does not compile (indices written out for 4).
    The YapsJelly `skip_variants` trick buys nothing here: no double-wide stereo variants compile,
    and instancing and the vertex-light keyword are both needed in ChilloutVR.
  - Step 3: fewer passes run the deform. ForwardAdd and the outline pass could take the base
    pass's deformed position only if the plug skipped them, which changes how it lights. Ask Joe
    first.
  - Step 4, last resort: the deform in a blit (as the jelly does), the vertex program only reading
    it. A redesign, measured against the in-game cost first.
- **Tried and pulled, do not retry: parallel shader compiles.** Handing every new copy's passes to the
  compiler at once measured nothing; the import is the cost (archive).
- **A repatch's compile check uses the original's default keywords**, not the material's, so the
  variant a scene draws compiles again later.
- **Item 10's leftovers (the 2026-10-16 to 10-21 speed work), moved here 2026-11-18:**
  - **Squash, `YapsSquash.Add`:** on the heaviest of subset `ab5` (47.7 s) Add took 42.2 s: Shares
    16.9, Prewrite 11.2, Slots 5.5, SaveAssets 5.3, Flush 1.8; FollowSwaps 5.1 outside it. Shares and
    Prewrite were the targets (Shares 1.8 to 3.8 s on small avatars). Measured 2026-10-17: re-time
    before working on it, since the 2026-11-16 pass skips the spread on skin far from every part and
    remembers shader copies, and a tester's squash went 104 to 42.7 s.
  - **The first material on a fresh patch pays the compile** (P0): `new Material(shader)` 89.9 s in
    the trigger probe, the squash's first copy 445 s, the second 71 s, the rest about 200 ms; the GUI
    editor waits the same (20.1 s against 22.8 s in batch). Not the patcher (about 8 s) and not Thry
    (5.6 s). **Open:** whether anything avoids it short of fewer patches (one shader shared by more
    materials).
  - **Probes P1 to P5, never run** (copy project): P1 whether `new Material` /
    `CopyPropertiesFromMaterial` / `CopySerialized` carry override tags; P2 lock the same stand-ins in
    two calls and other company, diff the text; P3 how many compiles a line-1-blind `NameHash` would
    save over the 21 probe avatars (build it only if at least one per avatar); P4 Thry Unlock on a
    copy sharing a patch, tag kept against cleared; P5 a forced Thry exception, does the
    StartAssetEditing block stay open.
  - **The shared patch folder (`Assets/YAPS/Generated/Shaders`):** *Remove unused patched shaders*
    does not cover Generated/Locked. Deleting the folder turns every YAPS avatar in the project
    magenta until each is converted again (README Troubleshooting says so); a `.meta` GUID derived
    from each copy's hash would let one reconvert relink the rest, left out until somebody hits it.
    The stereo patcher's copies could share a folder the same way, but their verify depends on the
    folder, so not without a probe.
  - **Stereo planning, point (4) not done:** the report check reads each unit a second time where it
    could reuse what Prejudge read (about 4 ms a shader, read and SHA-256 twice per path). Also:
    Stage plans again a shader whose verdict an earlier avatar cached.
  - **Animator merge:** the texture clones in `RehomeVolatileAssets` (`SceneAssetRehomer`, each
    `CreateAsset` 0.7 s outside any batch). Batching them means a rescued material naming a texture
    created in the same batch; that path broke before (particles drawn as white squares). Timings
    [M], fix [R]. The material copies were batched on 2026-11-17 (936bfcc8).
  - **Contacts:** what is left of zone growth is the samples themselves, 3.3 s scan and 7.0 s
    attribution on the 212-contact avatar (was 282.8 s).
  - **Not to be retried** (measured, in the archive): compiling a material's passes together or in
    parallel (146 s against 147 s; the vertex-only verify 387 s); a home for the patches per output
    folder.
  - Timers left in the log on purpose: `YapsSquash.Add`'s steps, `ContactsConverter.Run`,
    `Texture sizes steps`, the merge's own.
- **Item 48's queued parts (the 2026-11-17 optimisation pass), moved here 2026-11-18:**
  - **Queued, larger:**
    - One tissue state texture for all slots (fxc: -11 KB a body pass at 3 slots, about 100-145 ms of
      first view; risk: several steps writing one pair).
    - Lighting keywords the host's vertex never reads still multiply the YAPS vertex variants.
      `LIGHTPROBE_SH` and the ForwardAdd axes come from Unity's built-in sets, which cannot be split
      by stage without redeclaring them: not done.
  - **Queued, smaller:** the empty asset refreshes per avatar (attribute first); the orphan clips
    left in a merged controller; batching the rehomer's texture writes (texture clones must stay
    immediate: a batched material must not name a texture written in the same batch, see
    "Conversion speed: what is left").
