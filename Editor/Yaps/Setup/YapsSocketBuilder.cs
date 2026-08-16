// Turns a YapsSocket into a socket every plug can find, and builds the
// universal Hole and Ring prefabs from the same code. Marker lights at
// VRCFury's ranges, SPS and TPS pointers, and a front a centimetre along
// +Z. The children are named so a rebuild replaces rather than stacks.
#if CVR_CCK_EXISTS
using System.Linq;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsSocketBuilder
    {
        // VRCFury's exact values, emitted byte for byte.
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
                      "DPS, TPS and SPS plugs and for converted ones. A socket with a mesh of its " +
                      "own can open as a plug goes in: pick that mesh and its shapes on the " +
                      "component, then bake from Tools ▸ YAPS ▸ Setup.");
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

        // Kind changed on the component: the markers must say so too. Any
        // root light under the socket takes the new range, any SPS root
        // pointer the new name, the toolkit's own folders are rebuilt, and
        // a default-named object is renamed to match.
        public static void ApplyKind(YapsSocket socket)
        {
            if (socket == null) return;
            bool hole = socket.kind == YapsSocket.SocketKind.Hole;
            var t = socket.transform;
            foreach (var l in t.GetComponentsInChildren<Light>(true))
            {
                if (!YapsScanner.IsProtocolLight(l)) continue;
                int d = YapsScanner.LightDigit(l);
                if (d >= 1 && d <= 4)
                {
                    Undo.RecordObject(l, "YAPS socket kind");
                    l.range = hole ? HoleRange : RingRange;
                    EditorUtility.SetDirty(l);
                }
            }
            foreach (var p in t.GetComponentsInChildren<CVRPointer>(true))
            {
                if (p == null || string.IsNullOrEmpty(p.type)) continue;
                string from = hole ? "SPSLL_Socket_Ring" : "SPSLL_Socket_Hole";
                string to = hole ? "SPSLL_Socket_Hole" : "SPSLL_Socket_Ring";
                if (p.type.StartsWith(from))
                {
                    Undo.RecordObject(p, "YAPS socket kind");
                    p.type = to + p.type.Substring(from.Length);
                    EditorUtility.SetDirty(p);
                }
            }
            Build(socket);
            string was = hole ? "YAPS Ring" : "YAPS Hole";
            if (t.name == was || t.name.StartsWith(was + " "))
            {
                Undo.RecordObject(t.gameObject, "YAPS socket kind");
                t.name = (hole ? "YAPS Hole" : "YAPS Ring") + t.name.Substring(was.Length);
            }
        }

        // --- the build ---------------------------------------------------

        // Lights and pointers as children, replacing what it built before.
        public static void Build(YapsSocket socket)
        {
            if (socket == null) return;
            var t = socket.transform;
            bool hole = socket.kind == YapsSocket.SocketKind.Hole;

            // What is already announced outside the toolkit's folders. Only
            // what is missing is added; only the folders are replaced.
            bool hasRootLight = false, hasFrontLight = false;
            var havePointers = new System.Collections.Generic.HashSet<string>();
            foreach (var l in t.GetComponentsInChildren<Light>(true))
            {
                if (l.transform.IsChildOf(t) && Owned(l.transform, t)) continue;
                if (!YapsScanner.IsProtocolLight(l)) continue;
                int d = YapsScanner.LightDigit(l);
                if (d >= 1 && d <= 4) hasRootLight = true;
                if (d == 5 || d == 6) hasFrontLight = true;
            }
            foreach (var p in t.GetComponentsInChildren<CVRPointer>(true))
            {
                if (p == null || string.IsNullOrEmpty(p.type) || Owned(p.transform, t)) continue;
                havePointers.Add(p.type);
            }

            Replace(t, LightsName, lights =>
            {
                if (!socket.emitLights) return;
                if (!hasRootLight) MarkerLight(lights, "Root", hole ? HoleRange : RingRange, Vector3.zero);
                if (!hasFrontLight) MarkerLight(lights, "Front", FrontRange, new Vector3(0, 0, FrontOffset));
            });

            Replace(t, PointersName, pointers =>
            {
                string spsRoot = hole ? "SPSLL_Socket_Hole" : "SPSLL_Socket_Ring";
                bool anySpsRoot = havePointers.Any(k => k.StartsWith("SPSLL_Socket_Root") || k.StartsWith("SPSLL_Socket_Hole") || k.StartsWith("SPSLL_Socket_Ring"));
                if (!anySpsRoot) Pointer(pointers, "SPS Root", spsRoot, Vector3.zero);
                if (!havePointers.Any(k => k.StartsWith("SPSLL_Socket_Front"))) Pointer(pointers, "SPS Front", "SPSLL_Socket_Front", new Vector3(0, 0, FrontOffset));
                if (!havePointers.Any(k => k.StartsWith("TPS_Orf_Root"))) Pointer(pointers, "TPS Root", "TPS_Orf_Root", Vector3.zero);
                if (!havePointers.Any(k => k.StartsWith("TPS_Orf_Norm"))) Pointer(pointers, "TPS Norm", "TPS_Orf_Norm", new Vector3(0, 0, FrontOffset));
            });
        }

        // Inside one of the toolkit's own folders under the socket.
        static bool Owned(Transform marker, Transform socket)
        {
            for (var at = marker; at != null && at != socket; at = at.parent)
            {
                if (at.parent == socket && (at.name == LightsName || at.name == PointersName)) return true;
            }
            return false;
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
            // Black, never intensity zero: Unity drops a light that adds nothing.
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
