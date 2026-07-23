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

        static readonly string[] SpsParamPrefixes =
        {
            "OGB/", "TPS_", "SPS", "VF77_", "VF23_", "pcs/", "VRCF_WSD", "WH_"
        };
        // "wholesome" and its helper layers belong to the Wholesome SPS audio add-on.
        static readonly string[] SpsLayerHints =
        {
            "sps", "ogb", "pcs", "haptic", "wsd", "world scale detector",
            "wholesome", "frametime counter", "max(gestureleft", "either fist"
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

        static HashSet<string> CollectParameterRefs(AnimatorStateMachine machine)
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
