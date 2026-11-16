#if CVR_CCK_EXISTS
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A locked Poiyomi outline at width 0 clips every pixel, so its pass is left unbent: bending it
    // only cost each viewer's driver a build of the whole bend. An outline with a width still bends.
    public static class OutlineClipTest
    {
        const string Dir = "Assets/OutlineClipTest";
        static int failures;

        static string Pass(string vert, string width) => $@"
        Pass
        {{
            CGPROGRAM
            #pragma vertex {vert}
            #pragma fragment frag
            #include ""UnityCG.cginc""
            sampler2D _OutlineMask;
            struct appdata {{ float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; uint vid : SV_VertexID; }};
            struct v2f {{ float4 pos : SV_POSITION; float2 uv : TEXCOORD0; }};
            v2f {vert} (appdata v) {{ v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }}
            fixed4 frag (v2f i) : SV_Target
            {{
                {(width == null ? "" : $"clip(tex2D(_OutlineMask, i.uv).r * ({width} /*_LineWidth*/) - 0.001);")}
                return 1;
            }}
            ENDCG
        }}";

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Zero-width outline left unbent")]
        public static void Run()
        {
            failures = 0;
            var generated = GeneratedFiles.Snapshot();
            try
            {
                Directory.CreateDirectory(Dir);
                string path = $"{Dir}/Outlined.shader";
                File.WriteAllText(path, "Shader \"Test/Outlined\"\n{\n    Properties { _OutlineMask (\"Mask\", 2D) = \"white\" {} }\n    SubShader\n    {" + Pass("vertMain", null)
                    + Pass("vertZero", "0.0") + Pass("vertWide", "0.5") + "\n    }\n}\n");
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(path));
                var result = YapsShaderPatcher.Patch(material, null, out string refusal, out _);
                if (refusal != null) Debug.Log($"[OutlineClipTest]   refused: {refusal}");
                string written = result != null ? AssetDatabase.GetAssetPath(result) : null;
                string text = written != null && File.Exists(written) ? File.ReadAllText(written) : "";
                Check(result != null, "the shader patches");
                Check(text.Contains("#define YAPS_ENTRY_vertMain"), "the main pass bends");
                Check(text.Contains("#define YAPS_ENTRY_vertWide"), "an outline with a width bends");
                Check(!text.Contains("#define YAPS_ENTRY_vertZero"), "a zero-width outline is left alone");
                if (result != null) Check(!ShaderUtil.ShaderHasError(result), "the patched shader has no errors");
                Object.DestroyImmediate(material);
            }
            finally
            {
                AssetDatabase.DeleteAsset(Dir);
                GeneratedFiles.DeleteNew(generated);
            }
            Debug.Log(failures == 0 ? "[OutlineClipTest] PASS" : $"[OutlineClipTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[OutlineClipTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
