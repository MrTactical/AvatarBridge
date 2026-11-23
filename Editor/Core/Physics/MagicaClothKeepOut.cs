#if CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    // Here, not at the top: MagicaCloth 1's global namespace MagicaCloth beats a file-level using.
    using MagicaCloth2;

    // MagicaCloth2 has no ignore list: every child under a root became a cloth-radius particle that
    // held hands off and shook when pinched. Marking a bone invalid in its point-paint attributes
    // (cloth-space positions) makes it ride its parent, as a PhysBone ignore does. No VRChat SDK,
    // so the add-on builds without one.
    public static class MagicaClothKeepOut
    {
        // Every bone under the cloth's roots listed, keep and all below invalid. A bone the cloth
        // already had an attribute for keeps it, so a hand-painted selection survives; a new one
        // is fixed at a root and moves elsewhere.
        public static void KeepOut(MagicaCloth cloth, IEnumerable<Transform> keep)
        {
            Undo.RecordObject(cloth, "Keep bones out of cloth");
            var sdata = cloth.SerializeData;
            var sdata2 = cloth.GetSerializeData2();
            var toCloth = cloth.ClothTransform.worldToLocalMatrix;
            var roots = new HashSet<Transform>(sdata.rootBones.Where(r => r != null));
            var off = new HashSet<Transform>(keep.Where(t => t != null));
            var old = sdata2.selectionData;
            bool had = old != null && old.IsValid() && old.IsUserEdit();

            var positions = new List<Unity.Mathematics.float3>();
            var attributes = new List<VertexAttribute>();
            var seen = new HashSet<Transform>();
            float longest = 0f;
            void Walk(Transform t, bool under, Vector3 parent, bool hasParent)
            {
                if (!seen.Add(t)) return;
                Vector3 at = toCloth.MultiplyPoint3x4(t.position);
                var was = had ? Had(old, at) : null;
                bool invalid = under || off.Contains(t) || was?.IsInvalid() == true;
                positions.Add(at);
                attributes.Add(invalid ? VertexAttribute.Invalid : was ?? (roots.Contains(t) ? VertexAttribute.Fixed : VertexAttribute.Move));
                if (hasParent) longest = Mathf.Max(longest, (at - parent).magnitude);
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), invalid, at, true);
            }
            foreach (var r in roots) Walk(r, false, Vector3.zero, false);
            if (positions.Count == 0) return;

            sdata2.selectionData = new SelectionData
            {
                positions = positions.ToArray(),
                attributes = attributes.ToArray(),
                maxConnectionDistance = longest,
                userEdit = true,
            };
            // A start scale recorded before these bones existed lists the wrong transforms.
            if (sdata2.initData != null && sdata2.initData.HasData()) Pin(cloth);
            EditorUtility.SetDirty(cloth);
        }

        // Each cloth under avatar whose roots hold any of these, kept out of it.
        public static int KeepOut(Transform avatar, ICollection<Transform> objects)
        {
            int clothes = 0;
            foreach (var cloth in avatar.GetComponentsInChildren<MagicaCloth>(true))
            {
                var mine = objects.Where(o => o != null && cloth.SerializeData.rootBones.Any(r => r != null && o.IsChildOf(r))).ToList();
                if (mine.Count == 0) continue;
                KeepOut(cloth, mine);
                clothes++;
            }
            return clothes;
        }

        // MagicaCloth2 reads the selection by nearest position, so a kept-out bone sitting exactly on a
        // moving parent took the parent's mark and moved (Modular Avatar's hidden-skin bones sit at zero
        // offset). Each such bone steps 2 mm off, and every mesh skinned to it or below takes the same
        // step out of its bind pose, so the skin stays where it was. Meshes outside the output folder are
        // copied there first. Returns how many bones moved.
        public static int Separate(BridgeContext ctx, IEnumerable<Transform> keep)
        {
            var off = new HashSet<Transform>(keep.Where(t => t != null));
            var moved = off.Where(t => t.parent != null && !off.Any(o => o != t && t.IsChildOf(o))
                                       && (t.position - t.parent.position).sqrMagnitude < 1e-6f).ToList();
            if (moved.Count == 0) return 0;
            var skinned = ctx.Target.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh != null && r.bones.Any(b => b != null && moved.Any(m => b.IsChildOf(m)))).ToList();
            var before = skinned.SelectMany(r => r.bones).Where(b => b != null).Distinct().ToDictionary(b => b, b => b.localToWorldMatrix);
            foreach (var m in moved) m.position += m.parent.up * 0.002f;
            var copies = new Dictionary<Mesh, Mesh>();
            foreach (var r in skinned)
            {
                var mesh = r.sharedMesh;
                if (!copies.TryGetValue(mesh, out var own))
                {
                    own = mesh;
                    string path = AssetDatabase.GetAssetPath(mesh);
                    if (string.IsNullOrEmpty(path) || !path.StartsWith(ctx.OutputDir.TrimEnd('/') + "/"))
                    {
                        own = Object.Instantiate(mesh);
                        own.name = mesh.name;
                        string dir = ctx.OutputDir.TrimEnd('/') + "/Physics";
                        OutputAssetPaths.EnsureFolder(dir);
                        AssetDatabase.CreateAsset(own, OutputAssetPaths.Claim($"{dir}/{OutputAssetPaths.SafeFileName(mesh.name)}.asset"));
                    }
                    copies[mesh] = own;
                    var binds = own.bindposes;
                    var bones = r.bones;
                    for (int i = 0; i < bones.Length && i < binds.Length; i++)
                        if (bones[i] != null && moved.Any(m => bones[i].IsChildOf(m)))
                            binds[i] = bones[i].worldToLocalMatrix * before[bones[i]] * binds[i];
                    own.bindposes = binds;
                    EditorUtility.SetDirty(own);
                }
                r.sharedMesh = own;
            }
            return moved.Count;
        }

        // Within a millimetre: the positions are the same bones', read in the same space.
        static VertexAttribute? Had(SelectionData old, Vector3 at)
        {
            for (int i = 0; i < old.positions.Length; i++)
                if (((Vector3) old.positions[i] - at).sqrMagnitude < 1e-6f)
                    return old.attributes[i];
            return null;
        }

        // CVR applies a saved avatar size before the cloth starts, so without initData gravity stayed in
        // metres. Its editor writes initData only when drawing; this does the same. False if invalid.
        public static bool Pin(MagicaCloth cloth)
        {
            var sdata = cloth.SerializeData;
            if (!sdata.IsValid()) return false;
            RenderSetupData setup = null;
            try
            {
                setup = sdata.clothType == ClothProcess.ClothType.BoneSpring
                    ? new RenderSetupData(null, RenderSetupData.SetupType.BoneSpring, cloth.ClothTransform, sdata.rootBones,
                        sdata.colliderCollisionConstraint.collisionBones, RenderSetupData.BoneConnectionMode.Line, cloth.name)
                    : new RenderSetupData(null, RenderSetupData.SetupType.BoneCloth, cloth.ClothTransform, sdata.rootBones,
                        null, sdata.connectionMode, cloth.name);
                if (setup.IsFaild()) return false;
                var normal = sdata.normalAlignmentSetting.adjustmentTransform
                    ? sdata.normalAlignmentSetting.adjustmentTransform
                    : cloth.ClothTransform;
                var init = new ClothInitSerializeData();
                init.Serialize(sdata, new TransformRecord(cloth.ClothTransform, read: true),
                    new TransformRecord(normal, read: true), new List<RenderSetupData> { setup });
                cloth.GetSerializeData2().initData = init;
                EditorUtility.SetDirty(cloth);
                return true;
            }
            finally
            {
                setup?.Dispose();
            }
        }
    }
}
#endif
