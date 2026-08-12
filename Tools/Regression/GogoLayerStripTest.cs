#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Known-answer test for the GoGo layer strip (task #12).
    //
    // GoGo Loco installed BY HAND names its layers "GoGo Loco ..." and the word hints catch it.
    // Installed through a VRCFury prefab it names them after its parameters. "Go/Beyond"; which
    // contains none of "gogo", "go loco" or "goloco", so the layer outlived a strip that had
    // already neutered its parameters. Nine of fifty-three corpus avatars carried the survivor,
    // and it was not inert: weight 1 on Override, transitions reading ChilloutVR's own Sitting /
    // Grounded / AFK, playing GoGo's chair clip over the station pose the moment the wearer sat.
    //
    // The trap on the other side is over-matching. "Cargo/Rack" contains "go/" and has nothing to
    // do with GoGo, so the match has to be anchored to a word start; that case is pinned here
    // precisely because a substring test would look like it worked.
    public static class GogoLayerStripTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test — GoGo layer strip")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[GogoLayerStripTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            string path = "Assets/__GogoLayerStripTest.controller";
            AssetDatabase.DeleteAsset(path);
            GameObject avatar = null;
            try
            {
                avatar = new GameObject("__GogoLayerStripTest");
                var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                controller.AddParameter("Go/Dash", AnimatorControllerParameterType.Float);
                controller.AddParameter("Sitting", AnimatorControllerParameterType.Bool);
                controller.AddParameter("Keep/Me", AnimatorControllerParameterType.Float);

                // Fury spelling, hand spelling, an innocent lookalike, and an unrelated layer.
                foreach (var name in new[] { "[FX] Go/Beyond", "[FX] GoGo Locomotion", "Cargo/Rack", "Wardrobe" })
                {
                    controller.AddLayer(name);
                }
                var layers = controller.layers;
                for (int i = 0; i < layers.Length; i++)
                {
                    var state = layers[i].stateMachine.AddState("s" + i);
                    var t = state.AddTransition(state);
                    // Every layer reads Sitting, so nothing here is removed for referencing
                    // stripped parameters; the NAME is what has to decide.
                    t.AddCondition(AnimatorConditionMode.If, 0f, "Sitting");
                }
                controller.layers = layers;

                var vrcLayers = controller.layers.ToList();
                var ctx = new BridgeContext
                {
                    Target = avatar,
                    Report = new BridgeReport(),
                    Settings = new BridgeSettings { stripGogoLoco = true, stripSpsSystems = false },
                    MergedController = controller,
                };
                SystemStripper.RemoveLayersForTest(ctx, controller, vrcLayers);

                var names = controller.layers.Select(l => l.name).ToArray();
                Debug.Log($"[GogoLayerStripTest] surviving layers: {string.Join(", ", names)}");

                fail += Check("Fury-spelt \"Go/Beyond\" is stripped",
                    !names.Any(n => n.Contains("Go/Beyond")));
                fail += Check("hand-spelt \"GoGo Locomotion\" is still stripped",
                    !names.Any(n => n.Contains("GoGo")));
                fail += Check("\"Cargo/Rack\" is NOT stripped (go/ must start a word)",
                    names.Any(n => n.Contains("Cargo/Rack")));
                fail += Check("unrelated \"Wardrobe\" layer survives",
                    names.Any(n => n == "Wardrobe"));
            }
            finally
            {
                if (avatar != null)
                {
                    Object.DestroyImmediate(avatar);
                }
                AssetDatabase.DeleteAsset(path);
            }

            Debug.Log(fail == 0
                ? "[GogoLayerStripTest] PASS — Fury-spelt GoGo layers go, lookalikes stay."
                : $"[GogoLayerStripTest] FAIL — {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
