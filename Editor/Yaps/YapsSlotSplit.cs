// Two plugs from different shafts on one material slot. A material holds one plug's
// bake, so the second took it over and the first went rigid. The bake is per vertex,
// so moving the second plug's triangles to their own submesh on a mesh copy gives it
// its own material without touching either bake.
// Several plugs on the SAME shaft share the slot; the last bake winning is right.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsSlotSplit
    {
        const string Suffix = " split";

        // Mostly the same vertices: the same shaft, however many plugs.
        public static bool SameShaft(bool[] mine, bool[] theirs)
        {
            if (mine == null || theirs == null) return true;
            int own = 0, both = 0;
            for (int i = 0; i < mine.Length && i < theirs.Length; i++)
            {
                if (!mine[i]) continue;
                own++;
                if (theirs[i]) both++;
            }
            return own == 0 || both * 2 >= own;
        }

        // The triangles of this slot touching the plug's vertices, moved to a
        // new submesh with its own slot holding the given material. Returns the
        // new slot, or -1 when there is nothing to move, nothing left behind,
        // or the renderer's slots do not line up with its submeshes.
        public static int Split(SkinnedMeshRenderer skin, Transform plugRoot, int slot, bool[] mine, Material material,
            string dir, out string why)
        {
            why = null;
            var mesh = skin != null ? skin.sharedMesh : null;
            var mats = skin != null ? skin.sharedMaterials.ToList() : null;
            if (mesh == null || mine == null || material == null)
            {
                why = "nothing to split";
                return -1;
            }
            if (mats.Count != mesh.subMeshCount || slot < 0 || slot >= mesh.subMeshCount
                || mesh.GetTopology(slot) != MeshTopology.Triangles)
            {
                why = "its material slots do not line up one to one with its submeshes";
                return -1;
            }
            var triangles = mesh.GetTriangles(slot);
            var keep = new List<int>();
            var move = new List<int>();
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                bool ours = (a < mine.Length && mine[a]) || (b < mine.Length && mine[b]) || (c < mine.Length && mine[c]);
                var into = ours ? move : keep;
                into.Add(a);
                into.Add(b);
                into.Add(c);
            }
            if (move.Count == 0 || keep.Count == 0)
            {
                why = move.Count == 0 ? "none of the slot's triangles are the plug's" : "the whole slot is the plug's";
                return -1;
            }

            var split = Object.Instantiate(mesh);
            split.name = mesh.name.EndsWith(Suffix, System.StringComparison.Ordinal) ? mesh.name : mesh.name + Suffix;
            int added = split.subMeshCount;
            split.subMeshCount = added + 1;
            split.SetTriangles(keep, slot);
            split.SetTriangles(move, added);
            split.bounds = mesh.bounds;

            // Named by place, as the bake is: a third plug splits the second's
            // mesh, which must survive for Remove.
            Directory.CreateDirectory(dir);
            string path = dir + "/YAPS " + Safe(skin.name) + " " + YapsBaker.PlaceKey(skin.transform, plugRoot) + Suffix + ".asset";
            // Rewritten in place: deleting it breaks a renderer from an earlier
            // conversion that still wears it.
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved != null)
            {
                EditorUtility.CopySerialized(split, saved);
                Object.DestroyImmediate(split);
                EditorUtility.SetDirty(saved);
                AssetDatabase.SaveAssetIfDirty(saved);
                split = saved;
            }
            else
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(split, path);
            }
            skin.sharedMesh = split;
            mats.Add(material);
            skin.sharedMaterials = mats.ToArray();
            EditorUtility.SetDirty(skin);
            YapsDebugOverlayBuilder.Replaced(skin, split);
            return added;
        }

        // The vertices a plug's bones carry more than half of.
        public static bool[] Mask(SkinnedMeshRenderer skin, Transform plugRoot)
        {
            var mesh = skin.sharedMesh;
            var bones = skin.bones;
            if (mesh == null || bones == null || bones.Length == 0)
            {
                return null;
            }

            var plugBones = new HashSet<int>();
            for (int b = 0; b < bones.Length; b++)
            {
                if (bones[b] != null && bones[b].IsChildOf(plugRoot))
                {
                    plugBones.Add(b);
                }
            }
            if (plugBones.Count == 0)
            {
                for (var above = plugRoot.parent; above != null && plugBones.Count == 0;
                     above = above.parent)
                {
                    for (int b = 0; b < bones.Length; b++)
                    {
                        if (bones[b] != null && bones[b].IsChildOf(above))
                        {
                            plugBones.Add(b);
                        }
                    }
                }
            }
            if (plugBones.Count == 0)
            {
                return null;
            }

            var weights = mesh.boneWeights;
            var mask = new bool[mesh.vertexCount];
            for (int i = 0; i < mask.Length && i < weights.Length; i++)
            {
                // The baker's sum: per bone, a vertex blended across the shaft's
                // bones would bend yet be left out here.
                mask[i] = YapsBaker.WeightOnPlug(weights[i], plugBones) > 0.5f;
            }
            return mask;
        }

        static string Safe(string name)
        {
            foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
            return name;
        }
    }
}
#endif
