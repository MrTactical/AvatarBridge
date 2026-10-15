// What moved between this conversion and the last one of the same avatar.
// The output folder keeps a plain list of what the conversion produced, and
// the next run compares against it, so reconverting on a new release or
// after changing one setting says what it changed. The file is dot-named,
// so Unity never imports it.
//
// Report details are left out on purpose: their wording improves between
// releases without the conversion changing. Category and subject carry the
// behaviour, the same rule the regression digests follow.
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

        public static void Run(BridgeContext ctx)
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
                Compare(ctx.Report, before, now, when);
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

        internal static void Compare(BridgeReport report, string[] before, List<string> now, DateTime when)
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

            var settings = changed.Where(c => c.StartsWith("setting ", StringComparison.Ordinal) || c.StartsWith("version ", StringComparison.Ordinal))
                .Select(c => c.Substring(c.IndexOf(' ') + 1)).ToList();
            var kinds = changed.Concat(gone).Concat(added).Select(l => l.Substring(0, l.IndexOf(' ')))
                .Where(k => k != "setting" && k != "version" && k != "count")
                .GroupBy(k => k).Select(g => BridgeElements.Count(g.Count(), Noun(g.Key, false), Noun(g.Key, true))).ToList();
            string summary = kinds.Count > 0 ? string.Join(", ", kinds) + " changed" : "only the counts moved";
            report.Converted(Category, $"Since the last conversion: {summary}",
                (settings.Count > 0 ? $"You changed {string.Join("; ", settings)}. " : "") +
                $"Compared with {since}. The full list is in ConversionReport.md, under Since the last conversion.");

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
