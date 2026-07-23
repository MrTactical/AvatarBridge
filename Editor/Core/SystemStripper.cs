#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using ABI.CCK.Components;

namespace AvatarBridge
{
    /// <summary>
    /// Removes VRChat-only subsystems that are dead weight (or actively harmful) in
    /// ChilloutVR, freeing large amounts of sync budget and menu space:
    ///
    ///   GoGo Loco  — replaced by CVR's own locomotion/emote system
    ///   SPS / OGB / TPS / PCS — VRChat-specific penetration & haptics stacks
    ///
    /// Strategy: identify parameters by prefix, remove animator layers that mostly serve
    /// those parameters, delete the systems' scene objects, drop their menu entries, and
    /// let every surviving reference fall back to a local ("#") parameter so nothing
    /// breaks and nothing syncs.
    /// </summary>
    public static class SystemStripper
    {
        const string Category = "System stripping";

        static readonly string[] GogoParamPrefixes = { "Go/" };
        static readonly string[] GogoNameHints = { "gogo", "go loco", "goloco" };

        // "OGB" (no separator) also catches OGB_ENABLED and friends.
        static readonly string[] SpsParamPrefixes =
        {
            "OGB", "TPS_", "SPS", "VF77_", "VF23_", "pcs/", "VRCF_WSD", "WH_"
        };
        // "wholesome" is the Wholesome SPS audio add-on. Do NOT match generic Fury helper
        // names like "FrameTime Counter" or "EITHER FIST" here: they also belong to the
        // face-gesture smoothing system, which must survive.
        static readonly string[] SpsLayerHints =
        {
            "sps", "ogb", "pcs", "haptic", "wsd", "world scale detector", "wholesome"
        };
        static readonly string[] SpsObjectHints =
        {
            "BakedSpsSocket", "BakedSpsPlug", "Haptic Plug", "Haptic Socket",
            "<PCS Target>", "Penetration Contact System", "World Scale Detector", "SpsAutoDistance"
        };
        static readonly string[] SpsPointerTypePrefixes = { "TPS_", "SPSLL_", "OGB", "PCS", "VRCF_" };

        public static void Run(BridgeContext ctx, AnimatorController master, List<AnimatorControllerLayer> vrcLayers)
        {
            var paramPrefixes = new List<string>();
            var layerHints = new List<string>();
            if (ctx.Settings.stripGogoLoco)
            {
                paramPrefixes.AddRange(GogoParamPrefixes);
                layerHints.AddRange(GogoNameHints);
            }
            if (ctx.Settings.stripSpsSystems)
            {
                paramPrefixes.AddRange(SpsParamPrefixes);
                layerHints.AddRange(SpsLayerHints);
            }
            // User-supplied keywords (comma separated) act as both parameter prefixes and
            // layer-name hints, for add-ons this list doesn't know about yet.
            if (!string.IsNullOrWhiteSpace(ctx.Settings.extraStripKeywords))
            {
                foreach (var raw in ctx.Settings.extraStripKeywords.Split(','))
                {
                    string keyword = raw.Trim();
                    if (keyword.Length >= 2)
                    {
                        paramPrefixes.Add(keyword);
                        layerHints.Add(keyword.ToLowerInvariant());
                    }
                }
            }
            if (paramPrefixes.Count == 0)
            {
                return;
            }

            bool IsStrippedParam(string name) =>
                !string.IsNullOrEmpty(name) &&
                paramPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

            // VRCFury tags every generated layer with its component id ("[VF77] ...").
            // If a component's synced parameters are stripped, all its layers go too.
            var strippedFuryIds = new HashSet<string>();
            var vrcParams = ctx.SourceDescriptor.expressionParameters;
            if (vrcParams != null && vrcParams.parameters != null)
            {
                foreach (var p in vrcParams.parameters)
                {
                    if (string.IsNullOrEmpty(p.name) || !IsStrippedParam(p.name))
                    {
                        continue;
                    }
                    var match = System.Text.RegularExpressions.Regex.Match(p.name, @"^VF(\d+)_");
                    if (match.Success)
                    {
                        strippedFuryIds.Add(match.Groups[1].Value);
                    }
                }
            }

            RemoveLayers(ctx, master, vrcLayers, layerHints, strippedFuryIds, IsStrippedParam);
            PruneDirectBlendTrees(ctx, master, vrcLayers, IsStrippedParam);
            if (ctx.Settings.stripSpsSystems)
            {
                RemoveObjects(ctx);
                RemoveOrphanedCvrComponents(ctx, IsStrippedParam);
            }
            RemoveMenuEntries(ctx, IsStrippedParam);

            // Anything still referencing a stripped parameter keeps working, but the
            // parameter must never sync: dropping it from the preserve sets makes the
            // rename pass give it the local "#" prefix.
            ctx.PreserveParameters.RemoveWhere(IsStrippedParam);
            ctx.ContactParameters.RemoveWhere(IsStrippedParam);

            RemoveUnreferencedParameters(ctx, master, vrcLayers, IsStrippedParam);
        }

        // ------------------------------------------------------------------ layers ----

        static void RemoveLayers(BridgeContext ctx, AnimatorController master,
            List<AnimatorControllerLayer> vrcLayers, List<string> layerHints,
            HashSet<string> strippedFuryIds, Func<string, bool> isStripped)
        {
            var removedMachines = new HashSet<AnimatorStateMachine>();
            foreach (var layer in vrcLayers.ToList())
            {
                string lower = layer.name.ToLowerInvariant();
                bool nameHit = layerHints.Any(lower.Contains) ||
                               strippedFuryIds.Any(id => layer.name.Contains($"[VF{id}]"));

                var refs = CollectParameterRefs(layer.stateMachine);
                int strippedRefs = refs.Count(isStripped);
                bool referenceHit = strippedRefs > 0 && strippedRefs >= refs.Count * 0.6f;

                if (nameHit || referenceHit)
                {
                    removedMachines.Add(layer.stateMachine);
                    vrcLayers.Remove(layer);
                    ctx.Report.Converted(Category, $"Removed animator layer \"{layer.name}\"",
                        nameHit ? "Matched a stripped system by name." : $"{strippedRefs}/{refs.Count} parameter references belong to a stripped system.");
                }
            }

            if (removedMachines.Count > 0)
            {
                master.layers = master.layers
                    .Where(l => l.stateMachine == null || !removedMachines.Contains(l.stateMachine))
                    .ToArray();
            }
        }

        /// <summary>
        /// Modern VRCFury merges many features into shared direct blend trees ("DBT"),
        /// with clips that write animator parameters (AAPs) as math. When a system is
        /// stripped, its branches must be pruned out of those shared trees or its
        /// leftover math keeps running (integrating garbage values forever).
        /// </summary>
        static void PruneDirectBlendTrees(BridgeContext ctx, AnimatorController master,
            List<AnimatorControllerLayer> vrcLayers, Func<string, bool> isStripped)
        {
            int pruned = 0;
            foreach (var layer in vrcLayers.ToList())
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        child.state.motion = PruneMotion(child.state.motion, isStripped, ref pruned);
                    }
                });
            }
            if (pruned > 0)
            {
                ctx.Report.Converted(Category, $"Pruned {pruned} stripped branch(es)/tree(s) from shared blend trees",
                    "Removes leftover VRCFury parameter math for the stripped systems.");
            }

            // Layers reduced to empty states (their whole content was stripped math) go too.
            var inert = new List<AnimatorControllerLayer>();
            foreach (var layer in vrcLayers.ToList())
            {
                if (IsLayerInert(layer))
                {
                    inert.Add(layer);
                    vrcLayers.Remove(layer);
                    ctx.Report.Converted(Category, $"Removed emptied animator layer \"{layer.name}\"",
                        "All of its content belonged to stripped systems.");
                }
            }
            if (inert.Count > 0)
            {
                var machines = new HashSet<AnimatorStateMachine>(inert.Select(l => l.stateMachine));
                master.layers = master.layers
                    .Where(l => l.stateMachine == null || !machines.Contains(l.stateMachine))
                    .ToArray();
            }
        }

        /// <summary>
        /// Recursive dead-code elimination for motions:
        ///  - a clip is dead when it only writes stripped parameters (Fury AAP math)
        ///  - a tree is dead when it blends ON a stripped parameter (its entire subtree
        ///    exists to respond to a system that no longer exists)
        ///  - a direct tree drops dead children; any tree whose children are all dead
        ///    is dead itself
        /// Returns null when the whole motion is dead.
        /// </summary>
        static Motion PruneMotion(Motion motion, Func<string, bool> isStripped, ref int pruned)
        {
            if (motion == null)
            {
                return null;
            }
            if (motion is AnimationClip clip)
            {
                if (ClipWritesOnlyStrippedParams(clip, isStripped))
                {
                    pruned++;
                    return null;
                }
                return motion;
            }

            var tree = (BlendTree)motion;
            bool is2D = tree.blendType == BlendTreeType.SimpleDirectional2D ||
                        tree.blendType == BlendTreeType.FreeformDirectional2D ||
                        tree.blendType == BlendTreeType.FreeformCartesian2D;
            if (tree.blendType != BlendTreeType.Direct &&
                !string.IsNullOrEmpty(tree.blendParameter) && isStripped(tree.blendParameter))
            {
                pruned++;
                return null;
            }
            if (is2D && !string.IsNullOrEmpty(tree.blendParameterY) && isStripped(tree.blendParameterY))
            {
                pruned++;
                return null;
            }

            var children = tree.children;
            var kept = new List<ChildMotion>(children.Length);
            bool anyAlive = false;
            foreach (var child in children)
            {
                if (tree.blendType == BlendTreeType.Direct &&
                    !string.IsNullOrEmpty(child.directBlendParameter) && isStripped(child.directBlendParameter))
                {
                    pruned++;
                    continue;
                }
                var newMotion = PruneMotion(child.motion, isStripped, ref pruned);
                if (tree.blendType == BlendTreeType.Direct && newMotion == null)
                {
                    // Dead branch in a direct tree contributes nothing; drop it entirely.
                    pruned++;
                    continue;
                }
                var keptChild = child;
                keptChild.motion = newMotion;
                if (newMotion != null)
                {
                    anyAlive = true;
                }
                // Non-direct trees keep their slot layout (thresholds/positions) even if
                // a child motion died, so blending between the others stays correct.
                kept.Add(keptChild);
            }

            if (!anyAlive)
            {
                pruned++;
                return null;
            }
            tree.children = kept.ToArray();
            return tree;
        }

        static bool IsLayerInert(AnimatorControllerLayer layer)
        {
            bool inert = true;
            WalkMachines(layer.stateMachine, machine =>
            {
                if (machine.behaviours != null && machine.behaviours.Length > 0)
                {
                    inert = false;
                }
                foreach (var child in machine.states)
                {
                    if (child.state.motion != null ||
                        (child.state.behaviours != null && child.state.behaviours.Length > 0))
                    {
                        inert = false;
                    }
                }
            });
            return inert;
        }

        static bool ClipWritesOnlyStrippedParams(AnimationClip clip, Func<string, bool> isStripped)
        {
            var bindings = UnityEditor.AnimationUtility.GetCurveBindings(clip);
            if (bindings.Length == 0)
            {
                return false;
            }
            foreach (var binding in bindings)
            {
                if (binding.type != typeof(Animator) || !string.IsNullOrEmpty(binding.path) ||
                    !isStripped(binding.propertyName))
                {
                    return false;
                }
            }
            return UnityEditor.AnimationUtility.GetObjectReferenceCurveBindings(clip).Length == 0;
        }

        internal static void WalkMachines(AnimatorStateMachine machine, Action<AnimatorStateMachine> visit)
        {
            if (machine == null)
            {
                return;
            }
            visit(machine);
            foreach (var child in machine.stateMachines)
            {
                WalkMachines(child.stateMachine, visit);
            }
        }

        internal static HashSet<string> CollectParameterRefs(AnimatorStateMachine machine)
        {
            var refs = new HashSet<string>();
            void AddRef(string name)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    refs.Add(name);
                }
            }
            void WalkMotion(Motion motion)
            {
                if (!(motion is BlendTree tree))
                {
                    return;
                }
                AddRef(tree.blendParameter);
                if (tree.blendType != BlendTreeType.Direct && tree.blendType != BlendTreeType.Simple1D)
                {
                    AddRef(tree.blendParameterY);
                }
                foreach (var child in tree.children)
                {
                    if (tree.blendType == BlendTreeType.Direct)
                    {
                        AddRef(child.directBlendParameter);
                    }
                    WalkMotion(child.motion);
                }
            }
            void Walk(AnimatorStateMachine m)
            {
                if (m == null)
                {
                    return;
                }
                foreach (var t in m.anyStateTransitions)
                    foreach (var c in t.conditions) AddRef(c.parameter);
                foreach (var t in m.entryTransitions)
                    foreach (var c in t.conditions) AddRef(c.parameter);
                foreach (var behaviour in m.behaviours)
                    AddDriverRefs(behaviour, AddRef);
                foreach (var child in m.states)
                {
                    var state = child.state;
                    if (state.timeParameterActive) AddRef(state.timeParameter);
                    if (state.speedParameterActive) AddRef(state.speedParameter);
                    if (state.mirrorParameterActive) AddRef(state.mirrorParameter);
                    if (state.cycleOffsetParameterActive) AddRef(state.cycleOffsetParameter);
                    WalkMotion(state.motion);
                    foreach (var behaviour in state.behaviours)
                        AddDriverRefs(behaviour, AddRef);
                    foreach (var t in state.transitions)
                        foreach (var c in t.conditions) AddRef(c.parameter);
                }
                foreach (var child in m.stateMachines)
                {
                    Walk(child.stateMachine);
                }
            }
            Walk(machine);
            return refs;
        }

        static void AddDriverRefs(StateMachineBehaviour behaviour, Action<string> addRef)
        {
            if (!(behaviour is AnimatorDriver driver))
            {
                return;
            }
            foreach (var task in driver.EnterTasks.Concat(driver.ExitTasks))
            {
                addRef(task.targetName);
                if (task.aType == AnimatorDriverTask.SourceType.Parameter) addRef(task.aName);
                if (task.bType == AnimatorDriverTask.SourceType.Parameter) addRef(task.bName);
            }
        }

        // ----------------------------------------------------------------- objects ----

        static void RemoveObjects(BridgeContext ctx)
        {
            var doomed = new List<Transform>();
            foreach (var transform in ctx.Target.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null || transform == ctx.Target.transform)
                {
                    continue;
                }
                if (SpsObjectHints.Any(hint => transform.name.Contains(hint)))
                {
                    doomed.Add(transform);
                }
            }
            // Destroy outermost first; skip transforms that died with a parent.
            int removed = 0;
            foreach (var transform in doomed.OrderBy(t => Depth(t)))
            {
                if (transform != null)
                {
                    UnityEngine.Object.DestroyImmediate(transform.gameObject);
                    removed++;
                }
            }
            if (removed > 0)
            {
                ctx.Report.Converted(Category, $"Deleted {removed} SPS/PCS scene object tree(s)");
            }
        }

        static int Depth(Transform t)
        {
            int depth = 0;
            while (t.parent != null)
            {
                depth++;
                t = t.parent;
            }
            return depth;
        }

        static void RemoveOrphanedCvrComponents(BridgeContext ctx, Func<string, bool> isStripped)
        {
            int removed = 0;
            foreach (var pointer in ctx.Target.GetComponentsInChildren<CVRPointer>(true))
            {
                if (!string.IsNullOrEmpty(pointer.type) &&
                    SpsPointerTypePrefixes.Any(p => pointer.type.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    UnityEngine.Object.DestroyImmediate(pointer.gameObject);
                    removed++;
                }
            }
            foreach (var trigger in ctx.Target.GetComponentsInChildren<CVRAdvancedAvatarSettingsTrigger>(true))
            {
                var names = trigger.enterTasks.Select(t => t.settingName)
                    .Concat(trigger.exitTasks.Select(t => t.settingName))
                    .Concat(trigger.stayTasks.Select(t => t.settingName))
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();
                if (names.Count > 0 && names.All(isStripped))
                {
                    UnityEngine.Object.DestroyImmediate(trigger.gameObject);
                    removed++;
                }
            }
            foreach (var exclusion in ctx.Target.GetComponentsInChildren<FPRExclusion>(true))
            {
                if (exclusion.target == null)
                {
                    UnityEngine.Object.DestroyImmediate(exclusion.gameObject);
                    removed++;
                }
            }
            if (removed > 0)
            {
                ctx.Report.Converted(Category, $"Removed {removed} orphaned pointer/trigger/exclusion object(s)");
            }
        }

        // -------------------------------------------------------------------- menu ----

        static void RemoveMenuEntries(BridgeContext ctx, Func<string, bool> isStripped)
        {
            var settings = ctx.CvrAvatar.avatarSettings.settings;
            int before = settings.Count;
            settings.RemoveAll(e => isStripped(e.machineName));
            int removed = before - settings.Count;
            if (removed > 0)
            {
                ctx.Report.Converted(Category, $"Removed {removed} Advanced Avatar Settings entr(ies)",
                    "Their sync bits and menu slots are freed.");
            }
        }

        // -------------------------------------------------------------- parameters ----

        static void RemoveUnreferencedParameters(BridgeContext ctx, AnimatorController master,
            List<AnimatorControllerLayer> vrcLayers, Func<string, bool> isStripped)
        {
            var stillReferenced = new HashSet<string>();
            foreach (var layer in master.layers)
            {
                stillReferenced.UnionWith(CollectParameterRefs(layer.stateMachine));
            }
            var menuNames = new HashSet<string>(
                ctx.CvrAvatar.avatarSettings.settings.Select(e => e.machineName));

            // Any parameter that nothing reads, nothing syncs and no menu drives is dead
            // weight left behind by removed layers (VRCFury internals, stripped systems).
            var parameters = master.parameters;
            var kept = parameters
                .Where(p => stillReferenced.Contains(p.name) ||
                            menuNames.Contains(p.name) ||
                            AnimatorMerger.CvrCoreParameters.Contains(p.name) ||
                            GestureMap.GestureParameters.Contains(p.name) ||
                            ctx.PreserveParameters.Contains(p.name) ||
                            ctx.ContactParameters.Contains(p.name))
                .ToArray();
            int removed = parameters.Length - kept.Length;
            if (removed > 0)
            {
                master.parameters = kept;
                ctx.Report.Converted(Category, $"Removed {removed} dead animator parameter(s)");
            }
        }
    }
}
#endif
