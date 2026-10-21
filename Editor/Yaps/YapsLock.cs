// Poiyomi's own lock, run before YAPS patches, on copies of the user's materials.
//
// Thry 2.7x strips an unlocked Thry shader from the build, and the upload locks every
// material still unlocked. A patch of the unlocked uber shader was locked a second time at
// upload and did not compile. MarkLocked in YapsShaderPatcher stops that but ships the whole
// uber shader. So an unlocked source gets a stand-in here, locked through Thry, and the patch
// is made from the stand-in's locked shader: what a hand lock ships, with YAPS on top.
//
// A stand-in is named by everything its lock reads, so it is never locked twice. Relocking
// in place writes a second shader under the same name, and the old one cannot simply go:
// Thry puts every material with matching settings on the FIRST one's shader. Changed
// settings make a new stand-in instead, and a reconvert finds the old one and locks nothing.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsLock
    {
        public const string Folder = "Assets/YAPS/Generated/Locked";
        const string Category = "YAPS";
        // Changed when a stand-in must be made again for a reason the key cannot see.
        const string Version = "1";

        // Thry's own test: the property whose first attribute is the lock button, locked
        // when it defaults to 1. An older Thry left the 0 in what it locked, so the Hidden/
        // name every Thry lock gives counts too. Null for a shader that is not Thry's.
        public static bool? ThryLocked(Shader shader)
        {
            if (shader == null) return null;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                var attributes = shader.GetPropertyAttributes(i);
                if (attributes.Length > 0 && attributes[0] == "ThryShaderOptimizerLockButton")
                    return shader.GetPropertyDefaultFloatValue(i) == 1f || shader.name.StartsWith("Hidden/");
            }
            return null;
        }

        // Material properties the avatar's clips animate, by name and for every renderer at
        // once: a name animated elsewhere costs one uniform, a baked one kills a toggle. Thry
        // reads no clips, so a property left untagged is a constant after the lock.
        static HashSet<string> animated = new HashSet<string>();

        public static void Animate(Transform avatar, IEnumerable<AnimationClip> more = null)
        {
            var clips = avatar != null ? YapsSwapFollow.RunnableClips(avatar).AsEnumerable() : Enumerable.Empty<AnimationClip>();
            if (more != null) clips = clips.Concat(more);
            animated = new HashSet<string>();
            foreach (var clip in clips.Where(c => c != null).Distinct())
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!typeof(Renderer).IsAssignableFrom(binding.type) || !binding.propertyName.StartsWith("material.")) continue;
                    // "material._Color.r" animates _Color.
                    string name = binding.propertyName.Substring("material.".Length);
                    int dot = name.IndexOf('.');
                    animated.Add(dot < 0 ? name : name.Substring(0, dot));
                }
        }

        // Thry tags a texture, never its _ST.
        static List<string> AnimatedOn(Material m) => animated
            .Select(p => m.HasProperty(p) ? p
                : p.EndsWith("_ST") && m.HasProperty(p.Substring(0, p.Length - 3)) ? p.Substring(0, p.Length - 3) : null)
            .Where(p => p != null).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();

        // Variants are left alone: Thry locks a variant's PARENT, which is the user's own.
        // SPS has its own deform in the shader, and DPS goes to Simple Lit.
        static bool Wants(Material m)
        {
            if (m == null || ThryLocked(m.shader) != false || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(m))) return false;
#if UNITY_2022_1_OR_NEWER
            if (m.isVariant) return false;
#endif
            var origin = YapsLegacyMap.Detect(m, out _);
            return origin != YapsLegacyMap.Origin.SPS && origin != YapsLegacyMap.Origin.DPS && origin != YapsLegacyMap.Origin.YAPS;
        }

        // Everything the locked text follows from: the material's own data, which properties
        // stay live, and the shader with its includes. Not the material's GUID, so two
        // materials with the same name and settings share one stand-in.
        // ponytail: a texture's size and sampler reach the locked text without moving this
        // key; a reimported texture keeps the old stand-in until the material changes.
        static string Key(Material source, List<string> props) => YapsShaderPatcher.Hash(
            Version + "\0" + EditorJsonUtility.ToJson(source) + "\0" + string.Join(",", props) + "\0" +
            AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(source.shader)), 8);

        static string PathFor(string key) => Folder + "/" + key + ".mat";

        static Material Existing(string key)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(PathFor(key));
            return m != null && ThryLocked(m.shader) == true ? m : null;
        }

        // Keys whose lock failed this session and why, so a slot loop does not pay for it twice.
        static readonly Dictionary<string, string> Failed = new Dictionary<string, string>();

        // The locked stand-in to patch from, or the material itself.
        public static Material Basis(Material m)
        {
            if (!Wants(m)) return m;
            KeyTime.Start();
            string key = Key(m, AnimatedOn(m));
            KeyTime.Stop();
            LoadTime.Start();
            var basis = Existing(key);
            LoadTime.Stop();
            return basis ?? m;
        }

        // Corpus 423: Basis was 1188 s of one avatar's 1190 s slot loop. Which half, keying or
        // loading the stand-in, is what the squash lap reports.
        internal static readonly System.Diagnostics.Stopwatch KeyTime = new System.Diagnostics.Stopwatch();
        internal static readonly System.Diagnostics.Stopwatch LoadTime = new System.Diagnostics.Stopwatch();

        // A copy carries its basis's list of every material on that locked shader, and Thry's
        // Unlock of the copy trashes the shader it wears unless one of those still wears it:
        // none do, so it trashed the patch every other copy shares. Empty, Thry keeps it.
        // And Thry's rename suffix is the material's name unless tagged: a copy saved under
        // its own name would unlock without its renamed values and relock off the clips.
        public static void Stamp(Material copy, Material source)
        {
            if (copy == null) return;
            if (copy.GetTag("AllLockedGUIDS", false, "") != "") copy.SetOverrideTag("AllLockedGUIDS", "");
            if (source != null && copy.GetTag("thry_rename_suffix", false, "") == "")
                copy.SetOverrideTag("thry_rename_suffix", source.GetTag("thry_rename_suffix", false, source.name));
        }

        // Makes and locks, in one Thry call, a stand-in for every unlocked source that has
        // none. Never inside StartAssetEditing: Thry finds its new shaders by name right
        // after importing them, and a deferred import leaves the material unlocked.
        public static void Prepare(IEnumerable<Material> sources)
        {
            var todo = new Dictionary<string, (Material Source, List<string> Props)>();
            foreach (var m in sources.Where(Wants).Distinct())
            {
                var props = AnimatedOn(m);
                string key = Key(m, props);
                if (!Failed.ContainsKey(key) && !todo.ContainsKey(key) && Existing(key) == null) todo[key] = (m, props);
            }
            if (todo.Count == 0) return;
            var call = LockCall(out string missing);
            if (call == null)
            {
                foreach (string key in todo.Keys) Failed[key] = missing;
                return;
            }

            YapsNativeBuilder.EnsureFolderPublic(Folder);
            var made = new List<Material>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var pair in todo)
                {
                    var (source, props) = pair.Value;
                    var standIn = new Material(source.shader);
                    EditorUtility.CopySerialized(source, standIn);
                    // Thry's folder for it is named after it.
                    standIn.name = pair.Key;
                    foreach (string p in props)
                        if (standIn.GetTag(p + "Animated", false, "") == "") standIn.SetOverrideTag(p + "Animated", "1");
                    // A property the author had Thry rename keeps the name the clips bind.
                    if (standIn.GetTag("thry_rename_suffix", false, "") == "") standIn.SetOverrideTag("thry_rename_suffix", source.name);
                    // Before the lock: TPS switched off on a locked copy is still compiled in.
                    if (YapsLegacyMap.Detect(source, out _) == YapsLegacyMap.Origin.TPS)
                        YapsNativeBuilder.SwitchOffLegacyDeform(standIn, YapsLegacyMap.Origin.TPS);
                    AssetDatabase.CreateAsset(standIn, PathFor(pair.Key));
                    made.Add(standIn);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }

            string threw = null;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try { call(made); }
            catch (Exception e) { threw = (e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e).Message; }
            // Thry's own save waits for an editor update a batch run may never reach.
            AssetDatabase.SaveAssets();
            Debug.Log($"[AvatarBridge] Poiyomi lock for YAPS: {made.Count} stand-ins in {clock.ElapsedMilliseconds} ms");

            foreach (var m in made.Where(m => ThryLocked(m.shader) != true).ToList())
            {
                Failed[m.name] = threw != null ? "Poiyomi's lock stopped: " + threw : "Poiyomi's lock left it unlocked";
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(m));
            }
        }

        // Stand-ins nothing needs any more, each with its locked shader, to the trash. One is
        // needed while a material wears its shader (Thry gives matching settings the first
        // lock's shader) or names it as the source of its YAPS patch, which Update every YAPS
        // shader patches from again. One for settings since changed is neither once its avatar
        // is converted again. Null when there are no stand-ins.
        public static string PruneUnused(Func<int, bool> confirm = null, string folder = Folder)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return null;
            var sets = Directory.GetFiles(folder, "*.mat").Select(f => f.Replace('\\', '/')).Select(mat =>
            {
                // Thry writes a material's locked shader into a folder named after the material.
                string dir = folder + "/OptimizedShaders/" + Path.GetFileNameWithoutExtension(mat);
                var shaders = AssetDatabase.IsValidFolder(dir)
                    ? AssetDatabase.FindAssets("t:Shader", new[] { dir }).Select(AssetDatabase.GUIDToAssetPath).ToList()
                    : new List<string>();
                return (mat, dir, shaders);
            }).ToList();
            if (sets.Count == 0) return null;
            var inSets = new HashSet<string>(sets.SelectMany(s => s.shaders.Append(s.mat)), StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var named = new HashSet<string>(StringComparer.Ordinal);
            void Note(Material m)
            {
                if (m == null || m.shader == null || inSets.Contains(AssetDatabase.GetAssetPath(m))) return;
                used.Add(AssetDatabase.GetAssetPath(m.shader));
                if (YapsShaderPatcher.SourceShaderOf(m) is string source) named.Add(source);
            }
            var guids = AssetDatabase.FindAssets("t:Material").Concat(AssetDatabase.FindAssets("t:Scene"))
                .Concat(AssetDatabase.FindAssets("t:Prefab")).Distinct().ToArray();
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("YAPS", $"Checking what uses each locked copy ({i + 1} of {guids.Length})",
                            (i + 1) / (float) guids.Length)) return "Stopped. No locked copy was removed.";
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (inSets.Contains(path)) continue;
                    used.UnionWith(AssetDatabase.GetDependencies(path, false));
                    if (path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) Note(AssetDatabase.LoadAssetAtPath<Material>(path));
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>()) Note(m);

            var unused = sets.Where(s => !used.Contains(s.mat) && !s.shaders.Any(p =>
                used.Contains(p) || named.Contains(AssetDatabase.LoadAssetAtPath<Shader>(p)?.name ?? ""))).ToList();
            int kept = sets.Count - unused.Count;
            if (unused.Count == 0) return $"Nothing to remove: {BridgeElements.Count(kept, "locked Poiyomi copy is", "locked Poiyomi copies are")} in use.";
            if (confirm != null && !confirm(unused.Count)) return "No locked copy was removed.";
            var failed = new List<string>();
            AssetDatabase.MoveAssetsToTrash(unused.SelectMany(s => AssetDatabase.IsValidFolder(s.dir) ? new[] { s.mat, s.dir } : new[] { s.mat }).ToArray(), failed);
            return $"Moved {BridgeElements.Count(unused.Count, "unused locked Poiyomi copy", "unused locked Poiyomi copies")} to the trash, " +
                   $"with {(unused.Count == 1 ? "its" : "their")} shaders; {kept} in use kept." +
                   (failed.Count > 0 ? $" {BridgeElements.Count(failed.Count, "file", "files")} could not be moved: {string.Join(", ", failed.Take(3).Select(Path.GetFileName))}." : "");
        }

        static BridgeReport warnedFor;
        static readonly HashSet<Material> warned = new HashSet<Material>();

        // Once per material and conversion: the patch of an unlocked Thry shader works, marked
        // locked by the patcher, but carries every Poiyomi feature into the upload.
        public static void WarnIfUnlocked(Material asked, Material basis, BridgeReport report)
        {
            if (report == null || ThryLocked(basis.shader) != false) return;
            if (report != warnedFor) { warnedFor = report; warned.Clear(); }
            if (!warned.Add(asked)) return;
            string why = string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asked)) ? "it is not a saved material"
#if UNITY_2022_1_OR_NEWER
                : asked.isVariant ? "it is a material variant, and Poiyomi locks a variant through its parent"
#endif
                : YapsLegacyMap.Detect(asked, out _) == YapsLegacyMap.Origin.SPS ? "VRCFury's deform is built into its shader"
                : YapsLegacyMap.Detect(asked, out _) == YapsLegacyMap.Origin.DPS ? "it carries DPS's properties, which YAPS never locks"
                : Failed.TryGetValue(Key(asked, AnimatedOn(asked)), out string failed) ? failed
                : "Poiyomi's lock did not run";
            report.Warning(Category, $"Poiyomi's unlocked shader ships under YAPS on \"{asked.name}\"",
                $"{why}, so YAPS patched the unlocked shader and marked it locked so the upload leaves it " +
                "alone. It works, but every Poiyomi feature uploads with it. Lock the material in " +
                "Poiyomi and convert again.");
        }

        // Thry by reflection: its assembly is not always there. LockMaterials in 2.7x, the
        // older SetLockedForAllMaterials before it; any other argument gets a quiet default.
        static Action<List<Material>> lockCall;
        static string lockMissing;

        internal static Action<List<Material>> LockCall(out string missing)
        {
            if (lockCall == null && lockMissing == null) Bind();
            missing = lockMissing;
            return lockCall;
        }

        static void Bind()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Thry.ThryEditor.ShaderOptimizer", false) ?? a.GetType("Thry.ShaderOptimizer", false))
                .FirstOrDefault(t => t != null);
            if (type == null) { lockMissing = "Poiyomi's editor is not in the project"; return; }
            bool TakesMaterials(MethodInfo m) => m.GetParameters() is var p && p.Length > 0
                                                 && p[0].ParameterType.IsAssignableFrom(typeof(List<Material>));
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
            var method = methods.FirstOrDefault(m => m.Name == "LockMaterials" && TakesMaterials(m))
                         ?? methods.FirstOrDefault(m => m.Name == "SetLockedForAllMaterials" && TakesMaterials(m));
            if (method == null) { lockMissing = "this version of Poiyomi's editor has no lock to call"; return; }
            var parameters = method.GetParameters();
            lockCall = materials =>
            {
                var args = new object[parameters.Length];
                args[0] = materials;
                for (int i = 1; i < parameters.Length; i++)
                {
                    var t = parameters[i].ParameterType;
                    // The lock state, and no progress bar, dialog or cancel.
                    args[i] = t == typeof(int) ? 1
                        : t == typeof(bool) ? (object)false
                        : t.IsEnum ? Enum.ToObject(t, 0)
                        : t.IsValueType ? Activator.CreateInstance(t) : null;
                }
                method.Invoke(null, args);
            };
        }
    }
}
#endif
