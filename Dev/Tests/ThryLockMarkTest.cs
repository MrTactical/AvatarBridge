#if CVR_CCK_EXISTS
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Thry 2.7x takes a shader as locked when its lock button property defaults to 1, for
    // the upload lock and the build strip alike. A patch of an UNLOCKED Poiyomi kept the 0,
    // so the upload locked it a second time and the result did not compile.
    public static class ThryLockMarkTest
    {
        const string Dir = "Assets/ThryLockMarkTest";
        static int failures;

        const string Source = @"Shader ""Test/ThryUnlocked""
{
    Properties
    {
        [HideInInspector] shader_is_using_thry_editor (""0"", Float) = 0
        [ThryShaderOptimizerLockButton] _ShaderOptimizerEnabled ("""", Int) = 0
        _Color (""Color"", Color) = (1,1,1,1)
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            fixed4 _Color;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; uint vid : SV_VertexID; };
            struct v2f { float4 pos : SV_POSITION; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
            fixed4 frag (v2f i) : SV_Target { return _Color; }
            ENDCG
        }
    }
}
";

        [MenuItem("Tools/AvatarBridge Dev/Test: Thry lock mark on a patch")]
        public static void Run()
        {
            failures = 0;
            var generated = GeneratedFiles.Snapshot();
            try
            {
                Directory.CreateDirectory(Dir);
                string path = Dir + "/ThryUnlocked.shader";
                File.WriteAllText(path, Source);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(path));
                var patched = YapsShaderPatcher.Patch(material, null, out string refusal, out _);
                if (refusal != null) Debug.Log($"[ThryLockMarkTest]   refused: {refusal}");
                Check(patched != null, "an unlocked Thry shader patches");
                if (patched != null)
                {
                    string written = AssetDatabase.GetAssetPath(patched);
                    string text = File.ReadAllText(written);
                    Check(patched.name.StartsWith("Hidden/Locked/YAPS/"), "it keeps the locked name");
                    int i = patched.FindPropertyIndex("_ShaderOptimizerEnabled");
                    Check(i >= 0 && patched.GetPropertyDefaultFloatValue(i) == 1, "the lock button defaults to 1, so Thry reads it as locked");
                    Check(!text.Contains("_ShaderOptimizerEnabled (\"\", Int) = 0"), "no 0 default is left in the text");
                    Check(!ShaderUtil.ShaderHasError(patched), "the patched shader has no errors");
                }
                Object.DestroyImmediate(material);

                // An older Thry left the button at 0 and named the locked shader under Hidden/,
                // so a shader it locked read as unlocked and YAPS warned the lock had not run.
                string old = Dir + "/ThryOldLocked.shader";
                File.WriteAllText(old, Source.Replace("Test/ThryUnlocked", "Hidden/.poiyomi/Test/Body-Body"));
                AssetDatabase.ImportAsset(old, ImportAssetOptions.ForceSynchronousImport);
                var oldShader = AssetDatabase.LoadAssetAtPath<Shader>(old);
                Check(YapsLock.ThryLocked(oldShader) == true, "a shader an older Thry locked, button still 0, reads as locked");
                Check(YapsLock.ThryLocked(AssetDatabase.LoadAssetAtPath<Shader>(path)) == false, "the unlocked one still reads as unlocked");
                var report = new BridgeReport();
                var oldMaterial = new Material(oldShader);
                YapsLock.WarnIfUnlocked(oldMaterial, oldMaterial, report);
                Check(report.CountOf(ReportStatus.Warning) == 0, "no warning that the lock did not run");
                Object.DestroyImmediate(oldMaterial);

                // YAPS never locks a shader with DPS's properties, and its warning said the lock
                // "did not run" as if it had failed.
                string dps = Dir + "/ThryUnlockedDps.shader";
                File.WriteAllText(dps, Source.Replace("Test/ThryUnlocked", "Test/ThryUnlockedDps").Replace("        _Color",
                    "        _OrificeData (\"\", 2D) = \"white\" {}\n        _Shape1Depth (\"\", Float) = 0.1\n        _Color"));
                AssetDatabase.ImportAsset(dps, ImportAssetOptions.ForceSynchronousImport);
                var dpsMaterial = new Material(AssetDatabase.LoadAssetAtPath<Shader>(dps));
                AssetDatabase.CreateAsset(dpsMaterial, Dir + "/ThryUnlockedDps.mat");
                var dpsReport = new BridgeReport();
                YapsLock.WarnIfUnlocked(dpsMaterial, dpsMaterial, dpsReport);
                string dpsWhy = dpsReport.Entries.Select(e => e.Detail).FirstOrDefault() ?? "";
                Debug.Log($"[ThryLockMarkTest]   DPS: {dpsWhy}");
                Check(dpsWhy.Contains("DPS") && !dpsWhy.Contains("did not run"), "an unlocked shader with DPS's properties says why it was not locked");
            }
            finally
            {
                AssetDatabase.DeleteAsset(Dir);
                GeneratedFiles.DeleteNew(generated);
            }
            Debug.Log(failures == 0 ? "[ThryLockMarkTest] PASS" : $"[ThryLockMarkTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[ThryLockMarkTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
