#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace AvatarBridge
{
    // Hand-written stereo fixes for individual shaders the generic patcher can't handle.
    //
    // Some shaders need more than the four stereo macros; a GrabPass is a per-eye texture
    // ARRAY under single-pass instanced, so reading it with tex2D shows one eye the other
    // eye's view no matter how many macros are present. That edit can't be derived
    // mechanically, but it CAN be written once per shader and reused forever.
    //
    // What is stored here is the EDIT, never the shader. Nothing is redistributed: the recipe
    // is applied to the user's own copy, in their own output folder, and the original file is
    // never touched; a shader's licence is not this tool's to re-grant.
    //
    // Every recipe is pinned to a FINGERPRINT of the exact source it was written against. A
    // shader that has been updated, edited, or merely shares a name will not match, and the
    // conversion falls back to refusing honestly instead of mangling a file nobody verified.
    // That is the whole reason for the hash: "same name" is not "same shader", and silently
    // applying edits to a stranger is how a fix becomes a bug.
    public static class ShaderFixRecipes
    {
        public class Recipe
        {
            public string ShaderName;
            public string Fingerprint;
            public string Note;
            public (string Find, string Replace)[] Edits;
        }

        // Empty, deliberately, and the reason is worth keeping.
        //
        // The first entry here rewrote a GrabPass shader's screen reads to the screen-space
        // macros (UNITY_DECLARE/SAMPLE_SCREENSPACE_TEXTURE). It compiled, it shipped, and in VR
        // the glasses came back GREY while desktop was fine; because those macros declare the
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
