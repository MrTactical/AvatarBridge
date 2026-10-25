#if CVR_CCK_EXISTS
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Two shaders under one name, the way two Poiyomi versions install side by side. A patch
    // must lead back to the file it was made from, never to whichever one the name finds.
    // Batch: -executeMethod AvatarBridge.Regression.TwinSourceTest.Run
    public static class TwinSourceTest
    {
        const string Dir = "Assets/TwinSourceTest";
        static int failures;

        static string ShaderText(string marker) =>
            "Shader \"TwinSourceTest/Twin\" { Properties { " + marker + " (\"x\", Float) = 0 } SubShader { Pass { CGPROGRAM\n" +
            "#pragma vertex vert\n#pragma fragment frag\n#include \"UnityCG.cginc\"\n" +
            "struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT; };\n" +
            "struct v2f { float4 pos : SV_POSITION; };\n" +
            "v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }\n" +
            "float4 frag (v2f i) : SV_Target { return 1; }\nENDCG } } }\n";

        [MenuItem("Tools/AvatarBridge Dev/Test: a patch finds its own source among twins")]
        public static void Run()
        {
            failures = 0;
            var generated = GeneratedFiles.Snapshot();
            try
            {
                AssetDatabase.DeleteAsset(Dir);
                foreach (string side in new[] { "A", "B" })
                {
                    Directory.CreateDirectory($"{Dir}/{side}");
                    File.WriteAllText($"{Dir}/{side}/Twin.shader", ShaderText("_Twin" + side));
                }
                AssetDatabase.Refresh();
                foreach (string side in new[] { "A", "B" })
                {
                    var source = AssetDatabase.LoadAssetAtPath<Shader>($"{Dir}/{side}/Twin.shader");
                    var material = new Material(source);
                    var patched = YapsShaderPatcher.Patch(material, null, out string refusal, out _);
                    if (patched == null) { Check(false, $"{side} patches ({refusal})"); continue; }
                    var back = YapsShaderPatcher.OriginalShaderOf(new Material(patched));
                    Check(back == source, $"the patch of {side} leads back to {side} ({AssetDatabase.GetAssetPath(back)})");
                }
            }
            catch (System.Exception e)
            {
                failures++;
                Debug.LogError("[TwinSourceTest] threw: " + e);
            }
            finally
            {
                AssetDatabase.DeleteAsset(Dir);
                GeneratedFiles.DeleteNew(generated);
            }
            Debug.Log(failures == 0 ? "[TwinSourceTest] PASS" : $"[TwinSourceTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[TwinSourceTest] {(ok ? "ok  " : "FAIL")}  {what}");
        }
    }
}
#endif
