#if CVR_CCK_EXISTS && UNITY_EDITOR && AVATARBRIDGE_YAPS
// What YAPS does to a Poiyomi material: which Poiyomi version each step carries, what Thry's
// Unlock puts back, and whether the material panel draws.
//
//   -executeMethod AvatarBridge.Regression.PoiyomiYapsProbe.Run [-poiShaders <path>,<path>] [-captureOut <dir>]
//
// Two installs of Poiyomi share shader names (".poiyomi/Poiyomi Toon" in two version folders),
// so anything that finds a shader by name can land on the other one. Per source shader file:
// the version label of the source, the locked stand-in, the patch, and what Unlock restores.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    public static class PoiyomiYapsProbe
    {
        const string Tag = "[PoiYaps] ";
        const string Dir = "Assets/PoiyomiYapsProbe";

        static void Log(string s) => Debug.Log(Tag + s);

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string n) { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            var paths = (Arg("-poiShaders") ?? "Assets/_PoiyomiShaders/Shaders/10.0/Toon/Poiyomi Toon.shader,Assets/_PoiyomiShaders/Shaders/9.3/Toon/Poiyomi Toon.shader")
                .Split(',');
            string outDir = Arg("-captureOut");
            int code = 0;
            try
            {
                Directory.CreateDirectory(Dir);
                AssetDatabase.Refresh();
                Log($"Shader.Find(\".poiyomi/Poiyomi Toon\") -> {Where(Shader.Find(".poiyomi/Poiyomi Toon"))}");
                foreach (string path in paths)
                {
                    var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                    if (shader == null) { Log("no shader at " + path); code = 1; continue; }
                    string stem = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)));
                    var source = new Material(shader) { name = "Poi " + stem };
                    AssetDatabase.CreateAsset(source, $"{Dir}/Poi {stem}.mat");
                    Log($"--- {path}: source {Where(source.shader)}");

                    var patched = YapsShaderPatcher.Patch(source, null, out string refusal, out _);
                    var basis = YapsLock.Basis(source);
                    Log($"  stand-in {(basis == source ? "none (patched unlocked)" : Where(basis.shader))}");
                    if (patched == null) { Log("  REFUSED " + refusal); code = 1; continue; }
                    Log($"  patch {Where(patched)}; source recorded \"{YapsShaderPatcher.SourceShaderOf(new Material(patched))}\"");
                    var copy = new Material(basis) { shader = patched, name = source.name + " YAPS" };
                    YapsLock.Stamp(copy, source);
                    AssetDatabase.CreateAsset(copy, $"{Dir}/Poi {stem} YAPS.mat");
                    string guid = copy.GetTag("OriginalShaderGUID", false, "");
                    Log($"  copy tags: OriginalShader \"{copy.GetTag("OriginalShader", false, "")}\", GUID -> {AssetDatabase.GUIDToAssetPath(guid)}");
                    Log($"  Thry reads the copy as locked: {YapsLock.ThryLocked(copy.shader)}");

                    if (outDir != null) Panel(copy, Path.Combine(outDir, $"poi-{stem}-yaps.png"));
                    if (outDir != null) Panel(source, Path.Combine(outDir, $"poi-{stem}-source.png"));

                    // What a user pressing Unlock in Poiyomi's panel gets back.
                    var unlock = Unlock();
                    if (unlock == null) Log("  no Thry unlock to call");
                    else
                    {
                        unlock(copy);
                        Log($"  after Thry Unlock: {Where(copy.shader)}");
                    }
                }
            }
            catch (Exception e) { Debug.LogException(e); code = 2; }
            finally { AssetDatabase.DeleteAsset(Dir); }
            Log(code == 0 ? "done" : "done with problems");
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static string Where(Shader s)
        {
            if (s == null) return "null";
            int i = s.FindPropertyIndex("shader_master_label");
            string label = i >= 0 ? System.Text.RegularExpressions.Regex.Replace(s.GetPropertyDescription(i), "<[^>]+>", "") : "no label";
            return $"\"{s.name}\" [{label}] at {AssetDatabase.GetAssetPath(s)}";
        }

        static Action<Material> Unlock()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Thry.ThryEditor.ShaderOptimizer", false) ?? a.GetType("Thry.ShaderOptimizer", false))
                .FirstOrDefault(t => t != null);
            var m = type?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(x => x.Name == "SetLockedForAllMaterials" && x.GetParameters().Length >= 2);
            if (m == null) return null;
            var ps = m.GetParameters();
            return mat =>
            {
                var a = new object[ps.Length];
                a[0] = new[] { mat }.ToList();
                a[1] = 0;
                for (int i = 2; i < ps.Length; i++)
                    a[i] = ps[i].ParameterType == typeof(bool) ? (object) false : ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                m.Invoke(null, a);
            };
        }

        // The material panel drawn offscreen by the UI probe; an exception from the panel shows in the log.
        static void Panel(Material m, string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            UiOffscreenProbe.Inspector.Target = m;
            var render = typeof(UiOffscreenProbe).GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Static);
            try
            {
                render.Invoke(null, new object[] { typeof(UiOffscreenProbe.Inspector), file, 560, 2400, false, null });
                Log($"  panel drawn: {file}");
            }
            catch (TargetInvocationException e) { Log($"  panel THREW: {e.InnerException}"); }
        }
    }
}
#endif
