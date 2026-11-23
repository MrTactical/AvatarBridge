#if CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.LowLevel;

namespace AvatarBridge.Regression
{
    using MagicaCloth2;

    // Slides a converted avatar forward and back and measures how far each Modular Avatar
    // hidden-skin bone ("NaNimated") strays from the bone it should ride. A cloth that simulates
    // one tears the skin weighted to it. -stKeepOut keeps them out of every cloth first, as the
    // converter now does. Frames of the chest go to -stOut/frames.
    // -executeMethod AvatarBridge.Regression.SlideTearProbe.Run -stPrefab <asset path> -stOut <dir> [-stKeepOut]
    [InitializeOnLoad]
    public static class SlideTearProbe
    {
        const string K = "SlideTear.";
        const string ScenePath = "Assets/ZZ_SlideTear/SlideTear.unity";
        const int Fps = 60, Frames = 510, Size = 640;
        static bool installed;
        static int frame;
        static Transform avatar, chest;
        static Vector3 start;
        static List<(Transform t, Vector3 pos, Quaternion rot, float maxAngle, float maxShift, float sumAngle)> helpers;
        static Camera cam;
        static float reach;
        static Transform[] breasts;
        static RenderTexture target;
        static Texture2D pixels;
        static string outDir;

        static SlideTearProbe() => EditorApplication.update += Tick;

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        static bool IsHelper(Transform t) => t.name.StartsWith("NaNimated") || t.name.StartsWith("NaNimation");

        public static void Run()
        {
            bool keep = Array.IndexOf(Environment.GetCommandLineArgs(), "-stKeepOut") >= 0;
            SessionState.SetString(K + "out", Arg("-stOut"));
            Directory.CreateDirectory(Path.Combine(Arg("-stOut"), "frames"));

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = (GameObject) PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Arg("-stPrefab")));
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "SlideTearAvatar";

            // Bind pose, no controller: the slide is the only motion.
            foreach (var a in go.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            // The hidden skin shown, as with the toggle that hides it switched off.
            int shown = 0;
            foreach (var t in go.GetComponentsInChildren<Transform>(true).Where(IsHelper))
            {
                foreach (var c in t.GetComponents<ScaleConstraint>()) { UnityEngine.Object.DestroyImmediate(c); shown++; }
                t.localScale = Vector3.one;
            }
            // The body alone in plain white, so a tear shows the background whatever shaders are installed.
            var white = new Material(Shader.Find("Standard")) { color = new Color(0.95f, 0.85f, 0.82f) };
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                bool body = r is SkinnedMeshRenderer && r.name.StartsWith("Body");
                r.enabled = body;
                if (body) r.sharedMaterials = r.sharedMaterials.Select(_ => white).ToArray();
                if (body) Debug.Log($"[SlideTear] showing {r.name}, bounds {r.bounds.size}");
            }
            var cloths = go.GetComponentsInChildren<MagicaCloth>(true);
            foreach (var c in cloths) c.SerializeData.cullingSettings.cameraCullingMode = CullingSettings.CameraCullingMode.Off;
            var buffers = go.GetComponentsInChildren<Transform>(true).Where(t => IsHelper(t) && !IsHelper(t.parent)).ToList();
            // -stAlso a;b: more bones kept out by name, to see whether keeping out takes at all.
            var also = (Arg("-stAlso") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var keepList = buffers.Concat(go.GetComponentsInChildren<Transform>(true).Where(t => also.Contains(t.name))).ToList();
            int stepped = 0;
            if (keep)
            {
                AssetDatabase.CreateFolder("Assets/ZZ_SlideTear", "Out");
                stepped = MagicaClothKeepOut.Separate(new BridgeContext { Target = go, OutputDir = "Assets/ZZ_SlideTear/Out" }, keepList);
            }
            int kept = keep ? MagicaClothKeepOut.KeepOut(go.transform, keepList) : 0;
            Debug.Log($"[SlideTear] {stepped} kept-out bones stepped off their parents");
            foreach (var c in cloths.Where(c => c.SerializeData.rootBones.Any(r => r != null && buffers.Any(b => b.IsChildOf(r)))))
                Debug.Log($"[SlideTear] cloth {c.name} on={c.enabled && c.gameObject.activeInHierarchy} roots={string.Join(",", c.SerializeData.rootBones.Select(r => r.name))}");
            Debug.Log($"[SlideTear] {buffers.Count} helper buffers, {shown} hiding constraints removed, keep-out {(keep ? $"on ({kept} cloths)" : "off")}");

            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(35f, 160f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);

            var ndmf = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("nadena.dev.ndmf.config.Config", false)).FirstOrDefault(x => x != null);
            ndmf?.GetProperty("ApplyOnPlay")?.SetValue(null, false);
            SessionState.SetInt(K + "stage", 1);
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            int stage = SessionState.GetInt(K + "stage", 0);
            if (stage == 1 && EditorApplication.isPlaying && !installed)
            {
                outDir = SessionState.GetString(K + "out", "");
                avatar = GameObject.Find("SlideTearAvatar").transform;
                start = avatar.position;
                chest = avatar.GetComponentsInChildren<Transform>(true).First(t => t.name == "Chest");
                helpers = avatar.GetComponentsInChildren<Transform>(true).Where(t => (IsHelper(t) || t.name.StartsWith("Breast")) && t.IsChildOf(chest))
                    .Select(t => (t, t.localPosition, t.localRotation, 0f, 0f, 0f)).ToList();
                // A fifth of the body's height away and above: the breast tops fill a 30 degree view.
                reach = avatar.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.enabled).Select(r => r.bounds.size.y).DefaultIfEmpty(1.5f).Max() * 0.2f;
                breasts = chest.Cast<Transform>().Where(t => t.name.StartsWith("Breast")).ToArray();
                cam = new GameObject("Cam").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.12f, 0.1f, 0.1f);
                cam.fieldOfView = 30f;
                cam.nearClipPlane = 0.05f;
                target = new RenderTexture(Size, Size, 24);
                pixels = new Texture2D(Size, Size, TextureFormat.RGB24, false);
                cam.targetTexture = target;
                Time.captureFramerate = Fps;
                Install();
                installed = true;
                frame = 0;
            }
            if (stage == 2 && !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.EraseInt(K + "stage");
                EditorApplication.Exit(0);
            }
        }

        struct Drive { }
        struct Read { }

        static void Install()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                var sys = loop.subSystemList[i];
                var list = sys.subSystemList.ToList();
                if (sys.type == typeof(UnityEngine.PlayerLoop.Update))
                    list.Insert(0, new PlayerLoopSystem { type = typeof(Drive), updateDelegate = DriveStep });
                if (sys.type == typeof(UnityEngine.PlayerLoop.PostLateUpdate))
                    list.Add(new PlayerLoopSystem { type = typeof(Read), updateDelegate = ReadStep });
                sys.subSystemList = list.ToArray();
                loop.subSystemList[i] = sys;
            }
            PlayerLoop.SetPlayerLoop(loop);
        }

        // 1 s still, 6 s of forward and back (0.35 m each way, 1.25 Hz, up to 2.7 m/s), 1.5 s still.
        static void DriveStep()
        {
            if (!EditorApplication.isPlaying || avatar == null) return;
            float t = frame / (float) Fps;
            float x = t < 1f || t > 7f ? 0f : 0.35f * Mathf.Sin(2f * Mathf.PI * 1.25f * (t - 1f));
            avatar.position = start + avatar.forward * x;
        }

        static void ReadStep()
        {
            if (!EditorApplication.isPlaying || avatar == null || SessionState.GetInt(K + "stage", 0) != 1) return;
            for (int i = 0; i < helpers.Count; i++)
            {
                var h = helpers[i];
                var p = h.t.parent;
                float angle = Quaternion.Angle(h.t.rotation, p.rotation * h.rot);
                float shift = (h.t.position - p.TransformPoint(h.pos)).magnitude;
                helpers[i] = (h.t, h.pos, h.rot, Mathf.Max(h.maxAngle, angle), Mathf.Max(h.maxShift, shift), h.sumAngle + angle);
            }
            if (frame % 2 == 0)
            {
                var top = (breasts[0].position + breasts[1].position) * 0.5f + Vector3.up * reach * 0.12f;
                cam.transform.position = top + avatar.forward * reach + Vector3.up * reach * 0.35f;
                cam.transform.LookAt(top);
                cam.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, "frames", $"{frame / 2:0000}.png"), pixels.EncodeToPNG());
            }
            if (++frame < Frames) return;

            var lines = helpers.Select(h => string.Format(CultureInfo.InvariantCulture,
                "{0:0.00}\t{1:0.0000}\t{2:0.00}\t{3}", h.maxAngle, h.maxShift, h.sumAngle / Frames, PathOf(h.t)));
            File.WriteAllLines(Path.Combine(outDir, "helpers.tsv"),
                new[] { "max_angle_deg\tmax_shift_m\tmean_angle_deg\tpath" }.Concat(lines));
            Debug.Log($"[SlideTear] worst {helpers.Max(h => h.maxAngle):0.00} deg, {helpers.Max(h => h.maxShift) * 1000f:0.0} mm over {helpers.Count} helpers");
            SessionState.SetInt(K + "stage", 2);
            EditorApplication.ExitPlaymode();
        }

        static string PathOf(Transform t) => t.parent == null || t == avatar ? t.name : PathOf(t.parent) + "/" + t.name;
    }
}
#endif
