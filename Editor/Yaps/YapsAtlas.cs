// The screen atlas, editor side: the objects that write it and the one that
// grabs it. The protocol itself lives in yaps_atlas.cginc, which is where the
// shaders read it from.
//
// No VRChat types here on purpose, so the standalone toolkit can call it.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarBridge
{
    public static class YapsAtlas
    {
        // ON. It was off while nothing read the atlas, because writing costs a
        // handful of tiny draws per socket and a grab is a full screen copy per
        // camera with mirrors getting their own, so publishing to a screen
        // nobody reads charges the whole room for nothing. The resolver reads
        // it now, on both builders.
        //
        // Still unproven in ChilloutVR: what it costs in a crowded instance,
        // how it behaves in mirrors and in VR, and whether a viewer with custom
        // shaders blocked sees anything at all. It also paints: the payload is
        // colour, so an occupied cell puts a few pixels on screen.
        public const bool Enabled = true;

        // Must match YAPS_ATLAS_LEVELS in yaps_atlas.cginc. Only the mesh
        // needs it here, and only to know how many quads to make.
        const int Levels = 4;

        // Shared, not per conversion. Every socket on every avatar writes the
        // same protocol, so the mesh and the two materials are one asset each.
        const string Folder = "Assets/YAPS";

        const string SocketShader = "YAPS/Atlas Socket";
        const string GrabShader = "YAPS/Atlas Grab";
        const string ClearShader = "YAPS/Atlas Clear";

        // Unity culls on renderer bounds and a culled object never draws. A
        // socket culled out of frame stops publishing, and the grab has to
        // happen while the wearer is behind the camera, because whoever is
        // reading it is somewhere else.
        const float BoundsSize = 1000f;

        public static GameObject AddWriter(Transform parent, bool hole)
        {
            var shader = Shader.Find(SocketShader);
            if (parent == null || shader == null)
            {
                return null;
            }
            var host = new GameObject(hole ? "Atlas Writer (hole)" : "Atlas Writer (ring)");
            host.transform.SetParent(parent, false);
            host.AddComponent<MeshFilter>().sharedMesh = LevelQuads();

            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Kind(shader, hole);
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

        // The clear, the grab and every socket writer, told apart by shader
        // rather than by name or by where they sit, because a report that
        // offers the user something to DO about an object has to skip these.
        // A prop chip or an atlasing suggestion on this tool's own plumbing is
        // advice nobody can act on, and reads as a bug.
        public static bool IsPlumbing(Material m) =>
            m != null && m.shader != null
            && m.shader.name.StartsWith("YAPS/Atlas", System.StringComparison.Ordinal);

        // The clear, without which an empty cell reads as the opaque screen
        // and every cell looks occupied. One per room is enough and a second
        // is harmless: it paints alpha 0 over alpha 0.
        //
        // Placed in PIXELS from _ScreenParams like everything else in the
        // atlas, so the object's transform is ignored and import scale cannot
        // reach it. That is why neither this nor the grab is counter-scaled.
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

        static void Quiet(MeshRenderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // One unit quad per (level, home), flagged by z. The writer reads that
        // flag to decide which level it is publishing to and which of the
        // cell's two homes it is filling. Two homes because a socket that loses
        // a slot clash is still readable from the other one.
        //
        // The shader ignores this object's transform and writes clip space, so
        // the quads themselves are never seen. The matrix is the payload.
        //
        // EVERY POSITION IS ZERO and the corner lives in UV0 instead, which is
        // what keeps that true for a viewer who has custom shaders turned off.
        // ChilloutVR replaces the shader then, not the mesh, and the
        // replacement reads POSITION the ordinary way: corners in POSITION drew
        // these as metre-wide grey slabs hanging off the avatar. A degenerate
        // triangle rasterises nothing under any shader.
        static Mesh LevelQuads()
        {
            string path = Folder + "/YAPS Atlas Quads.asset";
            int quads = Levels * 2;
            var have = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            // The UV check rebuilds a mesh saved before the corners moved off
            // POSITION: the vertex count did not change, so counting alone
            // would keep handing back the drawable one.
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
            return Save(mesh, path, have != null);
        }

        // One unit quad. The clear shader scales it to the atlas rect in
        // clip space, so the mesh carries no size of its own. Corners in UV0
        // and every position zero, for the reason given above LevelQuads.
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
            return Save(mesh, path, have != null);
        }

        // One degenerate triangle. The grab pass writes no colour and no depth,
        // so the draw exists only to make the grab run, and a grab runs because
        // the pass is rendered rather than because it covers a pixel. It used
        // to be a millimetre across; zero is the same answer without depending
        // on how far away somebody stands.
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
            return Save(mesh, path, have != null);
        }

        static Mesh Save(Mesh mesh, string path, bool replacing)
        {
            Directory.CreateDirectory(Folder);
            if (replacing)
            {
                AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Material Kind(Shader shader, bool hole)
        {
            var m = Made(Folder + (hole ? "/YAPS Atlas Socket Hole.mat" : "/YAPS Atlas Socket Ring.mat"),
                shader);
            m.SetFloat("_YAPS_Kind", hole ? 1f : 0f);
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
