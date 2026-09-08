// Tags: what a socket is, and which of them a plug will answer.
//
// SPS's function, exactly. A tag is a free-form string, trimmed and
// lowercased, hashed FNV-1a to 32 bits, so "Hips" and "hips " are the same
// tag and a name written on one tool means the same thing on the other.
// This was a fifteen-value enum until 2026-09-08, which could never carry
// an author's own name for anything.
//
// WHAT IS NOT SPS'S: where the hash goes. SPS gives a socket eight slots
// and writes each hash whole into a pair of material properties. The atlas
// has no room for that. It is a rectangle the view has to contain, one
// pixel per field per octant, and every extra pixel costs 256 pixels of
// width: the eight slots would take the rect from 808 wide to 2600, and
// 808 is already the number that decides whether a mirror resolves at all.
//
// So the socket's tags FOLD into one word. Each tag lights three bits of
// twenty, picked by its own hash, and the socket publishes the OR. A plug
// asks whether all three of a tag's bits are lit. That answers for any
// string in one pixel, and costs a false yes at a rate that depends on how
// many tags the socket wears: about 0.1% at one, 1.4% at two, 5% at three,
// 9% at four. Sockets in practice wear one or two.
//
// The direction of that error is the point. A false yes on the ANSWER list
// answers a socket that was not asking, which is the same permissiveness
// the feature already documents on every route but the atlas. A false yes
// on the REFUSE list refuses a socket it need not have, which errs toward
// not touching. Neither can invent a socket that is not there.
using System.Collections.Generic;

namespace AvatarBridge.Yaps
{
    public static class YapsTags
    {
        // Four channels at five bits. Five and not eight because the atlas
        // is read back through a half-float grab: eight would probably
        // survive, and a tag that decodes wrong sends a plug somewhere
        // nobody asked for, silently.
        public const int Bits = 20;

        // Bits one tag lights. Three, from the arithmetic above: two makes
        // a false yes about one time in eight, four crowds the word so
        // badly that a socket with three tags lights half of it.
        public const int PerTag = 3;

        // How many tags a plug may list on each side. The atlas cost is
        // zero, since these are the PLUG's own uniforms rather than
        // anything published, so the limit is the two Vector4s that carry
        // them and the size of a menu nobody wants to scroll.
        public const int PlugSlots = 4;

        // SPS's own, byte for byte. Changing it would silently stop
        // matching every socket somebody else's tool tagged.
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

        // The bits one tag lights: three DISTINCT ones, each from a fresh
        // round of MIXING, not a fresh round of the hash.
        //
        // Distinct is not tidiness. Successive remainders of one hash let a
        // tag repeat a bit and light only two, and a two-bit pattern is very
        // often a subset of some three-bit one: among the eleven names SPS
        // derives, "footleft" landed inside "handright", so a hand socket
        // read as a foot socket.
        //
        // The MIXER is the other half, and the reason it is this one. The
        // obvious step, h = h * 16777619 + 2166136261, is congruent to
        // 3h + 1 modulo 4, and that alternates between two residues rather
        // than visiting all four. Bit indices are taken modulo 20, so the
        // index inherits the restriction: consecutive picks came out of half
        // the word and only 200 of the 1140 possible patterns existed at
        // all. Unrelated names collided outright, "tag1" with "tag13" among
        // them. A multiply-and-add cannot be its own avalanche; this is
        // murmur3's finaliser, which is.
        public static int Pattern(string tag)
        {
            uint h = Hash(tag);
            if (h == 0) return 0;
            int word = 0;
            for (int n = 0; n < PerTag; n++)
            {
                // Bits attempts, then take the shorter pattern rather than
                // spin. Three of twenty cannot exhaust it and the loop is
                // what says so.
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

        // Trimmed, blank-free and without repeats, in the order they were
        // written. ONE place, because the bake and the menu each had their
        // own and disagreed: given "hips, HIPS, head, hand, foot" the bake
        // spent a slot on the repeat and dropped "foot", the menu did not,
        // and the menu's default state then changed which sockets the plug
        // answered the moment the controller entered it.
        //
        // The order is the author's, so the menu reads top to bottom the
        // way the inspector does.
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

        // The plug's side: one pattern per slot, in the author's order.
        // Both builders call this, because the converter and the toolkit
        // each having their own copy of a rule is how the socket ownership
        // bug got two different answers.
        public static UnityEngine.Vector4 Patterns(IList<string> tags)
        {
            var v = UnityEngine.Vector4.zero;
            var listed = Listed(tags);
            for (int i = 0; i < listed.Count && i < PlugSlots; i++) v[i] = Pattern(listed[i]);
            return v;
        }

        // Distinct entries past the slots, which are dropped. A caller that
        // does not say so leaves an author with a list the bake ignores and
        // nothing on screen admitting it.
        public static int Dropped(IList<string> tags)
        {
            int count = Listed(tags).Count;
            return count > PlugSlots ? count - PlugSlots : 0;
        }

        // Every tag a socket wears, folded into one word. Uncapped: a socket
        // pays for its tags with the OR rather than a slot each, so a long
        // list costs accuracy and nothing else.
        public static int Word(IEnumerable<string> tags)
        {
            int word = 0;
            foreach (string tag in Listed(tags)) word |= Pattern(tag);
            return word;
        }

        // SPS derives these from the bone nearest the socket and spells
        // them exactly this way. Offered in the inspector rather than
        // enforced, so an avatar converted from SPS keeps its meaning and
        // an author is still free to invent a name.
        public static readonly string[] Suggested =
        {
            "hips", "hipsfront", "hipsback", "head", "chest",
            "hand", "handleft", "handright",
            "foot", "footleft", "footright",
        };
    }
}
