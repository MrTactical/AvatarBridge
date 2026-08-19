// How a run ends, once, for both of them.
//
// A conversion and a native setup do entirely different work and then finish
// identically: validate what was built, read the avatar for the report, write
// the store description, write the report, save. That tail existed twice, and
// twice this week a change to it had to be made in both places — the second
// time by copying a method verbatim, which is what prompted this file.
//
// What stays behind: the converter's diagnostics and HTML report. Both are
// guarded on the VRChat SDK, so a file that ships in the standalone YAPS
// package cannot call them. The converter passes them in instead.
#if CVR_CCK_EXISTS
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class BridgeFinish
    {
        // `extras` runs after the markdown report exists and is allowed to
        // fail: the report is the deliverable, anything else is a bonus, and
        // a crash there would lose both.
        public static void Run(BridgeContext ctx, string reportName, string label, Action<BridgeContext> extras = null)
        {
            BridgeDiagnostics.Run(ctx, ctx.MergedController);
            ReadAvatar(ctx);
            ctx.Report.StoreDescription = AvatarDescription.Write(ctx);
            WriteReport(ctx, reportName, label, extras);
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
                if (ctx.Settings.surveyAvatar) ctx.Report.SurveyCard = AvatarSurvey.Markdown(survey);
                if (ctx.Settings.weighAvatar)
                {
                    ctx.Report.WeightCard = AvatarWeight.Markdown(AvatarWeight.Measure(ctx.CvrAvatar, survey));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarBridge] Could not read the avatar for its report: {e.Message}");
            }
        }

        static void WriteReport(BridgeContext ctx, string reportName, string label, Action<BridgeContext> extras)
        {
            string path = ctx.OutputDir + "/" + reportName;
            string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            File.WriteAllText(absolute, ctx.Report.ToMarkdown(ctx.Target.name));
            AssetDatabase.ImportAsset(path);
            ctx.Report.SavedReportPath = path;
            Debug.Log($"[AvatarBridge] {label} written to {path}");

            if (extras == null) return;
            try
            {
                extras(ctx);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarBridge] Diagnostics could not be written: {e}");
            }
        }
    }
}
#endif
