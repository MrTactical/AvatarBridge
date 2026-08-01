using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace AvatarBridge
{
    /// <summary>
    /// Picks the path an asset written into the conversion's output folder should occupy.
    ///
    /// Replaces AssetDatabase.GenerateUniqueAssetPath at every site that writes into the output
    /// folder, because "unique" is the wrong question there. GenerateUniqueAssetPath compares
    /// against everything on disk, including the previous conversion's output, so reconverting
    /// the same avatar never reuses a name — it appends a number and writes a fresh copy beside
    /// the old one. Kar's output folder had 22 copies of "Angry.anim" and 554 .anim files in
    /// total, one set per conversion, of which 21 sets were orphans nothing referenced.
    ///
    /// The right question is "is this name taken BY THIS RUN". Within a run two different source
    /// clips genuinely can share a name — two controllers each with their own "Angry" — and those
    /// must not overwrite each other. Across runs the previous copy is exactly what should be
    /// replaced.
    ///
    /// So: claimed names are tracked per conversion, and a path free this run is reused, deleting
    /// whatever a previous run left there. Output folders stop growing without bound, and the
    /// names stop drifting upward — which also happened to make every regression digest differ
    /// from the last on every avatar, for no reason anybody could see.
    /// </summary>
    internal static class OutputAssetPaths
    {
        static readonly HashSet<string> ClaimedThisRun =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Called once per conversion, before any pass writes an asset.</summary>
        internal static void Reset()
        {
            ClaimedThisRun.Clear();
        }

        /// <summary>
        /// Claims <paramref name="desiredPath"/>, or the first " N" variant of it not already
        /// claimed by this conversion, and clears anything a previous run left there.
        /// </summary>
        internal static string Claim(string desiredPath)
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

            // Deleted rather than overwritten: CopyAsset refuses an occupied destination, and
            // CreateAsset onto one leaves the old object's sub-assets behind. The GUID changes
            // either way — it already did, every run, when the name changed instead.
            if (AssetDatabase.LoadMainAssetAtPath(candidate) != null)
            {
                AssetDatabase.DeleteAsset(candidate);
            }
            return candidate;
        }
    }
}
