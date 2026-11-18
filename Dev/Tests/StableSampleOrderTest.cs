#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Report sample ordering (task #13). VRCFury renumbers its "[VF397]" ids on every bake, so
    // sorting the raw string churned the samples between runs of an unchanged avatar. The order
    // ignores Fury ids, but must never make two different strings equal, or the SortedSet drops one.
    public static class StableSampleOrderTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Stable report sample order")]
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
            // in bake B; different paths. That is exactly the bug.
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
                ? "[StableSampleOrderTest] PASS: samples survive a Fury renumber, nothing is lost."
                : $"[StableSampleOrderTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
