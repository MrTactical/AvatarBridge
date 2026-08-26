// Puts the YAPS deform into an avatar's own body shader.
//
// Inspired by VRCFury's SPS, which invented this technique for VRChat.
// The deform injected here is clean room: no SPS code is read or
// emitted. See docs/YAPS-CLEAN-ROOM.md.
//
// Clones the source unit, adds the properties, edits the vertex stage of
// every pass, compiles, and only then repoints the material. Every pass,
// or a bent mesh casts a straight shadow. Includes are inlined so the
// copy survives in a bundle. Struct members are found by semantic: the
// same field is called vertex, pos and positionOS in the wild.
#if CVR_CCK_EXISTS
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

        // What the emitted code IS, rather than a number somebody has to
        // remember to change when it moves. A constant like that was here
        // and was forgotten twice in one day: a property was added to the
        // block below and every already-patched shader went on without it,
        // silently, because the name it hashes to had not moved.
        //
        // Hashing the source itself means the shader's name changes exactly
        // when its contents do, which is the only rule that cannot be
        // forgotten.
        static string EmittedVersion(string yapsSource) => Hash(yapsSource + PropertyBlock);

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
        _YAPS_BakeGirth (""YAPS bake girth"", Float) = 1
        _YAPS_FrameFromVertex (""YAPS frame from vertex"", Range(0,1)) = 0
        _YAPS_SocketPos (""YAPS socket position"", Vector) = (0,0,0,0)
        _YAPS_SocketForward (""YAPS socket forward"", Vector) = (0,0,0,0)
        _YAPS_SocketUp (""YAPS socket up"", Vector) = (0,0,0,0)
        _YAPS_SocketFlags (""YAPS socket flags"", Vector) = (0,0,0,0)
        _YAPS_SocketFront (""YAPS socket front"", Vector) = (0,0,0,0)
        _YAPS_ChannelSpace (""YAPS channel space"", Range(0,1)) = 0
        _YAPS_ChannelExtents (""YAPS channel extents"", Vector) = (1,1,1,0)
        _YAPS_SelfTag (""YAPS self tag"", Float) = -1
        [Enum(Off,0,Resolved by,1,Gap to socket,2,Engagement,3)] _YAPS_Debug (""YAPS view"", Float) = 0
        _YAPS_TaperStart (""YAPS hole taper start"", Range(0,1)) = 0.10
        _YAPS_TaperEnd (""YAPS hole taper end"", Range(0,1)) = 0.30
        _YAPS_IdleLength (""YAPS idle length"", Range(0.1,1)) = 1
        _YAPS_IdleWidth (""YAPS idle width"", Range(0.1,1)) = 1
        _YAPS_Squeeze (""YAPS squeeze"", Range(0,1)) = 0
        _YAPS_SqueezeDistance (""YAPS squeeze reach"", Range(0.01,1)) = 0.15
        _YAPS_Bulge (""YAPS bulge"", Range(0,1)) = 0
        _YAPS_BulgeDistance (""YAPS bulge reach"", Range(0.01,1)) = 0.2
        _YAPS_PumpStrength (""YAPS pumping"", Range(0,0.5)) = 0
        _YAPS_PumpSpeed (""YAPS pumping speed"", Range(0,20)) = 6
        _YAPS_PumpWidth (""YAPS pumping width"", Range(0.05,1)) = 1
        _YAPS_WriggleStrength (""YAPS wriggle"", Range(0,0.5)) = 0
        _YAPS_WriggleSpeed (""YAPS wriggle speed"", Range(0,20)) = 2
        [Header(YAPS shape at rest)]
        _YAPS_Curvature (""YAPS curvature"", Range(-1.5,1.5)) = 0
        _YAPS_ReCurvature (""YAPS recurvature"", Range(-1.5,1.5)) = 0
        _YAPS_EntranceStiffness (""YAPS entrance stiffness"", Range(0,1)) = 0
        [Header(YAPS curve)]
        _YAPS_BezierSmoothness (""YAPS bezier smoothness"", Range(0.2,3)) = 1
        _YAPS_BezierStart (""YAPS straight before bend"", Range(0,0.8)) = 0
        _YAPS_SmoothStart (""YAPS ease into bend"", Range(0,0.5)) = 0
        _YAPS_MinimumSocketDistance (""YAPS minimum socket distance"", Range(0,1)) = 0
        [Header(YAPS tags)]
        _YAPS_TagInclude (""YAPS only sockets tagged"", Float) = 0
        _YAPS_TagExclude (""YAPS never sockets tagged"", Float) = 0
        _YAPS_ShapeCount (""YAPS shape count"", Float) = 0
        _YAPS_ShapeWeights (""YAPS shape weights 0-3"", Vector) = (0,0,0,0)
        _YAPS_ShapeWeights2 (""YAPS shape weights 4-7"", Vector) = (0,0,0,0)
        _YAPS_ShapeWeights3 (""YAPS shape weights 8-11"", Vector) = (0,0,0,0)
        _YAPS_ShapeWeights4 (""YAPS shape weights 12-15"", Vector) = (0,0,0,0)
        [Header(YAPS socket)]
        _YAPS_SocketPower (""YAPS socket power"", Range(0,1)) = 0
        _YAPS_SocketDepth (""YAPS socket depth (-1 none)"", Range(-1,1)) = -1
        _YAPS_SocketOrigin (""YAPS socket origin, mesh local"", Vector) = (0,0,0,0)
        _YAPS_SocketNoSelfExclude (""YAPS socket takes the wearer's own plug"", Range(0,1)) = 0
        _YAPS_SocketShapeStart (""YAPS socket shape starts 0-3"", Vector) = (0, 0.25, 0.5, 0.75)
        _YAPS_SocketShapeStart2 (""YAPS socket shape starts 4-7"", Vector) = (0, 0, 0, 0)
        _YAPS_SocketShapeStart3 (""YAPS socket shape starts 8-11"", Vector) = (0, 0, 0, 0)
        _YAPS_SocketShapeStart4 (""YAPS socket shape starts 12-15"", Vector) = (0, 0, 0, 0)
        _YAPS_SocketShapeFade (""YAPS socket shape fades 0-3"", Vector) = (0.3, 0.3, 0.3, 0.3)
        _YAPS_SocketShapeFade2 (""YAPS socket shape fades 4-7"", Vector) = (0.3, 0.3, 0.3, 0.3)
        _YAPS_SocketShapeFade3 (""YAPS socket shape fades 8-11"", Vector) = (0.3, 0.3, 0.3, 0.3)
        _YAPS_SocketShapeFade4 (""YAPS socket shape fades 12-15"", Vector) = (0.3, 0.3, 0.3, 0.3)
";

        // The name the patch of `original` would carry if it were made now.
        // Null when the question cannot be answered, which callers must read
        // as "leave it alone" rather than "it is stale".
        //
        // This is what lets a rebuild notice that the tool has moved on
        // since a material was patched. Without it a shader fix reaches
        // nobody who already converted: their bake refreshes, their shader
        // does not, and the two disagree in silence.
        public static string CurrentNameFor(Material patchedOrOriginal)
        {
            if (patchedOrOriginal == null || patchedOrOriginal.shader == null) return null;
            // A patched material names its own source; an unpatched one is
            // its own source.
            var from = OriginalShaderOf(patchedOrOriginal) ?? patchedOrOriginal.shader;
            string sourcePath = ShaderSpiPatcher.SourcePathOf(from);
            if (string.IsNullOrEmpty(sourcePath)) return null;
            var unit = ShaderSpiPatcher.ReadUnit(sourcePath);
            if (unit.Count == 0) return null;
            string yaps = LoadYapsSource(out _);
            if (yaps == null) return null;
            return PatchedName(unit[0].Text, Hash(sourcePath + EmittedVersion(yaps) + unit.Count));
        }

        // Whether a patched material is carrying code this version no
        // longer emits. False whenever it cannot be told.
        //
        // The original is recovered from the patch itself rather than from
        // the component: a CONVERTED avatar adopts its components and never
        // records what it was baked from, which is most avatars, and a check
        // that needs that field would quietly do nothing for all of them.
        // The patch carries its source shader's name in a hidden property's
        // description, which is the one place a string can ride.
        public static bool IsStale(Material patched)
        {
            return patched != null && patched.shader != null
                   && CurrentNameFor(patched) is string want && patched.shader.name != want;
        }

        // The shader a patched material was made from, or null.
        public static Shader OriginalShaderOf(Material patched)
        {
            string name = SourceShaderOf(patched);
            return string.IsNullOrEmpty(name) ? null : Shader.Find(name);
        }

        // Re-patch a material that has fallen behind, in place. Its values
        // survive the swap and a property the old code never had arrives at
        // the default its block declares.
        public static bool Refresh(Material patched, string outputDir, BridgeReport report)
        {
            var original = OriginalShaderOf(patched);
            if (original == null) return false;
            var stand = new Material(original) { name = patched.name };
            try
            {
                var current = Patch(stand, outputDir, report, out _, out _, allowSps: true);
                if (current == null) return false;
                patched.shader = current;
                return true;
            }
            finally { UnityEngine.Object.DestroyImmediate(stand); }
        }

        // What the patched shader is called, and it is not cosmetic.
        //
        // Poiyomi strips a shader from the BUILD when it carries Thry's two
        // marker properties and is not named "Hidden/Locked/...", reading
        // that as an unlocked uber-shader. Our patch of a Poiyomi material
        // inherits both markers, so under any other name it is excluded from
        // the bundle and the avatar uploads PINK — correct in the editor,
        // missing in game, which is the worst failure this tool can produce.
        //
        // A patch of a locked Poiyomi IS locked: its features are already
        // resolved to constants, and only the deform was added. So the name
        // is honest as well as necessary. Shaders with no Thry markers keep
        // the plain name; there is nothing to strip them.
        //
        // The name does a second job, found by reading Thry's own sweep
        // (ShaderOptimizer.SetLockedForAllMaterials). Auto-lock-on-upload
        // takes every material whose shader uses the optimizer and is not
        // already locked, and its test for "already locked" is this exact
        // prefix. Under any other name our patch is swept into a lock, which
        // resolves properties to constants — and OUR properties are the ones
        // animation drives, so the deform would freeze at whatever the
        // material happened to hold. The prefix keeps the sweep off it.
        //
        // Hence BOTH markers, not just the editor's: the sweep keys on
        // ThryShaderOptimizerLockButton, and a shader carrying that without
        // the editor marker would have been named plainly and swept.
        static string PatchedName(string sourceText, string hash)
        {
            bool thry = sourceText != null
                        && (sourceText.Contains("shader_is_using_thry_editor")
                            || sourceText.Contains("ThryShaderOptimizerLockButton"));
            return (thry ? "Hidden/Locked/YAPS/" : "Hidden/YAPS/") + hash;
        }

        public static Shader Patch(Material material, string outputDir, BridgeReport report,
            out string refusal, out int skippedShadowPasses, bool allowSps = false)
        {
            refusal = null;
            skippedShadowPasses = 0;
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
            if (shaderFile.Text.Contains("_SPS_Bake") && !allowSps)
            {
                // Two deform systems moving the same vertices would fight,
                // and the result would be neither. The converter suppresses
                // SPS at bake time so the plug's material arrives carrying
                // its plain shader; a shader that still has SPS in it means
                // that step did not happen.
                refusal = "it already carries VRChat's SPS, and two deform systems on the same " +
                          "vertices would fight — suppress SPS at bake time so the plain shader " +
                          "comes through instead";
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

            int patchedPasses = PatchProgramBlocks(unit, yaps, out string blockRefusal,
                out skippedShadowPasses);
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

            string hash = Hash(sourcePath + EmittedVersion(yaps) + unit.Count);
            string newName = PatchedName(shaderFile.Text, hash);
            shaderFile.Text = Regex.Replace(shaderFile.Text, @"Shader\s+""[^""]+""",
                "Shader \"" + newName + "\"");

            InstallShaderGui(shaderFile, material.shader.name);

            var patched = WriteAndVerify(unit, sourcePath, outputDir, hash, out string compileError);
            if (patched == null)
            {
                refusal = "the patched shader did not compile — " + compileError;
                return null;
            }

            report?.Converted(Category, material.name,
                $"Deform patched into \"{material.shader.name}\" across {patchedPasses} pass(es). " +
                "The original shader is untouched; a copy carrying YAPS was written beside the " +
                "converted avatar and the material repointed at it."
                + (skippedShadowPasses > 0
                    ? $" {skippedShadowPasses} shadow pass(es) use Unity's own shadow-caster " +
                      "function, which lives in Unity's includes rather than in this shader, so " +
                      "there is nothing of the avatar's to edit there — the shadow will not " +
                      "follow the bend. Cosmetic, and the alternative was no deform at all."
                    : ""));
            return patched;
        }

        // --- the material panel -----------------------------------------

        // YapsShaderGUI on top, the shader's own editor underneath. Its
        // class name and the source shader's name ride in the description
        // of a hidden property: the only place a string can travel.
        public const string SourceShaderProperty = "_YAPS_SourceShader";

        public static string SourceShaderOf(Material material)
        {
            if (material == null || material.shader == null) return null;
            int index = material.shader.FindPropertyIndex(SourceShaderProperty);
            if (index < 0) return null;
            string name = material.shader.GetPropertyDescription(index);
            return string.IsNullOrEmpty(name) ? null : name;
        }

        static void InstallShaderGui(ShaderSpiPatcher.SourceFile shaderFile, string sourceShaderName)
        {
            string original = null;
            var existing = Regex.Match(shaderFile.Text, @"^\s*CustomEditor\s+""([^""]+)""\s*$",
                RegexOptions.Multiline);
            if (existing.Success)
            {
                original = existing.Groups[1].Value;
                shaderFile.Text = shaderFile.Text.Remove(existing.Index, existing.Length);
            }

            // The marker properties, into the Properties block beside ours.
            var properties = Regex.Match(shaderFile.Text, @"Properties\s*\{");
            if (properties.Success)
            {
                string marker = "\n        [HideInInspector] " + YapsShaderGUI.OriginalEditorProperty
                                + " (\"" + (original ?? "") + "\", Float) = 0"
                                + "\n        [HideInInspector] " + SourceShaderProperty
                                + " (\"" + (sourceShaderName ?? "").Replace("\"", "") + "\", Float) = 0";
                shaderFile.Text = shaderFile.Text.Insert(properties.Index + properties.Length, marker);
            }

            // CustomEditor sits at shader scope, after the SubShaders. The
            // last closing brace of the file is the shader's own.
            int close = shaderFile.Text.LastIndexOf('}');
            if (close > 0)
            {
                shaderFile.Text = shaderFile.Text.Insert(close,
                    "    CustomEditor \"AvatarBridge.YapsShaderGUI\"\n");
            }
        }

        // --- the vertex wrapper ---------------------------------------

        // The vertex function is edited in place, not wrapped. Poiyomi
        // declares its signature across a preprocessor conditional, which
        // no wrapper can reproduce. The parameter is a local copy, so
        // deforming it at the top is the same thing.
        static int PatchProgramBlocks(List<ShaderSpiPatcher.SourceFile> unit, string yaps,
            out string refusal, out int skippedShadowPasses)
        {
            refusal = null;
            skippedShadowPasses = 0;
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
                    // A shadow caster is allowed to be out of reach. Unity's
                    // own vertShadowCaster lives in its CGIncludes, not in
                    // the shader's source unit, so there is nothing of ours
                    // to edit. Losing the whole deform to avoid a shadow
                    // that does not follow the bend is the wrong trade, the
                    // shadow is cosmetic, the deform is the feature.
                    if (IsShadowCasterPass(shaderFile.Text, block.Index))
                    {
                        skippedShadowPasses++;
                        continue;
                    }
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
            //, patching it once per pass, at offsets computed before the
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
            // emitting `endif.vertex.xyz`, which the compiler reported, with
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
            // BOTH ends, unconditionally, and this is deliberate.
            //
            // Each guards itself on its own enable, a plug has
            // _YAPS_Enabled and a socket has _YAPS_SocketPower, and both
            // default to a value that returns immediately. So a material is
            // whichever end its properties say it is, decided at conversion
            // time by what the converter sets rather than baked into which
            // variant of the patcher happened to run.
            //
            // The alternative was a socket-mode patcher, which means two
            // emitted bodies to keep in step. Every expensive bug on this
            // feature has come from two things that had to agree and
            // quietly stopped agreeing.
            body.AppendLine($"    YapsDeform(yapsPosition, yapsNormal, yapsTangent, {idExpression});");
            body.AppendLine(
                $"    YapsSocketDeform(yapsPosition, yapsNormal, yapsTangent, {idExpression});");
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
            List<ShaderSpiPatcher.SourceFile> unit, string structName, int depth = 0)
        {
            // The optional ": Base" matters. A struct can inherit, and then
            // POSITION lives in the parent rather than here, reading only
            // the body reports a vertex input with no vertex in it.
            var file = ShaderSpiPatcher.FindIn(unit,
                $@"struct\s+{Regex.Escape(structName)}\s*(?::\s*(\w+)\s*)?\{{",
                out var declaration);
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

            string baseName = declaration.Groups[1].Success ? declaration.Groups[1].Value : null;
            if (!string.IsNullOrEmpty(baseName) && !string.Equals(baseName, structName,
                    StringComparison.Ordinal) && depth < 4)
            {
                var inherited = ReadStructMembers(unit, baseName, depth + 1);
                if (inherited != null)
                {
                    foreach (var pair in inherited)
                    {
                        if (!members.ContainsKey(pair.Key))
                        {
                            members[pair.Key] = pair.Value;
                        }
                    }
                }
            }
            return members;
        }

        static bool IsShadowCasterPass(string text, int programIndex)
            => PassTagIs(text, programIndex, "ShadowCaster");

        static bool LooksLikeMetaPass(string text, int programIndex)
            => PassTagIs(text, programIndex, "Meta");

        // Read this pass's LightMode tag.
        //
        // Scoped by the END of the previous program block rather than by
        // looking back for the word "Pass". A flattened shader has its
        // includes inlined, and names like CGI_PoiPassShadow contain "Pass"
        //, so the backward search landed inside the previous pass's code
        // and read the wrong tag, or none. Whatever sits between one
        // ENDCG and the next CGPROGRAM is exactly this pass's declaration.
        static bool PassTagIs(string text, int programIndex, string lightMode)
        {
            int from = 0;
            foreach (Match end in Regex.Matches(text, @"\b(ENDCG|ENDHLSL)\b"))
            {
                if (end.Index >= programIndex)
                {
                    break;
                }
                from = end.Index + end.Length;
            }

            string head = text.Substring(from, programIndex - from);
            return Regex.IsMatch(head, $@"""LightMode""\s*=\s*""{Regex.Escape(lightMode)}""",
                RegexOptions.IgnoreCase);
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
            // ability to reorder anything.
            //
            // The socket include comes along on every patch, plug or
            // socket. It costs a few unused functions in a shader that will
            // not call them, and it means one emitted body serves both ends
            //, against keeping two divergent inline lists in step, which
            // is the drift that has cost the most time on this feature.
            string[] names =
            {
                "yaps_props.cginc", "yaps_resolve.cginc", "yaps_deform.cginc", "yaps_socket.cginc",
            };
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

            // The hash names the file as well as the shader. Two locked
            // Poiyomi materials both come from a "Poiyomi Toon.shader" of
            // their own, and one output name would have the second write
            // over the first.
            var shaderFile = unit[0];
            string tag = "_YAPS_" + hash.Substring(0, Math.Min(8, hash.Length));
            shaderFile.OutputName = Path.GetFileNameWithoutExtension(sourcePath) + tag + ".shader";

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { shaderFile.OutputName };
            foreach (var file in unit.Skip(1))
            {
                string stem = Path.GetFileNameWithoutExtension(file.OriginalPath) + tag;
                string extension = Path.GetExtension(file.OriginalPath);
                string name = stem + extension;
                for (int n = 2; !used.Add(name); n++)
                {
                    name = stem + "_" + n + extension;
                }
                file.OutputName = name;
            }

            // Resolve each include against the file it appears in, not
            // against the spelling it was first seen under, the same file
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
                    string candidate = Path.Combine(folder,
                        m.Groups[1].Value.TrimStart('/', '\\'));
                    if (File.Exists(candidate)
                        && byPath.TryGetValue(Path.GetFullPath(candidate), out var target)
                        && target != file)
                    {
                        return $"#include \"{target.OutputName}\"";
                    }
                    return m.Value;
                });
                // One line ending throughout, whichever the source used:
                // the inlined YAPS source may not match, and Unity warns
                // about a file that mixes them on every import.
                file.Text = file.Text.Replace("\r\n", "\n");
                if (file.Crlf)
                {
                    file.Text = file.Text.Replace("\n", "\r\n");
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
