#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    // Rebuilds the humanoid rig when the objects above the hips were moved or scaled after it
    // was made. Edit mode draws the body where it stands, but a running animator places the
    // hips from the rig's own record, so the avatar floats or sinks in Play mode and in game,
    // and full-body calibration measures a body the IK then does not drive.
    //
    // Only those objects and the hips' position are rewritten. The limbs keep the rig's
    // configured T-pose, which the current pose need not be.
    public static class RigSkeletonSync
    {
        const string Category = "Humanoid rig";

        public static void Run(BridgeContext ctx)
        {
            var animator = ctx.Target.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                return;
            }
            var root = animator.transform;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null || !hips.IsChildOf(root))
            {
                return;
            }

            var description = animator.avatar.humanDescription;
            var skeleton = description.skeleton.ToArray();
            if (skeleton.Length < 2)
            {
                return;
            }
            var chain = new List<Transform>();
            for (var t = hips; t != root; t = t.parent)
            {
                chain.Insert(0, t);
            }
            // Root entry aside, so a renamed clone root cannot stand in for an armature.
            var index = chain.Select(t => System.Array.FindIndex(skeleton, 1, skeleton.Length - 1, b => b.name == t.name)).ToArray();
            if (index.Any(i => i < 0))
            {
                return;   // a parent the rig never knew: the animator could not bind it either
            }

            var recorded = Matrix4x4.identity;
            foreach (int i in index)
            {
                recorded *= Matrix4x4.TRS(skeleton[i].position, skeleton[i].rotation, skeleton[i].scale);
            }
            Vector3 was = recorded.MultiplyPoint3x4(Vector3.zero);
            Vector3 now = root.InverseTransformPoint(hips.position);
            // A percent of the hips' height: a millimetre on a small avatar is a centimetre on a large one.
            if ((was - now).magnitude <= 0.01f * Mathf.Max(now.magnitude, 0.01f))
            {
                return;
            }

            var changed = new List<string>();
            for (int k = 0; k < chain.Count; k++)
            {
                var t = chain[k];
                var bone = skeleton[index[k]];
                bool hipsBone = t == hips;
                if (bone.position != t.localPosition || (!hipsBone && (bone.rotation != t.localRotation || bone.scale != t.localScale)))
                {
                    changed.Add(t.localScale == Vector3.one ? $"\"{t.name}\"" : $"\"{t.name}\" (scale {Format(t.localScale)})");
                }
                bone.position = t.localPosition;
                if (!hipsBone)
                {
                    bone.rotation = t.localRotation;
                    bone.scale = t.localScale;
                }
                skeleton[index[k]] = bone;
            }
            description.skeleton = skeleton;

            float off = was.y - now.y;
            string cost = $"The rig put the hips {was.y:0.00} m up and the body has them at {now.y:0.00} m, so once " +
                $"the animator ran, in Play mode and in game, the avatar {(off > 0 ? "floated" : "sank")} about " +
                $"{Mathf.Abs(off) * 100f:0} cm {(off > 0 ? "above" : "into")} the floor and full-body tracking " +
                "calibrated against the wrong height.";

            var ambiguous = JawUnmapper.AmbiguousBoneNames(root, description);
            if (ambiguous != null)
            {
                ctx.Report.Warning(Category, "Humanoid rig does not match the armature and cannot be rebuilt",
                    $"\"{ambiguous}\" appears more than once. Rename the duplicate and convert again. " + cost);
                return;
            }

            // Detached first, as in JawUnmapper, so the old rig does not validate the build.
            var previous = animator.avatar;
            var layers = JawUnmapper.Detach(animator);
            var rebuilt = AvatarBuilder.BuildHumanAvatar(root.gameObject, description);
            if (rebuilt == null || !rebuilt.isValid)
            {
                JawUnmapper.Reattach(animator, previous, layers);
                if (rebuilt != null)
                {
                    Object.DestroyImmediate(rebuilt);
                }
                ctx.Report.Warning(Category, "Humanoid rig does not match the armature and cannot be rebuilt",
                    "Unity refused the rebuild; the console says which check failed. Undo the scale or move on " +
                    $"{string.Join(", ", changed)} and set it in the model's import settings instead. " + cost);
                return;
            }
            JawUnmapper.SaveRig(ctx, rebuilt, previous.name, "_Matched", out _);
            JawUnmapper.Reattach(animator, rebuilt, layers);
            EditorUtility.SetDirty(animator);

            ctx.Report.Converted(Category, "Humanoid rig matched to the armature",
                $"{string.Join(", ", changed)} moved or scaled after the rig was made. {cost} Rebuilt from the " +
                "armature as it stands; the limbs keep the rig's own T-pose. Your source avatar is untouched.");
        }

        static string Format(Vector3 v) => Mathf.Approximately(v.x, v.y) && Mathf.Approximately(v.y, v.z)
            ? v.x.ToString("0.###") : $"{v.x:0.###}, {v.y:0.###}, {v.z:0.###}";
    }
}
#endif
