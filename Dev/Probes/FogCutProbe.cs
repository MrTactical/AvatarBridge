#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    // Fog compiled into the pixel program only, against the patch from before it. One patched
    // material per shader family that declares multi_compile_fog is drawn twice, on its old
    // shader and on a fresh patch, with fog off and in each of the three modes, lit by a sun
    // and a point light so both forward passes run. The two must match, and fog must show.
    //
    //   -executeMethod AvatarBridge.Regression.FogCutProbe.Run [-fogOut folder]
    public static class FogCutProbe
    {
        const string Tag = "[FogCut] ";
        const int Size = 160;

        public static void Run()
        {
            int code = 0;
            try { code = Measure() ? 0 : 1; }
            catch (Exception e) { Debug.LogError(Tag + "FAIL " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        static bool Measure()
        {
            string outDir = Environment.GetCommandLineArgs().SkipWhile(a => a != "-fogOut").Skip(1).FirstOrDefault();
            ShaderUtil.allowAsyncCompilation = false;
            var picked = new Dictionary<string, Material>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || m.shader == null || !m.shader.name.Contains("YAPS")) continue;
                var original = YapsShaderPatcher.OriginalShaderOf(m);
                string path = AssetDatabase.GetAssetPath(m.shader);
                if (original == null || picked.ContainsKey(original.name) || !File.Exists(path)
                    || !File.ReadAllText(path).Contains("multi_compile_fog")) continue;
                picked[original.name] = m;
            }
            Debug.Log(Tag + $"{picked.Count} families: " + string.Join(", ", picked.Keys));

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.transform.position = new Vector3(0, 0, -3);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.gray;
            cam.renderingPath = RenderingPath.Forward;
            cam.targetTexture = new RenderTexture(Size, Size, 24);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(40, -30, 0);
            var lamp = new GameObject("Lamp").AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.renderMode = LightRenderMode.ForcePixel;
            lamp.color = new Color(0.3f, 0.6f, 1f);
            lamp.intensity = 3f;
            lamp.range = 4f;
            lamp.transform.position = new Vector3(1.2f, 0.8f, -1.2f);
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(ball.GetComponent<Collider>());
            ball.transform.localScale = Vector3.one * 1.6f;
            var draw = ball.GetComponent<MeshRenderer>();
            RenderSettings.fogColor = new Color(0.9f, 0.2f, 0.1f);
            RenderSettings.fogStartDistance = 0f;
            RenderSettings.fogEndDistance = 5f;
            RenderSettings.fogDensity = 0.35f;
            RenderSettings.ambientLight = new Color(0.2f, 0.2f, 0.2f);

            bool pass = true;
            foreach (var pair in picked)
            {
                var old = new Material(pair.Value);
                var fresh = new Material(pair.Value);
                var roles = YapsShaderPatcher.RolesOf(old.shader);
                var shader = YapsShaderPatcher.WithRoles(fresh, roles, null, out string why);
                if (shader == null || ShaderUtil.ShaderHasError(shader))
                {
                    Debug.Log(Tag + $"FAIL {pair.Key}: no fresh patch ({why ?? "shader error"})");
                    pass = false;
                    continue;
                }
                YapsShaderPatcher.Wear(fresh, shader);
                string text = File.ReadAllText(AssetDatabase.GetAssetPath(shader));
                int cut = text.Split(new[] { "multi_compile_fragment _ FOG_LINEAR FOG_EXP FOG_EXP2" }, StringSplitOptions.None).Length - 1;
                int kept = text.Split(new[] { "multi_compile_fog" }, StringSplitOptions.None).Length - 1;

                var line = new List<string>();
                Color32[] plain = null;
                bool same = true, fogShows = true;
                foreach (var mode in new FogMode?[] { null, FogMode.Linear, FogMode.Exponential, FogMode.ExponentialSquared })
                {
                    RenderSettings.fog = mode != null;
                    if (mode != null) RenderSettings.fogMode = mode.Value;
                    draw.sharedMaterial = old;
                    var a = Grab(cam);
                    draw.sharedMaterial = fresh;
                    var b = Grab(cam);
                    if (outDir != null) Save(b, Path.Combine(outDir, $"{Safe(pair.Key)}_{(mode?.ToString() ?? "Off")}.png"));
                    int worst = Worst(a, b);
                    if (mode == null) plain = b;
                    double shift = mode == null ? 0 : Mean(plain, b);
                    line.Add($"{mode?.ToString() ?? "Off"} diff {worst}" + (mode == null ? "" : $" fog {shift:F1}"));
                    if (worst > 2) same = false;
                    if (mode != null && shift < 1.0) fogShows = false;
                }
                bool ok = same && fogShows;
                pass &= ok;
                Debug.Log(Tag + $"{(ok ? "ok  " : "FAIL")} {pair.Key} [{shader.name}] cut {cut} kept {kept}: " + string.Join(", ", line));
            }
            Debug.Log(Tag + (pass ? "PASS" : "FAIL"));
            return pass;
        }

        static Color32[] Grab(Camera cam)
        {
            cam.Render();
            var was = RenderTexture.active;
            RenderTexture.active = cam.targetTexture;
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            RenderTexture.active = was;
            var px = t.GetPixels32();
            Object.DestroyImmediate(t);
            return px;
        }

        static int Worst(Color32[] a, Color32[] b)
        {
            int worst = 0;
            for (int i = 0; i < a.Length; i++)
                worst = Math.Max(worst, Math.Max(Math.Abs(a[i].r - b[i].r), Math.Max(Math.Abs(a[i].g - b[i].g), Math.Abs(a[i].b - b[i].b))));
            return worst;
        }

        static double Mean(Color32[] a, Color32[] b)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
            return sum / (3.0 * a.Length);
        }

        static void Save(Color32[] px, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            t.SetPixels32(px);
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
        }

        static string Safe(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
    }
}
#endif
