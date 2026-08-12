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
    // repoint this avatar's materials only); refuse anything not
    // plainly written (surface, locked, shared-include structs);
    // prove the copy compiles before repointing, or delete it.
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

        public static void Run(BridgeContext ctx)
        {
            if (!ctx.Settings.patchNonSpiShaders)
            {
                return;
            }

            string dir = ctx.OutputDir.TrimEnd('/') + "/RehomedAssets";
            var patched = new Dictionary<Shader, Shader>();
            // One clone per material, not per material slot: a material used by four slots is
            // still one material, and cloning it per slot would break batching between them.
            var clones = new Dictionary<Material, Material>();
            var repointed = new List<string>();
            var recipesUsed = new List<string>();
            var refused = new List<string>();
            var alreadyCorrect = new List<string>();
            var grabLimited = new List<string>();

            foreach (var renderer in ctx.Target.GetComponentsInChildren<Renderer>(true))
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
                    if (patched.TryGetValue(shader, out var already))
                    {
                        if (already != null) { materials[i] = Repoint(material, already, dir, clones); changed = true; }
                        continue;
                    }

                    string source = SourcePathOf(shader);
                    if (source == null)
                    {
                        // Engine shaders have no source on disk and ship
                        // stereo-correct. Generated avatar shaders do
                        // have source and fall through to the real check.
                        patched[shader] = null;
                        continue;
                    }
                    if (DeclaresStereo(source))
                    {
                        patched[shader] = null;
                        alreadyCorrect.Add(shader.name);
                        continue;
                    }

                    var fixedShader = TryPatch(source, shader.name, dir, out string reason,
                        out var appliedRecipe, out bool recipeWasExact, out bool grabPassLimited);
                    patched[shader] = fixedShader;
                    if (fixedShader == null)
                    {
                        refused.Add($"{shader.name} ({reason})");
                        continue;
                    }
                    materials[i] = Repoint(material, fixedShader, dir, clones);
                    repointed.Add(shader.name);
                    if (grabPassLimited)
                    {
                        grabLimited.Add(shader.name);
                    }
                    if (appliedRecipe != null)
                    {
                        recipesUsed.Add($"{shader.name} — {appliedRecipe.Note}" +
                            (recipeWasExact ? "" : " (your copy differs from the revision the recipe was written against, but every line it edits matched)"));
                    }
                    changed = true;
                }
                if (changed)
                {
                    renderer.sharedMaterials = materials;
                }
            }

            // Materials that arrive by animated swap sit on no renderer
            // right now, invisible to the loop above, and draw into one
            // eye just as badly when assigned. The curve is rewritten
            // to the patched clone too; otherwise the toggle would
            // still assign the original.
            int swapsRepointed = 0;
            foreach (var clip in ClipsOf(ctx.MergedController))
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
                        if (!patched.TryGetValue(shader, out var fixedShader))
                        {
                            string source = SourcePathOf(shader);
                            if (source == null)
                            {
                                patched[shader] = null;
                                continue;
                            }
                            if (DeclaresStereo(source))
                            {
                                patched[shader] = null;
                                alreadyCorrect.Add(shader.name);
                                continue;
                            }
                            fixedShader = TryPatch(source, shader.name, dir, out string reason,
                                out var recipe, out bool exact, out bool grabbed);
                            patched[shader] = fixedShader;
                            if (fixedShader == null)
                            {
                                refused.Add($"{shader.name} ({reason})");
                                continue;
                            }
                            repointed.Add(shader.name);
                            if (grabbed) { grabLimited.Add(shader.name); }
                            if (recipe != null)
                            {
                                recipesUsed.Add($"{shader.name} — {recipe.Note}" +
                                    (exact ? "" : " (your copy differs from the revision the recipe was written against, but every line it edits matched)"));
                            }
                        }
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
            if (swapsRepointed > 0)
            {
                ctx.Report.Converted(Category,
                    $"{swapsRepointed} material-swap curve(s) repointed at a patched shader",
                    "These materials are never assigned to a renderer — an animation swaps them in, " +
                    "which is how hypno overlays, transformation skins and costume recolours are " +
                    "built. Patching the shader alone would have changed nothing, because the " +
                    "toggle would still have assigned the original, so the swap itself now points " +
                    "at the patched copy.");
            }

            if (repointed.Count > 0)
            {
                ctx.Report.Approximated(Category, $"{repointed.Distinct().Count()} shader(s) patched for VR stereo",
                    $"{string.Join(", ", repointed.Distinct())} — copied into RehomedAssets with the single-pass " +
                    "instanced macros added, and this avatar's materials repointed at the copies. The originals " +
                    "are untouched. Each copy was checked for compile errors, though whether it *looks* right " +
                    "can only be judged in VR, so check the effect in both eyes. " +
                    "This is a ChilloutVR problem specifically: ChilloutVR renders single-pass instanced " +
                    "while VRChat renders double-wide single-pass, and under double-wide a shader gets both " +
                    "eyes without asking — which is why it looked fine before converting. Nothing here needs " +
                    "undoing, though: the macros are the mode-agnostic ones, so the patched copy stays " +
                    "correct under VRChat's mode and on desktop as well.");
            }
            if (grabLimited.Count > 0)
            {
                ctx.Report.Warning(Category,
                    $"{grabLimited.Distinct().Count()} patched shader(s) grab the screen — the background they " +
                    "refract comes from one eye",
                    $"{string.Join(", ", grabLimited.Distinct())} — these now DRAW in both eyes, but they read " +
                    "the screen through a GrabPass, and ChilloutVR's rendering mode doesn't give a GrabPass " +
                    "per-eye content. So the glass, refraction or heat-haze shows one eye's view to both. " +
                    "Nothing here can fix that: rewriting the reads to the per-eye macros renders GREY in VR " +
                    "(tried, twice, once by hand). If it looks wrong in VR, use a shader that doesn't grab the " +
                    "screen. On desktop it is unaffected.");
            }
            if (recipesUsed.Count > 0)
            {
                ctx.Report.Approximated(Category,
                    $"{recipesUsed.Count} shader(s) fixed by a hand-written stereo recipe",
                    string.Join("; ", recipesUsed) + ". These need more than the standard macros, so the " +
                    "edit was written by hand once and pinned to that exact version of the file — a shader " +
                    "that has been updated or edited will not match, and is refused rather than guessed at. " +
                    "Only your copy in RehomedAssets is changed; the original shader is untouched. Worth a " +
                    "look in VR with both eyes open.");
            }
            if (refused.Count > 0)
            {
                ctx.Report.Warning(Category, $"{refused.Count} shader(s) could not be patched for VR stereo",
                    $"{string.Join(", ", refused)} — these still won't draw correctly in both eyes. Patching is " +
                    "only attempted on plainly written vertex/fragment shaders; anything else needs doing by " +
                    "hand or replacing with a different shader.");
            }
            // Every shader gets a verdict: patched, refused, or did
            // not need it. Silence reads as a miss.
            if (alreadyCorrect.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"{alreadyCorrect.Distinct().Count()} shader(s) already speak single-pass instanced — left untouched",
                    $"{string.Join(", ", alreadyCorrect.Distinct())} — the source declares the full " +
                    "stereo-instancing macro set, so ChilloutVR's rendering mode is already handled and " +
                    "patching would change nothing. Locked and generated shaders (Hidden/Locked/…) are " +
                    "checked like any other. If one of these looks wrong in game, the cause is something " +
                    "other than the macros this option adds.");
            }
        }

        internal static string SourcePathOf(Shader shader)
        {
            string path = AssetDatabase.GetAssetPath(shader);
            return !string.IsNullOrEmpty(path) && path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)
                   && File.Exists(path) ? path : null;
        }

        internal static bool DeclaresStereo(string path)
        {
            // The whole include unit, not the one file, through the
            // same ReadUnit the patcher uses. Modern toon shaders keep
            // their real code, stereo macros included, in includes.
            var remaining = new HashSet<string>(StereoMacros, StringComparer.Ordinal);
            try
            {
                foreach (var file in ReadUnit(path))
                {
                    remaining.RemoveWhere(m => file.Text.Contains(m, StringComparison.Ordinal));
                    if (remaining.Count == 0) return true;
                }
            }
            catch { return true; } // unreadable: leave it alone
            return false;
        }

        // One file of a shader's source: the .shader, or a .cginc it
        // pulls in. The vertex stage often lives in an include, and
        // editing a shared include reaches every shader using it, so
        // includes are cloned and the clones are what get edited.
        class SourceFile
        {
            public string OriginalPath;   // as on disk
            public string IncludedAs;     // exactly as written in the #include, or null for the shader
            public string OutputName;     // file name inside RehomedAssets
            public string Text;
            public bool Crlf;
        }

        static List<SourceFile> ReadUnit(string shaderPath)
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
                foreach (Match m in Regex.Matches(text, @"#include\s+""([^""]+)"""))
                {
                    string rel = m.Groups[1].Value;
                    string candidate = Path.Combine(folder, rel);
                    if (File.Exists(candidate))
                    {
                        Walk(candidate.Replace('\\', '/'), rel);
                    }
                }
            }

            Walk(shaderPath, null);
            return unit;
        }

        static SourceFile FindIn(List<SourceFile> unit, string pattern, out Match match)
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

        static Shader TryPatch(string sourcePath, string shaderName, string dir, out string reason,
            out ShaderFixRecipes.Recipe appliedRecipe, out bool recipeWasExact, out bool grabPassLimited)
        {
            appliedRecipe = null;
            recipeWasExact = false;
            grabPassLimited = false;
            var unit = ReadUnit(sourcePath);
            if (unit.Count == 0)
            {
                reason = "source unreadable";
                return null;
            }
            var shaderFile = unit[0];
            string text = shaderFile.Text;

            // Taken before a single edit, so the fingerprint identifies
            // the file as the user has it.
            var recipe = ShaderFixRecipes.Find(shaderName, text, out bool exactRecipeRevision);

            // Line endings are tracked per file (SourceFile.Crlf) and reapplied before writing:
            // the inserted lines use \n, and mixing them into a CRLF file makes Unity warn about
            // inconsistent endings on import and blame AvatarBridge for it. An include can easily
            // disagree with its shader, so this can't be decided once for the whole unit.

            if (Regex.IsMatch(text, @"#pragma\s+surface"))
            {
                reason = "surface shader — Unity generates the vertex stage, nothing to patch";
                return null;
            }
            // A GrabPass is patched like anything else; the macros make
            // the effect draw in both eyes. Its screen grab cannot be
            // made eye-correct here: the screen-space macros expect a
            // per-eye array a GrabPass never produces and render grey.
            // One eye's view shown to both beats grey.
            grabPassLimited = Regex.IsMatch(text, @"GrabPass\s*\{");
            var vertPragma = Regex.Match(text, @"#pragma\s+vertex\s+(\w+)");
            var fragPragma = Regex.Match(text, @"#pragma\s+fragment\s+(\w+)");
            if (!vertPragma.Success || !fragPragma.Success)
            {
                reason = "no vertex/fragment pragma found";
                return null;
            }
            string vertName = vertPragma.Groups[1].Value, fragName = fragPragma.Groups[1].Value;

            // "v2fType vertName (appdataType v)". Searched across the whole unit, because the
            // vertex function is as likely to be in an include as in the .shader.
            var vertFile = FindIn(unit,
                $@"(\w+)\s+{Regex.Escape(vertName)}\s*\(\s*(\w+)\s+(\w+)\s*\)", out var sig);
            if (vertFile == null)
            {
                reason = "vertex function signature not recognised";
                return null;
            }
            string v2fType = sig.Groups[1].Value, inType = sig.Groups[2].Value, inArg = sig.Groups[3].Value;

            var inFile = FindIn(unit, $@"struct\s+{Regex.Escape(inType)}\s*\{{", out _);
            var v2fFile = FindIn(unit, $@"struct\s+{Regex.Escape(v2fType)}\s*\{{", out _);
            if (inFile == null || v2fFile == null)
            {
                reason = "its vertex structs couldn't be found in the shader or its includes";
                return null;
            }

            // 1 and 2: the struct members, in whichever file declares each.
            inFile.Text = Regex.Replace(inFile.Text, $@"(struct\s+{Regex.Escape(inType)}\s*\{{)",
                "$1\n\t\t\t\tUNITY_VERTEX_INPUT_INSTANCE_ID");
            v2fFile.Text = Regex.Replace(v2fFile.Text, $@"(struct\s+{Regex.Escape(v2fType)}\s*\{{)",
                "$1\n\t\t\t\tUNITY_VERTEX_OUTPUT_STEREO");

            // 3 and 4: located after the struct edits; that order is
            // load-bearing. Insertion is by index, and the edits above
            // shift everything below them.
            //
            // The output struct is found anywhere in the function body,
            // not just as its first statement; authors routinely compute
            // values before declaring it. The body is delimited by brace
            // matching rather than a regex so a declaration in the NEXT function can't be
            // mistaken for this one's.
            int vertStart = vertFile.Text.IndexOf(sig.Value, StringComparison.Ordinal);
            int bodyOpen = vertStart < 0 ? -1 : vertFile.Text.IndexOf('{', vertStart);
            int bodyEnd = -1;
            if (bodyOpen >= 0)
            {
                int depth = 0;
                for (int i = bodyOpen; i < vertFile.Text.Length; i++)
                {
                    if (vertFile.Text[i] == '{') depth++;
                    else if (vertFile.Text[i] == '}' && --depth == 0) { bodyEnd = i; break; }
                }
            }
            if (bodyEnd < 0)
            {
                reason = "the vertex function's body couldn't be delimited";
                return null;
            }
            string body = vertFile.Text.Substring(bodyOpen, bodyEnd - bodyOpen);
            var declaration = Regex.Match(body,
                $@"\b{Regex.Escape(v2fType)}\s+(\w+)\s*(?:=[^;]*)?;");
            if (!declaration.Success)
            {
                reason = "the vertex function never declares a variable of its output type";
                return null;
            }
            string outVar = declaration.Groups[1].Value;
            int insertAt = bodyOpen + declaration.Index + declaration.Length;

            vertFile.Text = vertFile.Text.Insert(insertAt,
                $"\n\t\t\t\tUNITY_SETUP_INSTANCE_ID({inArg});\n\t\t\t\tUNITY_INITIALIZE_VERTEX_OUTPUT_STEREO({outVar});");

            // 5: the eye index in the fragment stage, wherever frag lives.
            var fragFile = FindIn(unit,
                $@"\w+\s+{Regex.Escape(fragName)}\s*\(\s*{Regex.Escape(v2fType)}\s+(\w+)\s*\)[^{{]*\{{",
                out var fragSig);
            if (fragFile != null)
            {
                string fragArg = fragSig.Groups[1].Value;
                fragFile.Text = fragFile.Text.Replace(fragSig.Value, fragSig.Value +
                    $"\n\t\t\t\tUNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX({fragArg});");
            }

            // 6: screen-space depth, which the four-macro test misses.
            // _CameraDepthTexture is an array under instancing, so a
            // sampler2D read is wrong whatever macros exist.
            foreach (var file in unit)
            {
                file.Text = Regex.Replace(file.Text, @"sampler2D\s+_CameraDepthTexture\s*;",
                    "UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);");
                file.Text = Regex.Replace(file.Text,
                    @"tex2Dproj\s*\(\s*_CameraDepthTexture\s*,\s*(UNITY_PROJ_COORD\([^)]*\))\s*\)\s*\.\s*r",
                    "SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, $1)");
            }

            // 7: the hand-written recipe for this exact file, applied
            // last, describing only what the generic pass cannot derive.
            if (recipe != null)
            {
                if (!ShaderFixRecipes.TryApply(recipe, shaderFile.Text, out string patched, out string failure))
                {
                    // Every anchor existed in the original, so a generic
                    // edit moved one. A recipe applies whole or not at all.
                    reason = $"its stereo recipe no longer fits after the generic patch ({failure})";
                    return null;
                }
                shaderFile.Text = patched;
                appliedRecipe = recipe;
                recipeWasExact = exactRecipeRevision;
            }

            // Rename so it can't collide with the original in the shader list.
            string newName = shaderName + " (SPI)";
            shaderFile.Text = Regex.Replace(shaderFile.Text, @"Shader\s+""[^""]+""",
                "Shader \"" + newName + "\"", RegexOptions.None);

            // Name every file, then repoint the #include lines at the copies. Flattened into one
            // folder, so an include written as "sub/foo.cginc" becomes just "foo_SPI.cginc".
            Directory.CreateDirectory(dir);
            shaderFile.OutputName = Path.GetFileNameWithoutExtension(sourcePath) + "_SPI.shader";
            // Flattened into one folder, so two includes with the same basename in different
            // source folders would otherwise overwrite each other and silently give the shader
            // the wrong file.
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { shaderFile.OutputName };
            foreach (var file in unit.Skip(1))
            {
                string stem = Path.GetFileNameWithoutExtension(file.OriginalPath) + "_SPI";
                string extension = Path.GetExtension(file.OriginalPath);
                string name = stem + extension;
                for (int n = 2; !used.Add(name); n++)
                {
                    name = stem + "_" + n + extension;
                }
                file.OutputName = name;
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
                file.Text = Regex.Replace(file.Text, @"#include\s+""([^""]+)""", m =>
                {
                    string candidate = Path.Combine(folder, m.Groups[1].Value);
                    if (File.Exists(candidate) &&
                        byPath.TryGetValue(Path.GetFullPath(candidate), out var target) &&
                        target != file)
                    {
                        return $"#include \"{target.OutputName}\"";
                    }
                    return m.Value; // Unity's own, or something we didn't clone: leave it
                });
                if (file.Crlf)
                {
                    file.Text = file.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                }
            }

            var written = new List<string>();
            foreach (var file in unit)
            {
                string path = dir + "/" + file.OutputName;
                File.WriteAllText(path, file.Text);
                written.Add(path);
            }

            // Includes first, shader last. Importing the shader is what
            // compiles it, and it fails on includes not yet in the
            // AssetDatabase.
            foreach (string path in written.Skip(1))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            AssetDatabase.Refresh();

            string outPath = written[0];
            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceSynchronousImport);

            var result = AssetDatabase.LoadAssetAtPath<Shader>(outPath);
            if (result == null || ShaderUtil.ShaderHasError(result))
            {
                // The message alone fixes nothing. The line number says
                // which edit; the platform says which #if branch ran.
                string errors = result != null
                    ? string.Join("; ", ShaderUtil.GetShaderMessages(result)
                        .Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        .Take(3).Select(m =>
                        {
                            string where = m.line > 0 ? $" at line {m.line}" : "";
                            string platform = m.platform != UnityEditor.Rendering.ShaderCompilerPlatform.None
                                ? $" on {m.platform}" : "";
                            string detail = string.IsNullOrEmpty(m.messageDetails)
                                ? "" : $" — {m.messageDetails.Trim()}";
                            return $"{m.message}{where}{platform}{detail}";
                        }))
                    : "copy failed to import";

                // Keep the failed source, as .txt so Unity never tries to compile it. Without
                // this the evidence is deleted at the exact moment it becomes interesting, and
                // the only way to see the offending line is another round trip through a tester.
                string kept = null;
                try
                {
                    kept = outPath + ".failed.txt";
                    File.WriteAllText(kept, unit[0].Text);
                }
                catch { kept = null; }
                // Every file of the unit goes, not just the shader.
                // No orphaned include copies in the output folder.
                foreach (string path in written)
                {
                    AssetDatabase.DeleteAsset(path);
                }
                reason = "patched copy did not compile: " + errors +
                         (kept != null
                             ? $". The attempted source was kept at {kept} — attach that to a bug report, " +
                               "the failing line is in it"
                             : "");
                return null;
            }
            reason = null;
            return result;
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
                // In-memory clips (no path) are ours by construction; on-disk ones must sit inside
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
