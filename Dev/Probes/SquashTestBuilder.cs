// Puts the bone-scale squash on a converted avatar for an in-game test: the
// build PhysicsAbProbe -abDriver runs, made persistent, so it can be uploaded.
//
// Each soft pair (found by name: breast, boob, butt, booty, glute, one each
// side) gets a constraint stack per part that the part's skin moves through,
// a box trigger on one part's aim reading a pointer on its partner, and one
// synced float per pair driving a blend tree layer that sets both scales.
// Measured at rest, in edit mode, from the skin the parts' bones carry.
//
// What the in-game test answers: does the trigger fire on the wearer's own
// pointer, does SetFromPosition read as the decompile says, and does the
// float reach remote copies. Only ChilloutVR can say.
//
//   Menu: select the converted avatar, Tools/YAPS/Dev/Add the squash test
//   Batch: -executeMethod AvatarBridge.Regression.SquashTestBuilder.Batch
//          -squashScene <scene> -squashOut <scene to save>
#if CVR_CCK_EXISTS && VRC_SDK_VRCSDK3 && UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Regression
{
    public static class SquashTestBuilder
    {
        const string RootName = "YAPS squash";
        const string LayerPrefix = "YAPS squash ";
        const float Overfill = 2.33f, Flattest = 0.6f, Fullest = 1.5f;
        static readonly Regex Soft = new Regex("breast|boob|butt|booty|glute", RegexOptions.IgnoreCase);
        static readonly Regex Side = new Regex(@"left|right|[._ ]?[lr](?=$|[._ ])", RegexOptions.IgnoreCase);

        sealed class Part
        {
            public Transform root, top, follow, aim, scale, counter;
            public List<(Transform bone, float weight)> carriers;
            public List<Vector3> points;
            public Vector3 centre;
            public float reach, volume;
        }

        [MenuItem("Tools/YAPS/Dev/Add the squash test")]
        static void Menu()
        {
            var avatar = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<CVRAvatar>() : null;
            Debug.Log(avatar == null ? "[Squash] select a converted avatar first" : "[Squash] " + Add(avatar));
        }

        public static void Batch()
        {
            int code = 1;
            try
            {
                var scene = EditorSceneManager.OpenScene(Arg("-squashScene"), OpenSceneMode.Single);
                // A conversion saved in the scene would be a second avatar to pick from. Gone in memory only.
                foreach (var old in scene.GetRootGameObjects().Where(r => r.name.EndsWith(" (ChilloutVR)")))
                    UnityEngine.Object.DestroyImmediate(old);
                var descriptor = scene.GetRootGameObjects().Select(r => r.GetComponentInChildren<VRCAvatarDescriptor>(true)).FirstOrDefault(d => d != null);
                var report = BridgeConverter.Convert(descriptor, new BridgeSettings { cloneAvatar = true });
                var avatar = report.ConvertedRoot != null ? report.ConvertedRoot.GetComponent<CVRAvatar>() : null;
                if (avatar == null) throw new Exception("conversion made nothing");
                Debug.Log("[Squash] " + Add(avatar));
                EditorSceneManager.SaveScene(scene, Arg("-squashOut"), true);
                AssetDatabase.SaveAssets();
                Debug.Log("[Squash] saved " + Arg("-squashOut"));
                code = 0;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            EditorApplication.Exit(code);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static bool Sided(Transform t) => Soft.IsMatch(t.name) && Side.IsMatch(t.name);

        public static string Add(CVRAvatar avatar)
        {
            var root = avatar.transform;
            if (root.Find(RootName) != null) return "already has the squash; convert again first";
            var controllers = YapsOwner.Targets(avatar);
            if (controllers.Count == 0) return "no controller to add the layer to";

            // Skinned chain roots naming a side, paired by name with the side taken out. Skinned,
            // so a cloth or collider object named after its chain does not pair up as well; a side,
            // so a root both parts hang from is not taken for one of them.
            var skinned = new HashSet<Transform>(root.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.bones).Where(t => t != null));
            var found = skinned
                .Where(t => Sided(t) && (t.parent == null || !Sided(t.parent)))
                .GroupBy(t => Side.Replace(t.name, "").ToLowerInvariant())
                .Where(g => g.Count() == 2)
                .Select(g => g.OrderBy(t => root.InverseTransformPoint(t.position).x).ToList())
                .ToList();
            if (found.Count == 0) return "no soft pair found by name among " + string.Join(", ", skinned.Where(t => Soft.IsMatch(t.name)).Select(t => t.name + "<" + t.parent?.name));

            var squash = new GameObject(RootName).transform;
            squash.SetParent(root, false);
            var log = new List<string>();
            int pair = 0;
            foreach (var bones in found)
            {
                var parts = Measure(root, bones);
                if (parts == null) { log.Add($"{bones[0].name}/{bones[1].name}: too little skin"); continue; }
                var (a, b) = (parts[0], parts[1]);
                var axis = (b.centre - a.centre).normalized;
                a.reach = Reach(a, axis);
                b.reach = Reach(b, -axis);
                float gap = Vector3.Distance(a.centre, b.centre);
                float start = Mathf.Min(gap, a.reach + b.reach);
                float share = a.volume + b.volume > 0f ? a.volume / (a.volume + b.volume) : 0.5f;
                foreach (var p in parts) Stack(root, squash, p);
                Aim(root, a, b);
                Aim(root, b, a);
                string param = "YAPS/Squash" + pair;
                Driver(a, b, param, start);
                var keys = Keys(a, b, share, start);
                var meter = Meter(root, squash, a, b, param);
                foreach (var c in controllers) Layer(root, c, a, b, share, start, param, keys, meter);
                Slider(avatar, param, "Squash test " + pair);
                log.Add($"{a.root.name}/{b.root.name}: box {start * 100f:0.0} cm, reach {a.reach * 100f:0.0} + {b.reach * 100f:0.0}, " +
                        $"share {share:0.00}, {keys.Count} keys, {param}");
                pair++;
            }
            EditorUtility.SetDirty(avatar.gameObject);
            EditorSceneManager.MarkSceneDirty(avatar.gameObject.scene);
            return $"{pair} pair(s) on {avatar.name}, layer in {string.Join(", ", controllers.Select(AssetDatabase.GetAssetPath))}: " + string.Join("; ", log);
        }

        // The skin each part's bones carry, posed as it stands: points from the renderer
        // carrying the most of both, weights from every renderer.
        static List<Part> Measure(Transform root, List<Transform> bones)
        {
            var sets = bones.Select(b => new HashSet<Transform>(b.GetComponentsInChildren<Transform>(true))).ToList();
            var carried = bones.Select(_ => new Dictionary<Transform, float>()).ToList();
            List<Vector3>[] best = null;
            int bestCount = 0;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var rb = smr.bones;
                var owner = rb.Select(t => t == null ? -1 : sets.FindIndex(s => s.Contains(t))).ToArray();
                if (owner.All(x => x < 0)) continue;
                var weights = mesh.boneWeights;
                var verts = mesh.vertices;
                var bind = mesh.bindposes;
                var skin = rb.Select((t, i) => t != null && i < bind.Length ? t.localToWorldMatrix * bind[i] : Matrix4x4.zero).ToArray();
                var points = new[] { new List<Vector3>(), new List<Vector3>() };
                for (int v = 0; v < Mathf.Min(weights.Length, verts.Length); v++)
                {
                    var w = weights[v];
                    int[] index = { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
                    float[] weight = { w.weight0, w.weight1, w.weight2, w.weight3 };
                    var at = Vector3.zero;
                    var share = new float[2];
                    for (int q = 0; q < 4; q++)
                    {
                        at += skin[index[q]].MultiplyPoint3x4(verts[v]) * weight[q];
                        int part = owner[index[q]];
                        if (weight[q] <= 0f || part < 0) continue;
                        share[part] += weight[q];
                        carried[part][rb[index[q]]] = (carried[part].TryGetValue(rb[index[q]], out var t) ? t : 0f) + weight[q];
                    }
                    for (int part = 0; part < 2; part++) if (share[part] >= 0.5f) points[part].Add(at);
                }
                int count = points[0].Count + points[1].Count;
                if (count > bestCount) { bestCount = count; best = points; }
            }
            if (best == null || best.Any(p => p.Count < 20)) return null;

            int Depth(Transform t) { int d = 0; for (; t != null; t = t.parent) d++; return d; }
            return Enumerable.Range(0, 2).Select(i =>
            {
                float total = carried[i].Values.Sum();
                var p = new Part
                {
                    root = bones[i],
                    points = best[i],
                    centre = best[i].Aggregate(Vector3.zero, (s, x) => s + x) / best[i].Count,
                    carriers = carried[i].Select(kv => (kv.Key, kv.Value / total)).Where(x => x.Item2 >= 0.03f)
                        .OrderBy(x => Depth(x.Key)).ToList(),
                };
                p.top = p.carriers[0].bone;
                p.volume = Mathf.Sqrt(Mathf.Max(Det(p.points, p.centre), 0f));
                return p;
            }).ToList();
        }

        static float Det(List<Vector3> points, Vector3 centre)
        {
            var c = new float[3, 3];
            foreach (var x in points)
            {
                var d = x - centre;
                for (int r = 0; r < 3; r++) for (int k = 0; k < 3; k++) c[r, k] += d[r] * d[k] / points.Count;
            }
            return c[0, 0] * (c[1, 1] * c[2, 2] - c[1, 2] * c[2, 1]) - c[0, 1] * (c[1, 0] * c[2, 2] - c[1, 2] * c[2, 0])
                 + c[0, 2] * (c[1, 0] * c[2, 1] - c[1, 1] * c[2, 0]);
        }

        // The 98th percentile, so one stray vertex does not set it.
        static float Reach(Part p, Vector3 way)
        {
            var along = p.points.Select(x => Vector3.Dot(x - p.centre, way)).OrderBy(x => x).ToList();
            return Mathf.Max(along[(int)(0.98f * (along.Count - 1))], 1e-4f);
        }

        static T Constrain<T>(Transform on, Transform source) where T : Behaviour, IConstraint
        {
            var c = on.gameObject.AddComponent<T>();
            c.AddSource(new ConstraintSource { sourceTransform = source, weight = 1f });
            c.locked = true;
            c.constraintActive = true;
            return c;
        }

        static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        // follow (on the top carrying bone, at the part's centre) > aim > scale > counter > a skin
        // bone per carrying bone, in the bones' own hierarchy. Identity at rest, so the bind pose
        // holds; the part's skin moves onto the skin bones in every renderer.
        static void Stack(Transform root, Transform squash, Part p)
        {
            p.follow = Child(squash, p.root.name);
            p.follow.SetPositionAndRotation(p.centre, p.top.rotation);
            var parent = Constrain<ParentConstraint>(p.follow, p.top);
            parent.SetTranslationOffset(0, Vector3.Scale(p.top.InverseTransformPoint(p.centre), p.top.lossyScale));
            parent.SetRotationOffset(0, Vector3.zero);
            p.aim = Child(p.follow, "aim");
            p.scale = Child(p.aim, "scale");
            p.counter = Child(p.scale, "counter");
            Constrain<RotationConstraint>(p.counter, p.follow);
            var skins = new List<(Transform bone, Transform skin)>();
            foreach (var (bone, _) in p.carriers)
            {
                var under = skins.Where(x => bone.IsChildOf(x.bone)).Select(x => x.skin).LastOrDefault() ?? p.counter;
                var skin = Child(under, bone.name + " skin");
                skin.SetPositionAndRotation(bone.position, bone.rotation);
                skin.localScale = bone.lossyScale / under.lossyScale.x;
                Constrain<RotationConstraint>(skin, bone);
                // Below the top a joint stretches; the skin keeps it at the cost of the squash on its own origin.
                if (under != p.counter) Constrain<PositionConstraint>(skin, bone);
                skins.Add((bone, skin));
            }
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = smr.bones;
                bool any = false;
                foreach (var (bone, skin) in skins)
                {
                    int at = Array.IndexOf(bones, bone);
                    if (at >= 0) { bones[at] = skin; any = true; }
                }
                if (any) smr.bones = bones;
            }
        }

        static void Aim(Transform root, Part p, Part partner)
        {
            var aim = Constrain<AimConstraint>(p.aim, partner.follow);
            aim.aimVector = Vector3.forward;
            aim.upVector = Vector3.up;
            aim.worldUpType = AimConstraint.WorldUpType.ObjectRotationUp;
            aim.worldUpObject = root;
            aim.worldUpVector = Vector3.up;
        }

        // A box on a's aim, its z down the line to b: 0 where the surfaces meet at rest, 1 with
        // the centres together. Sampled on -Z; the exit task writes 0, since a stay task writes
        // only while the pointer is inside.
        static void Driver(Part a, Part b, string param, float start)
        {
            var pointer = Child(b.follow, "squash pointer").gameObject.AddComponent<CVRPointer>();
            pointer.type = param;
            var box = a.aim.gameObject.AddComponent<CVRAdvancedAvatarSettingsTrigger>();
            float unit = a.aim.lossyScale.x;
            box.areaSize = Vector3.one * (start / unit);
            box.areaOffset = new Vector3(0f, 0f, 0.5f * start / unit);
            box.useAdvancedTrigger = true;
            box.isLocalInteractable = true;
            box.isNetworkInteractable = false;
            box.allowedPointer.Add(pointer);
            box.allowedTypes = new[] { param };
            box.sampleDirection = CVRAdvancedAvatarSettingsTrigger.SampleDirection.ZNegative;
            box.stayTasks.Add(new CVRAdvancedAvatarSettingsTriggerTaskStay
                { settingName = param, minValue = 0f, maxValue = 1f, updateMethod = CVRAdvancedAvatarSettingsTriggerTaskStay.UpdateMethod.SetFromPosition });
            box.exitTasks.Add(new CVRAdvancedAvatarSettingsTriggerTask
                { settingName = param, settingValue = 0f, updateMethod = CVRAdvancedAvatarSettingsTriggerTask.UpdateMethod.Override });
        }

        static (Vector3 scale, float back) Squashed(Part p, float pushed)
        {
            float along = Mathf.Clamp(1f - pushed / (2f * p.reach), Flattest, 1f);
            float across = Mathf.Min(1f + Overfill * (1f - along), Fullest);
            return (new Vector3(across, across, along), -p.reach * (1f - along) / p.follow.lossyScale.x);
        }

        // Keyed at 0, 1 and wherever either part stops flattening or growing, so a 1D blend
        // tree reproduces the squash exactly.
        static List<float> Keys(Part a, Part b, float share, float start)
        {
            var keys = new List<float> { 0f, 1f };
            foreach (var (p, s) in new[] { (a, share), (b, 1f - share) })
                foreach (float bend in new[] { 1f - Flattest, (Fullest - 1f) / Overfill })
                {
                    float v = bend * 2f * p.reach / (start * s);
                    if (v > 0f && v < 1f) keys.Add(v);
                }
            return keys.Distinct().OrderBy(v => v).ToList();
        }

        // Built after the controller is an asset, so its parts embed in it or serialise as nothing.
        // Instruments, since only the game can say which link fails. A bar in front of the pair
        // that grows with the synced value: grows when squeezed, the trigger works. A menu slider
        // on the same value: squashes when dragged, the animator and stack work. The trigger
        // writes only while the pointer is inside, so at rest the slider is not overwritten.
        const float MeterTall = 0.3f;
        static Transform Meter(Transform root, Transform squash, Part a, Part b, string param)
        {
            var meter = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            UnityEngine.Object.DestroyImmediate(meter.GetComponent<Collider>());
            meter.name = param.Replace('/', ' ') + " meter";
            meter.SetParent(squash, false);
            meter.position = 0.5f * (a.centre + b.centre) + root.forward * 0.4f;
            meter.localScale = new Vector3(0.03f, 0.005f, 0.03f) / squash.lossyScale.x;
            return meter;
        }

        static void Slider(CVRAvatar avatar, string param, string label)
        {
            var settings = avatar.avatarSettings?.settings;
            if (settings == null || settings.Any(x => x != null && x.machineName == param)) return;
            settings.Insert(0, new CVRAdvancedSettingsEntry
            {
                name = label,
                machineName = param,
                unlinkNameFromMachineName = true,
                type = CVRAdvancedSettingsEntry.SettingsType.Slider,
                setting = new CVRAdvancesAvatarSettingSlider { defaultValue = 0f, usedType = CVRAdvancesAvatarSettingBase.ParameterType.Float },
            });
        }

        static void Layer(Transform root, AnimatorController controller, Part a, Part b, float share, float start, string param, List<float> keys, Transform meter)
        {
            string name = LayerPrefix + param;
            var tree = new BlendTree
            {
                name = name, blendParameter = param, blendType = BlendTreeType.Simple1D,
                useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            foreach (float v in keys)
            {
                var clip = new AnimationClip { name = $"{name} {v:0.000}", hideFlags = HideFlags.HideInHierarchy };
                foreach (var (p, s) in new[] { (a, share), (b, 1f - share) })
                {
                    var (scale, back) = Squashed(p, v * start * s);
                    string path = AnimationUtility.CalculateTransformPath(p.scale, root);
                    void Key(string prop, float x) =>
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), prop), AnimationCurve.Constant(0f, 1f / 60f, x));
                    Key("m_LocalScale.x", scale.x);
                    Key("m_LocalScale.y", scale.y);
                    Key("m_LocalScale.z", scale.z);
                    Key("m_LocalPosition.z", back);
                }
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(meter, root), typeof(Transform), "m_LocalScale.y"),
                    AnimationCurve.Constant(0f, 1f / 60f, (0.005f + MeterTall * v) / meter.parent.lossyScale.x));
                AssetDatabase.AddObjectToAsset(clip, controller);
                tree.AddChild(clip, v);
            }
            var machine = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(machine, controller);
            var state = machine.AddState(name);
            state.motion = tree;
            state.writeDefaultValues = true;
            machine.defaultState = state;
            if (controller.parameters.All(x => x.name != param)) controller.AddParameter(param, AnimatorControllerParameterType.Float);
            var layers = controller.layers.ToList();
            layers.Add(new AnimatorControllerLayer { name = name, defaultWeight = 1f, stateMachine = machine });
            controller.layers = layers.ToArray();
            EditorUtility.SetDirty(controller);
        }
    }
}
#endif
