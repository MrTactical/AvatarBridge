#if CVR_CCK_EXISTS
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Remove unused patched shaders on two fake sets in a folder of its own: the one a material
    // uses stays with its include, the one nothing uses goes with its include, and a file
    // without the tag is never touched. The project's real shared folder is never read.
    // Batch: -executeMethod AvatarBridge.Regression.PruneShadersTest.Run
    public static class PruneShadersTest
    {
        const string Dir = "Assets/PruneShadersTest", Folder = Dir + "/Shaders";
        const string Used = "PruneTest_YAPS_0000aaaa", Unused = "PruneTest_YAPS_0000bbbb";
        static int failures;

        [MenuItem("Tools/AvatarBridge Dev/Test: Remove unused patched shaders")]
        public static void Run()
        {
            failures = 0;
            string stray = Folder + "/PruneTest stray.cginc";
            try
            {
                AssetDatabase.DeleteAsset(Dir);
                Directory.CreateDirectory(Folder);
                foreach (string set in new[] { Used, Unused })
                {
                    File.WriteAllText($"{Folder}/{set}.cginc", "float4 PruneTestTint;\n");
                    File.WriteAllText($"{Folder}/{set}.shader",
                        $"Shader \"Hidden/{set}\" {{ SubShader {{ Pass {{ CGPROGRAM\n" +
                        "#pragma vertex v\n#pragma fragment f\n#include \"UnityCG.cginc\"\n" +
                        $"#include \"{set}.cginc\"\n" +
                        "float4 v(float4 p : POSITION) : SV_POSITION { return UnityObjectToClipPos(p); }\n" +
                        "float4 f() : SV_Target { return PruneTestTint; }\nENDCG } } }\n");
                }
                File.WriteAllText(stray, "// no tag\n");
                AssetDatabase.Refresh();
                var shader = AssetDatabase.LoadAssetAtPath<Shader>($"{Folder}/{Used}.shader");
                Check(shader != null, "the fake shaders imported");
                AssetDatabase.CreateAsset(new Material(shader), Dir + "/Uses it.mat");
                AssetDatabase.SaveAssets();

                int asked = -1;
                string said = YapsShaderPatcher.PruneUnused(n => { asked = n; return true; }, Folder);
                Debug.Log("[PruneShadersTest] said: " + said);
                Check(asked == 1, $"asked before removing the one unused shader ({asked})");
                Check(File.Exists($"{Folder}/{Used}.shader") && File.Exists($"{Folder}/{Used}.cginc"), "the set a material uses is kept whole");
                Check(!File.Exists($"{Folder}/{Unused}.shader") && !File.Exists($"{Folder}/{Unused}.cginc"), "the unused set went with its include");
                Check(File.Exists(stray), "a file without the tag is left alone");

                string declined = YapsShaderPatcher.PruneUnused(n => false, Folder);
                Check(File.Exists($"{Folder}/{Used}.shader"), "nothing more to remove: " + declined);
            }
            catch (System.Exception e)
            {
                failures++;
                Debug.LogError("[PruneShadersTest] threw: " + e);
            }
            finally
            {
                AssetDatabase.DeleteAsset(Dir);
            }
            Debug.Log(failures == 0 ? "[PruneShadersTest] PASS" : $"[PruneShadersTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[PruneShadersTest] {(ok ? "ok  " : "FAIL")}  {what}");
        }
    }
}
#endif
