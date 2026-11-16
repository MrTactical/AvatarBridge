#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // The report's sync budget walk must match the GS2 client (AvatarAnimatorManager.FitsAasBudget):
    // bools pack eight to a byte, a parameter that does not fit is skipped while later ones still
    // can, and Swimming, AFK and IsAnimatorEnabled are core and cost nothing.
    public static class SyncBudgetTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Sync budget matches the client")]
        public static void Run()
        {
            failures = 0;
            var c = new AnimatorController();
            try
            {
                var ps = new List<AnimatorControllerParameter>();
                void Add(string n, AnimatorControllerParameterType t) => ps.Add(new AnimatorControllerParameter { name = n, type = t });
                Add("Swimming", AnimatorControllerParameterType.Bool);
                Add("AFK", AnimatorControllerParameterType.Bool);
                Add("IsAnimatorEnabled", AnimatorControllerParameterType.Bool);
                for (int i = 0; i < 99; i++) Add("F" + i, AnimatorControllerParameterType.Float);   // 3168
                for (int i = 0; i < 8; i++) Add("A" + i, AnimatorControllerParameterType.Bool);     // one byte: 3176
                Add("Late", AnimatorControllerParameterType.Float);                                  // 3208 > 3200: skipped
                for (int i = 0; i < 9; i++) Add("B" + i, AnimatorControllerParameterType.Bool);     // two bytes: 3192
                Add("Last", AnimatorControllerParameterType.Bool);                                   // inside the second byte: free
                c.parameters = ps.ToArray();

                var walk = BridgeDiagnostics.BudgetOrder(c).ToDictionary(x => x.param.name, x => x.fits);
                Check(!walk.ContainsKey("Swimming") && !walk.ContainsKey("AFK") && !walk.ContainsKey("IsAnimatorEnabled"),
                    "Swimming, AFK and IsAnimatorEnabled spend nothing");
                Check(walk.TryGetValue("F98", out var f) && f, "the 99th float fits");
                Check(walk.TryGetValue("Late", out var late) && !late, "a float past the cap is dropped");
                Check(walk.TryGetValue("B8", out var b8) && b8, "bools after the dropped float still fit");
                Check(walk.TryGetValue("Last", out var last) && last, "a bool inside a paid byte fits");
                Check(BridgeDiagnostics.BudgetBits(c) == 3192, $"3192 bits spent (got {BridgeDiagnostics.BudgetBits(c)})");
            }
            finally
            {
                Object.DestroyImmediate(c);
            }
            Debug.Log(failures == 0 ? "[SyncBudgetTest] PASS" : $"[SyncBudgetTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[SyncBudgetTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
