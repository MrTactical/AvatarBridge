#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Known-answer test for the blend-tree half of the off-state restore.
    //
    // VRCFury rewrites toggle LAYERS into 1D blend trees nested under one Direct tree, so the
    // toggle's off half stops being an empty animator state and becomes an empty tree CHILD.
    // FillEmptyStatesWithRestoreClips only ever read state.motion, so those toggles switched on
    // and never off; reported in the wild on an avatar whose whole wardrobe was one-way.
    //
    // The gates being pinned here are the ones that decide whether this is a repair or a new bug:
    //
    //   - only 1D trees, because they NORMALISE; a restore in a Direct child would merely add
    //     itself to whatever else the parent is summing;
    //   - only two children with exactly one empty, the tree spelling of VRChat's toggle idiom;
    //   - a property animated by TWO toggles in one layer is restored by NEITHER, because a
    //     Direct parent sums its children rather than letting the top one win: the toggle
    //     switched ON would write 0, the sibling's restore would write 1, and the sum reads on;
    //   - Animator-type bindings are never snapshotted. Fury's AAP trees wear this exact shape
    //     and pinning one would freeze a value the math exists to compute;
    //   - an off half that animates only paths already dead in VRChat is as empty as a missing
    //     one; one on a path the conversion renamed is not, since the path repair puts it back
    //     later, and nor is one that animates a live material property, though the value reads
    //     as missing to a script;
    //   - a slider with no empty child whose ends animate different properties: at one end the
    //     tree says nothing about the other's, and CVR leaves it wherever the last rebind found
    //     it. Each end gets the others' at rest, on a copy, so a clip shared elsewhere is untouched.
    public static class TreeToggleRestoreTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Blend-tree toggle restore")]
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
                foreach (var name in new[] { "Hat", "Shirt", "Dress", "Boots", "Scarf", "Skirt", "Cape" })
                {
                    var go = new GameObject(name);
                    go.transform.SetParent(avatar.transform, false);
                    go.SetActive(true); // the value every restore below must capture
                }

                var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                foreach (var p in new[] { "Weight", "HatP", "ShirtP", "PresetP", "AapP", "EmptyP", "DirectP", "ShapeP" })
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

                // Off halves authored for another body, and one that only a renderer can read.
                var scarf = Tree("Scarf", BlendTreeType.Simple1D, "HatP", Off("ScarfOn", "Scarf"));
                var kids = scarf.children;
                kids[0].motion = Off("ScarfGhost", "Old Body/Scarf");
                scarf.children = kids;
                avatar.transform.Find("Skirt").gameObject.AddComponent<MeshRenderer>();
                var tint = new AnimationClip { name = "SkirtTint" };
                AnimationUtility.SetEditorCurve(tint,
                    new EditorCurveBinding { path = "Skirt", type = typeof(MeshRenderer), propertyName = "material._Color.r" },
                    AnimationCurve.Constant(0f, 0f, 0.5f));
                AssetDatabase.AddObjectToAsset(tint, controller);
                var skirt = Tree("Skirt", BlendTreeType.Simple1D, "ShirtP", Off("SkirtOff", "Skirt"));
                kids = skirt.children;
                kids[0].motion = tint;
                skirt.children = kids;

                var cape = Tree("Cape", BlendTreeType.Simple1D, "EmptyP", Off("CapeOn", "Cape"));
                kids = cape.children;
                var renamed = Off("CapeRenamed", "Old Cape");
                kids[0].motion = renamed;
                cape.children = kids;

                // A slider whose ends move two different shapes. The rest value is not 0, so a
                // fill that wrote 0 instead of reading the body would show.
                var body = new GameObject("Body");
                body.transform.SetParent(avatar.transform, false);
                var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } };
                var deltas = new[] { Vector3.up, Vector3.up, Vector3.up };
                mesh.AddBlendShapeFrame("Wide", 100f, deltas, null, null);
                mesh.AddBlendShapeFrame("Tall", 100f, deltas, null, null);
                var smr = body.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.SetBlendShapeWeight(0, 0f);
                smr.SetBlendShapeWeight(1, 25f);
                AnimationClip Shape(string shape)
                {
                    var c = new AnimationClip { name = shape };
                    AnimationUtility.SetEditorCurve(c,
                        new EditorCurveBinding { path = "Body", type = typeof(SkinnedMeshRenderer), propertyName = "blendShape." + shape },
                        AnimationCurve.Constant(0f, 0f, 100f));
                    AssetDatabase.AddObjectToAsset(c, controller);
                    return c;
                }
                var wide = Shape("Wide");
                var tall = Shape("Tall");
                var slider = Tree("Slider", BlendTreeType.Simple1D, "ShapeP", tall);
                kids = slider.children;
                kids[0].motion = wide;
                slider.children = kids;

                var root = new BlendTree { name = "Direct", blendType = BlendTreeType.Direct };
                AssetDatabase.AddObjectToAsset(root, controller);
                root.children = new[] { hat, shirt, preset, aap, bothEmpty, directPair, scarf, skirt, cape, slider }
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
                    SourcePaths = new HashSet<string> { "Old Cape" },
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
                fail += Check("off half on missing paths: filled with the current value (Scarf active = 1)",
                    Mathf.Approximately(Restored(scarf, "Scarf"), 1f));
                fail += Check("off half on a live material property: left as it was",
                    skirt.children[0].motion == tint);
                fail += Check("off half on a path the conversion renamed: left as it was",
                    cape.children[0].motion == renamed);
                float ShapeAt(Motion motion, string shape)
                {
                    var curve = motion is AnimationClip clip ? AnimationUtility.GetEditorCurve(clip,
                        new EditorCurveBinding { path = "Body", type = typeof(SkinnedMeshRenderer), propertyName = "blendShape." + shape }) : null;
                    return curve == null ? float.NaN : curve.Evaluate(0f);
                }
                var low = slider.children[0].motion;
                var high = slider.children[1].motion;
                fail += Check($"slider: the Wide end holds Tall at rest (25, read {ShapeAt(low, "Tall")}) and keeps its own Wide 100",
                    Mathf.Approximately(ShapeAt(low, "Tall"), 25f) && Mathf.Approximately(ShapeAt(low, "Wide"), 100f));
                fail += Check($"slider: the Tall end holds Wide at rest (0, read {ShapeAt(high, "Wide")}) and keeps its own Tall 100",
                    Mathf.Approximately(ShapeAt(high, "Wide"), 0f) && Mathf.Approximately(ShapeAt(high, "Tall"), 100f));
                fail += Check("slider: the original clips are untouched",
                    low != wide && high != tall && float.IsNaN(ShapeAt(wide, "Tall")) && float.IsNaN(ShapeAt(tall, "Wide")));
                fail += Check("report names the slider it gave rest values",
                    ctx.Report.Entries.Any(e => e.Status == ReportStatus.Converted
                        && e.Subject != null && e.Subject.Contains("slider")
                        && e.Detail != null && e.Detail.Contains("Slider")));
                fail += Check("report names the toggle it repaired",
                    ctx.Report.Entries.Any(e => e.Status == ReportStatus.Converted
                        && e.Subject != null && e.Subject.Contains("blend-tree toggle")
                        && e.Detail != null && e.Detail.Contains("Hat")));
                fail += Check("report names the toggles whose shared properties were left alone",
                    ctx.Report.Entries.Any(e => e.Detail != null
                        && e.Detail.Contains("Shared with another toggle")
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
                ? "[TreeToggleRestoreTest] PASS: tree toggles restore, shared and AAP trees left alone."
                : $"[TreeToggleRestoreTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
