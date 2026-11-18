#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using ABI.CCK.Components;
using ABI.CCK.Scripts;

namespace AvatarBridge
{
    // Store-listing description, counted from the finished avatar so it cannot drift.
    // A file and the clipboard, not the CCK field: CVRAssetInfo has no description, and the
    // Content Manager keeps it in SessionState (lost on restart), which
    // BuilderTab.SelectContent clears the moment the user picks their avatar.
    public static class AvatarDescription
    {
        const string Category = "Store description";
        public const string FileName = "Description.txt";

        public static string Write(BridgeContext ctx)
        {
            string text = null;
            try
            {
                text = Build(ctx);
                string relative = ctx.OutputDir.TrimEnd('/') + "/" + FileName;
                File.WriteAllText(Path.GetFullPath(relative), text, DiagnosticsWriter.Marked);
                UnityEditor.AssetDatabase.ImportAsset(relative);
                ctx.Report.Converted(Category, ctx.Target.name,
                    $"Wrote a store description to {FileName}. \"Copy description\" in the AvatarBridge " +
                    "window copies it for the CCK's Description box. Edit it before uploading.");
            }
            catch (System.Exception e)
            {
                ctx.Report.Warning(Category, ctx.Target.name,
                    $"Could not produce the store description: {e.Message}. Nothing else about the " +
                    "conversion is affected; this only writes listing text you were free to write " +
                    "yourself. Worth reporting, since it should not happen.");
            }
            return text;
        }

        public const int MaxLength = 256;

        const int ReservedForUser = 90;

        public static string Build(BridgeContext ctx)
        {
            // The credit is the fixed cost and gets measured first; features fill what is left.
            string credit = (ctx.Standalone ? "Set up with the ChilloutVR Toolkit\n" : "Converted from VRChat with AvatarBridge\n")
                            + ShortLink(BridgeLinks.Repo);
            string name = DisplayName(ctx.Target.name);

            // Two blank lines first: the user almost always adds their own text (creator,
            // source, price), and ReservedForUser keeps room for it.
            const string gap = "\n\n";

            int budget = MaxLength - ReservedForUser - gap.Length - name.Length - credit.Length - 4;

            var kept = new List<string>();
            foreach (string feature in Features(ctx))
            {
                // " · " between entries, so each costs itself plus the separator.
                int cost = feature.Length + (kept.Count > 0 ? 3 : 0);
                if (cost > budget)
                {
                    continue;   // skip, don't stop: a later entry may still fit
                }
                budget -= cost;
                kept.Add(feature);
            }

            var sb = new StringBuilder();
            sb.Append(gap);
            sb.AppendLine(name);
            if (kept.Count > 0)
            {
                sb.AppendLine(string.Join(" · ", kept));
            }
            sb.AppendLine();
            sb.Append(credit);

            string text = sb.ToString();
            // Never hand back something the box would truncate.
            return text.Length <= MaxLength ? text : text.Substring(0, MaxLength).TrimEnd();
        }

        static string ShortLink(string url)
        {
            return url.StartsWith("https://") ? url.Substring("https://".Length) : url;
        }

        static List<string> Features(BridgeContext ctx)
        {
            var lines = new List<string>();

            // Order matters: Build() drops whatever does not fit, so marquee features go first,
            // or a toggle-heavy avatar spends the box on counts and never mentions face tracking.
            CountMenu(ctx, out int toggles, out int sliders, out int puppets, out int colours);
            if (toggles > 0) lines.Add(Plural(toggles, "toggle"));

            int chains = CountPhysics(ctx, out string physicsName);
            if (chains > 0)
            {
                lines.Add($"{Plural(chains, "physics chain")} ({physicsName})");
            }

            // Checked against the component, never the setting: a mode can produce nothing
            // (no compatible blendshapes, an unreadable mesh), and the claim would be false.
            if (ctx.Target.GetComponentInChildren<CVRFaceTracking>(true) != null
                || (FindDeep(ctx.Target.transform, "EyeTracking.L") != null
                    && FindDeep(ctx.Target.transform, "EyeTracking.R") != null))
            {
                lines.Add("face tracking");
            }

            // Triangles first: the one number that decides whether someone can wear it.
            CountGeometry(ctx, out int triangles, out int materials, out int shapes);
            if (triangles > 0)
            {
                lines.Add(Triangles(triangles));
            }

            // Same rule: injection is skipped on rigs it cannot measure.
            if (HasMenuEntry(ctx, "Height"))
            {
                lines.Add("height slider");
            }

            // Each needs its own switch on AND a shape to drive; the flag can be set with an empty array.
            bool blinks = ctx.CvrAvatar != null && ctx.CvrAvatar.useBlinkBlendshapes
                          && HasAny(ctx.CvrAvatar.blinkBlendshape);
            bool lipSync = ctx.CvrAvatar != null && ctx.CvrAvatar.useVisemeLipsync
                           && (ctx.CvrAvatar.visemeMode == CVRAvatar.CVRAvatarVisemeMode.JawBone
                               || HasAny(ctx.CvrAvatar.visemeBlendshapes));
            if (blinks && lipSync) lines.Add("blink and lip sync");
            else if (blinks) lines.Add("auto blink");
            else if (lipSync) lines.Add("lip sync");

            // The long tail: true, but nobody picks an avatar for its joystick count.
            if (sliders > 0) lines.Add(Plural(sliders, "slider"));
            if (puppets > 0) lines.Add(Plural(puppets, "joystick"));
            if (colours > 0) lines.Add(Plural(colours, "colour picker"));
            if (materials > 0) lines.Add(Plural(materials, "material"));
            if (shapes > 0) lines.Add(Plural(shapes, "blendshape"));

            return lines;
        }

        static bool HasAny(string[] shapes)
        {
            return shapes != null && shapes.Any(s => !string.IsNullOrEmpty(s));
        }

        static bool HasMenuEntry(BridgeContext ctx, string name)
        {
            var settings = ctx.CvrAvatar != null && ctx.CvrAvatar.avatarSettings != null
                ? ctx.CvrAvatar.avatarSettings.settings
                : null;
            return settings != null && settings.Any(e => e != null && e.name == name);
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindDeep(root.GetChild(i), name);
                if (hit != null)
                {
                    return hit;
                }
            }
            return null;
        }

        static void CountMenu(BridgeContext ctx, out int toggles, out int sliders, out int puppets,
            out int colours)
        {
            toggles = sliders = puppets = colours = 0;
            var settings = ctx.CvrAvatar != null && ctx.CvrAvatar.avatarSettings != null
                ? ctx.CvrAvatar.avatarSettings.settings
                : null;
            if (settings == null)
            {
                return;
            }
            foreach (var entry in settings)
            {
                if (entry == null || string.IsNullOrEmpty(entry.name))
                {
                    continue;
                }
                switch (entry.type)
                {
                    case CVRAdvancedSettingsEntry.SettingsType.Toggle: toggles++; break;
                    case CVRAdvancedSettingsEntry.SettingsType.Dropdown: toggles++; break;
                    case CVRAdvancedSettingsEntry.SettingsType.Slider: sliders++; break;
                    case CVRAdvancedSettingsEntry.SettingsType.InputSingle: sliders++; break;
                    case CVRAdvancedSettingsEntry.SettingsType.Color: colours++; break;
                    case CVRAdvancedSettingsEntry.SettingsType.Joystick2D:
                    case CVRAdvancedSettingsEntry.SettingsType.Joystick3D:
                    case CVRAdvancedSettingsEntry.SettingsType.InputVector2:
                    case CVRAdvancedSettingsEntry.SettingsType.InputVector3: puppets++; break;
                }
            }
        }

        static int CountPhysics(BridgeContext ctx, out string physicsName)
        {
            physicsName = null;
            if (ctx.Target == null)
            {
                return 0;
            }

            // A grouped cloth holds several converted chains; each counts.
            int magica = CountByTypeName(ctx.Target, "MagicaCloth2.MagicaCloth");
            if (magica > 0 && ctx.ConvertedPhysicsChains != null)
            {
                var converted = ctx.ConvertedPhysicsChains.Where(c => c.Physics != null).ToList();
                magica += converted.Count - converted.Select(c => c.Physics).Distinct().Count();
            }
            if (magica > 0)
            {
                physicsName = "MagicaCloth 2";
                return magica;
            }

            int dynamic = CountByTypeName(ctx.Target, "DynamicBone");
            if (dynamic > 0)
            {
                physicsName = "DynamicBone";
                return dynamic;
            }
            return 0;
        }

        static int CountByTypeName(GameObject root, string typeName)
        {
            int found = 0;
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                // The live tissue's probe cloth is not a chain; name from YapsJelly.cs.
                if (behaviour != null && behaviour.GetType().FullName == typeName
                    && behaviour.gameObject.name != "Jelly Probes")
                {
                    found++;
                }
            }
            return found;
        }

        static void CountGeometry(BridgeContext ctx, out int triangles, out int materials,
            out int shapes)
        {
            triangles = shapes = 0;
            var distinct = new HashSet<Material>();
            if (ctx.Target == null)
            {
                materials = 0;
                return;
            }

            foreach (var skinned in ctx.Target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.sharedMesh != null)
                {
                    triangles += TriangleCount(skinned.sharedMesh);
                    shapes += skinned.sharedMesh.blendShapeCount;
                }
                Collect(skinned.sharedMaterials, distinct);
            }
            foreach (var filter in ctx.Target.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null)
                {
                    triangles += TriangleCount(filter.sharedMesh);
                }
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    Collect(renderer.sharedMaterials, distinct);
                }
            }
            materials = distinct.Count;
        }

        static void Collect(Material[] source, HashSet<Material> into)
        {
            if (source == null)
            {
                return;
            }
            foreach (var material in source)
            {
                if (material != null)
                {
                    into.Add(material);
                }
            }
        }

        internal static int TriangleCount(Mesh mesh)
        {
            int indices = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                if (mesh.GetTopology(i) == MeshTopology.Triangles)
                {
                    indices += (int)mesh.GetIndexCount(i);
                }
            }
            return indices / 3;
        }

        static string DisplayName(string name)
        {
            const string suffix = " (ChilloutVR)";
            if (name != null && name.EndsWith(suffix, System.StringComparison.Ordinal))
            {
                string trimmed = name.Substring(0, name.Length - suffix.Length).TrimEnd();
                if (trimmed.Length > 0)
                {
                    return trimmed;
                }
            }
            return name;
        }

        static string Plural(int count, string noun)
        {
            return count == 1 ? $"1 {noun}" : $"{count:N0} {noun}s";
        }

        static string Triangles(int count)
        {
            return count >= 10000 ? $"{count / 1000}k tris" : $"{count:N0} tris";
        }
    }
}
#endif
