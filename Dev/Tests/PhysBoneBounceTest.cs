#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge.Regression
{
    // The restoration's velocity attenuation came from the class preset, so a springy PhysBone
    // converted with a dead chain's bounce and a dead one with a springy chain's. Each converted
    // chain's step response, from the coefficients the writer wrote, has to follow its PhysBone's.
    public static class PhysBoneBounceTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/PhysBone bounce")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[PhysBoneBounceTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            var root = new GameObject("PhysBoneBounceTest");
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

                foreach (var (name, pull, spring, advanced) in new[]
                {
                    ("Hair_Dead", 0.2f, 0.2f, false),
                    ("Hair_Springy", 0.2f, 0.8f, true),
                    ("Hair_Loose", 0.1f, 0.8f, false),
                })
                {
                    var b = Bone(name, head, new Vector3(0, 0, -0.08f));
                    var t = b;
                    for (int i = 1; i <= 6; i++) t = Bone(name + i, t, new Vector3(0, -0.05f, 0));
                    var pb = b.gameObject.AddComponent<VRCPhysBone>();
                    pb.pull = pull;
                    pb.spring = spring;
                    var integration = pb.GetType().GetField("integrationType");
                    integration.SetValue(pb, Enum.Parse(integration.FieldType, advanced ? "Advanced" : "Simplified"));
                    var ctx = new BridgeContext { Target = root, Report = new BridgeReport(), Settings = new BridgeSettings() };
                    var data = PhysBoneChainData.Read(pb, root.GetComponent<Animator>(), false);
                    var cloth = MagicaClothWriter.Write(ctx, data, new Dictionary<VRCPhysBoneCollider, MagicaCloth2.ColliderComponent>()).SerializeData;

                    float damping = cloth.damping.Evaluate(0.5f);
                    float restoration = cloth.angleRestorationConstraint.stiffness.Evaluate(0.5f);
                    float attenuation = cloth.angleRestorationConstraint.velocityAttenuation;
                    float error = StepError(data.Pull, data.Spring,
                        PhysBoneSolverMap.AsVersion11(data.Stiffness, data.IsVersion10), advanced, damping, restoration, attenuation);
                    fail += Check($"{name} (pull {pull}, spring {spring}) follows its PhysBone: damping {damping:0.###}, " +
                        $"restoration {restoration:0.###}, attenuation {attenuation:0.###}, step error {error:0.####}", error < 0.01f);
                }
            }
            catch (Exception e)
            {
                fail++;
                Debug.LogError("[PhysBoneBounceTest] threw: " + e);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            Debug.Log(fail == 0 ? "[PhysBoneBounceTest] PASS" : $"[PhysBoneBounceTest] FAIL: {fail}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        // Both solvers on one bone whose rest jumps by 1, each from its own step function
        // (docs/SolverCalibration.md); the mean distance between them over two seconds.
        static float StepError(float pull, float spring, float stiffness, bool advanced, float damping, float restoration, float attenuation)
        {
            float r = PhysBoneSolverMap.Retention60(pull, spring, stiffness, advanced);
            float q = PhysBoneSolverMap.Restore60(pull, spring, stiffness, advanced);
            var physBone = new float[120];
            float x = 0f, d = 0f;
            for (int i = 0; i < physBone.Length; i++) { d = r * d + q * (1f - x); x += d; physBone[i] = x; }

            float keep = Mathf.Clamp01(1f - damping * PhysBoneSolverMap.DampingScale);
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(restoration * PhysBoneSolverMap.RestorationScale),
                PhysBoneSolverMap.RestorationIterations);
            float error = 0f, v = 0f;
            x = 0f;
            for (int i = 0; i < 180; i++)
            {
                v *= keep;
                float moved = x + v, pulled = k * (1f - moved);
                v = moved + pulled - x - attenuation * pulled;
                x = moved + pulled;
                if (i % 3 == 2) error += Mathf.Abs(x - physBone[i * 2 / 3]);
            }
            return error / 60f;
        }
    }
}
#endif
