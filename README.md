# AvatarBridge — VRChat → ChilloutVR avatar converter

AvatarBridge is a Unity Editor tool that converts a **VRChat SDK3 avatar** into a
**ChilloutVR CCK avatar**, keeping as much functionality as possible and leaving you a
working starting point to finish by hand.

What makes it different from older converters:

- **VRCFury avatars work.** Fury only builds its real layers, parameters and menus at
  upload time, so AvatarBridge runs Fury's own builder first and converts the baked result.
- **PhysBones become real physics.** Built-in **PhysBones → MagicaCloth2** (or DynamicBone)
  conversion, no external tool needed.
- **Toggles come out readable.** Clothing and prop toggles are pulled out of VRCFury's
  merged blend tree into one clean `Toggle <name>` layer each, driven by real `bool`
  parameters.
- **VRChat-only bloat is removed.** GoGo Loco, SPS/OGB/PCS and friends are stripped, which
  on the test avatar cut sync usage from 3088 to 240 of 3200 bits.
- **Face tracking is set up for you.** VRCFaceTracking / Unified Expressions / SRanipal
  blendshapes are detected and wired into CVR's native `CVRFaceTracking` component.

Every conversion produces a `ConversionReport.md` listing what converted 1:1, what was
approximated, and what has no ChilloutVR equivalent — the table below marks which parts
have actually been confirmed in-game.

> **Status: early but working.** A full VRCFury avatar ( toggles, MagicaCloth
> physics, contacts) converts and runs in ChilloutVR. Expect rough edges on anything
> marked 🔷 below, and please open issues.

## Read this first: it's a head start, not a magic button

AvatarBridge does the tedious 90% — rebuilding the animator, menus, physics, contacts and
face tracking — so you don't have to do it by hand. It does **not** turn avatar setup into
a no-brainer, and it never will: VRChat and ChilloutVR are different platforms, VRCFury
setups are effectively infinite in variety, and no tool can read intent or cover every
edge case.

So this tool assumes **you already know your way around Unity and avatar setup.** You
should be comfortable with the Animator window, blend trees, the CVRAvatar component, and
reading a hierarchy. If a toggle animates something unexpected, a physics chain needs
tuning, or a menu entry points at the wrong object, that's yours to finish — the tool got
you to the 90%, not the 100%.

Concretely, every conversion:

- **Produces a report you are expected to read.** `ConversionReport.md` lists everything
  marked *Warning*, *Approximated* or *Skipped*. Those are the spots that need your eyes.
- **Should be tested in ChilloutVR before you call it done.** The editor can't show you
  gestures, contacts or synced parameters actually running.
- **Is a starting point to build on, not a finished upload.** Treat the output like a
  fresh avatar port you're now polishing, because that's what it is.

If you don't know Unity yet, that's fine — but learn the basics first, or grab someone who
has. This tool will save an experienced creator hours; it won't make an inexperienced one
into one.

## Requirements

| What | Version | Notes |
|---|---|---|
| Unity | **2022.3.22f1** | same version VRChat and CCK 4 use |
| VRChat Avatars SDK | SDK3, 3.10.x tested | already in any Creator Companion avatar project |
| ChilloutVR CCK | **4.0.x** | tested against 4.0.1; CCK 3 paths are also handled |
| [VRCFury](https://vrcfury.com/download) | current | only if your avatars use it — most modern ones do, and it's usually already installed via VCC |
| [MagicaCloth2](https://assetstore.unity.com/packages/tools/physics/magica-cloth-2-242307) | *optional* | recommended PhysBone target; most ChilloutVR avatars use it |
| [DynamicBone](https://assetstore.unity.com/packages/tools/animation/dynamic-bone-16743) | *optional* | alternative PhysBone target; the free [VRLabs stub](https://github.com/VRLabs/Dynamic-Bones-Stub) is enough to convert |

Neither physics package is required: choose **Convert PhysBones to → None** and everything
except jiggle physics still converts.

## Installation

> ⚠️ **Import order matters.** Follow the steps top to bottom and let Unity finish
> compiling after each one. Importing out of order can corrupt VRCFury data or leave
> broken scripting defines.

1. **Unity 2022.3.22f1** — the exact version both the current VRChat SDK and CCK 4 target.
2. **A VRChat avatar project copy** — duplicate your existing Creator Companion project
   (Avatars SDK, VRCFury and the avatar are already inside). Never convert in your real
   upload project. Open the copy and make sure it compiles cleanly before continuing.
3. **ChilloutVR CCK 4** — import the unitypackage.
4. **A physics package — optional.** Needed only to convert PhysBone jiggle (hair, ears,
   tails, skirts). Pick one:
   - [**MagicaCloth2**](https://assetstore.unity.com/packages/tools/physics/magica-cloth-2-242307)
     (paid, recommended) — the modern jiggle-physics system most ChilloutVR avatars use.
     This is the best-quality conversion target.
   - [**DynamicBone**](https://assetstore.unity.com/packages/tools/animation/dynamic-bone-16743)
     (paid) — the older system ChilloutVR also supports natively. The free
     [VRLabs Dynamic-Bones-Stub](https://github.com/VRLabs/Dynamic-Bones-Stub) is enough
     to *convert* an avatar, though the physics won't run in the editor.
   - **Neither?** Set **Convert PhysBones to → None** in the converter window. Everything
     else (toggles, menus, animators, contacts, constraints) still converts — you just
     get no jiggle physics, and can add it by hand later.
5. **VRCFury** — needed if your avatars use it (most modern ones do). Usually it's
   already installed through the Creator Companion, so check the project first: if you
   see VRCFury components on your avatar, or a *VRCFury* entry in VCC's package list,
   you're done. Otherwise add it via VCC or from [vrcfury.com](https://vrcfury.com/download).
6. **Avatar packages** — only if you're adding avatars that aren't in the project yet:
   import them now, *after* VRCFury.
7. **AvatarBridge** — always last. Grab the `.unitypackage` from the
   [Releases page](https://github.com/MrTactical/AvatarBridge/releases), or copy this
   repository into the project as `Assets/AvatarBridge`. It must live under `Assets`,
   not `Packages` — that's how the optional MagicaCloth2 / DynamicBone integration
   resolves automatically.

One extra recompile right after importing AvatarBridge is normal — that's the tool
registering its `AVATARBRIDGE_MAGICA` / `AVATARBRIDGE_DYNBONE` scripting defines after
detecting what's installed.

### Install troubleshooting

| Symptom | Cause & fix |
|---|---|
| Converter window shows a ✔/✘ checklist instead of options | A required SDK is missing — import it, let Unity recompile, reopen the window. |
| Don't own MagicaCloth2 or DynamicBone | Set **Convert PhysBones to → None**. Everything but jiggle physics converts. |
| VRCFury error: *"Found a null SerializeReference"* | The avatar was imported while VRCFury was missing, which corrupted its Fury component data. Delete the avatar's assets and scene copies, then re-import its package with VRCFury already installed. |
| Physics target warning in the converter window | MagicaCloth2 / DynamicBone isn't installed, or needs one more recompile to be detected. |
| Project completely wedged after an out-of-order import | Close Unity, delete the `Library` folder, reopen and let it reimport. |

## Usage

1. Open **Tools → Avatar Bridge → VRChat to ChilloutVR Converter**.
2. Drop your scene avatar (the object with the `VRCAvatarDescriptor`) into the field.
3. Pick the physics target (MagicaCloth2 recommended, or **None** if you don't own either
   physics package) and review the options — the defaults are the recommended ones.
4. **Convert avatar.** The original is deactivated and a `<name> (ChilloutVR)` copy appears,
   with its generated controller and report under `Assets/AvatarBridge/Output/<name>/`.
5. Read `ConversionReport.md` and act on anything marked *Warning*, *Skipped* or
   *Approximated*.
6. Upload through the CCK as usual.

Converting again always works on the **original** avatar — delete the previous
`(ChilloutVR)` copy and its output folder first, so you don't stack results.

Because every toggle is generated as its own animator layer, you do **not** need to press
*Create Controller* on the `CVRAvatar` component. (If you switch **Toggle style** to *CVR
Native Targets*, you do — that mode hands object toggles to the CCK's own builder.)

## What gets converted

AvatarBridge is young software. To avoid overselling, every row below carries an honest
status:

- ✅ **Confirmed in ChilloutVR** — actually tested on an uploaded avatar.
- 🔷 **Converts cleanly, not yet confirmed in-game** — the output is correct in Unity and
  the conversion report is clean, but nobody has verified the in-game behaviour yet.
- ⚠️ **Approximation** — deliberately lossy; details in the notes.

| VRChat | ChilloutVR | Status | Notes |
|---|---|---|---|
| Avatar descriptor (viewpoint, voice, face mesh, visemes, blink) | `CVRAvatar` | ✅ | voice position placed at the head bone like VRChat |
| Expression parameters + menus | Advanced Avatar Settings (toggles / sliders / dropdowns) | ✅ | entries are named after the menu control's own label (`Cloak`), qualified only when two collide (`Hoodie (Tops)`) |
| Clothing / prop toggles | one `Toggle <name>` animator layer each | ✅ | pulled out of VRCFury's merged blend tree into classic Off/On layers; optionally handed to CVR's own builder as GameObject targets instead |
| Toggle parameters | real `bool` parameters | ✅ | VRCFury bakes toggles as floats; those used only in conditions are retyped |
| FX / Gesture layers (Base, Additive, Action optional) | merged into one CVR animator on top of the CCK `AvatarAnimator` | ✅ | CVR hand layers are removed when the Gesture layer is converted |
| PhysBones (+ colliders) | **MagicaCloth2 BoneCloth** or DynamicBone | ✅ (MagicaCloth2) | DynamicBone path is 🔷; see mapping below |
| Non-synced parameters | `#`-prefixed (CVR local-only convention) | ✅ | keeps network traffic equivalent; test avatar went 3088 → 240 of 3200 sync bits |
| `GestureLeft/Right` int values | CVR float gesture values | ✅ | mapping verified against the official CCK parameter reference — Open Hand −1, Fist 0–1, Thumbs Up 2, Gun 3, Point 4, Peace 5, Rock'n'Roll 6 |
| `GestureLeftWeight/RightWeight`, `MuteSelf`, `VRMode` | fed by a `CVRParameterStream` | 🔷 | trigger squeeze / mute / VR-mode piped from the game like VRChat's built-ins |
| VRC Parameter Driver | CCK `AnimatorDriver` | 🔷 | Set / Add / Random / Copy incl. range conversion; Random on a bool is ⚠️ (chance weighting is lost) |
| Contact senders | `CVRPointer` (one per collision tag) | 🔷 | with matching trigger collider shape |
| Contact receivers | `CVRAdvancedAvatarSettingsTrigger` | 🔷 | OnEnter → pulse, Proximity → distance-driven stay task; Constant is ⚠️ (exit resets to 0 even if a second pointer is still inside) |
| Built-in VRC colliders (hands, fingers, head…) | `CVRPointer`s with standard tags | 🔷 | only for tags your receivers actually listen to |
| VRC Constraints (all 6 types) | Unity constraints | 🔷 | Parent/Position/Rotation/Scale/LookAt exercised on a real avatar; Aim is untested. `Freeze To World` and target-transform redirection are ⚠️ dropped |
| VRC Head Chop | `FPRExclusion` | 🔷 | ⚠️ show/hide only — scale factors between 0 and 1 can't be represented |
| VRC Spatial Audio Source | `AudioSource` spatial settings | 🔷 | ⚠️ approximation; the gain curve is not reproduced exactly |
| `Viseme`, `Voice`, `Seated`, `IsOnFriendsList`… | `VisemeIdx`, `VisemeLoudness`, `Sitting`, `IsFriend`… | 🔷 | CVR core parameter renames |
| Face-tracking blendshapes (VRCFaceTracking / Unified / SRanipal) | native `CVRFaceTracking` component | 🔷 | auto-detected and auto-mapped; VRChat's OSC-driven FX plumbing is dropped in favour of CVR's native path |
| Menu **Button** controls | `<impulse=0.1>` auto-reset parameters | 🔷 | this convention comes from CCK 3-era tooling and hasn't been re-verified on CCK 4 |

**A note on movement parameters.** VRChat and ChilloutVR name these differently *and mean
different things*: VRChat's `VelocityX/Y/Z` is world-space movement speed, while CVR's
`MovementX/Y` is thumbstick/input deflection. They are **not** interchangeable, so
AvatarBridge does not auto-rename between them. Locomotion is left to CVR's own system by
default (the Base layer isn't converted unless you opt in), which sidesteps the mismatch.

### PhysBones → MagicaCloth2 mapping

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

Tuning constants (`GravityScale`, damping range) sit at the top of
`Editor/Core/Physics/MagicaClothWriter.cs` if the feel needs adjusting.

Honesty notes on this table: the mapping is a *feel* approximation, not a physics-accurate
translation — the two solvers are different, so expect to nudge values. `immobile` and the
angle limits are applied through reflection because MagicaCloth2's constraint fields move
between versions; if your version doesn't match, the conversion says so in the report
instead of failing silently. Squash & stretch (`maxStretch`), grab/pose interaction, and
the `_IsGrabbed` / `_Angle` / `_Stretch` parameters have **no** ChilloutVR equivalent and
are reported as skipped.

## VRChat-only system stripping

Two subsystems common on modern avatars are dead weight in ChilloutVR, and AvatarBridge
strips them by default (both toggleable in the window):

- **GoGo Loco** — CVR ships its own locomotion, flight and emote system; GoGo's layers
  fight it and waste ~15 synced parameters (including a 256-value emote int).
- **SPS / OGB / TPS haptics, PCS and the Wholesome add-on** — VRChat-specific
  penetration/haptics stacks. Their shaders and contact conventions don't function in CVR,
  and they typically burn more sync budget than everything else combined.

Stripping removes their animator layers, scene objects, menu entries and synced
parameters, and prunes their leftover parameter math out of VRCFury's shared blend trees
(without that, orphaned integrators keep running and produce garbage values). Any
surviving reference falls back to a local (`#`) parameter, so nothing breaks — it just
stops syncing. On a Fury-heavy test avatar this took sync usage from **3088 to 240 of
3200 bits**.

There's also an **Extra strip keywords** field for VRChat-only add-ons this list doesn't
know about yet: each keyword is matched as both a parameter prefix and a layer-name
fragment.

## VRCFury avatars

Most modern avatars use [VRCFury](https://vrcfury.com/), which only creates its real
animator layers, parameters and menus **at build time** — converting such an avatar
directly would silently lose every Fury feature (toggles, linked clothing, full
controllers, GogoLoco hooks…).

AvatarBridge handles this automatically: when it detects VRCFury components it first runs
**VRCFury's own "Build a Test Copy" pipeline** (via reflection, so any Fury version works
without a hard dependency), then converts the fully-baked copy. The baked avatar contains
plain FX layers, expression parameters, menus and PhysBones with all Fury components
already stripped — exactly what the converter needs. Fury's internal `VF##_`-prefixed
parameters come out non-synced and are automatically marked local (`#` prefix) in CVR.

If the automatic bake fails (e.g. an unusual VRCFury version), the report tells you the
manual route: right-click the avatar → **VRCFury → Build a Test Copy**, then run
AvatarBridge on the test copy.

## Known limitations

**Not converted at all** (no ChilloutVR equivalent, or not implemented yet):

- **Eye look / gaze.** Only the blink blendshape is transferred. Set up eye movement
  yourself under *Eye Look Settings* on the `CVRAvatar` component. (Blendshape-based face
  tracking, including eye-region shapes, *is* set up automatically — see the table above.)
- **PhysBone interaction** — grabbing, posing, stretch/squish and the
  `_IsGrabbed` / `_Angle` / `_Stretch` parameters (reported per chain).
- **VRC state behaviours** other than Parameter Driver — Animator Layer Control, Tracking
  Control, Locomotion Control, Playable Layer Control, Animator Play Audio. Removed and
  counted in the report.
- **Synced animator layers**, **ONSP audio sources** and **jaw-flap lip sync**.
- **Content tags.** ChilloutVR's *Advanced Tagging* (NSFW, loud audio, …) is not inferred
  from the avatar — set it yourself before uploading.

**Converted with caveats:**

- **Action layer emotes** depend on VRChat's emote flow, so converted states may be
  unreachable. The layer is off by default; CVR has its own emote system.
- **Constant contact receivers** reset to 0 when *any* pointer exits, even if a second one
  is still inside (CVR triggers don't count occupants).
- **2D blend trees driven by `GestureLeft/Right`** are flagged for manual review, since the
  gesture value ordering differs.
- **Stacked PhysBones** (several chains on one bone that VRChat toggles between, e.g. cake
  PB) all convert, but only the ones enabled at bake time start active — if *none* were,
  the report says so and that chain won't move until you enable one.
- **Shaders are untouched.** Poiyomi and friends generally work, but anything relying on
  VRChat-specific rendering (SPS/TPS penetration shaders in particular) will not.

A converted avatar is a starting point, not a finished upload: read the report, test in
ChilloutVR, and expect to tune physics feel by hand.

## Credits

- Gesture value tables, CVR core parameter list and several conversion patterns were
  studied from [vrc3cvr](https://github.com/imagitama/vrc3cvr) (MIT) and the
  [Narazaka fork](https://github.com/Narazaka/vrc3cvr) — thank you to those authors.
- Gesture value mapping and the CVR Parameter Stream approach follow the official
  ChilloutVR *Avatar Animator Parameters* and *Parameter Stream* references, cross-checked
  against [vrc3cvr](https://github.com/imagitama/vrc3cvr).
- Gravity split formula for the DynamicBone fallback mirrors the relation documented by
  [PhysBone-to-DynamicBone](https://github.com/FACS01-01/PhysBone-to-DynamicBone).
- MagicaCloth2 API usage follows the official
  [runtime construction docs](https://magicasoft.jp/en/mc2_runtime_build/).
- VRCFury avatars are baked by [VRCFury](https://vrcfury.com/)'s own builder; AvatarBridge
  bundles no Fury code and has no hard dependency on it.

## License

MIT — see [LICENSE.md](LICENSE.md).
