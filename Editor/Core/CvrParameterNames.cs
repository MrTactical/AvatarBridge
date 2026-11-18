// Which parameter names ChilloutVR owns, and what a VRChat name becomes. Separate from the
// merger because the toolkit ships without it.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;

namespace AvatarBridge
{
    internal static class CvrParameterNames
    {
        // VRChat parameter -> ChilloutVR core parameter.
        // GestureLeftWeight/RightWeight stay unrenamed.
        // The merger works them out from GestureLeft/Right instead.
        internal static readonly Dictionary<string, string> RenameMap = new Dictionary<string, string>
        {
            { "Viseme", "VisemeIdx" },
            { "Voice", "VisemeLoudness" },
            // NOT Seated or InStation onto "Sitting", which CVR drives itself: the merged Base
            // layer outranks CVR's and its seated blend tree fired mid-blend, the "bicycle pose"
            // (confirmed in game). LocomotionGrafter.GraftSitting carries the authored sit instead.
            // InStation is also true in any station (props, beds).
            { "IsOnFriendsList", "IsFriend" }
        };

        // VRC built-ins with no CVR equivalent, frozen "#" locals. Stream-fed names are live
        // (StreamFedParameters); AFK syncs natively in CVR. VelocityMagnitude is here only for the
        // "#" prefix (FeedVelocityMagnitude recomputes it); scale family: see FeedScaleParameters.
        internal static readonly HashSet<string> NeverFedNames = new HashSet<string>
        {
            "Earmuffs", "AngularY",
            "AvatarVersion", "VelocityMagnitude", "GroundProximity", "InStation",
            "IsAnimatorEnabled",
            // Not Seated: it re-arms the Base layer's seated state over CVR's (see the rename table).
            "Seated"
        };

        // Declared so merged layers bind, never written. The survey must not report them as dead.
        internal static bool NeverFed(string name)
            => !string.IsNullOrEmpty(name) && NeverFedNames.Contains(name.TrimStart('#'));

        // Parameters ChilloutVR drives itself; these must never be renamed or prefixed.
        internal static readonly HashSet<string> Core = new HashSet<string>
        {
            "MovementX", "MovementY", "Grounded", "Emote", "CancelEmote",
            "GestureLeft", "GestureRight", "GestureLeftIdx", "GestureRightIdx",
            "Toggle", "Sitting", "Crouching", "Prone", "Flying", "Swimming",
            "IsLocal", "DistanceTo", "VisemeIdx", "VisemeLoudness", "IsFriend",
            "VelocityX", "VelocityY", "VelocityZ", "AFK"
        };

        // CVR's Locomotion/Emotes inputs. Never a rename target: the merged layer above would
        // win with CVR's own signal. VisemeIdx or IsFriend are fine, nothing is outranked.
        internal static readonly HashSet<string> LocomotionDriven = new HashSet<string>
        {
            "MovementX", "MovementY", "Grounded", "Emote", "CancelEmote",
            "Toggle", "Sitting", "Crouching", "Prone", "Flying", "Swimming"
        };

        // Table consistency, checked at conversion: two contradicting tables once renamed Seated
        // onto Sitting unnoticed. Each rule alone would have caught it.
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
            "MuteSelf", "VRMode", "Upright", "TrackingType", "EyeHeightAsMeters"
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

        // Floats a clip in the controller writes. The client marks these read-only and never
        // syncs them or spends bits on them, whatever their name, so counting one as synced
        // reports a budget the avatar does not use.
        internal static HashSet<string> CurveDriven(UnityEditor.Animations.AnimatorController controller)
        {
            var floats = new HashSet<string>(controller.parameters
                .Where(p => p.type == UnityEngine.AnimatorControllerParameterType.Float)
                .Select(p => p.name));
            var driven = new HashSet<string>();
            foreach (var clip in controller.animationClips.Where(c => c != null).Distinct())
            {
                foreach (var b in UnityEditor.AnimationUtility.GetCurveBindings(clip))
                {
                    if (b.type == typeof(UnityEngine.Animator) && string.IsNullOrEmpty(b.path)
                        && floats.Contains(b.propertyName))
                    {
                        driven.Add(b.propertyName);
                    }
                }
            }
            return driven;
        }
    }
}
#endif
