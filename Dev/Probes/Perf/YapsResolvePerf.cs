using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace AvatarBridge.Regression
{
    // The atlas read's cost per vertex, one read against two. A plug of 1.28 m
    // or more reads the top level once; a 0.649 m plug reads two. Same scan,
    // same taps per read, so the difference is the second read. An empty atlas
    // costs one header tap a cell; a full one (every header counting a socket
    // whose tag never matches) opens all eight buckets in both homes, the
    // worst any cell can cost. Every vertex asks from one root, as every
    // vertex of one plug does, so the texture cache is as warm as in game.
    //
    // Run: -executeMethod AvatarBridge.Regression.YapsResolvePerf.Run
    public static class YapsResolvePerf
    {
        public static void Run()
        {
            void Log(string s) => Debug.Log("[YapsResolvePerf] " + s);
            var shader = Shader.Find("Hidden/YapsResolvePerf");
            if (shader == null || !shader.isSupported) { Log("FAIL Hidden/YapsResolvePerf did not compile"); return; }
            var mat = new Material(shader);
            var atlas = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGBHalf);
            var target = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGBHalf);
            var go = new GameObject("__PerfCamera");
            var cam = go.AddComponent<Camera>();
            cam.targetTexture = target;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.cullingMask = 0;
            Shader.SetGlobalTexture("_YAPS_Atlas", atlas);
            Shader.SetGlobalVector("_YAPS_Atlas_TexelSize", new Vector4(1f / 1024, 1f / 1024, 1024, 1024));
            var sync = new Texture2D(1, 1, TextureFormat.RGBAHalf, false);
            const int vertices = 1 << 20, draws = 16;
            try
            {
                double Time(int pass, float length, float fill)
                {
                    RenderTexture.active = atlas;
                    GL.Clear(false, true, new Color(0, 0, 0, fill));
                    RenderTexture.active = null;
                    mat.SetVector("_PerfRoot", new Vector3(0.3f, 1.0f, 0.2f));
                    mat.SetVector("_PerfAxis", Vector3.forward);
                    mat.SetFloat("_PerfLength", length);
                    var cb = new CommandBuffer();
                    for (int i = 0; i < draws; i++) cb.DrawProcedural(Matrix4x4.identity, mat, pass, MeshTopology.Points, vertices);
                    cam.AddCommandBuffer(CameraEvent.AfterEverything, cb);
                    double best = double.MaxValue;
                    for (int trial = 0; trial < 9; trial++)
                    {
                        var watch = Stopwatch.StartNew();
                        cam.Render();
                        // A readback waits for the GPU to finish everything before it.
                        RenderTexture.active = target;
                        sync.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                        RenderTexture.active = null;
                        if (trial > 0) best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
                    }
                    cam.RemoveCommandBuffer(CameraEvent.AfterEverything, cb);
                    cb.Release();
                    return best * 1e6 / ((double) draws * vertices);   // ns per vertex
                }

                Log($"GPU {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}");
                double floor = Time(1, 0.649f, 0f);
                Log($"floor {floor:0.00} ns a vertex");
                foreach (var (label, fill) in new[] { ("empty atlas", 0f), ("full atlas", 1f / 255f) })
                {
                    double one = Time(0, 1.5f, fill) - floor;
                    double two = Time(0, 0.649f, fill) - floor;
                    Log($"{label}: one read {one:0.00} ns a vertex, two reads {two:0.00} ns, " +
                        $"{two / Math.Max(one, 1e-6):0.00}x; a 6433-vertex plug costs {two * 6433 / 1000:0.0} us a pass " +
                        $"({(two - one) * 6433 / 1000:0.0} us of it the second read)");
                }
            }
            finally
            {
                cam.targetTexture = null;
                target.Release();
                atlas.Release();
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
