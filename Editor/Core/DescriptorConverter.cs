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

            if (targetFace != null)
            {
                cvrAvatar.bodyMesh = targetFace;
                ctx.Report.Converted(Category, "Face mesh", targetFace.name);
            }
            else
            {
                ctx.Report.Warning(Category, "Face mesh", "No viseme skinned mesh set on the VRC descriptor.");
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
            else if (vrc.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.JawFlapBone)
            {
                ctx.Report.Skipped(Category, "Jaw-flap lip sync", "CVR conversion only supports viseme blendshapes.");
            }
            else
            {
                ctx.Report.Warning(Category, "Visemes", "No viseme blendshapes found on the VRC descriptor.");
            }

            // --- Blinking ------------------------------------------------------------
            string blinkShape = GetBlinkBlendshapeName(vrc, sourceFace);
            if (!string.IsNullOrEmpty(blinkShape))
            {
                cvrAvatar.useBlinkBlendshapes = true;
                if (cvrAvatar.blinkBlendshape == null || cvrAvatar.blinkBlendshape.Length < 1)
                {
                    cvrAvatar.blinkBlendshape = new string[4];
                }
                cvrAvatar.blinkBlendshape[0] = blinkShape;
                ctx.Report.Converted(Category, "Blink blendshape", blinkShape);
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
            else
            {
                string single = combined ?? left ?? right;
                cvrAvatar.blinkBlendshape[0] = single;
                AvatarFeatureDetect.SetBlinkMode(cvrAvatar, "Combined");
                ctx.Report.Converted(Category, "Blink blendshape auto-detected",
                    $"Descriptor set none; wired CVR blink to \"{single}\" (Combined).");
            }
            return true;
        }

        static string GetBlinkBlendshapeName(VRCAvatarDescriptor vrc, SkinnedMeshRenderer face)
        {
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
            return mesh.GetBlendShapeName(eyelids[0]);
        }
    }
}
#endif
