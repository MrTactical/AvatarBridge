// Tags: what a socket is, and which of them a plug will answer.
// Same hash as SPS: trimmed, lowercased, FNV-1a 32 bit, so names mean the same on both tools.
//
// Unlike SPS, a socket's tags fold into one word: each lights 3 of 20 bits, the socket
// publishes the OR. SPS's eight whole hashes would take the atlas rect from 808 to 2600 px
// wide, and 808 already decides whether a mirror resolves. False yes rate: ~0.1% at one tag,
// 1.4% at two, 5% at three, 9% at four. On the answer list that only answers an extra socket,
// on the refuse list it only refuses one; neither invents a socket.
using System.Collections.Generic;

namespace AvatarBridge.Yaps
{
    public static class YapsTags
    {
        // Four channels at five bits, not eight: the atlas is read back through a
        // half-float grab, and a tag that decodes wrong misroutes a plug silently.
        public const int Bits = 20;

        // Two bits gives a false yes ~1 in 8; four crowds the word so a three-tag
        // socket lights half of it.
        public const int PerTag = 3;

        // Per side. Plug uniforms, so no atlas cost: the limit is the two Vector4s
        // that carry them and the menu size.
        public const int PlugSlots = 4;

        // Nearly every SPS socket carries it, so a plug's list narrows rather than
        // excludes. Sockets built here carry it too, or converted plugs would refuse them.
        public const string Shared = "shared";

        // SPS's own, byte for byte. Changing it stops matching every SPS-tagged socket.
        public static uint Hash(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return 0;
            string normalised = tag.Trim().ToLowerInvariant();
            uint hash = 2166136261;
            foreach (char c in normalised)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash == 0 ? 1u : hash;
        }

        // Three DISTINCT bits, each from a fresh round of mixing. A repeated bit leaves
        // a two-bit pattern, often a subset of another tag's: "footleft" fell inside
        // "handright". The mixer is murmur3's finaliser because the obvious
        // h * 16777619 + 2166136261 is 3h + 1 mod 4, alternating two residues, so only
        // 200 of 1140 patterns existed and "tag1" collided with "tag13".
        public static int Pattern(string tag)
        {
            uint h = Hash(tag);
            if (h == 0) return 0;
            int word = 0;
            for (int n = 0; n < PerTag; n++)
            {
                // Bounded so it cannot spin; three of twenty never exhausts it.
                for (int t = 0; t < Bits; t++)
                {
                    h = Mix(h + 0x9E3779B9u);
                    int bit = 1 << (int) (h % Bits);
                    if ((word & bit) != 0) continue;
                    word |= bit;
                    break;
                }
            }
            return word;
        }

        static uint Mix(uint h)
        {
            h ^= h >> 16;
            h *= 2246822507u;
            h ^= h >> 13;
            h *= 3266489909u;
            h ^= h >> 16;
            return h;
        }

        // Trimmed, no blanks or repeats, in the author's order. The one place for this:
        // separate copies in the bake and menu disagreed on repeats and changed which
        // sockets a plug answered once the menu's default state applied.
        public static List<string> Listed(IEnumerable<string> tags)
        {
            var found = new List<string>();
            if (tags == null) return found;
            foreach (string tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                string clean = tag.Trim();
                bool seen = false;
                foreach (string had in found)
                {
                    if (!string.Equals(had, clean, System.StringComparison.OrdinalIgnoreCase)) continue;
                    seen = true;
                    break;
                }
                if (!seen) found.Add(clean);
            }
            return found;
        }

        // One pattern per slot. Both builders call this so they cannot disagree.
        public static UnityEngine.Vector4 Patterns(IList<string> tags)
        {
            var v = UnityEngine.Vector4.zero;
            var listed = Listed(tags);
            for (int i = 0; i < listed.Count && i < PlugSlots; i++) v[i] = Pattern(listed[i]);
            return v;
        }

        // Distinct entries past the slots, which the bake drops. Callers should say so.
        public static int Dropped(IList<string> tags)
        {
            int count = Listed(tags).Count;
            return count > PlugSlots ? count - PlugSlots : 0;
        }

        // Uncapped: a long list costs accuracy, not slots.
        public static int Word(IEnumerable<string> tags)
        {
            int word = 0;
            foreach (string tag in Listed(tags)) word |= Pattern(tag);
            return word;
        }

        // SPS's names, derived from the nearest bone. Suggested, not enforced.
        public static readonly string[] Suggested =
        {
            "hips", "hipsfront", "hipsback", "head", "chest",
            "hand", "handleft", "handright",
            "foot", "footleft", "footright",
        };
    }
}
