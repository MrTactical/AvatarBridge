#if AVATARBRIDGE_YAPS
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A reconvert reuses the generated material at its fixed path. Copying the source's
    // values onto it gave it the source shader's property layout while it kept the
    // patched shader, and Wear skips a shader it already wears, so nothing laid it out
    // again. Every bake value then sat outside the layout, and the next asset refresh
    // dropped them all: no bake texture, vertex count 0, own-sockets row gone.
    public static class GeneratedRelayoutTest
    {
        const string TestPath = "Assets/YAPS/Generated/Relayout Test.mat";

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Reused YAPS material keeps its layout")]
        public static void Run()
        {
            int fail = 0;
            void Check(string what, bool ok)
            {
                if (!ok) fail++;
                Debug.Log($"[GeneratedRelayoutTest] {(ok ? "ok   " : "WRONG")} {what}");
            }

            string shaderPath = Directory.Exists("Assets/YAPS/Generated/Shaders")
                ? Directory.GetFiles("Assets/YAPS/Generated/Shaders", "*.shader")
                    .Select(p => p.Replace('\\', '/'))
                    .FirstOrDefault(p => File.ReadAllText(p).Contains("_YAPS_SelfTag"))
                : null;
            var patched = shaderPath != null ? AssetDatabase.LoadAssetAtPath<Shader>(shaderPath) : null;
            if (patched == null)
            {
                Debug.Log("[GeneratedRelayoutTest] WRONG no patched YAPS shader in the project");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            AssetDatabase.DeleteAsset(TestPath);
            var source = new Material(Shader.Find("Standard")) { name = "Relayout Source" };
            try
            {
                YapsBaker.Generated(source, patched, TestPath);
                var reused = YapsBaker.Generated(source, patched, TestPath);
                Check("the reused material reads its shader's properties",
                    reused.HasProperty("_YAPS_SelfTag") && reused.HasProperty("_YAPS_VertexCount"));

                reused.SetFloat("_YAPS_VertexCount", 123f);
                EditorUtility.SetDirty(reused);
                AssetDatabase.SaveAssetIfDirty(reused);
                AssetDatabase.ImportAsset(TestPath, ImportAssetOptions.ForceUpdate);
                var reloaded = AssetDatabase.LoadAssetAtPath<Material>(TestPath);
                Check("a value written after reuse survives a reimport",
                    reloaded != null && Mathf.Approximately(reloaded.GetFloat("_YAPS_VertexCount"), 123f));
            }
            finally
            {
                Object.DestroyImmediate(source);
                AssetDatabase.DeleteAsset(TestPath);
            }

            Debug.Log($"[GeneratedRelayoutTest] {(fail == 0 ? "PASS" : fail + " WRONG")}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
