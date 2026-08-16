#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Known-answer test for the hand-pose override audit (task #15).
    //
    // A user reported "gestures are just wrong, with the wrong thresholds". Both avatars sent in
    // had the same shape: their FX playable layer carried its own layers called "Left Hand" and
    // "Right Hand", so the converted controller ended up with FOUR hand layers; the promoted
    // pair at 2 and 3, the FX duplicates above them, everything unmasked at weight 1. The FX copy
    // won. On one avatar that copy had no Idle state and a fist band starting at -0.9, parking the
    // hand in a fist at rest; the promoted layer's own bands were correct all along.
    //
    // It cannot happen in VRChat; the FX playable cannot drive humanoid muscles there, so those
    // layers never touched a finger. Merging everything into one ChilloutVR controller hands them
    // muscles they never had.
    //
    // Two things are asserted, and the second is the one that keeps this honest: the offender must
    // be stopped, and the real hand layer must NOT be, because "mask everything" would also pass a
    // test that only checked the first.
    public static class HandPoseOverrideTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test — hand pose override audit")]
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
                ? "[HandPoseOverrideTest] PASS — the duplicate is muzzled, the real hand layer is not."
                : $"[HandPoseOverrideTest] FAIL — {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
