#if AVATARBRIDGE_YAPS
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Every plug with live tissue patched in warned that its readout "could not carry"
    // the plug number and the jelly's values. The readout draws none of them: the plug
    // number is the atlas relay's and the jelly's are the skin's. A warning on every
    // build for a cell that does not exist.
    public static class ReadoutCarryTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Readout warns only for what it draws")]
        public static void Run()
        {
            int fail = 0;
            void Check(string what, bool ok)
            {
                if (!ok) fail++;
                Debug.Log($"[ReadoutCarryTest] {(ok ? "ok   " : "WRONG")} {what}");
            }

            string shaderPath = Directory.Exists("Assets/YAPS/Generated/Shaders")
                ? Directory.GetFiles("Assets/YAPS/Generated/Shaders", "*.shader")
                    .Select(p => p.Replace('\\', '/'))
                    .FirstOrDefault(p => { var t = File.ReadAllText(p); return t.Contains("_YAPS_JellyLattice") && t.Contains("_YAPS_PlugIndex"); })
                : null;
            var plugShader = shaderPath != null ? AssetDatabase.LoadAssetAtPath<Shader>(shaderPath) : null;
            var readoutShader = Shader.Find("YAPS/Debug Overlay");
            var copy = typeof(YapsDebugOverlayBuilder).GetMethod("Copy", BindingFlags.NonPublic | BindingFlags.Static);
            if (plugShader == null || readoutShader == null || copy == null)
            {
                Debug.Log($"[ReadoutCarryTest] WRONG setup: plug shader {plugShader != null}, readout {readoutShader != null}, Copy {copy != null}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            var plug = new Material(plugShader);
            var readout = new Material(readoutShader);
            var report = new BridgeReport();
            try
            {
                copy.Invoke(null, new object[] { plug, readout, "plug", report });
                var warning = report.Entries.FirstOrDefault(e => e.Detail != null && e.Detail.Contains("could not carry"));
                Check("no warning for the plug number or the jelly's values" + (warning != null ? ": " + warning.Detail : ""),
                    warning == null || !(warning.Detail.Contains("_YAPS_PlugIndex") || warning.Detail.Contains("_YAPS_Jelly")));
            }
            finally
            {
                Object.DestroyImmediate(plug);
                Object.DestroyImmediate(readout);
            }

            Debug.Log($"[ReadoutCarryTest] {(fail == 0 ? "PASS" : fail + " WRONG")}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
