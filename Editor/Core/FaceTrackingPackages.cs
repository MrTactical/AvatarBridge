#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using UnityEditor;
using UnityEditor.Animations;

namespace AvatarBridge
{
    /// <summary>
    /// Soft (dependency-free) detection of the injectable CVR face-tracking animator used
    /// by the DragonSkyRunner mode. AvatarBridge never references the package's types, so
    /// there's no hard dependency — it just looks the controller up in the AssetDatabase by
    /// its stable asset GUID (falling back to the folder / a name search). When the mode is
    /// selected but the package isn't present, the window points the user at where to get it.
    /// </summary>
    public static class FaceTrackingPackages
    {
        public const string DisplayName = "DragonSkyRunner — CVR Eye & Face Tracking";
        public const string Url = "https://booth.pm/en/items/5761383";

        // Stable GUID of "Face Tracking Layers.controller" shipped in the package.
        const string ControllerGuid = "d9d4007a1a5aa2347a6a360555797b47";

        public static AnimatorController LoadController()
        {
            string path = AssetDatabase.GUIDToAssetPath(ControllerGuid);
            if (string.IsNullOrEmpty(path))
            {
                path = FindByName();
            }
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        }

        public static bool IsInstalled()
        {
            if (!string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(ControllerGuid)))
            {
                return true;
            }
            return !string.IsNullOrEmpty(FindByName());
        }

        static string FindByName()
        {
            foreach (var guid in AssetDatabase.FindAssets("\"Face Tracking Layers\" t:AnimatorController"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("Face Tracking Layers.controller"))
                {
                    return path;
                }
            }
            return null;
        }
    }
}
#endif
