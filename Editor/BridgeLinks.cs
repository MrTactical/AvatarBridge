using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    // Where users go for help, and the environment blob that makes a bug report useful.
    //
    // Nearly every bug in this tool has been diagnosed from a conversion report or a log,
    // so the goal here is that a report arrives complete the first time: the issue opens
    // pre-filled with versions and detected packages, and the user is told which log to
    // attach for the kind of failure they hit.
    public static class BridgeLinks
    {
        public const string Repo = "https://github.com/MrTactical/AvatarBridge";
        public const string Releases = Repo + "/releases/latest";
        // Anchor must match a real heading in README.md, or the button silently drops the user at
        // the top of the repo page; which is what it did for a long time, pointing at an
        // "#install-troubleshooting" section that never existed.
        public const string Troubleshooting = Repo + "#troubleshooting";

        public const string DiscordUser = "mrtactical";

        public const string DiscordInvite = "";

        public const string DiscordUserId = "846491891805847572";

        public static bool HasDiscordLink =>
            !string.IsNullOrEmpty(DiscordInvite) || !string.IsNullOrEmpty(DiscordUserId);

        public static void OpenDiscord()
        {
            if (!string.IsNullOrEmpty(DiscordInvite))
            {
                Application.OpenURL(DiscordInvite);
                return;
            }
            if (!string.IsNullOrEmpty(DiscordUserId))
            {
                Application.OpenURL("https://discord.com/users/" + DiscordUserId);
                return;
            }
            CopyDiscord();
        }

        public static void CopyDiscord()
        {
            EditorGUIUtility.systemCopyBuffer = DiscordUser;
        }

        public static void OpenBugReport(BridgeReport report = null)
        {
            string url = Repo + "/issues/new?template=bug_report.yml&environment=" +
                         Uri.EscapeDataString(BuildDiagnostics(report));
            Application.OpenURL(url);
        }

        public static void CopyDiagnostics(BridgeReport report = null)
        {
            EditorGUIUtility.systemCopyBuffer = BuildDiagnostics(report);
        }

        public static string BuildDiagnostics(BridgeReport report = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"AvatarBridge:  {BridgeDefines.Version}");
            sb.AppendLine($"Unity:         {Application.unityVersion}");
            sb.AppendLine($"VRChat SDK3:   {(BridgeDefines.HasVrcAvatarSdk ? "yes" : "NOT FOUND")}");
            sb.AppendLine($"ChilloutVR CCK:{(BridgeDefines.HasCck ? " yes" : " NOT FOUND")}");
            sb.AppendLine($"MagicaCloth2:  {(BridgeDefines.HasMagicaCloth2 ? "yes" : "no")}");
            sb.AppendLine($"DynamicBone:   {(BridgeDefines.HasDynamicBone ? "yes" : "no")}");

            if (report != null)
            {
                sb.AppendLine();
                sb.AppendLine($"Last run:      {report.CountOf(ReportStatus.Converted)} converted, " +
                              $"{report.CountOf(ReportStatus.Approximated)} approximated, " +
                              $"{report.CountOf(ReportStatus.Skipped)} skipped, " +
                              $"{report.CountOf(ReportStatus.Warning)} warnings, " +
                              $"{report.CountOf(ReportStatus.Error)} errors");
            }
            return sb.ToString();
        }
    }
}
