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
| PhysBones → DynamicBone | ✅ built in | via the external [PhysBone-to-DynamicBone](https://github.com/FACS01-01/PhysBone-to-DynamicBone) |
| PhysBones → **MagicaCloth2**, feel derived from both solvers' decompiled source | ✅ | — |
| **Modular Avatar** | ✅ baked automatically | ✅ via its own component + manual bake |
| **VRCFury** (toggles, linked clothing, merged armatures survive) | ✅ baked automatically | manual |
| VRCFury's sync workarounds removed instead of carried across broken | ✅ | — |
| Contacts | **ChilloutVR's own contact components** — real proximity, tags verbatim, no sync bits spent ([experimental](#native-contacts)) | emulated with `CVRPointer` + trigger, which fire on collision rather than on proximity |
| Stereo shaders patched so effects stop drawing into one eye | ✅ | — |
| Gaze limits *measured off your avatar's own poses*; the viewpoint your avatar already shipped with | ✅ | — |
| Constraints that drive another transform (Avatar Limb Scaling et al.) | ✅ | — |
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
- **Toggles that go both ways** — VRChat's standard toggle leaves its "off" state empty and lets
  Write Defaults undo the change. Nothing carries that rule across, so those toggles used to switch
  on and stay on. The off direction is now [real animation](#a-toggle-switches-on-but-never-back-off),
  reusing your avatar's own clip where it has one.
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
  Animator Tester*: gestures, stances, visemes, emotes, face tracking and the avatar's whole
  Advanced Settings menu, plus a live Animator-layers readout. VRChat's Gesture Manager can't do
  this — it needs the VRC descriptor, which conversion removes.
  <details><summary>What it drives, and how faithfully</summary>

  Locomotion is offered as the **exclusive stances the game can actually produce** — standing,
  crouching, prone, airborne, flying, sitting, swimming — with Upright coupled to stance the way VR
  height is. Menu controls are coerced by declared type exactly like the client, and the card
  follows the controller on the avatar's Animator: it refreshes when the controller or its
  parameter list changes, and greys any entry whose parameter the controller doesn't declare,
  because driving those would do nothing in game either.

  **Visemes and blink** are held on the face mesh **every frame, after the animator** — the same
  place and order ChilloutVR writes them (3.4.32). Before that, each slider wrote once, and on any
  avatar whose animator also touches the same blendshape the very next animator evaluation erased
  it — the blink slider "did nothing" on avatars whose blink was wired perfectly. Holding the value
  also reproduces the game's conflicts honestly: an animation fighting the blink loses here exactly
  as it will in game.

  The **Face tracking** section drives every eye and Unified Expressions parameter the controller
  declares, grouped by region, with each slider's range read from the rig's own blend trees — so
  bipolar shapes (JawX, SmileFrown, the tongue axes) get their full −1…1 travel instead of half.
  On an avatar converted to **native** `CVRFaceTracking` there are no such parameters — the client
  writes blendshapes straight from the headset, with no animator in the loop — so the section shows
  a slider per **mapped blendshape** instead (3.1.0), writing the mesh the same way the client will.
  Before that it showed a single lone toggle and looked broken while being perfectly correct.

  The **Remote view** card (3.5.1) snaps every `#` local parameter to its default — the value it
  holds forever on other players' clients, which never receive local parameters or parameter
  streams. A layer that starts cycling or lands in a different state after pressing it is doing
  exactly that in game for everyone but the wearer — the cause of "an animation loops rapidly for
  others but looks fine to me".

  The **Animator layers** readout is pinned to the bottom of the window so it stays visible while
  you drive the controls above it, and shows every layer's weight, avatar mask and currently
  playing clips — the same view ChilloutVR's in-game CCK Debugger gives, plus the mask column it
  can't show. Any layer sitting above the hand-pose layers that could overwrite your gestures is
  marked.
  </details>
- **Animations that can't possibly work get named** — a locked Poiyomi shader silently deletes any
  property that wasn't flagged animated, so the toggle plays perfectly and changes nothing, in
  VRChat as much as here. The report [lists every one](#a-toggle-switches-on-the-layer-plays--and-nothing-changes-on-screen)
  with the renderer it belongs to, so the fix is a minute in Poiyomi's inspector instead of a day
  in the animator.
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
2. **Check the options** — physics target, face tracking mode, height scaler. Defaults suit most
   avatars.
3. **Convert.** Output lands in `Assets/AvatarBridgeOutput/<avatar>/` — a sibling of the tool's
   folder, so deleting `Assets/AvatarBridge` to update it never touches your conversions. Read
   the report, then test in game.

## What gets converted

| VRChat | ChilloutVR | Notes |
|---|---|---|
| Avatar descriptor | `CVRAvatar` | visemes, blink, eye look (gaze limits measured from the poses); the **viewpoint your author already placed in VRChat**, copied across unchanged, with the CCK's Auto placement (eye-bone midpoint) as the fallback; voice at the jaw bone, else measured. On a [quadruped decoy rig](#the-viewpoint-or-voice-position-is-nowhere-near-the-head) both are re-measured on the bones you can actually see |
| Expression parameters + menus | Advanced Avatar Settings | named after the menu control's label |
| Clothing / prop toggles | one `Toggle <name>` layer each | pulled out of VRCFury's merged blend trees; the "off" direction becomes [real animation](#a-toggle-switches-on-but-never-back-off) instead of relying on Write Defaults |
| Parameter types | real `bool` / `int` / `float` | see [below](#parameter-types) |
| Gestures | float threshold bands, the CCK's own idiom | analog fist blends in by trigger pressure, like VRChat |
| Animation clips + masks | copied into `RehomedAssets`, controller repointed | the output folder alone is the whole conversion |
| Skinned mesh bounds | normalized — centre 0, extents ≥ the avatar's height | stops meshes vanishing at screen edges; larger authored boxes are kept |
| PhysBones + colliders | **MagicaCloth2** or DynamicBone | see [below](#physbones--magicacloth2) |
| Contacts | native contacts, or `CVRPointer` / trigger | see [below](#native-contacts) |
| VRC Constraints | Unity constraints | including *Target Transform* — see [below](#constraints-that-drive-another-object) |
| VRCFury parameter compressor | removed | a VRChat sync workaround that breaks sync here |
| FinalIK components | kept as-is | ⚠️ CVR deletes some — see [quadrupeds](#quadruped--finalik-avatars) |
| VRC tracking / locomotion control | `BodyControl` | hands a limb from IK over to animation |
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

**Rotation offsets are measured, not copied** (3.4.28). Copying VRChat's `RotationOffset` field
trusts that both engines apply it in the same space, and that held until a constraint crossed two
very differently oriented bones — a car avatar's windshield pupils mirror its face eye bones through
rotation constraints, and the copied offset left them rotated 77° edge-on, invisible. VRC constraints
evaluate in the editor, so at conversion time the scene pose *is* VRChat's solver output; for an
active, full-weight, single-source rotation constraint the offset is now derived from that pose
directly, with no cross-engine assumption. Multi-source or inactive constraints still copy the field.

**An unfollowable local-space constraint yields to its animation** (3.4.29). VRChat constraints can
solve in the source's *local* space; Unity's only solve in world space, and when the constrained bone
can't be re-parented to bridge that (it skins the mesh), the converted constraint is wrong whenever
the two parent chains move apart. If a clip in the controller *also* poses that bone, the wrong
constraint overrides the right animation — constraints evaluate after animators. So in exactly that
case the constraint is now **disabled** and the animation stands. The avatar that forced the choice:
windshield pupils mirroring the face eye bones folded 77° edge-on the moment the body folded into a
car, while the car animation had them keyed perfectly all along. What's lost is only the live follow
— the pupils sit where the author posed them instead of tracking eye movement. Bones nothing
animates keep the world-space follow, which is the behaviour the walking quadruped shipped with.

**Solving in local space is repaired where it can be.** VRChat's constraints can read the source's
**local** rotation instead of its world one, and that's the default in the SDK's own inspector.
Unity's constraints only ever solve in world space, and ChilloutVR ships no equivalent — its
constraint types are Unity's own.

Most of the time this costs nothing: where the constrained object and its source hang off the same
parent, that parent's rotation appears on both sides and cancels, so the two spaces agree exactly.
It matters when the source sits in a **different chain** — and there the parents can be *made* to
agree, by moving the constrained bone under the source's own parent. That isn't an approximation:
it turns the one constraint Unity can't express into one it can, and it cascades, because once a
chain's root pair matches every pair below it matches too. This is what makes
[quadrupeds](#quadruped--finalik-avatars) work.

Moving a bone is only safe when nothing depends on where it *is*, so it happens only when the relay
is rotation-only, **no mesh skins to the bone**, **no animation addresses it** (curves are matched
by path, and a moved bone would silently stop being animated), and **nothing involved is mirrored**
— a negative scale flips handedness, which no re-parenting can carry across. Every move is named in
the report, and anything failing a check is left alone and reported instead.

⚠️ **A mirrored parent is lossy whatever happens.** VRChat's solver corrects a constraint's result
when the parent's scale has a negative axis; Unity's constraints don't, and ChilloutVR ships no type
that does. Constraints under such a parent land reflected, and the report names every one.

## PhysBones → MagicaCloth2

**Structure transfers exactly:** which bone the chain hangs from, which colliders it collides with,
which transforms to leave out, whether it started enabled.

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

- **`Is Animated` sets Animation Pose Ratio to 1** (3.4.19). MagicaCloth2 settles a chain back to
  the pose the avatar was *built* in; a PhysBone marked `Is Animated` is one an animation moves. Left
  at the default the two fight and the cloth wins — a chest or ear slider that scales its own bones
  simply stops working, and the avatar quietly has a different shape from the original at identical
  menu settings. The source flag decides this, so it's applied rather than reported.

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
| **Derive physics from the PhysBone** | on | Converts pull, spring and stiffness into damping and angle restoration, replacing the preset's feel. Turn it off to get the preset's feel back |
| **Cap particle radius to bone spacing** | on | A safety rail: MagicaCloth2's radius is the particle *size*, and particles wider than the gap between bones shove each other apart |
| **Convert toe PhysBones** | off | Toes are left out of the simulation entirely — both chains *rooted* at them and toe branches found part-way down a longer chain (a leg or skirt chain that runs through the feet), for MagicaCloth2 and DynamicBone alike. Simulated toes splay and swing while IK plants the foot, which reads as broken feet rather than as physics. Turn on if the toe physics are deliberate |
| **Transfer angle limits** | off | ⚠️ Genuinely avatar-dependent — shakes some chains, best result the tool gives on others. Worth trying if physics feels loose |
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

**The failure this causes is silent and looks like something else** (3.4.1). On one balloon avatar
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

Receivers now listen for both (3.4.0), so a stranger's hand or finger sets them off:

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
developer, and this is confirmed in game: every client simulates every avatar's contacts itself.

> ⚠️ Experimental — this talks to a component internal to the game, not the CCK, so any ChilloutVR
> update can break it, possibly for good. Treat it as a bonus, not something the avatar depends
> on. (The window shows the same note while the option is on.)

AvatarBridge's generated declarations are verified **field-for-field against the decompiled game
client** — the only layout that matters, since the client is what reads the uploaded avatar. The
author's public repository is a diverged work-in-progress: **don't import it into a conversion
project** while it disagrees with the game (its current layout drops fields the shipped client
still reads, including the content-type flag that lets other players' hands trigger receivers).
Its MIT-licensed custom inspector is adapted into the generated declarations, so contact
components get proper foldouts and per-receiver-type help text in the editor.

**How it works without CCK support.** An uploaded asset bundle carries no script assemblies — only
a record of each component's assembly, namespace and class name, which every player's client
resolves against its own assemblies at load. The contact implementation already ships *inside the
ChilloutVR client*; it is the CCK that provides no way to author it. So AvatarBridge generates
matching declarations into `AvatarBridge/Runtime` on import — same identity, same field layout,
verified against the decompiled client — and the game's own implementation is what runs. Nothing
here reimplements contacts in Unity, and nothing is bundled into the avatar; the declarations are
removed automatically if a future CCK ships the real thing.

That is also why sync works out differently from the `CVRPointer` + trigger emulation other
converters use. Emulation needs the network to agree on *collision events*; here every client
already simulates every avatar's contacts locally, so **detection** costs no sync at all. Whether
the parameter a receiver drives replicates its *value* is that parameter's own sync declaration —
unchanged, and exactly as everywhere else.

> ✅ **Confirmed in a live ChilloutVR instance:** validation clean, avatar uploaded, contacts
> triggered by other players, and CVR's own runtime gizmos drawing the components — proof the
> game's real implementation is running against declarations generated here.
>
> **Still off by default** — chiefly because of the note above, beyond just breadth of testing.
> Turn it on deliberately and test in game; the conversion falls back to the legacy path by
> itself if anything is wrong.

> ⚠️ **If a conversion leaves broken `Contact_*` components behind, delete them and reopen the scene
> before converting again.** Unity manufactures a placeholder script for a dangling component
> reference, and that placeholder then captures every *new* component of the same class — one bad
> conversion quietly poisons the next. AvatarBridge detects this and refuses rather than producing
> another broken avatar; *Tools → Avatar Bridge → Diagnose native contacts* shows what Unity is
> holding.

## Shaders that only draw into one eye

**ChilloutVR renders single-pass instanced; VRChat renders double-wide single-pass.** Both SDKs
force their own mode unconditionally.

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

- **Your original shader and material are never modified** — both are copied, so other avatars
  sharing them are unaffected. (Those shaders usually aren't yours.)
- **A copy that doesn't compile is thrown away**, so the worst case is a line in the report rather
  than wrong pixels.
- **Screen-grab effects are fixed too.** A `GrabPass` texture is a texture *array* under
  instancing — one slice per eye — so lens, refraction and heat-haze shaders that read it with
  `tex2D` show one eye the other eye's view. Those reads are rewritten to the screen-space
  macros, same as `_CameraDepthTexture`.
- **Shaders needing more than the macros get a recipe.** Some fixes can't be derived — a
  `GrabPass` is a per-eye texture *array* under instancing, so a lens or refraction shader reading
  it with `tex2D` shows one eye the other eye's view no matter how many macros it has. Those are
  written by hand once and kept in AvatarBridge's recipe list, then applied to *your* copy on
  every later conversion. Each recipe is pinned to a fingerprint of the exact shader version it
  was written for: an updated or edited shader doesn't match and is refused rather than guessed
  at. Nothing is redistributed — the recipe is the edit, not the shader, and your original file is
  never touched. Hit one that has no recipe yet? Open an issue and it can be added for everyone.
- **Not everything can be patched.** Surface shaders have no vertex stage to edit, and structs in
  a shared include can't always be edited from one file. Those are listed for hand-fixing instead.
- **Every shader gets a verdict in the report** — patched, couldn't be patched, or *already
  speaks single-pass instanced and was left untouched*. Locked and generated shaders (Poiyomi
  lock-in and SPS live at `Hidden/Locked/…`) are read and checked like any other; modern Poiyomi
  declares the full macro set, so these normally land in the already-correct list rather than
  needing anything.
- **There's nothing to undo.** The macros are mode-agnostic — real instancing code under CVR,
  nothing under VRChat or on desktop. The patched copy stays correct everywhere.

> ✅ **Confirmed in game:** a soft-particle effect the CCK flagged as non-SPI was patched, validated,
> uploaded, and renders correctly **in both eyes**.
>
> **Still off by default**, on one avatar's evidence. Compilation is all that can be checked
> automatically — whether it *looks* right is a judgement no editor script can make, so turn it on
> deliberately and **check the effect in both eyes**.

> Passing the CCK's check isn't the same as being correct. It looks for four macros; a shader can
> have all four and still be broken, because both the depth texture and any `GrabPass` are texture
> *arrays* under instancing — a soft-particle shader reading `_CameraDepthTexture` through
> `sampler2D`/`tex2Dproj`, or a glass/refraction shader reading its grab texture through `tex2D`,
> takes the wrong slice however many macros are present. AvatarBridge rewrites both.

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

Two buttons on the report:

- **Fill CCK description** — types it into the Content Manager's Description box. Open the CCK
  Control Panel on the **Builder** tab with your avatar selected first. It won't touch the box if
  you've already written something there.
- **Copy description** — puts it on the clipboard to place yourself.

Either way it's saved as `Description.txt` beside the report.

**Every line is counted from the finished avatar**, so no two are alike — and each claim is checked
against what was *built*, not what you asked for. Face tracking is only mentioned if the component
is really there; the height slider only if its control reached the menu. This text goes into a
public listing under your name, so a line it can't verify is a line it doesn't print.

It's also built to fit: ChilloutVR's box holds **256 characters**, and ~90 of those are left free
for your own words. The generated part is meant to be the footer of your description, not the whole
of it.

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

## Known limitations

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

**The viewpoint and voice get rescued when they land off the body** (3.3.0). Rather than trying to
recognise a fourth rig design, this asks the one question no skeleton can lie about: is the marker
further from *every bone that deforms mesh* than this rig's own proportions allow? If so it isn't on
the avatar, however well it measured, and it's re-placed from the eye markers that **are** on the
body. A marker already on the body is never touched, so avatars that are correct — including the
other two quadrupeds — can't be disturbed. Naming only nominates candidates; geometry decides.

**A constraint-relay quadruped walks in game** — confirmed by wearing one, which is the only test
that counts. Getting there took several separate fixes, and the walls below are still standing.

**Turn off "Base / locomotion" for a quadruped.** It's off by default. VRChat's stock locomotion
layer and ChilloutVR's own both drive the decoy biped, and with both present the avatar holds its
rest pose. With it off, ChilloutVR drives the decoy and the relays carry it onto the animal.

**Most quadrupeds are a hidden humanoid rig.** The model carries a second, invisible biped skeleton
— bones named like `HipsHuman`, `thighHuman.L`, `HeadHuman` — and Unity's humanoid map points only
at *those*. Nothing the engine solves ever touches a bone that deforms the mesh. Constraints then
relay that decoy onto the real skeleton, bone by bone: the torso rides the biped hips, the neck
chain *is* the biped spine, the front legs are the biped's legs. It's an elegant trick and it needs
no FinalIK at all, so ChilloutVR has nothing to delete.

Most of that converts. On the avatar this was worked out from, 51 of 59 relays needed nothing at
all. **Two things break the rest, and the report names both:**

- **Relays that solve in [local space](#constraints-that-drive-another-object)** — typically the
  hind legs copying the humanoid legs' articulation, so one walk cycle moves four legs. Where the
  copied bone can be moved under the same parent as the bone it copies, that is repaired exactly;
  where it can't, it's reported.
- **Mirrored bones.** A hind rig is usually the front rig *mirrored* — which is why its relays
  cross left to right — and a mirrored bone carries a negative scale. VRChat's solver corrects a
  constraint result for that; **Unity's constraints have no such step and ChilloutVR ships no type
  that does**, so those relays land reflected. Nothing on this side can fix it: Unity's constraint
  computes and writes its own rotation with no hook in between. Un-mirroring the bones and
  re-rigging is the only cure, and that's a job for your 3D package.
- **PhysBones on a relayed bone (2.91.0, widened in 2.97.0).** A constraint writes that bone every frame; a cloth
  solver integrates it from its own last state. Together they feed each other until the transform
  goes **NaN**, and the chain hangs broken with nothing to see in the animator. VRChat survives it
  because PhysBones re-read the constraint each frame; MagicaCloth2 and DynamicBone don't. Those
  chains are skipped now and listed in the report. **Unity's own constraints count too** (2.97.0) —
  the loop is engine-level and doesn't care which component writes the rotation. 2.91.0 only looked
  for VRChat's, so a quadruped base built entirely on Unity constraints went straight past it with a
  tail cloth simulating three constraint-driven bones.
- **Both markers on the decoy (2.92.0).** ChilloutVR parents the viewpoint and voice position to the
  humanoid Head bone, which on these rigs is part of the decoy — so the camera ends up inside the
  animal's skull. Both are measured on the relayed bones instead; see
  [the viewpoint troubleshooting](#the-viewpoint-or-voice-position-is-nowhere-near-the-head).
- **Toggles that switch a constraint on and off (2.94.0).** This one isn't quadruped-specific, it
  just bites hardest here. An animation curve carries the component **type** and the **serialized
  property name**, and conversion changes both — a clip still saying `VRCParentConstraint.IsActive`
  plays as silence, with nothing to see in the animator. That's how limb locks, sit/lay-down/loaf
  poses and flight modes work on any constraint-driven avatar: release the relay so an animation can
  take the bones over. Curves are repointed at the Unity constraint now
  (`IsActive` → `m_Active`, `GlobalWeight` → `m_Weight`, `Locked` → `m_IsLocked`). **`FreezeToWorld`
  has no Unity or ChilloutVR equivalent** and is dropped — a toggle relying on it will change the
  constraint but won't pin anything in world space.
- **First-person head hiding aimed at the decoy (2.93.0).** ChilloutVR hides your own head by adding
  an `FPRExclusion` to the humanoid Head bone. That bone skins nothing here, so nothing was hidden
  and the view filled with the inside of the animal's head. One is added to the head you can see
  instead — your camera only; everyone else sees the whole avatar. Delete it if you'd rather not.

⚠️ **A mirrored hind rig still doesn't work**, and that's the common build — the front half moves,
the back half lands reflected. The report names the exact bones. Both quadrupeds dissected here that
use VRC constraints hit it in the same place, and it looks the same on each: the hind limbs are the
humanoid legs relayed *in local space* and *mirrored*, so they fail both walls at once —

```
LOCAL_INVERSE_CNST_UpperLeg_L  ←  HOOMAN_UpperLeg_L      (local space, mirrored parent)
LOCAL_INVERSE_CNST_LowerLeg_L  ←  HOOMAN_LowerLeg_L
LOCAL_INVERSE_CNST_Foot_L      ←  HOOMAN_Foot_L
```

If your quad has a **"biped" mode**, try it — it usually stops using the mirrored hind rig entirely
and is often the configuration that converts cleanly.

**Honest summary: this is limited support, not full support.** One rig style walks; one converts
almost perfectly; one mostly doesn't. Nothing here is a silent failure — the report and
`Diagnostics.md` name which family you have, which bones fail, and why — but a quadruped still needs
testing in game before you trust it, and some will need author-side changes no converter can make.

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

- **Action-layer emotes.** Only **Gesture** and **FX** convert by default — Base, Additive and
  Action are off, because CVR drives locomotion and emotes itself. You can tick Action on, but its
  states rely on VRChat's emote flow and may be unreachable. When it is ticked, the layer is merged
  at **weight 0 — the weight VRChat itself gives it.** VRChat raises the Action playable layer only
  while an emote plays, which is why its idle state can hold a full-body clip with Write Defaults on
  and harm nothing; ChilloutVR has no playable layers, so at weight 1 that idle state would hold
  your whole body in its rest pose above locomotion and walking would stop working entirely. Raise
  the weight yourself in the Animator window if you want the layer live. **The one exception is a
  kept GoGo Loco** — GoGo drives Action itself, so with *Remove GoGo Loco* unticked the layer is
  merged live at weight 1 instead.
- **Constant contact receivers** reset to 0 when *any* pointer exits — CVR triggers don't count
  occupants.
- **Stacked PhysBones** (several chains on one bone that VRChat toggles between) all convert, but
  only one is left driving the chain — two solvers on the same bones jitter rather than blend.
  Nothing is deleted, so switching variant is one checkbox; the report names the one kept.
- **Toggled physics follows its toggle.** Hair swaps and outfit toggles that activated the
  original PhysBone's object (or animated the component on/off) are re-wired to switch the
  generated MagicaCloth/DynamicBone too — a chain belonging to a style that was inactive at
  conversion time wakes up when its style does. Only *activations* are mirrored: an add-on style
  grafted onto another style's simulated bones must not have that chain switched off with the
  base style's mesh, so a hidden style's cloth may keep simulating (invisible, harmless). The
  report counts the re-wired curves.
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

### Nothing compiles — `'ImageDownloader' does not contain a definition for 'GetImage'`

The project has the **legacy `.unitypackage` VRChat SDK** installed (an `Assets/VRCSDK` folder)
instead of the Creator Companion / VPM one. The old SDK ships an `ImageDownloader` class in the
global namespace; C# resolves names through enclosing namespaces — global included — *before*
`using` directives, so it shadows the CCK's own `ImageDownloader` and the CCK stops compiling.
That takes the whole editor assembly down, AvatarBridge included, before any of it runs.

**Install the SDK through the [Creator Companion](https://vcc.docs.vrchat.com/) (or ALCOM)
instead** — the VPM packages keep VRChat's types in their own assemblies, where they can't shadow
anything. This collision exists between the CCK and the legacy SDK with no AvatarBridge in the
project at all, and the legacy SDK is deprecated by VRChat anyway.

### Nothing compiles — Poiyomi's `AbiAutoAnchor.cs` / `AbiAutoLock.cs`: `'ABI' could not be found`

Some Poiyomi versions ship ChilloutVR helper scripts that reference the CCK **directly**, inside
Poiyomi's own `ThryExternal` assembly. The CCK sets `CVR_CCK_EXISTS` project-wide, which
activates those scripts — but an assembly-definition assembly can never reference
`Assembly-CSharp`, where the Assets-installed CCK's types live, so they cannot compile no matter
what you do. (Other Poiyomi versions use reflection there and are fine.)

**Fix: delete the two files** — `…/ThryEditor/External/Editor/AbiAutoAnchor.cs` and
`AbiAutoLock.cs`. They are optional CCK upload conveniences (auto-anchor override, auto-lock on
upload); nothing in conversion or the CCK's own upload needs them. A Poiyomi update may bring
them back — delete again, or update to a version whose ABI scripts start with `using System;`
(reflection-based) instead of `using ABI…`.

### The avatar stands in a bent rest pose, only the head and hands follow me

The "bicycle pose". **Reconvert on a current version** — since 2.62.0 merged layers are always
masked off the humanoid rig (it used to be an Advanced option, confirmed in game and now
mandatory).

VRChat keeps FX on its own playable layer, so an FX layer there physically can't write humanoid
muscles. ChilloutVR runs one controller, so nothing stops a merged layer doing exactly that and
fighting locomotion for the body every frame. The masking restores VRChat's separation; layers
that animate the body on purpose are left alone, and object toggles, blendshapes and material
animation are untouched. If you see this pose on a 2.62.0+ conversion, report it — the report
names every layer that could write muscles.

### A bone chain hangs broken, or MagicaCloth throws in the Scene view

**Reconvert on 2.91.0 or later.** A chain whose bones are also driven by a **constraint** is no
longer simulated, and the report says which and why.

A constraint writes its bone's rotation every frame from somewhere else; a cloth solver integrates
that bone from its own previous state. Run both on one bone and each is fed the other's output —
the integration diverges, and within seconds the transform is **NaN**. NaN never recovers and
spreads down the hierarchy, so the chain hangs in a pose nothing explains, identically at rest, in
play mode and in game. MagicaCloth's own Scene-view gizmo can throw a `NullReferenceException` from
inside Burst while sorting those points, which is the same problem seen from a different angle.

VRChat tolerates the overlap because PhysBones re-read the constraint's result each frame.
MagicaCloth2 and DynamicBone don't, and nothing here can reorder them. The constraint fully
determines that bone's rotation anyway, so the physics had nothing to add — remove the constraint if
you want the chain simulated instead.

### A chain moves differently in game than in Unity

Expected — **Unity can't preview cloth.** Nothing steps the solver in edit mode, and in play mode
the avatar is standing still while in game it walks, turns and head-tracks constantly. Shaking the
root is not a valid test: MagicaCloth2's speed limits make a chain follow rigidly the moment they're
exceeded, so a fast shake looks still whatever the settings say. Judge physics in game.

### Unity crashes when you press Convert

**Fixed in 3.4.2 — update and try again.**

The hazard itself is repaired, so it's gone from every direction: converting, selecting the avatar,
entering play mode, **and uploading**. Unity works out a state's duration and a blend tree's blend
while it builds the playable graph, so **any motion slot with nothing in it** — an empty animator
state, or a blend tree child whose asset is gone — makes that builder walk into a hole and segfault.
The CCK's uploader instantiates your avatar to build it, so an avatar in that state couldn't be
uploaded at all.

Every empty slot now gets a genuinely empty clip. Nothing is lost — the slot animated nothing — and
blend thresholds all stay where the author put them. The report says how many, and how many were
states rather than blend tree slots, so you can still chase down why those motions never arrived.

*3.3.4 fixed only the blend tree half of this and attached its filler clip before the controller was
an asset, so on an avatar whose empty slots were plain states it silently did nothing at all.*

**A controller referencing assets that resolve to nothing makes Unity's Mecanim graph builder
segfault**, and *assigning* such a controller to an `Animator` is enough to trigger it — the setter
calls `Animator::Rebind`, which builds the whole graph regardless of whether the component is
enabled. That last part is what took four attempts to get right: disabling the Animator, deferring
the assignment and unlinking afterwards all left the assignment itself in place, so each fix only
moved where it died.

3.3.3 checks the controller **before** assigning it and doesn't assign one that would crash.
**ChilloutVR is unaffected**: the CVRAvatar still carries the base controller and the overrides,
which is what the client reads on load. The broken references remain in the controller though, so
fix them and convert again before uploading — see the unresolvable-asset error for where they came
from, usually a VRCFury or Modular Avatar bake that errored partway.

Unity hard-crashes (not an error — the editor vanishes), most often when converting the *same*
avatar a second time or when clicking the converted avatar afterwards. The dump always lands in the
same place: `GenerateGraph` → `SetStateMachineInInitialState` → `DoBlendTreeEvaluation`.

**If your avatar's controller has no broken references, none of this applies** — the Animator is
wired up exactly as before.

**A blend tree naming a parameter that doesn't exist is the same crash from a different direction**
(fixed in 3.4.11). Unity binds *every* blend tree parameter field when it builds a graph — including
the ones a Direct tree never reads and the Y axis a 1D tree ignores — and resolves each to an index
in the parameter table. A name that isn't there resolves to nothing, and the read happens inside
`DoBlendTreeEvaluation`, so the editor dies instead of logging. `Blend`, `Value` and `Smooth Amount`
are Unity's own defaults left behind on trees that stopped using them, so they arrive on plenty of
avatars through no fault of yours; one conversion had six, including **a field that was blank**.

Every such field is now renamed to a single `#`-prefixed name, so none of them is blank and they all
agree. They are deliberately **not** declared as parameters — that was tried in 3.4.12 and brought
the crash straight back, measured in both directions on a reproducible case. The blank one is almost
certainly what mattered: Unity resolves a missing *name* to an index of -1 and reads 0, while a blank
name goes somewhere else entirely. Nothing changes about how the avatar behaves — those fields were
being read as garbage or not at all.

### Unity crashes when you press Play, or the avatar renders with the wrong materials there

**Reconvert on 3.4.14 or later.** Reconverting used to replace the saved controller by copying raw
bytes over the file and force-reimporting it — which keeps the GUID but **destroys the native object
and every sub-asset**, leaving everything that still held them (the Animator window, tester tools,
anything alive across a play session with domain reload off) holding corpses. With "Enter Play Mode
Options" on, nothing between conversions throws that stale state away, and pressing Play re-awakes
Animators against it: `Assertion failed: 'MecanimDataWasBuilt()'`, then a SIGSEGV inside
`GenerateGraph`. A hand-edited controller never crashes this way because hand-editing *mutates the
existing object* — and from 3.4.14, so does reconverting. Same file, same GUID, same native object;
Unity rebuilds its animation data the ordinary way. The `AnimatorStateMachine has been destroyed`
console spam after reconverting goes away with it.

*The rest of this section describes the earlier symptoms and remains true of older versions.*

**Reconvert on 3.4.9 or later.** If you can't yet, turning off Edit → Project Settings → Editor →
"Enter Play Mode Settings" and reopening the scene clears it immediately, with no reconversion.

The symptoms, all at once on the avatar this was found on:

- `Assertion failed on expression: 'mem->m_ConstantClipValueCount >= 0 && ...'`, repeated;
- menu controls **swapped places with each other** — sliders on toggles, a dropdown under the wrong
  name;
- white skin and flat clothes, on an avatar that looked correct in the scene a second earlier;
- and, on the next Play, the editor dying with `Assertion failed on expression:
  'MecanimDataWasBuilt()'` and a SIGSEGV inside `mecanim::statemachine::EvaluateState`.

**Two things had to line up.** "Enter Play Mode Options" skips the scene and/or domain reload, so
pressing Play *restores a backup* of the scene rather than reloading it and rebinds every Animator
against state carried over from edit mode. And, up to 3.4.8, the clip this tool put into empty
animator states had **no curves in it at all**. Mecanim sizes the array it reads and writes bindings
through from the curve count, so a curve-less clip is exactly what it asserts about — and with 66
states sharing one, our own output was what made that editor option toxic.

From 3.4.9 the placeholder animates one inert value on a dedicated `AvatarBridge_EmptySlot` object
added to the avatar. It changes nothing on any frame, it exists purely so the clip has a curve to
count, and any curve-less clip the avatar arrived with is swapped for it too. The conversion no
longer depends on that editor setting either way, which is the point — it's a setting people turn on
for speed, and a converter shouldn't care.

The report still names the setting when it's on, and `Diagnostics.md` records it, so the next bug
report carries the answer.

### Converted avatars broke after updating AvatarBridge — Missing controllers, pink particles

Only affects conversions made before 2.59.0, which were written **inside the tool's own folder**
(`Assets/AvatarBridge/Output`) — so deleting that folder to update the `.unitypackage` erased them
with it. **Check the Windows Recycle Bin first:** Unity trashes deleted assets rather than
destroying them, and restoring the folder relinks everything, because the `.meta` files carry the
GUIDs the scene points at. Otherwise reconvert — the source avatars were never touched.

Output has landed in the sibling `Assets/AvatarBridgeOutput` since 2.59.0, where the update flow
can't reach it, and anything left in the old location is moved there automatically with its GUIDs
intact.

### A hat or held item drifts off when I resize myself

**Reconvert on 3.4.7 or later**, with the avatar scaler on.

A `ParentConstraint` holds its target a fixed distance from its source, and that distance is in
**metres** — Unity rotates it by the source bone but never scales it. So with the height slider the
body moved and the offset didn't: shrink and the prop hung off you, grow and it sank inside you. One
cowboy hat sat 13–18 cm out.

From 3.4.7 each offset is handed to the hierarchy instead. A small empty named
`AvatarBridge_ScaleRelay_<prop>` is parented to the source bone at exactly the point the offset was
already producing, and the constraint is re-pointed at it with a zero offset. Being a real child, it
inherits the avatar's scale, so the gap grows and shrinks with you. Nothing moves at the default
size, and no animation, layer or curve is involved.

Four cases are deliberately left as they were, and the report names each one:

| Left alone | Why |
|---|---|
| Offsets an animation drives | Zeroing an offset a curve is driving would hand the prop to an animation that no longer matches it |
| Sources inside a cloth or dynamic-bone chain | A new child of a simulated bone becomes a new particle and changes how the chain moves |
| Sources outside the avatar | An offset from a world anchor is meant to be in metres |
| Unlocked constraints | Unity re-derives their offsets from the live transform and would write the old one straight back |

For those, nudge the offset by hand for the size you actually use, or leave the slider near default.

*3.4.5 attempted this in the animation instead — scaled copies of every offset written into the
generated scale clips — and got it wrong: an avatar rendered pure white in play mode and the editor
crashed on scene reload. Reverted in 3.4.6. The attempt and why it failed are recorded in
`AvatarScalerInjector.cs`.*

### Something is bright magenta

A material or shader the avatar points at no longer exists. Almost always VRCFury's temp folder,
which Fury deletes on its next build — so this typically appears *after* a later bake, on an avatar
that converted fine. Convert again on the current version; if it persists, that's worth an issue.

### A mesh renders white, washed out, or loses its eyes

Different from magenta and worth reporting separately. The material survived but its **textures**
didn't — the same VRCFury temp problem one level deeper. Convert again on the current version.

### Gestures freeze in game, or on another PC

**Reconvert on 2.71.0 or later.**

Before 2.71.0, a merged FX layer carrying finger curves was narrowed to a hands-only mask instead of
being blocked. Merged layers sit **above** ChilloutVR's `LeftHand`/`RightHand` layers, so on
Override at full weight they overwrote the pose the gesture had just played — even material-swap
layers with no finger animation of their own, purely by writing defaults into channels the mask let
through. The signature is unmistakable and maddening: the in-game CCK Debugger reports `LeftHand —
Layer Weight: 1.00, Playing Clips: 1.00 Thumbs Up` while your fingers sit in their rest pose. The
animator is correct; something above it is winning. Those curves never moved a finger in VRChat
either — its FX playable layer can't drive humanoid muscles — so they're blocked with the rest now,
and a final audit strips fingers from any mask left above the hand layers.

Also worth knowing, and not a conversion problem: on Index-type controllers ChilloutVR only
registers gestures at all while *Skeletal Input* or *Infer Gestures from Finger Tracking* is enabled
in its settings. With both off, no avatar gestures work, stock or converted.

<details><summary>Two older causes, both fixed long before that</summary>

*Before 2.62.0*, gesture conditions selected poses via the integer `GestureLeftIdx`/`RightIdx`
parameters — which ChilloutVR's own stock avatar animator never uses, and which the game doesn't
reliably feed. Fingers worked in the editor tester (which drives them directly) and froze in game.
Conversions condition on the `GestureLeft`/`GestureRight` floats with the CCK's own threshold bands
now, the same client path every stock avatar runs.

*Before 2.61.0*, the controller referenced its clips wherever the source avatar kept them; in a
project without those folders every missing clip resolves to None and plays as stillness, with no
error anywhere. "Works on the author's PC, frozen on someone else's" is that one's signature. Every
referenced clip and mask is copied into the output's `RehomedAssets` now.

</details>

### The viewpoint or voice position is nowhere near the head

**Reconvert on 2.86.1 or later.**

**The viewpoint comes from your avatar's VRChat descriptor** — the position its author placed by eye
and shipped, copied across unchanged. The CCK's *Auto* button instead reads the humanoid **eye
bones**, and on rigs where those bones aren't where the eyes are it is confidently wrong: one robot
avatar's eye mapping sat 6 cm off-centre and 9 cm behind its face, while the author's own value
matched the hand-corrected position exactly on X and to half a millimetre on Z. Auto is still used
when the descriptor has no viewpoint set, and the report says which was used and how far apart they
were.

**Eye bones are found by name when the rig doesn't map them**, and that search knows Blender's
`.L`/`.R` suffix (`eye.L`, `eye.R`) as well as `LeftEye`-style names (3.0.2) — Blender exports that
suffix by default, so it covers most anthro avatars. It also looks **anywhere on the avatar**, not
only under the head bone (3.0.3): rigs park eye bones outside the head all the time when something
else needs to drive them, and one taur base keeps its pair in a cloned spine chain under a node
named `Head.children.go.here`. A match is only accepted if it lands within the same distance of the
head that this rig's proportions already allow, so a stray bone elsewhere on the body is refused.
Without this the viewpoint fell through to a blind head-offset estimate and sat 14 cm low, on the
muzzle.

**Unless the authored value is provably in the wrong place** (2.100.0). A viewpoint isn't a matter
of taste — it's where your eyes go — so if it lands further from the head bone than the rig's own
proportions allow *and* Auto lands within them, Auto wins and the report says so. One taur base
shipped its viewpoint at the avatar's **hips**, 0.6 m from its head; the old rule copied it faithfully
and then warned about the value it had just chosen.

The conversion checks its own answer — it re-draws each position the way the CCK's inspector will
and measures it against the head bone — so the report tells you when a placement is wrong instead
of leaving it for the first person who hears your voice coming from ten metres away. Putting the
avatar at the top of the scene hierarchy before converting avoids the scale cases entirely, and the
CVRAvatar inspector's own **Auto** buttons are always a safe manual fix.

**On a quadruped, neither the author's viewpoint nor Auto is looking at your avatar** (2.92.0). Those
rigs are a [hidden humanoid decoy](#quadruped--finalik-avatars), and ChilloutVR hangs both
markers off the humanoid **Head** bone — which is part of the decoy, not part of the animal. On the
dragon this was found on, the shipped viewpoint sat **0.57 m** from the dragon's eyes, inside its
skull: looking up was fine, looking down filled the screen with the inside of its own mouth. Auto
was no better — it reads the decoy's eye bones and lands half a metre out too.

The relay constraints say where the real bones are, so the conversion follows them: a constraint
whose **source** is the humanoid head or an eye bone is driving that bone's visible counterpart, and
both markers are measured there instead.

**This only fires when your avatar relays both humanoid EYE bones** (2.99.0), and that requirement
is doing real work. A taur base kept tripping earlier versions: its constraint sourced from the
humanoid head drives a hip clone, because that's its head-puppet feature — the head as an *input*
that swings the body, not a bone being reproduced somewhere visible. The result was a viewpoint
placed at the avatar's hips. Nothing about the rig separated the two cases (that base has a genuine
stand-in skeleton too), and nor did distance — the dragon's real head sits 0.46 m from its humanoid
head, the taur's hip clone 0.50 m. Relayed eyes do: an eye bone exists to aim eyeballs, so a
constraint driven from one is reproducing a face, and a puppet input never has them.

A decoy rig that maps no eye bones therefore keeps the author's own viewpoint, as before 2.92.0. An
unhelpful viewpoint beats one confidently placed at your hips.

It still can't be perfect, because the markers ride the humanoid Head bone whatever happens — so
check them with the gizmo and drag either one if you want it elsewhere.

<details><summary>Three older causes, all fixed</summary>

- **A viewpoint above the eyes (3.4.17).** The author's VRChat viewpoint is preferred over the CCK's
  Auto placement, and it's overridden only when the rig itself proves it wrong. That check measured
  against the **hips** — and hips get mis-mapped as readily as jaws: one avatar pointed them at the
  armature root on the floor, which stretched the tolerance to 0.89 m and would have accepted a
  viewpoint anywhere in its upper half. Its authored value sat 10 cm above the eye bones, on the brow.
  A viewpoint is now also rejected when it sits above the eye midpoint by more than the avatar's own
  **interpupillary distance** — a yardstick that comes from the two bones being compared, so no other
  slot has to be mapped correctly, and that scales with the avatar (about 6 cm on a human, far outside
  where anyone places a viewpoint deliberately). *Below* the eyes is deliberately left alone: down a
  muzzle or inside a helmet is a real choice.
- **A "jaw" bone that isn't a jaw (3.4.15, extended in 3.4.16).** The humanoid **Jaw** slot is optional and nothing
  validates it, so riggers fill it with whatever was nearest — one avatar mapped it to a bone called
  `fronthair1`, 21 cm *above* the head bone and a centimetre above the viewpoint, and the voice duly
  came out of the top of its head. A mapped Jaw is now checked before it's believed: it must sit
  **below the eyes** (a jaw hinges under them — the head bone is a bad reference, since a jaw is
  legitimately level with the base of the skull) and within a quarter of the rig's own hips-to-head
  span of the head bone. Failing either, the jaw is ignored and the voice is measured from an
  open-mouth viseme instead — the vertices that shape moves *are* the mouth — falling back to the head
  bone only if there's no viseme either. The rejected bone is named in the report and flagged in
  `Diagnostics.md`. Nothing is retargeted — that would move geometry — so fix it in the model's Rig
  tab if you want jaw-flap animation as well. A bone's local
  axes are whatever the rigger chose — on one robot avatar the head bone's forward pointed at the
  sky, so "6 cm in front of the head" placed the voice 6 cm *above the eyes*. Offsets use the avatar
  root's orientation now, the one transform whose forward is really forward.
- **Scaled bones (2.82.0).** With no jaw bone the voice position sits a few centimetres in front of
  the head, and that offset used to be applied through the head bone's own transform — which
  multiplies it by the **bone's** scale. Rigs derived from Second Life routinely carry ~100× bone
  scales, turning a 6 cm nudge into 6 m. The offset is sized from the avatar's own hips-to-head span
  and applied by rotation only now, so bone scale can't reach it. The viewpoint was never affected:
  it's a midpoint between two eye-bone *positions*, with no offset to inflate.
- **Scaled parents (2.81.0).** ChilloutVR stores both positions as an offset carrying the avatar's
  own **`localScale`** — what the CCK's inspector reads and writes. Earlier conversions used the
  avatar's *world* scale, identical until the avatar sits under a parent with a scale on it, at
  which point they diverge by the parent's factor.

</details>

Whatever the cause, the CVRAvatar inspector's own **Auto** buttons place both exactly where the
conversion aims to, so they're always a safe manual fix.

### A toggle switches on, the layer plays — and nothing changes on screen

If the report says **"animated material property(ies) don't exist on the shader they target"**,
this is it, and **it is not the conversion**: the same animation does nothing in VRChat either.

**Two report lines cover the other version of this** — a clip whose curves address objects that
aren't on the avatar — and 2.97.0 split them apart, because they mean opposite things:

- **"animate paths that were ALREADY missing in VRChat"** is not a problem. Every dead path is now
  checked against the avatar *as it arrived*, and these weren't there either — so the curves were
  silent in VRChat exactly as they will be here, and nothing was lost. Usually the clip belongs to a
  feature that variant isn't configured for. It used to be reported as a warning, which made healthy
  conversions look alarming: one quadruped base tripped it 44 times, all harmless.
- **"LOST paths that existed before conversion"** is the real one. Those objects were on the avatar
  when it arrived and aren't now, so the feature worked in VRChat and won't here. A stripped system
  (GoGo, SPS) taking objects a clip still references is the usual innocent cause — turn that strip
  off and convert again to check. Anything else is worth reporting as a bug.

**Clips that switch a constraint on and off get the same treatment** (3.1.2), because the same three
outcomes mean three different things:

- **"repointed at the Unity constraints"** — working. The type and property names change during
  conversion, and these followed. Some also had to follow their constraint to a different object,
  because a VRC constraint using `Target Transform` is rebuilt *on the thing it drives*.
- **"drove a constraint that was never built"** — **check your bake, not the conversion.** The object
  is on the avatar but has no constraint and never did, so there was nothing to convert. VRCFury and
  Modular Avatar generate constraints *during the bake*, and a bake that errors partway generates
  some sets and not others — one quadruped had its ear, tongue, wrist and toe constraints built and
  its finger set missing, leaving 140 curves addressing things that never existed. Build a test copy
  of the **source** avatar on its own and confirm it completes without errors.
- **"drove a constraint on an object that is now GONE"** — the object itself vanished during
  conversion. A stripped system is the innocent cause; anything else is a bug worth reporting.

**Locked (optimised) Poiyomi/Thry shaders bake any property that wasn't flagged animated *at lock
time* into the shader as a fixed value and delete the property.** Writing to it afterwards goes
nowhere. Flagging it later sets `_<Name>Animated` on the *material*, but that changes nothing until
the material is **unlocked and locked again** — so a material can claim a property is animated while
its shader genuinely has no such property.

**Fix it in Poiyomi's own material inspector**, and only there: unlock the material, right-click the
property, mark it animated, then lock again. Flags are per material, so if only some of the
avatar's materials carry one, only those respond.

It has to be Poiyomi's UI because marking a property animated *also enables the shader section it
belongs to*. Poiyomi is modular: a disabled section is compiled out of the locked shader
completely, and no flag on a property inside it will bring it back. AvatarBridge briefly shipped a
command that automated the unlock/flag/re-lock, and it was withdrawn in 2.83.0 for exactly this —
it set the flags correctly and the properties still didn't appear, because their sections were off,
while the rebuild changed how the avatar looked. Only Poiyomi knows which section each property
needs.

**Some of them will never work again, and the report says which.** It splits the list in two:

| the report says | what it means |
|---|---|
| **Worth fixing** | Nothing has flagged the property yet. Unlock → mark animated → lock, then convert again. |
| **Probably not fixable** | The material *already* carries the animated flag and the property still isn't in the shader. Someone has done that fix and it didn't take — the section is off, or the animation predates the installed Poiyomi. Re-locking again changes nothing. |

The second group is worth knowing about before you spend an evening on it. Those controls are
[removed from the converted avatar](#what-gets-converted) by default rather than shipped as menu
entries that do nothing.

Everything upstream looks healthy while this is happening, which is what makes it expensive — the
parameter syncs, the layer sits at weight 1, the clip plays, and both the CCK Debugger and the
tester's own **Animator layers** readout confirm it. The report names the property and the renderer
so you don't have to work back from the animator.

### A toggle switches on but never back off

**Reconvert on 2.87.0 or later.**

VRChat's usual toggle is two animator states: one holding the clip that changes something, and one
holding **nothing at all**, whose job is to put it back. That empty state works because VRChat's
Write Defaults writes each property's captured default — the off direction is an implicit rule
rather than animation. Converted, there is nothing in the off state to undo the change, so the
toggle switches on and stays on. Every toggle on such an avatar behaves the same way, because
they're all built the same way.

Conversions now give that off state a real animation. **If your avatar already has a clip that does
the job it uses yours** — the "on" half of a pair that was simply never wired into the empty state.
Only when there's nothing suitable is one generated, by **measuring** the property off your avatar:
whatever it is at conversion time — the object active, the blendshape at rest, the material as
authored — becomes an explicit curve. Either way the toggle restores by animating, which behaves
the same on any platform. The report says which layers reused an existing clip and which got a new
one.

Three things worth knowing:

- **Whatever is true at conversion time is what "off" now means.** If a toggle should rest in its
  other position, set the avatar up that way before converting.
- **Where several layers animate one thing, only the lowest restores it.** A dress toggle and a
  shirt toggle that both move the shirt is the usual case: if the dress layer restored the shirt it
  would assert it from above and the shirt could never come off; if neither did, it could never go
  back on. The lower layer owns it, the higher stays silent, and both toggles work. The report
  counts what was left to a lower layer.
- **Only two-state toggles are filled** (3.4.18). VRChat's idiom is exactly one empty "off" state and
  one state holding the clip, and that shape is the only one where a snapshot of your avatar belongs
  in the empty half. Bigger layers are machines whose empty states are structural — a chest slider's
  `Reset/Pause`, a local/remote gate — and filling those *changes how the avatar looks*: one snapshot
  pinned seven chest blendshapes to zero and flattened the model the moment the layer rested there.
  Those layers are now left exactly as VRChat had them, and the report names each one.
- **Not every empty state is an off state** (3.4.10). Some exist to *choose* — the local/remote gate
  VRChat avatars put at the top of a layer, whose transitions split on `IsLocal` so the wearer's
  controls drive one branch and a synced dropdown drives the other. The layer only passes through
  it, so handing it values makes it hold them for as long as it sits there — and if the gate's
  condition never resolves, forever. Those are now recognised by their transitions covering every
  value of a parameter, and left empty. The report counts them.

  *Before 3.4.10 a hat-grab layer's gate was given the "hat on the head" animation, which asserted
  the hat visible from above its own toggle.*

### Movement doesn't animate, and Airborne / Flying / Sitting / Swimming do nothing

**Reconvert on 3.4.31 or later** — enabling "Base / locomotion" can no longer cause this.

Merged into one ChilloutVR controller, a `[Base]` layer lands **above** the client's own
`Locomotion/Emotes` layer on Override at full weight. From there it can't *add* to CVR's locomotion,
only replace it — and CVR's layer is where the movement sliders and every stance button are answered,
so letting it through costs you all of them. One avatar's `[Base]` layer turned out to be a
calibration utility (states literally named `measure me`, `Preview`, `reinitialize`); unmasked at
weight 1 it simply held the body still.

`[Base]` layers are now masked off the humanoid rig, exactly like merged FX layers. Everything else
in them still converts — object toggles, blendshapes, materials, parameters, additive motion — and
CVR's locomotion stays authoritative.

**And the animations themselves survive** (3.5.0): custom walking, crouching, crawling, falling and
sitting clips are grafted into ChilloutVR's *own* locomotion layer, matched by their position in the
movement blend trees — a clip at the forward-run position lands at CVR's forward-run position,
whatever it's named. The structure stays ChilloutVR's, so movement and stances always answer; the
art becomes the avatar's. Each grafted movement cycle's **loop setting is matched to the slot it
fills** (3.5.1) — a cycle authored without looping would otherwise play once and freeze — while
jump and fall grafts play **once**, as their exit-time transitions always said (3.5.4: loop-matching
a wing-flap fall made it flap forever on every hop). A **flight pose lands on CVR's `LocFlying`
state**: ChilloutVR flies natively, so a VRChat copter/flight system needs none of its speed
machinery here, just its pose where the client will show it. The pose is **scored, not
name-matched** (3.5.4) — a state you can sit in: looping clip, idle/hover naming — after the first
try grafted "Copter *to Robot*", the un-transformation, and flight mode transformed endlessly.
Pose-style stance states (single-clip Standing/Crouching/Lying) are left alone: they are VR
tracking poses, and one seated as a desktop crouch idle sank the wearer into the floor. One discovery made this precise: most VRChat avatars don't ship walking
animations at all — their trees reference `proxy_*` placeholder clips that the VRChat *client*
replaces at runtime. The real walk was never in the avatar, so there's nothing to carry; ChilloutVR's
own animation set is this platform's version of those placeholders, and the report says which of the
two cases your avatar is.

**Genuine locomotion replacements can't be rescued this way**, and it's worth knowing why: they lean
on runtime layer-weight control, which ChilloutVR has no equivalent for, so they don't run here
whether masked or not. Nothing is lost by blocking them. If you want one driving your body anyway,
clear its Mask in the Animator window — and expect the stances to stop responding.

### An animation flickers rapidly — often only on other players' screens

**Reconvert on 3.5.2 or later.** Unity's AnyState transitions default to "Can Transition To Self",
which with ordinary conditions means the destination state re-enters **every frame** the conditions
hold, restarting its animation each time. Nearly every avatar carries dozens of these and VRChat
never shows it, because the states involved are mostly *empty* there — restarting nothing looks
like nothing. Conversion has to fill empty states (they crash Unity's graph builder), and a filled
state restarted every frame strobes its clip.

The "only other people see it" shape is the same mechanism plus networking: remote copies of your
avatar hold every `#` local parameter at its default forever (local parameters never sync, and
parameter streams are stripped from remote copies), so a re-entry condition your live values keep
false can sit permanently true on everyone else's client. The conversion now disables the self
re-entry flag on merged AnyState transitions — except those conditioned on a Trigger, where firing
once per pulse is the intended use. The **Remote view** card in the CCK Animator Tester reproduces
the remote valuation locally if you want to verify an avatar before uploading.

Root motion is also stripped from animations that **travel** (3.5.2, refined in 3.5.3): VRChat
flight and vehicle systems move the player by animating the body, because VRChat allows nothing
else. ChilloutVR moves the player itself and hangs the first-person camera on the head bone — the
same baked movement here shoves the wearer around with no input, so it is removed. The test is
whether the clip's root **ends where it started**: a backflip's flip is root rotation and a dance
sways the whole body, both returning home, and those keep their curves — stripping them broke the
animations while removing nothing a player could feel. A clip that ends displaced is a mover, and
looped, a vehicle; those lose the curves. Locomotion-tree grafts are always stripped — there the
capsule owns every metre.

### An emote replays forever instead of playing once

**Reconvert on 3.5.3 or later.** Emotes live in menus that **hold** their value, and VRChat's
Action graph is built for that: after a play-once emote it parks in a state whose only way back
requires the value to return to zero, so an emote fires on the **rise** of its condition, once.
The converted pose states are re-armed from the locomotion resting state instead — and arming on
a *level* replays the emote every time the pose hands back, forever, as long as the menu holds
the value. Conversion now reproduces the rise-only behaviour: a local ready flag gates every
arming transition, dropped the moment a pose is armed and raised again only when its conditions
have gone false — select the emote again (or re-select after None) and it plays again, exactly
like VRChat. Hold-style emotes (dances, AFK poses) are unaffected; they loop until deselected, as
their own exit conditions have always said.

### A menu control appears, moves, syncs — and does nothing

Check the report for that control's name. Three known causes, all fixed, all worth naming if you
still hit them: a prefab whose constraints drive your bones from proxy objects (see
[above](#constraints-that-drive-another-object)); a slider whose neutral is 0.5 being declared 0; or
**a feature living in the Action layer** (fixed in 3.4.20).

That last one is worth understanding, because it looks exactly like a dead parameter. VRChat's Action
layer is its emote player: VRChat keeps it at weight **0** and raises it only while an emote runs, so
its waiting state can hold a full-body clip and harm nothing. ChilloutVR has no playable layers to
raise it, so conversions rest it at 0 too — otherwise that waiting state asserts a stand-still pose
over your locomotion and you walk on the spot.

Some avatars put a *feature* there anyway. One transforming robot kept its entire vehicle mode in an
Action layer gated on its own `CarMode`/`TransformMode` parameters: every parameter converted, the
menu toggled them correctly, and nothing happened, because the layer holding the animation could
never reach any weight.

An Action layer whose transitions wait on the avatar's **own** parameters — anything outside VRChat's
`VRCEmote`/`AFK`/state built-ins — is now merged at **weight 1**, with its waiting state emptied and
Write Defaults turned off so it contributes nothing until something drives it. That's the same net
effect as VRChat's weight 0, and then it animates at full weight. VRChat fades that weight in over
about half a second and ChilloutVR can't, so expect the change to **snap rather than ease**.

**The full-body poses move into ChilloutVR's own locomotion layer** (3.4.26) — the one place on this
platform a pose can both assert and let go. VRChat raises the Action playable's weight at runtime
while a sequence plays; ChilloutVR has no runtime weight control, and a *separate* layer has no way
to yield — inert states with Write Defaults off hold the last written muscles (the avatar freezes
mid-pose; observed directly), Write Defaults on asserts rest pose over locomotion. Five versions of
state surgery hit that wall. Inside the locomotion layer it doesn't exist: when the pose states
aren't active, locomotion's own states are, still writing muscles every frame — handing back *is*
yielding.

What moves is the **live window** — the states between the behaviour that raises the Action weight
and the one that fades it, read from VRChat's own behaviours before they're stripped; exactly the
states VRChat ever showed. The avatar's arming conditions (parameters, gestures) carry over as the
entry conditions, and the original layer stays merged at weight 0 so its parameter drivers keep
firing on schedule. Not carried over: VRChat's tracking control (IK cut-off during sequences) and
its half-second weight fades — entering and leaving the pose blends over a fixed quarter second.
When no live window can be identified, the layer stays at weight 0 and the report says exactly what
that costs.

### Two near-identical menu controls, and only one works

**Fixed in 3.4.22.** ChilloutVR syncs straight from the animator, so a synced parameter with no menu
control still needs somewhere to live — conversions create one. That guess is wrong when the avatar
*writes* the parameter itself from a parameter driver: the new control then sits in your menu fighting
the animator, right next to the control that really works. One transforming avatar shipped a `Car Mode`
entry (the author's, driving `TransformMode`) directly above a `CarMode` one (ours, driving what the
Action layer sets for itself).

Invented controls are now withdrawn once the merged animator shows a driver writing that parameter.
Only controls this tool added are eligible — anything the author put in the menu stays. The parameter
is untouched and still syncs.

### Your eyes stay open, start closed, or lose a pupil

**Fixed in 3.4.25** — and the fix went through three wrong versions worth recording. An avatar that
blinks **from its own animator** (VRCFury and similar, usually driving `vrc.Blink`) can't keep that
system through conversion, and can't share the eyes with the native one either:

- 3.4.21 enabled native blink *alongside* it, guessing `Blink` while the animator drove `vrc.Blink` —
  two systems, wrong shape, pupil gone.
- 3.4.22 left blink entirely to the avatar's system. That system's "eyes open" states are **empty**
  in VRChat, relying on Write Defaults to reopen the lids — and empty states can't survive
  conversion, because they crash Unity's graph builder and get a filler clip. A state with a motion
  stops writing defaults, so the first blink wrote the shape to 100 and nothing ever wrote it back:
  eyes shut from the first blink, pupil with them, on the body *and* on anything else built from the
  same mesh.

- 3.4.24 picked the shape by name before looking at the layers — and on a mesh carrying both a
  `Blink` shape and a `vrc.Blink` shape it wired the native blink to the wrong one while the real
  driver kept running.

The animator blink system only exists because **VRChat has no built-in blink. ChilloutVR does.** So
the conversion (3.4.25) finds the animator layer whose *only job is blinking* — every curve a
blendshape, the only shape it ever raises matches "blink", no objects, no materials — lets **that
layer name the shape**, removes it, wires ChilloutVR's native Eye Blink to the same shape, and zeroes
the shape's live weight in case the old system left the eyes mid-blink. If no layer can be safely
identified, nothing is removed, native blink stays off, and the report says so.

One refinement (3.4.32): ChilloutVR writes its blink weight onto the mesh **every frame, after the
animator** — so whatever shape it's given, the client owns it outright, and an expression animating
the same shape would silently stop closing the eyes. If the removed layer's shape is also used by
surviving expression clips, the native blink is moved to a free shape family instead — a `Blink L` /
`Blink R` pair or a spare combined shape that nothing animates — so the blink and the expressions
both work. If no free shape exists, the blink keeps the contested shape and the report warns which
expressions lose. Expressions that close the eyes through *other* shapes were never affected.

### An effect draws in one eye only in VR

Expected, and not caused by converting — see
[shaders that only draw into one eye](#shaders-that-only-draw-into-one-eye).

### There's no "Convert a VRChat avatar" tab

The VRChat Avatars SDK isn't installed. Without it a VRChat avatar's components can't be read at
all, so only [Setup mode](#setup-mode) is offered.

### Uploading fails with "Failed to generate new object ID"

Not AvatarBridge — that's the CCK. ChilloutVR's API refused to allocate a content slot, usually
because of the account's private upload limit. The real message is in `Player.log` or the Unity
console just above the exception.

### One extra recompile after importing

Normal. That's AvatarBridge registering its scripting defines.

## Reporting a bug

Hit **Report an issue** in the AvatarBridge window — it opens a pre-filled GitHub issue with your
versions and detected packages already in it.

Two things make a report solvable immediately:

1. **Attach `ConversionReport.md` and `Diagnostics.md`** from `Assets/AvatarBridgeOutput/<avatar>/`.
   Nearly every bug fixed so far was diagnosed from the report; `Diagnostics.md` (3.2.0) carries the
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
