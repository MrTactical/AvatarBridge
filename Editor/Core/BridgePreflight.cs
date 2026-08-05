#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AvatarBridge
{
    /// <summary>
    /// Checks the TOOLCHAIN before a conversion starts — is the thing that bakes this avatar
    /// actually there and actually compiled.
    ///
    /// Written after a day that lost about four full corpus runs to this. A GoGo Loco
    /// ".unitypackage" was imported over the project and overwrote Packages/com.vrcfury.vrcfury
    /// with a partial bundled copy: 50 files, no package.json. Unity cannot load a package
    /// without one, so VRCFury never compiled, never registered its build hooks, and every avatar
    /// converted as though it had no VRCFury components at all. Nothing said a word. The symptom
    /// arrived 45 minutes later as a corpus where every avatar had quietly lost its "VF#_"
    /// parameters and grown an "NDMFAvatarRoot" that should have been consumed by the bake.
    ///
    /// WHY THIS SHAPE, and not a list of known packages. The corpus was measured before this was
    /// designed: of 60 conversions, 7 hit a dependency failure, and every one was an avatar ADD-ON
    /// — Dismay PCS, a bespoke GoGo build, Wholesome SPS. Not one was the core toolchain. Those
    /// add-ons are also exactly where a hardcoded list fails: they are niche, they are sometimes
    /// custom builds that exist in one project on earth, and VRCFury ALREADY detects them and
    /// names the missing files precisely — a report entry carries that message today.
    ///
    /// So this deliberately does not enumerate anything avatars might carry. It asks one question
    /// nobody else asks: the project claims to have a baker, so is the baker loaded? That is the
    /// failure with no other detection, and it is cheap to answer.
    /// </summary>
    public static class BridgePreflight
    {
        const string Category = "Preflight";

        /// <summary>A package folder that must correspond to a loaded assembly if it exists.</summary>
        struct Baker
        {
            public string Name;        // what a user calls it
            public string FolderName;  // under Packages/
            public string Assembly;    // substring of the assembly name it compiles to
        }

        static readonly Baker[] Bakers =
        {
            new Baker { Name = "VRCFury", FolderName = "com.vrcfury.vrcfury", Assembly = "VRCFury" },
            new Baker { Name = "Modular Avatar", FolderName = "nadena.dev.modular-avatar",
                        Assembly = "nadena.dev.modular-avatar" },
            new Baker { Name = "NDMF", FolderName = "nadena.dev.ndmf", Assembly = "nadena.dev.ndmf" },
        };

        /// <summary>
        /// Returns false when the conversion must not proceed. Every problem is reported first, so
        /// the user sees all of them rather than fixing one and meeting the next.
        /// </summary>
        public static bool Check(BridgeContext ctx)
        {
            var loaded = new HashSet<string>(
                AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetName().Name)
                    .Where(n => !string.IsNullOrEmpty(n)),
                StringComparer.OrdinalIgnoreCase);

            bool ok = true;
            foreach (var baker in Bakers)
            {
                string folder = "Packages/" + baker.FolderName;
                if (!Directory.Exists(folder))
                {
                    continue; // not installed, and not everything needs it
                }
                if (loaded.Any(n => n.IndexOf(baker.Assembly, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    continue; // installed and compiled — nothing to say
                }

                ok = false;
                bool hasManifest = File.Exists(folder + "/package.json");
                int files = 0;
                try { files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length; }
                catch (IOException) { }

                ctx.Report.Error(Category, $"{baker.Name} is installed but did not compile",
                    $"\"{folder}\" exists ({files} file(s))" +
                    (hasManifest ? "" : " and has NO package.json, which is why Unity ignored it") +
                    $", but no {baker.Name} assembly is loaded. Converting now would treat every " +
                    $"{baker.Name} component on this avatar as though it were not there: the bake " +
                    "would not run, and the result would be missing everything that package builds " +
                    "— silently, because an avatar with no baker looks exactly like an avatar with " +
                    "nothing to bake. This is what an avatar or prop \".unitypackage\" does when it " +
                    "ships its own bundled copy and overwrites yours. Reinstall " +
                    $"{baker.Name} through the VRChat Creator Companion, let Unity finish " +
                    "compiling, and convert again.");
            }
            return ok;
        }
    }
}
#endif
