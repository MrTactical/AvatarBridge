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
// Sockets come with it. The bake reads SelfTag off whether the avatar carries
// any YapsSocket, so a plug on a socketless avatar bakes to -1, ownership is
// never asked, nothing is ever rejected, and the test passes without running.
//
//   Tools/YAPS/Own-body test: build the rig
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

        [MenuItem("Tools/YAPS/Own-body test: build the rig")]
        static void Rig()
        {
            var selected = Selection.activeGameObject;
            var avatar = selected != null ? selected.GetComponentInParent<CVRAvatar>() : null;
            if (avatar == null)
            {
                Debug.Log("TEST PLUG: select the avatar to put it on first.");
                return;
            }

            // SOCKETS FIRST. The plug's bake decides SelfTag from whether the
            // avatar has any, so a plug baked before them is baked at -1.
            //
            // Two. NEAR is inside engagement onset and is the case that
            // matters. BAND is past the atlas's own reach, not past the
            // ownership gate: for a plug this long no socket can be both
            // visible to the grid and far enough to be rejected, so what it
            // actually measures is where the neighbourhood scan stops.
            int already = avatar.GetComponentsInChildren<YapsSocket>(true).Length;
            if (already == 0)
            {
                AddSocket(avatar, "YAPS Test Socket NEAR", Length * 1.0f);
                AddSocket(avatar, "YAPS Test Socket BAND", Length * 2.0f);
            }
            int sockets = avatar.GetComponentsInChildren<YapsSocket>(true).Length;

            // The writer meshes ride each socket, but the rect they draw into
            // and the read back off it live on the avatar. Without these the
            // atlas is empty and every tier below it answers instead.
            YapsAtlas.AddClear(avatar.transform);
            YapsAtlas.AddGrab(avatar.transform);

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
                $"The avatar carries {sockets} YapsSocket(s)." + (self < 0
                    ? " SELFTAG IS NEGATIVE: ownership is never asked and nothing will be rejected, " +
                      "so every result would pass without running."
                    : " Duplicate the avatar for body B, then run the own-body report."));

            Selection.activeGameObject = go;
        }

        // TEST 4. Another body's socket alongside the wearer's own, which is
        // the case the relaxed rule can make worse rather than better.
        //
        // The two bodies end up almost inside each other, and that is correct.
        // The atlas returns nothing past about 1.2 plug lengths, so for a 22 cm
        // plug somebody else's socket is only ever visible within a quarter of
        // a metre. In use that is exactly where it is.
        //
        // Both sockets sit close to their OWN hip, which the first version got
        // wrong. Ownership is decided by the nearest hip to the socket, so a
        // socket floating midway between two bodies belongs to whichever hip
        // wins by a millimetre and the answer is a coin toss.
        [MenuItem("Tools/YAPS/Own-body test: add the other body")]
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
            // NO MARKER LIGHTS. A light resolves the socket too, and on the
            // "Resolved by" view its answer is 0.75 against the atlas taps'
            // 0.66: two centimetres apart on a plug this long, which is not a
            // reading. With the lights gone the lower tier cannot answer at
            // all, so anything but a quarter or full is the atlas.
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

        // A tube from the origin along +Z. No caps: nothing here looks at them
        // and a cap ring at the base is one more thing to explain when the
        // bend pinches.
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
                    // Wound so the OUTSIDE faces out. The first version had
                    // these reversed: every triangle's normal pointed into the
                    // tube, so Unity culled the near side and RecalculateNormals
                    // lit what was left backwards.
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
