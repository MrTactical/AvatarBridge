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

        // lilToon's UV-channel pass, which is never drawn.
        const string NeverPass = @"
        Pass
        {
            Tags { ""LightMode"" = ""Never"" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct appdataNever { float4 pos : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT; uint vid : SV_VertexID; };
            struct v2fNever { float4 pos : POSITION; };
            struct v2fNever vert (struct appdataNever input) { struct v2fNever output; output.pos = float4(0, 0, 0, 1); return output; }
            float4 frag (v2fNever i) : SV_Target { return 0; }
            ENDCG
        }";

        // A caster with its own appdata, no SV_VertexID, ahead of the include's in the file.
        const string OwnStructCaster = @"
        Pass
        {
            Tags { ""LightMode"" = ""ShadowCaster"" }
            CGPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #include ""UnityCG.cginc""
            struct appdata { float4 vertex : POSITION; };
            struct v2fs { float4 pos : SV_POSITION; };
            v2fs vertShadow (appdata v) { v2fs o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
            float4 fragShadow (v2fs i) : SV_Target { return 0; }
            ENDCG
        }";

        // A shader locked by Poiyomi comments out a pass it turns off, line by line.
        const string CommentedPass = @"
//        Pass
//        {
//            CGPROGRAM
//            #pragma vertex vert
//            #pragma fragment frag
//            #include ""INCLUDE""
//            ENDCG
//        }";

        const string TessPass = @"
        Pass
        {
            Tags { ""LightMode"" = ""ForwardAdd"" }
            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vertTess
            #pragma hull hull
            #pragma domain domain
            #pragma fragment frag
            #include ""UnityCG.cginc""
            #include ""INCLUDE""
            ENDCG
        }";

        // Shaped like lilToon's: a pass-through vertex stage, and a domain that calls vert.
        const string TessInclude = @"
struct cp { float4 vertex : INTERNALTESSPOS; float3 normal : NORMAL; float4 tangent : TANGENT; uint vid : TEXCOORD8; };
struct tf { float edge[3] : SV_TessFactor; float inside : SV_InsideTessFactor; };
cp vertTess (appdata v) { cp o; o.vertex = v.vertex; o.normal = v.normal; o.tangent = v.tangent; o.vid = v.vid; return o; }
tf hullConst (InputPatch<cp, 3> p) { tf f; f.edge[0] = 1; f.edge[1] = 1; f.edge[2] = 1; f.inside = 1; return f; }
[domain(""tri"")] [partitioning(""integer"")] [outputtopology(""triangle_cw"")] [patchconstantfunc(""hullConst"")] [outputcontrolpoints(3)]
cp hull (InputPatch<cp, 3> p, uint id : SV_OutputControlPointID) { return p[id]; }
[domain(""tri"")]
v2f domain (tf f, const OutputPatch<cp, 3> p, float3 b : SV_DomainLocation)
{
    appdata a;
    a.vertex = p[0].vertex * b.x + p[1].vertex * b.y + p[2].vertex * b.z;
    a.normal = p[0].normal; a.tangent = p[0].tangent; a.vid = p[0].vid;
    return vert(a);
}
";

        // Shaped like Poiyomi Pro's tessellated shaders: the entry copies the id into a texcoord
        // for the later stages, and the struct names that field SV_VertexID only when untessellated.
        const string PoiTessPass = @"
        Pass
        {
            Tags { ""LightMode"" = ""ForwardAdd"" }
            CGPROGRAM
            #pragma target 5.0
            #define POI_TESSELLATED
            #pragma vertex vertPrePass
            #pragma hull hull
            #pragma domain domain
            #pragma fragment frag
            #include ""UnityCG.cginc""
            #include ""INCLUDE""
            ENDCG
        }";

        const string PoiTessInclude = @"
fixed4 _Color;
struct appdata
{
    float4 vertex : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT;
    #ifdef POI_INTERNAL_WATER
    float4 poiBindNormal : TEXCOORD6;
    #endif
    #ifdef POI_TESSELLATED
    uint vertexId : TEXCOORD6;
    #else
    uint vertexId : SV_VertexID;
    #endif
};
struct v2f { float4 pos : SV_POSITION; };
struct tf { float edge[3] : SV_TessFactor; float inside : SV_InsideTessFactor; };
v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
fixed4 frag (v2f i) : SV_Target { return _Color; }
appdata vertPrePass (appdata v) { appdata t = v; t.vertexId = (uint) v.vertexId; return t; }
tf hullConst (InputPatch<appdata, 3> p) { tf f; f.edge[0] = 1; f.edge[1] = 1; f.edge[2] = 1; f.inside = 1; return f; }
[domain(""tri"")] [partitioning(""integer"")] [outputtopology(""triangle_cw"")] [patchconstantfunc(""hullConst"")] [outputcontrolpoints(3)]
appdata hull (InputPatch<appdata, 3> p, uint id : SV_OutputControlPointID) { return p[id]; }
[domain(""tri"")]
v2f domain (tf f, const OutputPatch<appdata, 3> p, float3 b : SV_DomainLocation)
{
    appdata a = p[0];
    a.vertex = p[0].vertex * b.x + p[1].vertex * b.y + p[2].vertex * b.z;
    return vert(a);
}
";

        // A second visible pass whose include holds its own vert, as an outline pass does.
        const string OutlinePass = @"
        Pass
        {
            Name ""OUTLINE""
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            #include ""IncVertSplitOutline.cginc""
            ENDCG
        }";

        const string OutlineInclude = @"
struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT; };
struct v2f { float4 pos : SV_POSITION; };
v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex + float4(v.normal * 0.01, 0)); return o; }
fixed4 frag (v2f i) : SV_Target { return 0; }
";

        const string Include = @"
fixed4 _Color;
struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT; /*ID*/ };
struct v2f { float4 pos : SV_POSITION; };
v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
fixed4 frag (v2f i) : SV_Target { return _Color; }
";

        static readonly string NoPosition = Include.Replace("float4 vertex : POSITION;", "float4 vertex : TEXCOORD5;");

        [MenuItem("Tools/AvatarBridge Dev/Test: include vertex function")]
        public static void Run()
        {
            failures = 0;
            try
            {
                Directory.CreateDirectory(Dir);
                // A third of the refused hosts in the shader sweep had no SV_VertexID in their
                // vertex input. The entry takes one as a parameter instead, at the default target.
                var noId = Patched("IncVertNoId", false, out _, out int noIdCalls);
                Check(noId != null && Compiles(noId) && DeformsIn(noId, 0) == 1 && DeformsIn(noId, 1) == 1,
                    "an include vert with no SV_VertexID takes one and patches, in every pass");
                Check(noIdCalls == 1, $"its include holds the deform once ({noIdCalls})");

                // A vert that reads no POSITION is still refused, and nothing is written for it.
                var bad = Patched("IncVertBad", true, out string badRefusal, out int badCalls, null, NoPosition);
                Check(bad == null, "an include vert with no POSITION is refused, not patched straight");
                Check(badRefusal != null && badRefusal.Contains("POSITION"), "the refusal names POSITION");
                Check(badCalls == 0, "nothing was written for it");

                var good = Patched("IncVertGood", true, out string goodRefusal, out int goodCalls);
                Check(good != null, "the same include with SV_VertexID patches");
                Check(goodCalls == 1, $"its include holds the deform once ({goodCalls})");
                if (good != null) Check(!ShaderUtil.ShaderHasError(good), "the patched shader has no errors");

                // A borrowed pass keeps its lender's vertex stage, so the lender is patched too.
                // A borrowed caster alone is no reason to touch it.
                var caster = Patched("IncVertBorrowCaster", true, out _, out _, "        UsePass \"Legacy Shaders/VertexLit/SHADOWCASTER\"");
                Check(caster != null, "a borrowed shadow caster still patches");
                var forward = Patched("IncVertBorrowForward", true, out _, out _, "        UsePass \"Test/IncVertGood/FORWARD\"");
                Check(forward != null, "a pass borrowed from a shader that patches is patched");
                if (forward != null)
                {
                    string text = File.ReadAllText(AssetDatabase.GetAssetPath(forward));
                    Check(Regex.IsMatch(text, @"UsePass\s+""Hidden/YAPS/[0-9a-f]+/FORWARD"""), "the copy borrows it from the lender's patch");
                    Check(!ShaderUtil.ShaderHasError(forward), "and compiles");
                }
                var badLender = Patched("IncVertBorrowBad", true, out string borrowRefusal, out _, "        UsePass \"Test/IncVertBad/FORWARD\"");
                Check(badLender == null, "a pass borrowed from a shader that cannot patch is refused, not patched straight");
                Check(borrowRefusal != null && borrowRefusal.Contains("borrows") && borrowRefusal.Contains("POSITION"),
                    "the refusal names the lender's reason");
                // A forward client never draws a deferred pass, so a lender of one alone is left as
                // it is, even one that cannot be patched.
                File.WriteAllText($"{Dir}/IncVertBadDeferredInc.cginc", NoPosition.Replace("/*ID*/", "uint vid : SV_VertexID;"));
                File.WriteAllText($"{Dir}/IncVertBadDeferred.shader", "Shader \"Test/IncVertBadDeferred\"\n{" + Source
                    .Replace("SECOND", "").Replace("INCLUDE", "IncVertBadDeferredInc.cginc")
                    .Replace("Name \"FORWARD\"", "Name \"DEFERRED\"").Replace("\"ForwardBase\"", "\"Deferred\"") + "}\n");
                AssetDatabase.ImportAsset($"{Dir}/IncVertBadDeferredInc.cginc", ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset($"{Dir}/IncVertBadDeferred.shader", ImportAssetOptions.ForceSynchronousImport);
                var deferred = Patched("IncVertBorrowDeferred", true, out string deferredRefusal, out _, "        UsePass \"Test/IncVertBadDeferred/DEFERRED\"");
                Check(deferred != null && Compiles(deferred),
                    $"a deferred pass borrowed from a shader that cannot patch leaves the shader patched ({deferredRefusal ?? "patched"})");

                // A word in front of the return type ("inline v2f vert") was left in front of the
                // deform's source. Only a compile of the pass shows it.
                var qualified = Patched("IncVertInline", true, out _, out _, null,
                    Include.Replace("v2f vert (appdata v)", "inline v2f vert (appdata v)"));
                Check(qualified != null && Compiles(qualified), "a vert with a word before its type compiles once patched");

                // lilToon's tessellation drew nothing: its "Never" pass, kept only to hold UV channels,
                // was patched, and Unity's upgrader took "struct v2f vert(...) {" for a struct.
                var never = Patched("IncVertNever", true, out _, out _, NeverPass);
                Check(never != null && Compiles(never), "a shader with a never-drawn pass compiles once patched");
                if (never != null)
                {
                    string text = File.ReadAllText(AssetDatabase.GetAssetPath(never));
                    int from = text.IndexOf("\"Never\"");
                    Check(from >= 0 && !text.Substring(from, text.IndexOf("ENDCG", from) - from).Contains("YAPS"), "and that pass is left alone");
                }

                // The same shape in a pass that draws. The upgrader drops it for DX11 with a
                // warning, so the copy must be refused with a reason, never handed back invisible.
                var drawn = Patched("IncVertDrawnStruct", true, out string drawnRefusal, out _, NeverPass.Replace("\"Never\"", "\"ForwardAdd\""));
                Check(drawn == null ? drawnRefusal != null : Compiles(drawn), "a copy that would draw nothing is refused, not handed back");

                // Each pass compiles alone, so two may declare one struct name differently. The
                // include's vert was read against the caster's appdata, the first in the file.
                var sameName = Patched("IncVertSameName", true, out string sameRefusal, out _, OwnStructCaster);
                Check(sameName != null && Compiles(sameName) && DeformsIn(sameName, 0) == 1,
                    $"a vert reads its own program's struct, not another pass's of the same name ({sameRefusal ?? "patched"})");
                Check(Written("IncVertSameName").Contains("yapsTangent, v.vid);"), "and takes its vertex id from that struct");

                // The entry define went in after the commented CGPROGRAM, and the copy did not parse.
                var commented = Patched("IncVertCommented", true, out string commentedRefusal, out _, Caster + CommentedPass);
                Check(commented != null && Compiles(commented) && DeformsIn(commented, 0) == 1,
                    $"a commented-out pass is left as a comment ({commentedRefusal ?? "patched"})");

                // lilToon's tessellation drew nothing once bent: its passes deform in vertTess,
                // and the domain then calls the vert the other passes had patched, bending twice.
                var tess = Patched("IncVertTess", true, out _, out _, TessPass, Include + TessInclude);
                Check(tess != null && Compiles(tess), "a tessellated pass compiles once patched");
                if (tess != null)
                {
                    Check(DeformsIn(tess, 0) == 1, $"the plain pass still deforms, once ({DeformsIn(tess, 0)})");
                    Check(DeformsIn(tess, 1) == 1, $"the tessellated pass deforms once, not again in what its domain calls ({DeformsIn(tess, 1)})");
                }

                // The same with no vertex id anywhere: the domain still calls vert with one argument.
                var tessNoId = Patched("IncVertTessNoId", false, out string tessNoIdRefusal, out _, TessPass,
                    Include + TessInclude.Replace("o.vid = v.vid;", "o.vid = 0;").Replace(" a.vid = p[0].vid;", ""));
                Check(tessNoId != null && Compiles(tessNoId) && DeformsIn(tessNoId, 0) == 1 && DeformsIn(tessNoId, 1) == 1,
                    $"a tessellated pass with no SV_VertexID patches, once per pass ({tessNoIdRefusal ?? "patched"})");

                // Poiyomi Pro's tessellated shaders carry the id as TEXCOORD6 there, so a deform
                // reading the struct's field read a mesh UV channel: every vertex took one vertex's
                // squash share and plug frame. The entry is a real vertex stage, so it takes one.
                var poiTess = Patched("IncVertPoiTess", false, out string poiTessRefusal, out _, PoiTessPass, PoiTessInclude);
                Check(poiTess != null && Compiles(poiTess) && DeformsIn(poiTess, 0) == 1 && DeformsIn(poiTess, 1) == 1,
                    $"a Poiyomi-shaped tessellated pass patches, once per pass ({poiTessRefusal ?? "patched"})");
                if (poiTess != null)
                {
                    string tessCode = Preprocessed(poiTess, 1);
                    Check(Regex.IsMatch(tessCode, @"YapsDeform\s*\(\s*yapsPosition\s*,\s*yapsNormal\s*,\s*yapsTangent\s*,\s*yapsVertexId\s*\)")
                          && Regex.IsMatch(tessCode, @"uint\s+yapsVertexId\s*:\s*SV_VertexID"),
                        "its tessellated pass reads the id from SV_VertexID, not the texcoord the struct holds there");
                    Check(Regex.IsMatch(Preprocessed(poiTess, 0), @"YapsDeform\s*\(\s*yapsPosition\s*,\s*yapsNormal\s*,\s*yapsTangent\s*,\s*v\s*\.\s*vertexId\s*\)"),
                        "its plain pass still reads the struct's SV_VertexID");
                }

                // An old copy of the function left in a comment, ahead of the real one.
                var oldCopy = Patched("IncVertOldCopy", false, out string oldCopyRefusal, out _, null,
                    "// v2f vert (appdata v)\n// {\n// }\n" + Include);
                Check(oldCopy != null && Compiles(oldCopy) && DeformsIn(oldCopy, 0) == 1,
                    $"a commented-out vert is passed over for the real one ({oldCopyRefusal ?? "patched"})");

                // Each pass compiles alone, so each finds the vert its own includes declare.
                File.WriteAllText($"{Dir}/IncVertSplitOutline.cginc", OutlineInclude);
                AssetDatabase.ImportAsset($"{Dir}/IncVertSplitOutline.cginc", ImportAssetOptions.ForceSynchronousImport);
                var split = Patched("IncVertSplit", false, out string splitRefusal, out int splitCalls, OutlinePass);
                Check(split != null && Compiles(split) && DeformsIn(split, 0) == 1 && DeformsIn(split, 1) == 1,
                    $"two passes with a vert each in their own include both deform ({splitRefusal ?? "patched"})");
                Check(splitCalls == 2, $"one deform in each include ({splitCalls})");

                // One include, a vert per pass behind its own #if, as Mochie's Uber keeps them.
                var forked = Patched("IncVertForked", false, out string forkedRefusal, out int forkedCalls,
                    Caster.Replace("#include \"UnityCG.cginc\"", "#define FORK_SHADOW\n            #include \"UnityCG.cginc\""),
                    "#if defined(FORK_SHADOW)" + Include.Replace("return o; }", "o.pos.z += 0.001; return o; }") + "#else" + Include + "#endif\n");
                Check(forked != null && Compiles(forked) && DeformsIn(forked, 0) == 1 && DeformsIn(forked, 1) == 1,
                    $"a vert split by #if deforms in each pass ({forkedRefusal ?? "patched"})");
                Check(forkedCalls == 2, $"both copies carry the deform ({forkedCalls})");

                // One include choosing between two files by #if, a vert in each.
                File.WriteAllText($"{Dir}/IncVertTwoFilesB.cginc",
                    Include.Replace("return o; }", "o.pos.z += 0.001; return o; }").Replace("/*ID*/", ""));
                AssetDatabase.ImportAsset($"{Dir}/IncVertTwoFilesB.cginc", ImportAssetOptions.ForceSynchronousImport);
                var twoFiles = Patched("IncVertTwoFiles", false, out string twoFilesRefusal, out int twoFilesCalls,
                    Caster.Replace("#include \"UnityCG.cginc\"", "#define FORK_SHADOW\n            #include \"UnityCG.cginc\""),
                    "#if defined(FORK_SHADOW)\n#include \"IncVertTwoFilesB.cginc\"\n#else" + Include + "#endif\n");
                Check(twoFiles != null && Compiles(twoFiles) && DeformsIn(twoFiles, 0) == 1 && DeformsIn(twoFiles, 1) == 1,
                    $"a vert in each of two files an #if chooses between deforms in each pass ({twoFilesRefusal ?? "patched"})");
                Check(twoFilesCalls == 2, $"both files carry the deform ({twoFilesCalls})");
            }
            finally
            {
                // One refresh for the lot: each delete on its own re-read the project's shaders.
                AssetDatabase.StartAssetEditing();
                try
                {
                    AssetDatabase.DeleteAsset(Dir);
                    string shared = YapsShaderPatcher.SharedFolder;
                    foreach (string file in Directory.Exists(shared) ? Directory.GetFiles(shared, "IncVert*_YAPS_*") : new string[0])
                    {
                        if (!file.EndsWith(".meta")) AssetDatabase.DeleteAsset(file.Replace('\\', '/'));
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
            }
            Debug.Log(failures == 0 ? "[IncludeVertexTest] PASS" : $"[IncludeVertexTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static Shader Patched(string name, bool vertexId, out string refusal, out int calls, string second = null, string source = null)
        {
            string include = name + "Inc.cginc";
            File.WriteAllText($"{Dir}/{include}", (source ?? Include).Replace("/*ID*/", vertexId ? "uint vid : SV_VertexID;" : ""));
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

        static string Written(string name)
        {
            string shared = YapsShaderPatcher.SharedFolder;
            return !Directory.Exists(shared) ? "" : string.Concat(Directory.GetFiles(shared, name + "*_YAPS_*")
                .Where(f => !f.EndsWith(".meta")).Select(File.ReadAllText));
        }

        // The import reports no error in a pass it has not compiled; drawing compiles it. A pass
        // that fails then is dropped with a warning, "not supported on this GPU", not an error.
        static bool Compiles(Shader shader)
        {
            ShaderUtil.allowAsyncCompilation = false;
            var material = new Material(shader);
            for (int i = 0; i < material.passCount; i++) material.SetPass(i);
            Object.DestroyImmediate(material);
            return shader.isSupported && !ShaderUtil.ShaderHasError(shader);
        }

        // Deform calls left in a pass once its preprocessor has run.
        static int DeformsIn(Shader shader, int pass)
        {
            string code = Preprocessed(shader, pass);
            return code != null ? Regex.Matches(code, @"\bYapsDeform\s*\(\s*yapsPosition").Count : -1;
        }

        static string Preprocessed(Shader shader, int pass)
        {
            var variant = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(pass).PreprocessVariant(
                UnityEditor.Rendering.ShaderType.Vertex, new string[0], UnityEditor.Rendering.ShaderCompilerPlatform.D3D,
                BuildTarget.StandaloneWindows64, true);
            return variant.Success ? variant.PreprocessedCode : null;
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[IncludeVertexTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
