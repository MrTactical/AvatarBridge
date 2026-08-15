// Turns a YapsSocket component into a socket every plug on the platform
// can find, and builds the universal Hole and Ring prefabs from the same
// code — so a prefab dropped under a bone and a socket authored by hand
// are the same thing.
//
// "Universal" was settled by the green test socket, in game, on
// 2026-08-14 and 15: it means speaking LEGACY plus both contact families,
// not speaking more. On a socket that is:
//
//   marker lights   black, ForceVertex, at VRCFury's exact ranges — 0.4106
//                   for a hole, 0.4206 for a ring, 0.4506 for the normal a
//                   centimetre along +Z. Every DPS plug and every converted
//                   plug reads these, and they cost no sync.
//   SPS pointers    SPSLL_Socket_Hole or _Ring at the origin (the kind IS
//                   the tag), SPSLL_Socket_Front a centimetre along +Z.
//   TPS pointers    TPS_Orf_Root at the origin, TPS_Orf_Norm along +Z.
//
// A root with no front is a shape that does not exist in the wild — the
// plug aims at it but cannot thread it — so the front is not optional.
//
// The lights are the whole cost of a socket, and it is a real one: Unity
// gives a mesh four vertex-light slots and a socket takes two, so an
// avatar with many sockets should wire them to menu toggles rather than
// leave them all lit. That is the toolkit's job on an avatar; a prefab
// starts lit because a lone socket lit is exactly right.
//
// The generated children are NAMED so a rebuild replaces rather than
// stacks, and so a scan recognises its own work.
#if CVR_CCK_EXISTS
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsSocketBuilder
    {
        // VRCFury's exact values. Matching the ecosystem byte for byte
        // beats reasoning about which digits matter — the fourth decimal
        // does not survive the light's reconstruction, but emitting what
        // everyone else emits removes a whole class of "why does mine
        // differ" questions.
        public const float HoleRange = 0.4106f;
        public const float RingRange = 0.4206f;
        public const float FrontRange = 0.4506f;
        public const float FrontOffset = 0.01f;

        const string LightsName = "YAPS Lights";
        const string PointersName = "YAPS Pointers";
        const string PrefabFolder = "Assets/YAPS/Prefabs";

        // --- the prefabs ------------------------------------------------

        [MenuItem("Tools/YAPS/Create universal socket prefabs")]
        public static void CreatePrefabs()
        {
            EnsureFolder(PrefabFolder);
            var hole = CreatePrefab("YAPS Hole", YapsSocket.SocketKind.Hole);
            var ring = CreatePrefab("YAPS Ring", YapsSocket.SocketKind.Ring);
            AssetDatabase.SaveAssets();
            Selection.objects = new Object[] { hole, ring };
            Debug.Log("[YAPS] Universal socket prefabs written to " + PrefabFolder + ". Drag one " +
                      "under a bone, point its +Z the way a plug should enter, and it works for " +
                      "DPS, TPS and SPS plugs and for converted ones. Pick a renderer and shapes " +
                      "on the component if you want it to open as a plug goes in, then run " +
                      "Tools ▸ YAPS ▸ Setup to bake those.");
        }

        static GameObject CreatePrefab(string name, YapsSocket.SocketKind kind)
        {
            var root = new GameObject(name);
            var socket = root.AddComponent<YapsSocket>();
            socket.kind = kind;
            Build(socket);
            string path = AssetDatabase.GenerateUniqueAssetPath(PrefabFolder + "/" + name + ".prefab");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        [MenuItem("GameObject/YAPS/Add a hole here", false, 10)]
        static void AddHole(MenuCommand cmd) => AddSocket(cmd, YapsSocket.SocketKind.Hole);

        [MenuItem("GameObject/YAPS/Add a ring here", false, 11)]
        static void AddRing(MenuCommand cmd) => AddSocket(cmd, YapsSocket.SocketKind.Ring);

        static void AddSocket(MenuCommand cmd, YapsSocket.SocketKind kind)
        {
            var parent = cmd.context as GameObject;
            var go = new GameObject(kind == YapsSocket.SocketKind.Hole ? "YAPS Hole" : "YAPS Ring");
            if (parent != null) go.transform.SetParent(parent.transform, false);
            var socket = go.AddComponent<YapsSocket>();
            socket.kind = kind;
            Build(socket);
            Undo.RegisterCreatedObjectUndo(go, "Add YAPS socket");
            Selection.activeGameObject = go;
        }

        // --- the build ---------------------------------------------------

        // Makes the component's object a working socket: lights and
        // pointers as children, replacing any it built before. Idempotent.
        public static void Build(YapsSocket socket)
        {
            if (socket == null) return;
            var t = socket.transform;

            Replace(t, LightsName, lights =>
            {
                if (!socket.emitLights) return;
                bool hole = socket.kind == YapsSocket.SocketKind.Hole;
                MarkerLight(lights, "Root", hole ? HoleRange : RingRange, Vector3.zero);
                MarkerLight(lights, "Front", FrontRange, new Vector3(0, 0, FrontOffset));
            });

            Replace(t, PointersName, pointers =>
            {
                bool hole = socket.kind == YapsSocket.SocketKind.Hole;
                Pointer(pointers, "SPS Root", hole ? "SPSLL_Socket_Hole" : "SPSLL_Socket_Ring", Vector3.zero);
                Pointer(pointers, "SPS Front", "SPSLL_Socket_Front", new Vector3(0, 0, FrontOffset));
                Pointer(pointers, "TPS Root", "TPS_Orf_Root", Vector3.zero);
                Pointer(pointers, "TPS Norm", "TPS_Orf_Norm", new Vector3(0, 0, FrontOffset));
            });
        }

        static void Replace(Transform parent, string name, System.Action<Transform> fill)
        {
            var old = parent.Find(name);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            fill(go.transform);
            if (go.transform.childCount == 0) Object.DestroyImmediate(go);
        }

        public static Light MarkerLight(Transform parent, string name, float range, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            // Black, but NOT zero intensity. Black is what lets the decoder
            // tell a protocol light from real lighting; intensity zero is
            // something else — Unity drops a light contributing nothing
            // from the per-object list, and the slot the decoder reads
            // never fills.
            light.color = Color.black;
            light.intensity = 1f;
            light.bounceIntensity = 0f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForceVertex;
            return light;
        }

        public static CVRPointer Pointer(Transform parent, string name, string type, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var p = go.AddComponent<CVRPointer>();
            p.type = type;
            return p;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
