// AccessoryAttach on many real accessories: every VRCFury, Modular Avatar or PhysBone prefab worn by
// the corpus avatars is dropped, one at a time, onto a converted ChilloutVR avatar at the matching
// humanoid bone, converted in place and undone, with the card's invariants checked on every run.
// New scenes only, nothing saved; output goes to Assets/__AccessoryWide, deleted after each run.
//
// -executeMethod AvatarBridge.Regression.AccessoryWideProbe.Run -awOut <tsv>
//   [-awTargets "<CVR prefab>|<CVR prefab>"] [-awScenes <file of Assets/ scene paths>] [-awMax 40] [-awScanMax 120]
// Batch. No -quit: it exits itself, 0 once started even with FAILs, 1 if it could not start.

#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Regression
{
    public static class AccessoryWideProbe
    {
        const string Dir = "Assets/__AccessoryWide";
        const string Tag = "[AccessoryWide] ";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Same as the corpus: scenes that are never avatars, matched as path substrings.
        static readonly string[] BuiltInExcluded =
        {
            "/AvatarBridgeOutput/", "/CVR.CCK/", "/MagicaCloth2/", "/UnityTechnologies/",
            "/Samples/", "/Scenes/SampleScene", "/MISC/",
        };

        class Accessory { public string Prefab, Bone, Scene, Summary; }

        public static void Run()
        {
            string outPath;
            string[] targets;
            int max, scanMax;
            List<string> scenes;
            try
            {
                outPath = Arg("-awOut", null) ?? throw new Exception("missing -awOut");
                targets = Arg("-awTargets", "Assets/AvatarBridgeOutput/Ampwave/Ampwave (ChilloutVR).prefab")
                    .Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
                foreach (var t in targets)
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(t) == null) throw new Exception("missing target " + t);
                max = int.Parse(Arg("-awMax", "40"), Inv);
                scanMax = int.Parse(Arg("-awScanMax", "120"), Inv);
                string list = Arg("-awScenes", null);
                scenes = list != null
                    ? File.ReadAllLines(list).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList()
                    : AllAvatarScenes();
                var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception e)
            {
                Debug.LogError(Tag + "could not start: " + FirstLine(e));
                EditorApplication.Exit(1);
                return;
            }

            var accessories = Harvest(scenes, max, scanMax);
            Debug.Log(Tag + $"harvested {accessories.Count} accessories");

            var verdicts = new List<string>();
            using (var tsv = new StreamWriter(outPath, false) { AutoFlush = true })
            {
                tsv.WriteLine(string.Join("\t", "index", "prefab", "bone", "target", "seconds", "exception", "errors", "warnings",
                    "objectsBack", "findAfter", "newMissing", "ownChanged", "newRoots", "assetsLeft", "undoDiffers", "findAfterUndo", "verdict"));
                for (int i = 0; i < accessories.Count; i++)
                {
                    // A target that already wears it would test a duplicate, not a new accessory.
                    string wearing = Path.GetFileNameWithoutExtension(accessories[i].Prefab);
                    var pick = Enumerable.Range(0, targets.Length).Select(k => targets[(i + k) % targets.Length])
                        .FirstOrDefault(t => !AssetDatabase.LoadAssetAtPath<GameObject>(t).GetComponentsInChildren<Transform>(true).Any(x => x.name == wearing))
                        ?? targets[i % targets.Length];
                    verdicts.Add(Attach(i, accessories[i], pick, tsv));
                }
            }
            Debug.Log(Tag + "done: " + string.Join(", ", verdicts.GroupBy(v => v).OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key}")));
            EditorApplication.Exit(0);
        }

        // ---------------------------------------------------------------- harvest

        static List<Accessory> Harvest(List<string> scenes, int max, int scanMax)
        {
            var found = new List<Accessory>();
            var seen = new HashSet<string>();
            var assetOk = new Dictionary<string, bool>();
            int scanned = 0;
            foreach (var scenePath in scenes)
            {
                if (found.Count >= max || scanned >= scanMax) break;
                scanned++;
                try
                {
                    // Discards unsaved changes without prompting; nothing is saved back.
                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var d in root.GetComponentsInChildren<VRCAvatarDescriptor>(true))
                            HarvestAvatar(d, scenePath, found, seen, assetOk, max);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(Tag + $"scene {scenePath} skipped: {FirstLine(e)}");
                }
            }
            Debug.Log(Tag + $"scanned {scanned} scenes");
            return found;
        }

        static void HarvestAvatar(VRCAvatarDescriptor d, string scenePath, List<Accessory> found, HashSet<string> seen,
            Dictionary<string, bool> assetOk, int max)
        {
            var avatar = d.transform;
            var animator = d.GetComponent<Animator>();
            var bones = new Dictionary<Transform, string>();
            if (animator != null && animator.isHuman)
                foreach (HumanBodyBones b in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (b == HumanBodyBones.LastBone) continue;
                    var bt = animator.GetBoneTransform(b);
                    if (bt != null && !bones.ContainsKey(bt)) bones[bt] = b.ToString();
                }

            var taken = new List<Transform>();
            foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
            {
                if (found.Count >= max) return;
                if (t == avatar || !PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) || taken.Any(x => t.IsChildOf(x))) continue;
                var comps = t.GetComponentsInChildren<Component>(true).Where(Qualifies).ToList();
                if (comps.Count == 0) continue;
                string asset = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
                if (string.IsNullOrEmpty(asset) || asset.Replace('\\', '/').StartsWith("Assets/AvatarBridgeOutput/")) continue;
                // The components must come with the asset: ones added as scene overrides, on a model
                // prefab say, would not arrive when the prefab is dropped.
                if (!assetOk.TryGetValue(asset, out bool ok)) assetOk[asset] = ok = AssetQualifies(asset);
                if (!ok) continue;
                taken.Add(t);
                if (!seen.Add(asset)) continue;

                string bone = null;
                for (var p = t.parent; p != null && p != avatar && bone == null; p = p.parent)
                    bones.TryGetValue(p, out bone);
                var a = new Accessory
                {
                    Prefab = asset,
                    Bone = bone ?? "root",
                    Scene = scenePath,
                    Summary = string.Join(", ", comps.GroupBy(c => c.GetType().Name).Select(g => $"{g.Key} x{g.Count()}")),
                };
                found.Add(a);
                Debug.Log(Tag + $"harvested #{found.Count - 1} {a.Prefab} at {a.Bone} from {a.Scene}: {a.Summary}");
            }
        }

        static bool Qualifies(Component c)
        {
            if (c == null) return false;
            var type = c.GetType();
            string ns = type.Namespace ?? "";
            // Colliders alone are no accessory to the card either.
            return ns.StartsWith("VF.") || ns.StartsWith("nadena.dev.modular_avatar") || type.Name.Contains("PhysBone") && !type.Name.Contains("Collider");
        }

        // A prefab that is itself an avatar is no accessory.
        static bool AssetQualifies(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return go != null && go.GetComponentInChildren<VRCAvatarDescriptor>(true) == null
                && go.GetComponentsInChildren<Component>(true).Any(Qualifies);
        }

        static List<string> AllAvatarScenes()
        {
            var excluded = BuiltInExcluded.Concat(CorpusExcluded()).ToArray();
            return AssetDatabase.FindAssets("t:Scene")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && p.StartsWith("Assets/", StringComparison.Ordinal))
                .Where(p => !excluded.Any(x => p.Replace('\\', '/').Contains(x)))
                .OrderBy(p => p, StableSampleOrder.Instance)
                .ToList();
        }

        // The corpus's own [excluded] list, when AVATARBRIDGE_REPO points at it.
        static IEnumerable<string> CorpusExcluded()
        {
            var repo = Environment.GetEnvironmentVariable("AVATARBRIDGE_REPO");
            if (string.IsNullOrEmpty(repo)) yield break;
            var cfg = Path.Combine(repo, "Regression", "corpus.cfg");
            if (!File.Exists(cfg)) yield break;
            string section = null;
            foreach (var raw in File.ReadAllLines(cfg))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).ToLowerInvariant(); continue; }
                if (section == "excluded") yield return line;
            }
        }

        // ---------------------------------------------------------------- attach

        // PASS, or REFUSED / KEPT when the card said why it changed nothing and nothing changed, else FAIL.
        static string Attach(int index, Accessory a, string targetPath, StreamWriter tsv)
        {
            var fails = new List<string>();
            void Expect(string what, bool ok) { if (!ok) fails.Add(what); }
            void Guard(string what, Action act)
            {
                try { act(); }
                catch (Exception e) { fails.Add($"{what} threw {FirstLine(e)}"); }
            }
            string seconds = "", exception = "", errors = "", warnings = "", objectsBack = "", findAfter = "", newMissing = "",
                ownChanged = "", newRoots = "", assetsLeft = "", undoDiffers = "", findAfterUndo = "", mapped = "root", why = "";
            bool refused = false, kept = false;
            try
            {
                Undo.ClearAll();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(Dir);
                AssetDatabase.CreateFolder("Assets", "__AccessoryWide");

                var target = (GameObject) PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(targetPath));
                var parent = Bone(target, a.Bone, out mapped);
                var own = Snapshot(target);
                var acc = (GameObject) PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(a.Prefab));
                acc.transform.SetParent(parent, false);
                string accName = acc.name;
                bool shows = acc.GetComponentsInChildren<Renderer>(true).Length > 0;
                string accPath = AnimationUtility.CalculateTransformPath(acc.transform, target.transform);
                var dropped = Snapshot(target);
                int missingBefore = Missing(target);
                int mismatchBefore = Mismatched(target);
                // The CCK adds its own object to a scene when it opens, so compare, never count.
                var roots = target.scene.GetRootGameObjects();
                // The card takes the nearest prefab root, so a nested prefab carrying the components is found in its place.
                Guard("found before", () => Expect("found before", AccessoryAttach.Find(target).Any(f => f.transform.IsChildOf(acc.transform))));

                BridgeReport report = null;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                try { report = AccessoryAttach.Run(target, new BridgeSettings(), r => Dir); }
                catch (Exception e) { exception = FirstLine(e); }
                seconds = clock.Elapsed.TotalSeconds.ToString("0.0", Inv);
                Expect("no exception", exception == "");

                if (report != null)
                {
                    foreach (var e in report.Entries) Debug.Log(Tag + $"  #{index} {e.Status} [{e.Category}] {e.Subject}: {e.Detail}");
                    var err = report.Entries.Where(e => e.Status == ReportStatus.Error).Select(e => e.Subject).ToList();
                    var warn = report.Entries.Where(e => e.Status == ReportStatus.Warning).Select(e => e.Subject).ToList();
                    errors = Counted(err);
                    warnings = Counted(warn);
                    Expect("no errors", err.Count == 0);
                    refused = err.Contains("The accessories did not convert");
                    kept = warn.Contains($"\"{accName}\" was not converted");
                    why = refused ? report.Entries.First(e => e.Status == ReportStatus.Error).Subject : kept ? warn.First(w => w.Contains(accName)) : "";
                    var converted = report.Entries.FirstOrDefault(e => e.Status == ReportStatus.Converted && e.Category == "Accessories");
                    Debug.Log(Tag + $"  #{index} converted: {converted?.Detail ?? "(no Converted entry)"}");
                }

                Guard("objects back", () =>
                {
                    // Where it was dropped, or where Armature Link or a bone proxy moved it; one with
                    // nothing to show is back when its toggles are.
                    var wrapped = new Regex(@"^\[VF\d+\] " + Regex.Escape(accName) + "$");
                    bool back = target.transform.Find(accPath) != null
                        || target.GetComponentsInChildren<Transform>(true).Any(t => t.name == accName || wrapped.IsMatch(t.name))
                        || !shows && report != null && report.Entries.Any(e => e.Subject.StartsWith("Carried their toggles"));
                    objectsBack = back ? "yes" : "no";
                    Expect("objects back", back);
                });
                Guard("find after", () =>
                {
                    int n = AccessoryAttach.Find(target).Count;
                    findAfter = n.ToString(Inv);
                    Expect("find after is 0", n == 0);
                });
                Guard("mismatch", () =>
                {
                    int n = Mismatched(target) - mismatchBefore;
                    Expect($"no new condition of the wrong type ({n})", n <= 0);
                });
                Guard("missing", () =>
                {
                    int n = Missing(target) - missingBefore;
                    newMissing = n.ToString(Inv);
                    Expect("no new missing references", n <= 0);
                });
                Guard("own objects", () =>
                {
                    var after = Snapshot(target);
                    var changed = own.Keys.Where(p => !after.TryGetValue(p, out var t) || t != own[p]).Select(p => $"{p} [{own[p]}]").ToList();
                    ownChanged = Counted(changed);
                    Expect("own objects unchanged", changed.Count == 0);
                });
                Guard("roots", () =>
                {
                    var extra = target.scene.GetRootGameObjects().Except(roots).Select(r => r.name).ToList();
                    newRoots = Counted(extra);
                    Expect("no new roots", extra.Count == 0);
                });
                Guard("assets", () =>
                {
                    var left = AssetDatabase.FindAssets("", new[] { Dir }).Select(AssetDatabase.GUIDToAssetPath).Distinct().ToList();
                    assetsLeft = Counted(left);
                    // Same bar as the single-case test: the copy's prefab and reports go, and every
                    // controller left is the one now wired in, or what that uses.
                    var cvr = target.GetComponent<CVRAvatar>();
                    var reach = new HashSet<string>(new UnityEngine.Object[] { cvr != null ? cvr.overrides : null, target.GetComponent<Animator>()?.runtimeAnimatorController }
                        .Where(o => o != null).SelectMany(o => AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(o), true)));
                    Expect("no stand-in assets left", !left.Any(p => p.EndsWith(".prefab") || p.Contains("ConversionReport") || p.EndsWith("Diagnostics.md")
                        || (p.EndsWith(".controller") || p.EndsWith(".overrideController")) && !reach.Contains(p)));
                });

                Guard("undo", () =>
                {
                    Undo.PerformUndo();
                    var undone = Snapshot(target);
                    var differ = dropped.Keys.Union(undone.Keys)
                        .Where(p => !dropped.TryGetValue(p, out var x) || !undone.TryGetValue(p, out var y) || x != y).ToList();
                    undoDiffers = Counted(differ);
                    Expect("undo restores the drop", differ.Count == 0);
                });
                Guard("find after undo", () =>
                {
                    var again = AccessoryAttach.Find(target);
                    bool back = again.Any(f => (AnimationUtility.CalculateTransformPath(f.transform, target.transform) + "/").StartsWith(accPath + "/"));
                    findAfterUndo = $"{again.Count}{(back ? "" : " (accessory not among them)")}";
                    Expect("undo finds the accessory again", back);
                });
            }
            catch (Exception e)
            {
                fails.Add("probe threw " + FirstLine(e));
            }
            finally
            {
                AssetDatabase.DeleteAsset(Dir);
            }

            // A refusal or a kept accessory is right when it is said and nothing else moved.
            if (refused || kept) fails.RemoveAll(f => f == "no errors" || f == "find after is 0" || f == "objects back");
            string verdict = fails.Count > 0 ? "FAIL " + fails[0] : refused ? "REFUSED" : kept ? "KEPT" : "PASS";
            string bone = mapped == a.Bone ? a.Bone : $"{a.Bone}->{mapped}";
            tsv.WriteLine(string.Join("\t", new[] { index.ToString(Inv), a.Prefab, bone, targetPath, seconds, exception, errors, warnings,
                objectsBack, findAfter, newMissing, ownChanged, newRoots, assetsLeft, undoDiffers, findAfterUndo, verdict }.Select(Cell)));
            Debug.Log(Tag + $"{verdict.Split(' ')[0]} #{index} {a.Prefab} at {bone} on {Path.GetFileNameWithoutExtension(targetPath)}" +
                $" in {seconds} s{(fails.Count == 0 ? (why != "" ? ": " + why : "") : ": " + string.Join("; ", fails))}");
            return verdict.Split(' ')[0];
        }

        // The target's bone for the one the accessory sat under, else the nearest humanoid parent that exists.
        static Transform Bone(GameObject target, string name, out string mapped)
        {
            mapped = "root";
            var animator = target.GetComponent<Animator>();
            if (name == "root" || animator == null || !animator.isHuman || !Enum.TryParse(name, out HumanBodyBones b)) return target.transform;
            for (int i = (int) b; i >= 0; i = HumanTrait.GetParentBone(i))
            {
                var t = animator.GetBoneTransform((HumanBodyBones) i);
                if (t == null) continue;
                mapped = ((HumanBodyBones) i).ToString();
                return t;
            }
            return target.transform;
        }

        // ---------------------------------------------------------------- helpers

        // Every object by path, with its components' types, the way the card compares them.
        static Dictionary<string, string> Snapshot(GameObject root) =>
            root.GetComponentsInChildren<Transform>(true).GroupBy(t => t == root.transform ? "" : AnimationUtility.CalculateTransformPath(t, root.transform))
                .ToDictionary(g => g.Key, g => string.Join(" ", g.First().GetComponents<Component>().Select(c => c == null ? "missing" : c.GetType().Name)));

        // Conditions that can never fire: Greater/Less/Equals on a Bool, If/IfNot on a number.
        static int Mismatched(GameObject target)
        {
            var cvr = target.GetComponent<CVRAvatar>();
            var over = cvr != null ? cvr.overrides as AnimatorOverrideController : null;
            var c = (over != null ? over.runtimeAnimatorController : cvr != null ? cvr.overrides : null) as AnimatorController;
            if (c == null) return 0;
            var types = c.parameters.GroupBy(p => p.name).ToDictionary(g => g.Key, g => g.First().type);
            int n = 0;
            void Count(AnimatorTransitionBase[] ts)
            {
                foreach (var t in ts)
                    foreach (var k in t.conditions)
                    {
                        if (!types.TryGetValue(k.parameter, out var type)) continue;
                        bool boolMode = k.mode == AnimatorConditionMode.If || k.mode == AnimatorConditionMode.IfNot;
                        if (boolMode != (type == AnimatorControllerParameterType.Bool)) n++;
                    }
            }
            void Walk(AnimatorStateMachine m)
            {
                if (m == null) return;
                Count(m.anyStateTransitions);
                Count(m.entryTransitions);
                foreach (var s in m.states) Count(s.state.transitions);
                foreach (var s in m.stateMachines) Walk(s.stateMachine);
            }
            foreach (var l in c.layers) Walk(l.stateMachine);
            return n;
        }

        static int Missing(GameObject root)
        {
            int n = 0;
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var it = new SerializedObject(c).GetIterator();
                while (it.Next(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null && it.objectReferenceInstanceIDValue != 0)
                        n++;
            }
            return n;
        }

        static string Counted(List<string> items) =>
            items.Count == 0 ? "0" : $"{items.Count}: {string.Join("; ", items.Take(8))}{(items.Count > 8 ? "; ..." : "")}";

        static string Cell(string s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        static string FirstLine(Exception e)
        {
            string m = e.Message ?? "";
            int nl = m.IndexOf('\n');
            return e.GetType().Name + ": " + (nl >= 0 ? m.Substring(0, nl).TrimEnd('\r') : m);
        }

        static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
    }
}
#endif
