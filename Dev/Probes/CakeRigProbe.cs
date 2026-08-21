// What a cascading physics addon actually looks like.
//
// Cake PB and its relatives build a rig of helper bones, each stage doing
// one job and feeding the next, and the avatar's own bone rides the end of
// it. Converting each stage into its own solver gives a dozen things
// swinging separately where the source had one composed result.
//
// Before deciding what to do about that, two facts are needed: what the
// rig is shaped like, and whether the MESH is skinned to the helper bones
// or to the avatar's own. Removing the rig is only safe if the mesh never
// mentions it.
//
//   -executeMethod AvatarBridge.Regression.CakeRigProbe.Run
//   AVATARBRIDGE_ONE  = output folder holding the prefab
//   AVATARBRIDGE_RIG  = the rig root object name (default "cake_PB")
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class CakeRigProbe
    {
        public static void Run()
        {
            string folder = Environment.GetEnvironmentVariable("AVATARBRIDGE_ONE");
            string rigName = Environment.GetEnvironmentVariable("AVATARBRIDGE_RIG") ?? "cake_PB";

            string prefabPath = AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase));
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);

            var rig = instance.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == rigName);
            if (rig == null)
            {
                Debug.Log($"[Cake] no object named \"{rigName}\"");
                UnityEngine.Object.DestroyImmediate(instance);
                EditorApplication.Exit(0);
                return;
            }

            var underRig = new HashSet<Transform>(rig.GetComponentsInChildren<Transform>(true));
            Debug.Log($"[Cake] \"{rigName}\" holds {underRig.Count} transform(s)");

            // Every bone any skinned mesh actually uses, and whether the rig
            // is among them. A mesh weighted to the rig cannot lose it.
            int meshesUsingRig = 0;
            var usedFromRig = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.bones == null) continue;
                var hits = skin.bones.Where(b => b != null && underRig.Contains(b)).ToList();
                if (hits.Count == 0) continue;
                meshesUsingRig++;
                Debug.Log($"[Cake] MESH \"{skin.name}\" is skinned to {hits.Count} bone(s) inside the rig");
                foreach (var b in hits.Take(8)) usedFromRig.Add(b.name);
            }
            Debug.Log(meshesUsingRig == 0
                ? "[Cake] NO mesh is skinned to the rig - it drives the avatar's own bones from outside"
                : $"[Cake] {meshesUsingRig} mesh(es) skinned INTO the rig: {string.Join(", ", usedFromRig)}");

            // What drives what: constraints inside the rig, and what they aim at.
            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
            {
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null) continue;
                    string type = c.GetType().Name;
                    if (!type.Contains("Constraint")) continue;
                    Debug.Log($"[Cake] constraint {type} on {Path(t, instance.transform)}");
                }
            }

            // The mapping that matters: constraints OUTSIDE the rig whose
            // source is INSIDE it. Those constrained objects are the real
            // bones the rig drives, and the ones a jiggle would go on.
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                if (underRig.Contains(t)) continue;
                foreach (var c in t.GetComponents<Component>())
                {
                    if (!(c is UnityEngine.Animations.IConstraint constraint)) continue;
                    var sources = new List<UnityEngine.Animations.ConstraintSource>();
                    constraint.GetSources(sources);
                    foreach (var s in sources)
                    {
                        if (s.sourceTransform == null || !underRig.Contains(s.sourceTransform)) continue;
                        Debug.Log($"[Cake] DRIVES  {Path(t, instance.transform)}  " +
                                  $"<- {c.GetType().Name} from \"{s.sourceTransform.name}\"");
                    }
                }
            }

            // Does anything ANIMATE a bone in the rig? A relay whose source
            // is animated still carries something once the rig's physics is
            // gone; one whose source is inert just copies a dead pose.
            var animatedInRig = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var animator in instance.GetComponentsInChildren<Animator>(true))
            {
                var controller = BridgeContext.Underlying(animator.runtimeAnimatorController);
                if (controller == null) continue;
                foreach (var clip in controller.animationClips)
                {
                    if (clip == null) continue;
                    foreach (var b in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (b.path.IndexOf(rigName, StringComparison.Ordinal) >= 0)
                        {
                            animatedInRig.Add($"{b.path} :: {b.propertyName}");
                        }
                    }
                }
            }
            Debug.Log(animatedInRig.Count == 0
                ? "[Cake] NOTHING in the rig is animated - every relay from it copies a dead pose"
                : $"[Cake] {animatedInRig.Count} animated binding(s) inside the rig");
            foreach (var a in animatedInRig.Take(10)) Debug.Log($"[Cake]   animated: {a}");

            // The cloth the conversion built from it, and what each simulates.
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null) continue;
                    string type = c.GetType().Name;
                    if (type != "MagicaCloth" && type != "DynamicBone") continue;
                    foreach (var root in RootsOf(c))
                    {
                        Debug.Log($"[Cake] cloth {t.name} simulates \"{root.name}\"" +
                                  (underRig.Contains(root) ? "  (INSIDE the rig)" : "  (outside)"));
                    }
                }
            }

            UnityEngine.Object.DestroyImmediate(instance);
            EditorApplication.Exit(0);
        }

        static IEnumerable<Transform> RootsOf(Component cloth)
        {
            if (cloth.GetType().Name == "DynamicBone")
            {
                var one = cloth.GetType().GetField("m_Root")?.GetValue(cloth) as Transform;
                if (one != null) yield return one;
                yield break;
            }
            var data = cloth.GetType().GetProperty("SerializeData")?.GetValue(cloth);
            var roots = data?.GetType().GetField("rootBones")?.GetValue(data) as System.Collections.IEnumerable;
            if (roots == null) yield break;
            foreach (var o in roots)
            {
                if (o is Transform t) yield return t;
            }
        }

        static string Path(Transform t, Transform root)
            => AnimationUtility.CalculateTransformPath(t, root);
    }
}
#endif
