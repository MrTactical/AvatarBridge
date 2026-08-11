#if VRC_SDK_VRCSDK3
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Why AvatarBuilder.BuildHumanAvatar refuses to rebuild a rig without its Jaw.
    //
    // Three static explanations for this have been wrong, so this stops explaining and measures:
    // it dumps what the baked HumanDescription actually contains, checks every name in it against
    // the LIVE hierarchy, and then tries the rebuild several ways to see which the builder accepts.
    // The variants are the point; one of them working names the cause without any theory.
    //
    // Set AVATARBRIDGE_JAW_SCENE to the scene to open. Dev tooling; never ships.
    public static class JawRebuildProbe
    {
        public static void RunBatch()
        {
            string scene = System.Environment.GetEnvironmentVariable("AVATARBRIDGE_JAW_SCENE");
            if (string.IsNullOrEmpty(scene))
            {
                Debug.LogError("[JawProbe] set AVATARBRIDGE_JAW_SCENE");
                if (Application.isBatchMode) EditorApplication.Exit(2);
                return;
            }
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);

            var animator = Object.FindObjectsOfType<Animator>(true)
                .FirstOrDefault(a => a.avatar != null && a.avatar.isHuman);
            if (animator == null)
            {
                Debug.LogError("[JawProbe] no humanoid Animator in the scene");
                if (Application.isBatchMode) EditorApplication.Exit(3);
                return;
            }

            var root = animator.gameObject;
            var d = animator.avatar.humanDescription;
            Debug.Log($"[JawProbe] live root \"{root.name}\"  avatar asset \"{animator.avatar.name}\"  " +
                      $"skeleton={d.skeleton.Length} human={d.human.Length}");

            // Every transform under the root, by name, so a skeleton entry can be checked and
            // duplicates are visible.
            var live = new Dictionary<string, List<Transform>>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!live.TryGetValue(t.name, out var list)) live[t.name] = list = new List<Transform>();
                list.Add(t);
            }

            int missing = 0, dupes = 0;
            foreach (var s in d.skeleton)
            {
                if (!live.TryGetValue(s.name, out var found))
                {
                    Debug.Log($"[JawProbe]   skeleton entry MISSING from hierarchy: \"{s.name}\"");
                    missing++;
                }
                else if (found.Count > 1)
                {
                    Debug.Log($"[JawProbe]   skeleton entry AMBIGUOUS ({found.Count}x): \"{s.name}\"");
                    dupes++;
                }
            }
            Debug.Log($"[JawProbe] skeleton: {missing} missing, {dupes} ambiguous");

            foreach (var h in d.human)
            {
                if (!live.ContainsKey(h.boneName))
                {
                    Debug.Log($"[JawProbe]   human bone MISSING: {h.humanName} -> \"{h.boneName}\"");
                }
            }

            // A skeleton array built from the LIVE hierarchy: every transform, its real local TRS,
            // in hierarchy order with the root first. If the builder accepts this and refuses the
            // baked one, the baked skeleton is the whole problem.
            SkeletonBone[] FromLive()
            {
                var all = root.GetComponentsInChildren<Transform>(true);
                return all.Select(t => new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale,
                }).ToArray();
            }

            var humanNoJaw = d.human.Where(h => h.humanName != "Jaw").ToArray();

            void Try(string label, HumanBone[] human, SkeletonBone[] skeleton)
            {
                var copy = d;
                copy.human = human;
                copy.skeleton = skeleton;
                var previous = animator.avatar;
                animator.avatar = null;
                Avatar built = null;
                try { built = AvatarBuilder.BuildHumanAvatar(root, copy); }
                catch (System.Exception e) { Debug.Log($"[JawProbe] {label}: THREW {e.GetType().Name} {e.Message}"); }
                bool ok = built != null && built.isValid;
                Debug.Log($"[JawProbe] {label}: {(ok ? "BUILT OK" : "refused")} " +
                          $"(human={human.Length} skeleton={skeleton.Length})");
                if (built != null) Object.DestroyImmediate(built);
                animator.avatar = previous;
            }

            var baked = d.skeleton.ToArray();
            var bakedRenamed = d.skeleton.ToArray();
            if (bakedRenamed.Length > 0) bakedRenamed[0].name = root.name;

            // The control: does the UNCHANGED description still build? If even this is refused,
            // nothing about removing the Jaw is to blame and the description was never buildable
            // against this hierarchy in the first place.
            Try("A control, unchanged human + baked skeleton", d.human.ToArray(), baked);
            Try("B no jaw + baked skeleton", humanNoJaw, baked);
            Try("C no jaw + baked skeleton, root renamed", humanNoJaw, bakedRenamed);
            Try("D no jaw + skeleton rebuilt from live hierarchy", humanNoJaw, FromLive());
            Try("E no jaw + EMPTY skeleton", humanNoJaw, new SkeletonBone[0]);
            Try("F unchanged human + skeleton from live hierarchy", d.human.ToArray(), FromLive());

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
#endif
