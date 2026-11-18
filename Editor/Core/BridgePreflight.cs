#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AvatarBridge
{
    // Is the project's baker actually loaded and compiled? A package imported over VRCFury left
    // it without package.json, so it never compiled and every avatar silently lost its Fury
    // features. Not a list of known add-ons: of 60 conversions, 7 dependency failures were all
    // niche add-ons, which VRCFury already detects and names.
    public static class BridgePreflight
    {
        const string Category = "Preflight";

        // A package folder that must correspond to a loaded assembly if it exists.
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
                    continue; // installed and compiled: nothing to say
                }

                ok = false;
                bool hasManifest = File.Exists(folder + "/package.json");
                int files = 0;
                try { files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length; }
                catch (IOException) { }

                ctx.Report.Error(Category, $"{baker.Name} is installed but did not compile",
                    $"\"{folder}\" exists ({BridgeElements.Count(files, "file", "files")})" +
                    (hasManifest ? "" : " and has NO package.json, which is why Unity ignored it") +
                    $", but no {baker.Name} assembly is loaded, so its components would be silently ignored. " +
                    "A package bundling its own copy does this. Reinstall " +
                    $"{baker.Name} through the VRChat Creator Companion, let Unity compile, and convert again.");
            }
            return ok;
        }
    }
}
#endif
