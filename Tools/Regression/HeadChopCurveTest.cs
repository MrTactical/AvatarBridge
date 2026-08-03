#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Regression
{
    /// <summary>
    /// Known-answer test for animated head-chop rewiring — specifically the m_Enabled POLARITY,
    /// which is per exclusion type and was inverted unconditionally before task #17:
    ///
    ///   - hiding chop (scale 0): enabling it HIDES, so m_Enabled inverts into isShown
    ///   - showing chop (scale 1, keep-my-accessory-visible-in-first-person): enabling it SHOWS,
    ///     so m_Enabled maps straight across — the old code played these exactly backwards
    ///
    /// Also asserts the silent-death fix: a curve driving a chop that was skipped (fractional
    /// scale factor) is removed WITH a warning naming it, never left addressing a deleted
    /// component.
    /// </summary>
    public static class HeadChopCurveTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test — head-chop curve polarity")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[HeadChopCurveTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            string path = "Assets/__HeadChopCurveTest.controller";
            AssetDatabase.DeleteAsset(path);
            GameObject avatar = null;
            try
            {
                avatar = new GameObject("__HeadChopCurveTest");
                GameObject Chop(string name, float scale)
                {
                    var host = new GameObject(name);
                    host.transform.SetParent(avatar.transform, false);
                    var bone = new GameObject(name + "_Bone");
                    bone.transform.SetParent(avatar.transform, false);
                    var chop = host.AddComponent<VRCHeadChop>();
                    chop.targetBones = new[]
                    {
                        new VRCHeadChop.HeadChopBone { transform = bone.transform, scaleFactor = scale },
                    };
                    chop.globalScaleFactor = 1f;
                    return host;
                }
                Chop("Hider", 0f);
                Chop("Shower", 1f);
                Chop("Fractional", 0.5f);

                var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                var clip = new AnimationClip { name = "dial_activate" };
                // m_Enabled rising 0 -> 1 on all three chops.
                var rising = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
                foreach (var chopName in new[] { "Hider", "Shower", "Fractional" })
                {
                    AnimationUtility.SetEditorCurve(clip,
                        new EditorCurveBinding { path = chopName, type = typeof(VRCHeadChop), propertyName = "m_Enabled" },
                        rising);
                }
                controller.AddMotion(clip);

                var ctx = new BridgeContext { Target = avatar, MergedController = controller, Report = new BridgeReport() };
                MiscConverter.ConvertHeadChopsForTest(ctx);

                // The rewiring clones the clip; find every isShown curve by exclusion path.
                var clips = controller.animationClips.Distinct().ToArray();
                AnimationCurve IsShownFor(string boneName)
                {
                    foreach (var c in clips)
                    {
                        foreach (var b in AnimationUtility.GetCurveBindings(c))
                        {
                            if (b.type == typeof(FPRExclusion) && b.path.Contains(boneName))
                            {
                                return AnimationUtility.GetEditorCurve(c, b);
                            }
                        }
                    }
                    return null;
                }

                var hider = IsShownFor("Hider_Bone");
                var shower = IsShownFor("Shower_Bone");
                fail += Check("hiding chop: m_Enabled INVERTED (enabled=1 -> isShown=0)",
                    hider != null && hider.Evaluate(1f) < 0.5f && hider.Evaluate(0f) > 0.5f);
                fail += Check("showing chop: m_Enabled DIRECT (enabled=1 -> isShown=1)",
                    shower != null && shower.Evaluate(1f) > 0.5f && shower.Evaluate(0f) < 0.5f);
                fail += Check("skipped chop: curve removed",
                    !clips.SelectMany(AnimationUtility.GetCurveBindings)
                        .Any(b => b.type == typeof(VRCHeadChop)));
                fail += Check("skipped chop: warning names clip and path",
                    ctx.Report.Entries.Any(e => e.Status == ReportStatus.Warning
                        && e.Detail != null && e.Detail.Contains("Fractional")));
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
                ? "[HeadChopCurveTest] PASS — polarity per exclusion type, skipped chops loud."
                : $"[HeadChopCurveTest] FAIL — {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
