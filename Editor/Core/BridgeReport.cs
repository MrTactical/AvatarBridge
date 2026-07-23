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

    /// <summary>
    /// Collects everything that happened during a conversion so the user gets a single
    /// honest summary of what carried over, what was approximated and what was lost.
    /// </summary>
    public class BridgeReport
    {
        public readonly List<ReportEntry> Entries = new List<ReportEntry>();

        public void Add(ReportStatus status, string category, string subject, string detail = "")
        {
            Entries.Add(new ReportEntry { Status = status, Category = category, Subject = subject, Detail = detail });

            string message = $"[AvatarBridge] {status}: [{category}] {subject}" +
                             (string.IsNullOrEmpty(detail) ? "" : " - " + detail);
            switch (status)
            {
                case ReportStatus.Error:
                    Debug.LogError(message);
                    break;
                case ReportStatus.Warning:
                case ReportStatus.Skipped:
                    Debug.LogWarning(message);
                    break;
                default:
                    Debug.Log(message);
                    break;
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
            return sb.ToString();
        }
    }
}
