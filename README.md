# AvatarBridge — VRChat → ChilloutVR avatar converter

A Unity Editor tool that converts a **VRChat SDK3 avatar** into a **ChilloutVR CCK avatar**,
keeping as much working as possible and leaving you a clean starting point to finish by hand.

**What sets it apart from older converters:**

- **VRCFury avatars work** — it runs Fury's own builder first, then converts the baked
  result, so toggles, linked clothing and full controllers survive.
- **PhysBones become real physics** — built-in **PhysBones → MagicaCloth2** (or DynamicBone),
  no external tool needed.
- **Readable toggles** — clothing/prop toggles come out as one clean `Toggle <name>` layer
  each, driven by real `bool` parameters.
- **Bloat removed** — GoGo Loco, SPS/OGB/PCS and friends are stripped (one test avatar went
  from 3088 to 240 of 3200 sync bits).
- **Face tracking, your way** — auto-set-up native `CVRFaceTracking`, *or* the bundled
  CVR-VRCFT rig with eye tracking wired automatically (empties + constraints, per-avatar
  repath). Your choice.
- **Mod-aware** — PhysBone grab reactions (`_IsGrabbed` / `_Angle`) are wired for the
  [GrabbyBones](https://github.com/kafeijao/Kafe_CVR_Mods/tree/master/GrabbyBones) mod.

> **Status: early but working.** A full VRCFury avatar — clothing toggles, MagicaCloth
> physics, dozens of contacts, face tracking — converts and runs in ChilloutVR. Anything
> marked 🔷 in the tables below is correct in Unity but not yet confirmed in-game. Please
> open issues.

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
| VRChat Avatars SDK | SDK3, 3.10.x tested | already in any Creator Companion avatar project |
| ChilloutVR CCK | **4.0.x** | tested against 4.0.1; CCK 3 paths are also handled |
| [VRCFury](https://vrcfury.com/download) | current | only if your avatars use it — most do, and it's usually already installed via VCC |
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
5. **VRCFury** — if your avatars use it (most do). Usually already installed via VCC; check
   for VRCFury components on the avatar or a *VRCFury* entry in the VCC package list.
   Otherwise add it from [vrcfury.com](https://vrcfury.com/download).
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
| Window shows a ✔/✘ checklist instead of options | A required SDK is missing — import it, let Unity recompile, reopen the window. |
| VRCFury error: *"Found a null SerializeReference"* | The avatar was imported while VRCFury was missing, corrupting its Fury data. Delete the avatar's assets and scene copies, then re-import with VRCFury already installed. |
| Convert button greyed out with a face-tracking warning | **Unity Animator Blendtrees (DSR)** is selected but its bundled assets (`Assets/AvatarBridge/FaceTracking`) are missing — reimport AvatarBridge, or switch **Face tracking** to another mode. |
| Physics target warning | MagicaCloth2 / DynamicBone isn't installed, or needs one more recompile to be detected. |
| Project wedged after an out-of-order import | Close Unity, delete the `Library` folder, reopen and let it reimport. |

## Usage

1. Open **Tools → Avatar Bridge → VRChat to ChilloutVR Converter**.
2. Drop your scene avatar (the object with the `VRCAvatarDescriptor`) into the field.
3. Review the options — the defaults are the recommended ones. Pick the physics target and
   face-tracking mode.
4. **Convert avatar.** The original is deactivated and a `<name> (ChilloutVR)` copy appears,
   with its generated controller and report under `Assets/AvatarBridge/Output/<name>/`.
5. Read `ConversionReport.md` and act on anything flagged.
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
| Avatar descriptor (viewpoint, voice, face mesh, visemes, blink) | `CVRAvatar` | ✅ | voice position placed at the head bone like VRChat |
| Expression parameters + menus | Advanced Avatar Settings (toggles / sliders / dropdowns) | ✅ | entries named after the menu control's label (`Cloak`), qualified only on collisions (`Hoodie (Tops)`) |
| Clothing / prop toggles | one `Toggle <name>` animator layer each | ✅ | pulled out of VRCFury's merged blend tree into classic Off/On layers |
| Toggle parameters | real `bool` parameters | ✅ | VRCFury bakes toggles as floats; those used only in conditions are retyped |
| FX / Gesture layers (Base, Additive, Action optional) | merged into one CVR animator over the CCK `AvatarAnimator` | ✅ | CVR hand layers are removed when the Gesture layer is converted |
| PhysBones (+ colliders) | **MagicaCloth2 BoneCloth** or DynamicBone | ✅ (Magica) | DynamicBone path is 🔷; see [mapping](#physbones--magicacloth2) |
| Non-synced parameters | `#`-prefixed (CVR local-only) | ✅ | keeps network traffic equivalent |
| `GestureLeft/Right` gesture selection | CVR `GestureLeftIdx/RightIdx` (int) | 🔷 | discrete gestures map 1:1 onto the int index params in both the FX layers and the CCK hand-pose layers; the analog fist (trigger-pressure curl) stays on the float `GestureLeft` |
| `GestureLeftWeight/RightWeight`, `MuteSelf`, `VRMode` | fed by a `CVRParameterStream` | 🔷 | trigger squeeze / mute / VR-mode piped from the game like VRChat's built-ins |
| VRC Parameter Driver | CCK `AnimatorDriver` | 🔷 | Set / Add / Random / Copy incl. range conversion; random-on-a-bool is ⚠️ (chance weighting lost) |
| Contacts (senders / receivers) | `CVRPointer` / `CVRAdvancedAvatarSettingsTrigger` | 🔷 | OnEnter → pulse, Proximity → distance stay task; Constant receivers are ⚠️ (exit resets to 0 even if a second pointer is inside) |
| Built-in VRC colliders (hands, fingers, head…) | `CVRPointer`s with standard tags | 🔷 | only for tags your receivers listen to |
| VRC Constraints (all 6 types) | Unity constraints | 🔷 | Parent/Position/Rotation/Scale/LookAt tested; Aim untested. `Freeze To World` and target-transform redirection are ⚠️ dropped |
| PhysBone `_IsGrabbed` / `_Angle` | [GrabbyBones](https://github.com/kafeijao/Kafe_CVR_Mods/tree/master/GrabbyBones) mod | 🔷 | cloth objects named after their PhysBone parameter so grab-reactive FX works for anyone running the mod; `_Stretch` / `_Squish` / `_IsPosed` have no equivalent |
| Face-tracking blendshapes | native `CVRFaceTracking` **or** bundled CVR-VRCFT rig (auto eye empties + constraints, per-avatar repath) | 🔷 | your choice — see [Face tracking](#face-tracking) |
| VRC Head Chop | `FPRExclusion` | 🔷 | ⚠️ show/hide only — fractional scale factors can't be represented |
| VRC Spatial Audio Source | `AudioSource` spatial settings | 🔷 | ⚠️ approximation; gain curve not reproduced exactly |
| `Viseme`, `Voice`, `Seated`, `IsOnFriendsList`… | `VisemeIdx`, `VisemeLoudness`, `Sitting`, `IsFriend`… | 🔷 | CVR core parameter renames |
| Menu **Button** controls | `<impulse=0.1>` auto-reset parameters | 🔷 | CCK 3-era convention, not re-verified on CCK 4 |

**Movement parameters.** CVR now has `VelocityX/Y/Z` core parameters (world-space speed), the
same idea as VRChat's, so AvatarBridge keeps them under their own names. CVR's `MovementX/Y`
is a *separate* thing — thumbstick/input deflection in `[-1..1]` — so nothing is auto-renamed
between them. Locomotion is left to CVR's own system by default (the Base layer isn't
converted unless you opt in). Caveat: CVR documents `VelocityX/Y/Z` as `[0…∞]` (magnitude)
while VRChat's are signed, so velocity-driven blends are worth a check.

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
  to whatever combined/split blendshapes your mesh actually has** (VRCFT blended-shape rules —
  e.g. driving a single `LipFunnel` when the rig has the four quadrants, or vice versa). Direct
  float params, no binary encoding or smoothing — smoother and more expressive than the built-in.
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
> Unified-Expressions blendshapes. After converting: set up the **Eye Tracking** / **Face
> Tracking** toggles in Advanced Avatar Settings, check the generated eye `RotationConstraints`
> in play mode, and tune the eye-gaze magnitude per DragonSkyRunner's readme. As DSR notes,
> neither the blendtree rig nor the native path picks up *every* possible combination of face
> shapes an avatar might use — expect some manual touch-up on unusual rigs.

The rig is bundled from
[DragonSkyRunner's CVR Eye & Face Tracking](https://github.com/DragonSkyRunner/ChilloutVR-Facetracking-Animator-Package)
and used with permission. See [Credits](#credits) for the redistribution note.

## PhysBones → MagicaCloth2

| PhysBone | MagicaCloth2 |
|---|---|
| pull / stiffness (+curves) | angle restoration stiffness |
| spring / momentum | damping (inverted) + velocity attenuation |
| gravity, gravityFalloff | gravity (m/s², scaled ×9.8), gravityFalloff (1:1) |
| immobile | world inertia (inverted) |
| radius + curve | particle radius + curve |
| limit type Angle/Hinge/Polar | angle limit (symmetric approximation for Hinge/Polar) |
| ignore transforms | bone attribute *Invalid* |
| colliders (sphere/capsule/plane) | Magica sphere/capsule/plane colliders |

This is a *feel* approximation, not a physics-accurate translation — the two solvers differ,
so expect to nudge values (tuning constants are at the top of
`Editor/Core/Physics/MagicaClothWriter.cs`). `immobile` and the angle limits are applied via
reflection since MagicaCloth2's fields move between versions; a mismatch is reported, not
silently dropped. `maxStretch` (squash & stretch) has no equivalent and is skipped.

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

## VRCFury avatars

Most modern avatars use [VRCFury](https://vrcfury.com/), which only builds its real animator
layers, parameters and menus **at upload time** — converting one directly would lose every
Fury feature. AvatarBridge handles this automatically: it runs **VRCFury's own "Build a Test
Copy" pipeline** (via reflection, so any Fury version works with no hard dependency), then
converts the fully-baked copy. If the auto-bake fails, the report tells you the manual route:
right-click the avatar → **VRCFury → Build a Test Copy**, then run AvatarBridge on that copy.

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
  but only the ones enabled at bake time start active — if none were, the report says so.
- **Shaders are untouched** — Poiyomi etc. generally work, but VRChat-specific rendering
  (SPS/TPS penetration shaders especially) will not.

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

## License

MIT — see [LICENSE.md](LICENSE.md).
