#if CVR_CCK_EXISTS
using System.IO;
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
            string written = null;
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
                    written = AssetDatabase.GetAssetPath(patched);
                    string text = File.ReadAllText(written);
                    Check(patched.name.StartsWith("Hidden/Locked/YAPS/"), "it keeps the locked name");
                    int i = patched.FindPropertyIndex("_ShaderOptimizerEnabled");
                    Check(i >= 0 && patched.GetPropertyDefaultFloatValue(i) == 1, "the lock button defaults to 1, so Thry reads it as locked");
                    Check(!text.Contains("_ShaderOptimizerEnabled (\"\", Int) = 0"), "no 0 default is left in the text");
                    Check(!ShaderUtil.ShaderHasError(patched), "the patched shader has no errors");
                }
                Object.DestroyImmediate(material);
            }
            finally
            {
                AssetDatabase.DeleteAsset(Dir);
                if (written != null) AssetDatabase.DeleteAsset(written);
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
