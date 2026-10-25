#if CVR_CCK_EXISTS
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A plug moved to YAPS Simple Lit glowed white all over: Poiyomi's emission is a colour
    // masked by a map, behind its own switch and strength, and Simple Lit took the colour alone.
    public static class SimpleLitEmissionTest
    {
        const string Source = @"Shader ""Hidden/AvatarBridge/SimpleLitEmissionTest""
{
    Properties
    {
        _Color (""Color"", Color) = (1,1,1,1)
        _MainTex (""Albedo"", 2D) = ""white"" {}
        _EnableEmission (""Enable Emission"", Float) = 0
        [HDR] _EmissionColor (""Emission Color"", Color) = (1,1,1,1)
        _EmissionMap (""Emission Map"", 2D) = ""white"" {}
        _EmissionStrength (""Emission Strength"", Range(0, 20)) = 0
    }
    SubShader { Pass { } }
}";

        [MenuItem("Tools/AvatarBridge Dev/Test: Simple Lit emission")]
        public static void Run()
        {
            int fail = 0;
            void Check(string label, bool ok)
            {
                Debug.Log($"[SimpleLitEmissionTest] {(ok ? "ok  " : "WRONG")} {label}");
                if (!ok) fail++;
            }

            var shader = ShaderUtil.CreateShaderAsset(Source);
            var mask = new Texture2D(2, 2);
            var source = new Material(shader);
            Material lit = null, off = null;
            try
            {
                source.SetFloat("_EnableEmission", 1f);
                source.SetColor("_EmissionColor", Color.white);
                source.SetTexture("_EmissionMap", mask);
                source.SetFloat("_EmissionStrength", 2f);
                lit = YapsNativeBuilder.OnSimpleLit(source, out string why);
                Check($"the plug moves to Simple Lit ({why ?? "moved"})", lit != null);
                if (lit != null)
                {
                    Check("its emission map comes with it",
                        lit.HasProperty("_EmissionMap") && lit.GetTexture("_EmissionMap") == mask);
                    var e = lit.GetColor("_EmissionColor");
                    Check($"its emission strength comes with it ({e.r:0.##})", Mathf.Approximately(e.r, 2f));
                }

                source.SetFloat("_EnableEmission", 0f);
                off = YapsNativeBuilder.OnSimpleLit(source, out _);
                var o = off != null ? off.GetColor("_EmissionColor") : Color.white;
                Check($"emission switched off stays off ({o.r:0.##})", off != null && o.maxColorComponent == 0f);
            }
            finally
            {
                if (lit != null) Object.DestroyImmediate(lit);
                if (off != null) Object.DestroyImmediate(off);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(mask);
                Object.DestroyImmediate(shader);
            }
            Debug.Log(fail == 0 ? "[SimpleLitEmissionTest] PASS" : $"[SimpleLitEmissionTest] FAIL: {fail}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
