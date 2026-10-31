#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Known-answer test for VRChat's GestureLeftWeight/RightWeight.
    //
    // VRChat fills the weight from whatever closes the fist: the grip on most controllers, the
    // trigger on Index and Vive. The converter fed it from ChilloutVR's trigger stream on every
    // controller, so a fist squeezed with the grip left the weight at 0. ChilloutVR already puts
    // that per-controller squeeze into GestureLeft/Right, 0 to 1 while the fist closes, so the
    // weight must follow that instead.
    //
    // The tasks run through the CCK's own Execute, so the operators are the client's, not a copy.
    public static class GestureWeightFeedTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Gesture weight follows the fist")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[GestureWeightFeedTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            const string dir = "Assets/__GestureWeightFeedTest";
            AssetDatabase.DeleteAsset(dir);
            AssetDatabase.CreateFolder("Assets", "__GestureWeightFeedTest");

            GameObject avatar = null;
            try
            {
                avatar = new GameObject("__GestureWeightFeedTest");
                var controller = AnimatorController.CreateAnimatorControllerAtPath(dir + "/test.controller");
                // As the merge leaves them: ChilloutVR's gesture floats, one weight already local.
                controller.AddParameter("GestureLeft", AnimatorControllerParameterType.Float);
                controller.AddParameter("GestureRight", AnimatorControllerParameterType.Float);
                controller.AddParameter("#GestureLeftWeight", AnimatorControllerParameterType.Float);
                controller.AddParameter("GestureRightWeight", AnimatorControllerParameterType.Float);

                var ctx = new BridgeContext
                {
                    Target = avatar,
                    Report = new BridgeReport(),
                    Settings = new BridgeSettings(),
                    MergedController = controller,
                };
                AnimatorMerger.FeedGameParametersForTest(controller, ctx);

                // The trigger alone must not feed a weight.
                var stream = avatar.GetComponent<CVRParameterStream>();
                var entries = stream?.GetType().GetField("entries")?.GetValue(stream) as IList;
                int fromStream = entries == null ? 0 : entries.Cast<object>()
                    .Select(e => e.GetType().GetField("parameterName")?.GetValue(e) as string)
                    .Count(n => n != null && n.TrimStart('#').StartsWith("Gesture"));
                fail += Check($"no stream entry feeds a gesture weight ({fromStream})", fromStream == 0);

                var drivers = controller.layers
                    .SelectMany(l => l.stateMachine.states)
                    .SelectMany(s => s.state.behaviours.OfType<AnimatorDriver>())
                    .Where(d => d.EnterTasks.Any(t => t.targetName.TrimStart('#').StartsWith("Gesture")))
                    .ToList();
                fail += Check($"a driver writes the weights ({drivers.Count})", drivers.Count > 0);
                fail += Check("it runs on remote copies too", drivers.All(d => !d.localOnly));

                var animator = avatar.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.Update(0f);
                animator.SetFloat("GestureLeft", 0.4f);
                fail += Check("the animator holds values in edit mode",
                    Mathf.Approximately(animator.GetFloat("GestureLeft"), 0.4f));

                // ChilloutVR's gesture float: -1 thumbs up, 0 open, up to 1 the fist closing, 2 to 6
                // the other poses. VRChat's weight is the squeeze in a fist and 1 in any other pose.
                var cases = new[] { (-1f, 1f), (0f, 0f), (0.25f, 0.25f), (0.6f, 0.6f), (1f, 1f), (2f, 1f), (3f, 1f), (6f, 1f) };
                foreach (var (hand, weight) in new[] { ("GestureLeft", "#GestureLeftWeight"), ("GestureRight", "GestureRightWeight") })
                {
                    foreach (var (g, want) in cases)
                    {
                        animator.SetFloat(hand, g);
                        animator.SetFloat(weight, 0.5f); // a stale value, which must be overwritten
                        foreach (var d in drivers)
                        {
                            foreach (var t in d.EnterTasks)
                            {
                                t.Execute(animator, d);
                            }
                        }
                        float got = animator.GetFloat(weight);
                        fail += Check($"{hand} {g} gives {weight} {want} (got {got})", Mathf.Abs(got - want) < 1e-4f);
                    }
                }
            }
            finally
            {
                if (avatar != null) Object.DestroyImmediate(avatar);
                AssetDatabase.DeleteAsset(dir);
            }

            Debug.Log(fail == 0
                ? "[GestureWeightFeedTest] PASS: the weight follows the fist on every controller."
                : $"[GestureWeightFeedTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
