#if CVR_CCK_EXISTS
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Poiyomi 7.3 carries Raliv's penetrator and orifice both, so a locked copy declares the
    // orifice's properties on a plug too. The orifice was checked first and the plug read as a
    // socket: neither the converter nor the YAPS window listed it, and a plug made by hand was
    // lost on every conversion. Its own penetrator toggle says which half is in use.
    public static class LegacyDetectTest
    {
        const string Source = @"Shader ""Hidden/AvatarBridge/LegacyDetectTest""
{
    Properties
    {
        _PenetratorEnabled (""Enabled"", Float) = 0
        _EntranceStiffness (""Entrance Stiffness"", Range(0.01, 1)) = 0.01
        _ReCurvature (""ReCurvature"", Range(-1, 1)) = 0
        _OrificeData (""OrificeData"", 2D) = ""white"" {}
        _Shape1Depth (""Shape1Depth"", Float) = 0
    }
    SubShader { Pass { } }
}";

        [MenuItem("Tools/AvatarBridge Dev/Test: legacy plug or socket")]
        public static void Run()
        {
            int fail = 0;
            void Check(string label, bool ok)
            {
                Debug.Log($"[LegacyDetectTest] {(ok ? "ok  " : "WRONG")} {label}");
                if (!ok) fail++;
            }

            var shader = ShaderUtil.CreateShaderAsset(Source);
            var material = new Material(shader);
            try
            {
                material.SetFloat("_PenetratorEnabled", 1f);
                var origin = YapsLegacyMap.Detect(material, out var part);
                Check($"a Poiyomi plug with its penetrator on is a DPS plug ({origin} {part})",
                    origin == YapsLegacyMap.Origin.DPS && part == YapsLegacyMap.Part.Plug);

                material.SetFloat("_PenetratorEnabled", 0f);
                origin = YapsLegacyMap.Detect(material, out part);
                Check($"the same shader with its penetrator off is a DPS socket ({origin} {part})",
                    origin == YapsLegacyMap.Origin.DPS && part == YapsLegacyMap.Part.Socket);
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
            }
            Debug.Log(fail == 0 ? "[LegacyDetectTest] PASS" : $"[LegacyDetectTest] FAIL: {fail}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
