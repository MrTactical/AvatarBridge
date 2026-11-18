// The physics half of the animator merge: the avatar's toggles switching the cloth that
// replaced its PhysBones.
#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    public static partial class AnimatorMerger
    {
        internal static void RewirePhysicsToggles(AnimatorController master, BridgeContext ctx)
        {
            var chains = ctx.ConvertedPhysicsChains;
            // Zero chains still runs: a synthesized rig needs phase 1, and stranded PhysBone
            // toggles must be reported. Only a target of None skips.
            if (chains == null || (chains.Count == 0 && !ctx.Settings.addPhysicsToRiggedStyles
                                   && ctx.Settings.physicsTarget == PhysicsTarget.None))
            {
                return;
            }

            var root = ctx.Target.transform;
            var pathCache = new Dictionary<string, Transform>();
            Transform Resolve(string path)
            {
                if (!pathCache.TryGetValue(path, out var t))
                {
                    pathCache[path] = t = BridgeContext.FindByAnimationPath(root, path);
                }
                return t;
            }

            var rewired = new Dictionary<AnimationClip, AnimationClip>();
            int curvesAdded = 0, clipsTouched = 0, deactivationsMirrored = 0, offsAsserted = 0;
            // Cloth bindings each clip switches on. A target is off-safe only while every
            // activating container passed the shared-rider test.
            var activatedByClip = new Dictionary<AnimationClip, HashSet<EditorCurveBinding>>();
            var offSafe = new Dictionary<EditorCurveBinding, bool>();
            // Every chain a binding reaches: grouped strands share one cloth's enabled flag.
            var chainByTarget = new Dictionary<EditorCurveBinding, List<BridgeContext.ConvertedPhysicsChain>>();
            var physicslessStyles = new HashSet<Transform>();
            // Chains left running when their toggle hides, because an outside mesh rides them.
            // Reported, so "this cloth never stops" is explained.
            var sharedChains = new SortedSet<string>(StringComparer.Ordinal);

            // PhysBone curves whose chain produced no component, so the toggle silently does
            // nothing. Reported with the skip that caused it.
            var strandedToggles = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

            bool ChainInSubtree(Transform container)
            {
                foreach (var chain in chains)
                {
                    if (chain.Source != null &&
                        (chain.Source.transform == container || chain.Source.transform.IsChildOf(container)))
                    {
                        return true;
                    }
                }
                return false;
            }

            // Meshes weighted to a chain's bones and anything parented beneath, once per chain.
            var ridersByChain = new Dictionary<BridgeContext.ConvertedPhysicsChain, List<Transform>>();
            List<Transform> RidersOf(BridgeContext.ConvertedPhysicsChain chain)
            {
                if (ridersByChain.TryGetValue(chain, out var known))
                {
                    return known;
                }
                var riders = new List<Transform>();
                var chainRoot = chain.Root != null ? chain.Root
                    : chain.Source != null ? chain.Source.transform : null;
                if (chainRoot != null)
                {
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        bool rides = renderer.transform == chainRoot
                            || renderer.transform.IsChildOf(chainRoot);
                        if (!rides && renderer is SkinnedMeshRenderer skinned)
                        {
                            foreach (var bone in WeightedBones(skinned))
                            {
                                if (bone != null && (bone == chainRoot || bone.IsChildOf(chainRoot)))
                                {
                                    rides = true;
                                    break;
                                }
                            }
                        }
                        if (rides)
                        {
                            riders.Add(renderer.transform);
                        }
                    }
                }
                ridersByChain[chain] = riders;
                return riders;
            }

            // The bones live inside the toggled object, so riders do not matter: a body mesh is
            // weighted to anything grafted on and stays visible while the toggle hides the part.
            bool ChainOwnedBy(BridgeContext.ConvertedPhysicsChain chain, Transform animated)
            {
                var chainRoot = chain.Root != null ? chain.Root
                    : chain.Source != null ? chain.Source.transform : null;
                return chainRoot != null && (chainRoot == animated || chainRoot.IsChildOf(animated));
            }

            // A mesh outside the toggled object rides this chain, so the cloth must keep running.
            bool ChainSharedOutside(BridgeContext.ConvertedPhysicsChain chain, Transform animated)
            {
                foreach (var rider in RidersOf(chain))
                {
                    if (rider != animated && !rider.IsChildOf(animated))
                    {
                        return true;
                    }
                }
                return false;
            }

            // Self-contained rig: most of the container's skinned bones live inside it (clothing
            // on body bones does not qualify). Root is the deepest common ancestor.
            bool IsSelfContainedRig(Transform container, out Transform rigRoot)
            {
                rigRoot = null;
                var inside = new List<Transform>();
                int total = 0;
                foreach (var smr in container.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    foreach (var bone in smr.bones)
                    {
                        if (bone == null) continue;
                        total++;
                        if (bone == container || bone.IsChildOf(container)) inside.Add(bone);
                    }
                }
                if (total < 5 || inside.Count * 2 < total)
                {
                    return false;
                }
                rigRoot = inside[0];
                while (rigRoot != null && rigRoot != container.parent)
                {
                    bool coversAll = true;
                    foreach (var bone in inside)
                    {
                        if (bone != rigRoot && !bone.IsChildOf(rigRoot))
                        {
                            coversAll = false;
                            break;
                        }
                    }
                    if (coversAll) break;
                    rigRoot = rigRoot.parent;
                }
                if (rigRoot == null || rigRoot == container.parent)
                {
                    rigRoot = container;
                }
                return true;
            }

            // A toggled rig with no converted chain had no physics in the source either. Reported,
            // so it reads as "always rigid", not broken.
            void NotePhysicslessStyle(Transform container)
            {
                // With no chain converted, "never had physics" and "failed to convert" look the same,
                // so say nothing. The Toolkit's card cannot see hand-placed cloth either.
                if (ctx.Standalone || (chains.Count == 0 && !ctx.Settings.addPhysicsToRiggedStyles))
                {
                    return;
                }
                if (!physicslessStyles.Add(container) || ChainInSubtree(container))
                {
                    return;
                }
                if (!IsSelfContainedRig(container, out _))
                {
                    return;
                }
                string hint = ctx.Settings.addPhysicsToRiggedStyles
                    ? " (\"Add physics to toggled rigs that have none\" is on, but it needs the " +
                      "MagicaCloth2 physics target and MagicaCloth2 installed.)"
                    : " Turn on \"Add physics to toggled rigs that have none\" (Physics options) " +
                      "to synthesize a MagicaCloth here.";
                ctx.Report.Skipped(Category, container.name,
                    "This toggled object carries its own bone rig and skinned mesh, but NOTHING " +
                    "simulated those bones in the source: no PhysBone, so no physics existed in " +
                    "VRChat either, and none was converted." + hint);
            }

            AnimationClip Rewire(AnimationClip clip)
            {
                if (clip == null)
                {
                    return null;
                }
                if (rewired.TryGetValue(clip, out var known))
                {
                    return known;
                }

                Dictionary<EditorCurveBinding, AnimationCurve> additions = null;
                var existing = AnimationUtility.GetCurveBindings(clip);
                foreach (var binding in existing)
                {
                    bool objectToggle = binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive";
                    bool physBoneToggle = !objectToggle && binding.propertyName == "m_Enabled"
                        && binding.type != null && binding.type.Name == "VRCPhysBone";
                    if (!objectToggle && !physBoneToggle)
                    {
                        continue;
                    }
                    var animated = Resolve(binding.path);
                    if (animated == null)
                    {
                        continue;
                    }

                    // Recorded, cleared by a match below. Only a PhysBone curve can be stranded: an
                    // object toggle still does its own job.
                    bool physBoneRetargeted = false;

                    bool anyChainInSubtree = false;
                    foreach (var chain in chains)
                    {
                        if (chain.Source == null || chain.Host == null)
                        {
                            continue;
                        }
                        var source = chain.Source.transform;
                        var host = chain.Host.transform;
                        EditorCurveBinding target;
                        if (objectToggle)
                        {
                            if (source != animated && !source.IsChildOf(animated))
                            {
                                continue;
                            }
                            anyChainInSubtree = true;
                            // Hosts inside the toggled subtree already ride along; only an outside
                            // host needs the extra curve.
                            if (host == animated || host.IsChildOf(animated))
                            {
                                continue;
                            }
                            // Onto the component: holders stay active and the cloth carries the off
                            // state, so m_IsActive would change nothing.
                            target = chain.Physics != null
                                ? EditorCurveBinding.FloatCurve(
                                    AnimationUtility.CalculateTransformPath(host, root),
                                    chain.Physics.GetType(), "m_Enabled")
                                : EditorCurveBinding.FloatCurve(
                                    AnimationUtility.CalculateTransformPath(host, root),
                                    typeof(GameObject), "m_IsActive");
                        }
                        else
                        {
                            // The PhysBone component itself was animated on/off; the component
                            // type died with the conversion, so retarget at what replaced it.
                            if (source != animated || chain.Physics == null)
                            {
                                continue;
                            }
                            target = EditorCurveBinding.FloatCurve(
                                AnimationUtility.CalculateTransformPath(host, root),
                                chain.Physics.GetType(), "m_Enabled");
                            physBoneRetargeted = true;
                        }
                        // Which chain a binding belongs to, so a later pass
                        // can ask what rides it.
                        if (!chainByTarget.TryGetValue(target, out var reached))
                        {
                            chainByTarget[target] = reached = new List<BridgeContext.ConvertedPhysicsChain>();
                        }
                        if (!reached.Contains(chain))
                        {
                            reached.Add(chain);
                        }
                        bool alreadyDriven = false;
                        foreach (var have in existing)
                        {
                            if (have.path == target.path && have.type == target.type
                                && have.propertyName == target.propertyName)
                            {
                                alreadyDriven = true;
                                break;
                            }
                        }
                        if (alreadyDriven)
                        {
                            continue; // the clip already drives it deliberately
                        }
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        if (curve == null)
                        {
                            continue;
                        }
                        if (objectToggle && !CurveActivates(curve))
                        {
                            // Mirrored: CVR restores nothing, so cloth switched on runs forever.
                            // Only a container deactivation needs the shared-rider proof.
                            if (source != animated && !ChainOwnedBy(chain, animated)
                                && ChainSharedOutside(chain, animated))
                            {
                                sharedChains.Add(chain.Source.name);
                                offSafe[target] = false;
                                continue;
                            }
                            if (additions == null || !additions.ContainsKey(target))
                            {
                                deactivationsMirrored++;
                            }
                        }
                        else
                        {
                            // An activation, recorded: the off states are known only after every
                            // clip. A component curve matches only its own chain, always safe to stop.
                            bool safe = source == animated || ChainOwnedBy(chain, animated)
                                        || !ChainSharedOutside(chain, animated);
                            if (!safe)
                            {
                                sharedChains.Add(chain.Source.name);
                            }
                            offSafe[target] = offSafe.TryGetValue(target, out bool had)
                                ? had && safe
                                : safe;
                        }
                        if (additions == null)
                        {
                            additions = new Dictionary<EditorCurveBinding, AnimationCurve>();
                        }
                        additions[target] = curve;
                    }

                    // Nothing to retarget at; this curve dies with the
                    // VRC components. Report which clip and which object.
                    if (physBoneToggle && !physBoneRetargeted)
                    {
                        if (!strandedToggles.TryGetValue(binding.path, out var clipNames))
                        {
                            strandedToggles[binding.path] = clipNames = new SortedSet<string>(StringComparer.Ordinal);
                        }
                        clipNames.Add(clip.name);
                    }

                    if (objectToggle && !anyChainInSubtree)
                    {
                        var activation = AnimationUtility.GetEditorCurve(clip, binding);
                        if (activation != null && CurveActivates(activation))
                        {
                            NotePhysicslessStyle(animated);
                        }
                    }
                }

                if (additions == null)
                {
                    rewired[clip] = clip;
                    return clip;
                }
                var clone = UnityEngine.Object.Instantiate(clip);
                clone.name = clip.name;
                clone.hideFlags = HideFlags.None;
                foreach (var pair in additions)
                {
                    AnimationUtility.SetEditorCurve(clone, pair.Key, pair.Value);
                }
                curvesAdded += additions.Count;
                clipsTouched++;
                rewired[clip] = clone;
                foreach (var pair in additions)
                {
                    if (!CurveActivates(pair.Value))
                    {
                        continue;
                    }
                    if (!activatedByClip.TryGetValue(clone, out var set))
                    {
                        activatedByClip[clone] = set = new HashSet<EditorCurveBinding>();
                    }
                    set.Add(pair.Key);
                }
                return clone;
            }

            static bool CurveActivates(AnimationCurve curve)
            {
                foreach (var key in curve.keys)
                {
                    if (key.value > 0.5f)
                    {
                        return true;
                    }
                }
                return false;
            }

            Motion RewireMotion(Motion motion)
            {
                if (motion is AnimationClip clip)
                {
                    return Rewire(clip);
                }
                if (motion is BlendTree tree)
                {
                    var children = tree.children;
                    bool changed = false;
                    for (int i = 0; i < children.Length; i++)
                    {
                        var replaced = RewireMotion(children[i].motion);
                        if (!ReferenceEquals(replaced, children[i].motion))
                        {
                            children[i].motion = replaced;
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        // Assigning children re-derives thresholds under automatic mode; pin
                        // manual mode for the write, exactly like the deep copier does.
                        bool auto = tree.useAutomaticThresholds;
                        tree.useAutomaticThresholds = false;
                        tree.children = children;
                        tree.useAutomaticThresholds = auto;
                    }
                }
                return motion;
            }

#if AVATARBRIDGE_MAGICA
            // Phase 1, before any curve is copied: a toggled rig with no chain gets a synthesized
            // MagicaCloth, here because only the animator knows what is toggled. It registers for
            // phase 2. A target that cannot do it says so.
            if (ctx.Settings.addPhysicsToRiggedStyles
                && ctx.Settings.physicsTarget != PhysicsTarget.MagicaCloth2)
            {
                ctx.Report.Skipped("Physics", "\"Add physics to toggled rigs that have none\" did nothing",
                    $"It needs MagicaCloth2, and this converted to {ctx.Settings.physicsTarget}.");
            }

            if (ctx.Settings.addPhysicsToRiggedStyles
                && ctx.Settings.physicsTarget == PhysicsTarget.MagicaCloth2)
            {
                var activated = new HashSet<Transform>();
                void CollectActivations(Motion motion)
                {
                    if (motion is AnimationClip clip)
                    {
                        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                        {
                            if (binding.type != typeof(GameObject) || binding.propertyName != "m_IsActive")
                            {
                                continue;
                            }
                            var curve = AnimationUtility.GetEditorCurve(clip, binding);
                            if (curve == null || !CurveActivates(curve))
                            {
                                continue;
                            }
                            var target = Resolve(binding.path);
                            if (target != null)
                            {
                                activated.Add(target);
                            }
                        }
                    }
                    else if (motion is BlendTree tree)
                    {
                        foreach (var child in tree.children)
                        {
                            CollectActivations(child.motion);
                        }
                    }
                }
                foreach (var layer in master.layers)
                {
                    WalkMachines(layer.stateMachine, machine =>
                    {
                        foreach (var child in machine.states)
                        {
                            CollectActivations(child.state.motion);
                        }
                    });
                }
                // Outermost first, skipping rigs inside one already clothed. Hash order simulated
                // nested bones twice or left the outer rig rigid, varying per run.
                int Depth(Transform t)
                {
                    int depth = 0;
                    for (; t.parent != null; t = t.parent) depth++;
                    return depth;
                }
                var synthesized = new List<Transform>();
                foreach (var container in activated.OrderBy(Depth))
                {
                    if (ChainInSubtree(container) || !IsSelfContainedRig(container, out var rigRoot)
                        || synthesized.Any(r => rigRoot == r || rigRoot.IsChildOf(r)))
                    {
                        continue;
                    }
                    MagicaClothWriter.WriteSynthesized(ctx, rigRoot);
                    synthesized.Add(rigRoot);
                }
            }
#endif

            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        child.state.motion = RewireMotion(child.state.motion);
                    }
                });
            }

            // Bindings the controller already switches off: a synthetic stop on a paired chain
            // would disable both halves. Per state, does it hide everything riding the chain? On a
            // dropdown the other rider is usually another option this state hides too.
            bool HidesEveryRider(EditorCurveBinding target, AnimationClip clip)
            {
                if (!chainByTarget.TryGetValue(target, out var chains)) return false;
                var hidden = new List<string>();
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(GameObject) || binding.propertyName != "m_IsActive") continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve != null && !CurveActivates(curve)) hidden.Add(binding.path);
                }
                if (hidden.Count == 0) return false;

                foreach (var rider in chains.SelectMany(RidersOf))
                {
                    string path = AnimationUtility.CalculateTransformPath(rider, root);
                    bool covered = false;
                    foreach (string off in hidden)
                    {
                        if (path == off || path.StartsWith(off + "/", StringComparison.Ordinal))
                        {
                            covered = true;
                            break;
                        }
                    }
                    if (!covered) return false;
                }
                return true;
            }

            var alreadySwitchedOff = new HashSet<EditorCurveBinding>();
            foreach (var pair in rewired)
            {
                var clip = pair.Value;
                if (clip == null) continue;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!offSafe.ContainsKey(binding)) continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve != null && !CurveActivates(curve))
                    {
                        alreadySwitchedOff.Add(binding);
                    }
                }
            }

            // The restore passes ran before these bindings existed, and
            // nothing takes the on curve back. Scoped to what this pass
            // switched on, in a layer that switches it, plain clips only.
            foreach (var layer in master.layers)
            {
                var activatedHere = new HashSet<EditorCurveBinding>();
                var states = new List<AnimatorState>();
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        states.Add(child.state);
                        if (child.state.motion is AnimationClip clip
                            && activatedByClip.TryGetValue(clip, out var activated))
                        {
                            activatedHere.UnionWith(activated);
                        }
                    }
                });
                bool selector = IsSelector(layer, states);
                // A binding the shared-rider test refused is kept for a selector and proved per
                // state below.
                activatedHere.RemoveWhere(b => alreadySwitchedOff.Contains(b)
                                               || (!selector && (!offSafe.TryGetValue(b, out bool ok) || !ok)));
                // Two states, or a selector (all states entered from AnyState on one parameter, so
                // each is the others' off half). On any other machine the states are unrelated, and
                // a stop written into them would switch off physics they know nothing about.
                if (activatedHere.Count == 0 || (states.Count != 2 && !selector))
                {
                    continue;
                }

                // Cloned per layer: the same clip can be another layer's off state too.
                var perLayer = new Dictionary<AnimationClip, AnimationClip>();
                foreach (var state in states)
                {
                    // A blend tree is left alone. A constant stop in a
                    // blended child fights the tree instead of resting it.
                    if (!(state.motion is AnimationClip clip))
                    {
                        continue;
                    }
                    var drives = new HashSet<EditorCurveBinding>(AnimationUtility.GetCurveBindings(clip));
                    foreach (var target in activatedHere)
                    {
                        if (drives.Contains(target))
                        {
                            continue;   // this state says its own piece already
                        }
                        // Refused globally: stop it here only when this very
                        // state hides everything that rides those bones.
                        if ((!offSafe.TryGetValue(target, out bool globallySafe) || !globallySafe)
                            && !HidesEveryRider(target, clip))
                        {
                            continue;
                        }
                        if (!perLayer.TryGetValue(clip, out var owned))
                        {
                            owned = UnityEngine.Object.Instantiate(clip);
                            owned.name = clip.name;
                            owned.hideFlags = HideFlags.None;
                            perLayer[clip] = owned;
                        }
                        AnimationUtility.SetEditorCurve(owned, target, AnimationCurve.Constant(0f, 1f / 60f, 0f));
                        offsAsserted++;
                    }
                    if (perLayer.TryGetValue(clip, out var replacement))
                    {
                        state.motion = replacement;
                    }
                }
            }

            if (clipsTouched > 0)
            {
                ctx.Report.Converted(Category,
                    $"{BridgeElements.Count(curvesAdded, "toggle curve", "toggle curves")} re-wired to generated physics in {BridgeElements.Count(clipsTouched, "clip", "clips")}",
                    "Hair and outfit toggles now switch their physics too. " +
                    (deactivationsMirrored > 0
                        ? $"{deactivationsMirrored} switch it back off when the style hides."
                        : "No style here switches its physics back off.") +
                    (offsAsserted > 0 ? $" {BridgeElements.Count(offsAsserted, "resting state", "resting states")} now stop it explicitly." : ""));
            }

            if (sharedChains.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"{BridgeElements.Count(sharedChains.Count, "chain keeps simulating while its", "chains keep simulating while their")} style is hidden",
                    string.Join(", ", sharedChains) + ": another mesh shares their bones, so they keep running. " +
                    "Costs a little performance, shows nothing.");
            }

            if (strandedToggles.Count > 0)
            {
                var lines = strandedToggles
                    .Select(entry => $"\"{entry.Key}\" (in {string.Join(", ", entry.Value)})")
                    .ToList();
                ctx.Report.Warning(Category,
                    $"{BridgeElements.Count(strandedToggles.Count, "animation switches", "animations switch")} a PhysBone that wasn't converted: " +
                    "those controls will do nothing",
                    string.Join("; ", lines) + ": the chain made no physics here. The PhysBones section says why " +
                    "for each; fix that and the toggle works.");
            }
        }

        // Bones a mesh is actually weighted to. Not skinned.bones: a body lists every bone at zero weight.
        static readonly Dictionary<SkinnedMeshRenderer, List<Transform>> WeightedBonesCache =
            new Dictionary<SkinnedMeshRenderer, List<Transform>>();

        static List<Transform> WeightedBones(SkinnedMeshRenderer skinned)
        {
            if (WeightedBonesCache.TryGetValue(skinned, out var known))
            {
                return known;
            }
            var bones = new List<Transform>();
            var mesh = skinned.sharedMesh;
            var bound = skinned.bones;
            if (mesh != null && bound != null && bound.Length > 0)
            {
                var used = new HashSet<int>();
                // GetAllBoneWeights: boneWeights keeps four influences and could miss a real rider,
                // the dangerous way for this test to be wrong.
                var weights = mesh.GetAllBoneWeights();
                for (int i = 0; i < weights.Length; i++)
                {
                    if (weights[i].weight > 0f) used.Add(weights[i].boneIndex);
                }
                foreach (int index in used)
                {
                    if (index >= 0 && index < bound.Length && bound[index] != null) bones.Add(bound[index]);
                }
            }
            WeightedBonesCache[skinned] = bones;
            return bones;
        }

        // A dropdown: every state reached from AnyState on one parameter. Strict, so a sequence or
        // wait state never passes as a set of alternatives.
        static bool IsSelector(AnimatorControllerLayer layer, List<AnimatorState> states)
        {
            if (states.Count < 2) return false;

            string parameter = null;
            var reached = new HashSet<AnimatorState>();
            bool any = false;

            WalkMachines(layer.stateMachine, machine =>
            {
                foreach (var transition in machine.anyStateTransitions)
                {
                    if (transition == null || transition.destinationState == null) continue;
                    any = true;
                    if (transition.conditions == null || transition.conditions.Length == 0)
                    {
                        parameter = "";   // unconditional: not a selection
                        continue;
                    }
                    foreach (var condition in transition.conditions)
                    {
                        if (parameter == null) parameter = condition.parameter;
                        else if (parameter != condition.parameter) parameter = "";
                    }
                    reached.Add(transition.destinationState);
                }
            });

            if (!any || string.IsNullOrEmpty(parameter)) return false;
            foreach (var state in states)
            {
                if (!reached.Contains(state)) return false;
            }
            return true;
        }

    }
}
#endif
