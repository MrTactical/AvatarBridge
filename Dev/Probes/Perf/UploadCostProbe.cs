#if CVR_CCK_EXISTS && UNITY_EDITOR
// What a ChilloutVR upload spends, stage by stage, without uploading: the
// CCK's own build pipeline as ContentBuildPipeline runs it, up to the bundle
// (BuildPurpose.LocalTest, so nothing leaves the machine), timed per stage and
// per build processor. Every avatar is built twice, cold then warm, so a first
// compile shows apart from what every upload pays.
//
//   -executeMethod AvatarBridge.Regression.UploadCostProbe.Run
//       [-upPrefabs <a.prefab;b.prefab>] [-upTop N] [-upOut <dir>]
//
// -upTop adds the N output prefabs with the most YAPS-patched shaders, and the
// largest one with none for contrast. Each bundle build sits between
// "[UploadCost] BEGIN" and "END" lines, so the per-shader compile lines Unity
// writes during it can be read from the log afterwards. No -quit: it exits.
// Poiyomi locks materials before a real upload; any it locks here are listed
// and unlocked again at the end.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ABI.CCK.Components;
using CVR.CCKEditor.ContentBuilder;
using CVR.CCKEditor.Validations;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    public static class UploadCostProbe
    {
        const string Tag = "[UploadCost] ";
        static void Log(string s) => Debug.Log(Tag + s);

        static readonly Queue<(string path, string pass)> Jobs = new Queue<(string, string)>();
        static readonly Dictionary<Material, string> Locked = new Dictionary<Material, string>();
        static string _out;
        static Func<bool> _waiting;
        static Action _then;

        public static void Run()
        {
            try
            {
                _out = Arg("-upOut") ?? Path.Combine(Path.GetTempPath(), "UploadCost");
                Directory.CreateDirectory(_out);
                var paths = (Arg("-upPrefabs") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                int top = int.TryParse(Arg("-upTop"), out int n) ? n : (paths.Count == 0 ? 2 : 0);
                if (top > 0) paths.AddRange(Census(top).Where(p => !paths.Contains(p)));
                Log($"GPU {SystemInfo.graphicsDeviceType}, target {EditorUserBuildSettings.activeBuildTarget}, " +
                    $"stereo {PlayerSettings.stereoRenderingPath}, cores {SystemInfo.processorCount}");
                foreach (string pass in new[] { "cold", "warm" })
                    foreach (string p in paths) Jobs.Enqueue((p, pass));
                EditorApplication.update += Tick;
            }
            catch (Exception e) { Debug.LogException(e); Finish(2); }
        }

        static void Tick()
        {
            try
            {
                if (_waiting != null)
                {
                    if (!_waiting()) return;
                    _waiting = null;
                    var then = _then;
                    _then = null;
                    then();
                    return;
                }
                if (Jobs.Count == 0) { Finish(0); return; }
                var (path, pass) = Jobs.Dequeue();
                Build(path, pass);
            }
            catch (Exception e) { Debug.LogException(e); Finish(2); }
        }

        // Every CVRAvatar prefab in the output folders, by YAPS-patched shaders among its dependencies.
        static List<string> Census(int top)
        {
            var rows = new List<(string path, int yaps, int shaders, int mats, int clips)>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/AvatarBridgeOutput" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null || go.GetComponent<CVRAvatar>() == null || go.GetComponent<CVRAssetInfo>() == null) continue;
                var deps = AssetDatabase.GetDependencies(path, true);
                var shaders = deps.Where(d => d.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)).ToList();
                rows.Add((path, shaders.Count(IsYaps), shaders.Count,
                    deps.Count(d => d.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)),
                    deps.Count(d => d.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))));
            }
            foreach (var r in rows.OrderByDescending(r => r.yaps).ThenByDescending(r => r.shaders))
                Log($"census yaps {r.yaps} shaders {r.shaders} mats {r.mats} clips {r.clips} {r.path}");
            var pick = rows.OrderByDescending(r => r.yaps).ThenByDescending(r => r.shaders).Take(top).Select(r => r.path).ToList();
            var plain = rows.Where(r => r.yaps == 0).OrderByDescending(r => r.shaders).Select(r => r.path).FirstOrDefault();
            if (plain != null) pick.Add(plain);
            return pick;
        }

        static bool IsYaps(string shaderPath)
        {
            if (shaderPath.StartsWith("Assets/YAPS/Generated/", StringComparison.OrdinalIgnoreCase)) return true;
            var s = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            return s != null && s.name.IndexOf("YAPS", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void Build(string path, string pass)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Log($"SKIP no prefab at {path}"); return; }
            string label = $"{pass} {Path.GetFileNameWithoutExtension(path)}";
            var before = MaterialShaders(path);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = (GameObject) PrefabUtility.InstantiatePrefab(prefab);
            var info = instance.GetComponent<CVRAssetInfo>();
            var laps = new List<string>();
            var total = Stopwatch.StartNew();
            var sw = Stopwatch.StartNew();
            void Lap(string what) { laps.Add($"{what} {sw.Elapsed.TotalSeconds:0.00}"); sw.Restart(); }

            TempBuildAsset asset = null;
            try
            {
                asset = TempBuildAsset.Create(info, BuildPurpose.LocalTest);
                Lap("create");
                asset.ValidateAsset();
                Lap("wants");
                // ProcessPreBuildAvatar, one processor at a time.
                foreach (var processor in asset.Processors)
                {
                    processor.OnPreProcessAvatar(asset.RootObject);
                    Lap("pre:" + processor.GetType().Name);
                }
                asset.SaveChangesToAsset();
                Lap("save");
            }
            catch
            {
                Cleanup(asset, instance);
                throw;
            }

            Task<ValidationPipelineResult> validate = Validator.Validate(asset.AssetInfo);
            _waiting = () => validate.IsCompleted;
            _then = () =>
            {
                try
                {
                    if (validate.IsFaulted) Log($"validate threw {validate.Exception?.GetBaseException().Message}");
                    else Log($"validate success {validate.Result.IsSuccess}");
                    Lap("validate");
                    Log("BEGIN " + label);
                    string bundle = asset.BuildAssetBundle(new BuildConfig
                    {
                        BuildTarget = EditorUserBuildSettings.activeBuildTarget,
                        BuildOutputPath = _out,
                    });
                    Log("END " + label);
                    Lap("bundle");
                    long size = File.Exists(bundle) ? new FileInfo(bundle).Length : -1;
                    asset.PostProcessAsset();
                    Lap("post");
                    Cleanup(asset, instance);
                    Lap("dispose");
                    Log($"RESULT {label}: total {total.Elapsed.TotalSeconds:0.00} s, bundle {size / 1048576.0:0.00} MB; {string.Join(", ", laps)}");
                    if (File.Exists(bundle)) File.Delete(bundle);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    Cleanup(asset, instance);
                }
                foreach (var kv in MaterialShaders(path))
                {
                    if (before.TryGetValue(kv.Key, out string was) && was != kv.Value && !Locked.ContainsKey(kv.Key))
                    {
                        Locked[kv.Key] = was;
                        Log($"locked by the build: {AssetDatabase.GetAssetPath(kv.Key)} ({was} -> {kv.Value})");
                    }
                }
            };
        }

        static void Cleanup(TempBuildAsset asset, GameObject instance)
        {
            try { asset?.Dispose(); } catch (Exception e) { Debug.LogException(e); }
            if (instance != null) Object.DestroyImmediate(instance);
        }

        static Dictionary<Material, string> MaterialShaders(string prefabPath)
        {
            var map = new Dictionary<Material, string>();
            foreach (string d in AssetDatabase.GetDependencies(prefabPath, true))
            {
                if (!d.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) continue;
                var m = AssetDatabase.LoadAssetAtPath<Material>(d);
                if (m != null && m.shader != null) map[m] = m.shader.name;
            }
            return map;
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            if (Locked.Count > 0)
            {
                // Thry's own unlock, by reflection: the probe must compile without Poiyomi.
                var optimizer = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("Thry.ThryEditor.ShaderOptimizer")).FirstOrDefault(t => t != null);
                var unlock = optimizer?.GetMethods().FirstOrDefault(m => m.Name == "UnlockMaterials");
                if (unlock != null)
                {
                    var args = unlock.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
                    args[0] = Locked.Keys.ToList();
                    Log($"unlocking {Locked.Count}: {unlock.Invoke(null, args)}");
                }
                else Log($"could not unlock {Locked.Count}: no ShaderOptimizer");
            }
            Log("DONE");
            if (Application.isBatchMode) EditorApplication.Exit(code);
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
