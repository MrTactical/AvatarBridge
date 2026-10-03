// The stereo patcher against the twelve faults its 2026-10-03 read found,
// one purpose-built shader per fault: every pass patched (by other names and
// by the same names), two shaders with one file name kept apart, a macro
// never added twice, #include_with_pragmas followed and kept, every depth
// read made eye-aware (on a shader that already had the macros too), surface
// passes left as Unity's own, a pass it cannot read reported, a stereo-only
// compile fault caught with every file kept, and the swap pass reporting all
// it found.
//
// Run: -executeMethod AvatarBridge.Regression.SpiPatcherProbe.Run
#if CVR_CCK_EXISTS
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class SpiPatcherProbe
    {
        const string Root = "Assets/__SpiPatcherProbe";
        static int fail;

        public static void Run()
        {
            fail = 0;
            try
            {
                AssetDatabase.DeleteAsset(Root);
                Write("MultiNames/Multi.shader", Multi("Hidden/SpiP/MultiNames", "vertOutline", "fragOutline", "appdata2", "v2f2"));
                Write("MultiSame/Multi.shader", Multi("Hidden/SpiP/MultiSame", "vert", "frag", "appdata", "v2f"));
                Write("One/Probe.shader", Including("Hidden/SpiP/CollideOne", "Base.cginc", "#include"));
                Write("One/Base.cginc", Base("1"));
                Write("Two/Probe.shader", Including("Hidden/SpiP/CollideTwo", "Base.cginc", "#include"));
                Write("Two/Base.cginc", Base("2"));
                Write("Dup/Dup.shader", Single("Hidden/SpiP/Dup",
                    "struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };",
                    "struct v2f { float4 pos : SV_POSITION; };",
                    "v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); o.pos = UnityObjectToClipPos(v.vertex); return o; }",
                    "fixed4 frag(v2f i) : SV_Target { return 1; }"));
                Write("Pragmas/WP.shader", Wrapped("Hidden/SpiP/Pragmas", "#include_with_pragmas \"WP.cginc\""));
                Write("Pragmas/WP.cginc", "#pragma vertex vert\n#pragma fragment frag\n" + Base("3"));
                Write("Depth/Depth.shader", DepthShader("Hidden/SpiP/Depth", stereo: false));
                Write("StereoDepth/SD.shader", DepthShader("Hidden/SpiP/StereoDepth", stereo: true));
                Write("Decode/Decode.shader", Wrapped("Hidden/SpiP/Decode", "#pragma vertex vert\n#pragma fragment frag\n#include \"UnityCG.cginc\"\n" +
                    "sampler2D _CameraDepthTexture;\nstruct appdata { float4 vertex : POSITION; };\nstruct v2f { float4 pos : SV_POSITION; float4 scr : TEXCOORD0; };\n" +
                    "v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.scr = ComputeScreenPos(o.pos); return o; }\n" +
                    "fixed4 frag(v2f i) : SV_Target { return DecodeFloatRG(tex2Dlod(_CameraDepthTexture, float4(i.scr.xy / i.scr.w, 0, 0))); }"));
                Write("Surf/Surf.shader", Surface("Hidden/SpiP/Surf", outline: false));
                Write("Mixed/Mixed.shader", Surface("Hidden/SpiP/Mixed", outline: true));
                Write("Partial/Partial.shader", PartialShader());
                Write("Bad/Bad.shader", Including("Hidden/SpiP/Bad", "Bad.cginc", "#include"));
                Write("Bad/Bad.cginc", BadBase());
                Write("Ready/Ready.shader", Single("Hidden/SpiP/Ready",
                    "struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };",
                    "struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };",
                    "v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos = UnityObjectToClipPos(v.vertex); return o; }",
                    "fixed4 frag(v2f i) : SV_Target { return 1; }"));
                Write("Grab/Grab.shader", GrabShader());
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                // Each case patched on its own, so the report and the output
                // folder say what that one shader did.
                Case("MultiNames", "MultiNames", (copy, _) =>
                {
                    Check(Count(copy, "UNITY_SETUP_INSTANCE_ID(") == 2 && Count(copy, "UNITY_VERTEX_OUTPUT_STEREO") == 2
                          && Count(copy, "UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(") == 2,
                        "both passes patched when they use different function names");
                });
                Case("MultiSame", "MultiSame", (copy, _) =>
                {
                    Check(Count(copy, "UNITY_SETUP_INSTANCE_ID(") == 2 && Count(copy, "UNITY_VERTEX_INPUT_INSTANCE_ID") == 2,
                        "both passes patched when every pass says vert and frag");
                });
                Case("Dup", "Dup", (copy, _) =>
                {
                    Check(Count(copy, "UNITY_VERTEX_INPUT_INSTANCE_ID") == 1 && Count(copy, "UNITY_SETUP_INSTANCE_ID(") == 1
                          && Count(copy, "UNITY_VERTEX_OUTPUT_STEREO") == 1 && Count(copy, "UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(") == 1,
                        "a macro already there is not added twice");
                });
                Case("Pragmas", "Pragmas", (copy, files) =>
                {
                    Check(copy.Contains("#include_with_pragmas \"WP_SPI.cginc\""), "#include_with_pragmas kept and pointed at the copy");
                    Check(files.Any(f => f.EndsWith("WP_SPI.cginc") && File.ReadAllText(f).Contains("UNITY_SETUP_INSTANCE_ID(")),
                        "the vertex function behind it patched");
                });
                Case("Depth", "Depth", (copy, _) =>
                {
                    Check(copy.Contains("UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture)") && copy.Contains("SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,")
                          && copy.Contains("SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture,") && copy.Contains("SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture,")
                          && !copy.Contains("tex2D(_CameraDepthTexture") && !copy.Contains("tex2Dlod(_CameraDepthTexture") && !copy.Contains("tex2Dproj(_CameraDepthTexture"),
                        "tex2D, tex2Dproj and tex2Dlod depth reads all made eye-aware");
                });
                Case("Decode", "Decode", (copy, _) =>
                {
                    Check(copy.Contains("sampler2D _CameraDepthTexture;") && copy.Contains("tex2Dlod(_CameraDepthTexture"),
                        "a depth read used for two channels is left as it was, not rewritten to read wrong");
                    Check(LastReport.Entries.Any(e => (e.Subject ?? "").Contains("some passes only") && (e.Detail ?? "").Contains("depth")),
                        "and the report says the depth reads were left");
                });
                // Every macro, and a plain depth read for an optional feature, as
                // locked Poiyomi Pro has: left alone as correct, not copied.
                {
                    var go = Renderer("StereoDepth", "StereoDepth/SD.shader");
                    var report = new BridgeReport();
                    ShaderSpiPatcher.Patch(go, Root + "/Out StereoDepth", null, report);
                    Check(go.GetComponent<MeshRenderer>().sharedMaterial.shader.name == "Hidden/SpiP/StereoDepth"
                          && report.Entries.Any(e => (e.Subject ?? "").Contains("already speak") && (e.Detail ?? "").Contains("Hidden/SpiP/StereoDepth")),
                        "a shader naming every macro is left alone for a plain depth read");
                    UnityEngine.Object.DestroyImmediate(go);
                }
                Case("Mixed", "Mixed", (copy, _) =>
                {
                    Check(Count(copy, "UNITY_SETUP_INSTANCE_ID(") == 1, "a surface shader's own outline pass patched, its surface passes left to Unity");
                });
                Case("Partial", "Partial", (copy, _) =>
                {
                    Check(Count(copy, "UNITY_SETUP_INSTANCE_ID(") == 1, "the readable pass patched");
                    Check(LastReport.Entries.Any(e => (e.Subject ?? "").Contains("patched in some passes only") && (e.Detail ?? "").Contains("vert2")),
                        "the pass it cannot read is named in the report");
                });

                // Two shaders with one file name and one include name, on one renderer.
                {
                    var go = Renderer("Collide", "One/Probe.shader", "Two/Probe.shader");
                    string dir = Root + "/Out Collide";
                    var report = new BridgeReport();
                    ShaderSpiPatcher.Patch(go, dir, null, report);
                    var mats = go.GetComponent<MeshRenderer>().sharedMaterials;
                    Check(mats.All(m => m.shader.name.EndsWith(" (SPI)")), "both colliding shaders patched");
                    var files = Directory.GetFiles(dir).Where(f => !f.EndsWith(".meta")).Select(Path.GetFileName).ToArray();
                    Check(files.Contains("Probe_SPI.shader") && files.Contains("Probe_SPI_2.shader")
                          && files.Contains("Base_SPI.cginc") && files.Contains("Base_SPI_2.cginc"),
                        $"each got files of its own ({string.Join(", ", files.Where(f => !f.EndsWith(".mat")))})");
                    bool apart = mats.All(m =>
                    {
                        string text = File.ReadAllText(AssetDatabase.GetAssetPath(m.shader));
                        string include = System.Text.RegularExpressions.Regex.Match(text, "#include \"(Base_SPI[^\"]*)\"").Groups[1].Value;
                        string mark = m.shader.name.Contains("One") ? "PROBE_MARK 1" : "PROBE_MARK 2";
                        return include.Length > 0 && File.ReadAllText(dir + "/" + include).Contains(mark);
                    });
                    Check(apart, "each copy includes its own include");
                    UnityEngine.Object.DestroyImmediate(go);
                }

                // Surface only: left alone and counted as already correct.
                {
                    var go = Renderer("Surf", "Surf/Surf.shader");
                    var report = new BridgeReport();
                    ShaderSpiPatcher.Patch(go, Root + "/Out Surf", null, report);
                    Check(go.GetComponent<MeshRenderer>().sharedMaterial.shader.name == "Hidden/SpiP/Surf"
                          && report.Entries.Any(e => (e.Subject ?? "").Contains("already speak single-pass instanced") && (e.Detail ?? "").Contains("Hidden/SpiP/Surf"))
                          && !report.Entries.Any(e => (e.Detail ?? "").Contains("Hidden/SpiP/Surf (")),
                        "a surface shader is left alone as already stereo, not refused");
                    Check(ShaderSpiPatcher.StereoProblems(Root + "/Surf/Surf.shader").Count == 0, "and the one-eye warning says nothing of it");
                    UnityEngine.Object.DestroyImmediate(go);
                }

                // A fault only the single-pass instanced variant has: refused, every file kept.
                {
                    var go = Renderer("Bad", "Bad/Bad.shader");
                    string dir = Root + "/Out Bad";
                    var report = new BridgeReport();
                    ShaderSpiPatcher.Patch(go, dir, null, report);
                    Check(go.GetComponent<MeshRenderer>().sharedMaterial.shader.name == "Hidden/SpiP/Bad"
                          && report.Entries.Any(e => (e.Detail ?? "").Contains("single-pass instanced")),
                        "a copy whose stereo variant fails is refused, and says which variant");
                    var kept = Directory.Exists(dir) ? Directory.GetFiles(dir).Select(Path.GetFileName).ToArray() : new string[0];
                    Check(kept.Contains("Bad_SPI.shader.failed.txt") && kept.Contains("Bad_SPI.cginc.failed.txt")
                          && !kept.Contains("Bad_SPI.cginc") && !kept.Contains("Bad_SPI.shader"),
                        $"the attempt kept whole as .failed.txt, the copies deleted ({string.Join(", ", kept)})");
                    UnityEngine.Object.DestroyImmediate(go);
                }

                // The swap pass reports what it finds: an already-correct and a
                // screen-grab shader that only a toggle assigns.
                {
                    Directory.CreateDirectory(Root + "/Anim");
                    var ready = Material("Ready/Ready.shader");
                    var grab = Material("Grab/Grab.shader");
                    var controller = AnimatorController.CreateAnimatorControllerAtPath(Root + "/Anim/Probe.controller");
                    var clip = new AnimationClip { name = "Swap" };
                    AssetDatabase.CreateAsset(clip, Root + "/Anim/Swap.anim");
                    var binding = EditorCurveBinding.PPtrCurve("Body", typeof(MeshRenderer), "m_Materials.Array.data[0]");
                    AnimationUtility.SetObjectReferenceCurve(clip, binding, new[]
                    {
                        new ObjectReferenceKeyframe { time = 0f, value = ready },
                        new ObjectReferenceKeyframe { time = 1f / 60f, value = grab },
                    });
                    controller.layers[0].stateMachine.AddState("Swap").motion = clip;
                    var go = new GameObject("Swaps");
                    var report = new BridgeReport();
                    ShaderSpiPatcher.Patch(go, Root + "/Out Swaps", controller, report);
                    Check(report.Entries.Any(e => (e.Subject ?? "").Contains("already speak single-pass instanced") && (e.Detail ?? "").Contains("Hidden/SpiP/Ready")),
                        "a swap-only shader that is already correct is reported");
                    Check(report.Entries.Any(e => (e.Subject ?? "").Contains("grab the screen") && (e.Detail ?? "").Contains("Hidden/SpiP/Grab")),
                        "a swap-only screen-grab shader's warning is reported");
                    UnityEngine.Object.DestroyImmediate(go);
                }

                // The one-eye warning reads the same plan.
                Check(ShaderSpiPatcher.StereoProblems(Root + "/MultiSame/Multi.shader").Any(p => p.StartsWith("missing")),
                    "the warning names missing macros");
                Check(ShaderSpiPatcher.StereoProblems(Root + "/StereoDepth/SD.shader").Count == 0,
                    "and nothing for a shader naming every macro beside a plain depth read");
                Check(ShaderSpiPatcher.StereoProblems(Root + "/Ready/Ready.shader").Count == 0, "and nothing for a ready shader");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                fail++;
            }
            AssetDatabase.DeleteAsset(Root);
            Log(fail == 0 ? "PASS" : $"FAIL: {fail} check(s)");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        static BridgeReport LastReport;

        // Patches one shader on its own renderer and hands the copy's text,
        // shader and includes together, to the checks.
        static void Case(string folder, string name, Action<string, string[]> checks)
        {
            string shaderPath = Directory.GetFiles(Root + "/" + folder, "*.shader").First().Replace('\\', '/');
            var go = Renderer(name, shaderPath.Substring(Root.Length + 1));
            string dir = Root + "/Out " + name;
            LastReport = new BridgeReport();
            ShaderSpiPatcher.Patch(go, dir, null, LastReport);
            var shader = go.GetComponent<MeshRenderer>().sharedMaterial.shader;
            bool patched = shader.name.EndsWith(" (SPI)");
            Check(patched, $"{name} patched ({shader.name}){(patched ? "" : ": " + string.Join(" | ", LastReport.Entries.Select(e => e.Subject + ": " + e.Detail)))}");
            if (patched)
            {
                var files = Directory.GetFiles(dir).Where(f => f.EndsWith(".shader") || f.EndsWith(".cginc")).ToArray();
                checks(string.Concat(files.Select(File.ReadAllText)), files);
            }
            UnityEngine.Object.DestroyImmediate(go);
        }

        static GameObject Renderer(string name, params string[] shaders)
        {
            var go = new GameObject("SpiPatcherProbe " + name);
            go.AddComponent<MeshRenderer>().sharedMaterials = shaders.Select(Material).ToArray();
            return go;
        }

        static Material Material(string shaderPath)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/" + shaderPath);
            var material = new Material(shader) { name = shaderPath.Replace('/', '_').Replace(".shader", "") };
            AssetDatabase.CreateAsset(material, Root + "/" + material.name + ".mat");
            return material;
        }

        static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Root + "/" + path));
            File.WriteAllText(Root + "/" + path, text);
        }

        static int Count(string s, string what) => (s.Length - s.Replace(what, "").Length) / what.Length;

        static void Check(bool ok, string what)
        {
            if (!ok) fail++;
            Log((ok ? "ok   " : "FAIL ") + what);
        }

        static void Log(string s) => Debug.Log("[SpiPatcherProbe] " + s);

        // ---- shaders ------------------------------------------------------------------

        static string Wrapped(string name, string program) =>
            $"Shader \"{name}\"\n{{\n    SubShader\n    {{\n        Pass\n        {{\n            CGPROGRAM\n            {program}\n            ENDCG\n        }}\n    }}\n}}\n";

        static string Single(string name, string input, string output, string vert, string frag) =>
            Wrapped(name, $"#pragma vertex vert\n#pragma fragment frag\n#include \"UnityCG.cginc\"\n{input}\n{output}\n{vert}\n{frag}");

        static string Including(string name, string include, string directive) =>
            Wrapped(name, $"#pragma vertex vert\n#pragma fragment frag\n{directive} \"{include}\"");

        static string Base(string mark) =>
            $"#include \"UnityCG.cginc\"\n#define PROBE_MARK {mark}\n" +
            "struct appdata { float4 vertex : POSITION; };\nstruct v2f { float4 pos : SV_POSITION; };\n" +
            "v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }\n" +
            "fixed4 frag(v2f i) : SV_Target { return PROBE_MARK; }\n";

        // A member the stereo variant also declares: harmless until the patch
        // adds the instancing macro, then a redefinition only that variant sees.
        static string BadBase() =>
            "#include \"UnityCG.cginc\"\n" +
            "struct appdata { float4 vertex : POSITION; uint instanceID : SV_InstanceID; };\nstruct v2f { float4 pos : SV_POSITION; };\n" +
            "v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }\n" +
            "fixed4 frag(v2f i) : SV_Target { return 1; }\n";

        static string Multi(string name, string vert2, string frag2, string in2, string out2) =>
            $"Shader \"{name}\"\n{{\n    SubShader\n    {{\n" +
            "        Pass\n        {\n            CGPROGRAM\n            #pragma vertex vert\n            #pragma fragment frag\n            #include \"UnityCG.cginc\"\n" +
            "            struct appdata { float4 vertex : POSITION; };\n            struct v2f { float4 pos : SV_POSITION; };\n" +
            "            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }\n" +
            "            fixed4 frag(v2f i) : SV_Target { return 1; }\n            ENDCG\n        }\n" +
            $"        Pass\n        {{\n            Cull Front\n            CGPROGRAM\n            #pragma vertex {vert2}\n            #pragma fragment {frag2}\n            #include \"UnityCG.cginc\"\n" +
            $"            struct {in2} {{ float4 vertex : POSITION; float3 normal : NORMAL; }};\n            struct {out2} {{ float4 pos : SV_POSITION; }};\n" +
            $"            {out2} {vert2}({in2} v) {{ {out2} o; o.pos = UnityObjectToClipPos(v.vertex + float4(v.normal * 0.001, 0)); return o; }}\n" +
            $"            fixed4 {frag2}({out2} i, fixed facing : VFACE) : SV_Target {{ return 0; }}\n            ENDCG\n        }}\n    }}\n}}\n";

        static string DepthShader(string name, bool stereo) =>
            Wrapped(name, "#pragma vertex vert\n#pragma fragment frag\n#include \"UnityCG.cginc\"\nsampler2D _CameraDepthTexture;\n" +
                $"struct appdata {{ float4 vertex : POSITION; {(stereo ? "UNITY_VERTEX_INPUT_INSTANCE_ID" : "")} }};\n" +
                $"struct v2f {{ float4 pos : SV_POSITION; float4 scr : TEXCOORD0; {(stereo ? "UNITY_VERTEX_OUTPUT_STEREO" : "")} }};\n" +
                $"v2f vert(appdata v) {{ v2f o; {(stereo ? "UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);" : "")} o.pos = UnityObjectToClipPos(v.vertex); o.scr = ComputeScreenPos(o.pos); return o; }}\n" +
                "fixed4 frag(v2f i) : SV_Target {\n" +
                "    float a = tex2D(_CameraDepthTexture, i.scr.xy / i.scr.w).r;\n" +
                (stereo ? "" :
                "    float b = tex2Dproj(_CameraDepthTexture, UNITY_PROJ_COORD(i.scr)).r;\n" +
                "    float c = tex2Dlod(_CameraDepthTexture, float4(i.scr.xy / i.scr.w, 0, 0)).x;\n    a += b + c;\n") +
                "    return LinearEyeDepth(a);\n}");

        static string Surface(string name, bool outline) =>
            $"Shader \"{name}\"\n{{\n    SubShader\n    {{\n" +
            "        CGPROGRAM\n        #pragma surface surf Lambert\n        struct Input { float2 uv_MainTex; };\n" +
            "        void surf(Input IN, inout SurfaceOutput o) { o.Albedo = 1; }\n        ENDCG\n" +
            (outline
                ? "        Pass\n        {\n            Cull Front\n            CGPROGRAM\n            #pragma vertex vert\n            #pragma fragment frag\n            #include \"UnityCG.cginc\"\n" +
                  "            struct appdata { float4 vertex : POSITION; };\n            struct v2f { float4 pos : SV_POSITION; };\n" +
                  "            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }\n" +
                  "            fixed4 frag(v2f i) : SV_Target { return 0; }\n            ENDCG\n        }\n"
                : "") +
            "    }\n}\n";

        static string PartialShader() =>
            "Shader \"Hidden/SpiP/Partial\"\n{\n    SubShader\n    {\n" +
            "        Pass\n        {\n            CGPROGRAM\n            #pragma vertex vert\n            #pragma fragment frag\n            #include \"UnityCG.cginc\"\n" +
            "            struct appdata { float4 vertex : POSITION; };\n            struct v2f { float4 pos : SV_POSITION; };\n" +
            "            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }\n" +
            "            fixed4 frag(v2f i) : SV_Target { return 1; }\n            ENDCG\n        }\n" +
            "        Pass\n        {\n            CGPROGRAM\n            #pragma vertex vert2\n            #pragma fragment frag2\n            #include \"UnityCG.cginc\"\n" +
            "            struct appdata2 { float4 vertex : POSITION; };\n            struct v2f2 { float4 pos : SV_POSITION; };\n" +
            "            void vert2(appdata2 v, out v2f2 o) { o.pos = UnityObjectToClipPos(v.vertex); }\n" +
            "            fixed4 frag2(v2f2 i) : SV_Target { return 0; }\n            ENDCG\n        }\n    }\n}\n";

        static string GrabShader() =>
            "Shader \"Hidden/SpiP/Grab\"\n{\n    SubShader\n    {\n        GrabPass { }\n" +
            "        Pass\n        {\n            CGPROGRAM\n            #pragma vertex vert\n            #pragma fragment frag\n            #include \"UnityCG.cginc\"\n" +
            "            sampler2D _GrabTexture;\n            struct appdata { float4 vertex : POSITION; };\n            struct v2f { float4 pos : SV_POSITION; float4 grab : TEXCOORD0; };\n" +
            "            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.grab = ComputeGrabScreenPos(o.pos); return o; }\n" +
            "            fixed4 frag(v2f i) : SV_Target { return tex2Dproj(_GrabTexture, i.grab); }\n            ENDCG\n        }\n    }\n}\n";
    }
}
#endif
