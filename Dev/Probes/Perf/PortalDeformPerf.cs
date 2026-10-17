// What the portal adds to a plug's vertex stage, resolve and deform together.
//
//   -executeMethod AvatarBridge.Regression.PortalDeformPerf.Run
//       -portalIncludes <Dev/Probes/Portal>   the spike copies, inlined per variant
//       [-portalLookups 0,1,2]                PORTAL_LOOKUP values to time, default 0,1,2
//       [-portalRepeats 7]
//
// YapsResolvePerf's method: 16 draws of 2^20 points into a 1024 square, best
// of eight renders after a warm one, the floor pass subtracted, reported for a
// 6433-vertex plug at L 0.649 (two reads). The atlas is the same 1024 half
// target, empty or full: full is every cell header counting a socket whose tag
// never matches, and every directory slot holding an entry nobody asks for.
//
// A hole sits a hundredth of a length ahead of the root and every vertex is
// baked past it, so all of them take the end of the path. Three contents:
//   keyless  the hole has no owner, so chain.portal is 0             (b)
//   miss     owner and number, no entry: any 5.0 hole that is no portal
//   portal   owner, number and an entry, so every vertex is cut     (c)
// The cases of one content are timed render by render in turn, so clock
// drift lands on all of them alike; the gate reads medians over repeats.
//
// Before timing, every case is checked: what the resolve and lookup
// answered, where each deformed vertex ended up, and how far the decoded
// exit is from the one written. A case that did not take the path it names
// fails the run rather than timing the wrong thing.
//
// Gates, per variant and fill: keyless within max(1 us, baseline spread) of
// the baseline; portal at most 21.6 us over it, what the second atlas read
// was accepted at. The miss case is reported against the same noise bound.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    public static class PortalDeformPerf
    {
        const string Tag = "[PortalDeformPerf] ";
        const string Folder = "Assets/__PortalDeformPerf";
        static readonly string[] Parts = { "yaps_props", "yaps_atlas", "yaps_resolve", "yaps_deform" };
        const string BaselineInclude = "#include \"../Yaps/yaps_deform.cginc\"";

        const int Plug = 6433, Vertices = 1 << 20, Draws = 16, Size = 1024;
        const float Length = 0.649f, Span = 0.02f, DirCell = 0.32f;
        const int Owner = 0x5A3C17, Number = 3;
        // Past 41 m from the origin, so a cell index decoded as absolute shows.
        static readonly Vector3 Root = new Vector3(60.3f, 1.0f, 20.2f);
        static readonly Vector3 Entrance = Root + new Vector3(0, 0, 0.01f * Length);
        static readonly Vector3 Exit = Root + new Vector3(1.3f, 0.4f, -0.9f);
        static readonly Vector3 ExitFwd = new Vector3(0.6f, 0.2f, -0.77f).normalized;
        const double GateNoise = 1.0, GatePortal = 21.6;

        enum Content { Keyless, Miss, Portal }

        [MenuItem("Tools/AvatarBridge Dev/Probe: Portal deform perf")]
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
            string spike = Arg("-portalIncludes");
            if (spike == null) throw new ArgumentException("-portalIncludes is required");
            var lookups = (Arg("-portalLookups") ?? "0,1,2").Split(',').Select(int.Parse).ToArray();
            int repeats = int.Parse(Arg("-portalRepeats") ?? "7");

            var baseShader = Shader.Find("Hidden/PortalDeformPerf");
            var writer = Shader.Find("YAPS/Atlas Socket");
            if (baseShader == null || !baseShader.isSupported || ShaderUtil.ShaderHasError(baseShader))
                throw new InvalidOperationException("Hidden/PortalDeformPerf did not compile");
            if (writer == null) throw new InvalidOperationException("YAPS/Atlas Socket not found");
            string template = Lf(File.ReadAllText(AssetDatabase.GetAssetPath(baseShader)));
            if (!template.Contains(BaselineInclude)) throw new InvalidOperationException("the template's include line moved");

            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__PortalDeformPerf");
            string inlined = string.Concat(Parts.Select(p => Part(spike, p)));
            var shaders = new List<(string name, Shader shader, int lookup)> { ("baseline", baseShader, -1) };
            foreach (int v in lookups)
            {
                string path = $"{Folder}/Lookup{v}.shader";
                string text = template.Replace("Shader \"Hidden/PortalDeformPerf\"", $"Shader \"Hidden/PortalDeformPerf/Lookup{v}\"")
                    .Replace(BaselineInclude, $"#define PORTAL_LOOKUP {v}\n{inlined}");
                File.WriteAllText(path, text);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var s = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (s == null || !s.isSupported || ShaderUtil.ShaderHasError(s))
                {
                    foreach (var m in s != null ? ShaderUtil.GetShaderMessages(s) : new ShaderMessage[0])
                        Debug.Log(Tag + $"  {m.severity} {m.message} line {m.line}");
                    throw new InvalidOperationException($"lookup {v} did not compile");
                }
                shaders.Add(($"lookup {v}", s, v));
            }

            var bake = Bake();
            var mats = shaders.Select(s => Plugged(new Material(s.shader), bake)).ToArray();
            var socket = new Material(writer);
            socket.SetFloat("_YAPS_Kind", 1);
            socket.SetFloat("_YAPS_SocketIndex", Number);
            var quads = LevelQuads();
            var atlas = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGBHalf);
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBHalf);
            var check = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGBFloat);
            var go = new GameObject("__PortalPerfCamera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.cullingMask = 0;
            var plugAt = Matrix4x4.TRS(Root, Quaternion.identity, Vector3.one);
            var sync = new Texture2D(1, 1, TextureFormat.RGBAHalf, false);
            Shader.SetGlobalVector("_YAPS_Atlas_TexelSize", new Vector4(1f / Size, 1f / Size, Size, Size));

            void Render(RenderTexture rt, CameraClearFlags clear, CommandBuffer cb)
            {
                cam.targetTexture = rt;
                cam.clearFlags = clear;
                cam.backgroundColor = new Color(0, 0, 0, 0);
                cam.AddCommandBuffer(CameraEvent.AfterEverything, cb);
                cam.Render();
                cam.RemoveCommandBuffer(CameraEvent.AfterEverything, cb);
            }

            // The production socket writer draws the hole; the spike's own
            // encoder draws the entry, at home B when A is taken.
            void Write(Content content, bool full, int shaderIndex)
            {
                var spikeMat = mats[shaderIndex == 0 ? 1 : shaderIndex];
                int lookup = shaders[shaderIndex == 0 ? 1 : shaderIndex].lookup;
                socket.SetFloat("_YAPS_Owner", content == Content.Keyless ? 0 : Owner);
                var cb = new CommandBuffer();
                cb.ClearRenderTarget(false, true, new Color(0, 0, 0, full ? 1f / 255f : 0f));
                if (full) cb.DrawProcedural(Matrix4x4.identity, spikeMat, 4, MeshTopology.Triangles, 6);
                var socketAt = Matrix4x4.TRS(Entrance, Quaternion.LookRotation(Vector3.back), Vector3.one);
                cb.DrawMesh(quads, socketAt, socket, 0, 0);
                cb.DrawMesh(quads, socketAt, socket, 0, 1);
                if (content == Content.Portal && shaderIndex > 0)
                {
                    spikeMat.SetFloat("_PerfHome", full && lookup != 1 ? 1 : 0);
                    cb.DrawProcedural(Matrix4x4.identity, spikeMat, 5, MeshTopology.Points, 4);
                }
                Render(atlas, CameraClearFlags.Nothing, cb);
                cb.Release();
                Shader.SetGlobalTexture("_YAPS_Atlas", atlas);
            }

            void Sync(RenderTexture rt)
            {
                // A readback waits for the GPU to finish everything before it.
                RenderTexture.active = rt;
                sync.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                RenderTexture.active = null;
            }

            // One render of each case in turn, best of eight after a warm one, so
            // clock drift lands on every case of a group alike. The atlas is
            // rewritten before each and waited on outside the clock.
            double[] Time(List<(int si, Content content, bool full)> group, int pass)
            {
                var cbs = group.Select(r =>
                {
                    var cb = new CommandBuffer();
                    for (int i = 0; i < Draws; i++) cb.DrawProcedural(plugAt, mats[r.si], pass, MeshTopology.Points, Vertices);
                    return cb;
                }).ToArray();
                var best = group.Select(_ => double.MaxValue).ToArray();
                for (int trial = 0; trial < 9; trial++)
                    for (int k = 0; k < group.Count; k++)
                    {
                        Write(group[k].content, group[k].full, group[k].si);
                        Sync(atlas);
                        cam.targetTexture = target;
                        cam.clearFlags = CameraClearFlags.SolidColor;
                        cam.AddCommandBuffer(CameraEvent.AfterEverything, cbs[k]);
                        var watch = Stopwatch.StartNew();
                        cam.Render();
                        Sync(target);
                        if (trial > 0) best[k] = Math.Min(best[k], watch.Elapsed.TotalMilliseconds);
                        cam.RemoveCommandBuffer(CameraEvent.AfterEverything, cbs[k]);
                    }
                foreach (var cb in cbs) cb.Release();
                return best.Select(ms => ms * 1e6 / ((double) Draws * Vertices) * Plug / 1000).ToArray();   // us a pass
            }

            bool Check(int si, Content content, bool full)
            {
                Write(content, full, si);
                var cb = new CommandBuffer();
                cb.DrawProcedural(plugAt, mats[si], 2, MeshTopology.Points, Plug);
                cb.DrawProcedural(plugAt, mats[si], 3, MeshTopology.Points, 3);
                Render(check, CameraClearFlags.SolidColor, cb);
                cb.Release();
                var read = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
                RenderTexture.active = check;
                read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                RenderTexture.active = null;
                var px = read.GetPixels();
                Object.DestroyImmediate(read);

                int vertices = 0, atExit = 0, atEntrance = 0;
                Color s0 = default, s1 = default, s2 = default;
                foreach (var c in px)
                {
                    if (c.a == 1)
                    {
                        vertices++;
                        var p = new Vector3(c.r, c.g, c.b);
                        if ((p - Exit).magnitude <= Length * 1.05f) atExit++;
                        else if ((p - Entrance).magnitude <= Length * 1.05f) atEntrance++;
                    }
                    else if (c.a >= 10 && c.a < 12) s0 = c;
                    else if (c.a == 20) s1 = c;
                    else if (c.a == 30) s2 = c;
                }
                bool spiked = si > 0, hitWanted = spiked && content == Content.Portal;
                float hit = s0.a - 10;
                bool ok = vertices == Plug && s0.r == 3 && s0.g == 1
                    && (!spiked || s0.b == (content == Content.Keyless ? 0 : 1))
                    && hit == (hitWanted ? 1 : 0)
                    && atExit == (hitWanted ? Plug : 0) && atEntrance == (hitWanted ? 0 : Plug);
                string exitNote = "";
                if (hitWanted)
                {
                    float dPos = (new Vector3(s1.r, s1.g, s1.b) - Exit).magnitude * 1000;
                    float dFwd = Vector3.Angle(new Vector3(s2.r, s2.g, s2.b), ExitFwd);
                    // S2's precision gate, here on a half target only.
                    ok &= dPos <= DirCell / 255 * 1000 + 0.1f && dFwd <= 0.5f;
                    exitNote = $", exit decoded {dPos:0.000} mm and {dFwd:0.000} deg off";
                }
                Debug.Log(Tag + $"{(ok ? "PASS" : "FAIL")} check {shaders[si].name}, {content}, {(full ? "full" : "empty")}: " +
                    $"tier {s0.r}, links {s0.g}, portal key {(spiked ? s0.b.ToString() : "n/a")}, exit found {hit}, " +
                    $"{vertices} vertices, {atExit} at the exit, {atEntrance} at the entrance{exitNote}");
                return ok;
            }

            try
            {
                Log($"GPU {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}; {Plug} vertices, L {Length}, " +
                    $"{repeats} repeats, lookups {string.Join(",", lookups)}");
                // A group shares one content; the baseline reads the owned hole
                // where the spike reads an entry, since it never looks at the margin.
                var groups = new List<List<(int si, Content content, bool full)>>();
                foreach (bool full in new[] { false, true })
                    foreach (Content c in Enum.GetValues(typeof(Content)))
                        groups.Add(Enumerable.Range(0, shaders.Count).Select(si => (si, c, full)).ToList());
                var runs = groups.SelectMany(g => g).ToList();
                bool checks = true;
                foreach (var r in runs) checks &= Check(r.si, r.content, r.full);
                if (!checks) { Log("FAIL a case did not take the path it names; nothing timed"); return false; }

                var times = runs.ToDictionary(r => r, r => new List<double>());
                var floors = new List<double>();
                for (int rep = 0; rep < repeats; rep++)
                {
                    double floor = Time(new List<(int, Content, bool)> { (0, Content.Keyless, false) }, 1)[0];
                    floors.Add(floor);
                    foreach (var g in groups)
                    {
                        var t = Time(g, 0);
                        for (int k = 0; k < g.Count; k++) times[g[k]].Add(t[k] - floor);
                    }
                    Log($"repeat {rep + 1} of {repeats} done, floor {floor:0.00} us");
                }
                Log($"floor median {Median(floors):0.00} us, spread {Spread(floors):0.00}");
                foreach (var r in runs)
                    Log($"time {shaders[r.si].name}, {r.content}, {(r.full ? "full" : "empty")}: median {Median(times[r]):0.00} us, " +
                        $"spread {Spread(times[r]):0.00} ({string.Join(" ", times[r].Select(t => t.ToString("0.00")))})");

                // The gate on medians as set; beside it the same difference taken
                // repeat by repeat, which drift between repeats cannot reach.
                string Paired(List<double> spike, List<double> base0)
                {
                    var d = spike.Zip(base0, (x, y) => x - y).ToList();
                    return $"paired {Median(d):+0.00;-0.00} us, spread {Spread(d):0.00}";
                }
                bool all = true;
                for (int si = 1; si < shaders.Count; si++)
                    foreach (bool full in new[] { false, true })
                    {
                        string fill = full ? "full" : "empty";
                        var a0 = times[(0, Content.Keyless, full)];
                        var a1 = times[(0, Content.Miss, full)];
                        var a2 = times[(0, Content.Portal, full)];
                        var sb = times[(si, Content.Keyless, full)];
                        var sm = times[(si, Content.Miss, full)];
                        var sc = times[(si, Content.Portal, full)];
                        double noise0 = Math.Max(GateNoise, Spread(a0)), noise1 = Math.Max(GateNoise, Spread(a1));
                        double b = Median(sb) - Median(a0), miss = Median(sm) - Median(a1), c = Median(sc) - Median(a2);
                        bool passB = b <= noise0, passC = c <= GatePortal;
                        all &= passB && passC;
                        Log($"{(passB ? "PASS" : "FAIL")} {shaders[si].name} {fill} no portal: {b:+0.00;-0.00} us over the baseline, " +
                            $"bound {noise0:0.00} (baseline spread {Spread(a0):0.00}, spike spread {Spread(sb):0.00}); {Paired(sb, a0)}");
                        Log($"{(passC ? "PASS" : "FAIL")} {shaders[si].name} {fill} portal: {c:+0.00;-0.00} us over the baseline, " +
                            $"bound {GatePortal}; {Paired(sc, a2)}");
                        Log($"{(miss <= noise1 ? "within" : "OVER")} {shaders[si].name} {fill} owned hole, no entry: {miss:+0.00;-0.00} us, " +
                            $"noise bound {noise1:0.00} (not gated); {Paired(sm, a1)}");
                    }
                return all;
            }
            finally
            {
                cam.targetTexture = null;
                target.Release();
                atlas.Release();
                check.Release();
                Object.DestroyImmediate(go);
            }
        }

        // A tapered rod along +Z, 64 round, every vertex past the hole and active.
        static Texture2D Bake()
        {
            const int width = 8192;
            int floats = 1 + Plug * 10, height = (floats + width - 1) / width;
            var bytes = new byte[width * height * 4];
            int at = 0;
            void Put(float f) { Buffer.BlockCopy(BitConverter.GetBytes(f), 0, bytes, at * 4, 4); at++; }
            Put(0);
            for (int i = 0; i < Plug; i++)
            {
                float t = (i / 64) / 100f, angle = (i % 64) * Mathf.PI * 2 / 64;
                float z = Mathf.Lerp(0.06f, 1, t) * Length, r = 0.08f * Length * (1 - 0.5f * t);
                float cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
                Put(r * cs); Put(r * sn); Put(z);
                Put(cs); Put(sn); Put(0);
                Put(-sn); Put(cs); Put(0);
                Put(1);
            }
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0 };
            tex.SetPixelData(bytes, 0);
            tex.Apply(false, false);
            return tex;
        }

        // The patcher's defaults, the plug's own frame, the atlas on.
        static Material Plugged(Material m, Texture2D bake)
        {
            m.SetTexture("_YAPS_Bake", bake);
            m.SetVector("_YAPS_Bake_TexelSize", new Vector4(1f / bake.width, 1f / bake.height, bake.width, bake.height));
            var f = new Dictionary<string, float>
            {
                ["_YAPS_VertexCount"] = Plug, ["_YAPS_Enabled"] = 1, ["_YAPS_Length"] = Length, ["_YAPS_Overrun"] = 1,
                ["_YAPS_BakeScale"] = 1, ["_YAPS_BakeGirth"] = 1, ["_YAPS_FrameFromVertex"] = 0, ["_YAPS_SelfTag"] = -1,
                ["_YAPS_UseAtlas"] = 1, ["_YAPS_TaperStart"] = 0.1f, ["_YAPS_TaperEnd"] = 0.3f, ["_YAPS_IdleLength"] = 1,
                ["_YAPS_IdleWidth"] = 1, ["_YAPS_SqueezeDistance"] = 0.15f, ["_YAPS_BulgeDistance"] = 0.2f,
                ["_YAPS_PumpSpeed"] = 6, ["_YAPS_PumpWidth"] = 1, ["_YAPS_WriggleSpeed"] = 2, ["_YAPS_BezierSmoothness"] = 1,
                ["_YAPS_PortalSpan"] = Span, ["_PerfOwner"] = Owner, ["_PerfNumber"] = Number,
            };
            foreach (var kv in f) m.SetFloat(kv.Key, kv.Value);
            m.SetVector("_PerfExitAt", Exit);
            m.SetVector("_PerfExitFwd", ExitFwd);
            return m;
        }

        // YapsAtlas.LevelQuads, in memory: eight quads, corners in UV0, positions zero.
        static Mesh LevelQuads()
        {
            var corners = new List<Vector3>();
            var tris = new List<int>();
            for (int q = 0; q < 8; q++)
            {
                int b = corners.Count;
                corners.Add(new Vector3(-0.5f, -0.5f, q));
                corners.Add(new Vector3(0.5f, -0.5f, q));
                corners.Add(new Vector3(-0.5f, 0.5f, q));
                corners.Add(new Vector3(0.5f, 0.5f, q));
                tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
            }
            var mesh = new Mesh();
            mesh.SetVertices(corners.Select(_ => Vector3.zero).ToList());
            mesh.SetUVs(0, corners);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000);
            return mesh;
        }

        static double Median(List<double> xs)
        {
            var s = xs.OrderBy(x => x).ToList();
            return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
        }

        static double Spread(List<double> xs) => xs.Max() - xs.Min();

        // As YapsShaderPatcher inlines it: the include lines go, the rest stays.
        static string Part(string dir, string name) =>
            Regex.Replace(Lf(File.ReadAllText(Path.Combine(dir, name + ".cginc"))), "#include\\s+\"yaps_\\w+\\.cginc\"\\s*", "");

        static string Lf(string s) => s.Replace("\r\n", "\n");

        static void Log(string s) => Debug.Log(Tag + s);

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
