using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    // Help links and a pre-filled bug report (versions, detected packages, which log to attach),
    // since nearly every bug is diagnosed from a report or a log.
    public static class BridgeLinks
    {
        public const string Repo = "https://github.com/MrTactical/AvatarBridge";
        public const string Releases = Repo + "/releases/latest";

        // Where the converter sends someone without the add-on. Both
        // downloads sit on the same release page; point this at its own repo
        // the day YAPS has one.
        public const string YapsRepo = Releases;

        // And the other way round: is the converter here? The YAPS tool asks
        // before it names a menu that may not exist.
        public static bool HasAvatarBridge =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetType("AvatarBridge.AvatarBridgeWindow", false) != null);
        // Anchor must match a real README heading, or the button silently lands at the page top.
        public const string Troubleshooting = Repo + "#troubleshooting";

        public const string DiscordUser = "mrtactical";

        public const string DiscordInvite = "";

        public const string DiscordUserId = "846491891805847572";

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

        // The YAPS tool's own chapter in the README, and its bug report:
        // the same issue tracker while the tool ships inside AvatarBridge,
        // with the environment blob saying which tool it came from.
        public const string YapsHelp = Repo + "#yaps-penetration-that-works-in-chilloutvr";

        public static void OpenYapsBugReport(string last = null)
        {
            string url = Repo + "/issues/new?template=bug_report.yml&environment=" +
                         Uri.EscapeDataString(YapsDiagnostics(last));
            Application.OpenURL(url);
        }

        // What the YAPS window last did goes after the versions, since the tool writes no report.
        public static string YapsDiagnostics(string last) =>
            "Tool:          YAPS (Tools ▸ Avatar Bridge ▸ YAPS ▸ Setup)\n" + BuildDiagnostics(null) + last;

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
            // Without the add-on penetration is removed, not converted.
            sb.AppendLine($"YAPS add-on:   {(BridgeDefines.HasYaps ? "yes" : "no")}");
            sb.AppendLine($"Machine:       {SystemInfo.processorType.Trim()}, {SystemInfo.processorCount} threads, {SystemInfo.systemMemorySize / 1024f:0.#} GB");

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
