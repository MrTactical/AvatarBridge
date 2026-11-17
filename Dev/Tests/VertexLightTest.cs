#if CVR_CCK_EXISTS
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A host that reads VERTEXLIGHT_ON per pixel only gets it declared for the pixel stage, so the
    // patched vertex program compiles once instead of twice. One whose vertex reads it keeps it.
    // The render proves the pixel stage still sees the keyword under a vertex light.
    public static class VertexLightTest
    {
        const string Dir = "Assets/VertexLightTest";
        static int failures;

        static string Shader(string name, string vertexExtra) => $@"Shader ""Test/{name}""
{{
    Properties {{ _Color (""Color"", Color) = (1,1,1,1) }}
    SubShader
    {{
        Pass
        {{
            Tags {{ ""LightMode"" = ""ForwardBase"" }}
            CGPROGRAM
            #pragma vertex vert{name}
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile _ VERTEXLIGHT_ON
            #include ""UnityCG.cginc""
            struct appdata {{ float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; uint vid : SV_VertexID; }};
            struct v2f {{ float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; }};
            v2f vert{name} (appdata v)
            {{
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                {vertexExtra}
                return o;
            }}
            fixed4 frag (v2f i) : SV_Target
            {{
            #ifdef VERTEXLIGHT_ON
                return fixed4(1, 0, 0, 1);
            #else
                return fixed4(0, 0, 1, 1);
            #endif
            }}
            ENDCG
        }}
    }}
}}
";

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Vertex light keyword per pixel")]
        public static void Run()
        {
            failures = 0;
            var generated = GeneratedFiles.Snapshot();
            try
            {
                Directory.CreateDirectory(Dir);
                var pixel = PatchText("PixelRead", "", out var pixelShader);
                Check(pixel.Contains("#pragma multi_compile_fragment _ VERTEXLIGHT_ON")
                      && !pixel.Contains("#pragma multi_compile _ VERTEXLIGHT_ON"), "a pixel-only reader gets the keyword per pixel");
                var vertex = PatchText("VertexRead", "#ifdef VERTEXLIGHT_ON\n o.wpos.x += 1;\n#endif", out _);
                Check(vertex.Contains("#pragma multi_compile _ VERTEXLIGHT_ON")
                      && !vertex.Contains("multi_compile_fragment _ VERTEXLIGHT_ON"), "a vertex reader keeps it for both stages");
                if (pixelShader != null)
                {
                    Check(Render(pixelShader, true).r > 0.9f, "under a vertex light the pixel stage sees the keyword");
                    Check(Render(pixelShader, false).b > 0.9f, "without one it does not");
                }
            }
            finally
            {
                AssetDatabase.DeleteAsset(Dir);
                GeneratedFiles.DeleteNew(generated);
            }
            Debug.Log(failures == 0 ? "[VertexLightTest] PASS" : $"[VertexLightTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static string PatchText(string name, string vertexExtra, out Shader patched)
        {
            string path = $"{Dir}/{name}.shader";
            File.WriteAllText(path, Shader(name, vertexExtra));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(path));
            patched = YapsShaderPatcher.Patch(material, null, out string refusal, out _);
            if (refusal != null) Debug.Log($"[VertexLightTest]   {name} refused: {refusal}");
            Check(patched != null && !ShaderUtil.ShaderHasError(patched), $"{name} patches without errors");
            Object.DestroyImmediate(material);
            string written = patched != null ? AssetDatabase.GetAssetPath(patched) : null;
            return written != null && File.Exists(written) ? File.ReadAllText(written) : "";
        }

        static Color Render(Shader shader, bool vertexLight)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.GetComponent<Renderer>().sharedMaterial = new Material(shader);
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(0, 180, 0);
            if (vertexLight)
            {
                var point = new GameObject("point").AddComponent<Light>();
                point.type = LightType.Point;
                point.renderMode = LightRenderMode.ForceVertex;
                point.range = 5;
                point.transform.position = new Vector3(0, 0, -1);
            }
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.transform.position = new Vector3(0, 0, -2);
            cam.renderingPath = RenderingPath.Forward;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var rt = new RenderTexture(16, 16, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var read = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, 16, 16), 0, 0);
            RenderTexture.active = null;
            var c = read.GetPixel(8, 8);
            Debug.Log($"[VertexLightTest]   render {(vertexLight ? "with" : "without")} a vertex light: {c}");
            return c;
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[VertexLightTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
