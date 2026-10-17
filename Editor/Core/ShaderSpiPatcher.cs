#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    // Patches shaders without single-pass instanced support into
    // copies that have it. CVR forces instanced; VRChat forces
    // double-wide, where shaders get both eyes for free.
    //
    // Three rules: never touch the original (copy into RehomedAssets,
    // repoint this avatar's materials only); patch only what is plainly
    // written, every vertex and fragment pass of it, and refuse a shader
    // with no pass it can read; prove the copy compiles, the single-pass
    // instanced variant included, before repointing, or delete it.
    // Only compilation is verifiable here; looks need VR eyes.
    public static class ShaderSpiPatcher
    {
        const string Category = "Shaders";

        static readonly string[] StereoMacros =
        {
            "UNITY_VERTEX_INPUT_INSTANCE_ID",
            "UNITY_VERTEX_OUTPUT_STEREO",
            "UNITY_SETUP_INSTANCE_ID",
            "UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO",
        };

        const string EyeIndexMacro = "UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX";

        // An include of either kind; group 2 is the path. The pragma kind
        // went unread, uncopied and unrepointed.
        internal static readonly Regex IncludeDirective =
            new Regex(@"(#include(?:_with_pragmas)?\s+"")([^""]+)("")");

        static readonly Regex VertexPragma = new Regex(@"#pragma\s+vertex\s+(\w+)");
        static readonly Regex FragmentPragma = new Regex(@"#pragma\s+fragment\s+(\w+)");
        static readonly Regex SurfacePragma = new Regex(@"#pragma\s+surface\b");
        static readonly Regex DepthDeclaration =
            new Regex(@"(?:uniform\s+)?sampler2D(?:_float|_half)?\s+_CameraDepthTexture\s*;");
        static readonly Regex DepthRead = new Regex(@"\btex2D(proj|lod)?\s*\(\s*_CameraDepthTexture\s*,");
        // A #define and its replacement, continuation lines included.
        static readonly Regex DefineLine = new Regex(@"#define[ \t]+(\w+)((?:\\\r?\n|[^\r\n])*)");

        // Per conversion: what the renderer pass patched, for the swap
        // pass that runs once the clips are the conversion's own.
        static Dictionary<Shader, Shader> _patched;
        static Dictionary<Material, Material> _clones;
        static string _dir;

        // Every file name written this run, across shaders. Names were only
        // kept apart within one shader, so two shaders with an include of
        // the same name wrote one file, and a failed patch deleted a file a
        // good copy still included.
        static readonly HashSet<string> Claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Written text, file names included, whose copy passed both compile
        // checks this session. The variant compile costs seconds a pass.
        static readonly HashSet<string> CompiledCopies = new HashSet<string>();

        // Log-only timings for one conversion, reset by Run. Never the report.
        static class Times
        {
            internal static int Judged, SameText, Patched, Reused, VariantChecks;
            internal static readonly System.Diagnostics.Stopwatch Planning = new System.Diagnostics.Stopwatch(),
                Patching = new System.Diagnostics.Stopwatch(), Variants = new System.Diagnostics.Stopwatch();

            internal static void Reset()
            {
                Judged = SameText = Patched = Reused = VariantChecks = 0;
                Planning.Reset();
                Patching.Reset();
                Variants.Reset();
            }
        }

        internal static string TimingSummary() =>
            $"judged {Times.Judged} ({Times.SameText} by text seen before, planning {Times.Planning.ElapsedMilliseconds}), " +
            $"Patched {Times.Patched} in {Times.Patching.ElapsedMilliseconds} ({Times.Reused} copies reused, " +
            $"StereoVariantError {Times.VariantChecks} in {Times.Variants.ElapsedMilliseconds})";

        public static void Run(BridgeContext ctx)
        {
            _patched = null;
            _clones = null;
            Times.Reset();
            if (!ctx.Settings.patchNonSpiShaders)
            {
                return;
            }
            // Renderers only here. The clips are still the avatar's own
            // assets at this point; RepointSwapClips runs after they are copied.
            Patch(ctx.Target, ctx.OutputDir.TrimEnd('/') + "/RehomedAssets", null, ctx.Report);
        }

        // Late pass: material-swap curves in the merged controller's clips
        // point at the patched copies. After the self-container, so every
        // clip it touches is a copy owned by this conversion.
        public static void RepointSwapClipsPass(BridgeContext ctx)
        {
            if (!ctx.Settings.patchNonSpiShaders || ctx.MergedController == null || _patched == null)
            {
                return;
            }
            var outcome = new Outcome();
            RepointSwapClips(ctx.MergedController, _dir, _patched, _clones, ctx.Report, outcome);
            Report(ctx.Report, outcome, swaps: true);
        }

        static void RepointSwapClips(AnimatorController controller, string dir,
            Dictionary<Shader, Shader> patched, Dictionary<Material, Material> clones, BridgeReport report,
            Outcome outcome)
        {
            int swapsRepointed = 0;
            var clips = ClipsOf(controller).ToList();
            var swapped = clips.SelectMany(c => AnimationUtility.GetObjectReferenceCurveBindings(c)
                    .SelectMany(b => AnimationUtility.GetObjectReferenceCurve(c, b) ?? Array.Empty<ObjectReferenceKeyframe>()))
                .Select(k => k.value is Material m && m != null ? m.shader : null)
                .Where(s => s != null && !patched.ContainsKey(s)).ToList();
            Prepared = Prejudge(swapped);
            Prepare(swapped, dir, patched, outcome);
            foreach (var clip in clips)
            {
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                    if (keys == null || keys.Length == 0)
                    {
                        continue;
                    }
                    bool rewritten = false;
                    for (int k = 0; k < keys.Length; k++)
                    {
                        var material = keys[k].value as Material;
                        var shader = material != null ? material.shader : null;
                        if (shader == null)
                        {
                            continue;
                        }
                        var fixedShader = Judge(shader, dir, patched, outcome);
                        if (fixedShader == null)
                        {
                            continue;
                        }
                        keys[k].value = Repoint(material, fixedShader, dir, clones);
                        rewritten = true;
                    }
                    if (rewritten)
                    {
                        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
                        swapsRepointed++;
                    }
                }
            }
            Prepared = null;
            if (swapsRepointed > 0)
            {
                report.Converted(Category,
                    $"{BridgeElements.Count(swapsRepointed, "material-swap curve", "material-swap curves")} repointed at a patched shader",
                    "Materials an animation swaps in now swap in the patched copy.");
            }
        }

        // What one pass over shaders found. The renderer pass and the swap
        // pass report it the same way; the swap pass used to drop every
        // already-correct and screen-grab line it collected.
        class Outcome
        {
            public readonly List<string> Repointed = new List<string>();
            public readonly List<string> Refused = new List<string>();
            public readonly List<string> AlreadyCorrect = new List<string>();
            public readonly List<string> GrabLimited = new List<string>();
            public readonly List<string> PartlyPatched = new List<string>();
        }

        // A shader's verdict, once per shader per conversion: the patched
        // copy, or null when it is left alone or refused, the reason going
        // into the outcome.
        static Shader Judge(Shader shader, string dir, Dictionary<Shader, Shader> patched, Outcome outcome)
        {
            if (!patched.ContainsKey(shader)) Prepare(new[] { shader }, dir, patched, outcome);
            return patched[shader];
        }

        // One shader on its way through Prepare.
        sealed class Weighed
        {
            public Shader Shader, Result;
            public bool Correct, Tried, Grabbed;
            public string Reason;
            public List<string> Left;
            public Staged Staged;
        }

        // Every shader not judged yet, in the order met. Each copy is written first, then all of
        // them import in one batch, then each is checked and reported in that same order, so the
        // names, the outcome and the report come out as one at a time made them. One at a time,
        // each copy cost its own imports and every package's import hooks with them.
        static void Prepare(IEnumerable<Shader> shaders, string dir, Dictionary<Shader, Shader> patched, Outcome outcome)
        {
            var weighed = new List<Weighed>();
            // A throw still imports and reports every copy written before it, as one at a time
            // did: written and never imported, they turned up unchecked at the next refresh.
            try
            {
                foreach (var shader in shaders.Where(s => s != null && !patched.ContainsKey(s)).Distinct())
                {
                    weighed.Add(Weigh(shader, dir));
                }
            }
            finally { Settle(weighed, patched, outcome); }
        }

        static Weighed Weigh(Shader shader, string dir)
        {
            var w = new Weighed { Shader = shader };
            string source = SourcePathOf(shader);
            // Engine shaders have no source on disk and ship stereo-correct.
            if (source != null)
            {
                // One read and at most one plan: the patch follows the plan the
                // verdict came from instead of reading and planning it again. A
                // throw goes on to the patch, which reads again and refuses it by name.
                List<SourceFile> unit;
                StereoPlan plan;
                try
                {
                    // Planned beside the others by Prejudge, the plan made for this very unit.
                    if (Prepared == null || !Prepared.TryGetValue(source, out var ready)) ready = (ReadUnit(source), null);
                    Prepared?.Remove(source);
                    unit = ready.unit;
                    w.Correct = StereoProblems(unit, out plan).Count == 0;
                    plan = plan ?? ready.plan;
                }
                catch
                {
                    unit = null;
                    plan = null;
                    w.Correct = false;
                }
                if (!w.Correct)
                {
                    w.Tried = true;
                    Times.Patched++;
                    Times.Patching.Start();
                    try { w.Staged = Stage(source, shader.name, dir, unit, plan, out w.Reason, out w.Grabbed, out w.Left, out w.Result); }
                    finally { Times.Patching.Stop(); }
                }
            }
            return w;
        }

        static void Settle(List<Weighed> weighed, Dictionary<Shader, Shader> patched, Outcome outcome)
        {
            Times.Patching.Start();
            Import(weighed.Where(w => w.Staged != null).Select(w => w.Staged).ToList());
            var doomed = new List<string>();
            foreach (var w in weighed.Where(w => w.Staged != null))
            {
                w.Result = Finish(w.Staged, doomed, out w.Reason);
            }
            // Every file of a failed copy goes, not just the shader: no orphaned
            // include copies in the output folder.
            if (doomed.Count > 0) AssetDatabase.DeleteAssets(doomed.ToArray(), new List<string>());
            Times.Patching.Stop();

            foreach (var w in weighed)
            {
                if (w.Correct)
                {
                    outcome.AlreadyCorrect.Add(w.Shader.name);
                }
                else if (w.Tried && w.Result == null)
                {
                    outcome.Refused.Add($"{w.Shader.name} ({w.Reason})");
                }
                else if (w.Tried)
                {
                    outcome.Repointed.Add(w.Shader.name);
                    if (w.Grabbed) outcome.GrabLimited.Add(w.Shader.name);
                    if (w.Left.Count > 0)
                    {
                        outcome.PartlyPatched.Add($"{w.Shader.name}: {string.Join(", ", w.Left)}");
                    }
                }
                patched[w.Shader] = w.Result;
            }
        }

        static void Report(BridgeReport report, Outcome o, bool swaps)
        {
            string where = swaps ? " in animated swaps" : "";
            if (o.Repointed.Count > 0)
            {
                if (swaps)
                {
                    report.Approximated(Category,
                        $"{BridgeElements.Count(o.Repointed.Distinct().Count(), "shader", "shaders")} patched for VR stereo, found only in animated swaps",
                        $"{string.Join(", ", o.Repointed.Distinct())}: never on a renderer at rest, assigned by a " +
                        "toggle. Copied into RehomedAssets with the stereo macros added and the swap repointed.");
                }
                else
                {
                    report.Approximated(Category, $"{BridgeElements.Count(o.Repointed.Distinct().Count(), "shader", "shaders")} patched for VR stereo",
                        $"{string.Join(", ", o.Repointed.Distinct())}: copied into RehomedAssets with the single-pass " +
                        "instanced macros added; originals untouched. They compile, the single-pass instanced " +
                        "variant included; check both eyes in VR.");
                }
            }
            if (o.PartlyPatched.Count > 0)
            {
                report.Warning(Category, $"{BridgeElements.Count(o.PartlyPatched.Count, "shader", "shaders")}{where} patched in some passes only",
                    $"{string.Join("; ", o.PartlyPatched)}: those passes are left as they were and still draw into " +
                    "one eye. The rest of each shader draws in both.");
            }
            if (o.GrabLimited.Count > 0)
            {
                report.Warning(Category,
                    $"{BridgeElements.Count(o.GrabLimited.Distinct().Count(), "patched shader", "patched shaders")}{where} grab the screen: the background they " +
                    "refract comes from one eye",
                    $"{string.Join(", ", o.GrabLimited.Distinct())}: they draw in both eyes, but refraction shows one " +
                    "eye's view. Unfixable here; use a shader that doesn't grab the screen if it bothers you.");
            }
            if (o.Refused.Count > 0)
            {
                report.Warning(Category, $"{BridgeElements.Count(o.Refused.Count, "shader", "shaders")}{where} could not be patched for VR stereo",
                    $"{string.Join(", ", o.Refused)}: " + (swaps
                        ? "these still draw into one eye when the toggle assigns them."
                        : "these still won't draw correctly in both eyes. Patching is only attempted on plainly " +
                          "written vertex/fragment shaders; anything else needs doing by hand or replacing with a " +
                          "different shader."));
            }
            // Every shader gets a verdict: patched, refused, or did not
            // need it. Silence reads as a miss.
            if (o.AlreadyCorrect.Count > 0)
            {
                report.Converted(Category,
                    $"{BridgeElements.Count(o.AlreadyCorrect.Distinct().Count(), "shader", "shaders")}{where} already speak single-pass instanced: left untouched",
                    $"{string.Join(", ", o.AlreadyCorrect.Distinct())}.");
            }
        }

        // The same pass on any object: the toolkit runs it standalone.
        public static void Patch(GameObject target, string dir, AnimatorController controller, BridgeReport report)
        {
            Claimed.Clear();
            var patched = new Dictionary<Shader, Shader>();
            // One clone per material, not per material slot: a material used by four slots is
            // still one material, and cloning it per slot would break batching between them.
            var clones = new Dictionary<Material, Material>();
            var outcome = new Outcome();

            var renderers = target.GetComponentsInChildren<Renderer>(true);
            var worn = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.shader).ToList();
            Prepared = Prejudge(worn);
            Prepare(worn, dir, patched, outcome);
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    var shader = material != null ? material.shader : null;
                    if (shader == null)
                    {
                        continue;
                    }
                    var fixedShader = Judge(shader, dir, patched, outcome);
                    if (fixedShader != null)
                    {
                        materials[i] = Repoint(material, fixedShader, dir, clones);
                        changed = true;
                    }
                }
                if (changed)
                {
                    renderer.sharedMaterials = materials;
                }
            }
            Prepared = null;

            // Materials that arrive by animated swap sit on no renderer
            // right now, invisible to the loop above, and draw into one
            // eye just as badly when assigned. Their curves are rewritten
            // to the patched clone too, in RepointSwapClips: the clips
            // are the avatar's own until the self-container copies them,
            // so the converter runs that part late. Kept for the caches.
            _patched = patched;
            _clones = clones;
            _dir = dir;
            if (controller != null)
            {
                RepointSwapClips(controller, dir, patched, clones, report, outcome);
            }
            Report(report, outcome, swaps: false);
        }

        internal static string SourcePathOf(Shader shader)
        {
            string path = AssetDatabase.GetAssetPath(shader);
            return !string.IsNullOrEmpty(path) && path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)
                   && File.Exists(path) ? path : null;
        }

        // Whether a shader already draws in both eyes, from the same plan the
        // patch would follow, so the two never disagree. A shader that throws
        // while being read goes on to the patch, which refuses it by name; it
        // used to read as correct and say nothing.
        internal static bool DeclaresStereo(string path)
        {
            try
            {
                return StereoProblems(path).Count == 0;
            }
            catch
            {
                return false;
            }
        }

        // What keeps a shader from drawing in both eyes, for the report: the
        // macros a pass is missing. A surface shader's passes are Unity's own,
        // generated with every macro (measured, Dev/Probes/SpiFactsProbe), so
        // only its vertex and fragment passes count, and a shader with none
        // of them has nothing to fix. A plain depth read alone is not counted:
        // locked Poiyomi Pro names every macro in every pass and still declares
        // a plain depth texture for optional features, and counting it sent
        // most Poiyomi materials to be copied (SpiSweepProbe).
        internal static List<string> StereoProblems(string path) => StereoProblems(ReadUnit(path), out _);

        // Verdicts by the text they came from, for the editor session. The
        // advisor, the patch pass and the report each judge the same shaders,
        // on every avatar wearing them, and one plan can take seconds. Keyed
        // by content, never path: a patched copy is rewritten in place.
        static readonly Dictionary<string, List<string>> ProblemsByContent = new Dictionary<string, List<string>>();

        // The plan comes back only when this call made it.
        static List<string> StereoProblems(List<SourceFile> unit, out StereoPlan plan)
        {
            plan = null;
            Times.Judged++;
            string key = ContentKey(unit, false);
            if (ProblemsByContent.TryGetValue(key, out var known))
            {
                Times.SameText++;
                return new List<string>(known);
            }
            Times.Planning.Start();
            var made = PlanStereo(unit);
            Times.Planning.Stop();
            plan = made;
            var problems = Verdict(unit, made);
            ProblemsByContent[key] = new List<string>(problems);
            return problems;
        }

        static List<string> Verdict(List<SourceFile> unit, StereoPlan made)
        {
            var problems = new List<string>();
            if (!(made.Passes == 0 && made.SurfaceOnly))
            {
                // A pass it cannot read is judged the old way, by the macros
                // being named anywhere in the unit.
                var missing = StereoMacros.Where(m => made.Adds.Contains(m)
                    || ((made.Passes == 0 || made.Left.Count > 0) && !unit.Any(f => f.Text.Contains(m)))).ToList();
                if (missing.Count > 0) problems.Add("missing " + string.Join(", ", missing));
            }
            return problems;
        }

        // Set while a pass judges: each shader's unit as read, with its plan when Prejudge made one.
        static Dictionary<string, (List<SourceFile> unit, StereoPlan plan)> Prepared;

        // Every shader a pass is about to judge, planned at once on the thread pool: one plan can
        // take seconds, and they ran one after another. Planning is string work on text already
        // read; reading stays here, since the AssetDatabase answers only on the main thread. The
        // verdicts land in ProblemsByContent, so the judging after it never plans these again.
        internal static Dictionary<string, (List<SourceFile> unit, StereoPlan plan)> Prejudge(IEnumerable<Shader> shaders)
        {
            var units = new Dictionary<string, (List<SourceFile> unit, StereoPlan plan)>();
            foreach (var shader in shaders.Where(s => s != null).Distinct())
            {
                string path = SourcePathOf(shader);
                if (path == null || units.ContainsKey(path)) continue;
                // One that throws is read again by Judge, which refuses it by name.
                try { units[path] = (ReadUnit(path), null); }
                catch { }
            }
            var fresh = units.Select(u => (path: u.Key, u.Value.unit, key: ContentKey(u.Value.unit, false)))
                .Where(u => !ProblemsByContent.ContainsKey(u.key)).GroupBy(u => u.key).Select(g => g.First()).ToList();
            var plans = new StereoPlan[fresh.Count];
            Times.Planning.Start();
            System.Threading.Tasks.Parallel.For(0, fresh.Count, i =>
            {
                try { plans[i] = PlanStereo(fresh[i].unit); }
                catch { }
            });
            Times.Planning.Stop();
            for (int i = 0; i < fresh.Count; i++)
            {
                if (plans[i] == null) continue;
                ProblemsByContent[fresh[i].key] = Verdict(fresh[i].unit, plans[i]);
                units[fresh[i].path] = (fresh[i].unit, plans[i]);
            }
            return units;
        }

        // Every file of a unit in order, with the names written when they
        // decide what an include finds. A length goes first so two files
        // never read as one. UTF-8 is exact: decoded text holds no lone surrogate.
        static string ContentKey(List<SourceFile> unit, bool names)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                void Feed(string text)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(text ?? "");
                    var length = BitConverter.GetBytes(bytes.Length);
                    sha.TransformBlock(length, 0, length.Length, null, 0);
                    sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
                }
                foreach (var file in unit)
                {
                    if (names) Feed(file.OutputName);
                    Feed(file.Text);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return Convert.ToBase64String(sha.Hash);
            }
        }

        // Every edit a shader's stereo patch needs, found on the text as read
        // and applied later in one go, from the end of each file back, so no
        // edit moves another's place. Each vertex function named by a pragma,
        // in every pass and every file: only the first was ever patched, so a
        // second pass with its own vertex function (an outline, a ForwardAdd)
        // stayed in one eye. A macro already present is never added twice;
        // a duplicated instancing member compiles everywhere but the
        // single-pass instanced variant (SpiFactsProbe).
        internal class StereoPlan
        {
            public readonly List<(SourceFile file, int at, string text)> Edits = new List<(SourceFile, int, string)>();
            public readonly HashSet<string> Adds = new HashSet<string>();
            public readonly List<string> Left = new List<string>();
            public int Passes;
            public bool Depth;              // reads _CameraDepthTexture as a plain texture
            public bool DepthRewritable;    // and every such read takes only the depth value
            public bool SurfaceOnly;
        }

        internal static StereoPlan PlanStereo(List<SourceFile> unit)
        {
            var plan = new StereoPlan();
            var offPc = unit.ToDictionary(f => f, f => OffPcPrograms(f.Text));
            bool OnPc(SourceFile f, int at) => !offPc[f].Any(r => at >= r.from && at < r.to);
            var vertNames = unit.SelectMany(f => VertexPragma.Matches(f.Text).Cast<Match>().Where(m => OnPc(f, m.Index)))
                .Select(m => m.Groups[1].Value).Distinct().ToList();
            var fragNames = unit.SelectMany(f => FragmentPragma.Matches(f.Text).Cast<Match>().Where(m => OnPc(f, m.Index)))
                .Select(m => m.Groups[1].Value).Distinct().ToList();
            plan.Depth = unit.Any(f => DepthRead.IsMatch(f.Text));
            plan.DepthRewritable = plan.Depth && unit.All(f => DepthRead.Matches(f.Text).Cast<Match>().All(m =>
            {
                int shut = MatchParen(f.Text, m.Index + m.Value.IndexOf('('));
                return shut >= 0 && OneChannel.Match(f.Text, shut + 1).Success;
            }));
            if (vertNames.Count == 0)
            {
                plan.SurfaceOnly = unit.Any(f => SurfacePragma.IsMatch(f.Text));
                return plan;
            }

            void Edit(SourceFile file, int at, string text, string macro)
            {
                if (!plan.Edits.Any(e => e.file == file && e.at == at && e.text == text))
                {
                    plan.Edits.Add((file, at, text));
                }
                if (macro != null) plan.Adds.Add(macro);
            }

            // Authors wrap the macros: locked Poiyomi Pro calls its own
            // POI_INITIALIZE_VERTEX_OUTPUT_STEREO, a #define holding Unity's.
            // Reading names literally sent every Poiyomi material to be patched
            // a second time (SpiSweepProbe), so a #define or a called function
            // that holds the macro counts, one level deep. One scan for every
            // #define, not one per macro.
            var defines = unit.SelectMany(f => DefineLine.Matches(f.Text).Cast<Match>())
                .Select(d => (name: d.Groups[1].Value, body: d.Groups[2].Value)).ToList();
            var wrappers = StereoMacros.Concat(new[] { EyeIndexMacro }).ToDictionary(m => m, m =>
                defines.Where(d => d.body.Contains(m)).Select(d => d.name).Distinct().ToList());

            // A called name's definition bodies, one per file that has one, found
            // once per plan: every pass calls the same helpers, and each search
            // scans the whole unit. A file without the name cannot match.
            var helpers = new Dictionary<string, List<string>>();
            List<string> HelpersOf(string callee)
            {
                if (helpers.TryGetValue(callee, out var found)) return found;
                found = new List<string>();
                var definition = new Regex($@"\b\w+\s+{Regex.Escape(callee)}\s*\([^;{{)]*\)\s*\{{");
                foreach (var f in unit)
                {
                    if (f.Text.IndexOf(callee, StringComparison.Ordinal) < 0) continue;
                    var d = definition.Match(f.Text);
                    if (!d.Success) continue;
                    int open = d.Index + d.Length - 1, close = MatchBrace(f.Text, open);
                    found.Add(close > open ? f.Text.Substring(open, close - open) : "");
                }
                helpers[callee] = found;
                return found;
            }
            bool Has(string code, string macro)
            {
                if (code.Contains(macro) || wrappers[macro].Any(w => Regex.IsMatch(code, $@"\b{Regex.Escape(w)}\b")))
                {
                    return true;
                }
                foreach (string callee in Regex.Matches(code, @"\b([A-Za-z_]\w*)\s*\(").Cast<Match>()
                             .Select(c => c.Groups[1].Value).Distinct())
                {
                    foreach (string helper in HelpersOf(callee))
                    {
                        if (helper.Contains(macro) || wrappers[macro].Any(w => Regex.IsMatch(helper, $@"\b{Regex.Escape(w)}\b")))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

            // Last in the struct, as Unity places them. First, the stereo member
            // came before a fragment's own system values (a VFACE) and only the
            // single-pass instanced variant failed to compile (SpiPatcherProbe).
            void Member(SourceFile owner, int open, string macro)
            {
                int end = MatchBrace(owner.Text, open);
                if (end > 0 && !Has(owner.Text.Substring(open, end - open), macro))
                {
                    Edit(owner, end, "\n\t\t\t\t" + macro + "\n\t\t\t", macro);
                }
            }

            // Built once per name, not per file. The look-behind only skips
            // starts inside a word, where the leftmost match never begins, and
            // retrying there cost the square of every word's length.
            var vertSignatures = vertNames.ToDictionary(n => n,
                n => new Regex($@"(?<!\w)(\w+)\s+{Regex.Escape(n)}\s*\(\s*(\w+)\s+(\w+)\s*\)"));
            var fragSignatures = fragNames.ToDictionary(n => n,
                n => new Regex($@"\b\w+\s+{Regex.Escape(n)}\s*\(\s*(\w+)\s+(\w+)"));

            var outputTypes = new HashSet<string>();
            var defined = new HashSet<string>();
            foreach (var file in unit)
            {
                foreach (string vertName in vertNames)
                {
                    foreach (Match sig in vertSignatures[vertName].Matches(file.Text))
                    {
                        int open = BodyOpen(file.Text, sig.Index + sig.Length);
                        if (open < 0) continue; // a call or a prototype, not the definition
                        if (!OnPc(file, sig.Index)) continue;
                        plan.Passes++;
                        defined.Add(vertName);
                        string v2fType = sig.Groups[1].Value, inType = sig.Groups[2].Value, inArg = sig.Groups[3].Value;
                        int close = MatchBrace(file.Text, open);
                        var inStruct = FindStruct(unit, file, sig.Index, inType);
                        var v2fStruct = FindStruct(unit, file, sig.Index, v2fType);
                        if (close < 0)
                        {
                            plan.Left.Add($"{vertName} (its body couldn't be delimited)");
                            continue;
                        }
                        if (inStruct.file == null || v2fStruct.file == null)
                        {
                            plan.Left.Add($"{vertName} (its vertex structs couldn't be found in the shader or its includes)");
                            continue;
                        }
                        string body = file.Text.Substring(open, close - open);
                        var declaration = Regex.Match(body, $@"\b{Regex.Escape(v2fType)}\s+(\w+)\s*(?:=[^;]*)?;");
                        if (!declaration.Success)
                        {
                            plan.Left.Add($"{vertName} (never declares a variable of its output type)");
                            continue;
                        }
                        Member(inStruct.file, inStruct.open, StereoMacros[0]);
                        Member(v2fStruct.file, v2fStruct.open, StereoMacros[1]);
                        string setup = "";
                        if (!Has(body, StereoMacros[2]))
                        {
                            setup += $"\n\t\t\t\t{StereoMacros[2]}({inArg});";
                            plan.Adds.Add(StereoMacros[2]);
                        }
                        if (!Has(body, StereoMacros[3]))
                        {
                            setup += $"\n\t\t\t\t{StereoMacros[3]}({declaration.Groups[1].Value});";
                            plan.Adds.Add(StereoMacros[3]);
                        }
                        if (setup.Length > 0) Edit(file, open + declaration.Index + declaration.Length, setup, null);
                        outputTypes.Add(v2fType);
                    }
                }
            }

            // A vertex function in some other shape, "void vert(inout appdata v,
            // out v2f o)", is a pass it cannot patch.
            foreach (string vertName in vertNames.Where(n => !defined.Contains(n)))
            {
                plan.Passes++;
                plan.Left.Add($"{vertName} (not in the plain \"Out {vertName}(In v)\" shape)");
            }

            // The eye index in each fragment function that takes a patched
            // output struct first; it may take more after it (a VFACE). Only
            // those: the macro reads a member the struct must carry.
            foreach (var file in unit)
            {
                foreach (string fragName in fragNames)
                {
                    foreach (Match sig in fragSignatures[fragName].Matches(file.Text))
                    {
                        if (!outputTypes.Contains(sig.Groups[1].Value) || !OnPc(file, sig.Index)) continue;
                        int paren = file.Text.IndexOf('(', sig.Index + sig.Groups[0].Value.IndexOf(fragName, StringComparison.Ordinal));
                        int shut = MatchParen(file.Text, paren);
                        int open = shut < 0 ? -1 : BodyOpen(file.Text, shut + 1);
                        int close = open < 0 ? -1 : MatchBrace(file.Text, open);
                        if (close < 0) continue;
                        if (!Has(file.Text.Substring(open, close - open), EyeIndexMacro))
                        {
                            Edit(file, open + 1, $"\n\t\t\t\t{EyeIndexMacro}({sig.Groups[2].Value});", null);
                        }
                    }
                }
            }
            return plan;
        }

        // A program that never compiles for PC is left alone. VRCFury's socket
        // marker ends in a Metal-only pass with no stereo macros, and patching
        // it copied a shader that already draws in both eyes (SpiSweepProbe).
        static readonly Regex Program = new Regex(@"\b(?:CG|HLSL)PROGRAM\b(.*?)\bEND(?:CG|HLSL)\b", RegexOptions.Singleline);
        static readonly Regex OnlyRenderers = new Regex(@"#pragma[ \t]+only_renderers\b([^\r\n]*)");
        static readonly Regex ExcludeRenderers = new Regex(@"#pragma[ \t]+exclude_renderers\b([^\r\n]*)");
        static readonly Regex D3D11 = new Regex(@"\bd3d11\b");

        static List<(int from, int to)> OffPcPrograms(string text) =>
            Program.Matches(text).Cast<Match>().Where(p =>
            {
                var only = OnlyRenderers.Match(p.Groups[1].Value);
                var exclude = ExcludeRenderers.Match(p.Groups[1].Value);
                return (only.Success && !D3D11.IsMatch(only.Groups[1].Value))
                    || (exclude.Success && D3D11.IsMatch(exclude.Groups[1].Value));
            }).Select(p => (p.Index, p.Index + p.Length)).ToList();

        // The definition's opening brace after a signature, across any return
        // semantic (": SV_Target"); -1 for a call or prototype.
        static int BodyOpen(string text, int from)
        {
            var m = BodyStart.Match(text, from);
            return m.Success ? m.Index + m.Length - 1 : -1;
        }

        // Anchored where the match starts, so no copy of the rest of the file
        // is made per match; locked shaders run to 800 KB.
        static readonly Regex BodyStart = new Regex(@"\G\s*(?::\s*\w+\s*)?\{");
        static readonly Regex OneChannel = new Regex(@"\G\s*\.\s*[rx](?!\w)");

        static int MatchBrace(string text, int open) => Closing(text, open, '{', '}');

        static int MatchParen(string text, int open) => Closing(text, open, '(', ')');

        static int Closing(string text, int open, char up, char down)
        {
            if (open < 0 || open >= text.Length || text[open] != up) return -1;
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == up) depth++;
                else if (text[i] == down && --depth == 0) return i;
            }
            return -1;
        }

        // A struct the way its function sees it: the last definition before
        // the function in the same file, as each pass of a multi-pass shader
        // defines its own; else the first in another file of the unit.
        static (SourceFile file, int open) FindStruct(List<SourceFile> unit, SourceFile file, int before, string type)
        {
            var pattern = new Regex($@"struct\s+{Regex.Escape(type)}\s*\{{");
            var local = pattern.Matches(file.Text).Cast<Match>().LastOrDefault(m => m.Index < before);
            if (local != null) return (file, local.Index + local.Length - 1);
            foreach (var other in unit)
            {
                if (other == file) continue;
                var m = pattern.Match(other.Text);
                if (m.Success) return (other, m.Index + m.Length - 1);
            }
            return (null, -1);
        }

        // Depth reads made eye-aware: the declaration becomes the stereo one
        // and tex2D, tex2Dproj and tex2Dlod reads become Unity's depth macros,
        // whatever their arguments. Only two exact spellings were caught. Run
        // only when every read takes the depth value alone (DepthRewritable).
        internal static string StereoDepth(string text)
        {
            text = DepthDeclaration.Replace(text, "UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);");
            var result = new System.Text.StringBuilder();
            int copied = 0;
            foreach (Match m in DepthRead.Matches(text))
            {
                if (m.Index < copied) continue;
                int shut = MatchParen(text, m.Index + m.Value.IndexOf('('));
                if (shut < 0) continue;
                string args = text.Substring(m.Index + m.Length, shut - m.Index - m.Length).Trim();
                // The macros return the red channel already, so the
                // single-channel swizzle after the read goes with it.
                var swizzle = OneChannel.Match(text, shut + 1);
                if (!swizzle.Success) continue;
                int end = shut + 1 + swizzle.Length;
                string macro = m.Groups[1].Value == "proj" ? "SAMPLE_DEPTH_TEXTURE_PROJ"
                    : m.Groups[1].Value == "lod" ? "SAMPLE_DEPTH_TEXTURE_LOD" : "SAMPLE_DEPTH_TEXTURE";
                result.Append(text, copied, m.Index - copied).Append($"{macro}(_CameraDepthTexture, {args})");
                copied = end;
            }
            return result.Append(text, copied, text.Length - copied).ToString();
        }

        // One file of a shader's source: the .shader, or a .cginc it
        // pulls in. The vertex stage often lives in an include, and
        // editing a shared include reaches every shader using it, so
        // includes are cloned and the clones are what get edited.
        internal class SourceFile
        {
            public string OriginalPath;   // as on disk
            public string IncludedAs;     // exactly as written in the #include, or null for the shader
            public string OutputName;     // file name inside RehomedAssets
            public string Text;
            public bool Crlf;
        }

        internal static List<SourceFile> ReadUnit(string shaderPath)
        {
            var unit = new List<SourceFile>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Walk(string path, string includedAs)
            {
                string full = Path.GetFullPath(path);
                if (!seen.Add(full) || !File.Exists(path))
                {
                    return;
                }
                string text;
                try { text = File.ReadAllText(path); }
                catch { return; }

                unit.Add(new SourceFile
                {
                    OriginalPath = path,
                    IncludedAs = includedAs,
                    Text = text,
                    Crlf = text.Contains("\r\n"),
                });

                string folder = Path.GetDirectoryName(path) ?? ".";
                foreach (Match m in IncludeDirective.Matches(text))
                {
                    string rel = m.Groups[2].Value;
                    string candidate = ResolveInclude(folder, rel);
                    if (candidate != null)
                    {
                        Walk(candidate, rel);
                    }
                }
            }

            Walk(shaderPath, null);
            return unit;
        }

        // The file an #include names, found the way Unity finds it: beside the
        // file it appears in, else from the project root, which is how an
        // "Assets/..." or "Packages/..." include is written. Only the first was
        // tried, so a shader whose vertex function sat in a root-written include
        // was refused as unreadable and drew in one eye. Null for Unity's own.
        internal static string ResolveInclude(string folder, string rel)
        {
            string trimmed = Regex.Replace(rel, @"[\\/]+", "/").TrimStart('/');
            string beside = Path.Combine(folder, trimmed);
            if (File.Exists(beside)) return beside.Replace('\\', '/');
            if (trimmed.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                string rooted = FileUtil.GetPhysicalPath(trimmed);
                if (!string.IsNullOrEmpty(rooted) && File.Exists(rooted)) return rooted.Replace('\\', '/');
            }
            return null;
        }

        // The declaration line, space or not: ShaderLab takes Shader"name",
        // and a copy the old pattern missed kept the original's name beside
        // it. Anchored to a line start so a display name ending in Shader is safe.
        internal static string Rename(string text, string newName) =>
            Regex.Replace(text, @"^([ \t]*)Shader\s*""[^""]+""",
                m => m.Groups[1].Value + "Shader \"" + newName + "\"", RegexOptions.Multiline);

        internal static SourceFile FindIn(List<SourceFile> unit, string pattern, out Match match)
        {
            foreach (var file in unit)
            {
                var m = Regex.Match(file.Text, pattern);
                if (m.Success)
                {
                    match = m;
                    return file;
                }
            }
            match = Match.Empty;
            return null;
        }

        // A copy written and waiting for its import and its checks.
        sealed class Staged
        {
            public List<SourceFile> Unit;
            public string Key, OutPath, Dir;
            public List<string> Written;
        }

        // Everything up to the import: the plan applied, the files named and written. Null with
        // `reused` set for a copy already in place, or with the reason it was refused. The unit
        // and plan are the judge's, or null to make them here.
        static Staged Stage(string sourcePath, string shaderName, string dir, List<SourceFile> unit,
            StereoPlan plan, out string reason, out bool grabPassLimited, out List<string> passesLeft, out Shader reused)
        {
            grabPassLimited = false;
            reused = null;
            passesLeft = new List<string>();
            if (unit == null)
            {
                try
                {
                    unit = ReadUnit(sourcePath);
                }
                catch (Exception e)
                {
                    reason = "source unreadable: " + e.Message;
                    return null;
                }
            }
            if (unit.Count == 0)
            {
                reason = "source unreadable";
                return null;
            }
            var shaderFile = unit[0];

            // Line endings are tracked per file (SourceFile.Crlf) and reapplied before writing:
            // the inserted lines use \n, and mixing them into a CRLF file makes Unity warn about
            // inconsistent endings on import and blame AvatarBridge for it. An include can easily
            // disagree with its shader, so this can't be decided once for the whole unit.

            plan = plan ?? PlanStereo(unit);
            if (plan.Passes == 0)
            {
                reason = plan.SurfaceOnly
                    ? "surface shader: Unity generates its passes, stereo included, so there is nothing to patch"
                    : "no vertex/fragment pragma found";
                return null;
            }
            if (plan.Left.Count == plan.Passes)
            {
                reason = string.Join("; ", plan.Left);
                return null;
            }
            passesLeft = plan.Left;

            // A GrabPass is patched like anything else; the macros make
            // the effect draw in both eyes. Its screen grab cannot be
            // made eye-correct here: the screen-space macros expect a
            // per-eye array a GrabPass never produces and render grey.
            // One eye's view shown to both beats grey.
            grabPassLimited = Regex.IsMatch(shaderFile.Text, @"GrabPass\s*\{");

            // The struct members, the vertex setup and the fragment eye index,
            // from the end of each file back, so no insertion moves another's place.
            foreach (var group in plan.Edits.GroupBy(e => e.file))
            {
                foreach (var edit in group.OrderByDescending(e => e.at))
                {
                    edit.file.Text = edit.file.Text.Insert(edit.at, edit.text);
                }
            }

            // Screen-space depth, which the macros alone never fix:
            // _CameraDepthTexture is an array under instancing. Unity's depth
            // macros return the depth value alone, so a read used for more
            // (DecodeFloatRG of two channels) would compile and read wrong;
            // then every read is left, and the report says so.
            if (plan.Depth && plan.DepthRewritable)
            {
                foreach (var file in unit)
                {
                    file.Text = StereoDepth(file.Text);
                }
            }
            else if (plan.Depth)
            {
                passesLeft.Add("its depth reads (one takes more than the depth value), which still read one eye");
            }

            // Rename so it can't collide with the original in the shader list.
            string newName = shaderName + " (SPI)";
            shaderFile.Text = Rename(shaderFile.Text, newName);

            // Name every file, then repoint the #include lines at the copies. Flattened into one
            // folder, so an include written as "sub/foo.cginc" becomes just "foo_SPI.cginc", and
            // named apart from every other file this run writes there.
            // A folder the AssetDatabase has not seen takes no imports, so it is made and seen
            // before the first write, while nothing of this batch is in it. A refresh rescans
            // the whole project, so only once.
            if (!(AssetDatabase.IsValidFolder(dir) && Directory.Exists(dir)))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }
            foreach (var file in unit)
            {
                file.OutputName = Claim(dir, Path.GetFileNameWithoutExtension(file.OriginalPath) + "_SPI",
                    Path.GetExtension(file.OriginalPath));
            }
            // Repoint by resolving each #include against the file it appears in, rather than by
            // string-matching the spelling it was first discovered under. The same file is often
            // referred to two ways: Cancercore.cginc includes "CGInclude/CSEnums.cginc" while the
            // files inside CGInclude include their siblings as plain "CSEnums.cginc". Matching one
            // remembered spelling repointed the first and left the second dangling, and the copy
            // failed to compile on an include it could no longer find.
            var byPath = new Dictionary<string, SourceFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in unit)
            {
                byPath[Path.GetFullPath(file.OriginalPath)] = file;
            }
            foreach (var file in unit)
            {
                string folder = Path.GetDirectoryName(file.OriginalPath) ?? ".";
                file.Text = IncludeDirective.Replace(file.Text, m =>
                {
                    string candidate = ResolveInclude(folder, m.Groups[2].Value);
                    if (candidate != null &&
                        byPath.TryGetValue(Path.GetFullPath(candidate), out var target) &&
                        target != file)
                    {
                        return m.Groups[1].Value + target.OutputName + m.Groups[3].Value;
                    }
                    return m.Value; // Unity's own, or something not cloned: leave it
                });
                if (file.Crlf)
                {
                    file.Text = file.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                }
            }

            // A reconvert into the same folder writes the same text again, and a
            // rewritten file is reimported and recompiled for nothing. Only byte for
            // byte what is already there; text not proven this session, after a
            // restart, is checked where it lies, and written again only if that fails.
            string key = ContentKey(unit, true);
            bool proven = CompiledCopies.Contains(key);
            string outPath = dir + "/" + shaderFile.OutputName;
            if (unit.All(f => Unchanged(dir + "/" + f.OutputName, f.Text))
                && AssetDatabase.LoadAssetAtPath<Shader>(outPath) is Shader existing
                && (proven || Verify(existing) == null))
            {
                CompiledCopies.Add(key);
                Times.Reused++;
                reason = null;
                reused = existing;
                return null;
            }

            var written = new List<string>();
            var before = new Dictionary<string, byte[]>();
            try
            {
                foreach (var file in unit)
                {
                    string path = dir + "/" + file.OutputName;
                    before[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;
                    File.WriteAllText(path, file.Text);
                    written.Add(path);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // Nothing of it is imported yet, so putting the disk back is the whole undo: what
                // this copy made goes and what it overwrote gets its old bytes, so a reconvert's
                // half copy never turns up mixed with the last one at the next refresh.
                foreach (var old in before)
                {
                    try
                    {
                        if (old.Value == null) File.Delete(old.Key);
                        else File.WriteAllBytes(old.Key, old.Value);
                    }
                    catch { }
                }
                reason = "the copy could not be written: " + e.Message;
                return null;
            }

            reason = null;
            return new Staged { Unit = unit, Key = key, OutPath = outPath, Dir = dir, Written = written };
        }

        // Includes first, shaders last. Importing a shader is what compiles it, and it fails on
        // includes not yet in the AssetDatabase. One batch each, for every copy at once.
        static void Import(List<Staged> staged)
        {
            if (staged.Count == 0) return;
            foreach (bool shaders in new[] { false, true })
            {
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var s in staged)
                    {
                        foreach (string path in shaders ? new List<string> { s.OutPath } : s.Written.Skip(1).ToList())
                        {
                            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                        }
                    }
                }
                finally { AssetDatabase.StopAssetEditing(); }
            }
        }

        // The imported copy, checked, or null and why. A failed copy's files join `doomed`.
        static Shader Finish(Staged s, List<string> doomed, out string reason)
        {
            // Checked even when the same text passed in another folder: an include
            // left as text can resolve differently beside a different copy.
            var result = AssetDatabase.LoadAssetAtPath<Shader>(s.OutPath);
            string errors = Verify(result);
            if (errors != null)
            {
                // Keep the failed source, as .txt so Unity never tries to compile it, every file
                // of it: the edits often land in an include, and keeping the shader alone kept
                // nothing of the line that failed.
                bool kept = true;
                foreach (var file in s.Unit)
                {
                    try { File.WriteAllText(s.Dir + "/" + file.OutputName + ".failed.txt", file.Text); }
                    catch { kept = false; }
                }
                doomed.AddRange(s.Written);
                reason = "patched copy did not compile: " + errors +
                         (kept
                             ? $". The attempted source was kept in {s.Dir} as {s.Unit[0].OutputName}.failed.txt" +
                               (s.Unit.Count > 1 ? $" and {BridgeElements.Count(s.Unit.Count - 1, "include", "includes")} beside it" : "") +
                               ": attach them to a bug report"
                             : "");
                return null;
            }
            CompiledCopies.Add(s.Key);
            reason = null;
            return result;
        }

        static string Verify(Shader copy) => copy == null ? "copy failed to import"
            : ShaderUtil.ShaderHasError(copy) ? ShaderErrors(copy)
            : StereoVariantError(copy);

        // Exactly what WriteAllText would put there: UTF-8 with no byte order mark.
        static bool Unchanged(string path, string text)
        {
            if (!File.Exists(path)) return false;
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            return new FileInfo(path).Length == bytes.Length && File.ReadAllBytes(path).SequenceEqual(bytes);
        }

        // A file name in the output folder nothing else this run has taken.
        static string Claim(string dir, string stem, string extension)
        {
            string name = stem + extension;
            for (int n = 2; !Claimed.Add(dir + "/" + name); n++)
            {
                name = stem + "_" + n + extension;
            }
            return name;
        }

        // The message alone fixes nothing. The line number says which edit;
        // the platform says which #if branch ran.
        static string ShaderErrors(Shader shader) =>
            string.Join("; ", ShaderUtil.GetShaderMessages(shader)
                .Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                .Take(3).Select(m =>
                {
                    string where = m.line > 0 ? $" at line {m.line}" : "";
                    string platform = m.platform != UnityEditor.Rendering.ShaderCompilerPlatform.None
                        ? $" on {m.platform}" : "";
                    string detail = string.IsNullOrEmpty(m.messageDetails) ? "" : $": {m.messageDetails.Trim()}";
                    return $"{m.message}{where}{platform}{detail}";
                }));

        // The variant ChilloutVR draws, compiled outright for every pass: a
        // fault only that variant has, a macro declared twice, left
        // ShaderHasError false and the default variant compiling
        // (SpiFactsProbe). Null when every pass compiles.
        static string StereoVariantError(Shader shader)
        {
            Times.VariantChecks++;
            Times.Variants.Start();
            var data = ShaderUtil.GetShaderData(shader);
            var keywords = new[] { "STEREO_INSTANCING_ON" };
            for (int s = 0; s < data.SubshaderCount; s++)
            {
                var subshader = data.GetSubshader(s);
                for (int p = 0; p < subshader.PassCount; p++)
                {
                    var pass = subshader.GetPass(p);
                    foreach (var stage in new[] { UnityEditor.Rendering.ShaderType.Vertex, UnityEditor.Rendering.ShaderType.Fragment })
                    {
                        if (!pass.HasShaderStage(stage))
                        {
                            continue;
                        }
                        var info = pass.CompileVariant(stage, keywords,
                            UnityEditor.Rendering.ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
                        if (info.Success)
                        {
                            continue;
                        }
                        var first = (info.Messages ?? new ShaderMessage[0])
                            .Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                            .Select(m => m.line > 0 ? $"{m.message} at line {m.line}" : m.message)
                            .FirstOrDefault() ?? "no message";
                        string passName = string.IsNullOrEmpty(pass.Name) ? $"pass {p}" : $"pass \"{pass.Name}\"";
                        Times.Variants.Stop();
                        return $"its single-pass instanced {stage.ToString().ToLowerInvariant()} variant fails in {passName}: {first}";
                    }
                }
            }
            Times.Variants.Stop();
            return null;
        }

        static IEnumerable<AnimationClip> ClipsOf(AnimatorController controller)
        {
            if (controller == null)
            {
                yield break;
            }
            string ours = AssetDatabase.GetAssetPath(controller);
            string folder = string.IsNullOrEmpty(ours) ? null : Path.GetDirectoryName(ours)?.Replace('\\', '/');
            var seen = new HashSet<AnimationClip>();
            var pending = new Stack<Motion>();
            foreach (var layer in controller.layers)
            {
                if (layer?.stateMachine == null) continue;
                foreach (var motion in MotionsOf(layer.stateMachine))
                {
                    pending.Push(motion);
                }
            }
            while (pending.Count > 0)
            {
                var motion = pending.Pop();
                if (motion is BlendTree tree)
                {
                    foreach (var child in tree.children)
                    {
                        if (child.motion != null) pending.Push(child.motion);
                    }
                    continue;
                }
                if (!(motion is AnimationClip clip) || !seen.Add(clip))
                {
                    continue;
                }
                string path = AssetDatabase.GetAssetPath(clip);
                // In-memory clips (no path) are generated by construction; on-disk ones must sit inside
                // this conversion's own folder.
                if (!string.IsNullOrEmpty(path)
                    && (folder == null || !path.Replace('\\', '/').StartsWith(folder, StringComparison.Ordinal)))
                {
                    continue;
                }
                yield return clip;
            }
        }

        static IEnumerable<Motion> MotionsOf(AnimatorStateMachine machine)
        {
            foreach (var child in machine.states)
            {
                if (child.state?.motion != null) yield return child.state.motion;
            }
            foreach (var sub in machine.stateMachines)
            {
                if (sub.stateMachine == null) continue;
                foreach (var motion in MotionsOf(sub.stateMachine)) yield return motion;
            }
        }

        static Material Repoint(Material original, Shader patchedShader, string dir,
            Dictionary<Material, Material> clones)
        {
            if (clones.TryGetValue(original, out var existing))
            {
                return existing;
            }

            var copy = UnityEngine.Object.Instantiate(original);
            copy.shader = patchedShader;
            Directory.CreateDirectory(dir);
            // CreateAsset renames the object after the file, so the path decides the final name.
            string path = OutputAssetPaths.Claim(dir + "/" + original.name + "_SPI.mat");
            AssetDatabase.CreateAsset(copy, path);
            clones[original] = copy;
            return copy;
        }
    }
}
#endif
