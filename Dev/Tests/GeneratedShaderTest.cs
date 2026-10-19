#if CVR_CCK_EXISTS
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A shader its importer makes from another file (ORL's .orlshader, for one) has an asset
    // path but no .shader text, and its refusal blamed a built-in or a compiled package.
    [ScriptedImporter(1, "abgenshader")]
    public class GeneratedShaderTestImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var shader = ShaderUtil.CreateShaderAsset(ctx, File.ReadAllText(ctx.assetPath), false);
            ctx.AddObjectToAsset("shader", shader);
            ctx.SetMainObject(shader);
        }
    }

    public static class GeneratedShaderTest
    {
        const string Asset = "Assets/GeneratedShaderTest.abgenshader";

        [MenuItem("Tools/AvatarBridge Dev/Test: generated shader refusal")]
        public static void Run()
        {
            int failures = 0;
            void Check(bool ok, string what)
            {
                if (!ok) failures++;
                Debug.Log($"[GeneratedShaderTest] {(ok ? "ok   " : "WRONG")} {what}");
            }
            try
            {
                File.WriteAllText(Asset, "Shader \"Test/Generated\" { SubShader { Pass { } } }");
                AssetDatabase.ImportAsset(Asset, ImportAssetOptions.ForceSynchronousImport);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(Asset);
                Check(shader != null, "the importer made a shader");
                if (shader != null)
                {
                    var material = new Material(shader);
                    var patched = YapsShaderPatcher.Patch(material, null, out string refusal, out _);
                    Debug.Log($"[GeneratedShaderTest]   refused: {refusal}");
                    Check(patched == null, "it is refused");
                    Check(refusal != null && refusal.Contains("GeneratedShaderTest.abgenshader"), "the refusal names the file it is made from");
                    Check(refusal != null && !refusal.Contains("compiled package"), "and does not call it a built-in or a package");
                    Object.DestroyImmediate(material);
                }
            }
            finally { AssetDatabase.DeleteAsset(Asset); }
            Debug.Log(failures == 0 ? "[GeneratedShaderTest] PASS" : $"[GeneratedShaderTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }
}
#endif
