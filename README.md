# AvatarBridge — convert your VRChat avatar to ChilloutVR

[![Latest release](https://img.shields.io/github/v/release/MrTactical/AvatarBridge?label=release&color=1778FF)](https://github.com/MrTactical/AvatarBridge/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-EE4408.svg)](LICENSE.md)

A Unity Editor tool that converts a **VRChat SDK3 avatar** into a **ChilloutVR CCK 4 avatar** —
animator, menus, physics, contacts and face tracking — and hands you a clean starting point to
finish by hand.

> ## ✅ It actually works
>
> **75+ avatars converted, uploaded and worn in ChilloutVR** — by the author and by independent
> testers, on other people's models, not just tidy test cases. Heavy ones too: 400k-triangle
> avatars with 56 material slots, full VRCFury rigs, face tracking, the lot.
>
> **And tested *in game*, which is the only test that counts.** An avatar that converts cleanly and
> validates green can still be wrong — a shader drawing into one eye, a chain moving unlike its
> original, a parameter reaching nobody. None of that shows in the editor. So each of these was
> confirmed in a live instance, by wearing it:
>
> - contacts triggered by other players, with real proximity and zero sync cost
> - stereo shaders rendering correctly **in both eyes**
> - prefab constraints driving real bones — Avatar Limb Scaling working end to end
> - PhysBone chains moving like their originals, derived from both solvers' source
> - the "bicycle pose" fixed, confirmed on a tester's avatar
>
> Bugs still turn up — every one above was found this way, by somebody putting an avatar on and
> looking. That's why the report exists and why [reporting a bug](#reporting-a-bug) gets a fix.

<p align="center">
  <img src="docs/images/window-262.png" alt="The AvatarBridge window: a blue-to-orange banner, three numbered steps, and collapsible option cards" width="480">
</p>

<p align="center"><em>The banner runs VRChat's blue into ChilloutVR's orange, and the step markers
sit along it — because that's the trip your avatar is making.</em></p>

**Contents:**
[Comparison](#already-using-vrc3cvr) ·
[Highlights](#highlights) ·
[Requirements](#requirements) ·
[Installation](#installation) ·
[Usage](#usage) ·
[What gets converted](#what-gets-converted) ·
[Constraints](#constraints-that-drive-another-object) ·
[Physics](#physbones--magicacloth2) ·
[Contacts](#native-contacts) ·
[Shaders](#shaders-that-only-draw-into-one-eye) ·
[Parameter types](#parameter-types) ·
[Face tracking](#face-tracking) ·
[Store description](#store-description) ·
[Setup mode](#setup-mode) ·
[Known limitations](#known-limitations) ·
[Troubleshooting](#troubleshooting) ·
[Reporting a bug](#reporting-a-bug) ·
[Credits](#credits)

## Already using vrc3cvr?

vrc3cvr is the reason this one exists — AvatarBridge started by studying it (see
[Credits](#credits)). **Use [Narazaka's vrc3cvr](https://github.com/Narazaka/vrc3cvr) (MIT) as the
point of comparison**: the [original](https://github.com/imagitama/vrc3cvr) was archived in May
2023, and Narazaka's is the maintained CCK4-era fork. If you want a smaller, focused converter,
it's a good tool and it is the one to compare against.

The two overlap less than the names suggest. What differs, from Narazaka's documentation as of
mid-2026:

| | AvatarBridge | vrc3cvr (Narazaka) |
|---|---|---|
| Menus, parameters, gestures | ✅ | ✅ |
| The settings picked *from your avatar* rather than from its documentation | ✅ [Analyse](#analyse-this-avatar) measures and offers each one | — |
| PhysBones → DynamicBone | ✅ built in | via the external [PhysBone-to-DynamicBone](https://github.com/FACS01-01/PhysBone-to-DynamicBone) |
| PhysBones → **MagicaCloth2**, feel derived from both solvers' decompiled source | ✅ | — |
| **Modular Avatar** | ✅ baked automatically | ✅ via its own component + manual bake |
| **VRCFury** (toggles, linked clothing, merged armatures survive) | ✅ baked automatically | manual |
| VRCFury's sync workarounds removed instead of carried across broken | ✅ | — |
| Contacts | **ChilloutVR's own contact components** — real proximity, tags verbatim, no sync bits spent ([experimental](#native-contacts)) | emulated with `CVRPointer` + trigger, which fire on collision rather than on proximity |
| Stereo shaders patched so effects stop drawing into one eye | ✅ | — |
| Gaze limits *measured off your avatar's own poses*; the viewpoint your avatar already shipped with | ✅ | — |
| Constraints that drive another transform (Avatar Limb Scaling et al.) | ✅ | — |
| Custom locomotion, fall, sit and flight animations carried into CVR's own locomotion | ✅ | — |
| A per-conversion report + diagnostics that know what ChilloutVR deletes on load | ✅ | — |
| A play-mode tester that drives the converted avatar the way the game does | ✅ | — |
| Store description generated and typed into the upload page | ✅ | — |

Every ✅ in the AvatarBridge column is documented on this page, and most are confirmed in game —
see the banner above for what that means here. The right-hand column is read from vrc3cvr's own
documentation, not from testing it; if anything there is wrong or has since changed,
[open an issue](https://github.com/MrTactical/AvatarBridge/issues) and it gets corrected.

## It's a head start, not a magic button

AvatarBridge does the tedious ~90%. It does **not** make avatar setup brainless, and can't — the
two platforms differ and VRCFury setups vary endlessly. **It assumes you know your way around
Unity**: the Animator window, blend trees, the `CVRAvatar` component.

Every run writes a `ConversionReport.md` you're **expected to read** — act on each *Warning*,
*Approximated* and *Skipped* entry — alongside a `Diagnostics.md` you don't need to read at all
unless something's wrong, and should attach to any bug report. Every conversion should be **tested
in ChilloutVR** before you call it done. The editor can't show you gestures, contacts, synced parameters or physics
actually running.

## Highlights

- **It reads your avatar and picks the settings for you.** Press
  [**Analyse this avatar**](#analyse-this-avatar) and it measures what's actually there
  — PhysBones, face-tracking blendshapes, shaders that lose an eye in VR, the layers the avatar
  built itself — then offers each setting those decide. What it *can't* measure is kept separately
  and never ticked for you, so the options you're asked to think about are only the ones that
  genuinely need thinking about.
- **VRCFury & Modular Avatar avatars work.** Fury's own builder (or NDMF's bake) runs first, so
  toggles, linked clothing and merged armatures survive — and Fury's VRChat-only sync workarounds
  are removed rather than carried across.
- **PhysBones become real physics** — **MagicaCloth2** or **DynamicBone**, no external tool, with
  the chain's feel converted from the PhysBone's own numbers.
- **Prefabs that drive your bones keep working**, including constraints that target a different
  transform — the way [Avatar Limb Scaling](https://github.com/xNanochip/VRC-Avatar-Limb-Scaling)
  and many others are built.
- **Readable output** — clothing toggles come out as one `Toggle <name>` layer each, on real
  `bool` parameters.
- **Toggles that go both ways** — VRChat's runtime quietly restores any property no animation is
  writing (Write Defaults); ChilloutVR's does not, which is why avatars that behave perfectly in
  VRChat used to come back one-way. Nothing is
  [left to that rule](#a-toggle-switches-on-but-never-back-off) any more: every direction is real
  animation, reusing your avatar's own clips where they exist, and the conversion **audits
  itself** — anything still able to fall back to the runtime is named in the report.
- **Your avatar's own locomotion crosses over** — custom walking, crouching, crawling, falling and
  sitting animations are grafted into ChilloutVR's locomotion layer, matched by blend-tree
  position; emotes move into the one layer that can both pose the body and hand it back; a flight
  pose rides CVR's **native** flight. The game moves you, with your avatar's art.
- **Bloat removed** — GoGo Loco and SPS/OGB/PCS stripped (one avatar went from 3088 to 240 of 3200
  sync bits).
- **Face tracking, your way** — native `CVRFaceTracking`, a bundled rig with eye tracking wired
  up, or your avatar's own FT rig converted whole. ARKit and Unified Expressions meshes both work.
- **ChilloutVR's native contacts** — one-to-one, with real proximity and zero sync cost (contacts
  are per-client by design), using a system the CCK doesn't expose ([experimental](#native-contacts)).
- **Shaders that lose an eye get fixed** — CVR renders single-pass instanced where VRChat renders
  double-wide, so shaders that never opted in draw into one eye only.
- **Diagnostics that know ChilloutVR** — the report names components CVR silently deletes on load,
  tracks the 3200-bit sync budget, and flags shaders the uploader will reject.
- **The output folder is the whole conversion** — every clip and mask the controller references is
  copied into `RehomedAssets` and the controller repointed, so a conversion survives being moved to
  a project without the source avatar's folders. One tester's controller referenced 71 clips that
  lived only next to the source avatar; anywhere else they'd have played as stillness, with no
  error. (The CCK's own clips stay referenced — uploading requires the CCK, so they're always
  present.)
- **A play-mode tester that drives avatars the way the game does** — *Tools → Avatar Bridge → CCK
  Animator Tester*: gestures, stances, visemes, emotes, face tracking and the whole Advanced
  Settings menu, plus a live Animator-layers readout with weights, masks and playing clips. Its
  **Remote view** card snaps every `#` local parameter to its default — what other players' clients
  hold forever — so you can see remote-only flickering before you upload. VRChat's Gesture Manager
  can't do any of this: it needs the VRC descriptor, which conversion removes.
- **Animations that can't possibly work get named** — a locked Poiyomi shader silently deletes any
  property that wasn't flagged animated, so the toggle plays perfectly and changes nothing, in
  VRChat as much as here. The report [lists every one](#a-toggle-switches-on-the-layer-plays--and-nothing-changes-on-screen)
  with its renderer.
- **Your avatar writes its own store listing** — counted from what was actually built, sized to
  ChilloutVR's 256-character box, and typed straight into the upload page.

*(No VRChat SDK installed? The tool still runs in [Setup mode](#setup-mode) and prepares any
humanoid for ChilloutVR.)*

## Requirements

| What | Version | Notes |
|---|---|---|
| [Creator Companion](https://vcc.docs.vrchat.com/) (or [ALCOM](https://vrc-get.anatawa12.com/en/alcom/)) | current | **how the project itself is made** — see [Installation](#installation) |
| Unity | **2022.3.22f1** | the version VRChat and CCK 4 both use; install it *through* VCC |
| ChilloutVR CCK | **4.0.x** | always required — it's what the tool builds for |
| VRChat Avatars SDK | SDK3, **via VCC / VPM** | required to convert; without it you get [Setup mode](#setup-mode). The legacy `.unitypackage` SDK cannot coexist with the CCK — see [Troubleshooting](#troubleshooting) |
| [VRCFury](https://vrcfury.com/download) / [Modular Avatar](https://modular-avatar.nadena.dev/) | current | only if your avatars use them; added in VCC alongside the SDK |
| [MagicaCloth2](https://assetstore.unity.com/packages/tools/physics/magica-cloth-2-242307) | *optional* | recommended physics target |
| [DynamicBone](https://assetstore.unity.com/packages/tools/animation/dynamic-bone-16743) | *optional* | alternative; the free [VRLabs stub](https://github.com/VRLabs/Dynamic-Bones-Stub) is enough to convert |

Neither physics package is required — choose **Convert PhysBones to → None** and everything else
still converts.

## Installation

**Everything on the VRChat side comes from the [Creator Companion](https://vcc.docs.vrchat.com/) —
including the project itself.** The SDK ships only as a VPM package, and VPM packages install only
into projects VCC manages, so a Unity project you make by hand has no supported way to get one.
([ALCOM](https://vrc-get.anatawa12.com/en/alcom/) is a drop-in alternative and works the same way.)

**If you already build avatars, you already have all of this** — duplicate that project and skip to
step 3. It has the SDK, VRCFury or Modular Avatar and your avatars in it, imported in the right
order, which is most of this list already done.

> ⚠️ **Import order matters.** Let Unity finish compiling after each step. Importing out of order
> can corrupt VRCFury data or leave broken scripting defines. Duplicating an existing project
> sidesteps this — it was already built in order.

1. **A VCC project on Unity 2022.3.22f1** — **duplicate your avatar project**, or **New Project →
   Avatars**. Never convert in your real upload project.
2. **VRCFury / Modular Avatar**, then **your avatars** — in VCC's **Manage Packages**, from the
   community repos you'll likely already have listed. Fury before the avatars that need it.
3. **ChilloutVR CCK 4** — the `.unitypackage` from
   [the ChilloutVR documentation](https://docs.chilloutvr.net/cck/setup/). Not a VPM package;
   import it into `Assets` like any other.
4. **A physics package** (optional) — MagicaCloth2 or DynamicBone.
5. **AvatarBridge, last** — the `.unitypackage` from
   [Releases](https://github.com/MrTactical/AvatarBridge/releases). It must live under `Assets`,
   not `Packages`, or the optional MagicaCloth2 / DynamicBone integration won't resolve.

One extra recompile after importing is normal — that's AvatarBridge registering its scripting
defines.

## Usage

**Tools → Avatar Bridge → VRChat to ChilloutVR Converter**, then:

1. **Pick the avatar** in your scene.
2. **Press "Analyse this avatar"** (greyed out until step 1). It reads the avatar and offers the
   settings its own contents decide — physics target, face tracking mode, which layers to merge —
   each with an **Apply**. Nothing changes until you press one.
3. **Look at Manual options** — the handful of things the avatar can't tell you. Leaving them all
   alone converts fine. Everything else lives in **Automated options**, folded away because the
   analysis sets it.
4. **Convert.** Output lands in `Assets/AvatarBridgeOutput/<avatar>/` — a sibling of the tool's
   folder, so deleting `Assets/AvatarBridge` to update it never touches your conversions. Read
   the report, then test in game. From 3.6.3 the report is also written as a **web page**
   (`ConversionReport.html`, "Open web report" in the window): what happened drawn as charts,
   every entry filterable, and the technical appendix rendered — self-contained, so it opens
   from disk and can be shared as-is. The markdown beside it stays the file to attach to bug
   reports.

## What gets converted

| VRChat | ChilloutVR | Notes |
|---|---|---|
| Avatar descriptor | `CVRAvatar` | visemes, blink, eye look (gaze limits measured from the poses); the **viewpoint your author already placed in VRChat**, copied across unchanged, with the CCK's Auto placement (eye-bone midpoint) as the fallback; voice at the jaw bone, else measured. On a [quadruped decoy rig](#the-viewpoint-or-voice-position-is-nowhere-near-the-head) both are re-measured on the bones you can actually see |
| Expression parameters + menus | Advanced Avatar Settings | named after the menu control's label |
| Clothing / prop toggles | one `Toggle <name>` layer each | pulled out of VRCFury's merged blend trees; the "off" direction becomes [real animation](#a-toggle-switches-on-but-never-back-off) instead of relying on Write Defaults |
| Parameter types | real `bool` / `int` / `float` | see [below](#parameter-types) |
| Gestures | float threshold bands, the CCK's own idiom | analog fist blends in by trigger pressure, like VRChat |
| Animation clips + masks | copied into `RehomedAssets`, controller repointed | the output folder alone is the whole conversion |
| Skinned mesh bounds | resized to the avatar's own volume, plus 0.3 × its height of clearance | stops meshes vanishing at screen edges. Measured from the bones that skin the avatar, so it's shaped like the avatar rather than a cube; boxes that were bigger are brought down to it too |
| PhysBones + colliders | **MagicaCloth2** or DynamicBone | see [below](#physbones--magicacloth2) |
| Contacts | native contacts, or `CVRPointer` / trigger | see [below](#native-contacts) |
| VRC Constraints | Unity constraints | including *Target Transform* — see [below](#constraints-that-drive-another-object) |
| VRCFury parameter compressor | removed | a VRChat sync workaround that breaks sync here |
| FinalIK components | kept as-is | ⚠️ CVR deletes some — see [quadrupeds](#quadruped--finalik-avatars) |
| VRC tracking / locomotion control | `BodyControl` | hands a limb from IK over to animation. Head, pelvis, arms, legs and locomotion map exactly; eyes, mouth and **fingers** have no ChilloutVR mask yet — see [emote hand poses](#an-emotes-hand-pose-is-wrong-or-follows-your-gesture) |
| Base / Action / Sitting locomotion animations | grafted into CVR's own `Locomotion/Emotes` layer | custom walk/crouch/crawl/fall/sit clips, matched by blend-tree position, loop settings matched to the slot; VRChat `proxy_*` placeholders skipped — those live in the VRChat client, and CVR's animation set is their equivalent here |
| VRChat flight / copter systems | pose grafted onto CVR's `LocFlying` state | ChilloutVR flies natively (keybind or double-jump where the world allows), so the VRChat system's speed logic isn't needed — the avatar's flight pose plays whenever the wearer actually flies |
| VRChat's scale parameters | `AvatarHeight` stream + derived arithmetic | `EyeHeightAsMeters` fed live; `ScaleFactor`, `ScaleFactorInverse`, `EyeHeightAsPercent`, `ScaleModified` computed from it each cycle against the converted viewpoint height |
| Jaw-flap lip sync | `visemeMode = JawBone` / `SingleBlendshape` | rig-driven, no wiring needed |
| VRC Head Chop | `FPRExclusion` | ⚠️ show/hide only |
| Avatar cameras / listeners | removed | a stray `Camera` crashes CVR's asset filter |
| Avatar audio sources | clamped to VRChat's limits — doppler 0, distance floors/caps | CVR feeds them to its spatializer unclamped; one `minDistance 0` source on the wearer's body can mute the whole game's audio while worn |
| PhysBone `_IsGrabbed` / `_Angle` | [GrabbyBones](https://github.com/kafeijao/Kafe_CVR_Mods/tree/master/GrabbyBones) mod | optional mod, not bundled — see [grabbing](#grabbing-a-chain) |
| Face-tracking blendshapes | native `CVRFaceTracking`, bundled rig, or your own rig converted | see [below](#face-tracking) |
| Menu **Button** controls | ordinary toggles | ⚠️ CVR has no momentary control |
| Shaders without stereo support | patched copy in `RehomedAssets` | optional — see [below](#shaders-that-only-draw-into-one-eye) |
| VRCFury temp materials/shaders | rescued into `RehomedAssets` | Fury deletes its temp folder on its next build |

**GoGo Loco and SPS/OGB/TPS/PCS are stripped by default** (both toggleable). CVR has its own
locomotion, and the haptics stacks don't function there while eating most of the sync budget.

**Animation that can't do anything is stripped too** (*Remove animation that can't do anything*,
on by default). A curve writing to a material property the renderer's shader doesn't have — the
signature of a [locked Poiyomi shader](#a-toggle-switches-on-the-layer-plays--and-nothing-changes-on-screen)
that baked it away — does nothing in ChilloutVR and did nothing in VRChat either. Those curves go,
and anything left with no purpose goes with them: a clip animating nothing, a layer whose every
clip is empty, then the parameter and the menu control that drove it. The result is an avatar
without sliders that move and change nothing. Renderers whose materials an animation *swaps* are
never touched — there the property may well exist on the material being swapped in — and only the
conversion's own copies of the clips are edited, so the source avatar is untouched. Fix the
materials in Poiyomi and convert again to get the real controls back; the report names everything
removed either way.

**Keeping GoGo is experimental, with hard limits.** With *Remove GoGo Loco* unticked, GoGo fully
replaces ChilloutVR's locomotion the way it replaces VRChat's: the CCK's own Locomotion/Emotes
layer is removed and GoGo's Base/Poses/Action take over, driven by the game-fed velocity and
upright parameters — so **Base, Additive and Action must be ticked** under layer merging or the
avatar has no locomotion at all. The limits are architectural, not bugs to file: GoGo leans on
VRChat-only animator primitives with no ChilloutVR equivalent — locomotion locking
(`VRCAnimatorLocomotionControl`, so poses slide if you walk mid-pose) and pose-space viewpoint
shifts (`VRCAnimatorTemporaryPoseSpace`, so the camera stays at standing height in floor poses) —
and CVR's quick-menu emotes won't animate, since GoGo's own wheel replaces them. ChilloutVR
provides locomotion, emotes, AFK and flight natively; removing GoGo remains the recommended
path. The strip removes GoGo's Base/Additive/Action layers *whole* — a locomotion replacement
left half-alive overrides CVR's locomotion with dead animation, which is worse than either
extreme.

**GoGo installed through a VRCFury prefab is stripped too, from 3.5.40.** Installed by hand it
names its layers "GoGo Loco …"; installed through Fury it names them after its parameters, as
`Go/Beyond`. Earlier versions matched only the first spelling, so on a Fury avatar the layer
outlived a strip that had already disabled its parameters — and it was not idle. It sat at full
weight on Override and read ChilloutVR's *own* `Sitting`, `Grounded` and `AFK`, so sitting on a
chair in game played GoGo's seat animation over ChilloutVR's station pose. If your seats look
wrong on a Fury avatar, reconvert.

**The VRCFury Parameter Compressor is removed.** It beats VRChat's 256-parameter ceiling by marking
your real parameters *not synced* and rotating mirrors through a couple of slots twice a second.
ChilloutVR has 3200 bits and syncs straight from the animator, so carried across it costs a
per-frame blend tree and — because the originals stay marked not-synced — **the values reach
nobody**. Removing it puts every affected parameter back to syncing natively.

### Third-party prefabs

Anything VRCFury or Modular Avatar installs is baked first, so most prefabs convert without needing
to be known about. Tested end to end and working in game:

| Prefab | Notes |
|---|---|
| [Avatar Limb Scaling](https://github.com/xNanochip/VRC-Avatar-Limb-Scaling) | sliders scale the real bones; needs the *Target Transform* handling below |
| [GoGo Loco](https://franadavrc.gumroad.com/l/gogoloco) | stripped (CVR has its own locomotion, emotes, AFK and flight; GoGo relies on VRChat-only animator primitives and cannot function in CVR — see [What gets converted](#what-gets-converted)) |
| VRCFaceTracking / ARKit rigs (Jerry's, Pawlygon…) | replaced by the chosen face-tracking mode — or converted whole with *Keep the avatar's own rig* (smoothing proxies go `#`-local, zero sync cost) |

If a prefab's feature comes through inert — the menu control appears, moves, and does nothing —
that's worth reporting. Every case so far has been a fixable gap in AvatarBridge.

## Constraints that drive another object

A VRC constraint can sit on one object and drive a **different** one, through its `Target
Transform` field. Unity's constraints have no equivalent — they always affect the transform they're
attached to. AvatarBridge honours it by putting the Unity constraint **on the target** instead,
carrying the same sources.

That matters more than it sounds: prefabs routinely put constraints on proxy objects inside their
own hierarchy and point them at your real bones. Dropping the redirection doesn't weaken such a
prefab — it silently stops it working while everything still *looks* wired up.

A Target Transform pointing outside the avatar can't be honoured (it wouldn't survive an upload);
the report says so plainly.

⚠️ **Several constraints of the same type on one object still merge into one.** Unity and CVR allow
only one per type per object, so the second's offsets are dropped — its sources are kept.

**Rotation offsets are measured, not copied.** VRC constraints evaluate in the editor, so at
conversion time the scene pose *is* VRChat's solver output — the offset is derived from that
directly rather than trusting both engines to apply the field in the same space. (Multi-source or
inactive constraints still copy it.) One car avatar's windshield pupils came out 77° edge-on before
this.

**Solving in local space is repaired where it can be.** VRChat's constraints can read the source's
**local** rotation — the SDK's own default — and Unity's only ever solve in world space.

Usually this costs nothing: where object and source share a parent, its rotation cancels on both
sides. It matters when the source sits in a **different chain**, and there the parents can be *made*
to agree by moving the constrained bone under the source's own parent. That's not an approximation —
it turns a constraint Unity can't express into one it can, and it cascades down the chain. This is
what makes [quadrupeds](#quadruped--finalik-avatars) work.

Moving a bone is only safe when nothing depends on where it *is*: rotation-only relay, no mesh
skinned to it, no animation addressing it (curves match by path), and nothing mirrored. Every move is
named in the report; anything failing a check is left alone and reported.

**Where it can't be repaired, the constraint yields to its animation.** If the bone skins the mesh
and so can't be moved, the converted constraint is wrong whenever the two parent chains diverge — and
since constraints evaluate *after* animators, a wrong constraint overrides a right animation. So when
a clip also poses that bone, the constraint is **disabled** and the animation stands. What's lost is
only the live follow. Bones nothing animates keep the world-space follow.

⚠️ **A mirrored parent is lossy whatever happens.** VRChat's solver corrects a constraint's result
when the parent's scale has a negative axis; Unity's constraints don't, and ChilloutVR ships no type
that does. Constraints under such a parent land reflected, and the report names every one.

## PhysBones → MagicaCloth2

**Structure transfers exactly:** which bone the chain hangs from, which colliders it collides with,
which transforms to leave out, whether it started enabled.

**Size is measured as you wear it, once.** Every radius here comes from the mesh with your
blendshape weights applied, so an avatar saved with a body slider part-way up is measured at the
shape people will see. What it can't do is *follow* that slider in game: a size slider that works
by **scaling bones** is fine, because the cloth simulates those bones and follows for free, but one
that works by **blendshape** moves no bones — and MagicaCloth2 won't take an animated radius (of
its parameters only pose ratio, gravity, damping, inertia, wind and blend weight can be animated at
all). So instead the mesh is measured a *second* time with every animated blendshape pushed as far
as the animator can take it, and the larger reading wins: collision covers the body when the slider
is up and is a little generous when it's down, which beats reaching into a body that's visibly
there. Shapes that *shrink* cost nothing, because the saved reading wins. The report names every
chain sized this way, and every chain whose slider it couldn't follow.

**The colliders themselves are fitted to the body.** A PhysBone collider carries one radius from
end to end; MagicaCloth2's capsule takes a start radius and an end radius separately, so a converted
thigh or arm collider tapers the way the limb does instead of splitting the difference. The body
part the collider sits on is measured and the capsule fitted to it. That measurement *replaces* the
source's dimensions, because a PhysBone collider's size is invisible in VRChat unless something
collides with it — one avatar here carries the same 0.07 radius and 0.4 length on the thigh and the
shin alike, which is a default rather than a decision. Only the host bone's own vertices are read,
so a leg collider can only come out leg-sized. Every change is in the report with its before and
after, and *Fit colliders to the mesh* turns it off.

**So does the feel.** Each chain's `pull`, `spring` and `stiffness` are converted into
MagicaCloth2's damping and angle restoration, and `immobile` into its inertia. Every adjustment is
named in the report alongside the PhysBone's original numbers.

<details>
<summary>How the conversion is derived, and why it took so long</summary>

For a dozen versions AvatarBridge refused to map these at all, on the stated grounds that PhysBones
were per-bone rotational springs and MagicaCloth2 a particle position solver, so no arithmetic
between them could mean anything.

**That was wrong.** The VRChat SDK ships `VRC.Dynamics.dll` unobfuscated, and
`PhysBoneManager.PhysBoneJob.SolveChain` integrates bone *endpoints*, reading rotations back out of
where they land — the same thing MagicaCloth2 does. The real obstacle was calibration: both apply
per-step coefficients at a fixed rate, PhysBone 60 Hz and MagicaCloth2 90 Hz, so a retention `r` on
one side is `r^(60/90)` on the other. Three multipliers had to be undone along the way —
MagicaCloth2 scales its restoration stiffness by `0.2` before the solver sees it *and* applies it
three times per step, and PhysBone's `stiffness` isn't an independent axis at all (the algebra
collapses it into a scale on the other two, and Simplified integration never reads it).

The check that it's right: push MagicaCloth2's *own* default restoration back through the mapping
in reverse and you get a PhysBone pull of **0.168**, against a default PhysBone's actual **0.160**.
Two authors who never spoke, five percent apart.
</details>

Four facts about the source carry over without any conversion, because they're categorical rather
than numeric:

- **No gravity** stays none — presets ship their own, and one of them would make a chain fall for
  the first time in ChilloutVR
- **Negative gravity** points up
- **Immobile** becomes inertia influence, applied to *both* of MagicaCloth2's inertia values and —
  for the default *All Motion* type — an **inertia anchor** on the chain's parent bone. That anchor
  is what makes a chain stop swinging when your head turns, not only when you walk.
- **Wind influence goes to zero**, because VRChat has no wind at all. ChilloutVR worlds do, and
  MagicaCloth2 ships fully responsive to it, so a converted chain would otherwise pick up motion
  its author never tuned for.
- **`Is Animated` sets Animation Pose Ratio to 1**. MagicaCloth2 settles a chain back to the pose
  the avatar was *built* in; a PhysBone marked `Is Animated` is one an animation moves. Left at
  the default the two fight and the cloth wins — a chest or ear slider that scales its own bones
  simply stops working, and the avatar quietly has a different shape from the original at
  identical menu settings. The source flag decides this, so it's applied rather than reported.

Stretch & squish, multi-child blending and angle limits are reported rather than converted, each
naming the field to change if that chain wants it.

**Using DynamicBone instead?** None of this applies — PhysBones and DynamicBone *are* the same kind
of simulation, so that path maps values 1:1.

> ⚠️ **Physics can only be judged in game.** Nothing steps a cloth solver in edit mode, and shaking
> the avatar root in play mode proves nothing — MagicaCloth2's speed limits make a chain follow
> rigidly the moment they're exceeded, so a fast shake looks still whatever the settings say.

### Options

| setting | default | what it does |
|---|---|---|
| **Match a preset to each chain** | on | Hair, tail, skirt, cape or accessory by bone name; otherwise a soft/middle/hard spring by how firmly the PhysBone held its rest pose |
| **Fit the preset to the PhysBone** | on | The four categorical facts above. Turn it off to get the preset exactly as its author wrote it |
| **Derive physics from the PhysBone** | on | Converts pull, spring and stiffness into damping and angle restoration. It can *firm* the matched preset with the source's own character but never soften it below that preset's baseline — MagicaCloth2's own presets are the floor of a spring that still reads as one, and a very loose PhysBone converts faithfully to mush without it. The report says when the floor held. Turn it off to get the preset exactly as authored |
| **Size particles from the mesh** | on | MagicaCloth2's radius is the collision body of a simulated bone. Left alone it is whatever the matched preset shipped — the same size on a breast as on a hair strand — so collision covers a fraction of what you see. This measures the mesh those bones move — with your blendshapes applied, so a body slider left part-way up is measured as you actually wear it — and sizes each chain to it. The source PhysBone's radius is deliberately *not* used: in VRChat it only governs contact with PhysBone colliders, so it is routinely near zero |
| **Size for the largest a slider makes the body** | on | A body slider grows the mesh, but MagicaCloth2's radius is fixed — of its parameters only pose ratio, gravity, damping, inertia, wind and blend weight can be animated at all — so collision is right at one slider position and wrong at the rest. This measures the mesh again with every animated blendshape pushed as far as the animator can take it, and keeps the larger reading: collision covers the body when the slider is up and is a little generous when it's down, which is the better way round. Shapes that *shrink* cost nothing — the saved reading simply wins |
| **Fit colliders to the mesh** | on | A PhysBone collider carries *one* radius from end to end, so an author covering a thigh has to choose between fitting the hip and fitting the knee. MagicaCloth2's capsule takes a start and an end radius separately, so the converted one can taper the way the limb does. This measures the body part the collider sits on and fits the capsule to it. The measurement *replaces* the source's numbers: a PhysBone collider's size is invisible in VRChat unless something collides with it, so it's routinely one default stamped onto every collider on the avatar. Only the host bone's own vertices are read, so a leg collider can only come out leg-sized. The report gives the before and after for each |
| **Cap particle radius to bone spacing** | off | Bounds each particle to half the gap between its bones. Off since the radius above became a measurement rather than a guess — on a soft-body chain, where two or three bones carry a large volume, this throws most of that measurement away. The overlap it guards against only bites with self-collision, which MagicaCloth2 leaves off. Turn on if a long chain of closely-spaced bones misbehaves |
| **Convert toe PhysBones** | off | Toes are left out of the simulation entirely — both chains *rooted* at them and toe branches found part-way down a longer chain (a leg or skirt chain that runs through the feet), for MagicaCloth2 and DynamicBone alike. Simulated toes splay and swing while IK plants the foot, which reads as broken feet rather than as physics. Turn on if the toe physics are deliberate |
| **Bound swing to the source's limit** | on | A PhysBone's angle limit is often the only thing keeping a deliberately loose chain presentable — convert the looseness without it and the chain swings much further here than it did in VRChat. This bounds how far each bone may travel from rest, worked out from that limit and the chain's length, easing to nothing at the root. It's a *distance* bound rather than an angle limit, so it removes motion instead of adding a restoring force and can't set the chain vibrating |
| **Auto-assign nearby colliders** | off | Gives each cloth the avatar's own colliders it could swing into. Improves on the original rather than copying it, so check before uploading |
| **Add physics to toggled rigs that have none** | off | A toggled style (usually add-on hair) carrying its own rig and mesh but no PhysBone was rigid in VRChat too; this synthesizes a MagicaCloth for it, preset by classification, wired to the style's toggle. Off because it invents physics the author never made |

## Native contacts

ChilloutVR's contact system is a near-exact match for VRChat's — the same Sphere/Capsule shapes,
the same `allowSelf` / `allowOthers` / collision tags under the same names. It lives inside the
game client and the CCK ships no way to author it, so converters have always had to approximate it
with pointers and triggers.

**AvatarBridge can author it directly.** Turn on *Use ChilloutVR's native contacts* under
**Advanced** and contacts convert one to one: real proximity, tags verbatim.

### Grabbing a chain

**MagicaCloth2 has no grab.** VRChat lets you take hold of a PhysBone and pull it, and plenty of
avatars are built entirely around that — a pump handle, a leash, a lever, anything a stranger is
meant to pull. Converted, those chains still hang and swing, but nobody can hold them.

[GrabbyBones](https://github.com/kafeijao/Kafe_CVR_Mods/tree/master/GrabbyBones) adds grabbing back,
and AvatarBridge already targets it: converted cloths are named after the PhysBone's parameter so
the mod's `_IsGrabbed` and `_Angle` drive your existing grab-reactive logic, and those parameters
are kept synced rather than made local. That's as far as any converter can go — **grabbing is a
client mod**, so only people who have installed it can grab anything on your avatar.

**The failure this causes is silent and looks like something else.** On one balloon avatar
the pump handle carries a contact *sender*, and inflating works by someone grabbing the handle so
that sender reaches its receiver. Convert it and every part checks out — cloth present, sender
present, receiver present, tags matching — but the handle can't be grabbed, so it never moves and
nothing fires. An afternoon went into blaming the contact tags. The report now lists every chain
that was grabbable in VRChat and marks the ones carrying a contact, because those are the features
that go completely inert without the mod.

### Being touched by ordinary ChilloutVR players

A contact only fires when something **sends** a matching tag, and the two platforms name the same
body parts differently. Everyone in ChilloutVR carries pointers on their hands and index fingers
whatever avatar they wear — the client turns each `CVRPointer` into a contact sender tagged with its
`type` — but those types are `LeftHand`, `RightHand`, `index`, where VRChat says `HandL`, `HandR`,
`FingerIndexL`. `Hand` happens to be spelled the same on both, which is why *some* converted
contacts worked and others silently never fired.

Receivers listen for both, so a stranger's hand or finger sets them off:

| Your receiver listens for | Also listens for |
|---|---|
| `Hand` | `grab` |
| `HandL` / `HandR` | `LeftHand` / `RightHand` |
| any `FingerIndex*` | `index` |

The VRChat tags are kept, so converted avatars still trigger each other exactly as before.

**Tags the author invented — `pump`, `Balloon`, a system's private name — reach nobody**, because
nothing else in the game sends that word. Between two copies of the same avatar they work fine; to
everyone else those receivers are inert. That's usually deliberate, so nothing is changed, but the
report lists them so it isn't a surprise. Add a body-part tag to a receiver if you want strangers to
be able to set it off.

**Contacts are per-client by design** — the system is by
[NotAKidoS](https://github.com/NotAKidoS/Misc-Unity-Stuffs/tree/main/NAK.Contacts), a ChilloutVR
developer. Every client simulates every avatar's contacts itself, so **detection costs no sync at
all**. Whether the parameter a receiver drives replicates its value is that parameter's own sync
declaration, unchanged.

**Contacts anchor where VRChat anchored them.** A contact's shape rides its `Root Transform`
override when one is set — the component itself often lives somewhere central while the shape
follows a bone, which is how head-pat receivers and VRCFury-baked contacts are built (about a
quarter of all contacts measured in the wild). Converted contacts are parented at that anchor on
both paths, so they follow the same bone they did in VRChat.

**Animated contact switches follow their contact** — on the legacy path and the native one. VRChat
avatars animate a contact's enabled flag to switch it off ("disable head pats" is built this way);
that component is deleted by conversion, so the curve used to play as silence while its menu entry,
parameter and layer all converted. Those curves now toggle the converted contact's own object, the
form the client honours everywhere (read from the decompiled `ContactBase`, which registers in
`OnEnable` and de-registers in `OnDisable`). Curves animating a contact's shape or filters have no
equivalent and are removed with a report line naming each.

**How it works without CCK support.** An uploaded asset bundle carries no script assemblies — only
each component's assembly, namespace and class name, resolved against the player's own assemblies at
load. The contact implementation already ships *inside the ChilloutVR client*; the CCK simply
provides no way to author it. AvatarBridge generates matching declarations into
`AvatarBridge/Runtime`, verified **field-for-field against the decompiled client**, and the game's
own implementation runs. Nothing is reimplemented and nothing extra is bundled into the avatar.

> ✅ **Confirmed in a live instance:** validation clean, uploaded, contacts triggered by other
> players, CVR's own runtime gizmos drawing the components.
>
> ⚠️ **Experimental, and off by default.** This talks to a component internal to the game rather
> than the CCK, so any ChilloutVR update can break it, possibly for good. The conversion falls back
> to the legacy path by itself if anything is wrong.

Two practical notes:

- **Don't import the author's public repository** into a conversion project. It's a diverged
  work-in-progress whose current layout drops fields the shipped client still reads — including the
  flag that lets other players' hands trigger receivers.
- **If a conversion leaves broken `Contact_*` components behind, delete them and reopen the scene**
  before converting again. Unity manufactures a placeholder script for a dangling reference, and it
  then captures every new component of that class — one bad conversion poisons the next. AvatarBridge
  detects this and refuses; *Tools → Avatar Bridge → Diagnose native contacts* shows what Unity holds.

## Shaders that only draw into one eye

**ChilloutVR renders single-pass instanced; VRChat renders double-wide single-pass.** Both SDKs
force their own mode unconditionally.

From 3.6.4 the check reads the shader's *whole* include chain, the way the compiler does — so a
shader that keeps its stereo handling in include files (lilToon, most modern toon shaders) is
recognised as already correct instead of flagged. The CCK's own upload warning still judges the
one file and may keep naming such shaders; that warning is theirs, and safe to ignore for them.

Under double-wide a shader gets both eyes without asking. Under instancing it has to declare that it
knows which eye it's drawing — so a shader that never opted in looked perfectly fine in VRChat and
draws into one eye only here. Nobody did anything wrong; it's a conversion problem, which makes it
worth fixing here.

<details>
<summary>Fixing one by hand</summary>

Four macros, each with one home — copy the shader first, it's usually someone else's asset:

| macro | goes in |
|---|---|
| `UNITY_VERTEX_INPUT_INSTANCE_ID` | the vertex **input** struct (`appdata`) |
| `UNITY_VERTEX_OUTPUT_STEREO` | the **interpolator** struct (`v2f`) |
| `UNITY_SETUP_INSTANCE_ID(v);` | top of the vertex function, after the output struct is declared |
| `UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);` | same place, right after it |

Add `UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);` at the top of the fragment function too if it
samples anything screen-space. Only realistic on a plainly written shader: surface shaders have no
vertex stage to edit, and locked or generated shaders aren't worth attempting.

</details>

Turn on **Patch non-SPI shaders for VR** in *Advanced*. For each affected shader it writes a patched
copy into `RehomedAssets`, adds the stereo macros, and points this avatar's materials at the copy.

- **Your originals are never modified** — shader and material are both copied, so other avatars
  sharing them are unaffected.
- **A copy that doesn't compile is thrown away**, so the worst case is a report line rather than
  wrong pixels.
- **Screen-grab and depth reads are fixed too.** Both are texture *arrays* under instancing — one
  slice per eye — so lens, refraction and soft-particle shaders take the wrong slice however many
  macros they have. Those reads are rewritten to the screen-space macros. **This is why passing the
  CCK's check isn't the same as being correct**: it looks for four macros, and a shader can have all
  four and still be broken.
- **Shaders needing more than the derivable fixes get a recipe** — written by hand once and applied
  to your copy on later conversions, pinned to a fingerprint of the exact shader version so an
  edited shader is refused rather than guessed at. Nothing is redistributed; the recipe is the edit,
  not the shader. Hit one with no recipe? Open an issue.
- **Not everything can be patched.** Surface shaders have no vertex stage, and structs in a shared
  include can't always be edited from one file. Those are listed for hand-fixing.
- **Every shader gets a verdict in the report** — patched, couldn't be, or already correct. Modern
  Poiyomi declares the full macro set, so locked shaders normally land in the last group.
- **There's nothing to undo.** The macros are mode-agnostic: real instancing code under CVR, nothing
  under VRChat or desktop.

> ✅ **Confirmed in game:** a soft-particle effect the CCK flagged was patched, uploaded, and renders
> correctly in both eyes.
>
> **Still off by default**, on one avatar's evidence. Compilation is all that can be checked
> automatically — whether it *looks* right is a judgement no editor script can make, so turn it on
> deliberately and **check the effect in both eyes**.

## Parameter types

**VRCFury bakes every menu parameter as a `float`**, whatever it really is. Harmless in VRChat; not
here, because ChilloutVR writes a menu value using the entry's *own* declared type — write a Bool
into a Float parameter and nothing happens. That's the most common cause of "the toggle does nothing
in game".

So each parameter is retyped from what the avatar's logic says it is: the **menu control** it drives
(Toggle → bool, Dropdown → int, Slider → float), or for parameters with no control, how the
**animator compares** it. Anything read as a *quantity* — blend tree, motion time, or written by a
clip — stays `float`, and is named in the report so you can see the tool declined rather than
missed it.

## Face tracking

Pick one in the **Face tracking** dropdown. The two set-up modes remove whatever FT rig the avatar
shipped with — animator layers, parameters *and* objects — so nothing is left fighting over the same
blendshapes. On a typical VRCFT avatar that's a couple of layers and a few hundred parameters.

- **Native CVR Component** — sets up `CVRFaceTracking` and maps the shapes. Self-contained, but the
  built-in solver is a bit stiff.
- **Unity Animator Blendtrees (DSR)** — injects DragonSkyRunner's *CVR Eye & Face Tracking* rig
  (bundled), repaths every clip onto your actual eye bones and face mesh, and reconciles its shape
  vocabulary against whatever your mesh has — by name, casing, **ARKit ↔ Unified Expressions**
  aliases, and combined/split rules. An **ARKit avatar** works without renaming anything. Smoother
  and more expressive.
- **Keep the avatar's own rig** — nothing is stripped, and this is *not* a do-it-yourself option:
  the existing rig (Jerry's Templates, Pawlygon, OSCmooth setups…) **converts** with the rest of
  the animator. Smoothing proxies VRChat never synced automatically become `#`-local — which costs
  **zero** sync bits, so a full smoothed rig fits ChilloutVR's 3200-bit budget comfortably — and
  the FT parameters that were synced keep syncing. The pick for avatars whose shipped rig is the
  point.

**Every mode needs a tracking source at runtime** — true of any CVR face-tracking avatar. Run
[VRCFaceTracking](https://store.steampowered.com/app/3329480) and set CVR's *Eye Tracking* and
*Mouth Tracking* modules to **OSC**.

## Store description

ChilloutVR's upload page wants a description, and most listings never get one. AvatarBridge writes
a starting point out of what the conversion actually produced:

```

                        ← your own words go here

Vap
8 toggles · 9 sliders · 9 physics chains (MagicaCloth 2) · blink and lip sync

Converted from VRChat with AvatarBridge
github.com/MrTactical/AvatarBridge
```

Two buttons on the report — **Fill CCK description** (types it into the Content Manager's Description
box; open the Control Panel's **Builder** tab first, and it won't overwrite anything you've already
written) and **Copy description**. Either way it's saved as `Description.txt` beside the report.

**Every claim is checked against what was built**, not what you asked for — face tracking is only
mentioned if the component is really there. This goes into a public listing under your name, so a
line it can't verify is a line it doesn't print. It's sized to ChilloutVR's 256-character box with
~90 left free for your own words: it's meant to be the footer of your description, not all of it.

## Setup mode

Without the VRChat SDK there's no VRChat data to read — a VRChat avatar's components won't even
deserialize — so conversion isn't possible. Instead the tool prepares **any humanoid** for
ChilloutVR: `CVRAvatar` with viewpoint and voice position, viseme and blink detection, face
tracking, and the height scaler. Useful for a Booth model or an original avatar.

<details>
<summary>Why there's no VRChat SDK stub</summary>

A GUID-matching stub (like the DynamicBone one) could recover simple components, but it could never
run VRCFury or NDMF — those are real code needing the real SDK — so Fury avatars would silently
convert to empty shells. Unity also deserializes by field name and *silently defaults* what it can't
match, so every SDK update would quietly change the output. The DynamicBone stub exists to work
around a paywall; the VRChat SDK is free, and VCC installs it with the project.
</details>

## Options reference

Every setting in the window, with the default it ships with. Labels match the window verbatim; the
tooltip on each control says the same thing at more length. Per-chain physics tuning has its own
table [above](#options) and isn't repeated here.

### Analyse this avatar

The button above both option cards, greyed out until an avatar is picked. It reads the avatar as it
sits in the scene — PhysBones, blendshapes, shaders, parameters and layers — and lists what it found
against the settings those decide, each row with its own **Apply**. Nothing changes until you press
one. Each check asks the converter's own question through the converter's own code, so a
recommendation and the conversion that follows can't disagree.

| row | means |
|---|---|
| **Recommended** | Measured, and the current setting doesn't match. **Apply all** takes these |
| **Blocked** | The setting can't do what it says — a missing package, usually. Fix attached where there is one |
| **Your call** | Nothing in the avatar answers this. Never included in **Apply all**; the button is a shortcut for a decision you've made, not one made for you |
| **Already set** | Measured, and the current setting is right |
| **Not needed** | Nothing on this avatar for the setting to act on, whichever way it's set |

On a VRCFury or Modular Avatar setup it says so first: the scan runs *before* the bake, so every
count is a floor and a zero means "none yet", not "none". That's also why it won't recommend
switching physics off on a baked avatar whose hair and clothing haven't arrived yet.

### Manual options — what the avatar can't tell you

The card that stays open. Every row is a departure from the source avatar, a judgement about the
author's intent, a choice about how *you* finish the avatar, or something only wearing it can
settle. Leaving all of them alone converts fine.

| setting | default | what it does |
|---|---|---|
| **Use ChilloutVR's native contacts** | off · BETA | One-to-one onto CVR's own contact components — real proximity, tags verbatim — instead of approximating with pointers and triggers. Talks to a component internal to the game |
| **Patch non-SPI shaders for VR** | off · BETA | Copies shaders that [draw into one eye only](#shaders-that-only-draw-into-one-eye) into `RehomedAssets` with the stereo macros added. Analyse counts them; whether a patched copy *looks* right is a VR question |
| **Toggle style** | Animator Layers | *Animator Layers* gives each toggle its own Off/On layer and works immediately. *CVR Native Targets* leaves object toggles to the CCK's builder — you must press **Create Controller** yourself |
| **Add height scaler  ("Height" slider)** | on | A quick-menu slider from 0.25× to 4× of this avatar's measured height, centred on its original size. Parent-constrained props are re-anchored so they scale with you |
| **Extra strip keywords** | *(empty)* | Comma separated. Each is matched as a parameter prefix and a layer name, for other VRChat-only systems |
| **Output folder** | `Assets/AvatarBridgeOutput` | Where the converted avatar and its rehomed clips, materials and controllers are written. The folder alone is the whole conversion |

Physics has a card of its own, above Manual and Automated: which solver to convert into isn't
something the avatar decides, so it isn't buried with the settings that are. Its **Your call**
section holds the three that depend on intent rather than measurement — **Convert toe PhysBones**,
**Add physics to toggled rigs that have none** and **Auto-assign nearby colliders** — described in
the [physics table](#options) above and not repeated here.

### Automated options — set from the avatar

Folded away behind a warning, because each of these has a right answer the avatar already gives and
Analyse sets them to match. Open it to override a measurement deliberately, not to browse.

| setting | default | what it does |
|---|---|---|
| **Work on a clone (recommended)** | on | Converts a copy and leaves your original untouched. Turning it off edits the avatar in the scene |
| **Convert PhysBones to** | MagicaCloth 2 | MagicaCloth 2 gives the best result in ChilloutVR; DynamicBone is the built-in fallback. Analyse checks which is actually installed |
| **Delete PhysBones after converting** | on | Removes the VRChat components once their replacements exist. Off leaves both, which uploads but simulates nothing |
| **GrabbyBones mod support** | on | Keeps chains grabbable by the GrabbyBones mod, the closest thing CVR has to VRChat's bone grabbing |
| **Face tracking** | Native CVR Component | Native drives blendshapes through CVR's own `CVRFaceTracking` — self-contained, a bit stiff. *Unity Animator Blendtrees (DSR)* rebuilds DragonSkyRunner's rig onto the avatar — smoother, more expressive. *Keep the avatar's own rig* strips nothing. Both set-up modes replace any existing FT rig |
| **Remove GoGo Loco (recommended)** | on | Strips GoGo Loco, whose locomotion VRChat needs and ChilloutVR provides natively |
| **Remove SPS / OGB / PCS / Wholesome (recommended)** | on | Strips VRChat-only intimacy systems that have no ChilloutVR equivalent |
| **Remove animation that can't do anything (recommended)** | on | Drops curves pointing at material properties the shader doesn't have — dead in VRChat too, noisy in CVR |
| **FX (toggles, expressions)** | on | The layer nearly every toggle lives in |
| **Gesture (hand poses)** | on | Hand poses, converted to the CCK's own float threshold idiom |
| **Base / locomotion** | off | Brings across what VRChat kept in Base — toggles, blendshapes, materials, additive motion — and grafts the avatar's own walk, crouch and crawl onto CVR's locomotion. Analyse recommends it when the avatar has a Base layer of its own that isn't GoGo |
| **Additive** | off | VRChat's additive layer, usually breathing |
| **Action (emotes, AFK)** | off | Emotes and AFK. Off by default because Action takes full body control and misfires are very visible |
| **Preserve parameter sync state** | on | Keeps each parameter's local/synced status as VRChat had it, rather than syncing everything |
| **Expose menu-less synced parameters** | on | Synced parameters with no menu control still [need an entry to exist](#a-menu-control-appears-moves-syncs--and-does-nothing) in CVR |
| **Convert contact senders/receivers** | on | VRChat contacts become pointers and triggers, or [native contacts](#native-contacts) below |
| **Recreate built-in VRC colliders as pointers** | on | The fingers, head and torso colliders VRChat gives every avatar for free |
| **Convert VRC constraints** | on | VRChat constraints become Unity constraints; [driven objects](#constraints-that-drive-another-object) are handled separately |
| **Convert VRC Head Chop** | on | `VRCHeadChop` becomes `FPRExclusion` — CVR's first-person hiding |
| **Convert spatial audio** | on | `VRCSpatialAudioSource` becomes a plain `AudioSource` with equivalent spatial settings |
| **Auto-wire blink blendshapes** | on | Detects blink shapes on the face mesh (`Blink L`/`Blink R` and similar) and turns on CVR's Eye Blink Settings when the descriptor didn't name any |

## Known limitations

### Transforming avatars: desktop-only turn

**An avatar that folds its whole body into something else — a biped into a car — converts with one
limitation, and it is a platform difference rather than a conversion fault.**

What works everywhere: the **descent**. A sequence that lowers the body to the floor comes down
smoothly through the animation instead of snapping at the end.

What works on **desktop only**: the **turn**. Rotating from upright to lying flat plays on desktop
and does not in VR — there the body stays upright through the sequence and the final orientation
arrives in one frame at the end. The end pose is correct on both; only the transition differs.

Why, since it looks like a bug worth reporting: ChilloutVR's character controller owns the player
capsule and keeps it upright, so an animation cannot turn the root. The rotation is therefore moved
into the **bones** — Unity's own "bake into pose", the same mechanism that makes the descent work —
which is the only form the client will show. In VR the IK solver is then driving those same bones
against the animation and wins. Three routes were tried and measured on a real avatar worn in game:
the raw root curve (does nothing), Body Control at weight 0 (frees limbs from IK but never hands
over the root), and the bake (works on desktop, loses to IK in VR).

Nothing on the conversion side can settle this — it needs the client to yield the body, which is
what VRChat's Action layer weight did and ChilloutVR has no equivalent for.

### Quadruped / FinalIK avatars

**Quadruped support is real but partial, and how much you get depends on how yours was built.**
Unity has no quadruped rig — every quad on VRChat is a humanoid skeleton driving an animal-shaped
one by some trick, and *which trick* decides what survives conversion. Three have been converted and
dissected here; they behave completely differently.

All three share one symptom, which is the fastest way to recognise the family: **none of their
humanoid bones move any geometry.** The conversion reports that outright, and `Diagnostics.md` gives
you the number (`Mapped bones … that deform mesh: 0 (0%)`) plus every mapped bone's full path. It
matters because ChilloutVR hangs the viewpoint, the voice position *and* first-person head hiding off
humanoid bones — so on all three they follow a skeleton nobody can see, pass every internal check
against it, and still land half a metre from the avatar's face.

| How it's built | Tell-tale | What you get |
|---|---|---|
| **Constraint relay** — hidden biped, VRC constraints copying it onto the animal | bones named `*Human`, ~60 VRC constraints | **Walks in game.** Locomotion, poses, limb locks, viewpoint, first-person head all working. Hind legs land reflected — see below |
| **Unity-constraint rig** (e.g. AnyTaur) — same idea, Unity's own constraints | `RotationConstraint` throughout, no VRC constraints | **Best case.** Nothing to translate, so none of the constraint walls apply at all |
| **FinalIK proxy** — humanoid mapped into a `VRIK` proxy skeleton | mapped bones sit under `.../VRIK/PROXY_*` | **Least working.** The visible body is posed by IK and relays that can't convert |

**The one thing worth knowing before you start:** a quad built on **Unity constraints converts almost
untouched**, because VRChat's constraint features are what don't survive — local-space solving and
the negative-scale correction. If you're choosing or commissioning a quad base for ChilloutVR, that
is the single biggest predictor of how well it will land.

**The viewpoint and voice get rescued when they land off the body.** Rather than trying to
recognise a fourth rig design, this asks the one question no skeleton can lie about: is the marker
further from *every bone that deforms mesh* than this rig's own proportions allow? If so it isn't on
the avatar, however well it measured, and it's re-placed from the eye markers that **are** on the
body. A marker already on the body is never touched, so avatars that are correct — including the
other two quadrupeds — can't be disturbed. Naming only nominates candidates; geometry decides.

**A constraint-relay quadruped walks in game** — confirmed by wearing one. Most of it converts: on
the avatar this was worked out from, 51 of 59 relays needed nothing at all. **"Base / locomotion" is
safe to leave on**, quadruped or not.

Four things break the rest, and the report names each:

- **Relays that solve in [local space](#constraints-that-drive-another-object)** — typically the
  hind legs, so one walk cycle moves four legs. Repaired where the copied bone can be moved under
  the same parent; reported where it can't.
- ⚠️ **Mirrored bones — the common build, and unfixable here.** A hind rig is usually the front rig
  mirrored, and a mirrored bone carries a negative scale. VRChat's solver corrects for that; Unity's
  constraints have no such step and ChilloutVR ships no type that does, so those relays land
  reflected. Un-mirroring and re-rigging is the only cure — a job for your 3D package. If your quad
  has a **"biped" mode**, try it: it usually stops using the mirrored hind rig and often converts
  cleanly.
- **PhysBones on a relayed bone** feed the constraint their own output until the transform goes NaN.
  Those chains are skipped and listed. Unity's own constraints count too — the loop is engine-level.
- **Both markers, and first-person head hiding, aimed at the decoy.** ChilloutVR hangs the viewpoint,
  voice position and `FPRExclusion` off the humanoid Head bone, which here skins nothing. All three
  are measured on the relayed bones you can actually see instead.

Also worth knowing, though not quadruped-specific: **toggles that switch a constraint on and off**
are how limb locks, sit/loaf poses and flight modes work on these avatars. Curves are repointed at
the Unity constraint (`IsActive` → `m_Active`, `GlobalWeight` → `m_Weight`, `Locked` → `m_IsLocked`,
and **per-source weights** — which is how a prop is handed from one hand to the other).
**`FreezeToWorld` has no equivalent** and is dropped.

**Honest summary: limited support, not full support.** One rig style walks, one converts almost
perfectly, one mostly doesn't. Nothing fails silently — the report names which family you have and
which bones fail — but a quadruped needs testing in game, and some need author-side changes no
converter can make.

**A minority are FinalIK quadrupeds**, and those have a second, unrelated problem:

<details>
<summary>What's known about those, from reading ChilloutVR's own code</summary>

- **`GrounderVRIK` is deleted on load.** CVR whitelists components per-avatar and destroys the rest
  silently — worlds get 57 FinalIK types, avatars 13. `VRIK`, `LookAtIK`, `TwistRelaxer`,
  `GrounderIK`, `GrounderBipedIK`, `CCDIK`, `FABRIK`, `AimIK` and `LimbIK` survive;
  `GrounderVRIK`, `GrounderQuadruped`, `GrounderFBBIK`, `ArmIK`, `LegIK` and `FingerRig` don't. The
  report names these.
- **`GrounderIK` is not a substitute.** It drives separate per-leg IK components; `GrounderVRIK`
  feeds position offsets into VRIK's own solver from inside its update callbacks. Swapping them
  gives no grounding at all, and CVR has no native foot placement to fall back on.
- **ChilloutVR always installs its own `VRIK`**, auto-detecting references from the *humanoid* rig.
  On a quadruped rigged as humanoid that's a biped solve over a quad rig, and nothing a converter
  does can prevent it. Best current guess at the root cause; not proven.

Tracking control does convert correctly, so the groundwork is there if this is picked up again.
Bipeds are unaffected by any of it.
</details>

### Not converted

- **Blendshape-based gaze** — bone-based eye look converts (gaze limits measured from the VRChat
  poses), but VRChat avatars whose eyes move by blendshape only get a report entry. (Blink and
  blendshape face tracking *are* handled.)
- **PhysBone posing, stretch & squish** and their `_Stretch` / `_Squish` / `_IsPosed` parameters
- **VRC state behaviours** other than Parameter Driver and Tracking Control (which becomes
  `BodyControl`) — removed and counted
- **Synced animator layers** and **ONSP audio**
- **Content tags** — set CVR's *Advanced Tagging* (NSFW, loud audio…) yourself before uploading
- **VRChat-only rendering** — SPS/TPS deformation and anything needing VRChat's own shader systems.
  Meshes and materials survive; the effect doesn't.

### Converted with caveats

- **Action-layer emotes and features.** Only **Gesture** and **FX** convert by default — Base,
  Additive and Action are off, because CVR drives locomotion and emotes itself. Ticking Action on
  merges the layer at **weight 0, the weight VRChat itself gives it** (VRChat raises the Action
  playable only while an emote plays; ChilloutVR has no playable layers to raise, and at weight 1
  its idle state would hold your body in rest pose above locomotion). The layer's **full-body
  poses are transplanted into ChilloutVR's own locomotion layer** instead — the one place a pose
  can both assert and hand back — armed once per condition-rise, exactly like VRChat's emote flow;
  see [the menu-control entry](#a-menu-control-appears-moves-syncs--and-does-nothing) for the
  mechanics. **The one exception is a kept GoGo Loco** — GoGo drives Action itself, so with
  *Remove GoGo Loco* unticked the layer is merged live at weight 1 instead.
- **Constant contact receivers** reset to 0 when *any* pointer exits — CVR triggers don't count
  occupants.
- **Stacked PhysBones** (several chains on one bone that VRChat toggles between) all convert, but
  only one is left driving the chain — two solvers on the same bones jitter rather than blend.
  Nothing is deleted, so switching variant is one checkbox; the report names the one kept.
- **Toggled physics follows its toggle, both ways.** Hair swaps and outfit toggles that switched the
  original PhysBone's object are re-wired to switch the generated cloth too — on *and* off, which
  matters because ChilloutVR does not restore a binding nothing writes: mirror only the "on" and a
  control like *Belly physics* turns the physics on the first time and can never turn it back off.
  The one deactivation that is deliberately *not* mirrored is a whole style container being hidden
  while a mesh outside it is still skinned to the same bones — add-on hair grafted onto a base
  hairstyle's rig. Stopping that chain would leave the visible add-on rigid, so it keeps simulating
  instead; the report names each one. **If the chain wasn't
  converted there's nothing to re-wire to**, and the control will look right and do nothing; the
  report warns for each, naming the clip and the PhysBone, next to the *Skipped* entry saying why.
  **Collider switches follow too**: a dress that disables the leg colliders that would clip it
  animates the converted collider's own object now, a form both MagicaCloth2 and DynamicBone honour.
  What can't follow is animation of **live physics values** — a size slider growing a chain's
  radius, gravity changing with an outfit — because MagicaCloth2's parameters cannot be driven by
  animation at all. The chain keeps its converted values, the rest of the animation plays, and the
  report names each lost parameter.
- **Dropdowns sometimes keep `(unused)` entries.** CVR selects options by *position*, so gaps need
  padding. Normally removed by renumbering, but that's unsafe when the value is used as a quantity
  or passed to a driver — the report says which applied.
- **Parameter-packing optimisers** (`MemOpt_*` and similar) leave odd-looking menu entries.
  ⚠️ **Don't delete them** — they're what carries your toggles to other players. (Syncing comes from
  the animator declaration; the menu entry decides whether the value is remembered in your avatar
  profile between loads.)
- **Shaders aren't translated.** Poiyomi etc. work as-is, and VRCFury-baked materials are rescued
  out of Fury's temp folder so they don't render pink.
- **Merged layers can fight CVR's locomotion** — see [the bicycle pose](#the-avatar-stands-in-a-bent-rest-pose-only-the-head-and-hands-follow-me).

## Troubleshooting

Symptoms that have actually come up. **Read `ConversionReport.md` first** — most of these name
themselves in it.

Many entries below say *reconvert on a current release*. That is the whole fix: the cause was in
the converter and is repaired, and your source avatar was never touched.

### Nothing compiles — `'ImageDownloader' does not contain a definition for 'GetImage'`

The project has the **legacy `.unitypackage` VRChat SDK** (an `Assets/VRCSDK` folder). Its global
`ImageDownloader` shadows the CCK's, and the CCK stops compiling — taking the whole editor assembly
with it.

**Install the SDK through the [Creator Companion](https://vcc.docs.vrchat.com/) or ALCOM instead.**
The collision exists between the CCK and the legacy SDK with no AvatarBridge involved.

### Nothing compiles — Poiyomi's `AbiAutoAnchor.cs` / `AbiAutoLock.cs`: `'ABI' could not be found`

Some Poiyomi versions ship CCK helper scripts inside an assembly that can never reference the
Assets-installed CCK, so they cannot compile.

**Delete both files** (`…/ThryEditor/External/Editor/`). They're optional upload conveniences;
nothing in conversion needs them. A Poiyomi update may restore them — delete again.

### The avatar stands in a bent rest pose, only the head and hands follow me

The "bicycle pose". **Reconvert on a current release.**

Merged layers are always masked off the humanoid rig now. VRChat keeps FX on its own playable layer
where it physically can't write muscles; ChilloutVR runs one controller, so an unmasked merged layer
fights locomotion for the body. Layers that animate the body on purpose are left alone.

### A bone chain hangs broken, or MagicaCloth throws in the Scene view

**Reconvert on a current release.** A chain whose bones are also driven by a **constraint** is no
longer simulated, and the report says which and why.

A constraint writes the bone every frame; a cloth solver integrates it from its own last state. Run
both and each is fed the other's output until the transform goes **NaN**, which never recovers.
VRChat tolerates it because PhysBones re-read the constraint each frame; MagicaCloth2 and DynamicBone
don't. Remove the constraint if you want the chain simulated.

### A chain moves differently in game than in Unity

Expected — **Unity can't preview cloth.** Nothing steps the solver in edit mode, and in play mode the
avatar stands still while in game it walks and head-tracks constantly. Shaking the root isn't a valid
test either: MagicaCloth2's speed limits make a chain follow rigidly once exceeded. Judge physics in
game.

### Unity crashes when you press Convert

**Update to the current release and try again.** Unity's playable-graph builder segfaults instead of
logging on three kinds of controller damage, all now repaired during conversion and counted in the
report: empty motion slots (given a placeholder clip), blend-tree parameter fields naming nothing
(renamed to inert `#` names), and controllers referencing assets that resolve to nothing (checked
before assignment).

Broken references usually come from a VRCFury or Modular Avatar bake that errored partway — build a
test copy of the source avatar, fix what errors there, then convert again.

### The console floods in Play mode — "Statemachine for layer is missing" or "Parameter type does not match"

**Reconvert on 3.5.29 or later.** Both are noise rather than damage, but can run to tens of thousands
of lines per session, and the second hides a real fault — a driver reading a parameter as the wrong
type is handed a **0** and calculates from it.

### Unity crashes when you press Play, or the avatar renders with the wrong materials there

**Reconvert on a current release.**

If it persists, check **Enter Play Mode Options** (Edit → Project Settings → Editor) — turning it off
and reopening the scene clears the stale-state class of crash immediately, and the report warns
whenever it's on.

### Converted avatars broke after updating AvatarBridge — missing controllers, pink particles

Only affects conversions made before 2.59.0, which were written inside the tool's own folder — so
deleting it to update erased them.

**Check the Recycle Bin first:** Unity trashes deleted assets rather than destroying them, and
restoring the folder relinks everything. Otherwise reconvert. Output has landed in the sibling
`Assets/AvatarBridgeOutput` since 2.59.0, where updates can't reach it.

### A hat or held item drifts off when I resize myself

**Reconvert on 3.4.7 or later**, with the avatar scaler on.

A `ParentConstraint` offset is in **metres** and never scales, so the body moved and the offset
didn't. Each offset is now handed to the hierarchy instead — a small empty parented to the source
bone, inheriting the avatar's scale. Nothing moves at default size.

Four cases are deliberately left alone, and the report names each:

| Left alone | Why |
|---|---|
| Offsets an animation drives | Zeroing one a curve is driving hands the prop to an animation that no longer matches |
| Sources inside a cloth or dynamic-bone chain | A new child of a simulated bone becomes a new particle |
| Sources outside the avatar | An offset from a world anchor is meant to be in metres |
| Unlocked constraints | Unity re-derives their offsets and writes the old one back |

### Something is bright magenta

A material or shader the avatar points at no longer exists — almost always VRCFury's temp folder,
which Fury deletes on its next build. So it appears *after* a later bake, on an avatar that converted
fine. **Convert again**; if it persists, that's worth an issue.

### A mesh renders white, washed out, or loses its eyes

Same VRCFury temp problem one level deeper: the material survived, its **textures** didn't. **Convert
again**, and report separately from magenta.

### Gestures freeze in game, or on another PC

**Reconvert on a current release.**

Not a conversion problem, and worth checking first: on Index-type controllers ChilloutVR only
registers gestures while *Skeletal Input* or *Infer Gestures from Finger Tracking* is enabled in its
settings. With both off, no avatar gestures work — stock or converted.

If you converted before 3.5.13, **delete the output folder once and convert again**: reconverting
used to stack duplicate copies of every rescued asset rather than replacing them.

### The viewpoint or voice position is nowhere near the head

**Reconvert on a current release.** The **Auto** buttons on the CVRAvatar inspector are always a safe
manual fix, and place both where the conversion aims to.

The viewpoint comes from your avatar's VRChat descriptor — the value its author placed and shipped —
rather than the CCK's *Auto*, which reads humanoid eye bones and is confidently wrong on rigs where
those aren't where the eyes are. Auto is used when the descriptor has none, and the report says which
was used and how far apart they were. An authored value is only overridden when the rig proves it
wrong: further from the head than the rig's own proportions allow, or above the eyes by more than the
avatar's own interpupillary distance.

**On a [quadruped](#quadruped--finalik-avatars) neither value is looking at your avatar** — both
markers hang off the humanoid Head bone, which is part of the hidden decoy rig. Where the avatar
relays both humanoid **eye** bones, the conversion follows those constraints to the visible head and
measures there instead. Requiring eyes is deliberate: a constraint sourced from the humanoid head
alone can be a puppet *input* rather than a face being reproduced, and one taur base put its viewpoint
at the hips that way.

Putting the avatar at the top of the scene hierarchy before converting avoids the scale cases
entirely.

### The report says VRCFury errored, or that files are missing

**"VRCFury reported N error(s) during its own build"** is *Fury's own message, quoted verbatim* — so
Fury **is** installed and it **did** run. The fault is in what it was asked to build. The usual form
is *"You're missing some files needed for this VRCFury asset"* followed by paths; the folder in the
path names the package.

The report also lists, before the bake, **"N VRCFury component(s) reference assets that aren't in this
project"** — Fury's message says which files, this says which component wants them.

**Either install the package, or delete that VRCFury component**, then convert again. Fury wraps each
feature so a failure shows a dialog and the build *continues* — which is why a bake can "succeed" with
half the avatar missing, and why this is an Error that says not to upload.

### A toggle switches on, the layer plays — and nothing changes on screen

If the report says **"animated material property(ies) don't exist on the shader they target"**, this
is **not the conversion** — the same animation does nothing in VRChat either.

Two report lines cover the other version, and they mean opposite things:

| The report says | What it means |
|---|---|
| animate paths that were **already missing in VRChat** | Not a problem. Silent there too; nothing was lost |
| **LOST** paths that existed before conversion | Real. A stripped system (GoGo, SPS) is the innocent cause — turn that strip off and check. Anything else is a bug |

Clips that switch a **constraint** on and off split the same three ways: *repointed at the Unity
constraints* (working), *drove a constraint that was never built* (check your bake — a partial
VRCFury/MA bake generates some constraint sets and not others), and *drove a constraint on an object
that is now gone* (a stripped system, or a bug).

**Locked Poiyomi/Thry shaders** bake any property not flagged animated *at lock time* into the shader
and delete it, so writing to it goes nowhere. **Fix it in Poiyomi's own material inspector** — unlock,
right-click the property, mark animated, lock again. It has to be Poiyomi's UI because that also
re-enables the shader *section* the property belongs to; a disabled section is compiled out entirely
and no flag will bring it back.

The report splits these in two: **worth fixing** (nothing has flagged it yet) and **probably not
fixable** (the material already carries the flag and the property still isn't in the shader — the
section is off, or the animation predates the installed Poiyomi). The second group is worth knowing
before you spend an evening on it.

### Other people see my avatar flickering, cycling colours or thrashing — I don't

Look for **"layer(s) may thrash on OTHER players' screens right after the avatar loads"** in the
report — it names the layer and the state.

Remote copies don't start with your parameter values; everything sits at its **serialized default**
until they replicate. A layer your own copy never moves can, at those defaults, satisfy a loop of
transitions and re-enter a state every frame. **You cannot see this or reproduce it by looking** —
your copy is correct, and your copy is what Unity previews.

Two fixes, either works:

- **Change the parameter's default** to a value that parks the layer. Better, because it also makes
  the avatar look right during the seconds before your values arrive.
- **Give the looping transition an exit time**, so it can't fire twice in one frame.

The CCK Animator Tester's **Remote view** card reproduces it locally. One cause was the conversion's
own and is fixed in 3.5.26 — if you converted earlier, reconvert.

### A toggle switches on but never back off

**Reconvert on a current release.**

VRChat's usual toggle is one state holding a clip and one holding **nothing**, relying on Write
Defaults to undo it. Converted, there's nothing in the off state to restore. Conversions now give it a
real animation — reusing your own clip where one exists, otherwise measuring the property off your
avatar as it is at conversion time.

**VRCFury toggles are repaired too, from 3.5.37.** Fury rewrites whole toggle layers into blend
trees, which moves the empty "off" half one level down out of reach of the repair above — so on a
Fury avatar the wardrobe could still be one-way while everything else went both ways. Those are now
filled as well. If your toggles stick on and you converted before 3.5.37, reconvert.

**From 3.6.0, nothing is left to Write Defaults at all.** VRChat's runtime quietly puts a
property back to its default when no animation writes it; **ChilloutVR's does not** — measured in
game, and it is why avatars that behave perfectly in VRChat came back one-way here. The layer
that owns a property now asserts its value from *every* state it can rest in, so the game is
never asked to fill a gap. Two spellings of the empty-off idiom that used to slip through are
caught as well: Fury parking a shared clip with *no curves in it* rather than nothing at all, and
an **animation library** — a layer full of states with no transitions, where authors park clips
for easy previewing — counting as animating everything it held, which refused every real toggle a
restore while the library, which can never play, restored nothing. Anything a blend tree drives
is left to the tree — a constant assertion from a plain state would fight a parameter-driven
value — and sliders, pass-through gates and muscle curves are excluded too; each of those has
been a shipped bug before. The conversion then **audits itself**: any property that could still
fall back to the runtime is named in the report as a warning, state by state, so this whole class
of failure is caught at conversion time rather than discovered in game.

Four things worth knowing:

- **Whatever is true at conversion time is what "off" means.** Set the avatar up that way first.
- **Where several layers animate one thing, only the lowest restores it** — otherwise a dress toggle
  would assert the shirt from above and it could never come off.
- **Two toggles inside one Fury tree that move the same thing get their own layers, from 3.6.2.**
  Toggles blended into a single tree *add up*, so no restore could be written for the shared part
  and both toggles stuck — a whisker style-swap over a whisker hide, or an "all clothing off"
  preset over its garments. Each is now lifted into its own layer, where the top one wins while it
  acts and each restores on its own. The one visible difference: with **both** switched on at
  once, the higher one decides the shared part, where VRChat showed an arithmetic mix of the two.
- **Only two-state toggles are filled.** Bigger layers are machines whose empty states are structural
  (a slider's `Reset`, a local/remote gate), and filling those changes how the avatar looks.

### A material swap changes only some parts

**Reconvert on 3.6.4 or later.**

A toggle that swaps several material slots at once — a full-body recolour, say — used to come
through swapping only its first slot in game: the body changed, the fur kept its old colour. The
Animation window previewed the very same clip perfectly, which is what made it maddening — the
swap only loses slots when the animator itself plays it.

The cause is an undocumented Unity behaviour: a layer wearing an avatar mask applies only the
*first* material-reference curve of its clips, and conversion used to give every merged layer a
protective mask. Material-swap layers now keep no mask — they drive no muscles, so the mask
protected nothing — and every slot of the swap lands. If you masked such a layer yourself in the
source avatar, the conversion warns instead of editing your work: clear that layer's mask in the
Animator window and reconvert.

### Gestures play the wrong pose, or a hand sits in a fist at rest

**Reconvert on 3.6.0 or later.**

Some avatars keep a *second* copy of their hand-pose layers in the FX playable — layers literally
called "Left Hand" and "Right Hand" alongside the real ones in the Gesture playable. In VRChat that
copy is harmless, because the FX layer there **cannot drive humanoid muscles at all**, so it never
touches a finger.

Converted, everything lands in one animator, where it can. The copy sits above the real hand layers
and wins — so gestures land on whatever *that* copy says. On one reported avatar the copy had no
neutral state and a fist band starting below zero, which parked the hand in a fist at rest and made
every threshold look wrong. The real layer had been correct all along.

Conversion now masks fingers off any layer above the hand-pose layers that would otherwise write
them, and names each one in the report. Layers that deliberately animate the **body** are left
alone and warned about instead — silently overruling those would be the converter second-guessing
the author.

### An emote's hand pose is wrong, or follows your gesture

The dance plays, the body is right, and the hands hold whatever gesture your controller is
reporting instead of the pose the emote wants.

This one is ChilloutVR's, not the conversion's. VRChat's tracking control can hand **fingers** over
to animation for the length of an emote; ChilloutVR's Body Control has no finger mask yet — its own
CCK carries the note *"TODO: Add FingerTracking masks when GS is ready"*. There is nothing to map
it onto until that lands.

The layer order compounds it. In VRChat the Action layer sits **above** Gesture, so an emote
outranks your hand pose anyway. In ChilloutVR emotes are grafted into `Locomotion/Emotes`, which
sits **below** the hand-pose layers — so the gesture wins.

**Workaround:** hold an open or neutral gesture while the emote plays. Everything else about the
emote — body, head, locomotion — converts and behaves normally.

Eyes and mouth are in the same boat and it matters far less: those channels stay with the avatar's
own animation and face tracking, which is usually where you want them.

### A particle effect only you can see

**Reconvert on 3.5.38 or later.**

Effects are built two ways, and only one of them travels. If the clip switches the effect's
**GameObject** on and off, everyone sees it — that's an ordinary animation every client plays. If
the emitter is left **off** in the prefab and the clip switches on the particle system's *emission
module* instead, the object turns on for other players and emits nothing. It looks perfect to you
and is invisible to everyone else.

Conversion now switches those emitters on permanently and lets the object's own on/off animation
gate the effect — the way the effects that already worked are built. Nothing plays at rest, because
the same clip that turns the object on also turns it off. The report names each one.

Animated particle modules **other than emission** are left alone and reported. If an effect looks
wrong to other players but right to you, that's the first thing to check.

### "VRCFury is installed but did not compile" — conversion refuses to start

**From 3.6.0**, conversion stops before doing anything if the project has VRCFury, Modular Avatar
or NDMF installed but Unity never compiled it. That happens when an avatar or prop
`.unitypackage` ships its own bundled copy and overwrites yours — usually leaving the folder
in place but stripped of its `package.json`, which Unity needs to load a package at all.

It stops rather than warns because the result would be **silently wrong rather than visibly
broken**: with no baker loaded, every component it owns reads as though it isn't there, so the
avatar converts "successfully" and quietly comes out missing everything that package builds.

Reinstall the named package through the VRChat Creator Companion, let Unity finish compiling,
and convert again. Nothing is changed by the refused run.

### A particle effect draws as plain coloured squares

That's Unity's **default particle material** — the one a particle system gets when nobody assigns
it one. It was already that way in your avatar; conversion copies materials across unchanged, so a
system on the default in VRChat is on the default here too.

The report names each one, because the editor gives no hint and the effect only looks wrong once
somebody sees it in game. Assign it a real material, or — if the system exists only to spawn
another one and was never meant to be seen — turn its **Renderer** off.

**If it used to have a picture and now draws as white squares, that was a bug of ours, fixed in
3.6.0 — reconvert.** VRCFury bakes generated textures as sub-assets of a single file in its temp
folder and deletes that folder on its next build. Conversion rescued the material and its shader
out of there but left the *textures* pointing in, so the pictures died with the folder while
everything else survived. It was never particle-specific; particles are just where a missing
texture is unmistakable rather than merely wrong.

### A limb-lock, sit or flight toggle does nothing, and the report mentions protected clips

Those toggles are driven by curves that switch a **constraint** on and off, and conversion normally
repoints them at the Unity constraint it built. It will not do that to an animation file that lives
outside the conversion's own output folder — those are your originals, and rewriting them would
repair the conversion by damaging the avatar in VRChat.

Avatars built with **VRCFury or Modular Avatar are unaffected**: their bake hands the converter
copies to work on. If you see this warning, convert from a baked copy of the avatar.

### Movement doesn't animate, and Airborne / Flying / Sitting / Swimming do nothing

**Reconvert on 3.5.8 or later** — enabling "Base / locomotion" can no longer cause this.

A merged `[Base]` layer lands **above** ChilloutVR's own `Locomotion/Emotes` on Override at full
weight, where it can only replace that layer, not add to it — and CVR's layer is where the movement
sliders and stance buttons are answered. `[Base]` layers are now masked off the humanoid rig; object
toggles, blendshapes, materials and parameters in them still convert.

**The animations themselves survive.** Custom walk, crouch, crawl, fall and sit clips are grafted into
ChilloutVR's own locomotion layer, matched by their **position in the movement blend trees** rather
than by name. Loop settings are matched to the slot; jump and fall grafts play once; a flight pose
lands on CVR's `LocFlying` state, since ChilloutVR flies natively and needs the pose, not the speed
machinery.

Worth knowing: **most VRChat avatars don't ship walking animations at all** — their trees reference
`proxy_*` placeholders the VRChat *client* replaces at runtime. ChilloutVR's own animation set is this
platform's equivalent, and the report says which case your avatar is. Genuine locomotion replacements
lean on runtime layer-weight control, which ChilloutVR has no equivalent for, so they can't be rescued.

### An animation flickers rapidly — often only on other players' screens

**Reconvert on 3.5.8 or later.** Unity's AnyState transitions default to "Can Transition To Self",
re-entering the destination **every frame** the conditions hold. VRChat never shows it because those
states are mostly *empty* there; conversion has to fill empty states, and a filled state restarted
every frame strobes.

The "only other people see it" shape adds networking: remote copies hold every `#` local parameter at
its default forever, so a re-entry condition your live values keep false can sit permanently true for
everyone else.

Self re-entry is now disabled on merged AnyState transitions **only where the restart carries no
meaning**. States with a real clip keep it, and so do states with an **exit-time transition out** —
there the restart resets the clock so the timed exit never fires, which is the entire mechanism
holding the state.

**Root motion is stripped from animations that travel.** VRChat moves the player by animating the
body because it allows nothing else; ChilloutVR moves the player itself, so the same baked movement
shoves the wearer around with no input. The test is whether the clip's root **ends where it started** —
a backflip's flip and a dance's sway return home and keep their curves. A clip that ends displaced has
each root curve **flattened to its starting value**, never deleted, because the same curve carries the
body's baseline height.

### An emote replays forever instead of playing once

**Reconvert on 3.5.5 or later.** Emote menus **hold** their value, and VRChat's Action graph fires on
the **rise** of a condition, once. Converted poses are re-armed from the locomotion resting state
instead, so arming on a *level* replays forever. A local ready flag now gates every arming transition,
so an emote fires once, switching straight to another plays the new one, and re-selecting replays it.
Hold-style emotes (dances, AFK poses) are unaffected.

### A menu control appears, moves, syncs — and does nothing

Check the report for that control's name. The interesting cause is **a feature living in the Action
layer**.

VRChat keeps Action at weight **0** and raises it only while an emote runs, so its waiting state can
hold a full-body clip harmlessly. ChilloutVR has no playable layers to raise, so conversions rest it
at 0 too — otherwise that waiting state asserts a stand-still pose over your locomotion.

Some avatars put a *feature* there anyway. An Action layer whose transitions wait on the avatar's
**own** parameters is now merged at **weight 1**, with its waiting state emptied and Write Defaults
off so it contributes nothing until something drives it. VRChat fades that weight in over about half a
second and ChilloutVR can't, so expect the change to **snap rather than ease**.

The feature stays **disarmed until one of its own parameters actually changes**. Conditions that are
permanently true are free inside a weight-0 layer and plenty are; copied into an always-on layer they
would fire the moment the avatar loaded.

**Full-body poses move into ChilloutVR's own locomotion layer** — the one place on this platform a
pose can both assert and let go. What moves is the **live window**: the states between the behaviour
that raises the Action weight and the one that fades it, read from VRChat's own behaviours. The
original layer stays merged at weight 0 so its parameter drivers keep firing.

A moved pose keeps its own **height** and its **orientation**, both moved into the bones rather than
the root, since ChilloutVR discards root motion. Travel across the floor still goes. The turn is
[desktop-only](#transforming-avatars-desktop-only-turn).

### Two near-identical menu controls, and only one works

**Reconvert on a current release.** A synced parameter with no menu control gets one created — the
wrong guess when the avatar *writes* that parameter itself from a driver, leaving an invented control
next to the one that really works. Invented controls are now withdrawn once a driver is found writing
that parameter. Anything the author put in the menu stays.

### Your eyes stay open, start closed, or lose a pupil

**Reconvert on a current release.**

An avatar that blinks from its own animator can't keep that system: its "eyes open" states are *empty*
in VRChat, and empty states can't survive conversion. It exists only because **VRChat has no built-in
blink and ChilloutVR does** — so the conversion finds the layer whose only job is blinking, removes
it, and wires ChilloutVR's native Eye Blink.

If no layer can be safely identified nothing is removed and native blink stays off, but **the shape
slots are filled in anyway** — so if the eyes don't blink in game the fix is one tick of *Use Blink
Blendshapes*. Only tick it if they **don't** blink: the client writes its blink weight every frame
after the animator, so with both systems running any expression using that shape stops closing the
eyes. Where the removed layer's shape is also used by surviving expressions, the native blink is moved
to a free shape pair instead.

### An effect draws in one eye only in VR

Expected, and not caused by converting — see
[shaders that only draw into one eye](#shaders-that-only-draw-into-one-eye).

### There's no "Convert a VRChat avatar" tab

The VRChat Avatars SDK isn't installed, so only [Setup mode](#setup-mode) is offered.

### Uploading fails with "Failed to generate new object ID"

Not AvatarBridge — that's the CCK. ChilloutVR's API refused a content slot, usually the account's
private upload limit. The real message is in `Player.log` or the console just above the exception.

### One extra recompile after importing

Normal. That's AvatarBridge registering its scripting defines.

## Reporting a bug

Hit **Report an issue** in the AvatarBridge window — it opens a pre-filled GitHub issue with your
versions and detected packages already in it.

Two things make a report solvable immediately:

1. **Attach `ConversionReport.md` and `Diagnostics.md`** from `Assets/AvatarBridgeOutput/<avatar>/`.
   Nearly every bug fixed so far was diagnosed from the report; `Diagnostics.md` carries the
   measurements behind it — package versions, every setting used, the rig's shape, where the head
   and eye bones actually sit against the viewpoint, the constraint census, and which asset
   references resolve to nothing. It's all facts and no advice, so it diffs cleanly between two
   conversions. Nothing in either file leaves your machine unless you send it.
2. **Attach the right log:**

   | Symptom | Log |
   |---|---|
   | Conversion errors, or wrong result in Unity | Unity console text or `Editor.log` |
   | Avatar misbehaves or won't load **in ChilloutVR** | `%USERPROFILE%\AppData\LocalLow\ChilloutVR\ChilloutVR\Player.log` |

   A clean Unity log says nothing about an in-game failure — that's exactly how the "Error robot"
   bug was found.

Please re-run on the [latest release](https://github.com/MrTactical/AvatarBridge/releases/latest)
first. Logs contain your project's file paths (and CVR logs your display name) — skim and redact if
you'd rather not post them.

**Quick questions** can go to **`mrtactical`** on Discord. Anything reproducible is better as a
GitHub issue — those get tracked, linked to a fix and closed with a release.

## Credits

- Gesture tables, CVR core parameters and several conversion patterns were studied from
  [vrc3cvr](https://github.com/Narazaka/vrc3cvr) (MIT), maintained by **Narazaka**, and from the
  [original by imagitama](https://github.com/imagitama/vrc3cvr) (MIT, archived 2023) it forks.
  AvatarBridge is MIT too, deliberately: anything here that's useful to vrc3cvr is theirs to take.
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
