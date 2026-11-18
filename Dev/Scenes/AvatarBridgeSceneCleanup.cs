// One-off cleanup of scenes converted in repeatedly. Development only, never shipped.
//
// A converted copy left in a saved scene points at controllers a later conversion regenerated:
// "Missing (Runtime Animator Controller)". 29 of 48 references here dangled that way. The
// harness resets this in memory only; this fixes the scenes on disk.
//
// Unlike the harness, this SAVES. Run the dry run first.

#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Regression
{
    public static class SceneCleanup
    {
        static readonly string[] Excluded =
        {
            "/AvatarBridgeOutput/", "/CVR.CCK/", "/MagicaCloth2/", "/UnityTechnologies/",
            "/Samples/", "/Scenes/SampleScene", "/MISC/",
        };

        [MenuItem("Tools/Avatar Bridge/Dev/Scenes/List what would be cleaned (dry run)")]
        public static void DryRun() => Run(false);

        [MenuItem("Tools/Avatar Bridge/Dev/Scenes/Clean and SAVE all scenes")]
        public static void CleanAndSave()
        {
            if (!EditorUtility.DisplayDialog(
                    "Clean every scene?",
                    "This deletes leftover converted avatars from every scene in the project and " +
                    "SAVES each one. Scene files are rewritten on disk and there is no undo.\n\n" +
                    "Run the dry run first if you have not.",
                    "Clean and save", "Cancel"))
            {
                return;
            }
            Run(true);
        }

        static void Run(bool save)
        {
            var scenes = AssetDatabase.FindAssets("t:Scene")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && p.StartsWith("Assets/", StringComparison.Ordinal))
                .Where(p => !Excluded.Any(x => p.Replace('\\', '/').Contains(x)))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            var log = new StringBuilder();
            int touchedScenes = 0, removed = 0, reactivated = 0;

            try
            {
                for (int i = 0; i < scenes.Count; i++)
                {
                    string path = scenes[i];
                    if (EditorUtility.DisplayCancelableProgressBar(
                            (save ? "Cleaning scenes" : "Scanning scenes") + $": {i + 1}/{scenes.Count}",
                            Path.GetFileNameWithoutExtension(path), (float)i / scenes.Count))
                    {
                        log.AppendLine("  CANCELLED: scenes already saved stay saved.");
                        break;
                    }

                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

                    var leftovers = new List<GameObject>();
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        foreach (var cvr in root.GetComponentsInChildren<CVRAvatar>(true))
                        {
                            // A converted avatar carries a CVRAvatar and no VRChat descriptor,
                            // because conversion deletes the VRC components from its output.
                            // Anything carrying BOTH is someone's work in progress: left alone.
                            if (cvr == null || cvr.GetComponent<VRCAvatarDescriptor>() != null) continue;
                            leftovers.Add(cvr.gameObject);
                        }
                    }
                    if (leftovers.Count == 0) continue;

                    var names = string.Join(", ", leftovers.Select(g => g.name));
                    int reactivatedHere = 0;

                    if (save)
                    {
                        foreach (var go in leftovers)
                        {
                            if (go != null) UnityEngine.Object.DestroyImmediate(go);
                        }

                        // Only re-activate where a conversion is demonstrably why it was switched
                        // off; i.e. this scene HAD a leftover. Turning every descriptor on
                        // unconditionally would override deliberate choices in scenes holding
                        // several avatars, and that is the user's call.
                        foreach (var root in scene.GetRootGameObjects())
                        {
                            foreach (var d in root.GetComponentsInChildren<VRCAvatarDescriptor>(true))
                            {
                                if (d == null) continue;
                                for (var t = d.transform; t != null; t = t.parent)
                                {
                                    if (t.gameObject.activeSelf) continue;
                                    t.gameObject.SetActive(true);
                                    reactivatedHere++;
                                }
                            }
                        }
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
                    }

                    touchedScenes++;
                    removed += leftovers.Count;
                    reactivated += reactivatedHere;
                    log.AppendLine($"  {Path.GetFileNameWithoutExtension(path)}: " +
                                   $"{leftovers.Count} leftover(s) [{names}]" +
                                   (save ? $", {reactivatedHere} object(s) re-activated" : ""));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            string head = save
                ? $"[SceneCleanup] cleaned and SAVED {touchedScenes} scene(s): " +
                  $"{removed} leftover conversion(s) removed, {reactivated} object(s) re-activated."
                : $"[SceneCleanup] DRY RUN: {touchedScenes} scene(s) would change, " +
                  $"{removed} leftover conversion(s) would be removed. Nothing was written.";
            Debug.Log(head + (log.Length > 0 ? "\n" + log : "\n  (nothing to do)"));
        }
    }
}
#endif
