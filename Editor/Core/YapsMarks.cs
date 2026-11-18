using System;
using UnityEngine;

namespace AvatarBridge
{
    // Recognises YAPS materials. In the core because the survey and weigh pass must know them
    // after the add-on is removed; matched by name too, since without the add-on they wear
    // the error shader. Not CCK-gated: used on avatars of either kind.
    public static class YapsMarks
    {
        public static bool IsAtlasMaterial(Material m) =>
            m != null && ((m.shader != null && m.shader.name.StartsWith("YAPS/Atlas", StringComparison.Ordinal))
                          || m.name.StartsWith("YAPS Atlas", StringComparison.Ordinal));

        // In-game readouts. Each carries its own plug's values, so never merge two.
        public static bool IsReadoutMaterial(Material m) =>
            m != null && ((m.shader != null
                           && (m.shader.name == "YAPS/Debug Overlay" || m.shader.name == "YAPS/Socket Readout"))
                          || m.name.StartsWith("YAPS Debug Overlay", StringComparison.Ordinal)
                          || m.name.StartsWith("YAPS Socket Readout", StringComparison.Ordinal));

        // A baked plug or socket. Bake, length and frame are per part, so never merge two.
        public static bool IsBakedMaterial(Material m) =>
            m != null && m.HasProperty("_YAPS_Bake") && m.GetTexture("_YAPS_Bake") != null;
    }
}
