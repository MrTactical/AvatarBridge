using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace AvatarBridge.Regression
{
    // Builds one avatar's bundle the way the CCK upload does (ChunkBasedCompression, one prefab),
    // timed, with the heaviest dependencies listed. -bundleScene, -bundleObject, -bundleOut.
    public static class BundleBuildProbe
    {
        static string Arg(string name, string fallback)
        {
            var a = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : fallback;
        }

        public static void Run()
        {
            int code = 1;
            try
            {
                EditorSceneManager.OpenScene(Arg("-bundleScene", ""));
                string objName = Arg("-bundleObject", "");
                var go = Object.FindObjectsOfType<Component>(true).Where(c => c.GetType().Name == "CVRAvatar")
                    .Select(c => c.gameObject).OrderBy(g => g.name == objName ? 0 : 1).First();
                Debug.Log($"[BundleProbe] avatar {go.name}");
                Directory.CreateDirectory("Assets/_BundleProbe");
                string prefab = "Assets/_BundleProbe/probe.prefab";
                var sw = Stopwatch.StartNew();
                var clone = Object.Instantiate(go);
                PrefabUtility.SaveAsPrefabAsset(clone, prefab);
                Object.DestroyImmediate(clone);
                Debug.Log($"[BundleProbe] save prefab {sw.Elapsed.TotalSeconds:F1} s");

                var deps = AssetDatabase.GetDependencies(prefab, true);
                var rows = deps.Select(p => (p, obj: AssetDatabase.LoadMainAssetAtPath(p)))
                    .Where(r => r.obj != null)
                    .Select(r => (r.p, type: r.obj.GetType().Name, mem: Profiler.GetRuntimeMemorySizeLong(r.obj)))
                    .OrderByDescending(r => r.mem).ToList();
                Debug.Log($"[BundleProbe] {rows.Count} dependencies, {rows.Sum(r => r.mem) / 1048576.0:F0} MB in memory");
                foreach (var g in rows.GroupBy(r => r.type).OrderByDescending(g => g.Sum(r => r.mem)))
                    Debug.Log($"[BundleProbe] type {g.Key}: {g.Count()} assets, {g.Sum(r => r.mem) / 1048576.0:F1} MB");
                foreach (var r in rows.Take(40))
                    Debug.Log($"[BundleProbe] dep {r.mem / 1048576.0,8:F1} MB {r.type,-18} {r.p}");

                string outDir = Arg("-bundleOut", "Temp/BundleProbe");
                Directory.CreateDirectory(outDir);
                sw.Restart();
                var manifest = BuildPipeline.BuildAssetBundles(outDir,
                    new[] { new AssetBundleBuild { assetBundleName = "probe.cvravatar", assetNames = new[] { prefab } } },
                    BuildAssetBundleOptions.ChunkBasedCompression, EditorUserBuildSettings.activeBuildTarget);
                Debug.Log($"[BundleProbe] BuildAssetBundles {sw.Elapsed.TotalSeconds:F1} s, manifest {(manifest ? "ok" : "NULL")}");
                var file = new FileInfo(Path.Combine(outDir, "probe.cvravatar"));
                Debug.Log($"[BundleProbe] bundle {(file.Exists ? file.Length / 1048576.0 : -1):F1} MB");
                code = manifest ? 0 : 1;
            }
            catch (System.Exception e) { Debug.LogException(e); }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
