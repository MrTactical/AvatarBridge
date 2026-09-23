#if CVR_CCK_EXISTS
using System;
using UnityEngine;

namespace AvatarBridge
{
    // Recognises what the YAPS add-on leaves behind. It lives in the core
    // because the survey and the weigh pass meet these materials on avatars
    // converted while the add-on was installed, and must go on knowing they
    // are not the user's own after it is gone.
    public static class YapsMarks
    {
        public static bool IsAtlasMaterial(Material m) =>
            m != null && m.shader != null
            && m.shader.name.StartsWith("YAPS/Atlas", StringComparison.Ordinal);

        // The in-game readouts: a plug's is a slot on the plug's own
        // renderer, a socket's a renderer of its own. Each carries its own
        // plug's values, so no two are ever one material.
        public static bool IsReadoutMaterial(Material m) =>
            m != null && m.shader != null
            && (m.shader.name == "YAPS/Debug Overlay" || m.shader.name == "YAPS/Socket Readout");
    }
}
#endif
