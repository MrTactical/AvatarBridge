#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;
using MagicaCloth2;

namespace AvatarBridge
{
    // Writes a VRCPhysBone chain as a MagicaCloth2 BoneCloth.
    // Structure transfers directly: root bones, colliders, exclusions,
    // enabled state. Physics values default to MagicaCloth2's own or a
    // matched preset; PhysBoneSolverMap derives the optional conversion
    // from both solvers' source (60 Hz vs 90 Hz per-step coefficients).
    public static class MagicaClothWriter
    {
        const string Category = "PhysBones -> MagicaCloth2";

        public static MagicaCloth Write(BridgeContext ctx, PhysBoneChainData data,
            Dictionary<VRCPhysBoneCollider, ColliderComponent> colliderCache)
        {
            // The GrabbyBones mod derives its animator parameters from the GameObject name
            // that holds the cloth component: "<name>_IsGrabbed" and "<name>_Angle". Naming
            // the holder after the PhysBone's parameter makes those line up with the FX
            // logic the avatar already has (e.g. "CPB_L" -> "CPB_L_IsGrabbed").
            string holderName = "MagicaCloth_" + data.Root.name;
            if (ctx.Settings.grabbyBonesSupport && !string.IsNullOrEmpty(data.Parameter))
            {
                holderName = GrabbyBonesSupport.RegisterAndName(ctx, data.Parameter);
            }
            // All holders together under one object. Placed before any clip
            // is aimed at it, so the animator pass computes the real path.
            var home = HolderHome(ctx);
            // Sibling-unique, because animation paths address children by name: an avatar with
            // four hairstyles produces several chains rooted at a bone called "Hair_root", and
            // two holders both named "MagicaCloth_Hair_root" mean every animation curve aimed at
            // one of them resolves to whichever Unity finds first.
            holderName = PhysBoneConverter.UniqueChildName(home, holderName);
            var holder = new GameObject(holderName);
            holder.transform.SetParent(home, false);
            var cloth = holder.AddComponent<MagicaCloth>();

            // Where "off" lives. Holders sit in one collection under the
            // avatar, so a holder is inactive only when the avatar itself
            // is: every source then reads inactive too, and only the
            // component's own flag says anything. Otherwise off is carried
            // on the component, and the animator pass writes the switch
            // onto that flag.
            bool ridesObject = !holder.activeInHierarchy;
            if (ridesObject)
            {
                cloth.enabled = data.ComponentEnabled;
                if (!data.ComponentEnabled)
                {
                    ctx.Report.Approximated(Category, data.Root.name,
                        "Source PhysBone component was disabled; cloth created disabled. Component " +
                        "toggles are re-wired by the animator pass.");
                }
            }
            else if (!data.InitiallyActive)
            {
                cloth.enabled = false;
                ctx.Report.Approximated(Category, data.Root.name, data.Synthesized
                    ? "Style was inactive, so the cloth starts off; its toggle switches it on."
                    : "Source PhysBone was off, so the cloth starts off; toggles that enabled it switch it on.");
            }
            ctx.ConvertedPhysicsChains.Add(new BridgeContext.ConvertedPhysicsChain
            {
                Source = data.SourceGameObject,
                Host = holder,
                Physics = cloth,
                Root = data.Root
            });
            var sdata = cloth.SerializeData;

            // Optional preset per chain kind. No arithmetic; one
            // author-tuned baseline swaps for another. ImportJson
            // preserves structural fields.
            string preset = null;
            bool customPreset = false;
            // Classified whether or not presets are in use. The chain's
            // kind decides its MagicaCloth2 idiom either way.
            var cls = MagicaPresetLibrary.Classify(data);
            string chainClass = cls.Name;
            if (ctx.Settings.useMagicaPresets)
            {
                if (!MagicaPresetLibrary.TryApply(sdata, cls, out preset, out customPreset, out string presetError))
                {
                    ctx.Report.Approximated(Category, data.Root.name,
                        $"No preset applied for chain class \"{cls.Name}\": {presetError}. Using " +
                        "MagicaCloth2's defaults instead.");
                    preset = null;
                }
            }

            // --- structure ---
            // Which MagicaCloth2 idiom this chain is. Soft bodies
            // anchored to a bone become BoneSpring, with selective
            // collision. Hair, tails, skirts stay BoneCloth.
            // After the preset import: ImportJson carries clothType,
            // so a later preset would take the idiom back.
            bool softBody = chainClass != null && SoftBodyClasses.Contains(chainClass);
            sdata.clothType = softBody
                ? ClothProcess.ClothType.BoneSpring
                : ClothProcess.ClothType.BoneCloth;

            // "Is Animated" means an animation moves these bones.
            // MagicaCloth2 settles to the initial pose by default, so
            // the two would fight and the cloth wins. Settling to the
            // animated pose is what the source was already doing.
            if (data.IsAnimated)
            {
                sdata.animationPoseRatio = 1f;
            }

            // Multi Child Type "Ignore" leaves a branching root unsimulated in VRChat, its
            // children fixed at their rest offsets beside it, and each branch simulates from its
            // own first bone. So the cloth is rooted at those children, not at the branching bone:
            // rooted there with rootRotation 0, the children swung about it like a hinge at the
            // waistband or hairline that VRChat does not have. rootRotation 1 everywhere: PhysBone
            // turns a root to follow its children, and 0.5 would leave every chain one joint
            // stiffer at the base. Presets never carry rootRotation, so this survives either order.
            // Not for a soft body, whose root is held by a spring rather than pinned.
            bool ignoreBranching = !softBody && IgnoresBranching(data);
            sdata.rootRotation = 1f;

            ctx.Report.Converted(Category, data.Root.name, ignoreBranching
                ? "Rooted at each branch's first bone, the branching bone itself left to the animation, as Multi Child Type Ignore does."
                : "Root turns with the chain (rotation 1), as in VRChat. Lower Root Rotation if the base moves too much.");

            if (data.HumanoidExclusions.Count > 0)
            {
                ctx.Report.Approximated(Category, data.Root.name,
                    $"{data.HumanoidExclusions.Count} humanoid-mapped bone(s) excluded from simulation: " +
                    $"{string.Join(", ", data.HumanoidExclusions.Take(4).Select(t => t.name))}" +
                    $"{(data.HumanoidExclusions.Count > 4 ? ", …" : "")}. The animator and IK own them; their " +
                    "other children still simulate.");
            }

            if (data.ToeExclusions.Count > 0)
            {
                ctx.Report.Approximated(Category, data.Root.name,
                    $"{data.ToeExclusions.Count} toe branch(es) excluded from simulation: " +
                    $"{string.Join(", ", data.ToeExclusions.Take(4).Select(t => t.name))}" +
                    $"{(data.ToeExclusions.Count > 4 ? ", …" : "")} and below. Turn on \"Convert toe PhysBones\" " +
                    "if they are deliberate.");
            }

            if (data.Ignores.Count == 0 && !ignoreBranching)
            {
                sdata.rootBones.Add(data.Root);
            }
            else
            {
                WriteRootsExcluding(ctx, sdata, data);   // with no ignores, that roots each child
            }

            // Endpoint Position appends a virtual bone to every leaf.
            // MagicaCloth2 only simulates transforms that exist, and a
            // childless root is one fixed particle that never moves.
            // So the virtual bone is made real: each leaf gets a
            // "<leaf>_End" child at the endpoint offset.
            if (data.EndpointPosition.sqrMagnitude > 1e-8f)
            {
                int tips = SynthesizeEndpointBones(sdata, data);
                if (tips > 0)
                {
                    ctx.Report.Approximated(Category, data.Root.name,
                        $"Endpoint Position ({data.EndpointPosition.x:0.###}, {data.EndpointPosition.y:0.###}, " +
                        $"{data.EndpointPosition.z:0.###}) realised as {tips} \"_End\" bone(s), so the tip can move.");
                }
            }

            if (data.Colliders.Count > 0)
            {
                // Edge, not Point: VRChat collides the whole bone, a capsule from joint to joint, so
                // with points a collider slips between two particles that VRChat's capsule would hit.
                // MagicaCloth2 keeps a soft body on points whatever this says.
                sdata.colliderCollisionConstraint.mode = ColliderCollisionConstraint.Mode.Edge;
                foreach (var pbCollider in data.Colliders)
                {
                    var collider = GetOrCreateCollider(ctx, pbCollider, colliderCache);
                    if (collider != null && !sdata.colliderCollisionConstraint.colliderList.Contains(collider))
                    {
                        sdata.colliderCollisionConstraint.colliderList.Add(collider);
                    }
                }
            }

            // --- the two opt-in extras -----------------------------------------------------

            // Particle size from the mesh. BEFORE the bound below, so a measurement that comes
            // back wider than the chain can carry is still railed in rather than trusted.
            if (ctx.Settings.fitRadiusToMesh)
            {
                float measured = MeasureMesh(ctx, data, out int samples, out _, out bool grownForReach);
                if (measured > 0f)
                {
                    float before = sdata.radius.value;
                    sdata.radius.value = measured;   // direct, so any depth curve on it survives
                    ctx.Report.Converted(Category, data.Root.name,
                        $"Particle radius {before:0.###} → {measured:0.###}, measured from the mesh these " +
                        $"bones move ({samples} vertex sample(s))." +
                        (grownForReach ? " Measured with its sliders at full." : ""));
                }
                else
                {
                    ctx.Report.Skipped(Category, data.Root.name,
                        "Particle radius left at the preset's value: no mesh vertices are weighted to " +
                        "these bones (or the mesh could not be read), so there was nothing to measure. " +
                        "Set it on the cloth by hand if this chain collides at the wrong size.");
                }
            }

            // Particle radius bound. A safety rail, not a conversion.
            // A particle wider than the bone gap overlaps its neighbour
            // and the solver shoves them apart.
            float spacing = MeasureBoneSpacing(data.Root);
            if (ctx.Settings.capParticleRadius && spacing > 0f && sdata.radius.value > spacing * 0.5f)
            {
                float was = sdata.radius.value;
                sdata.radius.value = spacing * 0.5f;   // assign directly, keeping any depth curve
                ctx.Report.Approximated(Category, data.Root.name,
                    $"Particle radius {was:0.###} reduced to {sdata.radius.value:0.###}: anything wider than " +
                    "the gap between bones makes neighbouring particles overlap and shove each other apart.");
            }

            if (data.Synthesized)
            {
                // No source PhysBone; nothing to derive, fit or limit
                // from. The preset stands as authored.
                ctx.Report.Converted(Category, data.Root.name,
                    $"SYNTHESIZED BoneCloth{(preset != null ? $" on the \"{chainClass}\" preset" : "")}: " +
                    "this toggled rig had NO physics in the source (no PhysBone; rigid in VRChat too). " +
                    "Created because \"Add physics to toggled rigs that have none\" is on. There was " +
                    "no source feel to derive from, so the preset stands as authored; tune the cloth " +
                    "directly if it moves wrong, or delete it if this rig was rigid on purpose.");
                return cloth;
            }

            if (ctx.Settings.derivePhysicsFromPhysBone)
            {
                DerivePhysics(ctx, data, sdata, softBody);
            }

            if (softBody)
            {
                ConfigureSoftBody(ctx, data, sdata, chainClass);
            }

            if (ctx.Settings.fitToPhysBone)
            {
                FitToPhysBone(ctx, data, sdata, softBody);
            }

            if (ctx.Settings.boundSwingToSourceLimit && softBody && !string.IsNullOrEmpty(data.LimitTypeName) && data.LimitTypeName != "None")
            {
                ctx.Report.Approximated(Category, data.Root.name,
                    $"Source {data.LimitTypeName} limit not converted: MagicaCloth2 turns distance bounds off for a soft " +
                    "body, whose spring range holds it near rest instead.");
            }
            else if (ctx.Settings.boundSwingToSourceLimit)
            {
                ApplyMotionLeash(ctx, data, sdata);
            }


            ReportSourceSettings(ctx, data, preset, chainClass, customPreset);
            return cloth;
        }

        public static MagicaCloth WriteSynthesized(BridgeContext ctx, Transform rigRoot)
        {
            var data = new PhysBoneChainData
            {
                SourceGameObject = rigRoot.gameObject,
                Root = rigRoot,
                InitiallyActive = rigRoot.gameObject.activeInHierarchy,
                Synthesized = true
            };
            return Write(ctx, data, new Dictionary<VRCPhysBoneCollider, ColliderComponent>());
        }

        public const string CollectionName = "MagicaCloth Phys";

        // One object for all of them, under the avatar.
        //
        // A cloth simulates the roots it is given wherever it sits.
        //
        // Holders no longer inherit an outfit's toggle, so the off state
        // rides the component and RewirePhysicsToggles asserts every stop.
        public static Transform HolderHome(BridgeContext ctx) =>
            PhysBoneConverter.CollectionUnder(ctx, CollectionName);

        static int SynthesizeEndpointBones(ClothSerializeData sdata, PhysBoneChainData data)
        {
            var ignored = new HashSet<Transform>(data.Ignores);
            int added = 0;

            void Walk(Transform node)
            {
                bool leaf = true;
                for (int i = 0; i < node.childCount; i++)
                {
                    var child = node.GetChild(i);
                    if (ignored.Contains(child))
                    {
                        continue;
                    }
                    leaf = false;
                    Walk(child);
                }
                // A tip written by an earlier cloth on the same root is a
                // leaf too; stacked PhysBones must not grow X_End_End.
                if (leaf && !node.name.EndsWith("_End", StringComparison.Ordinal))
                {
                    var tip = new GameObject(node.name + "_End");
                    tip.transform.SetParent(node, false);
                    tip.transform.localPosition = data.EndpointPosition;
                    added++;
                }
            }

            foreach (var root in sdata.rootBones)
            {
                if (root != null)
                {
                    Walk(root);
                }
            }
            return added;
        }

        static void WriteRootsExcluding(BridgeContext ctx, ClothSerializeData sdata, PhysBoneChainData data)
        {
            var ignored = new HashSet<Transform>(data.Ignores.Where(t => t != null));

            bool SubtreeHasIgnored(Transform t)
            {
                if (ignored.Contains(t))
                {
                    return true;
                }
                for (int i = 0; i < t.childCount; i++)
                {
                    if (SubtreeHasIgnored(t.GetChild(i)))
                    {
                        return true;
                    }
                }
                return false;
            }

            // Branches kept by giving up their ignores, for the report. Empty in the ordinary case.
            var unhonouredBranches = new List<Transform>();

            bool HumanoidUnder(Transform t)
            {
                foreach (var h in data.HumanoidExclusions)
                {
                    if (h != null && (h == t || h.IsChildOf(t)))
                    {
                        return true;
                    }
                }
                return false;
            }

            // Returns how many roots this subtree contributed, because a branch that contributes
            // NONE has to be noticed rather than silently dropped.
            int Collect(Transform t)
            {
                int added = 0;
                for (int i = 0; i < t.childCount; i++)
                {
                    var child = t.GetChild(i);
                    if (ignored.Contains(child))
                    {
                        continue; // an ignored transform takes its whole subtree with it
                    }
                    if (SubtreeHasIgnored(child))
                    {
                        int deeper = Collect(child); // something inside is ignored, so this can't be one root
                        if (deeper == 0 && !HumanoidUnder(child))
                        {
                            // Nothing rootable below; every path is
                            // blocked by an ignore. Root at the branch
                            // head and give up its ignores: a few bones
                            // jiggling beats losing the chain. Refused
                            // when a humanoid-mapped bone is among them.
                            sdata.rootBones.Add(child);
                            unhonouredBranches.Add(child);
                            deeper = 1;
                        }
                        added += deeper;
                    }
                    else
                    {
                        sdata.rootBones.Add(child);
                        added++;
                    }
                }
                return added;
            }

            Collect(data.Root);

            // A promoted root only moves through its descendants;
            // MagicaCloth2 pins roots, and a pinned leaf is a statue.
            // An endpoint offset rescues a leaf root, so leaves only
            // count as dead without one.
            int MovableDescendants(Transform t)
            {
                int n = 0;
                for (int i = 0; i < t.childCount; i++)
                {
                    var child = t.GetChild(i);
                    if (!ignored.Contains(child))
                    {
                        n += 1 + MovableDescendants(child);
                    }
                }
                return n;
            }

            var deadRoots = new List<Transform>();
            if (data.EndpointPosition.sqrMagnitude <= 1e-8f)
            {
                deadRoots = sdata.rootBones.Where(r => MovableDescendants(r) == 0).ToList();
            }

            // Excluding a bone roots below it, so ignores near the tips
            // can price out the chain. Losing over half of it means the
            // whole-root fallback, over-simulated but intact.
            int wholeChain = MovableDescendants(data.Root);
            int kept = sdata.rootBones.Where(r => !deadRoots.Contains(r))
                                      .Sum(r => 1 + MovableDescendants(r));
            bool costsTooMuch = wholeChain > 0 && kept * 2 < wholeChain
                                && data.HumanoidExclusions.Count == 0;

            if (sdata.rootBones.Count > deadRoots.Count && !costsTooMuch)
            {
                // At least one branch still moves: keep those, drop the statues.
                foreach (var dead in deadRoots)
                {
                    sdata.rootBones.Remove(dead);
                }
                var names = sdata.rootBones.Select(b => b.name).Take(6).ToList();
                ctx.Report.Approximated(Category, data.Root.name,
                    $"{data.Ignores.Count} Ignore Transform(s) honoured by rooting the cloth at " +
                    $"{sdata.rootBones.Count} branch(es) instead: {string.Join(", ", names)}" +
                    $"{(sdata.rootBones.Count > names.Count ? ", …" : "")}. Each branch bends from its second " +
                    "joint, so it may feel stiff at the base." +
                    (deadRoots.Count > 0 ? $" {deadRoots.Count} childless branch(es) dropped." : "") +
                    (unhonouredBranches.Count > 0
                        ? $" {unhonouredBranches.Count} branch(es) simulate their excluded bones, or nothing of " +
                          $"them would move: {string.Join(", ", unhonouredBranches.Select(b => b.name).Take(4))}" +
                          $"{(unhonouredBranches.Count > 4 ? ", …" : "")}."
                        : ""));
                return;
            }

            // Nothing movable survives the decomposition. Honouring the
            // ignores here ships a cloth that can never move; the choice
            // is over-simulating or a statue.
            bool humanoidInvolved = data.HumanoidExclusions.Count > 0;
            if (humanoidInvolved && deadRoots.Count > 0)
            {
                // Simulating a humanoid-mapped bone fights the animator and IK for the transform
                // every frame; a dead chain is the safer of two wrong answers. The pinned roots
                // stay so the cloth remains valid and inspectable.
                ctx.Report.Warning(Category, data.Root.name,
                    $"This cloth CANNOT move: honouring the {data.Ignores.Count} excluded transform(s) " +
                    $"(including {data.HumanoidExclusions.Count} humanoid bone(s)) leaves nothing to move. Kept " +
                    "for inspection; delete it or fix the source PhysBone.");
                return;
            }

            sdata.rootBones.Clear();
            sdata.rootBones.Add(data.Root);
            ctx.Report.Warning(Category, data.Root.name,
                $"{data.Ignores.Count} Ignore Transform(s) NOT honoured: MagicaCloth2 can only exclude " +
                "a bone by rooting below it, and " +
                (costsTooMuch
                    ? $"that would simulate only {kept} of {wholeChain} bone(s). "
                    : "that would leave nothing to move. ") +
                $"The whole tree simulates from \"{data.Root.name}\", so those bones jiggle where VRChat held them.");
        }

        static void ReportSourceSettings(BridgeContext ctx, PhysBoneChainData data, string preset,
            string chainClass, bool customPreset)
        {
            string baseline;
            if (preset == null)
            {
                baseline = "MagicaCloth2's defaults";
            }
            else
            {
                // Naming the class as well as the preset makes a misread name obvious, and tells
                // you which file to drop in if you want to tune this kind of chain.
                baseline = $"the {(customPreset ? "custom" : "MagicaCloth2")} " +
                           $"\"{MagicaPresetLibrary.DisplayName(preset)}\" preset (read as a " +
                           $"\"{chainClass}\" chain)";
            }
            string fate = ctx.Settings.derivePhysicsFromPhysBone
                ? "Pull, spring and stiffness were converted into damping and angle restoration; gravity " +
                  "and immobile are handled separately, and the particle radius is measured from the mesh " +
                  "rather than taken from this number. Tune the cloth directly if this chain wants a " +
                  "different feel."
                : "Those numbers were not transferred: the cloth uses the baseline above. Turn on \"Derive " +
                  "physics from the PhysBone\" to convert pull, spring and stiffness, or tune the cloth by hand.";

            ctx.Report.Converted(Category, data.Root.name,
                $"BoneCloth on {baseline}, {data.Colliders.Count} collider(s). Source PhysBone was pull " +
                $"{data.Pull:0.##}, spring {data.Spring:0.##}, stiffness {data.Stiffness:0.##}, gravity " +
                $"{data.Gravity:0.##}, immobile {data.Immobile:0.##}, radius {data.Radius:0.###}. {fate}");

            if (data.MaxStretch > 0f)
            {
                ctx.Report.Skipped(Category, data.Root.name,
                    $"Max Stretch {data.MaxStretch:0.##} is not converted: MagicaCloth2 lets no chain stretch more " +
                    "than 3% of its length, however it is set.");
            }

            if (data.IsAnimated)
            {
                // The report entry for the animationPoseRatio decision
                // taken above. The source flag answers it; nothing to ask.
                ctx.Report.Converted(Category, data.Root.name,
                    "'Is Animated' was on, so the cloth follows the animated pose (Animation Pose Ratio 1).");
            }

            if (data.RootHasMultipleChildren && !string.IsNullOrEmpty(data.MultiChildTypeName))
            {
                ctx.Report.Approximated(Category, data.Root.name, data.MultiChildTypeName == "Ignore"
                    ? "Multi Child Type 'Ignore': each branch simulates from its own first bone, as in VRChat."
                    : data.MultiChildTypeName == "Average"
                        ? "Multi Child Type 'Average': the root turns toward the average of its branches, as in VRChat, but " +
                          "MagicaCloth2 cannot carry the other branches rigidly with it."
                        : $"Multi Child Type '{data.MultiChildTypeName}' has no equivalent: the root turns toward the average of its branches.");
            }

            if (!string.IsNullOrEmpty(data.Parameter))
            {
                ctx.Report.Approximated(Category, data.Root.name, ctx.Settings.grabbyBonesSupport
                    ? $"PhysBone parameter \"{data.Parameter}\": _IsGrabbed and _Angle work via the GrabbyBones " +
                      "mod (cloth object named to match). _Stretch/_Squish/_IsPosed have no equivalent."
                    : $"PhysBone parameter \"{data.Parameter}\" (_IsGrabbed/_Angle/_Stretch) has no CVR equivalent.");
            }
        }

        static void DerivePhysics(BridgeContext ctx, PhysBoneChainData data, ClothSerializeData sdata, bool softBody)
        {
            bool advanced = data.IsAdvancedIntegration;

            // The preset's damping, read before overwrite, kept as a floor. Restoration has
            // none on a hanging chain: a floor at the preset's root value made loose hair about
            // nine times stiffer than its PhysBone, measured in Play mode as half the swing. A
            // soft body keeps the preset's root restoration as its floor at both ends: its
            // PhysBone's pull is low because the PhysBone holds it in other ways, and with the
            // floor gone the copies swung 100 to 180 degrees on a walk where the originals swung
            // 8; following the preset's taper instead still left them swinging 50.
            float dampFloor = sdata.damping.value;
            float restPreset = sdata.angleRestorationConstraint.stiffness.value;
            float restFloorRoot = softBody ? restPreset : 0f;
            float restFloorTip = restFloorRoot;

            // Evaluate both ends of the chain. PhysBone multiplies each base value by its curve
            // at the bone's depth, so root and tip can want quite different things.
            float pullRoot = data.Pull * PhysBoneSolverMap.SafeEvaluate(data.PullCurve, 0f);
            float pullTip = data.Pull * PhysBoneSolverMap.SafeEvaluate(data.PullCurve, 1f);
            float springRoot = data.Spring * PhysBoneSolverMap.SafeEvaluate(data.SpringCurve, 0f);
            float springTip = data.Spring * PhysBoneSolverMap.SafeEvaluate(data.SpringCurve, 1f);
            float stiffRoot = PhysBoneSolverMap.AsVersion11(
                data.Stiffness * PhysBoneSolverMap.SafeEvaluate(data.StiffnessCurve, 0f), data.IsVersion10);
            float stiffTip = PhysBoneSolverMap.AsVersion11(
                data.Stiffness * PhysBoneSolverMap.SafeEvaluate(data.StiffnessCurve, 1f), data.IsVersion10);

            float dampRoot = PhysBoneSolverMap.Damping(pullRoot, springRoot, stiffRoot, advanced);
            float dampTip = PhysBoneSolverMap.Damping(pullTip, springTip, stiffTip, advanced);
            float dampDerived = Mathf.Max(dampRoot, dampTip);
            dampRoot = Mathf.Max(dampRoot, dampFloor);
            dampTip = Mathf.Max(dampTip, dampFloor);
            PhysBoneSolverMap.MapCurve(dampRoot, dampTip,
                out float dampValue, out float dampStart, out float dampEnd, out bool dampCurve);
            sdata.damping.SetValue(dampValue, dampStart, dampEnd, dampCurve);

            float restRoot = Mathf.Max(restFloorRoot, PhysBoneSolverMap.RestorationStiffness(
                pullRoot, springRoot, stiffRoot, advanced, out bool satRoot));
            float restTip = Mathf.Max(restFloorTip, PhysBoneSolverMap.RestorationStiffness(
                pullTip, springTip, stiffTip, advanced, out bool satTip));
            PhysBoneSolverMap.MapCurve(restRoot, restTip,
                out float restValue, out float restStart, out float restEnd, out bool restCurve);

            sdata.angleRestorationConstraint.useAngleRestoration = restValue > 0.0001f;
            sdata.angleRestorationConstraint.stiffness.SetValue(restValue, restStart, restEnd, restCurve);

            bool dampFloored = dampValue > dampDerived + 0.0001f;
            ctx.Report.Approximated(Category, data.Root.name,
                $"Physics derived from the PhysBone ({(advanced ? "Advanced" : "Simplified")} integration): " +
                $"damping {dampValue:0.###}, angle restoration {restValue:0.###}, from 60 Hz to 90 Hz " +
                $"(the preset had damping {dampFloor:0.###}, restoration {restPreset:0.###})." +
                (dampFloored ? $" Damping held at the preset's; derived it would have been {dampDerived:0.###}." : ""));

            if (satRoot || satTip)
            {
                ctx.Report.Approximated(Category, data.Root.name,
                    $"Pull {data.Pull:0.##} is stiffer than MagicaCloth2 can express: its restoration tops " +
                    "out above a pull of about 0.6. Both settle within a frame at that point, so this should " +
                    "not be visible, but the chain will not get any stiffer than it now is.");
            }

        }

        static void FitToPhysBone(BridgeContext ctx, PhysBoneChainData data, ClothSerializeData sdata,
            bool softBody)
        {
            // VRChat has no wind, so every PhysBone was tuned without
            // it. MagicaCloth2 ships wind influence 1.0 and CVR worlds
            // carry wind zones. A categorical fact about the source,
            // not a number being converted.
            if (sdata.wind.influence > 0f)
            {
                sdata.wind.influence = 0f;
                ctx.Report.Approximated(Category, data.Root.name,
                    "Wind influence 0: VRChat has no wind, so the chain was tuned without it.");
            }

            // VRChat clamps nothing; the author tuned against full
            // movement. MagicaCloth2's spring presets ship a 1 m/s
            // clamp, below walking pace. Raised to MagicaCloth2's own
            // code defaults, not removed: a limit still stops a
            // teleport flinging the chain across the world.
            RaiseSpeedLimit(sdata.inertiaConstraint, "movementSpeedLimit", 5f, ctx, data, "world movement");
            RaiseSpeedLimit(sdata.inertiaConstraint, "localMovementSpeedLimit", 5f, ctx, data, "local movement");
            RaiseSpeedLimit(sdata.inertiaConstraint, "rotationSpeedLimit", 720f, ctx, data, "world rotation");
            RaiseSpeedLimit(sdata.inertiaConstraint, "localRotationSpeedLimit", 720f, ctx, data, "local rotation");
            RaiseSpeedLimit(sdata.inertiaConstraint, "particleSpeedLimit", 4f, ctx, data, "particle");

            // The source's own angle limit is the leash's job. A preset's MagicaCloth2 angle
            // limit bounds chains whose source had none, and it is the constraint that set
            // converted chains vibrating before 3.7.0.
            if (sdata.angleLimitConstraint.useAngleLimit)
            {
                sdata.angleLimitConstraint.useAngleLimit = false;
                ctx.Report.Approximated(Category, data.Root.name,
                    "The preset's own angle limit turned off: the source's limit, if it had one, is the swing bound.");
            }

            if (Mathf.Approximately(data.Gravity, 0f))
            {
                // The author gave this chain no gravity, so it was never meant to hang. Presets
                // carry their own (Long Hair ships 5.0), which would make it fall for the first
                // time in ChilloutVR.
                if (!Mathf.Approximately(sdata.gravity, 0f))
                {
                    sdata.gravity = 0f;
                    ctx.Report.Approximated(Category, data.Root.name,
                        "Gravity set to 0: the source PhysBone had none, so this chain was never " +
                        "meant to hang under its own weight.");
                }
            }
            else if (!softBody)
            {
                float was = sdata.gravity;
                sdata.gravity = ConvertedGravity(data, sdata, out float angle);
                TrySetMember(sdata, "gravityFalloff", Mathf.Clamp01(data.GravityFalloff));
                if (data.Gravity < 0f)
                {
                    sdata.gravityDirection = new Unity.Mathematics.float3(0f, 1f, 0f);
                }
                ctx.Report.Approximated(Category, data.Root.name,
                    $"Gravity {sdata.gravity:0.##} (the preset had {was:0.##}), from the PhysBone's {data.Gravity:0.##} " +
                    $"(version {(data.IsVersion10 ? "1.0" : "1.1")}): held out level, the chain settles about {angle:0}° " +
                    $"{(data.Gravity < 0f ? "up" : "down")} in VRChat, and this is the pull that holds it there against " +
                    $"this chain's restoration. Falloff {GetFloat(sdata, "gravityFalloff"):0.##} carried as it is.");
            }

            // immobile -> inertia influence, the same question with the
            // polarity flipped. Both values move together: split, they ask
            // the chain to hold still and swing at once. Never on a soft
            // body, which cannot be flung anyway.
            // Bone length. A PhysBone bone keeps its length unless Max Squish lets a collider or a
            // grab shorten it, and pull brings it back. MagicaCloth2 holds length through distance
            // stiffness, which also resists some bending, so making every chain rigid swung them
            // measurably less than their PhysBones (Play-mode A/B, 2026-10-06) and was undone. Only
            // a squishing source changes it: softened by its squish, never below 0.2, which would
            // leave a bone held by the tether alone, never firmer than the preset, and the tether's
            // shrink limit opened to match. Soft bodies keep MagicaCloth2's own fixed values.
            if (!softBody && data.MaxSquish > 0f)
            {
                float squish = Mathf.Clamp01(data.MaxSquish);
                float was = sdata.distanceConstraint.stiffness.value;
                float stiffness = Mathf.Min(was, Mathf.Max(0.2f, 1f - squish));
                sdata.distanceConstraint.stiffness.SetValue(stiffness);
                if (squish > sdata.tetherConstraint.distanceCompression)
                {
                    sdata.tetherConstraint.distanceCompression = squish;
                }
                ctx.Report.Approximated(Category, data.Root.name,
                    $"Bones hold their length at stiffness {stiffness:0.##} (the preset had {was:0.##}), so a collider " +
                    $"can shorten them, for the source's Max Squish {squish:0.##}.");
            }

            // Depth inertia holds the bones near the root still while the avatar moves; several
            // presets ship 0.7 to 1.0 of it, and VRChat has nothing like it.
            float depth = GetFloat(sdata.inertiaConstraint, "depthInertia");
            if (!softBody && depth > 0f && TrySetMember(sdata.inertiaConstraint, "depthInertia", 0f))
            {
                ctx.Report.Approximated(Category, data.Root.name,
                    $"The preset's depth inertia ({depth:0.##}) turned off: VRChat has nothing like it.");
            }

            // VRChat applies immobile once per step, against one frame: the chain's parent for All
            // Motion, the world for World. MagicaCloth2's world, local and anchor inertia multiply,
            // so setting all three to 1 - immobile froze an immobile chain (measured in Play mode:
            // 0.1° of swing where the PhysBone had 7°). Each type gets the one control that matches
            // it, and the others stay at 1, MagicaCloth2's full response.
            if (data.Immobile > 0.01f && !softBody)
            {
                float influence = Mathf.Clamp01(1f - data.Immobile);
                TrySetMember(sdata.inertiaConstraint, "localInertia", 1f);
                bool anchored = false;
                if (data.ImmobileTypeName == "AllMotion" && data.Root != null && data.Root.parent != null)
                {
                    var anchor = data.Root.parent;
                    if (TrySetReference(sdata.inertiaConstraint, "anchor", anchor))
                    {
                        anchored = TrySetMember(sdata.inertiaConstraint, "anchorInertia", influence);
                        TrySetMember(sdata.inertiaConstraint, "worldInertia", 1f);
                        ctx.Report.Approximated(Category, data.Root.name,
                            $"Inertia anchored to \"{anchor.name}\" at {influence:0.##}: the source PhysBone was " +
                            $"{data.Immobile:0.##} immobile against its parent (\"All Motion\").");
                    }
                }
                if (!anchored && TrySetMember(sdata.inertiaConstraint, "worldInertia", influence))
                {
                    ctx.Report.Approximated(Category, data.Root.name,
                        $"World inertia {influence:0.##}: the source PhysBone was {data.Immobile:0.##} immobile " +
                        "against the world, and MagicaCloth2 measures the same thing the other way round.");
                }
            }
        }

        static void ApplyMotionLeash(BridgeContext ctx, PhysBoneChainData data, ClothSerializeData sdata)
        {
            if (data.LimitTypeName == "None" || string.IsNullOrEmpty(data.LimitTypeName))
            {
                return;   // the author set no limit, so there is nothing to honour
            }
            // VRChat reads only X for Angle and Hinge, whose Z still holds its default
            // 45; Polar clamps both axes.
            float limitAngle = data.LimitTypeName == "Polar" ? Mathf.Max(data.MaxAngleX, data.MaxAngleZ) : data.MaxAngleX;
            if (limitAngle <= 0f)
            {
                return;
            }
            var segments = SimulatedPath(data);
            if (segments.Sum() <= 0f)
            {
                return;   // single bone with no reach; a leash would only pin it
            }
            var curve = LeashCurve(segments, Mathf.Min(limitAngle, 180f), out float reach);
            if (reach <= 0f)
            {
                return;
            }

            bool applied = TrySetMember(sdata.motionConstraint, "useMaxDistance", true)
                           && TrySetCurve(sdata.motionConstraint, "maxDistance", reach, curve);
            if (applied)
            {
                ctx.Report.Converted(Category, data.Root.name,
                    $"Swing bounded to {reach:0.###} at the tip, converted from the source's {limitAngle:0}° " +
                    $"{data.LimitTypeName} limit at every joint, as a distance bound that cannot vibrate.");
            }
            else
            {
                ctx.Report.Skipped(Category, data.Root.name,
                    $"Source {data.LimitTypeName} limit ({limitAngle:0}°) could not be applied as a movement " +
                    "bound on this MagicaCloth2 version: the chain will swing further here than in VRChat.");
            }
        }

        static void RaiseSpeedLimit(object inertia, string fieldName, float floor,
            BridgeContext ctx, PhysBoneChainData data, string label)
        {
            var field = inertia?.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            var holder = field?.GetValue(inertia);
            if (holder == null)
            {
                return;
            }
            var valueField = holder.GetType().GetField("value", BindingFlags.Public | BindingFlags.Instance);
            if (valueField == null || !(valueField.GetValue(holder) is float current) || current >= floor)
            {
                return;
            }
            valueField.SetValue(holder, floor);
            field.SetValue(inertia, holder);   // struct-safe: write the modified copy back
            ctx.Report.Approximated(Category, data.Root.name,
                $"{label} speed limit raised {current:0.##} → {floor:0.##}, MagicaCloth2's default: the preset's " +
                "sat below walking pace.");
        }

        // MagicaCloth2's gravity is an acceleration that angle restoration holds off: a segment
        // settles where a step's pull, G dt² cos θ, equals what restoration takes back, ρ θ L.
        // So G is solved for the angle the chain settles at in VRChat. MagicaCloth2 also carries
        // a chain's weight down to its root, which VRChat does not; the load factor for that,
        // 3.4 times the cube root of the segment count, was fitted in Play mode against the
        // original PhysBones on five avatars of both versions. A chain with no restoration had
        // no pull in VRChat either, so gravity never reached it there.
        static float ConvertedGravity(PhysBoneChainData data, ClothSerializeData sdata, out float angleDeg)
        {
            var segments = SimulatedPath(data);
            int count = Mathf.Max(1, segments.Count);
            float pull = data.Pull * PhysBoneSolverMap.SafeEvaluate(data.PullCurve, 0.5f);
            float theta = PhysBoneHangAngle(count, Mathf.Clamp(Mathf.Abs(data.Gravity), 0f, 0.99f), pull,
                data.IsVersion10, Mathf.Clamp01(data.GravityFalloff));
            angleDeg = theta * Mathf.Rad2Deg;
            var restoration = sdata.angleRestorationConstraint;
            float inspector = restoration.useAngleRestoration ? restoration.stiffness.Evaluate(0.5f) : 0f;
            float perStep = 1f - Mathf.Pow(1f - Mathf.Clamp01(inspector * PhysBoneSolverMap.RestorationScale),
                PhysBoneSolverMap.RestorationIterations);
            float length = segments.Count > 0 ? segments.Average() : data.Root.TransformVector(data.EndpointPosition).magnitude;
            float load = 3.4f * Mathf.Pow(count, 1f / 3f);
            const float dt = 1f / PhysBoneSolverMap.MagicaHz;
            return Mathf.Clamp(perStep * theta * length / (dt * dt * Mathf.Max(Mathf.Cos(theta), 0.05f) * load), 0f, 20f);
        }

        // Where a straight chain held out level settles in VRChat, measured root to tip. Each bone
        // balances against its already-bent parent: in 1.1 its rest leans toward down by the
        // gravity fraction (SolveChain lerps the pose toward down), in 1.0 gravity is a force that
        // pull holds off. Falloff weakens a bone's gravity as the bone itself turns toward down.
        static float PhysBoneHangAngle(int bones, float gravity, float pull, bool version10, float falloff)
        {
            float phi = 0f, x = 0f, y = 0f;
            for (int i = 0; i < bones; i++)
            {
                float lo = phi, hi = Mathf.PI / 2f;
                for (int k = 0; k < 30; k++)
                {
                    float mid = (lo + hi) * 0.5f;
                    float g = gravity * ((1f - falloff) + falloff * (1f - Mathf.Sin(mid)));
                    float want = version10
                        ? phi + Mathf.Atan(g * Mathf.Cos(mid) / Mathf.Max(pull, 0.01f))
                        : Mathf.Atan2((1f - g) * Mathf.Sin(phi) + g, (1f - g) * Mathf.Cos(phi));
                    if (want > mid)
                    {
                        lo = mid;
                    }
                    else
                    {
                        hi = mid;
                    }
                }
                phi = (lo + hi) * 0.5f;
                x += Mathf.Cos(phi);
                y += Mathf.Sin(phi);
            }
            return Mathf.Atan2(y, x);
        }

        static bool IgnoresBranching(PhysBoneChainData data) =>
            data.RootHasMultipleChildren && data.MultiChildTypeName == "Ignore";

        // The segments VRChat actually bends: from the root, or, when Multi Child Type Ignore
        // leaves a branching root still, from the longest branch's first bone.
        static List<float> SimulatedPath(PhysBoneChainData data)
        {
            if (!IgnoresBranching(data))
            {
                return LongestPath(data.Root, data.Ignores);
            }
            var best = new List<float>();
            for (int i = 0; i < data.Root.childCount; i++)
            {
                var child = data.Root.GetChild(i);
                if (data.Ignores.Contains(child))
                {
                    continue;
                }
                var path = LongestPath(child, data.Ignores);
                if (path.Sum() > best.Sum())
                {
                    best = path;
                }
            }
            return best;
        }

        // Segment lengths along the longest branch below root, ignored bones left out.
        static List<float> LongestPath(Transform root, List<Transform> ignores)
        {
            var best = new List<float>();
            if (root == null)
            {
                return best;
            }
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (ignores.Contains(child))
                {
                    continue;
                }
                var branch = LongestPath(child, ignores);
                branch.Insert(0, Vector3.Distance(root.position, child.position));
                if (branch.Sum() > best.Sum())
                {
                    best = branch;
                }
            }
            return best;
        }

        // VRChat limits every joint against its parent, so the reach a limit allows grows
        // faster than linearly down a chain: each joint adds its own bend. A point's bound is
        // the farthest it gets with every joint above it bent the same way, at any bend up to
        // the limit. MagicaCloth2 squares depth before it reads this curve (MotionConstraint,
        // "depth = depth * depth"), so the curve is written against depth squared; a straight
        // curve there pinned the base and middle of every limited chain.
        static AnimationCurve LeashCurve(List<float> segments, float limitDeg, out float reach)
        {
            int n = segments.Count;
            float total = segments.Sum();
            var atJoint = new float[n + 1];
            for (int step = 1; step <= 8; step++)
            {
                float bend = limitDeg * step / 8f * Mathf.Deg2Rad;
                var bent = Vector2.zero;
                float along = 0f;
                for (int i = 0; i < n; i++)
                {
                    bent += segments[i] * new Vector2(Mathf.Cos((i + 1) * bend), Mathf.Sin((i + 1) * bend));
                    along += segments[i];
                    atJoint[i + 1] = Mathf.Max(atJoint[i + 1], Vector2.Distance(bent, new Vector2(along, 0f)));
                }
            }
            reach = atJoint.Max();
            var curve = new AnimationCurve();
            if (reach <= 0f)
            {
                return curve;
            }
            for (int k = 0; k <= 16; k++)
            {
                float x = k / 16f;
                float s = Mathf.Sqrt(x) * total, start = 0f, r = atJoint[n];
                for (int i = 0; i < n; i++)
                {
                    if (s <= start + segments[i] || i == n - 1)
                    {
                        float u = segments[i] > 0f ? Mathf.Clamp01((s - start) / segments[i]) : 1f;
                        r = Mathf.Lerp(atJoint[i], atJoint[i + 1], u);
                        break;
                    }
                    start += segments[i];
                }
                curve.AddKey(new Keyframe(x, r / reach));
            }
            for (int k = 0; k < curve.length; k++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
            }
            return curve;
        }

        static ColliderComponent GetOrCreateCollider(BridgeContext ctx, VRCPhysBoneCollider pbCollider,
            Dictionary<VRCPhysBoneCollider, ColliderComponent> cache)
        {
            if (cache.TryGetValue(pbCollider, out var cached))
            {
                return cached;
            }

            Transform parent = pbCollider.rootTransform != null ? pbCollider.rootTransform : pbCollider.transform;
            string shape = pbCollider.shapeType.ToString();

            if (pbCollider.insideBounds)
            {
                ctx.Report.Skipped("PhysBone colliders", PathOf(pbCollider.transform),
                    "'Inside bounds' colliders have no MagicaCloth2 equivalent.");
                cache[pbCollider] = null;
                return null;
            }

            // Sibling-unique: two colliders on one bone would share an animation path.
            var go = new GameObject(PhysBoneConverter.UniqueChildName(parent, "MagicaCollider_" + parent.name));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pbCollider.position;
            go.transform.localRotation = pbCollider.rotation;

            ColliderComponent collider;
            string fitted = null;
            // ChilloutVR gives each hand its own MagicaCloth2 capsule, the one that pushes other
            // people's cloth, but only when the hand carries no capsule of its own; one on a finger
            // also gets filtered out as not being a hand. So a hand or finger collider becomes a
            // sphere, at the capsule's middle, as wide as its radius and a little of its length.
            bool onHand = OnHand(ctx, parent);
            if (shape.Contains("Capsule") && onHand)
            {
                var sphere = go.AddComponent<MagicaSphereCollider>();
                float half = Mathf.Max(pbCollider.height * 0.5f, pbCollider.radius);
                float radius = Mathf.Lerp(pbCollider.radius, half, 0.5f);
                sphere.SetSize(radius);
                collider = sphere;
                fitted = $"made a sphere of radius {radius:0.###}: a capsule on a hand would stop ChilloutVR adding " +
                         "the hand capsule that pushes other people's cloth";
            }
            else if (shape.Contains("Capsule"))
            {
                var capsule = go.AddComponent<MagicaCapsuleCollider>();
                capsule.direction = MagicaCapsuleCollider.Direction.Y; // PB capsules extend along local Y
                float authorLength = Mathf.Max(pbCollider.height, pbCollider.radius * 2f);
                float startRadius = pbCollider.radius, endRadius = pbCollider.radius, length = authorLength;
                if (ctx.Settings.fitCollidersToMesh
                    && MeasureColliderFit(ctx, parent, go.transform, pbCollider.radius, authorLength,
                        out startRadius, out endRadius, out length, out float offset, out int sampled))
                {
                    fitted = $"fitted to the mesh from {sampled} vertices: radius " +
                        $"{pbCollider.radius:0.###} -> {startRadius:0.###} at one end and " +
                        $"{endRadius:0.###} at the other, length {authorLength:0.###} -> {length:0.###}";
                    // Slide it along its own axis onto the middle of what it measured, since the
                    // capsule is centred on this object. Done AFTER measuring, so the radii and
                    // the length stay the ones taken in the space they were measured in.
                    if (Mathf.Abs(offset) > 1e-4f)
                    {
                        go.transform.localPosition += pbCollider.rotation * (Vector3.up * offset);
                        fitted += $", slid {offset:0.###} along its axis onto the middle of it";
                    }
                }
                // SetSize turns on the capsule's own Start/End radius split whenever the
                // two differ, so a tapered capsule arrives shaped rather than needing the checkbox.
                capsule.SetSize(startRadius, endRadius, length);
                collider = capsule;
            }
            else if (shape.Contains("Plane"))
            {
                collider = go.AddComponent<MagicaPlaneCollider>();
            }
            else
            {
                var sphere = go.AddComponent<MagicaSphereCollider>();
                float radius = pbCollider.radius;
                // A sphere has no axis to taper along, so the same measurement gives it one
                // number: the narrower of the two ends, which is the half-width it fits inside.
                // Position is left alone here: a sphere has no axis to slide along, and moving it
                // in three dimensions would be repositioning the author's collider rather than
                // sizing it.
                if (ctx.Settings.fitCollidersToMesh
                    && MeasureColliderFit(ctx, parent, go.transform, pbCollider.radius,
                        pbCollider.radius * 2f, out float a, out float b, out _, out _, out int sampled))
                {
                    radius = Mathf.Min(a, b);
                    fitted = $"fitted to the mesh from {sampled} vertices: radius " +
                        $"{pbCollider.radius:0.###} -> {radius:0.###}";
                }
                sphere.SetSize(radius);
                collider = sphere;
            }

            ctx.Report.Converted("PhysBone colliders", PathOf(pbCollider.transform),
                fitted == null
                    ? shape + " -> Magica collider"
                    : shape + " -> Magica collider, " + fitted);
            PhysBoneConverter.RecordColliderHost(ctx, pbCollider, go);
            cache[pbCollider] = collider;
            return collider;
        }

        static bool OnHand(BridgeContext ctx, Transform t)
        {
            var animator = ctx.TargetAnimator;
            if (animator == null || !animator.isHuman || t == null)
            {
                return false;
            }
            foreach (var bone in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
            {
                var hand = animator.GetBoneTransform(bone);
                if (hand != null && (t == hand || t.IsChildOf(hand)))
                {
                    return true;
                }
            }
            return false;
        }

        const int MeshSampleTarget = 200000;

        const int MinMeshSamples = 12;

        const float MinBoneWeight = 0.2f;

        static float MeasureMesh(BridgeContext ctx, PhysBoneChainData data, out int sampled,
            out HashSet<Transform> meshBones, out bool grown)
        {
            grown = false;
            float saved = MeasureMeshAt(ctx, data, out sampled, out meshBones, false);
            if (!ctx.Settings.sizePhysicsForLargest || MeshGrowth.Reach(ctx).Count == 0)
            {
                return saved;
            }
            // Measured again with every animated shape at full reach,
            // keeping the larger. That catches a growth slider without
            // deciding per shape which way it goes.
            float atReach = MeasureMeshAt(ctx, data, out _, out _, true);
            grown = atReach > saved;
            return Mathf.Max(saved, atReach);
        }

        static float MeasureMeshAt(BridgeContext ctx, PhysBoneChainData data, out int sampled,
            out HashSet<Transform> meshBones, bool atReach)
        {
            sampled = 0;
            meshBones = new HashSet<Transform>();
            var chain = new HashSet<Transform>();
            CollectChainBones(data.Root, data.Ignores, chain);
            if (chain.Count == 0)
            {
                return 0f;
            }

            var distances = new List<float>();
            var flat = new Dictionary<Transform, List<Vector2>>();
            // In the order the meshes first name each bone: ChooseCollisionBones breaks a tie
            // between two bones by the order meshBones gives them.
            foreach (var entry in BindLocalVertices(ctx, atReach))
            {
                if (chain.Contains(entry.Key))
                {
                    AddRadiusSamples(distances, flat, meshBones, entry.Key, entry.Value);
                }
            }

            sampled = distances.Count;
            if (distances.Count < MinMeshSamples)
            {
                return 0f;
            }
            distances.Sort();

            // A particle is a sphere, bounded by the narrowest way
            // across the mesh, not the average distance out. On a flat
            // panel the median reads half-width where half-thickness
            // is wanted.
            var perBone = new List<float>();
            foreach (var section in flat.Values)
            {
                float caliper = MinimumCaliperRadius(section);
                if (caliper < float.MaxValue)
                {
                    perBone.Add(caliper);
                }
            }
            if (perBone.Count == 0)
            {
                return distances[distances.Count / 2];
            }
            perBone.Sort();
            return Mathf.Min(distances[distances.Count / 2], perBone[perBone.Count / 2]);
        }

        static bool MeasureColliderFit(BridgeContext ctx, Transform host, Transform colliderObject,
            float authorRadius, float authorLength,
            out float startRadius, out float endRadius, out float length, out float offset,
            out int sampled)
        {
            startRadius = endRadius = authorRadius;
            length = authorLength;
            offset = 0f;
            sampled = 0;
            if (host == null || colliderObject == null)
            {
                return false;
            }

            // Only the bone the collider hangs on. Walking into its children would pull the
            // forearm into an upper-arm collider and read the whole limb as one shape.
            if (!BoneVertices(ctx).TryGetValue(host, out var world))
            {
                return false;
            }

            // World scale is divided out here and multiplied back by the solver (ColliderManager
            // scales size by the collider transform's own scale), which is the same footing the
            // author's radius is written on.
            var toLocal = colliderObject.worldToLocalMatrix;
            var points = new List<Vector3>(world.Count);
            foreach (var w in world)
            {
                points.Add(toLocal.MultiplyPoint3x4(w));
            }

            sampled = points.Count;
            if (points.Count < MinMeshSamples * 4)
            {
                return false;   // too little of this bone's flesh to say anything about its shape
            }

            // Capsules are written along local Y (see the caller). Ends are taken at the 2nd and
            // 98th percentile rather than the extremes, so one stray vertex weighted to a distant
            // part of the body cannot stretch the capsule to reach it.
            var along = new List<float>(points.Count);
            foreach (var p in points)
            {
                along.Add(p.y);
            }
            along.Sort();
            float low = along[Mathf.Clamp(Mathf.RoundToInt(along.Count * 0.02f), 0, along.Count - 1)];
            float high = along[Mathf.Clamp(Mathf.RoundToInt(along.Count * 0.98f), 0, along.Count - 1)];
            float span = high - low;
            if (span <= 0f)
            {
                return false;
            }

            // A thin station at each end, widened only as far as it
            // must be to have something to measure. A wide slab pools
            // the taper and neighbouring mass into the reading.
            float measuredStart = MinimumCaliperRadius(Station(points, high, low, span, true));
            float measuredEnd = MinimumCaliperRadius(Station(points, high, low, span, false));
            if (measuredStart == float.MaxValue || measuredEnd == float.MaxValue)
            {
                return false;
            }

            startRadius = measuredStart;
            endRadius = measuredEnd;
            length = span;
            // Where the flesh's middle sits relative to the collider's own origin. A capsule set
            // "aligned on center" grows symmetrically about that origin, so a length taken from
            // the mesh without this would push one end past the limb and leave the other short of
            // it. Zero whenever the author already centred the collider on what it covers.
            offset = (low + high) * 0.5f;
            return true;
        }

        static List<Vector2> Station(List<Vector3> points, float high, float low, float span, bool topEnd)
        {
            List<Vector2> slab = null;
            foreach (float fraction in StationFractions)
            {
                float edge = span * fraction;
                slab = new List<Vector2>();
                foreach (var p in points)
                {
                    if (topEnd ? p.y >= high - edge : p.y <= low + edge)
                    {
                        slab.Add(new Vector2(p.x, p.z));
                    }
                }
                if (slab.Count >= MinMeshSamples * 2)
                {
                    break;
                }
            }
            return slab;
        }

        static readonly float[] StationFractions = { 0.1f, 0.18f, 0.26f, 0.34f };

        static BridgeContext boneVertexOwner;
        static Dictionary<Transform, List<Vector3>> boneVertexCache;

        // The saved samples in world space. The bind pose put each vertex in its bone's own
        // space; that bone's current matrix puts it back where it is skinned to.
        static Dictionary<Transform, List<Vector3>> BoneVertices(BridgeContext ctx)
        {
            if (ReferenceEquals(boneVertexOwner, ctx) && boneVertexCache != null)
            {
                return boneVertexCache;
            }
            var byBone = new Dictionary<Transform, List<Vector3>>();
            foreach (var entry in BindLocalVertices(ctx, false))
            {
                if (entry.Key == null)
                {
                    continue;   // destroyed since the meshes were read
                }
                var toWorld = entry.Key.localToWorldMatrix;
                var world = new List<Vector3>(entry.Value.Count);
                foreach (var local in entry.Value)
                {
                    world.Add(toWorld.MultiplyPoint3x4(local));
                }
                byBone[entry.Key] = world;
            }
            boneVertexOwner = ctx;
            boneVertexCache = byBone;
            return byBone;
        }

        static BridgeContext bindLocalOwner;
        static SkinnedMeshRenderer[] bindLocalRenderers;
        static Dictionary<Transform, List<Vector3>>[] bindLocalCache;   // [0] saved, [1] at full reach

        // Per bone, every vertex weighted to it at MinBoneWeight or more, in that bone's own
        // space by its bind pose. Read from the meshes once per conversion: every chain used
        // to walk every vertex of every mesh, up to four times, for its own few bones.
        static Dictionary<Transform, List<Vector3>> BindLocalVertices(BridgeContext ctx, bool atReach)
        {
            // Read again when a pass since has added or removed a mesh, as walking afresh did.
            var renderers = ctx.Target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (!ReferenceEquals(bindLocalOwner, ctx) || bindLocalRenderers == null
                || !renderers.SequenceEqual(bindLocalRenderers))
            {
                bindLocalOwner = ctx;
                bindLocalRenderers = renderers;
                bindLocalCache = new Dictionary<Transform, List<Vector3>>[2];
            }
            int slot = atReach ? 1 : 0;
            if (bindLocalCache[slot] != null)
            {
                return bindLocalCache[slot];
            }

            var byBone = new Dictionary<Transform, List<Vector3>>();
            foreach (var renderer in renderers)
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }
                Vector3[] vertices;
                BoneWeight[] weights;
                Matrix4x4[] binds;
                try
                {
                    // As the avatar is actually worn: base mesh plus whatever blendshape weights
                    // the renderer carries. Measuring mesh.vertices sizes physics to a silhouette
                    // nobody sees whenever a body slider is shipped part-way up.
                    vertices = MeshGrowth.Deformed(ctx, renderer, mesh, atReach);
                    weights = mesh.boneWeights;
                    binds = mesh.bindposes;
                }
                catch
                {
                    continue;   // unreadable mesh, nothing to measure, and not worth failing over
                }
                var bones = renderer.bones;
                if (bones == null || vertices.Length == 0 || weights.Length != vertices.Length)
                {
                    continue;
                }

                // One sample in N on a dense mesh: a 60k-vertex body does not need every vertex
                // to say how thick a breast is.
                int stride = Mathf.Max(1, vertices.Length / MeshSampleTarget);
                for (int i = 0; i < vertices.Length; i += stride)
                {
                    var w = weights[i];
                    AddBindLocal(byBone, vertices[i], w.boneIndex0, w.weight0, bones, binds);
                    AddBindLocal(byBone, vertices[i], w.boneIndex1, w.weight1, bones, binds);
                    AddBindLocal(byBone, vertices[i], w.boneIndex2, w.weight2, bones, binds);
                    AddBindLocal(byBone, vertices[i], w.boneIndex3, w.weight3, bones, binds);
                }
            }
            bindLocalCache[slot] = byBone;
            return byBone;
        }

        static void AddBindLocal(Dictionary<Transform, List<Vector3>> byBone, Vector3 vertex,
            int boneIndex, float weight, Transform[] bones, Matrix4x4[] binds)
        {
            if (weight < MinBoneWeight || boneIndex < 0
                || boneIndex >= bones.Length || boneIndex >= binds.Length)
            {
                return;
            }
            var bone = bones[boneIndex];
            if (bone == null)
            {
                return;
            }
            if (!byBone.TryGetValue(bone, out var list))
            {
                byBone[bone] = list = new List<Vector3>();
            }
            list.Add(binds[boneIndex].MultiplyPoint3x4(vertex));
        }

        // Drops every per-conversion cache in this file; each is rebuilt on its next use.
        internal static void ReleaseCaches()
        {
            boneVertexOwner = bindLocalOwner = null;
            boneVertexCache = null;
            bindLocalRenderers = null;
            bindLocalCache = null;
        }

        static float MinimumCaliperRadius(List<Vector2> section)
        {
            if (section.Count < MinMeshSamples)
            {
                return float.MaxValue;   // nothing to bound with; the median stands
            }
            float narrowest = float.MaxValue;
            const int directions = 16;
            for (int d = 0; d < directions; d++)
            {
                float angle = Mathf.PI * d / directions;
                var axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float min = float.MaxValue, max = float.MinValue;
                foreach (var p in section)
                {
                    float projected = Vector2.Dot(p, axis);
                    if (projected < min) min = projected;
                    if (projected > max) max = projected;
                }
                float width = max - min;
                if (width < narrowest)
                {
                    narrowest = width;
                }
            }
            return narrowest * 0.5f;
        }

        // One chain bone's samples. Its axis and scale are read live, never cached with the
        // vertices: the axis is the first child, and the writers hang "_End" tips and
        // colliders under bones as they go.
        static void AddRadiusSamples(List<float> into, Dictionary<Transform, List<Vector2>> flat,
            HashSet<Transform> meshBones, Transform bone, List<Vector3> bindLocal)
        {
            Vector3 axis = bone.childCount > 0 ? bone.GetChild(0).localPosition : Vector3.zero;
            bool hasAxis = axis.sqrMagnitude > 1e-10f;
            Vector3 a = hasAxis ? axis.normalized : Vector3.up;

            // Bone-local units become world units, which is what MagicaCloth2's radius is in.
            Vector3 scale = bone.lossyScale;
            float mean = (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f;
            if (!(mean > 0f))
            {
                return;
            }

            // The same offset in the plane across the bone, kept PER BONE so the
            // cross-section's narrowest width can be measured. Per bone matters: each one
            // has its own frame, and pooling a panel whose bones fan out smears the section
            // into a cloud wider than any single bone's, which is most of what a flat mesh
            // needed measuring for. See MinimumCaliperRadius.
            Vector3 e1 = Vector3.Cross(a, Mathf.Abs(a.x) < 0.9f ? Vector3.right : Vector3.forward).normalized;
            Vector3 e2 = Vector3.Cross(a, e1).normalized;
            List<Vector2> section = null;
            foreach (var local in bindLocal)
            {
                // In the bone's own space, so this does not move when the avatar does.
                Vector3 perpendicular = hasAxis ? Vector3.ProjectOnPlane(local, a) : local;
                float distance = perpendicular.magnitude;
                if (!(distance > 0f))
                {
                    continue;
                }
                into.Add(distance * mean);
                meshBones.Add(bone);
                if (section == null)
                {
                    flat[bone] = section = new List<Vector2>();
                }
                section.Add(new Vector2(Vector3.Dot(perpendicular, e1), Vector3.Dot(perpendicular, e2)) * mean);
            }
        }

        static void CollectChainBones(Transform root, List<Transform> ignores, HashSet<Transform> into)
        {
            if (root == null || (ignores != null && ignores.Contains(root)))
            {
                return;
            }
            into.Add(root);
            for (int i = 0; i < root.childCount; i++)
            {
                CollectChainBones(root.GetChild(i), ignores, into);
            }
        }

        const float StandInRestoration = 1f;

        static readonly HashSet<string> SoftBodyClasses = new HashSet<string>
        {
            "Breast", "Butt", "Belly", "Thigh",
        };

        const float SoftBodySpringPower = 0.06f;

        static void ConfigureSoftBody(BridgeContext ctx, PhysBoneChainData data,
            ClothSerializeData sdata, string chainClass)
        {
            bool spring = TrySetMember(sdata.springConstraint, "useSpring", true);
            float presetPower = 0f;
            var powerField = sdata.springConstraint?.GetType()
                .GetField("springPower", BindingFlags.Public | BindingFlags.Instance);
            if (powerField != null && powerField.GetValue(sdata.springConstraint) is float existing)
            {
                presetPower = existing;
            }
            // The floor applies to a dropped-in preset too: it shares the shipped file's name, so the
            // two cannot be told apart, and letting the shipped Breast preset's 0.01 through made
            // soft bodies six times floppier.
            float power = Mathf.Max(presetPower, SoftBodySpringPower);
            spring &= TrySetMember(sdata.springConstraint, "springPower", power);

            var collisionBones = ChooseCollisionBones(ctx, data, out float collisionRadius);
            // A stand-in collides at its root as well, which slides: a push then moves the whole
            // body, as the add-on did, instead of turning it about a fixed root.
            if (data.StandIn && collisionBones.Count > 0 && !collisionBones.Contains(data.Root))
            {
                collisionBones.Insert(0, data.Root);
            }
            bool collision = false, sized = false;
            if (collisionBones.Count > 0)
            {
                var list = sdata.colliderCollisionConstraint?.GetType()
                    .GetField("collisionBones", BindingFlags.Public | BindingFlags.Instance);
                if (list != null && list.GetValue(sdata.colliderCollisionConstraint) is List<Transform> bones)
                {
                    bones.Clear();
                    bones.AddRange(collisionBones);
                    collision = true;
                }
                // This runs after "Size particles from the mesh" and the radius
                // cap, so it honours both itself. Writing past them silently
                // undid the user's choice and the cap's "reduced to" line.
                if (collision && collisionRadius > 0f && ctx.Settings.fitRadiusToMesh)
                {
                    float spacing = ctx.Settings.capParticleRadius ? MeasureBoneSpacing(data.Root) : 0f;
                    if (spacing > 0f)
                    {
                        collisionRadius = Mathf.Min(collisionRadius, spacing * 0.5f);
                    }
                    sdata.radius.value = collisionRadius;   // direct, keeping any depth curve
                    sized = true;
                }
            }
            // A Bone Spring lets its root slide, which a PhysBone never does: the preset's 5 cm moved
            // a breast's root by a third of its length on a walk, dragging straps and jewellery
            // weighted to it off the body, and a cap at a quarter of its size still left a tenth.
            // At 0 the jiggle stays in the tip, and the swing matched the original as well or
            // better (Play-mode A/B, 2026-10-07).
            // A stand-in for an add-on is the opposite case: marshmallow PB moves the bone itself,
            // about its own length under a squeeze, and bends it 6 to 8 degrees where this cloth
            // bent 38. So its root keeps the preset's slide and its bending is held near rest.
            bool pinned = !data.StandIn && GetFloat(sdata.springConstraint, "limitDistance") is float range && range > 0f
                && TrySetMember(sdata.springConstraint, "limitDistance", 0f);
            if (data.StandIn)
            {
                // As far as the body is long: the add-on moved the bone about its own length.
                float reach = SimulatedPath(data).Sum();
                if (reach > 0f && GetFloat(sdata.springConstraint, "limitDistance") is float preset && reach > preset)
                {
                    TrySetMember(sdata.springConstraint, "limitDistance", reach);
                }
                sdata.angleRestorationConstraint.useAngleRestoration = true;
                sdata.angleRestorationConstraint.stiffness.SetValue(StandInRestoration);
            }

            string collisionBone = collisionBones.Count > 0
                ? string.Join("\", \"", collisionBones.Select(b => b.name))
                : null;

            ctx.Report.Converted(Category, data.Root.name,
                $"Built as a SOFT BODY (\"{chainClass}\"), held near rest by a spring" +
                (spring ? $" (power {power:0.###})" : " (spring settings unavailable on this MagicaCloth2 version)") +
                (collision
                    ? $", and only \"{collisionBone}\" ({collisionBones.Count} of {data.Root.childCount} " +
                      "branch(es)) is offered for collision" +
                      (sized ? $", sized {collisionRadius:0.###} from the mesh" : "")
                    : ", though its collision bone could not be set on this MagicaCloth2 version") +
                (pinned ? ". Its root stays put, as a PhysBone's does, so straps weighted to it stay on" : "") +
                (data.StandIn ? ". It stands in for an add-on that slid the bone rather than swinging it, so its root " +
                    "may slide and its bending is held near rest, which reads as squish rather than swing" : "") +
                ". Inertia stays at the preset's value.");
        }

        static List<Transform> ChooseCollisionBones(BridgeContext ctx, PhysBoneChainData data, out float radius)
        {
            radius = 0f;
            var chosen = new List<Transform>();
            var chain = new HashSet<Transform>();
            CollectChainBones(data.Root, data.Ignores, chain);
            if (chain.Count == 0)
            {
                return chosen;
            }

            // The middle of the MESH, not the middle of the bones. A chain's bones are spaced
            // along its length while the mesh they carry is a lump somewhere on it, so the two
            // midpoints are different places and the difference picks a different bone.
            radius = MeasureMesh(ctx, data, out int samples, out HashSet<Transform> meshBones, out _);
            if (samples < MinMeshSamples || meshBones.Count == 0)
            {
                return chosen;   // nothing measurable; better no collision bone than a guessed one
            }

            var volumeRadii = new List<float>();

            // One per branch, not one per chain. A single root often
            // carries a mirrored pair, and one bone for the whole mesh
            // leaves half the body without collision. Branches are the
            // root's own children.
            var branches = new List<List<Transform>>();
            for (int i = 0; i < data.Root.childCount; i++)
            {
                var child = data.Root.GetChild(i);
                var members = meshBones.Where(b => b == child || b.IsChildOf(child)).ToList();
                if (members.Count > 0)
                {
                    branches.Add(members);
                }
            }
            // A root that carries mesh itself and branches nowhere useful still needs a bone.
            if (branches.Count == 0)
            {
                branches.Add(meshBones.ToList());
            }

            // Each branch measures against the middle of the mesh it
            // carries, weighted by vertex count. Averaging bone positions
            // ties on two bones and leaves floating point to choose.
            var vertices = BoneVertices(ctx);
            foreach (var branch in branches)
            {
                var boneCentre = new Dictionary<Transform, Vector3>();
                var boneWeight = new Dictionary<Transform, int>();
                foreach (var bone in branch)
                {
                    Vector3 sum = Vector3.zero;
                    int n = 0;
                    if (vertices.TryGetValue(bone, out var points))
                    {
                        foreach (var p in points)
                        {
                            sum += p;
                            n++;
                        }
                    }
                    // A bone with no vertices of its own still has to sit somewhere for the
                    // comparison below; it just brings no weight to the middle.
                    boneCentre[bone] = n > 0 ? sum / n : bone.position;
                    boneWeight[bone] = n;
                }

                Vector3 branchCentre = Vector3.zero;
                int total = 0;
                foreach (var bone in branch)
                {
                    branchCentre += boneCentre[bone] * boneWeight[bone];
                    total += boneWeight[bone];
                }
                if (total > 0)
                {
                    branchCentre /= total;
                }
                else
                {
                    foreach (var bone in branch)
                    {
                        branchCentre += bone.position;
                    }
                    branchCentre /= branch.Count;
                }

                // Judged on the bone's pivot, where MagicaCloth2 puts
                // the collision sphere, not on its vertex average.
                // The bone standing in the middle of the volume wins.
                Transform best = null;
                float bestDistance = float.MaxValue;
                foreach (var bone in branch)
                {
                    float d = Vector3.SqrMagnitude(bone.position - branchCentre);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = bone;
                    }
                }
                if (best != null && !chosen.Contains(best))
                {
                    chosen.Add(best);
                    // How far the mesh it carries reaches from that pivot: the size of the volume,
                    // which is what a soft body collides as. The chain measurement above takes the
                    // narrowest cross-section, half a panel's thickness, and on a breast whose
                    // bones carry the front skin that came out a crescent 2 mm thick.
                    var reach = new List<float>();
                    foreach (var bone in branch)
                    {
                        if (vertices.TryGetValue(bone, out var points))
                        {
                            foreach (var p in points)
                            {
                                reach.Add(Vector3.Distance(p, best.position));
                            }
                        }
                    }
                    if (reach.Count >= MinMeshSamples)
                    {
                        reach.Sort();
                        volumeRadii.Add(reach[reach.Count / 2]);
                    }
                }
            }

            if (volumeRadii.Count > 0)
            {
                volumeRadii.Sort();
                radius = volumeRadii[0];
            }
            return chosen;
        }

        static float MeasureBoneSpacing(Transform root)
        {
            float total = 0f;
            int steps = 0;
            var current = root;
            while (current != null && current.childCount > 0 && steps < 8)
            {
                var child = current.GetChild(0);
                float step = Vector3.Distance(current.position, child.position);
                if (step > 0.0001f)
                {
                    total += step;
                    steps++;
                }
                current = child;
            }
            return steps > 0 ? total / steps : 0f;
        }

        // MagicaCloth2 constraint layouts differ slightly across versions; reflection keeps
        // this compiling everywhere and degrades to a report entry instead of an error.
        static bool TrySetReference(object target, string fieldName, UnityEngine.Object value)
        {
            if (target == null)
            {
                return false;
            }
            var field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (field == null || (value != null && !field.FieldType.IsInstanceOfType(value)))
            {
                return false;
            }
            try
            {
                field.SetValue(target, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Read by name: fields MagicaCloth2 added in later versions must not break the build on older ones.
        static float GetFloat(object target, string fieldName) =>
            target?.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target) is float f ? f : 0f;

        static bool TrySetMember(object target, string fieldName, object value)
        {
            if (target == null)
            {
                return false;
            }
            var field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
            {
                return false;
            }
            try
            {
                field.SetValue(target, System.Convert.ChangeType(value, field.FieldType));
                return true;
            }
            catch
            {
                return false;
            }
        }

        static bool TrySetCurveValue(object target, string fieldName, float value,
            float curveStart, float curveEnd)
        {
            if (target == null)
            {
                return false;
            }
            var field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (field == null || !(field.GetValue(target) is CurveSerializeData curveData))
            {
                return false;
            }
            curveData.SetValue(value, curveStart, curveEnd);
            return true;
        }

        static bool TrySetCurve(object target, string fieldName, float value, AnimationCurve curve)
        {
            var field = target?.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (field == null || !(field.GetValue(target) is CurveSerializeData curveData))
            {
                return false;
            }
            curveData.SetValue(value, curve);
            return true;
        }

        static string PathOf(Transform t) => t != null ? t.name : "(null)";
    }
}
#endif
