#if CVR_CCK_EXISTS && UNITY_EDITOR && AVATARBRIDGE_YAPS
// The atlas cells seen as spots: a camera clearing to a solid colour or to transparent has
// nothing in the scene to draw over the top-left rect the atlas writes into.
//
//   -executeMethod AvatarBridge.Regression.AtlasSpotsProbe.Run [-spotsAvatar <converted prefab>]
//
// A converted avatar's plug and a socket resolved through the atlas only, against an opaque
// background and a transparent one. Per background:
//   spots  pixels that differ from the background with the plug hidden: should be 0
//   bend   pixels the plug moves when the socket appears: the atlas still reads (> 200)
using System;
using System.Collections.Generic;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
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
            try { code = Measure(cleanup) ? 0 : 1; }
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
            skin.updateWhenOffscreen = true;
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
                if (r != skin) r.enabled = false;
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

            Color32[] Frame()
            {
                // Three times: the grab is the frame's own, the readers read last frame's.
                for (int i = 0; i < 3; i++) { YapsOwnerStandIn.Apply(); cam.Render(); }
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                RenderTexture.active = null;
                return read.GetPixels32();
            }

            bool all = true;
            foreach (var bg in new[] { new Color32(51, 102, 153, 255), new Color32(0, 177, 64, 0) })
            {
                cam.backgroundColor = bg;
                skin.enabled = false;
                var empty = Frame();
                int spots = empty.Count(c => Math.Abs(c.r - bg.r) > 1 || Math.Abs(c.g - bg.g) > 1 || Math.Abs(c.b - bg.b) > 1 || Math.Abs(c.a - bg.a) > 1);
                skin.enabled = true;
                sock.SetActive(false);
                var straight = Frame();
                sock.SetActive(true);
                var bent = Frame();
                bool Off(Color32 c) => Math.Abs(c.r - bg.r) > 3 || Math.Abs(c.g - bg.g) > 3 || Math.Abs(c.b - bg.b) > 3;
                int moved = 0;
                for (int i = 0; i < bent.Length; i++) if (Off(bent[i]) != Off(straight[i])) moved++;
                bool ok = spots == 0 && moved > 200;
                all &= ok;
                Log($"{(ok ? "PASS" : "FAIL")} background {bg}: {spots} atlas px left on screen, plug bend {moved} px");
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
