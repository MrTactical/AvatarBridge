using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    /// <summary>
    /// Where users go for help, and the environment blob that makes a bug report useful.
    ///
    /// Nearly every bug in this tool has been diagnosed from a conversion report or a log,
    /// so the goal here is that a report arrives complete the first time: the issue opens
    /// pre-filled with versions and detected packages, and the user is told which log to
    /// attach for the kind of failure they hit.
    /// </summary>
    public static class BridgeLinks
    {
        public const string Repo = "https://github.com/MrTactical/AvatarBridge";
        public const string Releases = Repo + "/releases/latest";
        public const string Troubleshooting = Repo + "#install-troubleshooting";

        /// <summary>
        /// Maintainer's Discord handle for quick questions. Empty hides the button.
        /// </summary>
        public const string DiscordUser = "mrtactical";

        /// <summary>
        /// A server invite — "https://discord.gg/xxxx". The spam-resistant option, and the one
        /// to prefer: people ask in a channel where anyone can answer and where a nuisance can be
        /// removed, instead of arriving in the maintainer's DMs where they can't be.
        /// </summary>
        public const string DiscordInvite = "";

        /// <summary>
        /// Numeric Discord user id, which opens the profile card. Used only when there's no
        /// invite. Note this is the *id*, not the handle — "mrtactical" won't work.
        ///
        /// Worth being straight about the limit: Discord has no link that sends a friend request
        /// or opens a DM on someone's behalf, and won't, because that is exactly how you would
        /// build a spam tool. The furthest a link can go is opening the profile with the Add
        /// Friend button sitting there for the user to press themselves.
        /// </summary>
        public const string DiscordUserId = "846491891805847572";

        public static bool HasDiscordLink =>
            !string.IsNullOrEmpty(DiscordInvite) || !string.IsNullOrEmpty(DiscordUserId);

        /// <summary>
        /// Opens the invite, or the profile, or — with neither configured — falls back to putting
        /// the handle on the clipboard, which is all a username can ever do.
        /// </summary>
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

        /// <summary>Opens a bug report with the Environment field already filled in.</summary>
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

        /// <summary>
        /// Versions and detected packages — deliberately no file paths or user names, so
        /// this blob is safe to paste anywhere. (Unity/CVR *logs* do contain paths; the
        /// issue template warns about that separately.)
        /// </summary>
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
