#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System.Collections.Generic;
using UnityEngine;
using MagicaCloth2;

namespace AvatarBridge
{
    /// <summary>
    /// Builds a body collider set from the avatar's humanoid rig.
    ///
    /// Plenty of VRChat avatars never define PhysBone colliders — VRChat supplies global
    /// hand/finger colliders, so authors often don't bother with a body. Converted straight
    /// across, those chains reach ChilloutVR with no collision at all and hair, ears and tails
    /// sweep through the body. This fills that gap using bones AvatarBridge already reads,
    /// sizing each capsule from the avatar's own bone lengths rather than assuming human
    /// proportions — which matters here, since a lot of these avatars aren't human-shaped.
    ///
    /// Only applied to chains that arrived with no colliders of their own: an author who set
    /// colliders up made a deliberate choice, and piling more on top invites jitter.
    /// </summary>
    public static class MagicaBodyColliders
    {
        const string Category = "PhysBones -> MagicaCloth2";

        /// <summary>One generated collider: which bone it hangs off and how it's sized.</summary>
        struct Segment
        {
            public HumanBodyBones Bone;
            public HumanBodyBones Toward;   // the bone that gives this one its length/direction
            public float RadiusFactor;      // radius as a fraction of that length
            public string Label;
        }

        // Torso capsules are fat relative to their length, limbs are slim. Every size is derived
        // from the avatar's measured bone lengths, so this scales with the character.
        static readonly Segment[] Segments =
        {
            new Segment { Bone = HumanBodyBones.Hips,          Toward = HumanBodyBones.Spine,         RadiusFactor = 0.62f, Label = "Hips" },
            new Segment { Bone = HumanBodyBones.Spine,         Toward = HumanBodyBones.Chest,         RadiusFactor = 0.55f, Label = "Spine" },
            new Segment { Bone = HumanBodyBones.Chest,         Toward = HumanBodyBones.Neck,          RadiusFactor = 0.52f, Label = "Chest" },
            new Segment { Bone = HumanBodyBones.LeftUpperArm,  Toward = HumanBodyBones.LeftLowerArm,  RadiusFactor = 0.17f, Label = "UpperArm.L" },
            new Segment { Bone = HumanBodyBones.RightUpperArm, Toward = HumanBodyBones.RightLowerArm, RadiusFactor = 0.17f, Label = "UpperArm.R" },
            new Segment { Bone = HumanBodyBones.LeftLowerArm,  Toward = HumanBodyBones.LeftHand,      RadiusFactor = 0.14f, Label = "LowerArm.L" },
            new Segment { Bone = HumanBodyBones.RightLowerArm, Toward = HumanBodyBones.RightHand,     RadiusFactor = 0.14f, Label = "LowerArm.R" },
            new Segment { Bone = HumanBodyBones.LeftUpperLeg,  Toward = HumanBodyBones.LeftLowerLeg,  RadiusFactor = 0.21f, Label = "UpperLeg.L" },
            new Segment { Bone = HumanBodyBones.RightUpperLeg, Toward = HumanBodyBones.RightLowerLeg, RadiusFactor = 0.21f, Label = "UpperLeg.R" },
            new Segment { Bone = HumanBodyBones.LeftLowerLeg,  Toward = HumanBodyBones.LeftFoot,      RadiusFactor = 0.16f, Label = "LowerLeg.L" },
            new Segment { Bone = HumanBodyBones.RightLowerLeg, Toward = HumanBodyBones.RightFoot,     RadiusFactor = 0.16f, Label = "LowerLeg.R" },
        };

        /// <summary>
        /// Generates the body collider set, or an empty list when the avatar isn't humanoid
        /// (nothing to derive sizes from) or the feature is switched off.
        /// </summary>
        public static List<ColliderComponent> Build(BridgeContext ctx)
        {
            var result = new List<ColliderComponent>();
            if (!ctx.Settings.generateBodyColliders)
            {
                return result;
            }

            var animator = ctx.TargetAnimator;
            if (animator == null || !animator.isHuman)
            {
                ctx.Report.Approximated(Category, "Body colliders not generated",
                    "The avatar isn't a Humanoid rig, so there are no mapped bones to build them from. " +
                    "Chains without colliders of their own will pass through the body.");
                return result;
            }

            var root = new GameObject("AvatarBridge_BodyColliders");
            root.transform.SetParent(ctx.Target.transform, false);

            foreach (var segment in Segments)
            {
                var bone = animator.GetBoneTransform(segment.Bone);
                var toward = animator.GetBoneTransform(segment.Toward);
                if (bone == null || toward == null)
                {
                    continue; // optional bone (chest, neck…) this rig doesn't map
                }

                float length = Vector3.Distance(bone.position, toward.position);
                if (length <= 0.0001f)
                {
                    continue;
                }
                float radius = length * segment.RadiusFactor;

                var go = new GameObject("BodyCollider_" + segment.Label);
                go.transform.SetParent(bone, false);
                // Centre the capsule on the segment and point its local Y down the bone.
                go.transform.localPosition = bone.InverseTransformPoint(
                    Vector3.Lerp(bone.position, toward.position, 0.5f));
                go.transform.localRotation = Quaternion.FromToRotation(
                    Vector3.up, bone.InverseTransformPoint(toward.position).normalized);
                go.transform.localScale = Vector3.one;

                var capsule = go.AddComponent<MagicaCapsuleCollider>();
                capsule.direction = MagicaCapsuleCollider.Direction.Y;
                // Magica measures a capsule by its total length, so the straight sections plus
                // both hemispherical caps.
                capsule.SetSize(radius, radius, Mathf.Max(length, radius * 2f));
                result.Add(capsule);
            }

            // The head is close enough to a ball that a sphere beats a capsule, and it's the
            // one most hair actually needs.
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var neck = animator.GetBoneTransform(HumanBodyBones.Neck)
                       ?? animator.GetBoneTransform(HumanBodyBones.Chest);
            if (head != null && neck != null)
            {
                float headRadius = Vector3.Distance(head.position, neck.position) * 0.72f;
                if (headRadius > 0.0001f)
                {
                    var go = new GameObject("BodyCollider_Head");
                    go.transform.SetParent(head, false);
                    // Sit the sphere above the head bone, where the skull actually is.
                    go.transform.localPosition = head.InverseTransformPoint(
                        head.position + (head.position - neck.position).normalized * headRadius * 0.5f);
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;

                    var sphere = go.AddComponent<MagicaSphereCollider>();
                    sphere.SetSize(headRadius);
                    result.Add(sphere);
                }
            }

            if (result.Count == 0)
            {
                Object.DestroyImmediate(root);
            }
            return result;
        }
    }
}
#endif
