#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarBridge.Regression
{
    // Known-answer test for what counts as a black marker light (audit YSH-13).
    //
    // A plug took a socket light as protocol only below 1e-4 a channel; the socket readers took a
    // plug's tip light below 0.01 in length. A census of the projects' avatars found kits that
    // author their lights 1/255 grey rather than black, scaled by intensity, which on a plug's tip
    // is its length: one plug's tip went over the socket's bound, and grey sockets sat right at
    // the plug's. So the lights are rendered for real and unity_LightColor read back, the shipped
    // classifier is asked, and every reader must use one test that judges colour against the
    // light's own intensity.
    public static class MarkerLightColourTest
    {
        const string Dir = "Assets/__MarkerLightColourTest";
        const string Include = "Assets/AvatarBridge/Editor/Yaps/yaps_resolve.cginc";

        [MenuItem("Tools/AvatarBridge Dev/Test: marker light colour")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[MarkerLightColourTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            AssetDatabase.DeleteAsset(Dir);
            AssetDatabase.CreateFolder("Assets", "__MarkerLightColourTest");
            RenderTexture rt = null;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Debug.Log($"[MarkerLightColourTest] colour space {PlayerSettings.colorSpace}, " +
                          $"linear intensity {GraphicsSettings.lightsUseLinearIntensity}");

                // x the plug's classification, y the light's red, z its alpha (a tip's length).
                var classify = MakeShader("Classify",
                    "float4(YapsClassifyLight(0), unity_LightColor[0].r, unity_LightColor[0].a, 1)");
                // x the one black test every reader shares.
                var marker = MakeShader("Marker", "float4(YapsLightIsMarker(0) ? 1 : 0, 0, 0, 1)");
                fail += Check("the classifier compiles", classify != null && classify.isSupported);
                fail += Check("one shared marker test exists and compiles", marker != null && marker.isSupported);

                rt = new RenderTexture(4, 4, 16, RenderTextureFormat.ARGBFloat);
                var cam = new GameObject("cam").AddComponent<Camera>();
                cam.transform.position = new Vector3(0f, 0f, -1f);
                cam.orthographic = true;
                cam.orthographicSize = 0.2f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(-1f, -1f, -1f, -1f);
                cam.targetTexture = rt;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.transform.localScale = Vector3.one * 0.5f;
                var renderer = quad.GetComponent<Renderer>();
                var light = new GameObject("marker").AddComponent<Light>();
                light.type = LightType.Point;
                light.renderMode = LightRenderMode.ForceVertex;
                light.transform.position = new Vector3(0f, 0f, -0.1f);

                Vector4 Render(Shader shader, Color colour, float intensity, float range)
                {
                    light.color = colour;
                    light.intensity = intensity;
                    light.range = range;
                    renderer.sharedMaterial = new Material(shader);
                    cam.Render();
                    var was = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(4, 4, TextureFormat.RGBAFloat, false);
                    tex.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
                    RenderTexture.active = was;
                    var px = tex.GetPixel(2, 2);
                    Object.DestroyImmediate(tex);
                    return px;
                }

                var grey = new Color(1f / 255f, 1f / 255f, 1f / 255f, 1f);
                // name, colour, intensity, range, a socket the plug must see (digit 3 = hole),
                // marker by the shared test. Colours and intensities are the census's own.
                var cases = new (string name, Color colour, float intensity, float range, bool hole, bool isMarker)[]
                {
                    ("black hole", Color.black, 1f, 0.43f, true, true),
                    ("grey hole at 0.354", grey, 0.354f, 0.43f, true, true),
                    ("grey hole at 1", grey, 1f, 0.43f, true, true),
                    ("grey hole at 4.43", grey, 4.42765f, 0.43f, true, true),
                    ("grey tip 31.85 long", grey, 31.8529f, 0.39f, false, true),
                    ("black tip 1.54 long", Color.black, 1.54474f, 0.39f, false, true),
                    ("cyan light", new Color(0f, 0.990399f, 1f, 1f), 0.4f, 0.46f, false, false),
                    ("white light", Color.white, 1f, 0.43f, false, false),
                    ("orange light", new Color(1f, 0.529901f, 0f, 1f), 2f, 0.45f, false, false),
                };
                foreach (var c in cases)
                {
                    if (classify != null && classify.isSupported)
                    {
                        var got = Render(classify, c.colour, c.intensity, c.range);
                        Debug.Log($"[MarkerLightColourTest] {c.name}: unity_LightColor r {got.y:0.#######} a {got.z:0.####}, class {got.x}");
                        if (!c.name.Contains("tip"))
                        {
                            fail += Check($"{c.name}: the plug {(c.hole ? "sees a hole" : "sees no socket")} (class {got.x})",
                                c.hole ? Mathf.Approximately(got.x, 3f) : Mathf.Approximately(got.x, 0f));
                        }
                    }
                    if (marker != null && marker.isSupported)
                    {
                        var got = Render(marker, c.colour, c.intensity, c.range);
                        fail += Check($"{c.name}: {(c.isMarker ? "a marker" : "real light")} to the shared test",
                            Mathf.Approximately(got.x, c.isMarker ? 1f : 0f));
                    }
                }

                // Every reader uses the shared test: no colour check of its own.
                var readers = new[] { "yaps_resolve.cginc", "yaps_socket.cginc", "yaps_dent.cginc", "YapsSocketReadout.shader" }
                    .Select(f => "Assets/AvatarBridge/Editor/Yaps/" + f).ToList();
                foreach (string path in readers)
                {
                    string src = File.Exists(path) ? File.ReadAllText(path) : "";
                    int own = src.Split('\n').Count(l => !l.TrimStart().StartsWith("//")
                        && (l.Contains("dot(unity_LightColor") || l.Contains("colour.rgb >")));
                    fail += Check($"{Path.GetFileName(path)}: no black test of its own ({own})", src.Length > 0 && own == 0);
                }
            }
            finally
            {
                if (rt != null) Object.DestroyImmediate(rt);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(Dir);
                Debug.Log(fail == 0
                    ? "[MarkerLightColourTest] PASS: one black test, judged against intensity, for every reader."
                    : $"[MarkerLightColourTest] FAIL: {fail} case(s) wrong.");
                if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
            }
        }

        static Shader MakeShader(string name, string output)
        {
            string path = $"{Dir}/{name}.shader";
            File.WriteAllText(path,
$@"Shader ""Hidden/AvatarBridge/MarkerLightColourTest/{name}""
{{
    SubShader
    {{
        Tags {{ ""RenderType""=""Opaque"" }}
        Pass
        {{
            Tags {{ ""LightMode""=""ForwardBase"" }}
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include ""UnityCG.cginc""
            #include ""{Include}""
            float4 vert(float4 vertex : POSITION) : SV_POSITION {{ return UnityObjectToClipPos(vertex); }}
            float4 frag() : SV_Target {{ return {output}; }}
            ENDCG
        }}
    }}
}}
");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader != null && ShaderUtil.ShaderHasError(shader))
            {
                foreach (var m in ShaderUtil.GetShaderMessages(shader))
                {
                    Debug.Log($"[MarkerLightColourTest] {name} shader: {m.message} (line {m.line})");
                }
                return null;
            }
            return shader;
        }
    }
}
#endif
