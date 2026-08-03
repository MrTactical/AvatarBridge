#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    /// <summary>
    /// Census of every VRC-namespace component actually present on the corpus avatars — the
    /// component-level companion to AnimatedVrcPropertyScan's curve-level one.
    ///
    /// The parity deep dive found seven SDK component types no converter file references
    /// (VRCStation, VRCAnimatorPlayAudio, VRCAnimatorTemporaryPoseSpace, VRCImpostorSettings,
    /// VRCImpostorEnvironment, VRCRaycast, VRCPhysBoneRoot). All are removed by the final
    /// delete-VRC-components sweep, so nothing breaks — but whether that silently costs a
    /// feature depends on whether real avatars carry them, which is what this measures.
    ///
    /// Opens every scene under Assets (excluding output), tallies component types under every
    /// root, and never saves anything.
    /// </summary>
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
                    // State behaviours live in controllers, not on objects — walk those too.
                    foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                    {
                        var rac = animator.runtimeAnimatorController;
                        if (rac == null)
                        {
                            continue;
                        }
                        foreach (var behaviour in AssetDatabase.LoadAllAssetsAtPath(
                                     AssetDatabase.GetAssetPath(rac)).OfType<StateMachineBehaviour>())
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
    }
}
#endif
