#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Pass ordering (task #5): a pass that rewrites clips must run after the one copying them into
    // the conversion's folder. ConstraintConverter ran nine passes early and edited the author's
    // own clips, breaking three GoGo flight clips for VRChat; VRCFury's throwaway bake hid it.
    //
    // The first case matters most: a mis-ordered pipeline must be rejected, or a validator that
    // always says "fine" passes.
    public static class PipelineOrderTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Pass ordering invariant")]
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

            // An editor with nothing to take the clips over is just as wrong.
            var orphan = new List<BridgePass> { P("Constraints", PassTraits.EditsClips) };
            fail += Check("clip editor with no self-containment at all is REJECTED",
                BridgePipeline.Validate(orphan) != null);

            // A pipeline that touches no clips has nothing to order.
            var inert = new List<BridgePass> { P("Descriptor", PassTraits.None), P("Shaders", PassTraits.None) };
            fail += Check("pipeline that edits no clips is accepted",
                BridgePipeline.Validate(inert) == null);

            // And the real thing must be sound; this is the assertion that would have caught the
            // shipped bug, and it runs against the pipeline BridgeConverter actually declares.
            string live = BridgeConverter.ValidateLivePipelineForTest();
            fail += Check($"the SHIPPING pipeline validates ({live ?? "sound"})", live == null);

            Debug.Log(fail == 0
                ? "[PipelineOrderTest] PASS: bad orders are caught, the shipping order is sound."
                : $"[PipelineOrderTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
