// How a run ends, for a conversion and for a native setup.
//
// Both do different work and finish the same way: validate, read the
// avatar for its cards, write the description, write the report, save.
#if CVR_CCK_EXISTS
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class BridgeFinish
    {
        public static void Run(BridgeContext ctx, string reportName, string label)
        {
            BridgeDiagnostics.Run(ctx, ctx.MergedController);
            ReadAvatar(ctx);
            ctx.Report.StoreDescription = AvatarDescription.Write(ctx);
            WriteReport(ctx, reportName, label);
            EditorUtility.SetDirty(ctx.CvrAvatar);
            AssetDatabase.SaveAssets();
        }

        // The two cards, from one reading of the avatar: the weight card asks
        // the survey which blendshapes anything animates and which renderers
        // nothing can switch on, so building the model twice would be waste.
        //
        // Never allowed to take a finished avatar down with it.
        static void ReadAvatar(BridgeContext ctx)
        {
            if (ctx.CvrAvatar == null) return;
            if (!ctx.Settings.weighAvatar && !ctx.Settings.surveyAvatar) return;
            try
            {
                var survey = AvatarSurvey.Build(ctx.CvrAvatar);
                if (ctx.Settings.surveyAvatar)
                {
                    ctx.Report.SurveyCard = AvatarSurvey.Markdown(survey);
                    // Also as rows. The cards are sections of a file, and a
                    // file is not where anybody looks after pressing Convert:
                    // the window shows entries, so findings that exist only
                    // in the markdown are findings nobody sees.
                    AvatarSurvey.Fill(ctx.Report, survey);
                }
                if (ctx.Settings.weighAvatar)
                {
                    var weight = AvatarWeight.Measure(ctx.CvrAvatar, survey);
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
            File.WriteAllText(absolute, ctx.Report.ToMarkdown(ctx.Target.name));
            AssetDatabase.ImportAsset(path);
            ctx.Report.SavedReportPath = path;
            Debug.Log($"[AvatarBridge] {label} written to {path}");

            // Never allowed to take a finished avatar down: the markdown is
            // the deliverable, these two are a bonus, and a crash here would
            // lose both. Both used to be guarded on the VRChat SDK, which is
            // why a native setup never got them; neither needs it.
            try
            {
                DiagnosticsWriter.Write(ctx);
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
