#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge.Regression
{
    // A socket's preview is editor state, never saved, so a recompile dropped a running one while
    // its button still read as on. And a preview standing down in Play Mode said nothing, which
    // read as a feature that does not work. Each step below crosses a domain reload, so the test
    // picks itself back up from SessionState.
    //
    // Run: -executeMethod AvatarBridge.Regression.SocketPreviewReloadTest.Run (no -quit)
    [InitializeOnLoad]
    public static class SocketPreviewReloadTest
    {
        const string StepKey = "SocketPreviewReloadTest.step", IdKey = "SocketPreviewReloadTest.id", FailKey = "SocketPreviewReloadTest.fail",
            OptionsKey = "SocketPreviewReloadTest.options";

        static SocketPreviewReloadTest()
        {
            if (SessionState.GetInt(StepKey, 0) > 0) EditorApplication.update += Tick;
        }

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var socket = new GameObject("__PreviewReloadSocket").AddComponent<YapsSocket>();
            YapsPreview.Set(socket, true, spawnPlugIfNone: false);
            Check(socket.preview, "the preview is on before the reload");
            SessionState.SetInt(IdKey, socket.GetInstanceID());
            SessionState.SetInt(StepKey, 1);
            EditorUtility.RequestScriptReload();
        }

        static int _frames;

        static void Tick()
        {
            // A few frames, so whatever restores the preview after a reload has run.
            if (++_frames < 10) return;
            EditorApplication.update -= Tick;
            var socket = EditorUtility.InstanceIDToObject(SessionState.GetInt(IdKey, 0)) as YapsSocket;
            int step = SessionState.GetInt(StepKey, 0);
            if (socket == null) { Check(false, $"the socket is still there at step {step}"); Finish(); return; }

            if (step == 1)
            {
                Check(socket.preview, "a running preview survives a recompile");
                // Ticked, the preview has to come through the reload into Play Mode too.
                socket.previewInPlayMode = true;
                YapsPreview.Set(socket, true, spawnPlugIfNone: false);
                // Entered with a domain reload, the strict case, whatever the project is set to.
                SessionState.SetInt(OptionsKey, EditorSettings.enterPlayModeOptionsEnabled ? 1 : 0);
                EditorSettings.enterPlayModeOptionsEnabled = false;
                SessionState.SetInt(StepKey, 2);
                EditorApplication.update += Tick;
                _frames = 0;
                EditorApplication.EnterPlaymode();
                return;
            }

            if (step == 2)
            {
                if (!EditorApplication.isPlaying) { _frames = 0; EditorApplication.update += Tick; return; }
                Check(socket.preview, "with Keep previewing in Play Mode ticked, it carries into Play Mode");
                // Unticked while previewing, it stands down. The inspector has to say so.
                socket.previewInPlayMode = false;
                var editor = Editor.CreateEditor(socket);
                var root = editor.CreateInspectorGUI();
                string said = string.Join(" | ", root.Query<TextElement>().ToList().Select(t => t.text).Where(t => !string.IsNullOrEmpty(t)));
                Check(said.Contains("paused"), "the inspector says the preview is paused in Play Mode");
                Object.DestroyImmediate(editor);
                Finish();
            }
        }

        static void Check(bool ok, string what)
        {
            if (!ok) SessionState.SetInt(FailKey, SessionState.GetInt(FailKey, 0) + 1);
            Debug.Log($"[SocketPreviewReloadTest] {(ok ? "ok   " : "WRONG")} {what}");
        }

        static void Finish()
        {
            int failures = SessionState.GetInt(FailKey, 0);
            if (SessionState.GetInt(OptionsKey, -1) >= 0) EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetInt(OptionsKey, 0) == 1;
            SessionState.EraseInt(OptionsKey);
            SessionState.EraseInt(StepKey);
            SessionState.EraseInt(IdKey);
            SessionState.EraseInt(FailKey);
            Debug.Log(failures == 0 ? "[SocketPreviewReloadTest] PASS" : $"[SocketPreviewReloadTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }
}
#endif
