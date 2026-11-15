using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using System.Text.RegularExpressions;
using Debug = UnityEngine.Debug;

namespace AvatarBridge.Regression
{
    // What loading an avatar costs on the main thread, the client's way: build its bundle as the CCK
    // does, then in Play mode load it, instantiate it and watch the frames after. Every profiler
    // sampler is recorded, and the worst frame of each is listed. Shader builds in the driver are not
    // in it (batch Unity draws nothing); ShaderDumpProbe and d3d-load.py measure those.
    // -executeMethod AvatarBridge.Regression.LoadHitchProbe.Run -hitchPrefab <a.prefab>[::<name>+<name>][,<b.prefab>...] -hitchOut <dir>
    // After ::, every object whose name starts with one of the names is removed before the instance wakes, to weigh it;
    // the name @materials puts plain Standard on every renderer instead, to weigh what its shaders cost the animator.
    [InitializeOnLoad]
    public static class LoadHitchProbe
    {
        const string K = "LoadHitchProbe.";
        const int Wait = 90, Watch = 150;

        static LoadHitchProbe()
        {
            if (SessionState.GetInt(K + "stage", -1) >= 0) EditorApplication.update += Tick;
        }

        static string Arg(string name, string fallback)
        {
            var a = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : fallback;
        }

        static void Log(string line)
        {
            Debug.Log("[LoadHitch] " + line);
            File.AppendAllText(Path.Combine(SessionState.GetString(K + "out", "Temp"), "hitch.txt"), line + "\n");
        }

        public static void Run()
        {
            try
            {
                string outDir = Path.GetFullPath(Arg("-hitchOut", "Temp/LoadHitch"));
                Directory.CreateDirectory(outDir);
                SessionState.SetString(K + "out", outDir);
                var bundles = new List<string>();
                int i = 0;
                foreach (string entry in Arg("-hitchPrefab", "").Split(','))
                {
                    string prefab = entry.Split(new[] { "::" }, System.StringSplitOptions.None)[0];
                    string strip = entry.Length > prefab.Length ? entry.Substring(prefab.Length + 2) : "";
                    string name = $"hitch{i++}.bundle";
                    var sw = Stopwatch.StartNew();
                    var manifest = BuildPipeline.BuildAssetBundles(outDir,
                        new[] { new AssetBundleBuild { assetBundleName = name, assetNames = new[] { prefab } } },
                        BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
                    var file = new FileInfo(Path.Combine(outDir, name));
                    Log($"bundle {prefab}: built in {sw.Elapsed.TotalSeconds:F0} s, {(file.Exists ? file.Length / 1048576.0 : -1):F1} MB, manifest {(manifest ? "ok" : "NULL")}");
                    if (file.Exists) bundles.Add(prefab + "|" + file.FullName + "|" + strip);
                }
                SessionState.SetString(K + "bundles", string.Join(";", bundles));
                SessionState.SetInt(K + "index", 0);
                SessionState.SetInt(K + "frame", 0);
                SessionState.SetInt(K + "stage", 0);
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
                EditorApplication.EnterPlaymode();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static AssetBundle bundle;
        static GameObject instance;
        static List<(string name, Recorder rec)> recorders;
        static Dictionary<string, (long ns, int frame)> worst;
        static double last;
        static List<string> frames;

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            var list = SessionState.GetString(K + "bundles", "").Split(';').Where(s => s.Length > 0).ToArray();
            int index = SessionState.GetInt(K + "index", 0), frame = SessionState.GetInt(K + "frame", 0) + 1;
            SessionState.SetInt(K + "frame", frame);
            double now = Time.realtimeSinceStartupAsDouble;
            try
            {
                if (index >= list.Length)
                {
                    SessionState.SetInt(K + "stage", -1);
                    EditorApplication.update -= Tick;
                    EditorApplication.ExitPlaymode();
                    EditorApplication.delayCall += () => EditorApplication.Exit(0);
                    return;
                }
                var parts = list[index].Split('|');
                if (frame == Wait)
                {
                    var names = new List<string>();
                    Sampler.GetNames(names);
                    // Main thread only, and markers only: the memory counters read as samplers too.
                    recorders = names.Distinct().Where(n => !Regex.IsMatch(n, "Memory|Bytes|Frame Time|Count$|^Idle$"))
                        .Select(n => (n, Recorder.Get(n))).Where(r => r.Item2 != null && r.Item2.isValid).ToList();
                    foreach (var r in recorders) { r.rec.FilterToCurrentThread(); r.rec.enabled = true; }
                    worst = new Dictionary<string, (long, int)>();
                    frames = new List<string>();
                    var sw = Stopwatch.StartNew();
                    bundle = AssetBundle.LoadFromFile(parts[1]);
                    double load = sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                    var prefab = bundle.LoadAllAssets<GameObject>().First();
                    double asset = sw.Elapsed.TotalMilliseconds;
                    // Instantiated under a switched-off parent, so what -hitchStrip names can go before
                    // anything wakes; then switched on, which is where Awake and OnEnable run.
                    var holder = new GameObject("LoadHitch holder");
                    holder.SetActive(false);
                    sw.Restart();
                    instance = Object.Instantiate(prefab, holder.transform);
                    double inst = sw.Elapsed.TotalMilliseconds;
                    string strip = parts[2];
                    var prefixes = strip.Split('+').Where(n => n.Length > 0 && n != "@materials").ToArray();
                    int stripped = 0;
                    if (prefixes.Length > 0)
                        foreach (var t in instance.GetComponentsInChildren<Transform>(true).Where(t => t != null && prefixes.Any(n => t.name.StartsWith(n))).ToList())
                        { if (t == null) continue; Object.DestroyImmediate(t.gameObject); stripped++; }
                    if (strip.Split('+').Contains("@materials"))
                    {
                        var plain = new Material(Shader.Find("Standard"));
                        foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
                            r.sharedMaterials = Enumerable.Repeat(plain, r.sharedMaterials.Length).ToArray();
                    }
                    sw.Restart();
                    holder.SetActive(true);
                    double wake = sw.Elapsed.TotalMilliseconds;
                    instance = holder;
                    Log($"=== {parts[0]}{(strip.Length > 0 ? $" without {stripped} \"{strip}\"" : "")}: LoadFromFile {load:F0} ms, LoadAsset {asset:F0} ms, Instantiate {inst:F0} ms, switch on {wake:F0} ms, {instance.GetComponentsInChildren<Transform>(true).Length} transforms");
                }
                else if (frame > Wait && frame <= Wait + Watch)
                {
                    frames.Add($"{(now - last) * 1000:F0}");
                    foreach (var r in recorders)
                    {
                        long ns = r.rec.elapsedNanoseconds;
                        if (ns > 0 && (!worst.TryGetValue(r.name, out var w) || ns > w.ns)) worst[r.name] = (ns, frame - Wait);
                    }
                }
                else if (frame == Wait + Watch + 1)
                {
                    Log("frame ms after instantiate: " + string.Join(" ", frames.Take(40)));
                    foreach (var kv in worst.OrderByDescending(kv => kv.Value.ns).Take(40))
                        Log($"  {kv.Value.ns / 1e6,9:F1} ms  frame {kv.Value.frame,3}  {kv.Key}");
                    foreach (var r in recorders) r.rec.enabled = false;
                    Object.Destroy(instance);
                    bundle.Unload(true);
                    SessionState.SetInt(K + "index", index + 1);
                    SessionState.SetInt(K + "frame", 0);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                SessionState.SetInt(K + "stage", -1);
                EditorApplication.update -= Tick;
                EditorApplication.Exit(1);
            }
            last = now;
        }
    }
}
