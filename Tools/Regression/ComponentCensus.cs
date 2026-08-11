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
    // The parity deep dive found seven SDK component types no converter file references
    // (VRCStation, VRCAnimatorPlayAudio, VRCAnimatorTemporaryPoseSpace, VRCImpostorSettings,
    // VRCImpostorEnvironment, VRCRaycast, VRCPhysBoneRoot). All are removed by the final
    // delete-VRC-components sweep, so nothing breaks; but whether that silently costs a
    // feature depends on whether real avatars carry them, which is what this measures.
    //
    // Opens every scene under Assets (excluding output), tallies component types under every
    // root, and never saves anything.
    //
    // COUNTS HERE ARE AUTHORED, PRE-BAKE, and behaviour counts were once badly wrong: reading
    // only Animator slots missed the descriptor's own layer lists, where a VRChat avatar actually
    // keeps Base/Additive/Gesture/Action/FX. That reported VRCAnimatorTemporaryPoseSpace as 2 in
    // the wild while a single GoGo-based avatar's conversion report found over a hundred, and the
    // parity matrix used the small number to judge the feature not worth building. Fixed; but
    // the lesson generalises: when an instrument and the reports disagree, THE REPORTS WIN. They
    // count what a pass actually met; this counts what a scene appears to hold.
    public static class ComponentCensus
    {
        [MenuItem("Tools/AvatarBridge Dev/Scan — VRC components on corpus avatars")]
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

                        // Contacts anchor their shape at rootTransform when it is set. The legacy
                        // conversion path used to ignore that, so how often the anchor actually
                        // differs from the component's own object decides how much that mattered.
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
                    // State behaviours live in CONTROLLERS, not on objects. Which controllers is
                    // the whole question, and getting it wrong is why this instrument lied.
                    //
                    // It used to read only animator.runtimeAnimatorController. A VRChat avatar
                    // barely uses that: its five real layers. Base, Additive, Gesture, Action, FX
                    //; hang off the DESCRIPTOR's baseAnimationLayers, and the sitting/TPose/IKPose
                    // ones off specialAnimationLayers. The Animator slot is often empty or holds
                    // one of them at most. So the census reported VRCAnimatorTemporaryPoseSpace as
                    // 2 occurrences in the wild while conversion reports were finding 100+ on a
                    // SINGLE GoGo-based avatar, and the parity matrix used the wrong number to
                    // decide the feature was not worth building.
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
