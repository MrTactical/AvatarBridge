#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    /// <summary>
    /// Known-answer test for the pass-ordering invariant (task #5).
    ///
    /// The rule: a pass that rewrites animation clips must run AFTER the pass that copies those
    /// clips into the conversion's own folder. Before this was checked, BridgeConverter carried
    /// the rule as a comment — correct, and violated three lines above it. ConstraintConverter
    /// rewrote curves nine passes before self-containment and edited the avatar author's own
    /// animation files, which broke three of GoGo Loco's shipped flight clips for VRChat in a real
    /// project. It went unseen because VRCFury bakes to a throwaway copy on most avatars, so the
    /// bake regenerated the damage away on every run.
    ///
    /// The case that matters is the FIRST one: a deliberately mis-ordered pipeline must be
    /// rejected. A validator that only ever says "fine" would have passed review and caught
    /// nothing, which is exactly what the comment did.
    /// </summary>
    public static class PipelineOrderTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test — pass ordering invariant")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[PipelineOrderTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            BridgePass P(string name, PassTraits traits) =>
                new BridgePass { Name = name, Traits = traits, Run = _ => { } };

            // The bug, reconstructed: an editor before the self-container.
            var bad = new List<BridgePass>
            {
                P("Constraints", PassTraits.EditsClips),
                P("Self-contain", PassTraits.MakesClipsOurs),
            };
            string badProblem = BridgePipeline.Validate(bad);
            fail += Check("mis-ordered pipeline is REJECTED", badProblem != null);
            fail += Check("the complaint names both passes",
                badProblem != null && badProblem.Contains("Constraints") && badProblem.Contains("Self-contain"));

            // The order it ships in.
            var good = new List<BridgePass>
            {
                P("Merge", PassTraits.None),
                P("Self-contain", PassTraits.MakesClipsOurs),
                P("Constraints", PassTraits.EditsClips),
                P("Contacts", PassTraits.EditsClips),
            };
            fail += Check("correct ordering is accepted", BridgePipeline.Validate(good) == null);

            // An editor with nothing to make the clips ours is just as wrong.
            var orphan = new List<BridgePass> { P("Constraints", PassTraits.EditsClips) };
            fail += Check("clip editor with no self-containment at all is REJECTED",
                BridgePipeline.Validate(orphan) != null);

            // A pipeline that touches no clips has nothing to order.
            var inert = new List<BridgePass> { P("Descriptor", PassTraits.None), P("Shaders", PassTraits.None) };
            fail += Check("pipeline that edits no clips is accepted",
                BridgePipeline.Validate(inert) == null);

            // And the real thing must be sound — this is the assertion that would have caught the
            // shipped bug, and it runs against the pipeline BridgeConverter actually declares.
            string live = BridgeConverter.ValidateLivePipelineForTest();
            fail += Check($"the SHIPPING pipeline validates ({live ?? "sound"})", live == null);

            Debug.Log(fail == 0
                ? "[PipelineOrderTest] PASS — bad orders are caught, the shipping order is sound."
                : $"[PipelineOrderTest] FAIL — {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
