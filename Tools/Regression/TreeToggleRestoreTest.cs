#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    /// <summary>
    /// Known-answer test for the blend-tree half of the off-state restore.
    ///
    /// VRCFury rewrites toggle LAYERS into 1D blend trees nested under one Direct tree, so the
    /// toggle's off half stops being an empty animator state and becomes an empty tree CHILD.
    /// FillEmptyStatesWithRestoreClips only ever read state.motion, so those toggles switched on
    /// and never off — reported in the wild on an avatar whose whole wardrobe was one-way.
    ///
    /// The gates being pinned here are the ones that decide whether this is a repair or a new bug:
    ///
    ///   - only 1D trees, because they NORMALISE — a restore in a Direct child would merely add
    ///     itself to whatever else the parent is summing;
    ///   - only two children with exactly one empty, the tree spelling of VRChat's toggle idiom;
    ///   - a property animated by TWO toggles in one layer is restored by NEITHER, because a
    ///     Direct parent sums its children rather than letting the top one win: the toggle
    ///     switched ON would write 0, the sibling's restore would write 1, and the sum reads on;
    ///   - Animator-type bindings are never snapshotted — Fury's AAP trees wear this exact shape
    ///     and pinning one would freeze a value the math exists to compute.
    /// </summary>
    public static class TreeToggleRestoreTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test — blend-tree toggle restore")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[TreeToggleRestoreTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            const string dir = "Assets/__TreeToggleRestoreTest";
            const string path = dir + "/test.controller";
            AssetDatabase.DeleteAsset(dir);
            AssetDatabase.CreateFolder("Assets", "__TreeToggleRestoreTest");

            GameObject avatar = null;
            try
            {
                avatar = new GameObject("__TreeToggleRestoreTest");
                foreach (var name in new[] { "Hat", "Shirt", "Dress", "Boots" })
                {
                    var go = new GameObject(name);
                    go.transform.SetParent(avatar.transform, false);
                    go.SetActive(true); // the value every restore below must capture
                }

                var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                foreach (var p in new[] { "Weight", "HatP", "ShirtP", "PresetP", "AapP", "EmptyP", "DirectP" })
                {
                    controller.AddParameter(p, AnimatorControllerParameterType.Float);
                }

                AnimationClip Off(string clipName, params string[] targets)
                {
                    var c = new AnimationClip { name = clipName };
                    foreach (var t in targets)
                    {
                        AnimationUtility.SetEditorCurve(c,
                            new EditorCurveBinding { path = t, type = typeof(GameObject), propertyName = "m_IsActive" },
                            AnimationCurve.Constant(0f, 0f, 0f));
                    }
                    AssetDatabase.AddObjectToAsset(c, controller);
                    return c;
                }

                // An AAP clip: animates a PARAMETER, not the avatar. Same two-child shape.
                var aapClip = new AnimationClip { name = "aap" };
                AnimationUtility.SetEditorCurve(aapClip,
                    new EditorCurveBinding { path = "", type = typeof(Animator), propertyName = "AapP" },
                    AnimationCurve.Constant(0f, 0f, 1f));
                AssetDatabase.AddObjectToAsset(aapClip, controller);

                BlendTree Tree(string name, BlendTreeType type, string param, Motion on)
                {
                    var t = new BlendTree { name = name, blendType = type, blendParameter = param };
                    AssetDatabase.AddObjectToAsset(t, controller);
                    t.children = new[]
                    {
                        new ChildMotion { motion = null, threshold = 0f, timeScale = 1f, directBlendParameter = "Weight" },
                        new ChildMotion { motion = on, threshold = 1f, timeScale = 1f, directBlendParameter = "Weight" },
                    };
                    return t;
                }

                var hat = Tree("Hat", BlendTreeType.Simple1D, "HatP", Off("HatOff", "Hat"));
                var shirt = Tree("Shirt", BlendTreeType.Simple1D, "ShirtP", Off("ShirtOff", "Shirt"));
                // The preset overlaps Shirt and additionally owns Dress outright.
                var preset = Tree("Preset", BlendTreeType.Simple1D, "PresetP", Off("PresetOff", "Shirt", "Dress"));
                var aap = Tree("Aap", BlendTreeType.Simple1D, "AapP", aapClip);
                var bothEmpty = Tree("BothEmpty", BlendTreeType.Simple1D, "EmptyP", null);
                var directPair = Tree("DirectPair", BlendTreeType.Direct, "DirectP", Off("BootsOff", "Boots"));

                var root = new BlendTree { name = "Direct", blendType = BlendTreeType.Direct };
                AssetDatabase.AddObjectToAsset(root, controller);
                root.children = new[] { hat, shirt, preset, aap, bothEmpty, directPair }
                    .Select(t => new ChildMotion { motion = t, timeScale = 1f, directBlendParameter = "Weight" })
                    .ToArray();

                var machine = controller.layers[0].stateMachine;
                machine.AddState("Toggles").motion = root;

                var ctx = new BridgeContext
                {
                    Target = avatar,
                    Report = new BridgeReport(),
                    Settings = new BridgeSettings(),
                    OutputDir = dir,
                };
                AnimatorMerger.FillEmptyTreeSlotsWithRestoreClipsForTest(controller, ctx);

                float Restored(BlendTree tree, string target)
                {
                    var motion = tree.children[0].motion;
                    if (!(motion is AnimationClip clip))
                    {
                        return float.NaN;
                    }
                    var curve = AnimationUtility.GetEditorCurve(clip,
                        new EditorCurveBinding { path = target, type = typeof(GameObject), propertyName = "m_IsActive" });
                    return curve == null ? float.NaN : curve.Evaluate(0f);
                }

                fail += Check("exclusive toggle: empty child holds the CURRENT value (Hat active = 1)",
                    Mathf.Approximately(Restored(hat, "Hat"), 1f));
                fail += Check("shared property: the toggle sharing it restores nothing at all",
                    shirt.children[0].motion == null);
                fail += Check("preset: restores the property it owns outright (Dress)",
                    Mathf.Approximately(Restored(preset, "Dress"), 1f));
                fail += Check("preset: does NOT restore the property it shares with Shirt",
                    float.IsNaN(Restored(preset, "Shirt")));
                fail += Check("AAP tree: parameter-driving tree left alone",
                    aap.children[0].motion == null);
                fail += Check("both halves empty: left alone",
                    bothEmpty.children[0].motion == null && bothEmpty.children[1].motion == null);
                fail += Check("Direct tree: not a toggle, left alone (children are summed, not blended)",
                    directPair.children[0].motion == null);
                fail += Check("report names the toggle it repaired",
                    ctx.Report.Entries.Any(e => e.Status == ReportStatus.Converted
                        && e.Subject != null && e.Subject.Contains("blend-tree toggle")
                        && e.Detail != null && e.Detail.Contains("Hat")));
                fail += Check("report names the toggles whose shared properties were left to nobody",
                    ctx.Report.Entries.Any(e => e.Detail != null
                        && e.Detail.Contains("left to nobody")
                        && e.Detail.Contains("Shirt")));
            }
            finally
            {
                if (avatar != null)
                {
                    Object.DestroyImmediate(avatar);
                }
                AssetDatabase.DeleteAsset(dir);
            }

            Debug.Log(fail == 0
                ? "[TreeToggleRestoreTest] PASS — tree toggles restore, shared and AAP trees left alone."
                : $"[TreeToggleRestoreTest] FAIL — {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
