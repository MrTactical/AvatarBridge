// The squash in the YAPS-patched body shader: every layer over a soft part takes the same move,
// worked out on the GPU from where the parts are. A material data provider writes each part's
// follow (a plain child of the bone the cloth moves most, at the part's centre) into the
// materials every frame, on every client, with nothing synced and no camera.
//
// How it feels comes from the avatar's YAPS Squash component, added on the first build. Its
// knobs write straight to the materials, so tuning needs no rebuild.
//
// Two ways to say where the parts are. Regions painted with the squash region tool, when the
// avatar carries them; otherwise bones found by name (breast, boob, butt, booty, glute, one each
// side), each vertex's share the skin weight its part's bones carry. Two pairs at most either
// way. Measured at rest, in edit mode. A shader the patcher refuses is reported and its layer
// left as it was.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarBridge
{
    public static class YapsSquash
    {
        public const string Category = "Squash";
        const string ShaderHostName = "YAPS squash (shader)", FollowName = "YAPS squash follow", RestName = "YAPS squash rest", BackName = "YAPS squash back";
        // On each copy's importer, before the material it was copied from, for Strip to put back.
        const string Mark = "squash:";
        static readonly Regex Soft = new Regex("breast|boob|butt|booty|glute", RegexOptions.IgnoreCase);
        static readonly Regex Side = new Regex(@"left|right|[._ ]?[lr](?=$|[._ ])", RegexOptions.IgnoreCase);

        // Every property the squash sets, so a swapped-in look can take the same values.
        static readonly string[] Properties =
        {
            "_YAPS_DentPower", "_YAPS_DentOwn", "_YAPS_SquashReach", "_YAPS_SquashAway", "_YAPS_SquashOut",
            "_YAPS_SquashIn", "_YAPS_SquashSideA", "_YAPS_SquashSideB", "_YAPS_SquashTune", "_YAPS_SquashPair",
            "_YAPS_SquashShape", "_YAPS_SquashPulse", "_YAPS_SquashPush", "_YAPS_SquashLimbL", "_YAPS_SquashLimbR",
            "_YAPS_SquashHands", "_YAPS_SquashMotion", "_YAPS_SquashPlug", "_YAPS_DentSpill",
        };

        sealed class Part
        {
            public string name;
            // The bone the follow rides and the one its rest sits on.
            public Transform mover, restOn, follow;
            public List<(Transform bone, float weight)> carriers;
            public List<Vector3> points;
            public Vector3 centre;
            // Where the part hangs from: the top of its chain, which the physics hold still.
            public Vector3 hangsFrom;
            public float reach, volume;
        }

        // A skinned renderer as it stands: each vertex posed, and its share of every part.
        sealed class Skin
        {
            public SkinnedMeshRenderer smr;
            public Transform[] bones;
            public BoneWeight[] weights;
            public Vector3[] at;
            public Vector4[] share;
            // Per renderer bone, the part whose bones hold it, or -1. Bone-name parts only.
            public int[] owner;
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

        // Whether the avatar carries a squash build.
        public static bool Has(Transform root) => root != null && root.Find(ShaderHostName) != null;

        public static YapsSquashSettings SettingsOf(Transform root) => root != null ? root.GetComponentInChildren<YapsSquashSettings>(true) : null;

        // The knobs onto one squash material. The skin stride in the hands' w is the build's.
        public static void WriteKnobs(YapsSquashSettings s, Material m)
        {
            var hands = m.GetVector("_YAPS_SquashHands");
            m.SetVector("_YAPS_SquashTune", new Vector4(s.lyingBack, s.strength, s.bendGive, s.deepestDent));
            m.SetVector("_YAPS_SquashShape", new Vector4(s.flattest, s.volumeKept, s.widest, s.meet ? 1f : 0f));
            m.SetFloat("_YAPS_DentSpill", s.spill);
            // The push's dead zone and the arms' soft edge are not knobs.
            m.SetVector("_YAPS_SquashPush", new Vector4(s.clothPush, 0.03f, 0.25f, s.ownArms > 0f ? 1f : 0f));
            m.SetVector("_YAPS_SquashHands", new Vector4(s.ownArms, s.handSize, s.othersHands, hands.w));
            m.SetFloat("_YAPS_SquashPlug", s.plugs);
            // From real flesh to anime: the stretch per reach of swing, the swing ignored as the
            // sag the physics settle into, and the longest it gets.
            float t = s.motionStyle;
            m.SetVector("_YAPS_SquashMotion", s.stretchInMotion
                ? new Vector4(Mathf.Lerp(0.6f, 3.5f, t), Mathf.Lerp(0.1f, 0.03f, t), Mathf.Lerp(1.12f, 1.8f, t), 0f)
                : Vector4.zero);
        }

        // Every material the build's providers write, alternates included.
        public static List<Material> Materials(Transform root)
        {
            var host = root != null ? root.Find(ShaderHostName) : null;
            if (host == null) return new List<Material>();
            return host.GetComponents<CVRMaterialDataProvider>().SelectMany(p => p.materials ?? new Material[0])
                .Where(m => m != null && m.HasProperty("_YAPS_DentOwn")).Distinct().ToList();
        }

        // The component's knobs onto the build, without rebuilding. How many materials took them.
        public static int Apply(Transform root)
        {
            var s = SettingsOf(root);
            var mats = s != null ? Materials(root) : new List<Material>();
            foreach (var m in mats)
            {
                WriteKnobs(s, m);
                EditorUtility.SetDirty(m);
            }
            return mats.Count;
        }

        // The slots as the provider writes them in game, for the edit mode preview.
        public static Transform[] Slots(Transform root)
        {
            var host = root != null ? root.Find(ShaderHostName) : null;
            var provider = host != null ? host.GetComponents<CVRMaterialDataProvider>()
                .FirstOrDefault(p => p.dataMode == CVRMaterialDataProvider.DataMode.TransformLocalToWorld) : null;
            return provider != null ? provider.transforms : null;
        }

        // The YAPS tool's build: the squash, then the toggles' swaps pointed at it.
        public static string Build(Transform root, out bool built)
        {
            string folder = YapsNativeBuilder.OutputRoot + "/Squash";
            string said = Add(root, folder, out built);
            if (built) said += " " + FollowSwaps(root, YapsSwapFollow.RunnableClips(root), folder, null);
            Debug.Log("[YAPS] Squash: " + said);
            return said.Trim();
        }

        // The regions to use, or null for parts by bone name. `refusal` says why regions the
        // avatar carries cannot be used; they are never quietly swapped for the name search.
        public static YapsSquashSettings RegionsOf(Transform root, out string refusal)
        {
            refusal = null;
            var regions = SettingsOf(root);
            if (regions == null || regions.pairs.Count == 0) return null;
            var mesh = regions.body != null ? regions.body.sharedMesh : null;
            if (mesh == null) refusal = "the squash regions name no body mesh";
            else if (!regions.body.transform.IsChildOf(root)) refusal = "the squash regions' body mesh is not on this avatar";
            else if (mesh.vertexCount != regions.vertexCount || Fingerprint(mesh) != regions.fingerprint)
                refusal = $"\"{mesh.name}\" changed since its squash regions were painted, so every weight would land on the wrong vertex. Paint them again";
            else if (regions.pairs.Any(p => p.a.Length != mesh.vertexCount || p.b.Length != mesh.vertexCount))
                refusal = "a squash region pair is missing weights";
            return refusal == null ? regions : null;
        }

        // The mesh as painted: vertex count and positions to the tenth of a millimetre.
        public static int Fingerprint(Mesh mesh)
        {
            unchecked
            {
                int h = mesh.vertexCount;
                foreach (var v in mesh.vertices)
                    h = h * 31 + Mathf.RoundToInt(v.x * 1e4f) * 73856093 ^ Mathf.RoundToInt(v.y * 1e4f) * 19349663 ^ Mathf.RoundToInt(v.z * 1e4f) * 83492791;
                return h;
            }
        }

        // Vertices sharing a position, one list each: the copies a UV seam splits a point into.
        // They take one weight, or the seam opens as the part squashes.
        public static List<int[]> Welds(Mesh mesh)
        {
            var verts = mesh.vertices;
            return Enumerable.Range(0, verts.Length)
                .GroupBy(v => new Vector3Int(Mathf.RoundToInt(verts[v].x * 1e5f), Mathf.RoundToInt(verts[v].y * 1e5f), Mathf.RoundToInt(verts[v].z * 1e5f)))
                .Where(g => g.Skip(1).Any()).Select(g => g.ToArray()).ToList();
        }

        public static string Add(Transform root, string folder, out bool built, BridgeReport report = null, float pulse = 0f)
        {
            built = false;
            Strip(root);
            var regions = RegionsOf(root, out string refusal);
            if (refusal != null) return refusal;
            List<List<Transform>> found = null;
            if (regions == null)
            {
                found = Pairs(root, out string none);
                if (none != null) return none;
            }

            var animator = root.GetComponent<Animator>();
            var humanoid = new HashSet<Transform>();
            if (animator != null && animator.isHuman)
                for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++)
                {
                    var t = animator.GetBoneTransform(b);
                    if (t != null) humanoid.Add(t);
                }

            var skins = Skins(root);
            int pairCount = Mathf.Min(regions != null ? regions.pairs.Count : found.Count, 2);
            if (regions == null)
            {
                var sets = Sets(found);
                foreach (var s in skins) NameShares(s, sets);
            }
            else
            {
                var body = skins.First(s => s.smr == regions.body);
                for (int k = 0; k < pairCount; k++)
                    for (int v = 0; v < body.at.Length; v++)
                    {
                        body.share[v][2 * k] = regions.pairs[k].a[v] / 255f;
                        body.share[v][2 * k + 1] = regions.pairs[k].b[v] / 255f;
                    }
                // One weight per point, whatever was painted on each copy of it.
                foreach (var weld in Welds(regions.body.sharedMesh))
                {
                    var mean = weld.Aggregate(Vector4.zero, (sum, v) => sum + body.share[v]) / weld.Length;
                    foreach (int v in weld) body.share[v] = mean;
                }
            }

            var parts = new List<Part>();
            var rests = new List<Transform>();
            var backs = new List<Transform>();
            // The socket between each pair, if any: a plug entering it parts the pair.
            var sockets = root.GetComponentsInChildren<YapsSocket>(true).Select(x => x.transform).ToList();
            var between = new List<Transform>();
            var kept = new List<int>();
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
            for (int k = 0; k < pairCount; k++)
            {
                string label = regions != null ? regions.pairs[k].name : $"{found[k][0].name}/{found[k][1].name}";
                var measured = Measure(skins, k, regions != null ? null : found[k], humanoid,
                    regions != null ? new[] { regions.pairs[k].name + " A", regions.pairs[k].name + " B" } : null);
                if (measured == null) { log.Add($"{label}: too little skin"); continue; }
                kept.Add(k);
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
                var restBone = a.restOn;
                var mid = 0.5f * (a.centre + b.centre);
                var level = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(mid - restBone.position, Vector3.up), axis);
                if (level.sqrMagnitude < 1e-6f) level = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(root.forward, Vector3.up), axis);
                var o = level.normalized;
                var back = Child(restBone, BackName);
                back.position = mid - o * a.reach;
                backs.Add(back);
                // In the gap the pair parts around: near the line from the body to the pair's
                // middle. Distance to the middle alone missed a socket at the skin between large
                // parts and could take one in the mouth above them.
                float Gap(Transform x) => HandleUtility.DistancePointLine(x.position, back.position, mid);
                var socket = sockets.Where(x => Gap(x) < Mathf.Min(a.reach, b.reach)).OrderBy(Gap).FirstOrDefault();
                between.Add(socket);
                if (socket != null) log.Add($"{label}: a plug in \"{socket.name}\" parts it");
                foreach (var p in measured)
                {
                    p.follow = Child(p.mover, FollowName);
                    p.follow.position = p.centre;
                    // The same point on the bone above the chain, where the follow sits when
                    // nothing pushes the part.
                    var rest = Child(p.restOn, RestName);
                    rest.position = p.centre;
                    // Its up axis for the stretch in motion: toward where the part hangs from,
                    // and up, as a soft part hangs under its weight too. A drop stretches it as
                    // well as a pull away from the body.
                    rest.rotation = Quaternion.FromToRotation(Vector3.up, (p.hangsFrom - p.centre).normalized + root.up);
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
                log.Add($"{label} on {a.follow.parent.name}/{b.follow.parent.name}: start {start * 100f:0.0} cm, reach {a.reach * 100f:0.0} + {b.reach * 100f:0.0}, " +
                        $"out {outward[i] * Unit(a.follow) * 100f:0.0}, in {inward[i] * Unit(a.follow) * 100f:0.0}, share {share:0.00}");
            }
            if (parts.Count == 0) return string.Join("; ", log);

            // A pair with too little skin is dropped, and the ones after it move up a slot.
            foreach (var s in skins)
                for (int v = 0; v < s.share.Length; v++)
                {
                    var was = s.share[v];
                    var now = Vector4.zero;
                    for (int j = 0; j < kept.Count; j++) { now[2 * j] = was[2 * kept[j]]; now[2 * j + 1] = was[2 * kept[j] + 1]; }
                    s.share[v] = now;
                }

            // Upper arm, lower arm, hand and middle finger per side, each capsule from one to the
            // next; its radius from the skin the bone carries, as a share of its length.
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
            var bodySkin = regions != null ? skins.First(s => s.smr == regions.body) : null;
            foreach (var (smr, own, shares) in Shares(skins, bodySkin, root.name, folder))
            {
                var mats = smr.sharedMaterials;
                for (int slot = 0; slot < mats.Length; slot++)
                {
                    var source = mats[slot];
                    if (source == null) continue;
                    if (!patched.TryGetValue(source, out var shader))
                    {
                        patched[source] = shader = ShaderFor(source, folder, report, log);
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
                                    log.Add($"\"{source.name}\" on {smr.name} cannot follow, so {parts[m & ~1].name}/{parts[m | 1].name} does not squash");
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
                    copy.SetVector("_YAPS_SquashPair", pair);
                    // A test build's pulse: the pair squeezes and lets go every few seconds with
                    // nobody moving it, which only happens if the provider is writing the follows.
                    copy.SetFloat("_YAPS_SquashPulse", pulse);
                    copy.SetVector("_YAPS_SquashLimbL", limbs[0]);
                    copy.SetVector("_YAPS_SquashLimbR", limbs[1]);
                    Save(copy, source, folder, $"{root.name} {smr.name} {slot}.mat");
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
            var slots = Enumerable.Repeat(empty, 20).ToArray();
            for (int i = 0; i < parts.Count; i++) { slots[i] = parts[i].follow; slots[4 + i] = rests[i]; }
            for (int i = 0; i < 8; i++) slots[8 + i] = arms[i / 4][i % 4] ?? empty;
            for (int i = 0; i < backs.Count; i++) slots[16 + i] = backs[i];
            for (int i = 0; i < between.Count; i++) slots[18 + i] = between[i] ?? empty;
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
            Asset(folder, mesh, $"{root.name} squash skin.asset");
            var blank = new Material(Shader.Find("Standard")) { name = "YAPS squash skin" };
            Asset(folder, blank, $"{root.name} squash skin.mat");
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

            // The knobs, from the avatar's component; a first build adds one with the defaults.
            if (SettingsOf(root) == null) root.gameObject.AddComponent<YapsSquashSettings>();
            Apply(root);
            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(root.gameObject);
            if (root.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            built = true;
            return $"{BridgeElements.Count(parts.Count / 2, "pair", "pairs")} {(regions != null ? "from painted regions" : "found by bone name")} on {root.name}, {BridgeElements.Count(copies.Count, "material", "materials")} in {folder}: " + string.Join("; ", log);
        }

        static List<HashSet<Transform>> Sets(List<List<Transform>> found) =>
            found.Take(2).SelectMany(p => p).Select(b => new HashSet<Transform>(b.GetComponentsInChildren<Transform>(true))).ToList();

        // Each vertex's share of each part found by name: the weight that part's bones carry.
        static void NameShares(Skin s, List<HashSet<Transform>> sets)
        {
            s.owner = s.bones.Select(t => t == null ? -1 : sets.FindIndex(x => x.Contains(t))).ToArray();
            for (int v = 0; v < s.at.Length; v++)
            {
                var w = s.weights[v];
                int[] index = { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
                float[] weight = { w.weight0, w.weight1, w.weight2, w.weight3 };
                for (int q = 0; q < 4; q++)
                    if (weight[q] > 0f && s.owner[index[q]] >= 0) s.share[v][s.owner[index[q]]] += weight[q];
            }
        }

        // What the name search would give one renderer, for the region tool to start from.
        public static Vector4[] NamedShares(Transform root, SkinnedMeshRenderer body, out List<string> names, out string none)
        {
            var found = Pairs(root, out none);
            names = found.Take(2).Select(p => Side.Replace(p[0].name, "").Trim('.', '_', ' ')).ToList();
            var skin = none == null ? Skins(root).FirstOrDefault(s => s.smr == body) : null;
            if (skin == null) return null;
            NameShares(skin, Sets(found));
            return skin.share;
        }

        // A material already carrying YAPS has the squash in it; anything else is patched.
        static Shader ShaderFor(Material source, string folder, BridgeReport report, List<string> log)
        {
            if (source.HasProperty("_YAPS_DentOwn")) return source.shader;
            var shader = YapsShaderPatcher.Patch(source, folder, report, out string refusal, out int shadows);
            if (shader == null) log?.Add($"left \"{source.name}\" as it was: {refusal}");
            // Unity's standard caster is wrapped; a caster from anywhere else cannot be reached.
            else if (shadows > 0) log?.Add($"\"{source.name}\" casts its shadow unsquashed");
            return shader;
        }

        static void Save(Material copy, Material source, string folder, string file)
        {
            Asset(folder, copy, file);
            AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(copy)).userData = Mark + GlobalObjectId.GetGlobalObjectIdSlow(source);
        }

        // The material a squash copy was made from, or null when it is not one.
        static Material SourceOf(Material m)
        {
            string data = m == null ? null : AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(m))?.userData;
            if (data == null || !data.StartsWith(Mark) || !GlobalObjectId.TryParse(data.Substring(Mark.Length), out var id)) return null;
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Material;
        }

        // The first material in a chain of copies, so a build over an earlier one copies the
        // original rather than the copy.
        static Material Original(Material m)
        {
            for (int guard = 0; guard < 8; guard++)
            {
                var from = SourceOf(m);
                if (from == null) return m;
                m = from;
            }
            return m;
        }

        // Every swap an animation makes into a squashed slot pointed at a squashed look. A toggle
        // handing the slot back to the original, or in an alternate, would leave that layer
        // unsquashed over skin that is, and the skin comes through it. Alternates get a copy of
        // their own with the slot's values, and both providers write into them too. Only clips
        // the user owns are edited.
        public static string FollowSwaps(Transform root, IList<AnimationClip> clips, string folder, BridgeReport report)
        {
            var host = root.Find(ShaderHostName);
            if (host == null || clips == null || clips.Count == 0) return null;
            var providers = host.GetComponents<CVRMaterialDataProvider>();
            var added = new List<Material>();
            var copyOf = new Dictionary<(Renderer, Material), Material>();
            var refused = new SortedSet<string>();
            var skipped = new SortedSet<string>();
            int repointed = 0;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = smr.sharedMaterials;
                string path = AnimationUtility.CalculateTransformPath(smr.transform, root);
                for (int slot = 0; slot < mats.Length; slot++)
                {
                    var template = mats[slot];
                    var own = template != null && SourceOf(template) != null && template.HasProperty("_YAPS_DentOwn") ? template.GetTexture("_YAPS_DentOwn") : null;
                    if (own == null) continue;
                    copyOf[(smr, Original(template))] = template;
                    foreach (var was in Keyed(clips, path, slot))
                    {
                        if (was.HasProperty("_YAPS_DentOwn") && was.GetTexture("_YAPS_DentOwn") == own) continue;
                        var original = Original(was);
                        if (!copyOf.TryGetValue((smr, original), out var to))
                        {
                            var shader = ShaderFor(original, folder, report, null);
                            if (shader == null) { refused.Add(original.name); continue; }
                            to = new Material(original) { shader = shader };
                            foreach (string name in Properties) Carry(template, to, name);
                            Save(to, original, folder, $"{root.name} {smr.name} {slot} {original.name}.mat");
                            copyOf[(smr, original)] = to;
                            added.Add(to);
                        }
                        repointed += YapsSwapFollow.RepointInClips(clips, path, slot, was, to, skipped);
                    }
                }
            }
            foreach (var p in providers)
            {
                p.materials = p.materials.Concat(added).Distinct().ToArray();
                EditorUtility.SetDirty(p);
            }
            AssetDatabase.SaveAssets();
            var said = new List<string>();
            if (repointed > 0)
            {
                string what = $"Pointed {BridgeElements.Count(repointed, "material swap", "material swaps")} at squashed copies";
                string why = added.Count > 0 ? $"{added.Count} of them alternate looks, given the squash too." : "So a clothing toggle keeps its layer on the skin.";
                report?.Converted(Category, what, why);
                said.Add(what + ". " + why);
            }
            if (refused.Count > 0)
            {
                string what = $"{BridgeElements.Count(refused.Count, "swapped-in material", "swapped-in materials")} cannot squash";
                string why = "Their shaders refused the patch, so while one is on, the skin can come through that layer: " + string.Join(", ", refused) + ".";
                report?.Warning(Category, what, why);
                said.Add(what + ". " + why);
            }
            if (skipped.Count > 0)
            {
                string what = $"{BridgeElements.Count(skipped.Count, "material swap", "material swaps")} left alone";
                string why = "They live in a package, so they were not edited. Copy the clip into your project: " + string.Join(", ", skipped) + ".";
                report?.Warning(Category, what, why);
                said.Add(what + ". " + why);
            }
            return said.Count > 0 ? string.Join(" ", said) : null;
        }

        static void Carry(Material from, Material to, string name)
        {
            if (!from.HasProperty(name) || !to.HasProperty(name)) return;
            if (name == "_YAPS_DentOwn") to.SetTexture(name, from.GetTexture(name));
            else if (name == "_YAPS_DentPower" || name == "_YAPS_SquashPulse" || name == "_YAPS_SquashPlug" || name == "_YAPS_DentSpill") to.SetFloat(name, from.GetFloat(name));
            else to.SetVector(name, from.GetVector(name));
        }

        // Every material a clip keys into this path and slot, once each.
        static List<Material> Keyed(IEnumerable<AnimationClip> clips, string path, int slot)
        {
            var seen = new List<Material>();
            foreach (var clip in clips.Where(c => c != null).Distinct())
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    if (binding.path != path || YapsSwapFollow.SlotIndex(binding.propertyName) != slot) continue;
                    foreach (var key in AnimationUtility.GetObjectReferenceCurve(clip, binding) ?? new ObjectReferenceKeyframe[0])
                        if (key.value is Material m && !seen.Contains(m)) seen.Add(m);
                }
            return seen;
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

        // Every skinned renderer posed as it stands, shares still empty.
        static List<Skin> Skins(Transform root)
        {
            var list = new List<Skin>();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var rb = smr.bones;
                var weights = mesh.boneWeights;
                var verts = mesh.vertices;
                var bind = mesh.bindposes;
                var skin = rb.Select((t, i) => t != null && i < bind.Length ? t.localToWorldMatrix * bind[i] : Matrix4x4.zero).ToArray();
                int count = Mathf.Min(weights.Length, verts.Length);
                var at = new Vector3[count];
                for (int v = 0; v < count; v++)
                {
                    var w = weights[v];
                    int[] index = { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
                    float[] weight = { w.weight0, w.weight1, w.weight2, w.weight3 };
                    for (int q = 0; q < 4; q++) at[v] += skin[index[q]].MultiplyPoint3x4(verts[v]) * weight[q];
                }
                list.Add(new Skin { smr = smr, bones = rb, weights = weights, at = at, share = new Vector4[count] });
            }
            return list;
        }

        // Each renderer's texture of its shares, in r, g, b, a. The skin is the painted body, or
        // the renderer carrying the most of the parts; a layer riding bones of its own takes the
        // share of the nearest skin vertex within 3 cm, or the skin would squash out from under it.
        static List<(SkinnedMeshRenderer smr, Texture2D own, Vector4[] shares)> Shares(List<Skin> skins, Skin painted, string name, string folder)
        {
            int Owned(Skin s) => s.share.Count(x => Mathf.Max(x.x, x.y, x.z, x.w) >= 0.5f);
            var body = painted ?? skins.OrderByDescending(Owned).FirstOrDefault();
            if (body == null || Owned(body) == 0) return new List<(SkinnedMeshRenderer, Texture2D, Vector4[])>();
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
            foreach (var m in skins)
            {
                if (m != body)
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

        // An earlier build off again: each layer back on the material it was copied from, and
        // with clips given, every swap pointed back the same way, or a toggle would put on a
        // squash copy no provider writes any more.
        public static void Strip(Transform root, IList<AnimationClip> clips = null)
        {
            var skipped = new SortedSet<string>();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = smr.sharedMaterials;
                string path = clips != null ? AnimationUtility.CalculateTransformPath(smr.transform, root) : null;
                bool any = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (clips != null)
                        foreach (var keyed in Keyed(clips, path, i))
                            if (SourceOf(keyed) != null) YapsSwapFollow.RepointInClips(clips, path, i, keyed, Original(keyed), skipped);
                    var source = SourceOf(mats[i]);
                    if (source == null) continue;
                    mats[i] = Original(mats[i]);
                    any = true;
                }
                if (any) smr.sharedMaterials = mats;
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == FollowName || t.name == RestName || t.name == BackName || t.name == ShaderHostName).ToList())
                if (t != null) UnityEngine.Object.DestroyImmediate(t.gameObject);
            if (root.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        }

        static T Asset<T>(string folder, T asset, string file) where T : UnityEngine.Object
        {
            YapsNativeBuilder.EnsureFolderPublic(folder);
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + file));
            return asset;
        }

        // Pair `k` as the skin carries it, posed as it stands: points from the renderer carrying
        // the most of both parts, weights from every renderer. By bone name, the parts' own bones
        // count; painted, every bone counts by how much of the region it carries.
        static List<Part> Measure(List<Skin> skins, int k, List<Transform> bones, HashSet<Transform> humanoid, string[] names)
        {
            var carried = new[] { new Dictionary<Transform, float>(), new Dictionary<Transform, float>() };
            List<Vector3>[] best = null;
            int bestCount = 0;
            foreach (var s in skins)
            {
                if (bones != null && s.owner.All(x => x < 2 * k || x > 2 * k + 1)) continue;
                var points = new[] { new List<Vector3>(), new List<Vector3>() };
                for (int v = 0; v < s.at.Length; v++)
                {
                    var w = s.weights[v];
                    int[] index = { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
                    float[] weight = { w.weight0, w.weight1, w.weight2, w.weight3 };
                    for (int q = 0; q < 4; q++)
                    {
                        if (weight[q] <= 0f) continue;
                        var bone = s.bones[index[q]];
                        for (int side = 0; side < 2; side++)
                        {
                            float on = bones != null ? (s.owner[index[q]] == 2 * k + side ? weight[q] : 0f) : weight[q] * s.share[v][2 * k + side];
                            if (on <= 0f || bone == null) continue;
                            carried[side][bone] = (carried[side].TryGetValue(bone, out var t) ? t : 0f) + on;
                        }
                    }
                    for (int side = 0; side < 2; side++) if (s.share[v][2 * k + side] >= 0.5f) points[side].Add(s.at[v]);
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
                    points = best[i],
                    centre = best[i].Aggregate(Vector3.zero, (s, x) => s + x) / best[i].Count,
                    carriers = carried[i].Select(kv => (kv.Key, kv.Value / total)).Where(x => x.Item2 >= 0.03f)
                        .OrderBy(x => Depth(x.Key)).ToList(),
                };
                var top = p.carriers[0].bone;
                if (bones != null)
                {
                    var root = bones[i];
                    p.name = root.name;
                    // On the bone the cloth moves most. The chain root is held still by the
                    // cloth, so a follow on it never saw the pair close in game.
                    p.mover = p.carriers.Where(c => c.bone != root).OrderByDescending(c => c.weight).Select(c => c.bone).FirstOrDefault() ?? top;
                    p.restOn = root.parent != null ? root.parent : root;
                    p.hangsFrom = root.position;
                }
                else
                {
                    p.name = names[i];
                    // Bones of its own carrying a good part of it, below the skeleton: the most
                    // weighted one hanging from another of them, since the cloth holds a chain's
                    // root still, with the rest on whatever that chain hangs from. Otherwise the
                    // region rides the bone carrying most of it, follow and rest together, so
                    // only a press moves it.
                    var loose = humanoid.Count == 0 ? new List<(Transform bone, float weight)>()
                        : p.carriers.Where(c => !humanoid.Contains(c.bone)).OrderByDescending(c => c.weight).ToList();
                    if (loose.Sum(c => c.weight) >= 0.25f)
                    {
                        var own = new HashSet<Transform>(loose.Select(c => c.bone));
                        p.mover = loose.Select(c => c.bone).FirstOrDefault(b => b.parent != null && own.Contains(b.parent)) ?? loose[0].bone;
                        var chain = p.mover;
                        while (chain.parent != null && own.Contains(chain.parent)) chain = chain.parent;
                        p.restOn = chain.parent != null ? chain.parent : chain;
                        p.hangsFrom = chain.position;
                    }
                    else
                    {
                        // Follow and rest ride one bone, so it never swings: the hang goes unused.
                        p.mover = p.restOn = p.carriers.OrderByDescending(c => c.weight).First().bone;
                        p.hangsFrom = p.mover.position;
                    }
                }
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

#if VRC_SDK_VRCSDK3
        // The converter's opt-in. After YAPS has baked and pointed its swaps, so the copies are
        // of what the avatar ends up wearing, and YAPS still finds its own materials on the mesh.
        public static void Run(BridgeContext ctx)
        {
            if (!ctx.Settings.squashSoftParts) return;
            var root = ctx.Target.transform;
            string folder = ctx.OutputDir + "/YAPS/Squash";
            string said = Add(root, folder, out bool built, ctx.Report);
            if (!built)
            {
                ctx.Report.Warning(Category, "Squash not built", said);
                return;
            }
            ctx.Report.Converted(Category, "Soft parts squash against each other and against hands",
                said + ". Tune it, and preview it, on the YAPS Squash component on the converted avatar");
            if (ctx.MergedController != null) FollowSwaps(root, ctx.MergedController.animationClips, folder, ctx.Report);
        }
#endif
    }
}
#endif
