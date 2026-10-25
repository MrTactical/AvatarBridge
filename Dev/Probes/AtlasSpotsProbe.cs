#if CVR_CCK_EXISTS && UNITY_EDITOR && AVATARBRIDGE_YAPS
// The atlas cells seen as spots: a camera clearing to a solid colour or to transparent has
// nothing in the scene to draw over the top-left rect the atlas writes into.
//
//   -executeMethod AvatarBridge.Regression.AtlasSpotsProbe.Run [-spotsAvatar <converted prefab>]
//
// A converted avatar's plug and a socket resolved through the atlas only, against an opaque
// background, a transparent one, and a picture an earlier camera left, which this camera only
// draws over. Per background:
//   spots  pixels that differ from the background with the plug hidden: should be 0,
//          with a marker light on the plug so its light relay writes too
//   bend   pixels the plug moves when the socket appears: the atlas still reads (> 200)
// First, the clear and the grab compile with STEREO_INSTANCING_ON, which no batch camera renders.
using System;
using System.Collections.Generic;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    public static class AtlasSpotsProbe
    {
        const string Tag = "[AtlasSpots] ";
        const int W = 1280, H = 720;
        const string DefaultAvatar = "Assets/AvatarBridgeOutput/Sally_PC_SPS/Sally_PC_SPS (ChilloutVR).prefab";

        static void Log(string s) => Debug.Log(Tag + s);

        public static void Run()
        {
            int code = 1;
            var cleanup = new List<Action>();
            try { code = Stereo() & Measure(cleanup) ? 0 : 1; }
            catch (Exception e) { Debug.LogException(e); code = 2; }
            finally { foreach (var c in cleanup) c(); }
            Log(code == 0 ? "RESULT PASS" : "RESULT FAIL");
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static bool Measure(List<Action> cleanup)
        {
            string path = Arg("-spotsAvatar") ?? DefaultAvatar;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new Exception("no prefab at " + path);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var avatar = (GameObject) PrefabUtility.InstantiatePrefab(prefab);
            avatar.transform.position = Vector3.zero;
            var plug = avatar.GetComponentsInChildren<YapsPlug>(true).FirstOrDefault(p => p.Target is SkinnedMeshRenderer);
            if (plug == null) throw new Exception("no skinned plug on " + path);
            var skin = (SkinnedMeshRenderer) plug.Target;
            for (var t = skin.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
            // A prefab converted before the light relay wears re-patched copies, and
            // gets the relay the next conversion would build.
            skin.sharedMaterials = skin.sharedMaterials.Select(m => m != null && YapsShaderPatcher.IsStale(m) ? Repatched(m) : m).ToArray();
            YapsOwner.Relays(avatar);
            var relay = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => YapsAtlas.IsRelay(r.sharedMaterial));
            if (relay == null) throw new Exception("no light relay built on " + path);
            skin.updateWhenOffscreen = true;
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
                if (r != skin && r != relay) r.enabled = false;
            foreach (var s in avatar.GetComponentsInChildren<YapsSocket>(true)) s.gameObject.SetActive(false);
            float length = skin.sharedMaterials.Where(m => m != null && m.HasProperty("_YAPS_Length"))
                .Select(m => m.GetFloat("_YAPS_Length")).DefaultIfEmpty(0f).Max();
            var marker = avatar.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "YAPS Markers") ?? plug.transform;
            Vector3 root = marker.position, fwd = marker.forward, up = marker.up;
            Log($"avatar {path}; plug \"{skin.name}\"; length {length:0.000} m; GPU {SystemInfo.graphicsDeviceType}");

            var plumbing = new GameObject("__Atlas");
            YapsAtlas.AddGrab(plumbing.transform);
            YapsAtlas.AddClear(plumbing.transform);

            // Atlas only: a writer and no marker light.
            var sock = new GameObject("__Socket");
            var socket = sock.AddComponent<YapsSocket>();
            socket.kind = YapsSocket.SocketKind.Hole;
            socket.emitLights = false;
            YapsSocketBuilder.Build(socket);
            var dir = Quaternion.AngleAxis(35f, up) * fwd;
            var at = root + dir * (0.7f * length);
            sock.transform.SetPositionAndRotation(at, Quaternion.LookRotation(root - at, up));
            // Lights only, in the same place, on for the spots so the relay has a socket to write.
            var lit = new GameObject("__Lit");
            var litSocket = lit.AddComponent<YapsSocket>();
            litSocket.kind = YapsSocket.SocketKind.Hole;
            YapsSocketBuilder.Build(litSocket);
            foreach (var w in lit.GetComponentsInChildren<Renderer>(true)) w.enabled = false;
            lit.transform.SetPositionAndRotation(at, sock.transform.rotation);
            lit.SetActive(false);

            var camGo = new GameObject("__Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 20f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.renderingPath = RenderingPath.Forward;
            cam.allowMSAA = false;
            cam.allowHDR = false;
            var mid = Vector3.Lerp(root, at, 0.6f);
            camGo.transform.position = mid + Vector3.Cross(dir, up).normalized * (1.6f * length) + up * (0.5f * length);
            camGo.transform.LookAt(at, up);
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            var read = new Texture2D(W, H, TextureFormat.RGBA32, false);
            cleanup.Add(() => { cam.targetTexture = null; rt.Release(); });

            // Not one colour, so no sample of it can stand for the rest.
            var picture = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var tones = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    tones[y * W + x] = new Color32((byte) (x * 255 / W), (byte) (y * 255 / H), (byte) ((x ^ y) & 255), 255);
            picture.SetPixels32(tones);
            picture.Apply();
            Texture under = null;

            Color32[] Frame()
            {
                // Three times: the grab is the frame's own, the readers read last frame's.
                for (int i = 0; i < 3; i++)
                {
                    if (under != null) Graphics.Blit(under, rt);
                    YapsOwnerStandIn.Apply();
                    cam.Render();
                }
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                RenderTexture.active = null;
                return read.GetPixels32();
            }

            // The relay's strip in the frame's grab, columns 0 to 7.
            Color32[] Strip()
            {
                var grab = Shader.GetGlobalTexture("_YAPS_Atlas") as RenderTexture;
                if (grab == null) return new Color32[0];
                var strip = new Texture2D(8, grab.height, TextureFormat.RGBA32, false);
                RenderTexture.active = grab;
                strip.ReadPixels(new Rect(0, 0, 8, grab.height), 0, 0);
                RenderTexture.active = null;
                var px = strip.GetPixels32();
                Object.DestroyImmediate(strip);
                return px;
            }

            bool all = true;
            foreach (var bg in new Color32?[] { new Color32(51, 102, 153, 255), new Color32(0, 177, 64, 0), null })
            {
                Color32[] back;
                if (bg is Color32 solid)
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = solid;
                    under = null;
                    back = Enumerable.Repeat(solid, W * H).ToArray();
                }
                else
                {
                    cam.clearFlags = CameraClearFlags.Depth;
                    under = picture;
                    Graphics.Blit(picture, rt);
                    RenderTexture.active = rt;
                    read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    RenderTexture.active = null;
                    back = read.GetPixels32();
                }
                skin.enabled = false;
                lit.SetActive(true);
                var empty = Frame();
                var withRelay = Strip();
                relay.enabled = false;
                Frame();
                var noRelay = Strip();
                relay.enabled = true;
                lit.SetActive(false);
                // Pixels the relay wrote: the strip with it against the strip without.
                int relayPx = withRelay.Length == noRelay.Length ? withRelay.Where((c, i) => !c.Equals(noRelay[i])).Count() : 0;
                int spots = 0;
                for (int i = 0; i < empty.Length; i++)
                {
                    Color32 c = empty[i], b = back[i];
                    if (Math.Abs(c.r - b.r) > 1 || Math.Abs(c.g - b.g) > 1 || Math.Abs(c.b - b.b) > 1 || Math.Abs(c.a - b.a) > 1) spots++;
                }
                skin.enabled = true;
                sock.SetActive(false);
                var straight = Frame();
                sock.SetActive(true);
                var bent = Frame();
                bool Off(Color32 c, Color32 b) => Math.Abs(c.r - b.r) > 3 || Math.Abs(c.g - b.g) > 3 || Math.Abs(c.b - b.b) > 3;
                int moved = 0;
                for (int i = 0; i < bent.Length; i++) if (Off(bent[i], back[i]) != Off(straight[i], back[i])) moved++;
                bool ok = spots == 0 && moved > 200 && relayPx > 0;
                all &= ok;
                Log($"{(ok ? "PASS" : "FAIL")} background {(bg is Color32 named ? named.ToString() : "an earlier camera's picture")}: " +
                    $"{spots} atlas px left on screen, {relayPx} relay px in the grab, plug bend {moved} px");
            }
            return all;
        }

        static Material Repatched(Material m)
        {
            var copy = new Material(m) { name = m.name + " (probe)" };
            if (!YapsShaderPatcher.Refresh(copy, null)) Log($"WARN {m.name}: the re-patch failed");
            return copy;
        }

        static bool Stereo()
        {
            bool all = true;
            foreach (var name in new[] { "YAPS/Atlas Clear", "YAPS/Atlas Grab", "YAPS/Atlas Relay" })
            {
                var data = ShaderUtil.GetShaderData(Shader.Find(name)).GetSubshader(0);
                for (int p = 0; p < data.PassCount; p++)
                {
                    var pass = data.GetPass(p);
                    if (string.IsNullOrEmpty(pass.SourceCode)) continue;
                    foreach (var stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                        foreach (var keywords in new[] { new string[0], new[] { "STEREO_INSTANCING_ON" } })
                        {
                            var info = pass.CompileVariant(stage, keywords, ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
                            var errors = (info.Messages ?? new ShaderMessage[0]).Where(m => m.severity == ShaderCompilerMessageSeverity.Error).ToArray();
                            all &= info.Success && errors.Length == 0;
                            Log($"{(info.Success && errors.Length == 0 ? "PASS" : "FAIL")} {name} pass {p} {stage} " +
                                $"[{string.Join(" ", keywords)}]{(errors.Length > 0 ? ": " + errors[0].message : "")}");
                        }
                }
            }
            return all;
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
