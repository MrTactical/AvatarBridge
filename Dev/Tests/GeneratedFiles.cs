#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Patching and baking write into the shared generated folder, which a test's own folder
    // cleanup never reaches, and a copy already there may be one a real avatar wears. List
    // the folder before a test and delete only what is new after it.
    public static class GeneratedFiles
    {
        public static HashSet<string> Snapshot() => new HashSet<string>(List());

        public static void DeleteNew(HashSet<string> before)
        {
            var added = new HashSet<string>(List().Where(p => !before.Contains(p)));
            // A new folder goes whole, so nothing under it is asked for twice.
            var top = added.Where(p => !added.Contains(Path.GetDirectoryName(p).Replace('\\', '/'))).ToArray();
            var failed = new List<string>();
            if (top.Length > 0 && !AssetDatabase.DeleteAssets(top, failed))
                Debug.LogWarning("[GeneratedFiles] left behind: " + string.Join(", ", failed));
        }

        static IEnumerable<string> List()
        {
            string root = YapsNativeBuilder.OutputRoot;
            if (!Directory.Exists(root)) return new string[0];
            return Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
                .Select(p => p.Replace('\\', '/')).Where(p => !p.EndsWith(".meta")).Concat(new[] { root });
        }
    }
}
#endif
