#if CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MagicaCloth2;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    // Soft bodies pushing each other apart: one breast against the other, a thigh against the
    // other thigh. Neither VRChat nor MagicaCloth2 collides two chains with each other, and
    // MagicaCloth2 switches its own mutual collision off for a soft body (Bone Spring), so the
    // only way is a sphere on each soft body's collision bone, handed to the OTHER soft bodies
    // near it. Never to its own cloth: a collider riding a particle of the cloth it pushes
    // chases that particle forever. No VRChat SDK here, so the Toolkit can run it on an avatar
    // converted before the option existed.
    public static class SoftBodyContact
    {
        const string Category = "Soft bodies";
        const string SphereName = "SoftBodyContact";

        public static void Link(GameObject avatar, BridgeReport report)
        {
            var bodies = avatar.GetComponentsInChildren<MagicaCloth>(true)
                .Where(c => c.SerializeData.clothType == ClothProcess.ClothType.BoneSpring)
                .Select(c => (cloth: c, bones: CollisionBones(c), radius: c.SerializeData.radius.value))
                .Where(b => b.bones.Count > 0 && b.radius > 0f)
                .ToList();
            if (bodies.Count < 2)
            {
                report.Skipped(Category, avatar.name, bodies.Count == 0
                    ? "No soft bodies to link: none of this avatar's cloths is a Bone Spring with a collision bone."
                    : "Only one soft body, so there is nothing for it to push against.");
                return;
            }

            // Each soft body collides at the size of its volume, which is right against hands,
            // but two neighbours sized that way already overlap standing still: given spheres the
            // same size, they shoved each other every frame and swung more than they did apart.
            // So each one is shrunk to 45% of the way to its nearest neighbour and its sphere made
            // the same size: at rest the two just miss, and they push the moment they meet.
            // Found again on a second run, so pressing the Toolkit button twice changes nothing.
            var fit = new Dictionary<MagicaCloth, float>();
            foreach (var (cloth, bones, radius) in bodies)
            {
                float room = radius;
                foreach (var other in bodies)
                {
                    if (other.cloth == cloth)
                    {
                        continue;
                    }
                    foreach (var bone in bones)
                    {
                        foreach (var otherBone in other.bones)
                        {
                            float gap = Vector3.Distance(bone.position, otherBone.position);
                            if (gap < 3f * (radius + other.radius))
                            {
                                room = Mathf.Min(room, 0.45f * gap);
                            }
                        }
                    }
                }
                fit[cloth] = room;
                if (room < radius - 1e-4f)
                {
                    Undo.RecordObject(cloth, "Link soft bodies");
                    cloth.SerializeData.radius.value = room;   // direct, keeping any depth curve
                    EditorUtility.SetDirty(cloth);
                    report.Approximated(Category, cloth.name,
                        $"Collides at {room:0.###} rather than {radius:0.###}, so it fits beside the soft body next to it.");
                }
            }
            var spheres = new Dictionary<Transform, MagicaSphereCollider>();
            foreach (var (cloth, bones, _) in bodies)
            {
                foreach (var bone in bones)
                {
                    spheres[bone] = SphereOn(bone, fit[cloth]);
                }
            }

            int links = 0;
            foreach (var a in bodies)
            {
                var list = a.cloth.SerializeData.colliderCollisionConstraint.colliderList;
                var met = new List<string>();
                foreach (var b in bodies)
                {
                    if (b.cloth == a.cloth)
                    {
                        continue;
                    }
                    foreach (var boneB in b.bones)
                    {
                        // Near enough to meet: within a few radii of one of a's own collision bones.
                        bool near = a.bones.Any(boneA => Vector3.Distance(boneA.position, boneB.position) < 3f * (a.radius + b.radius));
                        if (!near || list.Contains(spheres[boneB]))
                        {
                            continue;
                        }
                        Undo.RecordObject(a.cloth, "Link soft bodies");
                        list.Add(spheres[boneB]);
                        met.Add(boneB.name);
                        links++;
                    }
                }
                if (met.Count > 0)
                {
                    EditorUtility.SetDirty(a.cloth);
                    report.Converted(Category, a.cloth.name,
                        $"Pushed by \"{string.Join("\", \"", met)}\": soft bodies near each other no longer pass through.");
                }
            }

            foreach (var b in bodies.Where(x => x.bones.Count > 1))
            {
                report.Warning(Category, b.cloth.name,
                    $"Carries {b.bones.Count} sides on one cloth (\"{string.Join("\", \"", b.bones.Select(t => t.name))}\"), " +
                    "so those cannot push each other: MagicaCloth2 can only push a cloth with another cloth's collider.");
            }
            if (links == 0)
            {
                report.Skipped(Category, avatar.name, "No two soft bodies sit close enough to meet.");
            }
        }

        static List<Transform> CollisionBones(MagicaCloth cloth)
        {
            var constraint = cloth.SerializeData.colliderCollisionConstraint;
            var field = constraint?.GetType().GetField("collisionBones", BindingFlags.Public | BindingFlags.Instance);
            return field?.GetValue(constraint) is List<Transform> bones
                ? bones.Where(t => t != null).ToList()
                : new List<Transform>();
        }

        static MagicaSphereCollider SphereOn(Transform bone, float radius)
        {
            var existing = bone.Find(SphereName);
            var sphere = existing != null ? existing.GetComponent<MagicaSphereCollider>() : null;
            if (sphere == null)
            {
                var go = new GameObject(SphereName);
                Undo.RegisterCreatedObjectUndo(go, "Link soft bodies");
                go.transform.SetParent(bone, false);
                sphere = go.AddComponent<MagicaSphereCollider>();
            }
            // The cloth's radius is in world units; a collider's size is scaled by its own
            // transform, so the bone's scale is divided out.
            Vector3 s = bone.lossyScale;
            float scale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            sphere.SetSize(scale > 0f ? radius / scale : radius);
            return sphere;
        }
    }
}
#endif
