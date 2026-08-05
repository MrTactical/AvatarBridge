#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge
{
    /// <summary>
    /// Converts every VRCPhysBone on the avatar to the chosen ChilloutVR-compatible
    /// physics system (MagicaCloth2 preferred, DynamicBone as fallback) and removes the
    /// original PhysBone components afterwards.
    /// </summary>
    public static class PhysBoneConverter
    {
        const string Category = "PhysBones";

        /// <summary>
        /// Remembers where a VRC collider's replacement landed, keyed by the ORIGINAL component's
        /// animator path — which is what an m_Enabled curve binding carries. Called by both solver
        /// writers at collider creation, before the VRC component is deleted.
        /// </summary>
        internal static void RecordColliderHost(BridgeContext ctx, Component original, GameObject host)
        {
            string originalPath = BridgeContext.RelativePath(ctx.Target.transform, original.transform);
            string hostPath = BridgeContext.RelativePath(ctx.Target.transform, host.transform);
            if (!ctx.PhysicsColliderHosts.TryGetValue(originalPath, out var hosts))
            {
                ctx.PhysicsColliderHosts[originalPath] = hosts = new List<string>();
            }
            hosts.Add(hostPath);
        }

        /// <summary>
        /// Rewires animated collider on/off switches at the converted colliders. Runs after
        /// self-containment, from BridgeConverter, exactly like the contact-curve repoint.
        ///
        /// Avatars animate <c>VRCPhysBoneCollider.m_Enabled</c> so clothing can switch its own
        /// collision — a dress toggle disabling the leg colliders that would clip it (28 curves
        /// across the wild census: DressOn, CPB_Clipping_ON/OFF, CPB_Interaction_OFF). The
        /// component is deleted by conversion, so those curves played as silence.
        ///
        /// The retarget is the generated collider's own host object, verified against BOTH
        /// shipped solvers: MagicaCloth2's ColliderComponent routes OnEnable/OnDisable through
        /// MagicaManager.Collider.EnableCollider, and ChilloutVR's jobs-rewritten
        /// DynamicBoneColliderBase routes the same pair through SetColliderState — so a
        /// GameObject active toggle reaches each identically.
        /// </summary>
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
            var dropped = new SortedSet<string>(StableSampleOrder.Instance);
            foreach (var clip in clips)
            {
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
                        // A shape property (radius, height — no animatable equivalent written),
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

            if (repointed > 0)
            {
                ctx.Report.Converted(Category, $"{repointed} collider on/off animation(s) rewired",
                    "Curves that enabled or disabled a VRChat PhysBone collider now toggle the " +
                    "converted collider's own object instead — the form both MagicaCloth2 and " +
                    "DynamicBone honour. The usual author intent is clothing switching its own " +
                    "collision, a dress disabling the colliders that would clip it.");
            }
            if (dropped.Count > 0)
            {
                ctx.Report.Warning(Category, $"{dropped.Count} collider-animating curve(s) could not be carried",
                    string.Join("; ", dropped.Take(6)) + (dropped.Count > 6 ? ", …" : "") +
                    " — each animated something on a VRC collider that has no equivalent on the " +
                    "converted one, or a collider that was not converted (skipped, or its chain " +
                    "was). The curve was removed rather than left silently addressing a deleted " +
                    "component.");
            }
        }

        /// <summary>
        /// Names every animated PhysBone PARAMETER an avatar loses, and removes the dead curves.
        ///
        /// Avatars animate live physics values — radius with a size slider ("Boobs Bigger"),
        /// gravity, pull, immobile while a part resizes; ~110 curves across the wild census. The
        /// component is deleted by conversion and the path-based clip audit cannot see it (the
        /// OBJECT still exists), so until now these vanished with no trace.
        ///
        /// No retarget exists on the default path, and that is a measured fact, not a shrug:
        /// MagicaCloth2 has no animation hook — its parameters only re-apply through the
        /// SetParameterChange() API, which nothing animation-driven ever calls, and a helper
        /// component would be deleted by ChilloutVR's avatar whitelist. The shipped DynamicBone
        /// DOES carry OnDidApplyAnimationProperties, so a DynamicBone-target conversion could
        /// genuinely accept some of these (radius maps 1:1); recorded as future work rather than
        /// half-shipped, since Magica is the default and the value mappings are nonlinear.
        ///
        /// m_Enabled is deliberately not reported here: chain toggles are RewirePhysicsToggles'
        /// job (mirrored onto the generated cloth, stranded ones already warned). The dead VRC
        /// binding is still removed.
        /// </summary>
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
                ctx.Report.Skipped(Category,
                    $"{lost.Count} animated PhysBone parameter(s) have no converted equivalent",
                    string.Join("; ", lost.Select(kv => $"{kv.Key} (e.g. {string.Join(", ", kv.Value)})")) +
                    " — these animated LIVE physics values (a size slider growing a chain's radius, " +
                    "gravity or stiffness changing with an outfit). MagicaCloth2's parameters cannot " +
                    "be driven by animation, so the chain keeps the converted values it was built " +
                    "with; the rest of each animation still plays. If one of these mattered, say so " +
                    "in an issue — a DynamicBone-target conversion could support some of them.");
            }
        }

        public static void Run(BridgeContext ctx)
        {
            var physBones = ctx.Target.GetComponentsInChildren<VRCPhysBone>(true);
            if (physBones.Length == 0)
            {
                ctx.Report.Converted(Category, "No PhysBones found");
                return;
            }

            GrabbyBonesSupport.Reset();
            ReportGrabbableChains(ctx, physBones);

            // Stacked systems (e.g. cake PB) put several PhysBones on the same root and let the
            // animator switch between them. All get converted so nothing is lost, but at most
            // one is left driving the chain — see below for why, and why none are deleted.
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
                    // Two solvers on one chain always fight — they take turns moving the same
                    // bones and the result is jitter, not a blend. Nothing is deleted, because
                    // which variant the author wanted is their call: the extras are switched
                    // off so only one drives the chain, and a checkbox puts any of them back.
                    for (int i = 1; i < enabled.Count; i++)
                    {
                        enabled[i].enabled = false;
                    }
                    ctx.Report.Warning(Category, $"{group.Count()} PhysBones share root \"{group.Key.name}\"",
                        $"{enabled.Count} were enabled at once, which would give this chain {enabled.Count} " +
                        "MagicaCloth components fighting over the same bones. The extras were switched off " +
                        $"(not deleted), so only \"{DescribeVariant(enabled[0])}\" drives it — VRChat toggles " +
                        "between these at runtime, so pick the variant you want and re-enable it instead if " +
                        "this isn't the right one.");
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
                    foreach (var pb in physBones)
                    {
                        var chain = PhysBoneChainData.Read(pb, ctx.TargetAnimator, !ctx.Settings.convertToePhysBones);
                        if (SkipToeChain(ctx, chain) || SkipConstraintDrivenChain(ctx, chain))
                        {
                            continue;
                        }
                        writtenCloths.Add((chain, MagicaClothWriter.Write(ctx, chain, magicaColliderCache)));
                    }
                    // Runs last: every collider the avatar defines has to exist before a chain can
                    // be offered one it didn't originally reference.
                    MagicaColliderAutoAssign.Run(ctx, writtenCloths, magicaColliderCache);
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
                    foreach (var pb in physBones)
                    {
                        var dbChain = PhysBoneChainData.Read(pb, ctx.TargetAnimator, !ctx.Settings.convertToePhysBones);
                        if (SkipToeChain(ctx, dbChain) || SkipConstraintDrivenChain(ctx, dbChain))
                        {
                            continue;
                        }
                        DynamicBoneWriter.Write(ctx, dbChain, dbColliderCache);
                    }
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

            if (ctx.Settings.deleteConvertedPhysBones)
            {
                foreach (var pb in physBones)
                {
                    Object.DestroyImmediate(pb);
                }
                foreach (var collider in ctx.Target.GetComponentsInChildren<VRCPhysBoneCollider>(true))
                {
                    Object.DestroyImmediate(collider);
                }
            }
        }

        /// <summary>
        /// Names the chains whose whole point is being GRABBED, because that is the part which
        /// doesn't cross.
        ///
        /// MagicaCloth2 has no grab. VRChat's PhysBones do, and plenty of avatars are built
        /// entirely around it — a pump handle, a leash, a lever, anything a stranger is meant to
        /// pull. Worse, the failure is completely silent and looks like something else: on a
        /// balloon avatar the pump handle carries a contact SENDER, and inflating works by someone
        /// grabbing the handle so the sender moves into the matching receiver. Convert that and
        /// every part still checks out — cloth present, sender present, receiver present, tags
        /// matching — but nobody can grab the handle, so it never moves, so nothing fires. An
        /// afternoon went into blaming the contact tags for it.
        ///
        /// The GrabbyBones mod restores grabbing, but it is a client mod: it only works for people
        /// who have installed it, so an avatar that depends on it is inert for most of the room.
        /// Nothing here can fix that, which is exactly why it is worth saying.
        /// </summary>
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
            if (grabbable.Count == 0)
            {
                return;
            }
            ctx.Report.Approximated(Category,
                $"{grabbable.Count} chain(s) could be grabbed in VRChat; MagicaCloth2 can't be",
                string.Join(", ", grabbable.Take(8)) + (grabbable.Count > 8 ? ", …" : "") +
                ". VRChat lets you take hold of a PhysBone and pull it; MagicaCloth2 has no " +
                "equivalent, so these hang and swing but can't be held. The GrabbyBones mod adds " +
                "grabbing back, and this conversion names its cloths to match so it works — but it " +
                "is a client mod, so only people who have installed it can grab anything here. " +
                "Any chain marked \"carries a contact\" is worth a closer look: if the feature works " +
                "by someone pulling that chain so its contact reaches a receiver, then without the " +
                "mod the contact never fires and the whole feature is inert, with nothing visibly " +
                "wrong anywhere.");
        }

        /// <summary>
        /// A bone a constraint drives must never also be simulated.
        ///
        /// A constraint writes that bone's rotation every frame from somewhere else; a cloth
        /// solver integrates it from its own previous state. Run both on one bone and each is
        /// fed the other's output, the integration diverges, and within a second or two the
        /// transform is **NaN** — which never recovers, propagates down the hierarchy, and
        /// leaves everything below the cloth root at NaN position while the root itself looks
        /// fine.
        ///
        /// The symptom is not subtle and not obviously physics: the chain "explodes", or hangs
        /// in a pose nothing explains, at rest, in play mode and in game alike, with no motion
        /// needed to trigger it. It cost this project a long hunt through the animator and the
        /// constraint maths before the NaN was spotted.
        ///
        /// VRChat tolerates the overlap because its PhysBone integrator runs after constraints
        /// each frame and simply re-reads the result. MagicaCloth2 and DynamicBone do not, and
        /// nothing here can reorder them.
        ///
        /// Unity's own constraints count as much as VRChat's, which 2.91.0 missed by matching on
        /// the "VRC*Constraint" type name. The feedback loop is engine-level and does not care
        /// which component writes the rotation.
        ///
        /// The chain is skipped whole rather than trimmed. Simulating a constraint-driven bone
        /// is meaningless anyway — the constraint fully determines its rotation — and a chain
        /// re-rooted below the constrained part would be a different chain than its author made.
        /// </summary>
        static bool SkipConstraintDrivenChain(BridgeContext ctx, PhysBoneChainData chain)
        {
            if (chain.Root == null)
            {
                return false;
            }
            foreach (var t in chain.Root.GetComponentsInChildren<Transform>(true))
            {
                foreach (var component in t.GetComponents<Component>())
                {
                    if (component == null)
                    {
                        continue;
                    }
                    // Unity's constraints count too, not just VRChat's. 2.91.0 only matched
                    // "VRC*Constraint" — but the NaN is an engine-level feedback loop between a
                    // component that WRITES a rotation every frame and a solver that integrates
                    // from its own previous state, and which constraint type does the writing is
                    // irrelevant. An avatar authored on Unity constraints (the AnyTaur quadruped
                    // base is entirely built this way) sailed straight past the 2.91.0 check with
                    // a tail cloth simulating three constraint-driven bones.
                    bool isConstraint = component is UnityEngine.Animations.IConstraint
                        || (component.GetType().Name.StartsWith("VRC", System.StringComparison.Ordinal)
                            && component.GetType().Name.EndsWith("Constraint", System.StringComparison.Ordinal));
                    if (!isConstraint)
                    {
                        continue;
                    }
                    ctx.Report.Skipped(Category, ctx.PathInTarget(chain.Root),
                        $"Not simulated: \"{t.name}\" in this chain is driven by a " +
                        $"{component.GetType().Name}. A constraint writes that bone every frame " +
                        "and a cloth solver integrates it from its own last state, so together " +
                        "they feed each other until the transform goes NaN — the chain then hangs " +
                        "broken at rest, in play mode and in game, with nothing to see in the " +
                        "animator. VRChat gets away with it because PhysBones re-read the " +
                        "constraint result each frame; MagicaCloth2 and DynamicBone don't, and the " +
                        "order can't be changed here. The constraint fully determines that bone's " +
                        "rotation anyway, so the physics had nothing to add. Remove the constraint " +
                        "if you want the chain simulated instead.");
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Toe chains are skipped by default. VRChat avatars routinely put PhysBones on
        /// individual toe bones, and in ChilloutVR the result is toes that wiggle with every
        /// step — universally read as broken rather than expressive. A chain counts as a toe
        /// chain when its root is (or sits under) a humanoid Toes bone, or when its root's name
        /// says so. "Convert toe PhysBones" in the physics options brings them back.
        /// </summary>
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
                    "Toe chain not converted — simulated toes wiggle with every step in ChilloutVR, " +
                    "which reads as broken rather than expressive. Turn on \"Convert toe PhysBones\" " +
                    "in the physics options if this avatar's toe physics are deliberate.");
            }
            return isToe;
        }

        /// <summary>
        /// Enough to tell stacked variants apart in the report. They often sit on the same
        /// GameObject, so the name alone may not distinguish them; the parameter is what the
        /// animator was switching between.
        /// </summary>
        static string DescribeVariant(VRCPhysBone pb)
        {
            return string.IsNullOrEmpty(pb.parameter)
                ? pb.gameObject.name
                : $"{pb.gameObject.name} ({pb.parameter})";
        }
    }
}
#endif
