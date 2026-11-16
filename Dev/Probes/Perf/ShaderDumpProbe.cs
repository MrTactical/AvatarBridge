using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace AvatarBridge.Regression
{
    // Compiles the D3D11 programs a viewer's client draws for one avatar (every material, every pass,
    // the variant a lit scene picks) and writes the bytecode, so d3d-load.py can time the driver's
    // first draw of each. -dumpPrefab <a.prefab>[,<b.prefab>] -dumpOut <dir> [-dumpStereo]
    public static class ShaderDumpProbe
    {
        static string Arg(string name, string fallback)
        {
            var a = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : fallback;
        }

        // The built-in keywords a forward-lit scene with one shadowed directional light and a point light picks.
        static string[] LightKeywords(string mode)
        {
            switch (mode)
            {
                case "ForwardBase": return new[] { "DIRECTIONAL", "LIGHTPROBE_SH", "SHADOWS_SCREEN" };
                case "ForwardAdd": return new[] { "POINT" };
                case "ShadowCaster": return new[] { "SHADOWS_DEPTH" };
                default: return new string[0];
            }
        }

        public static void Run()
        {
            int code = 1;
            try
            {
                string outDir = Arg("-dumpOut", "Temp/ShaderDump");
                bool stereo = System.Environment.GetCommandLineArgs().Contains("-dumpStereo");
                Directory.CreateDirectory(outDir);
                var index = new List<string> { "label\tmaterial\tshader\tpass\tlightmode\tstage\tbytes\tcompile_s\tfile" };
                foreach (string prefab in Arg("-dumpPrefab", "").Split(','))
                {
                    var root = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
                    if (root == null) { Debug.LogError($"[ShaderDump] no prefab {prefab}"); continue; }
                    string label = Path.GetFileNameWithoutExtension(prefab);
                    // Everything the bundle carries: blitter steps and clip-swapped materials sit on no renderer.
                    var mats = AssetDatabase.GetDependencies(prefab, true).Where(d => d.EndsWith(".mat"))
                        .Select(AssetDatabase.LoadAssetAtPath<Material>)
                        .Where(m => m != null && m.shader != null).Distinct().ToList();
                    Debug.Log($"[ShaderDump] {label}: {mats.Count} materials, {mats.Select(m => m.shader).Distinct().Count()} shaders");
                    var seen = new HashSet<string>();
                    int n = 0;
                    foreach (var m in mats)
                    {
                        var data = ShaderUtil.GetShaderData(m.shader);
                        var sub = data.ActiveSubshader;
                        if (sub == null) continue;
                        for (int p = 0; p < sub.PassCount; p++)
                        {
                            var pass = sub.GetPass(p);
                            if (pass.IsGrabPass) continue;
                            string mode = pass.FindTagValue(new ShaderTagId("LightMode")).name ?? "";
                            if (mode == "Meta") continue;
                            var kw = m.shaderKeywords.Concat(LightKeywords(mode)).ToList();
                            if (stereo) kw.Add("STEREO_INSTANCING_ON");
                            string key = $"{m.shader.GetInstanceID()}|{p}|{string.Join(" ", kw.OrderBy(k => k))}";
                            if (!seen.Add(key)) continue;
                            n++;
                            foreach (var stage in new[] { ShaderType.Vertex, ShaderType.Fragment, ShaderType.Hull, ShaderType.Domain, ShaderType.Geometry })
                            {
                                if (!pass.HasShaderStage(stage)) continue;
                                var sw = Stopwatch.StartNew();
                                var info = pass.CompileVariant(stage, kw.ToArray(), ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64, true);
                                double s = sw.Elapsed.TotalSeconds;
                                if (!info.Success || info.ShaderData == null || info.ShaderData.Length == 0)
                                {
                                    Debug.LogWarning($"[ShaderDump] FAIL {label} {m.name} {pass.Name} {stage}: {string.Join("; ", info.Messages.Select(x => x.message).Take(3))}");
                                    continue;
                                }
                                string file = $"{label}_{n:D3}_{stage}.dxbc";
                                File.WriteAllBytes(Path.Combine(outDir, file), info.ShaderData);
                                index.Add($"{label}\t{m.name}\t{m.shader.name}\t{pass.Name}\t{mode}\t{stage}\t{info.ShaderData.Length}\t{s:F1}\t{file}");
                                Debug.Log($"[ShaderDump] {label} {m.name} | {pass.Name} ({mode}) {stage}: {info.ShaderData.Length / 1024} KB in {s:F1} s");
                            }
                        }
                    }
                }
                File.WriteAllLines(Path.Combine(outDir, "index.tsv"), index);
                code = 0;
            }
            catch (System.Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(code);
        }
    }
}
