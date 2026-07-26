# AvatarBridge — VRChat → ChilloutVR avatar converter

A Unity Editor tool that converts a **VRChat SDK3 avatar** into a **ChilloutVR CCK 4 avatar** —
animator, menus, physics, contacts and face tracking — and hands you a clean starting point to
finish by hand.

- **VRCFury & Modular Avatar avatars work.** It runs Fury's own builder (or NDMF's manual bake)
  first, then converts the baked result, so toggles, linked clothing and merged armatures survive —
  and Fury's VRChat-only sync workarounds are removed rather than carried across.
- **Prefabs that drive your bones keep working** — constraints that target a different transform,
  the way [Avatar Limb Scaling](https://github.com/xNanochip/VRC-Avatar-Limb-Scaling) and many
  others are built, are rehosted onto the bone they actually drive.
- **PhysBones become real physics** — **MagicaCloth2** or **DynamicBone**, no external tool.
- **Readable output** — clothing toggles come out as one `Toggle <name>` layer each, driven by
  real `bool` parameters.
- **Bloat removed** — GoGo Loco and SPS/OGB/PCS are stripped (one avatar went from 3088 to 240 of
  3200 sync bits).
- **Face tracking, your way** — native `CVRFaceTracking`, or the bundled CVR-VRCFT rig with eye
  tracking wired up. ARKit and Unified Expressions meshes both work.
- **ChilloutVR's native contacts** — VRChat contacts convert one to one, with real proximity and
  no sync cost, using a system the CCK doesn't expose. Confirmed working in game.
  See [below](#native-contacts).
- **Shaders that lose an eye get fixed** — CVR renders single-pass instanced where VRChat renders
  double-wide, so shaders that never opted in draw into one eye only. AvatarBridge reports them
  and can patch a working copy. See [below](#shaders-that-only-draw-into-one-eye).
- **Diagnostics that know ChilloutVR** — the report names components CVR will silently delete on
  load, tracks its 3200-bit sync budget, and flags shaders its uploader will reject, rather than
  leaving you to find out in game.
- **Avatar scaler** — a `Height (M)` menu control defaulting to the avatar's measured eye height,
  so it's the same size before and after and the number reads in real metres.
- **VRChat tracking control converts** — `VRCAnimatorTrackingControl` becomes CVR's `BodyControl`,
  so animations that take a limb away from IK still do.

*(No VRChat SDK installed? The tool still runs in [Setup mode](#setup-mode) and prepares any
humanoid for ChilloutVR.)*

<p align="center">
  <img src="docs/images/window.png" alt="The AvatarBridge window: a blue-to-orange banner, three numbered steps, and collapsible option cards" width="480">
</p>

<p align="center"><em>The banner runs VRChat's blue into ChilloutVR's orange, and the step markers
sit along it — because that's the trip your avatar is making.</em></p>

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
| Contacts | ChilloutVR's native contacts, or `CVRPointer` / trigger | see [below](#native-contacts) |
| VRC Constraints | Unity constraints | including *Target Transform* — see [below](#constraints-that-drive-another-object) |
| VRCFury parameter compressor | removed | a VRChat sync workaround that breaks sync here — see [below](#vrcfury-systems) |
| FinalIK components | kept as-is | ⚠️ CVR deletes some — see [quads on ice](#quadruped--finalik-avatars--on-ice) |
| VRC tracking / locomotion control | `BodyControl` | hands a limb from IK over to animation |
| Jaw-flap lip sync | `visemeMode = JawBone` / `SingleBlendshape` | rig-driven, no wiring needed |
| VRC Head Chop | `FPRExclusion` | ⚠️ show/hide only |
| Avatar cameras / listeners | removed | a stray `Camera` crashes CVR's asset filter |
| PhysBone `_IsGrabbed` / `_Angle` | [GrabbyBones](https://github.com/kafeijao/Kafe_CVR_Mods/tree/master/GrabbyBones) mod | optional mod, not bundled |
| Face-tracking blendshapes | native `CVRFaceTracking` or bundled rig | see [below](#face-tracking) |
| Menu **Button** controls | ordinary toggles | ⚠️ CVR has no momentary control |
| Shaders without stereo support | patched copy in `RehomedAssets` | optional, off by default — see [below](#shaders-that-only-draw-into-one-eye) |
| VRCFury temp materials/shaders | rescued into `RehomedAssets` | Fury deletes its temp folder on its next build |

**GoGo Loco and SPS/OGB/TPS/PCS are stripped by default** (both toggleable). CVR has its own
locomotion, and the haptics stacks don't function there while eating most of the sync budget.

### VRCFury systems

VRCFury avatars are baked with Fury's own builder before conversion, so anything it installs
arrives as ordinary layers and parameters. Two of its systems get special handling because they
exist to solve VRChat problems ChilloutVR doesn't have:

- **Parameter Compressor — removed.** It beats VRChat's 256-parameter ceiling by marking your real
  parameters as *not synced*, mirroring each into `VF<id>_<name>`, and rotating the mirrors through
  a couple of sync slots twice a second. ChilloutVR has 3200 bits and syncs straight from the
  animator, so carried across it costs a blend tree running every frame — and because the originals
  were left marked not-synced, **the values reach nobody**. An avatar whose author installed it to
  make more things sync ends up with those things not syncing at all. Removing it puts every
  affected parameter back to syncing natively and instantly.
- **Armature Link, Full Controller, toggles, menus** — nothing special needed; they're baked before
  AvatarBridge sees them.

### Third-party prefabs

Anything VRCFury or Modular Avatar installs is baked first, so most prefabs convert without needing
to be known about. These have been tested end to end and work in game:

| Prefab | Notes |
|---|---|
| [Avatar Limb Scaling](https://github.com/xNanochip/VRC-Avatar-Limb-Scaling) | Arms/Legs sliders scale the real bones. Needs the *Target Transform* handling below |
| [GoGo Loco](https://franadavrc.gumroad.com/l/gogoloco) | stripped by default — CVR has its own locomotion |
| VRCFaceTracking / ARKit rigs | replaced by the chosen face-tracking mode, or kept under **None** |

If a prefab's feature comes through inert — the menu control appears, moves, and does nothing —
that's worth reporting. Every case of it so far has been a fixable gap in AvatarBridge rather than
anything wrong with the prefab.

## Constraints that drive another object

A VRC constraint can sit on one object and drive a **different** one, through its `Target
Transform` field. Unity's constraints have no equivalent — they always affect the transform they're
attached to.

AvatarBridge honours it by putting the Unity constraint **on the target** instead, carrying the
same sources. That's exact rather than approximate, and it matters more than it sounds: prefabs
routinely put their constraints on proxy objects inside their own hierarchy and point them at your
real bones. Avatar Limb Scaling is built entirely that way. Dropping the redirection doesn't
weaken such a prefab, it silently stops it working while everything still *looks* wired up.

The one case that can't be honoured is a Target Transform pointing outside the avatar — not
AvatarBridge's to modify, and it wouldn't survive an upload. The report says so plainly when it
happens.

⚠️ **Several constraints of the same type on one object still merge into one.** Unity and CVR allow
only one per type per object, so the second's offsets are dropped (its sources are kept).

## PhysBones → MagicaCloth2

**Structure transfers exactly; feel does not.** Which bone the chain hangs from, which colliders
it collides with, which transforms to leave out, whether it started enabled — all verbatim. The
physics values start from a MagicaCloth2 preset instead of being derived from the PhysBone's.

That's deliberate. Earlier versions derived MagicaCloth2 settings from PhysBone settings — gravity
scaled into m/s², spring inverted into damping, immobile into inertia. Every one of those looked
reasonable and every one had to be walked back after a real avatar misbehaved.

The reason isn't that the numbers were wrong. **The two systems are different kinds of
simulation.** PhysBones — like DynamicBone, which they replaced — are per-bone *rotational
springs*. MagicaCloth2 is a *particle position* solver: it moves particles through space and reads
bone rotations back out of where they land. A value meaning "springiness" to one doesn't mean
anything in particular to the other.

So a stock MagicaCloth2 BoneCloth — a configuration tuned by the solver's own author — is where
every chain starts. Three PhysBone facts *do* carry over, because they ask the same question on
both sides rather than needing conversion: a chain with **no gravity** keeps none (presets ship
their own, and Long Hair's 5.0 would make it fall for the first time in ChilloutVR), **negative
gravity** points up, and **immobile** becomes world influence, which is the same 0–1 question
measured the other way round. Pull, spring and stiffness are left alone — they have no
counterpart. Each adjustment is named in the report, as are the PhysBone's own numbers, so you can
tune from there:

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
| **Fit the preset to the PhysBone** | on | The three facts above — no gravity, upward gravity, immobile → world influence. Turn it off to get the preset exactly as its author wrote it |
| **Cap particle radius to bone spacing** | on | A safety rail: MagicaCloth2's radius is the particle *size*, and particles wider than the gap between bones shove each other apart |
| **Transfer angle limits** | off | Copies each limit angle across. ⚠️ Genuinely avatar-dependent — this shakes some chains and is the best result the tool gives on others. Worth trying if physics feels loose |
| **Auto-assign nearby colliders** | off | Also gives each cloth the avatar's own colliders it could swing into, so a tail that passed through the leg in VRChat collides with it here. Improves on the original rather than copying it, so check before uploading |

## Native contacts

ChilloutVR's own contact system is a near-exact superset of VRChat's — same shapes plus Box, the
same `allowSelf` / `allowOthers` / `localOnly` / collision tags under the same names, and receiver
types covering Constant, OnEnter and three kinds of Proximity. It lives inside the game client and
the CCK ships no way to author it, so converters have always had to approximate it with pointers
and triggers.

**AvatarBridge can author it directly.** Turn on *Use ChilloutVR's native contacts* under
**Advanced**, and VRChat contacts convert one to one:

- **Real proximity**, rather than a distance-driven stand-in
- **Collision tags kept verbatim**, so contacts still meet other avatars' on the same names
- **`localOnly` honoured** — the legacy path has nowhere to put it
- **No sync cost at all.** Contacts sit on ChilloutVR's avatar whitelist rather than its local-only
  one, so every client simulates them for every avatar and reproduces the value rather than
  replicating it

It works by declaring the components locally: an asset bundle carries no script assemblies, only a
record of each script's assembly, namespace and class, which the player resolves against its own.
The declarations are generated into `AvatarBridge/Runtime` on import and removed automatically if a
future CCK provides the real thing.

> ✅ **This works.** Confirmed in a live ChilloutVR instance: CCK validation clean, avatar
> uploaded, contacts triggered by other players, and ChilloutVR's own runtime gizmos drawing the
> components — which is the proof that counts, because it means the game's real implementation is
> running against declarations generated here.
>
> **It is still off by default**, and stays that way until more than one avatar has confirmed it.
> Turn it on deliberately, test in game, and keep the legacy path in mind as the fallback — the
> conversion switches to it by itself if anything is wrong.

> ⚠️ **If a conversion ever leaves broken `Contact_*` components behind, delete them and reopen the
> scene before converting again.** Unity manufactures a placeholder script for a component whose
> reference is dangling, and that placeholder then captures every *new* component of the same
> class — so one bad conversion quietly poisons the next. AvatarBridge detects this and refuses
> rather than producing another broken avatar, and *Tools → Avatar Bridge → Diagnose native
> contacts* will tell you exactly what Unity is holding.

## Shaders that only draw into one eye

The two platforms don't render VR the same way, and this is one of the few places that difference
reaches your avatar. **ChilloutVR renders single-pass instanced; VRChat renders double-wide
single-pass.** Both SDKs force their own mode, unconditionally.

Under VRChat's double-wide mode a shader gets both eyes without having to ask for them. Under
instancing it has to declare that it knows which eye it's drawing — so a shader that never opted
in looked perfectly fine in VRChat and draws into one eye only in ChilloutVR. Nobody did anything
wrong; it's a conversion problem, which makes it worth fixing here.

The CCK flags these as *potentially non-SPI*; AvatarBridge reports them too, and can fix the ones
that are fixable mechanically.

Turn on **Patch non-SPI shaders for VR** in *Advanced*. For each affected shader it writes a
patched copy into `RehomedAssets` next to your converted avatar, adds the stereo macros, and
points this avatar's materials at the copy.

- **Your original shader is never modified**, and neither is the original material — both are
  copied. Other avatars sharing them are unaffected. (Those shaders usually aren't yours.)
- **A copy that doesn't compile is thrown away** and the original left in place, so the worst
  case is a line in the report rather than wrong pixels.
- **Not everything can be patched.** Surface shaders have no vertex stage to edit, locked or
  generated shaders can't be parsed, and structs living in a shared include can't be edited from
  one file. Those are listed in the report instead, for hand-fixing or replacing.
- **There's nothing to undo.** The macros are the mode-agnostic ones — they expand to real
  instancing code under ChilloutVR's mode and to nothing under VRChat's or on desktop. So the
  patched copy is still a correct shader everywhere; it just also works here.

> ✅ **This works.** Confirmed in a live ChilloutVR instance: a soft-particle effect that the CCK
> flagged as non-SPI was patched, validated clean, uploaded, and renders correctly **in both eyes**
> in game.
>
> **It is still off by default**, on one avatar's evidence. Compilation is the only thing that can
> be checked automatically — whether the result *looks* right is a judgement no editor script can
> make, so turn it on deliberately and **check the effect in both eyes**.

> Passing the CCK's check isn't the same as being correct. It looks for four macros; a shader can
> have all four and still be broken — a soft-particle shader reading `_CameraDepthTexture` through
> `sampler2D`/`tex2Dproj` is the common case, since that texture is an array under single-pass
> instanced. AvatarBridge rewrites that pair as well.

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

Pick one in the **Face tracking** dropdown. Both set-up modes remove whatever FT rig the avatar
shipped with — its animator layers, its parameters *and* its objects — so nothing is left fighting
over the same blendshapes. On a typical VRCFT avatar that's a couple of layers and a few hundred
parameters. **None** leaves the original rig completely alone.

- **Native CVR Component** — sets up `CVRFaceTracking` and maps the shapes. Self-contained, but
  the built-in solver is a bit stiff.
- **Unity Animator Blendtrees (DSR)** — injects DragonSkyRunner's *CVR Eye & Face Tracking* rig
  (bundled, no separate import), repaths every clip onto your actual eye bones and face mesh, and
  reconciles its shape vocabulary against whatever your mesh has — matching by name, casing,
  **ARKit ↔ Unified Expressions** aliases, and combined/split rules. So an **ARKit avatar** works
  without renaming anything. Smoother and more expressive.
- **None** — the avatar's own rig is left exactly as it is, on the assumption you'll handle it.

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
- **VRChat-only rendering** — SPS/TPS deformation and anything else that needs VRChat's own shader
  systems. The meshes and materials survive; the effect doesn't.

**Converted with caveats:**

- **Action-layer emotes.** Only **Gesture** and **FX** convert by default — Base, Additive and
  Action are off, because CVR drives locomotion and emotes itself and merging VRChat's versions
  fights it. You can tick Action on, but its states rely on VRChat's emote flow and may simply be
  unreachable.
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
  out of Fury's temp folder so they don't render pink. Shaders that don't support VR stereo are a
  separate problem, below.
- **Merged layers can fight CVR's locomotion** on rare avatars, because VRChat keeps FX on its own
  playable layer and CVR has no equivalent. If an FX layer is overriding your pose, *Mask merged
  layers off the humanoid rig* under **Advanced** restores that separation. Off by default — the
  one case that prompted it turned out to be a different bug.

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
