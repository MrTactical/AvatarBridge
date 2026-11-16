#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AvatarBridge.Regression
{
    // Where the live tissue's time goes, and what the error check sees: copies of the named squash
    // materials take the tissue's role one at a time, as YapsJelly.On does, each step timed. Then
    // the editor ticks until the shader compiler is idle and every new shader is checked again,
    // since a compile that times out was reported as clean once. Run without -quit; it exits itself:
    //   -executeMethod AvatarBridge.Regression.TissueCompileProbe.Run -tissueMaterials "Assets/a.mat|Assets/b.mat"
    // Leaves Assets/__TissueCompile and the new shaders in the YAPS folder for the caller to move out.
    public static class TissueCompileProbe
    {
        const string Folder = "Assets/__TissueCompile";
        const double IdleTimeoutSeconds = 1800;

        static void Log(string s) => Debug.Log("[TissueCompile] " + s);

        static readonly List<Shader> Made = new List<Shader>();
        static Stopwatch idle, all;

        public static void Run()
        {
            all = Stopwatch.StartNew();
            try
            {
                var args = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(args, "-tissueMaterials");
                if (at < 0 || at + 1 >= args.Length) throw new ArgumentException("-tissueMaterials is required");
                Log($"CPU {SystemInfo.processorType} x{SystemInfo.processorCount}, async compile {ShaderUtil.allowAsyncCompilation}");
                if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Folder.Substring("Assets/".Length));
                var report = new BridgeReport();
                foreach (string path in args[at + 1].Split('|'))
                {
                    string copy = AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + Path.GetFileName(path));
                    if (!AssetDatabase.CopyAsset(path, copy)) { Log($"MISSING {path}"); continue; }
                    var m = AssetDatabase.LoadAssetAtPath<Material>(copy);
                    string before = m.shader.name;
                    var clock = Stopwatch.StartNew();
                    var shader = YapsShaderPatcher.WithRole(m, YapsShaderPatcher.Roles.Jelly, report, out string why);
                    long patch = clock.ElapsedMilliseconds;
                    clock.Restart();
                    bool error = shader != null && ShaderUtil.ShaderHasError(shader);
                    long check = clock.ElapsedMilliseconds;
                    bool busy = ShaderUtil.anythingCompiling;
                    string imported = Messages(shader);
                    clock.Restart();
                    if (shader != null && !error) YapsShaderPatcher.Wear(m, shader);
                    long wear = clock.ElapsedMilliseconds;
                    bool worn = shader != null && ShaderUtil.ShaderHasError(shader);
                    string wornSaid = Messages(shader);
                    clock.Restart();
                    EditorUtility.SetDirty(m);
                    AssetDatabase.SaveAssets();
                    long save = clock.ElapsedMilliseconds;
                    Log($"{m.name}: {before} -> {(shader == null ? "none" : shader.name + " at " + AssetDatabase.GetAssetPath(shader))}; " +
                        (why != null ? $"refused: {why}; " : "") + $"{m.shaderKeywords.Length} keywords; patch and import {patch} ms, error check {check} ms says {error}, {imported}, compiler busy after {busy}; " +
                        $"wear {wear} ms, then the check says {worn}, {wornSaid}, busy {ShaderUtil.anythingCompiling}; save {save} ms, then the check says " +
                        $"{shader != null && ShaderUtil.ShaderHasError(shader)}, {Messages(shader)}");
                    if (shader != null) Made.Add(shader);
                }
                foreach (var e in report.Entries) Log($"report: {e.Status} [{e.Category}] {e.Subject} | {e.Detail}");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Exit(1);
                return;
            }
            idle = Stopwatch.StartNew();
            EditorApplication.update += Tick;
        }

        // Errors and warnings Unity holds for the shader now.
        static string Messages(Shader shader)
        {
            if (shader == null) return "no shader";
            var all = ShaderUtil.GetShaderMessages(shader);
            var errors = all.Where(x => x.severity == ShaderCompilerMessageSeverity.Error).ToArray();
            return $"{errors.Length} error(s) of {all.Length} message(s){(errors.Length > 0 ? ", first: " + errors[0].message.Split('\n')[0] : "")}";
        }

        static void Tick()
        {
            if (ShaderUtil.anythingCompiling && idle.Elapsed.TotalSeconds < IdleTimeoutSeconds) return;
            EditorApplication.update -= Tick;
            Log($"compiler idle {(ShaderUtil.anythingCompiling ? "NEVER, gave up" : "after")} {idle.ElapsedMilliseconds} ms more");
            foreach (var s in Made.Distinct())
                Log($"after idle: {s.name} error check says {ShaderUtil.ShaderHasError(s)}, supported {s.isSupported}, {Messages(s)}");
            Log($"total {all.ElapsedMilliseconds} ms");
            Exit(0);
        }

        static void Exit(int code)
        {
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
#endif
