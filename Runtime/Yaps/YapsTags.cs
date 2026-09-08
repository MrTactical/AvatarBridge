// What a socket says it is, and what a plug will answer.
//
// Fifteen bits, because that is what fits: a socket publishes its set in
// one atlas pixel, five bits a channel over rgb, and the plug carries an
// include set and an exclude set as plain floats so an animation can
// change them mid-session.
//
// The first eleven are SPS's automatic location tags, spelled the same
// way, so a converted avatar keeps the meaning its author wrote. The four
// spare slots are by convention only: a tag means the same thing to two
// avatars because both were built by this tool from the same list, and a
// custom name lives in the author's head rather than in the pixel. That
// is the price of putting a set on a GPU instead of a string.
using System;

namespace AvatarBridge
{
    [Flags]
    public enum YapsTags
    {
        None = 0,

        Hips = 1 << 0,
        Head = 1 << 1,
        Chest = 1 << 2,
        Hand = 1 << 3,
        HandLeft = 1 << 4,
        HandRight = 1 << 5,
        Foot = 1 << 6,
        FootLeft = 1 << 7,
        FootRight = 1 << 8,
        HipsFront = 1 << 9,
        HipsBack = 1 << 10,

        CustomA = 1 << 11,
        CustomB = 1 << 12,
        CustomC = 1 << 13,
        CustomD = 1 << 14,
    }
}
