using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    // A soft-body chain driven by Unity rigidbodies and configurable joints instead of
    // MagicaCloth2, which caps stretch at 3%. Select a chain's root bone on a converted
    // avatar and run the menu item. Each bone gets a body; the bone follows its body
    // through a parent constraint, so a body moving away from its parent stretches the bone.
    //
    // The bodies sit under the avatar root, never under the bone's own parent: a dynamic
    // body whose parent transform moves is teleported with it, and feels none of that
    // motion. Under the root, the player's own movement still carries them (so a teleport
    // carries them too), while everything the animation does to the hips reaches them
    // through the joint. No colliders, so no layer or content filter applies.
    //
    // Self-test: -executeMethod AvatarBridge.Regression.JointSpike.SelfTest -quit
    public static class JointSpike
    {
        public const float Mass = 0.1f;
        public const float StretchRoom = 0.5f;    // of each bone's length, either way
        public const float LengthSpring = 120f;   // pulls a bone back to its length
        public const float SwingLimit = 45f;
        public const float SwingSpring = 4f;    // pulls a bone back to its rest angle

        [MenuItem("Tools/AvatarBridge Dev/Joint spike: selected chain roots")]
        static void BuildOnSelection()
        {
            foreach (var root in Selection.transforms) Build(root, true, null);
        }

        // Bodies root first.
        public static List<Rigidbody> Build(Transform chainRoot, bool undo, Transform holderParent)
        {
            var avatar = holderParent != null ? holderParent : AvatarRoot(chainRoot);
            foreach (var c in avatar.GetComponentsInChildren<Component>(true).Where(c => c != null && Covers(c, chainRoot)).ToList())
            {
                Debug.Log($"[JointSpike] removed {c.GetType().Name} on {c.name}: it simulates the same bones");
                if (undo) Undo.DestroyObjectImmediate(c); else Object.DestroyImmediate(c);
            }

            var holder = new GameObject(chainRoot.name + " (joint bodies)").transform;
            holder.SetParent(avatar, false);
            var anchorGo = new GameObject(chainRoot.name + " (joint anchor)");
            anchorGo.transform.SetParent(chainRoot.parent, false);
            var anchor = anchorGo.AddComponent<Rigidbody>();
            anchor.isKinematic = true;
            anchor.useGravity = false;
            if (undo)
            {
                Undo.RegisterCreatedObjectUndo(holder.gameObject, "Joint spike");
                Undo.RegisterCreatedObjectUndo(anchorGo, "Joint spike");
            }

            var bodies = new List<Rigidbody>();
            var parentBody = anchor;
            var chain = Longest(chainRoot);
            for (int i = 0; i < chain.Count; i++)
            {
                var bone = chain[i];
                float length = i + 1 < chain.Count ? Vector3.Distance(bone.position, chain[i + 1].position)
                             : i > 0 ? Vector3.Distance(bone.position, chain[i - 1].position) : 0.05f;
                var go = new GameObject(bone.name + " (body)");
                go.transform.SetParent(holder, false);
                go.transform.SetPositionAndRotation(bone.position, bone.rotation);
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = Mass;
                rb.angularDrag = 0.5f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                // With no collider Unity would give it a unit inertia, far too heavy to swing.
                rb.inertiaTensor = Vector3.one * Mathf.Max(1e-6f, Mass * length * length / 6f);
                rb.inertiaTensorRotation = Quaternion.identity;

                var j = go.AddComponent<ConfigurableJoint>();
                j.connectedBody = parentBody;
                j.anchor = Vector3.zero;
                j.autoConfigureConnectedAnchor = true;
                if (i == 0)
                {
                    // The root stays on its parent, as a PhysBone root does.
                    j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
                }
                else
                {
                    j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Limited;
                    float parentLength = Vector3.Distance(bone.position, chain[i - 1].position);
                    j.linearLimit = new SoftJointLimit { limit = parentLength * StretchRoom };
                    var pull = new JointDrive { positionSpring = LengthSpring, positionDamper = LengthSpring * 0.02f, maximumForce = float.MaxValue };
                    j.xDrive = j.yDrive = j.zDrive = pull;
                }
                j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Limited;
                j.lowAngularXLimit = new SoftJointLimit { limit = -SwingLimit };
                j.highAngularXLimit = new SoftJointLimit { limit = SwingLimit };
                j.angularYLimit = j.angularZLimit = new SoftJointLimit { limit = SwingLimit };
                j.rotationDriveMode = RotationDriveMode.Slerp;
                j.slerpDrive = new JointDrive { positionSpring = SwingSpring, positionDamper = SwingSpring * 0.05f, maximumForce = float.MaxValue };
                // Snaps a joint back if a hitch flings it past its limits, instead of exploding.
                j.projectionMode = JointProjectionMode.PositionAndRotation;
                j.projectionDistance = Mathf.Max(0.01f, length);
                j.projectionAngle = SwingLimit;

                var pc = undo ? Undo.AddComponent<ParentConstraint>(bone.gameObject) : bone.gameObject.AddComponent<ParentConstraint>();
                pc.AddSource(new ConstraintSource { sourceTransform = go.transform, weight = 1f });
                pc.SetTranslationOffset(0, Vector3.zero);
                pc.SetRotationOffset(0, Vector3.zero);
                pc.translationAtRest = bone.localPosition;
                pc.rotationAtRest = bone.localEulerAngles;
                pc.locked = true;
                pc.constraintActive = true;

                bodies.Add(rb);
                parentBody = rb;
            }
            Debug.Log($"[JointSpike] {chainRoot.name}: {chain.Count} bodies under \"{holder.name}\"");
            return bodies;
        }

        static Transform AvatarRoot(Transform t)
        {
            var animator = t.GetComponentInParent<Animator>();
            return animator != null ? animator.transform : t.root;
        }

        static bool Covers(Component c, Transform chainRoot)
        {
            var type = c.GetType().Name;
            if (type == "VRCPhysBone")
            {
                var so = new SerializedObject(c);
                var root = so.FindProperty("rootTransform")?.objectReferenceValue as Transform;
                if (root == null) root = c.transform;
                return chainRoot == root || chainRoot.IsChildOf(root) || root.IsChildOf(chainRoot);
            }
            if (type == "MagicaCloth")
            {
                var so = new SerializedObject(c);
                var roots = so.FindProperty("serializeData.rootBones");
                for (int i = 0; roots != null && i < roots.arraySize; i++)
                {
                    var root = roots.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                    if (root != null && (chainRoot == root || chainRoot.IsChildOf(root) || root.IsChildOf(chainRoot))) return true;
                }
            }
            return false;
        }

        static List<Transform> Longest(Transform from)
        {
            var best = new List<Transform>();
            for (int i = 0; i < from.childCount; i++)
            {
                var path = Longest(from.GetChild(i));
                if (path.Count > best.Count) best = path;
            }
            best.Insert(0, from);
            return best;
        }

        // A bare rig: hips bobbing as a walk does, the root carrying it forward, a jump, then a
        // 100 m teleport. Bodies under the root against bodies under the hips, to show which
        // one feels the hips. Reports how far the chain tip strays from rest in the hips' frame,
        // and the bone lengths reached.
        public static void SelfTest()
        {
            var was = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                foreach (bool underRoot in new[] { true, false })
                {
                    var avatar = new GameObject("Avatar").transform;
                    var hips = new GameObject("Hips").transform;
                    hips.SetParent(avatar, false);
                    hips.localPosition = new Vector3(0f, 1f, 0f);
                    Transform prev = hips, soft = null;
                    for (int i = 0; i < 3; i++)
                    {
                        var b = new GameObject("Soft" + i).transform;
                        b.SetParent(prev, false);
                        b.localPosition = i == 0 ? new Vector3(0f, -0.05f, 0.12f) : new Vector3(0f, -0.02f, 0.08f);
                        if (i == 0) soft = b;
                        prev = b;
                    }
                    var bodies = Build(soft, false, underRoot ? avatar : hips);
                    var tip = bodies[bodies.Count - 1];
                    var restTip = hips.InverseTransformPoint(tip.position);
                    var settledTip = restTip;
                    var rest = new float[bodies.Count];
                    for (int i = 1; i < bodies.Count; i++) rest[i] = Vector3.Distance(bodies[i].position, bodies[i - 1].position);

                    const float step = 1f / 90f;
                    float walkStray = 0f, jumpStray = 0f, teleportStray = 0f, lo = float.MaxValue, hi = 0f;
                    // One still second first, so strays are measured from the settled sag.
                    for (int s = 0; s < 990; s++)
                    {
                        float t = s * step - 1f;
                        if (t < 0f)
                        {
                            Physics.SyncTransforms();
                            Physics.Simulate(step);
                            settledTip = hips.InverseTransformPoint(tip.position);
                            continue;
                        }
                        avatar.position = new Vector3(0f, 0f, t * 1.2f) + (t >= 8f ? new Vector3(100f, 0f, 0f) : Vector3.zero);
                        float bob = t < 4f ? Mathf.Abs(Mathf.Sin(t * 6f)) * 0.06f : 0f;
                        float jump = t >= 5f && t < 5.6f ? Mathf.Sin((t - 5f) / 0.6f * Mathf.PI) * 0.4f : 0f;
                        hips.localPosition = new Vector3(Mathf.Sin(t * 6f) * 0.03f * (t < 4f ? 1f : 0f), 1f + bob + jump, 0f);
                        Physics.SyncTransforms();
                        Physics.Simulate(step);
                        float stray = (hips.InverseTransformPoint(tip.position) - settledTip).magnitude * 100f;
                        if (t > 0.5f && t < 4f) walkStray = Mathf.Max(walkStray, stray);
                        if (t >= 5f && t < 6.5f) jumpStray = Mathf.Max(jumpStray, stray);
                        if (t >= 8f) teleportStray = Mathf.Max(teleportStray, stray);
                        for (int i = 1; i < bodies.Count; i++)
                        {
                            float r = Vector3.Distance(bodies[i].position, bodies[i - 1].position) / rest[i];
                            lo = Mathf.Min(lo, r); hi = Mathf.Max(hi, r);
                        }
                    }
                    Debug.Log($"[JointSpike] bodies under {(underRoot ? "the avatar root" : "the hips")}: settled sag " +
                              $"{(settledTip - restTip).magnitude * 100f:0.0} cm; from there the tip strays " +
                              $"{walkStray:0.0} cm walking, {jumpStray:0.0} cm on the jump, {teleportStray:0.0} cm after a 100 m teleport; " +
                              $"bone length {lo:0.00} to {hi:0.00} of rest");
                    Object.DestroyImmediate(avatar.gameObject);
                }
            }
            finally
            {
                Physics.simulationMode = was;
            }
        }
    }
}
