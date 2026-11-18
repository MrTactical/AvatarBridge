// The shared end of a conversion and a native setup.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class BridgeFinish
    {
        public static void Run(BridgeContext ctx, string reportName, string label)
        {
            // What each step costs, in the log and never the report, whose
            // lines must not move between two runs of the same avatar.
            var times = new List<string>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            void Lap(string step)
            {
                times.Add($"{step} {clock.ElapsedMilliseconds}");
                clock.Restart();
            }

            UploadPreflight.Run(ctx.Report, ctx.CvrAvatar);
            Lap("UploadPreflight");
            BridgeDiagnostics.Run(ctx, ctx.MergedController);
            Lap("Diagnostics");
            ReadAvatar(ctx);
            Lap("ReadAvatar");
            ctx.Report.StoreDescription = AvatarDescription.Write(ctx);
            Lap("Description");
            // Last before writing, so it compares everything the report says.
            ConversionDiff.Run(ctx, reportName);
            Lap("ConversionDiff");
            WriteReport(ctx, reportName, label);
            Lap("WriteReport");
            EditorUtility.SetDirty(ctx.CvrAvatar);
            AssetDatabase.SaveAssets();
            Lap("SaveAssets");
            Debug.Log("[AvatarBridge] Report step (ms): " + string.Join(", ", times));
        }

        // One reading for both cards: the weight card asks the survey what is animated and
        // switchable. Never allowed to take a finished avatar down with it.
        static void ReadAvatar(BridgeContext ctx)
        {
            if (ctx.CvrAvatar == null) return;
            if (!ctx.Settings.weighAvatar && !ctx.Settings.surveyAvatar) return;
            try
            {
                var survey = AvatarSurvey.Build(ctx.CvrAvatar);
                survey.RenamedFrom = ctx.RenamedFrom;
                if (ctx.Settings.surveyAvatar)
                {
                    ctx.Report.SurveyCard = AvatarSurvey.Markdown(survey);
                    // Also as rows: the window shows entries, and markdown-only findings go unseen.
                    AvatarSurvey.Fill(ctx.Report, survey);
                }
                if (ctx.Settings.weighAvatar)
                {
                    var weight = AvatarWeight.Measure(ctx.CvrAvatar, survey);
                    AvatarWeight.NoteLeftAlone(weight, ctx.SlimLeftAlone);
                    ctx.Report.WeightCard = AvatarWeight.Markdown(weight);
                    AvatarWeight.Fill(ctx.Report, weight);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarBridge] Could not read the avatar for its report: {e.Message}");
            }
        }

        static void WriteReport(BridgeContext ctx, string reportName, string label)
        {
            string path = ctx.OutputDir + "/" + reportName;
            string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            File.WriteAllText(absolute, ctx.Report.ToMarkdown(ctx.Target.name), DiagnosticsWriter.Marked);
            AssetDatabase.ImportAsset(path);
            ctx.Report.SavedReportPath = path;
            Debug.Log($"[AvatarBridge] {label} written to {path}");

            // Guarded: the markdown is the deliverable and a crash here would lose it.
            try
            {
                DiagnosticsWriter.Write(ctx, reportName);
                // The web report renders the same entries drawn. Written last
                // so it can show everything, the two cards included.
                HtmlReportWriter.Write(ctx);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarBridge] Diagnostics could not be written: {e}");
            }
        }
    }
}
#endif
