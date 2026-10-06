// Original PhysBones against their MagicaCloth2 conversion, chain by chain, driven by one
// scripted motion in Play mode: how far each tip hangs at rest, how far it swings, how long
// it takes to settle, how far it overshoots, how much of a swing reaches the middle of the
// chain, and where it hangs once the avatar lies down.
//
// Each scene is converted with a new user's defaults, the source switched back on beside its
// copy, every Animator off so only the script moves them, and the copy set aside so the two
// never touch. One timeline drives both roots (walk, turn, jump, shake, lie down) and both
// Head bones (a head turn the root never sees). Time is pinned at 90 fps with
// captureFramerate, so a run repeats and the machine's speed does not matter. Positions are
// read after both solvers wrote them, from a step appended to the end of the frame; the drive
// runs first thing in Update, after MagicaCloth2 has restored its bones.
//
// Survey: -executeMethod AvatarBridge.Regression.PhysicsAbProbe.Survey -abOut <dir>
//   every avatar scene in the project, its active chains counted by class.
// A/B:    -executeMethod AvatarBridge.Regression.PhysicsAbProbe.Run -abScenes "a.unity;b.unity" -abOut <dir>
//   no -quit: it walks the list, in and out of Play mode, and exits itself.
#if CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA && VRC_SDK_VRCSDK3
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MagicaCloth2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge.Regression
{
    [InitializeOnLoad]
    public static class PhysicsAbProbe
    {
        const string K = "PhysicsAbProbe.";
        const float Fps = 90f;
        const float End = 27f;

        // Motion start, motion end, end of the window that watches it settle.
        static readonly (string name, float start, float stop, float end)[] Phases =
        {
            ("walk", 2f, 4f, 6.5f),
            ("turn", 6.5f, 7f, 9.5f),
            ("head", 9.5f, 9.9f, 12f),
            ("jump", 12f, 12.6f, 15f),
            ("shake", 15f, 16f, 18.5f),
            ("lie", 18.5f, 19.2f, 27f),
        };

        sealed class Side
        {
            public Transform root, tip, mid;
            public Vector3 endpoint, bind, bindMid;
            public readonly List<Vector3> v = new List<Vector3>(), m = new List<Vector3>();
        }

        sealed class Chain
        {
            public string name, cls, pars;
            public readonly Side src = new Side(), dst = new Side();
        }

        sealed class Body
        {
            public Transform avatar, head;
            public Vector3 pos0;
            public Quaternion rot0, headRel;
        }

        static List<Chain> chains;
        static Body srcBody, dstBody;
        static int startFrame = -1, playFrames;
        static float walked;
        static bool hooked, installed;
        static double waitingSince;

        struct Drive { }
        struct Read { }

        static PhysicsAbProbe()
        {
            if (SessionState.GetInt(K + "stage", -1) >= 0) Hook();
        }

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            EditorApplication.update += Tick;
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static string F(float x) => float.IsNaN(x) ? "never" : x.ToString("0.###", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ survey

        public static void Survey()
        {
            string outDir = Arg("-abOut") ?? Path.Combine(Path.GetTempPath(), "physics-ab");
            Directory.CreateDirectory(outDir);
            var sb = new StringBuilder("scene\tavatar\tchains\tfury\tma\tclasses\n");
            foreach (var path in AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath)
                         .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && !p.Contains("AvatarBridgeOutput"))
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                try
                {
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    var descriptor = scene.GetRootGameObjects().Select(r => r.GetComponentInChildren<VRCAvatarDescriptor>(true))
                        .FirstOrDefault(d => d != null);
                    if (descriptor == null) continue;
                    var animator = descriptor.GetComponent<Animator>();
                    var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
                    int total = 0;
                    foreach (var pb in descriptor.GetComponentsInChildren<VRCPhysBone>(true))
                    {
                        if (!pb.gameObject.activeInHierarchy || !pb.enabled) continue;
                        var data = PhysBoneChainData.Read(pb, animator, true);
                        if (data.Root == null) continue;
                        string cls = MagicaPresetLibrary.Classify(data).Name;
                        counts[cls] = counts.TryGetValue(cls, out int n) ? n + 1 : 1;
                        total++;
                    }
                    var types = descriptor.GetComponentsInChildren<Component>(true).Where(c => c != null).Select(c => c.GetType().FullName).ToList();
                    bool fury = types.Any(t => t.Contains("VRCFury"));
                    bool ma = types.Any(t => t.Contains("ModularAvatar"));
                    sb.Append(path).Append('\t').Append(descriptor.name).Append('\t').Append(total).Append('\t')
                      .Append(fury ? 1 : 0).Append('\t').Append(ma ? 1 : 0).Append('\t')
                      .Append(string.Join(" ", counts.Select(kv => $"{kv.Key}:{kv.Value}"))).Append('\n');
                }
                catch (Exception e)
                {
                    sb.Append(path).Append("\terror\t").Append(e.GetType().Name).Append('\n');
                }
            }
            File.WriteAllText(Path.Combine(outDir, "survey.tsv"), sb.ToString());
            Debug.Log("[PhysicsAb] survey written");
            EditorApplication.Exit(0);
        }

        // Each chain's root and PhysBone version, for the scenes in -abScenes, without Play mode.
        public static void Versions()
        {
            string outDir = Arg("-abOut") ?? Path.Combine(Path.GetTempPath(), "physics-ab");
            Directory.CreateDirectory(outDir);
            var sb = new StringBuilder();
            foreach (var path in (Arg("-abScenes") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var scene = EditorSceneManager.OpenScene(path.Trim(), OpenSceneMode.Single);
                var descriptor = scene.GetRootGameObjects().Select(r => r.GetComponentInChildren<VRCAvatarDescriptor>(true)).FirstOrDefault(d => d != null);
                if (descriptor == null) continue;
                foreach (var pb in descriptor.GetComponentsInChildren<VRCPhysBone>(true))
                {
                    var root = pb.rootTransform != null ? pb.rootTransform : pb.transform;
                    sb.Append(descriptor.name).Append("\t").Append(Rel(root, descriptor.transform)).Append("\t").Append(pb.version).Append("\n");
                }
            }
            File.WriteAllText(Path.Combine(outDir, "versions.tsv"), sb.ToString());
            EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------ edit mode

        public static void Run()
        {
            SessionState.SetString(K + "scenes", Arg("-abScenes") ?? "");
            SessionState.SetString(K + "out", Arg("-abOut") ?? Path.Combine(Path.GetTempPath(), "physics-ab"));
            SessionState.SetInt(K + "index", 0);
            SessionState.SetInt(K + "stage", 0);
            SessionState.SetString(K + "gravityScale", Arg("-abGravityScale") ?? "");
            Directory.CreateDirectory(SessionState.GetString(K + "out", ""));
            Hook();
            Next();
        }

        static void Next()
        {
            var scenes = SessionState.GetString(K + "scenes", "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            int index = SessionState.GetInt(K + "index", 0);
            if (index >= scenes.Length)
            {
                Debug.Log("[PhysicsAb] done");
                SessionState.EraseInt(K + "stage");
                EditorApplication.Exit(0);
                return;
            }
            try
            {
                if (Prepare(scenes[index].Trim()))
                {
                    SessionState.SetInt(K + "stage", 1);
                    waitingSince = EditorApplication.timeSinceStartup;
                    EditorApplication.EnterPlaymode();
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            SessionState.SetInt(K + "index", index + 1);
            EditorApplication.delayCall += Next;
        }

        static bool Prepare(string scenePath)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var descriptor = scene.GetRootGameObjects().Select(r => r.GetComponentInChildren<VRCAvatarDescriptor>(true))
                .FirstOrDefault(d => d != null);
            if (descriptor == null)
            {
                Debug.Log($"[PhysicsAb] {scenePath}: no avatar");
                return false;
            }
            for (var t = descriptor.transform; t != null; t = t.parent) t.gameObject.SetActive(true);

            // A new user's defaults; only what a repeatable run needs is pinned.
            var settings = new BridgeSettings
            {
                cloneAvatar = true,
                outputFolder = "Assets/AvatarBridgeOutput/PhysicsAb",
                slimTexturesOnConvert = false,
                physicsTarget = PhysicsTarget.MagicaCloth2,
            };
            var report = BridgeConverter.Convert(descriptor, settings);
            var converted = report.ConvertedRoot;
            if (converted == null)
            {
                Debug.Log($"[PhysicsAb] {scenePath}: conversion made nothing");
                return false;
            }
            // Converting a copy switches the original off.
            for (var t = descriptor.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
            converted.SetActive(true);
            converted.transform.position += Vector3.right * 3f;
            // A calibration sweep scales every cloth's gravity here, on the copy only.
            if (float.TryParse(SessionState.GetString(K + "gravityScale", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float gravityScale))
            {
                foreach (var cloth in converted.GetComponentsInChildren<MagicaCloth>(true)) cloth.SerializeData.gravity *= gravityScale;
                Debug.Log($"[PhysicsAb] gravity scaled by {gravityScale}");
            }

            // Only the script moves either avatar, and no emulator takes one over in Play.
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var c in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (c == null) continue;
                    string n = c.GetType().FullName ?? "";
                    if (n.Contains("Av3Emulator") || n.Contains("GestureManager") || n.Contains("LyumaAv3")) UnityEngine.Object.DestroyImmediate(c.gameObject);
                }
            }
            foreach (var a in descriptor.GetComponentsInChildren<Animator>(true).Concat(converted.GetComponentsInChildren<Animator>(true)))
            {
                // MagicaCloth2's default culling follows the Animator's, and nothing is on
                // screen in a batch run, so anything but AlwaysAnimate culls every cloth.
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                a.enabled = false;
            }

            var animator = descriptor.GetComponent<Animator>();
            var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            var dstHead = head != null ? Find(converted.transform, Rel(head, descriptor.transform)) : null;
            var clothRoots = converted.GetComponentsInChildren<MagicaCloth>(true)
                .SelectMany(c => c.SerializeData.rootBones.Where(r => r != null)).ToList();

            var lines = new List<string>();
            var seenRoots = new HashSet<Transform>();
            int skippedNoCloth = 0, skippedNoMatch = 0;
            foreach (var pb in descriptor.GetComponentsInChildren<VRCPhysBone>(true))
            {
                if (!pb.gameObject.activeInHierarchy || !pb.enabled) continue;
                var data = PhysBoneChainData.Read(pb, animator, true);
                var root = data.Root;
                if (root == null || !seenRoots.Add(root)) continue;

                // The deepest bone the chain simulates, and the joint halfway to it.
                Transform tip = root;
                int tipDepth = 0;
                void Walk(Transform t, int depth)
                {
                    if (data.Ignores.Contains(t)) return;
                    if (depth > tipDepth) { tip = t; tipDepth = depth; }
                    for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1);
                }
                for (int i = 0; i < root.childCount; i++) Walk(root.GetChild(i), 1);
                var mid = tip;
                for (int i = 0; i < tipDepth / 2 && mid != root; i++) mid = mid.parent;

                var dRoot = Find(converted.transform, Rel(root, descriptor.transform));
                var dTip = Find(converted.transform, Rel(tip, descriptor.transform));
                var dMid = Find(converted.transform, Rel(mid, descriptor.transform));
                if (dRoot == null || dTip == null || dMid == null || dRoot.parent == null)
                {
                    skippedNoMatch++;
                    continue;
                }
                if (!clothRoots.Any(r => r == dRoot || r.IsChildOf(dRoot) || dRoot.IsChildOf(r)))
                {
                    skippedNoCloth++;
                    continue;
                }

                var endpoint = tip == root ? data.EndpointPosition : Vector3.zero;
                string pars = string.Format(CultureInfo.InvariantCulture,
                    (data.IsVersion10 ? "v1.0 " : "v1.1 ") + "{0} pull={1:0.##} spring={2:0.##} stiff={3:0.##} grav={4:0.##}/{5:0.##} imm={6:0.##}({7}) limit={8}:{9:0}/{10:0} bones={11}",
                    data.IsAdvancedIntegration ? "adv" : "simple", data.Pull, data.Spring, data.Stiffness, data.Gravity,
                    data.GravityFalloff, data.Immobile, data.ImmobileTypeName, data.LimitTypeName, data.MaxAngleX, data.MaxAngleZ, tipDepth + 1);
                lines.Add(string.Join("|",
                    Rel(root, descriptor.transform).Replace("|", "_"),
                    MagicaPresetLibrary.Classify(data).Name,
                    pars,
                    IndexPath(root), IndexPath(tip), IndexPath(mid),
                    IndexPath(dRoot), IndexPath(dTip), IndexPath(dMid),
                    V3(endpoint)));
            }
            Debug.Log($"[PhysicsAb] {scenePath}: {lines.Count} chain(s) matched, {skippedNoCloth} without a cloth, {skippedNoMatch} not found in the copy");
            if (lines.Count == 0) return false;

            SessionState.SetString(K + "avatar", descriptor.name);
            SessionState.SetString(K + "bodies", string.Join("|", IndexPath(descriptor.transform), head != null ? IndexPath(head) : "",
                IndexPath(converted.transform), dstHead != null ? IndexPath(dstHead) : ""));
            SessionState.SetString(K + "chains", string.Join("\n", lines));
            return true;
        }

        static string Rel(Transform t, Transform root) => AnimationUtility.CalculateTransformPath(t, root);

        static Transform Find(Transform root, string rel)
        {
            if (string.IsNullOrEmpty(rel)) return root;
            var t = root;
            foreach (var part in rel.Split('/'))
            {
                Transform next = null;
                for (int i = 0; i < t.childCount && next == null; i++)
                {
                    if (t.GetChild(i).name == part) next = t.GetChild(i);
                }
                if (next == null) return null;
                t = next;
            }
            return t;
        }

        static string IndexPath(Transform t)
        {
            var parts = new List<int>();
            for (; t != null; t = t.parent) parts.Add(t.GetSiblingIndex());
            parts.Reverse();
            return string.Join("/", parts);
        }

        static Transform FromIndexPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var ix = path.Split('/').Select(int.Parse).ToArray();
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            var t = roots.FirstOrDefault(r => r.transform.GetSiblingIndex() == ix[0])?.transform;
            for (int i = 1; i < ix.Length && t != null; i++) t = ix[i] < t.childCount ? t.GetChild(ix[i]) : null;
            return t;
        }

        static string V3(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", v.x, v.y, v.z);

        static Vector3 ParseV3(string s)
        {
            var p = s.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            return new Vector3(p[0], p[1], p[2]);
        }

        // ------------------------------------------------------------------ play mode

        static void Tick()
        {
            int stage = SessionState.GetInt(K + "stage", -1);
            if (stage == 1 && !EditorApplication.isPlaying && EditorApplication.timeSinceStartup - waitingSince > 300 && waitingSince > 0)
            {
                Debug.LogError("[PhysicsAb] Play mode never started; skipping this scene");
                Advance();
                return;
            }
            if (stage == 1 && EditorApplication.isPlaying && !installed)
            {
                Load();
                Install(true);
                Time.captureFramerate = (int)Fps;
                installed = true;
                startFrame = -1;
                playFrames = 0;
                walked = 0;
                return;
            }
            if (stage == 2 && !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Advance();
            }
        }

        static void Advance()
        {
            installed = false;
            SessionState.SetInt(K + "index", SessionState.GetInt(K + "index", 0) + 1);
            SessionState.SetInt(K + "stage", 0);
            EditorApplication.delayCall += Next;
        }

        static void Load()
        {
            var b = SessionState.GetString(K + "bodies", "").Split('|');
            Body MakeBody(string avatar, string head)
            {
                var body = new Body { avatar = FromIndexPath(avatar), head = FromIndexPath(head) };
                body.pos0 = body.avatar.position;
                body.rot0 = body.avatar.rotation;
                if (body.head != null) body.headRel = Quaternion.Inverse(body.rot0) * body.head.rotation;
                return body;
            }
            srcBody = MakeBody(b[0], b[1]);
            dstBody = MakeBody(b[2], b[3]);
            chains = new List<Chain>();
            foreach (var line in SessionState.GetString(K + "chains", "").Split('\n'))
            {
                var f = line.Split('|');
                if (f.Length < 10) continue;
                var c = new Chain { name = f[0], cls = f[1], pars = f[2] };
                c.src.root = FromIndexPath(f[3]); c.src.tip = FromIndexPath(f[4]); c.src.mid = FromIndexPath(f[5]);
                c.dst.root = FromIndexPath(f[6]); c.dst.tip = FromIndexPath(f[7]); c.dst.mid = FromIndexPath(f[8]);
                c.src.endpoint = c.dst.endpoint = ParseV3(f[9]);
                if (c.src.root == null || c.dst.root == null || c.src.tip == null || c.dst.tip == null) continue;
                chains.Add(c);
            }
            foreach (var c in chains)
            {
                c.src.bind = Vec(c.src, false); c.src.bindMid = Vec(c.src, true);
                c.dst.bind = Vec(c.dst, false); c.dst.bindMid = Vec(c.dst, true);
            }
        }

        // The tip (or the midpoint) relative to the chain's root, in the space of the root's
        // parent, so the motion that carries the whole chain cancels out. A single-bone chain
        // has no child to watch; its virtual tip turns with the root.
        static Vector3 Vec(Side s, bool mid)
        {
            var parent = s.root.parent;
            if (s.tip == s.root)
            {
                return parent.InverseTransformVector(s.root.TransformVector(s.endpoint));
            }
            var target = mid ? s.mid : s.tip;
            return parent.InverseTransformPoint(target.position) - parent.InverseTransformPoint(s.root.position);
        }

        static void Install(bool on)
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                var sys = loop.subSystemList[i];
                var list = (sys.subSystemList ?? new PlayerLoopSystem[0])
                    .Where(s => s.type != typeof(Drive) && s.type != typeof(Read)).ToList();
                if (on && sys.type == typeof(UnityEngine.PlayerLoop.Update))
                    list.Insert(0, new PlayerLoopSystem { type = typeof(Drive), updateDelegate = DriveStep });
                if (on && sys.type == typeof(UnityEngine.PlayerLoop.PostLateUpdate))
                    list.Add(new PlayerLoopSystem { type = typeof(Read), updateDelegate = ReadStep });
                sys.subSystemList = list.ToArray();
                loop.subSystemList[i] = sys;
            }
            PlayerLoop.SetPlayerLoop(loop);
        }

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        static void DriveStep()
        {
            if (!EditorApplication.isPlaying || chains == null) return;
            if (startFrame < 0)
            {
                // A second for the cloths to build, longer only if they have not.
                playFrames++;
                var cloths = dstBody.avatar.GetComponentsInChildren<MagicaCloth>(true).Where(c => c.isActiveAndEnabled).ToArray();
                bool built = cloths.All(c => c.IsValid());
                if (playFrames < Fps || (!built && playFrames < 10 * Fps)) return;
                startFrame = Time.frameCount;
                Debug.Log($"[PhysicsAb] timeline starts, {cloths.Count(c => c.IsValid())}/{cloths.Length} cloth(s) built");
            }
            float t = (Time.frameCount - startFrame) / Fps;
            float v = t >= 2f && t <= 4f ? 1.4f * Mathf.Clamp01((t - 2f) / 0.25f) * Mathf.Clamp01((4f - t) / 0.25f) : 0f;
            walked += v / Fps;
            float yaw = 90f * Smooth((t - 6.5f) / 0.5f);
            float headYaw = 60f * Smooth((t - 9.5f) / 0.4f);
            float jump = t >= 12f && t <= 12.6f ? 0.35f * 4f * ((t - 12f) / 0.6f) * (1f - (t - 12f) / 0.6f) : 0f;
            float shake = t >= 15f && t <= 16f ? 0.06f * Mathf.Sin(2f * Mathf.PI * 3f * (t - 15f)) : 0f;
            float pitch = 90f * Smooth((t - 18.5f) / 0.7f);
            foreach (var body in new[] { srcBody, dstBody })
            {
                var turned = body.rot0 * Quaternion.AngleAxis(yaw, Vector3.up);
                body.avatar.rotation = turned * Quaternion.AngleAxis(pitch, Vector3.right);
                body.avatar.position = body.pos0 + body.rot0 * Vector3.forward * walked + Vector3.up * jump
                                       + turned * Vector3.right * shake;
                if (body.head != null)
                {
                    body.head.rotation = Quaternion.AngleAxis(headYaw, body.avatar.up) * body.avatar.rotation * body.headRel;
                }
            }
        }

        static void ReadStep()
        {
            if (!EditorApplication.isPlaying || chains == null || startFrame < 0) return;
            foreach (var c in chains)
            {
                c.src.v.Add(Vec(c.src, false)); c.src.m.Add(Vec(c.src, true));
                c.dst.v.Add(Vec(c.dst, false)); c.dst.m.Add(Vec(c.dst, true));
            }
            if ((Time.frameCount - startFrame) / Fps < End) return;
            try
            {
                Write();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            Install(false);
            Time.captureFramerate = 0;
            chains = null;
            SessionState.SetInt(K + "stage", 2);
            EditorApplication.ExitPlaymode();
        }

        // ------------------------------------------------------------------ metrics

        sealed class Metrics
        {
            public float sag, stretch, lie, liehold;
            public readonly Dictionary<string, (float peak, float settle, float overshoot, float midShare)> phase =
                new Dictionary<string, (float, float, float, float)>();
        }

        static Metrics Measure(Side s)
        {
            int n = s.v.Count;
            int Ix(float t) => Mathf.Clamp(Mathf.RoundToInt(t * Fps), 0, n - 1);
            Vector3 Mean(List<Vector3> xs, float a, float b)
            {
                var sum = Vector3.zero;
                int i0 = Ix(a), i1 = Ix(b);
                for (int i = i0; i < i1; i++) sum += xs[i];
                return sum / Mathf.Max(1, i1 - i0);
            }
            var rest = Mean(s.v, 1.5f, 2f);
            var restMid = Mean(s.m, 1.5f, 2f);
            var m = new Metrics
            {
                sag = Vector3.Angle(s.bind, rest),
                stretch = s.v.Max(x => x.magnitude) / Mathf.Max(1e-6f, rest.magnitude),
            };
            foreach (var (name, start, stop, end) in Phases)
            {
                int i0 = Ix(start), i1 = Ix(stop), i2 = Ix(end);
                float peak = 0, midPeak = 0;
                int at = i0;
                for (int i = i0; i < i2; i++)
                {
                    float d = Vector3.Angle(s.v[i], rest);
                    if (d > peak) { peak = d; at = i; }
                    midPeak = Mathf.Max(midPeak, Vector3.Angle(s.m[i], restMid));
                }
                // Settled once it stays within a tenth of the peak, or 1.5 degrees.
                float limit = Mathf.Max(1.5f, 0.1f * peak);
                int lastAbove = -1;
                for (int i = i2 - 1; i >= i1; i--)
                {
                    if (Vector3.Angle(s.v[i], rest) > limit) { lastAbove = i; break; }
                }
                float settle = lastAbove < 0 ? 0f : lastAbove >= i2 - 1 ? float.NaN : (lastAbove + 1 - i1) / Fps;
                // How far past rest it comes back, against the direction of the peak.
                var swing = s.v[at] - rest;
                float overshoot = 0;
                if (swing.sqrMagnitude > 1e-12f)
                {
                    var dir = swing.normalized;
                    for (int i = Mathf.Max(at, i1); i < i2; i++)
                    {
                        overshoot = Mathf.Max(overshoot, -Vector3.Dot(s.v[i] - rest, dir) / swing.magnitude);
                    }
                }
                m.phase[name] = (peak, settle, overshoot, peak > 0.5f ? midPeak / peak : float.NaN);
            }
            // Lying down: where it hangs once it has had time to.
            float hang = 0;
            int h0 = Ix(21f), h1 = Ix(22f);
            for (int i = h0; i < h1; i++) hang += Vector3.Angle(s.v[i], rest);
            m.lie = hang / Mathf.Max(1, h1 - h0);
            // and where it settles once even a slow chain has had time to
            hang = 0;
            h0 = Ix(25f); h1 = Ix(27f);
            for (int i = h0; i < h1; i++) hang += Vector3.Angle(s.v[i], rest);
            m.liehold = hang / Mathf.Max(1, h1 - h0);
            return m;
        }

        static void Write()
        {
            string outDir = SessionState.GetString(K + "out", "");
            string avatar = SessionState.GetString(K + "avatar", "avatar");
            foreach (char ch in Path.GetInvalidFileNameChars()) avatar = avatar.Replace(ch, '_');
            var rows = new StringBuilder("chain\tclass\tmetric\tsrc\tdst\tparams\n");
            var perClass = new Dictionary<string, Dictionary<string, List<(float s, float d)>>>();
            void Row(Chain c, string metric, float s, float d)
            {
                rows.Append(c.name).Append('\t').Append(c.cls).Append('\t').Append(metric).Append('\t')
                    .Append(F(s)).Append('\t').Append(F(d)).Append('\t').Append(c.pars).Append('\n');
                if (!perClass.TryGetValue(c.cls, out var byMetric)) perClass[c.cls] = byMetric = new Dictionary<string, List<(float, float)>>();
                if (!byMetric.TryGetValue(metric, out var list)) byMetric[metric] = list = new List<(float, float)>();
                list.Add((s, d));
            }
            foreach (var c in chains)
            {
                if (c.src.v.Count < End * Fps * 0.9f) continue;
                var a = Measure(c.src);
                var b = Measure(c.dst);
                Row(c, "sag", a.sag, b.sag);
                Row(c, "stretch", a.stretch, b.stretch);
                Row(c, "lie", a.lie, b.lie);
                Row(c, "liehold", a.liehold, b.liehold);
                foreach (var (name, _, _, _) in Phases)
                {
                    var p = a.phase[name];
                    var q = b.phase[name];
                    // A head turn only reaches chains below the head.
                    if (name == "head" && p.peak < 1f && q.peak < 1f) continue;
                    Row(c, name + ".peak", p.peak, q.peak);
                    Row(c, name + ".settle", p.settle, q.settle);
                    Row(c, name + ".overshoot", p.overshoot, q.overshoot);
                    Row(c, name + ".midshare", p.midShare, q.midShare);
                }
            }
            File.WriteAllText(Path.Combine(outDir, avatar + ".tsv"), rows.ToString());

            // Medians per class: the copy against the original, side by side.
            var sum = new StringBuilder($"{avatar}: {chains.Count} chain(s)\n");
            foreach (var cls in perClass.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                sum.Append($"\n[{cls}] n={perClass[cls]["sag"].Count}\n");
                foreach (var metric in new[] { "sag", "lie", "liehold", "walk.peak", "walk.settle", "walk.overshoot", "walk.midshare",
                             "turn.peak", "head.peak", "head.settle", "jump.peak", "shake.peak", "stretch" })
                {
                    if (!perClass[cls].TryGetValue(metric, out var list)) continue;
                    float Median(IEnumerable<float> xs)
                    {
                        var o = xs.Where(x => !float.IsNaN(x)).OrderBy(x => x).ToList();
                        return o.Count == 0 ? float.NaN : o[o.Count / 2];
                    }
                    int never = list.Count(x => float.IsNaN(x.d)) ;
                    sum.Append($"  {metric,-15} original {F(Median(list.Select(x => x.s))),7}   copy {F(Median(list.Select(x => x.d))),7}" +
                               (never > 0 ? $"   ({never} never settle in the copy)" : "") + "\n");
                }
            }
            File.WriteAllText(Path.Combine(outDir, avatar + ".txt"), sum.ToString());
            Debug.Log("[PhysicsAb] wrote " + avatar);
        }
    }
}
#endif
