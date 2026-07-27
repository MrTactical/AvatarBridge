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

        /// <summary>
        /// Builds the text and writes it next to the report. Returns it for the clipboard.
        ///
        /// The whole thing is inside one try, <em>including</em> building the string. This runs at
        /// the very end of a conversion, after every real piece of work has succeeded, and it
        /// produces a nicety — so there is no failure here worth losing an avatar over. It walks
        /// renderers, meshes and settings written by a dozen other passes; one unexpected null in
        /// any of them would otherwise throw out of the last line of the conversion and lose the
        /// lot. A missing description is a line in the report; a lost conversion is an evening.
        /// </summary>
        public static string Write(BridgeContext ctx)
        {
            string text = null;
            try
            {
                text = Build(ctx);
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
                    $"Could not produce the store description — {e.Message}. Nothing else about the " +
                    "conversion is affected; this only writes listing text you were free to write " +
                    "yourself. Worth reporting, since it should not happen.");
            }
            return text;
        }

        /// <summary>
        /// ChilloutVR's description box holds 256 characters — `max-length="256"` on the
        /// `input-description` field in the CCK's own `ContentBuilder2.uxml`. Nothing in the
        /// upload path enforces it, but that is the box people paste into, so anything longer is
        /// silently cut off mid-sentence.
        /// </summary>
        public const int MaxLength = 256;

        /// <summary>
        /// How much of the box to leave for the user's own words. The generated text is a footer,
        /// not the listing — taking the whole budget would make it one.
        /// </summary>
        const int ReservedForUser = 90;

        public static string Build(BridgeContext ctx)
        {
            // The credit is the fixed cost and gets measured first; features fill what is left.
            string credit = "Converted from VRChat with AvatarBridge\n" + ShortLink(BridgeLinks.Repo);
            string name = DisplayName(ctx.Target.name);

            // Two blank lines first, deliberately. Whoever pastes this almost always has something
            // of their own to say — who made the model, where it came from, what it costs — and a
            // block of generated text starting hard against the top of the box invites them to
            // either delete it or leave the listing sounding machine-written. The cursor lands in
            // the gap, and ReservedForUser keeps room for what they type there.
            const string gap = "\n\n";

            int budget = MaxLength - ReservedForUser - gap.Length - name.Length - credit.Length - 4;

            var kept = new List<string>();
            foreach (string feature in Features(ctx))
            {
                // " · " between entries, so each costs itself plus the separator.
                int cost = feature.Length + (kept.Count > 0 ? 3 : 0);
                if (cost > budget)
                {
                    continue;   // skip, don't stop — a later entry may still fit
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
            // Belt and braces: never hand back something the box would truncate, whatever the
            // arithmetic above did.
            return text.Length <= MaxLength ? text : text.Substring(0, MaxLength).TrimEnd();
        }

        /// <summary>The box is 256 characters; "https://" is eight of them for no information.</summary>
        static string ShortLink(string url)
        {
            return url.StartsWith("https://") ? url.Substring("https://".Length) : url;
        }

        /// <summary>
        /// The bullet list. Ordered by what someone browsing avatars actually cares about, and
        /// silent about anything the avatar hasn't got.
        /// </summary>
        static List<string> Features(BridgeContext ctx)
        {
            var lines = new List<string>();

            // Order matters: Build() fills its budget from the top and drops whatever will not
            // fit, so this is the marquee features first and the exhaustive counts after. A
            // heavily-toggled avatar was spending the whole box on "3 sliders · 1 joystick" and
            // never getting as far as saying it had face tracking at all.
            CountMenu(ctx, out int toggles, out int sliders, out int puppets, out int colours);
            if (toggles > 0) lines.Add(Plural(toggles, "toggle"));

            int chains = CountPhysics(ctx, out string physicsName);
            if (chains > 0)
            {
                lines.Add($"{Plural(chains, "physics chain")} ({physicsName})");
            }

            // Checked against the avatar, never against the setting that asked for it. A mode can
            // be selected and produce nothing — no compatible blendshapes, a mesh it couldn't read
            // — and a listing that claims face tracking on an avatar without the component is a
            // false advertisement written by this tool, under someone else's name. The setting is
            // a request; only the component is evidence.
            if (ctx.Target.GetComponentInChildren<CVRFaceTracking>(true) != null
                || (FindDeep(ctx.Target.transform, "EyeTracking.L") != null
                    && FindDeep(ctx.Target.transform, "EyeTracking.R") != null))
            {
                lines.Add("face tracking");
            }

            // Triangle count above the softer features: it is the one number that decides whether
            // someone can wear the avatar at all.
            CountGeometry(ctx, out int triangles, out int materials, out int shapes);
            if (triangles > 0)
            {
                lines.Add(Triangles(triangles));
            }

            // Same rule: the scaler is claimed only if its menu control actually reached the
            // avatar. Injection is skipped on rigs it can't measure.
            if (HasMenuEntry(ctx, "Height"))
            {
                lines.Add("height slider");
            }

            // Blink and lip sync are separate features on separate fields, and were being claimed
            // together off the blink flag alone. Each is now only stated if its own switch is on
            // AND it names a shape to drive — the flag can be set with an empty array.
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

        /// <summary>True when at least one entry in the array names a shape.</summary>
        static bool HasAny(string[] shapes)
        {
            return shapes != null && shapes.Any(s => !string.IsNullOrEmpty(s));
        }

        /// <summary>Is a named control actually on the finished avatar's menu?</summary>
        static bool HasMenuEntry(BridgeContext ctx, string name)
        {
            var settings = ctx.CvrAvatar != null && ctx.CvrAvatar.avatarSettings != null
                ? ctx.CvrAvatar.avatarSettings.settings
                : null;
            return settings != null && settings.Any(e => e != null && e.name == name);
        }

        /// <summary>Depth-first search by exact name, since these sit at a rig-dependent depth.</summary>
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

        /// <summary>"107k tris" rather than "107,050 triangles" — this is a 256-character box.</summary>
        static string Triangles(int count)
        {
            return count >= 10000 ? $"{count / 1000}k tris" : $"{count:N0} tris";
        }
    }
}
#endif
