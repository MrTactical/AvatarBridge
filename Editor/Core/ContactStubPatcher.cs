using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    // Until 3.7.4 this file generated declarations for ChilloutVR's
    // client-internal contact components, so avatars could be authored
    // against them. That conversion path is gone: the components are
    // not in the CCK, and uploads built on them stopped being wired up
    // by the client. Contacts convert through pointers and triggers.
    //
    // What remains is the cleanup. Projects that used the old path
    // still carry the generated files under AvatarBridge/Runtime, and
    // a future CCK shipping real contact components would collide with
    // them. They are recognised by their header line and deleted once.
    [InitializeOnLoad]
    public static class ContactStubPatcher
    {
        const string GeneratedHeader = "// AvatarBridge generated contact declaration";

        static ContactStubPatcher()
        {
            EditorApplication.delayCall += RemoveGeneratedDeclarations;
        }

        static void RemoveGeneratedDeclarations()
        {
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
            Debug.Log($"[AvatarBridge] Removed {removed} generated contact declaration(s). " +
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
