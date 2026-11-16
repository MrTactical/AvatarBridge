#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Reflection;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // The menu check reads a controller with two parameters of one name. It built a dictionary of
    // them and threw on the second, so Check showed nothing.
    public static class DiagnosticsDuplicateParameterTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Check survives duplicate parameter names")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("DuplicateParameterAvatar");
            var controller = new AnimatorController();
            try
            {
                controller.parameters = new[]
                {
                    new AnimatorControllerParameter { name = "A", type = AnimatorControllerParameterType.Float },
                    new AnimatorControllerParameter { name = "A", type = AnimatorControllerParameterType.Float },
                };
                root.AddComponent<Animator>();
                var avatar = root.AddComponent<CVRAvatar>();
                avatar.avatarSettings = new CVRAdvancedAvatarSettings { settings = new List<CVRAdvancedSettingsEntry>(), initialized = true };
                var ctx = new BridgeContext { Target = root, CvrAvatar = avatar, Report = new BridgeReport(), Settings = new BridgeSettings() };

                var validate = typeof(BridgeDiagnostics).GetMethod("ValidateMenu", BindingFlags.NonPublic | BindingFlags.Static);
                Exception thrown = null;
                try { validate.Invoke(null, new object[] { ctx, controller, new HashSet<string> { "A" } }); }
                catch (TargetInvocationException e) { thrown = e.InnerException; }
                Check(thrown == null, "the menu check runs" + (thrown != null ? $" ({thrown.GetType().Name}: {thrown.Message})" : ""));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(controller);
            }
            Debug.Log(failures == 0 ? "[DiagnosticsDuplicateParameterTest] PASS" : $"[DiagnosticsDuplicateParameterTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[DiagnosticsDuplicateParameterTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
