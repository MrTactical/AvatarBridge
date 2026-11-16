#if CVR_CCK_EXISTS && UNITY_EDITOR && AVATARBRIDGE_YAPS
// Every popular avatar shader, each of its modes that moves vertices, through
// the shipped YAPS patcher: does it patch or refuse with a reason, does the
// patched copy draw as the original while YAPS is off, and does every pass it
// draws follow the deform?
//
//   -executeMethod AvatarBridge.Regression.ShaderSweepProbe.Run
//       [-sweepHosts Mochie]     only hosts whose name contains it (a|b for either)
//       [-sweepDump <dir>]       every host's frames as PNGs, strays marked
//
// PortalCopyProbe's tube on a plain mesh, its frame from the renderer, seen
// from above with a point light (the add pass) and a sun whose shadow falls on
// a floor well clear of the tube (the caster). Two moves, each against YAPS
// Simple Lit drawing the same move: the plug bending sideways into a hole on
// the atlas, and a socket opening, one baked shape carrying the tube 0.15 m
// across at full depth. A pass the patch missed draws where the tube was, so
// per move a host may draw nothing where only Simple Lit's unmoved tube is,
// outlines and all, in the view and in the shadow.
//
// Gates per host: the patch answers (patched, or refused with a reason, then
// drawn on its fallback as the converter does); off is the original (YAPS
// disabled: 0 pixels more than 2/255 from the unpatched shader); each move
// draws 0 pixels where only the unmoved tube was and covers at least half of
// Simple Lit's moved tube; an outline mode draws more than Simple Lit. A
// shadow left straight is a FAIL unless the patch's report says so.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    public static class ShaderSweepProbe
    {
        const string Tag = "[ShaderSweep] ";
        const string Folder = "Assets/__ShaderSweep";
        internal const string PoiyomiPath = "Assets/_PoiyomiShaders/Shaders/9.3/Toon/Poiyomi Toon.shader";
        const string PoiyomiProFolder = "Assets/_PoiyomiShaders/Shaders/9.3/Pro/";
        const int W = 1280, H = 800, Layer = 31, Owner = 0x5A3C17, Number = 3;
        // Wider than any outline below (about 5 mm, 7 px at the tube), narrower than the
        // gap between the unmoved tube and either move (90 mm and more).
        const int Margin = 60, ShadowMargin = 40;
        static readonly Vector3 Root = new Vector3(30.4f, 1.0f, 12.6f);
        // Ahead of the root and square beside it, facing back the way the plug comes in.
        static readonly Vector3 HoleAt = Root + new Vector3(0.15f, 0, 0.10f);
        static readonly Vector3 Shift = new Vector3(0.15f, 0, 0);
        const float Drop = 0.5f;

        internal class SweepHost
        {
            public string family, name;
            public Material source;
            public bool outline;
        }

        enum State { Off, Rest, Bend, Open }

        [MenuItem("Tools/Avatar Bridge/Dev/Portal/Shader sweep")]
        public static void Run()
        {
            int code = 1;
            bool async = ShaderUtil.allowAsyncCompilation;
            var before = PortalCopyProbe.Entries("Assets");
            var shadows = QualitySettings.shadows;
            var resolution = QualitySettings.shadowResolution;
            var distance = QualitySettings.shadowDistance;
            var cascades = QualitySettings.shadowCascades;
            var projection = QualitySettings.shadowProjection;
            int pixelLights = QualitySettings.pixelLightCount;
            var ambientMode = RenderSettings.ambientMode;
            var ambient = RenderSettings.ambientLight;
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
                QualitySettings.shadows = shadows;
                QualitySettings.shadowResolution = resolution;
                QualitySettings.shadowDistance = distance;
                QualitySettings.shadowCascades = cascades;
                QualitySettings.shadowProjection = projection;
                QualitySettings.pixelLightCount = pixelLights;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambient;
                var made = PortalCopyProbe.Entries("Assets").Except(before).OrderBy(p => p.Length).ToList();
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
            string only = PortalCopyProbe.Arg("-sweepHosts");
            string dump = PortalCopyProbe.Arg("-sweepDump");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__ShaderSweep");
            var hosts = Hosts(Folder, n => only == null || only.Split('|').Any(n.Contains), out bool all);
            if (hosts.Count == 0) throw new InvalidOperationException("no host to draw");
            // One lock for every Poiyomi, as the converter's batch does.
            YapsLock.Prepare(hosts.Select(h => h.source));
            Log($"GPU {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}; {hosts.Count} hosts");

            var made = new List<Object>();
            T Made<T>(T o) where T : Object { made.Add(o); return o; }
            PortalCopyProbe.BuildPlug(false, out var mesh, out var points, out var plugBake);
            Made(mesh); Made(points); Made(plugBake);
            var socketBake = Made(SocketBake());

            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowDistance = 20;
            QualitySettings.shadowCascades = 1;
            QualitySettings.shadowProjection = ShadowProjection.CloseFit;
            QualitySettings.pixelLightCount = 4;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.25f, 0.25f, 0.3f, 1);

            var plugGo = Made(new GameObject("__SweepPlug") { hideFlags = HideFlags.DontSave, layer = Layer });
            plugGo.transform.position = Root;
            plugGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            var plug = plugGo.AddComponent<MeshRenderer>();
            plug.shadowCastingMode = ShadowCastingMode.On;
            plug.receiveShadows = false;
            plug.lightProbeUsage = LightProbeUsage.Off;
            plug.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var floorGo = Made(GameObject.CreatePrimitive(PrimitiveType.Plane));
            floorGo.hideFlags = HideFlags.DontSave;
            floorGo.layer = Layer;
            floorGo.transform.position = Root + new Vector3(0.3f, -Drop, 0.15f);
            floorGo.transform.localScale = Vector3.one * 0.3f;
            var floor = floorGo.GetComponent<MeshRenderer>();
            floor.sharedMaterial = Made(new Material(Shader.Find("Standard")) { color = new Color(0.8f, 0.8f, 0.8f, 1) });
            floor.shadowCastingMode = ShadowCastingMode.Off;
            floor.receiveShadows = true;
            floor.lightProbeUsage = LightProbeUsage.Off;
            floor.reflectionProbeUsage = ReflectionProbeUsage.Off;
            // The sun's shadow lands Drop across in x, clear of the tube in the view.
            var sunGo = Made(new GameObject("__SweepSun") { hideFlags = HideFlags.DontSave });
            sunGo.transform.rotation = Quaternion.LookRotation(new Vector3(1, -1, 0));
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 1;
            sun.shadowResolution = LightShadowResolution.VeryHigh;
            sun.shadowBias = 0.02f;
            sun.shadowNormalBias = 0;
            sun.cullingMask = 1 << Layer;
            // Never black: a black point light is a plug talking to sockets.
            var lampGo = Made(new GameObject("__SweepLamp") { hideFlags = HideFlags.DontSave });
            lampGo.transform.position = Root + new Vector3(0.05f, 0.25f, 0.15f);
            var lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.range = 1.5f;
            lamp.intensity = 1.5f;
            lamp.color = new Color(1, 0.85f, 0.7f, 1);
            lamp.renderMode = LightRenderMode.ForcePixel;
            lamp.shadows = LightShadows.None;
            lamp.cullingMask = 1 << Layer;
            var camGo = Made(new GameObject("__SweepCamera") { hideFlags = HideFlags.DontSave });
            camGo.transform.SetPositionAndRotation(Root + new Vector3(0.25f, 0.9f, 0.12f), Quaternion.LookRotation(Vector3.down, Vector3.forward));
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 36;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 10;
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.07f, 0.09f, 1);
            cam.renderingPath = RenderingPath.Forward;
            cam.allowMSAA = false;
            cam.allowHDR = false;
            var target = Made(new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default));
            cam.targetTexture = target;
            var read = Made(new Texture2D(W, H, TextureFormat.RGBA32, false, false));

            // The atlas as a frame leaves it, with the hole and without.
            var atlasGo = Made(new GameObject("__SweepAtlasCamera") { hideFlags = HideFlags.DontSave });
            var atlasCam = atlasGo.AddComponent<Camera>();
            atlasCam.enabled = false;
            atlasCam.cullingMask = 0;
            atlasCam.clearFlags = CameraClearFlags.SolidColor;
            var levelQuads = Made(PortalCopyProbe.Quads(8));
            var socketMat = Made(new Material(PortalCopyProbe.Compiled("YAPS/Atlas Socket")));
            socketMat.SetFloat("_YAPS_Kind", 1);
            socketMat.SetFloat("_YAPS_Owner", Owner);
            socketMat.SetFloat("_YAPS_SocketIndex", Number);
            var clearMat = Made(new Material(PortalCopyProbe.Compiled("YAPS/Atlas Clear")));
            Shader.SetGlobalVector("_YAPS_Atlas_TexelSize", new Vector4(1f / W, 1f / H, W, H));
            RenderTexture Atlas(bool hole)
            {
                var rt = Made(new RenderTexture(W, H, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point });
                var cb = new CommandBuffer();
                cb.DrawMesh(levelQuads, Matrix4x4.identity, clearMat, 0, 0);
                if (hole)
                {
                    var at = Matrix4x4.TRS(HoleAt, Quaternion.LookRotation(Vector3.left), Vector3.one);
                    cb.DrawMesh(levelQuads, at, socketMat, 0, 0);
                    cb.DrawMesh(levelQuads, at, socketMat, 0, 1);
                }
                atlasCam.targetTexture = rt;
                atlasCam.backgroundColor = new Color(0.3f, 0.6f, 0.9f, 1);
                atlasCam.AddCommandBuffer(CameraEvent.AfterEverything, cb);
                atlasCam.Render();
                atlasCam.RemoveCommandBuffer(CameraEvent.AfterEverything, cb);
                atlasCam.targetTexture = null;
                cb.Release();
                return rt;
            }
            var withHole = Atlas(true);
            var bare = Atlas(false);

            // The patcher's defaults ride the material; the state rides a block.
            var blocks = Enum.GetValues(typeof(State)).Cast<State>().Select(s =>
            {
                var b = new MaterialPropertyBlock();
                foreach (var kv in PortalCopyProbe.Floats(false)) b.SetFloat(kv.Key, kv.Value);
                var bake = s == State.Open ? socketBake : plugBake;
                b.SetTexture("_YAPS_Bake", bake);
                b.SetVector("_YAPS_Bake_TexelSize", PortalCopyProbe.TexelSize(bake));
                b.SetFloat("_YAPS_Enabled", s == State.Rest || s == State.Bend ? 1 : 0);
                b.SetFloat("_YAPS_SocketPower", s == State.Open ? 1 : 0);
                b.SetFloat("_YAPS_SocketDepth", s == State.Open ? 1 : -1);
                b.SetFloat("_YAPS_ShapeCount", s == State.Open ? 1 : 0);
                b.SetVector("_YAPS_SocketShapeStart", Vector4.zero);
                b.SetVector("_YAPS_SocketShapeFade", Vector4.one * 0.5f);
                return b;
            }).ToArray();
            Material Wear(Material m, Shader patched)
            {
                m.shader = patched;
                PortalCopyProbe.Plugged(m);
                m.SetTexture("_YAPS_Bake", plugBake);
                m.SetVector("_YAPS_Bake_TexelSize", PortalCopyProbe.TexelSize(plugBake));
                return Made(m);
            }
            Color32[] Frame(Material m, State s, bool withFloor)
            {
                Shader.SetGlobalTexture("_YAPS_Atlas", s == State.Bend ? withHole : bare);
                plug.enabled = m != null;
                if (m != null)
                {
                    plug.sharedMaterial = m;
                    plug.SetPropertyBlock(blocks[(int) s]);
                }
                floor.enabled = withFloor;
                cam.Render();
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                RenderTexture.active = null;
                return read.GetPixels32();
            }

            try
            {
                var clear = Frame(null, State.Off, false);
                var floorOnly = Frame(null, State.Off, true);
                int uneven = clear.Count(c => !Same(c, clear[0]));
                if (uneven != 0) { Log($"FAIL {uneven} pixels of the empty frame differ from the clear"); all = false; }

                // The reference: YAPS Simple Lit, patched as any host is.
                var litSource = Made(new Material(Shader.Find("YAPS/Simple Lit")) { name = "Simple Lit reference" });
                var litShader = YapsShaderPatcher.Patch(litSource, new BridgeReport(), out string litRefusal, out _);
                if (litShader == null) throw new InvalidOperationException("YAPS Simple Lit refused the patch: " + litRefusal);
                var lit = Wear(new Material(litSource), litShader);
                var view = new bool[4][];
                foreach (State s in Enum.GetValues(typeof(State))) view[(int) s] = Drawn(Frame(lit, s, false), clear);
                var region = Not(Dilate(Or(Or(view[(int) State.Off], view[(int) State.Bend]), view[(int) State.Open]), Margin));
                var shade = new bool[4][];
                foreach (State s in Enum.GetValues(typeof(State))) shade[(int) s] = Shadow(Frame(lit, s, true), floorOnly, region);
                var zone = new bool[4][];
                var shadeZone = new bool[4][];
                foreach (var s in new[] { State.Bend, State.Open })
                {
                    zone[(int) s] = And(view[(int) State.Off], Not(Dilate(view[(int) s], Margin)));
                    shadeZone[(int) s] = And(shade[(int) State.Off], Not(Dilate(shade[(int) s], ShadowMargin)));
                    int z = Count(zone[(int) s]), sz = Count(shadeZone[(int) s]);
                    Log($"Simple Lit, {s}: {Count(view[(int) s])} pixels drawn, {Count(shade[(int) s])} in shadow; " +
                        $"{z} where only the unmoved tube is, {sz} where only its shadow is");
                    if (z < 1000 || sz < 500) { Log($"FAIL {s}: Simple Lit's move leaves too little of the unmoved tube to test against"); all = false; }
                }
                Log($"Simple Lit: {Count(view[(int) State.Off])} pixels unmoved, {Count(shade[(int) State.Off])} in its shadow; " +
                    $"{Count(region)} pixels of floor read for shadows");

                var rows = new List<string>();
                foreach (var h in hosts)
                {
                    var report = new BridgeReport();
                    var basis = YapsLock.Basis(h.source);
                    var patched = YapsShaderPatcher.Patch(h.source, report, out string refusal, out int skipped);
                    // A fresh report per host, and the entry names the locked stand-in, not the host.
                    string said = string.Join(" ", report.Entries.Select(e => e.Detail));
                    bool native = patched != null;
                    // A fallback that is the host's own source must draw as the host did.
                    bool sameLook = native;
                    string host = "patched";
                    Material drawn;
                    string patch;
                    var fails = new List<string>();
                    if (native)
                    {
                        drawn = Wear(new Material(basis) { name = h.name }, patched);
                        patch = $"patched to \"{patched.name}\" ({Passes(patched)}, {Calls(patched, dump, h.name)})";
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(refusal)) fails.Add("refused with no reason");
                        var plain = YapsNativeBuilder.OnFallback(h.source, out string why);
                        var onLit = plain != null ? YapsShaderPatcher.Patch(plain, report, out _, out skipped) : null;
                        if (onLit == null)
                        {
                            Log($"FAIL {h.name}: refused ({refusal}), and its fallback could not take it either ({why})");
                            all = false;
                            continue;
                        }
                        drawn = Wear(new Material(plain) { name = h.name }, onLit);
                        host = plain.shader.name;
                        sameLook = host == YapsNativeBuilder.StandardName;
                        patch = $"refused: {refusal}; drawn on {host}, as the converter does";
                    }
                    bool skinnedStraight = said.Contains("a skinned plug on this shader stays straight");
                    bool shadowSaid = skipped > 0;
                    Log($"{h.name}: {patch}" + (shadowSaid ? $"; {skipped} shadow passes left straight, the report says so" : "") +
                        (skinnedStraight ? "; a skinned plug stays straight on it, the report says so" : ""));

                    // Off is the original, and so, near enough, is on with nothing in reach.
                    string off = "n/a", rest = "n/a";
                    bool castsAny = true;
                    if (sameLook)
                    {
                        if (!basis.shader.isSupported || ShaderUtil.ShaderHasError(basis.shader)) fails.Add("the original does not compile here");
                        var orig = Frame(basis, State.Off, true);
                        int drawnPx = Count(Drawn(orig, floorOnly));
                        castsAny = Count(Shadow(orig, floorOnly, region)) >= 100;
                        var (n0, d0) = Diff(orig, Frame(drawn, State.Off, true), 2);
                        var (n1, d1) = Diff(orig, Frame(drawn, State.Rest, true), 2);
                        off = $"{n0} px over 2/255 (max {d0})";
                        rest = $"{n1} px over 2/255 (max {d1})";
                        if (drawnPx < 1000) fails.Add($"the original draws only {drawnPx} pixels");
                        if (n0 != 0) fails.Add("off is not the original: " + off);
                        Log($"  {h.name}: original {drawnPx} px; YAPS off {off}; on with nothing in reach {rest}");
                    }

                    var verdicts = new List<string>();
                    foreach (var s in new[] { State.Bend, State.Open })
                    {
                        int k = (int) s;
                        var v = Drawn(Frame(drawn, s, false), clear);
                        int stray = Count(And(v, zone[k])), got = Count(And(v, view[k])), want = Count(view[k]), all1 = Count(v);
                        var floorFrame = Frame(drawn, s, true);
                        var f = Shadow(floorFrame, floorOnly, region);
                        int sStray = Count(And(f, shadeZone[k])), sGot = Count(And(f, shade[k])), sWant = Count(shade[k]);
                        bool viewOk = stray == 0 && got * 2 >= want;
                        bool shadowOk = !castsAny || (sStray == 0 && sGot * 2 >= sWant);
                        string sv = !castsAny ? "casts none" : shadowOk ? "PASS" : shadowSaid ? "straight, said" : "FAIL";
                        verdicts.Add($"{s} view {(viewOk ? "PASS" : "FAIL")} ({stray} stray, {got}/{want} covered), " +
                                     $"shadow {sv} ({sStray} stray, {sGot}/{sWant} covered)");
                        if (!viewOk) fails.Add($"{s} view: {stray} pixels where only the unmoved tube was, {got} of Simple Lit's {want} covered");
                        if (!shadowOk && !shadowSaid) fails.Add($"{s} shadow: {sStray} pixels where only the unmoved shadow was, {sGot} of {sWant} covered");
                        // Simple Lit has no outline: a refused host loses it by design.
                        if (s == State.Bend && h.outline && native)
                        {
                            bool outlined = all1 > want;
                            verdicts.Add($"outline {(outlined ? "drew" : "DID NOT DRAW")} ({(all1 - want) * 100.0 / Math.Max(want, 1):+0.0;-0.0}% on Simple Lit)");
                            if (!outlined) fails.Add("the outline did not draw, so its mode went untested");
                        }
                        if (dump != null) Dump(dump, $"{h.name}-{s}", floorFrame, And(v, zone[k]), zone[k], And(f, shadeZone[k]), shadeZone[k]);
                    }
                    bool pass = fails.Count == 0;
                    all &= pass;
                    Log($"{(pass ? "PASS" : "FAIL")} {h.name}: " + string.Join("; ", verdicts) + (pass ? "" : " -- " + string.Join("; ", fails)));
                    rows.Add(string.Join("\t", h.family, h.name, pass ? "PASS" : "FAIL", host,
                        native ? "" : refusal, off, rest, shadowSaid ? "shadow straight, said" : "", skinnedStraight ? "skinned straight, said" : "",
                        string.Join("; ", verdicts)));
                }
                foreach (string r in rows) Log("ROW\t" + r);
                return all;
            }
            finally
            {
                cam.targetTexture = null;
                foreach (var o in made)
                {
                    if (o is RenderTexture rt) rt.Release();
                    Object.DestroyImmediate(o);
                }
            }
        }

        // The hosts, every material an asset (a lock reads it from disk). A mode set on a
        // property the shader lacks would test the default, so that host fails instead.
        internal static List<SweepHost> Hosts(string folder, Func<string, bool> wanted, out bool ok)
        {
            var list = new List<SweepHost>();
            bool good = true;
            var mask = Asset(PortalCopyProbe.Texture((x, y) => { float g = 0.2f + 0.8f * ((x * 7 + y * 13) % 64) / 63f; return new Color(g, g, g, 1); }), folder, "SweepMask");
            // A tangent-space normal leaning well off the surface, for outlines that read one.
            var tilt = Asset(PortalCopyProbe.Texture((x, y) => new Color(0.85f, 0.5f, 0.75f, 1)), folder, "SweepTilt");
            void Add(string family, string name, string shader, bool outline, Action<Material> set = null)
            {
                if (!wanted(name)) return;
                var s = shader.StartsWith("Assets/") || shader.StartsWith("Packages/") ? AssetDatabase.LoadAssetAtPath<Shader>(shader) : Shader.Find(shader);
                if (s == null) { Log($"MISSING {name}: \"{shader}\" is not in this project"); return; }
                var m = new Material(s) { name = name };
                try { set?.Invoke(m); }
                catch (InvalidOperationException e) { Log($"FAIL {name}: {e.Message}"); good = false; return; }
                AssetDatabase.CreateAsset(m, $"{folder}/SweepHost{list.Count}.mat");
                list.Add(new SweepHost { family = family, name = name, source = m, outline = outline });
            }

            Add("YAPS", "Simple Lit", "YAPS/Simple Lit", false);
            // Standard is refused and moves to YAPS Standard, so both are here, every map on as well:
            // a normal or height map changes which input Unity's own struct carries.
            void Maps(Material m)
            {
                F(m, "_BumpMap", tilt); m.EnableKeyword("_NORMALMAP");
                F(m, "_MetallicGlossMap", mask); m.EnableKeyword("_METALLICGLOSSMAP");
                F(m, "_ParallaxMap", mask); m.EnableKeyword("_PARALLAXMAP");
                F(m, "_OcclusionMap", mask);
            }
            // Cutout adds the caster's output struct, which the wrapper passes through.
            void Cutout(Material m) { F(m, "_Mode", 1); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = 2450; }
            Add("YAPS", "YAPS Standard", "YAPS/Standard", false);
            Add("YAPS", "YAPS Standard, every map", "YAPS/Standard", false, Maps);
            Add("YAPS", "YAPS Standard cutout, every map", "YAPS/Standard", false, m => { Maps(m); Cutout(m); });
            Add("Unity", "Standard", "Standard", false);
            Add("Unity", "Standard, every map", "Standard", false, Maps);
            Add("Unity", "Autodesk Interactive", "Autodesk Interactive", false);
            Add("VRChat", "VRChat Mobile Toon Lit", "VRChat/Mobile/Toon Lit", false);
            Add("VRChat", "VRChat Mobile Standard Lite", "VRChat/Mobile/Standard Lite", false);
            Add("VRChat", "VRChat Toon Standard", "VRChat/Mobile/Toon Standard", false);
            Add("VRChat", "VRChat Toon Standard outline", "VRChat/Mobile/Toon Standard (Outline)", true,
                m => { F(m, "_OutlineThickness", 0.2f); F(m, "_OutlineMask", mask); });
            // Width from a mask and from vertex red, so it differs vertex by vertex.
            void Poi(Material m)
            {
                F(m, "_EnableOutlines", 1);
                F(m, "_LineWidth", 1);
                F(m, "_OutlineMask", mask);
                F(m, "_OutlineVertexColorMask", 1);
            }
            Add("Poiyomi", "Poiyomi 9.3", PoiyomiPath, false);
            Add("Poiyomi", "Poiyomi 9.3 outline", PoiyomiPath, true, Poi);
            Add("Poiyomi", "Poiyomi 9.3 outline, vertex colour normals, local", PoiyomiPath, true, m => { Poi(m); F(m, "_OutlineUseVertexColorNormals", 1); });
            Add("Poiyomi", "Poiyomi 9.3 outline, vertex colour normals, world", PoiyomiPath, true, m => { Poi(m); F(m, "_OutlineUseVertexColorNormals", 1); F(m, "_OutlineSpace", 1); });
            Add("Poiyomi", "Poiyomi 9.3 outline, drop shadow", PoiyomiPath, true, m => { Poi(m); F(m, "_OutlineExpansionMode", 4); F(m, "_OutlineDropShadowOffset", new Vector4(0.01f, -0.01f, 0, 0)); });
            Add("Poiyomi", "Poiyomi Pro 9.3 tessellated", PoiyomiProFolder + "Poiyomi Pro Tessellated.shader", false);
            Add("Poiyomi", "Poiyomi Pro 9.3 fur", PoiyomiProFolder + "Poiyomi Pro Fur.shader", true);
            Add("lilToon", "lilToon", "lilToon", false);
            Add("lilToon", "lilToon outline", "Hidden/lilToonOutline", true, m => { F(m, "_OutlineWidth", 0.5f); F(m, "_OutlineWidthMask", mask); });
            // The other render modes borrow from lenders of their own; multi borrows nothing.
            Add("lilToon", "lilToon cutout", "Hidden/lilToonCutout", false);
            Add("lilToon", "lilToon transparent", "Hidden/lilToonTransparent", false);
            Add("lilToon", "lilToon two-pass transparent", "Hidden/lilToonTwoPassTransparent", false);
            Add("lilToon", "lilToon lite", "Hidden/lilToonLite", false);
            Add("lilToon", "lilToon tessellation", "Hidden/lilToonTessellation", false);
            Add("lilToon", "lilToon multi", "_lil/lilToonMulti", false);
            // Fur grows shells in a geometry stage from what the vertex stage hands it, so it
            // counts as an outline: the shells have to draw wider than Simple Lit's shaft.
            Add("lilToon", "lilToon fur", "Hidden/lilToonFur", true);
            Add("lilToon", "lilToon fur cutout", "Hidden/lilToonFurCutout", true);
            Add("lilToon", "lilToon fur two-pass", "Hidden/lilToonFurTwoPass", true);
            Add("Mochie", "Mochie Standard", "Mochie/Standard", false);
            Add("Mochie", "Mochie Uber", "Mochie/Uber", false);
            Add("Mochie", "Mochie Uber outline", "Mochie/Uber (Outline)", true, m => F(m, "_OutlineThicc", 0.5f));
            Add("Mochie", "Mochie Uber outline, vertex colour width", "Mochie/Uber (Outline)", true, m => { F(m, "_OutlineThicc", 0.5f); F(m, "_UseVertexColor", 1); });
            // UTS 2 takes its width in millimetres, its mode as a keyword.
            void Uts(Material m, string mode)
            {
                F(m, "_Outline_Width", 5);
                F(m, "_OUTLINE", mode == "POS" ? 1 : 0);
                m.DisableKeyword("_OUTLINE_NML");
                m.DisableKeyword("_OUTLINE_POS");
                m.EnableKeyword("_OUTLINE_" + mode);
            }
            const string UtsOutlined = "UnityChanToonShader/Toon_DoubleShadeWithFeather";
            Add("UTS", "UTS 2", "UnityChanToonShader/NoOutline/ToonColor_DoubleShadeWithFeather", false);
            Add("UTS", "UTS 2 outline, normal", UtsOutlined, true, m => Uts(m, "NML"));
            Add("UTS", "UTS 2 outline, position", UtsOutlined, true, m => Uts(m, "POS"));
            Add("UTS", "UTS 2 outline, baked normal", UtsOutlined, true, m => { Uts(m, "NML"); F(m, "_Is_BakedNormal", 1); F(m, "_BakedNormal", tilt); });
            Add("Xiexe", "XSToon 3", "Xiexe/Toon3/Standard", false);
            Add("Xiexe", "XSToon 3 outline", "Xiexe/Toon3/Standard_Outlined", true, m => F(m, "_OutlineWidth", 0.5f));
            Add("Xiexe", "XSToon 3 outline, vertex colour normals", "Xiexe/Toon3/Standard_Outlined", true, m => { F(m, "_OutlineWidth", 0.5f); F(m, "_OutlineNormalMode", 1); });
            void Scss(Material m) { F(m, "_OutlineMode", 1); F(m, "_outline_width", 0.5f); }
            Add("Silent", "Crosstone", "Silent's Cel Shading/Crosstone", false);
            Add("Silent", "Crosstone outline", "Silent's Cel Shading/Crosstone (Outline)", true, Scss);
            Add("Silent", "Lightramp outline", "Silent's Cel Shading/Lightramp (Outline)", true, Scss);
            Add("Silent", "Filamented", "Silent/Filamented", false);
            Add("ORL", "ORL Toon", "orels1/Toon/Main", false);
            Add("ORL", "ORL Toon v2", "orels1/Toon/v2/Main", false);
            Add("ORL", "ORL Standard", "orels1/Standard", false);
            Add("arktoon", "arktoon", "arktoon/Opaque", false);
            Add("arktoon", "arktoon outline", "arktoon/Opaque", true, m => { F(m, "_UseOutline", 1); F(m, "_OutlineWidth", 0.5f); });
            // Sunao 1.6.2: the outline grows along the normal by a centimetre times the scale, through a mask.
            void Sunao(Material m) { F(m, "_OutLineEnable", 1); F(m, "_OutLineSize", 1f); F(m, "_OutLineMask", mask); }
            Add("Sunao", "Sunao", "Sunao Shader/Opaque", false);
            Add("Sunao", "Sunao outline", "Sunao Shader/Opaque", true, Sunao);
            Add("Sunao", "Sunao stencil outline", "Sunao Shader/[Stencil Outline]/Opaque", true, Sunao);
            Add("Sunao", "Sunao transparent outline", "Sunao Shader/Transparent", true, Sunao);
            Add("Sunao", "Sunao fur", "Sunao Shader/Fur", true, m => { F(m, "_FurEnable", 1); F(m, "_FurLength", 0.5f); });
            ok = good;
            return list;
        }

        static void F(Material m, string p, float v) { Need(m, p); m.SetFloat(p, v); }
        static void F(Material m, string p, Texture v) { Need(m, p); m.SetTexture(p, v); }
        static void F(Material m, string p, Vector4 v) { Need(m, p); m.SetVector(p, v); }

        static void Need(Material m, string p)
        {
            if (!m.HasProperty(p)) throw new InvalidOperationException($"\"{m.shader.name}\" has no {p}, so this mode would be its default");
        }

        static Texture2D Asset(Texture2D t, string folder, string name)
        {
            AssetDatabase.CreateAsset(t, $"{folder}/{name}.asset");
            return t;
        }

        // The plug bake's layout with one shape after it: the tube carried by Shift, its
        // normals and tangents unchanged.
        static Texture2D SocketBake()
        {
            const int width = 8192;
            int count = 1 + PortalCopyProbe.Plug * 19;
            var floats = new float[width * ((count + width - 1) / width)];
            int at = 1;
            for (int v = 0; v < PortalCopyProbe.Plug; v++)
            {
                var p = PortalCopyProbe.Rest(v, out var n, out var t);
                foreach (float f in new[] { p.x, p.y, p.z, n.x, n.y, n.z, t.x, t.y, t.z, 1 }) floats[at++] = f;
            }
            for (int v = 0; v < PortalCopyProbe.Plug; v++, at += 9)
            {
                floats[at] = Shift.x;
                floats[at + 1] = Shift.y;
                floats[at + 2] = Shift.z;
            }
            var bytes = new byte[floats.Length * 4];
            Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
            var bake = new Texture2D(width, floats.Length / width, TextureFormat.RGBA32, false, true)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0 };
            bake.SetPixelData(bytes, 0);
            bake.Apply(false, false);
            return bake;
        }

        // What the render has to cover. A UsePass brings in another shader's pass, which the
        // patch never edits, so a straight one names its source.
        // How many deform calls the patched copy and its includes hold, and with a dump
        // folder the copy itself, since the probe deletes it afterwards. The copies share
        // the shader's _YAPS_ suffix.
        static string Calls(Shader s, string dump, string host)
        {
            string path = AssetDatabase.GetAssetPath(s);
            var suffix = Regex.Match(Path.GetFileNameWithoutExtension(path), @"_YAPS_[0-9a-f]+$");
            if (!suffix.Success) return "its copy was not found";
            var unit = Directory.GetFiles(Path.GetDirectoryName(path)).Where(f => !f.EndsWith(".meta")
                && Path.GetFileNameWithoutExtension(f).EndsWith(suffix.Value)).ToList();
            int calls = unit.Sum(f => Regex.Matches(File.ReadAllText(f), @"\bYapsDeform\s*\(\s*yapsPosition").Count);
            if (dump != null)
            {
                string to = Path.Combine(dump, "units", host);
                Directory.CreateDirectory(to);
                foreach (var f in unit) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            }
            return $"{calls} deform calls in {unit.Count} files";
        }

        static string Passes(Shader s)
        {
            var sub = ShaderUtil.GetShaderData(s).GetSubshader(0);
            int stages = Enumerable.Range(0, sub.PassCount).Count(p => sub.GetPass(p).HasShaderStage(ShaderType.Vertex));
            var used = Regex.Matches(File.ReadAllText(AssetDatabase.GetAssetPath(s)), @"^\s*UsePass\s+""([^""]+)""", RegexOptions.Multiline)
                .Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            return $"{stages} vertex passes in its first subshader" + (used.Count > 0 ? $", UsePass {string.Join(", ", used)}" : "");
        }

        static bool[] Drawn(Color32[] f, Color32[] empty)
        {
            var m = new bool[f.Length];
            for (int i = 0; i < f.Length; i++) m[i] = !Same(f[i], empty[i]);
            return m;
        }

        // Floor pixels clear of the tube and darker than the bare floor.
        static bool[] Shadow(Color32[] f, Color32[] floorOnly, bool[] region)
        {
            var m = new bool[f.Length];
            for (int i = 0; i < f.Length; i++)
                m[i] = region[i] && f[i].r + f[i].g + f[i].b < 0.85f * (floorOnly[i].r + floorOnly[i].g + floorOnly[i].b);
            return m;
        }

        static (int count, int max) Diff(Color32[] a, Color32[] b, int over)
        {
            int n = 0, max = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int d = Math.Max(Math.Max(Math.Abs(a[i].r - b[i].r), Math.Abs(a[i].g - b[i].g)), Math.Max(Math.Abs(a[i].b - b[i].b), Math.Abs(a[i].a - b[i].a)));
                if (d > over) n++;
                max = Math.Max(max, d);
            }
            return (n, max);
        }

        // A square of side 2r + 1, by running counts along rows then columns.
        static bool[] Dilate(bool[] m, int r)
        {
            var rows = new bool[m.Length];
            var o = new bool[m.Length];
            var p = new int[Math.Max(W, H) + 1];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++) p[x + 1] = p[x] + (m[y * W + x] ? 1 : 0);
                for (int x = 0; x < W; x++) rows[y * W + x] = p[Math.Min(W, x + r + 1)] - p[Math.Max(0, x - r)] > 0;
            }
            for (int x = 0; x < W; x++)
            {
                for (int y = 0; y < H; y++) p[y + 1] = p[y] + (rows[y * W + x] ? 1 : 0);
                for (int y = 0; y < H; y++) o[y * W + x] = p[Math.Min(H, y + r + 1)] - p[Math.Max(0, y - r)] > 0;
            }
            return o;
        }

        static bool[] And(bool[] a, bool[] b) => a.Select((x, i) => x && b[i]).ToArray();
        static bool[] Or(bool[] a, bool[] b) => a.Select((x, i) => x || b[i]).ToArray();
        static bool[] Not(bool[] a) => a.Select(x => !x).ToArray();
        static int Count(bool[] a) => a.Count(x => x);

        // The frame with the floor, the zones tinted and strays magenta (view) or orange (shadow).
        static void Dump(string dir, string name, Color32[] frame, bool[] stray, bool[] zone, bool[] shadeStray, bool[] shadeZone)
        {
            var c = (Color32[]) frame.Clone();
            for (int i = 0; i < c.Length; i++)
            {
                if (stray[i]) c[i] = new Color32(255, 0, 255, 255);
                else if (shadeStray[i]) c[i] = new Color32(255, 140, 0, 255);
                else if (zone[i] || shadeZone[i]) c[i] = Color32.Lerp(c[i], new Color32(0, 160, 255, 255), 0.25f);
            }
            var shot = new Texture2D(W, H, TextureFormat.RGBA32, false, false);
            shot.SetPixels32(c);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes($"{dir}/{Regex.Replace(name, "[^A-Za-z0-9]+", "-")}.png", shot.EncodeToPNG());
            Object.DestroyImmediate(shot);
        }

        static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        static void Log(string s) => Debug.Log(Tag + s);
    }
}
#endif
