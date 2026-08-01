// Builds one scene holding every converted avatar, for eyeballing a whole corpus run at once —
// DEVELOPMENT ONLY, never shipped in the .unitypackage.
//
// A digest catches what changed between runs. It cannot catch an avatar that converted "cleanly"
// and looks wrong: inside-out geometry, a missing material, a rig collapsed to the origin, a mesh
// left at hip height. Those need eyes, and opening 54 scenes one at a time to use them is why
// nobody does it.
//
// The scene is written INTO Assets/AvatarBridgeOutput/, which both the regression harness and the
// scene-cleanup pass already exclude. That matters: it is full of CVRAvatars with no VRChat
// descriptor, which is exactly the signature the cleanup pass deletes on sight. Anywhere else and
// the next cleanup would quietly empty it.

#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AvatarBridge.Regression
{
    public static class TestScene
    {
        const string OutputFolder = "Assets/AvatarBridgeOutput";
        const string ScenePath = OutputFolder + "/_AllConverted.unity";

        // Generous, because avatars are not one size. Rows wrap past this width.
        const float Padding = 1.5f;
        const float RowWidthLimit = 60f;

        [MenuItem("Tools/AvatarBridge Dev/Scenes — build \"all converted\" test scene")]
        public static void Build()
        {
            var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { OutputFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith("(ChilloutVR).prefab", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (prefabs.Count == 0)
            {
                Debug.LogError($"[TestScene] no \"(ChilloutVR)\" prefabs found under {OutputFolder}.");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Build test scene?",
                    $"Instantiates {prefabs.Count} converted avatars into one scene and saves it as\n" +
                    $"{ScenePath}\n\n" +
                    "That is a lot of geometry in one place — expect it to be heavy to open, and " +
                    "do not press Play in it expecting anything sensible.",
                    "Build it", "Cancel"))
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            float cursorX = 0f, cursorZ = 0f, rowDepth = 0f;
            int placed = 0, failed = 0;
            var log = new StringBuilder();

            try
            {
                for (int i = 0; i < prefabs.Count; i++)
                {
                    string path = prefabs[i];
                    string name = Path.GetFileNameWithoutExtension(path);
                    EditorUtility.DisplayProgressBar($"Building test scene — {i + 1}/{prefabs.Count}",
                        name, (float)i / prefabs.Count);

                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (asset == null) { failed++; log.AppendLine($"  could not load {path}"); continue; }

                    // InstantiatePrefab, not Instantiate: the instance stays LINKED to the asset, so
                    // selecting one here and pressing "Open Prefab" lands on the real converted
                    // avatar rather than a detached copy that cannot be fixed.
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
                    if (instance == null) { failed++; log.AppendLine($"  could not instantiate {name}"); continue; }

                    // Lay out by measured width rather than a fixed grid — a corpus runs from
                    // chibis to dragons, and a fixed pitch either overlaps the big ones or scatters
                    // the small ones so far apart you cannot see them together.
                    var size = MeasuredSize(instance);
                    float width = Mathf.Max(size.x, 0.5f);
                    float depth = Mathf.Max(size.z, 0.5f);

                    if (cursorX > 0f && cursorX + width > RowWidthLimit)
                    {
                        cursorX = 0f;
                        cursorZ += rowDepth + Padding;
                        rowDepth = 0f;
                    }

                    instance.transform.position = new Vector3(cursorX + width * 0.5f, 0f, cursorZ);
                    cursorX += width + Padding;
                    rowDepth = Mathf.Max(rowDepth, depth);
                    placed++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[TestScene] {placed} avatar(s) placed" + (failed > 0 ? $", {failed} failed" : "") +
                      $"\n  saved: {ScenePath}" +
                      (log.Length > 0 ? "\n" + log : ""));
        }

        /// <summary>
        /// World-space size from the renderers, falling back to a person-sized box.
        ///
        /// Renderer bounds are only meaningful once the object is in a scene and its skinning has
        /// been evaluated; an avatar whose meshes are all disabled (a toggled-off outfit variant)
        /// legitimately measures nothing, and must not collapse the layout onto its neighbour.
        /// </summary>
        static Vector3 MeasuredSize(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(false);
            bool any = false;
            var bounds = new Bounds();
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any ? bounds.size : new Vector3(1f, 2f, 1f);
        }
    }
}
#endif
