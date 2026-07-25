#if CVR_CCK_EXISTS
using System.Collections.Generic;

namespace AvatarBridge
{
    /// <summary>
    /// Recognises the parameters of an *existing* face-tracking rig baked into the avatar
    /// (VRCFaceTracking / Jerry's Templates / Pawlygon / OSCmooth). These are matched so
    /// they can be stripped (Native and DragonSkyRunner modes both replace the avatar's FT)
    /// and never turned into menu toggles.
    ///
    /// Matching is by namespace (`FT/...`, `OSCm/...`) plus a set of well-known control
    /// flags that don't carry a namespace. Note: DragonSkyRunner's own params use a bare
    /// `v2/...` prefix (no `FT/`), so this deliberately does NOT match `v2/` — the injected
    /// rig must survive.
    /// </summary>
    public static class FaceTrackingParameters
    {
        static readonly HashSet<string> KnownControlFlags = new HashSet<string>
        {
            "EyeTrackingActive", "LipTrackingActive", "FaceTrackingActive",
            "VisemesEnable", "EyeDilationEnable", "EyeDilationTracking",
            "FacialExpressionsDisabled", "FaceTrackingEmulation", "FaceTrackingLimits",
            "RemoteModeActive", "BinaryBlendshapes", "SmoothingAmount"
        };

        public static bool IsFaceTracking(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
            {
                return false;
            }
            string name = rawName.TrimStart('#');
            if (KnownControlFlags.Contains(name))
            {
                return true;
            }
            return name.StartsWith("OSCm/") || name.Contains("/OSCm/") ||
                   name.StartsWith("FT/") || name.Contains("/FT/");
        }
    }
}
#endif
