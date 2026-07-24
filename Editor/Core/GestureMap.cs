using System.Collections.Generic;

namespace AvatarBridge
{
    /// <summary>
    /// VRChat and ChilloutVR both expose GestureLeft/GestureRight, but the numbering
    /// differs. Confirmed against the official CCK "Core Parameters" reference:
    ///
    ///   Gesture       VRChat int    CVR float / int
    ///   Neutral       0             0
    ///   Fist          1             0.0..1.0 float, index 1 (fist is analog on the float)
    ///   Open Hand     2             -1
    ///   Point         3             4
    ///   Peace         4             5
    ///   Rock'n'Roll   5             6
    ///   Gun           6             3
    ///   Thumbs Up     7             2
    ///
    /// CVR also exposes GestureLeftIdx/GestureRightIdx (Int, [-1..6]) — the discrete
    /// gesture without analog fist weighting. Discrete Equals/NotEqual conditions map
    /// cleanly onto those integer parameters, so we redirect conditions there instead of
    /// doing fragile float-range matching on the analog GestureLeft float. Blend trees and
    /// motion-time still use the float GestureLeft so fist weighting stays smooth.
    /// </summary>
    public static class GestureMap
    {
        public static readonly HashSet<string> GestureParameters = new HashSet<string>
        {
            "GestureLeft", "GestureRight"
        };

        public static readonly HashSet<string> GestureWeightParameters = new HashSet<string>
        {
            "GestureLeftWeight", "GestureRightWeight"
        };

        /// <summary>The discrete integer-index parameter matching a gesture parameter.</summary>
        public static string IdxParameterFor(string gestureParameter)
        {
            switch (gestureParameter)
            {
                case "GestureLeft": return "GestureLeftIdx";
                case "GestureRight": return "GestureRightIdx";
                default: return gestureParameter;
            }
        }

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

        /// <summary>VRChat gesture int → ChilloutVR discrete gesture index.</summary>
        public static int VrcToCvrIdx(int vrcGesture) => (int)VrcToCvr(vrcGesture);
    }
}
