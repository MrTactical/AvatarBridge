#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // VRCFury names what it moves "[VFnnn] Name" with a fresh number each build. The marker
    // lights and the atlas numbers were ordered by the raw path, so a reconvert of an unchanged
    // avatar could light another socket and renumber the rest. Two builds of one hierarchy,
    // differing only in those ids, must give one order.
    public static class LightOrderTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test: light order")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[LightOrderTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            // The ids run against the names in the first build and with them in the second.
            var a = Build("LightOrderTest A", 100, 900);
            var b = Build("LightOrderTest B", 900, 100);
            try
            {
                string Lit(GameObject root) => string.Join(", ", YapsSocketBuilder.Lit(root.GetComponentInChildren<YapsSocket>())
                    .Select(s => YapsScanner.StripFuryId(s.transform.parent.name)));
                string la = Lit(a), lb = Lit(b);
                fail += Check($"marker lights in one order (\"{la}\", \"{lb}\")", la == lb);

                string Numbers(GameObject root) => string.Join(", ", YapsOwner.NumberWriters(root, true)
                    .OrderBy(kv => kv.Value).Select(kv => $"{kv.Value} {YapsScanner.StripFuryId(kv.Key.transform.parent.name)}"));
                string na = Numbers(a), nb = Numbers(b);
                fail += Check($"atlas numbers in one order (\"{na}\", \"{nb}\")", na == nb);
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }

            Debug.Log($"[LightOrderTest] {(fail == 0 ? "PASS" : fail + " WRONG")}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        static GameObject Build(string name, int alpha, int zeta)
        {
            var root = new GameObject(name);
            var hips = new GameObject("Hips").transform;
            hips.SetParent(root.transform, false);
            void Socket(string parent, YapsSocket.SocketKind kind)
            {
                var owner = new GameObject(parent).transform;
                owner.SetParent(hips, false);
                var t = new GameObject("YAPS Socket").transform;
                t.SetParent(owner, false);
                var socket = t.gameObject.AddComponent<YapsSocket>();
                socket.kind = kind;
                socket.emitLights = true;
            }
            Socket($"[VF{zeta}] Zeta", YapsSocket.SocketKind.Hole);
            Socket($"[VF{alpha}] Alpha", YapsSocket.SocketKind.Hole);
            Socket($"[VF{zeta + 1}] Zeta Ring", YapsSocket.SocketKind.Ring);
            Socket($"[VF{alpha + 1}] Alpha Ring", YapsSocket.SocketKind.Ring);
            return root;
        }
    }
}
#endif
