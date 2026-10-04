// Every shader the project's materials use, put through the stereo patcher
// one at a time, each verdict logged as one tab-separated line, so two builds
// of the patcher can be compared shader by shader on real avatars.
//
// Run: -executeMethod AvatarBridge.Regression.SpiSweepProbe.Run
// Lines: [SpiSweep] <shader> <verdict> <detail>
#if CVR_CCK_EXISTS
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class SpiSweepProbe
    {
        const string Out = "Assets/__SpiSweep";

        public static void Run()
        {
            try
            {
                AssetDatabase.DeleteAsset(Out);
                var materials = AssetDatabase.FindAssets("t:Material")
                    .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)))
                    .Where(m => m != null && m.shader != null && !m.shader.name.EndsWith(" (SPI)")
                                && !m.shader.name.StartsWith("Hidden/YAPS") && !m.shader.name.StartsWith("Hidden/Locked/YAPS"))
                    .GroupBy(m => m.shader).Select(g => g.First()).OrderBy(m => m.shader.name, StringComparer.Ordinal).ToList();
                Debug.Log($"[SpiSweep]\t#\t{materials.Count} shaders");
                int i = 0;
                foreach (var material in materials)
                {
                    var go = new GameObject("SpiSweep");
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    var report = new BridgeReport();
                    string name = material.shader.name;
                    string verdict, detail = "";
                    try
                    {
                        ShaderSpiPatcher.Patch(go, $"{Out}/{i++}", null, report);
                        var entries = report.Entries;
                        if (renderer.sharedMaterial.shader.name.EndsWith(" (SPI)"))
                        {
                            var partial = entries.FirstOrDefault(e => (e.Subject ?? "").Contains("some passes only"));
                            verdict = partial != null ? "partial" : "patched";
                            detail = partial?.Detail ?? "";
                        }
                        else if (entries.Any(e => (e.Subject ?? "").Contains("could not be patched")))
                        {
                            verdict = "refused";
                            detail = entries.First(e => (e.Subject ?? "").Contains("could not be patched")).Detail;
                        }
                        else if (entries.Any(e => (e.Subject ?? "").Contains("already speak")))
                        {
                            verdict = "correct";
                        }
                        else
                        {
                            verdict = "nosource";
                        }
                    }
                    catch (Exception e)
                    {
                        verdict = "threw";
                        detail = e.GetType().Name + ": " + e.Message;
                    }
                    detail = detail.Replace('\n', ' ').Replace('\t', ' ');
                    Debug.Log($"[SpiSweep]\t{name}\t{verdict}\t{(detail.Length > 300 ? detail.Substring(0, 300) : detail)}");
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            AssetDatabase.DeleteAsset(Out);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
#endif
