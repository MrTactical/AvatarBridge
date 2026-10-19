#if CVR_CCK_EXISTS && UNITY_EDITOR && AVATARBRIDGE_YAPS
// Does anything draw between a portal's entrance and its exit? (S3)
//
//   -executeMethod AvatarBridge.Regression.PortalBandProbe.Run
//       -portalIncludes <Dev/Probes/Portal>   the spike copies, which must BE the
//                                             project's YAPS includes for the run
//       [-portalHosts Poiyomi]                only hosts whose name contains it
//       [-portalPoses 50]
//       [-portalDump <dir>]                   each host's first corridor frame as a PNG
//
// The project's shipped props, atlas, resolve and deform are swapped for the
// spike copies before Unity starts, so every host is patched by the production
// patcher as 5.0 would patch it. The probe refuses to run when they are not
// the copies it was pointed at.
//
// A synthetic plug, 97 rings of 25 vertices (the 25th a UV seam), its portal
// span set by the baker's rule, goes into a hole and out of an exit 2 m away,
// from 95% of its length short of the hole to 1%, each pose at _YAPS_Enabled
// 0, 0.25, 0.5, 0.999 and 1, on an HDR camera reading a half atlas and an
// 8-bit camera reading an 8-bit one. Hosts: Standard (refused, then on Simple
// Lit as the converter does), YAPS Simple Lit, locked Poiyomi Toon 9.3 with
// outlines four ways, and lilToon 1.7.3's opaque pass shader (its outline
// passes) with outlines four ways and its own input switches on.
//
// Per render, a pixel the plug drew is a corridor pixel when its ray passes
// near neither mouth. Per pose, the deform's own output through a capture
// shader: the band's two ends bit for bit, every vertex out of the exit
// against where the decoded exit puts it, and the seam's two copies of each
// ring bit for bit.
//
// Gates: 0 corridor pixels on every host; exit vertices within 0.5 mm; seams
// 0.00 mm. Sanity, so a host that drew nothing cannot pass: pixels near the
// entrance whenever any vertex was walked in, near the exit exactly when
// vertices were portalled, and every outline host covering more than Simple Lit.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    public static class PortalBandProbe
    {
        const string Tag = "[PortalBand] ";
        const string Folder = "Assets/__PortalBand";
        const string PoiyomiPath = "Assets/_PoiyomiShaders/Shaders/9.3/Toon/Poiyomi Toon.shader";
        static readonly string[] Parts = { "yaps_props", "yaps_atlas", "yaps_resolve", "yaps_deform" };

        const int Rings = 96, Sides = 24, Columns = Sides + 1, Plug = (Rings + 1) * Columns;
        const float Length = 0.3f, Radius = 0.03f, Start = 0.02f;
        // The baker's rule: the largest z-span of any triangle over the length, times 1.25.
        const float Span = 1.25f * (1 - Start) / Rings;
        const int Owner = 0x5A3C17, Number = 3, W = 1280, H = 800, Layer = 31;
        const double ExitBound = 0.5;
        // Past 41 m from the origin, so a cell index decoded as absolute shows.
        static readonly Vector3 Entrance = new Vector3(60.3f, 1.0f, 20.2f);
        static readonly Vector3 Exit = Entrance + new Vector3(2, 0, 0);
        static readonly Vector3 ExitFwd = new Vector3(0.4f, 0.3f, 1).normalized;
        // All the plug draws at a mouth is inside this, outlines included.
        const float Reach = 1.1f * Length + 0.05f;
        static readonly float[] Enables = { 0, 0.25f, 0.5f, 0.999f, 1 };

        class Host
        {
            public string name;
            public Material mat;
            public bool outline;
            public readonly long[] drawn = new long[2], corridor = new long[2];
            public int failures;
            public readonly bool[] dumped = new bool[2];
        }

        [MenuItem("Tools/AvatarBridge Dev/Probe: Portal band")]
        public static void Run()
        {
            int code = 1;
            bool async = ShaderUtil.allowAsyncCompilation;
            var before = Entries("Assets");
            try
            {
                // A host still compiling draws Unity's placeholder, unbent.
                ShaderUtil.allowAsyncCompilation = false;
                code = Measure() ? 0 : 1;
            }
            catch (Exception e) { Debug.LogError(Tag + "FAIL " + e); }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
                // Everything the run made: the folder, the patches, the locked stand-ins.
                var made = Entries("Assets").Except(before).OrderBy(p => p.Length).ToList();
                var gone = new List<string>();
                foreach (string p in made)
                    if (!gone.Any(g => p.StartsWith(g + "/")) && AssetDatabase.DeleteAsset(p)) gone.Add(p);
                Log($"cleaned up {gone.Count} new assets: {string.Join(", ", gone.Take(12))}{(gone.Count > 12 ? ", ..." : "")}");
            }
            Debug.Log(Tag + (code == 0 ? "RESULT PASS" : "RESULT FAIL"));
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static bool Measure()
        {
            string spike = Arg("-portalIncludes") ?? throw new ArgumentException("-portalIncludes is required");
            string only = Arg("-portalHosts");
            int poses = int.Parse(Arg("-portalPoses") ?? "50");
            string dump = Arg("-portalDump");
            bool Wanted(string name) => only == null || name.Contains(only);

            var socketShader = Compiled("YAPS/Atlas Socket");
            var clearShader = Compiled("YAPS/Atlas Clear");
            string shipped = Path.GetDirectoryName(AssetDatabase.GetAssetPath(socketShader)).Replace('\\', '/');
            foreach (string p in Parts)
                if (Lf(File.ReadAllText($"{shipped}/{p}.cginc")) != Lf(File.ReadAllText($"{spike}/{p}.cginc")))
                    throw new InvalidOperationException($"{shipped}/{p}.cginc is not the copy in {spike}; swap it in before Unity starts");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__PortalBand");
            var capture = Plugged(new Material(Template(spike, "PortalBandCapture", shipped)));
            var exitMat = new Material(Template(spike, "YapsAtlasExitSpike", shipped));
            Log($"GPU {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}; includes {shipped} = {spike}; " +
                $"{Plug} vertices, L {Length} m, span {Span:0.00000} (2w {2 * Span * Length * 1000:0.000} mm, ring step " +
                $"{(1 - Start) * Length / Rings * 1000:0.000} mm), {poses} poses, enabled {string.Join(" ", Enables)}");

            // The hosts, each patched by the shipped patcher.
            bool all = true;
            var report = new BridgeReport();
            var hosts = new List<Host>();
            var mask = Asset(Texture((x, y) => { float g = 0.2f + 0.8f * ((x * 7 + y * 13) % 64) / 63f; return new Color(g, g, g, 1); }), "Mask");
            Shader Patched(Material m, string what)
            {
                var s = YapsShaderPatcher.Patch(m, report, out string refusal, out int shadows);
                Log($"{what}: {(s != null ? $"patched to \"{s.name}\"" : "refused: " + refusal)}" +
                    (shadows > 0 ? $", {shadows} shadow passes left straight" : ""));
                return s;
            }
            void Add(string name, Material source, Shader patched, bool outline)
            {
                if (patched == null) { Log($"FAIL {name}: no patched shader to draw"); all = false; return; }
                var m = new Material(YapsLock.Basis(source)) { name = name };
                m.shader = patched;
                hosts.Add(new Host { name = name, mat = Plugged(m), outline = outline });
            }

            if (Wanted("Standard"))
            {
                var standard = new Material(Shader.Find("Standard")) { name = "Standard" };
                bool refused = Patched(standard, "Standard, as the converter asks first") == null;
                var lit = YapsNativeBuilder.OnSimpleLit(standard, out string why);
                if (!refused || lit == null) { Log($"FAIL Standard: refused {refused}, on Simple Lit {(lit != null ? "yes" : why)}"); all = false; }
                else Add("Standard", lit, Patched(lit, "Standard on Simple Lit"), false);
            }
            if (Wanted("Simple Lit"))
            {
                var lit = new Material(Shader.Find("YAPS/Simple Lit")) { name = "Simple Lit" };
                Add("Simple Lit", lit, Patched(lit, "YAPS Simple Lit"), false);
            }
            if (Wanted("Poiyomi"))
            {
                var poi = AssetDatabase.LoadAssetAtPath<Shader>(PoiyomiPath);
                if (poi == null || YapsLock.ThryLocked(poi) != false) throw new InvalidOperationException("no unlocked Poiyomi Toon at " + PoiyomiPath);
                // Width from a mask and from vertex red, so it differs vertex by vertex.
                var sets = new (string name, Action<Material> set)[]
                {
                    ("Poiyomi outline, mask and vertex red", m => { }),
                    ("Poiyomi vertex colour normals, local", m => m.SetFloat("_OutlineUseVertexColorNormals", 1)),
                    ("Poiyomi vertex colour normals, world", m => { m.SetFloat("_OutlineUseVertexColorNormals", 1); m.SetFloat("_OutlineSpace", 1); }),
                    ("Poiyomi drop shadow", m => { m.SetFloat("_OutlineExpansionMode", 4); m.SetVector("_OutlineDropShadowOffset", new Vector4(0.01f, -0.01f, 0, 0)); }),
                };
                var mats = new List<(string name, Material m)>();
                for (int i = 0; i < sets.Length; i++)
                {
                    if (!Wanted(sets[i].name)) continue;
                    var m = new Material(poi);
                    m.SetFloat("_EnableOutlines", 1);
                    m.SetFloat("_LineWidth", 1);
                    m.SetTexture("_OutlineMask", mask);
                    m.SetFloat("_OutlineVertexColorMask", 1);
                    sets[i].set(m);
                    AssetDatabase.CreateAsset(m, $"{Folder}/Poi{i}.mat");
                    mats.Add((sets[i].name, m));
                }
                // One lock for all of them, as the converter's batch does.
                YapsLock.Prepare(mats.Select(x => x.m));
                foreach (var (name, m) in mats)
                {
                    var basis = YapsLock.Basis(m);
                    Log($"{name}: locked {YapsLock.ThryLocked(basis.shader)} as \"{basis.shader.name}\"");
                    if (YapsLock.ThryLocked(basis.shader) != true) { Log($"FAIL {name}: Poiyomi's lock did not run"); all = false; continue; }
                    Add(name, m, Patched(m, name), true);
                }
            }
            if (Wanted("lilToon"))
            {
                // What a user's lilToon material is on, as the shipped patcher answers it.
                // Its drawing passes are UsePass into Hidden/ltspass_opaque, which the
                // patch leaves pointing at the original, so it is logged, not drawn.
                foreach (string n in new[] { "lilToon", "Hidden/lilToonOutline" })
                {
                    var s = Shader.Find(n);
                    if (s == null) { Log($"FAIL no \"{n}\" in the project"); all = false; continue; }
                    var ps = Patched(new Material(s), $"lilToon as shipped, \"{n}\"");
                    string text = ps != null ? File.ReadAllText(AssetDatabase.GetAssetPath(ps)) : "";
                    Log($"  its patch: {Regex.Matches(text, @"YapsDeform\(yapsPosition").Count} deformed vertex stage, " +
                        $"{Regex.Matches(text, @"^\s*UsePass\s+""Hidden/ltspass_opaque/", RegexOptions.Multiline).Count} passes drawn through UsePass into the unpatched original");
                }
                var opaque = Shader.Find("Hidden/ltspass_opaque");
                if (opaque == null) { Log("FAIL no \"Hidden/ltspass_opaque\" in the project"); all = false; return false; }
                Patched(new Material(opaque), "lilToon's opaque pass shader");
                // The same passes with lilToon's own switches for the inputs the deform
                // reads, so the patch compiles. lilToon's outline maths is untouched.
                string opaquePath = AssetDatabase.GetAssetPath(opaque);
                string forced = Regex.Replace(Lf(File.ReadAllText(opaquePath)), @"^(\s*)HLSLPROGRAM\s*$",
                    m => m.Value + "\n#define LIL_REQUIRE_APP_NORMAL\n#define LIL_REQUIRE_APP_TANGENT\n#define LIL_REQUIRE_APP_VERTEXID",
                    RegexOptions.Multiline);
                forced = new Regex("Shader\\s+\"[^\"]*\"").Replace(forced, "Shader \"Hidden/PortalBand/ltspass_opaque\"", 1);
                string forcedPath = Path.GetDirectoryName(opaquePath).Replace('\\', '/') + "/PortalBand_ltspass_opaque.shader";
                File.WriteAllText(forcedPath, forced);
                AssetDatabase.ImportAsset(forcedPath, ImportAssetOptions.ForceSynchronousImport);
                var pass = Compiled(AssetDatabase.LoadAssetAtPath<Shader>(forcedPath), forcedPath);
                var patched = Patched(new Material(pass), "lilToon's opaque pass shader, inputs on");
                // Unpacked as DXT5nm: x from alpha times red, y from green.
                var tilt = Texture((x, y) => new Color(1, 0.5f, 0.5f, 0.8f));
                var sets = new (string name, Action<Material> set)[]
                {
                    ("lilToon outline, mask and vertex red", m => { }),
                    ("lilToon outline, z bias", m => m.SetFloat("_OutlineZBias", 0.01f)),
                    ("lilToon outline, vector from vertex colour", m => m.SetFloat("_OutlineVertexR2Width", 2)),
                    ("lilToon outline, tilted vector texture", m => m.SetTexture("_OutlineVectorTex", tilt)),
                };
                foreach (var set in sets.Where(s => Wanted(s.name)))
                {
                    var m = new Material(pass) { name = set.name };
                    m.SetFloat("_UseOutline", 1);
                    m.SetFloat("_OutlineWidth", 0.5f);
                    m.SetTexture("_OutlineWidthMask", mask);
                    m.SetFloat("_OutlineVertexR2Width", 1);
                    set.set(m);
                    Add(set.name, m, patched, true);
                }
            }
            // The shader sweep's other families. One the patcher refuses draws on Simple Lit,
            // already a host above.
            if (Environment.GetCommandLineArgs().Contains("-portalSweep"))
            {
                var covered = new[] { "YAPS", "Unity", "Poiyomi", "lilToon" };
                var sweep = ShaderSweepProbe.Hosts(Folder, n => Wanted(n), out bool setUp).Where(h => !covered.Contains(h.family)).ToList();
                if (!setUp) all = false;
                foreach (var h in sweep)
                {
                    var s = Patched(h.source, h.name);
                    if (s != null) Add(h.name, h.source, s, h.outline);
                }
            }
            foreach (var e in report.Entries.Where(e => e.Status == ReportStatus.Warning))
                Log($"  report warning: {e.Subject}: {e.Detail}");
            if (hosts.Count == 0) throw new InvalidOperationException("no host to draw");

            // The scene: the plug on its own layer, one light, two cameras.
            var made = new List<Object>();
            T Made<T>(T o) where T : Object { made.Add(o); return o; }
            BuildPlug(out var mesh, out var points, out var bake);
            Made(mesh); Made(points); Made(bake);
            capture.SetTexture("_YAPS_Bake", bake);
            capture.SetVector("_YAPS_Bake_TexelSize", TexelSize(bake));
            foreach (var h in hosts)
            {
                h.mat.SetTexture("_YAPS_Bake", bake);
                h.mat.SetVector("_YAPS_Bake_TexelSize", TexelSize(bake));
            }
            var plugGo = Made(new GameObject("__PortalBandPlug") { hideFlags = HideFlags.DontSave, layer = Layer });
            plugGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            var plug = plugGo.AddComponent<MeshRenderer>();
            plug.shadowCastingMode = ShadowCastingMode.Off;
            plug.receiveShadows = false;
            plug.lightProbeUsage = LightProbeUsage.Off;
            plug.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var lightGo = Made(new GameObject("__PortalBandLight") { hideFlags = HideFlags.DontSave });
            lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << Layer;
            var camGo = Made(new GameObject("__PortalBandCamera") { hideFlags = HideFlags.DontSave });
            var mid = (Entrance + Exit) / 2;
            camGo.transform.position = mid + new Vector3(0, 1.2f, -2.6f);
            // Looking above the mouths, so a readback upside down puts the plug outside them.
            camGo.transform.LookAt(mid + new Vector3(0, 0.6f, 0));
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 50;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 50;
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.07f, 0.09f, 1);
            cam.renderingPath = RenderingPath.Forward;
            cam.allowMSAA = false;
            var atlasGo = Made(new GameObject("__PortalBandAtlasCamera") { hideFlags = HideFlags.DontSave });
            var atlasCam = atlasGo.AddComponent<Camera>();
            atlasCam.enabled = false;
            atlasCam.cullingMask = 0;
            atlasCam.clearFlags = CameraClearFlags.SolidColor;

            var levelQuads = Made(Quads(8));
            var exitQuads = Made(Quads(2));
            var socketMat = Made(new Material(socketShader));
            socketMat.SetFloat("_YAPS_Kind", 1);
            socketMat.SetFloat("_YAPS_Owner", Owner);
            socketMat.SetFloat("_YAPS_SocketIndex", Number);
            var clearMat = Made(new Material(clearShader));
            exitMat.SetFloat("_YAPS_Owner", Owner);
            exitMat.SetFloat("_YAPS_EntranceIndex", Number);
            // The capture's shader declares no properties, and its material's
            // floats read 0 when it drew (measured: no atlas, no length), so
            // the block carries them to every draw.
            var block = new MaterialPropertyBlock();
            foreach (var kv in Floats) block.SetFloat(kv.Key, kv.Value);
            block.SetTexture("_YAPS_Bake", bake);
            block.SetVector("_YAPS_Bake_TexelSize", TexelSize(bake));
            float w = Span * Length;

            void Draw(Camera c, RenderTexture rt, Color background, CommandBuffer cb)
            {
                c.targetTexture = rt;
                c.backgroundColor = background;
                c.AddCommandBuffer(CameraEvent.AfterEverything, cb);
                c.Render();
                c.RemoveCommandBuffer(CameraEvent.AfterEverything, cb);
                cb.Release();
            }

            var worstExit = new double[2];
            var worstPlain = new double[2];
            var worstRoll = new double[2];
            var worstAxial = new double[2];
            var worstSeam = new double[2];
            var worstDecoded = new (double mm, double deg)[2];
            int captureFails = 0, bandRings = 0;
            try
            {
                for (int f = 0; f < 2; f++)
                {
                    bool hdr = f == 0;
                    string fname = hdr ? "HDR" : "8-bit";
                    var format = hdr ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
                    var atlas = Made(new RenderTexture(W, H, 0, format, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point });
                    var target = Made(new RenderTexture(W, H, 24, format, RenderTextureReadWrite.Default));
                    var grab = Made(new RenderTexture(W, H, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point });
                    var read = Made(new Texture2D(W, H, hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, true));
                    var floats = Made(new Texture2D(W, H, TextureFormat.RGBAFloat, false, true));

                    // The atlas as a frame leaves it: the scene, the clear, the hole, the exit.
                    Shader.SetGlobalVector("_YAPS_Atlas_TexelSize", new Vector4(1f / W, 1f / H, W, H));
                    var cb = new CommandBuffer();
                    cb.DrawMesh(levelQuads, Matrix4x4.identity, clearMat, 0, 0);
                    var holeAt = Matrix4x4.TRS(Entrance, Quaternion.LookRotation(Vector3.back), Vector3.one);
                    cb.DrawMesh(levelQuads, holeAt, socketMat, 0, 0);
                    cb.DrawMesh(levelQuads, holeAt, socketMat, 0, 1);
                    cb.DrawMesh(exitQuads, Matrix4x4.TRS(Exit, Quaternion.LookRotation(ExitFwd), Vector3.one), exitMat, 0, 0);
                    Draw(atlasCam, atlas, new Color(0.3f, 0.6f, 0.9f, 1), cb);
                    Shader.SetGlobalTexture("_YAPS_Atlas", atlas);
                    {
                        RenderTexture.active = atlas;
                        floats.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                        RenderTexture.active = null;
                        var raw = floats.GetRawTextureData<float>();
                        var alphas = new Dictionary<float, int>();
                        for (int i = 0; i < W * H; i++) { float a = raw[i * 4 + 3]; alphas[a] = alphas.TryGetValue(a, out int n) ? n + 1 : 1; }
                        var ones = Enumerable.Range(0, W * H).Where(i => Mathf.Abs(raw[i * 4 + 3] * 255 - 1) < 0.01f).Take(8).Select(i => $"{i % W},{i / W}");
                        Log($"{fname} atlas 1/255 pixels at x,y from the bottom: {string.Join(" ", ones)}");
                        Log($"{fname} atlas alphas: " + string.Join(", ", alphas.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Key * 255:0.###}/255 x{kv.Value}")));
                    }

                    cam.allowHDR = hdr;
                    cam.targetTexture = target;
                    var zone = Zones(cam);
                    Log($"{fname}: {zone.Count(z => (z & 1) != 0)} pixels near the entrance, {zone.Count(z => (z & 2) != 0)} near the exit, " +
                        $"{zone.Count(z => z == 0)} neither, of {W * H}");
                    var px = new ulong[W * H];
                    void Frame()
                    {
                        cam.Render();
                        RenderTexture.active = target;
                        read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                        RenderTexture.active = null;
                        if (hdr) read.GetRawTextureData<ulong>().CopyTo(px);
                        else { var raw = read.GetRawTextureData<uint>(); for (int i = 0; i < px.Length; i++) px[i] = raw[i]; }
                    }
                    plug.enabled = false;
                    Frame();
                    var empty = (ulong[]) px.Clone();
                    int uneven = empty.Count(p => p != empty[0]);
                    if (uneven != 0) { Log($"FAIL {fname}: {uneven} pixels of the empty frame differ from the clear"); all = false; }
                    plug.enabled = true;

                    for (int pose = 0; pose < poses; pose++)
                    {
                        float d = Length * Mathf.Lerp(0.95f, 0.01f, poses > 1 ? pose / (poses - 1f) : 0);
                        var root = Entrance - Vector3.forward * d;
                        plugGo.transform.position = root;
                        var plugAt = Matrix4x4.TRS(root, Quaternion.identity, Vector3.one);
                        var line = new List<string>();
                        foreach (float e in Enables)
                        {
                            block.SetFloat("_YAPS_Enabled", e);
                            block.SetFloat("_YAPS_PortalSpan", Span);

                            // The deform's own answer, vertex by vertex.
                            var ccb = new CommandBuffer();
                            ccb.DrawMesh(points, plugAt, capture, 0, 0, block);
                            ccb.DrawProcedural(plugAt, capture, 1, MeshTopology.Points, 5, 1, block);
                            Draw(atlasCam, grab, new Color(0, 0, 0, 0), ccb);
                            RenderTexture.active = grab;
                            floats.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                            RenderTexture.active = null;
                            var data = floats.GetRawTextureData<float>();
                            var got = new Vector3[Plug];
                            int vertices = 0;
                            Vector4 s0 = default, s1 = default, s2 = default, s3 = default, s4 = default;
                            for (int i = 0; i < W * H; i++)
                            {
                                float a = data[i * 4 + 3];
                                var c = new Vector4(data[i * 4], data[i * 4 + 1], data[i * 4 + 2], a);
                                if (a >= 100 && a < 100 + Plug) { got[(int) a - 100] = c; vertices++; }
                                else if (a == 10 || a == 11) s0 = c;
                                else if (a == 20) s1 = c;
                                else if (a == 30) s2 = c;
                                else if (a == 40) s3 = c;
                                else if (a == 50) s4 = c;
                            }
                            var exitAt = (Vector3) s1;
                            var fwd = ((Vector3) s2).normalized;
                            var hole = (Vector3) s3;
                            bool resolved = s0.x == 3 && s0.y == 1 && s0.z == 1 && s0.w == 11;
                            double decodedMm = (exitAt - Exit).magnitude * 1000.0, decodedDeg = Vector3.Angle(fwd, ExitFwd);
                            worstDecoded[f] = (Math.Max(worstDecoded[f].mm, decodedMm), Math.Max(worstDecoded[f].deg, decodedDeg));

                            // 0 walked, 1 band end A, 2 band end B, 3 out of the exit, 4 none
                            var pa = hole + Vector3.forward * w;
                            var pb = exitAt - fwd * w;
                            var kind = new int[Plug];
                            for (int v = 0; v < Plug; v++)
                            {
                                var p = got[v];
                                kind[v] = (p - pa).magnitude < 1e-4f ? 1 : (p - pb).magnitude < 1e-4f ? 2
                                        : (p - exitAt).magnitude <= Length * 1.05f ? 3
                                        : (p - Entrance).magnitude <= Length * 1.05f ? 0 : 4;
                            }
                            int portalled = kind.Count(k => k == 3);
                            int walked = e >= 1 ? kind.Count(k => k == 0) : Plug;
                            int stray = kind.Count(k => k == 4);

                            // The seam: two copies of each ring's first vertex, which nothing tells apart but the UV.
                            int seamSplit = 0;
                            double seam = 0;
                            for (int k = 0; k <= Rings; k++)
                            {
                                var x = got[k * Columns];
                                var y = got[k * Columns + Sides];
                                if (!Same(x, y)) seamSplit++;
                                seam = Math.Max(seam, (x - y).magnitude * 1000.0);
                            }
                            worstSeam[f] = Math.Max(worstSeam[f], seam);

                            bool ok = vertices == Plug && resolved && stray == 0 && seamSplit == 0;
                            string note = "";
                            if (e >= 1)
                            {
                                // Whole rings, in order along the shaft, each band end one point bit for bit.
                                var ringKind = Enumerable.Range(0, Rings + 1).Select(k =>
                                {
                                    var ks = Enumerable.Range(0, Columns).Select(j => kind[k * Columns + j]).Distinct().ToList();
                                    return ks.Count == 1 ? ks[0] : 5;
                                }).ToList();
                                bool order = !ringKind.Contains(5) && ringKind.Zip(ringKind.Skip(1), (a, b) => a <= b).All(x => x);
                                int splitA = Enumerable.Range(0, Plug).Count(v => kind[v] == 1 && !Same(got[v], got[Array.IndexOf(kind, 1)]));
                                int splitB = Enumerable.Range(0, Plug).Count(v => kind[v] == 2 && !Same(got[v], got[Array.IndexOf(kind, 2)]));
                                int band = ringKind.Count(k => k == 1 || k == 2);
                                bandRings += band;

                                // Out of the exit as the decoded exit puts it: along its facing by
                                // what was left past the hole less the band, hung off the arrival's up.
                                double exitErr = 0, axialErr = 0, plainErr = 0;
                                (float ax, float rad, float tan, float z) worstSplit = default;
                                float depth = (hole - root).magnitude;
                                var up = (Vector3.up - fwd * Vector3.Dot(Vector3.up, fwd)).normalized;
                                var right = Vector3.Cross(up, fwd);
                                // The exit's roll comes from the arrival, approximate by design, and
                                // the arrival bends toward the hole as decoded. One roll per pose,
                                // fitted, so the bound is on the placement; the roll is reported.
                                double sn = 0, cs = 0;
                                for (int v = 0; v < Plug; v++)
                                {
                                    if (kind[v] != 3) continue;
                                    var r = Rest(v, out _, out _);
                                    var g = got[v] - (exitAt + fwd * (r.z - depth - 2 * w));
                                    float gx = Vector3.Dot(g, right), gy = Vector3.Dot(g, up);
                                    sn += r.x * gy - r.y * gx;
                                    cs += r.x * gx + r.y * gy;
                                }
                                double roll = Math.Atan2(sn, cs);
                                float rc = (float) Math.Cos(roll), rs = (float) Math.Sin(roll);
                                worstRoll[f] = Math.Max(worstRoll[f], Math.Abs(roll * 180 / Math.PI));
                                for (int v = 0; v < Plug; v++)
                                {
                                    if (kind[v] != 3) continue;
                                    var r = Rest(v, out _, out _);
                                    var unrolled = exitAt + fwd * (r.z - depth - 2 * w) + right * r.x + up * r.y;
                                    plainErr = Math.Max(plainErr, (got[v] - unrolled).magnitude * 1000.0);
                                    var want = exitAt + fwd * (r.z - depth - 2 * w) + right * (r.x * rc - r.y * rs) + up * (r.x * rs + r.y * rc);
                                    var off = got[v] - want;
                                    if (off.magnitude * 1000.0 > exitErr)
                                    {
                                        var radial = (right * r.x + up * r.y).normalized;
                                        worstSplit = (Vector3.Dot(off, fwd) * 1000, Vector3.Dot(off, radial) * 1000,
                                            Vector3.Dot(off, Vector3.Cross(fwd, radial)) * 1000, r.z);
                                    }
                                    exitErr = Math.Max(exitErr, off.magnitude * 1000.0);
                                    axialErr = Math.Max(axialErr, Math.Abs(Vector3.Dot(off, fwd)) * 1000.0);
                                }
                                worstExit[f] = Math.Max(worstExit[f], exitErr);
                                worstPlain[f] = Math.Max(worstPlain[f], plainErr);
                                worstAxial[f] = Math.Max(worstAxial[f], axialErr);
                                bool tipOut = Length - d > 2 * w + (1 - Start) * Length / Rings;
                                ok &= order && splitA == 0 && splitB == 0 && exitErr <= ExitBound
                                      && (!tipOut || (portalled > 0 && band > 0));
                                note = $"rings {ringKind.Count(k => k == 0)}/{ringKind.Count(k => k == 1)}/{ringKind.Count(k => k == 2)}/" +
                                       $"{ringKind.Count(k => k == 3)} walked/A/B/out, in order {order}, {splitA + splitB} off their end, " +
                                       $"exit {exitErr:0.000} mm, roll {roll * 180 / Math.PI:0.000} deg fitted ({plainErr:0.000} mm unfitted; {axialErr:0.000} axial; worst {worstSplit.ax:0.000} axial {worstSplit.rad:0.000} radial " +
                                       $"{worstSplit.tan:0.000} around at z {worstSplit.z:0.000}; hole decoded {(hole - Entrance).z * 1000:0.000} mm along, " +
                                       $"{new Vector2((hole - Entrance).x, (hole - Entrance).y).magnitude * 1000:0.000} across)";
                            }
                            else
                            {
                                // Below full the portal is shut and the plug an ordinary hole's.
                                ok &= portalled == 0 && kind.Count(k => k == 1 || k == 2) == 0;
                                note = $"{portalled} out of the exit";
                            }
                            if (!ok)
                            {
                                captureFails++;
                                Log($"FAIL {fname} pose {pose} (hole {d / Length:0.000} L from the root), enabled {e}: {vertices} vertices, " +
                                    $"tier {s0.x} links {s0.y} portal {s0.z} found {s0.w - 10}, headers {s4.x} hits {s4.y} fits {s4.z}, {stray} stray, seam {seamSplit} split ({seam:0.0000} mm); {note}");
                            }
                            line.Add($"{e}: {note}");

                            // Every host at this pose.
                            foreach (var h in hosts)
                            {
                                plug.sharedMaterial = h.mat;
                                plug.SetPropertyBlock(block);
                                Frame();
                                long drawn = 0, corridor = 0, nearA = 0, nearB = 0;
                                int firstBad = -1;
                                for (int i = 0; i < px.Length; i++)
                                {
                                    if (px[i] == empty[i]) continue;
                                    drawn++;
                                    byte z = zone[i];
                                    if (z == 0) { corridor++; if (firstBad < 0) firstBad = i; }
                                    if ((z & 1) != 0) nearA++;
                                    if ((z & 2) != 0) nearB++;
                                }
                                h.drawn[f] += drawn;
                                h.corridor[f] += corridor;
                                bool hostOk = corridor == 0 && (walked == 0 || nearA > 0) && (portalled > 0 ? nearB > 0 : nearB == 0);
                                // A host's first frame with corridor pixels, as a picture: what it
                                // drew, the corridor pixels magenta, the two mouths' zones tinted.
                                if (dump != null && corridor > 0 && !h.dumped[f])
                                {
                                    h.dumped[f] = true;
                                    var shot = new Texture2D(W, H, TextureFormat.RGBA32, false, false);
                                    RenderTexture.active = target;
                                    shot.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                                    RenderTexture.active = null;
                                    var c = shot.GetPixels32();
                                    for (int i = 0; i < c.Length; i++)
                                    {
                                        if (px[i] != empty[i] && zone[i] == 0) c[i] = new Color32(255, 0, 255, 255);
                                        else if (px[i] == empty[i] && zone[i] != 0) c[i] = new Color32(30, 40, (byte) (zone[i] == 1 ? 70 : 40), 255);
                                    }
                                    shot.SetPixels32(c);
                                    Directory.CreateDirectory(dump);
                                    File.WriteAllBytes($"{dump}/{Regex.Replace(h.name, "[^A-Za-z0-9]+", "-")}-{fname}-pose{pose}.png", shot.EncodeToPNG());
                                    Object.DestroyImmediate(shot);
                                }
                                if (!hostOk && h.failures++ < 8)
                                    Log($"FAIL {h.name}, {fname}, pose {pose} (hole {d / Length:0.000} L from the root), enabled {e}: " +
                                        $"{drawn} drawn, {corridor} in the corridor" +
                                        (firstBad >= 0 ? $" (first at {firstBad % W},{firstBad / W})" : "") +
                                        $", {nearA} near the entrance, {nearB} near the exit, {portalled} vertices out of the exit");
                            }
                        }
                        Log($"{fname} pose {pose}, hole {d / Length:0.000} L from the root: " + string.Join("; ", line));
                    }
                }
            }
            finally
            {
                cam.targetTexture = null;
                atlasCam.targetTexture = null;
                foreach (var o in made)
                {
                    if (o is RenderTexture rt) rt.Release();
                    Object.DestroyImmediate(o);
                }
            }

            // The verdicts.
            foreach (var h in hosts)
                for (int f = 0; f < 2; f++)
                {
                    bool pass = h.corridor[f] == 0;
                    all &= pass;
                    Log($"{(pass ? "PASS" : "FAIL")} {h.name}, {(f == 0 ? "HDR" : "8-bit")}: {h.corridor[f]} corridor pixels " +
                        $"of {h.drawn[f]} drawn over the sweep");
                }
            all &= hosts.All(h => h.failures == 0);
            var plain = hosts.FirstOrDefault(h => h.name == "Simple Lit");
            if (plain != null)
                foreach (var h in hosts.Where(h => h.outline))
                    for (int f = 0; f < 2; f++)
                    {
                        bool pass = h.drawn[f] > plain.drawn[f];
                        all &= pass;
                        Log($"{(pass ? "PASS" : "FAIL")} {h.name}, {(f == 0 ? "HDR" : "8-bit")}: the outline drew, " +
                            $"{h.drawn[f]} pixels against Simple Lit's {plain.drawn[f]} ({(h.drawn[f] - plain.drawn[f]) * 100.0 / Math.Max(plain.drawn[f], 1):+0.0;-0.0}%)");
                    }
            for (int f = 0; f < 2; f++)
            {
                string fname = f == 0 ? "HDR" : "8-bit";
                bool pass = worstExit[f] <= ExitBound && worstSeam[f] == 0;
                all &= pass;
                Log($"{(pass ? "PASS" : "FAIL")} {fname} deform: exit vertices at most {worstExit[f]:0.0000} mm from the decoded exit's " +
                    $"line ({worstAxial[f]:0.0000} axial), bound {ExitBound}, with the roll fitted (at most {worstRoll[f]:0.000} deg); " +
                    $"{worstPlain[f]:0.0000} mm hung off the plug's own up; seams at most {worstSeam[f]:0.0000} mm; " +
                    $"decoded exit at most {worstDecoded[f].mm:0.000} mm and {worstDecoded[f].deg:0.000} deg from the one written");
            }
            Log($"{(captureFails == 0 ? "PASS" : "FAIL")} captures: {captureFails} failed; {bandRings} band rings over the sweep");
            return all && captureFails == 0;
        }

        // A tube along +Z: ring k at z, vertex j round it, the last column the
        // first again with u = 1. Normals out, tangents round.
        static Vector3 Rest(int v, out Vector3 n, out Vector4 t)
        {
            int k = v / Columns, j = v % Columns;
            float a = 2 * Mathf.PI * (j % Sides) / Sides, z = Length * (Start + (1 - Start) * k / Rings);
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            n = new Vector3(c, s, 0);
            t = new Vector4(-s, c, 0, 1);
            return new Vector3(Radius * c, Radius * s, z);
        }

        // The mesh, the same vertices as points for the capture, and the bake:
        // a header, then position, normal, tangent and active per vertex.
        static void BuildPlug(out Mesh mesh, out Mesh points, out Texture2D bake)
        {
            var pos = new Vector3[Plug];
            var nrm = new Vector3[Plug];
            var tan = new Vector4[Plug];
            var uv = new Vector2[Plug];
            var col = new Color[Plug];
            var rng = new System.Random(20261018);
            var tint = Enumerable.Range(0, (Rings + 1) * Sides).Select(_ =>
                new Color(0.2f + 0.8f * (float) rng.NextDouble(), (float) rng.NextDouble(), (float) rng.NextDouble(), 0.2f + 0.8f * (float) rng.NextDouble())).ToArray();
            for (int v = 0; v < Plug; v++)
            {
                pos[v] = Rest(v, out nrm[v], out tan[v]);
                int k = v / Columns, j = v % Columns;
                uv[v] = new Vector2((float) j / Sides, (float) k / Rings);
                col[v] = tint[k * Sides + j % Sides];
            }
            var tris = new List<int>();
            for (int k = 0; k < Rings; k++)
                for (int j = 0; j < Sides; j++)
                {
                    int a = k * Columns + j, b = a + 1, c = a + Columns, e = c + 1;
                    tris.AddRange(new[] { a, c, b, b, c, e });
                }
            var bounds = new Bounds(Vector3.zero, Vector3.one * 1000);
            mesh = new Mesh { name = "PortalBandPlug", vertices = pos, normals = nrm, tangents = tan, uv = uv, colors = col };
            mesh.SetTriangles(tris, 0);
            mesh.bounds = bounds;
            points = new Mesh { name = "PortalBandPoints", vertices = pos, normals = nrm, tangents = tan };
            points.SetIndices(Enumerable.Range(0, Plug).ToArray(), MeshTopology.Points, 0);
            points.bounds = bounds;

            const int width = 8192;
            int count = 1 + Plug * 10, height = (count + width - 1) / width;
            var bytes = new byte[width * height * 4];
            int at = 0;
            void Put(float f) { Buffer.BlockCopy(BitConverter.GetBytes(f), 0, bytes, at * 4, 4); at++; }
            Put(0);
            for (int v = 0; v < Plug; v++)
            {
                Put(pos[v].x); Put(pos[v].y); Put(pos[v].z);
                Put(nrm[v].x); Put(nrm[v].y); Put(nrm[v].z);
                Put(tan[v].x); Put(tan[v].y); Put(tan[v].z);
                Put(1);
            }
            bake = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0 };
            bake.SetPixelData(bytes, 0);
            bake.Apply(false, false);
        }

        static Vector4 TexelSize(Texture t) => new Vector4(1f / t.width, 1f / t.height, t.width, t.height);

        // The patcher's defaults, the plug's own frame, the atlas on; the
        // enable and the span ride the property block.
        static readonly Dictionary<string, float> Floats = new Dictionary<string, float>
        {
            ["_YAPS_VertexCount"] = Plug, ["_YAPS_Length"] = Length, ["_YAPS_Overrun"] = 1,
            ["_YAPS_BakeScale"] = 1, ["_YAPS_BakeGirth"] = 1, ["_YAPS_FrameFromVertex"] = 0, ["_YAPS_SelfTag"] = -1,
            ["_YAPS_UseAtlas"] = 1, ["_YAPS_TaperStart"] = 0.1f, ["_YAPS_TaperEnd"] = 0.3f, ["_YAPS_IdleLength"] = 1,
            ["_YAPS_IdleWidth"] = 1, ["_YAPS_SqueezeDistance"] = 0.15f, ["_YAPS_BulgeDistance"] = 0.2f,
            ["_YAPS_PumpSpeed"] = 6, ["_YAPS_PumpWidth"] = 1, ["_YAPS_WriggleSpeed"] = 2, ["_YAPS_BezierSmoothness"] = 1,
            ["_YAPS_Owner"] = 0, ["_YAPS_Debug"] = 0,
        };

        static Material Plugged(Material m)
        {
            foreach (var kv in Floats) m.SetFloat(kv.Key, kv.Value);
            return m;
        }

        // A probe shader from beside the spike copies, pointed at the project's own.
        static Shader Template(string spike, string file, string shipped)
        {
            string text = Lf(File.ReadAllText(Path.Combine(spike, file + ".shader")));
            text = Regex.Replace(text, "#include\\s+\"(yaps_\\w+\\.cginc)\"", m => $"#include \"{shipped}/{m.Groups[1].Value}\"");
            text = new Regex("Shader\\s+\"[^\"]*\"").Replace(text, $"Shader \"Hidden/PortalBand/{file}\"", 1);
            string path = $"{Folder}/{file}.shader";
            File.WriteAllText(path, text);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return Compiled(AssetDatabase.LoadAssetAtPath<Shader>(path), path);
        }

        static Shader Compiled(string name) => Compiled(Shader.Find(name), name);

        static Shader Compiled(Shader s, string name)
        {
            if (s == null) throw new InvalidOperationException(name + " not found");
            foreach (var m in ShaderUtil.GetShaderMessages(s))
                Log($"  {name}: {m.severity} {m.message} line {m.line}");
            if (!s.isSupported || ShaderUtil.ShaderHasError(s)) throw new InvalidOperationException(name + " did not compile");
            return s;
        }

        static Texture2D Texture(Func<int, int, Color> at)
        {
            var t = new Texture2D(64, 64, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    t.SetPixel(x, y, at(x, y));
            t.Apply(false, false);
            return t;
        }

        // A lock reads the material from disk, so what it points at must be there too.
        static Texture2D Asset(Texture2D t, string name)
        {
            AssetDatabase.CreateAsset(t, $"{Folder}/{name}.asset");
            return t;
        }

        // Which pixels look at the entrance (1), the exit (2) or neither.
        static byte[] Zones(Camera cam)
        {
            var z = new byte[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var ray = cam.ScreenPointToRay(new Vector3(x + 0.5f, y + 0.5f, 0));
                    z[y * W + x] = (byte) ((Near(ray, Entrance) ? 1 : 0) | (Near(ray, Exit) ? 2 : 0));
                }
            return z;
        }

        static bool Near(Ray r, Vector3 c)
        {
            var oc = c - r.origin;
            float t = Vector3.Dot(oc, r.direction);
            return t > 0 && oc.sqrMagnitude - t * t <= Reach * Reach;
        }

        // YapsAtlas.LevelQuads, in memory: corners in UV0, quad index in z, positions zero.
        static Mesh Quads(int count)
        {
            var corners = new List<Vector3>();
            var tris = new List<int>();
            for (int q = 0; q < count; q++)
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

        static HashSet<string> Entries(string root) => new HashSet<string>(
            Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
                .Select(p => p.Replace('\\', '/')).Where(p => !p.EndsWith(".meta")));

        static bool Same(Vector3 x, Vector3 y) =>
            BitConverter.SingleToInt32Bits(x.x) == BitConverter.SingleToInt32Bits(y.x)
            && BitConverter.SingleToInt32Bits(x.y) == BitConverter.SingleToInt32Bits(y.y)
            && BitConverter.SingleToInt32Bits(x.z) == BitConverter.SingleToInt32Bits(y.z);

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
#endif
