#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
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

            // --- Viewpoint & voice ---------------------------------------------------
            cvrAvatar.viewPosition = vrc.ViewPosition;
            cvrAvatar.voicePosition = vrc.ViewPosition;

            var animator = ctx.TargetAnimator;
            Transform head = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Head)
                : null;
            if (head != null)
            {
                // VRChat emits voice from the head bone; approximate that.
                Vector3 voice = ctx.Target.transform.InverseTransformPoint(head.position);
                voice.Scale(ctx.Target.transform.localScale);
                cvrAvatar.voicePosition = voice;
            }
            ctx.Report.Converted(Category, "Viewpoint and voice position");

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

            if (vrc.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape &&
                vrc.VisemeBlendShapes != null && vrc.VisemeBlendShapes.Length > 0)
            {
                cvrAvatar.useVisemeLipsync = true;
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
            else if (vrc.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.JawFlapBone)
            {
                ctx.Report.Skipped(Category, "Jaw-flap lip sync", "CVR conversion only supports viseme blendshapes.");
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
        /// Fallback when the descriptor declares no visemes: match the 15 standard viseme
        /// blendshapes on the face mesh by their conventional names. Avatars that shipped
        /// without lip sync configured get it for free in ChilloutVR.
        /// </summary>
        static bool TryDetectVisemes(BridgeContext ctx, CVRAvatar cvrAvatar, SkinnedMeshRenderer face)
        {
            var visemes = AvatarFeatureDetect.DetectVisemes(face != null ? face.sharedMesh : null);
            if (visemes == null)
            {
                return false;
            }
            cvrAvatar.useVisemeLipsync = true;
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
        /// VRChat has exactly one eyelid slot and expects a shape that closes both eyes, while
        /// ChilloutVR has two slots plus a mode that says how to read them. Authors do point
        /// VRChat's single slot at one half of an L/R pair — "vrc.blink_left" — because in
        /// VRChat nothing else is on offer. Copying that name into CVR's first slot and leaving
        /// the mode alone gives Separate mode with Right Blink empty, so only one eye ever
        /// closes. So the mode is always set explicitly here, and a side-specific shape sends us
        /// looking for its partner on the same mesh.
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

            if (namesLeft || namesRight)
            {
                AvatarFeatureDetect.DetectBlinkShapes(eyelidMesh, out string foundLeft, out string foundRight, out _);
                string left = namesLeft ? blinkShape : foundLeft;
                string right = namesRight ? blinkShape : foundRight;

                if (!string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right))
                {
                    cvrAvatar.blinkBlendshape[0] = left;
                    cvrAvatar.blinkBlendshape[1] = right;
                    AvatarFeatureDetect.SetBlinkMode(cvrAvatar, "Separate");
                    ctx.Report.Converted(Category, "Blink blendshapes",
                        $"\"{left}\" / \"{right}\" (Separate). The VRChat descriptor only named " +
                        $"\"{blinkShape}\" — it has a single eyelid slot — so the other side was matched " +
                        "on the same mesh, otherwise only one eye would blink.");
                    return;
                }

                // Side-specific with nothing to pair it with: one eye is all this shape can close.
                cvrAvatar.blinkBlendshape[0] = blinkShape;
                AvatarFeatureDetect.SetBlinkMode(cvrAvatar, "Combined");
                ctx.Report.Warning(Category, "Blink blendshape",
                    $"The descriptor named \"{blinkShape}\", which closes one eye, and no matching " +
                    $"{(namesLeft ? "right" : "left")}-side shape was found on the same mesh. Wired as " +
                    "Combined so it at least drives blinking; if the avatar has a separate shape for the " +
                    "other eye, set Blink Mode to Separate and assign both on the CVRAvatar.");
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
