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

        // A rig's own switches, read only when its layers are stripped: the menu keeps them, since
        // with the rig kept they are its controls. A layer that reads nothing else belongs to the
        // rig, as a viseme layer holding every mouth shape at 0 behind VisemesEnable. From Shiro
        // Usagi's face tracking fixer.
        static readonly HashSet<string> RigSwitches = new HashSet<string>
        {
            "EyeTrackingEnable", "MouthTrackingEnable", "TongueControlActive", "EyeTrackingActive 2",
            "EyelidSync", "LeftEyeLidExpandedSqueeze 2", "BrowEmulation", "ForceTongueOutToggle",
            "ForceTongueOutSync", "Tongue-x", "Tongue-y", "TongueXControl", "TongueYControl",
            "trackingEyes", "trackingMouth"
        };

        public static bool IsRigSwitch(string name)
        {
            return !string.IsNullOrEmpty(name) && RigSwitches.Contains(name.TrimStart('#'));
        }

        // Read by every kind of layer, so they say nothing about whose a layer is.
        public static bool IsNeutral(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            string n = name.TrimStart('#');
            return n == "IsLocal" || n == "TrackingType" || n == "VRMode" || n.StartsWith("Blend")
                || n.Contains("__QuantizationParameters") || n.Contains("__ModularAvatarInternal/One");
        }

        // VRCFaceTracking's binary sync: v2/JawOpen1, 2, 4, 8 and v2/JawOpenNegative.
        public static bool IsBinarySyncBit(string name)
        {
            return IsFaceTracking(name) && !name.Contains("/Smoothed/")
                && System.Text.RegularExpressions.Regex.IsMatch(name, @"v2/[A-Za-z]+(\d+|Negative)$");
        }

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
