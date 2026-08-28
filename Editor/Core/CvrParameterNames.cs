// Which parameter names ChilloutVR owns, and what a VRChat name becomes.
// Naming knowledge, not conversion: the diagnostics and the menu pass ask
// the same questions the merger does, and the toolkit ships without the
// merger, so the answers live here rather than inside ten thousand lines
// of it.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;

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
            // NOT Seated or InStation. Both used to become "Sitting", and
            // "Sitting" is a parameter ChilloutVR DRIVES ITSELF.
            //
            // The merged VRChat Base layer sits above CVR's Locomotion/Emotes
            // at full weight, so renaming onto Sitting handed CVR's own
            // sitting signal to a layer that outranks CVR's sitting. The
            // moment the game sat you down, the avatar's VRChat seated state
            // fired as well and won — landing mid-blend, because those states
            // are usually blend trees driven by parameters CVR never feeds.
            // That is the "bicycle pose", confirmed in game 2026-08-28, and it
            // is why sitting was the ONLY locomotion state that broke: no
            // other CVR parameter has a VRChat name pointed at it.
            //
            // Nothing is lost by dropping it. The avatar's authored sit is
            // already handled properly by LocomotionGrafter.GraftSitting,
            // which takes the clip from VRChat's dedicated Sitting layer and
            // grafts it INTO CVR's own sitting state, where it belongs.
            //
            // InStation was doubly wrong: it is true in ANY station, a
            // rideable prop or a bed included, and it was already listed in
            // KnownUnsupportedVrcParameters, so the two lists contradicted
            // each other and the rename won.
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

        // The subset of Core that CVR's own Locomotion/Emotes layer runs on.
        //
        // These are the ones a rename must never target. A merged VRChat
        // layer sits ABOVE that layer at full weight, so pointing a VRChat
        // name at one of these hands CVR's own locomotion signal to a layer
        // that outranks CVR's locomotion, and the two fire together with the
        // merged one winning. Renaming onto VisemeIdx or IsFriend is fine by
        // contrast: a layer reading those outranks nothing.
        internal static readonly HashSet<string> LocomotionDriven = new HashSet<string>
        {
            "MovementX", "MovementY", "Grounded", "Emote", "CancelEmote",
            "Toggle", "Sitting", "Crouching", "Prone", "Flying", "Swimming"
        };

        // What the tables must never say, checked at conversion.
        //
        // Seated and InStation were BOTH renamed onto Sitting for months.
        // Sitting drives CVR's locomotion; InStation was simultaneously
        // listed as unsupported by the merger, so two tables in two files
        // contradicted each other and the rename quietly won. Nothing
        // compared them, so nothing said so, and it surfaced as an avatar
        // pedalling in a chair.
        //
        // Three rules, each one of which alone would have caught it.
        internal static IEnumerable<string> Contradictions(IEnumerable<string> knownUnsupported)
        {
            var unsupported = new HashSet<string>(knownUnsupported ?? Enumerable.Empty<string>());
            foreach (var pair in RenameMap)
            {
                if (LocomotionDriven.Contains(pair.Value))
                {
                    yield return $"\"{pair.Key}\" is renamed onto \"{pair.Value}\", which drives " +
                                 "ChilloutVR's own Locomotion/Emotes layer. A merged layer reading it " +
                                 "sits above that layer and takes the body from it.";
                }
                if (unsupported.Contains(pair.Key))
                {
                    yield return $"\"{pair.Key}\" is renamed to \"{pair.Value}\" AND listed as a VRChat " +
                                 "built-in with no CVR equivalent. It cannot be both; the rename wins " +
                                 "and the second listing is a lie.";
                }
            }
            foreach (var same in RenameMap.GroupBy(p => p.Value).Where(g => g.Count() > 1))
            {
                yield return "\"" + string.Join("\", \"", same.Select(p => p.Key))
                             + $"\" all become \"{same.Key}\". Distinct VRChat parameters mean distinct "
                             + "things; collapsing them makes one of them wrong.";
            }
        }

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
