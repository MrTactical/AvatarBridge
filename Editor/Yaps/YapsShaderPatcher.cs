// Phase 1b. Puts the YAPS deform into an avatar's own body shader.
//
// Inspired by VRCFury's SPS, which invented this technique for VRChat.
// This patches the plug material's CURRENT shader — the same class of
// input their patcher takes — and injects our own clean-room deform. No
// SPS code is read or emitted. See Tools/SpsSpike/LICENSE-POSTURE.md.
//
// ---------------------------------------------------------------------
// WHAT IT DOES TO A SHADER
// ---------------------------------------------------------------------
//
//   1. Clones the whole source unit — the .shader and every .cginc it
//      pulls in — into the conversion output. Originals are never edited.
//   2. Adds the YAPS properties to Properties{}, so the material and the
//      animator have something to write to.
//   3. In EVERY pass that has a vertex stage, inlines the YAPS includes
//      and wraps the vertex function: the wrapper deforms position,
//      normal and tangent, then calls the original.
//   4. Compiles the result and only then repoints the material. A shader
//      that fails to compile is deleted and the material left alone.
//
// Every pass, not just the forward one, because a deform that ran in some
// passes and not others would cast an undeformed shadow through a bent
// mesh. The includes are INLINED rather than #included, so the patched
// shader is self-contained and survives being built into an avatar bundle
// with no dependency on where the package sits.
//
// The wrapper finds struct members by SEMANTIC, never by name. Shaders
// call the same field `vertex`, `pos`, `positionOS` and worse, but they
// all tag it POSITION.
#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsShaderPatcher
    {
        const string Category = "YAPS";

        // Bumped when the emitted code changes, so a stale cached patch is
        // never reused for new code.
        const string Revision = "1";

        // What Properties{} needs. Distinct from the HLSL declarations in
        // yaps_props.cginc: Unity needs its own syntax here, and only
        // properties that must be animatable or inspectable belong.
        const string PropertyBlock = @"
        [Header(YAPS)]
        _YAPS_Bake (""YAPS baked data"", 2D) = ""black"" {}
        _YAPS_VertexCount (""YAPS vertex count"", Float) = 0
        _YAPS_Enabled (""YAPS enabled"", Range(0,1)) = 1
        _YAPS_Length (""YAPS plug length"", Float) = 1
        _YAPS_Overrun (""YAPS allow overrun"", Range(0,1)) = 1
        _YAPS_BakeScale (""YAPS bake scale"", Float) = 1
        _YAPS_FrameFromVertex (""YAPS frame from vertex"", Range(0,1)) = 0
        _YAPS_SocketPos (""YAPS socket position"", Vector) = (0,0,0,0)
        _YAPS_SocketForward (""YAPS socket forward"", Vector) = (0,0,0,0)
        _YAPS_SocketUp (""YAPS socket up"", Vector) = (0,0,0,0)
        _YAPS_SocketFlags (""YAPS socket flags"", Vector) = (0,0,0,0)
";

        public static Shader Patch(Material material, string outputDir, BridgeReport report,
            out string refusal)
        {
            refusal = null;
            if (material == null || material.shader == null)
            {
                refusal = "the material has no shader";
                return null;
            }

            string sourcePath = ShaderSpiPatcher.SourcePathOf(material.shader);
            if (string.IsNullOrEmpty(sourcePath))
            {
                refusal = $"\"{material.shader.name}\" has no source file on disk, so there is " +
                          "nothing to patch — it is one of Unity's built-ins or lives inside a " +
                          "compiled package";
                return null;
            }

            var unit = ShaderSpiPatcher.ReadUnit(sourcePath);
            if (unit.Count == 0)
            {
                refusal = "the shader source could not be read";
                return null;
            }

            var shaderFile = unit[0];
            if (shaderFile.Text.Contains("_YAPS_Bake"))
            {
                refusal = "it already carries YAPS";
                return null;
            }
            if (Regex.IsMatch(shaderFile.Text, @"#pragma\s+surface"))
            {
                refusal = "it is a surface shader, so Unity generates the vertex stage and " +
                          "there is no vertex function of its own to wrap";
                return null;
            }

            string yaps = LoadYapsSource(out string loadFailure);
            if (yaps == null)
            {
                refusal = loadFailure;
                return null;
            }

            // Properties first: the block is in the shader file, and later
            // edits work on offsets that this would otherwise shift.
            var properties = Regex.Match(shaderFile.Text, @"Properties\s*\{");
            if (!properties.Success)
            {
                refusal = "it has no Properties block to add the YAPS properties to";
                return null;
            }
            shaderFile.Text = shaderFile.Text.Insert(properties.Index + properties.Length,
                PropertyBlock);

            int patchedPasses = PatchProgramBlocks(unit, yaps, out string blockRefusal);
            if (patchedPasses == 0)
            {
                refusal = blockRefusal ?? "no pass in it declares a vertex stage";
                return null;
            }
            if (blockRefusal != null)
            {
                // Half a patched shader is worse than none: the passes that
                // took the deform would disagree with the passes that did
                // not, and the mesh would be bent in some and straight in
                // others.
                refusal = blockRefusal;
                return null;
            }

            string hash = Hash(sourcePath + Revision + unit.Count);
            string newName = "Hidden/YAPS/" + hash;
            shaderFile.Text = Regex.Replace(shaderFile.Text, @"Shader\s+""[^""]+""",
                "Shader \"" + newName + "\"");

            var patched = WriteAndVerify(unit, sourcePath, outputDir, hash, out string compileError);
            if (patched == null)
            {
                refusal = "the patched shader did not compile — " + compileError;
                return null;
            }

            report?.Converted(Category, material.name,
                $"Deform patched into \"{material.shader.name}\" across {patchedPasses} pass(es). " +
                "The original shader is untouched; a copy carrying YAPS was written beside the " +
                "converted avatar and the material repointed at it.");
            return patched;
        }

        // --- the vertex wrapper ---------------------------------------

        // Modify the vertex function IN PLACE rather than wrapping it.
        //
        // The first attempt built a wrapper and renamed the pragma, which
        // needed the signature to be one simple parameter. Poiyomi — by a
        // distance the most common avatar shader — declares its vertex
        // stage across five lines with a preprocessor conditional choosing
        // between two input types:
        //
        //     VertexOut vert(
        //     #ifndef POI_TESSELLATED
        //     appdata v
        //     #else
        //     tessAppData v
        //     #endif
        //     )
        //
        // No wrapper can reproduce that signature without reimplementing
        // the preprocessor. Editing the body sidesteps the whole problem:
        // the parameter is a local copy in HLSL, so deforming it at the top
        // of the function is exactly equivalent to deforming it on the way
        // in, and the pragma never has to change.
        static int PatchProgramBlocks(List<ShaderSpiPatcher.SourceFile> unit, string yaps,
            out string refusal)
        {
            refusal = null;
            var shaderFile = unit[0];
            int patched = 0;
            var alreadyInjected = new HashSet<string>(StringComparer.Ordinal);

            var blocks = Regex.Matches(shaderFile.Text, @"(CGPROGRAM|HLSLPROGRAM)")
                .Cast<Match>().Reverse().ToList();

            foreach (var block in blocks)
            {
                int start = block.Index + block.Length;
                var end = Regex.Match(shaderFile.Text.Substring(start), @"(ENDCG|ENDHLSL)");
                if (!end.Success)
                {
                    continue;
                }

                var vertPragma = Regex.Match(
                    shaderFile.Text.Substring(start, end.Index), @"#pragma\s+vertex\s+(\w+)");
                if (!vertPragma.Success)
                {
                    continue;   // fragment-only, or a pass we need not touch
                }

                // Lightmap baking runs its own geometry and never shows the
                // player anything; deforming there is pointless and can
                // upset the bake.
                if (LooksLikeMetaPass(shaderFile.Text, block.Index))
                {
                    continue;
                }

                string vertName = vertPragma.Groups[1].Value;
                if (!PatchOneVertexFunction(unit, yaps, vertName,
                        start, start + end.Index, alreadyInjected, out string why))
                {
                    refusal = why;
                    return patched;
                }
                patched++;
            }

            return patched;
        }

        static bool PatchOneVertexFunction(List<ShaderSpiPatcher.SourceFile> unit, string yaps,
            string vertName, int blockStart, int blockEnd, HashSet<string> alreadyInjected,
            out string refusal)
        {
            refusal = null;
            var pattern = new Regex($@"(\w+)\s+{Regex.Escape(vertName)}\s*\(");

            // Look inside THIS program block first. A flattened shader
            // repeats its whole vertex function per pass, and searching the
            // file from the start would hand every pass the same first copy
            // — patching it once per pass, at offsets computed before the
            // previous edits, which shreds the preprocessor and reports as
            // an undeclared 'endif'.
            ShaderSpiPatcher.SourceFile file = unit[0];
            Match head = Match.Empty;
            if (blockStart >= 0 && blockEnd <= file.Text.Length)
            {
                var inBlock = pattern.Match(file.Text, blockStart, blockEnd - blockStart);
                if (inBlock.Success)
                {
                    head = inBlock;
                }
            }

            if (!head.Success)
            {
                // Not in the block, so it lives in an include. Those are
                // textually shared by every pass that includes them, so
                // patch such a function exactly once.
                file = ShaderSpiPatcher.FindIn(unit.Skip(1).ToList(), pattern.ToString(), out head);
                if (file == null)
                {
                    refusal = $"the vertex function \"{vertName}\" could not be found in the " +
                              "shader or its includes";
                    return false;
                }
                if (!alreadyInjected.Add(file.OriginalPath + "::" + vertName))
                {
                    return true;   // already done for another pass
                }
            }

            int parenOpen = file.Text.IndexOf('(', head.Index);
            int parenClose = MatchBracket(file.Text, parenOpen, '(', ')');
            if (parenClose < 0)
            {
                refusal = $"the parameter list of \"{vertName}\" is not closed";
                return false;
            }

            int braceOpen = file.Text.IndexOf('{', parenClose);
            if (braceOpen < 0)
            {
                refusal = $"\"{vertName}\" looks like a declaration without a body";
                return false;
            }

            string parameters = file.Text.Substring(parenOpen + 1, parenClose - parenOpen - 1);

            // Strip preprocessor lines before reading identifiers. Poiyomi's
            // parameter list ENDS with "#endif", and taking the last
            // identifier from the raw text duly named the parameter "endif",
            // emitting `endif.vertex.xyz` — which the compiler reported, with
            // some justification, as an undeclared identifier 'endif'.
            string cleaned = Regex.Replace(parameters, @"^[ \t]*#.*$", "", RegexOptions.Multiline);

            var identifiers = Regex.Matches(cleaned, @"[A-Za-z_]\w*")
                .Cast<Match>().Select(m => m.Value).ToList();
            if (identifiers.Count < 2)
            {
                refusal = $"\"{vertName}\" takes no vertex input this patcher can recognise";
                return false;
            }

            // The parameter's own name is the last identifier; everything
            // before it is candidate type names, preprocessor symbols and
            // qualifiers. Try each as a struct until one carries POSITION.
            string parameterName = identifiers[identifiers.Count - 1];
            Dictionary<string, string> members = null;
            foreach (string candidate in identifiers.Take(identifiers.Count - 1).Distinct())
            {
                var found = ReadStructMembers(unit, candidate);
                if (found != null && found.ContainsKey("POSITION"))
                {
                    members = found;
                    break;
                }
            }
            if (members == null)
            {
                refusal = $"no vertex input struct with a POSITION member could be found for " +
                          $"\"{vertName}\"";
                return false;
            }

            members.TryGetValue("POSITION", out string positionField);
            members.TryGetValue("NORMAL", out string normalField);
            members.TryGetValue("TANGENT", out string tangentField);
            members.TryGetValue("SV_VERTEXID", out string vertexIdField);

            string idExpression;
            if (!string.IsNullOrEmpty(vertexIdField))
            {
                idExpression = parameterName + "." + vertexIdField;
            }
            else
            {
                refusal = $"\"{vertName}\"'s input has no SV_VertexID, which is how the bake is " +
                          "addressed";
                return false;
            }

            var body = new StringBuilder();
            body.AppendLine();
            body.AppendLine("    // --- YAPS ---");
            body.AppendLine($"    float3 yapsPosition = {parameterName}.{positionField}.xyz;");
            body.AppendLine(normalField != null
                ? $"    float3 yapsNormal = {parameterName}.{normalField}.xyz;"
                : "    float3 yapsNormal = float3(0,0,1);");
            body.AppendLine(tangentField != null
                ? $"    float3 yapsTangent = {parameterName}.{tangentField}.xyz;"
                : "    float3 yapsTangent = float3(1,0,0);");
            body.AppendLine($"    YapsDeform(yapsPosition, yapsNormal, yapsTangent, {idExpression});");
            body.AppendLine($"    {parameterName}.{positionField}.xyz = yapsPosition;");
            if (normalField != null)
            {
                body.AppendLine($"    {parameterName}.{normalField}.xyz = yapsNormal;");
            }
            if (tangentField != null)
            {
                body.AppendLine($"    {parameterName}.{tangentField}.xyz = yapsTangent;");
            }
            body.AppendLine("    // --- end YAPS ---");

            // Body first, then the includes above the function, so the
            // insertion offsets stay valid. The YAPS source goes
            // immediately before the function rather than at the top of the
            // block: by here the shader's own includes have run, so
            // UnityCG's matrices and light arrays exist.
            file.Text = file.Text.Insert(braceOpen + 1, body.ToString());
            file.Text = file.Text.Insert(head.Index, "\n" + yaps + "\n");
            return true;
        }

        static int MatchBracket(string text, int open, char opening, char closing)
        {
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == opening) depth++;
                else if (text[i] == closing && --depth == 0) return i;
            }
            return -1;
        }

        // Members keyed by SEMANTIC, because names are anybody's guess but
        // semantics are the contract.
        static Dictionary<string, string> ReadStructMembers(
            List<ShaderSpiPatcher.SourceFile> unit, string structName)
        {
            var file = ShaderSpiPatcher.FindIn(unit,
                $@"struct\s+{Regex.Escape(structName)}\s*\{{", out var declaration);
            if (file == null)
            {
                return null;
            }

            int open = declaration.Index + declaration.Length;
            int close = file.Text.IndexOf('}', open);
            if (close < 0)
            {
                return null;
            }

            var members = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string body = file.Text.Substring(open, close - open);
            foreach (Match m in Regex.Matches(body, @"\w+\s+(\w+)\s*:\s*(\w+)\s*;"))
            {
                string semantic = m.Groups[2].Value.ToUpperInvariant();
                if (!members.ContainsKey(semantic))
                {
                    members[semantic] = m.Groups[1].Value;
                }
            }
            return members;
        }

        static bool LooksLikeMetaPass(string text, int programIndex)
        {
            // Look back to the enclosing Pass for a Meta light mode.
            int passStart = text.LastIndexOf("Pass", programIndex, StringComparison.Ordinal);
            if (passStart < 0)
            {
                return false;
            }
            string head = text.Substring(passStart, programIndex - passStart);
            return Regex.IsMatch(head, @"""LightMode""\s*=\s*""Meta""", RegexOptions.IgnoreCase);
        }

        // --- the YAPS source, inlined ---------------------------------

        static string LoadYapsSource(out string failure)
        {
            failure = null;
            string folder = FindYapsFolder();
            if (folder == null)
            {
                failure = "the YAPS shader includes could not be found in the project";
                return null;
            }

            // Dependency order, since inlining removes the include guards'
            // ability to reorder anything for us.
            string[] names = { "yaps_props.cginc", "yaps_resolve.cginc", "yaps_deform.cginc" };
            var sb = new StringBuilder();
            foreach (string name in names)
            {
                string path = folder + "/" + name;
                if (!File.Exists(path))
                {
                    failure = $"\"{name}\" is missing from the YAPS includes";
                    return null;
                }
                string text = File.ReadAllText(path);
                // Strip the includes of each other: everything is being
                // concatenated, so those lines would look for files that
                // will not exist beside the patched shader.
                text = Regex.Replace(text, @"#include\s+""yaps_\w+\.cginc""\s*", "");
                sb.AppendLine(text);
            }
            return sb.ToString();
        }

        static string FindYapsFolder()
        {
            foreach (string guid in AssetDatabase.FindAssets("yaps_deform"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("yaps_deform.cginc", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetDirectoryName(path)?.Replace('\\', '/');
                }
            }
            return null;
        }

        // --- writing and verifying ------------------------------------

        static Shader WriteAndVerify(List<ShaderSpiPatcher.SourceFile> unit, string sourcePath,
            string dir, string hash, out string error)
        {
            error = null;
            Directory.CreateDirectory(dir);

            var shaderFile = unit[0];
            shaderFile.OutputName = Path.GetFileNameWithoutExtension(sourcePath) + "_YAPS.shader";

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { shaderFile.OutputName };
            foreach (var file in unit.Skip(1))
            {
                string stem = Path.GetFileNameWithoutExtension(file.OriginalPath) + "_YAPS";
                string extension = Path.GetExtension(file.OriginalPath);
                string name = stem + extension;
                for (int n = 2; !used.Add(name); n++)
                {
                    name = stem + "_" + n + extension;
                }
                file.OutputName = name;
            }

            // Resolve each include against the file it appears in, not
            // against the spelling it was first seen under — the same file
            // is often referred to two different ways.
            var byPath = new Dictionary<string, ShaderSpiPatcher.SourceFile>(StringComparer.OrdinalIgnoreCase);
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
                    if (File.Exists(candidate)
                        && byPath.TryGetValue(Path.GetFullPath(candidate), out var target)
                        && target != file)
                    {
                        return $"#include \"{target.OutputName}\"";
                    }
                    return m.Value;
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

            // Includes first: importing the shader is what compiles it, and
            // it fails on includes not yet in the AssetDatabase.
            foreach (string path in written.Skip(1))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            AssetDatabase.Refresh();
            string shaderPath = written[0];
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);

            var result = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (result == null)
            {
                error = "Unity did not import it as a shader at all";
                CleanUp(written, shaderPath);
                return null;
            }
            if (ShaderUtil.ShaderHasError(result))
            {
                var messages = ShaderUtil.GetShaderMessages(result);
                error = messages.Length > 0
                    ? string.Join("; ", messages.Take(3).Select(m => $"line {m.line}: {m.message}"))
                    : "the compiler reported an error but no message";
                // Keep the failing source next to the avatar so the error
                // can be read against real line numbers.
                File.WriteAllText(shaderPath + ".failed.txt", shaderFile.Text);
                CleanUp(written, null);
                return null;
            }
            return result;
        }

        static void CleanUp(List<string> written, string keep)
        {
            foreach (string path in written)
            {
                if (path != keep)
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }
        }

        static string Hash(string input)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(bytes, 0, 6).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
#endif
