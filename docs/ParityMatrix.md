# VRCSDK ↔ ChilloutVR parity matrix

**Status: complete** (task #22, 2026-08-03). Living document — re-run the two censuses after
any SDK or CCK update, since new components arrive silently.

The deep-dive ledger. One row per thing the VRC SDK can put on an
avatar; the verdict says what conversion does with it **today**. Wild counts come from two census
tools in `Tools/Regression/` — `ComponentCensus` (85 scenes, components + state behaviours; re-measured 2026-08-04 after it was found to be missing descriptor layers) and
`AnimatedVrcPropertyScan` (6,661 clips, animated properties on deleted components) — so priority is
set by what avatars actually do, not by what the SDK offers.

Classification:
- **converted** — carried across, feature works
- **approximated** — works with a stated difference, report says so
- **reported** — dropped, but the report names it and what it costs
- **SILENT** — dropped with no trace: always a defect, either a fix or a report line owed
- **absent** — zero occurrences in the wild; delete-sweep is the correct handling

## Components

| type | wild n | verdict | notes |
|---|---|---|---|
| VRCAvatarDescriptor | 101 | converted | field-level rows below |
| VRCPhysBone | 1701 | converted | → MagicaCloth2/DynamicBone; animated parameter curves reported-as-lost with each named (#16, done; MC2 cannot be animation-driven — measured); m_Enabled chain toggles were already rewired |
| VRCPhysBoneCollider | 494 | converted | animated `m_Enabled` ×28 repointed at the generated collider host (task #15, done) |
| VRCContactReceiver | 456 | converted | enable curves fixed 3.5.36; position curves follow the contact on both paths and filter curves on native (#19, done — legacy filters bake at Create and drop loudly) |
| VRCContactSender | 165 | converted | as above |
| VRCRotation/Parent/Scale/Position Constraint | 203/154/68/20 | converted | source-weight curve spelling fixed 3.5.36; `FreezeToWorld` reported-dropped |
| VRCAimConstraint | 5 | converted | |
| VRCLookAtConstraint | 2 | converted | |
| VRCStation | 110 | reported | sit-on-me chairs. No seat type on the client avatar whitelist — platform gap. Kept stations get a Skipped entry naming each (#28, done); GoGo strip removes its own first. Wild-verified: GoAvatrCoat names its three chairs |
| VRCSpatialAudioSource | 91 | converted | → AudioSource + VRChat-parity clamps |
| VRCHeadChop | 17 | converted | → FPRExclusion; animated curves were already rewired onto isShown — #17 fixed the m_Enabled POLARITY for showing-type chops (was inverted unconditionally, backwards for the keep-visible idiom) and made curves on skipped chops loud. applyCondition still unaudited field-level |
| VRCPerPlatformOverrides | 5 | absent-in-effect | upload-time platform switching; nothing to convert, sweep-deleted |
| VRCImpostorSettings / VRCImpostorEnvironment | 0 | absent | VRChat-service impostor hints |
| VRCRaycast / VRCRaycastHandler | 0 | absent | |
| VRCPhysBoneRoot | 0 | absent | |
| PipelineManager | 102 | converted | deleted deliberately, reported |

## State behaviours

All unknown VRC behaviours are counted and reported when skipped (`ConvertBehaviours`' skip dict),
so nothing here is fully silent — the question is feature vs report.

**Behaviour counts re-measured 2026-08-04 and they moved enormously** — the census had been reading
only `animator.runtimeAnimatorController`, missing the descriptor's own `baseAnimationLayers` and
`specialAnimationLayers`, which is where a VRChat avatar actually keeps Base/Additive/Gesture/
Action/FX. Every number in this table was a floor; PoseSpace was wrong by 288×. Component counts
below barely moved, which is the check that the fix is aimed correctly: components sit on objects
and were never affected. **Where an instrument and the conversion reports disagree, the reports win.**

| type | wild n | verdict | notes |
|---|---|---|---|
| VRCAvatarParameterDriver | 3282 | converted | → AnimatorDriver; results sync (client-verified). Was measured as 1751 |
| VRCAnimatorTrackingControl | 581 | converted | → BodyControl; eyes/mouth/fingers have no CVR mask, reported. **Was measured as 24 — a 24× undercount, so "a rare gap" was wrong. At this scale the unmaskable channels deserve their own look** |
| VRCAnimatorTemporaryPoseSpace | 575 | reported | says what it did — viewpoint to hips while crawling etc. (#29). Was measured as **2**, which is how a 100-per-avatar behaviour got judged not worth building for |
| VRCAnimatorLayerControl | 262 | converted | Action-layer live-window detection. Was 198 |
| VRCAnimatorLocomotionControl | 152 | converted | → BodyControl Locomotion mask. Was 31 |
| VRCPlayableLayerControl | 129 | converted | Action transplant. Was 11 |
| VRCAnimatorPlayAudio | 132 | reported richly | own inventory naming state, source path, clips, looping (#29, done — lumar names 22). Approximation deliberately deferred: the enable-window play/stop trick interacts with Write Defaults and needs the off-state restore machinery; hand-wiring hint in the report (AudioSource Play On Awake, CVRAudioDriver animated index). Was 86 |

## Descriptor, field level (complete 2026-08-03)

| field | verdict | notes |
|---|---|---|
| ViewPosition | converted | with believability checks + quad rescue |
| lipSync = VisemeBlendShape / JawFlapBone / JawFlapBlendShape | converted | |
| lipSync = VisemeParameterOnly / Default | approximated | both fall to the name-match auto-detect, Warning when nothing found. Right for Default (that IS auto-detect in VRChat). For ParameterOnly the avatar drives visemes from its own animator, so wiring native visemes on the same shapes contests them — the client writes per-frame and wins, same as blink. Report entry live (#29): "Visemes were parameter-driven in VRChat", firing on both GoGo demo corpus avatars |
| customEyeLookSettings: eyes (bones, rotations) | converted | gaze limits measured from poses; signed-euler client contract |
| eyelids = Blendshapes | converted | |
| eyelids = Bones / Rotations | reported, cause unnamed | `GetBlinkBlendshapeName` returns null → auto-detect fallback → "Blink blendshape: none found" Warning. Bone-driven eyelids themselves are unconvertible (CVR blink is blendshape-only); the cause is now named first: "Eyelids are bone-driven" Approximated entry (#29); no corpus avatar uses the mode, placement code-verified |
| collider_* (head, torso, both hands, both feet, all 8 fingers) | converted | per-collider radius/height/position/rotation honoured; `State.Disabled` skipped; L/R enumerated separately so the mirrored flag has nothing to do |
| portraitCameraPositionOffset | absent-in-effect | VRChat website thumbnail camera; CVR takes its own capture |
| expressionParameters / expressionsMenu | converted | all control types incl. puppets |
| baseAnimationLayers | converted | five-layer merge |
| specialAnimationLayers: Sitting | converted | custom sit pose grafted onto CVR's Sitting state, chosen by state-count vote; tie leaves the CCK's |
| specialAnimationLayers: TPose / IKPose | absent-in-effect | VRChat IK calibration poses; CVR calibrates its own way, nothing user-visible to lose |

## CCK side (complete 2026-08-03)

Avatar-relevant only; world components excluded.

**CVRAvatar field audit — no gaps.** Every field is either written by conversion, or untouched
for a stated reason:

| untouched field | why that's correct |
|---|---|
| voiceParent | defaults to Head; our voice position is measured head-relative |
| eyeMovementInterval | CCK idle-gaze timing; VRChat has no equivalent field to convert |
| visemeSmoothing | CCK default 50; VRChat has no smoothing knob |
| enableAdvancedTagging / tags | content tagging is an upload-time author decision, not descriptor data |
| fprSettingsList | first-person visibility — we author `FPRExclusion` components instead, which is the converted form of VRCHeadChop |
| fallbackGameObject | CVR fallback-avatar feature; VRChat fallbacks are separate uploads, nothing to convert |
| ~~blinkMode~~ | actually WRITTEN — reflectively, via `AvatarFeatureDetect.SetBlinkMode` (enum shape varies across CCK versions). Recorded because a grep audit reports it untouched: **reflective writes are invisible to field greps** |

| CCK type never authored | why |
|---|---|
| CVRAttachment | no VRC equivalent to convert *from* |
| CVRIKAngleLimit / CVRIKHingeLimit | wraps FinalIK limits; no VRC source |
| CVRHapticZone / haptic areas beyond chest | VRC has no haptics authoring |
| CVRDistanceLod | no VRC source; enhancement only |

## PhysBone, field level (complete 2026-08-03; deep audits in tasks #7 and #11)

Read and mapped: rootTransform, radius (+radiusCurve), gravity, gravityFalloff, pull/spring/
stiffness curves, immobile (+curve, +immobileType), limitType, integrationType, multiChildType,
isAnimated, maxStretch, maxSquish, collider list, parameter (grab/stretch parameter family),
version.

Never read — with why, in three groups:

- **Per-bone modulation curves** (gravityCurve, gravityFalloffCurve, limitRotationX/Y/ZCurve,
  maxAngleX/ZCurve, maxStretchCurve, maxSquishCurve, stretchMotion + curve): refinements of
  values whose base IS converted. MagicaCloth2 has its own along-chain curves for some of these;
  mapping curve-to-curve is possible future precision work, not a silent feature loss — the
  chain still moves, with uniform values.
- **Cross-avatar filtering** (allowSelf, allowOthers, ignoreOtherPhysBones, contentTypes):
  VRChat's other-players-touch-my-physbones system. ChilloutVR has no native cross-avatar
  physbone interaction (GrabbyBones is the mod-side answer, and it is supported) — platform
  gap, nothing to map onto.
- **Niche behaviours** (snapToHand, resetWhenDisabled, staticFreezeAxis, localGravityDirection,
  sphereCollision): small semantic differences; candidates for report lines if a tester ever
  hits one, none observed in the wild census.

Everything else on the type is solver runtime state (netId, childIndex, rest*, etc.), not
authored data.

## Standing findings

- The delete-VRC-components sweep means nothing survives unconverted — so every gap is at worst
  a *silent feature loss*, never a broken upload. The audit's job is promoting SILENT → reported →
  approximated → converted, in wild-count order.
- Census before code: both instruments exist now; run them after SDK updates too, since new SDK
  components arrive silently.
