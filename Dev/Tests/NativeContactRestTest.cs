#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge.Regression
{
    // Known-answer test for a contact's or collider's rest state under native toggles (audit PHY-03).
    //
    // A contact anchored outside its own object (rootTransform on a bone) is rebuilt under that
    // bone, so its object switching off no longer switches it. With animator toggles the host
    // starts off and the toggle's clips carry it. With ChilloutVR's native toggles the host stayed
    // live from load, so a contact under an object that starts off, or that nothing ever turns on,
    // fired anyway.
    //
    // The case that keeps this honest is the disabled component: its host must start off AND stay
    // out of the toggle, or switching the object on wakes a contact the author turned off. A fix
    // that only turned hosts off at rest would fail the first toggle case instead, because the
    // toggle skipped every host that was off.
    public static class NativeContactRestTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test: contact rest state under native toggles")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[NativeContactRestTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            GameObject avatar = null;
            try
            {
                avatar = new GameObject("__NativeContactRestTest");
                avatar.AddComponent<Animator>();
                avatar.AddComponent<VRCAvatarDescriptor>();
                var bone = new GameObject("Bone");
                bone.transform.SetParent(avatar.transform, false);

                GameObject Part(string name, bool active, string parameter, bool contactOn)
                {
                    var go = new GameObject(name);
                    go.transform.SetParent(avatar.transform, false);
                    var receiver = go.AddComponent<VRCContactReceiver>();
                    receiver.rootTransform = bone.transform;
                    receiver.collisionTags.Add("Hand");
                    receiver.parameter = parameter;
                    receiver.enabled = contactOn;
                    go.SetActive(active);
                    return go;
                }
                Part("Outfit", false, "OutfitPat", true); // a toggle turns it on
                Part("Never", false, "NeverPat", true);   // nothing turns it on
                Part("Hat", true, "HatPat", false);       // the contact itself is off

                var ctx = new BridgeContext
                {
                    Target = avatar,
                    SourceDescriptor = avatar.GetComponent<VRCAvatarDescriptor>(),
                    Report = new BridgeReport(),
                    Settings = new BridgeSettings
                    {
                        convertContacts = true,
                        createDefaultColliderPointers = false,
                        toggleStyle = ToggleStyle.CvrNativeTargets,
                    },
                };
                ContactsConverter.Run(ctx);

                string HostPath(string part) =>
                    ctx.ContactHosts.TryGetValue((part, false), out var hosts) ? hosts.FirstOrDefault() : null;
                bool Live(string part)
                {
                    var t = BridgeContext.FindByAnimationPath(avatar.transform, HostPath(part));
                    return t != null && t.gameObject.activeSelf;
                }
                fail += Check("every contact got a host under the bone",
                    new[] { "Outfit", "Never", "Hat" }.All(p => HostPath(p)?.StartsWith("Bone/") == true));
                fail += Check("a contact under an object that starts off starts off", !Live("Outfit"));
                fail += Check("a contact under an object nothing turns on starts off", !Live("Never"));
                fail += Check("a switched-off contact starts off", !Live("Hat"));

                fail += Check("the Outfit toggle switches its contact's host",
                    ToggleNativizer.ContactHostsJoiningForTest(ctx, "Outfit", true).Contains(HostPath("Outfit")));
                fail += Check("the Hat toggle leaves the switched-off contact alone",
                    !ToggleNativizer.ContactHostsJoiningForTest(ctx, "Hat", true).Contains(HostPath("Hat")));

                // Colliders take the same rule; the physics pass makes their hosts.
                string Collider(string name, bool active, bool on)
                {
                    var go = new GameObject(name);
                    go.transform.SetParent(avatar.transform, false);
                    var collider = go.AddComponent<VRCPhysBoneCollider>();
                    collider.enabled = on;
                    go.SetActive(active);
                    var host = new GameObject(name + " collider");
                    host.transform.SetParent(bone.transform, false);
                    PhysBoneConverter.RecordColliderHost(ctx, collider, host);
                    return "Bone/" + host.name;
                }
                string belt = Collider("Belt", false, true);
                string glove = Collider("Glove", true, false);
                bool HostLive(string path) =>
                    BridgeContext.FindByAnimationPath(avatar.transform, path)?.gameObject.activeSelf == true;
                fail += Check("a collider under an object that starts off starts off", !HostLive(belt));
                fail += Check("a switched-off collider starts off", !HostLive(glove));
                fail += Check("the Belt toggle switches its collider's host",
                    ToggleNativizer.ContactHostsJoiningForTest(ctx, "Belt", true).Contains(belt));
                fail += Check("the Glove toggle leaves the switched-off collider alone",
                    !ToggleNativizer.ContactHostsJoiningForTest(ctx, "Glove", true).Contains(glove));
            }
            finally
            {
                if (avatar != null) Object.DestroyImmediate(avatar);
            }

            Debug.Log(fail == 0
                ? "[NativeContactRestTest] PASS: hosts rest as their contacts do, and native toggles carry them."
                : $"[NativeContactRestTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
