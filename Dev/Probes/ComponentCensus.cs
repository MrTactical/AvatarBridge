#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Census of every VRC-namespace component actually present on the corpus avatars; the
    // component-level companion to AnimatedVrcPropertyScan's curve-level one.
    //
    // Measures whether real avatars carry the seven SDK types no converter handles (VRCStation,
    // VRCAnimatorPlayAudio, VRCAnimatorTemporaryPoseSpace, VRCImpostorSettings,
    // VRCImpostorEnvironment, VRCRaycast, VRCPhysBoneRoot); the final sweep deletes them.
    // Opens every scene under Assets (not output), never saves.
    // Counts are authored, pre-bake. When this and conversion reports disagree, the reports win.
    public static class ComponentCensus
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Corpus/Scan: VRC components on corpus avatars")]
        public static void Run()
        {
            var tally = new Dictionary<string, (int count, HashSet<string> scenes)>();
            var sceneGuids = AssetDatabase.FindAssets("t:Scene");
            int opened = 0;

            foreach (var guid in sceneGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)
                    || path.Contains("AvatarBridgeOutput") || path.Contains("/AvatarBridge/"))
                {
                    continue;
                }
                UnityEngine.SceneManagement.Scene scene;
                try { scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single); }
                catch { continue; }
                opened++;
                string sceneName = System.IO.Path.GetFileNameWithoutExtension(path);
                // Per scene: one avatar's Base layer and its Animator slot are frequently the same
                // asset, and a shared controller counted once per reference would inflate.
                var seenControllers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var c in root.GetComponentsInChildren<Component>(true))
                    {
                        if (c == null)
                        {
                            continue;
                        }
                        var t = c.GetType();
                        string ns = t.Namespace ?? "";
                        if (!ns.StartsWith("VRC", StringComparison.Ordinal))
                        {
                            continue;
                        }
                        if (!tally.TryGetValue(t.Name, out var e))
                        {
                            e = (0, new HashSet<string>());
                        }
                        e.count++;
                        if (e.scenes.Count < 5)
                        {
                            e.scenes.Add(sceneName);
                        }
                        tally[t.Name] = e;

                        // How often a contact's rootTransform anchor differs from its own object.
                        if (c is VRC.Dynamics.ContactBase contactBase
                            && contactBase.rootTransform != null
                            && contactBase.rootTransform != c.transform)
                        {
                            string key = t.Name + " [rootTransform elsewhere]";
                            if (!tally.TryGetValue(key, out var re))
                            {
                                re = (0, new HashSet<string>());
                            }
                            re.count++;
                            if (re.scenes.Count < 5)
                            {
                                re.scenes.Add(sceneName);
                            }
                            tally[key] = re;
                        }
                    }
                    // The descriptor's baseAnimationLayers and specialAnimationLayers, not just the
                    // Animator slot: reading only that counted VRCAnimatorTemporaryPoseSpace as 2
                    // in the wild while one GoGo-based avatar had 100+.
                    foreach (var controller in ControllersOn(root))
                    {
                        string assetPath = AssetDatabase.GetAssetPath(controller);
                        if (string.IsNullOrEmpty(assetPath) || !seenControllers.Add(assetPath))
                        {
                            continue; // the descriptor and the Animator often name the same asset
                        }
                        foreach (var behaviour in AssetDatabase.LoadAllAssetsAtPath(assetPath)
                                     .OfType<StateMachineBehaviour>())
                        {
                            var t = behaviour.GetType();
                            string ns = t.Namespace ?? "";
                            if (!ns.StartsWith("VRC", StringComparison.Ordinal))
                            {
                                continue;
                            }
                            string key = t.Name + " (behaviour)";
                            if (!tally.TryGetValue(key, out var e))
                            {
                                e = (0, new HashSet<string>());
                            }
                            e.count++;
                            if (e.scenes.Count < 5)
                            {
                                e.scenes.Add(sceneName);
                            }
                            tally[key] = e;
                        }
                    }
                }
            }

            Debug.Log($"[CompCensus] {opened} scenes; {tally.Count} distinct VRC component types:");
            foreach (var kv in tally.OrderByDescending(k => k.Value.count))
            {
                Debug.Log($"[CompCensus]   {kv.Key,-38} n={kv.Value.count,-6} e.g. {string.Join(", ", kv.Value.scenes)}");
            }
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }

        static IEnumerable<RuntimeAnimatorController> ControllersOn(GameObject root)
        {
            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
            {
                if (animator.runtimeAnimatorController != null)
                {
                    yield return animator.runtimeAnimatorController;
                }
            }
            foreach (var descriptor in root
                         .GetComponentsInChildren<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true))
            {
                if (descriptor.baseAnimationLayers != null)
                {
                    foreach (var layer in descriptor.baseAnimationLayers)
                    {
                        if (!layer.isDefault && layer.animatorController != null)
                        {
                            yield return layer.animatorController;
                        }
                    }
                }
                if (descriptor.specialAnimationLayers != null)
                {
                    foreach (var layer in descriptor.specialAnimationLayers)
                    {
                        if (!layer.isDefault && layer.animatorController != null)
                        {
                            yield return layer.animatorController;
                        }
                    }
                }
            }
        }
    }
}
#endif
