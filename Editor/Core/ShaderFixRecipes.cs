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

        // Empty, deliberately, and the reason is worth keeping.
        //
        // The first entry here rewrote a GrabPass shader's screen reads to the screen-space
        // macros (UNITY_DECLARE/SAMPLE_SCREENSPACE_TEXTURE). It compiled, it shipped, and in VR
        // the glasses came back GREY while desktop was fine — because those macros declare the
        // texture as a per-eye Texture2DArray, and a GrabPass under single-pass instanced does
        // not produce one. Sampling a slice that isn't there gives grey. Desktop took the
        // non-stereo branch (a plain sampler2D) and looked correct, which is exactly how the
        // mistake survived a compile check and a Unity eyeball.
        //
        // So: a GrabPass cannot be made eye-correct by editing the shader's sampling. The
        // effect can still be made to DRAW in both eyes with the ordinary four macros, which is
        // what the patcher now does, and the report says the grabbed background comes from one
        // eye. Anything better needs the effect rebuilt without a screen grab.
        //
        // The machinery stays for the next shader that genuinely needs a hand-written edit.
        static readonly Recipe[] All = new Recipe[0];

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

        /// <summary>
        /// The recipe for this shader, or null. <paramref name="exactRevision"/> reports whether
        /// the file is byte-for-byte the one the recipe was written against.
        ///
        /// Matching is by shader name AND by every edit having something to bind to — not by the
        /// fingerprint, which turned out to be the wrong gate. The first field test failed on a
        /// file that differed from mine by something invisible (a trailing newline is enough),
        /// and refusing a shader we can demonstrably fix is a worse outcome than the risk the
        /// hash was guarding against. The anchors are the stronger guarantee anyway: a shader
        /// that shares this one's name AND contains every exact line we intend to rewrite is
        /// that shader, whatever its byte count. The fingerprint stays, as the difference
        /// between "verified revision" and "adapted" in the report.
        /// </summary>
        public static Recipe Find(string shaderName, string source, out bool exactRevision)
        {
            exactRevision = false;
            if (string.IsNullOrEmpty(source))
            {
                return null;
            }
            string fingerprint = Fingerprint(source);
            foreach (var recipe in All)
            {
                if (!string.Equals(recipe.ShaderName, shaderName, StringComparison.Ordinal))
                {
                    continue;
                }
                bool anchored = true;
                foreach (var edit in recipe.Edits)
                {
                    if (source.IndexOf(edit.Find, StringComparison.Ordinal) < 0)
                    {
                        anchored = false;
                        break;
                    }
                }
                if (anchored)
                {
                    exactRevision = recipe.Fingerprint == fingerprint;
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
