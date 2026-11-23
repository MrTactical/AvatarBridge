#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Two controllers declaring one parameter as Bool and as Float: the first one merged won, so
    // VRCFury's direct blend tree read a Bool, which Unity refuses ("is not float type").
    public static class ParameterTypeClashTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Parameter type clash keeps Float")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("ParameterTypeClashAvatar");
            var master = new AnimatorController();
            var source = new AnimatorController();
            try
            {
                master.AddParameter("IsLocal", AnimatorControllerParameterType.Bool);
                master.AddParameter("Toggle", AnimatorControllerParameterType.Int);
                master.AddLayer("Base");
                var machine = master.layers[0].stateMachine;
                var a = machine.AddState("A");
                a.AddTransition(machine.AddState("B")).AddCondition(AnimatorConditionMode.If, 0f, "IsLocal");
                source.AddParameter("IsLocal", AnimatorControllerParameterType.Float);
                source.AddParameter("Toggle", AnimatorControllerParameterType.Bool);

                var ctx = new BridgeContext { Target = root, Report = new BridgeReport(), Settings = new BridgeSettings() };
                var flags = BindingFlags.NonPublic | BindingFlags.Static;
                typeof(AnimatorMerger).GetMethod("MergeParameters", flags).Invoke(null, new object[] { master, source, ctx });
                typeof(AnimatorMerger).GetMethod("ReconcileConditionModes", flags).Invoke(null, new object[] { master, ctx });

                var isLocal = master.parameters.First(p => p.name == "IsLocal").type;
                Check(isLocal == AnimatorControllerParameterType.Float, $"IsLocal is Float, as a blend tree needs ({isLocal})");
                var c = a.transitions[0].conditions[0];
                Check(c.mode == AnimatorConditionMode.Greater && Mathf.Approximately(c.threshold, 0.5f), $"its If became Greater 0.5 ({c.mode} {c.threshold})");
                var toggle = master.parameters.First(p => p.name == "Toggle").type;
                Check(toggle == AnimatorControllerParameterType.Int, $"a clash without a Float keeps the first type ({toggle})");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(master);
                Object.DestroyImmediate(source);
            }
            Debug.Log(failures == 0 ? "[ParameterTypeClashTest] PASS" : $"[ParameterTypeClashTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[ParameterTypeClashTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
