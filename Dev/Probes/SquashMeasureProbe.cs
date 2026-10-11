using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace AvatarBridge.Regression
{
    // Measurement gates for the squash architecture (Squash architecture.md, B2 B3 B4 B8 B12).
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
        readonly List<string> dtRows = new List<string>();

        Camera stampCam;
        RenderTexture stampRt;
        NativeArray<Color32> front, back;
        AsyncGPUReadbackRequest request;
        bool inFlight;

        Material dtMat;
        RenderTexture[] state = new RenderTexture[2];
        int ping;
        float dtSum;
        int dtReads;
        double dtWorst, sumWorst;

        void Start()
        {
            SquashMeasureProbe.Log($"device {SystemInfo.graphicsDeviceType}, async readback {SystemInfo.supportsAsyncGPUReadback}, RGBAHalf {SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)}");
            B8();
            B3();
            B2();
            SetUpStamp();
            SetUpDt();
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

        // B12: does a blit in LateUpdate see unity_DeltaTime, and does RGBAHalf carry a running sum
        // through ping-pong self-feedback.
        void SetUpDt()
        {
            dtMat = new Material(Make(DtSource));
            for (int i = 0; i < 2; i++) { state[i] = Rt(8, 4, RenderTextureFormat.ARGBHalf); state[i].Create(); }
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
            }
            if (!inFlight)
            {
                request = AsyncGPUReadback.RequestIntoNativeArray(ref back, stampRt, 0, TextureFormat.RGBA32);
                inFlight = true;
            }
            if (latency.Count >= 300 && dtReads >= 300) Finish();
        }

        void LateUpdate()
        {
            if (stage != 1) return;
            stampCam.Render();

            var src = state[ping];
            var dst = state[1 - ping];
            dtMat.SetFloat("_Frame", Time.frameCount % 1024);
            Graphics.Blit(src, dst, dtMat);
            ping = 1 - ping;
            dtSum += Time.deltaTime;
            float expectSum = dtSum;
            AsyncGPUReadback.Request(dst, 0, r =>
            {
                if (r.hasError) return;
                var px = r.GetData<ushort>();
                float dt = Mathf.HalfToFloat(px[0]), sum = Mathf.HalfToFloat(px[1]);
                int frame = Mathf.RoundToInt(Mathf.HalfToFloat(px[2]));
                float truth = dtByFrame.Where(kv => kv.Key % 1024 == frame).OrderByDescending(kv => kv.Key).Select(kv => kv.Value).FirstOrDefault();
                if (truth > 0) dtWorst = Math.Max(dtWorst, Math.Abs(dt - truth) / truth);
                sumWorst = Math.Max(sumWorst, Math.Abs(sum - expectSum));
                if (dtRows.Count < 5) dtRows.Add($"frame {frame}: blit dt {dt * 1000:0.000} ms, Time.deltaTime {truth * 1000:0.000} ms, sum {sum:0.000} s vs {expectSum:0.000} s");
                dtReads++;
            });
        }

        void Finish()
        {
            stage = 2;
            var hist = latency.GroupBy(x => x).OrderBy(g => g.Key).Select(g => $"{g.Key} frames x{g.Count()}");
            SquashMeasureProbe.Log($"B4 parser lag, render to applied value, {latency.Count} reads: {string.Join(", ", hist)}");
            foreach (var r in dtRows) SquashMeasureProbe.Log("B12 " + r);
            SquashMeasureProbe.Log($"B12 {dtReads} reads: worst dt error {dtWorst * 100:0.00}% (gate 1%), worst running-sum drift {sumWorst * 1000:0.000} ms over {dtSum:0.0} s (half precision)");
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
            float _ProbeFrame;
            float4 vert(float4 v : POSITION, float2 uv : TEXCOORD0) : SV_POSITION { return float4(uv * 2 - 1, 0.5, 1); }
            float4 frag() : SV_Target
            {
                float f = fmod(_ProbeFrame, 65536);
                return float4(fmod(f, 256) / 255, floor(f / 256) / 255, 0, 1);
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
                return float4(unity_DeltaTime.x, prev.g + unity_DeltaTime.x, _Frame, 1);
            }
            ENDCG
        }
    }
}";
    }
}
