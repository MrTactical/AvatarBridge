using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    // Moves output out of Assets/AvatarBridge/Output, which an update deletes (it erased every
    // converted controller and material in a project), to Assets/AvatarBridgeOutput. MoveAsset
    // keeps GUIDs, so references still resolve. Idempotent; runs from the window and on load.
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
                // Both exist: move every entry across; clashes stay put and are reported, not overwritten.
                bool anyLeft = false;
                foreach (var guid in AssetDatabase.FindAssets("", new[] { OldFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path) ||
                        System.IO.Path.GetDirectoryName(path).Replace('\\', '/') != OldFolder)
                    {
                        continue;
                    }
                    string target = NewFolder + "/" + System.IO.Path.GetFileName(path);
                    if (AssetDatabase.LoadMainAssetAtPath(target) != null)
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
                if (anyLeft)
                {
                    return;   // the warnings above said what stayed; claiming success would not
                }
                // Trash, not delete: files Unity does not import never reached FindAssets.
                AssetDatabase.MoveAssetToTrash(OldFolder);
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
            Debug.Log("[AvatarBridge] Conversions moved to " + NewFolder + ": outside the tool's folder, " +
                      "so deleting/reimporting AvatarBridge to update it can never erase them again. " +
                      "All GUIDs preserved; existing references keep working.");
        }
    }
}
