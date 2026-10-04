// Renders each AvatarBridge window to PNGs, dark and a light preview, so a layout change can be
// looked at without a person at the screen. Compiling a UI change proves almost nothing; looking
// found every real fault so far. GUI Unity, never -batchmode: editor windows only draw with a GUI,
// and the screen must be unlocked, because the pixels are read off it.
//
// The light preview flips the windows' own .dark/.light classes; Unity's built-in controls stay
// in the editor's skin. Switching the real skin writes a machine-wide preference, and a run
// stopped halfway once left it on light.
//
// Run: Unity.exe -projectPath <p> -executeMethod AvatarBridge.Regression.UiCaptureProbe.Run
//        -captureOut <dir> [-captureScene <Assets/...unity>] [-capturePrefab <Assets/...prefab>] [-capturePlay]
// The scene's VRChat avatar is selected for the converter; the instantiated prefab (a converted
// avatar) for the other windows. -capturePlay also captures the tester live, in Play mode.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge.Regression
{
    [InitializeOnLoad]
    public static class UiCaptureProbe
    {
        const string StateKey = "UiCaptureProbe.state";
        const string OutKey = "UiCaptureProbe.out";

        static readonly (string type, string file, bool converted)[] Windows =
        {
            ("AvatarBridge.AvatarBridgeWindow", "converter", false),
            ("AvatarBridge.ToolkitWindow", "toolkit", true),
            ("AvatarBridge.CckAnimatorTester", "tester", true),
            ("AvatarBridge.YapsSetupWindow", "yaps", true),
        };

        static readonly Queue<Func<bool>> Steps = new Queue<Func<bool>>();
        static int _wait;
        static string _out;
        static GameObject _source, _converted;

        static UiCaptureProbe()
        {
            if (SessionState.GetString(StateKey, "") == "play")
            {
                EditorApplication.delayCall += ResumeInPlay;
            }
        }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            _out = Arg("-captureOut") ?? Path.Combine(Path.GetTempPath(), "ab-ui");
            Directory.CreateDirectory(_out);
            SessionState.SetString(OutKey, _out);

            string scene = Arg("-captureScene");
            if (!string.IsNullOrEmpty(scene)) EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            _source = FindSource();
            string prefab = Arg("-capturePrefab");
            if (!string.IsNullOrEmpty(prefab))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
                if (asset != null)
                {
                    _converted = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                    _converted.transform.position = new Vector3(2f, 0f, 0f);
                }
            }
            if (_converted == null) _converted = UnityEngine.Object.FindObjectOfType<CVRAvatar>()?.gameObject;
            Log($"source {(_source != null ? _source.name : "none")}, converted {(_converted != null ? _converted.name : "none")}");

            foreach (bool light in new[] { false, true })
            {
                foreach (var w in Windows) EnqueueWindow(w.type, w.file + (light ? "-light" : "-dark"), w.converted, light);
            }
            if (Array.IndexOf(args, "-capturePlay") >= 0)
            {
                Steps.Enqueue(() =>
                {
                    Selection.activeGameObject = _converted;
                    SessionState.SetString(StateKey, "play");
                    EditorApplication.EnterPlaymode();
                    return true;
                });
            }
            else
            {
                Steps.Enqueue(() => { Finish(); return true; });
            }
            EditorApplication.update += Tick;
        }

        static void EnqueueWindow(string typeName, string file, bool converted, bool light)
        {
            EditorWindow window = null;
            ScrollView scroll = null;
            float offset = 0f;
            int shot = 0;
            Steps.Enqueue(() =>
            {
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName, false)).FirstOrDefault(t => t != null);
                if (type == null) { Log($"skip {typeName}: not in this project"); return true; }
                var target = converted ? _converted : _source;
                if (target != null) Selection.activeGameObject = target;
                window = EditorWindow.GetWindow(type, true);
                window.position = new Rect(80, 80, 560, 860);
                window.Show();
                window.Focus();
                _wait = 90;
                return true;
            });
            // One capture per viewport down the window's main scroll view.
            Steps.Enqueue(() =>
            {
                if (window == null) return true;
                if (scroll == null)
                {
                    scroll = window.rootVisualElement.Query<ScrollView>().ToList()
                        .OrderByDescending(s => s.contentContainer.layout.height).FirstOrDefault();
                }
                if (light) Light(window.rootVisualElement);
                window.Focus();
                Capture(window, Path.Combine(_out, $"{file}-{shot++:00}.png"));
                if (scroll == null) return true;
                float max = Mathf.Max(0f, scroll.contentContainer.layout.height - scroll.contentViewport.layout.height);
                if (offset >= max || shot >= 12) { window.Close(); return true; }
                offset = Mathf.Min(max, offset + scroll.contentViewport.layout.height * 0.9f);
                scroll.scrollOffset = new Vector2(0f, offset);
                window.Repaint();
                _wait = 20;
                return false;
            });
        }

        static void Tick()
        {
            if (_wait-- > 0) return;
            if (Steps.Count == 0) { EditorApplication.update -= Tick; return; }
            try
            {
                if (Steps.Peek()()) Steps.Dequeue();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Steps.Dequeue();
            }
        }

        static void ResumeInPlay()
        {
            _out = SessionState.GetString(OutKey, Path.Combine(Path.GetTempPath(), "ab-ui"));
            double start = EditorApplication.timeSinceStartup;
            EditorWindow window = null;
            int shot = 0;
            float offset = 0f;
            void Step()
            {
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - start < 4.0) return;
                if (window == null)
                {
                    var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("AvatarBridge.CckAnimatorTester", false)).FirstOrDefault(t => t != null);
                    window = EditorWindow.GetWindow(type, true);
                    window.position = new Rect(80, 80, 560, 860);
                    window.Show();
                    start = EditorApplication.timeSinceStartup - 2.0;
                    return;
                }
                var scroll = window.rootVisualElement.Query<ScrollView>().ToList()
                    .OrderByDescending(s => s.contentContainer.layout.height).FirstOrDefault();
                Capture(window, Path.Combine(_out, $"tester-play-{shot++:00}.png"));
                float max = scroll == null ? 0f : Mathf.Max(0f, scroll.contentContainer.layout.height - scroll.contentViewport.layout.height);
                if (scroll == null || offset >= max || shot >= 12)
                {
                    EditorApplication.update -= Step;
                    SessionState.EraseString(StateKey);
                    Finish();
                    return;
                }
                offset = Mathf.Min(max, offset + scroll.contentViewport.layout.height * 0.9f);
                scroll.scrollOffset = new Vector2(0f, offset);
                window.Repaint();
                start = EditorApplication.timeSinceStartup - 3.6;
            }
            EditorApplication.update += Step;
        }

        // Off the screen, in pixels. GUIView.GrabPixels was tried first and returns an unwritten
        // (magenta) texture for a floating window on this Unity.
        static void Capture(EditorWindow window, string file)
        {
            float ppp = EditorGUIUtility.pixelsPerPoint;
            int width = Mathf.RoundToInt(window.position.width * ppp);
            int height = Mathf.RoundToInt(window.position.height * ppp);
            var pixels = InternalEditorUtility.ReadScreenPixel(window.position.position * ppp, width, height);
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Log($"wrote {file} ({width}x{height})");
        }

        static void Light(VisualElement root)
        {
            foreach (var e in root.Query(className: "dark").ToList().Append(root))
            {
                if (!e.ClassListContains("dark") && e != root) continue;
                e.EnableInClassList("dark", false);
                e.EnableInClassList("light", true);
            }
        }

        static GameObject FindSource()
        {
            var descriptor = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor", false)).FirstOrDefault(t => t != null);
            if (descriptor == null) return null;
            return UnityEngine.Object.FindObjectsOfType(descriptor).OfType<Component>().FirstOrDefault()?.gameObject;
        }

        static void Finish()
        {
            Log("done");
            EditorApplication.Exit(0);
        }

        static void Log(string s) => Debug.Log("[UiCaptureProbe] " + s);
    }
}
#endif
