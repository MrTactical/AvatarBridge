// What changed since the last conversion of this avatar, from a dot-named list (never
// imported) in the output folder. Details are left out: their wording changes between
// releases without the conversion changing.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AvatarBridge
{
    public static class ConversionDiff
    {
        const string FileName = ".last-conversion.txt";
        const string Category = "Changes";

        public static void Run(BridgeContext ctx, string reportName = "ConversionReport.md")
        {
            try
            {
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ctx.OutputDir, FileName));
                var now = Snapshot(ctx);
                string[] before = File.Exists(path) ? File.ReadAllLines(path) : null;
                DateTime when = before != null ? File.GetLastWriteTime(path) : default;
                File.WriteAllLines(path, now);
                // Always one entry, so the count of converted lines does not move
                // between a first conversion and a second.
                if (before == null)
                {
                    ctx.Report.Converted(Category, "First conversion of this avatar into this folder",
                        "The next conversion here will say what changed since this one.");
                    return;
                }
                Compare(ctx.Report, before, now, when, reportName);
            }
            catch (Exception e)
            {
                ctx.Report.Approximated(Category, "Could not compare with the last conversion", e.Message);
            }
        }

        // One fact per line, "kind key" then " = value" for facts that change
        // value in place, so a changed setting reads as one change, not two.
        static List<string> Snapshot(BridgeContext ctx)
        {
            var lines = new List<string> { $"version AvatarBridge = {BridgeDefines.Version}" };
            foreach (var f in typeof(BridgeSettings).GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.Name))
                lines.Add($"setting {f.Name} = {f.GetValue(ctx.Settings)}");

            foreach (ReportStatus status in Enum.GetValues(typeof(ReportStatus)))
                lines.Add($"count {status} = {ctx.Report.CountOf(status)}");
            lines.AddRange(ctx.Report.Entries.Where(e => e.Status != ReportStatus.Converted)
                .Select(e => $"report {e.Status} [{e.Category}] {Stable(e.Subject)}"));

            var controller = ctx.MergedController;
            if (controller != null)
            {
                lines.AddRange(controller.layers.Select(l =>
                    $"layer {Stable(l.name)} · {BridgeDiagnostics.CountStates(l.stateMachine)} states · weight {l.defaultWeight:0.##} · " +
                    $"{l.blendingMode}{(l.avatarMask != null ? " · mask " + l.avatarMask.name : "")}"));
                lines.AddRange(controller.parameters.Select(p => $"parameter {Stable(p.name)} : {p.type}"));
            }
            var menu = ctx.CvrAvatar != null && ctx.CvrAvatar.avatarSettings != null ? ctx.CvrAvatar.avatarSettings.settings : null;
            if (menu != null)
                lines.AddRange(menu.Where(e => e != null).Select(e => $"menu {Stable(e.name)} ({Stable(e.machineName)}, {e.type})"));

            if (ctx.Target != null)
                lines.AddRange(ctx.Target.GetComponentsInChildren<Component>(true).Where(c => c != null)
                    .GroupBy(c => c.GetType().Name).Select(g => $"component {g.Key} = {g.Count()}"));

            return lines.Distinct().OrderBy(l => l, StringComparer.Ordinal).ToList();
        }

        internal static void Compare(BridgeReport report, string[] before, List<string> now, DateTime when,
            string reportName = "ConversionReport.md")
        {
            var was = Keyed(before);
            var isNow = Keyed(now);
            var changed = new List<string>();
            var gone = new List<string>();
            var added = new List<string>();
            foreach (var pair in isNow)
            {
                if (!was.TryGetValue(pair.Key, out var old)) added.Add(pair.Key + Value(pair.Value));
                else if (old != pair.Value) changed.Add($"{pair.Key}: {old ?? "-"} to {pair.Value ?? "-"}");
            }
            gone.AddRange(was.Keys.Where(k => !isNow.ContainsKey(k)).Select(k => k + Value(was[k])));

            string since = $"the last conversion ({when:yyyy-MM-dd HH:mm})";
            if (changed.Count + gone.Count + added.Count == 0)
            {
                report.Converted(Category, "Nothing changed since the last conversion",
                    $"Same settings, report lines, layers, parameters, menu and components as {since}.");
                return;
            }

            var settings = changed.Where(c => c.StartsWith("setting ", StringComparison.Ordinal))
                .Select(c => Readable(c.Substring(8))).ToList();
            var version = changed.FirstOrDefault(c => c.StartsWith("version ", StringComparison.Ordinal));
            var kinds = changed.Concat(gone).Concat(added).Select(l => l.Substring(0, l.IndexOf(' ')))
                .Where(k => k != "setting" && k != "version" && k != "count")
                .GroupBy(k => k).Select(g => BridgeElements.Count(g.Count(), Noun(g.Key, false), Noun(g.Key, true))).ToList();
            bool counts = changed.Concat(gone).Concat(added).Any(l => l.StartsWith("count ", StringComparison.Ordinal));
            string summary = kinds.Count > 0 ? string.Join(", ", kinds) + " changed"
                : counts ? "only the counts moved" : "only settings or the version changed";
            report.Converted(Category, $"Since the last conversion: {summary}",
                (version != null ? $"Updated {version.Substring(8)}. " : "") +
                (settings.Count > 0 ? $"You changed {string.Join("; ", settings)}. " : "") +
                $"Compared with {since}. The full list is in {reportName}, under Since the last conversion.");

            var sb = new StringBuilder();
            sb.AppendLine($"Compared with {since}.");
            sb.AppendLine();
            sb.AppendLine("```diff");
            foreach (var l in changed) sb.AppendLine("~ " + l);
            foreach (var l in gone) sb.AppendLine("- " + l);
            foreach (var l in added) sb.AppendLine("+ " + l);
            sb.AppendLine("```");
            report.ChangesCard = sb.ToString();
        }

        static Dictionary<string, string> Keyed(IEnumerable<string> lines)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in lines)
            {
                int eq = line.IndexOf(" = ", StringComparison.Ordinal);
                if (eq < 0) map[line] = null;
                else map[line.Substring(0, eq)] = line.Substring(eq + 3);
            }
            return map;
        }

        static string Value(string v) => v == null ? "" : " = " + v;

        // "field: old to new" with the window's label for the field and on/off for a tick.
        static string Readable(string change)
        {
            int colon = change.IndexOf(": ", StringComparison.Ordinal);
            string field = change.Substring(0, colon), values = change.Substring(colon + 2);
            values = Regex.Replace(values, @"\b(True|False)\b", m => m.Value == "True" ? "on" : "off");
            return (WindowLabels.TryGetValue(field, out var label) ? label : field) + ": " + values;
        }

        // The converter window's own labels, "(recommended)" left off. ConversionDiffTest
        // fails when one stops appearing in the window.
        internal static readonly Dictionary<string, string> WindowLabels = new Dictionary<string, string>
        {
            ["cloneAvatar"] = "Work on a clone",
            ["outputFolder"] = "Output folder",
            ["linkConvertedPrefab"] = "Keep the converted avatar a prefab",
            ["convertBaseLayer"] = "Base / locomotion",
            ["convertAdditiveLayer"] = "Additive",
            ["convertGestureLayer"] = "Gesture (hand poses)",
            ["convertActionLayer"] = "Action (emotes, AFK)",
            ["convertFxLayer"] = "FX (toggles, expressions)",
            ["toggleStyle"] = "Toggle style",
            ["preserveParameterSyncState"] = "Preserve parameter sync state",
            ["exposeMenulessSyncedParameters"] = "Expose menu-less synced parameters",
            ["physicsTarget"] = "Convert PhysBones to",
            ["deleteConvertedPhysBones"] = "Delete PhysBones after converting",
            ["grabbyBonesSupport"] = "GrabbyBones mod support",
            ["groupIdenticalPhysBones"] = "Group identical PhysBone chains",
            ["useMagicaPresets"] = "Match a preset to each chain",
            ["fitToPhysBone"] = "Fit the preset to the PhysBone",
            ["derivePhysicsFromPhysBone"] = "Derive physics from the PhysBone",
            ["fitRadiusToMesh"] = "Size particles from the mesh",
            ["fitCollidersToMesh"] = "Fit colliders to the mesh",
            ["sizePhysicsForLargest"] = "Size for the largest a slider makes the body",
            ["boundSwingToSourceLimit"] = "Bound swing to the source's limit",
            ["capParticleRadius"] = "Cap particle radius to bone spacing",
            ["convertToePhysBones"] = "Convert toe PhysBones",
            ["autoAssignNearbyColliders"] = "Auto-assign nearby colliders",
            ["addPhysicsToRiggedStyles"] = "Add physics to toggled rigs that have none",
            ["weighAvatar"] = "Say what this avatar costs",
            ["surveyAvatar"] = "Say what this avatar does",
            ["stripGogoLoco"] = "Remove GoGo Loco",
            ["gogoEmotes"] = "Put GoGo poses in the emote wheel",
            ["stripSpsSystems"] = "Penetration",
            ["convertYapsSystems"] = "Penetration",
            ["keepHapticsContacts"] = "Keep the OGB / PCS haptics contacts",
            ["syncHapticsForOsc"] = "Keep OGB haptics synced (OSCGoesBrrr, Lovense)",
            ["syncSocketDepthForOthers"] = "Show the avatar's OWN depth animations to other players",
            ["squashSoftParts"] = "Soft body shape: squash where parts press together",
            ["extraStripKeywords"] = "Extra strip keywords",
            ["stripDeadMaterialAnimation"] = "Remove animation that can't do anything",
            ["convertContacts"] = "Convert contact senders/receivers",
            ["createDefaultColliderPointers"] = "Recreate built-in VRC colliders as pointers",
            ["sizeContactZonesForLargest"] = "Grow contact zones with the body's sliders",
            ["slimTexturesOnConvert"] = "Resize oversized textures",
            ["patchNonSpiShaders"] = "Patch non-SPI shaders for VR",
            ["convertConstraints"] = "Convert VRC constraints",
            ["convertHeadChop"] = "Convert VRC Head Chop",
            ["convertSpatialAudio"] = "Convert spatial audio",
            ["wireBlinkBlendshapes"] = "Auto-wire blink blendshapes",
            ["addAvatarScaler"] = "Add height scaler",
            ["faceTrackingMode"] = "Face tracking",
        };

        static string Noun(string kind, bool many) =>
            kind == "report" ? (many ? "report lines" : "report line")
            : kind == "menu" ? (many ? "menu entries" : "menu entry")
            : many ? kind + "s" : kind;

        // Numbers VRCFury stamps fresh on every bake.
        static string Stable(string s) => string.IsNullOrEmpty(s) ? s
            : Regex.Replace(Regex.Replace(Regex.Replace(s, @"VF_\d+_", "VF_#_"), @"\[VF\d+\]", "[VF#]"), @"\bVF\d+_", "VF#_");
    }
}
#endif
