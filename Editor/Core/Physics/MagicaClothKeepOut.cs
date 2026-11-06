#if CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    // Here, not at the top: MagicaCloth 1's global namespace MagicaCloth beats a file-level using.
    using MagicaCloth2;

    // Bones a BoneCloth leaves alone. MagicaCloth2 simulates every transform under its roots and
    // has no ignore list, so a child under a cloth bone became a particle of the cloth's radius:
    // an accessory a PhysBone ignored, or a helper object, each a ball as wide as the part that
    // held a hand off it and shook it when pinched. A bone its per-bone attributes call
    // invalid it neither moves nor writes, so it rides its parent as any child does: a PhysBone's
    // ignore. Those attributes are what its own point paint stores, positions in the cloth's space.
    // No VRChat SDK here, so the add-on builds without one.
    public static class MagicaClothKeepOut
    {
        // Every bone under the cloth's roots listed, roots fixed, keep and all below invalid, the
        // rest moving. Bones already kept out stay out, read from the attributes it had.
        public static void KeepOut(MagicaCloth cloth, IEnumerable<Transform> keep)
        {
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
                bool was = had && WasInvalid(old, at);
                bool invalid = under || off.Contains(t) || was;
                positions.Add(at);
                attributes.Add(invalid ? VertexAttribute.Invalid : roots.Contains(t) ? VertexAttribute.Fixed : VertexAttribute.Move);
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

        // Within a millimetre: the positions are the same bones', read in the same space.
        static bool WasInvalid(SelectionData old, Vector3 at)
        {
            for (int i = 0; i < old.positions.Length; i++)
                if (old.attributes[i].IsInvalid() && ((Vector3) old.positions[i] - at).sqrMagnitude < 1e-6f)
                    return true;
            return false;
        }

        // MagicaCloth2 reads a cloth's transforms afresh when it starts, and ChilloutVR applies a
        // saved avatar size before that, so a cloth without initData kept gravity in metres. Its
        // editor writes initData only when it draws a cloth's gizmo or inspector; this writes it the
        // same way. False when the cloth does not validate, and it builds as before.
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
