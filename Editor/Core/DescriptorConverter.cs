#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using ABI.CCK.Components;
using ABI.CCK.Scripts;

namespace AvatarBridge
{
    // Converts the VRCAvatarDescriptor basics onto a CVRAvatar component:
    // viewpoint, voice position, face mesh, visemes and blinking.
    public static class DescriptorConverter
    {
        const string Category = "Avatar descriptor";

        static string Vec(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";

        public static void Run(BridgeContext ctx)
        {
            var vrc = ctx.SourceDescriptor;

            var cvrAvatar = ctx.Target.GetComponent<CVRAvatar>();
            if (cvrAvatar == null)
            {
                cvrAvatar = ctx.Target.AddComponent<CVRAvatar>();
            }
            ctx.CvrAvatar = cvrAvatar;

            // --- Viewpoint ---
            // The CCK's Auto placement is the fallback for rigs whose
            // authored viewpoint cannot be used.
            var animator = ctx.TargetAnimator;
            bool autoView = AvatarFeatureDetect.CckAutoViewPosition(ctx.Target, animator, out var viewAuto);

            // The author's viewpoint wins when there is one. Auto reads
            // the eye bones, which are not always where the eyes are.
            // Copied, not scaled: VRChat's value is authored at shipping
            // size, already the world-metre offset CVR stores.
            bool haveAuthored = vrc.ViewPosition != Vector3.zero;
            var authored = vrc.ViewPosition;

            // ...but only when it is anywhere near the head. A viewpoint
            // is where the player's eyes go, not a matter of taste.
            // When the authored value fails the head check and Auto
            // passes it, Auto wins.
            bool authoredIsWrong = false;
            string autoOverrideNote = null;
            if (haveAuthored && autoView
                && AvatarFeatureDetect.HeadDistance(ctx.Target, animator, authored, out float authoredOff, out float tolerance)
                && AvatarFeatureDetect.HeadDistance(ctx.Target, animator, viewAuto, out float autoOff, out _))
            {
                authoredIsWrong = authoredOff > tolerance && autoOff <= tolerance;
                if (authoredIsWrong)
                {
                    // Held, not reported, until the decoy check below
                    // has its say. Whichever wins speaks alone.
                    autoOverrideNote =
                        $"The viewpoint this avatar shipped with in VRChat lands {authoredOff:0.##} m from its " +
                        $"head bone, past the {tolerance:0.##} m this rig's own proportions allow, so it is in " +
                        "the wrong place rather than merely unusual — you would spawn looking out of the " +
                        $"avatar's body. The CCK's Auto placement (midpoint of the eye bones) lands {autoOff:0.##} m " +
                        "from the head and was used instead. The author's value is normally preferred, because a " +
                        "human placed it by eye; it is only overridden when it fails a check the rig itself " +
                        "settles. Drag the gizmo on the CVRAvatar if neither is right.";
                }
            }
            // The check above measures against the hips, which can be
            // mis-mapped too. The eyes answer for themselves.
            if (haveAuthored && autoView && !authoredIsWrong
                && AvatarFeatureDetect.ViewpointSitsAboveEyes(ctx.Target, animator, authored,
                    out float aboveEyes, out float eyeSeparation))
            {
                authoredIsWrong = true;
                autoOverrideNote =
                    $"The viewpoint this avatar shipped with in VRChat sits {aboveEyes:0.###} m ABOVE the " +
                    $"midpoint of its eye bones — more than the {eyeSeparation:0.###} m between the eyes " +
                    "themselves, so it is on the brow or above it rather than behind the eyes. A viewpoint " +
                    "is never legitimately above the eyes: you would spawn looking over your own face. The " +
                    "CCK's Auto placement (the eye midpoint) was used instead. Below the eyes is left alone " +
                    "— down a muzzle or inside a helmet is a real choice — and the author's value is " +
                    "otherwise always preferred. Drag the gizmo on the CVRAvatar if neither is right.";
            }

            var humanoidView = haveAuthored && !authoredIsWrong
                ? authored
                : autoView ? viewAuto : AvatarFeatureDetect.EstimateViewPosition(ctx.Target, animator);

            // ...unless the humanoid rig is a DECOY and none of the above is looking at the
            // avatar at all. See AvatarFeatureDetect.DecoyRigAnchors: on a quadruped whose
            // humanoid map points at a hidden stand-in skeleton, the author's VRChat viewpoint,
            // the CCK's Auto button and the local estimate all agree with each other and all sit on
            // the stand-in. The 5 cm gate keeps this silent on every rig where the relay exists
            // but changes nothing, so an ordinary avatar's conversion is untouched.
            bool decoyRig = AvatarFeatureDetect.DecoyRigPlacement(ctx.Target, animator,
                                out var decoyView, out var decoyVoice, out var visibleHead, out string decoyDetail)
                            && Vector3.Distance(decoyView, humanoidView) > 0.05f;
            cvrAvatar.viewPosition = decoyRig ? decoyView : humanoidView;

            if (decoyRig && AvatarFeatureDetect.ExcludeVisibleHeadFromFirstPerson(animator, visibleHead))
            {
                ctx.Report.Converted(Category, $"First-person head hiding moved to \"{visibleHead.name}\"",
                    "ChilloutVR hides your own head in first person by adding an FPRExclusion to the " +
                    "humanoid Head bone. On a decoy rig that bone is part of the stand-in skeleton and " +
                    "skins nothing, so the client hides nothing and you spend the session looking at " +
                    "the inside of your own head. One was added to the head you can actually see " +
                    "instead. It only affects YOUR camera — everyone else sees the whole avatar — and " +
                    "deleting it puts your head back in shot.");
            }

            if (decoyRig)
            {
                float moved = Vector3.Distance(humanoidView, decoyView);
                ctx.Report.Approximated(Category, "Viewpoint & voice measured on the VISIBLE head",
                    "This avatar's humanoid rig is a decoy: the bones Unity's humanoid map points " +
                    "at are a hidden stand-in skeleton, and constraints relay them onto the body " +
                    "you can actually see. ChilloutVR parents both the viewpoint and the voice " +
                    "position to the humanoid Head bone, so the viewpoint this avatar shipped " +
                    "with — and the CCK's Auto button, and every estimate here — all land on the " +
                    $"stand-in, {moved:0.##} m from where this avatar's face is. Far enough to sit " +
                    "INSIDE the head, which looks fine until you glance down and the inside of " +
                    $"your own mouth fills the screen. Both were measured on the relayed bones " +
                    $"instead — {decoyDetail}. Check them with the CVRAvatar gizmo before " +
                    "uploading: the markers ride the humanoid Head bone whatever happens, so on a " +
                    "rig like this they can be put in the right place but not made to track it " +
                    "perfectly. Drag either gizmo if you want it elsewhere.");
            }
            else if (authoredIsWrong)
            {
                ctx.Report.Approximated(Category, "Viewpoint — the CCK's Auto placement, not the author's",
                    autoOverrideNote);
            }
            else if (haveAuthored && autoView)
            {
                float apart = Vector3.Distance(authored, viewAuto);
                ctx.Report.Converted(Category, "Viewpoint — the author's own, from the VRChat descriptor",
                    $"Placed at the viewpoint this avatar shipped with in VRChat. The CCK's Auto " +
                    $"button (midpoint of the eye bones) would put it {apart:0.###} m away" +
                    (apart > 0.05f
                        ? " — they disagree by more than a viewpoint usually moves, which happens when a " +
                          "rig's eye bones sit somewhere other than its eyes, or when the author placed " +
                          "the value by eye and was a little out. The author's is kept: it is right far " +
                          "more often, and it is the only one of the two a human ever looked through."
                        : ", so the two agree and either would have done.") +
                    " Auto remains one click away in the CVRAvatar inspector if you prefer it.");
            }
            cvrAvatar.voicePosition = cvrAvatar.viewPosition;   // replaced below, once the face is known

            // --- Face mesh, visemes --------------------------------------------------
            SkinnedMeshRenderer sourceFace = vrc.VisemeSkinnedMesh;
            SkinnedMeshRenderer targetFace = null;
            if (sourceFace != null)
            {
                Transform match = ctx.FindInTarget(sourceFace.transform);
                targetFace = match != null ? match.GetComponent<SkinnedMeshRenderer>() : null;
            }

            bool faceMeshDetected = false;
            if (targetFace == null)
            {
                // Plenty of avatars never set a viseme mesh (no lip sync, or jaw-bone lip
                // sync). Detect the face mesh anyway: blink, face tracking and viseme
                // detection all bind through bodyMesh, so leaving it null silently costs
                // the avatar all three.
                targetFace = AvatarFeatureDetect.FindFaceMesh(ctx.Target);
                faceMeshDetected = targetFace != null;
            }

            if (targetFace != null)
            {
                cvrAvatar.bodyMesh = targetFace;
                if (faceMeshDetected)
                {
                    ctx.Report.Approximated(Category, "Face mesh auto-detected",
                        $"The VRChat descriptor named no viseme mesh, so \"{targetFace.name}\" was picked " +
                        "(most blendshapes, debug meshes skipped). Check it on the CVRAvatar if lip sync or " +
                        "blink look wrong.");
                }
                else
                {
                    ctx.Report.Converted(Category, "Face mesh", targetFace.name);
                }
            }
            else
            {
                ctx.Report.Warning(Category, "Face mesh",
                    "No viseme mesh on the VRC descriptor, and no skinned mesh with blendshapes to fall back " +
                    "on — visemes, blink and face tracking have nothing to bind to.");
            }

            // --- Voice position ------------------------------------------------------
            // The CCK's Auto placement again (jaw bone, else a head-bone offset); the
            // viseme-measured mouth stays as the fallback for rigs without either bone.
            bool autoVoice = AvatarFeatureDetect.CckAutoVoicePosition(ctx.Target, animator, out var voiceAuto);
            if (decoyRig)
            {
                // Reported with the viewpoint above; one cause, one fix.
                cvrAvatar.voicePosition = decoyVoice;
            }
            else if (autoVoice)
            {
                cvrAvatar.voicePosition = voiceAuto;
            }
            else
            {
                cvrAvatar.voicePosition = MouthLocator.Locate(ctx.Target, targetFace, vrc.VisemeBlendShapes,
                    animator, cvrAvatar.viewPosition, out var mouthMethod, out string mouthDetail,
                    out string badJaw);
                MouthLocator.Report(ctx, Category, cvrAvatar.voicePosition, mouthMethod, mouthDetail, badJaw);
            }

            // Reported whenever the humanoid rig moves no geometry;
            // everything above then measured a skeleton nobody can see.
            // A check that passes on the wrong skeleton is worth nothing.
            AvatarFeatureDetect.HumanoidDeformShare(ctx.Target, animator, out int mappedBones, out int deformingBones);

            // Last line of defence, skeleton-free: a viewpoint outside
            // the renderer bounds is not on the avatar. Only runs when
            // the rig drives no geometry and the current answer is
            // demonstrably off the body.
            if (!decoyRig && mappedBones > 0 && deformingBones == 0
                && AvatarFeatureDetect.RescueViewpointOffBody(ctx.Target, animator,
                    cvrAvatar.viewPosition, out var rescued, out string rescueDetail))
            {
                var before = cvrAvatar.viewPosition;
                cvrAvatar.viewPosition = rescued;
                ctx.Report.Approximated(Category, "Viewpoint moved onto the visible body",
                    $"The viewpoint worked out from this rig — {Vec(before)} — is further from every " +
                    "bone that actually deforms mesh than this rig's own proportions allow, so it " +
                    "was placed on a skeleton you can't see. " +
                    "That happens when the humanoid map points at a stand-in: a decoy rig, a " +
                    "poseclone, or a FinalIK proxy. Every distance check still passes, because they " +
                    "all measure against that same skeleton. Re-placed from the eye markers that ARE " +
                    $"on the body — {rescueDetail} — giving {Vec(rescued)}. Nothing moves when the " +
                    "original already lands on the body, so this can only ever fire on a rig where " +
                    "it was wrong. Check it with the CVRAvatar gizmo before uploading.");

                // The voice rides the same invisible skeleton and would otherwise be left on it.
                // Placed with the viewpoint rather than guessed at: on a rig whose humanoid jaw is
                // part of the stand-in there is nothing better to measure from, and a voice coming
                // from the avatar's face beats one coming from thin air beside it.
                if (AvatarFeatureDetect.PointIsOffBody(ctx.Target, animator, cvrAvatar.voicePosition))
                {
                    var voiceBefore = cvrAvatar.voicePosition;
                    cvrAvatar.voicePosition = rescued;
                    ctx.Report.Approximated(Category, "Voice position moved onto the visible body",
                        $"It was at {Vec(voiceBefore)}, off the body for the same reason as the " +
                        "viewpoint — this rig's humanoid jaw and head are part of the stand-in " +
                        "skeleton, so there was nothing on the real face to measure from. Placed " +
                        "with the viewpoint, which puts your voice at the avatar's face rather than " +
                        "in the air beside it. Drag it onto the mouth on the CVRAvatar if you want " +
                        "it exact — it is a little high by design, sitting at eye level.");
                }
            }

            if (mappedBones > 0 && deformingBones == 0)
            {
                ctx.Report.Warning(Category,
                    $"None of this avatar's {mappedBones} humanoid bones move any geometry",
                    "Unity's humanoid map points at a skeleton that deforms no mesh — a stand-in, a " +
                    "poseclone, or a FinalIK proxy — and the visible body is driven from it " +
                    "indirectly. That matters here because ChilloutVR hangs the viewpoint, the voice " +
                    "position AND first-person head hiding off humanoid bones, so all three follow " +
                    "the stand-in rather than the body you see. They may be measurably correct " +
                    "against that skeleton and still be in the wrong place on your avatar. Check all " +
                    "three with the CVRAvatar gizmos before uploading and drag them onto the visible " +
                    "head if they're off. Diagnostics.md lists every bone the map points at, with " +
                    "full paths, if you need to see which skeleton was used.");
            }

            // Skipped on a decoy rig: this check measures both markers against the humanoid head
            // bone, and on such a rig they are deliberately NOT near it. Left in, it would fire a
            // warning blaming a scaled ancestor for the placement that just fixed the avatar.
            if (!decoyRig)
            {
                AvatarFeatureDetect.VerifyHeadPlacement(ctx, Category, animator,
                    cvrAvatar.viewPosition, cvrAvatar.voicePosition);
            }
            if (!haveAuthored && (autoView || autoVoice) && !decoyRig)
            {
                // Usable, not merely mapped. A Jaw on a hair bone is neither.
                var jawBone = animator != null && animator.isHuman
                    ? animator.GetBoneTransform(HumanBodyBones.Jaw) : null;
                bool hasJaw = jawBone != null &&
                              AvatarFeatureDetect.JawIsBelievable(ctx.Target, animator, jawBone, out _);
                ctx.Report.Converted(Category, "View & voice placed the CCK's own way",
                    (autoView
                        ? "View between the eye bones (this avatar's VRChat descriptor had no viewpoint set)"
                        : "View estimated from the avatar's bounds (no usable eye or head bones)") + "; " +
                    (autoVoice
                        ? (hasJaw ? "voice at the jaw bone" : "voice just ahead of the head bone (no jaw bone)")
                        : "voice measured from the viseme mesh") +
                    " — the same positions the Auto buttons on the CVRAvatar inspector produce.");
            }
            // Only when the CCK-Auto path placed the voice. Otherwise
            // MouthLocator already reported where and how.
            else if (haveAuthored && autoVoice)
            {
                // Usable, not merely mapped. A Jaw on a hair bone is neither.
                var jawBone = animator != null && animator.isHuman
                    ? animator.GetBoneTransform(HumanBodyBones.Jaw) : null;
                bool hasJaw = jawBone != null &&
                              AvatarFeatureDetect.JawIsBelievable(ctx.Target, animator, jawBone, out _);
                ctx.Report.Converted(Category, "Voice position",
                    (hasJaw
                        ? "At the jaw bone — a bone that exists is worth more than any estimate."
                        : "Just ahead of the head bone; this rig has no jaw bone to use. " +
                          "Offsets are applied along the AVATAR's forward, not the head bone's, " +
                          "because a bone's own axes can point anywhere.") +
                    " VRChat has no voice position to inherit, so unlike the viewpoint this one is " +
                    "always derived. Check it with the CVRAvatar gizmo before uploading.");
            }

            // CVR has all three lip-sync styles: Visemes,
            // SingleBlendshape, JawBone. An explicit jaw-flap choice
            // outranks a name match on the face mesh.
            if (vrc.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.JawFlapBone)
            {
                WireJawBoneLipSync(ctx, cvrAvatar);
            }
            else if (vrc.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape
                     && !string.IsNullOrEmpty(vrc.MouthOpenBlendShapeName))
            {
                WireSingleBlendshapeLipSync(ctx, cvrAvatar, vrc.MouthOpenBlendShapeName);
            }
            else if (vrc.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape &&
                vrc.VisemeBlendShapes != null && vrc.VisemeBlendShapes.Length > 0)
            {
                cvrAvatar.useVisemeLipsync = true;
                cvrAvatar.visemeMode = CVRAvatar.CVRAvatarVisemeMode.Visemes;
                if (cvrAvatar.visemeBlendshapes == null || cvrAvatar.visemeBlendshapes.Length < vrc.VisemeBlendShapes.Length)
                {
                    cvrAvatar.visemeBlendshapes = new string[Mathf.Max(15, vrc.VisemeBlendShapes.Length)];
                }
                for (int i = 0; i < vrc.VisemeBlendShapes.Length; i++)
                {
                    cvrAvatar.visemeBlendshapes[i] = vrc.VisemeBlendShapes[i];
                }
                ctx.Report.Converted(Category, "Visemes", vrc.VisemeBlendShapes.Length + " blendshapes");
            }
            else if (TryDetectVisemes(ctx, cvrAvatar, targetFace))
            {
                // Reported inside.
                if (vrc.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly)
                {
                    ctx.Report.Approximated(Category, "Visemes were parameter-driven in VRChat",
                        "This avatar drove its visemes from its own animator (\"Viseme Parameter " +
                        "Only\"), a system that has no feed here. ChilloutVR's native visemes were " +
                        "wired to the same blendshapes instead — the client writes them every " +
                        "frame after the animator, so lip sync works and the old animator system " +
                        "simply loses the shapes it can no longer reach.");
                }
            }
            else
            {
                ctx.Report.Warning(Category, "Visemes",
                    "None on the VRC descriptor, and no standard viseme blendshapes (vrc.v_aa / v_aa / aa …) " +
                    "on the face mesh — the avatar will have no lip sync in ChilloutVR.");
            }

            // --- Blinking ------------------------------------------------------------
            if (vrc.customEyeLookSettings.eyelidType == VRCAvatarDescriptor.EyelidType.Bones)
            {
                // Name the cause before the fallback runs, or "none
                // found" reads like the avatar has no blink at all.
                ctx.Report.Approximated(Category, "Eyelids are bone-driven",
                    "This avatar blinks by rotating eyelid BONES, which ChilloutVR's blink cannot " +
                    "drive — its native blink is blendshape-only. Blink blendshapes are searched " +
                    "for on the face mesh instead; if none exist, the eyes will not blink here, " +
                    "and the fix is authoring a blink blendshape.");
            }
            string blinkShape = GetBlinkBlendshapeName(vrc, sourceFace, out Mesh eyelidMesh);
            if (!string.IsNullOrEmpty(blinkShape))
            {
                WireDescriptorBlink(ctx, cvrAvatar, blinkShape, eyelidMesh);
            }
            else if (ctx.Settings.wireBlinkBlendshapes && TryWireBlinkFromMesh(ctx, vrc, cvrAvatar))
            {
                // Reported inside.
            }
            else
            {
                ctx.Report.Warning(Category, "Blink blendshape", "None found (eye look eyelid blendshapes not set).");
            }

            // --- Eye look ------------------------------------------------------------
            ConvertEyeLook(ctx, cvrAvatar, vrc);

            // --- Advanced settings container ----------------------------------------
            cvrAvatar.avatarUsesAdvancedSettings = true;
            cvrAvatar.avatarSettings = new CVRAdvancedAvatarSettings
            {
                settings = new System.Collections.Generic.List<CVRAdvancedSettingsEntry>(),
                initialized = true
            };

            EditorUtility.SetDirty(cvrAvatar);
        }

        static void ConvertEyeLook(BridgeContext ctx, CVRAvatar cvrAvatar, VRCAvatarDescriptor vrc)
        {
            if (!vrc.enableEyeLook)
            {
                cvrAvatar.useEyeMovement = false;
                ctx.Report.Converted(Category, "Eye movement disabled",
                    "The VRChat avatar has eye look turned off, so ChilloutVR's idle eye movement " +
                    "is turned off too — the CCK's default would have added eye darting the author " +
                    "never gave this avatar.");
                return;
            }

            var settings = vrc.customEyeLookSettings;
            if (settings.leftEye == null && settings.rightEye == null)
            {
                ctx.Report.Skipped(Category, "Eye look",
                    "VRChat eye look is enabled but names no eye bones (blendshape-only or " +
                    "unconfigured), so there is nothing to measure gaze limits from. Set up Eye " +
                    "Look Settings on the CVRAvatar by hand if this avatar's eyes should wander.");
                return;
            }

            // The rest pose the four directional poses are measured against. VRChat treats an
            // unset straight pose as identity, and so does this.
            var eyes = new List<CVRAvatar.EyeMovementInfoEye>();
            void Add(Transform sourceEye, bool isLeft)
            {
                if (sourceEye == null)
                {
                    return;
                }
                Transform target = ctx.FindInTarget(sourceEye);
                if (target == null)
                {
                    return;
                }

                Quaternion Straight(VRCAvatarDescriptor.CustomEyeLookSettings.EyeRotations r) =>
                    r == null ? Quaternion.identity : (isLeft ? r.left : r.right);
                float Travel(VRCAvatarDescriptor.CustomEyeLookSettings.EyeRotations pose)
                {
                    if (pose == null)
                    {
                        return 0f;
                    }
                    return Quaternion.Angle(Straight(settings.eyesLookingStraight),
                        isLeft ? pose.left : pose.right);
                }

                // Toward the nose is "in": the left eye's in-limit is its looking-RIGHT pose.
                var inward = isLeft ? settings.eyesLookingRight : settings.eyesLookingLeft;
                var outward = isLeft ? settings.eyesLookingLeft : settings.eyesLookingRight;

                // CVR's limits are signed euler bounds, not magnitudes.
                // Down and In must be negative or both clamps collapse
                // to a point and pin the eyes.
                eyes.Add(new CVRAvatar.EyeMovementInfoEye
                {
                    isLeft = isLeft,
                    eyeTransform = target,
                    eyeAngleLimitUp = Travel(settings.eyesLookingUp),
                    eyeAngleLimitDown = -Travel(settings.eyesLookingDown),
                    eyeAngleLimitIn = -Travel(inward),
                    eyeAngleLimitOut = Travel(outward)
                });
            }
            Add(settings.leftEye, isLeft: true);
            Add(settings.rightEye, isLeft: false);

            if (eyes.Count == 0)
            {
                ctx.Report.Warning(Category, "Eye look",
                    "The VRChat descriptor names eye bones, but they could not be found on the " +
                    "converted copy — gaze was not set up.");
                return;
            }

            cvrAvatar.useEyeMovement = true;
            cvrAvatar.eyeMovementInfo = new CVRAvatar.EyeMovementInfo
            {
                type = CVRAvatar.CVRAvatarEyeLookMode.Transform,
                eyes = eyes.ToArray()
            };

            var sample = eyes[0];
            ctx.Report.Converted(Category, "Eye movement",
                $"{eyes.Count} eye(s) set up in Transform mode, gaze limits measured from the " +
                $"VRChat poses (up {sample.eyeAngleLimitUp:0.#}°, down {Mathf.Abs(sample.eyeAngleLimitDown):0.#}°, " +
                $"in {Mathf.Abs(sample.eyeAngleLimitIn):0.#}°, out {sample.eyeAngleLimitOut:0.#}°) — the angle " +
                "between looking-straight and each directional pose IS that direction's limit, so " +
                "these are measured off your avatar rather than defaulted. (Stored signed: ChilloutVR " +
                "wants Down and In negative.)");
        }

        static void WireJawBoneLipSync(BridgeContext ctx, CVRAvatar cvrAvatar)
        {
            cvrAvatar.useVisemeLipsync = true;
            cvrAvatar.visemeMode = CVRAvatar.CVRAvatarVisemeMode.JawBone;

            var animator = ctx.Target.GetComponent<Animator>();
            bool hasJaw = animator != null && animator.isHuman && animator.avatar != null
                          && animator.GetBoneTransform(HumanBodyBones.Jaw) != null;
            if (hasJaw)
            {
                ctx.Report.Converted(Category, "Jaw-flap lip sync",
                    "ChilloutVR drives the rig's Jaw bone from voice loudness. It reads the jaw off the " +
                    "humanoid rig itself, so nothing else needed transferring.");
            }
            else
            {
                ctx.Report.Warning(Category, "Jaw-flap lip sync has no Jaw bone to drive",
                    "The avatar's descriptor asks for jaw-flap lip sync, but the rig is not humanoid or has " +
                    "no bone mapped to Jaw. ChilloutVR reads the jaw from the humanoid rig, so lip sync will " +
                    "do nothing until Jaw is mapped in the rig's Avatar configuration.");
            }
        }

        static void WireSingleBlendshapeLipSync(BridgeContext ctx, CVRAvatar cvrAvatar, string shapeName)
        {
            cvrAvatar.useVisemeLipsync = true;
            cvrAvatar.visemeMode = CVRAvatar.CVRAvatarVisemeMode.SingleBlendshape;
            cvrAvatar.visemeBlendshapes = new string[15];
            cvrAvatar.visemeBlendshapes[0] = shapeName;

            var mesh = cvrAvatar.bodyMesh != null ? cvrAvatar.bodyMesh.sharedMesh : null;
            if (mesh != null && mesh.GetBlendShapeIndex(shapeName) < 0)
            {
                ctx.Report.Warning(Category, $"Jaw-flap blendshape \"{shapeName}\" is not on the face mesh",
                    $"ChilloutVR resolves it against \"{cvrAvatar.bodyMesh.name}\", which has no shape by that " +
                    "name, so lip sync will do nothing. Check the face mesh on the CVRAvatar.");
                return;
            }
            ctx.Report.Converted(Category, "Jaw-flap blendshape lip sync",
                $"\"{shapeName}\" driven from voice loudness.");
        }

        static bool TryDetectVisemes(BridgeContext ctx, CVRAvatar cvrAvatar, SkinnedMeshRenderer face)
        {
            var visemes = AvatarFeatureDetect.DetectVisemes(face != null ? face.sharedMesh : null);
            if (visemes == null)
            {
                return false;
            }
            cvrAvatar.useVisemeLipsync = true;
            cvrAvatar.visemeMode = CVRAvatar.CVRAvatarVisemeMode.Visemes;
            cvrAvatar.visemeBlendshapes = visemes;
            int found = visemes.Count(v => !string.IsNullOrEmpty(v));
            ctx.Report.Approximated(Category, "Visemes auto-detected",
                $"The VRChat descriptor declared none, so {found} of 15 were matched by name on " +
                $"\"{face.name}\". Verify the mapping on the CVRAvatar.");
            return true;
        }

        static bool AnimatedBlinkShape(BridgeContext ctx, VRCAvatarDescriptor vrc, out string drivenShape, out string drivenBy)
        {
            drivenShape = null;
            drivenBy = null;
            // The source controllers, off the descriptor. This runs long
            // before AnimatorMerger; ctx.MergedController is still null.
            var clips = new HashSet<AnimationClip>();
            void Collect(VRCAvatarDescriptor.CustomAnimLayer[] layers)
            {
                if (layers == null)
                {
                    return;
                }
                foreach (var layer in layers)
                {
                    if (layer.animatorController == null)
                    {
                        continue;
                    }
                    foreach (var clip in layer.animatorController.animationClips)
                    {
                        if (clip != null)
                        {
                            clips.Add(clip);
                        }
                    }
                }
            }
            Collect(vrc != null ? vrc.baseAnimationLayers : null);
            Collect(vrc != null ? vrc.specialAnimationLayers : null);
            if (ctx.MergedController != null)
            {
                foreach (var clip in ctx.MergedController.animationClips)
                {
                    if (clip != null)
                    {
                        clips.Add(clip);
                    }
                }
            }

            foreach (var clip in clips)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    const string prefix = "blendShape.";
                    if (!binding.propertyName.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string shape = binding.propertyName.Substring(prefix.Length);
                    // "blink" anywhere in the name, which covers vrc.Blink, Blink L/R, EyeBlink
                    // and the handful of naming schemes avatars actually use. Deliberately loose:
                    // a false positive costs the client's blink on an avatar that has its own,
                    // while a false negative is two systems fighting.
                    if (shape.IndexOf("blink", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        drivenShape = shape;
                        drivenBy = $"\"{clip.name}\" drives \"{shape}\"";
                        return true;
                    }
                }
            }
            return false;
        }

        static bool TryWireBlinkFromMesh(BridgeContext ctx, VRCAvatarDescriptor vrc, CVRAvatar cvrAvatar)
        {
            var mesh = cvrAvatar.bodyMesh != null ? cvrAvatar.bodyMesh.sharedMesh : null;
            if (mesh == null)
            {
                return false;
            }
            AvatarFeatureDetect.DetectBlinkShapes(mesh, out string left, out string right, out string combined);
            if (left == null && right == null && combined == null)
            {
                return false;
            }

            // An avatar that blinks itself gets the native blink, but
            // the decision defers to the merge: the blinking layer
            // names the shape, not a name match here. Native blink
            // stays off here so the two never overlap.
            if (AnimatedBlinkShape(ctx, vrc, out _, out _))
            {
                cvrAvatar.useBlinkBlendshapes = false;
                ctx.AnimatorBlinkPending = true;
                return true;    // reported by the merge pass, which knows what it actually did
            }

            cvrAvatar.useBlinkBlendshapes = true;
            if (cvrAvatar.blinkBlendshape == null || cvrAvatar.blinkBlendshape.Length < 4)
            {
                cvrAvatar.blinkBlendshape = new string[4];
            }

            if (left != null && right != null)
            {
                cvrAvatar.blinkBlendshape[0] = left;   // Left Blink slot
                cvrAvatar.blinkBlendshape[1] = right;  // Right Blink slot
                AvatarFeatureDetect.SetBlinkMode(cvrAvatar, "Separate");
                ctx.Report.Converted(Category, "Blink blendshapes auto-detected",
                    $"Descriptor set none; wired CVR blink to \"{left}\" / \"{right}\" (Separate). Enables " +
                    "CVR's native blink and the CVR-VRCFT rig's eye-tracking-off fallback. Verify L/R aren't swapped.");
            }
            else if (combined != null)
            {
                cvrAvatar.blinkBlendshape[0] = combined;
                AvatarFeatureDetect.SetBlinkMode(cvrAvatar, "Combined");
                ctx.Report.Converted(Category, "Blink blendshape auto-detected",
                    $"Descriptor set none; wired CVR blink to \"{combined}\" (Combined).");
            }
            else
            {
                // Half a pair and no combined shape closes one eye only.
                string single = left ?? right;
                cvrAvatar.blinkBlendshape[0] = single;
                AvatarFeatureDetect.SetBlinkMode(cvrAvatar, "Combined");
                ctx.Report.Warning(Category, "Blink blendshape auto-detected",
                    $"Descriptor set none, and only \"{single}\" was found — a {(left != null ? "left" : "right")}-side " +
                    "shape with no counterpart, so blinking will close one eye. Wired as Combined; if the " +
                    "other eye's shape exists under a name without a side marker, assign it on the CVRAvatar " +
                    "and set Blink Mode to Separate.");
            }
            return true;
        }

        static void WireDescriptorBlink(BridgeContext ctx, CVRAvatar cvrAvatar, string blinkShape, Mesh eyelidMesh)
        {
            cvrAvatar.useBlinkBlendshapes = true;
            if (cvrAvatar.blinkBlendshape == null || cvrAvatar.blinkBlendshape.Length < 4)
            {
                cvrAvatar.blinkBlendshape = new string[4];
            }

            string lower = blinkShape.ToLowerInvariant();
            bool namesLeft = AvatarFeatureDetect.IsSide(lower, "left", 'l');
            bool namesRight = !namesLeft && AvatarFeatureDetect.IsSide(lower, "right", 'r');

            AvatarFeatureDetect.DetectBlinkShapes(eyelidMesh, out string foundLeft, out string foundRight, out _);
            string left = namesLeft ? blinkShape : foundLeft;
            string right = namesRight ? blinkShape : foundRight;

            if (!string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right))
            {
                cvrAvatar.blinkBlendshape[0] = left;
                cvrAvatar.blinkBlendshape[1] = right;
                AvatarFeatureDetect.SetBlinkMode(cvrAvatar, "Separate");
                ctx.Report.Converted(Category, "Blink blendshapes",
                    $"\"{left}\" / \"{right}\" (Separate). " + (namesLeft || namesRight
                        ? $"The descriptor named only \"{blinkShape}\" — VRChat has a single eyelid slot — " +
                          "so the other side was matched on the same mesh, otherwise one eye would never close."
                        : $"The descriptor named \"{blinkShape}\", but this mesh also carries a separate " +
                          "left/right pair, which ChilloutVR can drive independently. To go back to the " +
                          $"single shape, set Blink Mode to Combined and put \"{blinkShape}\" in Left Blink."));
                return;
            }

            if (namesLeft || namesRight)
            {
                // Half a pair with nothing to pair it with: one eye is all this shape can close.
                cvrAvatar.blinkBlendshape[0] = blinkShape;
                AvatarFeatureDetect.SetBlinkMode(cvrAvatar, "Combined");
                ctx.Report.Warning(Category, "Blink blendshape",
                    $"The descriptor named \"{blinkShape}\", which closes one eye, and no matching " +
                    $"{(namesLeft ? "right" : "left")}-side shape was found on the same mesh. Wired as " +
                    "Combined so it at least drives blinking; if the other eye's shape exists under a name " +
                    "without a side marker, assign it on the CVRAvatar and set Blink Mode to Separate.");
                return;
            }

            cvrAvatar.blinkBlendshape[0] = blinkShape;
            AvatarFeatureDetect.SetBlinkMode(cvrAvatar, "Combined");
            ctx.Report.Converted(Category, "Blink blendshape", $"\"{blinkShape}\" (Combined).");
        }

        static string GetBlinkBlendshapeName(VRCAvatarDescriptor vrc, SkinnedMeshRenderer face, out Mesh eyelidMesh)
        {
            eyelidMesh = null;
            if (vrc.customEyeLookSettings.eyelidType != VRCAvatarDescriptor.EyelidType.Blendshapes)
            {
                return null;
            }
            int[] eyelids = vrc.customEyeLookSettings.eyelidsBlendshapes;
            if (eyelids == null || eyelids.Length < 1 || eyelids[0] == -1)
            {
                return null;
            }
            // VRChat stores eyelid blendshapes against the eyelids mesh (usually the face mesh).
            var mesh = vrc.customEyeLookSettings.eyelidsSkinnedMesh != null
                ? vrc.customEyeLookSettings.eyelidsSkinnedMesh.sharedMesh
                : face != null ? face.sharedMesh : null;
            if (mesh == null || eyelids[0] >= mesh.blendShapeCount)
            {
                return null;
            }
            eyelidMesh = mesh;
            return mesh.GetBlendShapeName(eyelids[0]);
        }

    }
}
#endif
