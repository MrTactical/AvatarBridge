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

            // End to end: a plugin's baked material and texture end up as copies in the output folder,
            // so the plugin replacing its own files on the next build leaves the avatar alone.
            const string Plugin = "Assets/ZZ_Test__Generated", Out = "Assets/ZZ_GeneratedFolderTestOut";
            var avatar = new GameObject("GeneratedFolderAvatar");
            try
            {
                AssetDatabase.CreateFolder("Assets", Plugin.Substring(7));
                AssetDatabase.CreateFolder("Assets", Out.Substring(7));
                var tex = new Texture2D(4, 4);
                System.IO.File.WriteAllBytes(Plugin + "/Baked.png", tex.EncodeToPNG());
                AssetDatabase.ImportAsset(Plugin + "/Baked.png");
                var mat = new Material(Shader.Find("Standard")) { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Plugin + "/Baked.png") };
                AssetDatabase.CreateAsset(mat, Plugin + "/Baked.mat");
                var r = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<MeshRenderer>();
                r.transform.SetParent(avatar.transform, false);
                r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Plugin + "/Baked.mat");
                SceneAssetRehomer.Run(new BridgeContext { Target = avatar, Report = new BridgeReport(), Settings = new BridgeSettings(), OutputDir = Out });
                string matPath = AssetDatabase.GetAssetPath(r.sharedMaterial);
                string texPath = AssetDatabase.GetAssetPath(r.sharedMaterial.mainTexture);
                Check(matPath.StartsWith(Out + "/"), $"the material is copied into the output ({matPath})");
                Check(texPath.StartsWith(Out + "/"), $"and so is its texture ({texPath})");
            }
            finally
            {
                Object.DestroyImmediate(avatar);
                AssetDatabase.DeleteAsset(Plugin);
                AssetDatabase.DeleteAsset(Out);
            }
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
