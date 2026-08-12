#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;
using MagicaCloth2;

namespace AvatarBridge
{
    // Optionally lends each cloth the colliders it plausibly bumps into.
    //
    // Both systems keep explicit per-chain collider lists, and AvatarBridge copies each one
    // faithfully; so a tail whose author never wired the leg colliders keeps passing through
    // the leg, exactly as it did in VRChat. This pass goes beyond that, handing a chain any of
    // the avatar's OWN converted colliders that it could reach.
    //
    // The trap is the one that sank the generated-body-collider experiment: give a chain a
    // collider it already lives inside and the solver hurls it out. Thigh jiggle sits
    // permanently within the hip capsule, so proximity alone is not enough. The rule that
    // separates the two cases is REST POSITION; a chain that starts outside a collider can
    // only ever collide with it, while a chain that starts inside one would be ejected. So a
    // collider is only offered when every bone of the chain begins outside it.
    //
    // Everything here is measured against the original PhysBone collider, whose shape
    // AvatarBridge already understands, and only ever adds to a list; no geometry invented.
    public static class MagicaColliderAutoAssign
    {
        const string Category = "PhysBones -> MagicaCloth2";


        public static void Run(BridgeContext ctx,
            List<(PhysBoneChainData Data, MagicaCloth Cloth)> cloths,
            Dictionary<VRCPhysBoneCollider, ColliderComponent> colliderCache)
        {
            if (!ctx.Settings.autoAssignNearbyColliders || cloths.Count == 0 || colliderCache.Count == 0)
            {
                return;
            }

            int assignments = 0, chainsTouched = 0;
            foreach (var (data, cloth) in cloths)
            {
                if (cloth == null)
                {
                    continue;
                }
                var bones = CollectChainBones(data);
                if (bones.Count < 2)
                {
                    continue;
                }

                var sdata = cloth.SerializeData;
                var added = new List<string>();

                foreach (var pair in colliderCache)
                {
                    var pbCollider = pair.Key;
                    var magica = pair.Value;
                    if (pbCollider == null || magica == null ||
                        sdata.colliderCollisionConstraint.colliderList.Contains(magica))
                    {
                        continue; // already assigned, or unconvertible
                    }

                    // A plane is unbounded, so "near" is meaningless and everything would match.
                    if (pbCollider.shapeType.ToString().Contains("Plane"))
                    {
                        continue;
                    }
                    // A collider riding on this chain's own bones travels with it.
                    var owner = pbCollider.rootTransform != null ? pbCollider.rootTransform : pbCollider.transform;
                    if (owner == null || bones.Contains(owner) || IsDescendantOf(owner, data.Root))
                    {
                        continue;
                    }

                    if (TryReach(pbCollider, bones, out float clearance))
                    {
                        if (sdata.colliderCollisionConstraint.colliderList.Count == 0)
                        {
                            sdata.colliderCollisionConstraint.mode = ColliderCollisionConstraint.Mode.Point;
                        }
                        sdata.colliderCollisionConstraint.colliderList.Add(magica);
                        added.Add($"{owner.name} ({clearance * 100f:0.#} cm)");
                        assignments++;
                    }
                }

                if (added.Count > 0)
                {
                    chainsTouched++;
                    ctx.Report.Approximated(Category, data.Root.name,
                        $"Auto-assigned {added.Count} nearby collider(s): {string.Join(", ", added)}. " +
                        "The original PhysBone didn't list these, so this is a deliberate improvement on the " +
                        "source rather than a faithful copy — remove any that make the chain behave oddly.");
                }
            }

            if (assignments > 0)
            {
                ctx.Report.Converted(Category,
                    $"Auto-assigned {assignments} collider(s) across {chainsTouched} chain(s)",
                    "Each chain was given the avatar's own colliders that it starts clear of and could swing " +
                    "into. Colliders a chain begins inside are skipped, since those would push it out.");
            }
        }

        static bool TryReach(VRCPhysBoneCollider collider, List<Transform> bones, out float clearance)
        {
            clearance = float.MaxValue;
            bool reachable = false;
            Vector3 origin = bones[0].position; // the chain root, which stays put

            for (int i = 0; i < bones.Count; i++)
            {
                float distance = SurfaceDistance(collider, bones[i].position);
                if (distance < 0f)
                {
                    // This bone starts inside the collider; assigning it would eject the chain.
                    clearance = distance;
                    return false;
                }
                clearance = Mathf.Min(clearance, distance);

                // The chain hangs off a fixed root, so a bone can never leave the sphere of that
                // radius around it. Anything nearer than that is genuinely within reach; and
                // this is a hard geometric bound, not a tuned guess. Per-bone parent distance
                // would badly understate it, since every joint above a bone rotates as well.
                if (distance <= Vector3.Distance(bones[i].position, origin))
                {
                    reachable = true;
                }
            }
            return reachable;
        }

        static float SurfaceDistance(VRCPhysBoneCollider collider, Vector3 point)
        {
            var owner = collider.rootTransform != null ? collider.rootTransform : collider.transform;
            Vector3 scale = owner.lossyScale;
            float uniform = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Vector3 centre = owner.TransformPoint(collider.position);
            float radius = collider.radius * uniform;

            if (collider.shapeType.ToString().Contains("Capsule"))
            {
                // PhysBone capsules run along the collider's local Y, height being the full span.
                Vector3 axis = (owner.rotation * collider.rotation) * Vector3.up;
                float half = Mathf.Max(0f, collider.height * uniform * 0.5f - radius);
                return DistanceToSegment(point, centre - axis * half, centre + axis * half) - radius;
            }
            return Vector3.Distance(point, centre) - radius;
        }

        static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            if (lengthSq < 1e-8f)
            {
                return Vector3.Distance(point, a);
            }
            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSq);
            return Vector3.Distance(point, a + ab * t);
        }

        static List<Transform> CollectChainBones(PhysBoneChainData data)
        {
            var bones = new List<Transform>();
            void Walk(Transform t)
            {
                if (t == null || data.Ignores.Contains(t))
                {
                    return; // ignored transforms take their children with them
                }
                bones.Add(t);
                for (int i = 0; i < t.childCount; i++)
                {
                    Walk(t.GetChild(i));
                }
            }
            Walk(data.Root);
            return bones;
        }

        static bool IsDescendantOf(Transform candidate, Transform root)
        {
            var current = candidate;
            while (current != null)
            {
                if (current == root)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }
    }
}
#endif
