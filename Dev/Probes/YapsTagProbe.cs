// Does the tag hash still agree with SPS, and can two tags be confused?
//
// The first half is interop: the hash is FNV-1a over the trimmed lowercase
// string, and the eleven values below were read out of VRCFury rather than
// computed here. If one of them moves, a socket somebody else's tool tagged
// stops matching and nothing says so.
//
// The second half is the fold. Twenty bits and three per tag leaves room
// for one tag's pattern to sit inside another's, and then the two names are
// the same name forever. It already happened once: taking successive
// remainders of a single hash let "footleft" light only two bits, both of
// them inside "handright". Distinct bits fixed it and this is what proves
// it stayed fixed.
#if AVATARBRIDGE_YAPS
using System.Collections.Generic;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Dev
{
    public static class YapsTagProbe
    {
        // name, FNV-1a as SPS computes it.
        static readonly (string Name, uint Hash)[] Known =
        {
            ("hips", 1387166935u), ("hipsfront", 1115972766u), ("hipsback", 2279869664u),
            ("head", 845761475u), ("chest", 2554875734u),
            ("hand", 3300536096u), ("handleft", 451431169u), ("handright", 2593739682u),
            ("foot", 760240793u), ("footleft", 1814848884u), ("footright", 1439624737u),
        };

        [MenuItem("Tools/AvatarBridge Dev/Probe: YAPS tags")]
        public static void Run()
        {
            var fail = new List<string>();

            foreach (var (name, hash) in Known)
            {
                uint got = YapsTags.Hash(name);
                if (got != hash) fail.Add($"hash \"{name}\": {got}, SPS says {hash}");
            }

            // Case and padding are the same tag, or an author's "Hips" and a
            // converter's "hips" are two different places.
            if (YapsTags.Hash("Hips") != YapsTags.Hash(" hips ")) fail.Add("case or padding changes the hash");
            if (YapsTags.Hash("") != 0 || YapsTags.Hash(null) != 0) fail.Add("empty hashes to something");

            var pattern = Known.ToDictionary(k => k.Name, k => YapsTags.Pattern(k.Name));
            foreach (var (name, word) in pattern)
            {
                int bits = 0;
                for (int b = 0; b < YapsTags.Bits; b++) if ((word & (1 << b)) != 0) bits++;
                if (bits != YapsTags.PerTag) fail.Add($"pattern \"{name}\" lights {bits} bits, not {YapsTags.PerTag}");
                if (word >> YapsTags.Bits != 0) fail.Add($"pattern \"{name}\" runs past {YapsTags.Bits} bits");
            }

            // The one that matters: A must never read as present in B alone.
            foreach (var a in pattern)
            {
                foreach (var b in pattern)
                {
                    if (a.Key == b.Key) continue;
                    if ((b.Value & a.Value) == a.Value)
                        fail.Add($"\"{a.Key}\" reads as present on a socket tagged only \"{b.Key}\"");
                }
            }

            // And the shape the atlas actually publishes: an OR of what a
            // socket wears has to keep each of them findable.
            var worn = new[] { "hips", "hipsfront" };
            int wornWord = YapsTags.Word(worn);
            foreach (string t in worn)
            {
                int p = YapsTags.Pattern(t);
                if ((wornWord & p) != p) fail.Add($"\"{t}\" lost inside the word its own socket published");
            }

            // Arbitrary names, not just the eleven. The mixer decides how much
            // of the pattern space exists at all, and a bad one collapses it
            // without failing any single-name test: the first version reached
            // 200 of the 1140 three-bit patterns, and "tag1" and "tag13" were
            // the same tag forever.
            var seen = new HashSet<int>();
            for (int i = 0; i < 10000; i++) seen.Add(YapsTags.Pattern("tag" + i));
            int space = YapsTags.Bits * (YapsTags.Bits - 1) * (YapsTags.Bits - 2) / 6;
            if (seen.Count < space) fail.Add($"only {seen.Count} of {space} patterns are reachable");

            // And the union, which is what a socket actually publishes. Some
            // rate is inherent to a fold and the number is recorded rather
            // than asserted; a jump means the mixer moved.
            int loose = 0, asked = 0;
            for (int a = 0; a < Known.Length; a++)
            {
                for (int b = a + 1; b < Known.Length; b++)
                {
                    for (int c = b + 1; c < Known.Length; c++)
                    {
                        int word = pattern[Known[a].Name] | pattern[Known[b].Name] | pattern[Known[c].Name];
                        foreach (var (name, _) in Known)
                        {
                            if (name == Known[a].Name || name == Known[b].Name || name == Known[c].Name) continue;
                            asked++;
                            if ((word & pattern[name]) == pattern[name]) loose++;
                        }
                    }
                }
            }
            float rate = asked == 0 ? 0f : 100f * loose / asked;
            if (rate > 8f) fail.Add($"three-tag false match {rate:0.0}%, was 5.4% when this was written");

            if (fail.Count == 0) Debug.Log($"YAPS tags: ok. {Known.Length} names, {YapsTags.Bits} bits, {YapsTags.PerTag} per tag, "
                + $"{seen.Count}/{space} patterns reachable, {rate:0.0}% false on a three-tag socket.");
            else Debug.LogError("YAPS tags FAILED:\n" + string.Join("\n", fail));
        }
    }
}
#endif
