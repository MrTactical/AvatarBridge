# VRCSDK ↔ ChilloutVR parity matrix

The deep-dive ledger (task #22, started 2026-08-03). One row per thing the VRC SDK can put on an
avatar; the verdict says what conversion does with it **today**. Wild counts come from two census
tools in `Tools/Regression/` — `ComponentCensus` (84 scenes, components + state behaviours) and
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
| VRCAvatarDescriptor | 93 | converted | field-level rows below |
| VRCPhysBone | 1531 | converted | → MagicaCloth2/DynamicBone; animated property curves are the gap (tasks #15, #16) |
| VRCPhysBoneCollider | 449 | converted | animated `m_Enabled` ×28 unhandled — task #15 |
| VRCContactReceiver | 447 | converted | enable curves fixed 3.5.36; `position.xyz`/`allowSelf` curves — task #19 |
| VRCContactSender | 165 | converted | as above |
| VRCRotation/Parent/Scale/Position Constraint | 195/144/68/29 | converted | source-weight curve spelling fixed 3.5.36; `FreezeToWorld` reported-dropped |
| VRCAimConstraint | 5 | converted | |
| VRCLookAtConstraint | 2 | converted | |
| **VRCStation** | **102** | **SILENT → make reported** | sit-on-me chairs. No seat type exists on the client's avatar whitelist — avatar seats are a ChilloutVR platform gap, so the honest ceiling is a report line. Much of the count is GoGo Loco's own stations (stripped with GoGo) — the report should say when a kept one is dropped. Task #28 |
| VRCSpatialAudioSource | 91 | converted | → AudioSource + VRChat-parity clamps |
| VRCHeadChop | 17 | converted | → FPRExclusion; animated `globalScaleFactor` ×25 + `m_Enabled` ×10 — task #17. Per-bone scale factors and `applyCondition` not audited field-level yet |
| VRCPerPlatformOverrides | 5 | absent-in-effect | upload-time platform switching; nothing to convert, sweep-deleted |
| VRCImpostorSettings / VRCImpostorEnvironment | 0 | absent | VRChat-service impostor hints |
| VRCRaycast / VRCRaycastHandler | 0 | absent | |
| VRCPhysBoneRoot | 0 | absent | |
| PipelineManager | 94 | converted | deleted deliberately, reported |

## State behaviours

All unknown VRC behaviours are counted and reported when skipped (`ConvertBehaviours`' skip dict),
so nothing here is fully silent — the question is feature vs report.

| type | wild n | verdict | notes |
|---|---|---|---|
| VRCAvatarParameterDriver | 1751 | converted | → AnimatorDriver; results sync (client-verified) |
| VRCAnimatorLayerControl | 198 | converted | Action-layer live-window detection |
| VRCAnimatorLocomotionControl | 31 | converted | → BodyControl Locomotion mask |
| VRCAnimatorTrackingControl | 24 | converted | → BodyControl; eyes/mouth/fingers have no CVR mask, reported |
| VRCPlayableLayerControl | 11 | converted | Action transplant |
| **VRCAnimatorPlayAudio** | **86** | **reported, featureless** | animator-driven audio (music toggles, SFX). lumar (corpus) and CowBot carry it. Candidate approximation: generate enable/clip curves against a real AudioSource, or CVRAudioDriver. Task #29 |
| VRCAnimatorTemporaryPoseSpace | 2 | reported, featureless | viewpoint-to-hips during a pose; no client hook — report line should say what it would have done. Task #29 |

## Descriptor, field level (partial — continue here)

| field | verdict | notes |
|---|---|---|
| ViewPosition | converted | with believability checks + quad rescue |
| lipSync = VisemeBlendShape / JawFlapBone / JawFlapBlendShape | converted | |
| lipSync = **VisemeParameterOnly** / Default | **verify** | falls through to the name-match fallback; confirm that's right for ParameterOnly (avatar drives visemes itself — fallback may double-drive) |
| customEyeLookSettings: eyes (bones, rotations) | converted | gaze limits measured from poses; signed-euler client contract |
| eyelids = Blendshapes | converted | |
| eyelids = **Bones** / Rotations | **verify** | `!= Blendshapes` branch — confirm it reports rather than silently skips |
| collider_head/torso/hands/feet (+fingers?) | converted | → pointers for listened tags; **verify finger colliders** and per-collider radius/height overrides are honoured |
| portraitCameraPositionOffset | verify | likely irrelevant (VRChat UI), confirm and record |
| expressionParameters / expressionsMenu | converted | all control types incl. puppets; verify FourAxis axis labels |
| baseAnimationLayers / specialAnimationLayers | converted | five-layer merge; Sitting/TPose/IKPose special layers — **verify** what happens to each |

## CCK side — what ChilloutVR offers that conversion never authors

Avatar-relevant only; world components excluded.

| CCK type | opportunity |
|---|---|
| CVRAttachment | no VRC equivalent to convert *from*; possible Analyse-button suggestion someday |
| CVRIKAngleLimit / CVRIKHingeLimit | wraps FinalIK limits; no VRC source — correctly unused |
| CVRHapticZone / haptic areas beyond chest | VRC has no haptics authoring; unused is correct |
| CVRDistanceLod | no VRC source; enhancement only |
| advanced tagging / CVRAvatar fields beyond what we set | **unaudited — next slice of task #22** |

## Standing findings

- The delete-VRC-components sweep means nothing survives unconverted — so every gap is at worst
  a *silent feature loss*, never a broken upload. The audit's job is promoting SILENT → reported →
  approximated → converted, in wild-count order.
- Census before code: both instruments exist now; run them after SDK updates too, since new SDK
  components arrive silently.
