#if CVR_CCK_EXISTS
using UnityEditor;
using UnityEditor.Animations;

namespace AvatarBridge
{
    // Finds the bundled face-tracking controller by GUID, name search as fallback, so relocated
    // assets still resolve. IsInstalled is false only if the bundle was deleted.
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
