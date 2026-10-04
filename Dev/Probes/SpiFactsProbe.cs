// Two facts the stereo patcher's design rests on, measured rather than
// assumed: does Unity's generated code for a surface shader carry the
// single-pass instanced macros, and does compiling a pass with
// STEREO_INSTANCING_ON catch an error that only that variant has.
//
// Run: -executeMethod AvatarBridge.Regression.SpiFactsProbe.Run
#if CVR_CCK_EXISTS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class SpiFactsProbe
    {
        const string Root = "Assets/__SpiFactsProbe";

        public static void Run()
        {
            try
            {
                AssetDatabase.DeleteAsset(Root);
                Directory.CreateDirectory(Root);
                File.WriteAllText(Root + "/Surf.shader", Surf);
                File.WriteAllText(Root + "/Dup.shader", Dup);
                File.WriteAllText(Root + "/Plain.shader", Plain);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                // 1. The surface shader's generated code, as the inspector's
                // "Show generated code" writes it.
                var surf = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Surf.shader");
                var open = typeof(ShaderUtil).GetMethod("OpenParsedSurfaceShader", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                Log($"OpenParsedSurfaceShader: {(open != null ? string.Join(",", open.GetParameters().Select(p => p.ParameterType.Name)) : "missing")}");
                try { open?.Invoke(null, new object[] { surf }); } catch (Exception e) { Log("open threw: " + (e.InnerException ?? e).Message); }
                foreach (var f in Directory.GetFiles("Temp", "*Surface*.shader"))
                {
                    string code = File.ReadAllText(f);
                    Log($"generated {Path.GetFileName(f)}: OUTPUT_STEREO {Count(code, "UNITY_VERTEX_OUTPUT_STEREO")}, " +
                        $"INIT_STEREO {Count(code, "UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO")}, SETUP_INSTANCE {Count(code, "UNITY_SETUP_INSTANCE_ID")}, " +
                        $"INPUT_INSTANCE {Count(code, "UNITY_VERTEX_INPUT_INSTANCE_ID")}, passes {Count(code, "Pass {") + Count(code, "Pass\n")}");
                }

                // 2. One stereo-only fault: an instancing member declared twice.
                foreach (var name in new[] { "Plain", "Dup" })
                {
                    var shader = AssetDatabase.LoadAssetAtPath<Shader>($"{Root}/{name}.shader");
                    Log($"{name}: ShaderHasError {ShaderUtil.ShaderHasError(shader)}");
                    var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
                    foreach (var keywords in new[] { new string[0], new[] { "STEREO_INSTANCING_ON" } })
                    {
                        var info = pass.CompileVariant(ShaderType.Vertex, keywords, ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
                        string first = info.Messages != null && info.Messages.Length > 0 ? info.Messages[0].message : "";
                        Log($"{name} vertex [{string.Join(" ", keywords)}]: success {info.Success}, {info.Messages?.Length ?? 0} message(s) {first}");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            AssetDatabase.DeleteAsset(Root);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static int Count(string s, string what) => (s.Length - s.Replace(what, "").Length) / what.Length;

        static void Log(string s) => Debug.Log("[SpiFactsProbe] " + s);

        const string Surf = @"Shader ""Hidden/SpiFactsSurf""
{
    SubShader
    {
        CGPROGRAM
        #pragma surface surf Lambert
        struct Input { float2 uv_MainTex; };
        void surf(Input IN, inout SurfaceOutput o) { o.Albedo = 1; }
        ENDCG
    }
}
";

        const string Plain = @"Shader ""Hidden/SpiFactsPlain""
{
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos = UnityObjectToClipPos(v.vertex); return o; }
            fixed4 frag(v2f i) : SV_Target { return 1; }
            ENDCG
        }
    }
}
";

        const string Dup = @"Shader ""Hidden/SpiFactsDup""
{
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos = UnityObjectToClipPos(v.vertex); return o; }
            fixed4 frag(v2f i) : SV_Target { return 1; }
            ENDCG
        }
    }
}
";
    }
}
#endif
