#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Hand-pose override audit (task #15). FX layers named "Left Hand"/"Right Hand" sat above the
    // promoted pair, unmasked at weight 1, and won: one had no Idle and a fist band from -0.9, so the
    // hand rested in a fist. VRChat's FX cannot drive muscles; one merged CVR controller can.
    //
    // Asserts the offender is stopped AND the real hand layer is not, since "mask everything"
    // passes the first check alone.
    public static class HandPoseOverrideTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Hand pose override audit")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[HandPoseOverrideTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            const string dir = "Assets/__HandPoseOverrideTest";
            const string path = dir + "/test.controller";
            AssetDatabase.DeleteAsset(dir);
            AssetDatabase.CreateFolder("Assets", "__HandPoseOverrideTest");

            GameObject avatar = null;
            try
            {
                avatar = new GameObject("__HandPoseOverrideTest");
                var controller = AnimatorController.CreateAnimatorControllerAtPath(path);

                // A clip that animates a FINGER muscle, and one that animates a BODY muscle.
                AnimationClip Muscle(string clipName, string property)
                {
                    var c = new AnimationClip { name = clipName };
                    AnimationUtility.SetEditorCurve(c,
                        new EditorCurveBinding { path = "", type = typeof(Animator), propertyName = property },
                        AnimationCurve.Constant(0f, 1f, 0.5f));
                    AssetDatabase.AddObjectToAsset(c, controller);
                    return c;
                }
                var fingerClip = Muscle("fingers", "LeftHand.Index.1 Stretched");
                var bodyClip = Muscle("body", "Spine Front-Back");

                void Layer(string name, AnimationClip clip)
                {
                    controller.AddLayer(name);
                    var layers = controller.layers;
                    var layer = layers[layers.Length - 1];
                    layer.defaultWeight = 1f;
                    layer.stateMachine.AddState("s").motion = clip;
                    layers[layers.Length - 1] = layer;
                    controller.layers = layers;
                }

                // The real hand layer, then the FX duplicate above it, then a body layer above too.
                Layer("LeftHand", fingerClip);
                Layer("[FX] Left Hand", fingerClip);
                Layer("[FX] Body Thing", bodyClip);

                var ctx = new BridgeContext
                {
                    Target = avatar,
                    Report = new BridgeReport(),
                    Settings = new BridgeSettings(),
                    MergedController = controller,
                };
                AnimatorMerger.ResetMaskCache();
                AnimatorMerger.AuditHandPoseConflictsForTest(controller, ctx);

                AvatarMask MaskOf(string name) =>
                    controller.layers.First(l => l.name == name).avatarMask;

                bool FingersBlocked(AvatarMask m) =>
                    m != null
                    && !m.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers)
                    && !m.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers);

                fail += Check("the FX duplicate above LeftHand loses its fingers",
                    FingersBlocked(MaskOf("[FX] Left Hand")));
                fail += Check("the REAL LeftHand layer keeps its fingers",
                    !FingersBlocked(MaskOf("LeftHand")));
                fail += Check("a body-driving layer is left alone, not silently stripped",
                    !FingersBlocked(MaskOf("[FX] Body Thing")));
                fail += Check("the repair is reported",
                    ctx.Report.Entries.Any(e => e.Subject != null
                        && e.Subject.Contains("stopped from overwriting gestures")));
                fail += Check("the body layer is warned about rather than changed",
                    ctx.Report.Entries.Any(e => e.Status == ReportStatus.Warning
                        && e.Detail != null && e.Detail.Contains("[FX] Body Thing")));
            }
            finally
            {
                if (avatar != null)
                {
                    Object.DestroyImmediate(avatar);
                }
                AssetDatabase.DeleteAsset(dir);
            }

            Debug.Log(fail == 0
                ? "[HandPoseOverrideTest] PASS: the duplicate is muzzled, the real hand layer is not."
                : $"[HandPoseOverrideTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
