#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge
{
    // Converts every VRCPhysBone on the avatar to the chosen ChilloutVR-compatible
    // physics system (MagicaCloth2 preferred, DynamicBone as fallback) and removes the
    // original PhysBone components afterwards.
    public static class PhysBoneConverter
    {
        const string Category = "PhysBones";

        // Where a writer parks what it makes. Here rather than in either
        // writer because both call it and the two compile on different
        // defines: with these in the Magica writer, a project that had
        // DynamicBone and not MagicaCloth would not compile at all.
        internal static Transform CollectionUnder(BridgeContext ctx, string name)
        {
            var target = ctx.Target.transform;
            var home = target.Find(name);
            if (home != null) return home;
            var made = new GameObject(name);
            made.transform.SetParent(target, false);
            return made.transform;
        }

        // Compared by name, not Transform.Find: a '/' in a parameter or bone
        // name reads to Find as a path, so a clash went unseen.
        internal static string UniqueChildName(Transform parent, string name)
        {
            var taken = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (Transform child in parent)
            {
                taken.Add(child.name);
            }
            string candidate = name;
            for (int n = 2; taken.Contains(candidate); n++)
            {
                candidate = $"{name} {n}";
            }
            return candidate;
        }

        // Null with physics conversion off: no solver, so no line names one. A context with
        // no settings reads as the default target.
        static string TargetName(BridgeContext ctx)
        {
            switch (ctx.Settings != null ? ctx.Settings.physicsTarget : PhysicsTarget.MagicaCloth2)
            {
                case PhysicsTarget.MagicaCloth2: return "MagicaCloth2";
                case PhysicsTarget.DynamicBone: return "DynamicBone";
                default: return null;
            }
        }

        // Both writers route every collider host through here, so the rest
        // state is matched once for both.
        internal static void RecordColliderHost(BridgeContext ctx, VRCPhysBoneCollider original, GameObject host)
        {
            string originalPath = BridgeContext.RelativePath(ctx.Target.transform, original.transform);
            string hostPath = BridgeContext.RelativePath(ctx.Target.transform, host.transform);
            if (!ctx.PhysicsColliderHosts.TryGetValue(originalPath, out var hosts))
            {
                ctx.PhysicsColliderHosts[originalPath] = hosts = new List<string>();
            }
            hosts.Add(hostPath);

            // A new host is live from load, so a collider that shipped switched off came out
            // always on. The host takes the off state the hierarchy does not already give it: a
            // disabled component, or an inactive object the host is not under (a rootTransform
            // elsewhere). RepointColliderEnableCurves carries the switching onto it.
            bool live = original.enabled;
            // Native toggles switch objects without a clip, so nothing could switch on a host
            // outside the object; there it stays live as before.
            bool nativeToggles = ctx.Settings.toggleStyle == ToggleStyle.CvrNativeTargets;
            // Judged inside the avatar only: one converted in place may itself be off in the scene.
            for (var t = original.transform; live && !nativeToggles && t != null && t != ctx.Target.transform;
                 t = t.parent)
            {
                live = t.gameObject.activeSelf || host.transform.IsChildOf(t);
            }
            if (!live)
            {
                host.SetActive(false);
            }
        }

        internal static void RepointColliderEnableCurves(BridgeContext ctx)
        {
            if (ctx.MergedController == null || ctx.PhysicsColliderHosts.Count == 0)
            {
                return;
            }

            var clips = new HashSet<AnimationClip>();
            foreach (var clip in ctx.MergedController.animationClips)
            {
                if (clip != null)
                {
                    clips.Add(clip);
                }
            }

            int repointed = 0;
            int mirrored = 0;
            var dropped = new SortedSet<string>(StableSampleOrder.Instance);
            foreach (var clip in clips)
            {
                // An object toggle above the collider switched it off in VRChat. A host under
                // a rootTransform elsewhere is not below that object, so the toggle is copied
                // onto it. Before the component repoint, which wins where both exist.
                foreach (var binding in UnityEditor.AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(GameObject) || binding.propertyName != "m_IsActive"
                        || binding.path.Length == 0)
                    {
                        continue;
                    }
                    string under = binding.path + "/";
                    Transform toggled = null;
                    foreach (var entry in ctx.PhysicsColliderHosts)
                    {
                        if (entry.Key != binding.path && !entry.Key.StartsWith(under, System.StringComparison.Ordinal))
                        {
                            continue;
                        }
                        if (toggled == null)
                        {
                            toggled = BridgeContext.FindByAnimationPath(ctx.Target.transform, binding.path);
                            if (toggled == null) break;
                        }
                        var curve = UnityEditor.AnimationUtility.GetEditorCurve(clip, binding);
                        foreach (var hostPath in entry.Value)
                        {
                            var hostBinding = UnityEditor.EditorCurveBinding.FloatCurve(
                                hostPath, typeof(GameObject), "m_IsActive");
                            if (hostPath.StartsWith(under, System.StringComparison.Ordinal)
                                || UnityEditor.AnimationUtility.GetEditorCurve(clip, hostBinding) != null)
                            {
                                continue;
                            }
                            // Resting off under an object resting on: something else holds the
                            // collider off (a disabled component, a higher inactive object), and
                            // this toggle must not wake it. Same guard as the contact hosts.
                            var host = BridgeContext.FindByAnimationPath(ctx.Target.transform, hostPath);
                            if (host == null || (!host.gameObject.activeSelf && toggled.gameObject.activeSelf))
                            {
                                continue;
                            }
                            UnityEditor.AnimationUtility.SetEditorCurve(clip, hostBinding, curve);
                            mirrored++;
                        }
                    }
                }

                foreach (var binding in UnityEditor.AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(VRCPhysBoneCollider))
                    {
                        continue;
                    }
                    var curve = UnityEditor.AnimationUtility.GetEditorCurve(clip, binding);
                    // The original binding goes either way: its component is deleted, so leaving
                    // it means a curve that silently does nothing.
                    UnityEditor.AnimationUtility.SetEditorCurve(clip, binding, null);

                    if (binding.propertyName != "m_Enabled"
                        || !ctx.PhysicsColliderHosts.TryGetValue(binding.path, out var hosts))
                    {
                        // A shape property (radius, height; no animatable equivalent written),
                        // or a collider that was skipped rather than converted.
                        dropped.Add($"\"{clip.name}\" -> {binding.path} ({binding.propertyName})");
                        continue;
                    }
                    foreach (var hostPath in hosts)
                    {
                        UnityEditor.AnimationUtility.SetEditorCurve(clip,
                            UnityEditor.EditorCurveBinding.FloatCurve(hostPath, typeof(GameObject), "m_IsActive"),
                            curve);
                    }
                    repointed++;
                }
            }

            if (repointed > 0 || mirrored > 0)
            {
                ctx.Report.Converted(Category, $"{repointed + mirrored} collider on/off animation(s) rewired",
                    "They now toggle the converted collider's object." +
                    (mirrored > 0 ? $" {mirrored} switched its parent object and now reach it too." : ""));
            }
            if (dropped.Count > 0)
            {
                ctx.Report.Warning(Category, $"{dropped.Count} collider-animating curve(s) could not be carried",
                    string.Join("; ", dropped.Take(6)) + (dropped.Count > 6 ? ", …" : "") +
                    ": no equivalent on the converted collider, or the collider was not converted. Removed.");
            }
        }

        internal static void ReportAnimatedPhysBoneProperties(BridgeContext ctx)
        {
            if (ctx.MergedController == null)
            {
                return;
            }

            var clips = new HashSet<AnimationClip>();
            foreach (var clip in ctx.MergedController.animationClips)
            {
                if (clip != null)
                {
                    clips.Add(clip);
                }
            }

            var lost = new SortedDictionary<string, SortedSet<string>>(); // property -> clips
            int removed = 0;
            foreach (var clip in clips)
            {
                foreach (var binding in UnityEditor.AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(VRCPhysBone))
                    {
                        continue;
                    }
                    UnityEditor.AnimationUtility.SetEditorCurve(clip, binding, null);
                    removed++;
                    if (binding.propertyName == "m_Enabled")
                    {
                        continue; // chain toggles: RewirePhysicsToggles' job, not a loss
                    }
                    if (!lost.TryGetValue(binding.propertyName, out var names))
                    {
                        lost[binding.propertyName] = names = new SortedSet<string>(StableSampleOrder.Instance);
                    }
                    if (names.Count < 4)
                    {
                        names.Add(clip.name);
                    }
                }
            }

            if (lost.Count > 0)
            {
                string target = TargetName(ctx);
                ctx.Report.Skipped(Category,
                    $"{lost.Count} animated PhysBone parameter(s) have no converted equivalent",
                    string.Join("; ", lost.Select(kv => $"{kv.Key} (e.g. {string.Join(", ", kv.Value)})")) +
                    (target != null
                        ? $": {target} cannot animate these, so the chain keeps its converted values."
                        : ": physics conversion is off, so nothing carries these.") +
                    " The rest of each animation plays.");
            }
        }

        // A collection this tool made can only have arrived with the avatar,
        // which means the avatar carries an earlier conversion's cloth. New
        // holders land beside the old ones and both simulate the same bones.
        static void ReportInheritedCloth(BridgeContext ctx)
        {
            foreach (string name in new[] { "MagicaCloth Phys", "DynamicBone Phys" })
            {
                var home = ctx.Target.transform.Find(name);
                if (home == null || home.childCount == 0)
                {
                    continue;
                }
                ctx.Report.Warning(Category,
                    $"The avatar already carries {home.childCount} converted cloth object(s)",
                    $"\"{name}\" was on the avatar before this conversion started, so it arrived with " +
                    "the avatar: a conversion that copies the avatar never writes one onto the source. " +
                    "The known way in is converting once with \"Make a copy\" off. Anything this run " +
                    "converts is ADDED beside them, and both sets simulate the same bones, which fight. " +
                    $"Delete \"{name}\" from the avatar being converted, then convert again.");
            }
        }

        public static void Run(BridgeContext ctx)
        {
            // Before the early return: the synthesized rig cloth later in the
            // conversion reads both caches on an avatar with no PhysBones too.
            GrabbyBonesSupport.Reset();
#if AVATARBRIDGE_MAGICA
            MagicaPresetLibrary.Reset();
#endif
            ReportInheritedCloth(ctx);
            var physBones = ctx.Target.GetComponentsInChildren<VRCPhysBone>(true);
            if (physBones.Length == 0)
            {
                ctx.Report.Converted(Category, "No PhysBones found");
                return;
            }

            ReportGrabbableChains(ctx, physBones);

            // Stacked systems (e.g. cake PB) put several PhysBones on the same root and let the
            // animator switch between them. All get converted so nothing is lost, but at most
            // one is left driving the chain; see below for why, and why none are deleted.
            foreach (var group in physBones
                .GroupBy(pb => pb.rootTransform != null ? pb.rootTransform : pb.transform)
                .Where(g => g.Count() > 1))
            {
                var enabled = group.Where(pb => pb.isActiveAndEnabled).ToList();
                if (enabled.Count == 0)
                {
                    ctx.Report.Warning(Category, $"\"{group.Key.name}\" has NO active physics",
                        $"All {group.Count()} PhysBones on this chain were disabled when baked, so every " +
                        "generated cloth starts disabled and the chain will not move. Enable the variant " +
                        "you want on the converted avatar.");
                }
                else if (enabled.Count > 1)
                {
                    // Two solvers on one chain always fight; they take turns moving the same
                    // bones and the result is jitter, not a blend. Nothing is deleted, because
                    // which variant the author wanted is their call: the extras are switched
                    // off so only one drives the chain, and a checkbox puts any of them back.
                    for (int i = 1; i < enabled.Count; i++)
                    {
                        enabled[i].enabled = false;
                    }
                    ctx.Report.Warning(Category, $"{group.Count()} PhysBones share root \"{group.Key.name}\"",
                        $"{enabled.Count} were enabled at once, which would give this chain {enabled.Count} " +
                        $"cloths on the same bones. Only \"{DescribeVariant(enabled[0])}\" is on; the rest are " +
                        "switched off, not deleted.");
                }
                else
                {
                    ctx.Report.Warning(Category, $"{group.Count()} PhysBones share root \"{group.Key.name}\"",
                        "VRChat toggles between them at runtime; 1 started enabled and only that one was " +
                        "activated. Review the generated components and delete the variants you don't need.");
                }
            }

            switch (ctx.Settings.physicsTarget)
            {
                case PhysicsTarget.MagicaCloth2:
#if AVATARBRIDGE_MAGICA
                    var magicaColliderCache = new Dictionary<VRCPhysBoneCollider, MagicaCloth2.ColliderComponent>();
                    var writtenCloths = new List<(PhysBoneChainData, MagicaCloth2.MagicaCloth)>();
                    var skinnedM = SkinnedBones(ctx);
                    var drivenM = ConstraintDriven(ctx);
                    foreach (var pb in physBones)
                    {
                        var chain = PhysBoneChainData.Read(pb, ctx.TargetAnimator, !ctx.Settings.convertToePhysBones);
                        if (SkipToeChain(ctx, chain) || SkipConstraintDrivenChain(ctx, chain, drivenM)
                            || SkipSquishOnlyChain(ctx, chain, skinnedM, drivenM)
                            || SkipHelperRigChain(ctx, chain, skinnedM))
                        {
                            continue;
                        }
                        writtenCloths.Add((chain, MagicaClothWriter.Write(ctx, chain, magicaColliderCache)));
                    }
                    // Runs last: every collider the avatar defines has to exist before a chain can
                    // be offered one it didn't originally reference.
                    MagicaColliderAutoAssign.Run(ctx, writtenCloths, magicaColliderCache);
                    if (ctx.Settings.softBodiesCollide)
                    {
                        SoftBodyContact.Link(ctx.Target, ctx.Report);
                    }
                    DeleteConverted(ctx, physBones);
                    break;
#else
                    // The break belongs inside this #if rather than after the #endif: without
                    // MagicaCloth2 the branch above ends in a return, which leaves a trailing
                    // break unreachable and earns CS0162 in every project that picked DynamicBone.
                    // A warning naming AvatarBridge is the first thing someone checks when they
                    // suspect the tool.
                    ctx.Report.Error(Category, "MagicaCloth2 is not installed",
                        "Import MagicaCloth2 (or choose the DynamicBone target) and convert again.");
                    return;
#endif

                case PhysicsTarget.DynamicBone:
#if AVATARBRIDGE_DYNBONE
                    var dbColliderCache = new Dictionary<VRCPhysBoneCollider, DynamicBoneColliderBase>();
                    var skinnedD = SkinnedBones(ctx);
                    var drivenD = ConstraintDriven(ctx);
                    foreach (var pb in physBones)
                    {
                        var dbChain = PhysBoneChainData.Read(pb, ctx.TargetAnimator, !ctx.Settings.convertToePhysBones);
                        if (SkipToeChain(ctx, dbChain) || SkipConstraintDrivenChain(ctx, dbChain, drivenD)
                            || SkipSquishOnlyChain(ctx, dbChain, skinnedD, drivenD)
                            || SkipHelperRigChain(ctx, dbChain, skinnedD))
                        {
                            continue;
                        }
                        DynamicBoneWriter.Write(ctx, dbChain, dbColliderCache);
                    }
                    DeleteConverted(ctx, physBones);
                    break;
#else
                    // Same shape as the MagicaCloth2 branch above, and the same reason.
                    ctx.Report.Error(Category, "DynamicBone is not installed",
                        "Import DynamicBone or the VRLabs Dynamic-Bones-Stub, or choose MagicaCloth2.");
                    return;
#endif

                default:
                    ctx.Report.Skipped(Category, $"{physBones.Length} PhysBone(s)",
                        "Physics conversion disabled in settings.");
                    return;
            }
        }

        // Called from each converting branch, not after the switch: with
        // neither package installed every branch returns, and code after it
        // was unreachable, the CS0162 the breaks above exist to avoid.
        static void DeleteConverted(BridgeContext ctx, VRCPhysBone[] physBones)
        {
            if (!ctx.Settings.deleteConvertedPhysBones) return;
            foreach (var pb in physBones)
            {
                Object.DestroyImmediate(pb);
            }
            foreach (var collider in ctx.Target.GetComponentsInChildren<VRCPhysBoneCollider>(true))
            {
                Object.DestroyImmediate(collider);
            }
        }

        static void ReportGrabbableChains(BridgeContext ctx, VRCPhysBone[] physBones)
        {
            var grabbable = new List<string>();
            foreach (var pb in physBones)
            {
                // AdvancedBool, not bool: "Other" means "defer to the filter", which still allows
                // grabbing, so only an explicit False rules a chain out.
                if (pb == null || pb.allowGrabbing == VRC.Dynamics.VRCPhysBoneBase.AdvancedBool.False)
                {
                    continue;
                }
                var root = pb.rootTransform != null ? pb.rootTransform : pb.transform;
                // Contacts riding the chain are the tell that grabbing DRIVES something, rather
                // than just being a nicety on some hair.
                bool carriesContact = root.GetComponentInChildren<
                    VRC.SDK3.Dynamics.Contact.Components.VRCContactSender>(true) != null;
                grabbable.Add(root.name + (carriesContact ? " (carries a contact)" : ""));
            }
            // With physics off nothing simulates the chain, so there is no solver to name.
            string target = TargetName(ctx);
            if (grabbable.Count == 0 || target == null)
            {
                return;
            }
            ctx.Report.Approximated(Category,
                $"{grabbable.Count} chain(s) could be grabbed in VRChat; {target} can't be",
                string.Join(", ", grabbable.Take(8)) + (grabbable.Count > 8 ? ", …" : "") +
                ". Only players with the GrabbyBones mod can grab them. A chain that \"carries a contact\" " +
                "may need grabbing to work at all.");
        }

        // Every bone any skinned mesh lists. Built once per run, here and by
        // HelperRigCleanup. Listed, not weighted, unlike the constraint pass:
        // a helper rig judged by this gets deleted, and a listed bone that is
        // deleted leaves a hole in that renderer's bone array.
        internal static HashSet<Transform> SkinnedBones(BridgeContext ctx)
        {
            var used = new HashSet<Transform>();
            foreach (var skin in ctx.Target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin == null || skin.bones == null) continue;
                foreach (var b in skin.bones)
                {
                    if (b != null) used.Add(b);
                }
            }
            return used;
        }

        // A chain that moves nothing on its own: no mesh is weighted to any
        // bone in it. Some rigs are built this way: a cascade of
        // helper bones, each stage doing one job and feeding the next, with
        // constraints copying the composed result onto the avatar's real
        // bones.
        //
        // Converting each stage separately does not reproduce that. The
        // cascade stops composing and the constraints faithfully copy a
        // dozen solvers fighting, which lands on the mesh as deformation.
        // The chain is recorded so a later pass, once constraints exist, can
        // put ONE chain on the real bone it was driving.
        static bool SkipHelperRigChain(BridgeContext ctx, PhysBoneChainData chain, HashSet<Transform> skinned)
        {
            if (chain.Root == null) return false;
            foreach (var t in chain.Root.GetComponentsInChildren<Transform>(true))
            {
                if (skinned.Contains(t)) return false;
            }
            // A rigid accessory (an earring, a charm, a particle prop) skins
            // no mesh either, and HelperRigCleanup deletes what it is handed.
            if (HelperRigCleanup.HoldsContent(chain.Root)) return false;

            ctx.Report.Skipped(Category, ctx.PathInTarget(chain.Root),
                "A helper stage that skins no mesh. If a constraint carried its motion onto a bone, a later " +
                "pass puts one chain there.");
            ctx.HelperRigChains.Add(new BridgeContext.HelperRigChain
            {
                Root = chain.Root,
                Pull = chain.Pull,
                Spring = chain.Spring,
                Stiffness = chain.Stiffness,
                Gravity = chain.Gravity,
                Immobile = chain.Immobile,
                IsAdvancedIntegration = chain.IsAdvancedIntegration,
            });
            return true;
        }

        // A chain whose only motion was stretch and squish. Neither solver can
        // lengthen a bone, so converting one builds a chain that SWINGS where
        // the author asked for scale, and bones meant to stay put wander.
        //
        // Cake PB is the common case: its Squish chains are pull 1, spring 0,
        // stiffness 0, max squish 1, and the squish itself is driven by the
        // animator, which converts fine on its own. Chains with real spring or
        // stiffness are left alone; they lose the squish and keep the swing
        // they were also doing. So are chains with pull below 1 or any
        // gravity: spring 0 only removes momentum, and those still lag and hang.
        //
        // Unless nothing anyone sees moves with them. A grab-and-stretch
        // handle is built that way, and its stretch drives the animator. Let
        // through, it skins no mesh, so the helper-rig path took it, deleted
        // it with the empty bones around it, and reported a cascade.
        static bool SkipSquishOnlyChain(BridgeContext ctx, PhysBoneChainData chain, HashSet<Transform> skinned,
            Dictionary<Transform, List<Component>> driven)
        {
            if (chain.Root == null) return false;
            if (chain.MaxStretch <= 0f && chain.MaxSquish <= 0f) return false;
            if (chain.Spring > 0.01f || chain.Stiffness > 0.01f) return false;
            if (chain.Pull < 0.99f || Mathf.Abs(chain.Gravity) > 0.01f)
            {
                if (MovesSomething(chain.Root, skinned, driven)) return false;
                ctx.Report.Skipped(Category, chain.Root.name,
                    $"Moves nothing rendered (max stretch {chain.MaxStretch:0.##}, pull {chain.Pull:0.##}, gravity " +
                    $"{chain.Gravity:0.##}): no mesh is weighted to it, nothing visible sits under it, and no " +
                    "constraint carries its motion to either. No cloth made; its bones are left as they were.");
                return true;
            }

            ctx.Report.Skipped(Category, chain.Root.name,
                $"Stretch and squish were all this chain did (max stretch {chain.MaxStretch:0.##}, max squish " +
                $"{chain.MaxSquish:0.##}, pull {chain.Pull:0.##}, spring {chain.Spring:0.##}, stiffness " +
                $"{chain.Stiffness:0.##}, gravity {chain.Gravity:0.##}), which neither solver can do. No cloth made.");
            return true;
        }

        // A mesh weighted to the chain, content under it, or a constraint
        // relaying its motion onto either. Relays are followed through empty
        // bones in between: a staged rig reaches the mesh in several hops, and
        // a relay onto an empty bone alone moves nothing.
        static bool MovesSomething(Transform root, HashSet<Transform> skinned,
            Dictionary<Transform, List<Component>> driven)
        {
            if (HelperRigCleanup.HoldsContent(root)) return true;
            var reached = new HashSet<Transform>(root.GetComponentsInChildren<Transform>(true));
            if (reached.Overlaps(skinned)) return true;
            for (bool grew = true; grew;)
            {
                grew = false;
                foreach (var relay in driven)
                {
                    if (reached.Contains(relay.Key)
                        || !relay.Value.Any(c => AvatarFeatureDetect.ConstraintSources(c).Any(reached.Contains))) continue;
                    var moved = relay.Key.GetComponentsInChildren<Transform>(true);
                    if (moved.Any(skinned.Contains) || HelperRigCleanup.HoldsContent(relay.Key)) return true;
                    reached.UnionWith(moved);
                    grew = true;
                }
            }
            return false;
        }

        // Every constraint on the avatar, keyed by the transform it WRITES. A
        // VRC constraint with Target Transform set drives that target wherever
        // the component sits, and the constraint pass rebuilds it there, so
        // looking only at components on the chain's own bones missed it.
        static Dictionary<Transform, List<Component>> ConstraintDriven(BridgeContext ctx)
        {
            var driven = new Dictionary<Transform, List<Component>>();
            foreach (var component in ctx.Target.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    continue;
                }
                // Unity's constraints count too, not just VRChat's. 2.91.0 only matched
                // "VRC*Constraint"; but the NaN is an engine-level feedback loop between a
                // component that WRITES a rotation every frame and a solver that integrates
                // from its own previous state, and which constraint type does the writing is
                // irrelevant. A base authored entirely on Unity constraints sailed straight
                // past the 2.91.0 check with a tail cloth simulating three constraint-driven
                // bones.
                var type = component.GetType();
                bool vrc = type.Name.StartsWith("VRC", System.StringComparison.Ordinal)
                           && type.Name.EndsWith("Constraint", System.StringComparison.Ordinal);
                if (!vrc && !(component is UnityEngine.Animations.IConstraint))
                {
                    continue;
                }
                // Not "?? component.transform": an unassigned Transform is
                // Unity's fake null; == says null, ?? passes it.
                var target = vrc
                    ? (type.GetField("TargetTransform")?.GetValue(component)
                       ?? type.GetProperty("TargetTransform")?.GetValue(component)) as Transform
                    : null;
                // A target outside the avatar is not followed: the constraint
                // pass rebuilds it on its own object then (HostFor).
                var writes = target != null && target.IsChildOf(ctx.Target.transform)
                    ? target : component.transform;
                if (!driven.TryGetValue(writes, out var writers))
                {
                    driven[writes] = writers = new List<Component>();
                }
                writers.Add(component);
            }
            return driven;
        }

        static bool SkipConstraintDrivenChain(BridgeContext ctx, PhysBoneChainData chain,
            Dictionary<Transform, List<Component>> driven)
        {
            if (chain.Root == null)
            {
                return false;
            }
            // Only the bones that can simulate. A humanoid exclusion takes its
            // subtree out of the solver for good, so a twist constraint on a
            // thigh must not cost a tail rooted at Hips. An author's own ignore
            // still counts: MagicaCloth2 gives those up when honouring them
            // would leave the chain unable to move.
            var simulated = new List<Transform>();
            void Walk(Transform t)
            {
                if (t == null || chain.HumanoidExclusions.Contains(t)) return;
                simulated.Add(t);
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i));
            }
            Walk(chain.Root);

            string scaleOnly = null;
            foreach (var t in simulated)
            {
                if (!driven.TryGetValue(t, out var writers))
                {
                    continue;
                }
                foreach (var component in writers)
                {
                    // Except scale. The loop above is two things writing the
                    // SAME channel, and a scale constraint writes localScale
                    // and nothing else: the solver moves and rotates the bone,
                    // which a scale constraint never touches. Refusing these
                    // too cost avatars their chain for no reason, and the
                    // control that switched it kept converting into a menu
                    // entry with no physics behind it.
                    if (WritesScaleOnly(component))
                    {
                        scaleOnly = t.name;
                        continue;
                    }
                    ctx.Report.Skipped(Category, ctx.PathInTarget(chain.Root),
                        $"Not simulated: \"{t.name}\" in this chain is driven by a " +
                        $"{component.GetType().Name}, and the two would break the bone. Remove the constraint to " +
                        "simulate the chain.");
                    return true;
                }
            }
            if (scaleOnly != null)
            {
                ctx.Report.Approximated(Category, ctx.PathInTarget(chain.Root),
                    $"Simulated even though \"{scaleOnly}\" in this chain is driven by a scale constraint. "
                    + "Scale does not fight the solver, but bone lengths are measured once, at this scale.");
            }
            return false;
        }

        // Scale is the one channel a cloth solver never writes. Everything
        // else a constraint can drive, position and rotation, is exactly what
        // the solver is writing every frame.
        static bool WritesScaleOnly(Component component)
        {
            return component is UnityEngine.Animations.ScaleConstraint
                   || component.GetType().Name == "VRCScaleConstraint";
        }

        static bool SkipToeChain(BridgeContext ctx, PhysBoneChainData chain)
        {
            if (ctx.Settings.convertToePhysBones || chain.Root == null)
            {
                return false;
            }
            bool isToe = chain.Root.name.IndexOf("toe", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isToe)
            {
                var animator = ctx.TargetAnimator;
                if (animator != null && animator.isHuman)
                {
                    foreach (var bone in new[] { HumanBodyBones.LeftToes, HumanBodyBones.RightToes })
                    {
                        var toes = animator.GetBoneTransform(bone);
                        if (toes != null && (chain.Root == toes || chain.Root.IsChildOf(toes)))
                        {
                            isToe = true;
                            break;
                        }
                    }
                }
            }
            if (isToe)
            {
                ctx.Report.Skipped(Category, chain.Root.name,
                    "Toe chain not converted: simulated toes wiggle with every step in ChilloutVR, " +
                    "which reads as broken rather than expressive. Turn on \"Convert toe PhysBones\" " +
                    "in the physics options if this avatar's toe physics are deliberate.");
            }
            return isToe;
        }

        static string DescribeVariant(VRCPhysBone pb)
        {
            return string.IsNullOrEmpty(pb.parameter)
                ? pb.gameObject.name
                : $"{pb.gameObject.name} ({pb.parameter})";
        }
    }
}
#endif
