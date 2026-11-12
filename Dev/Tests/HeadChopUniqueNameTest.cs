#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Regression
{
    // Two head chops on bones of one name make exclusions of distinct names. Both were named
    // "FPRExclusion_Hair", so a toggle on either reached whichever Unity found first.
    public static class HeadChopUniqueNameTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Head chop exclusions named apart")]
        public static void Run()
        {
            failures = 0;
            var avatar = new GameObject("HeadChopUniqueAvatar");
            var controller = new AnimatorController();
            try
            {
                foreach (var side in new[] { "Front", "Back" })
                {
                    var holder = new GameObject(side);
                    holder.transform.SetParent(avatar.transform, false);
                    var bone = new GameObject("Hair");
                    bone.transform.SetParent(holder.transform, false);
                    var chop = holder.AddComponent<VRCHeadChop>();
                    chop.targetBones = new[] { new VRCHeadChop.HeadChopBone { transform = bone.transform, scaleFactor = 0f } };
                    chop.globalScaleFactor = 1f;
                }
                var ctx = new BridgeContext { Target = avatar, MergedController = controller, Report = new BridgeReport() };
                MiscConverter.ConvertHeadChopsForTest(ctx);

                var names = Enumerable.Range(0, avatar.transform.childCount).Select(i => avatar.transform.GetChild(i).name)
                    .Where(n => n.StartsWith("FPRExclusion_")).ToList();
                Check(names.Count == 2 && names.Distinct().Count() == 2, "two exclusions, named apart: " + string.Join(", ", names));
            }
            finally
            {
                Object.DestroyImmediate(avatar);
                Object.DestroyImmediate(controller);
            }
            Debug.Log(failures == 0 ? "[HeadChopUniqueNameTest] PASS" : $"[HeadChopUniqueNameTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[HeadChopUniqueNameTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
