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
    /// <summary>
    /// Converts the VRCAvatarDescriptor basics onto a CVRAvatar component:
    /// viewpoint, voice position, face mesh, visemes and blinking.
    /// </summary>
    public static class DescriptorConverter
    {
        const string Category = "Avatar descriptor";

        public static void Run(BridgeContext ctx)
        {
            var vrc = ctx.SourceDescriptor;

            var cvrAvatar = ctx.Target.GetComponent<CVRAvatar>();
            if (cvrAvatar == null)
            {
                cvrAvatar = ctx.Target.AddComponent<CVRAvatar>();
            }
            ctx.CvrAvatar = cvrAvatar;

            // --- Viewpoint -----------------------------------------------------------
            // The CCK's own Auto placement (between the eye bones) — maintainer's call: one
            // convention for every avatar beats per-source derivation. The author's VRChat
            // viewpoint stays as the fallback for rigs the Auto chain can't read.
            var animator = ctx.TargetAnimator;
            bool autoView = AvatarFeatureDetect.CckAutoViewPosition(ctx.Target, animator, out var viewAuto);

            // The AUTHOR'S viewpoint wins when there is one.
            //
            // This used to prefer the CCK's Auto placement, for one convention across every
            // avatar. The convention is worth less than being right: Auto reads the humanoid eye
            // bones, and on rigs where those bones are not where the eyes are — a robot avatar
            // whose eye mapping sat 6 cm off-centre and 9 cm behind the face — it produces a
            // confidently wrong answer, and so does the CCK's own button. The descriptor value is
            // the one a human placed by eye and then shipped, and on that avatar it matched the
            // hand-corrected position on X exactly and Z to half a millimetre.
            //
            // Copied, NOT scaled. An earlier revision multiplied this by the root's localScale,
            // on the reasoning that VRChat stores an unscaled local offset — that reasoning was
            // wrong. VRChat's viewpoint is authored against the avatar at the size it ships at,
            // so it is already the world-metre offset ChilloutVR stores. On a scale-1 avatar the
            // two are identical, which is why it went unnoticed; on a root scaled 1.374 it put
            // the viewpoint 70 cm above the head. The avatar that caught it settles it in two
            // axes: unscaled Z is 0.1105 against a hand-corrected 0.1095, and X is 0 in both.
            bool haveAuthored = vrc.ViewPosition != Vector3.zero;
            var authored = vrc.ViewPosition;

            // ...but only when it is anywhere near the head.
            //
            // Preferring the author's value assumes a human placed it by eye and shipped it. That
            // holds right up until it doesn't: a taur base shipped a viewpoint at its HIPS, 0.6 m
            // from its head bone, while the CCK's Auto placement landed on the face. The check
            // that catches this already ran — the report warned "Viewpoint lands 0.6 m from the
            // head bone" — it just warned about a value we had already chosen and written.
            //
            // A viewpoint is not a matter of taste: it is where the player's eyes go, and a rig
            // whose head bone is half a head-height away from it is not expressing a preference.
            // So when the authored value fails that check and Auto passes it, Auto wins.
            bool authoredIsWrong = false;
            if (haveAuthored && autoView
                && AvatarFeatureDetect.HeadDistance(ctx.Target, animator, authored, out float authoredOff, out float tolerance)
                && AvatarFeatureDetect.HeadDistance(ctx.Target, animator, viewAuto, out float autoOff, out _))
            {
                authoredIsWrong = authoredOff > tolerance && autoOff <= tolerance;
                if (authoredIsWrong)
                {
                    ctx.Report.Approximated(Category, "Viewpoint — the CCK's Auto placement, not the author's",
                        $"The viewpoint this avatar shipped with in VRChat lands {authoredOff:0.##} m from its " +
                        $"head bone, past the {tolerance:0.##} m this rig's own proportions allow, so it is in " +
                        "the wrong place rather than merely unusual — you would spawn looking out of the " +
                        $"avatar's body. The CCK's Auto placement (midpoint of the eye bones) lands {autoOff:0.##} m " +
                        "from the head and was used instead. The author's value is normally preferred, because a " +
                        "human placed it by eye; it is only overridden when it fails a check the rig itself " +
                        "settles. Drag the gizmo on the CVRAvatar if neither is right.");
                }
            }

            var humanoidView = haveAuthored && !authoredIsWrong
                ? authored
                : autoView ? viewAuto : AvatarFeatureDetect.EstimateViewPosition(ctx.Target, animator);

            // ...unless the humanoid rig is a DECOY and none of the above is looking at the
            // avatar at all. See AvatarFeatureDetect.DecoyRigAnchors: on a quadruped whose
            // humanoid map points at a hidden stand-in skeleton, the author's VRChat viewpoint,
            // the CCK's Auto button and our own estimate all agree with each other and all sit on
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
            else if (haveAuthored && autoView && !authoredIsWrong)
            {
                float apart = Vector3.Distance(authored, viewAuto);
                ctx.Report.Converted(Category, "Viewpoint — the author's own, from the VRChat descriptor",
                    $"Placed at the viewpoint this avatar shipped with in VRChat. The CCK's Auto " +
                    $"button (midpoint of the eye bones) would put it {apart:0.###} m away" +
                    (apart > 0.05f
                        ? " — far enough apart that this rig's eye bones are not where its eyes are, " +
                          "which is exactly the case the author's value exists to settle."
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
                // Reported together with the viewpoint above — the two share one cause and one fix.
                cvrAvatar.voicePosition = decoyVoice;
            }
            else if (autoVoice)
            {
                cvrAvatar.voicePosition = voiceAuto;
            }
            else
            {
                cvrAvatar.voicePosition = MouthLocator.Locate(ctx.Target, targetFace, vrc.VisemeBlendShapes,
                    animator, cvrAvatar.viewPosition, out var mouthMethod, out string mouthDetail);
                MouthLocator.Report(ctx, Category, cvrAvatar.voicePosition, mouthMethod, mouthDetail);
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
                bool hasJaw = animator != null && animator.isHuman &&
                              animator.GetBoneTransform(HumanBodyBones.Jaw) != null;
                ctx.Report.Converted(Category, "View & voice placed the CCK's own way",
                    (autoView
                        ? "View between the eye bones (this avatar's VRChat descriptor had no viewpoint set)"
                        : "View estimated from the avatar's bounds (no usable eye or head bones)") + "; " +
                    (autoVoice
                        ? (hasJaw ? "voice at the jaw bone" : "voice just ahead of the head bone (no jaw bone)")
                        : "voice measured from the viseme mesh") +
                    " — the same positions the Auto buttons on the CVRAvatar inspector produce.");
            }
            else if (haveAuthored)
            {
                bool hasJaw = animator != null && animator.isHuman &&
                              animator.GetBoneTransform(HumanBodyBones.Jaw) != null;
                ctx.Report.Converted(Category, "Voice position",
                    (autoVoice
                        ? (hasJaw
                            ? "At the jaw bone — a bone that exists is worth more than any estimate."
                            : "Just ahead of the head bone; this rig has no jaw bone to use. " +
                              "Offsets are applied along the AVATAR's forward, not the head bone's, " +
                              "because a bone's own axes can point anywhere.")
                        : "Measured from the viseme mesh — no jaw bone on this rig.") +
                    " VRChat has no voice position to inherit, so unlike the viewpoint this one is " +
                    "always derived. Check it with the CVRAvatar gizmo before uploading.");
            }

            // ChilloutVR has all three of VRChat's lip-sync styles, not just visemes:
            // CVRAvatarVisemeMode is { Visemes, SingleBlendshape, JawBone }. The field was never
            // written before, so every avatar relied on Visemes happening to be the zero value —
            // and a jaw-flap avatar was told, wrongly, that ChilloutVR couldn't do it.
            //
            // An explicit jaw-flap choice is honoured ahead of viseme auto-detection. The author
            // saying "this avatar flaps its jaw" outranks a name match on the face mesh.
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
            }
            else
            {
                ctx.Report.Warning(Category, "Visemes",
                    "None on the VRC descriptor, and no standard viseme blendshapes (vrc.v_aa / v_aa / aa …) " +
                    "on the face mesh — the avatar will have no lip sync in ChilloutVR.");
            }

            // --- Blinking ------------------------------------------------------------
            string blinkShape = GetBlinkBlendshapeName(vrc, sourceFace, out Mesh eyelidMesh);
            if (!string.IsNullOrEmpty(blinkShape))
            {
                WireDescriptorBlink(ctx, cvrAvatar, blinkShape, eyelidMesh);
            }
            else if (ctx.Settings.wireBlinkBlendshapes && TryWireBlinkFromMesh(ctx, cvrAvatar))
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

        /// <summary>
        /// Converts VRChat's eye look into ChilloutVR's eye movement — the gaze itself, not
        /// blinking, which is wired separately.
        ///
        /// The two describe the same thing in different encodings. VRChat stores five POSES:
        /// quaternions for straight/up/down/left/right per eye. ChilloutVR stores four LIMITS:
        /// degrees of travel up/down/in/out per eye, applied to an eye transform. A pose is a
        /// limit in disguise — the angle between looking-straight and looking-up IS the up
        /// limit — so each limit is measured with Quaternion.Angle rather than assumed.
        ///
        /// In/out need the side taken into account: an eye looking toward the nose is looking
        /// "in", so the LEFT eye's "in" comes from VRChat's looking-RIGHT pose and its "out"
        /// from looking-LEFT — mirrored for the right eye. ChilloutVR's own `isLeft` flag
        /// exists exactly to apply that asymmetry at runtime.
        ///
        /// Also honoured in the negative: a VRChat avatar with eye look DISABLED gets
        /// `useEyeMovement = false`, where the CCK's default is true — without this, every such
        /// avatar gained idle eye darting its author had turned off.
        /// </summary>
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

                // ChilloutVR's limits are SIGNED euler bounds, not magnitudes. The client clamps
                // pitch to [-Up, -Down] and yaw to [In, Out] (mirrored for the left eye) — read
                // straight out of EyeMovementEye.UpdateEyeRotation. Down and In must therefore be
                // NEGATIVE for the range to be a range: four positive values collapse both clamps
                // to a point, and Clamp(x, -30, -30) pins the eyes 30° upward permanently — which
                // is exactly how the first in-game test of this conversion looked.
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

        /// <summary>
        /// Fallback when the descriptor declares no visemes: match the 15 standard viseme
        /// blendshapes on the face mesh by their conventional names. Avatars that shipped
        /// without lip sync configured get it for free in ChilloutVR.
        /// </summary>
        /// <summary>
        /// VRChat's "Jaw Flap Bone" lip sync, which ChilloutVR supports with no wiring at all.
        ///
        /// CVRLipSyncJawBone takes the jaw straight off the humanoid rig — GetBoneTransform(Jaw)
        /// — and drives the "Jaw Close" muscle through a HumanPoseHandler from voice loudness.
        /// VRChat's own lipSyncJawBone reference isn't needed and isn't transferable; setting the
        /// mode is the entire conversion. It does require a humanoid rig with a mapped Jaw bone,
        /// which is the one thing worth checking, because without it the module silently does
        /// nothing.
        /// </summary>
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

        /// <summary>
        /// VRChat's "Jaw Flap Blend Shape" lip sync.
        ///
        /// CVRLipSyncSingleBlendshape scans visemeBlendshapes for the first entry that is neither
        /// empty nor "-none-" and resolves it against bodyMesh, so the single shape goes in slot
        /// 0 and the rest stay clear.
        /// </summary>
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

        /// <summary>
        /// Fallback when the VRChat descriptor has no eyelid/blink blendshape: detect blink
        /// shapes on the face mesh by name (e.g. "Blink L"/"Blink R" or a single "Blink") and
        /// turn on CVR's Eye Blink Settings. This also makes the CVR-VRCFT rig's
        /// "eye-tracking off" state blink — its ON/OFF clips drive useBlinkBlendshapes, which
        /// does nothing until blink shapes are wired here.
        /// </summary>
        static bool TryWireBlinkFromMesh(BridgeContext ctx, CVRAvatar cvrAvatar)
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
                // Half a pair and no combined shape — this can only ever close one eye.
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

        /// <summary>
        /// Wires CVR's Eye Blink Settings from the shape the VRChat descriptor named.
        ///
        /// VRChat has exactly one eyelid slot; ChilloutVR has two — Left Blink and Right Blink —
        /// plus a mode saying how to read them. So the descriptor can only ever describe half of
        /// what CVR wants, and what it names says little about what the mesh actually offers:
        /// authors point that single slot at one half of a pair ("vrc.blink_left") or at a
        /// both-eyes shape, in both cases because VRChat gives them no other choice.
        ///
        /// A separate L/R pair is therefore preferred whenever the mesh has one, whatever the
        /// descriptor named — CVR can drive the eyes independently, which is strictly more than
        /// the single slot could express. The mode is always set explicitly, because inheriting
        /// the CCK's default of Separate while filling only slot 0 closes one eye and nothing
        /// says why.
        /// </summary>
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
