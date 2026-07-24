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
            public string[] AssetGuids;       // signature asset GUIDs (most robust — resolve anywhere)
        }

        public static readonly Package[] Known =
        {
            new Package
            {
                // Jerry's templates: the VRCFury FT prefabs avatars reference by GUID.
                DisplayName = "VRCFT — Jerry's Templates (adjerry91)",
                Url = "https://github.com/adjerry91/VRCFaceTracking-Templates",
                FolderCandidates = new[]
                {
                    "Packages/adjerry91.vrcft.templates",
                    "Assets/adjerry91.vrcft.templates"
                },
                AssetGuids = new[]
                {
                    "40f7093df8038624c89c1bf989071a1d", // VRCFury - Face Tracking - UE Blendshapes.prefab
                    "b022bab8112640045a1d4c5c7ba78fac", // UE Blendshapes TongueSteps
                    "643e30d54e87ee8408452f49e3a1fdf5", // ARKit Blendshapes
                    "4eb9be63cec72ad41826a2dbc5ac710c"  // SRanipal Blendshapes
                }
            },
            new Package
            {
                DisplayName = "Pawlygon VRC-Facetracking",
                Url = "https://github.com/PawlygonStudio/VRC-Facetracking",
                FolderCandidates = new[]
                {
                    "Packages/net.pawlygon.vrc-facetracking",
                    "Assets/net.pawlygon.vrc-facetracking"
                },
                AssetGuids = new string[0]
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
            foreach (var guid in package.AssetGuids)
            {
                // A resolvable GUID means the exact signature asset is in the project,
                // regardless of whether it was imported under Packages/ or Assets/.
                if (!string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
                {
                    return true;
                }
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
