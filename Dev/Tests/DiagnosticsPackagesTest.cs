using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // The Diagnostics package table looked Modular Avatar up by a type no MA release has, so
    // every report said "not installed" beside a conversion that had just baked it. Each
    // package that is loaded here must read as installed.
    public static class DiagnosticsPackagesTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test: diagnostics packages")]
        public static void Run()
        {
            int fail = 0;
            var packages = typeof(BridgeReport).Assembly.GetType("AvatarBridge.DiagnosticsWriter")
                ?.GetMethod("Packages", BindingFlags.NonPublic | BindingFlags.Static);
            string table = packages?.Invoke(null, new object[] { null }) as string;
            if (table == null)
            {
                Debug.Log("[DiagnosticsPackagesTest] WRONG cannot read the package table");
                fail++;
            }
            else
            {
                bool Loaded(string type) => AppDomain.CurrentDomain.GetAssemblies().Any(a => { try { return a.GetType(type, false) != null; } catch { return false; } });
                foreach (var (row, type) in new[]
                {
                    ("Modular Avatar", "nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator"),
                    ("VRCFury", "VF.Model.VRCFury"),
                    ("ChilloutVR CCK", "ABI.CCK.Components.CVRAvatar"),
                })
                {
                    if (!Loaded(type)) continue;
                    string line = table.Split('\n').FirstOrDefault(l => l.StartsWith("| " + row + " |")) ?? "(no row)";
                    bool ok = !line.Contains("not installed") && line != "(no row)";
                    Debug.Log($"[DiagnosticsPackagesTest] {(ok ? "ok  " : "WRONG")} {line.Trim()}");
                    if (!ok) fail++;
                }
            }
            Debug.Log($"[DiagnosticsPackagesTest] {(fail == 0 ? "PASS" : fail + " WRONG")}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
