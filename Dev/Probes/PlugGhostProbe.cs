#if CVR_CCK_EXISTS && UNITY_EDITOR && AVATARBRIDGE_YAPS
// The plug "ghost": a faint plug shape seen through the receiving skin in game.
// Does the plug's depth pass (ShadowCaster, which fills the camera depth texture
// post effects read) draw where the visible pass does not?
//
//   -executeMethod AvatarBridge.Regression.PlugGhostProbe.Run
//       [-ghostAvatar <converted prefab>] [-ghostOut <dir>]
//
// A converted avatar's real plug on its real patched material, a socket off its
// axis, resolved through the atlas only and through marker lights only. Per route:
//   mask   the plug alone: pixels the depth texture covers and the colour does
//          not (caster only), and the reverse (forward only);
//   ghost  an opaque box behind the socket entrance as the receiving body:
//          pixels where the depth texture changes with the plug on while the
//          colour does not.
// Both should be 0 past a two-pixel edge. PNGs of each frame with -ghostOut.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    public static class PlugGhostProbe
    {
        const string Tag = "[PlugGhost] ";
        const int W = 768, H = 768, Edge = 2;
        const string DefaultAvatar = "Assets/AvatarBridgeOutput/Sally_PC_SPS/Sally_PC_SPS (ChilloutVR).prefab";

        const string DepthShader = @"Shader ""Hidden/PlugGhostDepth"" { SubShader { Pass {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include ""UnityCG.cginc""
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 frag (v2f_img i) : SV_Target
            {
                float d = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
                return float4(d, Linear01Depth(d), 0, 1);
            }
            ENDCG } } }";

        static void Log(string s) => Debug.Log(Tag + s);

        public static void Run()
        {
            int code = 1;
            var cleanup = new List<Action>();
            try { code = Measure(cleanup) ? 0 : 1; }
            catch (Exception e) { Debug.LogException(e); code = 2; }
            finally { foreach (var c in cleanup) c(); }
            Log(code == 0 ? "RESULT PASS" : "RESULT FAIL");
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static bool Measure(List<Action> cleanup)
        {
            string path = Arg("-ghostAvatar") ?? DefaultAvatar;
            string outDir = Arg("-ghostOut");
            if (outDir != null) Directory.CreateDirectory(outDir);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new Exception("no prefab at " + path);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var avatar = (GameObject) PrefabUtility.InstantiatePrefab(prefab);
            avatar.transform.position = Vector3.zero;
            var plug = avatar.GetComponentsInChildren<YapsPlug>(true).FirstOrDefault(p => p.Target is SkinnedMeshRenderer);
            if (plug == null) throw new Exception("no skinned plug on " + path);
            var skin = (SkinnedMeshRenderer) plug.Target;
            for (var t = skin.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
            skin.updateWhenOffscreen = true;
            // The plug alone: every other renderer of the avatar off.
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
                if (r != skin) r.enabled = false;
            foreach (var s in avatar.GetComponentsInChildren<YapsSocket>(true)) s.gameObject.SetActive(false);
            float length = skin.sharedMaterials.Where(m => m != null && m.HasProperty("_YAPS_Length"))
                .Select(m => m.GetFloat("_YAPS_Length")).DefaultIfEmpty(0f).Max();
            var marker = avatar.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "YAPS Markers") ?? plug.transform;
            Vector3 root = marker.position, fwd = marker.forward, up = marker.up;
            var side = Vector3.Cross(fwd, up).normalized;
            Log($"avatar {path}; plug \"{skin.name}\" on {string.Join(", ", skin.sharedMaterials.Where(m => m != null).Select(m => m.shader.name).Distinct())}; length {length:0.000} m");
            Log($"GPU {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}, reversed Z {SystemInfo.usesReversedZBuffer}");

            var plumbing = new GameObject("__Atlas");
            YapsAtlas.AddGrab(plumbing.transform);
            YapsAtlas.AddClear(plumbing.transform);

            var sunGo = new GameObject("__Sun");
            sunGo.transform.rotation = Quaternion.LookRotation(-up + side * 0.3f, fwd);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1f;
            sun.shadows = LightShadows.None;

            // The receiving body: an opaque box whose front face sits just behind the entrance.
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "__Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            var bodyMat = new Material(Shader.Find("Standard")) { color = new Color(0.85f, 0.7f, 0.6f, 1) };
            body.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;

            // A floor under the plug for the sun's shadow, off until the shadow check.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "__Floor";
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;
            floor.transform.SetPositionAndRotation(root - up * (0.6f * length), Quaternion.LookRotation(fwd, up));
            floor.transform.localScale = Vector3.one * length;
            floor.SetActive(false);
            // Put back after: batch Unity saves them into the project on exit.
            var shadows = QualitySettings.shadows;
            var distance = QualitySettings.shadowDistance;
            var resolution = QualitySettings.shadowResolution;
            cleanup.Add(() =>
            {
                QualitySettings.shadows = shadows;
                QualitySettings.shadowDistance = distance;
                QualitySettings.shadowResolution = resolution;
            });
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowDistance = 20f;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;

            var camGo = new GameObject("__Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 20f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.renderingPath = RenderingPath.Forward;
            cam.allowMSAA = false;
            cam.allowHDR = false;
            cam.depthTextureMode = DepthTextureMode.Depth;
            var colourRt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var depthRt = new RenderTexture(W, H, 0, RenderTextureFormat.RGFloat, RenderTextureReadWrite.Linear);
            cam.targetTexture = colourRt;
            var depthMat = new Material(ShaderUtil.CreateShaderAsset(DepthShader));
            var cb = new CommandBuffer { name = "ghost depth" };
            cb.Blit(BuiltinRenderTextureType.CurrentActive, depthRt, depthMat);
            cam.AddCommandBuffer(CameraEvent.AfterEverything, cb);
            var readC = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var readD = new Texture2D(W, H, TextureFormat.RGFloat, false, true);
            cleanup.Add(() =>
            {
                cam.RemoveCommandBuffer(CameraEvent.AfterEverything, cb);
                cb.Release();
                cam.targetTexture = null;
                colourRt.Release();
                depthRt.Release();
            });

            (Color32[] c, Color[] d) Frame()
            {
                // Three times: the atlas is a named GrabPass, and the depth pass runs
                // before the frame's grab, so a single render reads last frame's.
                for (int i = 0; i < 3; i++) { YapsOwnerStandIn.Apply(); cam.Render(); }
                RenderTexture.active = colourRt;
                readC.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                RenderTexture.active = depthRt;
                readD.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                RenderTexture.active = null;
                return (readC.GetPixels32(), readD.GetPixels());
            }
            bool reversed = SystemInfo.usesReversedZBuffer;
            bool Hit(Color d) => reversed ? d.r > 1e-7f : d.r < 1f - 1e-7f;
            bool Lit(Color32 c) => c.r > 3 || c.g > 3 || c.b > 3;

            bool all = true;
            var routes = new (string label, bool atlas, bool lights)[]
            {
                ("atlas only", true, false),
                ("marker lights only", false, true),
                ("atlas and lights", true, true),
            };
            foreach (float angle in new[] { 35f, 60f })
            foreach (var route in routes)
            {
                var sock = MakeSocket("__Socket", route.atlas, route.lights);
                var dir = Quaternion.AngleAxis(angle, up) * fwd;
                var at = root + dir * (0.7f * length);
                sock.transform.SetPositionAndRotation(at, Quaternion.LookRotation(root - at, up));
                var into = -sock.transform.forward;
                body.transform.SetPositionAndRotation(at + into * (0.005f + 0.75f * length), sock.transform.rotation);
                body.transform.localScale = new Vector3(1.2f * length, 1.2f * length, 1.5f * length);

                // From the plug's side of the entrance, off to one side, looking past the socket.
                var mid = Vector3.Lerp(root, at, 0.6f);
                camGo.transform.position = mid + Vector3.Cross(dir, up).normalized * (1.6f * length) + up * (0.5f * length);
                camGo.transform.LookAt(at + into * (0.2f * length), up);

                string name = $"{route.label} {angle:0}deg";
                // Straight reference: no socket, so the colour of an unbent plug.
                sock.SetActive(false);
                body.SetActive(false);
                var straight = Frame();
                sock.SetActive(true);
                var alone = Frame();
                int bent = 0;
                for (int i = 0; i < alone.c.Length; i++) if (Lit(alone.c[i]) != Lit(straight.c[i])) bent++;

                bool[] mc = alone.c.Select(Lit).ToArray(), md = alone.d.Select(Hit).ToArray();
                bool[] mcWide = Dilate(mc, Edge), mdWide = Dilate(md, Edge);
                var straightWide = Dilate(straight.c.Select(Lit).ToArray(), Edge);
                int casterOnly = 0, forwardOnly = 0, unbent = 0;
                var casterMask = new bool[mc.Length];
                for (int i = 0; i < mc.Length; i++)
                {
                    if (md[i] && !mcWide[i]) { casterOnly++; casterMask[i] = true; if (straightWide[i]) unbent++; }
                    if (mc[i] && !mdWide[i]) forwardOnly++;
                }

                body.SetActive(true);
                var withPlug = Frame();
                skin.enabled = false;
                var without = Frame();
                skin.enabled = true;
                int ghost = 0, depthMoved = 0;
                var ghostMask = new bool[mc.Length];
                bool[] colourMoved = new bool[mc.Length];
                for (int i = 0; i < mc.Length; i++)
                {
                    var a = withPlug.c[i]; var b = without.c[i];
                    colourMoved[i] = Math.Abs(a.r - b.r) > 2 || Math.Abs(a.g - b.g) > 2 || Math.Abs(a.b - b.b) > 2;
                }
                var colourWide = Dilate(colourMoved, Edge);
                for (int i = 0; i < mc.Length; i++)
                {
                    if (Mathf.Abs(withPlug.d[i].r - without.d[i].r) <= 1e-6f) continue;
                    depthMoved++;
                    if (!colourWide[i]) { ghost++; ghostMask[i] = true; }
                }
                // The sun's shadow on the floor, plug drawn as shadow only, from above.
                body.SetActive(false);
                floor.SetActive(true);
                sun.shadows = LightShadows.Hard;
                var view = (camGo.transform.position, camGo.transform.rotation);
                camGo.transform.position = Vector3.Lerp(root, at, 0.5f) + up * (2.2f * length);
                camGo.transform.LookAt(Vector3.Lerp(root, at, 0.5f) - up * (0.6f * length), fwd);
                skin.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                var shBent = Frame().c;
                sock.SetActive(false);
                var shStraight = Frame().c;
                sock.SetActive(true);
                skin.enabled = false;
                var shNone = Frame().c;
                skin.enabled = true;
                skin.shadowCastingMode = ShadowCastingMode.On;
                sun.shadows = LightShadows.None;
                floor.SetActive(false);
                camGo.transform.SetPositionAndRotation(view.Item1, view.Item2);
                bool Dark(Color32 a, Color32 b) => b.r - a.r > 10;
                int shadowPx = 0, shadowMoved = 0;
                for (int i = 0; i < shNone.Length; i++)
                {
                    bool x = Dark(shBent[i], shNone[i]), y = Dark(shStraight[i], shNone[i]);
                    if (x) shadowPx++;
                    if (x != y) shadowMoved++;
                }
                Log($"  shadow {name}: {shadowPx} px, {shadowMoved} px differ from the unbent plug's shadow");

                bool ok = casterOnly == 0 && ghost == 0;
                all &= ok;
                Log($"{(ok ? "PASS" : "FAIL")} {name}: bend {bent} px off the straight plug; plug alone {mc.Count(x => x)} colour px, " +
                    $"{md.Count(x => x)} depth px, {casterOnly} depth only ({unbent} on the unbent plug), {forwardOnly} colour only; with the body " +
                    $"{depthMoved} depth px moved by the plug, {ghost} of them where the colour shows only the body");
                if (bent < 200) Log($"WARN {name}: the plug barely moved, so the socket may not have resolved on this route");
                if (outDir != null)
                {
                    string stem = Path.Combine(outDir, name.Replace(' ', '-'));
                    Png(stem + "-alone.png", alone.c, alone.d, casterMask);
                    Png(stem + "-body.png", withPlug.c, withPlug.d, ghostMask);
                }
                Object.DestroyImmediate(sock);
            }
            return all;
        }

        // Colour on the left, depth on the right (Linear01, near bright), marked pixels red on both.
        static void Png(string file, Color32[] c, Color[] d, bool[] mark)
        {
            var tex = new Texture2D(W * 2, H, TextureFormat.RGB24, false);
            var px = new Color32[W * 2 * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                byte g = (byte) Mathf.Clamp(255f * (1f - d[i].g * 4f), 0, 255);
                var red = new Color32(255, 0, 0, 255);
                px[y * W * 2 + x] = mark[i] ? red : c[i];
                px[y * W * 2 + W + x] = mark[i] ? red : new Color32(g, g, g, 255);
            }
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static bool[] Dilate(bool[] m, int r)
        {
            var o = new bool[m.Length];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (!m[y * W + x]) continue;
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx >= 0 && yy >= 0 && xx < W && yy < H) o[yy * W + xx] = true;
                }
            }
            return o;
        }

        // As the bend tester builds one: the toolkit's socket, cut down to one transport.
        static GameObject MakeSocket(string name, bool atlas, bool lights)
        {
            var go = new GameObject(name);
            var socket = go.AddComponent<YapsSocket>();
            socket.kind = YapsSocket.SocketKind.Hole;
            socket.emitLights = lights;
            YapsSocketBuilder.Build(socket);
            if (!atlas)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    if (YapsMarks.IsAtlasMaterial(r.sharedMaterial))
                        Object.DestroyImmediate(r.gameObject);
            return go;
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
#endif
