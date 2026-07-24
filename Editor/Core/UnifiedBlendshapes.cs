#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;

namespace AvatarBridge
{
    public enum ShapeOp { Redirect, Expand }

    /// <summary>One shape's reconciliation: rename to a combined shape, or fan out to splits.</summary>
    public struct ShapeAction
    {
        public ShapeOp Op;
        public string[] Targets;   // Redirect → 1 target; Expand → the mesh's present components
        public float Scale;        // Redirect → 1 / (components collapsing onto the target)
    }

    /// <summary>
    /// Unified Expressions "blended shapes" reconciliation (see
    /// https://docs.vrcft.io/docs/tutorial-avatars/tutorial-avatars-extras/unified-blendshapes).
    ///
    /// The face-tracking rig drives a fixed set of shape names, but avatars only carry a subset
    /// at a given granularity: some ship a single combined shape (e.g. "LipFunnel"), others the
    /// directional components ("MouthUpperUpLeft/Right"). This maps a combined shape to its
    /// components so AvatarBridge can, per shape the mesh is missing, either COLLAPSE the driven
    /// components onto the combined shape the mesh has, or EXPAND a driven combined shape onto
    /// the split shapes the mesh has.
    ///
    /// It's an approximation: the rig's Direct blend tree *sums* contributions, so collapsing
    /// N components onto one shape scales each by 1/N (symmetric expressions reach full strength;
    /// asymmetric ones land at a fraction — which a combined-only mesh can't represent anyway).
    /// </summary>
    public static class UnifiedBlendshapes
    {
        // Combined shape → its directional components. Bilateral (Left/Right) unless quadranted.
        static readonly Dictionary<string, string[]> Combined = new Dictionary<string, string[]>
        {
            { "MouthUpperUp",   new[] { "MouthUpperUpLeft", "MouthUpperUpRight" } },
            { "MouthLowerDown", new[] { "MouthLowerDownLeft", "MouthLowerDownRight" } },
            { "MouthSmile",     new[] { "MouthSmileLeft", "MouthSmileRight" } },
            { "MouthFrown",     new[] { "MouthFrownLeft", "MouthFrownRight" } },
            { "MouthStretch",   new[] { "MouthStretchLeft", "MouthStretchRight" } },
            { "MouthPress",     new[] { "MouthPressLeft", "MouthPressRight" } },
            { "MouthDimple",    new[] { "MouthDimpleLeft", "MouthDimpleRight" } },
            { "MouthTightener", new[] { "MouthTightenerLeft", "MouthTightenerRight" } },
            { "LipPucker",      new[] { "LipPuckerLeft", "LipPuckerRight" } },
            { "CheekPuff",      new[] { "CheekPuffLeft", "CheekPuffRight" } },
            { "CheekSquint",    new[] { "CheekSquintLeft", "CheekSquintRight" } },
            { "CheekSuck",      new[] { "CheekSuckLeft", "CheekSuckRight" } },
            { "EyeSquint",      new[] { "EyeSquintLeft", "EyeSquintRight" } },
            { "EyeWide",        new[] { "EyeWideLeft", "EyeWideRight" } },
            { "BrowDown",       new[] { "BrowDownLeft", "BrowDownRight" } },
            { "BrowInnerUp",    new[] { "BrowInnerUpLeft", "BrowInnerUpRight" } },
            { "BrowOuterUp",    new[] { "BrowOuterUpLeft", "BrowOuterUpRight" } },
            { "NoseSneer",      new[] { "NoseSneerLeft", "NoseSneerRight" } },
            { "LipFunnel",      new[] { "LipFunnelUpperLeft", "LipFunnelUpperRight",
                                       "LipFunnelLowerLeft", "LipFunnelLowerRight" } },
        };

        static readonly Dictionary<string, string> ComponentToCombined = BuildReverse();

        static Dictionary<string, string> BuildReverse()
        {
            var d = new Dictionary<string, string>();
            foreach (var kv in Combined)
            {
                foreach (var c in kv.Value)
                {
                    d[c.ToLowerInvariant()] = kv.Key;
                }
            }
            return d;
        }

        /// <summary>
        /// Decides, for each shape the rig drives that the mesh lacks, how to reconcile it.
        /// <paramref name="meshActual"/> maps a lowercased shape name to its real casing.
        /// </summary>
        public static Dictionary<string, ShapeAction> BuildPlan(
            IEnumerable<string> rigShapes, Dictionary<string, string> meshActual, out List<string> unmapped)
        {
            var rig = rigShapes.Distinct().ToList();
            unmapped = new List<string>();
            var plan = new Dictionary<string, ShapeAction>();

            // How many driven components collapse onto each combined target — sets the scale so
            // simultaneous components sum to full, not N×full, through the Direct blend tree.
            var collapseCount = new Dictionary<string, int>();
            foreach (var shape in rig)
            {
                if (meshActual.ContainsKey(shape.ToLowerInvariant())) continue;
                if (ComponentToCombined.TryGetValue(shape.ToLowerInvariant(), out var comb)
                    && meshActual.ContainsKey(comb.ToLowerInvariant()))
                {
                    collapseCount[comb] = collapseCount.TryGetValue(comb, out var n) ? n + 1 : 1;
                }
            }

            foreach (var shape in rig)
            {
                if (meshActual.ContainsKey(shape.ToLowerInvariant())) continue; // mesh has it → keep

                // Collapse: this is a component and the mesh has its combined shape.
                if (ComponentToCombined.TryGetValue(shape.ToLowerInvariant(), out var combined)
                    && meshActual.TryGetValue(combined.ToLowerInvariant(), out var combinedActual))
                {
                    int n = collapseCount.TryGetValue(combined, out var c) ? c : 1;
                    plan[shape] = new ShapeAction
                    {
                        Op = ShapeOp.Redirect,
                        Targets = new[] { combinedActual },
                        Scale = 1f / Math.Max(1, n)
                    };
                    continue;
                }

                // Expand: this is a combined shape and the mesh has some of its components.
                if (Combined.TryGetValue(shape, out var comps))
                {
                    var present = comps
                        .Where(x => meshActual.ContainsKey(x.ToLowerInvariant()))
                        .Select(x => meshActual[x.ToLowerInvariant()])
                        .ToArray();
                    if (present.Length > 0)
                    {
                        plan[shape] = new ShapeAction { Op = ShapeOp.Expand, Targets = present, Scale = 1f };
                        continue;
                    }
                }

                unmapped.Add(shape);
            }
            return plan;
        }
    }
}
#endif
