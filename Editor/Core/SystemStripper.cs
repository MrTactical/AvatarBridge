#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using ABI.CCK.Components;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge
{
    // Removes VRChat-only subsystems that are dead weight or harmful
    // in ChilloutVR: GoGo Loco (CVR has its own locomotion) and the
    // SPS/OGB/TPS/PCS stacks. Identify parameters by prefix, remove
    // layers that mostly serve them, delete their scene objects and
    // menu entries, and let survivors fall back to "#" locals.
    public static class SystemStripper
    {
        const string Category = "System stripping";

        static readonly string[] GogoParamPrefixes = { "Go/" };
        static readonly string[] GogoNameHints = { "gogo", "go loco", "goloco" };

        // "OGB" (no separator) also catches OGB_ENABLED and friends.
        //
        // Named apart from the rest of the family because the object and
        // pointer lists below DO change when the penetration system is
        // being converted rather than removed. Parameters and layers do
        // not — see KeepingPenetration.
        static readonly string[] YapsParamPrefixes = { "OGB", "TPS_", "SPS" };
        static readonly string[] OtherSpsParamPrefixes =
        {
            "VF77_", "VF23_", "pcs/", "VRCF_WSD", "WH_"
        };
        // "wholesome" is the Wholesome SPS audio add-on. Do NOT match generic Fury helper
        // names like "FrameTime Counter" or "EITHER FIST" here: they also belong to the
        // face-gesture smoothing system, which must survive.
        static readonly string[] YapsLayerHints = { "sps", "ogb", "haptic" };
        static readonly string[] OtherSpsLayerHints =
        {
            "pcs", "wsd", "world scale detector", "wholesome"
        };
        static readonly string[] YapsObjectHints =
        {
            "BakedSpsSocket", "BakedSpsPlug", "Haptic Plug", "Haptic Socket", "SpsAutoDistance"
        };
        static readonly string[] OtherSpsObjectHints =
        {
            "<PCS Target>", "Penetration Contact System", "World Scale Detector"
        };
        static readonly string[] YapsPointerTypePrefixes = { "TPS_", "SPSLL_", "OGB" };
        static readonly string[] OtherSpsPointerTypePrefixes = { "PCS", "VRCF_" };

        // The penetration system survives the strip only while it is
        // being converted into something CVR can run.
        //
        // What survives is the SCENE: the plug and socket objects, their
        // marker lights, and the pointers a socket is found by. The
        // PARAMETERS still go, and that is not an oversight.
        //
        // Measured on a real avatar: converted without the penetration
        // system she spends 544 of ChilloutVR's 3200 sync bits, and keeping
        // VRChat's haptic parameters took her to 3200 exactly — around 83
        // synced floats for 119 contacts. YAPS needs none of them. It
        // builds its own channel out of four floats, or one, or none. What
        // is lost with them is VRChat's depth-driven haptic animation,
        // which was written against an API ChilloutVR does not have, and
        // 2650 sync bits is not a price worth paying for it.
        static bool KeepingPenetration(BridgeContext ctx) =>
            ctx.Settings.stripSpsSystems && ctx.Settings.convertYapsSystems;

        static readonly string[] AllSpsParamPrefixes =
            OtherSpsParamPrefixes.Concat(YapsParamPrefixes).ToArray();
        static readonly string[] AllSpsLayerHints =
            OtherSpsLayerHints.Concat(YapsLayerHints).ToArray();

        internal static bool AvatarUsesGogo(BridgeContext ctx) =>
            AvatarUsesGogo(ctx != null ? ctx.SourceDescriptor : null);

        internal static bool AvatarUsesGogo(VRCAvatarDescriptor descriptor)
        {
            var vrcParams = descriptor != null ? descriptor.expressionParameters : null;
            if (vrcParams == null || vrcParams.parameters == null)
            {
                return false;
            }
            return vrcParams.parameters.Any(p => p != null && !string.IsNullOrEmpty(p.name)
                && GogoParamPrefixes.Any(g => p.name.StartsWith(g, StringComparison.OrdinalIgnoreCase)));
        }

        static IEnumerable<string> StrippedParameterPrefixes(BridgeContext ctx)
        {
            var prefixes = new List<string>();
            if (ctx.Settings.stripGogoLoco)
            {
                prefixes.AddRange(GogoParamPrefixes);
                if (AvatarUsesGogo(ctx))
                {
                    prefixes.Add("VRCEmote");
                }
            }
            if (ctx.Settings.stripSpsSystems)
            {
                // Converting the penetration system keeps its DEPTH
                // parameters rather than stripping them, and makes them
                // local instead. That is what brings back the reactions a
                // socket plays as a plug arrives — the bulges, the winces —
                // which were being thrown out with everything else.
                //
                // Keeping them synced is what cost 2650 bits and made an
                // avatar unuploadable. Local costs nothing, and it is also
                // the only correct choice: a contact must drive a local
                // parameter or the incoming stream overwrites it. Every
                // client runs the contact itself and reaches the same
                // answer, so nothing needs transmitting.
                if (KeepingPenetration(ctx))
                {
                    prefixes.AddRange(OtherSpsParamPrefixes);
                }
                else
                {
                    prefixes.AddRange(AllSpsParamPrefixes);
                }
            }
            if (!string.IsNullOrWhiteSpace(ctx.Settings.extraStripKeywords))
            {
                foreach (var raw in ctx.Settings.extraStripKeywords.Split(','))
                {
                    string keyword = raw.Trim();
                    if (keyword.Length >= 2)
                    {
                        prefixes.Add(keyword);
                    }
                }
            }
            return prefixes;
        }

        public static bool WillBeStripped(BridgeContext ctx, string name) =>
            !string.IsNullOrEmpty(name) &&
            StrippedParameterPrefixes(ctx).Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        public static void Run(BridgeContext ctx, AnimatorController master, List<AnimatorControllerLayer> vrcLayers)
        {
            // Keeping GoGo is supported, but only whole: its poses are
            // states in Base and Action, and unmerged layers leave a
            // dance wheel of dead entries. Warn at the decision.
            if (!ctx.Settings.stripGogoLoco && AvatarUsesGogo(ctx)
                && (!ctx.Settings.convertBaseLayer || !ctx.Settings.convertActionLayer))
            {
                ctx.Report.Warning("System stripping", "GoGo Loco kept, but its home layers aren't merged",
                    "Stripping is off and this avatar carries GoGo Loco, but the Base/Action layers " +
                    "— where GoGo's poses and dances actually live — aren't ticked under \"Animator " +
                    "layers to merge\". The pose wheel will convert and drive nothing. Tick Base and " +
                    "Action and convert again to bring GoGo across whole.");
            }

            // First, and unconditionally: this one isn't a preference about which VRChat add-ons
            // you want kept, it's a workaround for a VRChat limit that breaks sync when carried
            // into ChilloutVR. It also has to run ahead of the early return below, which fires
            // when the user has turned every other stripper off.
            StripParameterCompressor(ctx, master, vrcLayers);

            var paramPrefixes = new List<string>(StrippedParameterPrefixes(ctx));
            var layerHints = new List<string>();
            if (ctx.Settings.stripGogoLoco)
            {
                layerHints.AddRange(GogoNameHints);
            }
            if (ctx.Settings.stripSpsSystems)
            {
                if (KeepingPenetration(ctx))
                {
                    // The layers that play a socket's reactions stay, since
                    // their parameters now do.
                    layerHints.AddRange(OtherSpsLayerHints);
                    ctx.ForceLocalPrefixes.AddRange(YapsParamPrefixes);
                }
                else
                {
                    layerHints.AddRange(AllSpsLayerHints);
                }
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
                // Scene objects are removed much earlier, by RemoveStrippedObjects.
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

        internal static void RemoveLayersForTest(BridgeContext ctx, AnimatorController master,
            List<AnimatorControllerLayer> vrcLayers)
        {
            var paramPrefixes = new List<string>(StrippedParameterPrefixes(ctx));
            var layerHints = new List<string>();
            if (ctx.Settings.stripGogoLoco)
            {
                layerHints.AddRange(GogoNameHints);
            }
            if (ctx.Settings.stripSpsSystems)
            {
                layerHints.AddRange(AllSpsLayerHints);
            }
            bool IsStrippedParam(string name) =>
                !string.IsNullOrEmpty(name) &&
                paramPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            RemoveLayers(ctx, master, vrcLayers, layerHints, new HashSet<string>(), IsStrippedParam);
        }

        static bool NamesGogoParameterFamily(string lowerName)
        {
            for (int at = 0; (at = lowerName.IndexOf("go/", at, StringComparison.Ordinal)) >= 0; at += 3)
            {
                if (at == 0 || !char.IsLetterOrDigit(lowerName[at - 1]))
                {
                    return true;
                }
            }
            return false;
        }

        static void RemoveLayers(BridgeContext ctx, AnimatorController master,
            List<AnimatorControllerLayer> vrcLayers, List<string> layerHints,
            HashSet<string> strippedFuryIds, Func<string, bool> isStripped)
        {
            var removedMachines = new HashSet<AnimatorStateMachine>();
            foreach (var layer in vrcLayers.ToList())
            {
                string lower = layer.name.ToLowerInvariant();
                bool nameHit = layerHints.Any(lower.Contains) ||
                               (ctx.Settings.stripGogoLoco && NamesGogoParameterFamily(lower)) ||
                               strippedFuryIds.Any(id => layer.name.Contains($"[VF{id}]"));

                var refs = CollectParameterRefs(layer.stateMachine);
                int strippedRefs = refs.Count(isStripped);
                bool referenceHit = strippedRefs > 0 && strippedRefs >= refs.Count * 0.6f;

                // ...unless the layer is shared. VRCFury folds unrelated
                // toggles into one Direct tree, so a majority of
                // stripped references proves nothing about the rest.
                // The majority heuristic never deletes a shared tree;
                // PruneDirectBlendTrees takes branches out one at a
                // time. A tree with zero survivors still goes whole.
                if (referenceHit && strippedRefs < refs.Count && ContainsDirectBlendTree(layer.stateMachine))
                {
                    ctx.Report.Converted(Category, $"Kept shared layer \"{layer.name}\" and pruned it instead",
                        $"{strippedRefs} of its {refs.Count} parameter references belong to stripped systems, " +
                        "but this layer is a shared blend tree — VRChat tooling packs unrelated toggles into " +
                        "one of these for performance. Removing it would take the other " +
                        $"{refs.Count - strippedRefs} along with it (that is how an avatar loses its whole " +
                        "wardrobe to an SPS strip). The stripped branches are pruned out individually below.");
                    referenceHit = false;
                }

                // Locomotion replacements are all-or-nothing. GoGo's
                // layers condition mostly on game-fed built-ins, so the
                // majority test never fires for them. Any GoGo
                // reference in a merged Base/Additive/Action layer
                // disqualifies the layer; only GoGo puts Go/ there.
                bool locomotionHit = false;
                if (strippedRefs > 0 &&
                    (layer.name.StartsWith("[Base]") || layer.name.StartsWith("[Additive]")
                     || layer.name.StartsWith("[Action]")))
                {
                    locomotionHit = refs.Any(r =>
                    {
                        string bare = r.TrimStart('#');
                        return isStripped(r) &&
                               (bare.StartsWith("Go/") || bare == "VRCEmote");
                    });
                }

                if (nameHit || referenceHit || locomotionHit)
                {
                    removedMachines.Add(layer.stateMachine);
                    vrcLayers.Remove(layer);
                    ctx.Report.Converted(Category, $"Removed animator layer \"{layer.name}\"",
                        nameHit ? "Matched a stripped system by name."
                        : referenceHit ? $"{strippedRefs}/{refs.Count} parameter references belong to a stripped system."
                        : "A Base/Additive/Action layer referencing GoGo parameters — locomotion " +
                          "replacements are all-or-nothing, and left in place with its parameters " +
                          "stripped this layer overrides ChilloutVR's own locomotion with half-dead " +
                          "animation.");
                }
            }

            if (removedMachines.Count > 0)
            {
                master.layers = master.layers
                    .Where(l => l.stateMachine == null || !removedMachines.Contains(l.stateMachine))
                    .ToArray();
            }
        }

        static bool ContainsDirectBlendTree(AnimatorStateMachine machine)
        {
            if (machine == null)
            {
                return false;
            }
            bool Search(Motion motion)
            {
                if (!(motion is BlendTree tree))
                {
                    return false;
                }
                if (tree.blendType == BlendTreeType.Direct)
                {
                    return true;
                }
                foreach (var child in tree.children)
                {
                    if (Search(child.motion))
                    {
                        return true;
                    }
                }
                return false;
            }
            foreach (var child in machine.states)
            {
                if (child.state != null && Search(child.state.motion))
                {
                    return true;
                }
            }
            foreach (var child in machine.stateMachines)
            {
                if (ContainsDirectBlendTree(child.stateMachine))
                {
                    return true;
                }
            }
            return false;
        }

        internal static void PruneDirectBlendTrees(BridgeContext ctx, AnimatorController master,
            List<AnimatorControllerLayer> vrcLayers, Func<string, bool> isStripped)
        {
            int pruned = 0;
            // Which parameter names justified each layer's pruning.
            // The report shows the reasoning, so an overreaching prefix
            // is visible in the conversion that did it.
            var perLayer = new Dictionary<AnimatorControllerLayer, SortedSet<string>>();
            foreach (var layer in vrcLayers.ToList())
            {
                var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        child.state.motion = PruneMotion(child.state.motion, isStripped, ref pruned, names);
                    }
                });
                if (names.Count > 0)
                {
                    perLayer[layer] = names;
                }
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
                    string evidence = perLayer.TryGetValue(layer, out var names)
                        ? $" The stripped parameter names that emptied it: {string.Join(", ", names)}. " +
                          "If one of these is a control you actually use, its machinery was " +
                          "misclassified — say so in an issue with this line, it is exactly the " +
                          "evidence needed."
                        : "";
                    ctx.Report.Converted(Category, $"Removed emptied animator layer \"{layer.name}\"",
                        "All of its content belonged to stripped systems." + evidence);
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

        static Motion PruneMotion(Motion motion, Func<string, bool> isStripped, ref int pruned,
            SortedSet<string> prunedNames = null)
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
                    if (prunedNames != null)
                    {
                        foreach (var binding in UnityEditor.AnimationUtility.GetCurveBindings(clip))
                        {
                            if (isStripped(binding.propertyName))
                            {
                                prunedNames.Add(binding.propertyName);
                            }
                        }
                    }
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
                prunedNames?.Add(tree.blendParameter);
                return null;
            }
            if (is2D && !string.IsNullOrEmpty(tree.blendParameterY) && isStripped(tree.blendParameterY))
            {
                pruned++;
                prunedNames?.Add(tree.blendParameterY);
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
                    prunedNames?.Add(child.directBlendParameter);
                    continue;
                }
                var newMotion = PruneMotion(child.motion, isStripped, ref pruned, prunedNames);
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

        static readonly string[] FaceTrackingObjectHints =
        {
            "VRCFT", "VRCFaceTracking", "OSCmooth", "OSCm_",
        };

        internal static void StripParameterCompressorForTest(BridgeContext ctx, AnimatorController master,
            List<AnimatorControllerLayer> vrcLayers) => StripParameterCompressor(ctx, master, vrcLayers);

        static void StripParameterCompressor(BridgeContext ctx, AnimatorController master,
            List<AnimatorControllerLayer> vrcLayers)
        {
            var declared = new HashSet<string>(master.parameters.Select(p => p.name));
            // VRCFury names rotation slots by the type they carry:
            // SyncDataBool0, SyncDataFloat3, SyncDataInt2, plus the
            // SyncIndex window pointer. The pattern must match all of
            // them, or an avatar with slots and no mirrors turns every
            // parameter local.
            var slots = new System.Text.RegularExpressions.Regex(
                @"^VF\d+_Sync(Index|Data(Bool|Float|Int|Num)?)\d*$");
            var mirror = new System.Text.RegularExpressions.Regex(@"^VF\d+_(.+)$");

            // Referenced names as well as declared ones. The
            // compressor's mirrors are usually referenced without
            // being declared.
            var known = new HashSet<string>(declared);
            foreach (var layer in master.layers)
            {
                known.UnionWith(CollectParameterRefs(layer.stateMachine));
            }

            // A mirror is only a mirror if the thing it shadows is a real, declared parameter.
            // That test is what separates "VF87_AvatarLimbScaling_Arms" from VRCFury's own working
            // values like "VF113_frameTime", which shadow nothing and must be left alone.
            var mirrors = new Dictionary<string, string>();
            foreach (string name in known)
            {
                var match = mirror.Match(name);
                if (match.Success && declared.Contains(match.Groups[1].Value))
                {
                    mirrors[name] = match.Groups[1].Value;
                }
            }
            var slotNames = known.Where(n => slots.IsMatch(n)).ToList();
            if (mirrors.Count == 0 && slotNames.Count == 0)
            {
                return;
            }

            bool IsCompressor(string name) =>
                !string.IsNullOrEmpty(name) &&
                (mirrors.ContainsKey(name) || slots.IsMatch(name));

            var doomed = new HashSet<AnimatorStateMachine>();
            foreach (var layer in vrcLayers.ToList())
            {
                if (layer.name != null &&
                    layer.name.IndexOf("Parameter Compressor", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    doomed.Add(layer.stateMachine);
                    vrcLayers.Remove(layer);
                }
            }
            if (doomed.Count > 0)
            {
                master.layers = master.layers
                    .Where(l => l.stateMachine == null || !doomed.Contains(l.stateMachine))
                    .ToArray();
            }
            // The rotation also lives as children of the shared math tree, not only in its own
            // layer, so the same Direct-blend pruning the other stripped systems get applies here.
            PruneDirectBlendTrees(ctx, master, vrcLayers, IsCompressor);

            // The real parameters sync natively from here on, so they must not be given the
            // local-only prefix on the strength of a not-synced flag the compressor set.
            foreach (string real in mirrors.Values.Distinct())
            {
                ctx.PreserveParameters.Add(real);
            }

            // Mirrors are not always there. When the compressor is
            // detected at all, every declared parameter that is not its
            // own artefact is treated as really synced. "Not synced" is
            // not trustworthy on a compressed avatar; de-syncing is
            // what the compressor does. Restoring sync costs 32 bits;
            // the other mistake costs the feature. VF-prefixed names
            // are VRCFury's own working values and stay excluded.
            var furyOwn = new System.Text.RegularExpressions.Regex(@"^VF\d+_");
            foreach (var p in master.parameters)
            {
                if (!string.IsNullOrEmpty(p.name) && !furyOwn.IsMatch(p.name) && !IsCompressor(p.name))
                {
                    ctx.PreserveParameters.Add(p.name);
                }
            }

            ctx.Report.Converted(Category,
                $"Removed VRCFury's parameter compressor — {doomed.Count} layer(s), " +
                $"{mirrors.Count} mirrored and {slotNames.Count} slot parameter(s)",
                "It works around VRChat's 256-parameter limit by de-syncing your parameters and " +
                "rotating copies of them through a couple of slots twice a second. ChilloutVR has " +
                "3200 bits and syncs straight from the animator, so this cost a per-frame blend " +
                "tree and — because the originals were left marked not-synced — stopped the values " +
                $"reaching anyone at all. {mirrors.Values.Distinct().Count()} parameter(s) now sync " +
                "natively and without the delay.");
        }

        internal static void RemoveStrippedObjects(BridgeContext ctx)
        {
            if (ctx.Settings.stripSpsSystems)
            {
                RemoveObjects(ctx);
            }
            RemoveFaceTrackingObjects(ctx);
        }

        static void RemoveFaceTrackingObjects(BridgeContext ctx)
        {
            if (ctx.Settings.faceTrackingMode == FaceTrackingMode.None)
            {
                return;
            }

            var doomed = new List<Transform>();
            foreach (var transform in ctx.Target.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null || transform == ctx.Target.transform)
                {
                    continue;
                }
                if (FaceTrackingObjectHints.Any(hint =>
                        transform.name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    doomed.Add(transform);
                }
            }
            if (doomed.Count == 0)
            {
                return;
            }

            var names = doomed.Select(t => t.name).Distinct().Take(6).ToList();
            int removed = 0;
            foreach (var transform in doomed.OrderBy(Depth))
            {
                if (transform == null)
                {
                    continue; // died with a parent
                }
                UnityEngine.Object.DestroyImmediate(transform.gameObject);
                removed++;
            }
            ctx.Report.Converted(Category, $"Removed the avatar's VRChat face-tracking rig — {removed} object(s)",
                $"{string.Join(", ", names)}{(doomed.Count > names.Count ? ", …" : "")} — the chosen face " +
                "tracking mode provides its own, and two rigs driving the same blendshapes fight each other. " +
                "Choose \"None\" if you want the original rig left in place.");
        }

        static void RemoveObjects(BridgeContext ctx)
        {
            var objectHints = KeepingPenetration(ctx)
                ? OtherSpsObjectHints
                : OtherSpsObjectHints.Concat(YapsObjectHints).ToArray();
            var doomed = new List<Transform>();
            foreach (var transform in ctx.Target.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null || transform == ctx.Target.transform)
                {
                    continue;
                }
                if (objectHints.Any(hint => transform.name.Contains(hint)))
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
            // A converted socket's pointer is the thing a plug looks for,
            // so it has to outlive the strip that used to delete it.
            var pointerPrefixes = KeepingPenetration(ctx)
                ? OtherSpsPointerTypePrefixes
                : OtherSpsPointerTypePrefixes.Concat(YapsPointerTypePrefixes).ToArray();
            int removed = 0;
            foreach (var pointer in ctx.Target.GetComponentsInChildren<CVRPointer>(true))
            {
                if (!string.IsNullOrEmpty(pointer.type) &&
                    pointerPrefixes.Any(p => pointer.type.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
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
