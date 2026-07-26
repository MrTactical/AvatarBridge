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
    /// Both ChilloutVR and VRChat render VR single-pass instanced: both eyes in one pass, with the
    /// shader responsible for knowing which eye it is drawing. A shader that never opted in draws
    /// into one eye only, or at the wrong offset. The CCK reports these; this fixes the ones that
    /// can be fixed mechanically.
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
            var refused = new List<string>();

            foreach (var renderer in ctx.Target.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    var shader = material != null ? material.shader : null;
                    if (shader == null || IsKnownStereo(shader.name))
                    {
                        continue;
                    }
                    if (patched.TryGetValue(shader, out var already))
                    {
                        if (already != null) { materials[i] = Repoint(material, already, dir, clones); changed = true; }
                        continue;
                    }

                    string source = SourcePathOf(shader);
                    if (source == null || DeclaresStereo(source))
                    {
                        patched[shader] = null;
                        continue;
                    }

                    var fixedShader = TryPatch(source, shader.name, dir, out string reason);
                    patched[shader] = fixedShader;
                    if (fixedShader == null)
                    {
                        refused.Add($"{shader.name} ({reason})");
                        continue;
                    }
                    materials[i] = Repoint(material, fixedShader, dir, clones);
                    repointed.Add(shader.name);
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
                    "The patched copy is a strict upgrade rather than a ChilloutVR-specific variant: these " +
                    "macros compile to nothing outside stereo rendering, so it behaves identically on desktop " +
                    "and works in VRChat too — worth copying back into the original project, since the shader " +
                    "was drawing into one eye there as well.");
            }
            if (refused.Count > 0)
            {
                ctx.Report.Warning(Category, $"{refused.Count} shader(s) could not be patched for VR stereo",
                    $"{string.Join(", ", refused)} — these still won't draw correctly in both eyes. Patching is " +
                    "only attempted on plainly written vertex/fragment shaders; anything else needs doing by " +
                    "hand or replacing with a different shader.");
            }
        }

        static bool IsKnownStereo(string name) =>
            name == "Standard" || name.StartsWith("Hidden/", StringComparison.Ordinal);

        static string SourcePathOf(Shader shader)
        {
            string path = AssetDatabase.GetAssetPath(shader);
            return !string.IsNullOrEmpty(path) && path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)
                   && File.Exists(path) ? path : null;
        }

        static bool DeclaresStereo(string path)
        {
            var remaining = new HashSet<string>(StereoMacros, StringComparer.Ordinal);
            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    remaining.RemoveWhere(m => line.Contains(m, StringComparison.Ordinal));
                    if (remaining.Count == 0) return true;
                }
            }
            catch { return true; } // unreadable: leave it alone
            return false;
        }

        /// <summary>
        /// Writes a patched copy, or returns null with the reason it was refused.
        /// </summary>
        static Shader TryPatch(string sourcePath, string shaderName, string dir, out string reason)
        {
            string text;
            try { text = File.ReadAllText(sourcePath); }
            catch { reason = "source unreadable"; return null; }

            // The inserted lines below are written with \n. If the shader is CRLF — most are —
            // mixing them makes Unity warn about inconsistent line endings on import, and blame
            // AvatarBridge for it. Remembered here, reapplied to the whole file before writing.
            bool crlf = text.Contains("\r\n");

            if (Regex.IsMatch(text, @"#pragma\s+surface"))
            {
                reason = "surface shader — Unity generates the vertex stage, nothing to patch";
                return null;
            }
            var vertPragma = Regex.Match(text, @"#pragma\s+vertex\s+(\w+)");
            var fragPragma = Regex.Match(text, @"#pragma\s+fragment\s+(\w+)");
            if (!vertPragma.Success || !fragPragma.Success)
            {
                reason = "no vertex/fragment pragma found";
                return null;
            }
            string vertName = vertPragma.Groups[1].Value, fragName = fragPragma.Groups[1].Value;

            // "v2fType vertName (appdataType v)" — the two struct names to patch.
            var sig = Regex.Match(text, $@"(\w+)\s+{Regex.Escape(vertName)}\s*\(\s*(\w+)\s+(\w+)\s*\)");
            if (!sig.Success)
            {
                reason = "vertex function signature not recognised";
                return null;
            }
            string v2fType = sig.Groups[1].Value, inType = sig.Groups[2].Value, inArg = sig.Groups[3].Value;

            if (!Regex.IsMatch(text, $@"struct\s+{Regex.Escape(inType)}\s*\{{")
                || !Regex.IsMatch(text, $@"struct\s+{Regex.Escape(v2fType)}\s*\{{"))
            {
                reason = "its structs aren't declared in this file (shared include)";
                return null;
            }

            // 1 & 2 — the struct members.
            text = Regex.Replace(text, $@"(struct\s+{Regex.Escape(inType)}\s*\{{)",
                "$1\n\t\t\t\tUNITY_VERTEX_INPUT_INSTANCE_ID");
            text = Regex.Replace(text, $@"(struct\s+{Regex.Escape(v2fType)}\s*\{{)",
                "$1\n\t\t\t\tUNITY_VERTEX_OUTPUT_STEREO");

            // 3 & 4 — after the output struct is declared inside the vertex function.
            var vertBody = Regex.Match(text, $@"({Regex.Escape(v2fType)}\s+{Regex.Escape(vertName)}\s*\([^)]*\)\s*\{{\s*{Regex.Escape(v2fType)}\s+(\w+)\s*;)");
            if (!vertBody.Success)
            {
                reason = "vertex function doesn't declare its output in a form this can patch";
                return null;
            }
            string outVar = vertBody.Groups[2].Value;
            text = text.Replace(vertBody.Groups[1].Value, vertBody.Groups[1].Value +
                $"\n\t\t\t\tUNITY_SETUP_INSTANCE_ID({inArg});\n\t\t\t\tUNITY_INITIALIZE_VERTEX_OUTPUT_STEREO({outVar});");

            // 5 — the eye index in the fragment stage.
            var fragSig = Regex.Match(text, $@"\w+\s+{Regex.Escape(fragName)}\s*\(\s*{Regex.Escape(v2fType)}\s+(\w+)\s*\)[^{{]*\{{");
            if (fragSig.Success)
            {
                string fragArg = fragSig.Groups[1].Value;
                text = text.Replace(fragSig.Value, fragSig.Value +
                    $"\n\t\t\t\tUNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX({fragArg});");
            }

            // 6 — screen-space depth, which the CCK's four-macro test does not cover. Under
            // single-pass instanced _CameraDepthTexture is an array, so a sampler2D read of it is
            // wrong however many macros are present. Common in soft-particle shaders.
            text = Regex.Replace(text, @"sampler2D\s+_CameraDepthTexture\s*;",
                "UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);");
            text = Regex.Replace(text,
                @"tex2Dproj\s*\(\s*_CameraDepthTexture\s*,\s*(UNITY_PROJ_COORD\([^)]*\))\s*\)\s*\.\s*r",
                "SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, $1)");

            // Rename so it can't collide with the original in the shader list.
            string newName = shaderName + " (SPI)";
            text = Regex.Replace(text, @"Shader\s+""[^""]+""", "Shader \"" + newName + "\"", RegexOptions.None);

            if (crlf)
            {
                text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
            }

            Directory.CreateDirectory(dir);
            string outPath = AssetDatabase.GenerateUniqueAssetPath(
                dir + "/" + Path.GetFileNameWithoutExtension(sourcePath) + "_SPI.shader");
            File.WriteAllText(outPath, text);
            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceSynchronousImport);

            var result = AssetDatabase.LoadAssetAtPath<Shader>(outPath);
            if (result == null || ShaderUtil.ShaderHasError(result))
            {
                // Never leave a broken shader behind; the original still works as well as it did.
                string errors = result != null
                    ? string.Join("; ", ShaderUtil.GetShaderMessages(result)
                        .Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        .Take(2).Select(m => m.message))
                    : "copy failed to import";
                AssetDatabase.DeleteAsset(outPath);
                reason = "patched copy did not compile: " + errors;
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
            string path = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + original.name + "_SPI.mat");
            AssetDatabase.CreateAsset(copy, path);
            clones[original] = copy;
            return copy;
        }
    }
}
#endif
