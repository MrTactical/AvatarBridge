#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_DYNBONE
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge.Regression
{
    // VRChat stops a chain at another PhysBone's root when Ignore Other PhysBones is on. The
    // conversion never read the flag, so the outer chain also moved the nested one's bones.
    public static class NestedPhysBoneTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test: nested PhysBones")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[NestedPhysBoneTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            var root = new GameObject("NestedPhysBoneTest");
            try
            {
                root.AddComponent<Animator>();
                Transform Bone(string name, Transform parent, Vector3 at)
                {
                    var t = new GameObject(name).transform;
                    t.SetParent(parent, false);
                    t.localPosition = at;
                    return t;
                }
                var step = new Vector3(0, -0.06f, 0);
                var hair = Bone("Hair", Bone("Armature", root.transform, Vector3.zero), new Vector3(0, 1.6f, 0));
                var front = Bone("Front", hair, new Vector3(0, 0, 0.08f));
                Bone("Front1", front, step);
                var bead = Bone("Bead", Bone("Back1", Bone("Back", hair, new Vector3(0, 0, -0.08f)), step), step);
                Bone("Bead1", bead, step);

                var outer = hair.gameObject.AddComponent<VRCPhysBone>();
                front.gameObject.AddComponent<VRCPhysBone>();
                // A component kept on its own object and pointed at the bone counts the same.
                var holder = new GameObject("Physics");
                holder.transform.SetParent(root.transform, false);
                holder.AddComponent<VRCPhysBone>().rootTransform = bead;

                List<Transform> Excluded(bool ignoreOthers)
                {
                    outer.ignoreOtherPhysBones = ignoreOthers;
                    var ctx = new BridgeContext { Target = root, Report = new BridgeReport(), Settings = new BridgeSettings() };
                    DynamicBoneWriter.Write(ctx, PhysBoneChainData.Read(outer, root.GetComponent<Animator>(), false),
                        new Dictionary<VRCPhysBoneCollider, DynamicBoneColliderBase>());
                    return root.GetComponentsInChildren<DynamicBone>(true).Last(d => d.m_Root == hair).m_Exclusions;
                }

                var on = Excluded(true);
                fail += Check($"both nested roots are excluded ({string.Join(", ", on.Select(t => t.name))})",
                    on.Count == 2 && on.Contains(front) && on.Contains(bead));
                var off = Excluded(false);
                fail += Check($"with the flag off nothing is ({off.Count})", off.Count == 0);
            }
            catch (Exception e)
            {
                fail++;
                Debug.LogError("[NestedPhysBoneTest] threw: " + e);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            Debug.Log(fail == 0 ? "[NestedPhysBoneTest] PASS" : $"[NestedPhysBoneTest] FAIL: {fail}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
