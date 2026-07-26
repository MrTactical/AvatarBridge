# AvatarBridge — VRChat → ChilloutVR avatar converter

A Unity Editor tool that converts a **VRChat SDK3 avatar** into a **ChilloutVR CCK 4 avatar** —
animator, menus, physics, contacts and face tracking — and hands you a clean starting point to
finish by hand.

- **VRCFury & Modular Avatar avatars work.** It runs Fury's own builder (or NDMF's manual bake)
  first, then converts the baked result, so toggles, linked clothing and merged armatures survive.
- **PhysBones become real physics** — **MagicaCloth2** or **DynamicBone**, no external tool.
- **Readable output** — clothing toggles come out as one `Toggle <name>` layer each, driven by
  real `bool` parameters.
- **Bloat removed** — GoGo Loco and SPS/OGB/PCS are stripped (one avatar went from 3088 to 240 of
  3200 sync bits).
- **Face tracking, your way** — native `CVRFaceTracking`, or the bundled CVR-VRCFT rig with eye
  tracking wired up. ARKit and Unified Expressions meshes both work.
- **Diagnostics that know ChilloutVR** — the report names components CVR will silently delete on
  load, and tracks its 3200-bit sync budget, rather than leaving you to find out in game.
- **Avatar scaler** — a `Height (M)` menu control defaulting to the avatar's measured eye height,
  so it's the same size before and after and the number reads in real metres.

*(No VRChat SDK installed? The tool still runs in [Setup mode](#setup-mode) and prepares any
humanoid for ChilloutVR.)*

## It's a head start, not a magic button

AvatarBridge does the tedious ~90%. It does **not** make avatar setup brainless, and can't —
VRChat and CVR are different platforms and VRCFury setups vary endlessly. **It assumes you know
your way around Unity**: the Animator window, blend trees, the `CVRAvatar` component.

Every run writes a `ConversionReport.md` you're **expected to read** — act on each *Warning*,
*Approximated* and *Skipped* entry — and every conversion should be **tested in ChilloutVR**
before you call it done. The editor can't show you gestures, contacts or synced parameters
actually running.

## Requirements

| What | Version | Notes |
|---|---|---|
| Unity | **2022.3.22f1** | the version VRChat and CCK 4 both use |
| ChilloutVR CCK | **4.0.x** | always required — it's what the tool builds for |
| VRChat Avatars SDK | SDK3 | required to convert; without it you get [Setup mode](#setup-mode) |
| [VRCFury](https://vrcfury.com/download) / [Modular Avatar](https://modular-avatar.nadena.dev/) | current | only if your avatars use them |
| [MagicaCloth2](https://assetstore.unity.com/packages/tools/physics/magica-cloth-2-242307) | *optional* | recommended physics target |
| [DynamicBone](https://assetstore.unity.com/packages/tools/animation/dynamic-bone-16743) | *optional* | alternative target; the free [VRLabs stub](https://github.com/VRLabs/Dynamic-Bones-Stub) is enough to convert |

Neither physics package is required — choose **Convert PhysBones to → None** and everything else
still converts.

## Installation

> ⚠️ **Import order matters.** Let Unity finish compiling after each step. Importing out of order
> can corrupt VRCFury data or leave broken scripting defines.

1. **Unity 2022.3.22f1.**
2. **A copy of your avatar project** — duplicate it. Never convert in your real upload project.
3. **ChilloutVR CCK 4.**
4. **A physics package** (optional).
5. **VRCFury / Modular Avatar** — whichever your avatars use.
6. **Your avatars** — import any that aren't already there, *after* VRCFury.
7. **AvatarBridge, last.** Grab the `.unitypackage` from
   [Releases](https://github.com/MrTactical/AvatarBridge/releases). It must live under `Assets`,
   not `Packages` — that's how the optional MagicaCloth2 / DynamicBone integration resolves.

One extra recompile after importing AvatarBridge is normal — that's it registering its scripting
defines.

## Usage

**Tools → Avatar Bridge → VRChat to ChilloutVR Converter**, then:

1. **Pick the avatar** in your scene.
2. **Check the options** — physics target, face tracking mode, height scaler. Defaults are fine
   for most avatars.
3. **Convert.** Output lands in `Assets/AvatarBridge/Output/<avatar>/`. Read the report, then test
   in game.

## What gets converted

| VRChat | ChilloutVR | Notes |
|---|---|---|
| Avatar descriptor | `CVRAvatar` | viewpoint, voice, visemes, blink |
| Expression parameters + menus | Advanced Avatar Settings | named after the menu control's label |
| Clothing / prop toggles | one `Toggle <name>` layer each | pulled out of VRCFury's merged blend trees |
| Parameter types | real `bool` / `int` / `float` | see [below](#parameter-types) |
| Gestures | `GestureLeftIdx` / `RightIdx` ints | analog fist curl stays native |
| PhysBones + colliders | **MagicaCloth2** or DynamicBone | see [below](#physbones--magicacloth2) |
| Contacts | `CVRPointer` / `CVRAdvancedAvatarSettingsTrigger` | ⚠️ Constant receivers are approximate |
| VRC Constraints | Unity constraints | ⚠️ several same-type on one object get merged |
| FinalIK components | kept as-is | ⚠️ CVR deletes some — see [quads on ice](#quadruped--finalik-avatars--on-ice) |
| VRC tracking / locomotion control | `BodyControl` | hands a limb from IK over to animation |
| Jaw-flap lip sync | `visemeMode = JawBone` / `SingleBlendshape` | rig-driven, no wiring needed |
| VRC Head Chop | `FPRExclusion` | ⚠️ show/hide only |
| Avatar cameras / listeners | removed | a stray `Camera` crashes CVR's asset filter |
| PhysBone `_IsGrabbed` / `_Angle` | [GrabbyBones](https://github.com/kafeijao/Kafe_CVR_Mods/tree/master/GrabbyBones) mod | optional mod, not bundled |
| Face-tracking blendshapes | native `CVRFaceTracking` or bundled rig | see [below](#face-tracking) |
| Menu **Button** controls | ordinary toggles | ⚠️ CVR has no momentary control |

**GoGo Loco and SPS/OGB/TPS/PCS are stripped by default** (both toggleable). CVR has its own
locomotion, and the haptics stacks don't function there while eating most of the sync budget.

## PhysBones → MagicaCloth2

**The conversion transfers structure and nothing else**: which bone the chain hangs from, which
colliders it collides with, which transforms to leave out, whether it started enabled. Every
physics value is left at MagicaCloth2's own defaults.

That's deliberate. Earlier versions derived MagicaCloth2 settings from PhysBone settings — gravity
scaled into m/s², spring inverted into damping, immobile into inertia. Every one of those looked
reasonable and every one had to be walked back after a real avatar misbehaved.

The reason isn't that the numbers were wrong. **The two systems are different kinds of
simulation.** PhysBones — like DynamicBone, which they replaced — are per-bone *rotational
springs*. MagicaCloth2 is a *particle position* solver: it moves particles through space and reads
bone rotations back out of where they land. A value meaning "springiness" to one doesn't mean
anything in particular to the other.

So a stock MagicaCloth2 BoneCloth — a configuration tuned by the solver's own author — is where
every chain starts, and the PhysBone's own numbers go into the report so you can tune from there:

> `tail — BoneCloth on the MagicaCloth2 "Tail" preset, 3 collider(s). Source PhysBone was pull`
> `0.2, spring 0.4, gravity 0, immobile 0.75, radius 0.02 — none of those transfer.`

Stretch & squish, multi-child blending, `Is Animated` and angle limits are reported the same way,
each naming the field to change if that chain wants it.

**Using DynamicBone instead?** None of this applies — PhysBones and DynamicBone *are* the same
kind of simulation, so that path maps values across 1:1.

### Options

None of these add arithmetic; they swap one author-tuned baseline for another, or copy a value
verbatim.

| setting | default | what it does |
|---|---|---|
| **Match a preset to each chain** | on | Hair, tail, skirt, cape or accessory by bone name; otherwise a soft/middle/hard spring by how firmly the PhysBone held its rest pose |
| **Cap particle radius to bone spacing** | on | A safety rail: MagicaCloth2's radius is the particle *size*, and particles wider than the gap between bones shove each other apart |
| **Transfer angle limits** | off | Copies each limit angle across. ⚠️ Genuinely avatar-dependent — this shakes some chains and is the best result the tool gives on others. Worth trying if physics feels loose |
| **Auto-assign nearby colliders** | off | Also gives each cloth the avatar's own colliders it could swing into, so a tail that passed through the leg in VRChat collides with it here. Improves on the original rather than copying it, so check before uploading |

## Parameter types

**VRCFury bakes every menu parameter as a `float`**, whatever it really is. Harmless in VRChat;
not here, because ChilloutVR writes a menu value using the entry's *own* declared type — write a
Bool into a Float parameter and nothing happens. That's the most common cause of "the toggle does
nothing in game".

So each parameter is retyped from what the avatar's logic says it is: the **menu control** it
drives (Toggle → bool, Dropdown → int, Slider → float), or for parameters with no control, how the
**animator compares** it. Anything read as a *quantity* — blend tree, motion time, or written by
an animation clip — stays `float`, and is named in the report so you can see the tool declined
rather than missed it.

## Face tracking

Pick one in the **Face tracking** dropdown. Both options first strip whatever FT rig the avatar
shipped with, so nothing fights over the same blendshapes.

- **Native CVR Component** — sets up `CVRFaceTracking` and maps the shapes. Self-contained, but
  the built-in solver is a bit stiff.
- **Unity Animator Blendtrees (DSR)** — injects DragonSkyRunner's *CVR Eye & Face Tracking* rig
  (bundled, no separate import), repaths every clip onto your actual eye bones and face mesh, and
  reconciles its shape vocabulary against whatever your mesh has — matching by name, casing,
  **ARKit ↔ Unified Expressions** aliases, and combined/split rules. So an **ARKit avatar** works
  without renaming anything. Smoother and more expressive.
- **None.**

**Either mode needs a tracking source at runtime** — true of any CVR face-tracking avatar. Run
[VRCFaceTracking](https://store.steampowered.com/app/3329480), and set CVR's *Eye Tracking* and
*Mouth Tracking* modules to **OSC**.

## Setup mode

Without the VRChat SDK there's no VRChat data to read, so conversion isn't possible — a VRChat
avatar's components won't even deserialize. Instead the tool prepares **any humanoid** for
ChilloutVR: `CVRAvatar` with viewpoint and voice position, viseme and blink detection, face
tracking, and the height scaler. Useful for a Booth model or an original avatar.

<details>
<summary>Why there's no VRChat SDK stub</summary>

A GUID-matching stub (like the DynamicBone one) could recover simple components, but it could
never run VRCFury or NDMF — those are real code needing the real SDK — so Fury avatars would
silently convert to empty shells. Unity also deserializes by field name and *silently defaults*
what it can't match, so every SDK update would quietly change the output. The DynamicBone stub
exists to work around a paywall; the VRChat SDK is free and one click.
</details>

## Known limitations

### Quadruped / FinalIK avatars — on ice

**Don't expect a working quad right now.** The conversion completes and the report comes back
clean, but the avatar holds its rest pose in game with only the IK-tracked parts following you.
Several real bugs were found and fixed chasing this and none of them were the cause, so it is
parked rather than solved.

What is known, from reading ChilloutVR's own code:

- **`GrounderVRIK` is deleted on load.** ChilloutVR whitelists components per-avatar and destroys
  the rest silently. Worlds are allowed 57 FinalIK types, avatars 13. `VRIK`, `LookAtIK`,
  `TwistRelaxer`, `GrounderIK`, `GrounderBipedIK`, `CCDIK`, `FABRIK`, `AimIK` and `LimbIK` survive;
  `GrounderVRIK`, `GrounderQuadruped`, `GrounderFBBIK`, `ArmIK`, `LegIK` and `FingerRig` do not.
  The report now names these.
- **`GrounderIK` is not a substitute for `GrounderVRIK`.** The first drives separate per-leg IK
  components; the second feeds position offsets into VRIK's own solver from inside its update
  callbacks. Swapping them gives no grounding at all, and ChilloutVR has no native foot placement
  to fall back on.
- **ChilloutVR always installs its own `VRIK`.** It destroys whatever is on the avatar's animator
  object, adds its own, and auto-detects references from the *humanoid* rig. On a quadruped rigged
  as humanoid that means a biped solve running over a quad rig, and nothing a converter does can
  prevent it. This is the current best guess at the root cause; it is not proven.

Tracking control does convert correctly — `VRCAnimatorTrackingControl` becomes ChilloutVR's
`BodyControl`, which is what hands a limb from IK over to animation — so the groundwork is there
if this gets picked up again. Bipeds are unaffected by any of it.

**Not converted:**

- **Eye look / gaze** — only blink transfers; set gaze up under *Eye Look Settings* on the
  `CVRAvatar`. (Blendshape face tracking *is* handled.)
- **PhysBone posing, stretch & squish** and their `_Stretch` / `_Squish` / `_IsPosed` parameters.
- **VRC state behaviours** other than Parameter Driver — removed and counted.
- **Synced animator layers** and **ONSP audio**.
- **Content tags** — set CVR's *Advanced Tagging* (NSFW, loud audio…) yourself before uploading.

**Converted with caveats:**

- **Action-layer emotes** rely on VRChat's emote flow, so converted states may be unreachable. CVR
  has its own emotes.
- **Constant contact receivers** reset to 0 when *any* pointer exits — CVR triggers don't count
  occupants.
- **Stacked PhysBones** (several chains on one bone that VRChat toggles between) all convert, but
  only one is left driving the chain — two solvers on the same bones jitter rather than blend.
  Nothing is deleted, so switching variant is one checkbox; the report names the one kept.
- **Stacked same-type constraints** merge into one — Unity and CVR allow only one per type per
  object, so the second's offsets are dropped (its sources are kept).
- **Dropdowns sometimes keep `(unused)` entries.** CVR selects dropdown options by *position*, so
  gaps need padding. These are normally removed by renumbering the parameter, but that's unsafe
  when the value is used as a quantity or passed to a driver — the report says which applied.
- **Parameter-packing optimisers** (`MemOpt_*` and similar) leave odd-looking entries in your
  menu. ⚠️ **Don't delete them.** They're what carries your toggles to other players. (An earlier
  version of this note said CVR can't sync a parameter without a menu entry — that's wrong.
  Syncing comes from the animator declaration; the menu entry decides whether the value is
  remembered in your avatar profile between loads. Still worth keeping.)
- **Shaders aren't translated.** Poiyomi etc. work as-is, and VRCFury-baked materials are rescued
  out of Fury's temp folder so they don't render pink — but VRChat-specific rendering (SPS/TPS
  especially) won't *function* in CVR.

## Reporting a bug

Hit **Report an issue** in the AvatarBridge window — it opens a pre-filled GitHub issue with your
versions and detected packages already in it.

Two things make a report solvable immediately:

1. **Attach `ConversionReport.md`** from `Assets/AvatarBridge/Output/<avatar>/`. Nearly every bug
   fixed so far was diagnosed from this file.
2. **Attach the right log:**

   | Symptom | Log |
   |---|---|
   | Conversion errors, or wrong result in Unity | Unity console text or `Editor.log` |
   | Avatar misbehaves or won't load **in ChilloutVR** | `%USERPROFILE%\AppData\LocalLow\ChilloutVR\ChilloutVR\Player.log` |

   A clean Unity log says nothing about an in-game failure. That's exactly how the "Error robot"
   bug was found — the Unity log was spotless.

Please re-run on the [latest release](https://github.com/MrTactical/AvatarBridge/releases/latest)
first. Logs contain your project's file paths (and CVR logs your display name) — skim and redact
if you'd rather not post them.

**Quick questions** can go to **`mrtactical`** on Discord. Anything reproducible is better as a
GitHub issue — those get tracked, linked to a fix and closed with a release.

## Credits

- Gesture tables, CVR core parameters and several conversion patterns were studied from
  [vrc3cvr](https://github.com/imagitama/vrc3cvr) (MIT) and the
  [Narazaka fork](https://github.com/Narazaka/vrc3cvr).
- Gesture mapping and the Parameter Stream approach follow the official ChilloutVR references.
- The DynamicBone gravity split mirrors
  [PhysBone-to-DynamicBone](https://github.com/FACS01-01/PhysBone-to-DynamicBone).
- MagicaCloth2 usage follows the official
  [runtime construction docs](https://magicasoft.jp/en/mc2_runtime_build/); chain presets are
  MagicaCloth2's own.
- VRCFury avatars are baked by [VRCFury](https://vrcfury.com/)'s own builder — no Fury code is
  bundled and there's no hard dependency.
- The **CVR VRCFT** face-tracking rig is **DragonSkyRunner's**
  [CVR Eye & Face Tracking](https://github.com/DragonSkyRunner/ChilloutVR-Facetracking-Animator-Package),
  bundled under `Assets/AvatarBridge/FaceTracking` and redistributed with the author's permission.
  All rights remain theirs; if you reuse it, credit them. *(Their upstream repo carries no explicit
  license file — a `LICENSE` there would make the redistribution terms unambiguous.)*
- [GrabbyBones](https://github.com/kafeijao/Kafe_CVR_Mods/tree/master/GrabbyBones) is an optional
  third-party mod AvatarBridge targets but does not bundle.
- The avatar scaler's constant-speed smoothing is built on
  [JustSleightly's Controller Templates](https://notes.sleightly.dev/controller-templates); those
  clips are bundled under fresh GUIDs to avoid clashing with the original package. Credit for the
  technique remains theirs.

## License

MIT — see [LICENSE.md](LICENSE.md).
