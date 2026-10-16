#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    // Where a patched shader's minute goes, and whether several can compile at once. The squash
    // pass imports each patched shader and asks ShaderHasError, one after another, and the
    // compilers sit idle between shaders. Each mode below copies the same patched sets into a
    // folder of its own, with a comment that differs per mode so none starts warm from another:
    //   serial  import, then ShaderHasError per shader, as the pass does today
    //   warm    ShaderHasError again on serial's folder, same session
    //   cross   serial's text unchanged in another folder: does the cache hit across folders
    //   async   every pass of every shader queued with CompilePass, waited for on editor ticks
    //           (a blocking wait spins forever in batch mode), then ShaderHasError per shader
    //   vertex  the vertex stage of every pass through CompileVariant, the check a cheaper
    //           verify would make
    //   restart ShaderHasError on serial's folder after a restart: does the cache persist
    // Run without -quit, since async needs editor ticks; the probe exits itself:
    //   -executeMethod AvatarBridge.Regression.ShaderVerifyCostProbe.Run
    //   -scvSource Assets/__SCV_Source [-scvModes serial,warm,cross,async,vertex]
    // Then a second launch with -scvModes restart. The source folder holds patched sets copied
    // out of an output folder, includes beside their shaders. Nothing is cleaned up, so restart
    // finds serial's folder: delete Assets/__SCV and the source folder by hand afterwards.
    public static class ShaderVerifyCostProbe
    {
        const string Root = "Assets/__SCV";
        const double TimeoutSeconds = 3600;

        static void Log(string s) => Debug.Log("[ShaderVerifyCost] " + s);

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        public static void Run()
        {
            string source = Arg("-scvSource") ?? "Assets/__SCV_Source";
            var modes = (Arg("-scvModes") ?? "serial,warm,cross,async,vertex").Split(',').Where(s => s.Length > 0).ToList();
            Log($"GPU {SystemInfo.graphicsDeviceName}, CPU {SystemInfo.processorType} x{SystemInfo.processorCount}, " +
                $"source {source}, modes {string.Join(",", modes)}");
            if (!Directory.Exists(source))
            {
                Log("FAIL no source folder");
                Exit(1);
                return;
            }
            try
            {
                foreach (string mode in modes.TakeWhile(m => m != "async"))
                {
                    RunMode(mode, source);
                }
            }
            catch (Exception e)
            {
                Log("FAIL " + e);
                Exit(1);
                return;
            }
            var rest = modes.SkipWhile(m => m != "async").ToList();
            if (rest.Count == 0)
            {
                Exit(0);
                return;
            }
            StartAsync(source, rest.Skip(1).ToList());
        }

        static void RunMode(string mode, string source)
        {
            switch (mode)
            {
                case "serial":
                {
                    var shaders = Place(source, Root + "/serial", "serial");
                    TimeErrors("serial", shaders);
                    break;
                }
                case "warm":
                case "restart":
                {
                    var shaders = Existing(Root + "/serial");
                    if (shaders.Count == 0) { Log($"{mode}: run serial first"); return; }
                    TimeErrors(mode, shaders);
                    break;
                }
                case "cross":
                {
                    if (!Directory.Exists(Root + "/serial")) { Log("cross: run serial first"); return; }
                    var shaders = Place(Root + "/serial", Root + "/cross", null);
                    TimeErrors("cross", shaders);
                    break;
                }
                case "vertex":
                {
                    var shaders = Place(source, Root + "/vertex", "vertex");
                    TimeVertex(shaders);
                    break;
                }
                default:
                    Log($"unknown mode {mode}");
                    break;
            }
        }

        // Copies every file, stamping each .shader with the mode so its text, and the cache key,
        // is its own. Imported in one batch, includes and shaders together.
        static List<Shader> Place(string from, string to, string stamp)
        {
            if (AssetDatabase.IsValidFolder(to)) AssetDatabase.DeleteAsset(to);
            Directory.CreateDirectory(to);
            var paths = new List<string>();
            foreach (string file in Directory.GetFiles(from).Where(f => !f.EndsWith(".meta")))
            {
                string dst = to + "/" + Path.GetFileName(file);
                string text = File.ReadAllText(file);
                if (stamp != null && file.EndsWith(".shader")) text = $"// probe {stamp}\n" + text;
                File.WriteAllText(dst, text);
                paths.Add(dst);
            }
            var clock = Stopwatch.StartNew();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Log($"{Path.GetFileName(to)}: imported {paths.Count} files in {clock.ElapsedMilliseconds} ms");
            return Existing(to);
        }

        static List<Shader> Existing(string folder) =>
            AssetDatabase.FindAssets("t:Shader", new[] { folder })
                .Select(g => AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(s => s != null).OrderBy(s => s.name, StringComparer.Ordinal).ToList();

        static void TimeErrors(string mode, List<Shader> shaders)
        {
            long total = 0;
            foreach (var shader in shaders)
            {
                var clock = Stopwatch.StartNew();
                bool error = ShaderUtil.ShaderHasError(shader);
                total += clock.ElapsedMilliseconds;
                Log($"{mode}: {Short(shader)} {clock.ElapsedMilliseconds} ms, {shader.passCount} passes{(error ? ", HAS ERROR" : "")}");
            }
            Log($"{mode}: TOTAL {total} ms over {shaders.Count} shaders");
        }

        static void TimeVertex(List<Shader> shaders)
        {
            long total = 0;
            foreach (var shader in shaders)
            {
                var clock = Stopwatch.StartNew();
                int failed = 0, compiled = 0;
                var data = ShaderUtil.GetShaderData(shader);
                for (int s = 0; s < data.SubshaderCount; s++)
                {
                    var subshader = data.GetSubshader(s);
                    for (int p = 0; p < subshader.PassCount; p++)
                    {
                        var pass = subshader.GetPass(p);
                        if (!pass.HasShaderStage(ShaderType.Vertex)) continue;
                        var info = pass.CompileVariant(ShaderType.Vertex, new string[0],
                            ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
                        compiled++;
                        if (!info.Success) failed++;
                    }
                }
                total += clock.ElapsedMilliseconds;
                Log($"vertex: {Short(shader)} {clock.ElapsedMilliseconds} ms, {compiled} stages, {failed} failed");
            }
            Log($"vertex: TOTAL {total} ms over {shaders.Count} shaders");
        }

        static string Short(Shader shader) => shader.name.Length > 60 ? "..." + shader.name.Substring(shader.name.Length - 57) : shader.name;

        // State for the async mode, which spans editor ticks.
        static List<Shader> asyncShaders;
        static List<Material> asyncMaterials;
        static List<string> afterAsync;
        static Stopwatch asyncClock;
        static bool wasAsync;
        static int peakCompilers;

        static void StartAsync(string source, List<string> after)
        {
            try
            {
                asyncShaders = Place(source, Root + "/async", "async");
                afterAsync = after;
                wasAsync = ShaderUtil.allowAsyncCompilation;
                ShaderUtil.allowAsyncCompilation = true;
                asyncMaterials = asyncShaders.Select(s => new Material(s)).ToList();
                asyncClock = Stopwatch.StartNew();
                int queued = 0;
                foreach (var material in asyncMaterials)
                {
                    for (int p = 0; p < material.shader.passCount; p++)
                    {
                        ShaderUtil.CompilePass(material, p, false);
                        queued++;
                    }
                }
                Log($"async: queued {queued} passes over {asyncShaders.Count} shaders in {asyncClock.ElapsedMilliseconds} ms, " +
                    $"anythingCompiling={ShaderUtil.anythingCompiling}");
                EditorApplication.update += AsyncTick;
            }
            catch (Exception e)
            {
                Log("async FAIL " + e);
                FinishAsync(1);
            }
        }

        static void AsyncTick()
        {
            peakCompilers = Math.Max(peakCompilers, Process.GetProcessesByName("UnityShaderCompiler").Length);
            if (ShaderUtil.anythingCompiling && asyncClock.Elapsed.TotalSeconds < TimeoutSeconds)
            {
                return;
            }
            EditorApplication.update -= AsyncTick;
            Log($"async: compiling finished after {asyncClock.ElapsedMilliseconds} ms " +
                $"(timed out: {asyncClock.Elapsed.TotalSeconds >= TimeoutSeconds}, compiler processes seen: {peakCompilers})");
            int code = 0;
            try
            {
                ShaderUtil.allowAsyncCompilation = wasAsync;
                TimeErrors("async-then-check", asyncShaders);
                foreach (string mode in afterAsync)
                {
                    RunMode(mode, Arg("-scvSource") ?? "Assets/__SCV_Source");
                }
            }
            catch (Exception e)
            {
                Log("FAIL " + e);
                code = 1;
            }
            FinishAsync(code);
        }

        static void FinishAsync(int code)
        {
            ShaderUtil.allowAsyncCompilation = wasAsync;
            foreach (var material in asyncMaterials ?? new List<Material>())
            {
                if (material != null) Object.DestroyImmediate(material);
            }
            Exit(code);
        }

        static void Exit(int code)
        {
            Log(code == 0 ? "DONE" : "DONE with failures");
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
#endif
