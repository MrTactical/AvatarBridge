// Puts the bone-scale squash on a converted avatar for an in-game test: the
// build PhysicsAbProbe -abReader runs, made persistent, so it can be uploaded.
//
// Each soft pair (found by name: breast, boob, butt, booty, glute, one each
// side) gets a constraint stack per part that the part's skin moves through.
// Nothing is synced: a camera on one part's aim renders a quad on its partner
// into a 4x4 texture, the quad's shader turns the distance into both parts'
// squash, and a texture parser writes it into the stacks. Every client runs
// that on its own copy, against its own cloth. Measured at rest, in edit
// mode, from the skin the parts' bones carry.
//
// What the in-game test answers: does an avatar camera render the quad, does
// the parser read it, and do remote copies do both. The cube in front of the
// chest shows the texture: black at rest, toward yellow when squeezed. A cube
// that changes with no squash means the parser is the link that failed.
// Only ChilloutVR can say.
//
//   Menu: select the converted avatar, Tools/YAPS/Dev/Add the squash test (an
//         earlier test on it is stripped and rebuilt, no conversion needed)
//   Batch: -executeMethod AvatarBridge.Regression.SquashTestBuilder.Batch
//          -squashScene <scene> -squashOut <scene to save>
#if CVR_CCK_EXISTS && VRC_SDK_VRCSDK3 && UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Rendering;
using VRC.SDK3.Avatars.Components;
using Channel = ABI.CCK.Components.CVRTexturePropertyParserTask.Channel;

namespace AvatarBridge.Regression
{
    public static class SquashTestBuilder
    {
        const string RootName = "YAPS squash";
        const string Folder = "Assets/AvatarBridgeOutput/SquashTest";
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

        // The diagnostic build draws the quad for any 4x4 target and spends pixels on the reader's
        // checks, so a failed check shows on the readout instead of reading as rest.
        static bool Diagnostic;

        [MenuItem("Tools/YAPS/Dev/Add the squash test (diagnostic)")]
        static void DiagnosticMenu()
        {
            Diagnostic = true;
            try { Menu(); } finally { Diagnostic = false; }
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
#if AVATARBRIDGE_YAPS
                Debug.Log("[Squash] " + (Array.IndexOf(Environment.GetCommandLineArgs(), "-squashShader") >= 0 ? AddShader(avatar) : Add(avatar)));
#else
                Debug.Log("[Squash] " + Add(avatar));
#endif
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

        // An earlier test off again, so it can be rebuilt without converting: each renderer's skin
        // bones back to the bones they copy (their rotation constraint's source), then the stacks.
        public static void Strip(Transform root)
        {
            var old = root.Find(RootName);
            if (old == null) return;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = smr.bones;
                bool any = false;
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] == null || !bones[i].IsChildOf(old)) continue;
                    var copy = bones[i].GetComponent<RotationConstraint>();
                    bones[i] = copy != null && copy.sourceCount > 0 ? copy.GetSource(0).sourceTransform : null;
                    any = true;
                }
                if (any) smr.bones = bones;
            }
            UnityEngine.Object.DestroyImmediate(old.gameObject);
        }

        static bool Sided(Transform t) => Soft.IsMatch(t.name) && Side.IsMatch(t.name);

        // Skinned chain roots naming a side, paired by name with the side taken out. Skinned,
        // so a cloth or collider object named after its chain does not pair up as well; a side,
        // so a root both parts hang from is not taken for one of them.
        static List<List<Transform>> Pairs(Transform root, out string none)
        {
            var skinned = new HashSet<Transform>(root.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.bones).Where(t => t != null));
            var found = skinned
                .Where(t => Sided(t) && (t.parent == null || !Sided(t.parent)))
                .GroupBy(t => Side.Replace(t.name, "").ToLowerInvariant())
                .Where(g => g.Count() == 2)
                .Select(g => g.OrderBy(t => root.InverseTransformPoint(t.position).x).ToList())
                .ToList();
            none = found.Count > 0 ? null : "no soft pair found by name among " + string.Join(", ", skinned.Where(t => Soft.IsMatch(t.name)).Select(t => t.name + "<" + t.parent?.name));
            return found;
        }

        public static string Add(CVRAvatar avatar)
        {
            var root = avatar.transform;
            Strip(root);
#if AVATARBRIDGE_YAPS
            StripShader(root);
#endif
            var found = Pairs(root, out string none);
            if (none != null) return none;

            var squash = new GameObject(RootName).transform;
            squash.SetParent(root, false);
            var shader = ReaderShader();
            var quad = Asset(ReaderQuad(), "Squash quad.asset");
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
                string name = $"{avatar.name} squash {pair}";
                var texture = Asset(ReaderTexture(), name + ".renderTexture");
                var material = Asset(new Material(shader), name + ".mat");
                Reader(a.aim, b.follow, a.scale, b.scale, a.reach, b.reach, start, share, Flattest, Overfill, material, texture, quad);
                EditorUtility.SetDirty(material);
                Readout(root, squash, a, b, Asset(new Material(Shader.Find("Unlit/Texture")) { mainTexture = texture }, name + " readout.mat"));
                log.Add($"{a.root.name}/{b.root.name}: start {start * 100f:0.0} cm, reach {a.reach * 100f:0.0} + {b.reach * 100f:0.0}, share {share:0.00}");
                pair++;
            }
            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(avatar.gameObject);
            EditorSceneManager.MarkSceneDirty(avatar.gameObject.scene);
            return $"{pair} pair(s) on {avatar.name}, assets in {Folder}: " + string.Join("; ", log);
        }

        static T Asset<T>(T asset, string file) where T : UnityEngine.Object
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/AvatarBridgeOutput", "SquashTest");
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + file));
            return asset;
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

        // The squash with nothing synced. A camera on a's aim renders one quad on b's follow into a
        // 4x4 linear texture; the quad's shader turns the camera's distance into both parts' along
        // and across, and a texture parser writes them into the scale nodes. Every client runs it on
        // its own copy, so each viewer's squash follows the cloth that viewer sees. Columns 0 to 3 are
        // a's flattening, a's widening, b's flattening, b's widening, each 0..1 over its range, all
        // on g. The parser reads 8 bits, a step of 0.0016 in scale. Only g reads 0 from every no-data
        // case: the client's no-data colour is opaque black, which swelled b while b sat on alpha,
        // and the error shader's magenta has r and b full, which flattened a while flattening sat on r.
        public static readonly Color Rest = new Color(0f, 0f, 0f, 0f);
        // Checked by the shader, so only the reader camera draws the quad. The quad draws in the
        // last queue there is: anything else in the camera's box draws into the texture too, and
        // in game something drawn after it blacked out pixels.
        public const float ReaderNear = 0.00123f;

        public static CVRTexturePropertyParser Reader(Transform aimA, Transform followB, Transform scaleA, Transform scaleB,
            float reachA, float reachB, float start, float share, float flattest, float overfill,
            Material material, RenderTexture texture, Mesh quad)
        {
            float unit = followB.lossyScale.x;
            material.SetFloat("_Start", start / unit);
            material.SetFloat("_ReachA", reachA / unit);
            material.SetFloat("_ReachB", reachB / unit);
            material.SetFloat("_Share", share);
            material.SetFloat("_Flattest", flattest);
            material.SetFloat("_Overfill", overfill);
            material.SetFloat("_Fullest", Fullest);
            material.SetFloat("_Near", ReaderNear);
            material.SetFloat("_Diagnostic", Diagnostic ? 1f : 0f);

            var target = Child(followB, "squash quad").gameObject;
            target.AddComponent<MeshFilter>().sharedMesh = quad;
            var renderer = target.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
            // Transparent draws sort by order in layer before queue: in game a UI canvas crossing the
            // reader's box drew after the quad and swelled one side. Last in its layer, then.
            renderer.sortingOrder = short.MaxValue;

            var camera = Child(aimA, "squash camera").gameObject.AddComponent<Camera>();
            camera.orthographic = true;
            // Wide enough to keep b in view when the aim lags it by a frame, and no wider: whatever
            // else stands in the box is drawn into the texture too. Past the far plane b reads as
            // rest, which is right up to an avatar scaled 2.5 times; the planes do not scale with it.
            camera.orthographicSize = 0.25f * start;
            camera.nearClipPlane = ReaderNear;
            camera.farClipPlane = 2.5f * start;
            camera.clearFlags = CameraClearFlags.SolidColor;
            // Blue in the diagnostic: a frame the quad missed entirely, told apart from rest.
            camera.backgroundColor = Diagnostic ? Color.blue : Rest;
            // Vertex lit renders no shadow maps for it, and the body's lit passes skip it.
            camera.renderingPath = RenderingPath.VertexLit;
            camera.useOcclusionCulling = false;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.depth = 1f;
            // The client's player layers only, local and remote: no UI, world, props or mirrors in the box.
            camera.cullingMask = (1 << 8) | (1 << 10);
            camera.targetTexture = texture;

            var parser = camera.gameObject.AddComponent<CVRTexturePropertyParser>();
            parser.texture = texture;
            foreach (var (scale, x, reach) in new[]
                { (scaleA, 0, reachA), (scaleB, 2, reachB) })
            {
                parser.tasks.Add(Task(scale, x + 1, "localScale", 0, Channel.g, 1f, Fullest));
                parser.tasks.Add(Task(scale, x + 1, "localScale", 1, Channel.g, 1f, Fullest));
                parser.tasks.Add(Task(scale, x, "localScale", 2, Channel.g, 1f, flattest));
                // The facing side back by what it flattened, the far side staying.
                parser.tasks.Add(Task(scale, x, "localPosition", 2, Channel.g, 0f, -reach * (1f - flattest) / scale.parent.lossyScale.x));
            }
            return parser;
        }

        static CVRTexturePropertyParserTask Task(Transform target, int x, string property, int index, Channel channel, float min, float max) =>
            new CVRTexturePropertyParserTask
            {
                x = x, channel = channel, minValue = min, maxValue = max,
                target = target.gameObject, component = target,
                propertyName = property, typeIndex = 4, targetIndex = index,
            };

        // Linear: an sRGB target puts the gamma curve on the value.
        public static RenderTexture ReaderTexture() =>
            new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point, useMipMap = false };

        // Four corners the shader stretches over the whole target. Small bounds, so another
        // avatar's quad is culled unless it sits in this camera's view.
        public static Mesh ReaderQuad()
        {
            var mesh = new Mesh { name = "squash quad" };
            mesh.vertices = new Vector3[4];
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 0.01f);
            return mesh;
        }

        static Shader ReaderShader()
        {
            string path = Folder + "/YapsSquashReader.shader";
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/AvatarBridgeOutput", "SquashTest");
            if (!System.IO.File.Exists(path) || System.IO.File.ReadAllText(path) != ReaderSource)
            {
                System.IO.File.WriteAllText(path, ReaderSource);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            return AssetDatabase.LoadAssetAtPath<Shader>(path);
        }

        public const string ReaderSource = @"Shader ""YAPS/Dev/Squash Reader""
{
    Properties
    {
        _Start (""Start"", Float) = 0
        _ReachA (""Reach A"", Float) = 1
        _ReachB (""Reach B"", Float) = 1
        _Share (""Share"", Float) = 0.5
        _Flattest (""Flattest"", Float) = 0.6
        _Overfill (""Overfill"", Float) = 2.33
        _Fullest (""Fullest"", Float) = 1.5
        _Near (""Reader near plane"", Float) = 0
        _Diagnostic (""Diagnostic"", Float) = 0
    }
    SubShader
    {
        Tags { ""Queue""=""Overlay+1000"" ""IgnoreProjector""=""True"" ""DisableBatching""=""True"" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            float _Start, _ReachA, _ReachB, _Share, _Flattest, _Overfill, _Fullest, _Near, _Diagnostic;
            struct v2f
            {
                float4 pos : SV_POSITION;
                nointerpolation float4 value : TEXCOORD0;
                float2 uv : TEXCOORD1;
                nointerpolation float4 checks : TEXCOORD2;
                nointerpolation float4 raw : TEXCOORD3;
            };

            // Flattening and widening, each 0..1 over its range, 0 at rest.
            float2 Squash(float pushed, float reach)
            {
                float along = clamp(1 - pushed / (2 * reach), _Flattest, 1);
                float across = min(1 + _Overfill * (1 - along), _Fullest);
                return float2((1 - along) / (1 - _Flattest), (across - 1) / (_Fullest - 1));
            }

            v2f vert(float4 vertex : POSITION, float2 uv : TEXCOORD0)
            {
                v2f o;
                float3 to = unity_ObjectToWorld._m03_m13_m23 - _WorldSpaceCameraPos;
                float3 forward = -normalize(UNITY_MATRIX_V[2].xyz);
                // Only the reader camera: orthographic, its near plane, facing this quad within what
                // a frame of aim lag allows. A main camera, a mirror or another pair's draws nothing.
                bool ortho = unity_OrthoParams.w > 0.5;
                bool near = abs(_ProjectionParams.y - _Near) < 1e-6;
                bool facing = dot(to, forward) >= 0.9 * length(to);
                bool reader = ortho && near && facing;
                float dist = length(to) / length(unity_ObjectToWorld._m00_m10_m20);
                float press = clamp(_Start - dist, 0, _Start);
                o.value = reader ? float4(Squash(press * _Share, _ReachA), Squash(press * (1 - _Share), _ReachB)) : 0;
                bool draw = _Diagnostic > 0.5 ? all(_ScreenParams.xy == 4) : reader;
                o.pos = draw ? float4(uv * 2 - 1, 0.5, 1) : float4(2, 2, 2, 1);
                // Upright on the readout, the way a projected image would land.
                if (_Diagnostic > 0.5) o.pos.y *= _ProjectionParams.x;
                o.uv = uv;
                o.checks = float4(ortho, near, facing, reader);
                o.raw = float4(0, 0.5 * _ProjectionParams.y / _Near, dot(to, forward) / max(length(to), 1e-6), 0.5 * dist / max(_Start, 1e-6));
                return o;
            }

            // The squash fills the top and bottom rows, one value per column on g, so the parser's
            // pixels read it whichever way up the target is; blue in the diagnostic so a foreign write
            // shows. Diagnostic cells in the middle rows. Checks: orthographic, near plane, facing, all; green passes, red fails. Greys: a
            // cell blinking once a second (the camera is rendering), the near plane (mid grey when
            // exact), the facing cosine, the distance (mid grey at the start of the press).
            float4 frag(v2f i) : SV_Target
            {
                int2 cell = (int2)floor(i.uv * 4);
                if (_Diagnostic < 0.5 || cell.y == 0 || cell.y == 3) return float4(0, i.value[cell.x], _Diagnostic, 0);
                if (cell.y == 2) return i.checks[cell.x] > 0.5 ? float4(0, 1, 0, 1) : float4(1, 0, 0, 1);
                if (cell.x == 0) return frac(_Time.y) < 0.5 ? float4(1, 1, 1, 1) : float4(0, 0, 0, 1);
                return float4(saturate(i.raw[cell.x].xxx), 1);
            }
            ENDCG
        }
    }
}
";

        // The texture itself on a cube in front of the pair, readable from any side.
        static void Readout(Transform root, Transform squash, Part a, Part b, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.name = "squash readout";
            cube.SetParent(squash, false);
            cube.position = 0.5f * (a.centre + b.centre) + root.forward * 0.4f;
            cube.localScale = Vector3.one * (Diagnostic ? 0.1f : 0.04f) / squash.lossyScale.x;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
#if AVATARBRIDGE_YAPS

        // The squash in the YAPS-patched body shader instead: every layer over a part takes the
        // same move, worked out on the GPU from where the parts are. A material data provider
        // writes each part's follow (a plain child of its top carrying bone, at its centre) into
        // the materials every frame, on every client, with nothing synced and no camera.
        //
        // What the in-game test answers: does the provider run on remote copies, do the client's
        // materials take what it writes, and does the squash look right. A shader the patcher
        // refuses is reported and its layer left as it was.
        const string ShaderHostName = "YAPS squash (shader)", FollowName = "YAPS squash follow", RestName = "YAPS squash rest";

        [MenuItem("Tools/YAPS/Dev/Add the squash test (shader, diagnostic)")]
        static void ShaderDiagnosticMenu()
        {
            Diagnostic = true;
            try { ShaderMenu(); } finally { Diagnostic = false; }
        }

        [MenuItem("Tools/YAPS/Dev/Add the squash test (shader)")]
        static void ShaderMenu()
        {
            var avatar = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<CVRAvatar>() : null;
            Debug.Log(avatar == null ? "[Squash] select a converted avatar first" : "[Squash] " + AddShader(avatar));
        }

        public static string AddShader(CVRAvatar avatar)
        {
            var root = avatar.transform;
            Strip(root);
            StripShader(root);
            var found = Pairs(root, out string none);
            if (none != null) return none;

            var parts = new List<Part>();
            var rests = new List<Transform>();
            var log = new List<string>();
            var reach = Vector4.zero;
            var pair = new Vector4(0f, 0.5f, 0f, 0.5f);
            // The shader has room for two pairs.
            foreach (var bones in found.Take(2))
            {
                var measured = Measure(root, bones);
                if (measured == null) { log.Add($"{bones[0].name}/{bones[1].name}: too little skin"); continue; }
                var (a, b) = (measured[0], measured[1]);
                var axis = (b.centre - a.centre).normalized;
                a.reach = Reach(a, axis);
                b.reach = Reach(b, -axis);
                float start = Mathf.Min(Vector3.Distance(a.centre, b.centre), a.reach + b.reach);
                float share = a.volume + b.volume > 0f ? a.volume / (a.volume + b.volume) : 0.5f;
                int i = parts.Count;
                foreach (var p in measured)
                {
                    // On the bone the cloth moves most. The chain root is held still by the
                    // cloth, so a follow on it never saw the pair close in game.
                    var mover = p.carriers.Where(c => c.bone != p.root).OrderByDescending(c => c.weight).Select(c => c.bone).FirstOrDefault() ?? p.top;
                    p.follow = Child(mover, FollowName);
                    p.follow.position = p.centre;
                    // The same point on the bone above the chain, where the follow sits when
                    // nothing pushes the part.
                    var rest = Child(p.root.parent != null ? p.root.parent : p.root, RestName);
                    rest.position = p.centre;
                    rests.Add(rest);
                    reach[parts.Count] = p.reach / Unit(p.follow);
                    parts.Add(p);
                }
                pair[i] = start / Unit(a.follow);
                pair[i + 1] = share;
                log.Add($"{a.root.name}/{b.root.name} on {a.follow.parent.name}/{b.follow.parent.name}: start {start * 100f:0.0} cm, reach {a.reach * 100f:0.0} + {b.reach * 100f:0.0}, share {share:0.00}");
            }
            if (parts.Count == 0) return string.Join("; ", log);

            // Upper arm, lower arm, hand and middle finger per side, each capsule from one to the
            // next; its radius from the skin the bone carries, as a share of its length.
            var animator = root.GetComponent<Animator>();
            Transform Bone(params HumanBodyBones[] ways) => animator == null || !animator.isHuman ? null
                : ways.Select(animator.GetBoneTransform).FirstOrDefault(t => t != null);
            var arms = new[]
            {
                new[] { Bone(HumanBodyBones.LeftUpperArm), Bone(HumanBodyBones.LeftLowerArm), Bone(HumanBodyBones.LeftHand),
                        Bone(HumanBodyBones.LeftMiddleDistal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleProximal) },
                new[] { Bone(HumanBodyBones.RightUpperArm), Bone(HumanBodyBones.RightLowerArm), Bone(HumanBodyBones.RightHand),
                        Bone(HumanBodyBones.RightMiddleDistal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleProximal) },
            };
            float[] thickest = { 0.22f, 0.2f, 0.3f };
            var limbs = arms.Select(arm => new Vector4(
                Thickness(root, arm[0], arm[1], thickest[0]), Thickness(root, arm[1], arm[2], thickest[1]), Thickness(root, arm[2], arm[3], thickest[2]), 0f)).ToArray();
            log.Add($"arms {limbs[0].x:0.00}/{limbs[0].y:0.00}/{limbs[0].z:0.00} and {limbs[1].x:0.00}/{limbs[1].y:0.00}/{limbs[1].z:0.00} of their lengths" +
                    (arms.SelectMany(x => x).Any(t => t == null) ? ", some bones missing" : ""));

            var copies = new List<Material>();
            var patched = new Dictionary<Material, Shader>();
            foreach (var (smr, own) in Shares(root, parts.Select(p => p.root).ToList(), avatar.name))
            {
                var mats = smr.sharedMaterials;
                for (int slot = 0; slot < mats.Length; slot++)
                {
                    var source = mats[slot];
                    if (source == null) continue;
                    if (!patched.TryGetValue(source, out var shader))
                    {
                        patched[source] = shader = YapsShaderPatcher.Patch(source, Folder, null, out string refusal, out int shadows);
                        if (shader == null) log.Add($"left \"{source.name}\" as it was: {refusal}");
                        // Unity's own shadow caster cannot be patched, so that shadow stays unsquashed.
                        else if (shadows > 0) log.Add($"\"{source.name}\" casts its shadow unsquashed");
                    }
                    if (shader == null) continue;
                    var copy = new Material(source) { shader = shader };
                    copy.SetFloat("_YAPS_DentPower", 1f);
                    copy.SetTexture("_YAPS_DentOwn", own);
                    copy.SetVector("_YAPS_SquashReach", reach);
                    copy.SetVector("_YAPS_SquashPair", pair);
                    copy.SetVector("_YAPS_SquashShape", new Vector4(Flattest, Overfill, Fullest, 1f));
                    // Diagnostic: the pair squeezes and lets go every few seconds with nobody
                    // moving it, which only happens if the provider is writing the follows.
                    copy.SetFloat("_YAPS_SquashPulse", Diagnostic ? 0.3f : 0f);
                    copy.SetVector("_YAPS_SquashPush", new Vector4(1f, 0.03f, 0.25f, 1f));
                    copy.SetVector("_YAPS_SquashLimbL", limbs[0]);
                    copy.SetVector("_YAPS_SquashLimbR", limbs[1]);
                    Asset(copy, $"{avatar.name} {smr.name} {slot}.mat");
                    // Where it came from, for StripShader to put back.
                    AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(copy)).userData = GlobalObjectId.GetGlobalObjectIdSlow(source).ToString();
                    copies.Add(copy);
                    mats[slot] = copy;
                }
                smr.sharedMaterials = mats;
            }

            // Off the root: the client works out whose avatar it is from the parent up.
            var host = Child(root, ShaderHostName).gameObject;
            var provider = host.AddComponent<CVRMaterialDataProvider>();
            provider.materials = copies.ToArray();
            provider.dataMode = CVRMaterialDataProvider.DataMode.TransformLocalToWorld;
            // The slots yaps_dent.cginc reads; a node scaled to zero for each one with nothing in it.
            var empty = Child(host.transform, "empty");
            empty.localScale = Vector3.zero;
            var slots = Enumerable.Repeat(empty, 16).ToArray();
            for (int i = 0; i < parts.Count; i++) { slots[i] = parts[i].follow; slots[4 + i] = rests[i]; }
            for (int i = 0; i < 8; i++) slots[8 + i] = arms[i / 4][i % 4] ?? empty;
            provider.transforms = slots;
            provider.shaderPropertyName = "_YAPS_SquashFollow";

            // One vertex on each slot, skinned to it, so the slots come through skinning, after
            // the cloth and the IK. Its triangles have no area, so it draws nothing.
            var skin = Child(host.transform, "skin").gameObject.AddComponent<SkinnedMeshRenderer>();
            var skinBones = slots.Select(t => t == empty ? host.transform : t).ToArray();
            var mesh = new Mesh { name = "YAPS squash skin" };
            mesh.vertices = slots.Select(t => skin.transform.InverseTransformPoint(t.position)).ToArray();
            mesh.boneWeights = Enumerable.Range(0, 16).Select(i => new BoneWeight { boneIndex0 = i, weight0 = 1f }).ToArray();
            mesh.bindposes = skinBones.Select(b => b.worldToLocalMatrix * skin.transform.localToWorldMatrix).ToArray();
            mesh.triangles = Enumerable.Range(0, 48).Select(i => i / 3).ToArray();
            Asset(mesh, $"{avatar.name} squash skin.asset");
            var blank = new Material(Shader.Find("Standard")) { name = "YAPS squash skin" };
            Asset(blank, $"{avatar.name} squash skin.mat");
            skin.sharedMesh = mesh;
            skin.bones = skinBones;
            skin.sharedMaterial = blank;
            skin.updateWhenOffscreen = true;
            skin.shadowCastingMode = ShadowCastingMode.Off;
            skin.receiveShadows = false;
            int stride = mesh.GetVertexBufferStride(0);
            foreach (var copy in copies) copy.SetVector("_YAPS_SquashHands", new Vector4(1f, 0.05f, 1f, stride));
            // The client disposes the skinned buffer it fetched right after binding it, before
            // anything draws, and the shader then read zeros. The previous-buffer mode binds a
            // copy the provider owns, one frame older.
            var skinned = host.AddComponent<CVRMaterialDataProvider>();
            skinned.materials = copies.ToArray();
            skinned.dataMode = CVRMaterialDataProvider.DataMode.SkinnedMeshRendererPreviousVertexBuffer;
            skinned.smr = skin;
            skinned.shaderPropertyName = "_YAPS_SquashSkin";
            log.Add($"skin stride {stride}");

            var controller = host.AddComponent<CVRRenderController>();
            controller.cvrRenderComponents.Add(provider);
            controller.cvrRenderComponents.Add(skinned);

            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(avatar.gameObject);
            EditorSceneManager.MarkSceneDirty(avatar.gameObject.scene);
            return $"{parts.Count / 2} pair(s) through the shader on {avatar.name}, {copies.Count} material(s) in {Folder}: " + string.Join("; ", log);
        }

        static float Unit(Transform follow) => ((Vector3)follow.localToWorldMatrix.GetColumn(0)).magnitude;

        // The median distance from the line between two joints of the skin the first one
        // carries, as a share of that line's length. Joints left out, which bulge.
        static float Thickness(Transform root, Transform from, Transform to, float fallback)
        {
            if (from == null || to == null) return fallback;
            var a = from.position;
            var ab = to.position - a;
            if (ab.sqrMagnitude < 1e-10f) return fallback;
            var found = new List<float>();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                int bone = mesh == null ? -1 : Array.IndexOf(smr.bones, from);
                if (bone < 0 || bone >= mesh.bindposes.Length) continue;
                var skin = from.localToWorldMatrix * mesh.bindposes[bone];
                var weights = mesh.boneWeights;
                var verts = mesh.vertices;
                for (int v = 0; v < Mathf.Min(weights.Length, verts.Length); v++)
                {
                    var w = weights[v];
                    float on = (w.boneIndex0 == bone ? w.weight0 : 0f) + (w.boneIndex1 == bone ? w.weight1 : 0f)
                             + (w.boneIndex2 == bone ? w.weight2 : 0f) + (w.boneIndex3 == bone ? w.weight3 : 0f);
                    if (on < 0.5f) continue;
                    var at = skin.MultiplyPoint3x4(verts[v]);
                    float t = Vector3.Dot(at - a, ab) / ab.sqrMagnitude;
                    if (t > 0.15f && t < 0.85f) found.Add(Vector3.Distance(at, a + ab * t));
                }
            }
            if (found.Count < 20) return fallback;
            found.Sort();
            return found[found.Count / 2] / ab.magnitude;
        }

        // Each skinned renderer's share of every part per vertex, in r, g, b, a: the weight the
        // part's bones carry. A layer riding bones of its own takes the share of the nearest skin
        // vertex within 3 cm, or the skin would squash out from under it.
        static List<(SkinnedMeshRenderer smr, Texture2D own)> Shares(Transform root, List<Transform> parts, string name)
        {
            var sets = parts.Select(b => new HashSet<Transform>(b.GetComponentsInChildren<Transform>(true))).ToList();
            var meshes = new List<(SkinnedMeshRenderer smr, Vector3[] at, Vector4[] share, int owned)>();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var rb = smr.bones;
                var owner = rb.Select(t => t == null ? -1 : sets.FindIndex(s => s.Contains(t))).ToArray();
                var weights = mesh.boneWeights;
                var verts = mesh.vertices;
                var bind = mesh.bindposes;
                var skin = rb.Select((t, i) => t != null && i < bind.Length ? t.localToWorldMatrix * bind[i] : Matrix4x4.zero).ToArray();
                int count = Mathf.Min(weights.Length, verts.Length);
                var at = new Vector3[count];
                var share = new Vector4[count];
                int owned = 0;
                for (int v = 0; v < count; v++)
                {
                    var w = weights[v];
                    int[] index = { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
                    float[] weight = { w.weight0, w.weight1, w.weight2, w.weight3 };
                    for (int q = 0; q < 4; q++)
                    {
                        at[v] += skin[index[q]].MultiplyPoint3x4(verts[v]) * weight[q];
                        if (weight[q] > 0f && owner[index[q]] >= 0) share[v][owner[index[q]]] += weight[q];
                    }
                    if (Mathf.Max(share[v].x, share[v].y, share[v].z, share[v].w) >= 0.5f) owned++;
                }
                meshes.Add((smr, at, share, owned));
            }

            // The skin is the renderer carrying the most of the parts, found through a 2 cm grid.
            var body = meshes.OrderByDescending(m => m.owned).FirstOrDefault();
            if (body.smr == null || body.owned == 0) return new List<(SkinnedMeshRenderer, Texture2D)>();
            const float Cell = 0.02f, Near = 0.03f;
            var grid = new Dictionary<Vector3Int, List<int>>();
            Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell), Mathf.FloorToInt(p.z / Cell));
            for (int v = 0; v < body.at.Length; v++)
            {
                if (body.share[v] == Vector4.zero) continue;
                var k = Key(body.at[v]);
                if (!grid.TryGetValue(k, out var list)) grid[k] = list = new List<int>();
                list.Add(v);
            }
            var result = new List<(SkinnedMeshRenderer, Texture2D)>();
            foreach (var m in meshes)
            {
                if (m.smr != body.smr)
                    for (int v = 0; v < m.at.Length; v++)
                    {
                        var k = Key(m.at[v]);
                        int best = -1;
                        float bestD = Near * Near;
                        for (int x = -2; x <= 2; x++)
                            for (int y = -2; y <= 2; y++)
                                for (int z = -2; z <= 2; z++)
                                {
                                    if (!grid.TryGetValue(k + new Vector3Int(x, y, z), out var list)) continue;
                                    foreach (int s in list)
                                    {
                                        float d = (body.at[s] - m.at[v]).sqrMagnitude;
                                        if (d < bestD) { bestD = d; best = s; }
                                    }
                                }
                        if (best >= 0) m.share[v] = body.share[best];
                    }
                if (m.share.All(x => x == Vector4.zero)) continue;
                int width = 1024, height = Mathf.Max(1, (m.at.Length + width - 1) / width);
                var pixels = new Color32[width * height];
                for (int v = 0; v < m.at.Length; v++)
                {
                    var s = m.share[v];
                    pixels[v] = new Color32((byte)(s.x * 255f + 0.5f), (byte)(s.y * 255f + 0.5f), (byte)(s.z * 255f + 0.5f), (byte)(s.w * 255f + 0.5f));
                }
                // Linear and point-sampled: a share is a number, not a colour.
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                tex.SetPixels32(pixels);
                tex.Apply(false);
                Asset(tex, $"{name} {m.smr.name} shares.asset");
                // What the CCK's upload checks ask of an avatar texture. The asset keeps its
                // pixels either way; readable only adds a CPU copy, and with no mips nothing streams.
                var flags = new SerializedObject(tex);
                flags.FindProperty("m_IsReadable").boolValue = false;
                flags.FindProperty("m_StreamingMipmaps").boolValue = true;
                flags.ApplyModifiedPropertiesWithoutUndo();
                result.Add((m.smr, tex));
            }
            return result;
        }

        // An earlier shader build off again: each layer back on the material it was copied from.
        public static void StripShader(Transform root)
        {
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = smr.sharedMaterials;
                bool any = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    string path = AssetDatabase.GetAssetPath(mats[i]);
                    if (mats[i] == null || !path.StartsWith(Folder + "/")) continue;
                    if (!GlobalObjectId.TryParse(AssetImporter.GetAtPath(path)?.userData, out var id)) continue;
                    var source = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Material;
                    if (source == null) continue;
                    mats[i] = source;
                    any = true;
                }
                if (any) smr.sharedMaterials = mats;
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == FollowName || t.name == RestName || t.name == ShaderHostName).ToList())
                if (t != null) UnityEngine.Object.DestroyImmediate(t.gameObject);
        }
#endif
    }
}
#endif
