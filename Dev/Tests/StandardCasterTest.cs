#if CVR_CCK_EXISTS
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A shader that leans on its Fallback for shadows gets Unity's own caster
    // written into it, wrapped so the shadow follows the deform. The caster
    // variant compiles only when something renders it, so the pass is
    // compiled here by hand: ShaderHasError alone passed a caster that broke
    // the moment Play mode drew a shadow.
    public static class StandardCasterTest
    {
        const string Dir = "Assets/StandardCasterTest";
        static int failures;

        const string Body = @"
    Properties { _Color (""Color"", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { ""RenderType"" = ""Opaque"" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            fixed4 _Color;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
            fixed4 frag (v2f i) : SV_Target { return _Color; }
            ENDCG
        }
    }
";

        [MenuItem("Tools/AvatarBridge Dev/Test: squash shadow caster")]
        public static void Run()
        {
            failures = 0;
            try
            {
                Directory.CreateDirectory(Dir);
                var opaque = Patched("CasterOpaque", "Diffuse", out string opaqueText, out int skipped);
                Check(opaque != null, "a Fallback-only shader patches");
                Check(opaqueText.Contains("yapsVertShadowCaster"), "it gained a wrapped caster");
                Check(skipped == 0, "no shadow pass was left unbent");
                if (opaque != null)
                {
                    Check(!ShaderUtil.ShaderHasError(opaque), "the patched shader has no errors");
                    var material = new Material(opaque);
                    int pass = material.FindPass("ShadowCaster");
                    Check(pass >= 0, "the caster pass is found by name");
                    if (pass >= 0) ShaderUtil.CompilePass(material, pass, true);
                    var errors = ShaderUtil.GetShaderMessages(opaque).Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToList();
                    foreach (var e in errors) Debug.Log($"[StandardCasterTest]   {e.message} ({e.file}:{e.line})");
                    Check(errors.Count == 0, "the caster pass compiles");
                    Object.DestroyImmediate(material);
                }

                var cutout = Patched("CasterCutout", "Legacy Shaders/Transparent/Cutout/Diffuse", out string cutoutText, out _);
                Check(cutout != null, "the cutout one patches too");
                Check(!cutoutText.Contains("yapsVertShadowCaster"), "a cutout Fallback keeps its own shadow");
            }
            finally
            {
                AssetDatabase.DeleteAsset(Dir);
            }
            Debug.Log(failures == 0 ? "[StandardCasterTest] PASS" : $"[StandardCasterTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static Shader Patched(string name, string fallback, out string text, out int skipped)
        {
            string path = $"{Dir}/{name}.shader";
            File.WriteAllText(path, $"Shader \"Test/{name}\"\n{{{Body}    Fallback \"{fallback}\"\n}}\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(path));
            var result = YapsShaderPatcher.Patch(material, $"{Dir}/Out", null, out string refusal, out skipped);
            if (refusal != null) Debug.Log($"[StandardCasterTest]   {name} refused: {refusal}");
            string written = result != null ? AssetDatabase.GetAssetPath(result) : null;
            text = written != null && File.Exists(written) ? File.ReadAllText(written) : "";
            Object.DestroyImmediate(material);
            return result;
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[StandardCasterTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
