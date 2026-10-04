#if CVR_CCK_EXISTS
using System.Collections.Generic;

namespace AvatarBridge
{
    // Recognises the parameters of an *existing* face-tracking rig baked into the avatar
    // (VRCFaceTracking / Jerry's Templates / Pawlygon / OSCmooth). These are matched so
    // they can be stripped (Native and DragonSkyRunner modes both replace the avatar's FT)
    // and never turned into menu toggles.
    //
    // Matching is by namespace (`FT/...`, `OSCm/...`, a bare `v2/...` segment) plus the
    // well-known names that carry none: control flags, the legacy eye parameters and the
    // CVR-VRCFT gates. The bare `v2/` rigs are matched too: the strip runs before the
    // CVR-VRCFT rig is injected, so there is nothing of ours yet for it to take.
    public static class FaceTrackingParameters
    {
        // The rig's master switches, which the tester shows as toggles.
        static readonly HashSet<string> GateNames = new HashSet<string>
        {
            "EyeTracking", "FaceTracking", "EyeTrackingActive", "LipTrackingActive",
            "FacialExpressionsDisabled"
        };

        // Settings, not expressions. Their resting value is the rig's own, often 1.
        static readonly HashSet<string> ControlFlags = new HashSet<string>
        {
            "FaceTrackingActive",
            "VisemesEnable", "EyeDilationEnable", "EyeDilationTracking",
            "FaceTrackingEmulation", "FaceTrackingLimits",
            "RemoteModeActive", "BinaryBlendshapes", "SmoothingAmount"
        };

        static readonly HashSet<string> LegacyEyeNames = new HashSet<string>
        {
            "EyesY", "LeftEyeX", "RightEyeX",
            "LeftEyeLidExpandedSqueeze", "RightEyeLidExpandedSqueeze", "EyesDilation"
        };

        public static bool IsGateName(string name)
        {
            return !string.IsNullOrEmpty(name) && GateNames.Contains(name.TrimStart('#'));
        }

        public static bool IsControlFlag(string name)
        {
            return !string.IsNullOrEmpty(name) && ControlFlags.Contains(name.TrimStart('#'));
        }

        public static bool IsFaceTracking(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
            {
                return false;
            }
            string name = rawName.TrimStart('#');
            if (ControlFlags.Contains(name) || LegacyEyeNames.Contains(name) || GateNames.Contains(name))
            {
                return true;
            }
            // v2/ as a whole segment only: "Jacketv2/On" is somebody's toggle.
            return name.StartsWith("OSCm/") || name.Contains("/OSCm/") ||
                   name.StartsWith("FT/") || name.Contains("/FT/") ||
                   name.StartsWith("v2/") || name.Contains("/v2/");
        }
    }
}
#endif
