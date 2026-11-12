#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A condition on a parameter the controller never declared is reconciled to the Float it is
    // declared as. It was declared after the reconcile ran, so an IfNot sat on a Float, which
    // ChilloutVR drops, while the report said the transition survived.
    public static class DanglingConditionModeTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Dangling parameter conditions match their type")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("DanglingConditionAvatar");
            var controller = new AnimatorController();
            try
            {
                controller.AddLayer("Base");
                var machine = controller.layers[0].stateMachine;
                var a = machine.AddState("A");
                var b = machine.AddState("B");
                var t = a.AddTransition(b);
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, "VF1_Hat");

                var ctx = new BridgeContext { Target = root, Report = new BridgeReport(), Settings = new BridgeSettings() };
                typeof(AnimatorMerger).GetMethod("DeclareDanglingParameters", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { controller, ctx });

                var c = a.transitions.Length > 0 && a.transitions[0].conditions.Length > 0 ? a.transitions[0].conditions[0] : default;
                Check(a.transitions.Length == 1, $"the transition survives ({a.transitions.Length})");
                Check(c.mode == AnimatorConditionMode.Less && Mathf.Approximately(c.threshold, 0.5f),
                    $"IfNot becomes Less 0.5 on the Float (was {c.mode} {c.threshold})");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(controller);
            }
            Debug.Log(failures == 0 ? "[DanglingConditionModeTest] PASS" : $"[DanglingConditionModeTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[DanglingConditionModeTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
