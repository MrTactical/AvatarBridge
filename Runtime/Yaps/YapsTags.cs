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
// string in one pixel and costs an occasional false yes: a socket wearing
// three tags lights about eight bits of twenty, so an unrelated tag reads
// as present about one time in twenty.
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
        // round of the same hash.
        //
        // Distinct is not tidiness. Taking successive remainders of one hash
        // let a tag repeat a bit and light only two, and a two-bit pattern
        // is very often a subset of some three-bit one: with the eleven
        // names SPS derives, "footleft" landed inside "handright", so a
        // socket on a hand read as a socket on a foot. Every pair is clear
        // once each tag spends its full three.
        public static int Pattern(string tag)
        {
            uint h = Hash(tag);
            if (h == 0) return 0;
            int word = 0;
            for (int n = 0; n < PerTag; n++)
            {
                // Bits attempts, then give up and take the shorter pattern
                // rather than spin. It cannot happen with three of twenty
                // and the loop is what says so.
                for (int t = 0; t < Bits; t++)
                {
                    h = h * 16777619 + 2166136261;
                    int bit = 1 << (int) (h % Bits);
                    if ((word & bit) != 0) continue;
                    word |= bit;
                    break;
                }
            }
            return word;
        }

        public static int Word(IEnumerable<string> tags)
        {
            int word = 0;
            if (tags == null) return 0;
            foreach (string tag in tags) word |= Pattern(tag);
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
