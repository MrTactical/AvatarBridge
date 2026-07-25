# AvatarBridge — VRChat → ChilloutVR avatar converter

A Unity Editor tool that converts a **VRChat SDK3 avatar** into a **ChilloutVR CCK avatar**,
keeping as much working as possible and leaving you a clean starting point to finish by hand.

*(Bonus: if you don't have the VRChat SDK installed, the tool still runs in
[Setup mode](#bonus-setup-mode-without-the-vrchat-sdk) and prepares any humanoid for ChilloutVR.)*

**What sets it apart from older converters:**

- **VRCFury & Modular Avatar avatars work** — it runs Fury's own builder (and, for MA-only
  avatars, NDMF's manual bake) first, then converts the baked result, so toggles, linked
  clothing, merged armatures and full controllers survive.
- **PhysBones become real physics** — built-in **PhysBones → MagicaCloth2** (or DynamicBone),
  no external tool needed. Chains start from MagicaCloth2's own tuned presets rather than
  numbers guessed out of the PhysBone, so they behave predictably and are easy to tune.
- **Readable toggles** — clothing/prop toggles come out as one clean `Toggle <name>` layer
  each, driven by real `bool` parameters.
- **Bloat removed** — GoGo Loco, SPS/OGB/PCS and friends are stripped (one test avatar went
  from 3088 to 240 of 3200 sync bits).
- **Face tracking, your way** — auto-set-up native `CVRFaceTracking`, *or* the bundled
  CVR-VRCFT rig with eye tracking wired automatically (empties + constraints, per-avatar
  repath). Your choice.
- **Mod-aware** — PhysBone grab reactions (`_IsGrabbed` / `_Angle`) are wired for the
  [GrabbyBones](https://github.com/kafeijao/Kafe_CVR_Mods/tree/master/GrabbyBones) mod.
- **Quad avatars convert** — FinalIK-driven quadruped/puppet rigs carry through intact
  (ChilloutVR runs FinalIK natively), and avatar-breaking leftovers like stray cameras are
  cleaned up so the avatar actually loads.
- **Avatar scaler** — a height scaler with *linear (constant-speed) smoothing* so size changes
  glide instead of snapping, exposed as a **"Height (M)"** menu that **defaults to the avatar's
  own measured eye height**, so it's the same size before and after conversion (and `Height (M)`
  reads true metres). On by default; toggle off under Advanced Settings. Smoothing math:
  [JustSleightly's Controller Templates](https://notes.sleightly.dev/controller-templates).

> **Status: working, actively refined.** Full VRCFury and Modular Avatar avatars — clothing
> toggles, MagicaCloth physics, dozens of contacts, face tracking, even heavy SPS / poiyomi
> avatars and **quadruped (FinalIK) puppet avatars** — convert and run in ChilloutVR. Rows
> marked 🔷 in the tables below are correct in Unity but not yet independently confirmed
> in-game. Bugs and requests → open an issue.

## It's a head start, not a magic button

AvatarBridge does the tedious ~90% — rebuilding the animator, menus, physics, contacts and
face tracking. It does **not** make avatar setup brainless, and can't: VRChat and CVR are
different platforms and VRCFury setups vary endlessly. **It assumes you know your way around
Unity** — the Animator window, blend trees, the `CVRAvatar` component, reading a hierarchy.

Every run:

- writes a `ConversionReport.md` you're **expected to read** — act on each *Warning*,
  *Approximated* and *Skipped* entry;
- should be **tested in ChilloutVR** before you call it done (the editor can't show
  gestures, contacts or synced parameters actually running);
- is a **starting point to polish**, not a finished upload.

It saves an experienced creator hours; it won't turn a beginner into one.

## Requirements

| What | Version | Notes |
|---|---|---|
| Unity | **2022.3.22f1** | same version VRChat and CCK 4 use |
| VRChat Avatars SDK | SDK3, 3.10.x tested | required to convert — already in any Creator Companion avatar project. (Without it the tool still runs in [Setup mode](#bonus-setup-mode-without-the-vrchat-sdk).) |
| ChilloutVR CCK | **4.0.x** | always required — it's what the tool builds for. Tested against 4.0.1; CCK 3 paths are also handled |
| [VRCFury](https://vrcfury.com/download) | current | only if your avatars use it — most do, and it's usually already installed via VCC |
| [Modular Avatar](https://modular-avatar.nadena.dev/) | current | only if your avatars use it — baked via NDMF's manual bake before converting |
| [MagicaCloth2](https://assetstore.unity.com/packages/tools/physics/magica-cloth-2-242307) | *optional* | recommended PhysBone target; most ChilloutVR avatars use it |
| [DynamicBone](https://assetstore.unity.com/packages/tools/animation/dynamic-bone-16743) | *optional* | alternative PhysBone target; the free [VRLabs stub](https://github.com/VRLabs/Dynamic-Bones-Stub) is enough to convert |

Neither physics package is required — choose **Convert PhysBones to → None** and everything
except jiggle physics still converts.

## Installation

> ⚠️ **Import order matters.** Follow the steps top to bottom and let Unity finish compiling
> after each one. Importing out of order can corrupt VRCFury data or leave broken scripting
> defines.

1. **Unity 2022.3.22f1** — the exact version both the current VRChat SDK and CCK 4 target.
2. **A VRChat avatar project copy** — duplicate your Creator Companion project (Avatars SDK,
   VRCFury and the avatar are already inside). Never convert in your real upload project.
   Open the copy and let it compile cleanly before continuing.
3. **ChilloutVR CCK 4** — import the unitypackage.
4. **A physics package** *(optional — for PhysBone jiggle)* — [MagicaCloth2](https://assetstore.unity.com/packages/tools/physics/magica-cloth-2-242307)
   (paid, recommended), [DynamicBone](https://assetstore.unity.com/packages/tools/animation/dynamic-bone-16743)
   (paid; the free [VRLabs stub](https://github.com/VRLabs/Dynamic-Bones-Stub) is enough to
   convert), or neither (set **Convert PhysBones to → None**).
5. **VRCFury / Modular Avatar** — whichever your avatars use (most use one or both). Usually
   already installed via VCC; check the avatar for VRCFury or Modular Avatar components.
   Otherwise add [VRCFury](https://vrcfury.com/download) / [Modular Avatar](https://modular-avatar.nadena.dev/docs/intro).
6. **Avatars** — import any avatars not already in the project *after* VRCFury. The CVR-VRCFT
   face-tracking rig is bundled with AvatarBridge, so there's nothing extra to import for it
   (see [Face tracking](#face-tracking)).
7. **AvatarBridge — always last.** Grab the `.unitypackage` from the
   [Releases page](https://github.com/MrTactical/AvatarBridge/releases), or copy this repo
   into the project as `Assets/AvatarBridge`. It must live under `Assets`, not `Packages` —
   that's how the optional MagicaCloth2 / DynamicBone integration resolves.

One extra recompile right after importing AvatarBridge is normal — that's the tool
registering its `AVATARBRIDGE_MAGICA` / `AVATARBRIDGE_DYNBONE` scripting defines.

### Install troubleshooting

| Symptom | Cause & fix |
|---|---|
| Menu controls below a certain point don't respond in ChilloutVR | An invalid character in a menu parameter name breaks the CCK's Advanced Settings inspector, taking out every control drawn after it. Fixed in 1.1.3+ (`<impulse=0.1>` on Button parameters was the culprit) — re-convert on the latest build. |
| Menu has controls that do nothing (`GestureLeft`, dropdown entries labelled `---`) | Fixed in 1.1.1–1.1.4 — dead entries are pruned, dropdown padding is removed by renumbering, and parameters ChilloutVR drives itself are no longer given controls. Re-convert on the latest build. |
| Window shows a ✔/✘ checklist instead of options | The **CCK** is missing — import it, let Unity recompile, reopen the window. |
| Window only offers "Set up any avatar", no Convert mode | The VRChat SDK isn't installed, so there's no VRChat avatar to read — import the SDK to convert. [Setup mode](#bonus-setup-mode-without-the-vrchat-sdk) runs meanwhile. |
| VRCFury error: *"Found a null SerializeReference"* | The avatar was imported while VRCFury was missing, corrupting its Fury data. Delete the avatar's assets and scene copies, then re-import with VRCFury already installed. |
| Convert button greyed out with a face-tracking warning | **Unity Animator Blendtrees (DSR)** is selected but its bundled assets (`Assets/AvatarBridge/FaceTracking`) are missing — reimport AvatarBridge, or switch **Face tracking** to another mode. |
| Converted avatar is blank / invisible, or some materials go **pink** and vanish on Play | VRCFury baked the avatar's meshes/materials/shaders into its temp folder and later deleted it, orphaning them (null mesh = invisible, null material/shader = pink). Fixed in 0.9.1+ (meshes since 0.8.2) — AvatarBridge now copies those assets into `<output>/RehomedAssets`; re-convert on the latest build. |
| Avatar uploads fine but loads as the **"Error" robot** in ChilloutVR | The avatar carried a leftover Unity `Camera` (a common packed-unitypackage leftover, e.g. a "3rd Person Camera" rig) — CVR's asset filter crashes on it and blocks the whole avatar. Fixed in 0.9.9+ — cameras and audio listeners are stripped during cleanup; re-convert on the latest build. |
| Physics target warning | MagicaCloth2 / DynamicBone isn't installed, or needs one more recompile to be detected. |
| Project wedged after an out-of-order import | Close Unity, delete the `Library` folder, reopen and let it reimport. |

## Usage

The window walks you through three steps:

1. Open **Tools → Avatar Bridge → VRChat to ChilloutVR Converter**.
2. **Step 1** — drop your scene avatar (the object with the `VRCAvatarDescriptor`) into the
   field. VRCFury / Modular Avatar are detected and called out automatically.
3. **Step 2** — pick the physics target and face-tracking mode. The defaults suit most
   avatars; everything unusual lives under **Advanced options**. Your choices are remembered
   between sessions.
4. **Step 3 — Convert.** The original is deactivated and a `<name> (ChilloutVR)` copy appears,
   with its generated controller and report under `Assets/AvatarBridge/Output/<name>/`.
5. Read the conversion report (the **Open full report** button in the window takes you there)
   and act on anything flagged.
6. Upload through the CCK as usual.

Re-converting always works on the **original** avatar — delete the previous `(ChilloutVR)`
copy and its output folder first so results don't stack. You do **not** need to press
*Create Controller* on the `CVRAvatar`: every toggle is generated as its own animator layer
(unless you switch **Toggle style** to *CVR Native Targets*, which defers to the CCK builder).

## What gets converted

Every row carries an honest status:
✅ **confirmed in ChilloutVR** · 🔷 **converts cleanly, not yet confirmed in-game** ·
⚠️ **deliberate approximation** (see notes).

| VRChat | ChilloutVR | Status | Notes |
|---|---|---|---|
| Avatar descriptor (viewpoint, voice, face mesh, visemes, blink) | `CVRAvatar` | ✅ | voice position placed at the head bone like VRChat; blink is wired with the matching Blink Mode — see [below](#blink-modes-dont-line-up) |
| Expression parameters + menus | Advanced Avatar Settings (toggles / sliders / dropdowns) | ✅ | entries named after the menu control's label (`Cloak`), qualified only on collisions (`Hoodie (Tops)`) |
| Clothing / prop toggles | one `Toggle <name>` animator layer each | ✅ | pulled out of VRCFury's merged blend tree into classic Off/On layers |
| Parameter types | real `bool` / `int` / `float` | ✅ | VRCFury bakes every menu parameter as a float; each is retyped to what its menu control and animator conditions actually use — see [below](#parameter-types) |
| FX / Gesture layers (Base, Additive, Action optional) | merged into one CVR animator over the CCK `AvatarAnimator` | ✅ | CVR hand layers are removed when the Gesture layer is converted |
| PhysBones (+ colliders) | **MagicaCloth2 BoneCloth** or DynamicBone | ✅ (Magica) | chain, colliders and ignores transfer; the *feel* comes from MagicaCloth2 rather than the PhysBone — see [why](#physbones--magicacloth2). DynamicBone is 🔷 and does map values across |
| Non-synced parameters | `#`-prefixed (CVR local-only) | ✅ | keeps network traffic equivalent |
| `GestureLeft/Right` gesture selection | CVR `GestureLeftIdx/RightIdx` (int) | 🔷 | discrete gestures map 1:1 onto the int index params in both the FX layers and the CCK hand-pose layers; the analog fist (trigger-pressure curl) stays on the float `GestureLeft` |
| `GestureLeftWeight/RightWeight`, `MuteSelf`, `VRMode` | fed by a `CVRParameterStream` | 🔷 | trigger squeeze / mute / VR-mode piped from the game like VRChat's built-ins |
| VRC Parameter Driver | CCK `AnimatorDriver` | 🔷 | Set / Add / Random / Copy incl. range conversion; random-on-a-bool is ⚠️ (chance weighting lost) |
| Contacts (senders / receivers) | `CVRPointer` / `CVRAdvancedAvatarSettingsTrigger` | 🔷 | OnEnter → pulse, Proximity → distance stay task; Constant receivers are ⚠️ (exit resets to 0 even if a second pointer is inside) |
| Built-in VRC colliders (hands, fingers, head…) | `CVRPointer`s with standard tags | 🔷 | only for tags your receivers listen to |
| VRC Constraints (all 6 types) | Unity constraints | ✅ | sources/weights/offsets/rest/axes transfer 1:1. `Freeze To World`, Aim world-up mode and target-transform redirection are ⚠️ dropped; several same-type constraints on one object are ⚠️ merged into one (Unity/CVR hard limit) |
| FinalIK components (VRIK, CCD, FABRIK, Grounder Quadruped…) | kept as-is | ✅ | ChilloutVR whitelists and runs FinalIK natively; components and bone references carry through the conversion — quad avatars work |
| Avatar cameras / audio listeners | removed | ✅ | a stray `Camera` on an avatar crashes ChilloutVR's asset filter (avatar loads as the "Error" robot); the components are stripped, the GameObjects (often constraint targets) stay |
| PhysBone `_IsGrabbed` / `_Angle` | [GrabbyBones](https://github.com/kafeijao/Kafe_CVR_Mods/tree/master/GrabbyBones) mod | 🔷 | cloth objects named after their PhysBone parameter so grab-reactive FX works for anyone running the mod; `_Stretch` / `_Squish` / `_IsPosed` have no equivalent |
| Face-tracking blendshapes | native `CVRFaceTracking` **or** bundled CVR-VRCFT rig (auto eye empties + constraints, per-avatar repath) | 🔷 | your choice — see [Face tracking](#face-tracking) |
| VRC Head Chop | `FPRExclusion` | 🔷 | ⚠️ show/hide only — fractional scale factors can't be represented |
| VRC Spatial Audio Source | `AudioSource` spatial settings | 🔷 | ⚠️ approximation; gain curve not reproduced exactly |
| `Viseme`, `Voice`, `Seated`, `IsOnFriendsList`… | `VisemeIdx`, `VisemeLoudness`, `Sitting`, `IsFriend`… | 🔷 | CVR core parameter renames |
| Menu **Button** controls | ordinary toggles | ⚠️ | ChilloutVR's menu has no momentary control, so a Button becomes a toggle you switch off again. (Before 1.1.3 these got a `<impulse=0.1>` suffix — a CCK 3 convention that CCK 4 rejects, breaking the Advanced Settings inspector.) |

**Movement parameters.** CVR now has `VelocityX/Y/Z` core parameters (world-space speed), the
same idea as VRChat's, so AvatarBridge keeps them under their own names. CVR's `MovementX/Y`
is a *separate* thing — thumbstick/input deflection in `[-1..1]` — so nothing is auto-renamed
between them. Locomotion is left to CVR's own system by default (the Base layer isn't
converted unless you opt in). Caveat: CVR documents `VelocityX/Y/Z` as `[0…∞]` (magnitude)
while VRChat's are signed, so velocity-driven blends are worth a check.

### Blink modes don't line up

VRChat has **one** eyelid blendshape slot and expects a shape that closes both eyes.
ChilloutVR has **two** slots — Left Blink and Right Blink — plus a **Blink Mode** that says how
to read them. Because VRChat offers nothing else, authors routinely point its single slot at one
half of an L/R pair, so a descriptor commonly reads `vrc.blink_left`.

Copying that name straight into CVR's first slot leaves Blink Mode on *Separate* with Right Blink
empty — and only one eye ever closes. So the mode is always set explicitly, and because what the
descriptor names says little about what the mesh actually has, **a left/right pair wins whenever
one exists** — ChilloutVR can drive the eyes independently, which is strictly more than VRChat's
single slot could express:

| the mesh has | result |
|---|---|
| a left/right pair | both slots + **Separate**, even if the descriptor named a both-eyes shape |
| no pair, descriptor named a both-eyes shape | slot 0 + **Combined** |
| no pair, descriptor named one side | slot 0 + **Combined**, plus a ⚠️ warning — one eye is all that shape can close |

The report says which shapes were used and why, so a deliberate Combined setup is one field to put
back.

Side matching only accepts a spelled-out `left`/`right` or a standalone `l`/`r` token (`Blink L`,
`blink_r`, `L_Blink`), so an unrelated `l` inside a word can't trigger it. Pairing then requires
the two shapes to be **the same name but for the side token** — meshes routinely carry several
blink families at once (`! - Blink L`/`! - Blink R` alongside `vrc.blink_left`/`vrc.blink_right`),
and matching each side independently could take the left eye from one family and the right from
another. If the descriptor named nothing at all, the same detection runs against the mesh directly.

## Parameter types

**VRCFury bakes every menu parameter as a `float`**, whatever it really is — a float can carry a
bool or an int, and that saves it reasoning about intent. Harmless in VRChat. Not harmless in
ChilloutVR, which writes a menu value using the entry's *own* declared type: write a Bool into a
Float animator parameter and nothing happens at all. That is the single most common cause of
"the toggle does nothing in game".

So each parameter is retyped to what the avatar's own logic says it is, from two sources:

- **The menu control it drives.** A Toggle is a bool, a Dropdown is an int, a Slider is a float.
  That's what the author built.
- **How the animator compares it**, for parameters with no menu control. A parameter matched with
  `Equals` / `NotEqual` against whole numbers reaching 2 or more is a selector — nothing else
  behaves that way.

Against both sits one veto, checked first and absolute: anything read as a **quantity** — a blend
tree, motion time, speed, cycle offset, or a parameter an animation clip writes — stays `float`,
because those need the values in between. Vetoed parameters are listed in the report so you can
see the tool declined rather than missed them.

Transitions are rewritten to match whatever each parameter becomes, so nothing is left comparing a
bool with `> 0.5` or an int fractionally.

## Face tracking

Pick one in the **Face tracking** dropdown. Both options first strip whatever FT rig the
avatar shipped with (VRCFaceTracking / Jerry's Templates / Pawlygon / OSCmooth) so nothing
fights over the same blendshapes.

- **Native CVR Component** *(default)* — detects the avatar's FT blendshapes (Unified
  Expressions / SRanipal) and sets up ChilloutVR's built-in `CVRFaceTracking` component,
  auto-mapping the shapes. Self-contained — but the built-in solver is a bit stiff.
- **Unity Animator Blendtrees (DSR)** — injects DragonSkyRunner's *CVR Eye & Face Tracking*
  rig (**bundled** with AvatarBridge — no separate import) and rebuilds it onto your avatar:
  its layers and ~56 parameters are copied into the generated controller, every clip is
  repathed onto your actual eye bones and face mesh, and its shape vocabulary is **reconciled
  to whatever blendshapes your mesh actually has** — matching by name, casing, **ARKit ↔ Unified
  Expressions** aliases (e.g. `eyeBlinkLeft` → `EyeClosedLeft`, `mouthClose` → `MouthClosed`),
  and combined/split rules (a single `LipFunnel` vs the four quadrants). So an **ARKit avatar**
  works with the UE rig without renaming a thing. Direct float params, no binary encoding or
  smoothing — smoother and more expressive than the built-in.
- **None** — leave face tracking entirely to you.

**Either mode still needs a tracking source at runtime** — this is true of *any* CVR
face-tracking avatar, not just AvatarBridge's: run the
[VRCFaceTracking](https://store.steampowered.com/app/3329480/VRCFaceTracking/) Steam tool for
your hardware, then in ChilloutVR set *Settings → Implementation → Face tracking* →
**Eye/Mouth Tracking Module** to **OSC** (or the native module for your headset — SRanipal /
Tobii). Without a source feeding it, neither the native component nor the blendtree rig moves.

### Blendtree mode — automatic eye rig

This part is **specific to Unity Animator Blendtrees (DSR)** — the Native component does its
own eye tracking and needs none of it. The DSR rig steers two empties (`EyeTracking.L/.R`);
AvatarBridge generates those at your eye bones and adds a `RotationConstraint` to each eye
bone that follows its empty. The bundled ON/OFF clips toggle that constraint against CVR's
native eye-look/blink, so no mesh edits are needed. Because the rig's clips are authored
against a fixed hierarchy and a mesh named `Body`, AvatarBridge clone-on-write repaths them
onto your avatar (Unity animation paths are case-sensitive, so this matters even when your
bones are "the same" names).

> The blendtree rig is a **drop-in starting point, not zero-touch.** It assumes
> Unified-Expressions blendshapes. The **Eye Tracking** / **Face Tracking** menu toggles are
> added for you; after converting, check the generated eye `RotationConstraints` in play mode
> and tune the eye-gaze magnitude per DragonSkyRunner's readme. As DSR notes, neither the
> blendtree rig nor the native path picks up *every* possible combination of face shapes an
> avatar might use — expect some manual touch-up on unusual rigs.

The rig is bundled from
[DragonSkyRunner's CVR Eye & Face Tracking](https://github.com/DragonSkyRunner/ChilloutVR-Facetracking-Animator-Package)
and used with permission. See [Credits](#credits) for the redistribution note.

## Avatar scaler

On by default (the **Add height scaler** toggle in step 2 of the window), AvatarBridge adds a
height scaler with **linear, constant-speed smoothing** — size changes glide instead of
snapping — driven by a **"Height (M)"** input in the CVR menu.

It's **calibrated to the avatar automatically**: AvatarBridge measures the avatar's eye height
and generates the scale layer so `Height (M)` is true metres (`localScale = originalScale ×
Height / measuredHeight`), with the menu defaulting to that measured height. So the avatar is
**the same size before and after conversion**, and setting the menu to, say, `1.5` makes it
1.5 m tall. Smoothing math is
[JustSleightly's Controller Templates](https://notes.sleightly.dev/controller-templates).

## PhysBones → MagicaCloth2

**The mapping transfers structure and nothing else.** Which bone the chain hangs from, which
colliders it collides with, which transforms to leave out, and whether it started enabled. Every
physics value stays at MagicaCloth2's own defaults.

That is deliberate, and it took several attempts to arrive at. Earlier versions derived
MagicaCloth2 settings from PhysBone settings — gravity scaled into m/s², spring inverted into
damping, immobile inverted into inertia, pull folded into angle restoration. Every one of those
looked reasonable and every one had to be walked back after a real avatar misbehaved.

The reason isn't that the numbers were wrong. **The two systems are different kinds of
simulation.** PhysBones — like DynamicBone, which they were built to replace — are per-bone
*rotational springs*. MagicaCloth2 is a *particle position* solver: it moves particles through
space and reads bone rotations back out of where they land. A value meaning "springiness" to one
doesn't mean anything in particular to the other, so arithmetic between them produces confident
nonsense.

So there is no arithmetic. A stock MagicaCloth2 BoneCloth is a known-good configuration tuned by
the solver's own author; every converted chain behaves the same predictable way, and the
PhysBone's own numbers go into the report so you can tune from there:

> `tail — BoneCloth on MagicaCloth2's defaults, 3 collider(s). Source PhysBone was pull 0.2,`
> `spring 0.4, stiffness 0, gravity 0, immobile 0.75, radius 0.02 — none of those transfer…`

Stretch & squish, multi-child blending, `Is Animated`, angle limits and the grab parameters are
reported the same way, each naming the field to change if that chain wants it.

### Options

None of these add arithmetic — they swap one author-tuned baseline for another, or copy a value
across verbatim.

| setting | default | what it does |
|---|---|---|
| **Start from MagicaCloth2 presets** | on | Uses the preset matching the kind of chain — hair, tail, skirt, cape, accessory, or a spring preset by how firmly the PhysBone held its rest pose — instead of the global defaults |
| **Transfer angle limits** | off | Copies each PhysBone's limit angle across. MagicaCloth2's limit pushes on particle *positions* at a stiffness that snaps back hard, so this shakes some avatars and is the best result the tool gives on others |
| **Cap particle radius to bone spacing** | on | A safety rail, not a conversion: MagicaCloth2's radius is the particle size, and particles wider than the gap between bones shove each other apart |
| **Auto-assign nearby colliders** | off | See below |

### Preset matching

| chain | preset |
|---|---|
| hair — front / bangs / fringe / ahoge | **Front Hair** |
| hair — 5+ bones, twintails, ponytails, braids | **Long Hair** |
| hair — shorter | **Short Hair** |
| tail | **Tail** |
| cape / cloak / mantle / coat | **Cape** |
| skirt / dress / apron | **Skirt** |
| earring, ribbon, bell, pendant, necklace, collar, strap, zipper | **Accessory** |
| anything else — breasts, ears, props | **Soft / Middle / Hard Spring**, by how firmly the PhysBone held its rest pose |

Hair is matched before tails on purpose, so `twintail` and `ponytail` get hair's lighter settling
rather than a tail's weight. Short words match whole-word only — `belly` is not a bell, `detail`
is not a tail — while longer distinctive ones match anywhere, so `backhair` is still hair.

### Adding an angle limit by hand

Where you want one — a tail that shouldn't fold backwards, say — tick **Angle Limit** on that
MagicaCloth component, enter the angle from the report, and lower **Stiffness** until it stops
snapping. Stiffness defaults to `1`, a rigid snap-back, which is what makes a blanket transfer so
destructive on a chain that is also being animated.


### Auto-assign nearby colliders (optional, off by default)

Both systems keep an **explicit per-chain collider list** — a PhysBone only collides with the
colliders it names, and `Allow Collision` adds VRChat's *global* hand and finger colliders, never
the avatar's own. So authors typically wire the body colliders into the skirt and dress chains and
leave the tail, ears and hair with none. AvatarBridge copies those lists exactly, which means a
tail that passed through the leg in VRChat keeps doing so in ChilloutVR.

Turning this on goes further: each cloth is also handed any of the avatar's **own** converted
colliders that it could plausibly swing into. Nothing is invented — only existing shapes get
referenced by more chains.

The rule that keeps it safe is **rest position**:

> A collider is assigned only if **every bone of the chain starts outside it**, and some bone can
> reach it — reach being a bone's distance from the chain root, since the chain hangs off a fixed
> root and no bone can leave the sphere of that radius around it.

That distinction matters. A tail tip swinging past the calf starts clear of it and only ever
collides. Thigh jiggle sits permanently *inside* the hip capsule, and handing it that collider
would make the solver eject it — so it's skipped. Colliders parented within the chain itself are
skipped too (they travel with it), as are planes (unbounded, so "nearby" is meaningless).

Only colliders that *some* chain already references are candidates — an avatar collider no
PhysBone ever named isn't converted at all, so there's nothing to hand out.

Every assignment is listed in the conversion report with the closest rest-pose approach, e.g.
`Tail — auto-assigned 2 nearby collider(s): Calf.L (4.2 cm), Calf.R (5.1 cm)`. Since this
deliberately departs from the source avatar, check the result in play mode before uploading and
delete any assignment that makes a chain behave oddly.

## VRChat-only system stripping

Two subsystems that are dead weight in ChilloutVR are stripped by default (both toggleable):

- **GoGo Loco** — CVR has its own locomotion/flight/emote system; GoGo's layers fight it and
  waste ~15 synced parameters.
- **SPS / OGB / TPS haptics, PCS and the Wholesome add-on** — VRChat-specific penetration/
  haptics stacks whose shaders and contacts don't function in CVR, and which burn most of the
  sync budget.

Stripping removes their animator layers, scene objects, menu entries and synced parameters,
and prunes their leftover parameter math out of VRCFury's shared blend trees. Surviving
references fall back to local (`#`) parameters, so nothing breaks — it just stops syncing.
There's also an **Extra strip keywords** field for VRChat-only add-ons this list doesn't know
about yet (matched as both a parameter prefix and a layer-name fragment).

## VRCFury & Modular Avatar

Most modern avatars are built with [VRCFury](https://vrcfury.com/) and/or
[Modular Avatar](https://modular-avatar.nadena.dev/), which only assemble their real animator
layers, parameters, menus and merged meshes **at build time** — converting one directly would
lose every feature. AvatarBridge bakes them first, then converts the fully-baked copy:

- **VRCFury** — runs Fury's own *"Build a Test Copy"* pipeline. If the auto-bake fails, the
  report gives you the manual route: right-click the avatar → **VRCFury → Build a Test Copy**,
  then run AvatarBridge on that copy.
- **Modular Avatar** — runs NDMF's own *manual bake* (`AvatarProcessor.ManualProcessAvatar`).
  Note a VRCFury Test Copy already runs NDMF internally, so **MA + VRCFury** avatars are baked
  by the Fury step; the MA bake is for **MA-only** avatars. Manual route: **Tools → Modular
  Avatar → Manual bake avatar**, then run AvatarBridge on the result.

Both are invoked via reflection, so any version works with no hard dependency, and both bakes
are toggleable in the window (on by default).

## Bonus: Setup mode (without the VRChat SDK)

Converting is what AvatarBridge is for, and that needs the VRChat SDK. But the *second half* of
what it does — the CVR-side setup — never needed VRChat at all, so it's available on its own.
If the VRChat SDK isn't installed the window opens straight into **Setup mode**, and if it is,
there's a mode switch at the top.

Point it at any humanoid in your scene — a Booth model, an original, or an already-converted
avatar — and it will:

| Setup mode does | It can't (needs VRChat data) |
|---|---|
| Add and configure the `CVRAvatar` — viewpoint estimated from the eye/head bones, voice position | Menus and parameters, which live in VRChat expression assets |
| Auto-detect **visemes** (`vrc.v_aa` / `v_aa` / `aa` …) and wire lip sync | PhysBone and contact conversion |
| Auto-detect and wire **blink** blendshapes | Animator merging — there's no VRChat animator to merge |
| Set up **face tracking** — native `CVRFaceTracking` or the bundled DSR rig, eye rig included | |
| Inject the **height scaler**, auto-calibrated to the avatar | |
| Build a controller from the CCK's `AvatarAnimator` and write a `SetupReport.md` | |

<details>
<summary><b>Why isn't the VRChat SDK just stubbed out, like the DynamicBone stub?</b></summary>

Because it's the **input format**, not an optional dependency. Without it installed, a VRChat
avatar's components load as **missing scripts** — Unity can't deserialize them, so there's
nothing for any amount of reflection to read. The values survive in the YAML but are invisible
to the editor API.

A GUID-matching stub (how the VRLabs DynamicBone stub works) could technically recover the
simple components, but: it can never run **VRCFury or Modular Avatar** — those are real code
needing the real SDK — so Fury avatars would silently convert as empty shells; it collides with
the real SDK when both are present; and Unity silently defaults any field whose name stops
matching, so SDK updates would quietly produce wrong output.

The DynamicBone stub exists to get around a paid asset. The VRChat SDK is free and one click in
VCC/ALCOM — and you need the real one anyway the moment an avatar uses VRCFury. Setup mode
covers the genuinely SDK-free use case instead.
</details>

## Known limitations

**Not converted** (no CVR equivalent, or not implemented yet):

- **Eye look / gaze** — only the blink blendshape transfers; set up eye movement under *Eye
  Look Settings* on the `CVRAvatar`. (Blendshape-based face tracking, eye-region shapes
  included, *is* handled — see [Face tracking](#face-tracking).)
- **PhysBone posing, stretch & squish** and their `_Stretch` / `_Squish` / `_IsPosed`
  parameters (grabbing is partially covered via GrabbyBones).
- **VRC state behaviours** other than Parameter Driver (Animator Layer / Tracking /
  Locomotion / Playable Layer Control, Animator Play Audio) — removed and counted.
- **Synced animator layers**, **ONSP audio** and **jaw-flap lip sync**.
- **Content tags** — CVR's *Advanced Tagging* (NSFW, loud audio…) isn't inferred; set it
  before uploading.

**Converted with caveats:**

- **Action-layer emotes** rely on VRChat's emote flow, so converted states may be unreachable
  (the layer is off by default; CVR has its own emotes).
- **Constant contact receivers** reset to 0 when *any* pointer exits (CVR triggers don't count
  occupants).
- **2D blend trees driven by `GestureLeft/Right`** are flagged for manual review.
- **Stacked PhysBones** (several chains on one bone that VRChat toggles between) all convert,
  but only one is left driving the chain — two solvers on the same bones jitter rather than
  blend. Nothing is deleted, so re-enabling a different variant is one checkbox; the report
  names the one that was kept, and says so if none were active to begin with.
- **Stacked same-type constraints** (two `VRCPositionConstraint`s on one object, etc.) are
  merged into one Unity constraint — Unity and CVR allow only one per type per object, so the
  second one's own offsets/rest values are dropped (its sources are kept; reported as
  *Approximated*).
- **Shaders aren't translated** — Poiyomi etc. work as-is, and VRCFury-baked materials/shaders
  (SPS-patched, locked) are rescued out of Fury's temp so they don't render pink — but
  VRChat-specific rendering (SPS/TPS penetration especially) won't *function* in CVR.

## Reporting a bug

The fastest route: hit **Report an issue** in the AvatarBridge window (bottom of the panel, or
next to the results whenever a run produced warnings or errors). It opens a pre-filled GitHub
issue with your versions and detected packages already in it.

Two things make a report solvable immediately:

1. **Attach `ConversionReport.md`** — it's in `Assets/AvatarBridge/Output/<avatar name>/`, and the
   window's **Open full report** / **Show in Project** buttons take you straight to it. Nearly
   every bug fixed so far was diagnosed from this file.
2. **Attach the right log**, which depends on where it went wrong:

   | Symptom | Log to attach |
   |---|---|
   | Conversion errors, or the result looks wrong in Unity | Unity's console text or `Editor.log` |
   | Avatar misbehaves, or won't load, **in ChilloutVR** | ChilloutVR's `Player.log` — `%USERPROFILE%\AppData\LocalLow\ChilloutVR\ChilloutVR\Player.log` |

   A clean Unity log says nothing about an in-game failure; those live only in the CVR client
   log. (This is exactly how the "Error robot" bug was found — the Unity log was spotless.)

Please re-run on the [latest release](https://github.com/MrTactical/AvatarBridge/releases/latest)
before reporting, and note that logs contain your project's file paths (and CVR logs your
display name) — skim and redact if you'd rather not post them.

**Quick questions** — *"is this expected?"*, *"which mode should I use?"* — can go to **`mrtactical`**
on Discord instead. Anything reproducible is better off as a GitHub issue: those get tracked,
linked to a fix and closed with a release, whereas Discord messages scroll away.

## Credits

- Gesture tables, CVR core parameters and several conversion patterns were studied from
  [vrc3cvr](https://github.com/imagitama/vrc3cvr) (MIT) and the
  [Narazaka fork](https://github.com/Narazaka/vrc3cvr).
- Gesture mapping and the CVR Parameter Stream approach follow the official ChilloutVR
  *Avatar Animator Parameters* and *Parameter Stream* references.
- The DynamicBone gravity split mirrors
  [PhysBone-to-DynamicBone](https://github.com/FACS01-01/PhysBone-to-DynamicBone).
- MagicaCloth2 usage follows the official
  [runtime construction docs](https://magicasoft.jp/en/mc2_runtime_build/).
- VRCFury avatars are baked by [VRCFury](https://vrcfury.com/)'s own builder — AvatarBridge
  bundles no Fury code and has no hard dependency on it.
- The **CVR VRCFT** face-tracking rig is **DragonSkyRunner's**
  [CVR Eye & Face Tracking](https://github.com/DragonSkyRunner/ChilloutVR-Facetracking-Animator-Package),
  bundled under `Assets/AvatarBridge/FaceTracking` and redistributed here with the author's
  permission. All rights to that rig remain DragonSkyRunner's; if you reuse it, credit them.
  *(Their upstream repo carries no explicit license file yet — a `LICENSE` there would make the
  redistribution terms unambiguous.)*
- The [GrabbyBones](https://github.com/kafeijao/Kafe_CVR_Mods/tree/master/GrabbyBones) mod is
  an optional third-party project AvatarBridge targets but does not bundle.
- The optional **avatar scaler**'s constant-speed smoothing is built on
  [JustSleightly's Controller Templates](https://notes.sleightly.dev/controller-templates)
  (`AdvancedBlendTree` / `SmoothedFloat`). Those building-block clips are bundled (under fresh
  GUIDs to avoid clashing with the original package); credit and all rights to that technique
  remain JustSleightly's.

## License

MIT — see [LICENSE.md](LICENSE.md).
