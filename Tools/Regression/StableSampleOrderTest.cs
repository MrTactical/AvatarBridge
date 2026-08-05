#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    /// <summary>
    /// Known-answer test for report sample ordering (task #13).
    ///
    /// Report details show a few entries out of many, taken from a SortedSet. That was already
    /// ordered, and still churned: VRCFury stamps generated objects with a component id —
    /// "[VF397] Assjob" — and assigns those ids fresh on every bake, so sorting the raw string
    /// reordered the set whenever Fury renumbered. The harness redacts "[VF397]" to "[VF#]" before
    /// comparing digests, so two runs of an unchanged avatar produced a diff naming a different
    /// path with an identical count. Seen on Angela_PC_SPS and Sally_PC_SPS on the same day.
    ///
    /// The ordering therefore ignores Fury ids. The second case below is the one that matters:
    /// ignoring them must NOT make two different strings compare equal, or a SortedSet would
    /// silently drop one and the report would under-count.
    /// </summary>
    public static class StableSampleOrderTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test — stable report sample order")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[StableSampleOrderTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            // The same two paths, as VRCFury numbered them on two different bakes.
            var bakeA = new SortedSet<string>(StableSampleOrder.Instance)
            {
                "Armature/Hips/[VF397] Assjob/Original",
                "Armature/Hips/[VF122] Pussy/Original",
            };
            var bakeB = new SortedSet<string>(StableSampleOrder.Instance)
            {
                "Armature/Hips/[VF605] Assjob/Original",
                "Armature/Hips/[VF801] Pussy/Original",
            };

            string firstA = bakeA.First();
            string firstB = bakeB.First();
            fail += Check($"same example chosen across renumbered bakes (\"{firstA}\" / \"{firstB}\")",
                StableSampleOrder.Key(firstA) == StableSampleOrder.Key(firstB));

            // Ordinal ordering would have put VF122 before VF397 in bake A and VF605 before VF801
            // in bake B — different paths. That is exactly the bug.
            var ordinalA = new SortedSet<string>(bakeA, System.StringComparer.Ordinal);
            var ordinalB = new SortedSet<string>(bakeB, System.StringComparer.Ordinal);
            fail += Check("plain ordinal ordering WOULD have churned (proving the case is real)",
                StableSampleOrder.Key(ordinalA.First()) != StableSampleOrder.Key(ordinalB.First()));

            // Different strings must stay distinct even when their keys match.
            var collide = new SortedSet<string>(StableSampleOrder.Instance)
            {
                "Armature/[VF1] Thing",
                "Armature/[VF2] Thing",
            };
            fail += Check("two entries differing ONLY by Fury id are both kept", collide.Count == 2);

            // And ordering must still be a total order for unrelated strings.
            var plain = new SortedSet<string>(StableSampleOrder.Instance) { "b", "a", "c", "a" };
            fail += Check("ordinary strings still sort and de-duplicate",
                plain.Count == 3 && string.Join(",", plain) == "a,b,c");

            Debug.Log(fail == 0
                ? "[StableSampleOrderTest] PASS — samples survive a Fury renumber, nothing is lost."
                : $"[StableSampleOrderTest] FAIL — {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
