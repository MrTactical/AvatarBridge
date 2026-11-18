using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    // Deletes contact-component stubs generated before 3.7.4 (recognised by their header),
    // which would collide with a future CCK's real components. Never delete this file: a package
    // import removes nothing, so an old install would keep running the old generator.
    [InitializeOnLoad]
    public static class ContactStubPatcher
    {
        const string GeneratedHeader = "// AvatarBridge generated contact declaration";

        static ContactStubPatcher()
        {
            // Once per editor session, not after every script reload: nothing
            // generates these files any more, so a reload cannot bring them back.
            if (SessionState.GetBool("AvatarBridge.ContactStubsSwept", false))
            {
                return;
            }
            EditorApplication.delayCall += RemoveGeneratedDeclarations;
        }

        static void RemoveGeneratedDeclarations()
        {
            // Marked here, not in the constructor: a reload before the next
            // tick drops the queued call, and the next reload must queue it again.
            SessionState.SetBool("AvatarBridge.ContactStubsSwept", true);
            string dir = GeneratedFolder();
            if (dir == null || !Directory.Exists(dir))
            {
                return;
            }

            int removed = 0;
            foreach (var path in Directory.GetFiles(dir, "*.cs"))
            {
                string asset = path.Replace('\\', '/');
                string firstLine;
                using (var reader = new StreamReader(asset))
                {
                    firstLine = reader.ReadLine() ?? "";
                }
                if (!firstLine.StartsWith(GeneratedHeader, StringComparison.Ordinal))
                {
                    continue;
                }
                if (AssetDatabase.DeleteAsset(asset))
                {
                    removed++;
                }
            }

            if (removed == 0)
            {
                return;
            }
            // Only if nothing else moved in; the folder was created for
            // the declarations alone.
            if (Directory.GetFileSystemEntries(dir).Length == 0)
            {
                AssetDatabase.DeleteAsset(dir);
            }
            Debug.Log($"[AvatarBridge] Removed {BridgeElements.Count(removed, "generated contact declaration", "generated contact declarations")}. " +
                      "Contacts convert through ChilloutVR pointers and triggers now; " +
                      "reconvert any avatar that used the old native-contact option.");
        }

        static string GeneratedFolder()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript ContactStubPatcher"))
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!scriptPath.EndsWith("/ContactStubPatcher.cs", StringComparison.Ordinal))
                {
                    continue;
                }
                var dir = Path.GetDirectoryName(scriptPath)?.Replace('\\', '/');
                int editor = dir?.LastIndexOf("/Editor", StringComparison.Ordinal) ?? -1;
                if (editor > 0)
                {
                    return dir.Substring(0, editor) + "/Runtime";
                }
            }
            return null;
        }
    }
}
