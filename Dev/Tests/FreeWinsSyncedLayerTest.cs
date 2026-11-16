#if CVR_CCK_EXISTS
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Removing an empty layer keeps a synced layer pointed at its source. The source moved down one
    // index and the synced layer still named the old one, so it mirrored itself or nothing.
    public static class FreeWinsSyncedLayerTest
    {
        static int failures;
        const string Folder = "Assets/__FreeWinsSyncedTest";

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Free wins keeps synced layers on their source")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("FreeWinsSyncedAvatar");
            try
            {
                AssetDatabase.DeleteAsset(Folder);
                AssetDatabase.CreateFolder("Assets", "__FreeWinsSyncedTest");
                var c = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/src.controller");
                c.layers[0].stateMachine.AddState("Idle");
                c.AddLayer("Empty");
                c.AddLayer("Source");
                c.layers[2].stateMachine.AddState("On");
                var layers = c.layers;
                var synced = new AnimatorControllerLayer { name = "Synced", syncedLayerIndex = 2, defaultWeight = 1f };
                System.Array.Resize(ref layers, layers.Length + 1);
                layers[layers.Length - 1] = synced;
                c.layers = layers;
                AssetDatabase.SaveAssets();
                Debug.Log("[FreeWinsSyncedLayerTest] source layers: " + string.Join(", ", System.Array.ConvertAll(c.layers, l => l.name + ":" + l.syncedLayerIndex)));

                root.AddComponent<Animator>().runtimeAnimatorController = c;
                var avatar = root.AddComponent<CVRAvatar>();
                var plan = new FreeWins.Plan { Controller = c };
                plan.Layers.Add("Empty");
                plan.LayerIndices.Add(1);
                var into = FreeWins.Apply(avatar, plan, Folder + "/tidy.controller", new BridgeReport());

                Check(into != null, "the tidy copy was made");
                if (into != null)
                {
                    var after = into.layers;
                    Debug.Log("[FreeWinsSyncedLayerTest] tidy layers: " + string.Join(", ", System.Array.ConvertAll(after, l => l.name + ":" + l.syncedLayerIndex)));
                    int s = System.Array.FindIndex(after, l => l.name == "Synced");
                    int src = System.Array.FindIndex(after, l => l.name == "Source");
                    Check(s >= 0 && after[s].syncedLayerIndex == src,
                        $"Synced still follows Source at {src} (points at {(s >= 0 ? after[s].syncedLayerIndex : -2)})");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(Folder);
            }
            Debug.Log(failures == 0 ? "[FreeWinsSyncedLayerTest] PASS" : $"[FreeWinsSyncedLayerTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[FreeWinsSyncedLayerTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
