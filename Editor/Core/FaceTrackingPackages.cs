#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;

namespace AvatarBridge
{
    /// <summary>
    /// Soft (dependency-free) detection of parameter-based face-tracking template packages.
    /// AvatarBridge never references these packages' types, so there is no hard dependency
    /// and nothing to break if they aren't installed — it just scans the AssetDatabase for
    /// their presence. The parameter-based face-tracking mode is only offered when one is
    /// found; otherwise the user is pointed at where to get it.
    /// </summary>
    public static class FaceTrackingPackages
    {
        public class Package
        {
            public string DisplayName;
            public string Url;
            public string[] FolderCandidates; // UPM/Assets folders that indicate presence
            public string SignatureSearch;    // AssetDatabase.FindAssets query as a fallback
        }

        public static readonly Package[] Known =
        {
            new Package
            {
                DisplayName = "Pawlygon VRC-Facetracking",
                Url = "https://github.com/PawlygonStudio/VRC-Facetracking",
                FolderCandidates = new[]
                {
                    "Packages/net.pawlygon.vrc-facetracking",
                    "Assets/net.pawlygon.vrc-facetracking"
                },
                SignatureSearch = "Pawlygon Face Tracking t:AnimatorController"
            }
        };

        public static bool IsInstalled(Package package)
        {
            foreach (var folder in package.FolderCandidates)
            {
                if (AssetDatabase.IsValidFolder(folder))
                {
                    return true;
                }
            }
            if (!string.IsNullOrEmpty(package.SignatureSearch))
            {
                return AssetDatabase.FindAssets(package.SignatureSearch).Length > 0;
            }
            return false;
        }

        /// <summary>The first installed known package, or null if none are present.</summary>
        public static Package FirstInstalled()
        {
            return Known.FirstOrDefault(IsInstalled);
        }

        public static bool AnyInstalled() => Known.Any(IsInstalled);
    }
}
#endif
