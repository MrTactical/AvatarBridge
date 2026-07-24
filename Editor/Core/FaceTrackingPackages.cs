#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using UnityEditor;
using UnityEditor.Animations;

namespace AvatarBridge
{
    /// <summary>
    /// Locates the CVR-VRCFT face-tracking animator that AvatarBridge injects. The rig is
    /// DragonSkyRunner's "CVR Eye &amp; Face Tracking" and ships bundled under
    /// Assets/AvatarBridge/FaceTracking, so this normally always resolves; the lookup is by
    /// the controller's stable asset GUID (with a name-search fallback) so it still works if
    /// the assets are relocated. IsInstalled only reports false if the bundle was deleted.
    /// </summary>
    public static class FaceTrackingPackages
    {
        public const string DisplayName = "CVR VRCFT — Eye & Face Tracking (DragonSkyRunner)";
        public const string Url = "https://github.com/DragonSkyRunner/ChilloutVR-Facetracking-Animator-Package";

        // Stable GUID of "Face Tracking Layers.controller" (bundled from the package).
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
