// Builds the bounce spike's first rig: does a float render texture that a blitter rewrites from
// itself every frame keep its state on a ChilloutVR avatar, and how often does it tick?
//
// A spring needs exactly that: last frame's state in, this frame's out. Two blitters each count
// into their own 2x1 float texture, one run by a render controller (where the squash's providers
// already run, so the real thing would sit there) and one left to the camera, as the control. A
// billboard reads both back, so the answer is visible in game with no parser and no sync.
//
// Per texture, texel 0 holds four sums decayed by 0.99 a run (about a second of frames): runs,
// unity_DeltaTime, and the steps of _Time.y between runs, with the last _Time.y seen. Runs over
// time is the tick rate; summed frame times over time passed is runs per frame, 1 when it ticks
// once a frame, 2 when twice, below 1 when it skips. Texel 1 is a wrapping counter for the sweep.
//
//   -executeMethod AvatarBridge.Regression.BounceSpikeBuilder.Build
//   -executeMethod AvatarBridge.Regression.BounceSpikeBuilder.Verify -bounceOut <dir>
//
// Verify builds, plays the preview scene in batch at a fixed 60 fps, reads both textures back
// after 600 frames and renders the readout. Batch, never -nographics.
#if CVR_CCK_EXISTS && UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LowLevel;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    [InitializeOnLoad]
    public static class BounceSpikeBuilder
    {
        const string Folder = "Assets/AvatarBridge_Bounce";
        const string Prefab = Folder + "/Bounce_Stage1.prefab";
        const string Scene = Folder + "/Bounce_Preview.unity";
        const string K = "BounceSpike.";
        const int Frames = 600;
        struct Late { }

        static int frame;
        static Camera eye;

        static BounceSpikeBuilder()
        {
            if (SessionState.GetInt(K + "on", 0) == 1) EditorApplication.update += Hook;
        }

        public static void Build()
        {
            bool ok = Make();
            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static void Verify()
        {
            if (!Make())
            {
                EditorApplication.Exit(1);
                return;
            }
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-bounceOut");
            SessionState.SetString(K + "out", at >= 0 && at + 1 < args.Length ? args[at + 1] : "Bounce");
            SessionState.SetInt(K + "on", 1);
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            EditorApplication.update += Hook;
            EditorApplication.EnterPlaymode();
        }

        static bool Make()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "AvatarBridge_Bounce");
            File.WriteAllText(Folder + "/BounceCount.shader", CountShader);
            File.WriteAllText(Folder + "/BounceReadout.shader", ReadoutShader);
            File.WriteAllText(Folder + "/README.md", Readme);
            AssetDatabase.Refresh();

            var count = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/BounceCount.shader");
            var readout = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/BounceReadout.shader");
            if (count == null || readout == null || ShaderUtil.ShaderHasError(count) || ShaderUtil.ShaderHasError(readout))
            {
                Debug.Log("[Bounce] BUILD ABORT: a shader did not import or has errors");
                return false;
            }

            var managedState = MakeTexture("/Bounce_RenderController.renderTexture");
            var cameraState = MakeTexture("/Bounce_Camera.renderTexture");
            var managedBlit = Save(new Material(count), "/Bounce_RenderController.mat");
            var cameraBlit = Save(new Material(count), "/Bounce_Camera.mat");
            var shown = new Material(readout);
            shown.SetTexture("_Managed", managedState);
            shown.SetTexture("_Camera", cameraState);
            Save(shown, "/Bounce_Readout.mat");

            var root = new GameObject("Bounce_Stage1");

            var controlled = new GameObject("Counted by the render controller");
            controlled.transform.SetParent(root.transform, false);
            var blitter = controlled.AddComponent<CVRBlitter>();
            // The same texture in and out: the blitter then renders into a temporary and copies
            // it back, which is the client's own route for a self-update.
            blitter.originTexture = managedState;
            blitter.destinationTexture = managedState;
            blitter.blitMaterial = managedBlit;
            blitter.destinationTextures = new RenderTexture[0];
            var controller = controlled.AddComponent<CVRRenderController>();
            controller.cvrRenderComponents.Add(blitter);

            var free = new GameObject("Counted by the camera");
            free.transform.SetParent(root.transform, false);
            var control = free.AddComponent<CVRBlitter>();
            control.originTexture = cameraState;
            control.destinationTexture = cameraState;
            control.blitMaterial = cameraBlit;
            control.destinationTextures = new RenderTexture[0];

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Readout";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(root.transform, false);
            quad.transform.localPosition = new Vector3(0f, 2.1f, 0f);
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = shown;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            Object.DestroyImmediate(root);
            if (prefab == null)
            {
                Debug.Log("[Bounce] BUILD FAILED: the prefab did not save");
                return false;
            }

            // A scene to press Play in, so the rig can be watched in the Game view before uploading.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PrefabUtility.InstantiatePrefab(prefab, scene);
            var look = new GameObject("Main Camera") { tag = "MainCamera" };
            look.transform.position = new Vector3(0f, 2.1f, -0.4f);
            var cam = look.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 40f;
            EditorSceneManager.SaveScene(scene, Scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Bounce] BUILD ok: {Prefab}, {Scene}");
            return true;
        }

        static RenderTexture MakeTexture(string file)
        {
            // Float, so a sum keeps its digits; point and no mips, so a texel is only ever itself.
            var rt = new RenderTexture(2, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
            };
            return Save(rt, file);
        }

        static T Save<T>(T asset, string file) where T : Object
        {
            AssetDatabase.DeleteAsset(Folder + file);
            AssetDatabase.CreateAsset(asset, Folder + file);
            return AssetDatabase.LoadAssetAtPath<T>(Folder + file);
        }

        static void Hook()
        {
            if (!EditorApplication.isPlaying || eye != null || SessionState.GetInt(K + "on", 0) != 1) return;
            eye = Object.FindObjectsOfType<Camera>().First();
            eye.targetTexture = new RenderTexture(600, 300, 24);
            Time.captureFramerate = 60;
            frame = 0;
            // Batch draws nothing on its own, so the camera is rendered once a frame by hand, after
            // every script's LateUpdate, where the player draws too. The camera blitter counts on it.
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                var sys = loop.subSystemList[i];
                if (sys.type != typeof(UnityEngine.PlayerLoop.PostLateUpdate)) continue;
                var list = sys.subSystemList.ToList();
                list.Add(new PlayerLoopSystem { type = typeof(Late), updateDelegate = OnLate });
                sys.subSystemList = list.ToArray();
                loop.subSystemList[i] = sys;
            }
            PlayerLoop.SetPlayerLoop(loop);
        }

        static void OnLate()
        {
            if (!EditorApplication.isPlaying || eye == null) return;
            eye.Render();
            if (++frame < Frames) return;

            string dir = SessionState.GetString(K + "out", "Bounce");
            Directory.CreateDirectory(dir);
            bool ok = true;
            foreach (var blitter in Object.FindObjectsOfType<CVRBlitter>().OrderBy(b => b.name))
            {
                var s = Read(blitter.destinationTexture);
                float rate = s[0].x / Mathf.Max(s[0].w, 1e-6f), per = s[0].z / Mathf.Max(s[0].w, 1e-6f);
                // The render controller's blitter may run a frame or two before this counts, so the
                // sweep only has to have moved.
                bool good = Mathf.Abs(rate - 60f) < 1f && Mathf.Abs(per - 1f) < 0.01f && s[1].x > 0f;
                ok &= good;
                Debug.Log($"[Bounce] {(good ? "ok" : "FAIL")} {blitter.name}: runs {s[0].x:0.###}, last time {s[0].y:0.###}, " +
                          $"frame time {s[0].z:0.####}, time passed {s[0].w:0.####}, sweep {s[1].x:0}, rate {rate:0.##}/s, runs per frame {per:0.####}");
            }
            var shot = eye.targetTexture;
            var was = RenderTexture.active;
            RenderTexture.active = shot;
            var png = new Texture2D(shot.width, shot.height, TextureFormat.RGB24, false);
            png.ReadPixels(new Rect(0, 0, shot.width, shot.height), 0, 0);
            RenderTexture.active = was;
            File.WriteAllBytes(Path.Combine(dir, "readout.png"), png.EncodeToPNG());
            Debug.Log($"[Bounce] {(ok ? "PASS" : "FAIL")} after {frame} frames");
            SessionState.SetInt(K + "on", 0);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static Vector4[] Read(RenderTexture rt)
        {
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = was;
            return tex.GetPixels().Select(c => new Vector4(c.r, c.g, c.b, c.a)).ToArray();
        }

        const string CountShader = @"// The bounce spike's counter. Written by BounceSpikeBuilder; edits here are overwritten.
Shader ""Hidden/AvatarBridge/BounceCount""
{
    Properties
    {
        _MainTex (""Last frame's state"", 2D) = ""black"" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 4.0
            #include ""UnityCG.cginc""

            // Loaded, never sampled: a texel reads only itself, whatever the filter or the flip.
            Texture2D<float4> _MainTex;

            // About a second of runs, so a change shows soon and no sum outgrows a float.
            static const float Keep = 0.99;

            float4 frag(v2f_img i) : SV_Target
            {
                int2 px = int2(i.pos.xy);
                float4 s = _MainTex.Load(int3(px, 0));
                float t = _Time.y;
                // The sweep's counter. NaN fails the test, so garbage starts it at 0.
                if (px.x == 1) return float4(s.x >= 0 && s.x < 240 ? fmod(floor(s.x) + 1, 240) : 0, 0, 0, 1);
                // x runs, y _Time.y at the last run, z unity_DeltaTime, w _Time.y's steps between
                // runs. A fresh texture holds zeros or garbage, and time going backwards (a new
                // level) makes w negative: both start over.
                if (!(s.x >= 1 && s.x < 1000 && s.z >= 0 && s.w >= 0)) return float4(1, t, 0, 0);
                return float4(s.x * Keep + 1, t, s.z * Keep + unity_DeltaTime.x, s.w * Keep + (t - s.y));
            }
            ENDCG
        }
    }
    Fallback Off
}
";

        const string ReadoutShader = @"// The bounce spike's readout. Written by BounceSpikeBuilder; edits here are overwritten.
//
// Two rows, top the render controller's texture, bottom the camera's. Three cells a row:
//   sweep            a white line crossing while the blit runs; still when it stops;
//                    all three cells dark when the texture was never written
//   rate             blue bar, runs per second, out of 180 with a tick every 30;
//                    amber for the first quarter second; red if _Time never moved in the blit
//   runs per frame   bar out of 2, the middle marked: green at 1, amber near, red off
Shader ""AvatarBridge/Bounce Readout""
{
    Properties
    {
        // Declared, not only set: the client points a material's render textures at the avatar
        // copy's own by walking its declared properties, and an undeclared one would go on
        // reading the asset, which nothing writes.
        _Managed (""Counted by the render controller"", 2D) = ""black"" {}
        _Camera (""Counted by the camera"", 2D) = ""black"" {}
        _Size (""Width in metres"", Float) = 0.3
    }
    SubShader
    {
        Tags { ""Queue"" = ""Overlay"" ""RenderType"" = ""Overlay"" ""IgnoreProjector"" = ""True"" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            #pragma multi_compile_instancing
            #include ""UnityCG.cginc""

            Texture2D<float4> _Managed;
            Texture2D<float4> _Camera;
            float _Size;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // A view-space billboard at the object, so it faces every eye and mirror.
                float3 centre = mul(UNITY_MATRIX_V, float4(mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz, 1)).xyz;
                float2 off = (v.uv - 0.5) * float2(_Size, _Size * 0.4);
                o.pos = mul(UNITY_MATRIX_P, float4(centre + float3(off, 0), 1));
                o.uv = v.uv;
                return o;
            }

            static const float3 Dead = float3(0.03, 0.03, 0.03);
            static const float3 Bad = float3(0.85, 0.15, 0.10);
            static const float3 Half = float3(0.95, 0.70, 0.10);
            static const float3 Good = float3(0.15, 0.80, 0.25);
            static const float3 Bar = float3(0.20, 0.45, 0.95);

            float3 Cell(Texture2D<float4> state, int cell, float x)
            {
                float4 s = state.Load(int3(0, 0, 0));
                float n = state.Load(int3(1, 0, 0)).x;
                // NaN fails this too.
                if (!(s.x >= 1 && s.x < 1000)) return Dead;
                if (cell == 0) return abs(x - n / 240.0) < 0.025 ? float3(1, 1, 1) : Dead;
                if (s.w <= 0) return Bad;
                bool warm = s.w > 0.25;
                if (cell == 1)
                {
                    if (frac(x * 6.0 + 0.05) < 0.1) return float3(0, 0, 0);
                    return x < s.x / s.w / 180.0 ? (warm ? Bar : Half) : Dead;
                }
                float per = s.z / s.w;
                float3 c = !warm ? Half : (abs(per - 1) < 0.03 ? Good : (abs(per - 1) < 0.25 ? Half : Bad));
                if (per < 0.05) return Bad;
                if (abs(x - 0.5) < 0.025) return float3(0.6, 0.6, 0.6);
                return x < per * 0.5 ? c : Dead;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                bool top = i.uv.y > 0.5;
                float y = frac(i.uv.y * 2.0);
                float cx = i.uv.x * 3.0;
                int cell = (int) floor(cx);
                float x = frac(cx);
                // A dark gutter, so six cells read as six.
                if (x < 0.03 || x > 0.97 || y < 0.1 || y > 0.9) return fixed4(0, 0, 0, 1);
                // Rescaled past the gutter, so the bars and ticks span the cell's face.
                x = saturate((x - 0.03) / 0.94);
                return fixed4(top ? Cell(_Managed, cell, x) : Cell(_Camera, cell, x), 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
";

        const string Readme = @"# Bounce spike, stage 1

One question the editor cannot answer: does a float render texture that a blitter rewrites from
itself every frame keep its state on a ChilloutVR avatar, and does it tick once a frame? A bounce
that outlives the push needs exactly that, last frame's state in and this frame's out.

Two blitters count, each into its own texture. One is run by a render controller, which is where
the real thing would sit. The other runs off the camera, as the control. The readout above the
avatar shows both: top row the render controller, bottom row the camera.

## Setting it up

1. Drop `Bounce_Stage1.prefab` under the root of any avatar you can upload.
2. Move the `Readout` child wherever you can see it. It always faces you and draws over
   everything, so a mirror works too. 2.1 m above the avatar's root by default.
3. Upload. Nothing else: no parameters, no menu, no sync.

Before uploading, `Bounce_Preview.unity` shows it in the editor: open it and press Play.

## Reading a row

| Cell | Healthy | Otherwise |
| --- | --- | --- |
| Sweep (left) | A white line crossing over and over | Still: the blit stopped. All three cells dark: the texture was never written, or the readout reads a different copy of it than the blitter writes |
| Rate (middle) | A blue bar ending at your frame rate, a tick every 30 per second | Red: time does not move inside the blit. Twice your frame rate: it runs twice a frame |
| Runs per frame (right) | Green, half full, at the middle mark | Red, full: twice a frame. Red, short: it skips frames. Red, empty: the frame time is 0 inside the blit |

## What to try

1. Wear it. Both rows should look healthy. Note anything that is not.
2. Have somebody look at you, or watch from a second client. Their readout is their own copy's,
   at their frame rate, and both rows should look healthy for them too.
3. Switch to another avatar and back. The sweep should be moving again within a second.
4. Optional: hide the avatar for a while (out of view, or blocked) and look again.

The top row is the answer. The bottom row only says whether a failure is the render controller's
or the texture's: if both fail, the texture or the readout is at fault; if only the top fails,
the render controller does not run the blitter.
";
    }
}
#endif
