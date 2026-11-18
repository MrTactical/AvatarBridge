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

        // Where a writer parks what it makes. Here because the two writers compile on different
        // defines; in the Magica writer, a DynamicBone-only project would not compile.
        internal static Transform CollectionUnder(BridgeContext ctx, string name)
        {
            var target = ctx.Target.transform;
            var home = target.Find(name);
            if (home != null) return home;
            var made = new GameObject(name);
            made.transform.SetParent(target, false);
            return made.transform;
        }

        // Two zones or holders under one bone would share an animation path, and every curve
        // reaches only the first. Compared by name, not Transform.Find: a '/' in a parameter or
        // bone name reads to Find as a path, so a clash went unseen.
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

            // A new host is live from load, so an off collider came out always on. The host takes
            // any off state the hierarchy does not already give it. RepointColliderEnableCurves
            // carries the switching, a native toggle ToggleNativizer.WithContactHosts.
            bool live = original.enabled;
            if (!live)
            {
                ctx.DisabledHosts.Add(hostPath);
            }
            // Judged inside the avatar only: one converted in place may itself be off in the scene.
            for (var t = original.transform; live && t != null && t != ctx.Target.transform; t = t.parent)
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
                ctx.Report.Converted(Category, $"{BridgeElements.Count(repointed + mirrored, "collider on/off animation", "collider on/off animations")} rewired",
                    "They now toggle the converted collider's object." +
                    (mirrored > 0 ? $" {mirrored} switched its parent object and now reach it too." : ""));
            }
            if (dropped.Count > 0)
            {
                ctx.Report.Warning(Category, $"{BridgeElements.Count(dropped.Count, "collider-animating curve", "collider-animating curves")} could not be carried",
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
                    $"{BridgeElements.Count(lost.Count, "animated PhysBone parameter has", "animated PhysBone parameters have")} no converted equivalent",
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
                    $"The avatar already carries {BridgeElements.Count(home.childCount, "converted cloth object", "converted cloth objects")}",
                    $"\"{name}\" was on the avatar before this conversion started, so it arrived with " +
                    "the avatar: a conversion that copies the avatar never writes one onto the source. " +
                    "The known way in is converting once with \"Work on a clone (recommended)\" off. Anything this run " +
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

            // The branches below that return convert nothing, so nothing is switched off either:
            // the Toolkit's card would leave the user's own variants disabled.
            bool converts =
#if AVATARBRIDGE_MAGICA
                ctx.Settings.physicsTarget == PhysicsTarget.MagicaCloth2 ||
#endif
#if AVATARBRIDGE_DYNBONE
                ctx.Settings.physicsTarget == PhysicsTarget.DynamicBone ||
#endif
                false;

            // Stacked systems (e.g. cake PB) put several PhysBones on the same root and let the
            // animator switch between them. All get converted so nothing is lost, but at most
            // one is left driving the chain; see below for why, and why none are deleted.
            foreach (var group in physBones
                .GroupBy(pb => pb.rootTransform != null ? pb.rootTransform : pb.transform)
                .Where(g => converts && g.Count() > 1))
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
                    // Two solvers on one chain jitter. The extras are switched off, not deleted,
                    // so a checkbox puts any of them back.
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
                    // Read every chain before writing any: grouping compares chains
                    // against each other, so it cannot run inside the write loop.
                    var readChains = new List<PhysBoneChainData>();
                    foreach (var pb in physBones)
                    {
                        var chain = PhysBoneChainData.Read(pb, ctx.TargetAnimator, !ctx.Settings.convertToePhysBones);
                        if (SkipToeChain(ctx, chain) || SkipConstraintDrivenChain(ctx, chain, drivenM)
                            || SkipSquishOnlyChain(ctx, chain, skinnedM, drivenM)
                            || SkipHelperRigChain(ctx, chain, skinnedM))
                        {
                            continue;
                        }
                        readChains.Add(chain);
                    }
                    foreach (var chain in PhysBoneChainGrouping.Merge(ctx, readChains))
                    {
                        writtenCloths.Add((chain, MagicaClothWriter.Write(ctx, chain, magicaColliderCache)));
                    }
                    // Runs last: every collider the avatar defines has to exist before a chain can
                    // be offered one it didn't originally reference.
                    MagicaColliderAutoAssign.Run(ctx, writtenCloths, magicaColliderCache);
                    DeleteConverted(ctx, physBones);
                    break;
#else
                    // Inside the #if: without MagicaCloth2 the branch above returns, and a trailing
                    // break would warn CS0162 in every DynamicBone project.
                    ctx.Report.Error(Category, "MagicaCloth2 is not installed", ctx.Standalone
                        ? "Import MagicaCloth2, or choose DynamicBone in the Convert tab's Physics options, and press again."
                        : "Import MagicaCloth2 (or choose the DynamicBone target) and convert again.");
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
                        "Import DynamicBone or the VRLabs Dynamic-Bones-Stub, or choose MagicaCloth2" +
                        (ctx.Standalone ? " in the Convert tab's Physics options." : "."));
                    return;
#endif

                default:
                    ctx.Report.Skipped(Category, $"{BridgeElements.Count(physBones.Length, "PhysBone", "PhysBones")}",
                        ctx.Standalone ? "Physics is set to None in the Convert tab's Physics options."
                            : "Physics conversion disabled in settings.");
                    return;
            }
        }

        // Called from each converting branch, not after the switch: with
        // neither package installed every branch returns, and code after it
        // was unreachable, the CS0162 the breaks above exist to avoid.
        static void DeleteConverted(BridgeContext ctx, VRCPhysBone[] physBones)
        {
            if (!ctx.Settings.deleteConvertedPhysBones) return;
            // The Toolkit's card edits the user's own avatar, so Ctrl+Z must bring them back.
            void Destroy(Object o)
            {
                if (ctx.Standalone) UnityEditor.Undo.DestroyObjectImmediate(o);
                else Object.DestroyImmediate(o);
            }
            foreach (var pb in physBones)
            {
                Destroy(pb);
            }
            foreach (var collider in ctx.Target.GetComponentsInChildren<VRCPhysBoneCollider>(true))
            {
                Destroy(collider);
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
                $"{BridgeElements.Count(grabbable.Count, "chain", "chains")} could be grabbed in VRChat; {target} can't be",
                string.Join(", ", grabbable.Take(8)) + (grabbable.Count > 8 ? ", …" : "") +
                ". Only players with the GrabbyBones mod can grab them. A chain that \"carries a contact\" " +
                "may need grabbing to work at all.");
        }

        // Every bone any skinned mesh lists, once per run (also HelperRigCleanup). Listed, not
        // weighted: deleting a listed bone leaves a hole in the renderer's bone array.
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

        // A chain no mesh is weighted to: a helper cascade whose constraints copy the result onto
        // real bones. Converted stage by stage, a dozen solvers fight and deform the mesh, so it
        // is recorded for a later pass to put one chain on the real bone.
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

            // The later pass is the converter's alone.
            ctx.Report.Skipped(Category, ctx.PathInTarget(chain.Root), ctx.Standalone
                ? "A helper stage that skins no mesh, left without physics here. A conversion from the VRChat " +
                  "avatar puts one chain on the bone a constraint carried its motion onto."
                : "A helper stage that skins no mesh. If a constraint carried its motion onto a bone, a later " +
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

        // A chain whose only motion was stretch and squish. Neither solver lengthens a bone, so
        // it would swing where the author asked for scale. Cake PB Squish chains are the common
        // case (pull 1, spring 0, stiffness 0, max squish 1; the animator does the squish).
        // Real spring or stiffness, pull below 1 or any gravity: kept, they still swing or hang.
        // Also caught when nothing visible moves with it: a grab-and-stretch handle drives the
        // animator, and the helper-rig path would delete it as a cascade.
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

        // Every constraint keyed by the transform it writes: Target Transform drives a bone from
        // anywhere, so checking the chain's own bones misses it.
        static Dictionary<Transform, List<Component>> ConstraintDriven(BridgeContext ctx)
        {
            var driven = new Dictionary<Transform, List<Component>>();
            foreach (var component in ctx.Target.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    continue;
                }
                // Unity's constraints too: the NaN is a feedback loop between any per-frame
                // rotation writer and a solver integrating its own previous state.
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
            // Only bones that can simulate: a thigh twist constraint must not cost a tail rooted at
            // Hips. Author ignores still count, MagicaCloth2 drops them if the chain could not move.
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
                    // Except scale: it writes only localScale, which the solver never touches.
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
