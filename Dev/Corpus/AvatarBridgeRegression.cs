// Regression harness. Development only, never shipped.
//
// Converts every avatar, reduces each to a deterministic text digest of
// behaviour (not the GUID-noisy .controller YAML), diffs against the last
// accepted run.
// Run through Dev/Corpus/run-corpus.sh: a raw launch is unthrottled and
// skips the stale-deploy and open-editor checks.
// Headless for determinism: batchmode answers VRCFury's Write Defaults
// dialog with Auto-Fix. Interactive runs: Auto-Fix, never "Skip and stop asking".

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
        // (scene path, converted root), once per conversion. See ConvertAndDigest.
        public static event Action<string, GameObject> Converted;

        // Digests live in the repo (AVATARBRIDGE_REPO) to survive a reimport.
        static string Repo
        {
            get
            {
                var repo = Environment.GetEnvironmentVariable("AVATARBRIDGE_REPO");
                if (string.IsNullOrEmpty(repo))
                    throw new InvalidOperationException(
                        "Set the AVATARBRIDGE_REPO environment variable to the AvatarBridge checkout path.");
                return repo.Replace('\\', '/').TrimEnd('/');
            }
        }

        static string Root
        {
            get
            {
                string repo = Repo;
                // Each profile keeps its own Baseline and Current, or they overwrite each other.
                string suffix = YapsMode ? "/Regression/Yaps" : "/Regression";
                // Advisor digests move when the advice moves, so never compare
                // with the fixed profile. The advisor picks the solver itself,
                // so no DynamicBone folder under it.
                if (AdvisorMode)
                {
                    suffix += "/Advisor";
                }
                else if (Environment.GetEnvironmentVariable("AVATARBRIDGE_PHYSICS") == "DynamicBone")
                {
                    suffix += "/DynamicBone";
                }
                // A different profile again, so never compared with the default one.
                if (SoftMode) suffix += "/Soft";
                return repo + suffix;
            }
        }
        static string BaselineDir => Root + "/Baseline";
        static string CurrentDir => Root + "/Current";
        // Removed only on an uncancelled finish: accepting a partial Current/
        // would silently shrink the corpus.
        static string PartialMarker => CurrentDir + "/PARTIAL-DO-NOT-ACCEPT";
        // Digest names that have a baseline and got no digest this run, for the report.
        // Not a .txt, so neither the comparison nor Accept picks it up.
        static string VanishedList => CurrentDir + "/_vanished.list";

        // Scenes that are never avatars. Matched as path substrings.
        static readonly string[] BuiltInExcluded =
        {
            "/AvatarBridgeOutput/", "/CVR.CCK/", "/MagicaCloth2/", "/UnityTechnologies/",
            "/Samples/", "/Scenes/SampleScene", "/MISC/",
        };

        // Local scene lists: [excluded] and [quickset], "#" comments.
        // NOT under Root, which moves per mode: other modes missed the file,
        // read it as an empty list and digested VRCFury's bake failures.
        static string CorpusConfigPath => Repo + "/Regression/corpus.cfg";

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

        // Most regression-prone scenes, checked in minutes. Keep a healthy
        // control in it: a control that moves means a fix masked something.
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

        [MenuItem("Tools/Avatar Bridge/Dev/Corpus/Regression: run quick set")]
        public static void RunQuick() => Run(QuickSet(), "quick");

        [MenuItem("Tools/Avatar Bridge/Dev/Corpus/Regression: run all scenes")]
        public static void RunAll() => Run(AllAvatarScenes(), "all");

        [MenuItem("Tools/Avatar Bridge/Dev/Corpus/Regression: accept current as baseline")]
        public static void AcceptCurrent()
        {
            if (!Directory.Exists(CurrentDir))
            {
                Debug.LogError("[Regression] nothing in Current/ to accept. Run first.");
                return;
            }
            if (File.Exists(PartialMarker))
            {
                Debug.LogError("[Regression] REFUSING: the last run was cancelled or never finished, so Current/ is " +
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
            Exit(Run(AllAvatarScenes(), "all"));
        }

        public static void RunQuickBatch()
        {
            Exit(Run(QuickSet(), "quick"));
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
            Exit(Run(scenes, $"subset({scenes.Length})"));
        }

        // One process died in the native allocator past ~70 conversions at 24 GB, so
        // run-corpus.sh caps (AVATARBRIDGE_PER_PROCESS) and resumes (AVATARBRIDGE_RESUME=1,
        // keeps Current/). Exit 4 = stopped early, more to do.
        static bool yielded;

        static void Exit(int changed) => EditorApplication.Exit(yielded ? 4 : changed == 0 ? 0 : 1);

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

            // Invariant culture, so a comma-decimal machine diffs cleanly against a point-decimal one.
            var previousCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            // VRCFury and the VRCSDK pop modals on failed bakes; batchmode auto-answers them.
            if (!Application.isBatchMode && scenes.Count > 4)
            {
                Debug.LogWarning(
                    "[Regression] running " + scenes.Count + " scenes interactively: VRCFury and " +
                    "the VRCSDK will block on modal dialogs for any avatar that fails to bake. " +
                    "Close Unity and run headless, throttled, instead:\n" +
                    "  bash Dev/Corpus/run-corpus.sh   (--quick, --subset FILE or --dynbone as needed)");
            }

            bool resume = Environment.GetEnvironmentVariable("AVATARBRIDGE_RESUME") == "1";
            int.TryParse(Environment.GetEnvironmentVariable("AVATARBRIDGE_PER_PROCESS"), out int perProcess);
            yielded = false;

            // A leftover digest from another run would be compared as current. A resume keeps them.
            if (!resume && Directory.Exists(CurrentDir))
            {
                foreach (var stale in Directory.GetFiles(CurrentDir, "*.txt")) File.Delete(stale);
            }
            Directory.CreateDirectory(CurrentDir);
            File.Delete(VanishedList);

            // Which build produced this run, kept OUT of the compared digests (see BuildDigest).
            // Not a .txt, so neither the comparison nor Accept picks it up.
            if (resume)
            {
                File.AppendAllText(CurrentDir + "/_run.info", $"resumed: {DateTime.Now:yyyy-MM-dd HH:mm}\n");
            }
            else
            {
                File.WriteAllText(CurrentDir + "/_run.info",
                    $"bridge: {BridgeDefines.Version}\nunity: {Application.unityVersion}\n" +
                    $"started: {DateTime.Now:yyyy-MM-dd HH:mm}\n");
            }

            // Up before the first avatar. A crash or a kill never reaches the end of this
            // method, so writing it only on cancel left those runs unmarked.
            if (!resume || !File.Exists(PartialMarker))
            {
                File.WriteAllText(PartialMarker,
                    $"Started {DateTime.Now:yyyy-MM-dd HH:mm} and never finished: cancelled, crashed or killed.\n" +
                    "AcceptCurrent refuses while this file exists. Re-run to clear it.\n");
            }

            var changes = new List<string>();
            var missing = new List<string>();
            var noAvatar = new List<string>();
            var sweep = new SweepTally();
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int ran = 0, failed = 0, converted = 0, reused = 0;
            bool cancelled = false;
            var started = DateTime.Now;

            try
            {
                for (int i = 0; i < scenes.Count; i++)
                {
                    var scenePath = scenes[i];
                    string name = Path.GetFileNameWithoutExtension(scenePath);

                    // Per avatar: a conversion is one blocking call. Cancel handled below.
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

                    string file = DigestName(scenePath);
                    string kept = Path.Combine(CurrentDir, file);
                    string digest;
                    if (resume && File.Exists(kept))
                    {
                        digest = File.ReadAllText(kept);
                        if (digest.Contains("\n[harness]\nEXCEPTION ")) failed++;
                        reused++;
                    }
                    else
                    {
                        if (perProcess > 0 && converted >= perProcess)
                        {
                            yielded = true;
                            break;
                        }
                        converted++;
                        try
                        {
                            digest = ConvertAndDigest(scenePath);
                            if (digest == null) { noAvatar.Add(scenePath); continue; }   // no avatar in this scene
                        }
                        catch (Exception e)
                        {
                            // A thrown conversion is a result worth diffing.
                            // Starting and stopping throwing both regress.
                            digest = $"scene: {scenePath}\n\n[harness]\nEXCEPTION {e.GetType().Name}: {e.Message}\n";
                            failed++;
                        }
                    }

                    ran++;
                    // Belt and braces on the naming rule below: if two scenes ever collide again
                    // the run must say so, not silently cover one of them.
                    if (!written.Add(file))
                        Debug.LogError($"[Regression] digest name collision on '{file}': " +
                                       $"'{scenePath}' has overwritten an earlier scene's digest.");
                    File.WriteAllText(kept, digest);

                    string baseline = Path.Combine(BaselineDir, file);
                    if (!File.Exists(baseline)) { missing.Add(name); TallySweep(sweep, name, digest, null); continue; }
                    string before = File.ReadAllText(baseline);
                    if (before != digest)
                    {
                        RegressionReport.LineDiff(before, digest, out int rem, out int add);
                        changes.Add($"{name}  (-{rem} +{add})");
                    }
                    TallySweep(sweep, name, digest, before);
                }
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previousCulture;
                if (!Application.isBatchMode) EditorUtility.ClearProgressBar();
            }

            if (yielded)
            {
                // No summary: the [Regression/...] line is what tells the launcher the run is over.
                Debug.Log($"[Regression] stopping after {converted} conversion(s) in this process " +
                          $"({ran} of {scenes.Count} scenes have digests); relaunch with AVATARBRIDGE_RESUME=1 to go on.");
                return 0;
            }

            // An avatar that stops producing a digest is a change, not a silence
            // (missing script, scene moved under an excluded path). Full runs only.
            var vanished = noAvatar.Select(s => DigestName(s))
                .Where(f => File.Exists(Path.Combine(BaselineDir, f)))
                .ToList();
            if (label == "all" && !cancelled && Directory.Exists(BaselineDir))
            {
                var expected = new HashSet<string>(scenes.Select(s => DigestName(s)), StringComparer.OrdinalIgnoreCase);
                vanished.AddRange(Directory.GetFiles(BaselineDir, "*.txt")
                    .Select(f => Path.GetFileName(f))
                    .Where(f => !expected.Contains(f))
                    .OrderBy(f => f, StringComparer.Ordinal));
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[Regression/{label}] {ran} avatar(s) in {(DateTime.Now - started).TotalSeconds:F0}s" +
                          (reused > 0 ? $" ({reused} converted by earlier processes of this run)" : "") +
                          (failed > 0 ? $", {failed} threw" : ""));
            if (ran == 0)
                sb.AppendLine("  NOTHING RAN: no scene yielded an avatar. This is not a pass.");
            if (noAvatar.Count > 0)
                sb.AppendLine($"  no avatar found ({noAvatar.Count}): " +
                              string.Join(", ", noAvatar.Select(s => Path.GetFileNameWithoutExtension(s))));
            if (missing.Count > 0)
                sb.AppendLine($"  no baseline yet ({missing.Count}): {string.Join(", ", missing)}");
            if (vanished.Count > 0)
            {
                sb.AppendLine($"  VANISHED ({vanished.Count}), a baseline with no digest this run:");
                foreach (var v in vanished) sb.AppendLine("    " + Path.GetFileNameWithoutExtension(v));
            }
            if (changes.Count == 0)
                sb.AppendLine(missing.Count > 0 || vanished.Count > 0 ? "  nothing else changed." : "  no changes.");
            else
            {
                sb.AppendLine($"  CHANGED ({changes.Count}):");
                foreach (var c in changes) sb.AppendLine("    " + c);
                sb.AppendLine($"  compare: {CurrentDir} vs {BaselineDir}");
            }
            AppendSweepReport(sb, sweep);

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
                return changes.Count + vanished.Count;
            }
            File.Delete(PartialMarker);
            if (vanished.Count > 0) File.WriteAllLines(VanishedList, vanished);

            // The run as a page a tester can read, beside the digests.
            try
            {
                string page = RegressionReport.Write(BaselineDir, CurrentDir, Root + "/report.html", label);
                sb.AppendLine($"  report: {page}");
            }
            catch (Exception e)
            {
                sb.AppendLine($"  report failed: {e.GetType().Name}: {e.Message}");
            }

            Debug.Log(sb.ToString());
            // An empty run exits non-zero: zero avatars compared is not zero changed.
            return Math.Max(changes.Count + vanished.Count, ran == 0 ? 1 : 0);
        }

        // Reads the [sweep] blocks back, or a baseline with stuck toggles passes forever.
        // Run totals, names of anything newly stuck or refused, and old failures still carried.
        class SweepTally
        {
            public int Swept, Params, Responded, Stuck, Refused, Invalid, Unswept;
            public readonly List<string> Worse = new List<string>();
            public readonly List<string> Carrying = new List<string>();
        }

        static void TallySweep(SweepTally tally, string name, string digest, string baseline)
        {
            if (!RegressionReport.ReadSweep(digest, out var now, out var names)) { tally.Unswept++; return; }
            tally.Swept++;
            tally.Params += now[0];
            tally.Responded += now[1];
            tally.Stuck += now[2];
            tally.Refused += now[3];
            tally.Invalid += now[4];

            int[] was = null;
            var old = new HashSet<string>();
            if (baseline != null && RegressionReport.ReadSweep(baseline, out was, out var oldNames)) old.UnionWith(oldNames);

            var fresh = names.Where(n => !old.Contains(n)).ToList();
            if (now[4] != 0 && (was == null || was[4] == 0)) fresh.Add("invalid");
            if (fresh.Count > 0) tally.Worse.Add($"{name}  +{string.Join("  +", fresh)}");
            else if (names.Count > 0 || now[4] != 0) tally.Carrying.Add($"{name} ({names.Count + now[4]})");
        }

        static void AppendSweepReport(StringBuilder sb, SweepTally sweep)
        {
            if (sweep.Swept == 0 && sweep.Unswept == 0) return;
            sb.AppendLine($"  [sweep] {sweep.Swept} swept: {sweep.Params} params, " +
                          $"{sweep.Responded} responded, {sweep.Stuck} stuck, " +
                          $"{sweep.Refused} refused, {sweep.Invalid} invalid" +
                          (sweep.Unswept > 0 ? $", {sweep.Unswept} not swept" : ""));
            if (sweep.Worse.Count > 0)
            {
                sb.AppendLine($"  [sweep] WORSE than baseline ({sweep.Worse.Count}):");
                foreach (var w in sweep.Worse) sb.AppendLine("    " + w);
            }
            if (sweep.Carrying.Count > 0)
                sb.AppendLine($"  [sweep] carrying old failures ({sweep.Carrying.Count}): " +
                              string.Join(", ", sweep.Carrying));
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
            // Discards unsaved changes without prompting; wanted, nothing is saved back.
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
            if (descriptor == null) return PropDigest(scene, scenePath);

            // A saved scene can hold its source switched off by an earlier conversion.
            for (var t = descriptor.transform; t != null; t = t.parent)
            {
                if (t.gameObject.activeSelf) continue;
                t.gameObject.SetActive(true);
                reset.reactivated++;
            }

            // Advice reads the source before Convert clones it, as the window does.
            AdvisorTrace advised = null;
            var settings = AdvisorMode ? AdvisorSettings(descriptor, out advised) : CorpusSettings();
            var report = BridgeConverter.Convert(descriptor, settings);
            // Never Selection: a failed conversion leaves it on the source or a half-built
            // target, and the sweep would then digest that as the conversion.
            var target = report.ConvertedRoot;
            // A probe riding the run reads the conversion here, before the sweep
            // moves the scene. It writes its own output, never the digest.
            try { Converted?.Invoke(scenePath, target); }
            catch (Exception e) { Debug.LogException(e); }

            var sb = new StringBuilder();
            sb.Append("avatar: ").Append(descriptor.gameObject.name).Append('\n');
            sb.Append("scene: ").Append(scenePath).Append('\n');
            // No timestamp or versions: they change without the conversion
            // changing. The version goes into Current/_run.info once per run.
            sb.Append('\n');

            AppendReset(sb, reset);
            if (advised != null) AppendAdvisor(sb, advised);
            AppendSettings(sb, settings);
            AppendReport(sb, report);
            AppendCvrSide(sb, target);
            AppendPlugMaterials(sb, target);
            AppendSweep(sb, target);
            return Stable(sb.ToString());
        }

        // Each root named "Fixture Prop..." (FixtureBuilder.RunPropsOnly) is re-baked,
        // made a prop and verified as a user would. Named roots only: loose plugs are not cases.
        static string PropDigest(Scene scene, string scenePath)
        {
            var roots = scene.GetRootGameObjects()
                .Where(r => r.name.StartsWith("Fixture Prop", StringComparison.Ordinal))
                .OrderBy(r => r.name, StringComparer.Ordinal)
                .ToList();
            if (roots.Count == 0) return null;

            var sb = new StringBuilder();
            sb.Append("scene: ").Append(scenePath).Append("\n\n");
            foreach (var root in roots)
            {
                sb.Append("[prop ").Append(root.name).Append("]\n");
                var plug = root.GetComponentInChildren<AvatarBridge.Yaps.YapsPlug>(true);
                if (plug != null)
                {
                    var baked = YapsNativeBuilder.Bake(plug);
                    sb.Append("  bake: ").Append(baked.Ok ? "ok " : "FAILED ").Append(Brief(baked.Message)).Append('\n');
                }
                var made = YapsPropBuilder.MakeProp(root);
                sb.Append("  make: ").Append(made.Ok ? "ok " : "FAILED ").Append(Brief(made.Message)).Append('\n');
                foreach (var note in made.Notes) sb.Append("    note: ").Append(Brief(note)).Append('\n');
                var verified = YapsPropBuilder.Verify(root);
                sb.Append("  verify: ").Append(verified.Ok ? "ok " : "FAILED ").Append(Brief(verified.Message)).Append('\n');

                var pickup = root.GetComponent<CVRPickupObject>();
                if (pickup != null)
                    sb.Append("  pickup: ").Append(pickup.moveMode).Append(", reach ").Append(F(pickup.maximumGrabDistance))
                      .Append(pickup.disallowTheft ? ", theft disallowed\n" : ", theft allowed\n");
                var spawnable = root.GetComponent<CVRSpawnable>();
                if (spawnable != null)
                    sb.Append("  spawnable: height ").Append(F(spawnable.spawnHeight)).Append(", values [")
                      .Append(string.Join(",", spawnable.syncValues.Select(v => v.name))).Append("]\n");
                foreach (var c in root.GetComponents<Collider>())
                    sb.Append("  collider: ").Append(ColliderOf(c)).Append('\n');
                // Every component, not the CCK-only count an avatar gets: a prop is
                // a handful of objects, and a lost light or renderer is the regression.
                foreach (var c in root.GetComponentsInChildren<Component>(true).Where(c => c != null && !(c is Transform)))
                {
                    sb.Append("  ").Append(HierarchyPath(root, c)).Append(": ").Append(c.GetType().Name);
                    if (c is Light l) sb.Append(" range ").Append(l.range.ToString("0.0000", CultureInfo.InvariantCulture));
                    sb.Append('\n');
                }
                sb.Append('\n');
            }
            return Stable(sb.ToString());
        }

        static string ColliderOf(Collider c)
        {
            switch (c)
            {
                case BoxCollider b: return $"box {V3(b.center)} {V3(b.size)}";
                case CapsuleCollider k: return $"capsule {V3(k.center)} r {F(k.radius)} h {F(k.height)} axis {k.direction}";
                case SphereCollider s: return $"sphere {V3(s.center)} r {F(s.radius)}";
                default: return c.GetType().Name;
            }
        }

        // Drives each menu parameter and names what does not come back (names
        // only: readings vary run to run). Runs last: it moves the scene.
        static void AppendSweep(StringBuilder sb, GameObject target)
        {
            sb.Append("[sweep] ");
            if (target == null)
            {
                sb.Append("skipped: no converted root\n\n");
                return;
            }
            int findings;
            // Exceptions a state behaviour throws inside Animator.Update are logged, not
            // thrown, and the write it was making is lost. Types only, so digests stay stable.
            var thrown = new SortedSet<string>(StringComparer.Ordinal);
            Application.LogCallback note = (condition, stack, type) =>
            {
                if (type == LogType.Exception) thrown.Add(condition.Split(':')[0]);
            };
            Application.logMessageReceived += note;
            try
            {
                findings = ToggleSweep.Sweep(target);
            }
            catch (Exception e)
            {
                // The digest keeps the type; the stack goes to the log, or the next one is a guess.
                Debug.LogException(e);
                sb.Append("failed: ").Append(e.GetType().Name).Append('\n').Append('\n');
                return;
            }
            finally
            {
                Application.logMessageReceived -= note;
            }
            var r = ToggleSweep.LastResult;
            if (findings < 0)
            {
                sb.Append("skipped: no controller\n\n");
                return;
            }
            // Zero swept printed as params=0 ... invalid=0, which read as clean.
            if (r.Parameters == 0)
            {
                sb.Append("skipped: no user-facing parameters\n\n");
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
            // Not counted: a rest pose that creeps is a finding of its own, not a stuck toggle.
            if (r.Unstable != null && r.Unstable.Count > 0)
            {
                sb.Append("  unstable at rest: ")
                  .Append(string.Join(", ", r.Unstable.OrderBy(n => n, StringComparer.Ordinal))).Append('\n');
            }
            if (thrown.Count > 0)
            {
                sb.Append("  threw while driving, writes lost: ").Append(string.Join(", ", thrown)).Append('\n');
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
        // Ordered output sorts by StableSampleOrder, which strips the same ids:
        // sorting raw text then redacting moved identical lines around.
        static string Stable(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return s;
            }
            // A GUID stamped into an object name, fresh on every bake.
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
        }

        // Saves the ResetScene cleanup to disk, so a scene converted by hand
        // stops reporting "leftover conversions removed: 1" every run.
        //   AVATARBRIDGE_CLEAN=<file of scene paths>
        //   Unity.exe -batchmode -quit -executeMethod AvatarBridge.Regression.RegressionRunner.CleanScenesBatch
        public static void CleanScenesBatch()
        {
            string listFile = Environment.GetEnvironmentVariable("AVATARBRIDGE_CLEAN");
            if (string.IsNullOrEmpty(listFile) || !File.Exists(listFile))
            {
                Debug.LogError("[Regression] AVATARBRIDGE_CLEAN must name a file of scene paths.");
                EditorApplication.Exit(2);
                return;
            }
            int cleaned = 0;
            foreach (string line in File.ReadAllLines(listFile))
            {
                string path = line.Trim();
                if (path.Length == 0 || path.StartsWith("#", StringComparison.Ordinal)) continue;
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var reset = ResetScene(scene);
                int reactivated = 0;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var d in root.GetComponentsInChildren<VRCAvatarDescriptor>(true))
                    {
                        for (var t = d.transform; t != null; t = t.parent)
                        {
                            if (t.gameObject.activeSelf) continue;
                            t.gameObject.SetActive(true);
                            reactivated++;
                        }
                    }
                }
                if (reset.leftovers > 0 || reactivated > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    cleaned++;
                }
                Debug.Log($"[Regression] cleaned \"{path}\": {reset.leftovers} conversion(s), {reactivated} re-activated");
            }
            Debug.Log($"[Regression] {cleaned} scene(s) written");
            EditorApplication.Exit(0);
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

        // AVATARBRIDGE_YAPS=1 = the user default (Regression/Yaps). Unset measures
        // the opt-out, which still has to hold. Folder names predate the default.
        static bool YapsMode =>
            Environment.GetEnvironmentVariable("AVATARBRIDGE_YAPS") == "1";

        // AVATARBRIDGE_PROFILE=advisor converts each avatar as Apply all would:
        // "does the advice work", where CorpusSettings asks "did the code change".
        static bool AdvisorMode =>
            Environment.GetEnvironmentVariable("AVATARBRIDGE_PROFILE") == "advisor";

        // AVATARBRIDGE_SOFT=1 turns the soft parts on, the one pass too slow for a full run.
        static bool SoftMode =>
            Environment.GetEnvironmentVariable("AVATARBRIDGE_SOFT") == "1";

        sealed class AdvisorTrace
        {
            public List<string> Unticked = new List<string>();
            public List<string> Applied = new List<string>();
            public List<string> Blocked = new List<string>();
            public List<string> LeftToUser = new List<string>();
        }

        static BridgeSettings AdvisorSettings(VRCAvatarDescriptor descriptor, out AdvisorTrace trace)
        {
            trace = new AdvisorTrace();
            // A new user's defaults, overriding only what the corpus cannot survive
            // (reasons at CorpusSettings).
            var settings = new BridgeSettings
            {
                convertYapsSystems = YapsMode,
                cloneAvatar = true,
                outputFolder = "Assets/AvatarBridgeOutput",
                slimTexturesOnConvert = false,
            };

            // Picking the avatar, then Apply all: the two steps the window
            // takes, through the same two calls it makes.
            trace.Unticked.AddRange(AvatarAdvisor.MatchOptionalLayers(descriptor, settings));
            foreach (var a in AvatarAdvisor.Analyse(descriptor, settings))
            {
                if (AvatarAdvisor.IsRecommendation(a))
                {
                    a.Apply(settings);
                    trace.Applied.Add($"{a.Kind}: {a.Setting}");
                }
                else if (a.Kind == AdviceKind.Blocked)
                {
                    trace.Blocked.Add(a.Setting);
                }
                else if (a.Kind == AdviceKind.Manual)
                {
                    trace.LeftToUser.Add(a.Setting);
                }
            }
            return settings;
        }

        // Advice changes show here, code changes everywhere else. Sorted: Analyse's order is no promise.
        static void AppendAdvisor(StringBuilder sb, AdvisorTrace trace)
        {
            void Section(string title, List<string> items)
            {
                sb.Append("  ").Append(title).Append(": ");
                sb.Append(items.Count == 0 ? "none" : string.Join(" | ", items.OrderBy(s => s, StringComparer.Ordinal)));
                sb.Append('\n');
            }
            sb.Append("[advisor]\n");
            Section("unticked on picking the avatar", trace.Unticked);
            Section("applied by Apply all", trace.Applied);
            Section("blocked, no fix applied", trace.Blocked);
            Section("left to the user", trace.LeftToUser);
            sb.Append('\n');
        }

        internal static BridgeSettings CorpusSettings() => new BridgeSettings
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

            // Off here, on for a user: it rewrites the corpus's own texture
            // imports. The weight card in the digest measures it.
            slimTexturesOnConvert = false,

            toggleStyle = ToggleStyle.AnimatorLayers,
            preserveParameterSyncState = true,
            exposeMenulessSyncedParameters = true,

            // AVATARBRIDGE_PHYSICS=DynamicBone runs the fallback solver, into its own folder.
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
            gogoEmotes = true,
            stripSpsSystems = true,
            extraStripKeywords = "",
            stripDeadMaterialAnimation = true,

            convertContacts = true,
            createDefaultColliderPointers = true,
            sizeContactZonesForLargest = true,
            patchNonSpiShaders = true,     // BETA: writes patched shader copies
            // Off unless AVATARBRIDGE_SOFT=1: each body shader compiles the jelly for
            // 20-60 s, a full run took ~12 h. Measure on a subset.
            squashSoftParts = SoftMode,

            convertConstraints = true,
            convertHeadChop = true,
            convertSpatialAudio = true,
            wireBlinkBlendshapes = true,
            addAvatarScaler = true,
            faceTrackingMode = FaceTrackingMode.Native,
        };

        static void AppendSettings(StringBuilder sb, BridgeSettings s)
        {
            // A profile change diffs every digest, correctly.
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

            // Errors and warnings only: Converted entries run to thousands and
            // their wording churns; the counts above catch volume. Detail is
            // truncated for the same churn; Category and Subject carry behaviour.
            var notable = report.Entries
                .Where(e => e.Status == ReportStatus.Error || e.Status == ReportStatus.Warning)
                .Select(e => $"  {e.Status.ToString().ToUpperInvariant()} [{e.Category}] {e.Subject} | {Brief(e.Detail)}")
                .OrderBy(s => s, StableSampleOrder.Instance);
            foreach (var line in notable) sb.Append(line).Append('\n');
            sb.Append('\n');
        }

        // Baked plug material values: a reconvert dropped them all (queue 38) and
        // the digest only showed one menu row.
        static void AppendPlugMaterials(StringBuilder sb, GameObject target)
        {
            if (target == null) return;
            string Num(Material m, string p) => m.HasProperty(p) ? F(m.GetFloat(p)) : "<none>";
            var lines = new List<string>();
            // The plugs' own meshes: a soft body's shader declares the plug's properties
            // too, and every body slot would read as a plug with no bake.
            var plugMeshes = target.GetComponentsInChildren<AvatarBridge.Yaps.YapsPlug>(true)
                .Select(p => p.Target).Where(r => r != null).Distinct();
            foreach (var r in plugMeshes)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.HasProperty("_YAPS_Bake")) continue;
                    lines.Add("  " + HierarchyPath(target, r) + " [" + i + "]: bake " + (m.GetTexture("_YAPS_Bake") != null ? "yes" : "NONE")
                        + ", vertices " + Num(m, "_YAPS_VertexCount") + ", length " + Num(m, "_YAPS_Length")
                        + ", self " + Num(m, "_YAPS_SelfTag") + ", atlas " + Num(m, "_YAPS_UseAtlas"));
                }
            }
            if (lines.Count == 0) return;
            sb.Append("[plug materials]\n");
            foreach (var line in lines.OrderBy(l => l, StringComparer.Ordinal)) sb.Append(line).Append('\n');
            sb.Append('\n');
        }

        static void AppendCvrSide(StringBuilder sb, GameObject target)
        {
            if (target == null) { sb.Append("[cvr]\nNO TARGET: conversion produced nothing\n"); return; }

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
            AppendHiddenMeshes(sb, avatar);
            AppendPointerFamilies(sb, avatar);
            AppendPhysics(sb, avatar, target);
            AppendControllers(sb, avatar, target);
        }

        static void AppendAas(StringBuilder sb, CVRAvatar avatar)
        {
            sb.Append("[advanced avatar settings]\n");
            var entries = avatar.avatarSettings != null ? avatar.avatarSettings.settings : null;
            if (entries == null || entries.Count == 0) { sb.Append("  <none>\n\n"); return; }

            // Sorted by name: menu order shifts everything after any append. Order changes get their own line.
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

            // Marker lights by range: the range IS the protocol (hole, ring, front, tip).
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

            AppendYapsTags(sb);
        }

        // Tag words decide whether a plug opens a socket; a wrong word is silence in game.
        // Read from the bake prep: the target only holds their hashes.
        // Counted by word, since a row per socket churns on every rename.
        static void AppendYapsTags(StringBuilder sb)
        {
            var sockets = YapsBakePrep.AuthoredSocketTags;
            var answers = YapsBakePrep.AuthoredAnswers;
            var refuses = YapsBakePrep.AuthoredRefuses;
            if (sockets.Count == 0 && answers.Count == 0 && refuses.Count == 0) return;

            var words = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var list in sockets.Values) CountTags(words, "socket ", list);
            foreach (var list in answers.Values) CountTags(words, "plug +", list);
            foreach (var list in refuses.Values) CountTags(words, "plug -", list);

            sb.Append("[yaps tags]\n");
            sb.Append("sockets=").Append(sockets.Count)
              .Append(" answering=").Append(answers.Count)
              .Append(" refusing=").Append(refuses.Count).Append('\n');
            foreach (var pair in words)
            {
                sb.Append("  ").Append(pair.Value.ToString("D3")).Append("  ")
                  .Append(pair.Key).Append('\n');
            }
            sb.Append('\n');
        }

        // Blank and case are not the author's meaning, and either would split
        // one word into two rows.
        static void CountTags(SortedDictionary<string, int> into, string prefix, List<string> list)
        {
            if (list == null) return;
            foreach (string word in list)
            {
                if (string.IsNullOrWhiteSpace(word)) continue;
                string key = prefix + word.Trim().ToLowerInvariant();
                into.TryGetValue(key, out int had);
                into[key] = had + 1;
            }
        }

        // `reachable` belongs at zero: renderers called dead that a clip can
        // switch on, read from the clips, not the survey. Above zero, the
        // optimiser strips a working renderer.
        static void AppendHiddenMeshes(StringBuilder sb, CVRAvatar avatar)
        {
            var survey = AvatarSurvey.Build(avatar);
            var weight = AvatarWeight.Measure(avatar, survey);
            if (weight.Dead.Count == 0) return;

            var switched = DeadRendererProbe.SwitchedByAnyClip(avatar, out int clips);
            int reachable = weight.Dead.Count(p => DeadRendererProbe.Reachable(p, switched));

            sb.Append("[hidden meshes]\n");
            sb.Append($"dead={weight.Dead.Count} freesNothing={weight.DeadFreesNothing.Count} ")
              .Append($"reclaim={(weight.DeadBytes / 1048576f).ToString("0.0", CultureInfo.InvariantCulture)}MB ")
              .Append($"clips={clips} reachable={reachable}\n");
            foreach (string path in weight.Dead.OrderBy(p => p, StableSampleOrder.Instance))
            {
                sb.Append("  ").Append(path)
                  .Append(weight.DeadFreesNothing.Contains(path) ? "  (frees nothing)" : "")
                  .Append(DeadRendererProbe.Reachable(path, switched) ? "  REACHABLE" : "")
                  .Append('\n');
            }
            sb.Append('\n');
        }

        // Each socket has a plain and a _SelfNotOnHips pointer, each a contact of the
        // instance's 512 pairs. `twinOnly` (belongs at zero) counts triggers that
        // accept the twin without the base, which collapsing the pair would silence.
        static void AppendPointerFamilies(StringBuilder sb, CVRAvatar avatar)
        {
            var f = PointerFamilyProbe.Read(avatar);
            if (f.Total == 0) return;

            sb.Append("[pointer families]\n");
            sb.Append($"pointers={f.Total} families={f.Emitted.Count} twinPairs={f.TwinPairs} ")
              .Append($"twinPointers={f.TwinPointers} twinOnly={f.TwinOnly.Count}\n");

            // Twins only. The full family list is the contact census already
            // in [components], and repeating it here is churn.
            foreach (var pair in f.Emitted.Where(p =>
                         p.Key.EndsWith(PointerFamilyProbe.Twin, StringComparison.Ordinal))
                     .OrderBy(p => p.Key, StableSampleOrder.Instance))
            {
                f.Listened.TryGetValue(pair.Key, out int heard);
                sb.Append("  ").Append(pair.Value.ToString("D3")).Append("  ")
                  .Append(heard > 0 ? $"heard by {heard}" : "heard by nothing").Append("  ")
                  .Append(pair.Key).Append('\n');
            }
            foreach (string one in f.TwinOnly) sb.Append("  TWIN ONLY  ").Append(one).Append('\n');
            sb.Append('\n');
        }

        // By name, not a compile-time reference, so the digest's shape does not
        // depend on installed packages; a missing define would read as lost cloth.
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

            // Counted separately: a disabled component and a never-activated holder
            // are different bugs with one symptom.
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

            // Curves that switch cloth on or off; any change in the count is the signal.
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

            // Contacts by route, which decides who sees it and how often it fires:
            // "#" names are computed per client, plain names ride the settings
            // stream, a bridge layer pairs the two. No other block shows a lane change.
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
            // "<unreadable>" = reflection lost MagicaCloth's shape; "<none>" = truly rootless. Keep apart.
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

        static void AppendControllers(StringBuilder sb, CVRAvatar avatar, GameObject target)
        {
            // Every Animator, nulls included. Judged on the serialized m_Controller,
            // which the prefab saves; the getter can disagree, then both print.
            sb.Append("[animators]\n");
            foreach (var a in target.GetComponentsInChildren<Animator>(true).OrderBy(x => HierarchyPath(target, x), StableSampleOrder.Instance))
            {
                var serialized = new SerializedObject(a)
                    .FindProperty("m_Controller").objectReferenceValue as RuntimeAnimatorController;
                var getter = a.runtimeAnimatorController;
                sb.Append("  ").Append(HierarchyPath(target, a)).Append(" -> ")
                  .Append(serialized == null ? "NULL (no controller: broken or unassigned)" : serialized.name);
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

                // Driver writes are named: a driver writing a parameter its own cycle
                // reads can break the loop, so names tell a real loop from a self-limiting one.
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
