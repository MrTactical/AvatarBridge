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

        public const float LagSpring = 60f;       // layered: how hard a body chases its cloth bone

        [MenuItem("Tools/AvatarBridge Dev/Joint spike: selected chain roots")]
        static void BuildOnSelection()
        {
            foreach (var root in Selection.transforms) Build(root, true, null, false);
        }

        // Sphere colliders on the bodies, so two chains squash against each other.
        [MenuItem("Tools/AvatarBridge Dev/Joint spike: selected chain roots, colliding")]
        static void BuildCollidingOnSelection()
        {
            foreach (var root in Selection.transforms) Build(root, true, null, true);
        }

        [MenuItem("Tools/AvatarBridge Dev/Joint spike: selected chain roots, layered on MagicaCloth2")]
        static void BuildLayeredOnSelection()
        {
            foreach (var root in Selection.transforms) BuildLayered(root, true, null);
        }

        // Bodies root first.
        public static List<Rigidbody> Build(Transform chainRoot, bool undo, Transform holderParent, bool colliders)
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
                if (colliders) go.AddComponent<SphereCollider>().radius = Mathf.Max(0.02f, length * 0.6f);
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

        // MagicaCloth2 keeps the swing and the pushes (ChilloutVR feeds every player's hands into
        // every cloth), simulating a copy of the chain. Each real bone turns with its copy and sits
        // where a body chasing the copy bone is: a push that shortens the copy squashes the bone,
        // and the body's lag overshoots on release. The copy is one frame behind MagicaCloth2,
        // since constraints run before it writes. Bodies root first; the root has none.
        public static List<Rigidbody> BuildLayered(Transform chainRoot, bool undo, Transform holderParent)
        {
            var avatar = holderParent != null ? holderParent : AvatarRoot(chainRoot);
            var copy = Object.Instantiate(chainRoot.gameObject, chainRoot.parent).transform;
            copy.name = chainRoot.name + " (cloth copy)";
            copy.SetPositionAndRotation(chainRoot.position, chainRoot.rotation);
            foreach (var c in copy.GetComponentsInChildren<Component>(true).Where(c => !(c is Transform)).ToList())
                Object.DestroyImmediate(c);
            if (undo) Undo.RegisterCreatedObjectUndo(copy.gameObject, "Joint spike");

            int retargeted = 0;
            foreach (var cloth in avatar.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "MagicaCloth"))
            {
                var so = new SerializedObject(cloth);
                var roots = so.FindProperty("serializeData.rootBones");
                for (int i = 0; roots != null && i < roots.arraySize; i++)
                {
                    var e = roots.GetArrayElementAtIndex(i);
                    if (e.objectReferenceValue == chainRoot) { e.objectReferenceValue = copy; retargeted++; }
                }
                so.ApplyModifiedProperties();
            }
            Debug.Log($"[JointSpike] {chainRoot.name}: {retargeted} MagicaCloth root(s) moved to the copy" +
                      (retargeted == 0 ? "; nothing simulates the copy, so the chain will hang still" : ""));

            var holder = new GameObject(chainRoot.name + " (lag bodies)").transform;
            holder.SetParent(avatar, false);
            if (undo) Undo.RegisterCreatedObjectUndo(holder.gameObject, "Joint spike");

            var real = Longest(chainRoot);
            var twin = Longest(copy);
            var bodies = new List<Rigidbody>();
            for (int i = 0; i < real.Count; i++)
            {
                var bone = real[i];
                var rc = undo ? Undo.AddComponent<RotationConstraint>(bone.gameObject) : bone.gameObject.AddComponent<RotationConstraint>();
                rc.AddSource(new ConstraintSource { sourceTransform = twin[i], weight = 1f });
                rc.rotationAtRest = bone.localEulerAngles;
                rc.rotationOffset = Vector3.zero;
                rc.locked = true;
                rc.constraintActive = true;
                if (i == 0) continue;

                float length = Vector3.Distance(bone.position, real[i - 1].position);
                var target = new GameObject(bone.name + " (lag target)");
                target.transform.SetParent(twin[i], false);
                var targetBody = target.AddComponent<Rigidbody>();
                targetBody.isKinematic = true;
                targetBody.useGravity = false;

                var go = new GameObject(bone.name + " (lag body)");
                go.transform.SetParent(holder, false);
                go.transform.SetPositionAndRotation(bone.position, bone.rotation);
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = Mass;
                rb.useGravity = false;   // the cloth already sags; this only lags
                rb.freezeRotation = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                var j = go.AddComponent<ConfigurableJoint>();
                j.connectedBody = targetBody;
                j.anchor = Vector3.zero;
                j.autoConfigureConnectedAnchor = false;
                j.connectedAnchor = Vector3.zero;
                j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Limited;
                j.linearLimit = new SoftJointLimit { limit = length * StretchRoom };
                var chase = new JointDrive { positionSpring = LagSpring, positionDamper = LagSpring * 0.03f, maximumForce = float.MaxValue };
                j.xDrive = j.yDrive = j.zDrive = chase;
                j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Free;

                var pc = undo ? Undo.AddComponent<PositionConstraint>(bone.gameObject) : bone.gameObject.AddComponent<PositionConstraint>();
                pc.AddSource(new ConstraintSource { sourceTransform = go.transform, weight = 1f });
                pc.translationAtRest = bone.localPosition;
                pc.translationOffset = Vector3.zero;
                pc.locked = true;
                pc.constraintActive = true;
                bodies.Add(rb);
            }
            Debug.Log($"[JointSpike] {chainRoot.name}: layered, {bodies.Count} lag bodies under \"{holder.name}\"");
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
                    var bodies = Build(soft, false, underRoot ? avatar : hips, false);
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
                SqueezeTest();
                LayeredTest();
            }
            finally
            {
                Physics.simulationMode = was;
            }
        }

        static Transform Rig(Transform parent, string name, Vector3 local, int bones, Vector3 step)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = local;
            var prev = root;
            for (int i = 0; i < bones; i++)
            {
                var b = new GameObject(name + i).transform;
                b.SetParent(prev, false);
                b.localPosition = step;
                prev = b;
            }
            return root.GetChild(0);
        }

        // Two colliding chains side by side, then their chest sides pressed 5 cm in each, as two
        // hands squeezing them together. Reports the shortest bone and how far a tip was pushed
        // from where it settled.
        static void SqueezeTest()
        {
            var avatar = new GameObject("Avatar").transform;
            var hips = new GameObject("Hips").transform;
            hips.SetParent(avatar, false);
            hips.localPosition = Vector3.up;
            var left = new GameObject("ChestL").transform; left.SetParent(hips, false); left.localPosition = new Vector3(-0.07f, 0.3f, 0f);
            var right = new GameObject("ChestR").transform; right.SetParent(hips, false); right.localPosition = new Vector3(0.07f, 0.3f, 0f);
            var l = Build(Rig(left, "L", Vector3.zero, 2, new Vector3(0f, -0.01f, 0.06f)), false, avatar, true);
            var r = Build(Rig(right, "R", Vector3.zero, 2, new Vector3(0f, -0.01f, 0.06f)), false, avatar, true);
            var all = new[] { l, r };
            const float step = 1f / 90f;
            Vector3 settled = Vector3.zero;
            float shortest = float.MaxValue, pushed = 0f;
            var rest = all.Select(c => Enumerable.Range(1, c.Count - 1).Select(i => Vector3.Distance(c[i].position, c[i - 1].position)).ToArray()).ToArray();
            for (int s = 0; s < 360; s++)
            {
                float t = s * step;
                float squeeze = Mathf.Clamp01((t - 1f) / 0.5f) * (t < 3f ? 1f : 0f) * 0.05f;
                left.localPosition = new Vector3(-0.07f + squeeze, 0.3f, 0f);
                right.localPosition = new Vector3(0.07f - squeeze, 0.3f, 0f);
                Physics.SyncTransforms();
                Physics.Simulate(step);
                var tip = right.InverseTransformPoint(r[r.Count - 1].position);
                if (t < 1f) { settled = tip; continue; }
                pushed = Mathf.Max(pushed, (tip - settled).magnitude * 100f);
                for (int c = 0; c < 2; c++)
                    for (int i = 1; i < all[c].Count; i++)
                        shortest = Mathf.Min(shortest, Vector3.Distance(all[c][i].position, all[c][i - 1].position) / rest[c][i - 1]);
            }
            Debug.Log($"[JointSpike] squeeze, two colliding chains pressed 5 cm in each: tip pushed {pushed:0.0} cm, " +
                      $"shortest bone {shortest:0.00} of rest");
            Object.DestroyImmediate(avatar.gameObject);
        }

        // Layered: the cloth copy stands in for MagicaCloth2, driven by hand. A push shortens its
        // second bone by 30% for half a second, then lets go. Reports how short the real bone got
        // and how far past rest it rebounded.
        static void LayeredTest()
        {
            var avatar = new GameObject("Avatar").transform;
            var hips = new GameObject("Hips").transform;
            hips.SetParent(avatar, false);
            hips.localPosition = Vector3.up;
            var soft = Rig(hips, "Soft", new Vector3(0f, 0.3f, 0.1f), 2, new Vector3(0f, -0.01f, 0.07f));
            var bodies = BuildLayered(soft, false, avatar);
            var copy = soft.parent.Find(soft.name + " (cloth copy)");
            var twin = Longest(copy);
            var restLocal = twin[1].localPosition;
            float rest = restLocal.magnitude;
            const float step = 1f / 90f;
            float shortest = float.MaxValue, rebound = 0f;
            for (int s = 0; s < 270; s++)
            {
                float t = s * step;
                float press = t >= 1f && t < 1.6f ? Mathf.Clamp01((t - 1f) / 0.1f) * 0.3f : 0f;
                twin[1].localPosition = restLocal * (1f - press);
                Physics.SyncTransforms();
                Physics.Simulate(step);
                float length = Vector3.Distance(bodies[0].position, twin[0].position) / rest;
                if (t >= 1f && t < 1.6f) shortest = Mathf.Min(shortest, length);
                if (t >= 1.6f) rebound = Mathf.Max(rebound, length);
            }
            Debug.Log($"[JointSpike] layered, cloth bone pushed 30% short for 0.5 s: real bone reached {shortest:0.00} of rest, " +
                      $"rebounded to {rebound:0.00} on release");
            Object.DestroyImmediate(avatar.gameObject);
        }
    }
}
