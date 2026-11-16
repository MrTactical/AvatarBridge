using System;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    // What a GrabPass costs one camera: the screen copied once for a named grab however many
    // objects read it, once per object for an unnamed one. In game every camera grabs again
    // (the eyes' frame, the portrait, the CVR camera, every mirror), so the figure is per camera.
    // A heavy quad keeps the GPU the slower side, or the copy would hide behind the editor's own
    // cost per render. Each reader has its own material, so none are batched into one draw.
    //
    // Run: -executeMethod AvatarBridge.Regression.GrabPassPerf.Run
    public static class GrabPassPerf
    {
        const int Layer = 31, Frames = 20, Readers = 8;

        const string LoadShader = @"Shader ""Hidden/GrabPassPerfLoad""
{
    Properties { _Work (""Work"", Float) = 64 }
    SubShader
    {
        Tags { ""Queue"" = ""Geometry"" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            float _Work;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
            float4 frag(v2f i) : SV_Target
            {
                float x = i.uv.x, y = i.uv.y;
                [loop] for (int k = 0; k < (int) _Work; k++) { x = frac(sin(x * 12.9898 + y) * 43758.5453); y = x + y * 0.5; }
                return float4(x, frac(y), 0, 1);
            }
            ENDCG
        }
    }
}";

        static string Reader(string name, string grab, string tex) => $@"Shader ""Hidden/GrabPassPerf{name}""
{{
    SubShader
    {{
        Tags {{ ""Queue"" = ""Transparent"" }}
        GrabPass {{ {grab} }}
        Pass
        {{
            ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            sampler2D {tex};
            struct v2f {{ float4 pos : SV_POSITION; float4 grab : TEXCOORD0; }};
            v2f vert(float4 v : POSITION) {{ v2f o; o.pos = UnityObjectToClipPos(v); o.grab = ComputeGrabScreenPos(o.pos); return o; }}
            float4 frag(v2f i) : SV_Target {{ return tex2Dproj({tex}, i.grab) * 0.5 + 0.25; }}
            ENDCG
        }}
    }}
}}";

        public static void Run()
        {
            void Log(string s) => Debug.Log("[GrabPassPerf] " + s);
            var made = new System.Collections.Generic.List<Object>();
            T Keep<T>(T o) where T : Object { made.Add(o); return o; }
            try
            {
                var load = Keep(ShaderUtil.CreateShaderAsset(LoadShader, true));
                var named = Keep(ShaderUtil.CreateShaderAsset(Reader("Named", @"""_GrabPassPerf""", "_GrabPassPerf"), true));
                var plain = Keep(ShaderUtil.CreateShaderAsset(Reader("Plain", "", "_GrabTexture"), true));
                foreach (var s in new[] { load, named, plain })
                    if (s == null || !s.isSupported || ShaderUtil.ShaderHasError(s)) { Log($"FAIL {(s != null ? s.name : "a shader")} did not compile"); return; }

                var cam = Keep(new GameObject("__GrabPassPerfCamera")).AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 1f;
                cam.transform.position = new Vector3(0f, 0f, -10f);
                cam.cullingMask = 1 << Layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.allowHDR = true;
                cam.allowMSAA = false;

                GameObject Quad(string name, Shader shader, Vector3 at, Vector3 size)
                {
                    var q = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));
                    Object.DestroyImmediate(q.GetComponent<Collider>());
                    q.name = name;
                    q.layer = Layer;
                    q.transform.position = at;
                    q.transform.localScale = size;
                    q.GetComponent<MeshRenderer>().sharedMaterial = Keep(new Material(shader));
                    return q;
                }
                var heavy = Quad("__GrabPassPerfLoad", load, Vector3.zero, new Vector3(8f, 2f, 1f));
                var namedReaders = Enumerable.Range(0, Readers).Select(i => Quad("__Named", named, new Vector3(-0.8f + 0.2f * i, 0.5f, -1f), Vector3.one * 0.02f)).ToArray();
                var plainReaders = Enumerable.Range(0, Readers).Select(i => Quad("__Plain", plain, new Vector3(-0.8f + 0.2f * i, -0.5f, -1f), Vector3.one * 0.02f)).ToArray();
                var sync = Keep(new Texture2D(1, 1, TextureFormat.RGBAHalf, false));

                void Show(int namedOn, int plainOn)
                {
                    for (int i = 0; i < Readers; i++) { namedReaders[i].SetActive(i < namedOn); plainReaders[i].SetActive(i < plainOn); }
                }
                double Time(RenderTexture target)
                {
                    double best = double.MaxValue;
                    for (int trial = 0; trial < 8; trial++)
                    {
                        var watch = Stopwatch.StartNew();
                        for (int f = 0; f < Frames; f++) cam.Render();
                        // A readback waits for the GPU to finish everything before it.
                        RenderTexture.active = target;
                        sync.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                        RenderTexture.active = null;
                        if (trial > 0) best = Math.Min(best, watch.Elapsed.TotalMilliseconds / Frames);
                    }
                    return best;
                }

                Log($"{SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}, {Frames} renders a trial, best of 7");
                // Both eyes of a VR headset as one copy (single-pass instanced grabs both slices), a
                // 1440p and a 1080p desktop.
                foreach (var (w, h, what) in new[] { (4032, 2240, "VR, both eyes"), (2560, 1440, "1440p"), (1920, 1080, "1080p") })
                {
                    var target = Keep(new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf));
                    cam.targetTexture = target;
                    cam.aspect = (float) w / h;
                    var work = heavy.GetComponent<MeshRenderer>().sharedMaterial;
                    Show(0, 0);
                    work.SetFloat("_Work", 0f);
                    double floor = Time(target);
                    float n = 16f;
                    double baseline;
                    for (;;)
                    {
                        work.SetFloat("_Work", n);
                        baseline = Time(target);
                        if (baseline > Math.Max(4.0, 3.0 * floor) || n >= 65536f) break;
                        n *= 2f;
                    }
                    Show(1, 0); double named1 = Time(target) - baseline;
                    Show(Readers, 0); double namedAll = Time(target) - baseline;
                    Show(0, 1); double plain1 = Time(target) - baseline;
                    Show(0, Readers); double plainAll = Time(target) - baseline;
                    Show(0, 0); double again = Time(target) - baseline;
                    Log($"{what} {w}x{h} ARGBHalf: empty render {floor:0.000} ms, GPU held at {baseline:0.000} ms (work {n}); " +
                        $"named grab, 1 reader +{named1:0.000} ms, {Readers} readers +{namedAll:0.000} ms; " +
                        $"unnamed grab, 1 reader +{plain1:0.000} ms, {Readers} readers +{plainAll:0.000} ms; baseline again {again:+0.000;-0.000} ms (noise)");
                    cam.targetTexture = null;
                }
                Log("done");
            }
            catch (Exception e) { Log("FAIL " + e); }
            finally
            {
                foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
        }
    }
}
