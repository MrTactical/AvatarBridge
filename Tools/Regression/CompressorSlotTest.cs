#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    /// <summary>
    /// Known-answer test for VRCFury parameter-compressor detection (task #16).
    ///
    /// The compressor beats VRChat's 256-parameter limit by marking your real parameters NOT
    /// SYNCED and rotating copies of them through a couple of slots twice a second. ChilloutVR
    /// syncs straight from the animator, so conversion strips the compressor and must then tell
    /// the rename pass that those parameters are really synced after all — otherwise
    /// preserveParameterSyncState copies the compressor's lie and every one gets the local "#".
    ///
    /// Reported in the wild as "all parameters became local": 131 of 170, the entire wardrobe
    /// among them, with no "Removed VRCFury's parameter compressor" line in the report at all.
    ///
    /// Detection has two independent routes — MIRRORS (VF&lt;n&gt;_&lt;RealName&gt; shadowing a declared
    /// parameter) and SLOTS (VF&lt;n&gt;_SyncIndex0, VF&lt;n&gt;_SyncDataBool0). A normal compressed avatar
    /// has dozens of mirrors, so the pass succeeded on those and nobody noticed that the slot
    /// pattern demanded digits straight after "Data" and therefore missed every SyncDataBool,
    /// SyncDataFloat and SyncDataInt. On one real avatar it saw 2 slots out of ~28.
    ///
    /// So the case that matters here is SLOTS WITH NO MIRRORS. That is the shape where the miss
    /// stops being cosmetic: both lists come back empty, the pass returns before doing anything,
    /// and every parameter goes local.
    /// </summary>
    public static class CompressorSlotTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test — parameter compressor slots")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[CompressorSlotTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            string path = "Assets/__CompressorSlotTest.controller";
            AssetDatabase.DeleteAsset(path);
            GameObject avatar = null;
            try
            {
                avatar = new GameObject("__CompressorSlotTest");
                var controller = AnimatorController.CreateAnimatorControllerAtPath(path);

                // A real, user-facing parameter the compressor has de-synced...
                controller.AddParameter("Accessories", AnimatorControllerParameterType.Bool);
                // ...and the compressor's own slots. NOTE: no VF<n>_Accessories mirror, which is
                // exactly the avatar shape that broke — slots are the only evidence available.
                foreach (var slot in new[]
                {
                    "VF89_SyncIndex0", "VF89_SyncIndex1",
                    "VF89_SyncDataBool0", "VF89_SyncDataBool11",
                    "VF89_SyncDataFloat2", "VF89_SyncDataInt3", "VF89_SyncDataNum4", "VF89_SyncData5",
                })
                {
                    controller.AddParameter(slot, AnimatorControllerParameterType.Float);
                }
                // A VRCFury working value that shadows nothing — must NOT be taken for a slot or
                // a mirror, or the pass would start deleting Fury's own machinery.
                controller.AddParameter("VF113_frameTime", AnimatorControllerParameterType.Float);

                var ctx = new BridgeContext
                {
                    Target = avatar,
                    Report = new BridgeReport(),
                    Settings = new BridgeSettings(),
                    MergedController = controller,
                };
                var vrcLayers = controller.layers.ToList();
                SystemStripper.StripParameterCompressorForTest(ctx, controller, vrcLayers);

                var entry = ctx.Report.Entries.FirstOrDefault(e =>
                    e.Subject != null && e.Subject.Contains("parameter compressor"));

                fail += Check("slots-with-no-mirrors is DETECTED (the reported failure)", entry != null);
                fail += Check("the real de-synced parameter is preserved, not localised",
                    ctx.PreserveParameters.Contains("Accessories"));
                fail += Check("all 8 slot spellings counted, not just SyncIndex",
                    entry != null && entry.Subject.Contains("8 slot parameter(s)"));
                fail += Check("VRCFury's own working value is left alone",
                    !ctx.PreserveParameters.Contains("VF113_frameTime")
                    && controller.parameters.Any(p => p.name == "VF113_frameTime"));
            }
            finally
            {
                if (avatar != null)
                {
                    Object.DestroyImmediate(avatar);
                }
                AssetDatabase.DeleteAsset(path);
            }

            Debug.Log(fail == 0
                ? "[CompressorSlotTest] PASS — slots alone are enough to catch the compressor."
                : $"[CompressorSlotTest] FAIL — {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
