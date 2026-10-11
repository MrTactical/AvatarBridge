using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections;
using System.Linq.Expressions;
using UnityEditor.Animations;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace AvatarBridge.Regression
{
    // Measurement gates for the squash architecture (Squash architecture.md, B1-B12; B13 waits on a
    // fallback design that exists only if the controller fails in game).
    // Batch Play mode in an empty scene; every line it logs starts with [SquashMeasure]. Editor
    // timings run above the player's, so read ratios and floors, not absolutes.
    [InitializeOnLoad]
    public static class SquashMeasureProbe
    {
        const string Flag = "SquashMeasureProbe.run";

        static SquashMeasureProbe()
        {
            EditorApplication.playModeStateChanged += s =>
            {
                if (s != PlayModeStateChange.EnteredPlayMode || SessionState.GetInt(Flag, 0) == 0) return;
                SessionState.EraseInt(Flag);
                new GameObject("__SquashMeasure").AddComponent<SquashMeasureDriver>();
            };
        }

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetInt(Flag, 1);
            EditorApplication.EnterPlaymode();
        }

        public static void Log(string s) => Debug.Log("[SquashMeasure] " + s);
    }

    public class SquashMeasureDriver : MonoBehaviour
    {
        const int Empty = 31;
        int stage, frames;
        readonly List<int> latency = new List<int>();
        readonly Dictionary<int, float> dtByFrame = new Dictionary<int, float>();
        readonly List<string> dtRows = new List<string>(), camRows = new List<string>();

        Camera stampCam;
        RenderTexture stampRt;
        NativeArray<Color32> front, back;
        AsyncGPUReadbackRequest request;
        bool inFlight;

        Material dtMat;
        RenderTexture[] state = new RenderTexture[2];
        int ping;
        int dtReads;
        double dtWorst, timeWorst;

        void Start()
        {
            SquashMeasureProbe.Log($"device {SystemInfo.graphicsDeviceType}, async readback {SystemInfo.supportsAsyncGPUReadback}, RGBAHalf {SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)}");
            B8();
            B3();
            B2();
            B1();
            B7();
            B9();
            B4Setters();
            SetUpStamp();
            SetUpDt();
            SetUpB5();
            SetUpB6();
            SetUpB11();
        }

        static Shader Make(string src) => ShaderUtil.CreateShaderAsset(src);

        static RenderTexture Rt(int w, int h, RenderTextureFormat f) =>
            new RenderTexture(w, h, 0, f, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point, useMipMap = false };

        static Camera Cam(RenderTexture rt, Vector3 at)
        {
            var c = new GameObject("cam").AddComponent<Camera>();
            c.transform.position = at;
            c.orthographic = true;
            c.orthographicSize = 0.05f;
            c.nearClipPlane = 0.001f;
            c.farClipPlane = 0.5f;
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = Color.clear;
            c.renderingPath = RenderingPath.VertexLit;
            c.useOcclusionCulling = false;
            c.allowHDR = c.allowMSAA = false;
            c.targetTexture = rt;
            c.enabled = false;
            return c;
        }

        static Color32 Read(RenderTexture rt, int x, int y)
        {
            var r = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
            r.WaitForCompletion();
            return r.hasError ? new Color32(1, 2, 3, 4) : r.GetData<Color32>()[y * rt.width + x];
        }

        // B8: what the parser decodes when our shader is not what drew. Channel value / 255 is what
        // a task maps over its range, so anything but 0 in r or g moves a part.
        void B8()
        {
            var fresh = Rt(4, 4, RenderTextureFormat.ARGB32);
            fresh.Create();
            Row("fresh texture, never rendered", Read(fresh, 0, 0));
            Row("parser no-data colour", new Color32(0, 0, 0, 255));

            var quad = SquashTestBuilder.ReaderQuad();
            var fallbacks = new (string label, Shader shader)[]
            {
                ("error shader", Shader.Find("Hidden/InternalErrorShader")),
                ("Unlit/Texture, no texture", Shader.Find("Unlit/Texture")),
                ("Unlit/Color default", Shader.Find("Unlit/Color")),
                ("Standard default", Shader.Find("Standard")),
            };
            foreach (var (label, shader) in fallbacks)
            {
                if (shader == null) { SquashMeasureProbe.Log($"B8 {label}: shader not found"); continue; }
                var rt = Rt(4, 4, RenderTextureFormat.ARGB32);
                var cam = Cam(rt, new Vector3(100, 0, 0));
                // A real mesh in front, since a fallback draws in object space, not our full-target trick.
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.transform.position = cam.transform.position + Vector3.forward * 0.1f;
                go.transform.localScale = Vector3.one;
                go.GetComponent<MeshRenderer>().sharedMaterial = new Material(shader);
                cam.Render();
                Row(label, Read(rt, 0, 0));
                Destroy(go);
                Destroy(cam.gameObject);
            }
            Destroy(quad);
        }

        static void Row(string label, Color32 c) =>
            SquashMeasureProbe.Log($"B8 {label}: rgba {c.r} {c.g} {c.b} {c.a}, {(c.r == 0 && c.g == 0 ? "rest" : "MOVES (flattening " + (c.r / 255f).ToString("0.00") + ", widening " + (c.g / 255f).ToString("0.00") + ")")}");

        // B3: the controller's per copy work on the CPU side: 20 matrices and three blits, 20 copies.
        void B3()
        {
            var mats = new Matrix4x4[20];
            var m = new Material(Shader.Find("Hidden/BlitCopy") ?? Shader.Find("Unlit/Texture"));
            var a = Rt(8, 4, RenderTextureFormat.ARGB32);
            var b = Rt(8, 4, RenderTextureFormat.ARGB32);
            var c = Rt(4, 4, RenderTextureFormat.ARGB32);
            a.Create(); b.Create(); c.Create();
            var times = new List<double>();
            for (int rep = 0; rep < 200; rep++)
            {
                var sw = Stopwatch.StartNew();
                for (int copy = 0; copy < 20; copy++)
                {
                    m.SetMatrixArray("_Bones", mats);
                    var t = RenderTexture.GetTemporary(8, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    Graphics.Blit(a, t, m);
                    Graphics.CopyTexture(t, b);
                    RenderTexture.ReleaseTemporary(t);
                    Graphics.Blit(b, a, m);
                    Graphics.Blit(a, c, m);
                }
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds * 1000.0 / 20);
            }
            SquashMeasureProbe.Log($"B3 controller per copy: median {Median(times):0.0} us, p90 {P(times, 0.9):0.0} us (CPU submit only)");
        }

        // B2: fixed cost of one tiny camera, by scene size, with the mask on Everything and on an
        // empty layer (what fix 2's player-layer mask approaches when no avatar is near).
        void B2()
        {
            var rt = Rt(4, 4, RenderTextureFormat.ARGB32);
            var cam = Cam(rt, new Vector3(0, -50, 0));
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<MeshFilter>().sharedMesh;
            var skinMesh = Instantiate(cube);
            skinMesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, skinMesh.vertexCount).ToArray();
            skinMesh.bindposes = new[] { Matrix4x4.identity };
            var mat = new Material(Shader.Find("Standard"));
            var root = new GameObject("scene").transform;
            int made = 0;
            var rng = new System.Random(1);
            foreach (int n in new[] { 500, 2000, 5000 })
            {
                for (; made < n; made++)
                {
                    var go = new GameObject("r" + made);
                    go.transform.SetParent(root, false);
                    go.transform.position = new Vector3((float)rng.NextDouble() * 60 - 30, (float)rng.NextDouble() * 4, (float)rng.NextDouble() * 60 - 30);
                    if (made % 2 == 0)
                    {
                        go.AddComponent<MeshFilter>().sharedMesh = cube;
                        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    }
                    else
                    {
                        var smr = go.AddComponent<SkinnedMeshRenderer>();
                        smr.sharedMesh = skinMesh;
                        smr.bones = new[] { go.transform };
                        smr.sharedMaterial = mat;
                    }
                }
                foreach (var (label, mask) in new[] { ("Everything", ~0), ("empty layer", 1 << Empty) })
                {
                    cam.cullingMask = mask;
                    for (int i = 0; i < 50; i++) cam.Render();
                    var times = new List<double>();
                    for (int i = 0; i < 1000; i++)
                    {
                        var sw = Stopwatch.StartNew();
                        cam.Render();
                        sw.Stop();
                        times.Add(sw.Elapsed.TotalMilliseconds * 1000.0);
                    }
                    SquashMeasureProbe.Log($"B2 {n} renderers, mask {label}: median {Median(times):0.0} us, p90 {P(times, 0.9):0.0} us");
                }
            }
            Destroy(root.gameObject);
            Destroy(cam.gameObject);
        }

        // B4: the client's parser loop as decompiled: in Update, swap a finished request in and
        // issue the next one, RGBA32 into a NativeArray, one in flight. The camera stamps the frame
        // it rendered, so the lag is exact: frames from the render to the value being applied.
        void SetUpStamp()
        {
            stampRt = Rt(4, 4, RenderTextureFormat.ARGB32);
            stampCam = Cam(stampRt, new Vector3(0, 200, 0));
            var q = new GameObject("stamp quad");
            q.transform.position = stampCam.transform.position + Vector3.forward * 0.1f;
            q.AddComponent<MeshFilter>().sharedMesh = SquashTestBuilder.ReaderQuad();
            q.AddComponent<MeshRenderer>().sharedMaterial = new Material(Make(StampSource));
            front = new NativeArray<Color32>(16, Allocator.Persistent);
            back = new NativeArray<Color32>(16, Allocator.Persistent);
        }

        // B12: a LateUpdate blit saw unity_DeltaTime as a constant 0.005 (first run; RGBAHalf
        // ping-pong itself worked). Does _Time advance there, so dt can come from the previous
        // frame's time kept in the state? Float state, so half precision does not hide the answer.
        void SetUpDt()
        {
            dtMat = new Material(Make(DtSource));
            for (int i = 0; i < 2; i++) { state[i] = Rt(8, 4, RenderTextureFormat.ARGBFloat); state[i].Create(); }
        }

        void Update()
        {
            Shader.SetGlobalFloat("_ProbeFrame", Time.frameCount);
            dtByFrame[Time.frameCount] = Time.deltaTime;
            if (stage == 0 && ++frames > 10) stage = 1;
            if (stage != 1) return;

            if (inFlight && request.hasError) inFlight = false;
            else if (inFlight && request.done)
            {
                (front, back) = (back, front);
                inFlight = false;
                var c = front[0];
                int drawn = c.r + 256 * c.g;
                latency.Add((Time.frameCount & 0xFFFF) - drawn);
                // The same question as B12 for an ordinary camera draw, to tell the blit from the editor's clock.
                if (dtByFrame.TryGetValue(drawn, out float truth) && camRows.Count < 5)
                    camRows.Add($"frame {drawn}: camera draw dt {c.b * 100f / 255f:0.0} ms (0.4 ms steps), Time.deltaTime {truth * 1000:0.000} ms");
            }
            if (!inFlight)
            {
                request = AsyncGPUReadback.RequestIntoNativeArray(ref back, stampRt, 0, TextureFormat.RGBA32);
                inFlight = true;
            }
            B5Sample();
            if (latency.Count >= 300 && dtReads >= 300 && b5Frames >= 300) Finish();
        }

        void LateUpdate()
        {
            if (stage != 1) return;
            stampCam.Render();

            var src = state[ping];
            var dst = state[1 - ping];
            dtMat.SetFloat("_Frame", Time.frameCount);
            Graphics.Blit(src, dst, dtMat);
            ping = 1 - ping;
            float now = Time.timeSinceLevelLoad;
            bool first = dtReads == 0 && dtRows.Count == 0;
            AsyncGPUReadback.Request(dst, 0, r =>
            {
                if (r.hasError) return;
                var px = r.GetData<float>();
                float uDt = px[0], tDt = px[1], t = px[2];
                int frame = Mathf.RoundToInt(px[3]);
                dtByFrame.TryGetValue(frame, out float truth);
                if (truth > 0 && dtReads > 2) dtWorst = Math.Max(dtWorst, Math.Abs(tDt - truth) / truth);
                timeWorst = Math.Max(timeWorst, Math.Abs(t - now));
                if (dtRows.Count < 5) dtRows.Add($"frame {frame}: unity_DeltaTime {uDt * 1000:0.000} ms, _Time.y step {tDt * 1000:0.000} ms, Time.deltaTime {truth * 1000:0.000} ms, _Time.y {t:0.0000} s vs {now:0.0000} s");
                dtReads++;
            });
        }

        static Color32[] ReadAll(RenderTexture rt)
        {
            var r = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
            r.WaitForCompletion();
            return r.hasError ? new Color32[0] : r.GetData<Color32>().ToArray();
        }

        // Every renderer in the mask through the nearness pass, the way CVRCameraHelper drives it:
        // r = lerp(2/255, 1, 1 - viewZ/far), BlendOp Max, ColorMask R.
        static Shader nearness;
        static Shader Nearness() => nearness = nearness ?? Make(NearnessSource);
        static byte Expected(float depth, float far) => (byte)Mathf.RoundToInt(255f * Mathf.Lerp(2f / 255f, 1f, 1f - depth / far));

        // B1 and B10: one renderer at a time, a known depth, the replacement on; is the nearness it
        // writes the nearness of where it really is (skinned: the posed place, not the bind pose)?
        void B1()
        {
            var rt = Rt(32, 32, RenderTextureFormat.ARGB32);
            var cam = Cam(rt, new Vector3(0, -300, 0));
            cam.orthographicSize = 0.5f;
            cam.farClipPlane = 1f;
            cam.SetReplacementShader(Nearness(), "");
            Vector3 At(float d) => cam.transform.position + Vector3.forward * d;

            GameObject Quad(Material m, float d)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.transform.position = At(d);
                go.transform.localScale = Vector3.one * 0.3f;
                go.GetComponent<MeshRenderer>().sharedMaterial = m;
                return go;
            }

            var cases = new List<(string label, Func<GameObject> make, float depth)>();
            foreach (var name in new[] { "Standard", "Unlit/Texture", "Unlit/Transparent", "Unlit/Transparent Cutout" })
            {
                var sh = Shader.Find(name);
                if (sh != null) cases.Add((name, () => Quad(new Material(sh), 0.4f), 0.4f));
            }
            foreach (var (label, m) in AvatarMaterials())
                cases.Add((label, () => Quad(m, 0.4f), 0.4f));
            cases.Add(("vertex displacement 0.1 toward the camera (A7: drawn undisplaced)", () => Quad(new Material(Make(DisplaceSource)), 0.4f), 0.4f));
            // Bound at 0.6, its bone moved to 0.3: the replacement must see 0.3.
            cases.Add(("skinned, bone posed 0.3 nearer than the bind pose", () =>
            {
                var go = new GameObject("skinned");
                var bone = new GameObject("bone").transform;
                bone.SetParent(go.transform, false);
                var mesh = Instantiate(GameObject.CreatePrimitive(PrimitiveType.Quad).GetComponent<MeshFilter>().sharedMesh);
                mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, mesh.vertexCount).ToArray();
                mesh.bindposes = new[] { Matrix4x4.identity };
                go.transform.position = At(0.6f);
                bone.localPosition = Vector3.zero;
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.bones = new[] { bone };
                smr.rootBone = bone;
                smr.updateWhenOffscreen = true;
                smr.sharedMaterial = new Material(Shader.Find("Standard"));
                bone.position = At(0.3f);
                return go;
            }, 0.3f));
            cases.Add(("particle", () =>
            {
                var go = new GameObject("particles");
                go.transform.position = At(0.5f);
                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.startSpeed = 0f;
                main.startSize = 0.2f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = ps.emission;
                em.enabled = false;
                go.GetComponent<ParticleSystemRenderer>().sharedMaterial = new Material(Shader.Find("Standard"));
                ps.Emit(new ParticleSystem.EmitParams { position = At(0.5f), startLifetime = 100f, startSize = 0.2f }, 1);
                return go;
            }, 0.5f));
            cases.Add(("line", () =>
            {
                var go = new GameObject("line");
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.SetPositions(new[] { At(0.5f) + Vector3.left * 0.2f, At(0.5f) + Vector3.right * 0.2f });
                lr.widthMultiplier = 0.1f;
                lr.sharedMaterial = new Material(Shader.Find("Standard"));
                return go;
            }, 0.5f));
            cases.Add(("TextMesh", () =>
            {
                var go = new GameObject("text");
                go.transform.position = At(0.5f);
                var tm = go.AddComponent<TextMesh>();
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                tm.font = font;
                tm.text = "W";
                tm.characterSize = 0.1f;
                tm.anchor = TextAnchor.MiddleCenter;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                return go;
            }, 0.5f));

            foreach (var (label, make, depth) in cases)
            {
                SquashMeasureProbe.Log($"B1 drawing {label}");
                var go = make();
                cam.Render();
                var px = ReadAll(rt);
                // Destroy waits for the frame's end, and every case here runs inside one frame.
                DestroyImmediate(go);
                int lit = px.Count(c => c.r > 0);
                byte max = px.Length == 0 ? (byte)0 : px.Max(c => c.r);
                byte want = Expected(depth, cam.farClipPlane);
                string verdict = lit == 0 ? "NOT DRAWN" : Math.Abs(max - want) <= 1 ? "ok" : "WRONG DEPTH";
                SquashMeasureProbe.Log($"B1 {verdict}: {label}, nearness {max} want {want}, {lit} texels");
            }
            Destroy(cam.gameObject);
        }

        // The project's own avatar shaders: locked Poiyomi, lilToon, then unlocked Poiyomi last,
        // since an unlocked Poiyomi Pro has crashed the editor mid-compile before.
        static IEnumerable<(string, Material)> AvatarMaterials()
        {
            var want = new (string label, Func<string, bool> match)[]
            {
                ("locked Poiyomi", n => n.StartsWith("Hidden/Locked/") && n.ToLowerInvariant().Contains("poi")),
                ("lilToon", n => n.ToLowerInvariant().Contains("liltoon")),
                ("unlocked Poiyomi", n => n.StartsWith(".poiyomi/")),
            };
            var found = new Dictionary<string, Material>();
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || m.shader == null) continue;
                foreach (var (label, match) in want)
                    if (!found.ContainsKey(label) && match(m.shader.name)) found[label] = m;
                if (found.Count == want.Length) break;
            }
            foreach (var (label, _) in want)
            {
                if (found.TryGetValue(label, out var m)) yield return ($"{label} ({m.shader.name})", m);
                else SquashMeasureProbe.Log($"B1 no {label} material in the project");
            }
        }

        // B7: Yaw > Pitch > Squash > CounterPitch > CounterYaw, with the counters reading the same byte
        // through the parser's lerp. At s = 1 the chain must be identity.
        void B7()
        {
            var root = new GameObject("sandwich").transform;
            Transform Node(Transform parent, string n) { var t = new GameObject(n).transform; t.SetParent(parent, false); return t; }
            var yaw = Node(root, "yaw");
            var pitch = Node(yaw, "pitch");
            var squash = Node(pitch, "squash");
            var cPitch = Node(squash, "counter pitch");
            var cYaw = Node(cPitch, "counter yaw");
            var rng = new System.Random(7);
            var points = Enumerable.Range(0, 64).Select(_ => new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 0.3f).ToArray();
            double worst = 0;
            for (int i = 0; i < 20000; i++)
            {
                float ty = rng.Next(256) / 255f, tp = rng.Next(256) / 255f;
                yaw.localEulerAngles = new Vector3(0, Mathf.LerpUnclamped(-100f, 100f, ty), 0);
                pitch.localEulerAngles = new Vector3(Mathf.LerpUnclamped(-100f, 100f, tp), 0, 0);
                cPitch.localEulerAngles = new Vector3(Mathf.LerpUnclamped(100f, -100f, tp), 0, 0);
                cYaw.localEulerAngles = new Vector3(0, Mathf.LerpUnclamped(100f, -100f, ty), 0);
                foreach (var p in points) worst = Math.Max(worst, (cYaw.TransformPoint(p) - root.TransformPoint(p)).magnitude);
            }
            SquashMeasureProbe.Log($"B7 sandwich at rest, 20000 random byte pairs: worst vertex error {worst * 1000:0.00000} mm (gate 0.05 mm) {(worst < 5e-5 ? "ok" : "FAIL")}");
            Destroy(root.gameObject);
        }

        // B9: two 32x32 viewport rects in one 64x32 target. Each frame a clear blit writes 0, then the
        // cameras clear their own rect to 1/255 and draw nearness. Does a camera's clear stay in its
        // rect, and does a disabled camera's rect read 0 the next frame?
        void B9()
        {
            SquashMeasureProbe.Log($"B9 colour space {QualitySettings.activeColorSpace}");
            foreach (var r in new[] { 1f / 255f, Mathf.LinearToGammaSpace(1.5f / 255f) })
                B9With(r);
        }

        float clearR;

        void B9With(float r)
        {
            clearR = r;
            SquashMeasureProbe.Log($"B9 clear colour r {r * 255f:0.00}/255");
            var rt = Rt(64, 32, RenderTextureFormat.ARGB32);
            rt.Create();
            var clear = new Material(Make(ZeroSource));
            Camera Half(float x, Vector3 at)
            {
                var c = Cam(rt, at);
                c.rect = new Rect(x, 0, 0.5f, 1);
                c.orthographicSize = 0.5f;
                c.farClipPlane = 1f;
                // The clear colour is gamma and the project linear: 1/255 as written clears to 0.
                c.backgroundColor = new Color(clearR, 0, 0, 0);
                c.SetReplacementShader(Nearness(), "");
                return c;
            }
            var left = Half(0f, new Vector3(0, -400, 0));
            var right = Half(0.5f, new Vector3(50, -400, 0));
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.position = right.transform.position + Vector3.forward * 0.5f;
            quad.transform.localScale = Vector3.one * 0.3f;

            string Rects(Color32[] px)
            {
                string Side(int x0) { var r = Enumerable.Range(0, 32 * 32).Select(i => px[(i / 32) * 64 + x0 + i % 32].r).ToList(); return $"min {r.Min()} max {r.Max()}"; }
                return $"left {Side(0)}, right {Side(32)}";
            }
            Graphics.Blit(null, rt, clear);
            left.Render();
            right.Render();
            var both = ReadAll(rt);
            Graphics.Blit(null, rt, clear);
            left.Render();
            var leftOnly = ReadAll(rt);
            SquashMeasureProbe.Log($"B9 both cameras: {Rects(both)} (want left 1..1, right 1..surface)");
            SquashMeasureProbe.Log($"B9 right camera off the next frame: {Rects(leftOnly)} (want left 1..1, right 0..0)");
            Destroy(quad);
            Destroy(left.gameObject);
            Destroy(right.gameObject);
        }

        // B4's second half: 32 compiled setters through object, the way a parser task sets a
        // component property.
        void B4Setters()
        {
            var targets = Enumerable.Range(0, 32).Select(i => new GameObject("s" + i).transform).ToArray();
            var o = Expression.Parameter(typeof(object));
            var v = Expression.Parameter(typeof(Vector3));
            var set = Expression.Lambda<Action<object, Vector3>>(
                Expression.Assign(Expression.Property(Expression.Convert(o, typeof(Transform)), "localScale"), v), o, v).Compile();
            var times = new List<double>();
            for (int rep = 0; rep < 500; rep++)
            {
                var sw = Stopwatch.StartNew();
                foreach (var t in targets) set(t, new Vector3(1, 1, 1 + rep * 1e-4f));
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds * 1000.0 / targets.Length);
            }
            SquashMeasureProbe.Log($"B4 setter through object: median {Median(times):0.000} us per set, p90 {P(times, 0.9):0.000} us (gate 1 us)");
            foreach (var t in targets) Destroy(t.gameObject);
        }

        // B5: 20 rigs of 20 skin constraints following moving bones, measured by the profiler's
        // constraint marker, 150 frames on and 150 off.
        readonly List<IConstraint> b5Constraints = new List<IConstraint>();
        readonly List<Transform> b5Bones = new List<Transform>();
        readonly List<double> b5On = new List<double>(), b5Off = new List<double>();
        readonly List<ProfilerRecorder> b5Recorders = new List<ProfilerRecorder>();
        readonly List<string> b5Names = new List<string>();
        int b5Frames;

        void SetUpB5()
        {
            for (int r = 0; r < 20; r++)
            {
                var rig = new GameObject("rig" + r).transform;
                rig.position = new Vector3(r, -600, 0);
                Transform bone = rig, skin = rig;
                for (int i = 0; i < 20; i++)
                {
                    var b = new GameObject("bone" + i).transform; b.SetParent(bone, false); b.localPosition = Vector3.up * 0.05f; bone = b; b5Bones.Add(b);
                    var s = new GameObject("skin" + i).transform; s.SetParent(skin, false); s.localPosition = Vector3.up * 0.05f; skin = s;
                    var rc = s.gameObject.AddComponent<RotationConstraint>();
                    rc.AddSource(new ConstraintSource { sourceTransform = b, weight = 1 });
                    rc.constraintActive = true;
                    b5Constraints.Add(rc);
                    if (i % 2 == 1)
                    {
                        var pc = s.gameObject.AddComponent<PositionConstraint>();
                        pc.AddSource(new ConstraintSource { sourceTransform = b, weight = 1 });
                        pc.constraintActive = true;
                        b5Constraints.Add(pc);
                    }
                }
            }
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                // The main-thread phase only; the others nest inside it or run on workers.
                if (d.Name != "PreLateUpdate.ConstraintManagerUpdate") continue;
                b5Names.Add(d.Name);
                b5Recorders.Add(ProfilerRecorder.StartNew(d.Category, d.Name));
            }
            SquashMeasureProbe.Log($"B5 {b5Constraints.Count} constraints on 20 rigs; profiler markers: {(b5Names.Count == 0 ? "none found" : string.Join(", ", b5Names))}");
        }

        void B5Sample()
        {
            if (b5Recorders.Count == 0) { b5Frames = 300; return; }
            float a = Time.frameCount * 0.1f;
            foreach (var b in b5Bones) b.localRotation = Quaternion.Euler(Mathf.Sin(a) * 10f, 0, 0);
            double ns = b5Recorders.Sum(r => (double)r.LastValue);
            if (b5Frames < 150) b5On.Add(ns / 1000.0); else b5Off.Add(ns / 1000.0);
            b5Frames++;
            if (b5Frames == 150) foreach (var c in b5Constraints) ((Behaviour)c).enabled = false;
        }

        void B5Report()
        {
            if (b5On.Count == 0) { SquashMeasureProbe.Log("B5 no constraint marker to read"); return; }
            SquashMeasureProbe.Log($"B5 constraint markers per frame: enabled median {Median(b5On.Skip(10).ToList()):0.0} us, disabled {Median(b5Off.Skip(10).ToList()):0.0} us (gate 300 us for 20 rigs)");
            foreach (var r in b5Recorders) r.Dispose();
        }

        // B6: a LateUpdate after everything moves the chest 10 cm a frame, as IK would. At the end
        // of the frame, how far behind are a plain child follow, a ParentConstraint follow, and a
        // skin bone under the child follow?
        B6Mover b6;
        double b6Child, b6Constraint, b6Skin;
        int b6Frames;

        void SetUpB6()
        {
            var chest = new GameObject("chest").transform;
            chest.position = new Vector3(0, -700, 0);
            var part = new GameObject("part bone").transform;
            part.SetParent(chest, false);
            part.localPosition = new Vector3(0.1f, 0, 0.1f);
            var child = new GameObject("child follow").transform;
            child.SetParent(chest, false);
            child.localPosition = new Vector3(0.1f, 0, 0.1f);
            var skin = new GameObject("skin").transform;
            skin.SetParent(child, false);
            var rc = skin.gameObject.AddComponent<RotationConstraint>();
            rc.AddSource(new ConstraintSource { sourceTransform = part, weight = 1 });
            rc.constraintActive = true;
            var follow = new GameObject("constraint follow").transform;
            follow.position = part.position;
            var pc = follow.gameObject.AddComponent<ParentConstraint>();
            pc.AddSource(new ConstraintSource { sourceTransform = chest, weight = 1 });
            pc.SetTranslationOffset(0, new Vector3(0.1f, 0, 0.1f));
            pc.constraintActive = true;
            b6 = chest.gameObject.AddComponent<B6Mover>();
            b6Start = chest.position;
            StartCoroutine(B6Watch(chest, part, child, follow, skin));
        }

        IEnumerator B6Watch(Transform chest, Transform part, Transform child, Transform follow, Transform skin)
        {
            var end = new WaitForEndOfFrame();
            while (true)
            {
                yield return end;
                if (++b6Frames < 5) continue;
                b6Child = Math.Max(b6Child, (child.position - part.position).magnitude);
                b6Constraint = Math.Max(b6Constraint, (follow.position - part.position).magnitude);
                b6Skin = Math.Max(b6Skin, (skin.position - part.position).magnitude);
            }
        }

        Vector3 b6Start;

        void B6Report() => SquashMeasureProbe.Log($"B6 chest travelled {(b6.transform.position - b6Start).magnitude:0.0} m; chest moving 10 cm a frame in a late LateUpdate, {b6Frames} frames, worst error at end of frame: child follow {b6Child * 1000:0.000} mm, ParentConstraint follow {b6Constraint * 1000:0.0} mm, skin under the child {b6Skin * 1000:0.000} mm (gate: child under 0.1 mm)");

        // B11: off screen under CullUpdateTransforms, do GameObject-active and Behaviour-enabled
        // curves still apply? A transform curve beside them shows whether the animator really culled.
        readonly List<(string mode, GameObject child, Light light, Transform moved)> b11 = new List<(string, GameObject, Light, Transform)>();

        void SetUpB11()
        {
            var clip = new AnimationClip();
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("child", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 0));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("lamp", typeof(Light), "m_Enabled"), AnimationCurve.Constant(0, 1, 0));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("moved", typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Constant(0, 1, 5));
            var ctrl = new AnimatorController();
            ctrl.AddLayer("L");
            ctrl.layers[0].stateMachine.AddState("s").motion = clip;
            foreach (var mode in new[] { AnimatorCullingMode.CullUpdateTransforms, AnimatorCullingMode.CullCompletely, AnimatorCullingMode.AlwaysAnimate })
            {
                var root = new GameObject("b11 " + mode);
                root.transform.position = new Vector3(0, -900, (int)mode * 10);
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.transform.SetParent(root.transform, false);
                var child = new GameObject("child"); child.transform.SetParent(root.transform, false);
                var lamp = new GameObject("lamp"); lamp.transform.SetParent(root.transform, false);
                var light = lamp.AddComponent<Light>();
                var moved = new GameObject("moved").transform; moved.SetParent(root.transform, false);
                var anim = root.AddComponent<Animator>();
                anim.cullingMode = mode;
                anim.runtimeAnimatorController = ctrl;
                b11.Add((mode.ToString(), child, light, moved));
            }
        }

        void B11Report()
        {
            foreach (var (mode, child, light, moved) in b11)
                SquashMeasureProbe.Log($"B11 {mode}, off screen: GameObject active curve {(child.activeSelf ? "NOT applied" : "applied")}, Behaviour enabled curve {(light.enabled ? "NOT applied" : "applied")}, transform curve {(Mathf.Approximately(moved.localPosition.x, 5f) ? "applied" : "not applied (culled)")}");
        }

        const string NearnessSource = @"Shader ""Hidden/SquashMeasure/Nearness""
{
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always BlendOp Max ColorMask R
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            struct v2f { float4 pos : SV_POSITION; float z : TEXCOORD0; };
            v2f vert(float4 v : POSITION) { v2f o; o.pos = UnityObjectToClipPos(v); o.z = -UnityObjectToViewPos(v.xyz).z; return o; }
            float4 frag(v2f i) : SV_Target { return float4(lerp(2.0 / 255, 1, saturate(1 - i.z / _ProjectionParams.z)), 0, 0, 0); }
            ENDCG
        }
    }
}";

        const string DisplaceSource = @"Shader ""Hidden/SquashMeasure/Displace""
{
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            float4 vert(float4 v : POSITION) : SV_POSITION { float3 w = mul(unity_ObjectToWorld, v).xyz; w -= normalize(w - _WorldSpaceCameraPos) * 0.1; return UnityWorldToClipPos(w); }
            float4 frag() : SV_Target { return 1; }
            ENDCG
        }
    }
}";

        const string ZeroSource = @"Shader ""Hidden/SquashMeasure/Zero""
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include ""UnityCG.cginc""
            float4 frag(v2f_img i) : SV_Target { return 0; }
            ENDCG
        }
    }
}";

        void Finish()
        {
            stage = 2;
            var hist = latency.GroupBy(x => x).OrderBy(g => g.Key).Select(g => $"{g.Key} frames x{g.Count()}");
            SquashMeasureProbe.Log($"B4 parser lag, render to applied value, {latency.Count} reads: {string.Join(", ", hist)}");
            foreach (var r in dtRows) SquashMeasureProbe.Log("B12 " + r);
            foreach (var r in camRows) SquashMeasureProbe.Log("B12 " + r);
            SquashMeasureProbe.Log($"B12 {dtReads} reads: dt from the _Time.y step, worst error {dtWorst * 100:0.00}% (gate 1%); _Time.y against timeSinceLevelLoad, worst {timeWorst * 1000:0.000} ms");
            B5Report();
            B6Report();
            B11Report();
            if (inFlight) request.WaitForCompletion();
            front.Dispose();
            back.Dispose();
            SquashMeasureProbe.Log("done");
            if (Application.isBatchMode) EditorApplication.Exit(0);
            else EditorApplication.ExitPlaymode();
        }

        static double Median(List<double> v) => P(v, 0.5);
        static double P(List<double> v, double p) { var s = v.OrderBy(x => x).ToList(); return s[(int)Math.Min(s.Count - 1, p * s.Count)]; }

        const string StampSource = @"Shader ""Hidden/SquashMeasure/Stamp""
{
    SubShader
    {
        Tags { ""Queue""=""Overlay"" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            float _ProbeFrame;
            float4 vert(float4 v : POSITION, float2 uv : TEXCOORD0) : SV_POSITION { return float4(uv * 2 - 1, 0.5, 1); }
            float4 frag() : SV_Target
            {
                float f = fmod(_ProbeFrame, 65536);
                return float4(fmod(f, 256) / 255, floor(f / 256) / 255, saturate(unity_DeltaTime.x * 10), 1);
            }
            ENDCG
        }
    }
}";

        const string DtSource = @"Shader ""Hidden/SquashMeasure/Dt""
{
    Properties { _MainTex (""Prev"", 2D) = ""black"" {} }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include ""UnityCG.cginc""
            sampler2D _MainTex;
            float _Frame;
            float4 frag(v2f_img i) : SV_Target
            {
                float4 prev = tex2D(_MainTex, float2(0.0625, 0.125));
                return float4(unity_DeltaTime.x, _Time.y - prev.b, _Time.y, _Frame);
            }
            ENDCG
        }
    }
}";
    }

    // After every other LateUpdate, the way IK moves bones after constraints have run.
    [DefaultExecutionOrder(10000)]
    public class B6Mover : MonoBehaviour
    {
        void LateUpdate() => transform.position += Vector3.right * 0.1f;
    }
}
