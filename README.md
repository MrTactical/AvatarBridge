# AvatarBridge — VRChat → ChilloutVR avatar converter

AvatarBridge is a Unity Editor tool that converts a **VRChat SDK3 avatar** into a
**ChilloutVR CCK avatar** in one click, keeping as much functionality as possible —
including a built-in **PhysBones → MagicaCloth2** converter so avatar physics survive the
trip without any external tools.

Every conversion produces a `ConversionReport.md` that honestly lists what was converted
1:1, what was approximated, and what has no ChilloutVR equivalent.

## Requirements

| What | Version |
|---|---|
| Unity | **2022.3.22f1** (same version VRChat and CCK 4 use) |
| VRChat Avatars SDK | SDK3, 3.10.x tested |
| ChilloutVR CCK | **4.0.x** (tested against 4.0.1; CCK 3 paths are also handled) |
| MagicaCloth2 *(optional)* | for PhysBone physics conversion (recommended — most CVR users have it) |
| DynamicBone *(optional)* | fallback physics target; the free [VRLabs Dynamic-Bones-Stub](https://github.com/VRLabs/Dynamic-Bones-Stub) also works |

## Installation

> ⚠️ **Import order matters.** Importing these packages out of order can leave the project
> with broken scripting defines or compile errors that are annoying to untangle. Follow the
> steps top to bottom.

1. **Unity 2022.3.22f1** — the exact version both the current VRChat SDK and CCK 4 target.
2. **Duplicate your VRChat avatar project** (made with the Creator Companion, so the
   Avatars SDK is already inside). The conversion project will contain both SDKs —
   never do this in your real upload project.
3. Open the copy and let it compile cleanly **before** importing anything else.
4. Import the ChilloutVR **CCK 4** unitypackage. Let it finish compiling.
5. Import **MagicaCloth2** (recommended) and/or **DynamicBone** — whichever physics
   target you plan to use. Let it finish compiling.
5b. **If your avatar uses VRCFury: install VRCFury *before* importing the avatar
   package.** Avatars imported while Fury is missing end up with "Missing Script"
   components whose data gets corrupted ("Found a null SerializeReference" when Fury
   later tries to build). If that happened, delete the avatar's assets and re-import
   its package with Fury already installed.
6. Import **AvatarBridge last**: grab the `.unitypackage` from the
   [Releases page](https://github.com/MrTactical/AvatarBridge/releases), or copy this
   repository into the project as `Assets/AvatarBridge`. It must live under `Assets`,
   not `Packages` — that's how the optional MagicaCloth2 / DynamicBone integration
   resolves automatically.

**One extra recompile right after importing AvatarBridge is normal** — that's the tool
registering its `AVATARBRIDGE_MAGICA` / `AVATARBRIDGE_DYNBONE` scripting defines after
detecting what's installed.

Got the order wrong anyway? Usually recoverable: import whatever is missing, let Unity
recompile, and reopen the window — it shows a ✔/✘ checklist of what it can and can't see.
If the project is truly wedged, delete the `Library` folder and let Unity reimport.

## Usage

1. Open **Tools → Avatar Bridge → VRChat to ChilloutVR Converter**.
2. Drop your scene avatar (the object with the `VRCAvatarDescriptor`) into the field.
3. Pick the physics target (MagicaCloth2 recommended) and check the options.
4. **Convert avatar.** The original is deactivated; a `<name> (ChilloutVR)` clone appears.
5. Read `Assets/AvatarBridge/Output/<name>/ConversionReport.md`, fix anything flagged,
   then upload through the CCK as usual.

## What gets converted

| VRChat | ChilloutVR | Notes |
|---|---|---|
| Avatar descriptor (viewpoint, voice, face mesh, visemes, blink) | `CVRAvatar` | voice position placed at the head bone like VRChat |
| Expression parameters + menus | Advanced Avatar Settings (toggles / sliders / dropdowns) | menu structure preserved in entry names; Buttons become `<impulse=0.1>` auto-reset parameters |
| FX / Gesture layers (Base, Additive, Action optional) | merged into one CVR animator on top of the CCK `AvatarAnimator` | CVR hand layers are removed when the Gesture layer is converted |
| `GestureLeft/Right` int values | CVR float gesture values | full remap incl. analog fist; `GestureLeftWeight` folds into `GestureLeft` |
| VRC Parameter Driver | CCK `AnimatorDriver` | Set / Add / Random / Copy incl. range conversion |
| Non-synced parameters | `#`-prefixed (CVR local-only convention) | keeps network traffic equivalent |
| PhysBones (+ colliders) | **MagicaCloth2 BoneCloth** or DynamicBone | see mapping below |
| Contact senders | `CVRPointer` (one per collision tag) | with matching trigger collider shape |
| Contact receivers | `CVRAdvancedAvatarSettingsTrigger` | Constant → enter/exit, OnEnter → pulse, Proximity → distance-driven stay task |
| Built-in VRC colliders (hands, fingers, head…) | `CVRPointer`s with standard tags | only for tags your receivers actually listen to |
| VRC Constraints (all 6 types) | Unity constraints | CVR runs Unity constraints natively |
| VRC Head Chop | `FPRExclusion` | show/hide only (scale factors between 0 and 1 can't be represented) |
| VRC Spatial Audio Source | `AudioSource` spatial settings | approximation |
| `Viseme`, `Voice`, `Seated`, `IsOnFriendsList`… | `VisemeIdx`, `VisemeLoudness`, `Sitting`, `IsFriend`… | CVR core parameter renames |

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

- **PhysBone interaction features** — grabbing, posing, stretch/squish and the
  `_IsGrabbed`/`_Angle`/`_Stretch` parameters have no CVR equivalent (reported per chain).
- **VRC state behaviours** other than Parameter Driver (Animator Layer Control, Tracking
  Control, Locomotion Control, Playable Layer Control…) are removed and reported.
- **Action layer emotes** rely on VRC's emote flow; converted states may be unreachable.
- **Constant contact receivers** reset to 0 when *any* pointer exits, even if a second one
  is still inside (single-trigger approximation).
- 2D blend trees driven directly by `GestureLeft/Right` are flagged for manual review
  (CVR's gesture value ordering differs).
- Synced animator layers, ONSP audio and jaw-flap lip sync are not converted.
- A converted avatar is a starting point — always test in ChilloutVR and check the report.

## Credits

- Gesture value tables, CVR core parameter list and several conversion patterns were
  studied from [vrc3cvr](https://github.com/imagitama/vrc3cvr) (MIT) and the
  [Narazaka fork](https://github.com/Narazaka/vrc3cvr) — thank you to those authors.
- Gravity split formula for the DynamicBone fallback mirrors the relation documented by
  [PhysBone-to-DynamicBone](https://github.com/FACS01-01/PhysBone-to-DynamicBone).
- MagicaCloth2 API usage follows the official
  [runtime construction docs](https://magicasoft.jp/en/mc2_runtime_build/).

## License

MIT — see [LICENSE.md](LICENSE.md).
