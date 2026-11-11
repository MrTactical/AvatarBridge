#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    // What the live tissue costs a viewer's GPU with every part on: the squash built on a copy of an
    // avatar into a folder of its own, its parts put on the tissue as the squash's Build does, then
    // played with the CCK's render controller. Each part's chain of passes is timed alone, then the
    // first k together, at rest and with eight hands in reach of each part. The bench's way: warmed
    // a third of a second, 600 runs, the GPU waited on, five rounds, the median.
    //   -executeMethod AvatarBridge.Regression.JellyCostProbe.Run -costScene <scene> -costPrefab <prefab>
    //       [-costPaint] [-costSquashOnly] [-costSave <Assets/x.prefab>] [-costNoPlay]
    // -costPaint: the probe's painted parts (breasts, butt, a belly alone, thighs) instead of the
    // bone names. -costSquashOnly: no tissue. -costSave: the built copy saved as a prefab, for
    // UploadCostProbe. -force-device-index 1 runs it on the CPU's own GPU. Run without -quit; it
    // exits itself. Leaves Assets/__JellyCost for the caller to move out.
    [InitializeOnLoad]
    public static class JellyCostProbe
    {
        const string Folder = "Assets/__JellyCost";
        const string On = "AvatarBridge.JellyCostProbe.on";
        const string Name = "Jelly Cost Avatar";
        const int Settle = 240;
        static int frame;

        static JellyCostProbe()
        {
            if (SessionState.GetBool(On, false)) EditorApplication.update += Tick;
        }

        static void Log(string s) => Debug.Log("[JellyCost] " + s);

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        static bool Flag(string name) => Environment.GetCommandLineArgs().Contains(name);

        public static void Run()
        {
            try
            {
                // -costCells <n>: the most cells a part's lattice may take, to price finer lattices.
                if (float.TryParse(Arg("-costCells"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var most))
                    YapsJelly.MostCellsOverride = most;
                var scene = EditorSceneManager.OpenScene(Arg("-costScene"), OpenSceneMode.Single);
                // Other avatars and props wearing the tissue share its step materials in the editor.
                foreach (var other in Object.FindObjectsOfType<Animator>()) other.gameObject.SetActive(false);
                foreach (var other in Object.FindObjectsOfType<CVRRenderController>(true))
                    if (other != null && other.GetComponentInParent<CVRAvatar>(true) == null) other.gameObject.SetActive(false);
                var made = (GameObject) PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Arg("-costPrefab")), scene);
                PrefabUtility.UnpackPrefabInstance(made, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                made.name = Name;
                var root = made.GetComponentInChildren<CVRAvatar>(true).transform;
                if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Folder.Substring("Assets/".Length));
                OwnControllers(made.GetComponentInChildren<CVRAvatar>(true));
                if (Flag("-costPaint"))
                    typeof(JellySpikeBuilder).GetMethod("PaintTest", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { root });
                var clock = System.Diagnostics.Stopwatch.StartNew();
                string said = YapsSquash.Add(root, Folder, out bool built);
                // Logs the patcher's compile time and counts as "YAPS shader timing".
                YapsShaderPatcher.EndPass("cost probe, the squash", null);
                Log($"squash built {built} in {clock.ElapsedMilliseconds} ms: {said}");
                if (!built)
                {
                    EditorApplication.Exit(1);
                    return;
                }
                if (!Flag("-costSquashOnly"))
                {
                    clock.Restart();
                    var (on, refused) = YapsJelly.On(root, Folder + "/Jelly", null);
                    YapsShaderPatcher.EndPass("cost probe, the tissue", null);
                    Log($"tissue on {on.Count} in {clock.ElapsedMilliseconds} ms: {string.Join("; ", on)}" + (refused.Count > 0 ? $"; refused {string.Join("; ", refused)}" : ""));
                    Knobs(root);
                    // Built again on the same avatar, as a user's rebuild is: its assets rewritten in place.
                    if (Flag("-costTwice"))
                    {
                        clock.Restart();
                        (on, refused) = YapsJelly.On(root, Folder + "/Jelly", null);
                        YapsShaderPatcher.EndPass("cost probe, the tissue again", null);
                        Log($"tissue again on {on.Count} in {clock.ElapsedMilliseconds} ms" + (refused.Count > 0 ? $"; refused {string.Join("; ", refused)}" : ""));
                    }
                }
                Log($"{YapsSquash.Materials(root).Count} squash materials, {Chains(root).Count} chains");
                // Every texture it carries left Read/Write enabled, which the CCK's upload check refuses.
                var readable = EditorUtility.CollectDependencies(new Object[] { root.gameObject }).OfType<Texture2D>().Where(t => t.isReadable).Select(t => AssetDatabase.GetAssetPath(t) is string at && at.Length > 0 ? at : t.name).Distinct().ToList();
                Log($"readable textures: {readable.Count}{(readable.Count > 0 ? " (" + string.Join(", ", readable.Take(8)) + ")" : "")}");
                string save = Arg("-costSave");
                if (save != null)
                {
                    PrefabUtility.SaveAsPrefabAsset(made, save, out bool saved);
                    Log($"saved {save}: {saved}");
                }
                if (Flag("-costNoPlay") || Chains(root).Count == 0)
                {
                    AssetDatabase.SaveAssets();
                    EditorApplication.Exit(0);
                    return;
                }
                SessionState.SetBool(On, true);
                EditorApplication.update += Tick;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        // Its own copy of every controller the avatar plays: the tissue declares its parameters in
        // each, and the unpacked copy still played the source avatar's own files (one took twelve
        // parameters, 2026-11-07).
        static void OwnControllers(CVRAvatar avatar)
        {
            var swaps = new Dictionary<RuntimeAnimatorController, RuntimeAnimatorController>();
            T Own<T>(T held) where T : RuntimeAnimatorController
            {
                if (held == null) return null;
                if (swaps.TryGetValue(held, out var had)) return (T) had;
                string from = AssetDatabase.GetAssetPath(held);
                if (string.IsNullOrEmpty(from) || held is UnityEditor.Animations.AnimatorController shared && CvrSetup.SharedController(shared)) return held;
                string to = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{System.IO.Path.GetFileName(from)}");
                if (!AssetDatabase.CopyAsset(from, to)) return held;
                var copy = AssetDatabase.LoadAssetAtPath<T>(to);
                swaps[held] = copy;
                if (copy is AnimatorOverrideController o && held is AnimatorOverrideController h) o.runtimeAnimatorController = Own(h.runtimeAnimatorController);
                return copy;
            }
            avatar.overrides = Own(avatar.overrides);
            var s = avatar.avatarSettings;
            if (s != null)
            {
                s.baseController = Own(s.baseController);
                s.baseOverrideController = Own(s.baseOverrideController);
                s.animator = Own(s.animator);
                s.overrides = Own(s.overrides);
            }
            var animator = avatar.GetComponent<Animator>();
            if (animator != null) animator.runtimeAnimatorController = Own(animator.runtimeAnimatorController);
            AssetDatabase.SaveAssets();
            Log($"{swaps.Count} controller(s) copied into {Folder}, the source's left alone: {string.Join(", ", swaps.Keys.Select(k => k.name))}");
        }

        // The stretch in motion and other players' hands, live: both off through the component's
        // retune, then back, each part's step reading them.
        static void Knobs(Transform root)
        {
            var s = YapsSquash.SettingsOf(root);
            var worn = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(YapsJelly.Worn + " ")).ToList();
            if (s == null || worn.Count == 0)
            {
                Log("knobs FAIL: no settings or no worn part");
                return;
            }
            string Read() => string.Join("; ", worn.Select(w =>
            {
                var m = w.GetComponentsInChildren<CVRMaterialDataProvider>(true).SelectMany(p => p.materials ?? new Material[0]).First(q => q != null && q.HasProperty("_MassHz"));
                return $"{w.name.Substring(YapsJelly.Worn.Length + 1)} motion {m.GetVector("_Motion")} arrays {m.GetVector("_JellyArrays").x}";
            }));
            string was = Read();
            bool stretch = s.stretchInMotion;
            float hands = s.othersHands;
            s.stretchInMotion = false;
            s.othersHands = 0f;
            foreach (var w in worn) YapsJelly.Retune(w, s);
            string off = Read();
            s.stretchInMotion = stretch;
            s.othersHands = hands;
            foreach (var w in worn) YapsJelly.Retune(w, s);
            string back = Read();
            bool ok = back == was && !off.Contains("arrays 1") && off.Split(';').All(q => q.Contains("motion (0.00,"));
            Log($"knobs {(ok ? "ok" : "FAIL")}: as built {was}; both off {off}; back {back}");
        }

        // Each worn part's chain, in the order its render controller runs them.
        static List<(string part, Transform at, List<CVRBlitter> passes)> Chains(Transform root) =>
            root.GetComponentsInChildren<CVRRenderController>(true)
                .Select(c => (c.transform.name, c.transform, c.cvrRenderComponents.OfType<CVRBlitter>().ToList()))
                .Where(c => c.Item3.Count > 0).ToList();

        static void Tick()
        {
            if (!Application.isPlaying) return;
            if (++frame < Settle) return;
            EditorApplication.update -= Tick;
            SessionState.SetBool(On, false);
            try
            {
                var root = GameObject.Find(Name).GetComponentInChildren<CVRAvatar>(true).transform;
                var chains = Chains(root);
                Log($"on {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), {chains.Count} parts after {frame} frames");
                void Run(IEnumerable<(string part, Transform at, List<CVRBlitter> passes)> some)
                {
                    foreach (var c in some)
                        foreach (var b in c.passes) Graphics.Blit(b.originTexture, b.destinationTexture, b.blitMaterial);
                }
                string Time(IEnumerable<(string part, Transform at, List<CVRBlitter> passes)> some, Vector3? hands, bool near = false, float dt = 0f)
                {
                    var list = some.ToList();
                    foreach (var c in list) foreach (var b in c.passes) b.blitMaterial.SetFloat("_FixedDt", dt);
                    var times = new List<double>();
                    for (int round = 0; round < 5; round++)
                    {
                        Players(hands, near);
                        times.Add(Timed(() => Run(list), list[0].passes[0].destinationTexture, 600));
                    }
                    times.Sort();
                    return $"{times[2]:0.000} ({times[0]:0.000} to {times[4]:0.000})";
                }
                // Far: the viewer 50 m off, the path a step takes only in the parser's few frames of lag.
                // Near: the viewer's head at the origin, the steps run; eight hands keep a worn part awake.
                foreach (var c in chains)
                    Log($"{c.part}: {c.passes.Count} passes; far {Time(new[] { c }, null)} ms a frame; near at rest {Time(new[] { c }, null, true)}; "
                        + $"near, eight hands, 90 fps {Time(new[] { c }, c.at.position, true, 1f / 90)}; 60 fps {Time(new[] { c }, c.at.position, true, 1f / 60)}");
                for (int k = 1; k <= chains.Count; k++)
                    Log($"the first {k} together: far {Time(chains.Take(k), null)} ms a frame; near, eight hands, 90 fps "
                        + $"{Time(chains.Take(k), chains[0].at.position, true, 1f / 90)}; 60 fps {Time(chains.Take(k), chains[0].at.position, true, 1f / 60)}");
                Log("PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        // The client's player globals: 64 players far off, and with hands given, eight of them
        // within 3 cm of that point, as the bench sets them.
        static void Players(Vector3? hands, bool near = false)
        {
            var far = new Vector4(50f, 50f, 50f, 0f);
            var left = Enumerable.Repeat(far, 255).ToArray();
            var right = (Vector4[]) left.Clone();
            var chest = (Vector4[]) left.Clone();
            var heads = (Vector4[]) left.Clone();
            var root = Enumerable.Repeat(new Vector4(50f, 0f, 50f, 1.6f), 255).ToArray();
            if (hands is Vector3 h)
                for (int e = 0; e < 8; e++)
                {
                    var at = new Vector4(h.x - 0.03f + 0.008f * e, h.y, h.z, 0f);
                    if ((e & 1) == 0) left[1 + (e >> 1)] = at;
                    else right[1 + (e >> 1)] = at;
                    chest[1 + (e >> 1)] = new Vector4(h.x, h.y + 0.3f, h.z + 0.5f, 0f);
                    root[1 + (e >> 1)] = new Vector4(h.x, 0f, h.z + 0.5f, 1.6f);
                }
            Shader.SetGlobalVectorArray("_CVR_PlayerLeftHandPositions", left);
            Shader.SetGlobalVectorArray("_CVR_PlayerRightHandPositions", right);
            Shader.SetGlobalVectorArray("_CVR_PlayerChestPositions", chest);
            Shader.SetGlobalVectorArray("_CVR_PlayerRootPositions", root);
            if (near) heads[0] = Vector4.zero;
            Shader.SetGlobalVectorArray("_CVR_PlayerHeadPositions", heads);
            Shader.SetGlobalVector("CVRGlobalParams1", new Vector4(0f, 64, 0f, 0f));
        }

        static double Timed(Action run, RenderTexture rt, int runs)
        {
            var warm = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 30 || warm.ElapsedMilliseconds < 330; i++) run();
            AsyncGPUReadback.Request(rt).WaitForCompletion();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < runs; i++) run();
            AsyncGPUReadback.Request(rt).WaitForCompletion();
            return clock.Elapsed.TotalMilliseconds / runs;
        }
    }
}
#endif
