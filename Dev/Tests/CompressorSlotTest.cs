#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace AvatarBridge.Regression
{
    // VRCFury parameter-compressor detection (task #16). The compressor marks real parameters not
    // synced and rotates them through slots; CVR syncs from the animator, so after stripping it the
    // rename pass must treat them as synced, or every one gets the local "#" (seen: 131 of 170).
    //
    // Two routes: MIRRORS (VF<n>_<RealName>) and SLOTS (VF<n>_SyncIndex0, VF<n>_SyncDataBool0). The
    // slot pattern wanted digits right after "Data" and missed SyncDataBool/Float/Int (2 of ~28
    // found); mirrors hid it. So the case tested is slots with no mirrors, where every parameter
    // went local.
    //
    // The restore takes expression parameters only: all animator parameters also synced clip-driven
    // floats, which never sync in CVR, and one avatar read 4856 of 3200 bits (2016 clip-driven).
    public static class CompressorSlotTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Parameter compressor slots")]
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
                // exactly the avatar shape that broke; slots are the only evidence available.
                foreach (var slot in new[]
                {
                    "VF89_SyncIndex0", "VF89_SyncIndex1",
                    "VF89_SyncDataBool0", "VF89_SyncDataBool11",
                    "VF89_SyncDataFloat2", "VF89_SyncDataInt3", "VF89_SyncDataNum4", "VF89_SyncData5",
                })
                {
                    controller.AddParameter(slot, AnimatorControllerParameterType.Float);
                }
                // A VRCFury working value that shadows nothing; must NOT be taken for a slot or
                // a mirror, or the pass would start deleting Fury's own machinery.
                controller.AddParameter("VF113_frameTime", AnimatorControllerParameterType.Float);
                // Built-ins the compressor never touched: one nothing feeds, one the game drives.
                controller.AddParameter("VelocityMagnitude", AnimatorControllerParameterType.Float);
                controller.AddParameter("VelocityX", AnimatorControllerParameterType.Float);
                // A helper the menu never lists, and an expression parameter a clip drives.
                controller.AddParameter("Helper", AnimatorControllerParameterType.Float);
                controller.AddParameter("Driven", AnimatorControllerParameterType.Float);
                var clip = new AnimationClip { name = "Driven" };
                clip.SetCurve("", typeof(Animator), "Driven", AnimationCurve.Constant(0f, 1f, 1f));
                AssetDatabase.AddObjectToAsset(clip, controller);
                controller.AddMotion(clip);

                var expressions = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                expressions.parameters = new[]
                {
                    new VRCExpressionParameters.Parameter
                        { name = "Accessories", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = false },
                    new VRCExpressionParameters.Parameter
                        { name = "Driven", valueType = VRCExpressionParameters.ValueType.Float, networkSynced = false },
                };
                var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
                descriptor.expressionParameters = expressions;

                var ctx = new BridgeContext
                {
                    Target = avatar,
                    Report = new BridgeReport(),
                    Settings = new BridgeSettings(),
                    MergedController = controller,
                    SourceDescriptor = descriptor,
                };
                var vrcLayers = controller.layers.ToList();
                SystemStripper.StripParameterCompressorForTest(ctx, controller, vrcLayers);

                var entry = ctx.Report.Entries.FirstOrDefault(e =>
                    e.Subject != null && e.Subject.Contains("parameter compressor"));

                fail += Check("slots-with-no-mirrors is DETECTED (the reported failure)", entry != null);
                fail += Check("the real de-synced parameter is preserved, not localised",
                    ctx.PreserveParameters.Contains("Accessories"));
                fail += Check("all 8 slot spellings counted, not just SyncIndex",
                    entry != null && entry.Subject.Contains("8 slot parameters"));
                fail += Check("VRCFury's own working value is left alone",
                    !ctx.PreserveParameters.Contains("VF113_frameTime")
                    && controller.parameters.Any(p => p.name == "VF113_frameTime"));
                fail += Check("built-ins are not preserved, so a frozen one can still be pruned",
                    !ctx.PreserveParameters.Contains("VelocityMagnitude")
                    && !ctx.PreserveParameters.Contains("VelocityX"));
                fail += Check("an animator parameter no menu lists is not synced on the compressor's account",
                    !ctx.PreserveParameters.Contains("Helper"));
                fail += Check("a clip-driven float is not synced, it never can be",
                    !ctx.PreserveParameters.Contains("Driven"));
                fail += Check("the budget check sees the clip-driven float, and only it",
                    CvrParameterNames.CurveDriven(controller).SetEquals(new[] { "Driven" }));
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
                ? "[CompressorSlotTest] PASS: slots alone are enough to catch the compressor."
                : $"[CompressorSlotTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
