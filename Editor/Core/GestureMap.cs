using System.Collections.Generic;

namespace AvatarBridge
{
    /// <summary>
    /// VRChat and ChilloutVR both expose GestureLeft/GestureRight, but the values differ.
    ///
    ///   VRChat (int)          ChilloutVR (float)
    ///   0 Neutral             0
    ///   1 Fist                0..1 (analog: trigger squeeze amount while fisting)
    ///   2 Open Hand           -1
    ///   3 Point               4
    ///   4 Peace               5
    ///   5 Rock'n'Roll         6
    ///   6 Gun                 3
    ///   7 Thumbs Up           2
    ///
    /// Because CVR's parameter is a float and the fist value is analog, integer Equals /
    /// NotEqual conditions must be rewritten as float range checks.
    /// </summary>
    public static class GestureMap
    {
        public const float Epsilon = 0.35f;   // half-window for exact gesture values
        public const float FistFloor = 0.005f; // fist is (0..1]; anything above this counts

        public static readonly HashSet<string> GestureParameters = new HashSet<string>
        {
            "GestureLeft", "GestureRight"
        };

        public static readonly HashSet<string> GestureWeightParameters = new HashSet<string>
        {
            "GestureLeftWeight", "GestureRightWeight"
        };

        public static float VrcToCvr(float vrcGesture)
        {
            switch ((int)vrcGesture)
            {
                case 0: return 0f;   // neutral
                case 1: return 1f;   // fist (analog 0..1 in CVR)
                case 2: return -1f;  // open hand
                case 3: return 4f;   // point
                case 4: return 5f;   // peace
                case 5: return 6f;   // rock'n'roll
                case 6: return 3f;   // gun
                case 7: return 2f;   // thumbs up
                default: return vrcGesture;
            }
        }

        /// <summary>
        /// Float window that detects the CVR value corresponding to a VRC gesture int.
        /// </summary>
        public static (float min, float max) CvrRangeForVrcGesture(float vrcGesture)
        {
            int gesture = (int)vrcGesture;
            if (gesture == 0)
            {
                // Neutral must not swallow small analog fist values.
                return (-Epsilon, FistFloor);
            }
            if (gesture == 1)
            {
                // Fist is analog: any squeeze counts.
                return (FistFloor, 1f + Epsilon);
            }
            float cvr = VrcToCvr(gesture);
            return (cvr - Epsilon, cvr + Epsilon);
        }
    }
}
