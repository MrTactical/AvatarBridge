#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge.Regression
{
    // A PhysBone's curves were read at the chain's two ends only, so a chain loose in the middle
    // and held at both ends converted as held all along. The derived restoration is sampled
    // along the chain now.
    public static class PhysBoneCurveTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test: PhysBone curves along the chain")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[PhysBoneCurveTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            var root = new GameObject("PhysBoneCurveTest");
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
                var head = Bone("Head", Bone("Armature", root.transform, Vector3.zero), new Vector3(0, 1.5f, 0));

                (MagicaCloth2.ClothSerializeData cloth, PhysBoneChainData data) Convert(string name, AnimationCurve pull)
                {
                    var b = Bone(name, head, new Vector3(0, 0, -0.08f));
                    var t = b;
                    for (int i = 1; i <= 6; i++) t = Bone(name + i, t, new Vector3(0, -0.05f, 0));
                    var pb = b.gameObject.AddComponent<VRCPhysBone>();
                    pb.pull = 0.6f;
                    if (pull != null) pb.pullCurve = pull;
                    var ctx = new BridgeContext { Target = root, Report = new BridgeReport(), Settings = new BridgeSettings() };
                    var data = PhysBoneChainData.Read(pb, root.GetComponent<Animator>(), false);
                    var mc = MagicaClothWriter.Write(ctx, data, new Dictionary<VRCPhysBoneCollider, MagicaCloth2.ColliderComponent>());
                    return (mc.SerializeData, data);
                }
                float Derived(PhysBoneChainData d, float t) => PhysBoneSolverMap.RestorationStiffness(
                    d.Pull * PhysBoneSolverMap.SafeEvaluate(d.PullCurve, t),
                    d.Spring * PhysBoneSolverMap.SafeEvaluate(d.SpringCurve, t),
                    PhysBoneSolverMap.AsVersion11(d.Stiffness * PhysBoneSolverMap.SafeEvaluate(d.StiffnessCurve, t), d.IsVersion10),
                    d.IsAdvancedIntegration, out _);

                var (dip, dipData) = Convert("Hair_Dip",
                    new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.5f, 0.1f), new Keyframe(1, 1)));
                var r = dip.angleRestorationConstraint.stiffness;
                fail += Check($"loose in the middle restores less there ({r.Evaluate(0):0.###}, {r.Evaluate(0.5f):0.###}, " +
                    $"{r.Evaluate(1):0.###}; derived {Derived(dipData, 0.5f):0.###})",
                    Mathf.Abs(r.Evaluate(0.5f) - Derived(dipData, 0.5f)) < 0.02f * r.value);

                var (ramp, rampData) = Convert("Hair_Ramp", AnimationCurve.Linear(0, 1, 1, 0.2f));
                r = ramp.angleRestorationConstraint.stiffness;
                fail += Check($"a ramp's ends are the two-end derivation's ({r.Evaluate(0):0.####} {Derived(rampData, 0):0.####}, " +
                    $"{r.Evaluate(1):0.####} {Derived(rampData, 1):0.####})",
                    Mathf.Abs(r.Evaluate(0) - Derived(rampData, 0)) < 1e-4f && Mathf.Abs(r.Evaluate(1) - Derived(rampData, 1)) < 1e-4f);
                fail += Check($"and its middle follows the derivation ({r.Evaluate(0.5f):0.####}, derived {Derived(rampData, 0.5f):0.####})",
                    Mathf.Abs(r.Evaluate(0.5f) - Derived(rampData, 0.5f)) < 0.02f * r.value);

                var (plain, _) = Convert("Hair_Plain", null);
                fail += Check($"no curve writes none (restoration {plain.angleRestorationConstraint.stiffness.useCurve}, " +
                    $"damping {plain.damping.useCurve})",
                    !plain.angleRestorationConstraint.stiffness.useCurve && !plain.damping.useCurve);
            }
            catch (Exception e)
            {
                fail++;
                Debug.LogError("[PhysBoneCurveTest] threw: " + e);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            Debug.Log(fail == 0 ? "[PhysBoneCurveTest] PASS" : $"[PhysBoneCurveTest] FAIL: {fail}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
