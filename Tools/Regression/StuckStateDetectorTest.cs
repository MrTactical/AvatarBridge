#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    /// <summary>
    /// Proves the stuck-state detector can actually fire.
    ///
    /// It came back clean on all fifty corpus avatars, which is worthless on its own — "the corpus
    /// is healthy" and "the check never fires" look identical from outside. This builds controllers
    /// with known answers and asserts both directions: a state whose only exit is unsatisfiable
    /// must be caught, and ordinary complementary bands must NOT be, or the detector is noise.
    ///
    /// The cases mirror the real gesture idiom, since that is what it exists to police: entering a
    /// gesture on "> 1.9 and < 2.1" and leaving on its complement.
    /// </summary>
    public static class StuckStateDetectorTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test — stuck-state detector")]
        public static void Run()
        {
            int failures = 0;

            // 1. POSITIVE: the only exit is self-contradictory. Must be flagged.
            failures += Expect("contradictory exit (> 2 && < 2)", shouldFlag: true, build: (c, from, to) =>
            {
                var t = from.AddTransition(to);
                t.hasExitTime = false;
                t.AddCondition(AnimatorConditionMode.Greater, 2f, "GestureRight");
                t.AddCondition(AnimatorConditionMode.Less, 2f, "GestureRight");
            });

            // 2. POSITIVE: bool demanded true and false at once.
            failures += Expect("bool exit demanding If and IfNot", shouldFlag: true, build: (c, from, to) =>
            {
                var t = from.AddTransition(to);
                t.hasExitTime = false;
                t.AddCondition(AnimatorConditionMode.If, 0f, "Flag");
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Flag");
            });

            // 3. NEGATIVE: the real gesture idiom. Enter (1.9,2.1), leave below it. Must NOT flag.
            failures += Expect("ordinary complement band (< 1.9)", shouldFlag: false, build: (c, from, to) =>
            {
                var t = from.AddTransition(to);
                t.hasExitTime = false;
                t.AddCondition(AnimatorConditionMode.Less, 1.9f, "GestureRight");
            });

            // 4. NEGATIVE: exit time leaves on the clock whatever the conditions say.
            failures += Expect("unsatisfiable conditions but hasExitTime", shouldFlag: false, build: (c, from, to) =>
            {
                var t = from.AddTransition(to);
                t.hasExitTime = true;
                t.AddCondition(AnimatorConditionMode.Greater, 2f, "GestureRight");
                t.AddCondition(AnimatorConditionMode.Less, 2f, "GestureRight");
            });

            // 5. NEGATIVE: own exit is dead, but AnyState can still reach elsewhere.
            failures += Expect("dead own-exit rescued by AnyState", shouldFlag: false, build: (c, from, to) =>
            {
                var dead = from.AddTransition(to);
                dead.hasExitTime = false;
                dead.AddCondition(AnimatorConditionMode.Greater, 2f, "GestureRight");
                dead.AddCondition(AnimatorConditionMode.Less, 2f, "GestureRight");

                var machine = c.layers[0].stateMachine;
                var escape = machine.AddAnyStateTransition(to);
                escape.hasExitTime = false;
                escape.AddCondition(AnimatorConditionMode.If, 0f, "Flag");
            });

            // 6. NEGATIVE: no outgoing transitions at all is a deliberate terminal state.
            failures += Expect("terminal state with no transitions", shouldFlag: false,
                build: (c, from, to) => { });

            Debug.Log(failures == 0
                ? "[StuckStateTest] PASS — detector fires on both dead-exit shapes and stays quiet on all four legitimate ones."
                : $"[StuckStateTest] FAIL — {failures} case(s) wrong. The detector cannot be trusted until these pass.");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        static int Expect(string label, bool shouldFlag,
            System.Action<AnimatorController, AnimatorState, AnimatorState> build)
        {
            string path = "Assets/__StuckStateTest.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("GestureRight", AnimatorControllerParameterType.Float);
            controller.AddParameter("Flag", AnimatorControllerParameterType.Bool);

            var machine = controller.layers[0].stateMachine;
            var from = machine.AddState("Held");
            var to = machine.AddState("Base");
            machine.defaultState = to;
            build(controller, from, to);

            var report = new BridgeReport();
            var ctx = new BridgeContext { Report = report };
            BridgeDiagnostics.RunStuckStateCheckForTest(ctx, controller);

            bool flagged = report.Entries.Any(e =>
                e.Subject != null && e.Subject.Contains("can be entered but never left"));
            AssetDatabase.DeleteAsset(path);

            bool ok = flagged == shouldFlag;
            Debug.Log($"[StuckStateTest] {(ok ? "ok  " : "WRONG")} {label} — expected {(shouldFlag ? "flag" : "no flag")}, got {(flagged ? "flag" : "no flag")}");
            return ok ? 0 : 1;
        }
    }
}
#endif
