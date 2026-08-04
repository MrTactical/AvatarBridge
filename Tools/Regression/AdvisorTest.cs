#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge.Regression
{
    /// <summary>
    /// Known-answer test for AvatarAdvisor (task #8).
    ///
    /// The advisor's whole value is that its recommendation and the conversion that follows agree,
    /// so what is worth pinning here is not the wording but the decisions:
    ///
    ///   - it recommends turning physics OFF only when there is genuinely nothing to convert;
    ///   - it recommends turning physics ON when there are chains and a solver to take them, and
    ///     says BLOCKED rather than recommending a solver that is not installed;
    ///   - the judgement calls come back as Manual with no claim of a right answer, and Manual is
    ///     what keeps them out of "Apply all" — an advisor that quietly ticked "Convert toe
    ///     PhysBones" would be deciding exactly the question it just said it could not;
    ///   - Apply actually writes the setting it advertises. A row that recommends something and
    ///     then applies nothing is worse than no row.
    ///
    /// Face tracking is asserted on the empty case only. Its detection is FaceTrackingConverter's,
    /// tested through the corpus on avatars that really carry the shapes, and a synthetic mesh with
    /// twelve plausibly-named blendshapes would only be testing this file's guess at the naming.
    /// </summary>
    public static class AdvisorTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test — avatar advisor")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[AdvisorTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            GameObject bare = null;
            GameObject chained = null;
            GameObject toed = null;
            try
            {
                // ---- an avatar with nothing on it -------------------------------------
                bare = new GameObject("__AdvisorTest_Bare");
                var bareDescriptor = bare.AddComponent<VRCAvatarDescriptor>();

                var settings = new BridgeSettings { physicsTarget = PhysicsTarget.MagicaCloth2 };
                var advice = AvatarAdvisor.Analyse(bareDescriptor, settings);
                var physics = Find(advice, "Convert PhysBones to");

                fail += Check("no PhysBones and no baker: physics target is recommended off",
                    physics != null && physics.Kind == AdviceKind.Change && physics.Apply != null);
                if (physics != null && physics.Apply != null)
                {
                    physics.Apply(settings);
                }
                fail += Check("...and applying it actually writes the setting",
                    settings.physicsTarget == PhysicsTarget.None);

                var face = Find(advice, "Face tracking");
                fail += Check("no shapes and no FT parameters: face tracking is recommended off",
                    face != null && face.Kind == AdviceKind.Change && face.Apply != null);

                // Nothing to convert anywhere, so the component settings must read as inert
                // rather than as four separate problems.
                fail += Check("components with nothing to act on are reported once, as Not needed",
                    advice.Count(a => a.Setting == "Components" && a.Kind == AdviceKind.Inert) == 1);

                // ---- an avatar with a chain, and physics switched off ------------------
                chained = new GameObject("__AdvisorTest_Chained");
                var chainedDescriptor = chained.AddComponent<VRCAvatarDescriptor>();
                var chain = new GameObject("Chain");
                chain.transform.SetParent(chained.transform);
                var bone = new GameObject("Bone");
                bone.transform.SetParent(chain.transform);
                var pb = chain.AddComponent<VRCPhysBone>();
                pb.rootTransform = chain.transform;

                var offSettings = new BridgeSettings { physicsTarget = PhysicsTarget.None };
                var offAdvice = AvatarAdvisor.Analyse(chainedDescriptor, offSettings);
                var offPhysics = Find(offAdvice, "Convert PhysBones to");

                // Deliberately asserted both ways round: the answer depends on the project, and a
                // test that only passed where MagicaCloth2 happens to be installed would be
                // testing the project rather than the advisor.
                bool solverAvailable = BridgeDefines.HasMagicaCloth2 || BridgeDefines.HasDynamicBone;
                fail += Check(solverAvailable
                        ? "chains present, physics off, a solver installed: recommended on"
                        : "chains present, physics off, no solver installed: reported as blocked",
                    offPhysics != null && (solverAvailable
                        ? offPhysics.Kind == AdviceKind.Change && offPhysics.Apply != null
                        : offPhysics.Kind == AdviceKind.Blocked && offPhysics.Apply == null));

                // ---- a chain that runs through a toe -----------------------------------
                toed = new GameObject("__AdvisorTest_Toed");
                var toedDescriptor = toed.AddComponent<VRCAvatarDescriptor>();
                var leg = new GameObject("Leg");
                leg.transform.SetParent(toed.transform);
                var toe = new GameObject("Toe1");
                toe.transform.SetParent(leg.transform);
                var legBone = leg.AddComponent<VRCPhysBone>();
                legBone.rootTransform = leg.transform;

                var toeSettings = new BridgeSettings
                {
                    physicsTarget = PhysicsTarget.MagicaCloth2,
                    convertToePhysBones = false,
                };
                var toeAdvice = AvatarAdvisor.Analyse(toedDescriptor, toeSettings);
                var toes = Find(toeAdvice, "Convert toe PhysBones");

                fail += Check("a chain running through a toe is found",
                    toes != null);
                fail += Check("...and offered as a judgement call, never as a recommendation",
                    toes != null && toes.Kind == AdviceKind.Manual && toes.Apply != null);
                fail += Check("...and is silent once the box is already ticked",
                    Find(AvatarAdvisor.Analyse(toedDescriptor, new BridgeSettings
                    {
                        physicsTarget = PhysicsTarget.MagicaCloth2,
                        convertToePhysBones = true,
                    }), "Convert toe PhysBones") == null);

                // ---- and it survives an avatar that is barely an avatar -----------------
                fail += Check("a null descriptor returns nothing rather than throwing",
                    AvatarAdvisor.Analyse(null, new BridgeSettings()).Count == 0);
            }
            finally
            {
                foreach (var go in new[] { bare, chained, toed })
                {
                    if (go != null)
                    {
                        Object.DestroyImmediate(go);
                    }
                }
            }

            Debug.Log(fail == 0
                ? "[AdvisorTest] PASS — the advisor decides what it can and declines what it can't."
                : $"[AdvisorTest] FAIL — {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        static Advice Find(System.Collections.Generic.List<Advice> advice, string setting) =>
            advice.FirstOrDefault(a => a.Setting == setting);
    }
}
#endif
