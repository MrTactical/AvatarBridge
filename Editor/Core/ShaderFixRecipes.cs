#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace AvatarBridge
{
    /// <summary>
    /// Hand-written stereo fixes for individual shaders the generic patcher can't handle.
    ///
    /// Some shaders need more than the four stereo macros — a GrabPass is a per-eye texture
    /// ARRAY under single-pass instanced, so reading it with tex2D shows one eye the other
    /// eye's view no matter how many macros are present. That edit can't be derived
    /// mechanically, but it CAN be written once per shader and reused forever.
    ///
    /// What is stored here is the EDIT, never the shader. Nothing is redistributed: the recipe
    /// is applied to the user's own copy, in their own output folder, and the original file is
    /// never touched — so a shader's licence is not our business to re-grant.
    ///
    /// Every recipe is pinned to a FINGERPRINT of the exact source it was written against. A
    /// shader that has been updated, edited, or merely shares a name will not match, and the
    /// conversion falls back to refusing honestly instead of mangling a file nobody verified.
    /// That is the whole reason for the hash: "same name" is not "same shader", and silently
    /// applying edits to a stranger is how a fix becomes a bug.
    /// </summary>
    public static class ShaderFixRecipes
    {
        public class Recipe
        {
            /// <summary>The Shader "…" name this was written for, for reporting.</summary>
            public string ShaderName;
            /// <summary>First 16 hex of SHA-256 over the source with line endings normalised.</summary>
            public string Fingerprint;
            /// <summary>What it fixes — printed in the report so the user knows what changed.</summary>
            public string Note;
            /// <summary>Applied in order. Every one must match, or the recipe is refused whole.</summary>
            public (string Find, string Replace)[] Edits;
        }

        static readonly Recipe[] All =
        {
            new Recipe
            {
                ShaderName = "Doppels shaders/Models shaders/Burning Glasses 1.11",
                Fingerprint = "dc6008e4134f5aa3",
                Note = "its GrabPass is read through a helper that samples the correct eye — the " +
                       "flame refraction showed one eye the other eye's view under ChilloutVR's " +
                       "rendering mode",
                Edits = new[]
                {
                    // The screen-space macros are used through a one-line helper rather than
                    // inline. Inline, the macro has to expand inside a fixed3(...) argument list
                    // with a swizzle attached to its result — which Unity's shader preprocessor
                    // rejected. Called through a helper it expands once, in a plain return
                    // statement, and the swizzles land on an ordinary function result.
                    ("uniform sampler2D _LensesGrabPass;",
                     "UNITY_DECLARE_SCREENSPACE_TEXTURE(_LensesGrabPass);\n" +
                     "            float4 SampleLensesGrabPass(float2 uv)\n" +
                     "            {\n" +
                     "                return UNITY_SAMPLE_SCREENSPACE_TEXTURE(_LensesGrabPass, uv);\n" +
                     "            }"),
                    ("tex2D(_LensesGrabPass, ", "SampleLensesGrabPass("),
                },
            },
        };

        /// <summary>
        /// Identity of a shader source, insensitive to line endings so a file that has been
        /// through a different git checkout still matches.
        /// </summary>
        public static string Fingerprint(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return string.Empty;
            }
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(source.Replace("\r", "")));
                var text = new StringBuilder(16);
                for (int i = 0; i < 8; i++)
                {
                    text.Append(hash[i].ToString("x2"));
                }
                return text.ToString();
            }
        }

        /// <summary>The recipe written for exactly this source, or null.</summary>
        public static Recipe Find(string source)
        {
            string fingerprint = Fingerprint(source);
            foreach (var recipe in All)
            {
                if (recipe.Fingerprint == fingerprint)
                {
                    return recipe;
                }
            }
            return null;
        }

        /// <summary>
        /// Applies every edit, or none. A partial application would leave a shader half-converted
        /// and compiling — the worst possible outcome, because it looks like it worked.
        /// </summary>
        public static bool TryApply(Recipe recipe, string source, out string result, out string failure)
        {
            result = source;
            failure = null;
            var working = source;
            foreach (var edit in recipe.Edits)
            {
                if (working.IndexOf(edit.Find, StringComparison.Ordinal) < 0)
                {
                    failure = $"the source no longer contains \"{Excerpt(edit.Find)}\"";
                    return false;
                }
                working = working.Replace(edit.Find, edit.Replace);
            }
            result = working;
            return true;
        }

        static string Excerpt(string text) =>
            text.Length <= 48 ? text : text.Substring(0, 45) + "…";
    }
}
#endif
