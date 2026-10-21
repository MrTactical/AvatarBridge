#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge.Regression
{
    // A soft body dropped its source's angle limit: the distance leash does nothing on a
    // BoneSpring, so the part swung unbounded, up past the chest on a forward walk where the
    // PhysBone stopped at its limit. A soft body now carries the limit as an angle limit.
    public static class SoftBodyLimitTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test: soft body angle limit")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[SoftBodyLimitTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            var root = new GameObject("SoftBodyLimitTest");
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
                var chest = Bone("Chest", Bone("Armature", root.transform, Vector3.zero), new Vector3(0, 1.3f, 0));

                MagicaCloth2.MagicaCloth Convert(string name, VRCPhysBoneBase.LimitType limit, float angle)
                {
                    var b = Bone(name, chest, new Vector3(0.08f, 0, 0.05f));
                    Bone(name + "_end", Bone(name + "1", b, new Vector3(0, 0, 0.06f)), new Vector3(0, 0, 0.06f));
                    var pb = b.gameObject.AddComponent<VRCPhysBone>();
                    pb.limitType = limit;
                    pb.maxAngleX = angle;
                    var ctx = new BridgeContext { Target = root, Report = new BridgeReport(), Settings = new BridgeSettings() };
                    var data = PhysBoneChainData.Read(pb, root.GetComponent<Animator>(), false);
                    return MagicaClothWriter.Write(ctx, data, new Dictionary<VRCPhysBoneCollider, MagicaCloth2.ColliderComponent>());
                }

                var limited = Convert("Breast_L", VRCPhysBoneBase.LimitType.Angle, 50f);
                var a = limited.SerializeData.angleLimitConstraint;
                fail += Check($"a soft body is a BoneSpring ({limited.SerializeData.clothType})",
                    limited.SerializeData.clothType == MagicaCloth2.ClothProcess.ClothType.BoneSpring);
                fail += Check($"its source's 50 degree limit is kept (use {a.useAngleLimit}, {a.limitAngle.value:0.#})",
                    a.useAngleLimit && Mathf.Approximately(a.limitAngle.value, 50f));

                var free = Convert("Breast_R", VRCPhysBoneBase.LimitType.None, 50f);
                fail += Check($"a source with no limit gets none ({free.SerializeData.angleLimitConstraint.useAngleLimit})",
                    !free.SerializeData.angleLimitConstraint.useAngleLimit);
            }
            catch (Exception e)
            {
                fail++;
                Debug.LogError("[SoftBodyLimitTest] threw: " + e);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            Debug.Log(fail == 0 ? "[SoftBodyLimitTest] PASS" : $"[SoftBodyLimitTest] FAIL: {fail}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
