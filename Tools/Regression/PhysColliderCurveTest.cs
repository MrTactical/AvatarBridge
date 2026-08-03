#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge.Regression
{
    /// <summary>
    /// Known-answer test for repointing animated PhysBone-collider switches.
    ///
    /// Avatars animate <c>VRCPhysBoneCollider.m_Enabled</c> so clothing can switch its own
    /// collision (28 curves in the wild census); conversion deletes the component, so the curve
    /// died silently. The repoint retargets it at the generated collider's host object — both
    /// shipped solvers route OnEnable/OnDisable into their managers, verified from source.
    ///
    /// Asserts all four corners: a mapped curve is rewired to GameObject.m_IsActive at the host,
    /// the original binding is gone either way, an unmapped curve (skipped collider) is dropped
    /// WITH a warning, and both outcomes appear in the report.
    /// </summary>
    public static class PhysColliderCurveTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test — collider enable curves")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[PhysColliderCurveTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            string path = "Assets/__PhysColliderCurveTest.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var clip = new AnimationClip { name = "DressOn" };
            var on = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            AnimationUtility.SetEditorCurve(clip,
                new EditorCurveBinding { path = "Armature/Hips", type = typeof(VRCPhysBoneCollider), propertyName = "m_Enabled" }, on);
            AnimationUtility.SetEditorCurve(clip,
                new EditorCurveBinding { path = "Armature/Skipped", type = typeof(VRCPhysBoneCollider), propertyName = "m_Enabled" }, on);
            controller.AddMotion(clip);

            var ctx = new BridgeContext { MergedController = controller, Report = new BridgeReport() };
            ctx.PhysicsColliderHosts["Armature/Hips"] =
                new System.Collections.Generic.List<string> { "Armature/Hips/MagicaCollider_Hips" };
            PhysBoneConverter.RepointColliderEnableCurves(ctx);

            var bindings = AnimationUtility.GetCurveBindings(clip);
            fail += Check("mapped curve rewired to host m_IsActive",
                bindings.Any(b => b.path == "Armature/Hips/MagicaCollider_Hips"
                                  && b.type == typeof(GameObject) && b.propertyName == "m_IsActive"));
            fail += Check("rewired curve keeps its keys",
                AnimationUtility.GetEditorCurve(clip, bindings.First(b => b.propertyName == "m_IsActive"))
                    .keys.Length == 2);
            fail += Check("no VRC collider binding survives",
                !bindings.Any(b => b.type == typeof(VRCPhysBoneCollider)));
            fail += Check("report: rewired entry present",
                ctx.Report.Entries.Any(e => e.Subject != null && e.Subject.Contains("collider on/off")));
            fail += Check("report: dropped warning names the skipped collider",
                ctx.Report.Entries.Any(e => e.Status == ReportStatus.Warning
                                            && e.Detail != null && e.Detail.Contains("Armature/Skipped")));

            AssetDatabase.DeleteAsset(path);

            // ---- Animated PhysBone PARAMETERS: reported as lost, removed; m_Enabled silent ----
            AssetDatabase.DeleteAsset(path);
            controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            clip = new AnimationClip { name = "Boobs Bigger" };
            AnimationUtility.SetEditorCurve(clip,
                new EditorCurveBinding { path = "Chest", type = typeof(VRCPhysBone), propertyName = "radius" }, on);
            AnimationUtility.SetEditorCurve(clip,
                new EditorCurveBinding { path = "Chest", type = typeof(VRCPhysBone), propertyName = "m_Enabled" }, on);
            controller.AddMotion(clip);

            ctx = new BridgeContext { MergedController = controller, Report = new BridgeReport() };
            PhysBoneConverter.ReportAnimatedPhysBoneProperties(ctx);

            fail += Check("param: every VRCPhysBone binding removed",
                !AnimationUtility.GetCurveBindings(clip).Any(b => b.type == typeof(VRCPhysBone)));
            fail += Check("param: radius reported as lost, clip named",
                ctx.Report.Entries.Any(e => e.Status == ReportStatus.Skipped
                                            && e.Detail != null && e.Detail.Contains("radius")
                                            && e.Detail.Contains("Boobs Bigger")));
            fail += Check("param: m_Enabled NOT reported (RewirePhysicsToggles' job)",
                !ctx.Report.Entries.Any(e => e.Detail != null && e.Detail.Contains("m_Enabled")));

            AssetDatabase.DeleteAsset(path);
            Debug.Log(fail == 0
                ? "[PhysColliderCurveTest] PASS — mapped curves rewired, unmapped dropped loudly, none left dead."
                : $"[PhysColliderCurveTest] FAIL — {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
