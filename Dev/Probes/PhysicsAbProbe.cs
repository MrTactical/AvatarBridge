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
        const float End = 35f;

        // Motion start, motion end, end of the window that watches it settle.
        static readonly (string name, float start, float stop, float end)[] Phases =
        {
            ("walk", 2f, 4f, 6.5f),
            ("turn", 6.5f, 7f, 9.5f),
            ("head", 9.5f, 9.9f, 12f),
            ("jump", 12f, 12.6f, 15f),
            ("shake", 15f, 16f, 18.5f),
            ("lie", 18.5f, 19.2f, 27f),
            ("squeeze", 31.5f, 33f, 35f),
        };

        sealed class Side
        {
            public Transform root, tip, mid, press;
            public Vector3 endpoint, bind, bindMid;
            public Vector3? restPress;
            public float pressRadius;
            public readonly List<Vector3> v = new List<Vector3>(), m = new List<Vector3>(), r = new List<Vector3>();
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
        static readonly HashSet<string> SoftClasses = new HashSet<string> { "Breast", "Butt", "Belly", "Thigh" };

        sealed class Pair
        {
            public Chain a, b;
            public float srcRest, dstRest, srcMin = float.MaxValue, dstMin = float.MaxValue;
        }

        static List<Pair> pairs;
        static List<(string name, Transform s, Transform d, List<Vector3> sp, List<Vector3> dp)> traced;
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

        // A chain layered by the joint spike is simulated on its copy, beside it.
        static bool Same(Transform r, Transform d) => r == d || (r.parent == d.parent && r.name == d.name + " (cloth copy)");

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
            SessionState.SetString(K + "capture", Arg("-abCapture") ?? "");
            SessionState.SetInt(K + "bake", Array.IndexOf(Environment.GetCommandLineArgs(), "-abBake") >= 0 ? 1 : 0);
            SessionState.SetInt(K + "softShots", Array.IndexOf(Environment.GetCommandLineArgs(), "-abSoftShots") >= 0 ? 1 : 0);
            SessionState.SetInt(K + "softCollide", Array.IndexOf(Environment.GetCommandLineArgs(), "-abSoftCollide") >= 0 ? 1 : 0);
            SessionState.SetInt(K + "live", Array.IndexOf(Environment.GetCommandLineArgs(), "-abLive") >= 0 ? 1 : 0);
            SessionState.SetString(K + "bones", Arg("-abBones") ?? "");
            SessionState.SetString(K + "trace", Arg("-abTrace") ?? "");
            SessionState.SetInt(K + "stripFury", Array.IndexOf(Environment.GetCommandLineArgs(), "-abStripBrokenFury") >= 0 ? 1 : 0);
            SessionState.SetFloat(K + "forceSquish", float.TryParse(Arg("-abForceSquish"), NumberStyles.Float, CultureInfo.InvariantCulture, out float squish) ? squish : 0f);
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
                if (SessionState.GetInt(K + "live", 0) == 1) return;   // watched: leave the editor open
                EditorApplication.Exit(0);
                return;
            }
            try
            {
                if (Prepare(scenes[index].Trim()))
                {
                    // NDMF rebuilds an avatar on entering Play (marshmallow PB is an NDMF plugin), which
                    // destroys the bones this probe drives. Its switch lasts the session only.
                    var ndmf = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType("nadena.dev.ndmf.config.Config", false)).FirstOrDefault(x => x != null);
                    ndmf?.GetProperty("ApplyOnPlay")?.SetValue(null, false);
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
            // A conversion saved in the scene would stand in the same spot on camera. Gone in memory only.
            foreach (var old in scene.GetRootGameObjects().Where(r => r.name.EndsWith(" (ChilloutVR)")))
                UnityEngine.Object.DestroyImmediate(old);
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
            // By name, so the probe still builds against a converter from before the option.
            if (SessionState.GetInt(K + "softCollide", 0) == 1)
            {
                typeof(BridgeSettings).GetField("softBodiesCollide")?.SetValue(settings, true);
            }
            // -abBake: an avatar whose PhysBones only exist after VRCFury or NDMF has run (marshmallow
            // PB adds its own at build) is compared against a baked copy, the way an upload sees it.
            // Baked BEFORE converting, from the untouched original; NDMF decides for itself whether
            // it has anything to do, since a plugin carries no Modular Avatar component to find.
            // -abStripBrokenFury: one VRCFury component whose files are not in this project stops the
            // whole build, and the PhysBones a build adds with it. Removed from the open scene only.
            if (SessionState.GetInt(K + "stripFury", 0) == 1)
            {
                if (PrefabUtility.IsPartOfPrefabInstance(descriptor.gameObject))
                {
                    PrefabUtility.UnpackPrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(descriptor.gameObject),
                        PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }
                foreach (var c in descriptor.GetComponentsInChildren<Component>(true).Where(IsBrokenFury).ToList())
                {
                    Debug.Log($"[PhysicsAb] removed the broken {c.GetType().Name} on {c.name}");
                    UnityEngine.Object.DestroyImmediate(c);
                }
            }
            // -abForceSquish v: Max Squish v on every soft-body PhysBone that has none, original and
            // copy alike, to measure the squish conversion on avatars that never used it.
            float forceSquish = SessionState.GetFloat(K + "forceSquish", 0f);
            if (forceSquish > 0f)
            {
                var anim = descriptor.GetComponent<Animator>();
                foreach (var pb in descriptor.GetComponentsInChildren<VRCPhysBone>(true))
                {
                    if (pb.maxSquish > 0f) continue;
                    var d = PhysBoneChainData.Read(pb, anim, true);
                    if (d.Root == null || !SoftClasses.Contains(MagicaPresetLibrary.Classify(d).Name)) continue;
                    pb.maxSquish = forceSquish;
                    Debug.Log($"[PhysicsAb] Max Squish {forceSquish} on {pb.name}");
                }
            }
            GameObject bakedSource = null;
            if (SessionState.GetInt(K + "bake", 0) == 1)
            {
                var bakeReport = new BridgeReport();
                bakedSource = VRCFuryBaker.HasFuryComponents(descriptor.gameObject)
                    ? VRCFuryBaker.TryBake(descriptor, bakeReport)
                    : ModularAvatarBaker.TryBake(descriptor, bakeReport);
                // To a file as well: a VRCFury build can leave Debug.Log silent for the rest of the call.
                File.WriteAllText(Path.Combine(SessionState.GetString(K + "out", ""), "bake.txt"),
                    $"returned {(bakedSource == null ? "null" : bakedSource.name)}, descriptor " +
                    $"{(bakedSource != null && bakedSource.GetComponent<VRCAvatarDescriptor>() != null)}, " +
                    $"{(bakedSource == null ? 0 : bakedSource.GetComponentsInChildren<VRCPhysBone>(true).Length)} PhysBone(s)\n" +
                    string.Join("\n", bakeReport.Entries.Select(e => e.Subject + " | " + e.Detail)) +
                    "\nscene roots: " + string.Join(", ", descriptor.gameObject.scene.GetRootGameObjects()
                        .Select(g => $"{g.name}[{g.transform.childCount}]{(g.activeSelf ? "" : " off")}")) +
                    (bakedSource == null ? "" : "\nbaked children: " + string.Join(", ",
                        Enumerable.Range(0, bakedSource.transform.childCount).Select(i => bakedSource.transform.GetChild(i))
                            .Select(c => $"{c.name}{(c.gameObject.activeSelf ? "" : " off")}"))));
                if (bakedSource == null || bakedSource.GetComponent<VRCAvatarDescriptor>() == null)
                {
                    bakedSource = null;
                    Debug.Log("[PhysicsAb] nothing to bake: " + string.Join(" | ", bakeReport.Entries.Select(e => e.Subject + " " + e.Detail)));
                }
                else
                {
                    // VRCFury replaces a test copy of the same name, so the converter's own bake would
                    // destroy this one.
                    bakedSource.name = descriptor.name + " (probe source)";
                    bakedSource.transform.SetPositionAndRotation(descriptor.transform.position, descriptor.transform.rotation);
                    bakedSource.SetActive(false);
                }
            }
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
            if (bakedSource != null)
            {
                bakedSource.SetActive(true);
                descriptor.gameObject.SetActive(false);
                descriptor = bakedSource.GetComponent<VRCAvatarDescriptor>();
                Debug.Log($"[PhysicsAb] comparing against the baked original, {descriptor.GetComponentsInChildren<VRCPhysBone>(true).Length} PhysBone(s)");
            }
            // A calibration sweep scales every cloth's gravity here, on the copy only.
            if (float.TryParse(SessionState.GetString(K + "gravityScale", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float gravityScale))
            {
                foreach (var cloth in converted.GetComponentsInChildren<MagicaCloth>(true)) cloth.SerializeData.gravity *= gravityScale;
                Debug.Log($"[PhysicsAb] gravity scaled by {gravityScale}");
            }
            // -abLayered a;b: these chains on the copy get the joint spike layered on MagicaCloth2.
            var layered = (Arg("-abLayered") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var bone in converted.GetComponentsInChildren<Transform>(true).Where(t => layered.Contains(t.name)).ToList())
                JointSpike.BuildLayered(bone, false, null);
            pressPushesBodies = layered.Length > 0;
#if AVATARBRIDGE_YAPS
            // -abDent: the skin over those chains takes the YAPS dent, fed the copy's presses.
            if (Dent)
            {
                // Every skinned layer, not only the skin: a top left undented had the skin push
                // through it in stripes. Clothing often rides bones of its own, so match by none.
                var done = new Dictionary<Material, Material>();
                foreach (var smr in converted.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mats = smr.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null) continue;
                        if (!done.TryGetValue(mats[i], out var dented))
                        {
                            var shader = YapsShaderPatcher.Patch(mats[i], "Assets/PhysicsAbDent", null, out string refusal, out _);
                            dented = shader == null ? null : new Material(mats[i]) { shader = shader };
                            if (dented != null) dented.SetFloat("_YAPS_DentPower", 1f);
                            // -abBulge x: the ring's height, for A/B against the default.
                            if (dented != null && float.TryParse(Arg("-abBulge"), NumberStyles.Float, CultureInfo.InvariantCulture, out float bulge))
                                dented.SetFloat("_YAPS_DentBulge", bulge);
                            if (dented != null && float.TryParse(Arg("-abSpill"), NumberStyles.Float, CultureInfo.InvariantCulture, out float spill))
                                dented.SetFloat("_YAPS_DentSpill", spill);
                            Debug.Log($"[PhysicsAb] dent on \"{mats[i].name}\" of {smr.name}: {(dented != null ? shader.name : refusal)}");
                            done[mats[i]] = dented;
                        }
                        if (dented != null) mats[i] = dented;
                    }
                    smr.sharedMaterials = mats;
                }
            }
#endif

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
            pressParent = null;
            int skippedNoCloth = 0, skippedNoMatch = 0;
            var pbList = new StringBuilder();
            foreach (var pb in descriptor.GetComponentsInChildren<VRCPhysBone>(true))
            {
                pbList.Append($"{Rel(pb.transform, descriptor.transform)} active={pb.gameObject.activeInHierarchy} enabled={pb.enabled} root={(pb.rootTransform != null ? pb.rootTransform.name : "-")} squish={pb.maxSquish} colliders=" +
                    string.Join(",", pb.colliders.Where(x => x != null).Select(x => x.name + (((VRCPhysBoneCollider)x).insideBounds ? "(in)" : ""))) + "\n");
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
                if (!clothRoots.Any(r => Same(r, dRoot) || r.IsChildOf(dRoot) || dRoot.IsChildOf(r)))
                {
                    skippedNoCloth++;
                    continue;
                }

                var endpoint = tip == root ? data.EndpointPosition : Vector3.zero;

                // A sphere for each side that presses into the tip along the chain after the lying
                // hold: a PhysBone collider on the source chain, a MagicaCloth2 one on the copy's cloth.
                float reach = tip == root ? root.TransformVector(endpoint).magnitude : Vector3.Distance(root.position, tip.position);
                float pressRadius = Mathf.Max(0.01f, 0.2f * reach);
                var srcPress = MakePress("PhysicsAb press", pressRadius);
                var physCollider = srcPress.gameObject.AddComponent<VRCPhysBoneCollider>();
                physCollider.radius = pressRadius;
                pb.colliders.Add(physCollider);
                var dstPress = MakePress("PhysicsAb press copy", pressRadius);
                var magicaCollider = dstPress.gameObject.AddComponent<MagicaSphereCollider>();
                magicaCollider.SetSize(pressRadius);
                foreach (var cloth in converted.GetComponentsInChildren<MagicaCloth>(true))
                {
                    if (!cloth.SerializeData.rootBones.Any(r => r != null && (Same(r, dRoot) || r.IsChildOf(dRoot) || dRoot.IsChildOf(r)))) continue;
                    var collision = cloth.SerializeData.colliderCollisionConstraint;
                    collision.colliderList.Add(magicaCollider);
                    if (collision.mode == ColliderCollisionConstraint.Mode.None) collision.mode = ColliderCollisionConstraint.Mode.Point;
                }
                string pars = string.Format(CultureInfo.InvariantCulture,
                    (IsVersion10(pb) ? "v1.0 " : "v1.1 ") + "{0} pull={1:0.##} spring={2:0.##} stiff={3:0.##} grav={4:0.##}/{5:0.##} imm={6:0.##}({7}) limit={8}:{9:0}/{10:0} bones={11} radius={12:0.###} squish={13:0.##} stretch={14:0.##}",
                    data.IsAdvancedIntegration ? "adv" : "simple", data.Pull, data.Spring, data.Stiffness, data.Gravity,
                    data.GravityFalloff, data.Immobile, data.ImmobileTypeName, data.LimitTypeName, data.MaxAngleX, data.MaxAngleZ, tipDepth + 1,
                    data.Radius, data.MaxSquish, data.MaxStretch);
                lines.Add(string.Join("|",
                    Rel(root, descriptor.transform).Replace("|", "_"),
                    MagicaPresetLibrary.Classify(data).Name,
                    pars,
                    IndexPath(root), IndexPath(tip), IndexPath(mid),
                    IndexPath(dRoot), IndexPath(dTip), IndexPath(dMid),
                    V3(endpoint),
                    IndexPath(srcPress), IndexPath(dstPress), pressRadius.ToString(CultureInfo.InvariantCulture)));
            }
            // A cloth the converter put on a bone no PhysBone roots: a physics add-on (marshmallow PB)
            // moves that bone through constraints from its own chains. Measured on the same bone,
            // pressed by a collider every source PhysBone hears, since which layer feels a touch is
            // the add-on's business.
            // -abBones a;b: these bones too, by name, cloth or not: a kept add-on rig moves them through
            // constraints, with every cloth hearing the press.
            var named = (SessionState.GetString(K + "bones", "")).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var namedBones = converted.GetComponentsInChildren<Transform>(true).Where(x => named.Contains(x.name)).ToList();
            foreach (var dRoot in clothRoots.Concat(namedBones).Distinct())
            {
                bool byName = namedBones.Contains(dRoot);
                if (seenRoots.Any(r => { var d = Find(converted.transform, Rel(r, descriptor.transform)); return d != null && (d == dRoot || dRoot.IsChildOf(d)); })) continue;
                var root = Find(descriptor.transform, Rel(dRoot, converted.transform));
                if (root == null || root.childCount == 0) continue;
                Transform tip = root;
                int tipDepth = 0;
                void Deep(Transform x, int depth)
                {
                    if (depth > tipDepth) { tip = x; tipDepth = depth; }
                    for (int i = 0; i < x.childCount; i++) Deep(x.GetChild(i), depth + 1);
                }
                Deep(root, 0);
                var mid = tip;
                for (int i = 0; i < tipDepth / 2 && mid != root; i++) mid = mid.parent;
                var dTip = Find(converted.transform, Rel(tip, descriptor.transform));
                var dMid = Find(converted.transform, Rel(mid, descriptor.transform));
                if (dTip == null || dMid == null) continue;
                float pressRadius = Mathf.Max(0.01f, 0.2f * Vector3.Distance(root.position, tip.position));
                var srcPress = MakePress("PhysicsAb press", pressRadius);
                var physCollider = srcPress.gameObject.AddComponent<VRCPhysBoneCollider>();
                physCollider.radius = pressRadius;
                foreach (var pb in descriptor.GetComponentsInChildren<VRCPhysBone>(true)) pb.colliders.Add(physCollider);
                var dstPress = MakePress("PhysicsAb press copy", pressRadius);
                var magicaCollider = dstPress.gameObject.AddComponent<MagicaSphereCollider>();
                magicaCollider.SetSize(pressRadius);
                foreach (var cloth in converted.GetComponentsInChildren<MagicaCloth>(true))
                {
                    if (!byName && !cloth.SerializeData.rootBones.Any(r => r != null && Same(r, dRoot))) continue;
                    var collision = cloth.SerializeData.colliderCollisionConstraint;
                    collision.colliderList.Add(magicaCollider);
                    if (collision.mode == ColliderCollisionConstraint.Mode.None) collision.mode = ColliderCollisionConstraint.Mode.Point;
                }
                string lower = root.name.ToLowerInvariant();
                lines.Add(string.Join("|",
                    Rel(root, descriptor.transform).Replace("|", "_"),
                    lower.Contains("butt") ? "Butt" : lower.Contains("breast") ? "Breast" : "Driven",
                    "driven by constraints",
                    IndexPath(root), IndexPath(tip), IndexPath(mid),
                    IndexPath(dRoot), IndexPath(dTip), IndexPath(dMid),
                    V3(Vector3.zero),
                    IndexPath(srcPress), IndexPath(dstPress), pressRadius.ToString(CultureInfo.InvariantCulture)));
                Debug.Log($"[PhysicsAb] driven bone {root.name} measured against its cloth");
            }
            File.WriteAllText(Path.Combine(SessionState.GetString(K + "out", ""), "physbones.txt"), pbList.ToString());
            // -abTrace a;b: every bone under these, on both sides, recorded each step, to find where
            // two hierarchies part company. Paired by path; a bone the copy lacks is left out.
            var traceRoots = SessionState.GetString(K + "trace", "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var trace = new List<string>();
            foreach (var x in descriptor.GetComponentsInChildren<Transform>(true).Where(x => traceRoots.Contains(x.name)))
            {
                foreach (var b in x.GetComponentsInChildren<Transform>(true))
                {
                    var d = Find(converted.transform, Rel(b, descriptor.transform));
                    if (d != null) trace.Add(Rel(b, descriptor.transform) + "|" + IndexPath(b) + "|" + IndexPath(d));
                }
            }
            SessionState.SetString(K + "tracePairs", string.Join("\n", trace));
            Debug.Log($"[PhysicsAb] {scenePath}: {lines.Count} chain(s) matched, {skippedNoCloth} without a cloth, {skippedNoMatch} not found in the copy");
            if (lines.Count == 0) return false;

            SessionState.SetString(K + "avatar", descriptor.name);
            // VRCFury builds every avatar in the scene on entering Play, running the upload hooks
            // (NDMF plugins included), which rebuilds bones this probe drives. It skips names
            // carrying Av3Emulator's "(ShadowClone)"; the scene is never saved, and no machine-wide
            // preference gets touched.
            descriptor.gameObject.name += " (ShadowClone)";
            SessionState.SetString(K + "bodies", string.Join("|", IndexPath(descriptor.transform), head != null ? IndexPath(head) : "",
                IndexPath(converted.transform), dstHead != null ? IndexPath(dstHead) : ""));
            SessionState.SetString(K + "chains", string.Join("\n", lines));
            return true;
        }

        static string Rel(Transform t, Transform root) => AnimationUtility.CalculateTransformPath(t, root);

        // From the component, not PhysBoneChainData, so the probe still builds against a converter
        // from before the chain data carried it.
        static bool IsVersion10(VRCPhysBone pb) => pb.version.ToString().Contains("1_0");

        // The test VRCFuryBaker reports with: a reference that was set and no longer resolves.
        static bool IsBrokenFury(Component c)
        {
            if (c == null) return false;
            string ns = c.GetType().Namespace ?? "";
            if (ns != "VF" && !ns.StartsWith("VF.") && !c.GetType().Name.StartsWith("VRCFury")) return false;
            var it = new SerializedObject(c).GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType == SerializedPropertyType.ObjectReference
                    && it.objectReferenceInstanceIDValue != 0 && it.objectReferenceValue == null) return true;
            }
            return false;
        }

        // Parked far below until the press; a plain sphere shows it on camera, without a collider of its own.
        static Transform pressParent;
        // A layered chain is squashed through its bodies, which only a physics collider reaches.
        static bool pressPushesBodies;
        // Read from the command line each time: a static set before Play is gone after the reload.
        static bool Dent => Array.IndexOf(Environment.GetCommandLineArgs(), "-abDent") >= 0;

        // The colliders go on an unscaled root and the visible ball on a child: both solvers scale a
        // collider by its transform, so one on a sphere scaled to its own diameter was far smaller
        // than it looked.
        static Transform MakePress(string name, float radius)
        {
            if (pressParent == null) pressParent = new GameObject("PhysicsAb presses").transform;
            var root = new GameObject(name).transform;
            root.SetParent(pressParent, false);
            root.position = Vector3.down * 100f;
            if (pressPushesBodies && name.EndsWith(" copy"))
            {
                root.gameObject.AddComponent<SphereCollider>().radius = radius;
                root.gameObject.AddComponent<Rigidbody>().isKinematic = true;
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "ball";
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            go.transform.localScale = Vector3.one * radius * 2f;
            // Drawn through the body: it presses into flesh, where an opaque sphere is hidden.
            var mat = new Material(Shader.Find("Hidden/Internal-Colored"));
            mat.SetColor("_Color", new Color(1f, 0.35f, 0.2f, 0.45f));
            mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            mat.SetInt("_ZWrite", 0);
            mat.SetInt("_Cull", 0);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.renderQueue = 4000;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            // Hidden in a dent run: drawn over the skin, even a faint ball hid the dent it made.
            go.GetComponent<Renderer>().enabled = !Dent;
            return root;
        }

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
            traced = new List<(string name, Transform s, Transform d, List<Vector3> sp, List<Vector3> dp)>();
            foreach (var line in SessionState.GetString(K + "tracePairs", "").Split('\n'))
            {
                var f = line.Split('|');
                if (f.Length < 3) continue;
                var s = FromIndexPath(f[1]); var d = FromIndexPath(f[2]);
                if (s != null && d != null) traced.Add((f[0], s, d, new List<Vector3>(), new List<Vector3>()));
            }
            chains = new List<Chain>();
            foreach (var line in SessionState.GetString(K + "chains", "").Split('\n'))
            {
                var f = line.Split('|');
                if (f.Length < 10) continue;
                var c = new Chain { name = f[0], cls = f[1], pars = f[2] };
                c.src.root = FromIndexPath(f[3]); c.src.tip = FromIndexPath(f[4]); c.src.mid = FromIndexPath(f[5]);
                c.dst.root = FromIndexPath(f[6]); c.dst.tip = FromIndexPath(f[7]); c.dst.mid = FromIndexPath(f[8]);
                c.src.endpoint = c.dst.endpoint = ParseV3(f[9]);
                if (f.Length >= 13)
                {
                    c.src.press = FromIndexPath(f[10]);
                    c.dst.press = FromIndexPath(f[11]);
                    c.src.pressRadius = c.dst.pressRadius = float.Parse(f[12], CultureInfo.InvariantCulture);
                }
                if (c.src.root == null || c.dst.root == null || c.src.tip == null || c.dst.tip == null) continue;
                chains.Add(c);
            }
            foreach (var c in chains)
            {
                c.src.bind = Vec(c.src, false); c.src.bindMid = Vec(c.src, true);
                c.dst.bind = Vec(c.dst, false); c.dst.bindMid = Vec(c.dst, true);
            }
            // Soft bodies whose tips start within reach of each other: the pair a breast makes.
            pairs = new List<Pair>();
            var soft = chains.Where(c => SoftClasses.Contains(c.cls)).ToList();
            for (int i = 0; i < soft.Count; i++)
            {
                for (int j = i + 1; j < soft.Count; j++)
                {
                    float rest = Vector3.Distance(soft[i].src.tip.position, soft[j].src.tip.position);
                    float reach = soft[i].src.bind.magnitude + soft[j].src.bind.magnitude;
                    if (rest > 0f && rest < 2f * reach)
                    {
                        pairs.Add(new Pair
                        {
                            a = soft[i], b = soft[j], srcRest = rest,
                            dstRest = Vector3.Distance(soft[i].dst.tip.position, soft[j].dst.tip.position),
                        });
                    }
                }
            }
            SetUpCapture();
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
            if (t >= 27f) Press(t);
            Watch(t);
        }

        // Push in over a second, hold a second, let go over half a second: the sphere starts just
        // past the tip and travels toward the root by 40% of the chain's reach.
        static void Press(float t)
        {
            float push = t < 28f ? Smooth(t - 27f) : t < 29f ? 1f : t < 29.5f ? 1f - Smooth((t - 29f) / 0.5f) : 0f;
            foreach (var c in chains)
            {
                foreach (var s in new[] { c.src, c.dst })
                {
                    if (s.press == null) continue;
                    if (s.restPress == null)
                    {
                        int from = Mathf.Max(0, s.v.Count - Mathf.RoundToInt(0.5f * Fps));
                        var sum = Vector3.zero;
                        for (int i = from; i < s.v.Count; i++) sum += s.v[i];
                        s.restPress = sum / Mathf.Max(1, s.v.Count - from);
                    }
                    var parent = s.root.parent;
                    var rootWorld = s.root.position;
                    var tipWorld = parent.TransformPoint(parent.InverseTransformPoint(rootWorld) + s.restPress.Value);
                    var along = tipWorld - rootWorld;
                    var dir = along.normalized;
                    // From 31.5 s a soft body with a partner of its class is squeezed instead: the same
                    // sphere comes in from its outer side toward the partner, as two hands pushing the
                    // pair together would.
                    if (t >= 31.5f)
                    {
                        push = t < 32.5f ? Smooth(t - 31.5f) : t < 33.5f ? 1f : t < 34f ? 1f - Smooth((t - 33.5f) / 0.5f) : 0f;
                        var partner = SoftClasses.Contains(c.cls)
                            ? chains.Where(o => o != c && o.cls == c.cls)
                                .OrderBy(o => Vector3.Distance((s == c.src ? o.src : o.dst).root.position, rootWorld)).FirstOrDefault()
                            : null;
                        if (partner == null) { s.press.position = Vector3.down * 100f; continue; }
                        // A palm-sized ball, the gap between the pair across, starts clear of the
                        // outer side and ends with its surface at the middle: the pair pushed into
                        // each other, as two hands squeezing would.
                        var other = (s == c.src ? partner.src : partner.dst).tip.position;
                        var outward = Vector3.ProjectOnPlane(tipWorld - other, Vector3.up).normalized;
                        float half = 0.5f * Vector3.Distance(tipWorld, other);
                        float big = Mathf.Max(s.pressRadius, half);
                        var middle = 0.5f * (tipWorld + other);
                        s.press.localScale = Vector3.one * (big / s.pressRadius);
                        // -abSqueezeDepth d: how far of the way to the middle the ball goes, 1 all of it.
                        // All of it drives the pair a whole gap into each other, past what a hand does.
                        float depth = float.TryParse(Arg("-abSqueezeDepth"), NumberStyles.Float, CultureInfo.InvariantCulture, out float d) ? d : 1f;
                        s.press.position = push <= 0f ? Vector3.down * 100f
                            : Vector3.Lerp(tipWorld + outward * (big + half), middle + outward * (big + (1f - depth) * half), push);
                        continue;
                    }
                    s.press.localScale = Vector3.one;
                    s.press.position = push <= 0f ? Vector3.down * 100f
                        : tipWorld + dir * (s.pressRadius * 1.05f) - dir * (0.4f * along.magnitude * push);
                }
            }
        }

        // A soft part, measured from the vertices its bones carry: where its middle is and how
        // its flesh spreads, in its bone's space so both follow the bone. Found on the first
        // frame of Play.
        sealed class DentPart
        {
            public string name;
            public Transform bone;
            public Vector3 middle;
            public Matrix4x4 spread;   // covariance of its skin, 3x3 in the corner
            public float restGap;      // to its partner, on the first frame
            public Vector3[] skin;     // its vertices, in its bone's space
            public float deepest;
            public string deepestNote;
        }
        static List<DentPart> dentParts;

        // Fed after MagicaCloth2 writes and before the frame is filmed, so the dent follows the
        // bones as drawn. Both avatars see the globals; only the copy's skin reads them.
        static void FeedDent()
        {
            if (!Dent) return;
            // Layered chains only: four slots, and other presses took them first.
            var layered = (Arg("-abLayered") ?? "").Split(';');
            var mine = chains.Where(c => layered.Contains(c.dst.root.name)).ToList();
            var spheres = new Vector4[4];
            int n = 0;
            // -abNoSpheres: the pair squash alone, without the presses' dents.
            bool noSpheres = Array.IndexOf(Environment.GetCommandLineArgs(), "-abNoSpheres") >= 0;
            foreach (var c in noSpheres ? new List<Chain>() : mine)
            {
                var p = c.dst.press;
                if (p == null || p.position.y < -50f || n == 4) continue;
                if (spheres.Take(n).Any(v => (Vector3)v == p.position)) continue;
                var at = p.position;
                spheres[n++] = new Vector4(at.x, at.y, at.z, c.dst.pressRadius * p.localScale.x);
            }
            Shader.SetGlobalVectorArray("_YAPS_DentSpheres", spheres);

            // In -abLayered order, so its names pair up two by two: a;b;c;d is pairs ab and cd.
            if (dentParts == null) dentParts = BuildDentParts(mine.OrderBy(c => Array.IndexOf(layered, c.dst.root.name)).Take(4).ToList());
            // -abNoPair: spheres only, to see what the pair squash adds.
            var part = new Vector4[4];
            var axes = new Vector4[4];
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-abNoPair") < 0)
                for (int i = 0; i + 1 < dentParts.Count; i += 2)
                {
                    var a = dentParts[i];
                    var b = dentParts[i + 1];
                    var ca = a.bone.TransformPoint(a.middle);
                    var cb = b.bone.TransformPoint(b.middle);
                    var sa = WorldSpread(a);
                    var sb = WorldSpread(b);
                    var axis = (cb - ca).normalized;
                    float gap = Vector3.Distance(ca, cb);
                    // How far each surface reaches toward the other, read from its own skin.
                    float ha = Reach(a, axis);
                    float hb = Reach(b, -axis);
                    if (a.restGap <= 0f)
                    {
                        a.restGap = b.restGap = gap;
                        // Never overlapping at rest, or the pair would spring apart on the first frame.
                        float fit = Mathf.Min(1f, gap / (ha + hb));
                        AddFlesh(a, ha * fit);
                        AddFlesh(b, hb * fit);
                    }
                    // Touching from where they stood at rest, or from where their surfaces meet
                    // if they stood apart: the rest pose stays exactly as authored.
                    float press = Mathf.Max(0f, Mathf.Min(a.restGap, ha + hb) - gap);
                    // The deepest press each pair saw, in the avatar's frame, to read against the film.
                    if (press > a.deepest)
                    {
                        a.deepest = press;
                        a.deepestNote = $"press {press * 100f:0.0} cm, gap {gap * 100f:0.0} (rest {a.restGap * 100f:0.0}), reach {ha * 100f:0.0} + {hb * 100f:0.0}, " +
                                        $"axis {dstBody.avatar.InverseTransformDirection(axis):F2}";
                    }
                    // The bigger part gives more, as a softer one would.
                    float va = Mathf.Sqrt(Mathf.Max(Det3(sa), 0f)), vb = Mathf.Sqrt(Mathf.Max(Det3(sb), 0f));
                    float share = va + vb > 0f ? va / (va + vb) : 0.5f;
                    // Each pushed back toward itself, so the two faces meet on one plane.
                    part[i] = new Vector4(ca.x, ca.y, ca.z, ha);
                    part[i + 1] = new Vector4(cb.x, cb.y, cb.z, hb);
                    axes[i] = new Vector4(axis.x, axis.y, axis.z, press * share);
                    axes[i + 1] = new Vector4(-axis.x, -axis.y, -axis.z, press * (1f - share));
                }
            // Whether the flesh spheres stop anything at all: two kinematic presses can force them through.
            for (int f = 0; f + 1 < flesh.Count; f += 2)
            {
                var fa = (SphereCollider)flesh[f];
                var fb = (SphereCollider)flesh[f + 1];
                float sank = fa.radius * fa.transform.lossyScale.x + fb.radius * fb.transform.lossyScale.x
                             - Vector3.Distance(fa.transform.position, fb.transform.position);
                fleshSank = Mathf.Max(fleshSank, sank);
            }
            Shader.SetGlobalVectorArray("_YAPS_DentPart", part);
            Shader.SetGlobalVectorArray("_YAPS_DentAxis", axes);
        }

        // A flesh-sized sphere on the part's lag body that meets only its partner's, so the pair
        // cannot pass through each other while every other body stays bone-sized (sized to the
        // flesh everywhere, chains moved as blocks and stretched). Short of the reach: the rest
        // is the shader's squash, which flattens the faces where the spheres stop them.
        const float FleshStop = 0.75f;
        static readonly List<Collider> flesh = new List<Collider>();
        static float fleshSank;

        static void AddFlesh(DentPart p, float reach)
        {
            // The nearest body at or below the part's bone: a chain root carries none of its own.
            var at = p.bone.TransformPoint(p.middle);
            // Found through the constraint that hangs each bone on its body: by name, chains whose
            // bones share a name took each other's.
            var body = p.bone.GetComponentsInChildren<UnityEngine.Animations.PositionConstraint>(true)
                .Where(c => c.sourceCount > 0 && c.GetSource(0).sourceTransform != null)
                .Select(c => c.GetSource(0).sourceTransform.GetComponent<Rigidbody>())
                .Where(r => r != null)
                .OrderBy(r => Vector3.Distance(r.position, at)).FirstOrDefault();
            if (body == null) { Debug.Log($"[PhysicsAb] dent part {p.name}: no lag body on {p.bone.name}, so no flesh sphere"); return; }
            var go = new GameObject(p.name + " (flesh)");
            go.transform.SetParent(body.transform, false);
            go.transform.position = at;
            var sphere = go.AddComponent<SphereCollider>();
            sphere.radius = FleshStop * reach / go.transform.lossyScale.x;
            // Frictionless, so a pair pressed together slides off along its contact and rolls aside,
            // the way pressed flesh gives; with friction they held and were forced through.
            sphere.sharedMaterial = new PhysicMaterial("flesh")
            {
                dynamicFriction = 0f, staticFriction = 0f,
                frictionCombine = PhysicMaterialCombine.Minimum, bounceCombine = PhysicMaterialCombine.Minimum
            };
            // Pairwise, not by layer: a layer matrix changed in the editor can outlive Play.
            foreach (var other in UnityEngine.Object.FindObjectsOfType<Collider>())
                if (other != sphere && !flesh.Contains(other)) Physics.IgnoreCollision(sphere, other);
            flesh.Add(sphere);
            Debug.Log($"[PhysicsAb] dent part {p.name}: flesh sphere {FleshStop * reach * 100f:0.0} cm on {body.name}");
        }

        // How far its skin really reaches one way: the spread said 15 cm where breasts that touched
        // at rest reached 22, so the squash barely began. The 98th percentile, so one stray vertex
        // does not set it.
        static float Reach(DentPart p, Vector3 way)
        {
            var local = p.bone.localToWorldMatrix.transpose.MultiplyVector(way);
            var along = new float[p.skin.Length];
            for (int i = 0; i < along.Length; i++) along[i] = Vector3.Dot(p.skin[i] - p.middle, local);
            Array.Sort(along);
            return Mathf.Max(along[(int)(0.98f * (along.Length - 1))], 1e-4f);
        }

        static Matrix4x4 WorldSpread(DentPart p)
        {
            var m = p.bone.localToWorldMatrix;
            m.SetColumn(3, new Vector4(0, 0, 0, 1));
            var w = m * p.spread * m.transpose;
            w.m33 = 1f;
            return w;
        }

        static float Det3(Matrix4x4 m) =>
            m.m00 * (m.m11 * m.m22 - m.m12 * m.m21) - m.m01 * (m.m10 * m.m22 - m.m12 * m.m20) + m.m02 * (m.m10 * m.m21 - m.m11 * m.m20);

        // Each vertex's share of each part is its skin weight to that part's bones, baked into a
        // texture per renderer and read by vertex id. Each part's shape is measured from the
        // vertices it carries at least half of, skinned by hand from the bind poses: a bake's
        // space depends on the renderer's scale, which a converted rig can carry at 100.
        static List<DentPart> BuildDentParts(List<Chain> soft)
        {
            var parts = soft.Select(c => new DentPart { name = c.dst.root.name }).ToList();
            var boneSets = soft.Select(c => new HashSet<Transform>(c.dst.root.GetComponentsInChildren<Transform>(true))).ToList();
            var points = parts.Select(_ => new List<Vector3>()).ToList();
            var carried = parts.Select(_ => new Dictionary<Transform, float>()).ToList();
            foreach (var smr in dstBody.avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var bones = smr.bones;
                var owner = new int[bones.Length];
                for (int b = 0; b < bones.Length; b++)
                    owner[b] = bones[b] == null ? -1 : boneSets.FindIndex(set => set.Contains(bones[b]));
                if (owner.All(x => x < 0)) continue;
                var weights = mesh.boneWeights;
                var verts = mesh.vertices;
                var bind = mesh.bindposes;
                var skin = new Matrix4x4[bones.Length];
                for (int b = 0; b < bones.Length && b < bind.Length; b++)
                    skin[b] = bones[b] != null ? bones[b].localToWorldMatrix * bind[b] : Matrix4x4.zero;
                int width = 1024, height = Mathf.Max(1, (verts.Length + width - 1) / width);
                var pixels = new Color32[width * height];
                var share = new float[4];
                for (int v = 0; v < weights.Length && v < verts.Length; v++)
                {
                    var w = weights[v];
                    Array.Clear(share, 0, 4);
                    int[] index = { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
                    float[] weight = { w.weight0, w.weight1, w.weight2, w.weight3 };
                    for (int q = 0; q < 4; q++)
                    {
                        int part = owner[index[q]];
                        if (weight[q] <= 0f || part < 0) continue;
                        share[part] += weight[q];
                        var bone = bones[index[q]];
                        carried[part][bone] = (carried[part].TryGetValue(bone, out var t) ? t : 0f) + weight[q];
                    }
                    pixels[v] = new Color32((byte)(share[0] * 255f + 0.5f), (byte)(share[1] * 255f + 0.5f),
                        (byte)(share[2] * 255f + 0.5f), (byte)(share[3] * 255f + 0.5f));
                    int mostly = Array.FindIndex(share, x => x >= 0.5f);
                    if (mostly < 0) continue;
                    var at = Vector3.zero;
                    for (int q = 0; q < 4; q++) at += skin[index[q]].MultiplyPoint3x4(verts[v]) * weight[q];
                    points[mostly].Add(at);
                }
                // Linear and point-sampled: a share is a number, not a colour.
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
                tex.SetPixels32(pixels);
                tex.Apply(false);
                var block = new MaterialPropertyBlock();
                smr.GetPropertyBlock(block);
                block.SetTexture("_YAPS_DentOwn", tex);
                block.SetVector("_YAPS_DentOwn_TexelSize", new Vector4(1f / width, 1f / height, width, height));
                smr.SetPropertyBlock(block);
            }
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (carried[i].Count == 0 || points[i].Count < 20) { Debug.Log($"[PhysicsAb] dent part {p.name}: too few vertices"); continue; }
                // Follows the bone that carries most of it.
                p.bone = carried[i].OrderByDescending(kv => kv.Value).First().Key;
                var local = points[i].Select(x => p.bone.InverseTransformPoint(x)).ToList();
                p.middle = local.Aggregate(Vector3.zero, (acc, x) => acc + x) / local.Count;
                var c = Matrix4x4.zero;
                foreach (var x in local)
                {
                    var d = x - p.middle;
                    for (int r = 0; r < 3; r++)
                        for (int k = 0; k < 3; k++)
                            c[r, k] += d[r] * d[k] / local.Count;
                }
                c.m33 = 1f;
                p.spread = c;
                p.skin = local.ToArray();
                var world = WorldSpread(p);
                Debug.Log($"[PhysicsAb] dent part {p.name}: {points[i].Count} vertices on {p.bone.name}, " +
                          $"half-widths {Mathf.Sqrt(3f * world.m00) * 100f:0.0} x {Mathf.Sqrt(3f * world.m11) * 100f:0.0} x {Mathf.Sqrt(3f * world.m22) * 100f:0.0} cm (world x y z)");
            }
            return parts.Where(p => p.bone != null).ToList();
        }

        // ------------------------------------------------------------------ filming

        // -abCapture <dir>: every third step (30 frames a second of simulated time) each shot is
        // rendered once per avatar, from the same offset, so the two can be laid side by side.
        // Each avatar is put on a layer of its own so neither camera sees the other one.
        sealed class Shot
        {
            public string name;
            public Vector3 offset;   // in the avatar's starting frame, in avatar heights
            public bool head, soft;
            public Camera src, dst;
        }

        static List<Shot> shots;
        static RenderTexture shotTarget;
        static Texture2D shotPixels;
        static float shotHeight;

        static void SetUpCapture()
        {
            shots = null;
            live = null;
            string dir = SessionState.GetString(K + "capture", "");
            bool watched = SessionState.GetInt(K + "live", 0) == 1;
            if (dir.Length == 0 && !watched) return;
            void Layer(Transform root, int layer)
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            }
            Layer(srcBody.avatar, 29);
            Layer(dstBody.avatar, 30);
            foreach (var c in chains)
            {
                if (c.src.press != null) Layer(c.src.press, 29);
                if (c.dst.press != null) Layer(c.dst.press, 30);
            }
            foreach (var c in chains)
            {
                if (c.src.press != null) Layer(c.src.press, 29);
                if (c.dst.press != null) Layer(c.dst.press, 30);
            }
            // Head height, not renderer bounds: one stray particle system or helper mesh makes the bounds huge.
            shotHeight = srcBody.head != null
                ? Mathf.Max(0.3f, Vector3.Dot(srcBody.head.position - srcBody.avatar.position, Vector3.up) * 1.1f)
                : 1.5f;
            if (!UnityEngine.Object.FindObjectsOfType<Light>().Any(l => l.isActiveAndEnabled && l.type == LightType.Directional))
            {
                var sun = new GameObject("PhysicsAb light").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
            Camera Make(int layer)
            {
                var cam = new GameObject("PhysicsAb camera").AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
                cam.cullingMask = 1 << layer;
                cam.fieldOfView = 35f;
                cam.nearClipPlane = 0.02f;
                return cam;
            }
            if (watched)
            {
                live = new Shot { src = Make(29), dst = Make(30) };
                foreach (var cam in new[] { live.src, live.dst })
                {
                    cam.enabled = true;
                    cam.depth = 50;
                    cam.fieldOfView = 40f;
                }
                live.src.rect = new Rect(0f, 0f, 0.5f, 1f);
                live.dst.rect = new Rect(0.5f, 0f, 0.5f, 1f);
                new GameObject("PhysicsAb captions").AddComponent<Captions>();
                Application.targetFrameRate = (int)Fps;
                QualitySettings.vSyncCount = 0;
            }
            if (dir.Length == 0) return;
            shots = SessionState.GetInt(K + "softShots", 0) == 1
                ? new List<Shot>
                {
                    // Close on the soft bodies, from the front, the side and low behind.
                    new Shot { name = "front", offset = new Vector3(0.25f, 0.05f, 0.55f), soft = true },
                    new Shot { name = "back", offset = new Vector3(0.6f, 0.02f, 0.05f), soft = true },
                    new Shot { name = "head", offset = new Vector3(-0.3f, 0.0f, -0.55f), soft = true },
                }
                : new List<Shot>
                {
                    new Shot { name = "front", offset = Angle("front") },
                    new Shot { name = "back", offset = Angle("back") },
                    new Shot { name = "head", offset = Angle("head"), head = true },
                };
            foreach (var s in shots)
            {
                s.src = Make(29);
                s.dst = Make(30);
                Directory.CreateDirectory(Path.Combine(dir, s.name));
            }
            shotTarget = new RenderTexture(ShotWidth, ShotHeight, 24);
            shotPixels = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
        }

        const int ShotWidth = 800, ShotHeight = 450;
        static Shot live;

        // Camera offsets in the avatar's starting frame, in avatar heights.
        static Vector3 Angle(string name)
        {
            switch (name)
            {
                // About 1.75 heights away: a 35 degree view then holds the whole avatar.
                case "front": return new Vector3(0.8f, 0.15f, 1.55f);
                case "back": return new Vector3(-0.8f, 0.3f, -1.5f);
                case "head": return new Vector3(0.5f, 0.12f, -0.65f);
                case "side": return new Vector3(1.7f, 0.15f, 0.1f);
                default: return new Vector3(1.3f, 1.1f, 0.5f);   // high, for lying down
            }
        }

        // The watched view picks the angle that shows each motion best.
        static (string name, bool head, string caption) Scene(float t)
        {
            if (t < 2f) return ("front", false, "Standing still");
            if (t < 6.5f) return ("front", false, "Walk forward, then stop");
            if (t < 9.5f) return ("back", false, "Turn 90 degrees");
            if (t < 12f) return ("head", true, "Head turn (body still)");
            if (t < 15f) return ("side", false, "Jump");
            if (t < 18.5f) return ("back", false, "Quick shake");
            if (t < 27f) return ("high", false, "Lie face down and hold (gravity)");
            return ("back", false, "A sphere presses into each chain");
        }

        static void Place(Camera cam, Body body, Vector3 offset, bool onHead)
        {
            var head = body.head != null ? body.head.position : body.avatar.position + body.avatar.up * shotHeight * 0.9f;
            var centre = onHead ? head : (body.avatar.position + head) * 0.5f;
            cam.transform.position = centre + body.rot0 * (offset * shotHeight);
            cam.transform.LookAt(centre, Vector3.up);
        }

        // Centred on the soft bodies' tips, this side's own, so both columns frame the same part.
        static void PlaceSoft(Camera cam, Body body, bool source, Vector3 offset)
        {
            var tips = chains.Where(c => SoftClasses.Contains(c.cls)).Select(c => (source ? c.src : c.dst).tip.position).ToList();
            var centre = tips.Count > 0 ? tips.Aggregate(Vector3.zero, (a, b) => a + b) / tips.Count : body.avatar.position + Vector3.up * shotHeight * 0.6f;
            cam.transform.position = centre + body.avatar.rotation * (offset * shotHeight);
            cam.transform.LookAt(centre, body.avatar.up);
        }

        static void Watch(float t)
        {
            if (live == null) return;
            var (name, head, caption) = Scene(t);
            Place(live.src, srcBody, Angle(name), head);
            Place(live.dst, dstBody, Angle(name), head);
            Captions.Line = caption;
        }

        // Editor-only and Play-mode-only, so a plain MonoBehaviour from this assembly is fine.
        sealed class Captions : MonoBehaviour
        {
            public static string Line = "";
            GUIStyle style;

            void OnGUI()
            {
                if (style == null)
                {
                    style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                    style.normal.textColor = Color.white;
                }
                style.fontSize = Mathf.Max(14, Screen.height / 28);
                float h = style.fontSize * 1.8f;
                void Box(Rect r, string text)
                {
                    GUI.color = new Color(0f, 0f, 0f, 0.55f);
                    GUI.DrawTexture(r, Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    GUI.Label(r, text, style);
                }
                Box(new Rect(10, 10, Screen.width / 2f - 20, h), "Original: VRChat PhysBones");
                Box(new Rect(Screen.width / 2f + 10, 10, Screen.width / 2f - 20, h), "Converted: MagicaCloth2");
                Box(new Rect(Screen.width * 0.2f, Screen.height - h - 14, Screen.width * 0.6f, h), Line);
            }
        }

        static void Film(int step)
        {
            if (shots == null || step % 3 != 0) return;
            string dir = SessionState.GetString(K + "capture", "");
            foreach (var s in shots)
            {
                foreach (var (cam, body, side) in new[] { (s.src, srcBody, "src"), (s.dst, dstBody, "dst") })
                {
                    if (s.soft) PlaceSoft(cam, body, side == "src", s.offset);
                    else Place(cam, body, s.offset, s.head);
                    cam.targetTexture = shotTarget;
                    cam.Render();
                    var was = RenderTexture.active;
                    RenderTexture.active = shotTarget;
                    shotPixels.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
                    shotPixels.Apply();
                    RenderTexture.active = was;
                    File.WriteAllBytes(Path.Combine(dir, s.name, $"{side}_{step / 3:00000}.png"), shotPixels.EncodeToPNG());
                }
            }
        }

        // Layered chains squash each other only if their bodies meet: the nearest pair of bodies
        // from two different chains, surface to surface, negative when they overlap.
        static float gap = float.MaxValue, gapTouch, restGap;
        static int gapAt = -1;
        static void BodyGap(int n)
        {
            if (n == 1) { gap = float.MaxValue; gapAt = -1; gapTouch = 0; }
            var spheres = UnityEngine.Object.FindObjectsOfType<SphereCollider>()
                .Where(x => x.transform.parent != null && x.transform.parent.name.EndsWith(" (lag bodies)")).ToList();
            float least = float.MaxValue;
            foreach (var a in spheres)
                foreach (var b in spheres)
                    if (a.transform.parent != b.transform.parent && a.GetInstanceID() < b.GetInstanceID())
                        least = Mathf.Min(least, Vector3.Distance(a.transform.position, b.transform.position) - a.radius - b.radius);
            if (least == float.MaxValue) return;
            if (n == 1) restGap = least;
            if (least <= 0f) gapTouch++;
            if (least < gap) { gap = least; gapAt = n; }
        }

        static void ReadStep()
        {
            if (!EditorApplication.isPlaying || chains == null || startFrame < 0) return;
            foreach (var c in chains)
            {
                c.src.v.Add(Vec(c.src, false)); c.src.m.Add(Vec(c.src, true));
                c.dst.v.Add(Vec(c.dst, false)); c.dst.m.Add(Vec(c.dst, true));
                if (c == chains[0]) BodyGap(c.dst.v.Count);
                // The root's own position: every other metric is root to tip, which a root that
                // moves as a whole leaves unchanged. A PhysBone never moves one; a Bone Spring does.
                c.src.r.Add(c.src.root.parent.InverseTransformPoint(c.src.root.position));
                c.dst.r.Add(c.dst.root.parent.InverseTransformPoint(c.dst.root.position));
            }
            foreach (var tr in traced)
            {
                // In each avatar's own frame, so the 3 m between them cancels.
                tr.sp.Add(srcBody.avatar.InverseTransformPoint(tr.s.position));
                tr.dp.Add(dstBody.avatar.InverseTransformPoint(tr.d.position));
            }
            FeedDent();
            Film(Time.frameCount - startFrame);
            foreach (var pr in pairs)
            {
                pr.srcMin = Mathf.Min(pr.srcMin, Vector3.Distance(pr.a.src.tip.position, pr.b.src.tip.position));
                pr.dstMin = Mathf.Min(pr.dstMin, Vector3.Distance(pr.a.dst.tip.position, pr.b.dst.tip.position));
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
            for (int i = 0; dentParts != null && i + 1 < dentParts.Count; i += 2)
                Debug.Log($"[PhysicsAb] pair {dentParts[i].name}/{dentParts[i + 1].name} deepest: {dentParts[i].deepestNote ?? "never pressed"}");
            if (flesh.Count >= 2)
                Debug.Log($"[PhysicsAb] flesh spheres sank {fleshSank * 100f:0.0} cm into each other at most");
            dentParts = null;
            flesh.Clear();
            fleshSank = 0f;
            SessionState.SetInt(K + "stage", 2);
            EditorApplication.ExitPlaymode();
        }

        // ------------------------------------------------------------------ metrics

        sealed class Metrics
        {
            public float sag, stretch, slide, squeeze, lie, liehold, pressRatio = float.NaN, pressAngle = float.NaN, pressRecover = float.NaN;
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
            var restRoot = Mean(s.r, 1.5f, 2f);
            m.slide = s.r.Max(x => (x - restRoot).magnitude) / Mathf.Max(1e-6f, rest.magnitude);
            // How far the tip itself moves while squeezed, root motion included, in chain lengths.
            var tipBefore = Mean(s.r, 31f, 31.5f) + Mean(s.v, 31f, 31.5f);
            for (int i = Ix(31.5f); i < n; i++)
                m.squeeze = Mathf.Max(m.squeeze, (s.r[i] + s.v[i] - tipBefore).magnitude / Mathf.Max(1e-6f, rest.magnitude));
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
            // Pressed: how much shorter it got, how far it bent out of the way, how fast it came back.
            if (s.restPress != null && s.restPress.Value.sqrMagnitude > 1e-12f)
            {
                var rp = s.restPress.Value;
                float ratio = 1f, angle = 0f;
                for (int i = Ix(27f); i < Ix(29.5f); i++)
                {
                    ratio = Mathf.Min(ratio, s.v[i].magnitude / rp.magnitude);
                    angle = Mathf.Max(angle, Vector3.Angle(s.v[i], rp));
                }
                int back = -1;
                for (int i = Ix(29.5f); i < n; i++)
                {
                    if (Mathf.Abs(s.v[i].magnitude / rp.magnitude - 1f) < 0.05f && Vector3.Angle(s.v[i], rp) < Mathf.Max(3f, 0.1f * angle)) { back = i; break; }
                }
                m.pressRatio = ratio;
                m.pressAngle = angle;
                m.pressRecover = back < 0 ? float.NaN : (back - Ix(29.5f)) / Fps;
            }
            return m;
        }

        static void Write()
        {
            string outDir = SessionState.GetString(K + "out", "");
            if (traced != null && traced.Count > 0)
            {
                // For each bone: how far apart the two sides sit at rest, the first time they are 1 cm
                // further apart than that, and the worst. Sorted by that first time: the top of the
                // list is where the copy starts to go wrong.
                var tsb = new StringBuilder("bone\trest_cm\tfirst_s\tmax_cm\tmax_at_s\n");
                foreach (var tr in traced.Select(tr =>
                {
                    int n = Mathf.Min(tr.sp.Count, tr.dp.Count);
                    var dev = Enumerable.Range(0, n).Select(i => (tr.sp[i] - tr.dp[i]).magnitude * 100f).ToList();
                    float rest = n > 0 ? dev[Mathf.Min(n - 1, Mathf.RoundToInt(1.5f * Fps))] : 0f;
                    int first = dev.FindIndex(v => v > rest + 1f);
                    int worst = n > 0 ? dev.IndexOf(dev.Max()) : 0;
                    return (tr.name, rest, first: first < 0 ? 999f : first / Fps, max: n > 0 ? dev.Max() : 0f, at: worst / Fps);
                }).OrderBy(x => x.first).ThenByDescending(x => x.max))
                {
                    tsb.Append($"{tr.name}\t{tr.rest:0.0}\t{tr.first:0.00}\t{tr.max:0.0}\t{tr.at:0.00}\n");
                }
                File.WriteAllText(Path.Combine(outDir, "trace.tsv"), tsb.ToString());
            }
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
                Row(c, "slide", a.slide, b.slide);
                Row(c, "squeeze.shift", a.squeeze, b.squeeze);
                Row(c, "lie", a.lie, b.lie);
                Row(c, "liehold", a.liehold, b.liehold);
                Row(c, "press.ratio", a.pressRatio, b.pressRatio);
                Row(c, "press.angle", a.pressAngle, b.pressAngle);
                Row(c, "press.recover", a.pressRecover, b.pressRecover);
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
            foreach (var pr in pairs)
            {
                var both = new Chain { name = pr.a.name + " + " + pr.b.name, cls = "pair " + pr.a.cls, pars = "" };
                Row(both, "pair.closest", pr.srcMin / Mathf.Max(1e-6f, pr.srcRest), pr.dstMin / Mathf.Max(1e-6f, pr.dstRest));
            }
            File.WriteAllText(Path.Combine(outDir, avatar + ".tsv"), rows.ToString());

            // Medians per class: the copy against the original, side by side.
            var sum = new StringBuilder($"{avatar}: {chains.Count} chain(s)\n");
            foreach (var cls in perClass.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                // Pair rows carry only their own metric, so count whatever the class has.
                sum.Append($"\n[{cls}] n={perClass[cls].Values.Max(l => l.Count)}\n");
                foreach (var metric in new[] { "sag", "lie", "liehold", "pair.closest", "press.ratio", "press.angle",
                             "walk.peak", "walk.settle", "walk.overshoot", "walk.midshare",
                             "turn.peak", "head.peak", "head.settle", "jump.peak", "shake.peak", "stretch", "slide", "squeeze.shift", "squeeze.peak" })
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
            if (gapAt >= 0)
                Debug.Log($"[PhysicsAb] layered bodies of two chains came within {gap * 100f:0.0} cm, surface to surface, at {gapAt / Fps:0.0} s" +
                          $"; touching {gapTouch / Fps:0.00} s in all; {restGap * 100f:0.0} cm apart at the start");
        }
    }
}
#endif
