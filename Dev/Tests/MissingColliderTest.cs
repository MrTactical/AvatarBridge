#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge.Regression
{
    // A PhysBone whose collider was deleted keeps a dead entry in its list. A plain type check let
    // it through, and the PhysBones pass threw MissingReferenceException on it.
    public static class MissingColliderTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Missing PhysBone collider")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[MissingColliderTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            var root = new GameObject("MissingColliderTest");
            try
            {
                root.AddComponent<Animator>();
                var tail = new GameObject("Tail").transform;
                tail.SetParent(root.transform, false);
                var tip = new GameObject("Tail1").transform;
                tip.SetParent(tail, false);
                tip.localPosition = new Vector3(0, -0.1f, 0);
                var kept = new GameObject("Kept").AddComponent<VRCPhysBoneCollider>();
                kept.transform.SetParent(root.transform, false);
                var gone = new GameObject("Gone").AddComponent<VRCPhysBoneCollider>();
                gone.transform.SetParent(root.transform, false);
                var pb = tail.gameObject.AddComponent<VRCPhysBone>();
                pb.colliders = new System.Collections.Generic.List<VRC.Dynamics.VRCPhysBoneColliderBase> { kept, gone };
                UnityEngine.Object.DestroyImmediate(gone.gameObject);

                var data = PhysBoneChainData.Read(pb, root.GetComponent<Animator>(), false);
                fail += Check($"only the live collider is read ({data.Colliders.Count})", data.Colliders.Count == 1 && data.Colliders[0] == kept);
            }
            catch (Exception e)
            {
                fail++;
                Debug.LogError("[MissingColliderTest] threw: " + e);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            Debug.Log(fail == 0 ? "[MissingColliderTest] PASS" : $"[MissingColliderTest] FAIL: {fail}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
