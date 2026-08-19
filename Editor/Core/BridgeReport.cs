using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AvatarBridge
{
    public enum ReportStatus
    {
        Converted,     // 1:1 or near-1:1 conversion
        Approximated,  // converted, but behaviour may differ; detail explains how
        Skipped,       // feature exists on the avatar but has no CVR equivalent
        Warning,       // something the user should look at
        Error          // conversion step failed
    }

    public class ReportEntry
    {
        public ReportStatus Status;
        public string Category; // e.g. "PhysBones", "Animator", "Menu"
        public string Subject;  // e.g. object path or parameter name
        public string Detail;
    }

    // Orders report samples so the same example shows every run.
    //
    // VRCFury stamps generated objects with an id, "[VF397] Assjob", and
    // assigns it fresh on every bake. Sorting raw text therefore reorders
    // whenever Fury renumbers, though nothing about the avatar changed.
    //
    // So ordering ignores those ids: two strings differing only by a Fury number sort as equal
    // text and fall back to a plain comparison, which keeps the set stable and total.
    public sealed class StableSampleOrder : IComparer<string>
    {
        public static readonly StableSampleOrder Instance = new StableSampleOrder();

        // VRCFury's per-bake numbers, and the GUIDs some tools stamp into an
        // object name, which change on every bake for the same reason.
        static readonly System.Text.RegularExpressions.Regex FuryIds =
            new System.Text.RegularExpressions.Regex(
                @"\[VF\d+\]|\bVF\d+_|VF_\d+_|\$[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        public static string Key(string value) =>
            string.IsNullOrEmpty(value) ? value : FuryIds.Replace(value, "#");

        public int Compare(string x, string y)
        {
            int byKey = string.CompareOrdinal(Key(x), Key(y));
            // Never return 0 for different strings: a SortedSet would silently discard one.
            return byKey != 0 ? byKey : string.CompareOrdinal(x, y);
        }
    }

    // Collects everything that happened during a conversion so the user gets a single
    // honest summary of what carried over, what was approximated and what was lost.
    public class BridgeReport
    {
        public readonly List<ReportEntry> Entries = new List<ReportEntry>();

        public string SavedReportPath;

        public string SavedHtmlPath;

        public string StoreDescription;

        public string Appendix;

        public string WeightCard;

        public string SurveyCard;

        [System.NonSerialized] public GameObject ConvertedRoot;

        public void Add(ReportStatus status, string category, string subject, string detail = "")
        {
            Entries.Add(new ReportEntry { Status = status, Category = category, Subject = subject, Detail = detail });

            // Only what the user has to act on reaches the console. Everything is in the report
            // file regardless, which is the thing worth reading.
            //
            // Every entry used to be logged. The editor captures a full managed stack trace for
            // each Debug call, so a large conversion; which routinely produces thousands of
            // entries, one per cloth chain, parameter and menu control; wrote tens of thousands
            // of stack frames into the editor log and pushed it past 38 MB. Skipped in particular
            // was a warning, and a conversion legitimately skips a great deal.
            if (status != ReportStatus.Error && status != ReportStatus.Warning)
            {
                return;
            }

            string message = $"[AvatarBridge] {status}: [{category}] {subject}" +
                             (string.IsNullOrEmpty(detail) ? "" : " - " + detail);
            if (status == ReportStatus.Error)
            {
                Debug.LogError(message);
            }
            else
            {
                Debug.LogWarning(message);
            }
        }

        public void Converted(string category, string subject, string detail = "") => Add(ReportStatus.Converted, category, subject, detail);
        public void Approximated(string category, string subject, string detail = "") => Add(ReportStatus.Approximated, category, subject, detail);
        public void Skipped(string category, string subject, string detail = "") => Add(ReportStatus.Skipped, category, subject, detail);
        public void Warning(string category, string subject, string detail = "") => Add(ReportStatus.Warning, category, subject, detail);
        public void Error(string category, string subject, string detail = "") => Add(ReportStatus.Error, category, subject, detail);

        public int CountOf(ReportStatus status) => Entries.Count(e => e.Status == status);

        public string ToMarkdown(string avatarName)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# AvatarBridge conversion report: {avatarName}");
            sb.AppendLine();
            // Stamp the build that produced this. A report is the main thing users send when
            // something's wrong, and without this there's no way to tell which version ran .
            // which has already cost time chasing a "broken" fix that simply wasn't installed.
            sb.AppendLine($"*AvatarBridge v{BridgeDefines.Version} · Unity {Application.unityVersion} · " +
                          $"{System.DateTime.Now:yyyy-MM-dd HH:mm}*");
            sb.AppendLine();
            sb.AppendLine($"Converted: {CountOf(ReportStatus.Converted)} | " +
                          $"Approximated: {CountOf(ReportStatus.Approximated)} | " +
                          $"Skipped: {CountOf(ReportStatus.Skipped)} | " +
                          $"Warnings: {CountOf(ReportStatus.Warning)} | " +
                          $"Errors: {CountOf(ReportStatus.Error)}");
            sb.AppendLine();

            foreach (var group in Entries.GroupBy(e => e.Category))
            {
                sb.AppendLine($"## {group.Key}");
                sb.AppendLine();
                foreach (var entry in group)
                {
                    sb.Append($"- **{entry.Status}** — {entry.Subject}");
                    if (!string.IsNullOrEmpty(entry.Detail))
                    {
                        sb.Append($" — {entry.Detail}");
                    }
                    sb.AppendLine();
                }
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(SurveyCard))
            {
                sb.AppendLine("## What this avatar does");
                sb.AppendLine();
                sb.Append(SurveyCard);
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(WeightCard))
            {
                sb.AppendLine("## What this avatar costs");
                sb.AppendLine();
                sb.Append(WeightCard);
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(Appendix))
            {
                sb.AppendLine("<details>");
                sb.AppendLine("<summary><b>Diagnostics</b> — what the converted avatar actually " +
                              "contains. Please leave this in when reporting a bug.</summary>");
                sb.AppendLine();
                sb.Append(Appendix);
                sb.AppendLine();
                sb.AppendLine("</details>");
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
