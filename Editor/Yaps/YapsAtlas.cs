// The screen atlas, editor side: its writers and grab. The protocol lives in
// yaps_atlas.cginc.
//
// Cost: a few tiny draws per socket and two full-screen grabs per camera,
// mirrors included. Unproven in ChilloutVR: crowded instances, mirrors, VR,
// and viewers with custom shaders blocked. The payload is colour, so the
// grab's second pass restores what the clear found under the rect.
//
// No VRChat types here on purpose, so the standalone toolkit can call it.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using AvatarBridge.Yaps;

namespace AvatarBridge
{
    public static class YapsAtlas
    {
        // Must match YAPS_ATLAS_LEVELS in yaps_atlas.cginc.
        const int Levels = 4;

        // Shared, not per conversion: every avatar writes the same protocol.
        const string Folder = "Assets/YAPS";

        const string SocketShader = "YAPS/Atlas Socket";
        const string GrabShader = "YAPS/Atlas Grab";
        const string ClearShader = "YAPS/Atlas Clear";
        const string ExitShader = "YAPS/Atlas Exit";
        const string RelayShader = "YAPS/Atlas Relay";

        // A culled renderer never draws, but a socket must publish and the grab
        // must run while its wearer is behind the camera.
        const float BoundsSize = 1000f;

        // Must match YAPS_KIND_* in yaps_atlas.cginc.
        public const int Ring = 0, Hole = 1, OneWayRing = 2;

        public static int KindOf(YapsSocket socket) =>
            socket.kind == YapsSocket.SocketKind.Hole ? Hole : socket.oneWay ? OneWayRing : Ring;

        public static GameObject AddWriter(Transform parent, int kind, IList<string> tags = null)
        {
            var shader = Shader.Find(SocketShader);
            if (parent == null || shader == null)
            {
                return null;
            }
            var host = new GameObject("Atlas Writer (" + KindName(kind).ToLowerInvariant() + ")");
            host.transform.SetParent(parent, false);
            host.AddComponent<MeshFilter>().sharedMesh = LevelQuads();

            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Kind(shader, kind, tags);
            Quiet(renderer);
            return host;
        }

        public static GameObject AddGrab(Transform root)
        {
            var shader = Shader.Find(GrabShader);
            if (root == null || shader == null)
            {
                return null;
            }
            var existing = root.Find(GrabName);
            if (existing != null)
            {
                return existing.gameObject;
            }

            var host = new GameObject(GrabName);
            host.transform.SetParent(root, false);
            host.AddComponent<MeshFilter>().sharedMesh = GrabTriangle();

            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Made(Folder + "/YAPS Atlas Grab.mat", shader);
            Quiet(renderer);
            return host;
        }

        public const string GrabName = "YAPS Atlas Grab";

        // Without a clear every cell reads occupied. One per room is enough, a
        // second is harmless. Placed in pixels, so import scale cannot reach it
        // and neither this nor the grab is counter-scaled.
        public static GameObject AddClear(Transform root)
        {
            var shader = Shader.Find(ClearShader);
            if (root == null || shader == null)
            {
                return null;
            }
            var existing = root.Find(ClearName);
            if (existing != null)
            {
                return existing.gameObject;
            }

            var host = new GameObject(ClearName);
            host.transform.SetParent(root, false);
            host.AddComponent<MeshFilter>().sharedMesh = UnitQuad();

            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Made(Folder + "/YAPS Atlas Clear.mat", shader);
            Quiet(renderer);
            return host;
        }

        public const string ClearName = "YAPS Atlas Clear";

        // A portal exit's writer, unkeyed: NumberWriters gives it the
        // entrance's number, so every door that renumbers rekeys it too.
        public static GameObject AddExit(Transform parent, string name)
        {
            var shader = Shader.Find(ExitShader);
            if (parent == null || shader == null) return null;
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.AddComponent<MeshFilter>().sharedMesh = HomeQuads();
            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Exit(0);
            Quiet(renderer);
            return host;
        }

        public static bool IsExit(Material m) => m != null && m.shader != null && m.shader.name == ExitShader;

        // One material per entrance number, shared like the socket writers'.
        public static Material Exit(int entrance)
        {
            var shader = Shader.Find(ExitShader);
            if (shader == null) return null;
            var m = Made(Folder + "/YAPS Atlas Exit" + (entrance > 0 ? " #" + entrance : "") + ".mat", shader);
            if (Mathf.RoundToInt(m.GetFloat("_YAPS_EntranceIndex")) != entrance)
            {
                m.SetFloat("_YAPS_EntranceIndex", entrance);
                EditorUtility.SetDirty(m);
            }
            return m;
        }

        // Skinned with no bones so its bounds can be the plug's box: Unity hands
        // a renderer the lights nearest its bounds, so it gets the plug's four.
        // Parented to the root bone so the box lands in the same place.
        // "[FPR]": the first-person copy switches off shadowless renderers.
        public static SkinnedMeshRenderer AddRelay(Renderer plug, int n)
        {
            var shader = Shader.Find(RelayShader);
            if (plug == null || shader == null) return null;
            var skinned = plug as SkinnedMeshRenderer;
            var space = skinned != null && skinned.rootBone != null ? skinned.rootBone : plug.transform;
            string name = RelayPrefix + n + " [FPR]";
            var host = space.Find(name);
            if (host == null)
            {
                host = new GameObject(name).transform;
                host.SetParent(space, false);
                Undo.RegisterCreatedObjectUndo(host.gameObject, "YAPS light relay");
            }
            host.gameObject.layer = plug.gameObject.layer;
            // Not ??: a missing component is Unity's fake null, which ?? takes as there.
            var relay = host.GetComponent<SkinnedMeshRenderer>();
            if (relay == null) relay = host.gameObject.AddComponent<SkinnedMeshRenderer>();
            relay.sharedMesh = HomeQuads();
            relay.sharedMaterial = Relay(n);
            relay.rootBone = host;
            relay.localBounds = skinned != null ? skinned.localBounds : plug.localBounds;
            relay.updateWhenOffscreen = false;
            relay.skinnedMotionVectors = false;
            Quiet(relay);
            return relay;
        }

        public const string RelayPrefix = "YAPS Relay #";

        public static bool IsRelay(Material m) => m != null && m.shader != null && m.shader.name == RelayShader;

        // One material per plug number, shared like the exits'.
        static Material Relay(int n)
        {
            var m = Made(Folder + "/YAPS Atlas Relay #" + n + ".mat", Shader.Find(RelayShader));
            if (Mathf.RoundToInt(m.GetFloat("_YAPS_PlugIndex")) != n)
            {
                m.SetFloat("_YAPS_PlugIndex", n);
                EditorUtility.SetDirty(m);
            }
            return m;
        }

        // LevelQuads for the directory's two homes, flagged by z the same way.
        static Mesh HomeQuads()
        {
            string path = Folder + "/YAPS Atlas Exit Quads.asset";
            var have = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (have != null && have.vertexCount == 8 && have.HasVertexAttribute(VertexAttribute.TexCoord0))
            {
                return have;
            }
            var corners = new List<Vector3>();
            var tris = new List<int>();
            for (int q = 0; q < 2; q++)
            {
                int b = corners.Count;
                corners.Add(new Vector3(-0.5f, -0.5f, q));
                corners.Add(new Vector3(0.5f, -0.5f, q));
                corners.Add(new Vector3(-0.5f, 0.5f, q));
                corners.Add(new Vector3(0.5f, 0.5f, q));
                tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
            }
            var mesh = new Mesh { name = "YAPS Atlas Exit Quads" };
            mesh.SetVertices(new Vector3[8]);
            mesh.SetUVs(0, corners);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * BoundsSize);
            return Save(mesh, path, have);
        }

        internal static void Quiet(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // One unit quad per (level, home), flagged by z. Two homes so a socket
        // that loses a slot clash is still readable from the other.
        //
        // EVERY POSITION IS ZERO, corners in UV0: with custom shaders off,
        // ChilloutVR swaps the shader, not the mesh, and corners in POSITION
        // drew metre-wide grey slabs. A degenerate triangle draws nothing.
        static Mesh LevelQuads()
        {
            string path = Folder + "/YAPS Atlas Quads.asset";
            int quads = Levels * 2;
            var have = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            // The UV check catches older meshes with corners in POSITION: same
            // vertex count, so counting alone would keep the drawable one.
            if (have != null && have.vertexCount == quads * 4
                && have.HasVertexAttribute(VertexAttribute.TexCoord0))
            {
                return have;
            }

            var verts = new List<Vector3>();
            var corners = new List<Vector3>();
            var tris = new List<int>();
            for (int q = 0; q < quads; q++)
            {
                int b = verts.Count;
                corners.Add(new Vector3(-0.5f, -0.5f, q));
                corners.Add(new Vector3(0.5f, -0.5f, q));
                corners.Add(new Vector3(-0.5f, 0.5f, q));
                corners.Add(new Vector3(0.5f, 0.5f, q));
                for (int c = 0; c < 4; c++) verts.Add(Vector3.zero);
                tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
            }
            var mesh = new Mesh { name = "YAPS Atlas Quads" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, corners);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * BoundsSize);
            return Save(mesh, path, have);
        }

        // The clear shader sizes it in clip space. Positions zero, see LevelQuads.
        static Mesh UnitQuad()
        {
            string path = Folder + "/YAPS Atlas Quad.asset";
            var have = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (have != null && have.HasVertexAttribute(VertexAttribute.TexCoord0))
            {
                return have;
            }
            var mesh = new Mesh { name = "YAPS Atlas Quad" };
            mesh.vertices = new[] { Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero };
            mesh.SetUVs(0, new List<Vector3>
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f)
            });
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * BoundsSize);
            return Save(mesh, path, have);
        }

        // Degenerate: a grab runs because the pass is rendered, not because it
        // covers a pixel, so zero size works at any distance.
        static Mesh GrabTriangle()
        {
            string path = Folder + "/YAPS Atlas Grab.asset";
            var have = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (have != null && have.vertexCount == 3 && have.bounds.extents.x >= BoundsSize * 0.4f
                && have.vertices[1] == Vector3.zero)
            {
                return have;
            }
            var mesh = new Mesh { name = "YAPS Atlas Grab" };
            mesh.vertices = new[] { Vector3.zero, Vector3.zero, Vector3.zero };
            mesh.triangles = new[] { 0, 1, 2 };
            // Last: assigning vertices recomputes bounds.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * BoundsSize);
            return Save(mesh, path, have);
        }

        // Rewritten in place, never deleted and recreated: that breaks the
        // file's identity, which earlier conversions point at.
        static Mesh Save(Mesh mesh, string path, Mesh have)
        {
            if (have != null)
            {
                EditorUtility.CopySerialized(mesh, have);
                Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(have);
                AssetDatabase.SaveAssetIfDirty(have);
                return have;
            }
            Directory.CreateDirectory(Folder);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // One material per kind, tag set and number, shared across avatars. Not
        // a property block: it is not serialised, so it does not survive upload.
        static Material Kind(Shader shader, int kind, IList<string> tags)
        {
            return Writer(shader, kind, YapsTags.Word(tags), 0);
        }

        static string KindName(int kind) => kind == Hole ? "Hole" : kind == OneWayRing ? "One-way Ring" : "Ring";

        // The same writer numbered among its wearer's own sockets, 1 to 15,
        // 0 for none. Null for anything that is not a writer material.
        public static Material Indexed(Material writer, int index)
        {
            if (writer == null || writer.shader == null || writer.shader.name != SocketShader) return null;
            return Writer(writer.shader, Mathf.RoundToInt(writer.GetFloat("_YAPS_Kind")),
                Mathf.RoundToInt(writer.GetFloat("_YAPS_SocketTags")), index);
        }

        static Material Writer(Shader shader, int kind, int word, int index)
        {
            string what = KindName(kind);
            string set = word == 0 ? "" : " " + word.ToString("X5");
            string number = index > 0 ? " #" + index : "";
            var m = Made(Folder + "/YAPS Atlas Socket " + what + set + number + ".mat", shader);
            m.SetFloat("_YAPS_Kind", kind);
            m.SetFloat("_YAPS_SocketTags", word);
            m.SetFloat("_YAPS_SocketIndex", index);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Made(string path, Shader shader)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = shader;
                return existing;
            }
            Directory.CreateDirectory(Folder);
            var made = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(made, path);
            return made;
        }
    }
}
