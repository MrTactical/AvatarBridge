#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Known-answer test for the fury-id harvest. Stripping SPS's two option rows with penetration
    // kept put id 80 into the harvest, and every "[VF80]" layer went with them: the depth
    // animations, auto mode, Init Behaviours, and on some avatars the shared FrameTime Counter.
    public static class SpsOptionStripTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test: SPS option strip")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[SpsOptionStripTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            var rows = new[] { "VF80_legacy", "VF80_stealth", "VF80_Pussy/Depth", "VF77_Haptic", "Keep/Me" };
            var optionsOnly = SystemStripper.StrippedFuryIds(rows,
                n => n == "VF80_legacy" || n == "VF80_stealth" || n.StartsWith("VF77_"));
            fail += Check("the option rows alone do not take id 80", !optionsOnly.Contains("80"));
            fail += Check("a stripped component still takes its id", optionsOnly.Contains("77"));
            var spsGone = SystemStripper.StrippedFuryIds(rows, n => n.StartsWith("VF80_"));
            fail += Check("stripping SPS itself still takes id 80", spsGone.Contains("80"));
            fail += Check("a null row is ignored", SystemStripper.StrippedFuryIds(new string[] { null }, n => true).Count == 0);

            Debug.Log(fail == 0
                ? "[SpsOptionStripTest] PASS: the option rows leave SPS's layers alone."
                : $"[SpsOptionStripTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
