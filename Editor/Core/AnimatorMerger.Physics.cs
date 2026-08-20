// The physics half of the animator merge: an avatar's own toggles
// switching the cloth that replaced its PhysBones.
//
// A partial class of AnimatorMerger. Same access, same signatures, one
// compiled type.
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
        static void RewirePhysicsToggles(AnimatorController master, BridgeContext ctx)
        {
            var chains = ctx.ConvertedPhysicsChains;
            // Zero chains still matters when style synthesis is on: an avatar whose ONLY
            // physics would be a synthesized rig must reach phase 1 below.
            if (chains == null || (chains.Count == 0 && !ctx.Settings.addPhysicsToRiggedStyles))
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
            // Which cloth bindings each rewired clip switches on, and
            // which may be asserted off elsewhere. A target is off-safe
            // only while every activating container passed the
            // shared-rider test.
            var activatedByClip = new Dictionary<AnimationClip, HashSet<EditorCurveBinding>>();
            var offSafe = new Dictionary<EditorCurveBinding, bool>();
            var chainByTarget = new Dictionary<EditorCurveBinding, BridgeContext.ConvertedPhysicsChain>();
            var physicslessStyles = new HashSet<Transform>();
            // Chains left running when their style hides, because a mesh outside the toggled
            // object rides them. Named in the report so "this cloth never stops" is explained
            // rather than mysterious.
            var sharedChains = new SortedSet<string>(StringComparer.Ordinal);

            // PhysBone on/off curves with nowhere to land: the chain
            // produced no physics, so there is no component to retarget.
            // The toggle then does nothing, silently, while every other
            // part of it converts. Reported with the skip that caused it.
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

            // Everything that depends on a chain's simulated bones:
            // meshes weighted to them, and anything parented beneath.
            // Built once per chain.
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

            // The toggled object owns the chain: the simulated bones live
            // inside it, not just the component that drove them.
            //
            // Who is weighted to them then does not matter. A body mesh is
            // weighted to anything grafted onto the body and stays visible
            // while the toggle hides the geometry.
            bool ChainOwnedBy(BridgeContext.ConvertedPhysicsChain chain, Transform animated)
            {
                var chainRoot = chain.Root != null ? chain.Root
                    : chain.Source != null ? chain.Source.transform : null;
                return chainRoot != null && (chainRoot == animated || chainRoot.IsChildOf(animated));
            }

            // True when a mesh outside the toggled object rides this
            // chain. Something still visible needs those bones moving,
            // so the cloth must not stop with the object.
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

            // A self-contained rig: most of the container's skinned-mesh
            // bones live inside it. Clothing skinned to body bones does
            // not qualify. The rig root is the deepest common ancestor.
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

            // A toggled container with its own rig and no converted
            // chain never had physics in the source either. Reported,
            // so "broken" reads as "always rigid".
            void NotePhysicslessStyle(Transform container)
            {
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
                    "simulated those bones in the source — no PhysBone, so no physics existed in " +
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

                    // Recorded before the search, cleared by a match below. A PhysBone curve that
                    // finds no chain is the stranded case; object toggles are not, because an
                    // object toggle still does its own job whether or not physics rode along.
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
                            // Hosts INSIDE the toggled subtree already ride along (DynamicBone
                            // lives on the source object; and toggling the avatar root covers
                            // everything). Only an outside host needs the extra curve.
                            if (host == animated || host.IsChildOf(animated))
                            {
                                continue;
                            }
                            // Onto the component, not the holder's active
                            // flag. Holders are created active, with the
                            // cloth carrying the off state on its own
                            // enabled flag. An activation on m_IsActive
                            // would change nothing.
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
                        chainByTarget[target] = chain;
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
                            // Mirrored, not dropped: CVR restores nothing,
                            // so cloth switched on runs forever. Only a
                            // container deactivation needs the proof that
                            // nothing visible still rides these bones.
                            if (source != animated && !ChainOwnedBy(chain, animated)
                                && ChainSharedOutside(chain, animated))
                            {
                                sharedChains.Add(chain.Source.name);
                                offSafe[target] = false;
                                continue;
                            }
                            deactivationsMirrored++;
                        }
                        else
                        {
                            // An activation. Recorded rather than acted
                            // on; the off-asserting states are not known
                            // until every clip has been through this.
                            // Both kinds of toggle count. A component
                            // curve only matches its own chain and is
                            // always safe to stop.
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
            // Phase 1, before any curve is copied: "Add physics to
            // toggled rigs that have none". A toggled self-contained rig
            // with no converted chain gets a synthesized MagicaCloth.
            // Done here because only the animator knows what is toggled.
            // The new chain registers itself for phase 2 wiring.
            // Asked for, and this target cannot do it. Said out loud rather
            // than left as a setting that quietly does nothing.
            if (ctx.Settings.addPhysicsToRiggedStyles
                && ctx.Settings.physicsTarget != PhysicsTarget.MagicaCloth2)
            {
                ctx.Report.Skipped("Physics", "\"Add physics to toggled rigs that have none\" did nothing",
                    "It synthesizes a MagicaCloth, and this avatar was converted to " +
                    $"{ctx.Settings.physicsTarget}. Toggled styles carrying a rig but no PhysBone stay " +
                    "rigid here, exactly as they were in VRChat. Convert to MagicaCloth2 if you want " +
                    "them to move.");
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
                foreach (var container in activated)
                {
                    if (ChainInSubtree(container) || !IsSelfContainedRig(container, out var rigRoot))
                    {
                        continue;
                    }
                    MagicaClothWriter.WriteSynthesized(ctx, rigRoot);
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

            // Bindings the finished controller already switches off. A
            // synthetic stop is for one nothing takes back; on a paired
            // chain it would leave both halves disabled.
            // Does this state hide everything that rides the chain?
            //
            // On a dropdown the other rider is usually another option, which
            // this state hides too. So the question is asked per state.
            bool HidesEveryRider(EditorCurveBinding target, AnimationClip clip)
            {
                if (!chainByTarget.TryGetValue(target, out var chain)) return false;
                var hidden = new List<string>();
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(GameObject) || binding.propertyName != "m_IsActive") continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve != null && !CurveActivates(curve)) hidden.Add(binding.path);
                }
                if (hidden.Count == 0) return false;

                foreach (var rider in RidersOf(chain))
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
                // A binding refused by the shared-rider test is kept for a
                // selector, and proved per state below: the thing riding
                // those bones is often just ANOTHER option of the same
                // dropdown, which that state hides too.
                activatedHere.RemoveWhere(b => alreadySwitchedOff.Contains(b)
                                               || (!selector && (!offSafe.TryGetValue(b, out bool ok) || !ok)));
                // Two states is the plain toggle. A SELECTOR is the other
                // shape that qualifies: a dropdown whose states are all
                // entered from AnyState on one parameter's value, so they
                // are mutually exclusive by construction and each one IS
                // the off half of the others.
                //
                // Anything else is left alone for the reason this used to
                // stop at two: on a general machine the other states are
                // unrelated, and a stop written into them would switch off
                // physics they know nothing about.
                if (activatedHere.Count == 0 || (states.Count != 2 && !selector))
                {
                    continue;
                }

                // Cloned per layer, never shared: the same clip can be the off state of this
                // toggle and of an unrelated layer, and a stop written into the shared asset
                // would have that other layer switching off physics it knows nothing about.
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
                    $"{curvesAdded} toggle curve(s) re-wired to generated physics in {clipsTouched} clip(s)",
                    "Animations that activated a converted PhysBone's object or component (hair swaps, " +
                    "outfit toggles) now activate the generated physics too. Without this, a chain " +
                    "belonging to a style that was inactive at conversion time could never wake up — " +
                    "its cloth lives on its own object at the avatar root, on a path the original " +
                    "animations never animated. " +
                    (deactivationsMirrored > 0
                        ? $"{deactivationsMirrored} of those curve(s) switch the physics back OFF " +
                          "again when the style hides, which matters here because ChilloutVR does " +
                          "not restore a binding nothing writes: without the off curve the cloth " +
                          "would stay running from the first time it appeared."
                        : "No style here switches its physics back off.") +
                    (offsAsserted > 0
                        ? $" A further {offsAsserted} resting state(s) were given an explicit stop " +
                          "for physics they leave alone. Those states were empty of it — VRChat " +
                          "relied on Write Defaults to undo the switch, and ChilloutVR has no such " +
                          "rule, so the cloth latched on the first time the toggle was used and " +
                          "never stopped. Chains that something outside the toggled object rides " +
                          "are excluded and listed separately."
                        : ""));
            }

            if (sharedChains.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"{sharedChains.Count} chain(s) keep simulating while their style is hidden",
                    string.Join(", ", sharedChains) + " — each of these is switched ON with the " +
                    "object it belongs to but never switched off, because a mesh OUTSIDE that " +
                    "object is skinned to the same bones. Add-on hair grafted onto a base " +
                    "hairstyle's rig is the usual shape. Stopping the chain with the base style's " +
                    "mesh would leave the add-on rigid, so it is left running instead: it " +
                    "simulates bones nobody can see, which costs a little performance and looks " +
                    "like nothing at all.");
            }

            if (strandedToggles.Count > 0)
            {
                var lines = strandedToggles
                    .Select(entry => $"\"{entry.Key}\" (in {string.Join(", ", entry.Value)})")
                    .ToList();
                ctx.Report.Warning(Category,
                    $"{strandedToggles.Count} animation(s) switch a PhysBone that wasn't converted — " +
                    "those controls will do nothing",
                    string.Join("; ", lines) + " — these clips turn a VRChat PhysBone on or off, " +
                    "which is how avatars pause a chain while a body part is resized. The chain " +
                    "they name produced no physics here, so there is no cloth component to switch " +
                    "instead, and the curve dies with the VRC components. Everything else about " +
                    "the control converts — menu entry, parameter, animator layer — so it looks " +
                    "correct and does nothing, which is the worst way for this to present. " +
                    "The PhysBones -> MagicaCloth2 section above has a Skipped entry for each of " +
                    "these paths saying WHY it wasn't converted (a constraint driving a bone in " +
                    "the chain is the usual reason); fix that and the toggle starts working. If " +
                    "the chain was never meant to be simulated, remove the control instead.");
            }
        }

        // The bones a mesh is actually weighted to.
        //
        // Not skinned.bones. That is the skeleton it was bound against, and
        // a body mesh lists every bone on the avatar at zero weight.
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
                // GetAllBoneWeights, not mesh.boneWeights: the legacy view
                // keeps four influences per vertex, and dropping the rest
                // could call a real rider unridden, which is the dangerous
                // direction of this test to be wrong in.
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

        // A dropdown: every state reached from AnyState, all testing the
        // same parameter. One value is live, so the others are its off half.
        //
        // Strict on purpose. Full coverage and a single parameter keep a
        // sequence or a wait state from passing as a set of alternatives.
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
