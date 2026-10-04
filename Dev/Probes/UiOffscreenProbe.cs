// Draws AvatarBridge windows into PNGs from a batch Unity, so a layout change can be looked at
// while the machine's screen is locked or off. Each window builds its UI (CreateGUI) into an
// editor panel made here, carrying the editor's own dark or light theme, and the panel is
// repainted into a render texture. Reading the screen needs an unlocked, drawing display; the
// GPU does not.
//
// Run: Unity.exe -batchmode -projectPath <p> -executeMethod AvatarBridge.Regression.UiOffscreenProbe.Run
//        -captureOut <dir> [-captureScene <Assets/...unity>] [-capturePrefab <Assets/...prefab>]
//        [-captureWindows converter,toolkit,tester,yaps] [-captureHeight 2400]
// Explore() lists the internal panel API, for when a Unity upgrade moves it.
#if CVR_CCK_EXISTS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge.Regression
{
    public static class UiOffscreenProbe
    {
        class Owner : ScriptableObject { }

        static readonly (string key, string type, bool converted)[] Windows =
        {
            ("converter", "AvatarBridge.AvatarBridgeWindow", false),
            ("toolkit", "AvatarBridge.ToolkitWindow", true),
            ("tester", "AvatarBridge.CckAnimatorTester", true),
            ("yaps", "AvatarBridge.YapsSetupWindow", true),
        };

        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            string outDir = Arg("-captureOut") ?? Path.Combine(Path.GetTempPath(), "ab-ui");
            Directory.CreateDirectory(outDir);
            int width = 560;
            int height = int.TryParse(Arg("-captureHeight"), out int h) ? h : 2400;
            var only = (Arg("-captureWindows") ?? "").Split(',').Where(s => s.Length > 0).ToArray();
            int failed = 0;
            try
            {
                string scene = Arg("-captureScene");
                if (!string.IsNullOrEmpty(scene)) EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
                GameObject source = FindSource(), converted = null;
                string prefab = Arg("-capturePrefab");
                if (!string.IsNullOrEmpty(prefab))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
                    if (asset != null) converted = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                }
                if (converted == null) converted = UnityEngine.Object.FindObjectOfType<CVRAvatar>()?.gameObject;
                Log($"source {(source != null ? source.name : "none")}, converted {(converted != null ? converted.name : "none")}");

                foreach (var (key, typeName, wantsConverted) in Windows)
                {
                    if (only.Length > 0 && !only.Contains(key)) continue;
                    var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName, false)).FirstOrDefault(t => t != null);
                    if (type == null) { Log($"skip {key}: not in this project"); continue; }
                    var target = wantsConverted ? converted : source;
                    if (target != null) Selection.activeGameObject = target;
                    foreach (bool light in new[] { false, true })
                    {
                        try
                        {
                            Render(type, Path.Combine(outDir, $"{key}-{(light ? "light" : "dark")}.png"), width, height, light);
                        }
                        catch (Exception e)
                        {
                            failed++;
                            Debug.LogException(e);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                failed++;
                Debug.LogException(e);
            }
            Log(failed == 0 ? "done" : $"done with {failed} failure(s)");
            EditorApplication.Exit(failed == 0 ? 0 : 1);
        }

        static void Render(Type windowType, string file, int width, int height, bool light)
        {
            var owner = ScriptableObject.CreateInstance<Owner>();
            var utility = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsUtility");
            var panel = utility.GetMethod("FindOrCreateEditorPanel", Any).Invoke(null, new object[] { owner });
            var root = (VisualElement)panel.GetType().GetProperty("visualTree", Any).GetValue(panel);

            // The editor's own theme, dark or light, without switching the editor's skin.
            var editorUtility = typeof(UnityEditor.UIElements.ObjectField).Assembly.GetType("UnityEditor.UIElements.UIElementsEditorUtility");
            root.styleSheets.Clear();
            var theme = (StyleSheet)editorUtility.GetMethod(light ? "GetCommonLightStyleSheet" : "GetCommonDarkStyleSheet", Any).Invoke(null, null);
            root.styleSheets.Add(theme);
            root.style.width = width;
            root.style.height = height;
            root.style.backgroundColor = light ? new Color(0.784f, 0.784f, 0.784f) : new Color(0.22f, 0.22f, 0.22f);

            // The window builds into its own rootVisualElement in CreateGUI; that element is
            // moved into this panel, which is what a docked window's host does.
            var window = (EditorWindow)ScriptableObject.CreateInstance(windowType);
            var content = window.rootVisualElement;
            content.style.flexGrow = 1;
            // A window's root is given the editor's current skin sheet when it is made, and an
            // ancestor's variables win over the panel's, so the swap is made there too.
            var other = (StyleSheet)editorUtility.GetMethod(light ? "GetCommonDarkStyleSheet" : "GetCommonLightStyleSheet", Any).Invoke(null, null);
            if (content.styleSheets.Contains(other))
            {
                content.styleSheets.Remove(other);
                content.styleSheets.Add(theme);
            }
            root.Add(content);
            windowType.GetMethod("CreateGUI", Any)?.Invoke(window, null);
            // The window themes itself from EditorGUIUtility.isProSkin; follow the capture instead.
            foreach (var e in content.Query(className: light ? "dark" : "light").ToList().Prepend(content))
            {
                if (e.ClassListContains("dark") || e.ClassListContains("light"))
                {
                    e.EnableInClassList("dark", !light);
                    e.EnableInClassList("light", light);
                }
            }

            var update = panel.GetType().GetMethod("UpdateForRepaint", Any) ?? panel.GetType().GetMethod("UpdateWithoutRepaint", Any);
            for (int i = 0; i < 4; i++) update?.Invoke(panel, null);

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Viewport(new Rect(0, 0, width, height));
            GL.Clear(true, true, light ? new Color(0.784f, 0.784f, 0.784f) : new Color(0.22f, 0.22f, 0.22f));
            panel.GetType().GetMethod("Repaint", Any, null, new[] { typeof(Event) }, null)
                .Invoke(panel, new object[] { new Event { type = EventType.Repaint } });
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = was;
            rt.Release();

            // Trim the empty tail so a short window is not a tall grey page.
            float used = content.layout.height > 0 ? content.contentRect.height : height;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Log($"wrote {file} ({width}x{height}, content {used:0} tall)");
            UnityEngine.Object.DestroyImmediate(window);
            UnityEngine.Object.DestroyImmediate(owner);
        }

        static GameObject FindSource()
        {
            var descriptor = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor", false)).FirstOrDefault(t => t != null);
            if (descriptor == null) return null;
            return UnityEngine.Object.FindObjectsOfType(descriptor).OfType<Component>().FirstOrDefault()?.gameObject;
        }

        public static void Explore()
        {
            var flags = Any | BindingFlags.DeclaredOnly;
            foreach (var name in new[] { "UnityEngine.UIElements.UIElementsUtility", "UnityEngine.UIElements.Panel", "UnityEngine.UIElements.BaseVisualElementPanel" })
            {
                var t = typeof(VisualElement).Assembly.GetType(name);
                Log($"{name}: {(t == null ? "missing" : "")}");
                if (t == null) continue;
                foreach (var m in t.GetMethods(flags).Where(m => m.Name.Contains("Panel") || m.Name.Contains("Repaint") || m.Name.Contains("Render") || m.Name.Contains("Update")))
                    Log($"  {(m.IsStatic ? "static " : "")}{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})");
            }
            EditorApplication.Exit(0);
        }

        static void Log(string s) => Debug.Log("[UiOffscreen] " + s);
    }
}
#endif
