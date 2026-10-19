// Does a locked Poiyomi plug pass still compile with the portal spike in it?
//
//   -executeMethod AvatarBridge.Regression.PortalHeadroom.Run
//       -portalShipped <Editor/Yaps> -portalIncludes <Dev/Probes/Portal>
//       [-portalOnly Fur]   only families whose name contains it, for a quick check
//
// Takes the largest locked YAPS patch of each Poiyomi family in the project and
// imports two copies: as patched, and with the inlined shipped includes swapped
// for the spike copies. Both folders stay outside Assets, so the patcher's own
// search for yaps_deform never meets a second copy.
//
// PASS is the spike copy importing with no error, every stage of every pass
// compiling for D3D with no keywords, the vertex stage also single-pass
// instanced, and no message the baseline copy lacks: Poiyomi and the shipped
// includes already warn, so a plain count is never zero. The vertex bytecode
// says what the spike costs in Unity's own compile rather than fxc's listing;
// the other stages' bytecode, equal or not, says whether it reaches them.
//
// Only the explicit compiles are compared. The importer's own messages depend
// on what the shader cache already held: the same baseline text gave 36, 33,
// 1 and 0 of them in different runs, so they are reported and never judged.
// The variants it compiled are not all here; the spike's own code branches
// only on stereo instancing.
//
// Locked shaders only, and headless: an unlocked Poiyomi Pro kills the editor
// mid-compile.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AvatarBridge.Regression
{
    public static class PortalHeadroom
    {
        const string Tag = "[PortalHeadroom] ";
        const string Folder = "Assets/__PortalHeadroom";
        const string Generated = "Assets/YAPS/Generated/Shaders";
        // Socket and dent are untouched by the spike.
        static readonly string[] Parts = { "yaps_props", "yaps_atlas", "yaps_resolve", "yaps_deform" };

        [MenuItem("Tools/AvatarBridge Dev/Probe: Portal headroom")]
        public static void Run()
        {
            int code = 1;
            try { code = Measure() ? 0 : 1; }
            catch (Exception e) { Debug.LogError(Tag + "FAIL " + e); }
            finally { AssetDatabase.DeleteAsset(Folder); }
            Debug.Log(Tag + (code == 0 ? "RESULT PASS" : "RESULT FAIL"));
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static bool Measure()
        {
            string shipped = Arg("-portalShipped"), spike = Arg("-portalIncludes"), only = Arg("-portalOnly");
            if (shipped == null || spike == null) throw new ArgumentException("-portalShipped and -portalIncludes are both required");

            // The largest of each family: the most passes and the most vertex work.
            var picks = Directory.GetFiles(Generated, "Poiyomi*_YAPS_*.shader")
                .GroupBy(f => Path.GetFileName(f).Split(new[] { "_YAPS_" }, StringSplitOptions.None)[0])
                .Select(g => g.OrderByDescending(f => new FileInfo(f).Length).First())
                .Where(f => only == null || Path.GetFileName(f).Contains(only))
                .OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (picks.Count == 0) throw new InvalidOperationException("no locked YAPS Poiyomi patches under " + Generated);
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__PortalHeadroom");

            bool all = true;
            for (int i = 0; i < picks.Count; i++)
            {
                string name = Path.GetFileNameWithoutExtension(picks[i]);
                string text = Lf(File.ReadAllText(picks[i]));
                string swapped = Swap(name, text, shipped, spike);
                if (swapped == null) { all = false; continue; }
                var baseline = Check(name, "baseline", i, text);
                var spiked = Check(name, "spike", i, swapped);
                // Poiyomi and the shipped includes already warn, so a message is
                // the spike's only when the baseline has no such message on the
                // same source line, counted: a pass that newly warns on a line
                // another pass already warned on is still one more.
                var left = baseline.Keys.GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
                var added = new List<string>();
                foreach (var k in spiked.Keys)
                    if (left.TryGetValue(k, out int n) && n > 0) left[k] = n - 1; else added.Add(k);
                bool pass = !spiked.Error && spiked.Failed == 0 && added.Count == 0;
                Debug.Log(Tag + $"{(pass ? "PASS" : "FAIL")} {name}: spike error {spiked.Error}, " +
                    $"{spiked.Failed} of {spiked.Stages} stages failed, compile messages {spiked.Keys.Count} " +
                    $"(baseline {baseline.Keys.Count}), {added.Count} added by the spike, vertex bytecode {spiked.Bytes} vs {baseline.Bytes} " +
                    $"({Growth(spiked.Bytes, baseline.Bytes)}), stereo {spiked.StereoBytes} vs {baseline.StereoBytes} " +
                    $"({Growth(spiked.StereoBytes, baseline.StereoBytes)}), " +
                    $"other stages {spiked.OtherBytes} vs {baseline.OtherBytes} ({(spiked.OtherBytes == baseline.OtherBytes ? "equal" : "differ")}); " +
                    $"import messages {spiked.Imported} (baseline {baseline.Imported}, not gated)");
                foreach (var g in added.GroupBy(k => k).Take(8))
                    Debug.Log(Tag + $"  added x{g.Count()}: {g.Key}");
                all &= pass && !baseline.Error && baseline.Failed == 0;
            }
            return all;
        }

        static string Swap(string name, string text, string shipped, string spike)
        {
            int calls = Count(text, "YapsDeform(yapsPosition");
            foreach (var part in Parts)
            {
                string from = Part(shipped, part);
                // Once a pass, or this is not the shipped include and the swap would measure nothing.
                int n = Count(text, from);
                if (calls == 0 || n != calls)
                {
                    Debug.Log(Tag + $"FAIL {name}: {part} inlined {n} times for {calls} deform calls");
                    return null;
                }
                text = text.Replace(from, Part(spike, part));
            }
            return text;
        }

        struct Result { public bool Error; public int Stages, Failed, Imported; public long Bytes, StereoBytes, OtherBytes; public List<string> Keys; }

        static readonly ShaderType[] Stages = { ShaderType.Vertex, ShaderType.Fragment, ShaderType.Geometry, ShaderType.Hull, ShaderType.Domain };
        static readonly string[] Plain = new string[0], Stereo = { "STEREO_INSTANCING_ON" };

        static Result Check(string name, string variant, int index, string text)
        {
            string path = $"{Folder}/{index}_{variant}.shader";
            text = new Regex("Shader\\s+\"[^\"]*\"").Replace(text, $"Shader \"Hidden/PortalHeadroom/{index}/{variant}\"", 1);
            File.WriteAllText(path, text);
            var lines = text.Split('\n');
            var clock = Stopwatch.StartNew();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null) throw new InvalidOperationException("no shader imported at " + path);

            // The real D3D compile of every stage of every pass, which never
            // answers from the cache; the import count is for the record.
            var r = new Result { Error = ShaderUtil.ShaderHasError(shader), Imported = ShaderUtil.GetShaderMessages(shader).Length, Keys = new List<string>() };
            var messages = new List<ShaderMessage>();
            var data = ShaderUtil.GetShaderData(shader);
            for (int s = 0; s < data.SubshaderCount; s++)
            {
                var sub = data.GetSubshader(s);
                for (int p = 0; p < sub.PassCount; p++)
                {
                    var pass = sub.GetPass(p);
                    foreach (var stage in Stages)
                    {
                        if (!pass.HasShaderStage(stage)) continue;
                        // The vertex stage twice: single-pass instanced, as CVR
                        // draws in VR, reads the atlas as an array, its own branch.
                        foreach (var keys in stage == ShaderType.Vertex ? new[] { Plain, Stereo } : new[] { Plain })
                        {
                            var info = pass.CompileVariant(stage, keys, ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
                            r.Stages++;
                            if (!info.Success) r.Failed++;
                            long bytes = info.ShaderData?.Length ?? 0;
                            if (stage != ShaderType.Vertex) r.OtherBytes += bytes;
                            else if (keys == Stereo) r.StereoBytes += bytes;
                            else r.Bytes += bytes;
                            if (info.Messages != null) messages.AddRange(info.Messages);
                        }
                    }
                }
            }
            // Keyed by the source line's text, not its number, which the spike shifts.
            foreach (var m in messages)
            {
                bool own = string.IsNullOrEmpty(m.file) || m.file.EndsWith(".shader");
                string at = own && m.line >= 1 && m.line <= lines.Length ? lines[m.line - 1].Trim()
                    : $"{Path.GetFileName(m.file)}:{m.line}";
                r.Keys.Add($"{m.severity} {m.message} @ {(at.Length > 90 ? at.Substring(0, 90) : at)}");
            }
            Debug.Log(Tag + $"{variant} {name}: error {r.Error}, {r.Stages} stages, {r.Failed} failed, " +
                $"{r.Keys.Count} compile messages ({r.Imported} on import, not counted), vertex {r.Bytes} bytes, " +
                $"stereo vertex {r.StereoBytes} bytes, other stages {r.OtherBytes} bytes, {clock.ElapsedMilliseconds} ms");
            return r;
        }

        static string Growth(long a, long b) => b > 0 ? ((a - b) * 100.0 / b).ToString("+0.00;-0.00") + "%" : "n/a";

        // As YapsShaderPatcher inlines it: the include lines go, the rest stays.
        static string Part(string dir, string name) =>
            Regex.Replace(Lf(File.ReadAllText(Path.Combine(dir, name + ".cginc"))), "#include\\s+\"yaps_\\w+\\.cginc\"\\s*", "");

        // Generated shaders are CRLF and the includes LF.
        static string Lf(string s) => s.Replace("\r\n", "\n");

        static int Count(string text, string what)
        {
            int n = 0;
            for (int at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal)) n++;
            return n;
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
