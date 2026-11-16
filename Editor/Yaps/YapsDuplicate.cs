// A plug's second copy, so one plug can enter two sockets at once.
//
// A copy of every corner of the plug slot's triangles whose three corners
// are all on the plug and carry a frame, appended after every vertex, and
// those triangles added to the same slot over the copies. Always built from
// the mesh under it, so copies never compound. The deform tells the copy
// apart by _YAPS_CopyStart, the original vertex count.
//
// ANCHORED, not weighted as their originals. With the originals' weights a
// bent plug's copy vertices recovered roots up to 119 mm apart, and near
// where the copy starts drawing a triangle hid one corner at a time. So
// every copy sits on one point, the bake's origin, wholly on the root's
// bone, its normal and tangent the root's up and forward, no blendshape
// deltas, colours and UVs its original's. Every copy vertex skins the same
// numbers, so every test the deform makes answers once for the whole copy,
// and a viewer without the deform sees nothing. Its shape is its original's
// bake entry: the plain bake, extended, never a bake over the copies, which
// would measure a point.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarBridge
{
    public static class YapsDuplicate
    {
        public const string Suffix = " duplicate";

        public static bool Is(Mesh mesh) => mesh != null && mesh.name.EndsWith(Suffix, StringComparison.Ordinal);

        // The copy onto the mesh as it stands and onto the bake made from it,
        // for a material about to take that bake. The copy's first vertex, or
        // -1 with why. result needs its Pixels.
        public static int Add(SkinnedMeshRenderer skin, int slot, YapsBaker.Result result, string dir, out string why)
        {
            var dupFrom = skin.sharedMesh;
            if (result.Pixels == null) { why = "the bake kept no copy of its data"; return -1; }
            if (dupFrom == null || dupFrom.vertexCount != result.VertexCount) { why = "the bake does not describe this mesh"; return -1; }
            var mesh = Build(dupFrom, slot, skin.bones, result.Root, result.Origin, result.Rotation, out int[] from, out why);
            if (mesh == null) return -1;
            int n = dupFrom.vertexCount;

            Directory.CreateDirectory(dir);
            string path = dir + "/YAPS " + Safe(skin.name) + " " + YapsBaker.PlaceKey(skin.transform, result.Root) + Suffix + ".asset";
            mesh = YapsBaker.SaveInPlace(mesh, path);

            // Over the plain bake's own file, so the material keeps pointing at it.
            var grown = ExtendBake(result.Pixels, n, from, result.Shapes.Count, result.Bake.width);
            var bake = YapsBaker.SaveInPlace(YapsBaker.BakeTexture(grown), AssetDatabase.GetAssetPath(result.Bake));
            YapsBaker.SettleForUpload(bake);
            result.Bake = bake;
            result.VertexCount = mesh.vertexCount;
            result.Pixels = null;

            skin.sharedMesh = mesh;
            EditorUtility.SetDirty(skin);
            YapsDebugOverlayBuilder.Replaced(skin, mesh);
            return n;
        }

        // from: which original each copy copies, in order; copy i is vertex
        // dupFrom.vertexCount + i. origin and rotation are the plug's frame
        // from dupFrom's bake, in world space, at the pose the bones hold now.
        public static Mesh Build(Mesh dupFrom, int submesh, Transform[] bones, Transform root,
            Vector3 origin, Quaternion rotation, out int[] from, out string why)
        {
            from = null;
            why = null;
            if (dupFrom == null || root == null || bones == null) { why = "no mesh, bones or root"; return null; }
            // Built on, the copies would be copied again.
            if (Is(dupFrom)) { why = "it is a duplicate already; build from the mesh under it"; return null; }
            if (submesh < 0 || submesh >= dupFrom.subMeshCount || dupFrom.GetTopology(submesh) != MeshTopology.Triangles)
            { why = "it has no triangle submesh " + submesh; return null; }
            int n = dupFrom.vertexCount;
            var weights = dupFrom.boneWeights;
            if (weights.Length != n) { why = "it has no bone weights"; return null; }
            var normals = dupFrom.normals;
            var tangents = dupFrom.tangents;
            // The deform recovers its frame from both, so without them the plug never bends.
            if (normals.Length != n || tangents.Length != n) { why = "it has no normals or no tangents, so the deform recovers no frame"; return null; }
            // The plug root's bone, or the nearest above it.
            int bone = -1;
            for (var t = root; t != null && bone < 0; t = t.parent) bone = Array.IndexOf(bones, t);
            if (bone < 0) { why = "neither the plug root nor anything above it is one of its bones"; return null; }
            // The bake's frame in mesh space, through that bone's bind.
            var toMesh = (bones[bone].localToWorldMatrix * dupFrom.bindposes[bone]).inverse;
            var anchor = toMesh.MultiplyPoint3x4(origin);
            var forward = toMesh.MultiplyVector(rotation * Vector3.forward).normalized;
            var up = toMesh.MultiplyVector(rotation * Vector3.up).normalized;
            var plugBones = new HashSet<int>();
            for (int b = 0; b < bones.Length; b++)
                if (bones[b] != null && bones[b].IsChildOf(root)) plugBones.Add(b);
            // Any weight at all, as the bake marks a vertex active, so the copy
            // is the plug's triangles. A corner with no frame in its bake entry
            // would hide alone: the deform still asks the entry for one.
            bool On(int v)
            {
                if (YapsBaker.WeightOnPlug(weights[v], plugBones) <= 0) return false;
                Vector3 nv = normals[v], tv = tangents[v];
                if (nv.sqrMagnitude < 1e-12f || tv.sqrMagnitude < 1e-12f) return false;
                nv.Normalize();
                tv.Normalize();
                // A hundred times the deform's own refusal, so rounding never crosses it.
                return (tv - nv * Vector3.Dot(tv, nv)).sqrMagnitude >= 1e-6f;
            }
            var tris = dupFrom.GetTriangles(submesh);
            var kept = new List<int>();
            var copied = new SortedSet<int>();
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                if (!On(tris[i]) || !On(tris[i + 1]) || !On(tris[i + 2])) continue;
                for (int c = 0; c < 3; c++) { kept.Add(tris[i + c]); copied.Add(tris[i + c]); }
            }
            if (kept.Count == 0) { why = "no triangle has all three corners on the plug"; return null; }
            from = copied.ToArray();
            var copyOf = new Dictionary<int, int>();
            for (int i = 0; i < from.Length; i++) copyOf[from[i]] = n + i;
            int total = n + from.Length;

            var mesh = new Mesh
            {
                name = dupFrom.name + Suffix,
                // A copy past 65535 has no 16-bit index.
                indexFormat = total > 65535 ? IndexFormat.UInt32 : dupFrom.indexFormat,
            };
            mesh.vertices = Extend(dupFrom.vertices, from.Length, anchor);
            mesh.normals = Extend(normals, from.Length, up);
            mesh.tangents = Extend(tangents, from.Length, new Vector4(forward.x, forward.y, forward.z, 1));
            mesh.colors = Extend(dupFrom.colors, from);
            var uv = new List<Vector4>();
            for (int channel = 0; channel < 8; channel++)
            {
                uv.Clear();
                dupFrom.GetUVs(channel, uv);
                // The readout's own rule, so the two extend a mesh alike.
                if (uv.Count == 0 && channel > 0) continue;
                while (uv.Count < n) uv.Add(Vector4.zero);
                for (int i = 0; i < from.Length; i++) uv.Add(uv[from[i]]);
                mesh.SetUVs(channel, uv);
            }

            // The originals keep their full weight lists, more than four included.
            var perVertex = dupFrom.GetBonesPerVertex();
            var all = dupFrom.GetAllBoneWeights();
            var perOut = new NativeArray<byte>(total, Allocator.Temp);
            var allOut = new NativeArray<BoneWeight1>(all.Length + from.Length, Allocator.Temp);
            try
            {
                NativeArray<byte>.Copy(perVertex, perOut, n);
                NativeArray<BoneWeight1>.Copy(all, allOut, all.Length);
                for (int i = 0; i < from.Length; i++)
                {
                    perOut[n + i] = 1;
                    allOut[all.Length + i] = new BoneWeight1 { boneIndex = bone, weight = 1 };
                }
                mesh.SetBoneWeights(perOut, allOut);
            }
            finally
            {
                perOut.Dispose();
                allOut.Dispose();
            }
            mesh.bindposes = dupFrom.bindposes;

            mesh.subMeshCount = dupFrom.subMeshCount;
            for (int s = 0; s < dupFrom.subMeshCount; s++)
            {
                var indices = dupFrom.GetIndices(s);
                if (s == submesh) indices = indices.Concat(kept.Select(v => copyOf[v])).ToArray();
                mesh.SetIndices(indices, dupFrom.GetTopology(s), s);
            }

            var dv = new Vector3[n];
            var dn = new Vector3[n];
            var dt = new Vector3[n];
            for (int s = 0; s < dupFrom.blendShapeCount; s++)
                for (int f = 0; f < dupFrom.GetBlendShapeFrameCount(s); f++)
                {
                    dupFrom.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    // None on a copy: a delta would open its point.
                    mesh.AddBlendShapeFrame(dupFrom.GetBlendShapeName(s), dupFrom.GetBlendShapeFrameWeight(s, f),
                        Extend(dv, from.Length, Vector3.zero), Extend(dn, from.Length, Vector3.zero),
                        Extend(dt, from.Length, Vector3.zero));
                }
            mesh.bounds = dupFrom.bounds;
            return mesh;
        }

        static T[] Extend<T>(T[] source, int[] from)
        {
            if (source == null || source.Length == 0) return source;
            var to = new T[source.Length + from.Length];
            Array.Copy(source, to, source.Length);
            for (int i = 0; i < from.Length; i++) to[source.Length + i] = source[from[i]];
            return to;
        }

        static T[] Extend<T>(T[] source, int count, T value)
        {
            var to = new T[source.Length + count];
            Array.Copy(source, to, source.Length);
            for (int i = 0; i < count; i++) to[source.Length + i] = value;
            return to;
        }

        // The plain bake grown to the duplicate: an original's entry as it was,
        // a copy's its original's marked active, every shape block laid out
        // again for the new count. Padded to whole rows as the baker pads.
        public static float[] ExtendBake(float[] floats, int n, int[] from, int shapes, int width)
        {
            int total = n + from.Length;
            int used = 1 + total * 10 + shapes * total * 9;
            var to = new float[width * Math.Max(1, (used + width - 1) / width)];
            Array.Copy(floats, to, 1 + n * 10);
            for (int i = 0; i < from.Length; i++)
            {
                Array.Copy(floats, 1 + from[i] * 10, to, 1 + (n + i) * 10, 9);
                to[1 + (n + i) * 10 + 9] = 1;
            }
            for (int s = 0; s < shapes; s++)
            {
                int was = 1 + n * 10 + s * n * 9, now = 1 + total * 10 + s * total * 9;
                Array.Copy(floats, was, to, now, n * 9);
                for (int i = 0; i < from.Length; i++) Array.Copy(floats, was + from[i] * 9, to, now + (n + i) * 9, 9);
            }
            return to;
        }

        static string Safe(string name)
        {
            foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
            return name;
        }
    }
}
#endif
