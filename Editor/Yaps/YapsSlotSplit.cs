// Two plugs on one material slot, from different shafts.
//
// A material holds one plug's bake, length and frame, so the second plug
// baked into the slot took the material over and the first never bent:
// two plugs on one accessory mesh, one of them rigid. The bake is indexed
// by vertex, not by triangle, so moving the second plug's triangles into a
// submesh of their own, on a copy of the mesh, gives it a slot and a
// material without touching either bake. The rest of the slot stays with
// the first plug's material, whose bake leaves every vertex but its own
// where it is.
//
// The same shaft baked twice is not this: three plug components on one
// shaft share the slot, and the last bake winning is right.
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

            // Named for where the renderer and the plug sit, as the bake is: a
            // third plug's split is made from the second's mesh, which has to
            // survive it for Remove to go back to.
            Directory.CreateDirectory(dir);
            string path = dir + "/YAPS " + Safe(skin.name) + " " + YapsBaker.PlaceKey(skin.transform, plugRoot) + Suffix + ".asset";
            if (AssetDatabase.GetAssetPath(mesh) == path)
            {
                // The mesh being replaced is this file: move off it first.
                skin.sharedMesh = split;
            }
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(split, path);
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
                var w = weights[i];
                mask[i] = (plugBones.Contains(w.boneIndex0) && w.weight0 > 0.5f)
                          || (plugBones.Contains(w.boneIndex1) && w.weight1 > 0.5f)
                          || (plugBones.Contains(w.boneIndex2) && w.weight2 > 0.5f)
                          || (plugBones.Contains(w.boneIndex3) && w.weight3 > 0.5f);
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
