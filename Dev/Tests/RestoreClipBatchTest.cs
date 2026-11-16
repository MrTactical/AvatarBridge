#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Known-answer test for the restore clips written in batches.
    //
    // The partial-off topping and the assertion pass wrote one clip per state, a refresh each;
    // they now go through the fillers' batch. Pinned here: every restored state holds a saved
    // clip with the value at conversion time, a second conversion replaces the files instead of
    // numbering new ones, and the assertion pass's "{layer} {state} restore" never takes the
    // file of a filler's "{layer} restore" spelled the same. That collision deleted the
    // filler's clip and left its toggle with nothing to switch off.
    public static class RestoreClipBatchTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Restore clips in batches")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[RestoreClipBatchTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            const string dir = "Assets/__RestoreClipBatchTest";
            AssetDatabase.DeleteAsset(dir);
            AssetDatabase.CreateFolder("Assets", "__RestoreClipBatchTest");

            GameObject avatar = null;
            try
            {
                avatar = new GameObject("__RestoreClipBatchTest");
                foreach (var name in new[] { "Hat", "Scarf", "Shirt", "Coat", "Belt" })
                {
                    new GameObject(name).transform.SetParent(avatar.transform, false);
                }

                AnimatorController Convert(string path)
                {
                    OutputAssetPaths.Reset();
                    var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                    AnimationClip Clip(string clipName, params (string path, float value)[] curves)
                    {
                        var c = new AnimationClip { name = clipName };
                        foreach (var (target, value) in curves)
                        {
                            AnimationUtility.SetEditorCurve(c, Active(target), AnimationCurve.Constant(0f, 0f, value));
                        }
                        AssetDatabase.AddObjectToAsset(c, controller);
                        return c;
                    }
                    AnimatorStateMachine Layer(string name)
                    {
                        controller.AddLayer(name);
                        return controller.layers.Last().stateMachine;
                    }
                    // A layer with no transitions reads as a clip library, and the restore passes
                    // then look at its default state alone. Each state is entered from Any State.
                    void Route(AnimatorStateMachine machine, string parameter)
                    {
                        controller.AddParameter(parameter, AnimatorControllerParameterType.Int);
                        var states = machine.states;
                        for (int i = 0; i < states.Length; i++)
                        {
                            machine.AddAnyStateTransition(states[i].state)
                                .AddCondition(AnimatorConditionMode.Equals, i, parameter);
                        }
                    }

                    // The filler's two-state toggle: an empty resting state, named so its
                    // restore clip is "Hat Off restore".
                    var hatOff = Layer("Hat Off");
                    hatOff.AddState("Idle");
                    hatOff.AddState("On").motion = Clip("ShirtOff", ("Shirt", 0f));
                    // Three states, so only the assertion pass reads it: "Off" lacks Scarf,
                    // and its copy is spelled "Hat Off restore" too.
                    var hat = Layer("Hat");
                    hat.AddState("Off").motion = Clip("HatOff", ("Hat", 0f));
                    hat.AddState("On").motion = Clip("HatOn", ("Hat", 1f), ("Scarf", 0f));
                    hat.AddState("Spin").motion = Clip("HatSpin", ("Hat", 1f));
                    // The partial-off topping's shape: off animates a subset of on.
                    var coat = Layer("Coat");
                    coat.AddState("CoatOff").motion = Clip("CoatOff", ("Coat", 0f));
                    coat.AddState("CoatOn").motion = Clip("CoatOn", ("Coat", 1f), ("Belt", 0f));
                    Route(hatOff, "HatOffP");
                    Route(hat, "HatP");
                    Route(coat, "CoatP");

                    var ctx = new BridgeContext
                    {
                        Target = avatar,
                        Report = new BridgeReport(),
                        Settings = new BridgeSettings(),
                        OutputDir = dir,
                        SourcePaths = new HashSet<string>(),
                    };
                    AnimatorMerger.WriteRestoreClipsForTest(controller, ctx);
                    return controller;
                }

                AnimationClip Motion(AnimatorController controller, string layer, string state)
                {
                    return controller.layers.First(l => l.name == layer).stateMachine.states
                        .First(s => s.state.name == state).state.motion as AnimationClip;
                }
                float Value(AnimationClip clip, string target)
                {
                    var curve = clip != null ? AnimationUtility.GetEditorCurve(clip, Active(target)) : null;
                    return curve == null ? float.NaN : curve.Evaluate(0f);
                }
                bool Saved(AnimationClip clip)
                {
                    return clip != null && AssetDatabase.Contains(clip)
                        && AssetDatabase.GetAssetPath(clip).StartsWith(dir + "/RehomedAssets/");
                }
                int Files()
                {
                    return AssetDatabase.FindAssets("t:AnimationClip", new[] { dir + "/RehomedAssets" }).Length;
                }
                void All(AnimatorController controller, string run)
                {
                    var idle = Motion(controller, "Hat Off", "Idle");
                    var off = Motion(controller, "Hat", "Off");
                    var spin = Motion(controller, "Hat", "Spin");
                    var coatOff = Motion(controller, "Coat", "CoatOff");
                    fail += Check($"{run}: filler's empty state holds its saved clip (Shirt = 1)",
                        Saved(idle) && Mathf.Approximately(Value(idle, "Shirt"), 1f));
                    fail += Check($"{run}: assertion copy of the same spelling is its own file (Scarf = 1, Hat = 0)",
                        Saved(off) && Mathf.Approximately(Value(off, "Scarf"), 1f) && Mathf.Approximately(Value(off, "Hat"), 0f)
                        && AssetDatabase.GetAssetPath(off) != AssetDatabase.GetAssetPath(idle));
                    fail += Check($"{run}: second assertion copy saved (Scarf = 1)",
                        Saved(spin) && Mathf.Approximately(Value(spin, "Scarf"), 1f));
                    fail += Check($"{run}: partial-off topping saved (Belt = 1, Coat = 0)",
                        Saved(coatOff) && Mathf.Approximately(Value(coatOff, "Belt"), 1f) && Mathf.Approximately(Value(coatOff, "Coat"), 0f));
                }

                var first = Convert(dir + "/first.controller");
                All(first, "first");
                int before = Files();
                fail += Check($"first: four restore files ({before})", before == 4);

                var second = Convert(dir + "/second.controller");
                All(second, "reconvert");
                fail += Check($"reconvert: files replaced, not numbered ({Files()} after {before})", Files() == before);
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
                ? "[RestoreClipBatchTest] PASS: batched restore clips saved, replaced and kept apart."
                : $"[RestoreClipBatchTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        static EditorCurveBinding Active(string path)
        {
            return new EditorCurveBinding { path = path, type = typeof(GameObject), propertyName = "m_IsActive" };
        }
    }
}
#endif
