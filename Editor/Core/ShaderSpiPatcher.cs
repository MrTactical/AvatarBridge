#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    /// <summary>
    /// Patches shaders that don't support single-pass instanced stereo into copies that do.
    ///
    /// The two platforms do not render VR the same way, and this is one of the few places where
    /// that difference reaches the avatar. ChilloutVR forces single-pass **instanced**
    /// (`CCK_EnvConfig.cs`: `StereoRenderingPath.Instancing`); VRChat forces plain single-pass,
    /// the double-wide one (`EnvConfig.cs`: `StereoRenderingPath.SinglePass`). Both are
    /// unconditional.
    ///
    /// That matters because under double-wide a shader gets both eyes without having to ask —
    /// so a shader that never opted into instancing looks perfectly fine in VRChat, and its
    /// author had no reason to notice. Under instancing the same shader draws into one eye only.
    /// It is a conversion problem rather than a broken shader, which is exactly the kind of thing
    /// worth fixing here. The CCK reports them; this fixes the ones fixable mechanically.
    ///
    /// Three rules make that safe enough to do automatically:
    ///
    /// **Never touch the original.** A patched copy goes in the output's RehomedAssets folder
    /// beside the other rescued assets, under its own shader name, and only this avatar's
    /// materials are repointed at it. The source shader usually belongs to somebody else.
    ///
    /// **Refuse anything not plainly written.** Surface shaders have no vertex function to patch,
    /// locked and generated shaders can't be parsed, and structs shared across includes can't be
    /// edited from one file. Those are reported, not attempted.
    ///
    /// **Prove it compiles.** The copy is imported and checked with ShaderUtil before any material
    /// is repointed; if it has errors the copy is deleted and the original left alone. That turns
    /// the worst case from silently wrong pixels into a report line.
    ///
    /// Compilation is the only thing that can be verified here — that the result *looks* right is
    /// not something an editor script can judge, so the report says what was patched and asks for
    /// it to be checked in VR.
    /// </summary>
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
                        // Engine shaders (Standard, Hidden/Internal-…) have no source file on
                        // disk to read or patch, and Unity's own ship stereo-correct. Generated
                        // avatar shaders — Poiyomi lock-in and SPS live at Hidden/Locked/… —
                        // DO have source, so they fall through to the honest check below. A
                        // name-based "Hidden/ belongs to the engine" skip used to silently
                        // ignore exactly the shaders people then asked about.
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
            // The verdict that used to be silence. "Why wasn't my shader patched" has one of
            // three answers — patched, refused, or didn't need it — and the report should give
            // whichever applies rather than leaving the third to be mistaken for a miss.
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

        /// <summary>
        /// Internal rather than private so AvatarAdvisor can count the shaders this pass would
        /// act on without running it. Same question, same answer — an advisor with its own
        /// notion of "supports stereo" would recommend the box and then patch nothing.
        /// </summary>
        internal static string SourcePathOf(Shader shader)
        {
            string path = AssetDatabase.GetAssetPath(shader);
            return !string.IsNullOrEmpty(path) && path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)
                   && File.Exists(path) ? path : null;
        }

        internal static bool DeclaresStereo(string path)
        {
            // The WHOLE unit, not the one file — through the same ReadUnit the patcher itself
            // uses, so the question "does this shader handle stereo" and the files that get
            // patched can never disagree. Judging the .shader alone called lilToon broken:
            // lts_fur.shader is a thousand lines of pass declarations whose every line of real
            // code — including all five stereo macros, wrapped as LIL_* — lives in Includes/.
            // The advisor then recommended patching the most widely used avatar shader there
            // is, and the patcher wasted a compile discovering the vertex stage it wanted to
            // edit was not in the file it was reading.
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

        /// <summary>
        /// One file of a shader's source: the .shader itself, or a .cginc it pulls in.
        ///
        /// A shader's vertex stage is often not in the .shader at all — Cancerspace declares
        /// "#pragma vertex vert" and keeps vert, and the structs, in Cancercore.cginc. Editing
        /// somebody's shared include in place would reach every other shader using it, so the
        /// includes are cloned alongside the shader and the copies are what get edited. The
        /// clone's #include lines are repointed at the clones, so the original files are never
        /// touched and never read by the patched copy.
        /// </summary>
        class SourceFile
        {
            public string OriginalPath;   // as on disk
            public string IncludedAs;     // exactly as written in the #include, or null for the shader
            public string OutputName;     // file name inside RehomedAssets
            public string Text;
            public bool Crlf;
        }

        /// <summary>
        /// The shader plus every local include it reaches, depth first. Includes that don't
        /// resolve next to their includer are Unity's own (UnityCG.cginc and friends) and are
        /// left alone — they already handle stereo, and they are not ours to copy.
        /// </summary>
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

        /// <summary>Finds the first file in the unit matching a pattern, or null.</summary>
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

        /// <summary>
        /// Writes a patched copy, or returns null with the reason it was refused.
        /// </summary>
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

            // Taken before a single edit, so the fingerprint identifies the file as the user has
            // it — not as we are about to leave it.
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
            // A GrabPass is refused on purpose, and early. Under single-pass instanced the
            // grabbed screen is a texture ARRAY with one slice per eye, so every sampler2D /
            // tex2D read of it takes the wrong slice — adding the four macros would produce a
            // shader that compiles, passes the CCK's check, and still shows one eye the other
            // eye's view. The correct rewrite (UNITY_DECLARE/SAMPLE_SCREENSPACE_TEXTURE) has to
            // be verified against a real HLSL compile, and an unverified guess here is worse
            // than an honest refusal: it was tried, and produced a copy Unity rejected.
            // A GrabPass is patched like anything else — the four macros make the effect DRAW in
            // both eyes, which is the bigger half of the problem — but its screen grab cannot be
            // made eye-correct here, and the report has to say so.
            //
            // Learned the hard way: rewriting the grab reads to the screen-space macros compiles
            // and renders GREY in VR, because those declare a per-eye Texture2DArray and a
            // GrabPass under single-pass instanced does not produce one. Desktop looked perfect
            // throughout, since it takes the plain-sampler branch. Left alone, the grab returns
            // one eye's view shown to both — imperfect parallax on the refraction, but visible
            // and stable, which beats grey.
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

            // 1 & 2 — the struct members, each in whichever file declares it.
            inFile.Text = Regex.Replace(inFile.Text, $@"(struct\s+{Regex.Escape(inType)}\s*\{{)",
                "$1\n\t\t\t\tUNITY_VERTEX_INPUT_INSTANCE_ID");
            v2fFile.Text = Regex.Replace(v2fFile.Text, $@"(struct\s+{Regex.Escape(v2fType)}\s*\{{)",
                "$1\n\t\t\t\tUNITY_VERTEX_OUTPUT_STEREO");

            // 3 & 4 — located AFTER the struct edits, and that order is load-bearing.
            //
            // These are inserted by INDEX, and in a self-contained shader the structs live in
            // this same file — so the two edits above push everything below them along by their
            // own length. An index taken before them lands ~68 characters early, which put the
            // macros INSIDE the identifier "vdir" and produced a shader whose only complaint was
            // "undeclared identifier 'r'". (The old code replaced a matched substring, which is
            // position-independent; switching to an index made the ordering matter and nothing
            // said so.) Everything below therefore reads the text as it now stands.
            //
            // The output struct is found ANYWHERE in the function body, not just as its first
            // statement: authors routinely compute normals, tangents and view directions before
            // declaring the output ("Burning Glasses" declares four float3s first), and an
            // initialiser is just as common ("v2f o = (v2f)0;"). The body is delimited by brace
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

            // 5 — the eye index in the fragment stage, wherever frag lives.
            var fragFile = FindIn(unit,
                $@"\w+\s+{Regex.Escape(fragName)}\s*\(\s*{Regex.Escape(v2fType)}\s+(\w+)\s*\)[^{{]*\{{",
                out var fragSig);
            if (fragFile != null)
            {
                string fragArg = fragSig.Groups[1].Value;
                fragFile.Text = fragFile.Text.Replace(fragSig.Value, fragSig.Value +
                    $"\n\t\t\t\tUNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX({fragArg});");
            }

            // 6 — screen-space depth, which the CCK's four-macro test does not cover. Under
            // single-pass instanced _CameraDepthTexture is an array, so a sampler2D read of it is
            // wrong however many macros are present. Common in soft-particle and effect shaders,
            // and it can be in any file of the unit.
            foreach (var file in unit)
            {
                file.Text = Regex.Replace(file.Text, @"sampler2D\s+_CameraDepthTexture\s*;",
                    "UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);");
                file.Text = Regex.Replace(file.Text,
                    @"tex2Dproj\s*\(\s*_CameraDepthTexture\s*,\s*(UNITY_PROJ_COORD\([^)]*\))\s*\)\s*\.\s*r",
                    "SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, $1)");
            }

            // 7 — the hand-written recipe for this exact file, if one exists. Applied last, so it
            // edits a shader that already carries the generic macros and only has to describe
            // what the generic pass cannot derive.
            if (recipe != null)
            {
                if (!ShaderFixRecipes.TryApply(recipe, shaderFile.Text, out string patched, out string failure))
                {
                    // Every anchor was present in the ORIGINAL file, so this means our own
                    // generic edits moved one. Refusing keeps the promise that a recipe applies
                    // whole or not at all.
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

            // Includes first, shader last, and a refresh in between.
            //
            // Unity tracks .cginc files as ShaderInclude assets, and importing the shader is what
            // compiles it. Import the shader before its includes exist in the AssetDatabase and
            // the compile fails on an include it cannot open — reported under the name in the
            // source, which reads as though the repointing never happened. It had; only the
            // ordering was wrong.
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
                // The message ALONE is not enough to fix anything — "undeclared identifier 'r'"
                // sent three rounds of guesswork chasing the wrong edit. The line number says
                // which edit, and the platform says which #if branch of the macros was taken.
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
                // Every file of the unit goes, not just the shader — a failed patch must not
                // leave orphaned include copies sitting in the output folder.
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

        /// <summary>
        /// A copy of the material pointing at the patched shader, so other avatars sharing the
        /// original material are unaffected. Cloned once and reused for every slot that had the
        /// same material, which is what keeps those slots batching together.
        /// </summary>
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
