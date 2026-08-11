#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Dynamics.Contact.Components;

namespace AvatarBridge.Regression
{
    // Known-answer test for the widened contact-curve repointing (task #19): enable curves
    // follow the converted contact's object, position curves its transform.
    //
    // Both verdicts are from the shipped client: the host registers in OnEnable so m_Enabled
    // maps onto m_IsActive, and the host carries the authored offset in its TRANSFORM, so
    // position curves map 1:1 onto m_LocalPosition. Filters bake once at TriggerToContact
    // Create and are never read again, so those drop with the warning.
    public static class ContactCurveRepointTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test — contact curve repointing")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[ContactCurveRepointTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            string path = "Assets/__ContactCurveRepointTest.controller";
            var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 0.25f));

            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var clip = new AnimationClip { name = "Boobs Bigger" };
            foreach (var prop in new[] { "m_Enabled", "position.y", "allowSelf" })
            {
                AnimationUtility.SetEditorCurve(clip,
                    new EditorCurveBinding { path = "Chest/Pats", type = typeof(VRCContactReceiver), propertyName = prop },
                    curve);
            }
            controller.AddMotion(clip);

            var ctx = new BridgeContext
            {
                MergedController = controller,
                Report = new BridgeReport(),
                Settings = new BridgeSettings(),
            };
            ctx.ContactHosts[("Chest/Pats", false)] =
                new System.Collections.Generic.List<string> { "Chest/Pats/Contact_Pats" };
            ContactsConverter.RepointContactEnableCurves(ctx);

            var bindings = AnimationUtility.GetCurveBindings(clip);
            fail += Check("m_Enabled -> host m_IsActive",
                bindings.Any(b => b.path == "Chest/Pats/Contact_Pats"
                                  && b.type == typeof(GameObject) && b.propertyName == "m_IsActive"));
            fail += Check("no VRC contact binding survives",
                !bindings.Any(b => b.type == typeof(VRCContactReceiver)));
            fail += Check("position.y -> host Transform m_LocalPosition.y",
                bindings.Any(b => b.type == typeof(Transform)
                                  && b.propertyName == "m_LocalPosition.y"
                                  && b.path == "Chest/Pats/Contact_Pats"));
            fail += Check("allowSelf dropped WITH warning (baked at Create)",
                !bindings.Any(b => b.propertyName == "allowSelf")
                && ctx.Report.Entries.Any(e => e.Status == ReportStatus.Warning
                    && e.Detail != null && e.Detail.Contains("allowSelf")));
            AssetDatabase.DeleteAsset(path);

            Debug.Log(fail == 0
                ? "[ContactCurveRepointTest] PASS — position follows the contact, filters drop loud."
                : $"[ContactCurveRepointTest] FAIL — {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
