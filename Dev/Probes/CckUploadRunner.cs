#if CVR_CCK_EXISTS && UNITY_EDITOR
// Uploads one scene object's avatar headless, the way the CCK builder tab does, keeping the
// name, description and tags already on the server. Only content that is already uploaded and
// owned is accepted, so it can update a file and never create a listing.
//
//   -executeMethod AvatarBridge.Regression.CckUploadRunner.Run
//       -upScene <Assets/...unity> -upObject <root name> [-upLegal rights+tags]
//
// Without -upLegal it stops after the server check: nothing builds or uploads. The two upload
// statements are the owner's to make, so -upLegal is passed only on the owner's word. Login is
// the CCK's own saved profile; nothing here reads it. No -quit: it exits.
using System;
using System.Linq;
using System.Threading.Tasks;
using ABI.CCK.Components;
using CVR.CCKEditor.API;
using CVR.CCKEditor.ContentBuilder;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class CckUploadRunner
    {
        const string Tag = "[CckUpload] ";
        static void Log(string s) => Debug.Log(Tag + s);

        static CVRAssetInfo _info;
        static Func<bool> _waiting;
        static Action _then;
        static ContentBuilderAPI.BuildPhase _phase;

        public static void Run()
        {
            try
            {
                string scene = Arg("-upScene"), name = Arg("-upObject");
                EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
                var root = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == name);
                _info = root != null ? root.GetComponent<CVRAssetInfo>() : null;
                if (_info == null) { Log($"REFUSE no CVRAssetInfo on '{name}' in {scene}"); Finish(3); return; }
                if (string.IsNullOrEmpty(_info.objectId)) { Log("REFUSE empty objectId: this would create new content"); Finish(3); return; }
                var shaders = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                    .Where(m => m != null && m.shader != null).Select(m => m.shader.name).Distinct().OrderBy(s => s);
                Log($"object {name}, id {_info.objectId}, shaders: {string.Join(" | ", shaders)}");

                ApiCredentialsHandler.LoadAllProfiles();
                if (!ApiCredentialsHandler.IsCurrentCredentialsLoaded()) { Log("REFUSE no saved CCK login"); Finish(3); return; }
                Task<bool> login = ApiConnection.IsLoggedIn ? Task.FromResult(true) : ApiConnection.Login();
                Wait(login, () =>
                {
                    if (!login.Result) { Log("REFUSE login failed"); Finish(3); return; }
                    Task<CVRApiContent> fetch = ContentService.FetchContentDetails(_info);
                    Wait(fetch, () => Check(fetch.Result));
                });
                EditorApplication.update += Tick;
            }
            catch (Exception e) { Debug.LogException(e); Finish(2); }
        }

        static void Check(CVRApiContent server)
        {
            if (server == null || !server.IsValid || !server.IsContentMine || server.ID != _info.objectId)
            {
                Log($"REFUSE server content not valid and owned (valid {server?.IsValid}, mine {server?.IsContentMine}, id {server?.ID})");
                Finish(3);
                return;
            }
            Log($"server: '{server.Name}', platforms {string.Join(",", server.Platforms)}, tags {JsonUtility.ToJson(server.ContentTags)}");
            if (Arg("-upLegal") != "rights+tags") { Log("DRY run: no -upLegal, nothing built or uploaded"); Finish(0); return; }

            var upload = new UploadInfo
            {
                Name = server.Name,
                Description = server.Description,
                ContentTags = server.ContentTags,
                Changelog = "",
                SetAsActiveFile = true,
                ThumbnailImagePath = null,   // keeps the current image
            };
            ContentBuilderAPI.OnProgressUpdated += Progress;
            Log("BEGIN upload");
            Task task = ContentBuilderAPI.BuildAndUpload(_info, BuildConfig.Default, upload, new LegalAssurance(true, true));
            Wait(task, () =>
            {
                ContentBuilderAPI.OnProgressUpdated -= Progress;
                if (task.IsFaulted) Log($"threw {task.Exception?.GetBaseException().Message}");
                var status = ContentBuilderAPI.LastBuildStatus;
                Log($"END upload: {status}");
                Finish(status == ContentBuilderAPI.BuildStatus.Completed ? 0 : 1);
            });
        }

        // One line per phase: the per-percent callbacks would bury the log.
        static void Progress(ContentBuilderAPI.BuildPhase phase, float progress, string details)
        {
            if (phase == _phase) return;
            _phase = phase;
            Log($"phase {phase}: {details}");
        }

        static void Wait(Task task, Action then)
        {
            _waiting = () => task.IsCompleted;
            _then = then;
        }

        static void Tick()
        {
            try
            {
                if (_waiting == null || !_waiting()) return;
                _waiting = null;
                var then = _then;
                _then = null;
                then();
            }
            catch (Exception e) { Debug.LogException(e); Finish(2); }
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            Log($"exit {code}");
            EditorApplication.Exit(code);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
#endif
