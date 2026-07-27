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
            cvrAvatar.viewPosition = vrc.ViewPosition;
            cvrAvatar.voicePosition = vrc.ViewPosition;   // replaced below, once the face is known
            var animator = ctx.TargetAnimator;

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
            // After the face mesh, because the best answer is measured from its viseme shapes.
            cvrAvatar.voicePosition = MouthLocator.Locate(ctx.Target, targetFace, vrc.VisemeBlendShapes,
                animator, cvrAvatar.viewPosition, out var mouthMethod, out string mouthDetail);
            MouthLocator.Report(ctx, Category, cvrAvatar.voicePosition, mouthMethod, mouthDetail);

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

                eyes.Add(new CVRAvatar.EyeMovementInfoEye
                {
                    isLeft = isLeft,
                    eyeTransform = target,
                    eyeAngleLimitUp = Travel(settings.eyesLookingUp),
                    eyeAngleLimitDown = Travel(settings.eyesLookingDown),
                    eyeAngleLimitIn = Travel(inward),
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
                $"VRChat poses (up {sample.eyeAngleLimitUp:0.#}°, down {sample.eyeAngleLimitDown:0.#}°, " +
                $"in {sample.eyeAngleLimitIn:0.#}°, out {sample.eyeAngleLimitOut:0.#}°) — the angle " +
                "between looking-straight and each directional pose IS that direction's limit, so " +
                "these are measured off your avatar rather than defaulted.");
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
