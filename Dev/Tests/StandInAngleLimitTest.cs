#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    using MagicaCloth2;

    // A stand-in cloth that is not a soft body loses the preset's angle limit like any chain. Every
    // stand-in kept it, though only a soft body's limit was set on purpose.
    public static class StandInAngleLimitTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Stand-in keeps only a soft body's angle limit")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("Hair_Back");
            var avatar = new GameObject("StandInAngleAvatar");
            try
            {
                var fit = typeof(MagicaClothWriter).GetMethod("FitToPhysBone", BindingFlags.NonPublic | BindingFlags.Static);
                var ctx = new BridgeContext { Target = avatar, Report = new BridgeReport(), Settings = new BridgeSettings() };

                var hair = new ClothSerializeData();
                hair.angleLimitConstraint.useAngleLimit = true;
                var data = new PhysBoneChainData { Root = root.transform, StandIn = true, Gravity = 1f };
                fit.Invoke(null, new object[] { ctx, data, hair, false });
                Check(!hair.angleLimitConstraint.useAngleLimit, "a hair stand-in loses the preset's limit");

                var soft = new ClothSerializeData();
                soft.angleLimitConstraint.useAngleLimit = true;
                fit.Invoke(null, new object[] { ctx, data, soft, true });
                Check(soft.angleLimitConstraint.useAngleLimit, "a soft-body stand-in keeps the limit it was given");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(avatar);
            }
            Debug.Log(failures == 0 ? "[StandInAngleLimitTest] PASS" : $"[StandInAngleLimitTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[StandInAngleLimitTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
