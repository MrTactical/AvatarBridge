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
        // DPS's digits, offset into the quiet part of the band.
        //
        // A decoder reads range % 0.1 and compares it to 0.01 hole, 0.02
        // ring, 0.05 front, 0.09 plug tip. Raliv's shader accepts anything
        // within 0.005 of those; toy mods reading the same protocol in C#
        // accept 0.001 (CVRGoesBrrr, verified against its source). VRCFury
        // authors +0.0006, inside both, so its sockets drive a bystander's
        // toy and their controllers from across a room.
        //
        // +0.003 is outside the mod's window and half of Raliv's, so DPS
        // content still reads the socket and a toy mod does not answer it.
        // The shader reconstructs range from an attenuation uniform and
        // loses about 0.0005 doing it, which this clears twice over.
        public const float HoleRange = 0.4130f;
        public const float RingRange = 0.4230f;
        public const float FrontRange = 0.4530f;
        public const float FrontOffset = 0.01f;

        // The self channel. A socket's own Self trigger listens for this
        // suffix alone, so a tag without its twin is remote-only.
        const string Twin = "_SelfNotOnHips";

        public const string LightsName = "YAPS Lights";
        const string PointersName = "YAPS Pointers";
        const string PrefabFolder = "Assets/YAPS/Prefabs";
        public const string PlugPropPrefabPath = PrefabFolder + "/YAPS Plug Prop.prefab";

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

        // A plug prop ready to upload, over the same file each time. An
        // uploaded prop keeps the bake and shader it was built with.
        [MenuItem("Tools/YAPS/Create a plug prop prefab")]
        public static void CreatePlugPropPrefab()
        {
            EnsureFolder(PrefabFolder);
            var root = YapsNativeBuilder.BuildTestPlug(null, select: false);
            if (root == null)
            {
                Debug.LogError("[YAPS] Could not build the plug: YAPS Simple Lit is missing from the project.");
                return;
            }
            root.name = "YAPS Plug Prop";
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var plug = root.GetComponent<YapsPlug>();
            var baked = YapsNativeBuilder.Bake(plug);
            if (!baked.Ok)
            {
                Object.DestroyImmediate(root);
                Debug.LogError("[YAPS] Could not bake the plug prop: " + baked.Message);
                return;
            }
            var prop = YapsPropBuilder.MakeProp(root);

            string path = PlugPropPrefabPath;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            Debug.Log("[YAPS] Plug prop prefab written to " + path + ". " + baked.Message + " " + prop.Message +
                      (prop.Notes.Count > 0 ? " " + string.Join(" ", prop.Notes) : "") +
                      " Upload it from the CCK as a prop. Run this again after updating AvatarBridge: a prop " +
                      "already uploaded keeps the shader and bake it was built with.");
        }

        static GameObject CreatePrefab(string name, YapsSocket.SocketKind kind)
        {
            var root = new GameObject(name);
            var socket = root.AddComponent<YapsSocket>();
            socket.kind = kind;
            Build(socket);
            // The same file each time: a prefab is a thing to update, not to number.
            string path = PrefabFolder + "/" + name + ".prefab";
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

        // Unity gives a mesh four vertex light slots, refills them every
        // frame from the ranges in reach, and a socket takes two. Every lit
        // socket therefore competes for those slots on every avatar standing
        // near it, not just its own, and a crowd of them makes the winners
        // change frame to frame. The converter caps a converted avatar the
        // same way a socket built here is capped. Holes first, then rings,
        // then hierarchy order, so a rebuild keeps the same ones.
        public static bool WithinLightCap(YapsSocket socket)
        {
            if (socket == null) return false;
            var lit = Lit(socket);
            return lit.IndexOf(socket) < Places(socket);
        }

        // Four slots, two to a socket, and a tracker light takes one of the
        // four before any socket does. The tracker that matters is rarely on
        // this avatar: it belongs to the prop or the partner entering the
        // socket, and there is no way to count those at build time. So the
        // budget always reserves one slot for a tracker it cannot see.
        //
        // Getting this wrong is what broke holes and left rings working. The
        // tracker outranks every marker at 0.4930, a front is 0.4530 and a
        // ring root 0.4230, so with two sockets lit the fifth candidate is
        // always the hole root at 0.4130 and Unity drops exactly that one.
        static int Places(YapsSocket socket)
        {
            var avatar = socket.GetComponentInParent<CVRAvatar>();
            var root = avatar != null ? avatar.transform : socket.transform.root;
            int trackers = root.GetComponentsInChildren<YapsPlug>(true)
                .Count(p => p != null && p.emitTipLight);
            int places = (4 - Mathf.Clamp(trackers, 1, 2)) / 2;
            return Mathf.Clamp(places, 1, BridgeSettings.DefaultMaxLightEmittingSockets);
        }

        // The sockets asking for lights on this avatar, in the order they
        // get them.
        public static System.Collections.Generic.List<YapsSocket> Lit(YapsSocket socket)
        {
            var avatar = socket.GetComponentInParent<CVRAvatar>();
            var root = avatar != null ? avatar.transform : socket.transform.root;
            return root.GetComponentsInChildren<YapsSocket>(true)
                .Where(s => s != null && s.emitLights)
                .OrderBy(s => s.kind == YapsSocket.SocketKind.Hole ? 0 : 1)
                .ThenBy(s => AnimationUtility.CalculateTransformPath(s.transform, root))
                .ToList();
        }

        // What Build tells the user when it withheld a socket's lights.
        public static string LightCapNote(YapsSocket socket)
        {
            if (socket == null || !socket.emitLights || WithinLightCap(socket)) return null;
            int places = Places(socket);
            var kept = Lit(socket).Take(places).Select(s => s.name);
            return "marker lights start dark: " +
                   $"{places} socket(s) start lit ({string.Join(", ", kept)}) and this one waits " +
                   "on the lighthouse — the \"Marker lights\" menu entry lights any one socket " +
                   "and darkens the rest. A mesh gets four vertex light slots, a socket needs " +
                   "two, and the tracker light of whatever enters takes a third, so a second " +
                   "lit socket used to evict the hole's root and break it while rings kept " +
                   "working. A dark pair costs nothing: a disabled light never enters Unity's " +
                   "ranking. Plugs built by this toolkit find sockets through contacts and do " +
                   "not need the lights; only old DPS plugs do.";
        }

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
                // 1 to 4 legacy roots, 7 the YAPS root; 5 and 6 legacy
                // fronts, 0 the YAPS front. A rebuild that did not know the
                // YAPS digits would add a second pair beside them.
                if ((d >= 1 && d <= 4) || d == 7) hasRootLight = true;
                if (d == 5 || d == 6 || d == 0) hasFrontLight = true;
            }
            foreach (var p in t.GetComponentsInChildren<CVRPointer>(true))
            {
                if (p == null || string.IsNullOrEmpty(p.type) || Owned(p.transform, t)) continue;
                havePointers.Add(p.type);
            }

            Replace(t, LightsName, lights =>
            {
                if (!socket.emitLights) return;
                // Every lit-capable socket CARRIES its pair; only the one
                // within the cap starts enabled. A disabled light never
                // enters Unity's per-mesh ranking, so the rest cost
                // nothing until the lighthouse menu hands them the slot.
                if (!hasRootLight) MarkerLight(lights, "Root", hole ? HoleRange : RingRange, Vector3.zero);
                if (!hasFrontLight) MarkerLight(lights, "Front", FrontRange, new Vector3(0, 0, FrontOffset));
                lights.gameObject.SetActive(WithinLightCap(socket));
            });

            Replace(t, PointersName, pointers =>
            {
                string spsRoot = hole ? "SPSLL_Socket_Hole" : "SPSLL_Socket_Ring";
                bool anySpsRoot = havePointers.Any(k => k.StartsWith("SPSLL_Socket_Root") || k.StartsWith("SPSLL_Socket_Hole") || k.StartsWith("SPSLL_Socket_Ring"));
                // Each tag twice: the bare one and its _SelfNotOnHips twin.
                //
                // The twin is not a duplicate, it is the SELF channel. A
                // socket's own Self trigger listens for the twin ALONE, so a
                // socket carrying only the bare tag answers everybody else
                // and is dead to the person wearing it. Measured that way on
                // a built socket: worked remotely, nothing locally.
                //
                // YapsPropBuilder has always emitted both. Sockets did not.
                if (!anySpsRoot)
                {
                    Pointer(pointers, "SPS Root", spsRoot, Vector3.zero);
                    Pointer(pointers, "SPS Root Self", spsRoot + Twin, Vector3.zero);
                }
                if (!havePointers.Any(k => k.StartsWith("SPSLL_Socket_Front")))
                {
                    Pointer(pointers, "SPS Front", "SPSLL_Socket_Front", new Vector3(0, 0, FrontOffset));
                    Pointer(pointers, "SPS Front Self", "SPSLL_Socket_Front" + Twin,
                        new Vector3(0, 0, FrontOffset));
                }
                if (!havePointers.Any(k => k.StartsWith("TPS_Orf_Root")))
                {
                    Pointer(pointers, "TPS Root", "TPS_Orf_Root", Vector3.zero);
                    Pointer(pointers, "TPS Root Self", "TPS_Orf_Root" + Twin, Vector3.zero);
                }
                if (!havePointers.Any(k => k.StartsWith("TPS_Orf_Norm")))
                {
                    Pointer(pointers, "TPS Norm", "TPS_Orf_Norm", new Vector3(0, 0, FrontOffset));
                    Pointer(pointers, "TPS Norm Self", "TPS_Orf_Norm" + Twin,
                        new Vector3(0, 0, FrontOffset));
                }
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
