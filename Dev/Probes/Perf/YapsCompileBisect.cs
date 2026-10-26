#if CVR_CCK_EXISTS && UNITY_EDITOR && AVATARBRIDGE_YAPS
// Which part of the YAPS vertex code makes each shader variant slow to compile.
// An upload compiles every variant of every patched shader its first time, and
// the YAPS vertex stage costs about 60x the host's per variant (UploadCostProbe).
//
//   -executeMethod AvatarBridge.Regression.YapsCompileBisect.Run
//       -ycbSource <a patched .shader> [-ycbEntry vertBase] [-ycbTarget 4.0] [-ycbCases squash,all]
//
// Lifts the YAPS block out of a patched shader, wraps it in a bare vertex
// shader, and compiles one vertex variant per case: roles switched on and off,
// loop bounds cut. Each case is salted so none starts from the cache.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AvatarBridge.Regression
{
    public static class YapsCompileBisect
    {
        const string Root = "Assets/__YCB";
        static void Log(string s) => Debug.Log("[YapsCompileBisect] " + s);

        static readonly (string name, string defines, string from, string to)[] Cases =
        {
            ("none", "#define YAPS_NO_PLUG\n#define YAPS_NO_SOCKET\n#define YAPS_NO_SQUASH\n", null, null),
            ("plug", "#define YAPS_NO_SOCKET\n#define YAPS_NO_SQUASH\n", null, null),
            ("socket", "#define YAPS_NO_PLUG\n#define YAPS_NO_SQUASH\n", null, null),
            ("squash", "#define YAPS_NO_PLUG\n#define YAPS_NO_SOCKET\n", null, null),
            ("squash+socket", "#define YAPS_NO_PLUG\n", null, null),
            ("all", "", null, null),
            ("plug+dup", "#define YAPS_NO_SOCKET\n#define YAPS_NO_SQUASH\n#define YAPS_DUPLICATE\n", null, null),
            ("plug walk4", "#define YAPS_NO_SOCKET\n#define YAPS_NO_SQUASH\n", "#define YAPS_WALK_STEPS 48", "#define YAPS_WALK_STEPS 4"),
            ("plug radius0", "#define YAPS_NO_SOCKET\n#define YAPS_NO_SQUASH\n", "#define YAPS_ATLAS_RADIUS 1", "#define YAPS_ATLAS_RADIUS 0"),
        };

        public static void Run()
        {
            int code = 0;
            try { Measure(); }
            catch (Exception e) { Debug.LogException(e); code = 2; }
            finally { AssetDatabase.DeleteAsset(Root); }
            Log("DONE");
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static void Measure()
        {
            string source = Arg("-ycbSource") ?? throw new Exception("-ycbSource missing");
            string entry = Arg("-ycbEntry") ?? "vertBase";
            string target = Arg("-ycbTarget") ?? "4.0";
            var lines = File.ReadAllLines(source);
            // The block runs from the source's own header comment to the host's entry function.
            int start = Array.FindIndex(lines, l => l.StartsWith("// YAPS, Yet Another Penetration System"));
            int end = Array.FindIndex(lines, start + 1, l => l.Contains(" " + entry + "("));
            if (start < 0 || end < 0) throw new Exception($"no YAPS block before {entry} in {source}");
            string block = string.Join("\n", lines.Skip(start).Take(end - start));
            Log($"block {end - start} lines from {source}, target {target}");

            Directory.CreateDirectory(Root);
            var paths = new List<(string name, string path)>();
            var only = Arg("-ycbCases")?.Split(',');
            foreach (var c in Cases.Where(c => only == null || only.Contains(c.name)))
            {
                string body = c.from == null ? block : block.Replace(c.from, c.to);
                if (c.from != null && body == block) Log($"{c.name}: \"{c.from}\" not found, case is the plain one");
                string path = $"{Root}/{c.name.Replace(' ', '_').Replace('+', '_')}.shader";
                File.WriteAllText(path, Wrap(c.name, target, c.defines, body));
                paths.Add((c.name, path));
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            foreach (var (name, path) in paths)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
                var sw = Stopwatch.StartNew();
                var info = pass.CompileVariant(ShaderType.Vertex, new string[0], ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
                double first = sw.Elapsed.TotalSeconds;
                sw.Restart();
                pass.CompileVariant(ShaderType.Vertex, new[] { "INSTANCING_ON" }, ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
                double second = sw.Elapsed.TotalSeconds;
                string err = info.Success ? "" : " ERRORS: " + string.Join(" | ", info.Messages.Take(3).Select(m => m.message));
                Log($"case {name,-14} vertex {first:0.00} s, instanced {second:0.00} s, bytes {info.ShaderData?.Length ?? 0}{err}");
            }
        }

        static string Wrap(string name, string target, string defines, string body) => $@"Shader ""Hidden/YCB/{name}"" {{ SubShader {{ Pass {{
CGPROGRAM
#pragma target {target}
#pragma vertex ycbVert
#pragma fragment ycbFrag
#pragma multi_compile_instancing
#include ""UnityCG.cginc""
// salt {Guid.NewGuid()}
{defines}
{body}
struct ycb_in {{ float4 vertex : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT; uint vertexId : SV_VertexID; UNITY_VERTEX_INPUT_INSTANCE_ID }};
float4 ycbVert(ycb_in v) : SV_POSITION
{{
    UNITY_SETUP_INSTANCE_ID(v);
    float3 p = v.vertex.xyz, n = v.normal, t = v.tangent.xyz;
#ifndef YAPS_NO_PLUG
    YapsDeform(p, n, t, v.vertexId);
#endif
#ifndef YAPS_NO_SOCKET
    YapsSocketDeform(p, n, t, v.vertexId);
#endif
#ifndef YAPS_NO_SQUASH
    YapsDentDeform(p, n, t, v.vertexId);
#endif
    return UnityObjectToClipPos(float4(p + (n + t) * 1e-4, 1));
}}
float4 ycbFrag() : SV_Target {{ return 1; }}
ENDCG
}} }} }}
";

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
#endif
