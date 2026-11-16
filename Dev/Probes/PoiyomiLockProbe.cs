// Poiyomi's own lock, measured: where its time goes, whether locking avatars together compiles
// fewer shaders, and what a conversion of an UNLOCKED avatar leaves for the upload to lock again.
//
// Run it in a throwaway copy of a project, never the corpus one: it unlocks materials, deletes
// locked shader folders and locks again, which rewrites the project's materials.
//
// Census:  -executeMethod AvatarBridge.Regression.PoiyomiLockProbe.Census -poiScenes <file> -poiOut <dir>
//   unlocks every material the listed avatars use and deletes their locked shader folders, then
//   counts the shaders Poiyomi would write locking one avatar at a time against all at once, from
//   its own settings hash. Nothing compiles.
// Timed:   -executeMethod AvatarBridge.Regression.PoiyomiLockProbe.Timed -poiScenes <file> -poiOut <dir> [-poiReps 2]
//   from the unlocked state: one avatar at a time, then all at once (ABBA with two reps), unlocking
//   and deleting what each lock wrote in between. Each lock is split into the imports of the files
//   it wrote and everything else, which is Poiyomi's own text work and its refreshes.
// Convert: -executeMethod AvatarBridge.Regression.PoiyomiLockProbe.Convert -poiScenes <file> -poiOut <dir> [-poiPasses locked|unlocked]
//   converts each avatar unlocked with the corpus profile, sorts the converted materials by where
//   they live and whether they are locked, and locks them the way the ChilloutVR upload does. Then
//   locks the source, converts again and locks again.
// Trigger: -executeMethod AvatarBridge.Regression.PoiyomiLockProbe.Trigger -poiOut <dir>
//   copies the largest YAPS patch of Poiyomi to a new path, so nothing of it is cached, and times
//   each step a squash copy takes on it, to find the one that waits for the shader to compile.
#if CVR_CCK_EXISTS && VRC_SDK_VRCSDK3
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Debug = UnityEngine.Debug;

namespace AvatarBridge.Regression
{
    // Times the imports of the files a lock writes. A batch runs from the first file it
    // preprocesses to the postprocess that reports it, so the sum is import time even if
    // the lock imports in several batches.
    class PoiyomiLockWatch : AssetPostprocessor
    {
        public static bool On;
        public static Stopwatch Clock = new Stopwatch();
        public static double BatchStart = -1, ImportSeconds, First = -1, Last = -1;
        public static int Batches;
        public static readonly HashSet<string> Shaders = new HashSet<string>();

        public static void Begin()
        {
            BatchStart = -1; ImportSeconds = 0; First = -1; Last = -1; Batches = 0;
            Shaders.Clear();
            Clock.Restart();
            On = true;
        }

        static bool Locked(string path) => path.Contains("/OptimizedShaders/");

        void OnPreprocessAsset()
        {
            if (!On || !Locked(assetPath)) return;
            double now = Clock.Elapsed.TotalSeconds;
            if (First < 0) First = now;
            if (BatchStart < 0) BatchStart = now;
            if (assetPath.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)) Shaders.Add(assetPath);
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!On || BatchStart < 0 || !imported.Any(Locked)) return;
            double now = Clock.Elapsed.TotalSeconds;
            ImportSeconds += now - BatchStart;
            Last = now;
            Batches++;
            BatchStart = -1;
        }
    }

    public static class PoiyomiLockProbe
    {
        const string Tag = "[PoiLock] ";
        // YapsShaderPatcher.SourceShaderProperty, spelled out so the probe builds without the add-on.
        const string YapsSource = "_YAPS_SourceShader";
        static readonly StringBuilder Out = new StringBuilder();
        static MethodInfo lockMaterials, unlockMaterials, isLocked, usesOptimizer, settingsHash;
        static object noBar;

        class Avatar
        {
            public string Scene, Name;
            public List<string> Materials = new List<string>();
        }

        public static void Census() => Batch("census", () =>
        {
            var avatars = Gather(Scenes());
            var all = Load(avatars.SelectMany(a => a.Materials));
            Say($"{avatars.Count} avatars, {all.Count} distinct material assets");
            foreach (var a in avatars)
            {
                var mats = Load(a.Materials);
                Say($"  {a.Name}: {mats.Count} materials, {mats.Count(Locked)} locked, " +
                    $"{mats.Count(Lockable)} unlocked and lockable, {mats.Count(m => !Locked(m) && !Lockable(m))} other");
            }

            double unlock = Reset(all, out int deleted);
            Say($"unlock all: {unlock:0.0} s, {deleted} locked shader folders deleted, " +
                $"{all.Count(Locked)} still locked (their original shader is not in the project), " +
                $"{all.Count(Lockable)} lockable now");

            // Poiyomi shares one locked shader between materials of the same settings hash, but
            // only inside one call, and skips a material an earlier call already locked.
            var done = new HashSet<Material>();
            int separate = 0;
            foreach (var a in avatars)
            {
                var todo = Load(a.Materials).Where(m => Lockable(m) && !done.Contains(m)).ToList();
                int shaders = todo.Select(Hash).Distinct().Count();
                separate += shaders;
                done.UnionWith(todo);
                Say($"  alone {a.Name}: {todo.Count} to lock, {shaders} shaders");
            }
            var lockable = all.Where(Lockable).ToList();
            int together = lockable.Select(Hash).Distinct().Count();
            Say($"shaders written: {separate} one avatar at a time, {together} all at once " +
                $"({lockable.Count} materials)");
            AssetDatabase.SaveAssets();
        });

        public static void Timed() => Batch("timed", () =>
        {
            var avatars = Gather(Scenes());
            var all = Load(avatars.SelectMany(a => a.Materials));
            int reps = int.TryParse(Arg("-poiReps"), out int r) ? r : 1;
            var order = reps >= 2 ? new[] { "alone", "together", "together", "alone" } : new[] { "alone", "together" };
            if (all.Any(Locked)) Say($"reset first: {Reset(all, out _):0.0} s");
            var lockable = all.Where(Lockable).ToList();
            Say($"{avatars.Count} avatars, {lockable.Count} lockable materials");

            foreach (string arm in order)
            {
                var sum = new Timing();
                if (arm == "alone")
                {
                    foreach (var a in avatars)
                    {
                        var mats = Load(a.Materials).Where(Lockable).ToList();
                        if (mats.Count == 0) continue;
                        var t = Lock(mats);
                        Say($"  alone {a.Name}: {t}");
                        sum.Add(t);
                    }
                }
                else sum.Add(Lock(lockable));
                Say($"{arm}: {sum}");
                Say($"  reset: {Reset(all, out _):0.0} s");
            }
        });

        public static void Convert() => Batch("convert", () =>
        {
            var settings = (BridgeSettings)typeof(RegressionRunner)
                .GetMethod("CorpusSettings", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, null);
            foreach (string scene in Scenes())
            {
                // -poiPasses locked (or unlocked) runs one of the two.
                foreach (bool lockFirst in new[] { false, true }.Where(l => Arg("-poiPasses") is string only ? only == (l ? "locked" : "unlocked") : true))
                {
                    var descriptor = Open(scene);
                    if (descriptor == null) { Say($"{scene}: no avatar"); break; }
                    var source = Load(SourceMaterials(descriptor));
                    string state;
                    if (lockFirst) state = "source locked first, " + Lock(source.Where(Lockable).ToList());
                    else
                    {
                        if (source.Any(Locked)) Reset(source, out _);
                        state = $"source unlocked ({source.Count(Lockable)} lockable, {source.Count(Locked)} would not unlock)";
                    }
                    Say($"{descriptor.gameObject.name}: {state}");

                    var clock = Stopwatch.StartNew();
                    var report = BridgeConverter.Convert(descriptor, settings);
                    double seconds = clock.Elapsed.TotalSeconds;
                    var root = report.ConvertedRoot;
                    if (root == null) { Say("  conversion failed"); continue; }

                    var converted = Load(ConvertedMaterials(root));
                    var sources = new HashSet<Material>(source);
                    var kinds = converted.GroupBy(m => Kind(m, sources, settings.outputFolder))
                        .OrderBy(g => g.Key, StringComparer.Ordinal)
                        .Select(g => $"{g.Count()} {g.Key}");
                    Say($"  converted in {seconds:0.0} s; {converted.Count} materials: {string.Join(", ", kinds)}");
                    Say($"  upload lock: {Lock(converted.Where(Lockable).ToList())}");
                    // What the bundle would get: a shader that fails to compile, one locked twice,
                    // or one Thry still reads as unlocked, which its build strip removes.
                    Say($"  after it: {converted.Count(m => ShaderUtil.ShaderHasError(m.shader))} with shader errors, " +
                        $"{converted.Count(m => m.shader.name.StartsWith("Hidden/Locked/Hidden/Locked/", StringComparison.Ordinal))} locked twice, " +
                        $"{converted.Count(Lockable)} still unlocked");
                }
            }
        });

        // ------------------------------------------------------------------ the lock

        struct Timing
        {
            public double Total, Import;
            public int Materials, Shaders, Batches;

            public void Add(Timing t)
            {
                Total += t.Total; Import += t.Import; Materials += t.Materials;
                Shaders += t.Shaders; Batches += t.Batches;
            }

            public override string ToString() => string.Format(CultureInfo.InvariantCulture,
                "{0} materials, {1} shaders, {2:0.0} s total = {3:0.0} s importing ({4} batches) + {5:0.0} s the rest",
                Materials, Shaders, Total, Import, Batches, Total - Import);
        }

        static Timing Lock(List<Material> materials)
        {
            if (materials.Count == 0) return new Timing();
            PoiyomiLockWatch.Begin();
            try { lockMaterials.Invoke(null, new object[] { materials, noBar }); }
            finally { PoiyomiLockWatch.On = false; }
            return new Timing
            {
                Total = PoiyomiLockWatch.Clock.Elapsed.TotalSeconds,
                Import = PoiyomiLockWatch.ImportSeconds,
                Batches = PoiyomiLockWatch.Batches,
                Materials = materials.Count,
                Shaders = PoiyomiLockWatch.Shaders.Count,
            };
        }

        // Unlocks, then deletes the locked shader folders nothing points into any more, so the
        // next lock compiles from nothing. Left in place, a rewritten file with the same text and
        // GUID could be served from the import cache and read as a fast lock.
        static double Reset(List<Material> materials, out int deleted)
        {
            var folders = new HashSet<string>();
            foreach (var m in materials.Where(Locked))
            {
                string path = AssetDatabase.GetAssetPath(m.shader);
                const string marker = "/OptimizedShaders/";
                int i = path.IndexOf(marker, StringComparison.Ordinal);
                int end = i < 0 ? -1 : path.IndexOf('/', i + marker.Length);
                if (end > 0) folders.Add(path.Substring(0, end));
            }
            var clock = Stopwatch.StartNew();
            unlockMaterials.Invoke(null, new object[] { materials, noBar });
            double seconds = clock.Elapsed.TotalSeconds;

            var inUse = materials.Select(m => AssetDatabase.GetAssetPath(m.shader)).ToList();
            deleted = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string folder in folders)
                {
                    if (inUse.Any(p => p.StartsWith(folder + "/", StringComparison.Ordinal))) continue;
                    if (AssetDatabase.DeleteAsset(folder)) deleted++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();
            return seconds;
        }

        static bool Locked(Material m) => m != null && (bool)isLocked.Invoke(null, new object[] { m });
        static bool Lockable(Material m) => m != null && !Locked(m) && (bool)usesOptimizer.Invoke(null, new object[] { m.shader });
        static string Hash(Material m) => (string)settingsHash.Invoke(null, new object[] { m });

        static string Kind(Material m, HashSet<Material> sources, string outputFolder)
        {
            string where = AssetDatabase.GetAssetPath(m).StartsWith(outputFolder + "/", StringComparison.Ordinal) ? "copied"
                : sources.Contains(m) ? "source" : "elsewhere";
            string state = Locked(m) ? "locked" : Lockable(m) ? "UNLOCKED" : "plain";
            int i = m.shader.FindPropertyIndex(YapsSource);
            if (i >= 0)
            {
                // The patch names the shader it was made from; locked or not decides what the
                // copy carries into the bundle.
                string from = m.shader.GetPropertyDescription(i);
                var original = Shader.Find(from);
                state = "YAPS patch of " + (from.StartsWith("Hidden/", StringComparison.Ordinal) ? "a locked shader"
                    : original != null && (bool)usesOptimizer.Invoke(null, new object[] { original }) ? "an UNLOCKED Poiyomi shader"
                    : "a plain shader");
            }
            return where + " " + state;
        }

        // ------------------------------------------------------------------ gathering

        static List<Avatar> Gather(IEnumerable<string> scenes)
        {
            var list = new List<Avatar>();
            foreach (string scene in scenes)
            {
                var descriptor = Open(scene);
                if (descriptor == null) { Say($"{scene}: no avatar"); continue; }
                list.Add(new Avatar { Scene = scene, Name = descriptor.gameObject.name, Materials = SourceMaterials(descriptor) });
            }
            return list;
        }

        static VRCAvatarDescriptor Open(string scene)
        {
            var opened = EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            foreach (var root in opened.GetRootGameObjects())
            {
                var descriptor = root.GetComponentInChildren<VRCAvatarDescriptor>(true);
                if (descriptor != null) return descriptor;
            }
            return null;
        }

        // What the VRChat upload hook locks: every renderer's slots, and every material a clip
        // swaps in, from the playable layers and any Animator below the root.
        static List<string> SourceMaterials(VRCAvatarDescriptor descriptor)
        {
            var controllers = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                .Select(l => l.animatorController)
                .Concat(descriptor.GetComponentsInChildren<Animator>(true).Select(a => a.runtimeAnimatorController));
            return Paths(descriptor.gameObject, controllers);
        }

        // What the ChilloutVR upload hook locks: the same, with the avatar's override controller.
        static List<string> ConvertedMaterials(GameObject root)
        {
            var cvr = root.GetComponent<CVRAvatar>();
            var controllers = root.GetComponentsInChildren<Animator>(true).Select(a => a.runtimeAnimatorController)
                .Append(cvr != null ? cvr.overrides : null);
            return Paths(root, controllers);
        }

        static List<string> Paths(GameObject root, IEnumerable<RuntimeAnimatorController> controllers)
        {
            var materials = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).ToList();
            foreach (var clip in controllers.Where(c => c != null).SelectMany(c => c.animationClips).Distinct())
            {
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    if (!binding.propertyName.StartsWith("m_Materials", StringComparison.Ordinal)) continue;
                    materials.AddRange(AnimationUtility.GetObjectReferenceCurve(clip, binding).Select(k => k.value as Material));
                }
            }
            return materials.Where(m => m != null).Select(AssetDatabase.GetAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                .Distinct().ToList();
        }

        static List<Material> Load(IEnumerable<string> paths) =>
            paths.Distinct().Select(AssetDatabase.LoadAssetAtPath<Material>).Where(m => m != null).ToList();

        // ------------------------------------------------------------------ plumbing

        static bool Bind()
        {
            var optimizer = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Thry.ThryEditor.ShaderOptimizer", false))
                .FirstOrDefault(t => t != null);
            if (optimizer == null) return false;
            const BindingFlags any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var bar = optimizer.GetNestedType("ProgressBar");
            if (bar == null) return false;
            noBar = Enum.ToObject(bar, 0);
            lockMaterials = optimizer.GetMethod("LockMaterials", any, null, new[] { typeof(IEnumerable<Material>), bar }, null);
            unlockMaterials = optimizer.GetMethod("UnlockMaterials", any, null, new[] { typeof(IEnumerable<Material>), bar }, null);
            isLocked = optimizer.GetMethod("IsMaterialLocked", any, null, new[] { typeof(Material) }, null);
            usesOptimizer = optimizer.GetMethod("IsShaderUsingThryOptimizer", any, null, new[] { typeof(Shader) }, null);
            settingsHash = optimizer.GetMethod("MaterialToShaderPropertyHash", any, null, new[] { typeof(Material) }, null);
            return lockMaterials != null && unlockMaterials != null && isLocked != null && usesOptimizer != null && settingsHash != null;
        }

        public static void Trigger() => Batch("trigger", () =>
        {
            var from = Directory.GetFiles(YapsShaderPatcher.SharedFolder, "Poiyomi*_YAPS_*.shader")
                .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
            var source = AssetDatabase.FindAssets("t:Material").Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<Material>)
                .FirstOrDefault(m => m != null && m.shader != null && m.shader.name.StartsWith("Hidden/Locked/.poiyomi"));
            if (from == null || source == null) { Say($"nothing to time: shader {from}, material {source}"); return; }
            string stamp = DateTime.Now.Ticks.ToString("x");
            string path = $"{YapsShaderPatcher.SharedFolder}/Probe_{stamp}.shader";
            // A new name and path: the cache keys on the text, and the path is in it.
            string text = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(from),
                @"^(\s*Shader\s+)""[^""]+""", $"$1\"Hidden/Locked/YAPS/probe{stamp}\"",
                System.Text.RegularExpressions.RegexOptions.Multiline);
            Say($"shader {from} ({text.Length} chars), material {source.name} on {source.shader.name}");
            var clock = Stopwatch.StartNew();
            void Step(string what) { Say($"{what}: {clock.ElapsedMilliseconds} ms"); clock.Restart(); }
            File.WriteAllText(path, text);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            Step("import");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            Step($"load ({shader?.name})");
            bool error = ShaderUtil.ShaderHasError(shader);
            Step($"has error ({error})");
            var bare = new Material(shader);
            Step("new Material(shader)");
            var copy = new Material(source);
            Step("new Material(source)");
            copy.shader = shader;
            Step("copy.shader = patched");
            copy.SetFloat("_YAPS_DentPower", 1f);
            copy.SetVector("_YAPS_SquashReach", Vector4.one);
            Step("set properties");
            string matPath = $"{YapsShaderPatcher.SharedFolder}/Probe_{stamp}.mat";
            AssetDatabase.CreateAsset(copy, matPath);
            Step("create asset");
            AssetDatabase.SaveAssets();
            Step("save assets");
            var second = new Material(source) { shader = shader };
            Step("second copy");
            UnityEngine.Object.DestroyImmediate(bare);
            UnityEngine.Object.DestroyImmediate(second);
            AssetDatabase.DeleteAsset(matPath);
            AssetDatabase.DeleteAsset(path);
            Step("cleaned up");
        });

        static void Batch(string stage, Action body)
        {
            Out.Clear();
            int code = 0;
            try
            {
                if (!Bind()) { Say("Poiyomi's optimizer was not found, or its methods changed"); code = 2; }
                else body();
            }
            catch (Exception e)
            {
                Say("FAILED: " + (e.InnerException ?? e));
                code = 1;
            }
            string dir = Arg("-poiOut") ?? Path.GetTempPath();
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"PoiLock-{stage}.txt"), Out.ToString());
            // -poiQuit closes a GUI run too, which times what the editor window does.
            if (Application.isBatchMode || Environment.GetCommandLineArgs().Contains("-poiQuit")) EditorApplication.Exit(code);
        }

        static IEnumerable<string> Scenes()
        {
            string file = Arg("-poiScenes");
            if (file == null || !File.Exists(file)) throw new ArgumentException("-poiScenes needs a file of scene paths");
            return File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
        }

        static void Say(string line)
        {
            Out.AppendLine(line);
            Debug.Log(Tag + line);
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
