#if CVR_CCK_EXISTS
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A vertex function in an include is patched once for every pass that uses it. Passes
    // are patched last first, so the shadow caster usually goes first, and a caster that
    // fails is skipped as cosmetic. It used to mark the include done before failing, so
    // every later pass returned "already patched" with nothing injected: the plug drew
    // straight with no refusal (found on two popular toon shaders by the shader sweep).
    public static class IncludeVertexTest
    {
        const string Dir = "Assets/IncludeVertexTest";
        static int failures;

        const string Source = @"
    Properties { _Color (""Color"", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { ""RenderType"" = ""Opaque"" }
        Pass
        {
            Name ""FORWARD""
            Tags { ""LightMode"" = ""ForwardBase"" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            #include ""INCLUDE""
            ENDCG
        }
SECOND
    }
";

        const string Caster = @"
        Pass
        {
            Tags { ""LightMode"" = ""ShadowCaster"" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            #include ""INCLUDE""
            ENDCG
        }";

        const string Include = @"
fixed4 _Color;
struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT; /*ID*/ };
struct v2f { float4 pos : SV_POSITION; };
v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
fixed4 frag (v2f i) : SV_Target { return _Color; }
";

        [MenuItem("Tools/AvatarBridge Dev/Test: include vertex function")]
        public static void Run()
        {
            failures = 0;
            try
            {
                Directory.CreateDirectory(Dir);
                var bad = Patched("IncVertBad", false, out string badRefusal, out int badCalls);
                Check(bad == null, "an include vert with no SV_VertexID is refused, not patched straight");
                Check(badRefusal != null && badRefusal.Contains("SV_VertexID"), "the refusal names SV_VertexID");
                Check(badCalls == 0, "nothing was written for it");

                var good = Patched("IncVertGood", true, out string goodRefusal, out int goodCalls);
                Check(good != null, "the same include with SV_VertexID patches");
                Check(goodCalls == 1, $"its include holds the deform once ({goodCalls})");
                if (good != null) Check(!ShaderUtil.ShaderHasError(good), "the patched shader has no errors");

                // A borrowed pass keeps its own vertex stage. A borrowed caster is no reason to refuse.
                var caster = Patched("IncVertBorrowCaster", true, out _, out _, "        UsePass \"Legacy Shaders/VertexLit/SHADOWCASTER\"");
                Check(caster != null, "a borrowed shadow caster still patches");
                var forward = Patched("IncVertBorrowForward", true, out string borrowRefusal, out _, "        UsePass \"Test/IncVertGood/FORWARD\"");
                Check(forward == null, "a borrowed forward pass is refused, not patched straight");
                Check(borrowRefusal != null && borrowRefusal.Contains("borrows"), "the refusal says the pass is borrowed");
            }
            finally
            {
                AssetDatabase.DeleteAsset(Dir);
                string shared = YapsShaderPatcher.SharedFolder;
                foreach (string file in Directory.Exists(shared) ? Directory.GetFiles(shared, "IncVert*_YAPS_*") : new string[0])
                {
                    if (!file.EndsWith(".meta")) AssetDatabase.DeleteAsset(file.Replace('\\', '/'));
                }
            }
            Debug.Log(failures == 0 ? "[IncludeVertexTest] PASS" : $"[IncludeVertexTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static Shader Patched(string name, bool vertexId, out string refusal, out int calls, string second = null)
        {
            string include = name + "Inc.cginc";
            File.WriteAllText($"{Dir}/{include}", Include.Replace("/*ID*/", vertexId ? "uint vid : SV_VertexID;" : ""));
            string path = $"{Dir}/{name}.shader";
            File.WriteAllText(path, $"Shader \"Test/{name}\"\n{{{Source.Replace("SECOND", second ?? Caster).Replace("INCLUDE", include)}}}\n");
            AssetDatabase.ImportAsset($"{Dir}/{include}", ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(path));
            var result = YapsShaderPatcher.Patch(material, null, out refusal, out _);
            if (refusal != null) Debug.Log($"[IncludeVertexTest]   {name} refused: {refusal}");
            string shared = YapsShaderPatcher.SharedFolder;
            calls = Directory.Exists(shared)
                ? Directory.GetFiles(shared, name + "*_YAPS_*").Where(f => !f.EndsWith(".meta"))
                    .Sum(f => Regex.Matches(File.ReadAllText(f), @"\bYapsDeform\s*\(\s*yapsPosition").Count)
                : 0;
            Object.DestroyImmediate(material);
            return result;
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[IncludeVertexTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
