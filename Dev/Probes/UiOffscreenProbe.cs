// Draws AvatarBridge windows into PNGs from a batch Unity, so a layout change can be looked at
// while the machine's screen is locked or off. Each window builds its UI (CreateGUI) into an
// editor panel made here, carrying the editor's own dark or light theme, and the panel is
// repainted into a render texture. Reading the screen needs an unlocked, drawing display; the
// GPU does not.
//
// Run: Unity.exe -batchmode -projectPath <p> -executeMethod AvatarBridge.Regression.UiOffscreenProbe.Run
//        -captureOut <dir> [-captureScene <Assets/...unity>] [-capturePrefab <Assets/...prefab>]
//        [-captureWindows converter,toolkit,tester,yaps] [-captureHeight 2600] [-capturePlay]
// Each render is cropped to its content and cut into tiles about 1000 px tall, so a long
// window reads at full size. -capturePlay then enters Play mode and renders the tester live.
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
    [InitializeOnLoad]
    public static class UiOffscreenProbe
    {
        class Owner : ScriptableObject { }

        const string PlayKey = "UiOffscreenProbe.play";

        static UiOffscreenProbe()
        {
            string pending = SessionState.GetString(PlayKey, "");
            if (pending.Length > 0) EditorApplication.delayCall += () => ResumeInPlay(pending);
        }

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
                    foreach (var (suffix, prepare) in Passes(key, type, source, converted))
                    {
                        foreach (bool light in new[] { false, true })
                        {
                            try
                            {
                                Render(type, Path.Combine(outDir, $"{key}{suffix}-{(light ? "light" : "dark")}.png"), width, height, light, prepare);
                            }
                            catch (Exception e)
                            {
                                failed++;
                                Debug.LogException(e);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                failed++;
                Debug.LogException(e);
            }
            if (Array.IndexOf(args, "-capturePlay") >= 0 && failed == 0)
            {
                SessionState.SetString(PlayKey, $"{outDir}|{width}|{height}");
                EditorApplication.EnterPlaymode();
                // A project with domain reload off for Play mode never reruns the static
                // constructor, so the resume is also queued from here; whichever runs first wins.
                EditorApplication.delayCall += () =>
                {
                    string pending = SessionState.GetString(PlayKey, "");
                    if (pending.Length > 0) ResumeInPlay(pending);
                };
                return;
            }
            Log(failed == 0 ? "done" : $"done with {failed} failure(s)");
            EditorApplication.Exit(failed == 0 ? 0 : 1);
        }

        // After the domain reload into Play: a few seconds for cloth to build, then the tester
        // live, both skins.
        static void ResumeInPlay(string pending)
        {
            SessionState.EraseString(PlayKey);
            var parts = pending.Split('|');
            double start = EditorApplication.timeSinceStartup;
            void Step()
            {
                if (!EditorApplication.isPlaying && EditorApplication.timeSinceStartup - start > 180.0)
                {
                    Log("Play mode never started; giving up");
                    EditorApplication.Exit(1);
                    return;
                }
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - start < 4.0) return;
                EditorApplication.update -= Step;
                int failed = 0;
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("AvatarBridge.CckAnimatorTester", false)).FirstOrDefault(t => t != null);
                var avatar = UnityEngine.Object.FindObjectsOfType<CVRAvatar>().FirstOrDefault();
                if (avatar != null) Selection.activeGameObject = avatar.gameObject;
                void Pass(string suffix)
                {
                    foreach (bool light in new[] { false, true })
                    {
                        try { Render(type, Path.Combine(parts[0], $"tester-play{suffix}-{(light ? "light" : "dark")}.png"), int.Parse(parts[1]), int.Parse(parts[2]), light); }
                        catch (Exception e) { failed++; Debug.LogException(e); }
                    }
                }
                // The layers card remembers being collapsed, which would hide its table every run.
                const string layersKey = "AvatarBridge.Card.Tester.Layers";
                bool wasOpen = EditorPrefs.GetBool(layersKey, false);
                EditorPrefs.SetBool(layersKey, true);
                try
                {
                    Pass("");
                    // The avatar root carries no chain, so the selected-chain panel needs one picked.
                    var chains = avatar != null ? type.GetMethod("Chains", Any)?.Invoke(null, new object[] { avatar }) as System.Collections.Generic.List<Component> : null;
                    var chain = chains?.FirstOrDefault();
                    if (chain != null)
                    {
                        Selection.activeGameObject = chain.gameObject;
                        Pass("-selected");
                    }
                    else Log("no chain under the avatar; selected pass skipped");
                }
                finally { EditorPrefs.SetBool(layersKey, wasOpen); }
                Log(failed == 0 ? "done" : $"done with {failed} failure(s)");
                EditorApplication.Exit(failed == 0 ? 0 : 1);
            }
            EditorApplication.update += Step;
        }

        // The converter ignores Selection, so the empty window is followed by one holding the
        // source avatar, one analysed, and one holding a report: the states a picked avatar
        // unlocks are the ones most likely to break.
        static (string suffix, Action<EditorWindow> prepare)[] Passes(string key, Type type, GameObject source, GameObject converted)
        {
            var empty = new (string, Action<EditorWindow>)[] { ("", null) };
            var field = key == "converter" ? type.GetField("avatar", Any) : null;
            var descriptor = field != null && source != null ? source.GetComponent(field.FieldType) : null;
            // The toolkit reads its kept target in CreateGUI. Check is pressed on the built window,
            // so the summary, the accent stripe and the report list get drawn too.
            if (key == "toolkit" && converted != null)
            {
                void Target(EditorWindow w) => type.GetField("target", Any).SetValue(w, converted);
                return empty.Concat(new (string, Action<EditorWindow>)[]
                {
                    ("-picked", Target),
                    ("-checked", w =>
                    {
                        Target(w);
                        type.GetMethod("CreateGUI", Any).Invoke(w, null);
                        var check = w.rootVisualElement.Query<Button>().Where(b => b.text == "Check").First();
                        ((Action)typeof(Clickable).GetField("clicked", Any).GetValue(check.clickable))();
                    }),
                }).ToArray();
            }
            if (descriptor == null) return empty;
            void Pick(EditorWindow w) => field.SetValue(w, descriptor);
            return empty.Concat(new (string, Action<EditorWindow>)[]
            {
                ("-picked", Pick),
                ("-analysed", w => { Pick(w); type.GetMethod("Reanalyse", Any).Invoke(w, null); }),
                ("-report", w => { Pick(w); type.GetField("lastReport", Any).SetValue(w, SampleReport(converted)); }),
            }).ToArray();
        }

        // Made up, with rows for every status, so each chip, the list, the reclaimed notice and
        // every Next row have something to draw. Subjects name the converted avatar's own
        // objects where there is one, so rows get their Show button.
        static BridgeReport SampleReport(GameObject converted)
        {
            var report = new BridgeReport
            {
                ConvertedRoot = converted,
                SavedReportPath = "Assets/AvatarBridgeOutput/Sample/Report.md",
                SavedHtmlPath = "Assets/AvatarBridgeOutput/Sample/Report.html",
                StoreDescription = "Sample",
                BytesReclaimed = 37L * 1048576,
            };
            var names = converted != null
                ? converted.GetComponentsInChildren<Transform>(true).Skip(1).Select(t => t.name).Take(5).ToArray()
                : new string[0];
            string Subject(int i) => i < names.Length ? names[i] : "Body";
            // Entries directly, not Add: Add logs warnings and errors, and these are not real.
            void Row(ReportStatus status, string category, int subject, string detail) =>
                report.Entries.Add(new ReportEntry { Status = status, Category = category, Subject = Subject(subject), Detail = detail });
            Row(ReportStatus.Converted, "PhysBones", 0, "Converted to MagicaCloth 2.");
            Row(ReportStatus.Converted, "Menu", 1, "");
            Row(ReportStatus.Approximated, "Contacts", 2, "Radius rounded to the nearest pointer size.");
            Row(ReportStatus.Skipped, "Components", 3, "No ChilloutVR equivalent.");
            Row(ReportStatus.Warning, "Shaders", 4, "Not single-pass instanced: renders in one eye only in VR.");
            Row(ReportStatus.Warning, "Animator", 0, "A layer writes a parameter nothing reads.");
            Row(ReportStatus.Error, "Textures", 1, "Could not read the import settings.");
            return report;
        }

        static void Render(Type windowType, string file, int width, int height, bool light, Action<EditorWindow> prepare = null)
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
            prepare?.Invoke(window);
            // A prepare that built the window to press something in it is not built over.
            if (content.childCount == 0) windowType.GetMethod("CreateGUI", Any)?.Invoke(window, null);
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

            Save(tex, file, light ? new Color(0.784f, 0.784f, 0.784f) : new Color(0.22f, 0.22f, 0.22f));
            UnityEngine.Object.DestroyImmediate(window);
            UnityEngine.Object.DestroyImmediate(owner);
        }

        // Rows are bottom-up in the texture. A band of more than 40 rows of one colour (the
        // empty part of a scroll view above a pinned footer, or the page below the content) is
        // cut to 16, so the tiles carry content; then the rest is cut into tiles from the top.
        static void Save(Texture2D tex, string file, Color background)
        {
            int w = tex.width, h = tex.height;
            var pixels = tex.GetPixels32();
            bool Plain(int y)
            {
                var first = pixels[y * w];
                for (int x = 1; x < w; x += 2)
                {
                    var p = pixels[y * w + x];
                    if (Mathf.Abs(p.r - first.r) + Mathf.Abs(p.g - first.g) + Mathf.Abs(p.b - first.b) > 9) return false;
                }
                return true;
            }
            var keep = new System.Collections.Generic.List<int>();   // top-down, in texture rows
            int run = 0;
            for (int y = h - 1; y >= 0; y--)
            {
                run = Plain(y) ? run + 1 : 0;
                if (run <= 16) keep.Add(y);
            }
            while (keep.Count > 0 && Plain(keep[keep.Count - 1]) && keep.Count > 1) keep.RemoveAt(keep.Count - 1);
            int used = keep.Count, tile = 1000, count = Mathf.CeilToInt(used / (float)tile);
            string stem = Path.Combine(Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(file));
            for (int i = 0; i < count; i++)
            {
                int rows = Mathf.Min(tile, used - i * tile);
                var part = new Texture2D(w, rows, TextureFormat.RGB24, false);
                var block = new Color32[w * rows];
                for (int r = 0; r < rows; r++)
                {
                    int src = keep[i * tile + r];                 // top-down source row
                    System.Array.Copy(pixels, src * w, block, (rows - 1 - r) * w, w);
                }
                part.SetPixels32(block);
                part.Apply();
                File.WriteAllBytes($"{stem}-{i:00}.png", part.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(part);
            }
            Log($"wrote {stem}-00..{count - 1:00}.png ({w}x{used} in {count} tile(s), {h - used} plain rows dropped)");
        }

        static GameObject FindSource()
        {
            var descriptor = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor", false)).FirstOrDefault(t => t != null);
            if (descriptor == null) return null;
            // Inactive too: a scene often keeps its source avatar switched off beside the copy.
            return Resources.FindObjectsOfTypeAll(descriptor).OfType<Component>().FirstOrDefault(c => c.gameObject.scene.IsValid())?.gameObject;
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
