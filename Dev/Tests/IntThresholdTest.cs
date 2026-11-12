#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A dropdown's float compared Greater or Less becomes an int: each threshold must keep the same
    // whole numbers on its side. Rounded half to even, "less than 0.5" became "less than 0", which a
    // dropdown never is, and "greater than 1.5" became "greater than 2", which dropped 2.
    public static class IntThresholdTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Int thresholds keep their whole numbers")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("IntThresholdAvatar");
            try
            {
                var c = new AnimatorController();
                c.AddParameter("Mode", AnimatorControllerParameterType.Float);
                c.AddLayer("Base");
                var sm = c.layers[0].stateMachine;
                var idle = sm.AddState("Idle");
                var first = sm.AddState("First");
                var band = sm.AddState("Band");
                idle.AddTransition(first).AddCondition(AnimatorConditionMode.Less, 0.5f, "Mode");
                var t = idle.AddTransition(band);
                t.AddCondition(AnimatorConditionMode.Greater, 1.5f, "Mode");
                t.AddCondition(AnimatorConditionMode.Less, 2.5f, "Mode");

                root.AddComponent<Animator>();
                var avatar = root.AddComponent<CVRAvatar>();
                avatar.avatarSettings = new CVRAdvancedAvatarSettings
                {
                    settings = new List<CVRAdvancedSettingsEntry>
                    {
                        new CVRAdvancedSettingsEntry { name = "Mode", machineName = "Mode", type = CVRAdvancedSettingsEntry.SettingsType.Dropdown },
                    },
                };
                var ctx = new BridgeContext { Settings = new BridgeSettings(), Report = new BridgeReport(), CvrAvatar = avatar };
                ParameterTypeInference.Run(c, ctx);

                Check(c.parameters.First(p => p.name == "Mode").type == AnimatorControllerParameterType.Int, "Mode retyped to Int");
                bool Holds(AnimatorStateTransition tr, int v) => tr.conditions.All(k =>
                    k.mode == AnimatorConditionMode.Greater ? v > k.threshold : k.mode == AnimatorConditionMode.Less ? v < k.threshold : true);
                var toFirst = idle.transitions.First(x => x.destinationState == first);
                var toBand = idle.transitions.First(x => x.destinationState == band);
                Check(Holds(toFirst, 0) && !Holds(toFirst, 1), "less than 0.5 still takes 0 and only 0");
                Check(Holds(toBand, 2) && !Holds(toBand, 1) && !Holds(toBand, 3), "between 1.5 and 2.5 still takes 2 and only 2");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            Debug.Log(failures == 0 ? "[IntThresholdTest] PASS" : $"[IntThresholdTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[IntThresholdTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
