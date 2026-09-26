// A shader whose vertex function sits in an include written from the project
// root ("Assets/..."), as Unity allows, and whose name follows Shader with no
// space. The SPI patcher must read that include, patch its copy, point the
// copied shader at the copied include, and rename the copy. It used to look
// only beside the shader and refuse the lot, leaving it drawing in one eye,
// and a copy it did make of a no-space shader kept the original's name.
//
// Run: -executeMethod AvatarBridge.Regression.SpiIncludeProbe.Run [-spiShader "<name>"]
// The optional shader is patched too and only has to come out as its (SPI) copy.
#if CVR_CCK_EXISTS
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class SpiIncludeProbe
    {
        const string Root = "Assets/__SpiIncludeProbe";
        static int fail;

        public static void Run()
        {
            fail = 0;
            try
            {
                AssetDatabase.DeleteAsset(Root);
                Directory.CreateDirectory(Root + "/Includes");
                Directory.CreateDirectory(Root + "/Shader");
                File.WriteAllText(Root + "/Includes/ProbeBase.cginc", Include);
                File.WriteAllText(Root + "/Shader/Probe.shader", ShaderText);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Try("Hidden/SpiIncludeProbe", true);
                var args = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(args, "-spiShader");
                if (at >= 0 && at + 1 < args.Length) Try(args[at + 1], false);
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

        // Written the way the shader that found this was: no space after
        // Shader, and the include from the project root with a doubled slash.
        // The display name ending in Shader must survive the rename.
        const string ShaderText = @"Shader""Hidden/SpiIncludeProbe""
{
    Properties
    {
        _Color (""Probe Shader"", Color) = (1,1,1,1)
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""Assets/__SpiIncludeProbe//Includes/ProbeBase.cginc""
            ENDCG
        }
    }
}
";

        const string Include = @"#include ""UnityCG.cginc""
struct appdata { float4 vertex : POSITION; };
struct v2f { float4 pos : SV_POSITION; };
float4 _Color;
v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
fixed4 frag(v2f i) : SV_Target { return _Color; }
";

        static void Try(string shaderName, bool synthetic)
        {
            var shader = Shader.Find(shaderName);
            Check(shader != null, $"{shaderName} is in the project");
            if (shader == null) return;
            string out_ = Root + "/Out " + (synthetic ? "synthetic" : "named");
            var go = new GameObject("SpiIncludeProbe");
            var material = new Material(shader) { name = "probe " + (synthetic ? "synthetic" : "named") };
            AssetDatabase.CreateAsset(material, Root + "/" + material.name + ".mat");
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            var report = new BridgeReport();
            ShaderSpiPatcher.Patch(go, out_, null, report);
            var now = go.GetComponent<MeshRenderer>().sharedMaterial;
            bool ok = now != null && now.shader != null && now.shader.name == shaderName + " (SPI)";
            Check(ok, $"{shaderName} comes out as its (SPI) copy ({now?.shader?.name})");
            if (!ok)
            {
                foreach (var e in report.Entries) Log($"  report: {e.Status} {e.Subject}: {e.Detail}");
            }
            if (synthetic && Directory.Exists(out_))
            {
                var files = Directory.GetFiles(out_);
                string copiedShader = files.Where(f => f.EndsWith(".shader")).Select(File.ReadAllText).FirstOrDefault() ?? "";
                string copiedInclude = files.Where(f => f.EndsWith(".cginc")).Select(File.ReadAllText).FirstOrDefault() ?? "";
                Check(copiedInclude.Contains("UNITY_VERTEX_OUTPUT_STEREO") && copiedInclude.Contains("UNITY_SETUP_INSTANCE_ID"),
                    "the copied include carries the stereo macros");
                Check(copiedShader.Contains("#include \"ProbeBase_SPI.cginc\"") && !copiedShader.Contains("__SpiIncludeProbe//"),
                    "the copied shader includes the copied include, not the original");
                Check(copiedShader.Contains("_Color (\"Probe Shader\", Color)"),
                    "a display name ending in Shader is left alone by the rename");
            }
            UnityEngine.Object.DestroyImmediate(go);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) fail++;
            Log((ok ? "ok   " : "FAIL ") + what);
        }

        static void Log(string s) => Debug.Log("[SpiIncludeProbe] " + s);
    }
}
#endif
