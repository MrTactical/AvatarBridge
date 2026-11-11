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
    //       [-costPaint] [-costSquashOnly] [-costSave <Assets/x.prefab>] [-costNoPlay] [-costCount]
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
                    if (Flag("-costCount"))
                    {
                        Cells(root);
                        EditorApplication.Exit(0);
                        return;
                    }
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

        // -costCount: how many of each region's particles matter, for pricing a sparse lattice. A: a
        // corner of a cell some moved vertex is drawn from, mapped as yaps_jelly.cginc's
        // YapsJellyRegion does; B: A and its 3x3x3 neighbours, the fit's reach; C: B and all it
        // encloses, flood filled from the box's faces.
        static void Cells(Transform root)
        {
            var found = YapsSquash.PartsOf(root, out string none);
            if (found == null)
            {
                Log("count: no parts, " + none);
                return;
            }
            var body = found.body;
            var baked = new Mesh();
            body.BakeMesh(baked);
            var world = baked.vertices.Select(v => body.transform.position + body.transform.rotation * v).ToArray();
            Object.DestroyImmediate(baked);
            for (int pair = 0; pair < found.parts.Count; pair++)
            {
                var (name, sides) = found.parts[pair];
                var worn = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == YapsJelly.WornName(name));
                var mat = worn?.GetComponentsInChildren<CVRMaterialDataProvider>(true).SelectMany(p => p.materials).FirstOrDefault(m => m != null && m.HasProperty("_Lattice"));
                if (mat == null)
                {
                    Log($"count {name}: no tissue");
                    continue;
                }
                var lat = mat.GetVector("_Lattice");
                var n = new Vector3Int((int) lat.x, (int) lat.y, (int) lat.z);
                float spacing = lat.w;
                int total = n.x * n.y * n.z;
                int Index(int x, int y, int z) => (z * n.y + y) * n.x + x;
                for (int side = 0; side < sides; side++)
                {
                    var region = worn.Find($"Jelly Region {side}");
                    var share = found.shares[body][2 * pair + side];
                    var a = new bool[total];
                    int moved = 0, outside = 0;
                    for (int v = 0; v < Mathf.Min(world.Length, share.Length); v++)
                    {
                        if (!(share[v] > 0f)) continue;
                        moved++;
                        // Region scale cancels as the draw's per does: InverseTransformPoint is rel.ax / |ax|^2.
                        var l = region.InverseTransformPoint(world[v]) / spacing + new Vector3(0.5f * (n.x - 1), 0.5f * (n.y - 1), 0f);
                        if (l.x < 0 || l.y < 0 || l.z < 0 || l.x > n.x - 1 || l.y > n.y - 1 || l.z > n.z - 1) outside++;
                        int cx = Mathf.Min((int) Mathf.Clamp(l.x, 0, n.x - 1), n.x - 2);
                        int cy = Mathf.Min((int) Mathf.Clamp(l.y, 0, n.y - 1), n.y - 2);
                        int cz = Mathf.Min((int) Mathf.Clamp(l.z, 0, n.z - 1), n.z - 2);
                        for (int k = 0; k < 8; k++) a[Index(cx + (k & 1), cy + ((k >> 1) & 1), cz + (k >> 2))] = true;
                    }
                    var b = new bool[total];
                    for (int z = 0; z < n.z; z++)
                        for (int y = 0; y < n.y; y++)
                            for (int x = 0; x < n.x; x++)
                                if (a[Index(x, y, z)])
                                    for (int dz = -1; dz <= 1; dz++)
                                        for (int dy = -1; dy <= 1; dy++)
                                            for (int dx = -1; dx <= 1; dx++)
                                            {
                                                int X = x + dx, Y = y + dy, Z = z + dz;
                                                if (X >= 0 && Y >= 0 && Z >= 0 && X < n.x && Y < n.y && Z < n.z) b[Index(X, Y, Z)] = true;
                                            }
                    // Outside: reached from the box's faces through non-B particles, six ways.
                    var outer = new bool[total];
                    var queue = new Queue<Vector3Int>();
                    for (int z = 0; z < n.z; z++)
                        for (int y = 0; y < n.y; y++)
                            for (int x = 0; x < n.x; x++)
                                if ((x == 0 || y == 0 || z == 0 || x == n.x - 1 || y == n.y - 1 || z == n.z - 1) && !b[Index(x, y, z)])
                                {
                                    outer[Index(x, y, z)] = true;
                                    queue.Enqueue(new Vector3Int(x, y, z));
                                }
                    var steps = new[] { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };
                    while (queue.Count > 0)
                    {
                        var p = queue.Dequeue();
                        foreach (var s in steps)
                        {
                            var q = p + s;
                            if (q.x < 0 || q.y < 0 || q.z < 0 || q.x >= n.x || q.y >= n.y || q.z >= n.z) continue;
                            int i = Index(q.x, q.y, q.z);
                            if (outer[i] || b[i]) continue;
                            outer[i] = true;
                            queue.Enqueue(q);
                        }
                    }
                    // Pinned as the step's Pinned(l): the back face, or within a core's capsule.
                    var ca = mat.GetVector(side == 0 ? "_JellyCoreA0" : "_JellyCoreA1");
                    var cb = mat.GetVector(side == 0 ? "_JellyCoreB0" : "_JellyCoreB1");
                    int pinned = 0;
                    for (int z = 0; z < n.z; z++)
                        for (int y = 0; y < n.y; y++)
                            for (int x = 0; x < n.x; x++)
                            {
                                if (!(ca.w > 0)) { if (z == 0) pinned++; continue; }
                                var r = new Vector3(x - 0.5f * (n.x - 1), y - 0.5f * (n.y - 1), z) * spacing;
                                Vector3 pa = ca, seg = (Vector3) cb - pa;
                                var near = pa + seg * Mathf.Clamp01(Vector3.Dot(r - pa, seg) / Mathf.Max(seg.sqrMagnitude, 1e-12f));
                                if (Vector3.Distance(r, near) < ca.w) pinned++;
                            }
                    int A = a.Count(t => t), B = b.Count(t => t), C = outer.Count(t => !t);
                    string Pc(int c) => $"{c} ({100f * c / total:0.0}%)";
                    Log($"count {name} region {side}: lattice {n.x}x{n.y}x{n.z} at {100f * spacing:0.00} cm, total {total}; {moved} moved vertices, {outside} outside the box; A {Pc(A)}, B {Pc(B)}, C {Pc(C)}, pinned {Pc(pinned)}{(ca.w > 0 ? " (cored)" : " (back face)")}");
                }
            }
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
            if (Arg("-costParity") is string oldStep && !Parity(oldStep)) return;
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
                    // -costSolid0: the bulk's squash map off, to price it.
                    if (Flag("-costSolid0")) foreach (var c in list) foreach (var b in c.passes) b.blitMaterial.SetFloat("_Solid", 0f);
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
                // With -costParity, the old step and the new timed in turns in this one run, so a busy
                // machine weighs on both alike.
                if (olds.Count > 0)
                {
                    var all = chains.ToList();
                    foreach (var dt in new[] { 1f / 90, 1f / 60 })
                    {
                        var a = new List<double>();
                        var b = new List<double>();
                        foreach (var c in all) foreach (var x in c.passes) { x.blitMaterial.SetFloat("_FixedDt", dt); olds[x.blitMaterial].CopyPropertiesFromMaterial(x.blitMaterial); }
                        for (int round = 0; round < 7; round++)
                        {
                            Players(all[0].at.position, true);
                            a.Add(Timed(() => { foreach (var c in all) foreach (var x in c.passes) Graphics.Blit(x.originTexture, x.destinationTexture, olds[x.blitMaterial]); }, all[0].passes[0].destinationTexture, 600));
                            b.Add(Timed(() => Run(all), all[0].passes[0].destinationTexture, 600));
                        }
                        a.Sort();
                        b.Sort();
                        Log($"A/B all parts, near, eight hands, {Mathf.RoundToInt(1 / dt)} fps: old {a[3]:0.000} ({a[0]:0.000} to {a[6]:0.000}) ms, new {b[3]:0.000} ({b[0]:0.000} to {b[6]:0.000})");
                    }
                }
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

        // -costParity <file>: the step shader as it was (its source, unescaped) against the one built
        // now, each frame for ParityFrames: both chains run on copies of the live state with the same
        // globals, hands moving in reach, at 90 and 60 fps and far, and every texel of every texture
        // they write must match to the bit. True once done; it exits on a mismatch.
        const int ParityFrames = 400;
        static readonly Dictionary<Material, Material> olds = new Dictionary<Material, Material>();
        static int matched, texels, rounded;
        static double worst;

        static bool Parity(string oldStep)
        {
            var root = GameObject.Find(Name).GetComponentInChildren<CVRAvatar>(true).transform;
            var chains = Chains(root);
            if (olds.Count == 0)
            {
                var any = chains[0].passes[0].blitMaterial;
                string at = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(any.shader)).Replace('\\', '/') + "/JellyStepOld.shader";
                System.IO.File.WriteAllText(at, System.IO.File.ReadAllText(oldStep).Replace("Shader \"Hidden/AvatarBridge/JellyStep\"", "Shader \"Hidden/AvatarBridge/JellyStepOld\""));
                AssetDatabase.ImportAsset(at, ImportAssetOptions.ForceSynchronousImport);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(at);
                if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("the old step shader did not compile");
                foreach (var c in chains)
                    foreach (var b in c.passes)
                        if (!olds.ContainsKey(b.blitMaterial))
                        {
                            var m = new Material(shader);
                            m.CopyPropertiesFromMaterial(b.blitMaterial);
                            foreach (string k in b.blitMaterial.shaderKeywords) m.EnableKeyword(k);
                            olds[b.blitMaterial] = m;
                        }
                Log($"parity: {olds.Count} step materials against {shader.name}; " + string.Join("; ", chains.Select(c => c.part + ": " + string.Join(", ", c.passes.Select(b => $"{b.originTexture.name} -> {b.destinationTexture.name}")))));
            }
            int f = frame - Settle, mode = f % 4;
            float dt = mode == 1 ? 1f / 60 : 1f / 90;
            foreach (var c in chains)
            {
                Players(mode == 2 ? (Vector3?) null : c.at.position + new Vector3(0.04f * Mathf.Sin(f * 0.21f), 0.03f * Mathf.Cos(f * 0.13f), 0.05f * Mathf.Sin(f * 0.17f)), mode != 3);
                var textures = c.passes.SelectMany(b => new[] { b.originTexture, b.destinationTexture }).OfType<RenderTexture>().Distinct().ToList();
                RenderTexture Clone(RenderTexture t) { var x = new RenderTexture(t.descriptor); x.Create(); Graphics.CopyTexture(t, x); return x; }
                var was = textures.ToDictionary(t => t, Clone);
                var now = textures.ToDictionary(t => t, Clone);
                foreach (var b in c.passes)
                {
                    // The anchors are the materials' own, set each frame, so copied each frame.
                    b.blitMaterial.SetFloat("_FixedDt", dt);
                    olds[b.blitMaterial].CopyPropertiesFromMaterial(b.blitMaterial);
                    Graphics.Blit(was[(RenderTexture) b.originTexture], was[(RenderTexture) b.destinationTexture], olds[b.blitMaterial]);
                    Graphics.Blit(now[(RenderTexture) b.originTexture], now[(RenderTexture) b.destinationTexture], b.blitMaterial);
                }
                foreach (var t in textures)
                {
                    float[] Read(RenderTexture r) { var q = AsyncGPUReadback.Request(r); q.WaitForCompletion(); return q.GetData<float>().ToArray(); }
                    float[] x = Read(was[t]), y = Read(now[t]);
                    // A side row's 87 to 92 are new: whether a probe is pushed, the anchor and its turn handed to the second pass, and whether a slot might grip; copies before.
                    int block = t.height / Mathf.Max(1, Mathf.RoundToInt(c.passes[0].blitMaterial.GetFloat("_Regions")));
                    for (int i = 0; i < x.Length; i++)
                        if (BitConverter.SingleToInt32Bits(x[i]) != BitConverter.SingleToInt32Bits(y[i]))
                        {
                            int texel = i / 4;
                            if (t.width > 128 && texel % t.width >= 87 && texel % t.width <= 92 && (texel / t.width + 1) % block == 0) continue;
                            // Going far, the second pass discards the first's particles: what the first wrote
                            // there is never read, a copy before and a step since.
                            int sideRow = texel / t.width / block * block + block - 1;
                            if (t == c.passes[0].destinationTexture && c.passes.Count > 1 && texel / t.width != sideRow && y[4 * (sideRow * t.width + 18)] > 0.5f) continue;
                            // A predicted right half the first pass hands on raw where the left marks it put
                            // back at rest, or over a snap: the second makes it, and is checked in its own.
                            int col = texel % t.width;
                            if (t == c.passes[0].destinationTexture && c.passes.Count > 1 && col >= 64 && col < 128 && texel / t.width != sideRow
                                && (y[4 * (texel - 64) + 3] < 0.5f || y[4 * (sideRow * t.width + 85) + 3] < 0f)) continue;
                            // A driver may order the same sums otherwise in an edited variant: a few bits
                            // are counted, not failed. Past 1e-5 of the value it is a different answer.
                            double off = Math.Abs((double) x[i] - y[i]), of = Math.Max(Math.Abs((double) x[i]), 1e-3);
                            if (off <= 1e-5 * of) { rounded++; worst = Math.Max(worst, off / of); continue; }
                            Log($"parity FAIL {c.part} frame {f} mode {mode} {t.name} texel ({texel % t.width}, {texel / t.width}).{"xyzw"[i % 4]}: was {x[i]:R} now {y[i]:R}");
                            foreach (var u in textures)
                            {
                                float[] ux = Read(was[u]), uy = Read(now[u]);
                                string T(float[] a, int tx, int ty) { int k = 4 * (ty * u.width + tx); return $"({a[k]:F4} {a[k + 1]:F4} {a[k + 2]:F4} {a[k + 3]:F4})"; }
                                for (int y0 = block - 1; y0 < u.height && u.width > 128; y0 += block)
                                    Log($"  {u.name} side row {y0}: was 18 {T(ux, 18, y0)} 25 {T(ux, 25, y0)} 26..29 {T(ux, 26, y0)} {T(ux, 27, y0)} {T(ux, 28, y0)} {T(ux, 29, y0)}; "
                                        + $"now 18 {T(uy, 18, y0)} 25 {T(uy, 25, y0)} 26..29 {T(uy, 26, y0)} {T(uy, 27, y0)} {T(uy, 28, y0)} {T(uy, 29, y0)} 88..90 {T(uy, 88, y0)} {T(uy, 89, y0)} {T(uy, 90, y0)}");
                            }
                            EditorApplication.Exit(1);
                            return false;
                        }
                    texels += x.Length / 4;
                }
                foreach (var t in was.Values.Concat(now.Values)) { t.Release(); Object.DestroyImmediate(t); }
                foreach (var b in c.passes) b.blitMaterial.SetFloat("_FixedDt", 0f);
            }
            if (++matched < ParityFrames) return false;
            Log($"parity PASS: {matched} frames, {texels} texels, all to the bit but {rounded} values off by at most {worst:E1} of themselves");
            return true;
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
