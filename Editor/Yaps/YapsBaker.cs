// Phase 1c. Bakes a plug mesh into the texture the deform reads.
//
// Inspired by VRCFury's SPS, which invented this technique for VRChat.
// The format is implemented from its documented layout, not from their
// code. See Tools/SpsSpike/LICENSE-POSTURE.md.
//
// ---------------------------------------------------------------------
// WHAT GETS BAKED, AND IN WHICH SPACE
// ---------------------------------------------------------------------
//
// The deform needs each vertex expressed as a point on a rod: Z along the
// shaft, X and Y off its centre line. So every vertex is measured in the
// PLUG ROOT's frame — its rotation and position, deliberately without its
// scale, so the numbers come out in the same units the renderer works in
// and the shader can compare them against skinned positions directly.
//
// Vertices are placed through the bind pose rather than read raw. A
// skinned mesh's stored vertices sit whereever the modeller left them;
// where the plug actually IS comes from bone × bindpose, which is what
// skinning itself computes.
//
// The `active` weight is how much of a vertex belongs to the plug — the
// total skin weight it has on the plug's own bone chain. That gives the
// feather at the base for free: a vertex half-weighted to the hip is half
// deformed, which is exactly right, and it is why the shader multiplies
// by this rather than thresholding it.
#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsBaker
    {
        const string Category = "YAPS";
        const int TextureWidth = 8192;
        const int FloatsPerVertex = 10;

        public class Result
        {
            public Texture2D Bake;
            public int VertexCount;
            public float Length;        // plug-local, along +Z
            public float ActiveVertices;
            public bool FromSkinnedMesh;
        }

        public static Result Bake(Renderer renderer, Transform plugRoot, string outputDir,
            BridgeReport report, out string failure)
        {
            failure = null;
            if (renderer == null || plugRoot == null)
            {
                failure = "no renderer or no plug root";
                return null;
            }

            var mesh = MeshOf(renderer);
            if (mesh == null)
            {
                failure = "the renderer has no mesh";
                return null;
            }
            if (!mesh.isReadable)
            {
                failure = $"\"{mesh.name}\" is not marked Read/Write, so its vertices cannot be " +
                          "read at all — tick Read/Write Enabled on the model importer";
                return null;
            }

            var skin = renderer as SkinnedMeshRenderer;
            if (!TryPlaceVertices(mesh, skin, renderer.transform,
                    out var worldPositions, out var worldNormals, out var worldTangents,
                    out var activeWeights, plugRoot, out failure))
            {
                return null;
            }

            // The plug's frame WITHOUT its scale, so baked units match the
            // renderer's and the shader can compare them to skinned
            // positions without a conversion it would have to be told about.
            var toPlug = Matrix4x4.TRS(plugRoot.position, plugRoot.rotation, Vector3.one).inverse;

            int count = worldPositions.Count;
            var positions = new Vector3[count];
            var normals = new Vector3[count];
            var tangents = new Vector3[count];
            float length = 0f;
            int active = 0;

            for (int i = 0; i < count; i++)
            {
                positions[i] = toPlug.MultiplyPoint3x4(worldPositions[i]);
                normals[i] = toPlug.MultiplyVector(worldNormals[i]).normalized;
                tangents[i] = toPlug.MultiplyVector(worldTangents[i]).normalized;
                if (activeWeights[i] > 0.001f)
                {
                    active++;
                    length = Mathf.Max(length, positions[i].z);
                }
            }

            if (active == 0)
            {
                failure = "no vertex on this renderer is weighted to the plug's bone chain, so " +
                          "there is nothing to bake";
                return null;
            }
            if (length <= 0.0001f)
            {
                failure = "the plug measures no length along its own +Z — its root is probably " +
                          "pointing the wrong way";
                return null;
            }

            var texture = WriteTexture(positions, normals, tangents, activeWeights, count);
            Directory.CreateDirectory(outputDir);
            string path = AssetDatabase.GenerateUniqueAssetPath(
                outputDir + "/YAPS " + Sanitise(renderer.name) + " bake.asset");
            AssetDatabase.CreateAsset(texture, path);

            // The deform throws vertices well outside the rest pose, and
            // Unity culls on the mesh's own bounds — so a plug that bends
            // toward someone can vanish mid-bend precisely when it matters.
            ExtendBounds(renderer, mesh, length);

            report?.Converted(Category, renderer.name,
                $"Baked {active} of {count} vertices, plug length {length:0.###} m. " +
                "Each vertex is stored in the plug root's own frame, with its skin weight on " +
                "the plug's bone chain as the blend weight, so the base feathers into the body " +
                "instead of shearing off.");

            return new Result
            {
                Bake = texture,
                VertexCount = count,
                Length = length,
                ActiveVertices = active,
                FromSkinnedMesh = skin != null,
            };
        }

        // Applies the bake to a material, cloning it first so two renderers
        // sharing one material cannot overwrite each other's vertex counts.
        // That is not hypothetical: a real avatar in the corpus has a
        // clothing material sharing a renderer with a plug, and its bake
        // data disagreed with its own declared vertex count.
        public static Material Apply(Result result, Material source, Shader patchedShader,
            string outputDir, bool skinned)
        {
            var clone = new Material(source) { name = source.name + " (YAPS)" };
            if (patchedShader != null)
            {
                clone.shader = patchedShader;
            }
            clone.SetTexture("_YAPS_Bake", result.Bake);
            clone.SetFloat("_YAPS_VertexCount", result.VertexCount);
            clone.SetFloat("_YAPS_Length", result.Length);
            clone.SetFloat("_YAPS_BakeScale", 1f);   // baked in renderer units by construction
            clone.SetFloat("_YAPS_FrameFromVertex", skinned ? 1f : 0f);

            Directory.CreateDirectory(outputDir);
            AssetDatabase.CreateAsset(clone, AssetDatabase.GenerateUniqueAssetPath(
                outputDir + "/" + Sanitise(clone.name) + ".mat"));
            return clone;
        }

        // --- placing the vertices -------------------------------------

        static bool TryPlaceVertices(Mesh mesh, SkinnedMeshRenderer skin, Transform rendererTransform,
            out List<Vector3> positions, out List<Vector3> normals, out List<Vector3> tangents,
            out List<float> active, Transform plugRoot, out string failure)
        {
            failure = null;
            positions = new List<Vector3>();
            normals = new List<Vector3>();
            tangents = new List<Vector3>();
            active = new List<float>();

            var meshVertices = mesh.vertices;
            var meshNormals = mesh.normals;
            var meshTangents = mesh.tangents;

            if (skin == null || skin.bones == null || skin.bones.Length == 0)
            {
                // A plain mesh renderer: its transform IS the placement.
                var toWorld = rendererTransform.localToWorldMatrix;
                for (int i = 0; i < meshVertices.Length; i++)
                {
                    positions.Add(toWorld.MultiplyPoint3x4(meshVertices[i]));
                    normals.Add(i < meshNormals.Length
                        ? toWorld.MultiplyVector(meshNormals[i]) : Vector3.forward);
                    tangents.Add(i < meshTangents.Length
                        ? toWorld.MultiplyVector(meshTangents[i]) : Vector3.right);
                    active.Add(1f);
                }
                return true;
            }

            var bones = skin.bones;
            var bindposes = mesh.bindposes;
            if (bindposes == null || bindposes.Length == 0)
            {
                failure = "the skinned mesh has no bindposes";
                return false;
            }

            // Which bones count as "the plug": its root and everything
            // beneath it, so a plug with its own little chain of bones is
            // baked whole rather than only at its base.
            var plugBones = new HashSet<int>();
            for (int b = 0; b < bones.Length; b++)
            {
                if (bones[b] != null && bones[b].IsChildOf(plugRoot))
                {
                    plugBones.Add(b);
                }
            }

            var weights = mesh.boneWeights;
            for (int i = 0; i < meshVertices.Length; i++)
            {
                var w = i < weights.Length ? weights[i] : default;
                // Skinning itself is bone × bindpose; anything else places
                // the vertex where the modeller left it, not where the
                // avatar wears it.
                Matrix4x4 place = Blend(bones, bindposes, w);
                positions.Add(place.MultiplyPoint3x4(meshVertices[i]));
                normals.Add(i < meshNormals.Length
                    ? place.MultiplyVector(meshNormals[i]) : Vector3.forward);
                tangents.Add(i < meshTangents.Length
                    ? place.MultiplyVector(meshTangents[i]) : Vector3.right);
                active.Add(WeightOnPlug(w, plugBones));
            }
            return true;
        }

        static Matrix4x4 Blend(Transform[] bones, Matrix4x4[] bindposes, BoneWeight w)
        {
            var result = new Matrix4x4();
            bool any = false;
            void Add(int index, float weight)
            {
                if (weight <= 0f || index < 0 || index >= bones.Length || index >= bindposes.Length
                    || bones[index] == null)
                {
                    return;
                }
                Matrix4x4 m = bones[index].localToWorldMatrix * bindposes[index];
                for (int e = 0; e < 16; e++)
                {
                    result[e] += m[e] * weight;
                }
                any = true;
            }
            Add(w.boneIndex0, w.weight0);
            Add(w.boneIndex1, w.weight1);
            Add(w.boneIndex2, w.weight2);
            Add(w.boneIndex3, w.weight3);
            return any ? result : Matrix4x4.identity;
        }

        static float WeightOnPlug(BoneWeight w, HashSet<int> plugBones)
        {
            float total = 0f;
            if (plugBones.Contains(w.boneIndex0)) total += w.weight0;
            if (plugBones.Contains(w.boneIndex1)) total += w.weight1;
            if (plugBones.Contains(w.boneIndex2)) total += w.weight2;
            if (plugBones.Contains(w.boneIndex3)) total += w.weight3;
            return Mathf.Clamp01(total);
        }

        // --- the texture ----------------------------------------------

        static Texture2D WriteTexture(Vector3[] positions, Vector3[] normals, Vector3[] tangents,
            List<float> active, int count)
        {
            int floats = 1 + count * FloatsPerVertex;
            int height = Mathf.Max(1, Mathf.CeilToInt((float) floats / TextureWidth));
            var pixels = new Color32[TextureWidth * height];

            int at = 0;
            Write(pixels, ref at, 0f);   // header
            for (int i = 0; i < count; i++)
            {
                Write(pixels, ref at, positions[i].x);
                Write(pixels, ref at, positions[i].y);
                Write(pixels, ref at, positions[i].z);
                Write(pixels, ref at, normals[i].x);
                Write(pixels, ref at, normals[i].y);
                Write(pixels, ref at, normals[i].z);
                Write(pixels, ref at, tangents[i].x);
                Write(pixels, ref at, tangents[i].y);
                Write(pixels, ref at, tangents[i].z);
                Write(pixels, ref at, active[i]);
            }

            // Point filtering and no mips are load-bearing, not tidiness:
            // the shader reads these pixels as raw bytes and reassembles
            // floats from them, so any interpolation corrupts the values.
            var texture = new Texture2D(TextureWidth, height, TextureFormat.RGBA32, false, true)
            {
                name = "YAPS Bake",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        static void Write(Color32[] pixels, ref int at, float value)
        {
            var bytes = BitConverter.GetBytes(value);
            pixels[at++] = new Color32(bytes[0], bytes[1], bytes[2], bytes[3]);
        }

        // --- housekeeping ---------------------------------------------

        static void ExtendBounds(Renderer renderer, Mesh mesh, float length)
        {
            if (renderer is SkinnedMeshRenderer skin)
            {
                // The cheapest correct answer for a skinned mesh, and it
                // also spares us guessing how far a bend can travel.
                skin.updateWhenOffscreen = true;
                return;
            }
            var bounds = mesh.bounds;
            bounds.Expand(length * 2f);
            mesh.bounds = bounds;
        }

        static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skin)
            {
                return skin.sharedMesh;
            }
            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        static string Sanitise(string name)
            => new string(name.Select(c => char.IsLetterOrDigit(c) || c == ' ' ? c : '_').ToArray());
    }
}
#endif
