// Which parameter names ChilloutVR owns, and what a VRChat name becomes.
// Naming knowledge, not conversion: the diagnostics and the menu pass ask
// the same questions the merger does, and the toolkit ships without the
// merger, so the answers live here rather than inside ten thousand lines
// of it.
#if CVR_CCK_EXISTS
using System.Collections.Generic;

namespace AvatarBridge
{
    internal static class CvrParameterNames
    {
        // VRChat parameter -> ChilloutVR core parameter.
        // GestureLeftWeight/RightWeight stay unrenamed.
        // A CVRParameterStream feeds them instead.
        internal static readonly Dictionary<string, string> RenameMap = new Dictionary<string, string>
        {
            { "Viseme", "VisemeIdx" },
            { "Voice", "VisemeLoudness" },
            { "Seated", "Sitting" },
            { "InStation", "Sitting" },
            { "IsOnFriendsList", "IsFriend" }
        };

        // Parameters ChilloutVR drives itself; these must never be renamed or prefixed.
        internal static readonly HashSet<string> Core = new HashSet<string>
        {
            "MovementX", "MovementY", "Grounded", "Emote", "CancelEmote",
            "GestureLeft", "GestureRight", "GestureLeftIdx", "GestureRightIdx",
            "Toggle", "Sitting", "Crouching", "Prone", "Flying", "Swimming",
            "IsLocal", "DistanceTo", "VisemeIdx", "VisemeLoudness", "IsFriend",
            "VelocityX", "VelocityY", "VelocityZ", "AFK"
        };

        // Fed from the game by a CVRParameterStream.
        // Never "#" prefixed. The stream runs on the wearer's copy only,
        // so sync is the sole path to other clients.
        internal static readonly HashSet<string> StreamFed = new HashSet<string>
        {
            "GestureLeftWeight", "GestureRightWeight", "MuteSelf", "VRMode",
            "Upright", "TrackingType", "EyeHeightAsMeters"
            // The rest of the scale family is derived locally from
            // EyeHeightAsMeters by FeedScaleParameters. Cheaper than sync.
        };

        // True when the game writes this parameter, under either name.
        internal static bool IsGameDriven(string vrcParameterName)
        {
            if (string.IsNullOrEmpty(vrcParameterName))
            {
                return false;
            }
            string bare = vrcParameterName.TrimStart('#');
            string mapped = RenameMap.TryGetValue(bare, out var renamed) ? renamed : bare;
            return Core.Contains(mapped)
                   || StreamFed.Contains(bare)
                   || GestureMap.GestureWeightParameters.Contains(bare);
        }
    }
}
#endif
