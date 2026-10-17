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
// PASS is the spike copy importing with no error, the vertex stage of every
// pass compiling for D3D, and no message the baseline copy lacks: Poiyomi and
// the shipped includes already warn, so a plain count is never zero. The bytecode sizes
// say what the spike costs in Unity's own compile rather than fxc's listing.
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
                // same source line. A set, since a cached import repeats a
                // compile's messages in one copy and not the other.
                var known = new HashSet<string>(baseline.Keys);
                var added = spiked.Keys.Where(k => !known.Contains(k)).ToList();
                bool pass = !spiked.Error && spiked.Failed == 0 && added.Count == 0;
                Debug.Log(Tag + $"{(pass ? "PASS" : "FAIL")} {name}: spike error {spiked.Error}, " +
                    $"{spiked.Failed} of {spiked.Stages} vertex stages failed, import messages {spiked.Imported} " +
                    $"(baseline {baseline.Imported}), compile messages {spiked.Keys.Count} (baseline {baseline.Keys.Count}), " +
                    $"{added.Count} added by the spike, bytecode {spiked.Bytes} vs {baseline.Bytes} " +
                    $"({(baseline.Bytes > 0 ? (spiked.Bytes - baseline.Bytes) * 100.0 / baseline.Bytes : 0):+0.00;-0.00}%)");
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

        struct Result { public bool Error; public int Stages, Failed, Imported; public long Bytes; public List<string> Keys; }

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

            // Import messages are what the inspector shows; the vertex compile
            // is the real D3D compile of every pass the plug draws in.
            var imported = ShaderUtil.GetShaderMessages(shader);
            var r = new Result { Error = ShaderUtil.ShaderHasError(shader), Imported = imported.Length, Keys = new List<string>() };
            var messages = new List<ShaderMessage>(imported);
            var data = ShaderUtil.GetShaderData(shader);
            for (int s = 0; s < data.SubshaderCount; s++)
            {
                var sub = data.GetSubshader(s);
                for (int p = 0; p < sub.PassCount; p++)
                {
                    var pass = sub.GetPass(p);
                    if (!pass.HasShaderStage(ShaderType.Vertex)) continue;
                    var info = pass.CompileVariant(ShaderType.Vertex, new string[0],
                        ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
                    r.Stages++;
                    if (!info.Success) r.Failed++;
                    r.Bytes += info.ShaderData?.Length ?? 0;
                    if (info.Messages != null) messages.AddRange(info.Messages);
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
            Debug.Log(Tag + $"{variant} {name}: error {r.Error}, {r.Stages} vertex stages, {r.Failed} failed, " +
                $"{r.Imported} import and {r.Keys.Count} total messages, {r.Bytes} bytes, {clock.ElapsedMilliseconds} ms");
            return r;
        }

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
