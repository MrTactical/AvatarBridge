// Phase 1b verification. Runs the YAPS patcher against every distinct
// shader used by a material in the conversion outputs, and reports what
// happened to each.
//
// A patcher that works on the one shader it was written against proves
// nothing. Avatar shaders in the wild are Poiyomi-locked monsters,
// hand-written unlit things, ancient forks with their own include trees —
// and the useful question is not "does it work" but "how often, and when
// it refuses, does it refuse for a reason we understand".
//
//   Unity.exe -batchmode -projectPath "<test project>" \
//     -executeMethod AvatarBridge.Spike.YapsPatcherProbe.RunBatch -quit
#if UNITY_EDITOR && VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Spike
{
    public static class YapsPatcherProbe
    {
        const string OutputDir = "Assets/SpsSpike/YapsPatchProbe";
        static readonly StringBuilder Log = new StringBuilder();

        [MenuItem("AvatarBridge/Spike/Probe the YAPS shader patcher")]
        public static void RunBatch()
        {
            Log.Clear();
            Line("# Phase 1b — shader patcher probe");
            Line("");
            Line($"Run: {DateTime.Now:yyyy-MM-dd HH:mm}");
            Line("");

            var materials = FindDistinctShaderMaterials();
            Line($"Trying **{materials.Count}** distinct shaders taken from real converted avatars.");
            Line("");

            int patched = 0;
            var refusals = new List<(string shader, string why)>();

            Line("| Shader | Result |");
            Line("|---|---|");
            foreach (var material in materials)
            {
                string shaderName = material.shader != null ? material.shader.name : "(none)";
                string refusal;
                Shader result = null;
                try
                {
                    result = YapsShaderPatcher.Patch(material, OutputDir, null, out refusal, out _);
                }
                catch (Exception e)
                {
                    refusal = $"threw {e.GetType().Name}: {e.Message}";
                }

                if (result != null)
                {
                    patched++;
                    Line($"| `{Short(shaderName)}` | **patched** |");
                }
                else
                {
                    refusals.Add((shaderName, refusal));
                    Line($"| `{Short(shaderName)}` | refused — {refusal} |");
                }
            }

            Line("");
            Line($"**{patched} patched, {refusals.Count} refused, of {materials.Count}.**");
            Line("");

            if (refusals.Count > 0)
            {
                Line("## Refusals grouped");
                Line("");
                foreach (var group in refusals.GroupBy(r => Bucket(r.why)).OrderByDescending(g => g.Count()))
                {
                    Line($"- **{group.Count()}× {group.Key}**");
                    foreach (var one in group.Take(4))
                    {
                        Line($"    - `{Short(one.shader)}`");
                    }
                }
                Line("");
            }

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                "YapsPatcherProbe.md"));
            File.WriteAllText(path, Log.ToString());
            Debug.Log($"[YAPS] Patcher probe written to {path} — {patched}/{materials.Count} patched");
        }

        // One material per distinct shader: patching the same shader twenty
        // times says nothing new and costs a compile each.
        static List<Material> FindDistinctShaderMaterials()
        {
            var bySharedShader = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets("t:Material",
                new[] { "Assets/AvatarBridgeOutput" }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (material == null || material.shader == null)
                {
                    continue;
                }
                if (!bySharedShader.ContainsKey(material.shader.name))
                {
                    bySharedShader[material.shader.name] = material;
                }
            }
            return bySharedShader.Values.ToList();
        }

        static string Bucket(string why)
        {
            if (why == null) return "no reason given";
            if (why.Contains("no source file")) return "no source on disk (built-in or packaged)";
            if (why.Contains("surface shader")) return "surface shader";
            if (why.Contains("Properties block")) return "no Properties block";
            if (why.Contains("does not recognise")) return "unrecognised vertex signature";
            if (why.Contains("could not be found")) return "vertex input struct not found";
            if (why.Contains("no POSITION")) return "no POSITION member";
            if (why.Contains("did not compile")) return "patched but failed to compile";
            if (why.Contains("already carries")) return "already patched";
            if (why.StartsWith("threw")) return "threw";
            return why;
        }

        static string Short(string name)
            => name.Length <= 60 ? name : name.Substring(0, 57) + "…";

        static void Line(string s) => Log.AppendLine(s);
    }
}
#endif
