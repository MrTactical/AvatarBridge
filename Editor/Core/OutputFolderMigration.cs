using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    /// <summary>
    /// Moves conversions out of the tool's own folder.
    ///
    /// Output used to default to Assets/AvatarBridge/Output — inside the folder every
    /// .unitypackage update flow deletes and reimports. One update erased every converted
    /// controller, override controller, prefab and rehomed material in a user's project at
    /// once: Missing (Runtime Animator Controller) on every converted avatar, particles
    /// rendering as pink squares. The new home is Assets/AvatarBridgeOutput, a sibling the
    /// tool's uninstall can't reach.
    ///
    /// Migration uses AssetDatabase.MoveAsset, which KEEPS every GUID — scene references,
    /// prefabs and override controllers keep resolving as if nothing happened. Runs from the
    /// window and on load; both are idempotent and silent when there is nothing to do.
    /// </summary>
    [InitializeOnLoad]
    public static class OutputFolderMigration
    {
        const string OldFolder = "Assets/AvatarBridge/Output";
        const string NewFolder = "Assets/AvatarBridgeOutput";

        static OutputFolderMigration()
        {
            EditorApplication.delayCall += MigrateIfNeeded;
        }

        public static void MigrateIfNeeded()
        {
            if (!AssetDatabase.IsValidFolder(OldFolder))
            {
                return;
            }
            if (AssetDatabase.IsValidFolder(NewFolder))
            {
                // Both exist (a conversion ran on an old version after the new folder was
                // made). Move the per-avatar subfolders across individually; name clashes
                // stay put and are reported rather than overwritten.
                bool anyLeft = false;
                foreach (var guid in AssetDatabase.FindAssets("", new[] { OldFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!AssetDatabase.IsValidFolder(path) ||
                        System.IO.Path.GetDirectoryName(path).Replace('\\', '/') != OldFolder)
                    {
                        continue;
                    }
                    string target = NewFolder + "/" + System.IO.Path.GetFileName(path);
                    if (AssetDatabase.IsValidFolder(target))
                    {
                        Debug.LogWarning($"[AvatarBridge] Not migrating {path}: {target} already exists. " +
                                         "Merge or delete one of them by hand.");
                        anyLeft = true;
                        continue;
                    }
                    string error = AssetDatabase.MoveAsset(path, target);
                    if (!string.IsNullOrEmpty(error))
                    {
                        Debug.LogWarning($"[AvatarBridge] Could not migrate {path}: {error}");
                        anyLeft = true;
                    }
                }
                if (!anyLeft)
                {
                    AssetDatabase.DeleteAsset(OldFolder);
                }
            }
            else
            {
                string error = AssetDatabase.MoveAsset(OldFolder, NewFolder);
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogWarning($"[AvatarBridge] Could not move conversions out of the tool folder: {error}");
                    return;
                }
            }
            Debug.Log("[AvatarBridge] Conversions moved to " + NewFolder + " — outside the tool's folder, " +
                      "so deleting/reimporting AvatarBridge to update it can never erase them again. " +
                      "All GUIDs preserved; existing references keep working.");
        }
    }
}
