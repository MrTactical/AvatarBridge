// Reads the live tissue's animator tells (#Jelly/<part>/Squeeze, Grab, Slap) off each part's points
// strip in Play mode while the Avatar Tester's test hand presses the first part. Pressed twice: with
// the step materials as built, then on copies with _Give 0.525, what a cloth-carried part had before
// 5.0.0. The copies are never saved, and it quits still in Play mode, so nothing writes the
// component's knobs back over the built materials. -jtBuild builds the squash and every part's tissue
// fresh on an unpacked copy of the prefab, into Assets/__JellyFloor (cleared from outside after), with
// the menu and animator left alone.
//
// -executeMethod AvatarBridge.Regression.JellyTellProbe.Run (-jtScene <scene> | -jtPrefab <prefab> [-jtBuild]) -jtOut <tsv>
// Batch, never -nographics: the steps are blits. No -quit: it exits itself.

#if AVATARBRIDGE_YAPS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    [InitializeOnLoad]
    public static class JellyTellProbe
    {
        const string K = "JellyTell.";
        // Rest, press as built, let go, press with the old give, let go.
        const int PressA = 120, ReleaseA = 300, OldGive = 360, PressB = 420, ReleaseB = 600, End = 660;
        static int frame = -1;
        static double startedAt;
        static List<(string part, RenderTexture strip, RenderTexture state)> parts;
        static Transform hold;
        static readonly List<string> rows = new List<string> { "frame\tphase\tpart\tsqueeze\tgrab\tslap\tgive\tsides" };

        static JellyTellProbe()
        {
            if (SessionState.GetInt(K + "on", 0) == 1) EditorApplication.update += Tick;
        }

        public static void Run()
        {
            SessionState.SetString(K + "out", Arg("-jtOut"));
            SessionState.SetInt(K + "on", 1);
            // -jtPrefab: the avatar alone in an empty scene, never saved.
            if (Arg("-jtPrefab") != "")
            {
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                var made = (GameObject) PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Arg("-jtPrefab")));
                if (Environment.GetCommandLineArgs().Contains("-jtBuild")) Build(made);
            }
            else EditorSceneManager.OpenScene(Arg("-jtScene"), OpenSceneMode.Single);
            // The scene may keep its tissue avatar switched off: on, alone, for the run (never saved).
            var s = UnityEngine.Object.FindObjectsOfType<YapsSquashSettings>(true).FirstOrDefault(x => x.transform.root.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith(YapsJelly.Worn + " ")));
            if (s != null)
            {
                foreach (var a in UnityEngine.Object.FindObjectsOfType<Animator>()) if (a.transform.root != s.transform.root) a.gameObject.SetActive(false);
                for (var t = s.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
                foreach (var w in s.transform.root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(YapsJelly.Worn + " ")))
                    for (var t = w; t != null; t = t.parent) t.gameObject.SetActive(true);
                Debug.Log("[JellyTell] avatar " + s.transform.root.name);
            }
            foreach (var a in UnityEngine.Object.FindObjectsOfType<Animator>()) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || SessionState.GetInt(K + "on", 0) != 1) return;
            if (frame < 0)
            {
                startedAt = EditorApplication.timeSinceStartup;
                var s = UnityEngine.Object.FindObjectsOfType<YapsSquashSettings>().FirstOrDefault();
                if (s == null) { Quit("no active YapsSquashSettings in the scene", 2); return; }
                var root = YapsSquash.RootOf(s);
                parts = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(YapsJelly.Worn + " "))
                    .Select(t => (t.name.Substring(YapsJelly.Worn.Length + 1), t.GetComponentInChildren<CVRTexturePropertyParser>(true)?.texture as RenderTexture,
                        t.GetComponentsInChildren<CVRBlitter>(true).Select(b => b.originTexture as RenderTexture).FirstOrDefault(r => r != null && r.name == "Jelly_State")))
                    .Where(p => p.Item2 != null).ToList();
                if (parts.Count == 0) { Quit("no live tissue with a points strip on " + root.name, 2); return; }
                YapsSquashPreview.Start(s);
                // Into the first part: the preview aims the hand at the nearest part from here.
                hold = YapsSquash.Slots(root)[0];
                Debug.Log($"[JellyTell] {root.name}: {string.Join(", ", parts.Select(p => p.part))}; pressing near {hold.name}");
                foreach (var b in root.GetComponentsInChildren<CVRBlitter>(true))
                    Debug.Log($"[JellyTell] blitter {b.name}: enabled {b.enabled}, active {b.gameObject.activeInHierarchy}, material {(b.blitMaterial != null ? b.blitMaterial.name + " " + b.blitMaterial.shader.name + (b.blitMaterial.shader.isSupported ? "" : " UNSUPPORTED") : "none")}, origin {(b.originTexture != null ? b.originTexture.name : "none")}");
                foreach (var c in root.GetComponentsInChildren<CVRRenderController>(true))
                    Debug.Log($"[JellyTell] controller {c.name}: enabled {c.enabled}, active {c.gameObject.activeInHierarchy}, components {c.cvrRenderComponents.Count}");
                foreach (var (part, strip, _) in parts) Debug.Log($"[JellyTell] strip {part}: {strip.name} {strip.width}x{strip.height} {strip.format} created {strip.IsCreated()}");
                frame = 0;
            }
            if (EditorApplication.timeSinceStartup - startedAt > 300) { Quit("timed out", 3); return; }

            bool pressing = (frame >= PressA && frame < ReleaseA) || (frame >= PressB && frame < ReleaseB);
            YapsSquashPreview.Hand = pressing;
            // A hand's length in front of the part's centre, pushed in to the centre: deep in the skin.
            YapsSquashPreview.HandAt(hold.position + hold.root.forward * 0.02f);
            if (frame == OldGive) SetGive(0.525f);

            string phase = frame < PressA ? "rest" : frame < ReleaseA ? "pressBuilt" : frame < PressB ? "release" : frame < ReleaseB ? "pressOldGive" : "release";
            foreach (var (part, strip, state) in parts)
            {
                var prev = RenderTexture.active;
                RenderTexture.active = strip;
                var read = new Texture2D(strip.width, 1, TextureFormat.RGBAFloat, false);
                read.ReadPixels(new Rect(0, 0, strip.width, 1), 0, 0);
                RenderTexture.active = prev;
                var px = read.GetPixels();
                UnityEngine.Object.DestroyImmediate(read);
                rows.Add($"{frame}\t{phase}\t{part}\t{px[3].r:0.####}\t{px[4].r:0.####}\t{px[5].r:0.####}\t{Give():0.###}\t{Sides(state)}");
            }
            if (++frame >= End) Quit(null, 0);
        }

        // Each region's row that ran: 86's strain and load, the own-capsule bits in 41, whether the
        // hand slots are live (1.w) and any grip held (112 to 119).
        static string Sides(RenderTexture state)
        {
            if (state == null) return "no state";
            var prev = RenderTexture.active;
            RenderTexture.active = state;
            var read = new Texture2D(state.width, state.height, TextureFormat.RGBAFloat, false);
            read.ReadPixels(new Rect(0, 0, state.width, state.height), 0, 0);
            RenderTexture.active = prev;
            var px = read.GetPixels();
            UnityEngine.Object.DestroyImmediate(read);
            var said = new List<string>();
            for (int y = 0; y < state.height; y++)
            {
                Color At(int x) => px[y * state.width + x];
                if (!(At(86).a > 0)) continue;
                bool grip = Enumerable.Range(112, 8).Any(x => At(x).a > 0);
                said.Add($"{y}:{At(86).r:0.###}/{At(86).g:0.####} own{(int) At(41).r} h{At(1).a:0.#} g{(grip ? 1 : 0)}");
            }
            return string.Join(" ", said);
        }

        static float Give() => UnityEngine.Object.FindObjectsOfType<CVRBlitter>(true).Select(b => b.blitMaterial)
            .Where(m => m != null && m.HasProperty("_Give")).Select(m => m.GetFloat("_Give")).DefaultIfEmpty(-1f).Max();

        static void Build(GameObject made)
        {
            PrefabUtility.UnpackPrefabInstance(made, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var root = made.GetComponentInChildren<CVRAvatar>(true).transform;
            const string folder = "Assets/__JellyFloor";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "__JellyFloor");
            Debug.Log("[JellyTell] squash: " + YapsSquash.Add(root, folder, out _));
            YapsShaderPatcher.EndPass(null, null);
            var found = YapsSquash.PartsOf(root, out _);
            foreach (var name in found?.parts.Select(x => x.name).Take(YapsSquash.JellySlots) ?? Enumerable.Empty<string>())
                Debug.Log("[JellyTell] tissue: " + YapsJelly.PairOn(root, new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(name) + "$", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                    folder + "/Jelly", YapsJelly.Shaders, new YapsJelly.Options { wire = false }, new BridgeReport()));
        }

        static void SetGive(float give)
        {
            var copies = new Dictionary<Material, Material>();
            foreach (var b in UnityEngine.Object.FindObjectsOfType<CVRBlitter>(true))
            {
                if (b.blitMaterial == null || !b.blitMaterial.HasProperty("_Give")) continue;
                if (!copies.TryGetValue(b.blitMaterial, out var copy))
                {
                    copy = copies[b.blitMaterial] = new Material(b.blitMaterial) { hideFlags = HideFlags.DontSave };
                    copy.SetFloat("_Give", give);
                }
                b.blitMaterial = copy;
            }
            // The providers feed the anchors and capsules into the materials they list: left on the
            // originals, a copy froze them, and the wearer's own hands read as pressing.
            int fed = 0;
            foreach (var p in UnityEngine.Object.FindObjectsOfType<CVRMaterialDataProvider>(true))
            {
                int was = fed;
                for (int i = 0; p.materials != null && i < p.materials.Length; i++)
                    if (p.materials[i] != null && copies.TryGetValue(p.materials[i], out var copy)) { p.materials[i] = copy; fed++; }
                // Off and on, in case it took its list once when enabled.
                if (fed > was && p.enabled) { p.enabled = false; p.enabled = true; }
            }
            Debug.Log($"[JellyTell] _Give {give} on {copies.Count} step material copies, {fed} provider slots moved onto them");
        }

        static void Quit(string why, int code)
        {
            SessionState.SetInt(K + "on", 0);
            EditorApplication.update -= Tick;
            var outFile = SessionState.GetString(K + "out", "");
            if (!string.IsNullOrEmpty(outFile)) File.WriteAllLines(outFile, rows);
            Debug.Log(why == null ? $"[JellyTell] done, {rows.Count - 1} rows into {outFile}" : "[JellyTell] FAIL: " + why);
            EditorApplication.Exit(code);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : "";
        }
    }
}
#endif
