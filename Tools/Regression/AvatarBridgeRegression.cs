// AvatarBridge regression harness — DEVELOPMENT ONLY, never shipped in the .unitypackage.
//
// Why this exists: the AnyState self-restart suppressor took four attempts (3.5.2 -> 3.5.6 ->
// 3.5.7 -> 3.5.8) and root-motion stripping took three (3.5.2 -> 3.5.4 -> 3.5.5). Every wrong
// attempt was correct reasoning about the avatar in front of us that silently broke a different
// avatar. Nothing caught that except wearing each one in game, one at a time, after release.
//
// So: convert every avatar in the project, reduce each result to a deterministic text digest,
// and diff those against the last accepted run. A change that was not intended shows up as a
// line of text before the headset goes on.
//
// The digest is deliberately NOT the .controller YAML — that is full of GUIDs, node positions
// and creation-order noise, and a diff of it is unreadable. This describes behaviour only:
// layers, states, transitions, conditions, motions by name, parameters, and the CVR-side
// components. If two runs differ here, something the avatar actually DOES has changed.
//
// Canonical copy lives in D:\AvatarBridge\Tools\Regression\ (version-controlled with the tool,
// pruned from the package build). Deployed into the test project's Assets/Editor/ to run.
//
// RUN IT HEADLESS for anything past the quick set:
//   Unity.exe -batchmode -quit -projectPath "<project>" \
//     -executeMethod AvatarBridge.Regression.RegressionRunner.RunAllBatch
//
// Not for speed — for determinism. Two of the modals that interrupt an interactive run come
// from VRCFury and the VRCSDK, and one of them, "VRCFury has detected a (likely) broken mix of
// Write Defaults", CHANGES THE AVATAR depending on which button is pressed. WD on/off per layer
// is exactly what AnimatorMerger reasons about, so answering Auto-Fix one run and Skip the next
// moves the digest for reasons that have nothing to do with our code, and the baseline becomes
// noise. Batchmode answers every dialog the same way (the first button, i.e. Auto-Fix) without
// a human in the loop, which is the only way a corpus stays comparable.
//
// If you do run interactively: always Auto-Fix, and never "Skip and stop asking" — that one
// persists, silently, and then the editor and headless runs disagree forever after.

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
        // Digests live beside the tool, not in the Unity project: they are the tool's test data,
        // and they must survive a delete-and-reimport of Assets/AvatarBridge.
        const string Root = "D:/AvatarBridge/Regression";
        static string BaselineDir => Root + "/Baseline";
        static string CurrentDir => Root + "/Current";
        // Written when a run is cancelled. A partial Current/ looks exactly like a complete one,
        // and accepting it would silently shrink the corpus to however far the run got.
        static string PartialMarker => CurrentDir + "/PARTIAL-DO-NOT-ACCEPT";

        // Scenes that are not avatars, or are our own output. Matched as path substrings.
        static readonly string[] Excluded =
        {
            "/AvatarBridgeOutput/", "/CVR.CCK/", "/MagicaCloth2/", "/UnityTechnologies/",
            "/Samples/", "/Scenes/SampleScene", "/MISC/",

            // Avatars VRCFury cannot bake. Their conversions start from a half-built avatar, so
            // their digests describe Fury's failure rather than ours and every diff on them is
            // noise. None of the three causes is fixable from this side: four carry a GoGo Loco
            // whose menu names a parameter its own params file does not declare, two are missing
            // a Wholesome/SPS package the avatar expects, and Branwen has a material whose shader
            // will not load. Put them back the moment the avatars themselves are repaired —
            // that is the only reason this list is spelt out rather than filtered by symptom.
            "/CowRobot/",              // CowBotNSFW + CowBotSFW  — GoGo Loco menu/params mismatch
            "/0.Kimmi/",               // Kimmi                   — same
            "/hypsi/",                 // hypsi                   — same
            "/!Arlo/",                 // Arlo                    — missing Wholesome/SPS package
            "/Satin Snake",            // Satin Snake             — same
            "/!BRANWEN/",              // Branwen                 — material shader will not load
        };

        // The quick set: every avatar that has taught us something, so a fix can be checked in
        // five minutes instead of forty. Each line says what it is watching, because a canary
        // nobody can explain gets deleted by the next person who finds it slow.
        //
        // The CONTROL matters as much as the targets. Sally_PC_SPS is the same avatar as the two
        // broken Sallys with a healthy source value, and it is here precisely because it should
        // NOT change — the prefab-override revert was invisible on it, so if it starts moving,
        // the revert was masking something else as well.
        //
        // The full run still covers every scene; this is only the tight loop while working.
        // The quick set: every avatar that has taught us something, so a fix can be checked in
        // six minutes instead of forty. Each line says what it is watching, because a canary
        // nobody can explain gets deleted by the next person who finds it slow.
        //
        // It was briefly narrowed to the three Sallys while that bug was chased, and narrowing it
        // was a mistake worth not repeating: a quick set covering only the bug currently in hand
        // is not a regression check. The wider set is what showed six other avatars still
        // converting correctly through four failed attempts at the Sally one — which is the only
        // reason those attempts could be made confidently.
        //
        // The CONTROL matters as much as the targets. Sally_PC_SPS is the same avatar as the two
        // broken Sallys with a healthy source value, and it is here precisely because it should
        // NOT change.
        static readonly string[] QuickSet =
        {
            // STILL FAILING as of 3.5.16 — the Animator link is null while every CVR-side field
            // is correct, so these convert and work in game but do not preview in the editor.
            // Four fixes have missed; the next step is a live experiment, not another guess.
            "Assets/SallyShopkeeper/Sally_PC.unity",
            "Assets/SallyShopkeeper/Sally_Quest.unity",
            // The control: same avatar, healthy source value, must stay correct.
            "Assets/SallyShopkeeper/Sally_PC_SPS.unity",

            // 3.5.10 — Action transplant armed at load. Oscillated between pose and idle...
            "Assets/lemur/lumar_ROUND_setup_release.unity",
            // ...and walked up its stages into a pose nobody chose.
            "Assets/Rytu_assets/Rytu_setup.unity",

            // 3.5.9 — crash guard read a GUID out of a "Missing Prefab" object NAME and refused
            // to assign a perfectly good controller. Both carry such a placeholder.
            "Assets/BHFBunny/BHFBUNNY.unity",
            "Assets/Bimbo Base.unity",              // avatar inside is "Sultry Snake"

            // 3.5.6 -> 3.5.8 — the AnyState self-restart arc, four attempts. Toggle layers here
            // are the most sensitive thing in the corpus to a change in transition handling.
            "Assets/Avatars/Others Characters/Kar/!!!OPEN ME SCENE/Kar.unity",
        };

        [MenuItem("Tools/AvatarBridge Dev/Regression — run quick set")]
        public static void RunQuick() => Run(QuickSet, "quick");

        [MenuItem("Tools/AvatarBridge Dev/Regression — run all scenes")]
        public static void RunAll() => Run(AllAvatarScenes(), "all");

        [MenuItem("Tools/AvatarBridge Dev/Regression — accept current as baseline")]
        public static void AcceptCurrent()
        {
            if (!Directory.Exists(CurrentDir))
            {
                Debug.LogError("[Regression] nothing in Current/ to accept — run first.");
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

            // Copy, never wipe: accepting after a QUICK run must update those avatars and leave
            // the other forty-odd baselines alone. Said out loud all the same, because "accepted
            // 8" after a quick run and "accepted 49" after a full one look identical at a glance
            // and mean very different things about what is now pinned.
            string note = existing > n
                ? $" ({existing - n} other baseline(s) left untouched — this was a partial run)"
                : "";
            Debug.Log($"[Regression] accepted {n} digest(s) as the new baseline{note}.");
        }

        /// <summary>Batch entry: Unity.exe -batchmode -quit -executeMethod
        /// AvatarBridge.Regression.RegressionRunner.RunAllBatch</summary>
        public static void RunAllBatch()
        {
            int changed = Run(AllAvatarScenes(), "all");
            EditorApplication.Exit(changed == 0 ? 0 : 1);
        }

        public static void RunQuickBatch()
        {
            int changed = Run(QuickSet, "quick");
            EditorApplication.Exit(changed == 0 ? 0 : 1);
        }

        static string[] AllAvatarScenes()
        {
            return AssetDatabase.FindAssets("t:Scene")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && p.StartsWith("Assets/", StringComparison.Ordinal))
                .Where(p => !Excluded.Any(x => p.Replace('\\', '/').Contains(x)))
                .OrderBy(p => p, StringComparer.Ordinal)
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

            // A big interactive run is a trap, and it is not our dialogs that spring it:
            // VRCFury pops a modal on a failed bake and the VRCSDK pops another when its
            // preprocess hook reports that failure, so a full corpus run stops dead on the first
            // avatar with a broken Fury component and waits for a click. Batchmode makes
            // EditorUtility.DisplayDialog return immediately instead of blocking, which is the
            // only reason RunAllBatch exists.
            if (!Application.isBatchMode && scenes.Count > 4)
            {
                Debug.LogWarning(
                    "[Regression] running " + scenes.Count + " scenes interactively — VRCFury and " +
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

                    // Progress is per avatar, not smoother, because a conversion is one long
                    // blocking call — pretending otherwise would be a lie drawn at 60fps. It is a
                    // no-op in batchmode, so the headless path is unaffected.
                    //
                    // Cancelable on purpose: a full corpus run is half an hour, and a run you
                    // cannot abort is its own trap. Cancelling is handled honestly below rather
                    // than leaving a half-finished Current/ that looks complete.
                    if (!Application.isBatchMode)
                    {
                        var elapsed = DateTime.Now - started;
                        string eta = i > 0
                            ? $", ~{TimeSpan.FromTicks(elapsed.Ticks / i * (scenes.Count - i)):mm\\:ss} left"
                            : "";
                        if (EditorUtility.DisplayCancelableProgressBar(
                                $"AvatarBridge regression — {i + 1}/{scenes.Count}",
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
                        // A thrown conversion is itself a result worth recording and diffing —
                        // "started throwing" and "stopped throwing" are both regressions.
                        digest = $"scene: {scenePath}\n\n[harness]\nEXCEPTION {e.GetType().Name}: {e.Message}\n";
                        failed++;
                    }

                    ran++;
                    string file = DigestName(scenePath);
                    // Belt and braces on the naming rule below: if two scenes ever collide again
                    // the run must say so, not silently cover one of them.
                    if (!written.Add(file))
                        Debug.LogError($"[Regression] digest name collision on '{file}' — " +
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
                // Said loudly and last. Current/ now holds a partial set that is indistinguishable
                // from a complete one by looking at it, and accepting it as a baseline would
                // quietly shrink the corpus to however far the run got — every avatar after the
                // cancel would read as "no baseline yet" forever after, and nobody would notice.
                File.WriteAllText(PartialMarker,
                    $"Cancelled after {ran} of {scenes.Count} scenes at {DateTime.Now:yyyy-MM-dd HH:mm}.\n" +
                    "AcceptCurrent refuses while this file exists. Re-run to clear it.\n");
                sb.AppendLine($"  CANCELLED after {ran} of {scenes.Count} — Current/ is PARTIAL. " +
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

        /// <summary>
        /// Digest filename from the scene's full path, not its basename.
        ///
        /// The first full run converted 56 avatars and left 51 files: this project has "OPEN ME",
        /// "OPENME", "OpenMe" and "Open_Me" scenes in four different folders, and Windows compares
        /// filenames case-insensitively, so five digests silently overwrote each other. Coverage
        /// vanished without a word — the worst thing a test harness can do.
        /// </summary>
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
            // OpenScene from script discards unsaved changes without prompting — which is what we
            // want, and what makes this safe to run in batchmode. The conversion mutates the
            // scene heavily and none of it is ever saved back.
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

            // Re-activate the source and everything above it. A conversion deactivates the avatar
            // it cloned from, so any scene saved after a conversion holds its source switched OFF
            // — and converting a deactivated avatar is not what a user does. Found by noticing the
            // original greyed out in the hierarchy next to a leftover conversion.
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
            // Deliberately no timestamp, no Unity version and NO AVATARBRIDGE VERSION: all three
            // change without the conversion changing, and every line in here has to earn its
            // place in a diff. The version was in here until 3.5.21 and was the worst of them —
            // it differs on literally every avatar after any release, so the first diff after a
            // bump reported all forty-nine as changed and buried whatever really moved. It is
            // written once per run to Current/_run.info instead, which nothing compares.
            sb.Append('\n');

            AppendReset(sb, reset);
            AppendSettings(sb, settings);
            AppendReport(sb, report);
            AppendCvrSide(sb, target);
            return sb.ToString();
        }

        class SceneReset
        {
            public int leftovers;      // previous conversions deleted out of the scene
            public int reactivated;    // objects switched back on above and including the source
            public string avatar = "";
        }

        /// <summary>
        /// Puts the scene back to how it was BEFORE anyone ever converted in it.
        ///
        /// Converting leaves two marks on a scene, and both persist if it is then saved: the
        /// converted avatar is added, and the SOURCE is switched off (BridgeConverter deactivates
        /// whatever it cloned from). A corpus run over scenes in that state is not measuring what
        /// a user does — it converts a deactivated avatar alongside a stale copy of its own
        /// previous output, whose asset references have since been regenerated and now dangle.
        /// 29 of the 48 controller references across these scenes were already broken that way.
        ///
        /// Nothing is saved back, so this is a per-run, in-memory reset: the scene files on disk
        /// are untouched and every run starts from the same place regardless of what state they
        /// were left in. That is the property the corpus actually needs — it makes the input
        /// deterministic without asking anyone to hand-clean 34 scenes.
        ///
        /// A leftover is identified by carrying a CVRAvatar and NO VRChat descriptor, which is
        /// exactly what conversion produces: it deletes the VRC components from its output. An
        /// avatar carrying both is someone's genuine work-in-progress and is left alone.
        /// </summary>
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
            // In the digest because it describes the INPUT. If a scene is later cleaned up by
            // hand these numbers change, the digest diffs, and the cause is named on the line —
            // rather than surfacing as unexplained movement somewhere in the animator sections.
            sb.Append("[scene reset]\n");
            sb.Append("  leftover conversions removed: ").Append(reset.leftovers).Append('\n');
            sb.Append("  objects re-activated: ").Append(reset.reactivated).Append('\n');
            sb.Append('\n');
        }

        /// <summary>
        /// The profile the corpus converts with. Deliberately NOT `new BridgeSettings()`.
        ///
        /// Two reasons every field is written out rather than only the ones that differ from the
        /// defaults. First, coverage: seven options are off by default, which means the locomotion
        /// grafter, the Action transplanter, native contacts and the SPI shader patcher — the code
        /// that shipped nine versions in one day — were never once exercised by the corpus.
        /// Turning them on puts 56 avatars through them. Second, and more important, PINNING: if a
        /// default in BridgeSettings is ever changed, an implicit corpus would silently change
        /// meaning and every digest would diff at once, looking exactly like a catastrophic
        /// regression. Stated in full, a defaults change moves the shipped product and leaves the
        /// corpus alone, which is what you want when you are trying to read a diff.
        ///
        /// This is NOT what a typical user gets. A digest change says "something moved with
        /// everything switched on", not "the default experience changed". For finding regressions
        /// that is the better trade; just don't read these digests as the out-of-the-box result.
        /// </summary>
        static BridgeSettings CorpusSettings() => new BridgeSettings
        {
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

            physicsTarget = PhysicsTarget.MagicaCloth2,
            deleteConvertedPhysBones = true,
            grabbyBonesSupport = true,
            useMagicaPresets = true,
            fitToPhysBone = true,
            derivePhysicsFromPhysBone = true,
            capParticleRadius = true,
            // The two that invent physics the author never made — on here for coverage.
            autoAssignNearbyColliders = true,
            addPhysicsToRiggedStyles = true,
            // Left off deliberately: both wreck specific avatars rather than exercising a path.
            transferAngleLimits = false,
            convertToePhysBones = false,

            stripGogoLoco = true,
            stripSpsSystems = true,
            extraStripKeywords = "",
            stripDeadMaterialAnimation = true,

            convertContacts = true,
            createDefaultColliderPointers = true,
            useNativeContacts = true,      // BETA — talks to a component internal to the game
            patchNonSpiShaders = true,     // BETA — writes patched shader copies

            convertConstraints = true,
            convertHeadChop = true,
            convertSpatialAudio = true,
            wireBlinkBlendshapes = true,
            addAvatarScaler = true,
            faceTrackingMode = FaceTrackingMode.Native,
        };

        static void AppendSettings(StringBuilder sb, BridgeSettings s)
        {
            // In the digest so a profile change is LOUD. Change CorpusSettings and all 56 digests
            // diff on these lines — which is correct, because all 56 conversions changed meaning.
            // Without this the same event would show up as unexplained churn deep in the animator
            // sections, and cost an afternoon.
            sb.Append("[settings]\n");
            foreach (var f in typeof(BridgeSettings).GetFields()
                         .OrderBy(f => f.Name, StringComparer.Ordinal))
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
            // Detail is TRUNCATED, and that is the whole point. These are documentation
            // paragraphs — the Kar digest had thirteen of them, the longest 1239 characters. They
            // get reworded whenever a message is improved, which is often and which changes
            // nothing about the conversion. Left whole, the first docs edit would light up every
            // avatar in the corpus as "changed" and the diff would stop being worth reading.
            // Category and Subject carry the behaviour; the opening of Detail is kept only to
            // tell two entries apart.
            var notable = report.Entries
                .Where(e => e.Status == ReportStatus.Error || e.Status == ReportStatus.Warning)
                .Select(e => $"  {e.Status.ToString().ToUpperInvariant()} [{e.Category}] {e.Subject} | {Brief(e.Detail)}")
                .OrderBy(s => s, StringComparer.Ordinal);
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
            foreach (var e in entries.OrderBy(x => x.machineName, StringComparer.Ordinal))
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
            // Type name and count only. Which cloth got which damping value belongs in a physics
            // digest, not this one — this is here to catch a pass that stops emitting a component
            // type altogether, which is how several regressions presented.
            var counts = target.GetComponentsInChildren<Component>(true)
                .Where(c => c != null)
                .Select(c => c.GetType().FullName)
                .Where(n => n.StartsWith("ABI.CCK", StringComparison.Ordinal)
                         || n.StartsWith("MagicaCloth", StringComparison.Ordinal)
                         || n.StartsWith("VRC.", StringComparison.Ordinal))
                .GroupBy(n => n)
                .OrderBy(g => g.Key, StringComparer.Ordinal);
            foreach (var g in counts) sb.Append("  ").Append(g.Count().ToString("D3")).Append("  ").Append(g.Key).Append('\n');
            sb.Append('\n');
        }

        static void AppendControllers(StringBuilder sb, CVRAvatar avatar, GameObject target)
        {
            // EVERY Animator in the hierarchy, not just the root's — and its controller reported
            // as null when it is null. The digest used to read only the root and silently skip a
            // null, which is exactly how five avatars shipped with a broken or absent Animator
            // controller and produced no diff at all: a dangling asset reference deserialises to
            // null, and skipping nulls made "broken" and "fine" render identically. A test that
            // cannot see a failure is not testing for it.
            // Judged on the SERIALIZED m_Controller, not the C# getter. The Sally investigation
            // established that the two can disagree: the getter answers from the native binding,
            // which both lags serialized writes and lies outright on a component whose rebind
            // failed — while the serialized value is what the prefab saves and what ChilloutVR
            // loads. When they disagree, both are printed, because a disagreement is itself a
            // finding worth diffing.
            sb.Append("[animators]\n");
            foreach (var a in target.GetComponentsInChildren<Animator>(true).OrderBy(x => HierarchyPath(target, x), StringComparer.Ordinal))
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
                    foreach (var p in pairs.OrderBy(p => p.Key != null ? p.Key.name : "", StringComparer.Ordinal))
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
            foreach (var p in ac.parameters.OrderBy(p => p.name, StringComparer.Ordinal))
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
                         .OrderBy(t => Dest(t), StringComparer.Ordinal)
                         .ThenBy(t => Conds(t), StringComparer.Ordinal))
            {
                sb.Append(indent).Append("any -> ").Append(Dest(t))
                  .Append(" self=").Append(t.canTransitionToSelf)
                  .Append(' ').Append(Timing(t))
                  .Append(' ').Append(Conds(t)).Append('\n');
            }

            // States sorted by name. Unity's array order is creation order, which shifts whenever
            // a pass adds a state earlier in its loop — a diff of that tells you nothing.
            foreach (var cs in sm.states.OrderBy(s => s.state.name, StringComparer.Ordinal))
            {
                var st = cs.state;
                sb.Append(indent).Append("state ").Append(prefix).Append(st.name)
                  .Append(" wd=").Append(st.writeDefaultValues)
                  .Append(" speed=").Append(F(st.speed))
                  .Append(st.mirror ? " mirror" : "")
                  .Append(" motion=").Append(MotionOf(st.motion)).Append('\n');

                var behaviours = st.behaviours
                    .Where(b => b != null)
                    .Select(b => b.GetType().Name)
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();
                if (behaviours.Count > 0)
                    sb.Append(indent).Append("  behaviours: ").Append(string.Join(",", behaviours)).Append('\n');

                foreach (var t in st.transitions
                             .OrderBy(t => Dest(t), StringComparer.Ordinal)
                             .ThenBy(t => Conds(t), StringComparer.Ordinal))
                {
                    sb.Append(indent).Append("  -> ").Append(Dest(t))
                      .Append(' ').Append(Timing(t))
                      .Append(' ').Append(Conds(t)).Append('\n');
                }
            }

            // Sub-state machines, recursed so a graft nested one level down is still described.
            foreach (var child in sm.stateMachines.OrderBy(c => c.stateMachine.name, StringComparer.Ordinal))
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
                .OrderBy(s => s, StringComparer.Ordinal);
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
                // Length and loop flag both matter and both have been the bug: 3.5.1 shipped
                // grafted clips that carried their FBX loop settings, and 3.5.4 shipped a wing
                // flap loop-matched to a state that never ended.
                return $"clip:{clip.name}(len={F(clip.length)},loop={clip.isLooping})";
            }

            return motion.GetType().Name + ":" + motion.name;
        }

        // ---------------------------------------------------------------- formatting

        // Fixed precision, invariant, and negative zero folded away — Unity hands back -0 for
        // plenty of computed values and it flips sign between runs for no reason at all.
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

        /// <summary>Hierarchy path of a component relative to the avatar root, "&lt;root&gt;" for the root itself.</summary>
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
