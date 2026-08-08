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

        /// <summary>
        /// GoGo Loco's own parameter list (GoAllParameters.asset) is sixteen "Go/" names plus one
        /// that carries no prefix at all: "VRCEmote", the community emote parameter GoGo declares
        /// and drives its whole emote/dance system through. Missing it unravels the entire strip —
        /// on a real avatar the 102-state action layer conditioned on VRCEmote survived, which
        /// kept every "Go/" parameter it referenced alive, which kept their garbage-labelled menu
        /// entries ("- (-)", "- (GoGo Loco By Franada)") in the converted menu.
        ///
        /// Claimed only when the avatar actually carries GoGo ("Go/" parameters declared), because
        /// VRCEmote itself is a VRChat community convention, not GoGo property — on a non-GoGo
        /// avatar it belongs to whatever emote system the author built, and that is not ours to
        /// condemn under a GoGo switch.
        /// </summary>
        internal static bool AvatarUsesGogo(BridgeContext ctx) =>
            AvatarUsesGogo(ctx != null ? ctx.SourceDescriptor : null);

        /// <summary>
        /// The same question asked of a descriptor alone, for AvatarAdvisor: it runs before any
        /// BridgeContext exists, and a separate "is this GoGo?" test in the advisor would be one
        /// more place for the two to disagree about what the conversion is going to do.
        /// </summary>
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

        /// <summary>
        /// The parameter prefixes this conversion is going to strip, given the settings.
        /// </summary>
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
                prefixes.AddRange(SpsParamPrefixes);
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

        /// <summary>
        /// Whether a parameter belongs to a system this conversion is about to remove.
        ///
        /// Needed by passes that run *before* stripping and might otherwise rename the parameter
        /// out of its own prefix — at which point the stripper no longer recognises it and the
        /// system it belonged to survives under an unrecognisable name. GoGo Loco ships a
        /// two-axis puppet on "Go/PuppetX"/"Go/PuppetY", and turning that into a joystick renamed
        /// both out of the "Go/" family that marks them for removal.
        /// </summary>
        public static bool WillBeStripped(BridgeContext ctx, string name) =>
            !string.IsNullOrEmpty(name) &&
            StrippedParameterPrefixes(ctx).Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        public static void Run(BridgeContext ctx, AnimatorController master, List<AnimatorControllerLayer> vrcLayers)
        {
            // Keeping GoGo is a supported choice, but only whole: its poses and dances are STATES
            // in the Base and Action layers, and with those layers unmerged the menus convert
            // while the motion they drive does not — a dance wheel full of dead entries that
            // reads as a converter bug. Warn at the decision, not after the confusion.
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
                // NOTE: the scene objects themselves are removed much earlier, by
                // RemoveStrippedObjects — see the comment there for why.
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

        /// <summary>
        /// Just the layer-removal half, for the known-answer test. Run() continues into menu
        /// entries, orphaned components and parameter pruning, which need a CVRAvatar and a
        /// descriptor the test has no reason to build — and none of which decide which LAYERS go.
        /// </summary>
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
                layerHints.AddRange(SpsLayerHints);
            }
            bool IsStrippedParam(string name) =>
                !string.IsNullOrEmpty(name) &&
                paramPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            RemoveLayers(ctx, master, vrcLayers, layerHints, new HashSet<string>(), IsStrippedParam);
        }

        /// <summary>
        /// A layer named after GoGo Loco's PARAMETER family — "Go/Beyond", "[VF80] Go/Locomotion".
        ///
        /// The word hints above ("gogo", "go loco", "goloco") are how GoGo names itself when a user
        /// installs it by hand. Installed through a VRCFury prefab it names its layers after its
        /// parameters instead, and "Go/Beyond" contains none of those three words — so the layer
        /// survived a strip that had already neutered its parameters. Measured on 9 of 53 corpus
        /// avatars, and it is not harmless: the survivor sits at weight 1 on Override and its
        /// transitions read ChilloutVR's OWN parameters — Sitting, Grounded, AFK, IsLocal, VRMode —
        /// so sitting on a chair in game played GoGo's seat clip over ChilloutVR's station pose.
        /// Reported from the headset before this check existed.
        ///
        /// Matched only where "go/" STARTS a word, so "Go/Beyond" and "[VF80] Go/Locomotion" hit
        /// while "Cargo/Rack" does not. The looser spelling would be a substring test, and this
        /// list's other entries carry a comment about exactly that kind of false positive.
        /// </summary>
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

                // …unless the layer is a SHARED one, in which case a majority means nothing.
                //
                // VRCFury's LayerToTreeService folds dozens of unrelated toggles into a single
                // Direct blend tree for performance. On an NSFW-heavy avatar most of those
                // toggles belong to systems being stripped, so the 60% test fired and deleted
                // the whole layer — and with it every innocent branch sharing the ride. One
                // avatar lost its ENTIRE wardrobe that way: 116 of 162 references were stripped
                // systems, the other 46 were the clothing, and all 46 went too. The menu entries
                // then looked dead (nothing read their parameters, because their only reader had
                // just been deleted) and were tidied away, so the toggles vanished from the menu
                // as well — three passes each behaving correctly on the wreckage of the first.
                //
                // A direct tree is the aggregator pattern by construction, and PruneDirectBlend-
                // Trees on the very next line exists to take exactly these branches out one at a
                // time. So: name matches still remove the layer (that names its owner), but the
                // majority heuristic never deletes a shared tree — it gets pruned instead.
                // …but only when something innocent is actually riding along. A shared tree whose
                // every reference belongs to the stripped system has no passengers to protect,
                // and keeping it just leaves that system's dead machinery in the avatar — which
                // is what the strip was asked to remove. Seen on "[VF173] PCS: Activation":
                // 7 of 7 references stripped, 0 survivors, kept for nothing.
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

                // Locomotion replacements are all-or-nothing. GoGo's Base/Poses/Action layers
                // condition mostly on Velocity/Upright/Grounded/AFK — the game-fed built-ins —
                // so the 60% majority above never fires for them, and with "Remove GoGo Loco"
                // on they survived as zombies: hundreds of states overriding ChilloutVR's own
                // locomotion, driven by parameters that had just been stripped. A tester
                // toggling GoGo on and off saw "no difference" because these layers were on
                // top either way. Any GoGo reference at all in a layer merged from the Base,
                // Additive or Action playable layers is disqualifying — those layers exist to
                // replace locomotion wholesale, and only GoGo puts Go/ parameters there.
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

        /// <summary>
        /// Modern VRCFury merges many features into shared direct blend trees ("DBT"),
        /// with clips that write animator parameters (AAPs) as math. When a system is
        /// stripped, its branches must be pruned out of those shared trees or its
        /// leftover math keeps running (integrating garbage values forever).
        /// </summary>
        /// <summary>
        /// True when any state in the layer plays a Direct blend tree — the shape VRChat tooling
        /// uses to pack many independent toggles into one layer, and therefore the shape that
        /// must be pruned rather than deleted.
        /// </summary>
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
            // Which parameter names justified each layer's pruning. Diagnostic, and hard-won: a
            // toggle chain died on one avatar because its bool→smoothed-float bridge shared a
            // layer with stripped math, and "all of its content belonged to stripped systems"
            // left no way to see WHICH names the stripper believed in. The report now shows its
            // reasoning, so an overreaching prefix is visible in the conversion that did it.
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

        /// <summary>
        /// Recursive dead-code elimination for motions:
        ///  - a clip is dead when it only writes stripped parameters (Fury AAP math)
        ///  - a tree is dead when it blends ON a stripped parameter (its entire subtree
        ///    exists to respond to a system that no longer exists)
        ///  - a direct tree drops dead children; any tree whose children are all dead
        ///    is dead itself
        /// Returns null when the whole motion is dead.
        /// </summary>
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

        /// <summary>
        /// Deletes the scene objects belonging to stripped VRChat-only systems, and does it
        /// FIRST — before assets are re-homed, PhysBones are converted, or anything else reads
        /// the hierarchy.
        ///
        /// Running it late (as part of the animator merge) meant AvatarBridge spent the whole
        /// conversion working on content it was about to throw away: SPS materials and their
        /// hidden shaders got rescued out of VRCFury's temp folder only to end up pink, and
        /// SPS's PhysBones became MagicaCloth components whose root bones were then deleted
        /// out from under them. One reported avatar came out with seventeen such orphans.
        /// </summary>
        /// <summary>
        /// The scene objects a third-party VRChat face-tracking rig installs. Matched on name
        /// because these arrive already baked by VRCFury — by conversion time there is no
        /// component left to identify them by.
        ///
        /// Deliberately narrow. "VRCFT" and "OSCmooth" belong to those systems and nothing else;
        /// a bare "FaceTracking" would also match objects an avatar author made themselves, and
        /// deleting somebody's own work is far worse than leaving a spare object behind.
        /// </summary>
        static readonly string[] FaceTrackingObjectHints =
        {
            "VRCFT", "VRCFaceTracking", "OSCmooth", "OSCm_",
        };

        /// <summary>
        /// Removes VRCFury's Parameter Compressor, which is not merely useless in ChilloutVR but
        /// actively harmful.
        ///
        /// It exists to beat VRChat's 256-parameter ceiling: it sets the avatar's real parameters
        /// to networkSynced = false, mirrors each into "VF&lt;id&gt;_&lt;name&gt;", and rotates those
        /// mirrors through a couple of sync slots about twice a second, reassembling them on the
        /// far side. Clever, and entirely a workaround for a limit ChilloutVR does not have —
        /// 3200 bits, and parameters sync straight from the animator declaration.
        ///
        /// Carrying it across costs three ways. The rotation is a permanent Direct blend tree
        /// evaluating every frame, which is most of what makes the "Internal Parameter Math" layer
        /// expensive. The mirrors and slot counters are dead parameters. Worst, because the real
        /// parameters were left marked not-synced, the conversion faithfully gives them the "#"
        /// local-only prefix — so the compressed values reach nobody, which is the opposite of
        /// what the compressor was installed to achieve.
        ///
        /// So: drop its layers, drop the mirrors and slots, and put the real names into
        /// PreserveParameters so they sync natively — instantly, and at no cost worth counting.
        /// </summary>
        internal static void StripParameterCompressorForTest(BridgeContext ctx, AnimatorController master,
            List<AnimatorControllerLayer> vrcLayers) => StripParameterCompressor(ctx, master, vrcLayers);

        static void StripParameterCompressor(BridgeContext ctx, AnimatorController master,
            List<AnimatorControllerLayer> vrcLayers)
        {
            var declared = new HashSet<string>(master.parameters.Select(p => p.name));
            // VRCFury names its rotation slots by the TYPE they carry: SyncDataBool0, SyncDataFloat3,
            // SyncDataInt2, alongside the SyncIndex0/1 that says which parameter is in the window.
            // The old pattern demanded digits straight after "Data", so it matched SyncData0 and
            // SyncDataNum0 and missed every SyncDataBool/Float/Int — on a real avatar that meant 2
            // of ~28 slots were seen.
            //
            // That went unnoticed because the pass ALSO finds "mirrors" (VF<n>_<RealName> shadowing
            // a declared parameter), and a normal compressed avatar has dozens, so detection
            // succeeded on their strength alone. It only bites when an avatar has slots and NO
            // mirrors: then both lists are empty, the pass returns early, PreserveParameters never
            // learns the real names, and preserveParameterSyncState faithfully copies the
            // compressor's "not synced" flags onto EVERY parameter — reported in the wild as
            // "all parameters became local", 131 of 170 including the entire wardrobe.
            var slots = new System.Text.RegularExpressions.Regex(
                @"^VF\d+_Sync(Index|Data(Bool|Float|Int|Num)?)\d*$");
            var mirror = new System.Text.RegularExpressions.Regex(@"^VF\d+_(.+)$");

            // Look at referenced names as well as declared ones. The compressor's mirrors are
            // usually referenced without ever being declared — they arrive later, from the
            // dangling-parameter repair — so scanning the declared list alone found none of them
            // and quietly did nothing.
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

            // Mirrors are not always there. An avatar can carry the compressor with slots and no
            // mirrors at all, and preserving from mirrors alone then preserves NOTHING — the
            // compressor is stripped, the rename pass is told nothing, and every parameter takes
            // the local "#" on the strength of a flag whose only author was the thing we just
            // removed. Reported in the wild as "all parameters became local": 131 of 170,
            // including every clothing toggle, so nobody but the wearer saw the avatar change.
            //
            // So when the compressor is detected at all, every DECLARED parameter that is not one
            // of its own artefacts is treated as really synced. "Not synced" is not trustworthy
            // evidence on a compressed avatar — de-syncing them is what the compressor does — and
            // ChilloutVR has 3200 bits, so restoring sync to a parameter the author truly wanted
            // local costs 32 bits, while getting it wrong the other way costs the feature.
            //
            // VF-prefixed names are excluded: those are VRCFury's own working values, and they
            // shadow nothing the user ever asked to sync.
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

        /// <summary>
        /// Deletes a baked-in VRCFT rig's objects when ChilloutVR is going to provide face
        /// tracking itself. In None mode the rig is left completely alone, which is the point of
        /// None: the user has said they will handle it.
        /// </summary>
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
            // The NATIVE contact components, which this sweep did not know about: it was written
            // for the legacy pointer/trigger route and the native one arrived later, so stripping
            // a system took its layers, its parameters and its triggers but left its contacts
            // standing. They are not harmless clutter — a native contact is simulated by EVERY
            // client that can see the avatar, so an inert one costs everyone in the instance a
            // collision test per frame to write a parameter that no longer exists.
            //
            // Only STRIPPED parameters are removed here. A contact whose parameter is simply
            // absent for reasons of the author's own is somebody else's content and is reported
            // rather than deleted — see ReportInertContacts.
            var nativeContact = ContactsConverter.NativeContactAnimatorType;
            if (nativeContact != null)
            {
                var field = nativeContact.GetField("parameter",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    foreach (var contact in ctx.Target.GetComponentsInChildren(nativeContact, true))
                    {
                        string name = field.GetValue(contact) as string;
                        if (string.IsNullOrEmpty(name)) continue;
                        // Both spellings: this runs inside the merge, and whether the rename pass
                        // has already given the name its local "#" depends on ordering that is
                        // not this sweep's business to depend on.
                        string bare = name.StartsWith("#", StringComparison.Ordinal) ? name.Substring(1) : name;
                        if (isStripped(name) || isStripped(bare))
                        {
                            UnityEngine.Object.DestroyImmediate(contact.gameObject);
                            removed++;
                        }
                    }
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
