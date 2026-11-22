#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Assets a build tool bakes into a "__Generated" folder are replaced on its next run, so a
    // conversion must copy what it uses out of them. NDMF's own folder was known; a plugin's
    // "<Name>__Generated" folder in Assets was not, and its baked textures went white.
    public static class GeneratedFolderTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Generated folders count as temporary")]
        public static void Run()
        {
            failures = 0;
            Check(AnimatorMerger.IsDoomedGeneratedPath("Packages/com.vrcfury.temp/x.mat"), "VRCFury's temp folder");
            Check(AnimatorMerger.IsDoomedGeneratedPath("Packages/nadena.dev.ndmf/__Generated/A(Clone)/_assets/assets 6.asset"), "NDMF's folder");
            Check(AnimatorMerger.IsDoomedGeneratedPath("Assets/Plugin__Generated/Light/BakedTextures/Baked_1.png"), "a plugin's own generated folder");
            Check(!AnimatorMerger.IsDoomedGeneratedPath("Packages/com.vrcfury.vrcfury/Runtime/x.mat"), "not the installed VRCFury package");
            Check(!AnimatorMerger.IsDoomedGeneratedPath("Assets/Avatar/Textures/Body__Generated.png"), "not a file that merely has the name");
            Check(!AnimatorMerger.IsDoomedGeneratedPath("Assets/AvatarBridgeOutput/A/RehomedAssets/Body.asset"), "not the output folder");
            Debug.Log(failures == 0 ? "[GeneratedFolderTest] PASS" : $"[GeneratedFolderTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[GeneratedFolderTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
