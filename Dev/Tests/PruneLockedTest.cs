#if CVR_CCK_EXISTS
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // The locked-copy half of Remove unused patched shaders, on three fake stand-ins in a folder of
    // its own: one a YAPS copy names as its source, one another material wears, one nothing uses.
    // Only the last goes, with its shader folder. No Poiyomi needed: the sweep reads files and names.
    // Batch: -executeMethod AvatarBridge.Regression.PruneLockedTest.Run
    public static class PruneLockedTest
    {
        const string Dir = "Assets/PruneLockedTest", Folder = Dir + "/Locked";
        const string Named = "aaaa", Unused = "bbbb", Worn = "cccc";
        static int failures;

        static string ShaderPath(string key) => $"{Folder}/OptimizedShaders/{key}/Locked.shader";

        static string ShaderText(string name, string extra = "") =>
            $"Shader \"{name}\" {{ Properties {{ {extra} }} SubShader {{ Pass {{ CGPROGRAM\n" +
            "#pragma vertex v\n#pragma fragment f\n#include \"UnityCG.cginc\"\n" +
            "float4 v(float4 p : POSITION) : SV_POSITION { return UnityObjectToClipPos(p); }\n" +
            "float4 f() : SV_Target { return 1; }\nENDCG } } }\n";

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Remove unused locked copies")]
        public static void Run()
        {
            failures = 0;
            try
            {
                AssetDatabase.DeleteAsset(Dir);
                foreach (string key in new[] { Named, Unused, Worn })
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ShaderPath(key)));
                    File.WriteAllText(ShaderPath(key), ShaderText("Hidden/PruneLocked_" + key));
                }
                File.WriteAllText(Dir + "/Copy.shader",
                    ShaderText("Hidden/PruneLocked_copy", $"{YapsShaderPatcher.SourceShaderProperty} (\"Hidden/PruneLocked_{Named}\", Float) = 0"));
                AssetDatabase.Refresh();
                foreach (string key in new[] { Named, Unused, Worn })
                    AssetDatabase.CreateAsset(new Material(AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath(key))), $"{Folder}/{key}.mat");
                AssetDatabase.CreateAsset(new Material(AssetDatabase.LoadAssetAtPath<Shader>(Dir + "/Copy.shader")), Dir + "/Copy.mat");
                AssetDatabase.CreateAsset(new Material(AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath(Worn))), Dir + "/Wears it.mat");
                AssetDatabase.SaveAssets();
                Check(YapsShaderPatcher.SourceShaderOf(AssetDatabase.LoadAssetAtPath<Material>(Dir + "/Copy.mat")) == "Hidden/PruneLocked_" + Named,
                    "the fake copy names its source");

                int asked = -1;
                string said = YapsLock.PruneUnused(n => { asked = n; return true; }, Folder);
                Debug.Log("[PruneLockedTest] said: " + said);
                Check(asked == 1, $"asked before removing the one unused copy ({asked})");
                Check(File.Exists($"{Folder}/{Named}.mat") && File.Exists(ShaderPath(Named)), "the copy a YAPS material was patched from is kept");
                Check(File.Exists($"{Folder}/{Worn}.mat") && File.Exists(ShaderPath(Worn)), "the copy whose shader another material wears is kept");
                Check(!File.Exists($"{Folder}/{Unused}.mat") && !Directory.Exists(Path.GetDirectoryName(ShaderPath(Unused))),
                    "the unused copy went with its shader folder");

                int again = -1;
                string second = YapsLock.PruneUnused(n => { again = n; return false; }, Folder);
                Check(again == -1, "nothing more to remove: " + second);
                Check(YapsLock.PruneUnused(null, Dir + "/None") == null, "no folder, nothing said");
            }
            catch (System.Exception e)
            {
                failures++;
                Debug.LogError("[PruneLockedTest] threw: " + e);
            }
            finally
            {
                AssetDatabase.DeleteAsset(Dir);
            }
            Debug.Log(failures == 0 ? "[PruneLockedTest] PASS" : $"[PruneLockedTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[PruneLockedTest] {(ok ? "ok  " : "FAIL")}  {what}");
        }
    }
}
#endif
