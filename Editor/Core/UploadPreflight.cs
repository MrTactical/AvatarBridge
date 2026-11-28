// What the CCK upload panel will refuse. CCK 4 hides Upload while any of its error-level
// validation rules fails; these are its avatar rules, each gathering what its pipeline gathers.
// Plus one the panel never checks: the upload saves a prefab, so references to unsaved
// non-avatar objects arrive as nothing. With CCK 4 the Toolkit runs the CCK's own validation in
// place of the shared rules, which still differ from it in what they reach.
#if CVR_CCK_EXISTS
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class UploadPreflight
    {
        const string Category = "Upload";
        const string FixInPanel = "The CCK's Control Panel shows an Auto Fix button beside it.";

        public static void Run(BridgeReport report, CVRAvatar avatar)
        {
            if (avatar == null) return;
            var root = avatar.gameObject;
            int before = report.Entries.Count;
            var refs = References(root).ToList();
            Shared(report, root, refs);
            Own(report, root, refs);
            if (report.Entries.Count == before)
                report.Converted(Category, "Nothing the CCK's upload check refuses",
                    "No missing scripts or broken shaders, an Animator on the root, textures, sounds, models and " +
                    "particles set up the way it asks, and nothing the upload would drop.");
        }

        // CCK 4's validation, found by name: older CCKs have none.
        static readonly MethodInfo Validate = System.AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("CVR.CCKEditor.Validations.Validator")).FirstOrDefault(t => t != null)
            ?.GetMethod("Validate", BindingFlags.Public | BindingFlags.Static);

        // With CCK 4, its own validation replaces the shared rules. It runs across editor
        // frames, so its findings take the place of a waiting row and done is called then.
        // Without it, or without an avatar CVRAssetInfo, this is Run, done is never called and it returns false.
        public static bool RunWithCck(BridgeReport report, CVRAvatar avatar, System.Action done)
        {
            var info = avatar != null ? avatar.GetComponent<CVRAssetInfo>() : null;
            Task task = null;
            if (Validate != null && info != null && info.type == CVRAssetInfo.AssetType.Avatar)
                try
                {
                    var args = new object[Validate.GetParameters().Length];
                    args[0] = info;
                    task = Validate.Invoke(null, args) as Task;
                }
                catch (System.Exception e) { Debug.LogWarning("[AvatarBridge] The CCK's validation did not start: " + (e.InnerException ?? e).Message); }
            if (task == null) { Run(report, avatar); return false; }

            Own(report, avatar.gameObject, References(avatar.gameObject).ToList());
            report.Skipped(Category, "The CCK's own check is running", "Its findings appear here when it ends.");
            var waiting = report.Entries[report.Entries.Count - 1];
            var clock = System.Diagnostics.Stopwatch.StartNew();
            void Wait()
            {
                // A check that never ends would hold the row for the whole session.
                bool late = clock.Elapsed.TotalSeconds > 120;
                if (!task.IsCompleted && !late) return;
                EditorApplication.update -= Wait;
                var found = new BridgeReport { Quiet = report.Quiet };
                try
                {
                    if (late || task.IsFaulted || task.IsCanceled)
                        throw new System.Exception(late ? "it did not end in two minutes" : task.Exception?.GetBaseException().Message ?? "it was cancelled");
                    Findings(found, task.GetType().GetProperty("Result")?.GetValue(task));
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[AvatarBridge] The CCK's validation failed, the Toolkit's own rules ran instead: " + e.Message);
                    found = new BridgeReport { Quiet = report.Quiet };
                    if (avatar != null) Shared(found, avatar.gameObject, References(avatar.gameObject).ToList());
                }
                // In the log, never the report, as the conversion's step times are.
                Debug.Log($"[AvatarBridge] The CCK's validation took {clock.ElapsedMilliseconds} ms");
                int at = report.Entries.IndexOf(waiting);
                if (at >= 0) report.Entries.RemoveAt(at);
                report.Entries.InsertRange(at >= 0 ? at : report.Entries.Count, found.Entries);
                done?.Invoke();
            }
            EditorApplication.update += Wait;
            return true;
        }

        // The CCK's errors in its own words, the panel's names beside them. Its warnings never
        // stop the upload and the panel lists them, so they are only counted.
        static void Findings(BridgeReport report, object result)
        {
            int warnings = 0;
            bool refused = false;
            foreach (var r in result?.GetType().GetProperty("StepResults")?.GetValue(result) as IEnumerable ?? new object[0])
            {
                var type = r.GetType();
                string severity = type.GetProperty("Severity")?.GetValue(r)?.ToString();
                if (severity == "Warning") warnings += type.GetProperty("IssueCount")?.GetValue(r) as int? ?? 1;
                if (severity != "Error") continue;
                refused = true;
                string message = (type.GetProperty("Message")?.GetValue(r) as string ?? "").Trim();
                int stop = message.IndexOf(". ");
                string first = (stop < 0 ? message : message.Substring(0, stop)).TrimEnd('.');
                string rest = stop < 0 ? "" : message.Substring(stop + 2);
                var names = (type.GetProperty("RootObjects")?.GetValue(r) as IEnumerable)?.OfType<Object>()
                    .Where(o => o != null).Select(o => o.name).Distinct().ToList() ?? new List<string>();
                bool fixable = type.GetProperty("AutoFix")?.GetValue(r) != null;
                var detail = new List<string>();
                if (names.Count > 0) detail.Add(AvatarSurvey.Few(names) + ".");
                if (rest.Length > 0) detail.Add(rest);
                if (fixable) detail.Add(FixInPanel);
                report.Warning(Category, "The CCK will not upload this" + (first.Length > 0 ? ": " + char.ToLower(first[0]) + first.Substring(1) : ""),
                    string.Join(" ", detail));
            }
            if (!refused)
                report.Converted(Category, "Nothing the CCK's upload check refuses",
                    "Its own validation, the one its Control Panel runs before Upload, found no errors.");
            if (warnings > 0)
                report.Approximated(Category, $"The CCK warns about {BridgeElements.Count(warnings, "thing", "things")}",
                    "None of them stops the upload. Its Control Panel lists them.");
        }

        // The rules the CCK runs too.
        static void Shared(BridgeReport report, GameObject root, List<(Component on, string name, Object value)> refs)
        {
            var missing = root.GetComponentsInChildren<Transform>(true)
                .Where(t => Counted(t) && GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
                .Select(t => t.name).ToList();
            if (missing.Count > 0)
                report.Warning(Category, $"The CCK will not upload this: {BridgeElements.Count(missing.Count, "object has", "objects have")} a missing script",
                    $"{AvatarSurvey.Few(missing)}. A package the avatar uses is not installed. Install it, or remove the " +
                    "scripts once you know what they did: the CCK's Control Panel shows an Auto Fix button beside it that removes them.");

            if (root.GetComponent<Animator>() == null)
                report.Warning(Category, "The CCK will not upload this: no Animator on the avatar's root", "Add one and give it the avatar's rig.");

            // Asked by name: CCKs before 4 have neither check.
            var common = typeof(CVRAvatar).Assembly.GetType("ABI.CCK.Scripts.CVRCommon");
            if (common?.GetMethod("IsUnityVersionSupported")?.Invoke(null, null) is false)
                report.Warning(Category, $"The CCK will not upload this: it does not support Unity {Application.unityVersion}",
                    $"It needs Unity {common.GetField("MinimumUnityVersion")?.GetValue(null)} to {common.GetField("MaximumUnityVersion")?.GetValue(null)}. Open the project in one of those.");
            if (common?.GetMethod("IsSupportedBuildTarget")?.Invoke(null, null) is false)
                report.Warning(Category, $"The CCK will not upload this: it does not build for {EditorUserBuildSettings.activeBuildTarget}",
                    $"Switch the build target to {string.Join(" or ", (common.GetField("SupportedBuildTargets")?.GetValue(null) as System.Collections.IEnumerable)?.Cast<object>() ?? new object[0])} in Build Settings. " + FixInPanel);

            var reached = refs.Select(r => (r.on, r.value)).Concat(Keys(root)).ToList();
            var uses = reached.Where(r => r.value is Material).Select(r => (r.on, material: (Material) r.value))
                .Where(u => u.material.shader != null).Distinct().ToList();

            Refuse(report, uses.Where(u => u.material.shader.name == "Hidden/InternalErrorShader").Select(u => u.material.name),
                "material draws", "materials draw", "with Unity's error shader",
                "Its shader is missing or failed to compile. Install the shader, or pick another.");

            // The CCK walks each material once, from the first component that reaches it, and asks
            // for streaming only when that is a mesh renderer: a particle or trail renderer's
            // textures may not stream. Counted as a mesh's if any mesh reaches it, stricter than the
            // CCK for a material a mesh shares with a particle effect, where the advice costs a warning.
            var textures = uses.Where(u => Editable(AssetDatabase.GetAssetPath(u.material)))
                .SelectMany(u => ShaderTextures(u.material).Select(t => (t, mesh: u.on is MeshRenderer || u.on is SkinnedMeshRenderer)))
                .Where(x => Editable(AssetDatabase.GetAssetPath(x.t)))
                .GroupBy(x => x.t).Select(g => (texture: g.Key, mesh: g.Any(x => x.mesh))).ToList();
            Refuse(report, textures.Where(x => x.texture.width > 8192 || x.texture.height > 8192).Select(x => x.texture.name),
                "texture is", "textures are", "larger than 8192",
                "ChilloutVR loads 8192 at most. Set Max Size to 8192 or lower in the import settings. " + FixInPanel);
            Refuse(report, textures.Where(x => x.mesh && !x.texture.streamingMipmaps).Select(x => x.texture.name),
                "texture on a mesh does", "textures on a mesh do", "not stream mipmaps",
                "ChilloutVR streams the mipmaps of what a mesh draws. Tick Streaming Mipmaps in the import settings. " +
                "Particle and trail textures are the other way round: the CCK asks them not to stream. " + FixInPanel);
            // Asked of every mesh texture, streaming or not, as the CCK asks it.
            Refuse(report, textures.Where(x => x.mesh && x.texture.streamingMipmapsPriority != 0).Select(x => x.texture.name),
                "texture on a mesh has", "textures on a mesh have", "a streaming priority other than 0",
                "Set the streaming Priority to 0 in the import settings. " + FixInPanel);
            Refuse(report, textures.Where(x => x.texture.isReadable).Select(x => x.texture.name),
                "texture is", "textures are", "readable from scripts",
                "Untick Read/Write in the import settings. " + FixInPanel);

            var clips = reached.Select(r => r.value).OfType<AudioClip>().Distinct().ToList();
            Refuse(report, clips.Where(c => !c.loadInBackground).Select(c => c.name),
                "sound does", "sounds do", "not load in the background",
                "Tick Load In Background in the import settings. " + FixInPanel);
            Refuse(report, clips.Where(c => c.length > 1f && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(c)) is AudioImporter a
                    && a.defaultSampleSettings.loadType == AudioClipLoadType.DecompressOnLoad).Select(c => c.name),
                "sound longer than a second decompresses", "sounds longer than a second decompress", "on load",
                "Set Load Type to Compressed In Memory in the import settings. " + FixInPanel);

            Refuse(report, reached.Select(r => r.value).OfType<Mesh>().Select(m => AssetDatabase.GetAssetPath(m)).Where(CalculatesBlendShapeNormals)
                    .Select(p => System.IO.Path.GetFileNameWithoutExtension(p)),
                "model calculates", "models calculate", "blend shape normals without the legacy option",
                "Tick Legacy Blend Shape Normals in the model's import settings, or set Blend Shape Normals to None. " + FixInPanel);

            Refuse(report, root.GetComponentsInChildren<ParticleSystem>(true).Where(p => Counted(p) && LongPrewarm(p)).Select(p => p.name),
                "particle system prewarms", "particle systems prewarm", "a lifetime over 100 seconds",
                "Untick Prewarm, or set Start Lifetime to 100 or less. " + FixInPanel);
        }

        // The rules the panel never shows.
        static void Own(BridgeReport report, GameObject root, List<(Component on, string name, Object value)> refs)
        {
            Unsaved(report, root, refs);
#if AVATARBRIDGE_YAPS
            YapsNativeBuilder.ReportLostShaders(report, root);
            // Read back now as well as at the build: a depth map was overwritten with garbage
            // minutes after a clean conversion.
            foreach (var worn in root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(YapsJelly.Worn + " ")))
                if (YapsJelly.Unsaved(worn.gameObject) is string lost)
                    report.Warning(Category, $"The live tissue on {worn.name.Substring(YapsJelly.Worn.Length + 1)} is damaged on disk",
                        $"Read back, {lost}, so it would not work in game. Convert again, and send Diagnostics.md and the avatar's Jelly folder with a bug report if it comes back.");
#endif
        }

        static void Refuse(BridgeReport report, IEnumerable<string> names, string one, string many, string what, string fix)
        {
            var list = names.Distinct().ToList();
            if (list.Count > 0)
                report.Warning(Category, $"The CCK will not upload this: {BridgeElements.Count(list.Count, one, many)} {what}",
                    $"{AvatarSurvey.Few(list)}. {fix}");
        }

        // Every object reference on every component, once: the CCK's walk and the unsaved check both read it.
        static IEnumerable<(Component on, string name, Object value)> References(GameObject root)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || !Counted(component)) continue;
                var it = new SerializedObject(component).GetIterator();
                while (it.Next(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null)
                        yield return (component, it.name, it.objectReferenceValue);
            }
        }

        // The CCK skips a component on an object tagged EditorOnly, and its own editor-only ones,
        // asked by name as CCKs before 4 have no such interface.
        static bool Counted(Component c) => !c.gameObject.CompareTag("EditorOnly") && c.GetType().GetInterface("ICCKEditorOnly") == null;

        // What an animation swaps in, found as the CCK finds it: a material on the component the
        // binding names, anything else on the animator. The overrides' replaced clips count too.
        static IEnumerable<(Component on, Object value)> Keys(GameObject root)
        {
            var avatar = root.GetComponent<CVRAvatar>();
            foreach (var animator in root.GetComponentsInChildren<Animator>(true).Where(Counted))
            {
                var clips = animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.animationClips.ToList() : new List<AnimationClip>();
                if (animator.gameObject == root && avatar.overrides != null)
                {
                    var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                    avatar.overrides.GetOverrides(pairs);
                    clips.AddRange(avatar.overrides.animationClips.Concat(pairs.SelectMany(p => new[] { p.Key, p.Value })));
                }
                foreach (var clip in clips.Where(c => c != null).Distinct())
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    {
                        var on = binding.type == null ? null : animator.transform.Find(binding.path)?.GetComponent(binding.type);
                        foreach (var key in AnimationUtility.GetObjectReferenceCurve(clip, binding))
                            if (key.value is Material ? on != null : key.value != null) yield return (key.value is Material ? on : animator, key.value);
                    }
            }
        }

        // The shader's texture properties only: a slot left from an earlier shader is not checked.
        static IEnumerable<Texture2D> ShaderTextures(Material m)
        {
            for (int i = 0; i < ShaderUtil.GetPropertyCount(m.shader); i++)
            {
                if (ShaderUtil.GetPropertyType(m.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                string name = ShaderUtil.GetPropertyName(m.shader, i);
                if (m.HasProperty(name) && m.GetTexture(name) is Texture2D t) yield return t;
            }
        }

        // The CCK leaves alone what nobody can change: built-in and package assets, and font atlases.
        static bool Editable(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) return false;
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            return type != typeof(Font) && (type == null || !type.Name.EndsWith("FontAsset"));
        }

        static readonly PropertyInfo LegacyBlendShapeNormals = typeof(ModelImporter).GetProperty(
            "legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        static bool CalculatesBlendShapeNormals(string path) =>
            !string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is ModelImporter importer
            && importer.importBlendShapeNormals == ModelImporterNormals.Calculate
            && LegacyBlendShapeNormals?.GetValue(importer) is false;

        static bool LongPrewarm(ParticleSystem ps)
        {
            var main = ps.main;
            if (!main.prewarm) return false;
            var life = main.startLifetime;
            return (life.mode == ParticleSystemCurveMode.TwoConstants ? life.constantMax : life.constant) > 100f;
        }

        // A reference to something that is neither a saved asset nor part of
        // the avatar. The prefab the upload saves keeps none of them.
        static void Unsaved(BridgeReport report, GameObject root, List<(Component on, string name, Object value)> refs)
        {
            var lost = new List<string>();
            var outside = new List<string>();
            foreach (var (component, name, value) in refs)
            {
                // A prefab instance's own bookkeeping points at a scene object that is no asset.
                if (name == "m_PrefabInstance" || name == "m_PrefabAsset" || name == "m_CorrespondingSourceObject") continue;
                // So is the root's parent: the upload saves the avatar alone, and nothing reads it.
                if (name == "m_Father" && component.transform == root.transform) continue;
                if (EditorUtility.IsPersistent(value)) continue;
                var go = value as GameObject ?? (value as Component)?.gameObject;
                if (go == null) lost.Add($"{value.name} ({value.GetType().Name}) on {component.name}");
                else if (!go.transform.IsChildOf(root.transform)) outside.Add($"{go.name}, from {component.name}");
            }
            foreach (var controller in Controllers(root))
                lost.AddRange(controller.animationClips.Where(c => c != null && !EditorUtility.IsPersistent(c))
                    .Select(c => $"{c.name} (AnimationClip) in {controller.name}"));

            if (lost.Count > 0)
                report.Warning(Category, $"{BridgeElements.Count(lost.Distinct().Count(), "thing the avatar uses is", "things the avatar uses are")} not saved, so the upload drops them",
                    $"{AvatarSurvey.Few(lost.Distinct())}. Made in memory and never saved as an asset, so the prefab the " +
                    "upload builds holds nothing there. Whatever made them needs to save them.");
            if (outside.Count > 0)
                report.Warning(Category, $"{BridgeElements.Count(outside.Distinct().Count(), "reference points", "references point")} outside the avatar, so the upload drops them",
                    $"{AvatarSurvey.Few(outside.Distinct())}. The upload keeps only the avatar's own hierarchy. Move the " +
                    "object under the avatar, or point the reference at one that is.");
        }

        static IEnumerable<RuntimeAnimatorController> Controllers(GameObject root)
        {
            var found = root.GetComponentsInChildren<Animator>(true).Select(a => a.runtimeAnimatorController).ToList();
            var avatar = root.GetComponent<CVRAvatar>();
            if (avatar != null && avatar.overrides != null) found.Add(avatar.overrides.runtimeAnimatorController);
            return found.Where(c => c != null).Distinct();
        }
    }
}
#endif
