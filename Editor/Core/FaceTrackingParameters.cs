#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using UnityEditor;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace AvatarBridge
{
    /// <summary>
    /// Collects the parameter names owned by a detected face-tracking template package
    /// (e.g. Jerry's Templates' ~152 "FT/v2/..." binary-encoded sync bits and control
    /// parameters). These are driven by the FT animator layers and the OSC face-tracking
    /// mod — they are NOT user toggles, so they must be excluded from menu exposure and
    /// toggle conversion. Without this, "expose menu-less synced parameters" turns every
    /// binary FT bit into a garbage menu toggle.
    ///
    /// The names come straight from the package's own VRCExpressionParameters assets, so
    /// this stays accurate to whatever package version is installed.
    /// </summary>
    public static class FaceTrackingParameters
    {
        /// <summary>
        /// True for any face-tracking parameter — either an exact name from a detected
        /// package's parameter asset (the synced binary bits and control flags), or one of
        /// the OSCmooth / FT namespaces the FT animator uses internally
        /// (e.g. "OSCm/Proxy/FT/v2/MouthUp", "OSCm/Remote/BinaryMod", "FT/v2/EyeSquintLeft1").
        /// The internal ones never appear in the expression-parameters asset, so pattern
        /// matching is required alongside the collected exact names.
        /// </summary>
        public static bool IsFaceTracking(string rawName, HashSet<string> collected)
        {
            if (string.IsNullOrEmpty(rawName))
            {
                return false;
            }
            string name = rawName.TrimStart('#');
            if (collected.Contains(name) || collected.Contains(rawName))
            {
                return true;
            }
            return name.StartsWith("OSCm/") || name.Contains("/OSCm/") ||
                   name.StartsWith("FT/") || name.Contains("/FT/");
        }

        public static HashSet<string> Collect()
        {
            var names = new HashSet<string>();
            foreach (var package in FaceTrackingPackages.Known)
            {
                if (!FaceTrackingPackages.IsInstalled(package))
                {
                    continue;
                }
                foreach (var folder in package.FolderCandidates)
                {
                    if (!AssetDatabase.IsValidFolder(folder))
                    {
                        continue;
                    }
                    foreach (var guid in AssetDatabase.FindAssets("t:VRCExpressionParameters", new[] { folder }))
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(
                            AssetDatabase.GUIDToAssetPath(guid));
                        if (asset?.parameters == null)
                        {
                            continue;
                        }
                        foreach (var parameter in asset.parameters)
                        {
                            if (!string.IsNullOrEmpty(parameter.name))
                            {
                                names.Add(parameter.name);
                            }
                        }
                    }
                }
            }
            return names;
        }
    }
}
#endif
