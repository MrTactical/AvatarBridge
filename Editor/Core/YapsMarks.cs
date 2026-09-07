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
    }
}
#endif
