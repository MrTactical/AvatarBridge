// A throwaway plug, for testing the resolver on an avatar that has none.
//
// The rule under test lives in the plug's shader, so sockets alone measure
// nothing. This makes the smallest thing that carries the shipped resolver: a
// tube along +Z on YAPS/Simple Lit, which exists for exactly this, baked
// through the real builder so the material is patched the way a real one is.
//
// A tube rather than Unity's cylinder primitive: that one has two rings and a
// bend needs segments along its length to show in.
//
// Plain MeshRenderer, never skinned. Unity skins into world space and hands a
// SkinnedMeshRenderer an identity matrix, so a skinned plug cannot work out
// where its own root is.
//
//   Tools/YAPS/Own-body test: add a throwaway plug
#if CVR_CCK_EXISTS && UNITY_EDITOR && AVATARBRIDGE_YAPS
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class TestPlugBuilder
    {
        const float Length = 0.22f;
        const float Radius = 0.022f;
        const int Rings = 24;
        const int Sides = 16;
        const string Dir = "Assets/YapsSpike";

        [MenuItem("Tools/YAPS/Own-body test: add a throwaway plug")]
        static void Add()
        {
            var selected = Selection.activeGameObject;
            var avatar = selected != null ? selected.GetComponentInParent<CVRAvatar>() : null;
            if (avatar == null)
            {
                Debug.Log("TEST PLUG: select the avatar to put it on first.");
                return;
            }

            int sockets = avatar.GetComponentsInChildren<YapsSocket>(true).Length;

            var go = new GameObject("YAPS Test Plug");
            Undo.RegisterCreatedObjectUndo(go, "Add a test plug");
            go.transform.SetParent(avatar.transform, false);
            // Hip height on the avatar's own forward, which is where a plug
            // lives and, more to the point, where its own sockets are near
            // enough to be the case under test.
            var hip = YapsFakePlayers.HipOf(avatar);
            go.transform.position = hip != null ? hip.position : avatar.transform.position;
            go.transform.rotation = avatar.transform.rotation;

            go.AddComponent<MeshFilter>().sharedMesh = Tube();
            var material = new Material(Shader.Find("YAPS/Simple Lit")) { name = "YapsTestPlug" };
            System.IO.Directory.CreateDirectory(Dir);
            AssetDatabase.DeleteAsset(Dir + "/YapsTestPlug.mat");
            AssetDatabase.CreateAsset(material, Dir + "/YapsTestPlug.mat");
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            var plug = go.AddComponent<YapsPlug>();
            plug.renderer = renderer;
            plug.lengthOverride = Length;

            var outcome = YapsNativeBuilder.Bake(plug);
            // The bake writes a PATCHED material and puts that on the renderer,
            // so the source material above is not the one carrying the flags.
            var built = outcome.Material != null ? outcome.Material : material;

            float self = built.HasProperty("_YAPS_SelfTag") ? built.GetFloat("_YAPS_SelfTag") : -99f;
            float atlas = built.HasProperty("_YAPS_UseAtlas") ? built.GetFloat("_YAPS_UseAtlas") : -99f;
            Debug.Log($"TEST PLUG on {avatar.name}: bake {(outcome.Ok ? "ok" : "FAILED")}" +
                (string.IsNullOrEmpty(outcome.Message) ? "" : " (" + outcome.Message + ")") +
                $", SelfTag {self:0.##}, atlas {atlas:0.##}. " +
                $"The avatar carries {sockets} YapsSocket(s)." + (sockets == 0
                    ? " NONE, so SelfTag is -1 and ownership is never asked: this avatar cannot be body A."
                    : " Move the plug so its own sockets are near, then run the own-body report."));

            Selection.activeGameObject = go;
        }

        // A tube from the origin along +Z. No caps: nothing here looks at them
        // and a cap ring at the base is one more thing to explain when the
        // bend pinches.
        static Mesh Tube()
        {
            string path = Dir + "/YapsTestPlugMesh.asset";
            var have = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (have != null) { return have; }

            var verts = new Vector3[(Rings + 1) * (Sides + 1)];
            var uvs = new Vector2[verts.Length];
            for (int r = 0; r <= Rings; r++)
            {
                float t = (float)r / Rings;
                for (int s = 0; s <= Sides; s++)
                {
                    float a = 2f * Mathf.PI * s / Sides;
                    int i = r * (Sides + 1) + s;
                    verts[i] = new Vector3(Mathf.Cos(a) * Radius, Mathf.Sin(a) * Radius, t * Length);
                    uvs[i] = new Vector2((float)s / Sides, t);
                }
            }

            var tris = new int[Rings * Sides * 6];
            int k = 0;
            for (int r = 0; r < Rings; r++)
            {
                for (int s = 0; s < Sides; s++)
                {
                    int a = r * (Sides + 1) + s;
                    int b = a + Sides + 1;
                    tris[k++] = a; tris[k++] = b; tris[k++] = a + 1;
                    tris[k++] = a + 1; tris[k++] = b; tris[k++] = b + 1;
                }
            }

            var mesh = new Mesh { name = "YAPS Test Plug" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            System.IO.Directory.CreateDirectory(Dir);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
#endif
