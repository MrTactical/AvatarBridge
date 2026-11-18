// A throwaway plug, for testing the resolver on an avatar that has none.
//
// The rule lives in the plug's shader: a tube along +Z on YAPS/Simple Lit,
// baked through the real builder. A tube because the cylinder primitive has
// two rings and cannot bend. Never skinned (identity matrix, no root).
// Sockets come with it: a socketless avatar bakes SelfTag -1, nothing is
// rejected, and the test passes without running.
//
//   Tools/Avatar Bridge/Dev/Atlas/Own-body test: build the rig
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

        [MenuItem("Tools/Avatar Bridge/Dev/Atlas/Own-body test: build the rig")]
        static void Rig()
        {
            var selected = Selection.activeGameObject;
            var avatar = selected != null ? selected.GetComponentInParent<CVRAvatar>() : null;
            if (avatar == null)
            {
                Debug.Log("TEST PLUG: select the avatar to put it on first.");
                return;
            }

            // Sockets first, or the plug bakes SelfTag -1.
            // NEAR is inside engagement onset, the case that matters. BAND is
            // past the atlas's reach, so it measures where the scan stops.
            int already = avatar.GetComponentsInChildren<YapsSocket>(true).Length;
            if (already == 0)
            {
                AddSocket(avatar, "YAPS Test Socket NEAR", Length * 1.0f);
                AddSocket(avatar, "YAPS Test Socket BAND", Length * 2.0f);
            }
            int sockets = avatar.GetComponentsInChildren<YapsSocket>(true).Length;

            // Clear and grab live on the avatar; without them a lower tier answers.
            YapsAtlas.AddClear(avatar.transform);
            YapsAtlas.AddGrab(avatar.transform);

            var go = new GameObject("YAPS Test Plug");
            Undo.RegisterCreatedObjectUndo(go, "Add a test plug");
            go.transform.SetParent(avatar.transform, false);
            // Hip height, forward: near enough its own sockets to be the case under test.
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
                $"The avatar carries {sockets} YapsSocket(s)." + (self < 0
                    ? " SELFTAG IS NEGATIVE: ownership is never asked and nothing will be rejected, " +
                      "so every result would pass without running."
                    : " Duplicate the avatar for body B, then run the own-body report."));

            Selection.activeGameObject = go;
        }

        // TEST 4: another body's socket beside the wearer's own, where the
        // relaxed rule can make things worse. The bodies nearly overlap, correctly:
        // atlas reach is ~1.2 plug lengths (~25 cm for 22 cm). Each socket sits
        // near its OWN hip, since the nearest hip decides ownership.
        [MenuItem("Tools/Avatar Bridge/Dev/Atlas/Own-body test: add the other body")]
        static void OtherBody()
        {
            var selected = Selection.activeGameObject;
            var avatar = selected != null ? selected.GetComponentInParent<CVRAvatar>() : null;
            var plug = avatar != null ? avatar.GetComponentInChildren<YapsPlug>(true) : null;
            if (plug == null)
            {
                Debug.Log("OTHER BODY: select the avatar carrying the test plug first.");
                return;
            }

            var root = plug.transform.position;
            var forward = plug.transform.forward;

            // Near enough to its own hip to own it, far enough from the plug to
            // be second in the chain.
            const float OwnAt = 0.06f;
            const float OtherAt = 0.18f;

            var ownSocket = Nearest(avatar);
            if (ownSocket == null)
            {
                Debug.Log("OTHER BODY: the avatar has no YapsSocket. Build the rig first.");
                return;
            }
            Undo.RecordObject(ownSocket.transform, "Move the wearer's socket");
            ownSocket.transform.position = root + forward * OwnAt;

            var copy = Object.Instantiate(avatar.gameObject, avatar.transform.parent);
            copy.name = "OTHER BODY";
            Undo.RegisterCreatedObjectUndo(copy, "Add the other body");

            // A second plug would answer the atlas as well and there is no way
            // to tell whose bend is whose.
            foreach (var extra in copy.GetComponentsInChildren<YapsPlug>(true))
            {
                Object.DestroyImmediate(extra.gameObject);
            }

            var otherAvatar = copy.GetComponent<CVRAvatar>();
            var otherSocket = Nearest(otherAvatar);
            if (otherSocket == null)
            {
                Debug.Log("OTHER BODY: the copy has no socket, so there is nothing to compete.");
                return;
            }
            // Move the whole body, never the socket alone: the socket has to
            // stay on its own hip or ownership cannot tell the two apart.
            copy.transform.position += (root + forward * OtherAt) - otherSocket.transform.position;

            float len = plug.lengthOverride > 0f ? plug.lengthOverride : Length;
            Debug.Log($"OTHER BODY: the wearer's socket is {OwnAt:0.00} m from the plug and the " +
                $"other body's is {OtherAt:0.00} m, both inside the roughly {Reach(len):0.00} m " +
                "the atlas can see. " +
                "View Off: the plug should take the wearer's own, being nearer. Then swap the two " +
                "distances and it should follow the other body's. Anything else, and admitting " +
                "own-body sockets has cost somebody the socket they were using.");

            Selection.activeGameObject = copy;
        }

        // Nearest socket to the avatar's hip, which is the one built as NEAR
        // unless the rig has been edited by hand.
        static YapsSocket Nearest(CVRAvatar avatar)
        {
            var hip = YapsFakePlayers.HipOf(avatar);
            var from = hip != null ? hip.position : avatar.transform.position;
            YapsSocket best = null;
            float bestD = float.MaxValue;
            foreach (var socket in avatar.GetComponentsInChildren<YapsSocket>(true))
            {
                float d = Vector3.Distance(socket.transform.position, from);
                if (d < bestD) { bestD = d; best = socket; }
            }
            return best;
        }

        // What the atlas can actually return, which is the three-cell scan and
        // not the far constant. Mirrors YapsResolveChain.
        static float Reach(float len)
        {
            float want = len * 0.5f;
            int lvl = Mathf.Clamp(Mathf.RoundToInt(Mathf.Log(want / 0.02f, 2f) * 0.5f), 0, 3);
            return len * 0.5f + 2f * 0.02f * Mathf.Pow(4f, lvl);
        }

        static void AddSocket(CVRAvatar avatar, string name, float ahead)
        {
            // No marker lights: they also resolve (0.75 vs the atlas's 0.66 on
            // "Resolved by"), too close to tell apart.
            var socket = YapsSocketBuilder.BuildPreviewSocket(
                name, YapsSocket.SocketKind.Hole, false);
            Undo.RegisterCreatedObjectUndo(socket, "Add a test socket");
            socket.transform.SetParent(avatar.transform, false);
            var hip = YapsFakePlayers.HipOf(avatar);
            var from = hip != null ? hip.position : avatar.transform.position;
            socket.transform.position = from + avatar.transform.forward * ahead;
            // Facing back down the shaft: a socket pointing the way the plug
            // travels is entered through its back.
            socket.transform.rotation = Quaternion.LookRotation(-avatar.transform.forward);
        }

        // No caps: a cap ring muddies a pinching bend.
        static Mesh Tube()
        {
            // Rebuilt every time rather than cached. A cached mesh survived a
            // fix to the winding and went on rendering the old one.
            string path = Dir + "/YapsTestPlugMesh.asset";
            AssetDatabase.DeleteAsset(path);

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
                    // Outside faces out; reversed, Unity culled the near side and lit it backwards.
                    tris[k++] = a; tris[k++] = a + 1; tris[k++] = b;
                    tris[k++] = a + 1; tris[k++] = b + 1; tris[k++] = b;
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
