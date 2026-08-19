// Regression harness. Development only, never shipped.
//
// Converts every avatar in the project. Reduces each result to a
// deterministic text digest. Diffs against the last accepted run.
// Unintended changes show as text, before any in-game test.
//
// The digest is not the .controller YAML. That is GUID noise.
// This records behaviour only: layers, states, transitions,
// conditions, motions by name, parameters, CVR components.
//
// Deploy into the test project's Assets/Editor/ to run.
//
// Run headless for anything past the quick set:
//   Unity.exe -batchmode -quit -projectPath "<project>" \
//     -executeMethod AvatarBridge.Regression.RegressionRunner.RunAllBatch
//
// Headless is for determinism. VRCFury's Write Defaults dialog
// changes the avatar per button pressed. Batchmode always answers
// Auto-Fix, so runs stay comparable.
// Interactive runs: always Auto-Fix, never "Skip and stop asking".

#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Regression
{
    public static class RegressionRunner
    {
        // Digests live beside the tool, not in the Unity project.
        // They must survive a reimport of Assets/AvatarBridge.
        // Set AVATARBRIDGE_REPO to the checkout path before running.
        static string Root
        {
            get
            {
                var repo = Environment.GetEnvironmentVariable("AVATARBRIDGE_REPO");
                if (string.IsNullOrEmpty(repo))
                    throw new InvalidOperationException(
                        "Set the AVATARBRIDGE_REPO environment variable to the AvatarBridge checkout path.");
                // A flag-on run keeps its own Baseline and Current. The two
                // answer different questions — one asks whether existing
                // users are unaffected, the other whether the new feature
                // is stable — and sharing a folder would have each
                // overwrite the other's reference.
                string suffix = YapsMode ? "/Regression/Yaps" : "/Regression";
                // The fallback solver is a different avatar at the end of the
                // run, so its digests never share a folder with the default.
                if (Environment.GetEnvironmentVariable("AVATARBRIDGE_PHYSICS") == "DynamicBone")
                {
                    suffix += "/DynamicBone";
                }
                return repo.Replace('\\', '/').TrimEnd('/') + suffix;
            }
        }
        static string BaselineDir => Root + "/Baseline";
        static string CurrentDir => Root + "/Current";
        // Written when a run is cancelled. A partial Current/ looks exactly like a complete one,
        // and accepting it would silently shrink the corpus to however far the run got.
        static string PartialMarker => CurrentDir + "/PARTIAL-DO-NOT-ACCEPT";

        // Scenes that are never avatars. Matched as path substrings.
        static readonly string[] BuiltInExcluded =
        {
            "/AvatarBridgeOutput/", "/CVR.CCK/", "/MagicaCloth2/", "/UnityTechnologies/",
            "/Samples/", "/Scenes/SampleScene", "/MISC/",
        };

        // Per-project scene lists live in Regression/corpus.cfg beside
        // the digests. Local test data, kept out of the repo like the
        // digests. Sections [excluded] and [quickset], "#" comments.
        static string CorpusConfigPath => Root + "/corpus.cfg";

        static string[] ReadCorpusSection(string section)
        {
            if (!File.Exists(CorpusConfigPath))
            {
                return null;
            }
            var entries = new List<string>();
            string current = null;
            foreach (var raw in File.ReadAllLines(CorpusConfigPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                if (line.StartsWith("[", StringComparison.Ordinal)
                    && line.EndsWith("]", StringComparison.Ordinal))
                {
                    current = line.Substring(1, line.Length - 2).ToLowerInvariant();
                    continue;
                }
                if (current == section)
                {
                    entries.Add(line);
                }
            }
            return entries.ToArray();
        }

        static string[] Excluded()
        {
            var extra = ReadCorpusSection("excluded");
            return extra == null ? BuiltInExcluded : BuiltInExcluded.Concat(extra).ToArray();
        }

        // The quick set: the most regression-prone scenes, so a fix can
        // be checked in minutes instead of a full run. Keep a healthy
        // control scene in it; a control that starts moving means a fix
        // was masking something.
        static string[] QuickSet()
        {
            var set = ReadCorpusSection("quickset");
            if (set == null || set.Length == 0)
            {
                throw new InvalidOperationException(
                    $"No [quickset] section in {CorpusConfigPath}. List scene paths there, one per line.");
            }
            return set;
        }

        [MenuItem("Tools/AvatarBridge Dev/Regression: run quick set")]
        public static void RunQuick() => Run(QuickSet(), "quick");

        [MenuItem("Tools/AvatarBridge Dev/Regression: run all scenes")]
        public static void RunAll() => Run(AllAvatarScenes(), "all");

        [MenuItem("Tools/AvatarBridge Dev/Regression: accept current as baseline")]
        public static void AcceptCurrent()
        {
            if (!Directory.Exists(CurrentDir))
            {
                Debug.LogError("[Regression] nothing in Current/ to accept. Run first.");
                return;
            }
            if (File.Exists(PartialMarker))
            {
                Debug.LogError("[Regression] REFUSING: the last run was cancelled, so Current/ is " +
                               "partial. Accepting it would shrink the corpus to however far that " +
                               "run got, and every avatar after the cancel would read as \"no " +
                               "baseline yet\" from then on. Re-run first.\n" +
                               File.ReadAllText(PartialMarker));
                return;
            }
            Directory.CreateDirectory(BaselineDir);
            int existing = Directory.GetFiles(BaselineDir, "*.txt").Length;
            int n = 0;
            foreach (var file in Directory.GetFiles(CurrentDir, "*.txt"))
            {
                File.Copy(file, Path.Combine(BaselineDir, Path.GetFileName(file)), true);
                n++;
            }

            // Copy, never wipe. A quick-run accept must leave the other
            // baselines alone. The note tells partial from full accepts.
            string note = existing > n
                ? $" ({existing - n} other baseline(s) left untouched; this was a partial run)"
                : "";
            Debug.Log($"[Regression] accepted {n} digest(s) as the new baseline{note}.");
        }

        public static void RunAllBatch()
        {
            int changed = Run(AllAvatarScenes(), "all");
            EditorApplication.Exit(changed == 0 ? 0 : 1);
        }

        public static void RunQuickBatch()
        {
            int changed = Run(QuickSet(), "quick");
            EditorApplication.Exit(changed == 0 ? 0 : 1);
        }

        public static void RunSubsetBatch()
        {
            string listFile = Environment.GetEnvironmentVariable("AVATARBRIDGE_SUBSET");
            if (string.IsNullOrEmpty(listFile) || !File.Exists(listFile))
            {
                Debug.LogError("[Regression] AVATARBRIDGE_SUBSET must name a file of scene paths, one per line.");
                EditorApplication.Exit(2);
                return;
            }
            var scenes = File.ReadAllLines(listFile)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal))
                .ToArray();
            var missing = scenes.Where(s => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(s) == null).ToArray();
            if (missing.Length > 0)
            {
                Debug.LogError("[Regression] subset lists scene(s) that do not exist: "
                               + string.Join(", ", missing));
                EditorApplication.Exit(2);
                return;
            }
            int changed = Run(scenes, $"subset({scenes.Length})");
            EditorApplication.Exit(changed == 0 ? 0 : 1);
        }

        static string[] AllAvatarScenes()
        {
            var excluded = Excluded();
            return AssetDatabase.FindAssets("t:Scene")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && p.StartsWith("Assets/", StringComparison.Ordinal))
                .Where(p => !excluded.Any(x => p.Replace('\\', '/').Contains(x)))
                .OrderBy(p => p, StableSampleOrder.Instance)
                .ToArray();
        }

        static int Run(IEnumerable<string> sceneSource, string label)
        {
            // Materialised once: the count is wanted up front for the progress bar and the
            // interactive warning, and AssetDatabase queries should not be re-run per use.
            var scenes = sceneSource.ToList();

            // Every number in a digest is formatted the same way regardless of the machine's
            // locale, for the same reason BridgeConverter pins the culture: a digest written on
            // a comma-decimal machine must diff cleanly against one written on a point-decimal
            // machine, or the baseline is worthless the moment anyone else runs it.
            var previousCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            // VRCFury and the VRCSDK pop modals on failed bakes, so a
            // big interactive run stops dead at the first broken avatar.
            // Batchmode auto-answers dialogs; that is why RunAllBatch exists.
            if (!Application.isBatchMode && scenes.Count > 4)
            {
                Debug.LogWarning(
                    "[Regression] running " + scenes.Count + " scenes interactively: VRCFury and " +
                    "the VRCSDK will block on modal dialogs for any avatar that fails to bake. " +
                    "Close Unity and run headless instead:\n" +
                    "  Unity.exe -batchmode -quit -projectPath \"<project>\" " +
                    "-executeMethod AvatarBridge.Regression.RegressionRunner.RunAllBatch");
            }

            // Start from empty. A digest left behind by a previous, differently-scoped run is
            // indistinguishable from one this run produced, and would be compared and reported
            // as though it were current.
            if (Directory.Exists(CurrentDir))
            {
                foreach (var stale in Directory.GetFiles(CurrentDir, "*.txt")) File.Delete(stale);
                if (File.Exists(PartialMarker)) File.Delete(PartialMarker);
            }
            Directory.CreateDirectory(CurrentDir);

            // Which build produced this run, kept OUT of the compared digests (see BuildDigest).
            // Not a .txt, so neither the comparison nor Accept picks it up.
            File.WriteAllText(CurrentDir + "/_run.info",
                $"bridge: {BridgeDefines.Version}\nunity: {Application.unityVersion}\n" +
                $"started: {DateTime.Now:yyyy-MM-dd HH:mm}\n");

            var changes = new List<string>();
            var missing = new List<string>();
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int ran = 0, failed = 0;
            bool cancelled = false;
            var started = DateTime.Now;

            try
            {
                for (int i = 0; i < scenes.Count; i++)
                {
                    var scenePath = scenes[i];
                    string name = Path.GetFileNameWithoutExtension(scenePath);

                    // Progress is per avatar; a conversion is one long
                    // blocking call. No-op in batchmode.
                    // Cancelable on purpose; the cancel is handled below.
                    if (!Application.isBatchMode)
                    {
                        var elapsed = DateTime.Now - started;
                        string eta = i > 0
                            ? $", ~{TimeSpan.FromTicks(elapsed.Ticks / i * (scenes.Count - i)):mm\\:ss} left"
                            : "";
                        if (EditorUtility.DisplayCancelableProgressBar(
                                $"AvatarBridge regression {i + 1}/{scenes.Count}",
                                $"{name}   ({elapsed:mm\\:ss} elapsed{eta})",
                                (float)i / scenes.Count))
                        {
                            cancelled = true;
                            break;
                        }
                    }

                    string digest;
                    try
                    {
                        digest = ConvertAndDigest(scenePath);
                        if (digest == null) continue;   // no avatar in this scene
                    }
                    catch (Exception e)
                    {
                        // A thrown conversion is a result worth diffing.
                        // Starting and stopping throwing both regress.
                        digest = $"scene: {scenePath}\n\n[harness]\nEXCEPTION {e.GetType().Name}: {e.Message}\n";
                        failed++;
                    }

                    ran++;
                    string file = DigestName(scenePath);
                    // Belt and braces on the naming rule below: if two scenes ever collide again
                    // the run must say so, not silently cover one of them.
                    if (!written.Add(file))
                        Debug.LogError($"[Regression] digest name collision on '{file}': " +
                                       $"'{scenePath}' has overwritten an earlier scene's digest.");
                    File.WriteAllText(Path.Combine(CurrentDir, file), digest);

                    string baseline = Path.Combine(BaselineDir, file);
                    if (!File.Exists(baseline)) { missing.Add(name); continue; }
                    string before = File.ReadAllText(baseline);
                    if (before != digest) changes.Add($"{name}  ({DiffSummary(before, digest)})");
                }
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previousCulture;
                if (!Application.isBatchMode) EditorUtility.ClearProgressBar();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[Regression/{label}] {ran} avatar(s) in {(DateTime.Now - started).TotalSeconds:F0}s" +
                          (failed > 0 ? $", {failed} threw" : ""));
            if (missing.Count > 0)
                sb.AppendLine($"  no baseline yet ({missing.Count}): {string.Join(", ", missing)}");
            if (changes.Count == 0)
                sb.AppendLine(missing.Count > 0 ? "  nothing else changed." : "  no changes.");
            else
            {
                sb.AppendLine($"  CHANGED ({changes.Count}):");
                foreach (var c in changes) sb.AppendLine("    " + c);
                sb.AppendLine($"  compare: {CurrentDir} vs {BaselineDir}");
            }

            if (cancelled)
            {
                // Said loudly and last. A partial Current/ looks
                // complete, and accepting it would shrink the corpus.
                File.WriteAllText(PartialMarker,
                    $"Cancelled after {ran} of {scenes.Count} scenes at {DateTime.Now:yyyy-MM-dd HH:mm}.\n" +
                    "AcceptCurrent refuses while this file exists. Re-run to clear it.\n");
                sb.AppendLine($"  CANCELLED after {ran} of {scenes.Count}. Current/ is PARTIAL. " +
                              "Do not accept it as a baseline; re-run.");
                Debug.LogError(sb.ToString());
                return changes.Count;
            }

            Debug.Log(sb.ToString());
            return changes.Count;
        }

        static string DiffSummary(string before, string after)
        {
            var a = before.Split('\n');
            var b = after.Split('\n');
            var removed = new HashSet<string>(a);
            removed.ExceptWith(b);
            var added = new HashSet<string>(b);
            added.ExceptWith(a);
            return $"-{removed.Count} +{added.Count}";
        }

        static string DigestName(string scenePath)
        {
            var rel = scenePath.Replace('\\', '/');
            if (rel.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) rel = rel.Substring(7);
            if (rel.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) rel = rel.Substring(0, rel.Length - 6);
            return Safe(rel.Replace('/', '~')) + ".txt";
        }

        static string Safe(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        // ---------------------------------------------------------------- conversion

        static string ConvertAndDigest(string scenePath)
        {
            // OpenScene from script discards unsaved changes without
            // prompting. Wanted here: the conversion mutates the scene
            // heavily and none of it is ever saved back.
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var reset = ResetScene(scene);

            VRCAvatarDescriptor descriptor = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                // Inactive included: plenty of avatars ship with the descriptor object disabled,
                // and a text search of the scene file misses prefab-instanced ones entirely.
                descriptor = root.GetComponentInChildren<VRCAvatarDescriptor>(true);
                if (descriptor != null) break;
            }
            if (descriptor == null) return null;

            // Re-activate the source and everything above it. A saved
            // scene can hold its source switched off from a previous
            // conversion, and converting a deactivated avatar is not
            // what a user does.
            for (var t = descriptor.transform; t != null; t = t.parent)
            {
                if (t.gameObject.activeSelf) continue;
                t.gameObject.SetActive(true);
                reset.reactivated++;
            }

            var settings = CorpusSettings();
            var report = BridgeConverter.Convert(descriptor, settings);
            reset.avatar = descriptor.gameObject.name;
            var target = Selection.activeGameObject;   // BridgeConverter sets this to ctx.Target

            var sb = new StringBuilder();
            sb.Append("avatar: ").Append(descriptor.gameObject.name).Append('\n');
            sb.Append("scene: ").Append(scenePath).Append('\n');
            // No timestamp, no Unity version, no AvatarBridge version.
            // All three change without the conversion changing, and a
            // version bump would diff every avatar at once. The version
            // goes once per run into Current/_run.info instead.
            sb.Append('\n');

            AppendReset(sb, reset);
            AppendSettings(sb, settings);
            AppendReport(sb, report);
            AppendCvrSide(sb, target);
            AppendSweep(sb, target);
            return Stable(sb.ToString());
        }

        // The sweep rides every corpus conversion: it drives each menu
        // parameter and reports what does not come back. Names only —
        // readings vary run to run, names of broken things do not. Runs
        // last: it moves the scene, and the scene is never saved.
        static void AppendSweep(StringBuilder sb, GameObject target)
        {
            sb.Append("[sweep] ");
            if (target == null)
            {
                sb.Append("skipped: no converted root\n\n");
                return;
            }
            int findings;
            try
            {
                findings = ToggleSweep.Sweep(target);
            }
            catch (Exception e)
            {
                sb.Append("failed: ").Append(e.GetType().Name).Append('\n').Append('\n');
                return;
            }
            var r = ToggleSweep.LastResult;
            if (findings < 0)
            {
                sb.Append("skipped: no controller\n\n");
                return;
            }
            sb.Append($"params={r.Parameters} responded={r.Responded} stuck={r.Stuck?.Count ?? 0} " +
                      $"refused={r.Refused?.Count ?? 0} invalid={(r.Invalid ? 1 : 0)}\n");
            foreach (var name in (r.Stuck ?? new List<string>()).OrderBy(n => n, StableSampleOrder.Instance))
            {
                sb.Append("  stuck ").Append(name).Append('\n');
            }
            foreach (var name in (r.Refused ?? new List<string>()).OrderBy(n => n, StableSampleOrder.Instance))
            {
                sb.Append("  refused ").Append(name).Append('\n');
            }
            sb.Append('\n');
        }

        static List<string> DriverTargets(StateMachineBehaviour behaviour)
        {
            var found = new SortedSet<string>(StringComparer.Ordinal);
            var type = behaviour.GetType();
            foreach (var listName in new[] { "EnterTasks", "ExitTasks", "UpdateTasks" })
            {
                var field = type.GetField(listName);
                if (field == null || !(field.GetValue(behaviour) is System.Collections.IEnumerable tasks))
                {
                    continue;
                }
                foreach (var task in tasks)
                {
                    if (task == null) continue;
                    var target = task.GetType().GetField("targetName");
                    if (target?.GetValue(task) is string name && !string.IsNullOrEmpty(name))
                    {
                        found.Add(name);
                    }
                }
            }
            return found.ToList();
        }

        // Redacts the ids VRCFury assigns fresh on every bake.
        //
        // Everything ORDERED in this digest sorts by StableSampleOrder, which
        // strips the same ids to build its key. Sorting on the raw text and
        // redacting afterwards is the bug that was here: Fury renumbers, the
        // order changes, and the digest shows identical-looking lines in new
        // places. It cost a full read of the contacts block to find out that
        // nothing had changed.
        static string Stable(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return s;
            }
            // A GUID stamped into an object name, fresh on every bake, the
            // way VRCFury's numbers are. Eight contact paths on one avatar
            // carried one and diffed on every run for it.
            s = System.Text.RegularExpressions.Regex.Replace(
                s, @"\$[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", "$#");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"VF_\d+_", "VF_#_");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\[VF\d+\]", "[VF#]");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\bVF\d+_", "VF#_");
            return s;
        }

        class SceneReset
        {
            public int leftovers;      // previous conversions deleted out of the scene
            public int reactivated;    // objects switched back on above and including the source
            public string avatar = "";
        }

        static SceneReset ResetScene(Scene scene)
        {
            var result = new SceneReset();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var cvr in root.GetComponentsInChildren<CVRAvatar>(true))
                {
                    // Null-checked because destroying a parent takes its children with it, and
                    // this array was captured before any of that happened.
                    if (cvr == null || cvr.GetComponent<VRCAvatarDescriptor>() != null) continue;
                    UnityEngine.Object.DestroyImmediate(cvr.gameObject);
                    result.leftovers++;
                }
            }
            return result;
        }

        static void AppendReset(StringBuilder sb, SceneReset reset)
        {
            // In the digest because it describes the input. A hand-edited
            // scene then diffs here, with the cause named on the line.
            sb.Append("[scene reset]\n");
            sb.Append("  leftover conversions removed: ").Append(reset.leftovers).Append('\n');
            sb.Append("  objects re-activated: ").Append(reset.reactivated).Append('\n');
            sb.Append('\n');
        }

        // Set AVATARBRIDGE_YAPS=1 to convert with the penetration system
        // on — the DEFAULT since 2026-08-15, so Regression/Yaps is what a
        // user gets. Unset measures the opt-out (convertYapsSystems false),
        // which still has to hold: it is one tick away for anyone. The
        // folder names predate the flip and stay, so both accepted
        // baselines remain valid.
        static bool YapsMode =>
            Environment.GetEnvironmentVariable("AVATARBRIDGE_YAPS") == "1";

        static BridgeSettings CorpusSettings() => new BridgeSettings
        {
            convertYapsSystems = YapsMode,
            cloneAvatar = true,
            outputFolder = "Assets/AvatarBridgeOutput",

            // All five layers. Base, Additive and Action are off by default.
            convertBaseLayer = true,
            convertAdditiveLayer = true,
            convertGestureLayer = true,
            convertActionLayer = true,
            convertFxLayer = true,

            toggleStyle = ToggleStyle.AnimatorLayers,
            preserveParameterSyncState = true,
            exposeMenulessSyncedParameters = true,

            // AVATARBRIDGE_PHYSICS=DynamicBone runs the fallback solver, which
            // the corpus otherwise never exercises. Digests land in their own
            // folder, so the two profiles never compare against each other.
            physicsTarget = Environment.GetEnvironmentVariable("AVATARBRIDGE_PHYSICS") == "DynamicBone"
                ? PhysicsTarget.DynamicBone
                : PhysicsTarget.MagicaCloth2,
            deleteConvertedPhysBones = true,
            grabbyBonesSupport = true,
            useMagicaPresets = true,
            fitToPhysBone = true,
            derivePhysicsFromPhysBone = true,
            capParticleRadius = true,
            // The two that invent physics the author never made. On for coverage.
            autoAssignNearbyColliders = true,
            addPhysicsToRiggedStyles = true,
            // Left off deliberately: both wreck specific avatars rather than exercising a path.
            convertToePhysBones = false,

            stripGogoLoco = true,
            stripSpsSystems = true,
            extraStripKeywords = "",
            stripDeadMaterialAnimation = true,

            convertContacts = true,
            createDefaultColliderPointers = true,
            sizeContactZonesForLargest = true,
            patchNonSpiShaders = true,     // BETA: writes patched shader copies

            convertConstraints = true,
            convertHeadChop = true,
            convertSpatialAudio = true,
            wireBlinkBlendshapes = true,
            addAvatarScaler = true,
            faceTrackingMode = FaceTrackingMode.Native,
        };

        static void AppendSettings(StringBuilder sb, BridgeSettings s)
        {
            // In the digest so a profile change is loud. Changing
            // CorpusSettings diffs every digest at once, correctly:
            // every conversion changed meaning.
            sb.Append("[settings]\n");
            // Instance fields only. A const is a public static literal, so it
            // lands here too and reads as a setting nobody can set.
            foreach (var f in typeof(BridgeSettings).GetFields()
                         .Where(f => !f.IsLiteral && !f.IsStatic)
                         .OrderBy(f => f.Name, StableSampleOrder.Instance))
            {
                sb.Append("  ").Append(f.Name).Append('=').Append(f.GetValue(s)).Append('\n');
            }
            sb.Append('\n');
        }

        static void AppendReport(StringBuilder sb, BridgeReport report)
        {
            sb.Append("[report]\n");
            sb.Append($"converted={report.CountOf(ReportStatus.Converted)} ")
              .Append($"approximated={report.CountOf(ReportStatus.Approximated)} ")
              .Append($"skipped={report.CountOf(ReportStatus.Skipped)} ")
              .Append($"warnings={report.CountOf(ReportStatus.Warning)} ")
              .Append($"errors={report.CountOf(ReportStatus.Error)}\n");

            // Only errors and warnings are listed line by line. The Converted entries number in
            // the thousands on a big avatar (one per cloth chain, parameter and menu control) and
            // their exact wording churns constantly; the counts above catch a change in volume,
            // which is the part that matters.
            //
            // Detail is truncated on purpose. Report wording churns
            // whenever a message improves, changing nothing about the
            // conversion. Category and Subject carry the behaviour.
            var notable = report.Entries
                .Where(e => e.Status == ReportStatus.Error || e.Status == ReportStatus.Warning)
                .Select(e => $"  {e.Status.ToString().ToUpperInvariant()} [{e.Category}] {e.Subject} | {Brief(e.Detail)}")
                .OrderBy(s => s, StableSampleOrder.Instance);
            foreach (var line in notable) sb.Append(line).Append('\n');
            sb.Append('\n');
        }

        static void AppendCvrSide(StringBuilder sb, GameObject target)
        {
            if (target == null) { sb.Append("[cvr]\nNO TARGET — conversion produced nothing\n"); return; }

            var avatar = target.GetComponent<CVRAvatar>();
            if (avatar == null) { sb.Append("[cvr]\nNO CVRAvatar on target\n"); return; }

            sb.Append("[cvravatar]\n");
            sb.Append("  viewPosition: ").Append(V3(avatar.viewPosition)).Append('\n');
            sb.Append("  useBlinkBlendshapes: ").Append(avatar.useBlinkBlendshapes).Append('\n');
            sb.Append("  blinkBlendshape: ").Append(Join(avatar.blinkBlendshape)).Append('\n');
            sb.Append("  useVisemeLipsync: ").Append(avatar.useVisemeLipsync).Append('\n');
            sb.Append("  visemeMode: ").Append(avatar.visemeMode).Append('\n');
            sb.Append("  visemeBlendshapes: ").Append(Join(avatar.visemeBlendshapes)).Append('\n');
            sb.Append("  bodyMesh: ").Append(avatar.bodyMesh != null ? avatar.bodyMesh.name : "<none>").Append('\n');
            sb.Append('\n');

            AppendAas(sb, avatar);
            AppendComponents(sb, target);
            AppendPhysics(sb, avatar, target);
            AppendControllers(sb, avatar, target);
        }

        static void AppendAas(StringBuilder sb, CVRAvatar avatar)
        {
            sb.Append("[advanced avatar settings]\n");
            var entries = avatar.avatarSettings != null ? avatar.avatarSettings.settings : null;
            if (entries == null || entries.Count == 0) { sb.Append("  <none>\n\n"); return; }

            // Sorted by machineName: the list order is menu order, which is worth seeing, but it
            // is also the single noisiest thing in the whole digest because any pass that appends
            // an entry shifts everything after it. Order changes show up as a separate line.
            foreach (var e in entries.OrderBy(x => x.machineName, StableSampleOrder.Instance))
            {
                sb.Append("  ").Append(e.machineName)
                  .Append(" | ").Append(e.type)
                  .Append(" | \"").Append(e.name).Append("\"\n");
            }
            sb.Append("  order: ").Append(string.Join(",", entries.Select(x => x.machineName))).Append('\n');
            sb.Append('\n');
        }

        static void AppendComponents(StringBuilder sb, GameObject target)
        {
            sb.Append("[components]\n");
            // Type name and count only. This catches a pass that stops
            // emitting a component type altogether.
            var counts = target.GetComponentsInChildren<Component>(true)
                .Where(c => c != null)
                .Select(c => c.GetType().FullName)
                .Where(n => n.StartsWith("ABI.CCK", StringComparison.Ordinal)
                         || n.StartsWith("MagicaCloth", StringComparison.Ordinal)
                         || n.StartsWith("VRC.", StringComparison.Ordinal))
                .GroupBy(n => n)
                .OrderBy(g => g.Key, StableSampleOrder.Instance);
            foreach (var g in counts) sb.Append("  ").Append(g.Count().ToString("D3")).Append("  ").Append(g.Key).Append('\n');
            sb.Append('\n');

            // Marker lights, by range. The range IS the protocol: it says hole,
            // ring, front or plug tip, and every decoder on the platform reads
            // it, including ones outside the game. A digest that only counted
            // Light components would not have noticed the day these moved.
            var marks = target.GetComponentsInChildren<Light>(true)
                .Where(l => l != null && l.type == LightType.Point
                            && l.color.maxColorComponent < 0.02f
                            && l.range > 0.05f && l.range < 0.5f)
                .Select(l => l.range.ToString("0.0000", CultureInfo.InvariantCulture))
                .GroupBy(r => r)
                .OrderBy(g => g.Key, StableSampleOrder.Instance)
                .ToList();
            if (marks.Count > 0)
            {
                sb.Append("[marker lights]\n");
                foreach (var g in marks)
                {
                    sb.Append("  ").Append(g.Count().ToString("D3")).Append("  range ").Append(g.Key).Append('\n');
                }
                sb.Append('\n');
            }
        }

        // MagicaCloth is reached by NAME rather than by a compile-time reference, so this block
        // renders identically in every scripting-define configuration. That is not fussiness: a
        // digest whose SHAPE depends on which packages happen to be installed cannot be diffed
        // across machines, and a missing define would present as "this avatar lost all its
        // cloth" rather than as "this build cannot see it".
        const string MagicaClothType = "MagicaCloth2.MagicaCloth";

        static void AppendPhysics(StringBuilder sb, CVRAvatar avatar, GameObject target)
        {
            sb.Append("[physics]\n");

            // Both solvers, or a DynamicBone run reports nothing about the
            // physics it exists to test.
            var cloths = target.GetComponentsInChildren<Component>(true)
                .Where(c => c != null
                            && (c.GetType().FullName == MagicaClothType || c.GetType().Name == "DynamicBone"))
                .OrderBy(c => HierarchyPath(target, c), StableSampleOrder.Instance)
                .ToList();

            // Component-enabled and object-active are counted SEPARATELY because they are
            // separate bugs with the same symptom: cloth that starts disabled though its menu
            // toggle defaults on, and a holder object that nothing ever activates. Reported as
            // one "is it running" flag, either would hide behind the other.
            int running = 0, componentOff = 0, objectOff = 0;
            var clothPaths = new HashSet<string>(StringComparer.Ordinal);
            var clothLines = new List<string>();
            foreach (var c in cloths)
            {
                bool component = !(c is Behaviour behaviour) || behaviour.enabled;
                bool active = c.gameObject.activeInHierarchy;
                if (component && active) running++;
                if (!component) componentOff++;
                if (!active) objectOff++;
                string path = HierarchyPath(target, c);
                clothPaths.Add(path);
                clothLines.Add($"  {path} | component={(component ? "on" : "OFF")} " +
                               $"object={(active ? "on" : "OFF")} | roots: {ClothRoots(c)}");
            }
            sb.Append($"cloth={cloths.Count} running={running} ")
              .Append($"componentOff={componentOff} objectOff={objectOff}\n");
            foreach (var line in clothLines) sb.Append(line).Append('\n');

            // Curves that switch cloth on or off, counted across the
            // merged controller. The count moving is the signal, in
            // either direction.
            var animator = target.GetComponent<Animator>();
            var runtime = animator != null ? animator.runtimeAnimatorController : null;
            if (runtime == null && avatar != null) runtime = avatar.overrides;
            var controller = BaseController(runtime);
            if (runtime == null)
            {
                sb.Append("clothCurves <no controller to read>\n");
            }
            else
            {
                int on = 0, off = 0, clips = 0;
                foreach (var clip in runtime.animationClips.Where(c => c != null).Distinct())
                {
                    bool touched = false;
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        bool isCloth =
                            (binding.propertyName == "m_Enabled"
                             && binding.type != null && binding.type.FullName == MagicaClothType)
                            || (binding.propertyName == "m_IsActive" && clothPaths.Contains(binding.path));
                        if (!isCloth) continue;
                        touched = true;
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        // The LAST key, not the first: a curve is classified by where it leaves
                        // the binding, which is what survives once the state settles.
                        float ends = curve != null && curve.length > 0
                            ? curve.keys[curve.length - 1].value
                            : 0f;
                        if (ends > 0.5f) on++; else off++;
                    }
                    if (touched) clips++;
                }
                sb.Append($"clothCurves on={on} off={off} clips={clips}\n");
            }

            // Contacts by ROUTE. Which route a contact takes decides whether anyone else sees it
            // and whether it fires once or twice, and the three routes are indistinguishable in
            // every other block of this digest: a "#" name is computed by each client and never
            // transmitted, a plain name rides the settings stream, and a bridge layer is the pair
            // of the two. A contact silently changing lanes is a behaviour change with no other
            // trace here.
            var bridged = BridgedLocalNames(controller);
            var contacts = new List<string>();
            int local = 0, streamed = 0, carried = 0;

            void Record(string path, string kind, string parameter)
            {
                string route = bridged.Contains(parameter) ? "bridged"
                    : parameter.StartsWith("#", StringComparison.Ordinal) ? "local"
                    : "streamed";
                if (route == "bridged") carried++;
                else if (route == "local") local++;
                else streamed++;
                contacts.Add($"  {path} | {kind} | {route} | {parameter}");
            }

            foreach (var trigger in target.GetComponentsInChildren<CVRAdvancedAvatarSettingsTrigger>(true))
            {
                string path = HierarchyPath(target, trigger);
                foreach (string parameter in TriggerParameters(trigger).OrderBy(p => p, StableSampleOrder.Instance))
                {
                    Record(path, "trigger", parameter);
                }
            }
            contacts.Sort(StableSampleOrder.Instance);

            sb.Append($"contacts={contacts.Count} local={local} bridged={carried} ")
              .Append($"streamed={streamed} bridgeLayers={bridged.Count}\n");
            foreach (var line in contacts) sb.Append(line).Append('\n');
            sb.Append('\n');
        }

        static AnimatorController BaseController(RuntimeAnimatorController runtime)
        {
            for (int guard = 0; runtime != null && guard < 8; guard++)
            {
                if (runtime is AnimatorController controller) return controller;
                if (runtime is AnimatorOverrideController over)
                {
                    runtime = over.runtimeAnimatorController;
                    continue;
                }
                break;
            }
            return null;
        }

        static HashSet<string> BridgedLocalNames(AnimatorController controller)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (controller == null) return names;
            foreach (var layer in controller.layers)
            {
                if (layer?.stateMachine == null || layer.name == null) continue;
                if (!layer.name.StartsWith("Contact sync ", StringComparison.Ordinal)) continue;
                foreach (var child in layer.stateMachine.states)
                {
                    if (child.state == null) continue;
                    foreach (var transition in child.state.transitions)
                    {
                        foreach (var condition in transition.conditions)
                        {
                            // "IsLocal" gates the bridge so the wearer computes and everyone else
                            // receives; it is not the parameter being carried.
                            if (!string.IsNullOrEmpty(condition.parameter) && condition.parameter != "IsLocal")
                            {
                                names.Add(condition.parameter);
                            }
                        }
                    }
                }
            }
            return names;
        }

        static string ClothRoots(Component cloth)
        {
            // DynamicBone simulates one root, held in a field of its own.
            if (cloth.GetType().Name == "DynamicBone")
            {
                var root = cloth.GetType().GetField("m_Root")?.GetValue(cloth) as Transform;
                return root != null ? root.name : "<none>";
            }

            var data = cloth.GetType().GetProperty("SerializeData")?.GetValue(cloth);
            var roots = data?.GetType().GetField("rootBones")?.GetValue(data)
                as System.Collections.IEnumerable;
            // "<unreadable>" and "<none>" are deliberately different words: the first means the
            // harness lost its grip on MagicaCloth's shape and every line will say it, the second
            // means this chain genuinely hangs from nothing. Collapsing them would turn a blind
            // harness into what looks like a fleet of rootless chains.
            if (roots == null) return "<unreadable>";
            var names = new List<string>();
            foreach (var o in roots) names.Add(o is Transform t ? t.name : "<null>");
            return names.Count == 0 ? "<none>" : string.Join(",", names);
        }

        static IEnumerable<string> TriggerParameters(CVRAdvancedAvatarSettingsTrigger trigger)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (trigger.enterTasks != null)
                foreach (var t in trigger.enterTasks) if (t != null && !string.IsNullOrEmpty(t.settingName)) names.Add(t.settingName);
            if (trigger.exitTasks != null)
                foreach (var t in trigger.exitTasks) if (t != null && !string.IsNullOrEmpty(t.settingName)) names.Add(t.settingName);
            if (trigger.stayTasks != null)
                foreach (var t in trigger.stayTasks) if (t != null && !string.IsNullOrEmpty(t.settingName)) names.Add(t.settingName);
            return names;
        }

        static Type FindTypeByName(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(fullName, false);
                    if (type != null) return type;
                }
                catch
                {
                    // Reflection-only and broken assemblies throw here; they are not where the
                    // answer lives.
                }
            }
            return null;
        }

        static void AppendControllers(StringBuilder sb, CVRAvatar avatar, GameObject target)
        {
            // Every Animator in the hierarchy, nulls included. A test
            // that cannot see a failure is not testing for it.
            // Judged on the serialized m_Controller, not the getter;
            // the two can disagree, and the serialized value is what
            // the prefab saves. Disagreements print both.
            sb.Append("[animators]\n");
            foreach (var a in target.GetComponentsInChildren<Animator>(true).OrderBy(x => HierarchyPath(target, x), StableSampleOrder.Instance))
            {
                var serialized = new SerializedObject(a)
                    .FindProperty("m_Controller").objectReferenceValue as RuntimeAnimatorController;
                var getter = a.runtimeAnimatorController;
                sb.Append("  ").Append(HierarchyPath(target, a)).Append(" -> ")
                  .Append(serialized == null ? "NULL (no controller — broken or unassigned)" : serialized.name);
                if (getter != serialized)
                {
                    sb.Append("  [getter disagrees: ")
                      .Append(getter == null ? "null" : getter.name).Append(']');
                }
                sb.Append('\n');
            }
            sb.Append('\n');

            // Two things to capture, and the graft lives in the second: the merged controller
            // itself, and the override pairs CVR uses to replace its stock locomotion clips.
            var animator = target.GetComponent<Animator>();
            var seen = new HashSet<AnimatorController>();

            foreach (var rac in new RuntimeAnimatorController[] {
                         animator != null ? animator.runtimeAnimatorController : null,
                         avatar.overrides })
            {
                if (rac == null) continue;

                if (rac is AnimatorOverrideController ovr)
                {
                    sb.Append("[overrides] ").Append(ovr.name).Append('\n');
                    var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                    ovr.GetOverrides(pairs);
                    foreach (var p in pairs.OrderBy(p => Stable(p.Key != null ? p.Key.name : ""), StableSampleOrder.Instance))
                    {
                        sb.Append("  ").Append(p.Key != null ? p.Key.name : "<null>")
                          .Append(" -> ").Append(p.Value != null ? p.Value.name : "<unchanged>").Append('\n');
                    }
                    sb.Append('\n');

                    if (ovr.runtimeAnimatorController is AnimatorController baseAc && seen.Add(baseAc))
                        AppendController(sb, baseAc);
                }
                else if (rac is AnimatorController ac && seen.Add(ac))
                {
                    AppendController(sb, ac);
                }
            }
        }

        static void AppendController(StringBuilder sb, AnimatorController ac)
        {
            sb.Append("[controller] ").Append(ac.name).Append('\n');

            sb.Append("  parameters:\n");
            foreach (var p in ac.parameters.OrderBy(p => Stable(p.name), StableSampleOrder.Instance))
            {
                string def;
                switch (p.type)
                {
                    case AnimatorControllerParameterType.Bool: def = p.defaultBool.ToString(); break;
                    case AnimatorControllerParameterType.Int: def = p.defaultInt.ToString(); break;
                    case AnimatorControllerParameterType.Float: def = F(p.defaultFloat); break;
                    default: def = "-"; break;   // Trigger
                }
                sb.Append("    ").Append(p.type).Append(' ').Append(p.name).Append(" = ").Append(def).Append('\n');
            }

            for (int i = 0; i < ac.layers.Length; i++)
            {
                var layer = ac.layers[i];
                // Layer INDEX is not sorted away: order is behaviour here. A layer that moves
                // changes which one wins, and that has to be visible.
                sb.Append("  layer ").Append(i).Append(": ").Append(layer.name)
                  .Append(" w=").Append(F(layer.defaultWeight))
                  .Append(" blend=").Append(layer.blendingMode)
                  .Append(" mask=").Append(layer.avatarMask != null ? layer.avatarMask.name : "<none>")
                  .Append(" ikPass=").Append(layer.iKPass)
                  .Append('\n');
                AppendStateMachine(sb, layer.stateMachine, "    ", "");
            }
            sb.Append('\n');
        }

        static void AppendStateMachine(StringBuilder sb, AnimatorStateMachine sm, string indent, string prefix)
        {
            if (sm == null) { sb.Append(indent).Append("<null state machine>\n"); return; }

            sb.Append(indent).Append("default: ")
              .Append(sm.defaultState != null ? sm.defaultState.name : "<none>").Append('\n');

            // AnyState transitions first and separately: this is where the self-restart bugs
            // lived, so canTransitionToSelf gets its own visible field on every line.
            foreach (var t in sm.anyStateTransitions
                         .OrderBy(t => Dest(t), StableSampleOrder.Instance)
                         .ThenBy(t => Conds(t), StringComparer.Ordinal))
            {
                sb.Append(indent).Append("any -> ").Append(Dest(t))
                  .Append(" self=").Append(t.canTransitionToSelf)
                  .Append(' ').Append(Timing(t))
                  .Append(' ').Append(Conds(t)).Append('\n');
            }

            // States sorted by name. Unity's array order is creation
            // order, and a diff of that tells you nothing.
            foreach (var cs in sm.states.OrderBy(s => s.state.name, StableSampleOrder.Instance))
            {
                var st = cs.state;
                sb.Append(indent).Append("state ").Append(prefix).Append(st.name)
                  .Append(" wd=").Append(st.writeDefaultValues)
                  .Append(" speed=").Append(F(st.speed))
                  .Append(st.mirror ? " mirror" : "")
                  .Append(" motion=").Append(MotionOf(st.motion)).Append('\n');

                // Drivers get their WRITES named, not just their type. "behaviours:
                // AnimatorDriver" says a state changes something and refuses to say what, which
                // is the one fact needed to judge a transition cycle: a driver inside the cycle
                // that writes a parameter the cycle's own conditions read can break the loop, and
                // without the target names the digest cannot tell a real loop from a self-
                // limiting one.
                var behaviours = st.behaviours
                    .Where(b => b != null)
                    .Select(b =>
                    {
                        string name = b.GetType().Name;
                        var writes = DriverTargets(b);
                        return writes.Count > 0 ? $"{name}(writes {string.Join("/", writes)})" : name;
                    })
                    .OrderBy(n => n, StableSampleOrder.Instance)
                    .ToList();
                if (behaviours.Count > 0)
                    sb.Append(indent).Append("  behaviours: ").Append(string.Join(",", behaviours)).Append('\n');

                foreach (var t in st.transitions
                             .OrderBy(t => Dest(t), StableSampleOrder.Instance)
                             .ThenBy(t => Conds(t), StringComparer.Ordinal))
                {
                    sb.Append(indent).Append("  -> ").Append(Dest(t))
                      .Append(' ').Append(Timing(t))
                      .Append(' ').Append(Conds(t)).Append('\n');
                }
            }

            // Sub-state machines, recursed so a graft nested one level down is still described.
            foreach (var child in sm.stateMachines.OrderBy(c => c.stateMachine.name, StableSampleOrder.Instance))
            {
                sb.Append(indent).Append("submachine ").Append(child.stateMachine.name).Append('\n');
                AppendStateMachine(sb, child.stateMachine, indent + "  ", prefix + child.stateMachine.name + "/");
            }
        }

        static string Dest(AnimatorStateTransition t)
        {
            if (t.destinationState != null) return t.destinationState.name;
            if (t.destinationStateMachine != null) return t.destinationStateMachine.name + "/*";
            if (t.isExit) return "<exit>";
            return "<none>";
        }

        static string Timing(AnimatorStateTransition t)
        {
            var sb = new StringBuilder();
            sb.Append("exit=").Append(t.hasExitTime);
            if (t.hasExitTime) sb.Append('@').Append(F(t.exitTime));
            sb.Append(" dur=").Append(F(t.duration));
            if (t.offset != 0f) sb.Append(" off=").Append(F(t.offset));
            if (t.mute) sb.Append(" MUTED");
            if (t.orderedInterruption) sb.Append(" ordered");
            if (t.interruptionSource != TransitionInterruptionSource.None)
                sb.Append(" interrupt=").Append(t.interruptionSource);
            return sb.ToString();
        }

        static string Conds(AnimatorStateTransition t)
        {
            if (t.conditions == null || t.conditions.Length == 0) return "[]";
            var parts = t.conditions
                .Select(c => $"{c.parameter} {c.mode} {F(c.threshold)}")
                .OrderBy(s => s, StableSampleOrder.Instance);
            return "[" + string.Join(" && ", parts) + "]";
        }

        static string MotionOf(Motion motion)
        {
            if (motion == null) return "<none>";

            if (motion is BlendTree tree)
            {
                var sb = new StringBuilder();
                sb.Append("tree:").Append(tree.blendType).Append('(').Append(tree.blendParameter);
                if (tree.blendType != BlendTreeType.Simple1D && tree.blendType != BlendTreeType.Direct)
                    sb.Append(',').Append(tree.blendParameterY);
                sb.Append(")[");
                // Children in declared order: for a 1D tree that IS the threshold ordering, and
                // for the locomotion graft the position of a clip in the tree is the whole point.
                sb.Append(string.Join(" ", tree.children.Select(c =>
                {
                    string at = tree.blendType == BlendTreeType.Simple1D
                        ? F(c.threshold)
                        : tree.blendType == BlendTreeType.Direct
                            ? c.directBlendParameter
                            : V2(c.position);
                    string scale = Math.Abs(c.timeScale - 1f) > 1e-4f ? "x" + F(c.timeScale) : "";
                    return $"{at}{scale}=>{MotionOf(c.motion)}";
                })));
                sb.Append(']');
                return sb.ToString();
            }

            if (motion is AnimationClip clip)
            {
                // Length and loop flag both matter; both have been the bug.
                return $"clip:{clip.name}(len={F(clip.length)},loop={clip.isLooping})";
            }

            return motion.GetType().Name + ":" + motion.name;
        }

        // ---------------------------------------------------------------- formatting

        // Fixed precision, invariant, negative zero folded away.
        // Unity flips -0 sign between runs for no reason at all.
        static string F(float f)
        {
            if (float.IsNaN(f)) return "NaN";
            if (float.IsInfinity(f)) return f > 0 ? "+Inf" : "-Inf";
            if (Mathf.Abs(f) < 1e-5f) return "0";
            return f.ToString("0.####", CultureInfo.InvariantCulture);
        }

        // One line, no runaway prose. Newlines collapse because the digest is diffed line by
        // line and a multi-line Detail would smear one entry across several of them.
        static string Brief(string detail)
        {
            if (string.IsNullOrEmpty(detail)) return "";
            var s = new StringBuilder(detail.Length);
            bool space = false;
            foreach (char c in detail)
            {
                if (char.IsWhiteSpace(c)) { space = true; continue; }
                if (space && s.Length > 0) s.Append(' ');
                space = false;
                s.Append(c);
            }
            var flat = s.ToString();
            return flat.Length <= 70 ? flat : flat.Substring(0, 70) + "…";
        }

        static string HierarchyPath(GameObject root, Component c)
        {
            if (c == null || c.transform == root.transform) return "<root>";
            var parts = new List<string>();
            for (var t = c.transform; t != null && t != root.transform; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static string V2(Vector2 v) => $"({F(v.x)},{F(v.y)})";
        static string V3(Vector3 v) => $"({F(v.x)},{F(v.y)},{F(v.z)})";

        static string Join(string[] a)
        {
            if (a == null || a.Length == 0) return "<none>";
            return string.Join(",", a.Select(s => string.IsNullOrEmpty(s) ? "-" : s));
        }
    }
}
#endif
