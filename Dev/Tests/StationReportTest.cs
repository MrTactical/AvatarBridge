#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Known-answer test for the seat goodbye (task #28). VRCStation is the sit-on-me chair; the
    // decompiled client's avatar whitelist has no seat type, so the honest ceiling is a Skipped
    // entry naming each; counted after the strips, so GoGo Loco's own stations never alarm
    // anyone (that half is verified in the wild on a GoGo corpus avatar).
    public static class StationReportTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test — seat report")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[StationReportTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            GameObject avatar = null;
            try
            {
                avatar = new GameObject("__StationReportTest");
                var seat = new GameObject("TailSeat");
                seat.transform.SetParent(avatar.transform, false);
                seat.AddComponent<VRC.SDK3.Avatars.Components.VRCStation>();

                var ctx = new BridgeContext
                {
                    Target = avatar,
                    Report = new BridgeReport(),
                    Settings = new BridgeSettings(),
                };
                MiscConverter.DeleteVrcComponents(ctx);

                fail += Check("seat reported as Skipped, path named",
                    ctx.Report.Entries.Any(e => e.Status == ReportStatus.Skipped
                        && e.Subject != null && e.Subject.Contains("seat")
                        && e.Detail != null && e.Detail.Contains("TailSeat")));
                fail += Check("station component removed by the sweep",
                    avatar.GetComponentsInChildren<Component>(true)
                        .All(c => c == null || !c.GetType().Name.StartsWith("VRC")));
            }
            finally
            {
                if (avatar != null)
                {
                    Object.DestroyImmediate(avatar);
                }
            }

            Debug.Log(fail == 0
                ? "[StationReportTest] PASS — seats leave loudly."
                : $"[StationReportTest] FAIL — {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
