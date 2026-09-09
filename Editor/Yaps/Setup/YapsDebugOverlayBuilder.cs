// The in-game readout for a plug that will not behave.
//
// A strip of six cells painted by our own shader, saying who resolved
// the socket, whether the plug is bending and what the atlas saw. The
// plug's own debug view answers in LENGTH and straightens the plug to
// do it, so the bend and the reason for the bend can never be read at
// the same time. This leaves the plug alone and reports beside it.
//
// DRAWN BY THE PLUG'S OWN RENDERER, as four extra vertices in their own
// submesh with their own material slot. The first version was a separate
// object beside the plug, and a separate renderer cannot see two things
// the plug sees. The contact channel and the enabled flag arrive as
// animated material properties, which land in the plug renderer's
// property block and nowhere else; and the plug's frame is recovered
// from its own skinned vertices. So that readout resolved from a bone
// matrix with no channel, and reported nobody while the plug was bent
// round a socket. A second opinion is not a readout.
//
// Sharing the renderer shares the block. The four vertices are skinned
// exactly like one plug vertex, the anchor, so they land where it lands
// and the shader recovers the frame the deform recovers, from the same
// bake entry. The plug's own material never draws them: they sit past
// _YAPS_VertexCount, in a submesh it does not own.
//
// Built only when the plug asks for it, and the scanner refuses an upload
// while one exists.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvatarBridge.Yaps;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarBridge
{
    public static class YapsDebugOverlayBuilder
    {
        const string Category = "YAPS penetration system";
        // The object the first version built beside the plug. Still
        // removed, so an avatar built with it does not keep a dead quad.
        public const string ObjectName = "YAPS Debug Overlay";
        const string ShaderName = "YAPS/Debug Overlay";
        const string Folder = "Assets/YAPS/Generated";
        const string MeshSuffix = " readout";
        const int Corners = 4;

        // The toolkit's entry: the plug component owns the tickbox.
        public static void Apply(YapsPlug plug, YapsBaker.Result result, Material patched,
            BridgeReport report)
        {
            if (plug == null)
            {
                return;
            }
            Apply(plug.transform, plug.name, plug.debugOverlay, result, patched, report, plug);
        }

        // Build, refresh or remove. Safe to run again: the last build is put
        // back before this one starts.
        //
        // TAKES THE ROOT AND THE FLAG RATHER THAN THE COMPONENT, because the
        // converter decides from its settings and only writes the component
        // afterwards. A version of this that needed the flag on the component
        // built readouts for the toolkit alone and left every converted plug,
        // which is most of them, with nothing.
        public static void Apply(Transform plugRoot, string label, bool wanted,
            YapsBaker.Result result, Material patched, BridgeReport report, YapsPlug record = null)
        {
            if (plugRoot == null)
            {
                return;
            }
            Transform parent = result != null && result.Root != null ? result.Root : plugRoot;
            var old = parent.Find(ObjectName);
            if (old != null)
            {
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            }
            if (record == null)
            {
                record = plugRoot.GetComponent<YapsPlug>();
            }
            Restore(record);
            if (!wanted)
            {
                return;
            }

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                report?.Warning(Category, label,
                    "The debug readout was asked for and its shader could not be found, so none " +
                    "was built. The plug itself is unaffected.");
                return;
            }
            if (result == null || result.Renderer == null || result.AnchorVertex < 0)
            {
                report?.Warning(Category, label,
                    "The debug readout was asked for and the bake has no vertex for it to hang " +
                    "from, so none was built. The plug itself is unaffected.");
                return;
            }
            if (record == null)
            {
                // Without somewhere to record the mesh this replaces, a later
                // build could not put it back, and the readout would outlive
                // the tick that asked for it.
                report?.Warning(Category, label,
                    "The debug readout was asked for and there is no plug component to remember " +
                    "the mesh it replaces, so none was built.");
                return;
            }

            var renderer = result.Renderer;
            var source = MeshOf(renderer);
            if (source == null)
            {
                report?.Warning(Category, label,
                    "The debug readout was asked for and the plug's renderer has no mesh.");
                return;
            }
            var mesh = WithReadout(source, result.AnchorVertex, out string why);
            if (mesh == null)
            {
                report?.Warning(Category, label,
                    "The debug readout could not be added to \"" + source.name + "\": " + why +
                    ". The plug itself is unaffected.");
                return;
            }

            string dir = DirOf(result.Bake) ?? Folder;
            Directory.CreateDirectory(dir);
            string meshPath = dir + "/YAPS " + Safe(source.name) + MeshSuffix + ".asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var material = MaterialFor(shader, dir, label);
            Copy(patched, material, label, report);
            material.SetFloat("_YAPS_AnchorVertex", result.AnchorVertex);
            EditorUtility.SetDirty(material);

            record.readoutRenderer = renderer;
            record.readoutReplaced = source;
            EditorUtility.SetDirty(record);

            var mats = renderer.sharedMaterials.ToList();
            mats.Add(material);
            SetMesh(renderer, mesh);
            renderer.sharedMaterials = mats.ToArray();
            EditorUtility.SetDirty(renderer);

            report?.Converted(Category, label,
                "A debug readout was drawn on this plug. Six cells, left to right: who " +
                "resolved the socket, whether it is bending, how far away the socket is, what " +
                "the screen atlas read, whether the atlas is on the camera drawing this view, and " +
                "whether this plug asks for the atlas at all. Grey or black is nothing, red is a " +
                "fault; in the first cell cyan, amber or green is the contact channel, a marker " +
                "light or the screen atlas answering, and in the second amber is a plug that " +
                "found its socket while its toggle holds it off. It is visible to everyone who " +
                "can see the plug, and an upload is refused while it exists: untick it when you " +
                "are done.");
        }

        // Put the mesh the last build replaced back, and drop its slot.
        //
        // Runs before every bake as well as before every build: a bake taken
        // over the readout copy would count its four vertices as the plug's,
        // and pick one of them as the next anchor.
        public static void Restore(YapsPlug plug)
        {
            if (plug == null || plug.readoutRenderer == null)
            {
                return;
            }
            var renderer = plug.readoutRenderer;
            var current = MeshOf(renderer);
            if (plug.readoutReplaced != null && current != null
                && current.name.EndsWith(MeshSuffix, StringComparison.Ordinal))
            {
                SetMesh(renderer, plug.readoutReplaced);
            }
            var mats = renderer.sharedMaterials.ToList();
            for (int i = mats.Count - 1; i >= 0; i--)
            {
                if (IsReadout(mats[i]))
                {
                    mats.RemoveAt(i);
                }
            }
            renderer.sharedMaterials = mats.ToArray();
            EditorUtility.SetDirty(renderer);
            plug.readoutRenderer = null;
            plug.readoutReplaced = null;
            EditorUtility.SetDirty(plug);
        }

        // By shader, and by name when the shader is gone: a project that
        // lost the shader shows the slot magenta and still has to shed it.
        public static bool IsReadout(Material m)
        {
            if (m == null)
            {
                return false;
            }
            if (m.shader != null && m.shader.name == ShaderName)
            {
                return true;
            }
            return m.name.StartsWith(ObjectName, StringComparison.Ordinal);
        }

        // The plug's mesh with four more vertices, every one a copy of the
        // anchor: position, normal, tangent, every uv but the first, bone
        // weights and every blendshape delta. Skinned and shaped the same,
        // they arrive in the vertex shader where the anchor arrives. The
        // first uv is the cell layout instead; the shader billboards the
        // corners from the recovered root, so where they rest is irrelevant.
        static Mesh WithReadout(Mesh source, int anchor, out string why)
        {
            why = null;
            int n = source.vertexCount;
            if (anchor < 0 || anchor >= n)
            {
                why = "the anchor vertex is not on this mesh";
                return null;
            }
            Vector3[] vertices;
            try
            {
                vertices = source.vertices;
            }
            catch (Exception e)
            {
                why = "it would not hand over its vertices (" + e.GetType().Name +
                      "): tick Read/Write Enabled on the model importer";
                return null;
            }

            var mesh = new Mesh { name = source.name + MeshSuffix, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = Extend(vertices, anchor);
            mesh.normals = Extend(source.normals, anchor);
            mesh.tangents = Extend(source.tangents, anchor);
            mesh.colors = Extend(source.colors, anchor);

            var uv = new List<Vector4>();
            for (int channel = 0; channel < 8; channel++)
            {
                uv.Clear();
                source.GetUVs(channel, uv);
                if (uv.Count == 0 && channel > 0)
                {
                    continue;
                }
                while (uv.Count < n)
                {
                    uv.Add(Vector4.zero);
                }
                if (channel == 0)
                {
                    uv.Add(new Vector4(0f, 0f));
                    uv.Add(new Vector4(1f, 0f));
                    uv.Add(new Vector4(0f, 1f));
                    uv.Add(new Vector4(1f, 1f));
                }
                else
                {
                    for (int i = 0; i < Corners; i++)
                    {
                        uv.Add(uv[anchor]);
                    }
                }
                mesh.SetUVs(channel, uv);
            }

            // The full weight list, not the four-per-vertex view: a mesh
            // imported with more than four influences keeps them.
            var perVertex = source.GetBonesPerVertex();
            if (perVertex.Length == n)
            {
                var weights = source.GetAllBoneWeights();
                int start = 0;
                for (int i = 0; i < anchor; i++)
                {
                    start += perVertex[i];
                }
                int count = perVertex[anchor];
                var perVertexOut = new NativeArray<byte>(n + Corners, Allocator.Temp);
                var weightsOut = new NativeArray<BoneWeight1>(weights.Length + Corners * count, Allocator.Temp);
                try
                {
                    NativeArray<byte>.Copy(perVertex, perVertexOut, n);
                    NativeArray<BoneWeight1>.Copy(weights, weightsOut, weights.Length);
                    for (int c = 0; c < Corners; c++)
                    {
                        perVertexOut[n + c] = (byte) count;
                        for (int k = 0; k < count; k++)
                        {
                            weightsOut[weights.Length + c * count + k] = weights[start + k];
                        }
                    }
                    mesh.SetBoneWeights(perVertexOut, weightsOut);
                }
                finally
                {
                    perVertexOut.Dispose();
                    weightsOut.Dispose();
                }
                mesh.bindposes = source.bindposes;
            }

            mesh.subMeshCount = source.subMeshCount + 1;
            for (int s = 0; s < source.subMeshCount; s++)
            {
                mesh.SetIndices(source.GetIndices(s), source.GetTopology(s), s);
            }
            mesh.SetTriangles(new[] { n, n + 2, n + 1, n + 2, n + 3, n + 1 }, source.subMeshCount);

            var dv = new Vector3[n];
            var dn = new Vector3[n];
            var dt = new Vector3[n];
            for (int s = 0; s < source.blendShapeCount; s++)
            {
                string name = source.GetBlendShapeName(s);
                int frames = source.GetBlendShapeFrameCount(s);
                for (int f = 0; f < frames; f++)
                {
                    source.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    mesh.AddBlendShapeFrame(name, source.GetBlendShapeFrameWeight(s, f),
                        Extend(dv, anchor), Extend(dn, anchor), Extend(dt, anchor));
                }
            }

            mesh.bounds = source.bounds;
            return mesh;
        }

        // An empty channel stays empty: Unity wants every channel either
        // absent or exactly the vertex count long.
        static T[] Extend<T>(T[] from, int anchor)
        {
            if (from == null || from.Length == 0)
            {
                return from;
            }
            var to = new T[from.Length + Corners];
            Array.Copy(from, to, from.Length);
            for (int i = 0; i < Corners; i++)
            {
                to[from.Length + i] = from[anchor];
            }
            return to;
        }

        static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skin)
            {
                return skin.sharedMesh;
            }
            var filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            return filter != null ? filter.sharedMesh : null;
        }

        static void SetMesh(Renderer renderer, Mesh mesh)
        {
            if (renderer is SkinnedMeshRenderer skin)
            {
                skin.sharedMesh = mesh;
                return;
            }
            var filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            if (filter != null)
            {
                filter.sharedMesh = mesh;
            }
        }

        // Beside the bake, which both builders already keep per avatar.
        static string DirOf(Texture2D bake)
        {
            string path = bake != null ? AssetDatabase.GetAssetPath(bake) : null;
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            return Path.GetDirectoryName(path)?.Replace('\\', '/');
        }

        // EVERY _YAPS_ VALUE THE PLUG HAS, copied by name rather than by a
        // list kept here. A hand-written list rots the first time a property
        // is added to the patcher's block, and the cell it feeds then reads
        // zero without saying so. Anything the overlay shader cannot hold is
        // reported, so the drift is loud.
        //
        // The animated ones arrive through the renderer's block at runtime
        // regardless; this is for the values that only live on the material.
        static void Copy(Material from, Material to, string label, BridgeReport report)
        {
            if (from == null || to == null)
            {
                return;
            }
            var missing = new List<string>();
            int count = ShaderUtil.GetPropertyCount(from.shader);
            for (int i = 0; i < count; i++)
            {
                string name = ShaderUtil.GetPropertyName(from.shader, i);
                if (!name.StartsWith("_YAPS_", StringComparison.Ordinal))
                {
                    continue;
                }
                var kind = ShaderUtil.GetPropertyType(from.shader, i);
                if (!to.HasProperty(name))
                {
                    if (kind == ShaderUtil.ShaderPropertyType.Float
                        || kind == ShaderUtil.ShaderPropertyType.Range
                        || kind == ShaderUtil.ShaderPropertyType.Vector)
                    {
                        missing.Add(name);
                    }
                    continue;
                }
                switch (kind)
                {
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        to.SetFloat(name, from.GetFloat(name));
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        to.SetVector(name, from.GetVector(name));
                        break;
                    case ShaderUtil.ShaderPropertyType.Color:
                        to.SetColor(name, from.GetColor(name));
                        break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        to.SetTexture(name, from.GetTexture(name));
                        break;
                }
            }
            EditorUtility.SetDirty(to);
            if (missing.Count == 0)
            {
                return;
            }
            report?.Warning(Category, label,
                $"The debug readout could not carry {missing.Count} of the plug's setting(s): " +
                string.Join(", ", missing) + ". Cells that depend on them read as nothing rather " +
                "than as their real value. The readout's shader needs the same property added.");
        }

        static Material MaterialFor(Shader shader, string dir, string label)
        {
            string path = dir + "/" + ObjectName + " " + Safe(label) + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = shader;
                return existing;
            }
            var made = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(made, path);
            return made;
        }

        static string Safe(string name)
        {
            foreach (char bad in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(bad, '_');
            }
            return name;
        }
    }
}
#endif
