// Runs the upload preflight and the CCK's own validation on one avatar and checks they refuse the
// same things. On a copy of the prefab, never saved, it adds two particle systems, one drawing a
// texture that streams and one a texture that does not, a cube drawing another that does not, a
// cube tagged EditorOnly drawing a fourth that does not, and a cube drawing a texture made here at
// streaming priority 5, not streaming, each from a material in Assets/__Preflight (cleared from
// outside after). The cube's texture must be refused by both, the particles' and the EditorOnly
// cube's by neither, and the made texture's priority by both. Then the Toolkit's route on the same
// avatar: each CCK error once, none of the shared rules, and the validation's time in the log.
//
// -executeMethod AvatarBridge.Regression.PreflightCckProbe.Run -pfPrefab <prefab>
// Batch. No -quit: it exits itself, 0 when the two agree.

#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ABI.CCK.Components;
using CVR.CCKEditor.Localization;
using CVR.CCKEditor.Validations;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class PreflightCckProbe
    {
        const string Folder = "Assets/__Preflight";
        // The CCK's error message key against the words the preflight's refusal carries.
        static readonly (string key, string words)[] Rules =
        {
            ("MISSING_SCRIPTS", "missing script"), ("AVATAR_ANIMATOR_MISSING", "no Animator"),
            ("MISSING_OR_BROKEN_SHADERS", "error shader"), ("TEXTURE_TOO_LARGE", "larger than 8192"),
            ("REQUIRES_STREAMING_MIPMAPS", "not stream mipmaps"), ("REQUIRES_ZERO_PRIORITY_STREAMING_MIPMAPS", "priority other than 0"),
            ("REQUIRES_NON_READABLE", "readable from scripts"), ("AUDIO_LOAD_IN_BACKGROUND", "load in the background"),
            ("AUDIO_DECOMPRESS_ON_LOAD_TOO_LONG", "on load"), ("NONE_LEGACY_BLENDSHAPES", "blend shape normals"),
            ("PARTICLE_PREWARM_LONG_LIFETIME", "prewarm"), ("UNSUPPORTED_UNITY_VERSION_ERROR", "does not support Unity"),
            ("UNSUPPORTED_BUILD_TARGET_ERROR", "does not build for"),
        };
        static Task<ValidationPipelineResult> pending;
        static BridgeReport report;
        static CVRAvatar target;
        static string streams, still, onMesh, hidden, ranked;

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var made = (GameObject) PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Arg("-pfPrefab")));
            var avatar = target = made.GetComponentInChildren<CVRAvatar>(true);
            var info = avatar.GetComponent<CVRAssetInfo>() ?? avatar.gameObject.AddComponent<CVRAssetInfo>();
            info.type = CVRAssetInfo.AssetType.Avatar;

            // Three textures the avatar does not use, with mipmaps and nothing else the CCK refuses.
            var used = new HashSet<string>(avatar.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null)
                .SelectMany(m => m.GetTexturePropertyNames().Select(m.GetTexture)).Where(t => t != null).Select(AssetDatabase.GetAssetPath));
            var spare = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).Distinct()
                .Where(p => !used.Contains(p)).Select(p => (path: p, importer: AssetImporter.GetAtPath(p) as TextureImporter))
                .Where(x => x.importer != null && x.importer.mipmapEnabled && !x.importer.isReadable && x.importer.maxTextureSize <= 8192
                            && x.importer.textureShape == TextureImporterShape.Texture2D && x.importer.streamingMipmapsPriority == 0).ToList();
            var on = spare.Where(x => x.importer.streamingMipmaps).Select(x => x.path).FirstOrDefault();
            var off = spare.Where(x => !x.importer.streamingMipmaps).Select(x => x.path).Take(3).ToList();
            if (on == null || off.Count < 3) { Debug.Log("[PreflightCck] FAIL: no spare textures to test with"); EditorApplication.Exit(2); return; }

            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__Preflight");
            Material Mat(string path, string name)
            {
                var m = new Material(Shader.Find("Unlit/Texture")) { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path) };
                AssetDatabase.CreateAsset(m, $"{Folder}/{name}.mat");
                return m;
            }
            void Particles(string path, string name)
            {
                var go = new GameObject(name);
                go.transform.SetParent(avatar.transform, false);
                go.AddComponent<ParticleSystem>();
                go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Mat(path, name);
            }
            Particles(on, "Preflight particles streaming");
            Particles(off[0], "Preflight particles not streaming");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.transform.SetParent(avatar.transform, false);
            cube.GetComponent<Renderer>().sharedMaterial = Mat(off[1], "Preflight cube not streaming");
            GameObject Cube(string name)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                c.name = name;
                UnityEngine.Object.DestroyImmediate(c.GetComponent<Collider>());
                c.transform.SetParent(avatar.transform, false);
                return c;
            }
            var editorOnly = Cube("Preflight cube EditorOnly");
            editorOnly.tag = "EditorOnly";
            editorOnly.GetComponent<Renderer>().sharedMaterial = Mat(off[2], "Preflight cube EditorOnly");
            // Made here, so no import setting of the project's own is touched.
            var rankedTex = new Texture2D(4, 4, TextureFormat.RGBA32, true) { name = "Preflight priority 5" };
            AssetDatabase.CreateAsset(rankedTex, $"{Folder}/Preflight priority 5.asset");
            var so = new SerializedObject(rankedTex);
            so.FindProperty("m_StreamingMipmapsPriority").intValue = 5;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Cube("Preflight cube priority 5").GetComponent<Renderer>().sharedMaterial = Mat($"{Folder}/Preflight priority 5.asset", "Preflight cube priority 5");
            streams = AssetDatabase.LoadAssetAtPath<Texture2D>(on).name;
            still = AssetDatabase.LoadAssetAtPath<Texture2D>(off[0]).name;
            onMesh = AssetDatabase.LoadAssetAtPath<Texture2D>(off[1]).name;
            hidden = AssetDatabase.LoadAssetAtPath<Texture2D>(off[2]).name;
            ranked = rankedTex.name;
            Debug.Log($"[PreflightCck] particles draw {streams} (streams) and {still} (does not); the cube draws {onMesh} (does not), " +
                      $"the EditorOnly cube {hidden} (does not), the priority cube {ranked} (priority {rankedTex.streamingMipmapsPriority}, streams {rankedTex.streamingMipmaps})");
            // The version checks are asked of the CCK by name: proven to resolve here.
            Debug.Log($"[PreflightCck] {(typeof(CVRAvatar).Assembly.GetType("ABI.CCK.Scripts.CVRCommon") == typeof(ABI.CCK.Scripts.CVRCommon) ? "ok   " : "WRONG")} the CCK's version checks resolve by name");

            report = new BridgeReport();
            UploadPreflight.Run(report, avatar);
            pending = Validator.Validate(info);
            EditorApplication.update += Wait;
        }

        static void Wait()
        {
            if (!pending.IsCompleted) return;
            EditorApplication.update -= Wait;
            if (pending.IsFaulted) { Debug.Log("[PreflightCck] FAIL: the CCK's validation threw " + pending.Exception); EditorApplication.Exit(2); return; }

            var said = report.Entries.Where(e => e.Category == "Upload").ToList();
            foreach (var e in said) Debug.Log($"[PreflightCck] preflight: {e.Subject} | {e.Detail}");
            var errors = pending.Result.StepResults.Where(r => r.Severity == ValidationSeverity.Error).ToList();
            foreach (var r in pending.Result.StepResults.Where(r => r.Severity >= ValidationSeverity.Warning))
                Debug.Log($"[PreflightCck] cck {r.Severity}: {r.Message} | {Names(r)}");

            int failures = 0;
            void Check(bool ok, string what) { if (!ok) failures++; Debug.Log($"[PreflightCck] {(ok ? "ok   " : "WRONG")} {what}"); }
            string Message(string key) => CCKLocalizationManager.GetString("Validations." + key).Split('{')[0];
            foreach (var (key, words) in Rules)
            {
                bool cck = errors.Any(r => r.Message != null && r.Message.StartsWith(Message(key)));
                bool ours = said.Any(e => e.Subject.StartsWith("The CCK will not upload this") && e.Subject.Contains(words));
                if (cck || ours) Check(cck == ours, $"{key}: the CCK {(cck ? "refuses" : "passes")}, the preflight {(ours ? "refuses" : "passes")}");
            }
            var known = Rules.Select(r => Message(r.key)).ToList();
            foreach (var r in errors.Where(r => !known.Any(k => r.Message != null && r.Message.StartsWith(k))))
                Check(false, $"a CCK error the preflight has no rule for: {r.Message}");

            var streaming = errors.FirstOrDefault(r => r.Message.StartsWith(Message("REQUIRES_STREAMING_MIPMAPS"))) as DetailedValidationResult;
            var ourStreaming = said.FirstOrDefault(e => e.Subject.Contains("not stream mipmaps"));
            Check(streaming != null && streaming.RootObjects.Any(o => o.name == onMesh), $"the CCK asks the cube's {onMesh} to stream");
            Check(ourStreaming != null && ourStreaming.Detail.Contains(onMesh), $"the preflight asks the cube's {onMesh} to stream");
            Check(streaming == null || streaming.RootObjects.All(o => o.name != streams && o.name != still), "the CCK asks no particle texture to stream");
            Check(ourStreaming == null || (!ourStreaming.Detail.Contains(streams) && !ourStreaming.Detail.Contains(still)), "the preflight names no particle texture");
            Check(streaming == null || streaming.RootObjects.All(o => o.name != hidden), $"the CCK skips the EditorOnly cube's {hidden}");
            Check(ourStreaming == null || !ourStreaming.Detail.Contains(hidden), $"the preflight skips the EditorOnly cube's {hidden}");
            var priority = errors.FirstOrDefault(r => r.Message.StartsWith(Message("REQUIRES_ZERO_PRIORITY_STREAMING_MIPMAPS"))) as DetailedValidationResult;
            var ourPriority = said.FirstOrDefault(e => e.Subject.Contains("priority other than 0"));
            Check(priority != null && priority.RootObjects.Any(o => o.name == ranked), $"the CCK asks {ranked}, not streaming, for priority 0");
            Check(ourPriority != null && ourPriority.Detail.Contains(ranked), $"the preflight asks {ranked}, not streaming, for priority 0");
            // Then the Toolkit's route: the CCK's errors in place of the shared rules, each once.
            var second = new BridgeReport();
            void Done()
            {
                var rows = second.Entries.Where(e => e.Category == "Upload").ToList();
                foreach (var e in rows) Debug.Log($"[PreflightCck] with the CCK: {e.Subject} | {e.Detail}");
                Check(rows.All(e => e.Subject != "The CCK's own check is running"), "the waiting row is gone");
                int refusals = rows.Count(e => e.Subject.StartsWith("The CCK will not upload this"));
                Check(refusals == errors.Count, $"{errors.Count} CCK errors, {refusals} refusals in the report");
                Check(rows.Any(e => e.Detail.Contains(onMesh)), $"the report names the cube's {onMesh}");
                Check(rows.Any(e => e.Detail.Contains(ranked)), $"the report names {ranked}");
                Debug.Log(failures == 0 ? "[PreflightCck] PASS" : $"[PreflightCck] FAIL: {failures} checks");
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
            if (!UploadPreflight.RunWithCck(second, target, Done)) { Check(false, "the upload check found the CCK's validation"); Done(); }
        }

        static string Names(ValidationResult r) => r is DetailedValidationResult d ? string.Join(", ", d.RootObjects.Where(o => o != null).Select(o => o.name).Take(12)) : "";

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : "";
        }
    }
}
#endif
