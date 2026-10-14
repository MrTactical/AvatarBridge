// The squash in the YAPS-patched body shader: every layer over a soft part takes the same move,
// worked out on the GPU from where the parts are. A material data provider writes each part's
// follow (a plain child of the bone the cloth moves most, at the part's centre) into the
// materials every frame, on every client, with nothing synced and no camera.
//
// Parts are found by bone name (breast, boob, butt, booty, glute, one each side), at most two
// pairs, and each vertex's share is the skin weight its part's bones carry. Measured at rest,
// in edit mode. A shader the patcher refuses is reported and its layer left as it was.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarBridge
{
    public static class YapsSquash
    {
        const string ShaderHostName = "YAPS squash (shader)", FollowName = "YAPS squash follow", RestName = "YAPS squash rest", BackName = "YAPS squash back";
        // On each copy's importer, before the material it was copied from, for Strip to put back.
        const string Mark = "squash:";
        const float Flattest = 0.6f, Fullest = 1.5f;
        static readonly Regex Soft = new Regex("breast|boob|butt|booty|glute", RegexOptions.IgnoreCase);
        static readonly Regex Side = new Regex(@"left|right|[._ ]?[lr](?=$|[._ ])", RegexOptions.IgnoreCase);

        sealed class Part
        {
            public Transform root, top, follow;
            public List<(Transform bone, float weight)> carriers;
            public List<Vector3> points;
            public Vector3 centre;
            public float reach, volume;
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

        public static string Add(CVRAvatar avatar, string folder, BridgeReport report = null, float pulse = 0f)
        {
            var root = avatar.transform;
            Strip(root);
            var found = Pairs(root, out string none);
            if (none != null) return none;

            var parts = new List<Part>();
            var rests = new List<Transform>();
            var backs = new List<Transform>();
            var log = new List<string>();
            var reach = Vector4.zero;
            // The other five reaches of each part, in the frame the shader builds: away from the
            // partner, out of the body, into it, and either way along cross(toward, out).
            var away = Vector4.zero;
            var outward = Vector4.zero;
            var inward = Vector4.zero;
            var sideA = Vector4.zero;
            var sideB = Vector4.zero;
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
                // Out of the body, level: from the bone above the pair to the pair's middle,
                // with the up and down and the line between them taken out. The shader reads
                // it as the pair's middle minus a node this far behind it on that bone.
                var restBone = a.root.parent != null ? a.root.parent : a.root;
                var mid = 0.5f * (a.centre + b.centre);
                var level = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(mid - restBone.position, Vector3.up), axis);
                if (level.sqrMagnitude < 1e-6f) level = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(root.forward, Vector3.up), axis);
                var o = level.normalized;
                var back = Child(restBone, BackName);
                back.position = mid - o * a.reach;
                backs.Add(back);
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
                    float unit = Unit(p.follow);
                    var toward = p == a ? axis : -axis;
                    var u = Vector3.Cross(toward, o);
                    reach[parts.Count] = p.reach / unit;
                    away[parts.Count] = Reach(p, -toward) / unit;
                    outward[parts.Count] = Reach(p, o) / unit;
                    inward[parts.Count] = Reach(p, -o) / unit;
                    sideA[parts.Count] = Reach(p, u) / unit;
                    sideB[parts.Count] = Reach(p, -u) / unit;
                    parts.Add(p);
                }
                pair[i] = start / Unit(a.follow);
                pair[i + 1] = share;
                log.Add($"{a.root.name}/{b.root.name} on {a.follow.parent.name}/{b.follow.parent.name}: start {start * 100f:0.0} cm, reach {a.reach * 100f:0.0} + {b.reach * 100f:0.0}, " +
                        $"out {outward[i] * Unit(a.follow) * 100f:0.0}, in {inward[i] * Unit(a.follow) * 100f:0.0}, share {share:0.00}");
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
            var still = new HashSet<int>();
            foreach (var (smr, own, shares) in Shares(root, parts.Select(p => p.root).ToList(), avatar.name, folder))
            {
                var mats = smr.sharedMaterials;
                for (int slot = 0; slot < mats.Length; slot++)
                {
                    var source = mats[slot];
                    if (source == null) continue;
                    if (!patched.TryGetValue(source, out var shader))
                    {
                        patched[source] = shader = YapsShaderPatcher.Patch(source, folder, report, out string refusal, out int shadows);
                        if (shader == null) log.Add($"left \"{source.name}\" as it was: {refusal}");
                        // Unity's standard caster is wrapped; a caster from anywhere else cannot be reached.
                        else if (shadows > 0) log.Add($"\"{source.name}\" casts its shadow unsquashed");
                    }
                    if (shader == null)
                    {
                        // A layer that cannot move over a part that does: the skin would come
                        // through it, so the pair under it holds still instead.
                        var sm = smr.sharedMesh;
                        int sub = Mathf.Min(slot, sm.subMeshCount - 1);
                        foreach (int v in sm.GetIndices(sub))
                            for (int m = 0; m < 4; m++)
                                if (v < shares.Length && shares[v][m] >= 0.05f && still.Add(m & ~1))
                                    log.Add($"\"{source.name}\" on {smr.name} cannot follow, so {parts[m & ~1].root.name}/{parts[m | 1].root.name} does not squash");
                        continue;
                    }
                    var copy = new Material(source) { shader = shader };
                    copy.SetFloat("_YAPS_DentPower", 1f);
                    copy.SetTexture("_YAPS_DentOwn", own);
                    copy.SetVector("_YAPS_SquashReach", reach);
                    copy.SetVector("_YAPS_SquashAway", away);
                    copy.SetVector("_YAPS_SquashOut", outward);
                    copy.SetVector("_YAPS_SquashIn", inward);
                    copy.SetVector("_YAPS_SquashSideA", sideA);
                    copy.SetVector("_YAPS_SquashSideB", sideB);
                    // Lying-back weight, strength, bend give, deepest dent.
                    copy.SetVector("_YAPS_SquashTune", new Vector4(0.25f, 1f, 0.35f, 0.35f));
                    copy.SetVector("_YAPS_SquashPair", pair);
                    // Volume kept whole: the overfill made a squashed part a third bigger.
                    copy.SetVector("_YAPS_SquashShape", new Vector4(Flattest, 1f, Fullest, 1f));
                    // A test build's pulse: the pair squeezes and lets go every few seconds with
                    // nobody moving it, which only happens if the provider is writing the follows.
                    copy.SetFloat("_YAPS_SquashPulse", pulse);
                    // Bone push off: every swing of the cloth read as a press.
                    copy.SetVector("_YAPS_SquashPush", new Vector4(0f, 0.03f, 0.25f, 1f));
                    copy.SetVector("_YAPS_SquashLimbL", limbs[0]);
                    copy.SetVector("_YAPS_SquashLimbR", limbs[1]);
                    Asset(folder, copy, $"{avatar.name} {smr.name} {slot}.mat");
                    // Where it came from, for StripShader to put back.
                    AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(copy)).userData = Mark + GlobalObjectId.GetGlobalObjectIdSlow(source);
                    copies.Add(copy);
                    mats[slot] = copy;
                }
                smr.sharedMaterials = mats;
            }
            if (still.Count > 0)
            {
                foreach (int m in still) { reach[m] = 0f; reach[m + 1] = 0f; }
                foreach (var copy in copies) copy.SetVector("_YAPS_SquashReach", reach);
            }

            // Off the root: the client works out whose avatar it is from the parent up.
            var host = Child(root, ShaderHostName).gameObject;
            var provider = host.AddComponent<CVRMaterialDataProvider>();
            provider.materials = copies.ToArray();
            provider.dataMode = CVRMaterialDataProvider.DataMode.TransformLocalToWorld;
            // The slots yaps_dent.cginc reads; a node scaled to zero for each one with nothing in it.
            var empty = Child(host.transform, "empty");
            empty.localScale = Vector3.zero;
            var slots = Enumerable.Repeat(empty, 18).ToArray();
            for (int i = 0; i < parts.Count; i++) { slots[i] = parts[i].follow; slots[4 + i] = rests[i]; }
            for (int i = 0; i < 8; i++) slots[8 + i] = arms[i / 4][i % 4] ?? empty;
            for (int i = 0; i < backs.Count; i++) slots[16 + i] = backs[i];
            provider.transforms = slots;
            provider.shaderPropertyName = "_YAPS_SquashFollow";

            // One vertex on each slot, skinned to it, so the slots come through skinning, after
            // the cloth and the IK. Its triangles have no area, so it draws nothing.
            var skin = Child(host.transform, "skin").gameObject.AddComponent<SkinnedMeshRenderer>();
            var skinBones = slots.Select(t => t == empty ? host.transform : t).ToArray();
            var mesh = new Mesh { name = "YAPS squash skin" };
            mesh.vertices = slots.Select(t => skin.transform.InverseTransformPoint(t.position)).ToArray();
            mesh.boneWeights = Enumerable.Range(0, slots.Length).Select(i => new BoneWeight { boneIndex0 = i, weight0 = 1f }).ToArray();
            mesh.bindposes = skinBones.Select(b => b.worldToLocalMatrix * skin.transform.localToWorldMatrix).ToArray();
            mesh.triangles = Enumerable.Range(0, 3 * slots.Length).Select(i => i / 3).ToArray();
            Asset(folder, mesh, $"{avatar.name} squash skin.asset");
            var blank = new Material(Shader.Find("Standard")) { name = "YAPS squash skin" };
            Asset(folder, blank, $"{avatar.name} squash skin.mat");
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
            return $"{parts.Count / 2} pair(s) through the shader on {avatar.name}, {copies.Count} material(s) in {folder}: " + string.Join("; ", log);
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
        static List<(SkinnedMeshRenderer smr, Texture2D own, Vector4[] shares)> Shares(Transform root, List<Transform> parts, string name, string folder)
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
            if (body.smr == null || body.owned == 0) return new List<(SkinnedMeshRenderer, Texture2D, Vector4[])>();
            const float Cell = 0.02f, Near = 0.03f;
            var grid = new Dictionary<Vector3Int, List<int>>();
            Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell), Mathf.FloorToInt(p.z / Cell));
            // Skin with no share goes in too: a strap painted onto the part's bones over a shoulder
            // that never moves must copy that nothing, or it squashes off the skin under it.
            for (int v = 0; v < body.at.Length; v++)
            {
                var k = Key(body.at[v]);
                if (!grid.TryGetValue(k, out var list)) grid[k] = list = new List<int>();
                list.Add(v);
            }
            var result = new List<(SkinnedMeshRenderer, Texture2D, Vector4[])>();
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
                Asset(folder, tex, $"{name} {m.smr.name} shares.asset");
                // What the CCK's upload checks ask of an avatar texture. The asset keeps its
                // pixels either way; readable only adds a CPU copy, and with no mips nothing streams.
                var flags = new SerializedObject(tex);
                flags.FindProperty("m_IsReadable").boolValue = false;
                flags.FindProperty("m_StreamingMipmaps").boolValue = true;
                flags.ApplyModifiedPropertiesWithoutUndo();
                result.Add((m.smr, tex, m.share));
            }
            return result;
        }

        // An earlier shader build off again: each layer back on the material it was copied from.
        public static void Strip(Transform root)
        {
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = smr.sharedMaterials;
                bool any = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    string data = mats[i] == null ? null : AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mats[i]))?.userData;
                    if (data == null || !data.StartsWith(Mark) || !GlobalObjectId.TryParse(data.Substring(Mark.Length), out var id)) continue;
                    var source = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Material;
                    if (source == null) continue;
                    mats[i] = source;
                    any = true;
                }
                if (any) smr.sharedMaterials = mats;
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == FollowName || t.name == RestName || t.name == BackName || t.name == ShaderHostName).ToList())
                if (t != null) UnityEngine.Object.DestroyImmediate(t.gameObject);
        }

        static T Asset<T>(string folder, T asset, string file) where T : UnityEngine.Object
        {
            YapsNativeBuilder.EnsureFolderPublic(folder);
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + file));
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

        static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }
    }
}
#endif
