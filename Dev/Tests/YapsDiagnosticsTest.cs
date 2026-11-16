#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Collections.Generic;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // The setup window's diagnostics: the issue link carries the last Build's counts and never its
    // lines, which name the avatar's parts in a public issue; Copy diagnostics carries the lines.
    //
    // Run: -executeMethod AvatarBridge.Regression.YapsDiagnosticsTest.Run
    public static class YapsDiagnosticsTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/YAPS setup diagnostics")]
        public static void Run()
        {
            int failures = 0;
            void Check(bool ok, string what)
            {
                if (!ok) failures++;
                Debug.Log($"[YapsDiagnosticsTest] {(ok ? "ok   " : "WRONG")} {what}");
            }

            string said = YapsSquash.LastSaid;
            bool built = YapsSquash.LastBuilt;
            try
            {
                YapsSetupWindow.Record("Built 1 of 2 plugs and 1 socket.", new List<string>
                {
                    "✓ Secret Plug: baked",
                    "✗ Other Secret Plug: could not bake",
                    "Secret Socket: built ⚠ its mesh has no blendshapes",
                });
                YapsSquash.LastSaid = "No pair found on Secret Mesh.";
                YapsSquash.LastBuilt = false;

                string link = BridgeLinks.YapsDiagnostics(YapsSetupWindow.Diagnostics(false, null));
                string copy = BridgeLinks.YapsDiagnostics(YapsSetupWindow.Diagnostics(true, null));
                Check(link.Contains("Built 1 of 2 plugs and 1 socket. 1 failed, 1 warning."), "the link carries the counts");
                Check(link.Contains("Last squash:   refused"), "the link says the squash was refused");
                Check(!link.Contains("Secret"), "the link names nothing on the avatar");
                Check(copy.Contains("  ✗ Other Secret Plug: could not bake") && copy.Contains("  No pair found on Secret Mesh."),
                    "the copy carries Build's lines and the squash's message");
                Check(copy.StartsWith("Tool:          YAPS"), "both say which tool they came from");
            }
            finally
            {
                YapsSquash.LastSaid = said;
                YapsSquash.LastBuilt = built;
            }
            Debug.Log(failures == 0 ? "[YapsDiagnosticsTest] PASS" : $"[YapsDiagnosticsTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }
}
#endif
