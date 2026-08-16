#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;

namespace AvatarBridge.Regression
{
    // Known-answer test for where converted contacts are anchored.
    //
    // VRChat positions a contact's shape relative to rootTransform when set; the
    // component itself often lives elsewhere. The converter used to ignore the override and
    // parent the pointer/trigger under the component's own object, so head-pat receivers and
    // every VRCFury-baked contact using the override converted mis-anchored; found by a
    // completion-verification pass, not a tester, which is why this asserts both the set and
    // unset cases.
    public static class ContactAnchorTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test — contact anchoring")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[ContactAnchorTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            GameObject avatar = null;
            try
            {
                avatar = new GameObject("__ContactAnchorTest");
                avatar.AddComponent<Animator>();
                avatar.AddComponent<VRCAvatarDescriptor>();
                var bone = new GameObject("HeadBone");
                bone.transform.SetParent(avatar.transform, false);
                var central = new GameObject("Central");
                central.transform.SetParent(avatar.transform, false);

                // The bug's shape: component central, shape anchored at a bone.
                var rerouted = central.AddComponent<VRCContactReceiver>();
                rerouted.rootTransform = bone.transform;
                rerouted.collisionTags.Add("Hand");
                rerouted.parameter = "Pats";

                // The common shape: no override, anchor is the component's object.
                var plainHost = new GameObject("OnBone");
                plainHost.transform.SetParent(avatar.transform, false);
                var plain = plainHost.AddComponent<VRCContactSender>();
                plain.collisionTags.Add("Hand");

                var ctx = new BridgeContext
                {
                    Target = avatar,
                    SourceDescriptor = avatar.GetComponent<VRCAvatarDescriptor>(),
                    Report = new BridgeReport(),
                    Settings = new BridgeSettings
                    {
                        convertContacts = true,
                        createDefaultColliderPointers = false,
                    },
                };
                ContactsConverter.Run(ctx);

                bool underBone = bone.transform.Cast<Transform>()
                    .Any(t => t.name.Contains("CVRTrigger"));
                bool underCentral = central.transform.childCount > 0;
                bool plainStaysHome = plainHost.transform.Cast<Transform>()
                    .Any(t => t.name.Contains("CVRPointer"));

                fail += Check("rootTransform contact anchored under the BONE", underBone);
                fail += Check("nothing left parented under the central object", !underCentral);
                fail += Check("plain contact stays on its own object", plainStaysHome);
                fail += Check("curve-repoint key uses the COMPONENT's path",
                    ctx.ContactHosts.Keys.Any(k => k.path == "Central" && !k.sender));
            }
            finally
            {
                if (avatar != null)
                {
                    Object.DestroyImmediate(avatar);
                }
            }

            Debug.Log(fail == 0
                ? "[ContactAnchorTest] PASS — contacts anchor at rootTransform, repoint keys unchanged."
                : $"[ContactAnchorTest] FAIL — {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
