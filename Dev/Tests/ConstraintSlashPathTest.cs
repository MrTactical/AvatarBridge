#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

namespace AvatarBridge.Regression
{
    // A constraint curve on an object with a slash in its name finds its constraint. Transform.Find
    // reads the slash as a step down, so the curve read as pointing at nothing and was dropped.
    public static class ConstraintSlashPathTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Constraint curve on a slash-named object")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("ConstraintSlashAvatar");
            try
            {
                var arm = new GameObject("Arm/L");
                arm.transform.SetParent(root.transform, false);
                arm.AddComponent<ParentConstraint>();
                var ctx = new BridgeContext { Target = root, Report = new BridgeReport(), Settings = new BridgeSettings() };
                string path = "Arm/L";
                Type found = null;
                try { found = ConstraintConverter.FollowConstraint(ctx, ref path, "VRCParentConstraint"); }
                catch (Exception e) { Debug.Log($"[ConstraintSlashPathTest] threw {e.GetType().Name}: {e.Message}"); }
                Check(found == typeof(ParentConstraint), $"the constraint is found (was {(found != null ? found.Name : "none")})");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            Debug.Log(failures == 0 ? "[ConstraintSlashPathTest] PASS" : $"[ConstraintSlashPathTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[ConstraintSlashPathTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
