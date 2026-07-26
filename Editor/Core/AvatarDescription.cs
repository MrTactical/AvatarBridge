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
    /// <summary>
    /// Writes a store-listing description for the converted avatar, out of what the conversion
    /// actually produced.
    ///
    /// It is written to a file and offered on the clipboard rather than filled into the CCK's field
    /// directly, and that is not laziness — the CCK has nowhere durable to put one. `CVRAssetInfo`,
    /// the per-avatar component, carries no description; the Content Manager keeps it in Unity's
    /// `SessionState` under `CCK.Builder.Description`, which is lost on an editor restart, and
    /// `BuilderTab.SelectContent` calls `ClearFields()` whenever the chosen content changes — so a
    /// value written here would be wiped by the CCK itself the moment the user picked their avatar
    /// in the Builder tab. Reaching into another tool's session keys to lose the race anyway is
    /// worse than handing over text that is one click from pasted.
    ///
    /// Everything it claims is counted from the finished avatar, so it cannot drift from what was
    /// built. Lines that would read as zero are left out entirely rather than printed as "0".
    /// </summary>
    public static class AvatarDescription
    {
        const string Category = "Store description";
        public const string FileName = "Description.txt";

        /// <summary>Builds the text and writes it next to the report. Returns it for the clipboard.</summary>
        public static string Write(BridgeContext ctx)
        {
            string text = Build(ctx);
            try
            {
                string relative = ctx.OutputDir.TrimEnd('/') + "/" + FileName;
                File.WriteAllText(Path.GetFullPath(relative), text);
                UnityEditor.AssetDatabase.ImportAsset(relative);
                ctx.Report.Converted(Category, ctx.Target.name,
                    $"Wrote a ready-made store description to {FileName} — \"Copy description\" in the " +
                    "AvatarBridge window puts it on your clipboard for the CCK's Description box. It " +
                    "starts with a blank line or two for your own words, so it reads as the footer of " +
                    "your description rather than all of it. Everything below that is counted from " +
                    "this avatar; edit any of it before uploading.");
            }
            catch (System.Exception e)
            {
                ctx.Report.Warning(Category, ctx.Target.name,
                    $"Could not write {FileName} — {e.Message}. The description is still available " +
                    "from the window's \"Copy description\" button.");
            }
            return text;
        }

        public static string Build(BridgeContext ctx)
        {
            var sb = new StringBuilder();

            // Two blank lines first, deliberately. Whoever pastes this almost always has something
            // of their own to say — who made the model, where it came from, what it costs — and a
            // block of generated text starting hard against the top of the box invites them to
            // either delete it or leave the listing sounding machine-written. Opening with room to
            // type turns this into the footer of their description rather than the whole of it,
            // and the cursor lands in the gap.
            sb.AppendLine();
            sb.AppendLine();

            sb.AppendLine(DisplayName(ctx.Target.name));
            sb.AppendLine();

            var features = Features(ctx);
            if (features.Count > 0)
            {
                foreach (string line in features)
                {
                    sb.AppendLine("• " + line);
                }
                sb.AppendLine();
            }

            sb.AppendLine("Converted from VRChat with AvatarBridge, a free and open-source");
            sb.AppendLine("VRChat to ChilloutVR avatar converter.");
            sb.Append(BridgeLinks.Repo);
            return sb.ToString();
        }

        /// <summary>
        /// The bullet list. Ordered by what someone browsing avatars actually cares about, and
        /// silent about anything the avatar hasn't got.
        /// </summary>
        static List<string> Features(BridgeContext ctx)
        {
            var lines = new List<string>();

            CountMenu(ctx, out int toggles, out int sliders, out int puppets, out int colours);
            var parts = new List<string>();
            if (toggles > 0) parts.Add(Plural(toggles, "toggle"));
            if (sliders > 0) parts.Add(Plural(sliders, "slider"));
            if (puppets > 0) parts.Add(Plural(puppets, "joystick"));
            if (colours > 0) parts.Add(Plural(colours, "colour picker"));
            if (parts.Count > 0)
            {
                lines.Add("Customisable — " + Join(parts) + " on the avatar menu");
            }

            int chains = CountPhysics(ctx, out string physicsName);
            if (chains > 0)
            {
                lines.Add($"Physics on {Plural(chains, "bone chain")} ({physicsName})");
            }

            if (ctx.Settings.faceTrackingMode != FaceTrackingMode.None)
            {
                lines.Add(ctx.Settings.faceTrackingMode == FaceTrackingMode.Native
                    ? "Face tracking ready — ChilloutVR's native component"
                    : "Face tracking ready — eye and face blendtrees");
            }

            if (ctx.CvrAvatar != null && ctx.CvrAvatar.useBlinkBlendshapes)
            {
                lines.Add("Blinks and lip syncs on its own");
            }

            if (ctx.Settings.addAvatarScaler)
            {
                lines.Add("Resizable in game — set your height in real metres");
            }

            CountGeometry(ctx, out int triangles, out int materials, out int shapes);
            if (triangles > 0)
            {
                string geo = $"{triangles:N0} triangles";
                if (materials > 0) geo += $", {Plural(materials, "material")}";
                lines.Add(geo);
            }
            if (shapes > 0)
            {
                lines.Add($"{Plural(shapes, "blendshape")} for expressions");
            }

            return lines;
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

        /// <summary>
        /// Counts whichever physics system was actually written, by name, so the description can
        /// say which one without the caller knowing. Both are looked up reflectively because
        /// either package may be absent from the project.
        /// </summary>
        static int CountPhysics(BridgeContext ctx, out string physicsName)
        {
            physicsName = null;
            if (ctx.Target == null)
            {
                return 0;
            }

            int magica = CountByTypeName(ctx.Target, "MagicaCloth2.MagicaCloth");
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
                if (behaviour != null && behaviour.GetType().FullName == typeName)
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

        static int TriangleCount(Mesh mesh)
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

        /// <summary>
        /// The conversion appends " (ChilloutVR)" to tell the copy apart from the original in the
        /// scene. That is a working name, not something anyone wants heading their store listing.
        /// </summary>
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

        /// <summary>"a, b and c" — an Oxford-comma-free list, because this is prose.</summary>
        static string Join(List<string> parts)
        {
            if (parts.Count == 1) return parts[0];
            if (parts.Count == 2) return parts[0] + " and " + parts[1];
            return string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts.Last();
        }
    }
}
#endif
