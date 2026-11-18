using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace AvatarBridge
{
    // Output paths unique per run, not on disk. GenerateUniqueAssetPath counts the last run's files,
    // so every reconvert added numbered copies (one folder had 22 "Angry.anim"). Names claimed this
    // run stay apart (two controllers can each bring an "Angry"); a previous run's file is replaced.
    internal static class OutputAssetPaths
    {
        static readonly HashSet<string> ClaimedThisRun =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Call at the start of every entry point that writes through Claim. One that skips it
        // inherits the last run's claims and parks a numbered copy instead.
        internal static void Reset()
        {
            ClaimedThisRun.Clear();
        }

        // With stale, the previous run's file is listed instead of deleted, for a caller that
        // deletes them all in one call: each DeleteAsset is a refresh of its own.
        internal static string Claim(string desiredPath, List<string> stale = null)
        {
            string normalised = desiredPath.Replace('\\', '/');
            string dir = Path.GetDirectoryName(normalised)?.Replace('\\', '/') ?? "";
            string name = Path.GetFileNameWithoutExtension(normalised);
            string extension = Path.GetExtension(normalised);

            string candidate = normalised;
            for (int n = 1; !ClaimedThisRun.Add(candidate); n++)
            {
                candidate = $"{dir}/{name} {n}{extension}";
            }

            // Deleted, not overwritten: CopyAsset refuses an occupied path, CreateAsset leaves old
            // sub-assets. Checked by GUID: loading the asset deserialised a whole mesh just to delete it.
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(candidate, AssetPathToGUIDOptions.OnlyExistingAssets)))
            {
                if (stale != null)
                {
                    stale.Add(candidate);
                }
                else
                {
                    AssetDatabase.DeleteAsset(candidate);
                }
            }
            return candidate;
        }

        // For a writer that picks its own names: a later Claim of the same path must see it
        // taken, or it deletes the file this run just wrote. False when it already was.
        internal static bool Reserve(string path)
        {
            return ClaimedThisRun.Add(path.Replace('\\', '/'));
        }

        // Directory plus a full Refresh, as both callers always did. CreateFolder would skip the
        // Refresh, and with it the import of anything earlier passes wrote straight to disk.
        internal static void EnsureFolder(string dir)
        {
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", dir)));
            AssetDatabase.Refresh();
        }

        // GetInvalidFileNameChars holds '/' on every platform, so shader category separators go too.
        internal static string SafeFileName(string n)
        {
            if (string.IsNullOrEmpty(n))
            {
                return "Asset";
            }
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                n = n.Replace(c, '_');
            }
            return n;
        }
    }
}
