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
            // The holder goes under the PhysBone component's own parent, never
            // loose at the root. Placed before any clip is aimed at it.
            var home = HolderHome(ctx, data);
            // Sibling-unique, because animation paths address children by name: an avatar with
            // four hairstyles produces several chains rooted at a bone called "Hair_root", and
            // two holders both named "MagicaCloth_Hair_root" mean every animation curve aimed at
            // one of them resolves to whichever Unity finds first.
            holderName = UniqueChildName(home, holderName);
            var holder = new GameObject(holderName);
            holder.transform.SetParent(home, false);
            var cloth = holder.AddComponent<MagicaCloth>();

            // Where "off" lives. A holder inside an inactive object rides
            // that object's toggle, so its component stays enabled unless
            // the source component itself was disabled. A holder that is
            // active while its source was not carries off on the component,
            // and the animator pass writes the switch onto that flag.
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
                    ? "Style was inactive at conversion; cloth created disabled (the component is " +
                      "off, its object stays active so a toggle can switch it on). Its toggle is " +
                      "re-wired to activate this cloth — see the Animator section of this report."
                    : "Source PhysBone was disabled; cloth created disabled (the component is off, " +
                      "its object stays active so a toggle can switch it on — a component enabled " +
                      "on an inactive object never runs). Animator toggles that activated the " +
                      "original object are re-wired to activate this cloth too — see the Animator " +
                      "section of this report.");
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
                        $"No preset applied for chain class \"{cls.Name}\" — {presetError}. Using " +
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

            // Multi Child Type "Ignore" pins a branching root; only the
            // branches simulate. rootRotation 0 reproduces the pin.
            // Otherwise 1.0 ("child-based"): PhysBone rotates the root
            // to follow its children, and 0.5 would leave every chain
            // one joint stiffer at the base. Presets never carry
            // rootRotation, so this survives either apply order.
            if (data.RootHasMultipleChildren && data.MultiChildTypeName == "Ignore")
            {
                sdata.rootRotation = 0f;
            }
            else
            {
                sdata.rootRotation = 1f;
            }

            ctx.Report.Converted(Category, data.Root.name,
                data.RootHasMultipleChildren && data.MultiChildTypeName == "Ignore"
                    ? "Root held still (rotation 0) — the source's Multi Child Type is Ignore, which pins a "
                      + "branching root in VRChat and simulates only the branches below it."
                    : "Root turns with the chain (rotation 1) — VRChat simulates a PhysBone's root bone, "
                      + "rotating it to follow the children it integrates, so the chain bends at its FIRST "
                      + "joint. MagicaCloth2 holds root bones still and would give this one half the "
                      + "rotation its children ask for, leaving the chain a joint stiffer at the base than "
                      + "the original. Lower Root Rotation on the cloth if the base moves more than you want.");

            if (data.HumanoidExclusions.Count > 0)
            {
                ctx.Report.Approximated(Category, data.Root.name,
                    $"{data.HumanoidExclusions.Count} humanoid-mapped bone(s) excluded from simulation — " +
                    $"{string.Join(", ", data.HumanoidExclusions.Take(4).Select(t => t.name))}" +
                    $"{(data.HumanoidExclusions.Count > 4 ? ", …" : "")}. The animator and IK drive " +
                    "humanoid bones every frame (locomotion curls toes, IK plants feet), so simulating " +
                    "one fights them for the transform. The cloth roots around these; their non-humanoid " +
                    "children still simulate.");
            }

            if (data.ToeExclusions.Count > 0)
            {
                ctx.Report.Approximated(Category, data.Root.name,
                    $"{data.ToeExclusions.Count} toe branch(es) excluded from simulation — " +
                    $"{string.Join(", ", data.ToeExclusions.Take(4).Select(t => t.name))}" +
                    $"{(data.ToeExclusions.Count > 4 ? ", …" : "")} (with everything under them). " +
                    "A rig maps \"Toes\" but not the individual digits, so the humanoid rule alone " +
                    "left them simulating whenever the chain started higher up the leg. Turn on " +
                    "\"Convert toe PhysBones\" in the physics options if the toe physics are deliberate.");
            }

            if (data.Ignores.Count == 0)
            {
                sdata.rootBones.Add(data.Root);
            }
            else
            {
                WriteRootsExcluding(ctx, sdata, data);
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
                        $"{data.EndpointPosition.z:0.###}) realised as {tips} \"_End\" bone(s) — VRChat " +
                        "simulates a virtual tip at that offset; MagicaCloth2 only simulates transforms " +
                        "that exist, so without these a single-bone chain would be one fixed particle " +
                        "that never moves.");
                }
            }

            if (data.Colliders.Count > 0)
            {
                sdata.colliderCollisionConstraint.mode = ColliderCollisionConstraint.Mode.Point;
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
                float measured = MeasureMeshRadius(ctx, data, out int samples, out bool grownForReach);
                if (measured > 0f)
                {
                    float before = sdata.radius.value;
                    sdata.radius.value = measured;   // direct, so any depth curve on it survives
                    ctx.Report.Converted(Category, data.Root.name,
                        $"Particle radius {before:0.###} → {measured:0.###}, measured from the mesh these " +
                        $"bones move ({samples} vertex sample(s)). MagicaCloth2's radius is the collision " +
                        "body of a simulated bone; left at the preset's value it is the same size on a " +
                        "breast as on a hair strand, and collision in game covers a fraction of what you " +
                        "can see. The source PhysBone's radius is not used for this — in VRChat it only " +
                        "governs contact with PhysBone colliders, so it is routinely near zero." +
                        (grownForReach
                            ? " This chain is measured at the LARGEST an animated blendshape makes it, " +
                              "not at the size it is saved: the radius cannot follow a slider in game, " +
                              "so it is set to cover the body when the slider is up rather than to fit " +
                              "it when down."
                            : ""));
                }
                else
                {
                    ctx.Report.Skipped(Category, data.Root.name,
                        "Particle radius left at the preset's value — no mesh vertices are weighted to " +
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
                    $"Particle radius {was:0.###} reduced to {sdata.radius.value:0.###} — anything wider than " +
                    "the gap between bones makes neighbouring particles overlap and shove each other apart.");
            }

            if (data.Synthesized)
            {
                // No source PhysBone; nothing to derive, fit or limit
                // from. The preset stands as authored.
                ctx.Report.Converted(Category, data.Root.name,
                    $"SYNTHESIZED BoneCloth{(preset != null ? $" on the \"{chainClass}\" preset" : "")} — " +
                    "this toggled rig had NO physics in the source (no PhysBone; rigid in VRChat too). " +
                    "Created because \"Add physics to toggled rigs that have none\" is on. There was " +
                    "no source feel to derive from, so the preset stands as authored — tune the cloth " +
                    "directly if it moves wrong, or delete it if this rig was rigid on purpose.");
                return cloth;
            }

            if (ctx.Settings.derivePhysicsFromPhysBone)
            {
                DerivePhysics(ctx, data, sdata);
            }

            if (softBody)
            {
                ConfigureSoftBody(ctx, data, sdata, chainClass);
            }

            if (ctx.Settings.fitToPhysBone)
            {
                FitToPhysBone(ctx, data, sdata, softBody);
            }

            if (ctx.Settings.boundSwingToSourceLimit)
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

        // Where the cloth holder lives: under the target-side counterpart of
        // the object the PhysBone COMPONENT sat on in the source. The
        // component is on the source avatar and the holder goes on the clone,
        // so the path is mapped across; if the mapping fails (the object was
        // stripped, or is the root itself) the avatar root is the fallback,
        // which is exactly where holders always went before.
        static Transform HolderHome(BridgeContext ctx, PhysBoneChainData data)
        {
            var target = ctx.Target.transform;
            // The chain data was read from the target itself, so its
            // objects are already target-side. Mapping them through the
            // source descriptor only works when the two are one object.
            var home = data.SourceGameObject != null ? data.SourceGameObject.transform : null;
            if (home == null || home == target || !home.IsChildOf(target)) return target;
            // A component on the chain's own bone: the holder goes beside it,
            // not inside the chain it drives.
            var chainRoot = data.Root;
            bool onChain = chainRoot != null && (home == chainRoot || home.IsChildOf(chainRoot));
            return onChain ? (home.parent != null ? home.parent : target) : home;
        }

        static string UniqueChildName(Transform parent, string name)
        {
            if (parent.Find(name) == null)
            {
                return name;
            }
            int suffix = 2;
            while (parent.Find($"{name} {suffix}") != null)
            {
                suffix++;
            }
            return $"{name} {suffix}";
        }

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
                    $"{(sdata.rootBones.Count > names.Count ? ", …" : "")}. MagicaCloth2 has no ignore list — " +
                    "every root simulates its whole subtree — so the excluded bones are left out by not " +
                    "rooting anything above them. MagicaCloth2 holds a root bone still, so each of these " +
                    "branches now bends from its second joint rather than its first; if one feels stiff at " +
                    "the base, that is why." +
                    (deadRoots.Count > 0
                        ? $" {deadRoots.Count} childless branch(es) were dropped rather than rooted " +
                          "(a root with nothing below it is a single pinned particle that can never move)."
                        : "") +
                    (unhonouredBranches.Count > 0
                        ? $" {unhonouredBranches.Count} branch(es) kept their excluded bones instead of " +
                          $"being lost: {string.Join(", ", unhonouredBranches.Select(b => b.name).Take(4))}" +
                          $"{(unhonouredBranches.Count > 4 ? ", …" : "")}. Everything below those is " +
                          "ignored by the source PhysBone, so honouring it would have left nothing of " +
                          "the branch to root at all — the excluded bones now simulate, which is the " +
                          "smaller error than the branch not being in the cloth."
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
                    $"(including {data.HumanoidExclusions.Count} humanoid-mapped bone(s), which must never " +
                    "simulate) leaves only pinned root particles. It was kept for inspection — delete it, " +
                    "or restructure the source PhysBone if this chain is meant to move.");
                return;
            }

            sdata.rootBones.Clear();
            sdata.rootBones.Add(data.Root);
            ctx.Report.Warning(Category, data.Root.name,
                $"{data.Ignores.Count} Ignore Transform(s) NOT honoured — MagicaCloth2 can only exclude " +
                "a bone by rooting the cloth below it, and here that costs more than it saves: " +
                (costsTooMuch
                    ? $"honouring them would have simulated {kept} of this chain's {wholeChain} bone(s), " +
                      "leaving the joints nearest the body — the ones that carry the motion — pinned. "
                    : "the exclusions sit at the chain's tips, or cover everything under the root, so " +
                      "nothing that can move is left. ") +
                $"The cloth is rooted at \"{data.Root.name}\" with the full tree simulating instead, so " +
                "the excluded bones (usually squish/deform helpers) now jiggle where VRChat held them " +
                "rigid — the far smaller error than the chain barely moving. Delete those bones from " +
                "the cloth by hand if it matters.");
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
                : "Those numbers were not transferred — the cloth uses the baseline above. Turn on \"Derive " +
                  "physics from the PhysBone\" to convert pull, spring and stiffness, or tune the cloth by hand.";

            ctx.Report.Converted(Category, data.Root.name,
                $"BoneCloth on {baseline}, {data.Colliders.Count} collider(s). Source PhysBone was pull " +
                $"{data.Pull:0.##}, spring {data.Spring:0.##}, stiffness {data.Stiffness:0.##}, gravity " +
                $"{data.Gravity:0.##}, immobile {data.Immobile:0.##}, radius {data.Radius:0.###}. {fate}");

            if (data.MaxStretch > 0f || data.MaxSquish > 0f)
            {
                ctx.Report.Skipped(Category, data.Root.name,
                    $"Stretch & Squish (max stretch {data.MaxStretch:0.##}, max squish {data.MaxSquish:0.##}) is " +
                    "not converted — MagicaCloth2's BoneCloth keeps each bone at its rest length, so a chain " +
                    "swings but never lengthens or compresses.");
            }

            if (data.IsAnimated)
            {
                // The report entry for the animationPoseRatio decision
                // taken above. The source flag answers it; nothing to ask.
                ctx.Report.Converted(Category, data.Root.name,
                    "Source PhysBone had 'Is Animated' on, so this cloth settles to the ANIMATED pose " +
                    "(Animation Pose Ratio 1) rather than the pose the avatar was built in. Without it the " +
                    "cloth holds these bones where they started and quietly overrides any animation that " +
                    "moves them — a chest or ear slider that scales its bones is the usual casualty. Set " +
                    "Animation Pose Ratio back to 0 on the cloth if you want it to ignore the animation.");
            }

            if (data.RootHasMultipleChildren && !string.IsNullOrEmpty(data.MultiChildTypeName))
            {
                ctx.Report.Approximated(Category, data.Root.name, data.MultiChildTypeName == "Ignore"
                    ? "Multi Child Type 'Ignore' pins a branching root in VRChat, so Root Rotation is set to 0 " +
                      "here — MagicaCloth2 defaults it to 0.5, which would let a root VRChat held still rotate " +
                      "halfway with its children. Raise it on the cloth if you want this root to follow them."
                    : $"Multi Child Type '{data.MultiChildTypeName}' has no MagicaCloth2 equivalent — every " +
                      "branch off this root simulates independently, where VRChat blended them.");
            }

            if (!string.IsNullOrEmpty(data.Parameter))
            {
                ctx.Report.Approximated(Category, data.Root.name, ctx.Settings.grabbyBonesSupport
                    ? $"PhysBone parameter \"{data.Parameter}\": _IsGrabbed and _Angle work via the GrabbyBones " +
                      "mod (cloth object named to match). _Stretch/_Squish/_IsPosed have no equivalent."
                    : $"PhysBone parameter \"{data.Parameter}\" (_IsGrabbed/_Angle/_Stretch) has no CVR equivalent.");
            }
        }

        static void DerivePhysics(BridgeContext ctx, PhysBoneChainData data, ClothSerializeData sdata)
        {
            bool advanced = data.IsAdvancedIntegration;

            // The preset's own numbers, read before overwrite, kept as
            // a floor. Faithful to a loose PhysBone means mush in
            // MagicaCloth2; the presets are the floor of a usable
            // spring. Derivation may firm a preset, never soften below
            // it. Each end floors on its own.
            float dampFloor = sdata.damping.value;
            float restFloor = sdata.angleRestorationConstraint.stiffness.value;

            // Evaluate both ends of the chain. PhysBone multiplies each base value by its curve
            // at the bone's depth, so root and tip can want quite different things.
            float pullRoot = data.Pull * PhysBoneSolverMap.SafeEvaluate(data.PullCurve, 0f);
            float pullTip = data.Pull * PhysBoneSolverMap.SafeEvaluate(data.PullCurve, 1f);
            float springRoot = data.Spring * PhysBoneSolverMap.SafeEvaluate(data.SpringCurve, 0f);
            float springTip = data.Spring * PhysBoneSolverMap.SafeEvaluate(data.SpringCurve, 1f);
            float stiffRoot = data.Stiffness * PhysBoneSolverMap.SafeEvaluate(data.StiffnessCurve, 0f);
            float stiffTip = data.Stiffness * PhysBoneSolverMap.SafeEvaluate(data.StiffnessCurve, 1f);

            float dampRoot = PhysBoneSolverMap.Damping(pullRoot, springRoot, stiffRoot, advanced);
            float dampTip = PhysBoneSolverMap.Damping(pullTip, springTip, stiffTip, advanced);
            float dampDerived = Mathf.Max(dampRoot, dampTip);
            dampRoot = Mathf.Max(dampRoot, dampFloor);
            dampTip = Mathf.Max(dampTip, dampFloor);
            PhysBoneSolverMap.MapCurve(dampRoot, dampTip,
                out float dampValue, out float dampStart, out float dampEnd, out bool dampCurve);
            sdata.damping.SetValue(dampValue, dampStart, dampEnd, dampCurve);

            float restRoot = PhysBoneSolverMap.RestorationStiffness(
                pullRoot, springRoot, stiffRoot, advanced, out bool satRoot);
            float restTip = PhysBoneSolverMap.RestorationStiffness(
                pullTip, springTip, stiffTip, advanced, out bool satTip);
            float restDerived = Mathf.Max(restRoot, restTip);
            restRoot = Mathf.Max(restRoot, restFloor);
            restTip = Mathf.Max(restTip, restFloor);
            PhysBoneSolverMap.MapCurve(restRoot, restTip,
                out float restValue, out float restStart, out float restEnd, out bool restCurve);

            sdata.angleRestorationConstraint.useAngleRestoration = restValue > 0.0001f;
            sdata.angleRestorationConstraint.stiffness.SetValue(restValue, restStart, restEnd, restCurve);

            bool dampFloored = dampValue > dampDerived + 0.0001f;
            bool restFloored = restValue > restDerived + 0.0001f;
            ctx.Report.Approximated(Category, data.Root.name,
                $"Physics derived from the PhysBone ({(advanced ? "Advanced" : "Simplified")} integration): " +
                $"damping {dampValue:0.###}, angle restoration {restValue:0.###}. Both solvers integrate " +
                "positions per step at a fixed rate, so PhysBone's 60 Hz coefficients were re-expressed at " +
                "MagicaCloth2's 90 Hz. This replaces the preset's feel — if the chain moves wrong, turning " +
                "\"Derive physics from PhysBone\" off restores it." +
                $" Baseline it started from: damping {dampFloor:0.###}, restoration {restFloor:0.###}." +
                (dampFloored || restFloored
                    ? $" Held to the preset's baseline where the source asked for less" +
                      (dampFloored ? $" (damping would have been {dampDerived:0.###})" : "") +
                      (restFloored ? $" (restoration would have been {restDerived:0.###})" : "") +
                      " — MagicaCloth2's own presets treat these as the floor of a spring that still reads " +
                      "as one, and below it a chain converts to mush rather than to a loose chain."
                    : ""));

            if (satRoot || satTip)
            {
                ctx.Report.Approximated(Category, data.Root.name,
                    $"Pull {data.Pull:0.##} is stiffer than MagicaCloth2 can express — its restoration tops " +
                    "out above a pull of about 0.6. Both settle within a frame at that point, so this should " +
                    "not be visible, but the chain will not get any stiffer than it now is.");
            }

            if (!advanced && data.Stiffness > 0.01f)
            {
                ctx.Report.Approximated(Category, data.Root.name,
                    $"Stiffness {data.Stiffness:0.##} was ignored, because VRChat ignores it too — " +
                    "PhysBone's Simplified integration never reads stiffness. Switching the source PhysBone " +
                    "to Advanced would make it mean something in both.");
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
                    "Wind influence set to 0 — VRChat has no wind, so this chain was tuned without it. " +
                    "ChilloutVR worlds can carry wind zones that drive MagicaCloth2, which would move " +
                    "the chain in game in a way it never moved in VRChat (and in a way a Unity scene " +
                    "with no wind zone can't preview). Raise it on the cloth if you want the world's " +
                    "wind to reach this chain.");
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

            if (Mathf.Approximately(data.Gravity, 0f))
            {
                // The author gave this chain no gravity, so it was never meant to hang. Presets
                // carry their own (Long Hair ships 5.0), which would make it fall for the first
                // time in ChilloutVR.
                if (!Mathf.Approximately(sdata.gravity, 0f))
                {
                    sdata.gravity = 0f;
                    ctx.Report.Approximated(Category, data.Root.name,
                        "Gravity set to 0 — the source PhysBone had none, so this chain was never " +
                        "meant to hang under its own weight.");
                }
            }
            else if (data.Gravity < 0f)
            {
                sdata.gravityDirection = new Unity.Mathematics.float3(0f, 1f, 0f);
                ctx.Report.Approximated(Category, data.Root.name,
                    "Gravity direction flipped to point up — the source PhysBone used negative gravity.");
            }

            // immobile -> inertia influence, the same question with the
            // polarity flipped. Both values move together: split, they ask
            // the chain to hold still and swing at once. Never on a soft
            // body, which cannot be flung anyway.
            if (data.Immobile > 0.01f && !softBody)
            {
                float influence = Mathf.Clamp01(1f - data.Immobile);
                bool world = TrySetMember(sdata.inertiaConstraint, "worldInertia", influence);
                bool local = TrySetMember(sdata.inertiaConstraint, "localInertia", influence);
                if (world || local)
                {
                    ctx.Report.Approximated(Category, data.Root.name,
                        $"World and local influence both set to {influence:0.##} — the source PhysBone was " +
                        $"{data.Immobile:0.##} immobile, and MagicaCloth2 measures the same thing the " +
                        "other way round. Both are set because they are the same question at two " +
                        "granularities, not a local/networked split.");
                }

                // All Motion cancels motion relative to the chain's parent, a head
                // turn included. MagicaCloth2's anchor does the same: anchorRatio is
                // 1 - anchorInertia, so all three take 1 - immobile. All Motion only;
                // a World PhysBone keeps reacting to its parent.
                if (data.ImmobileTypeName == "AllMotion" && data.Root != null && data.Root.parent != null)
                {
                    var anchor = data.Root.parent;
                    if (TrySetReference(sdata.inertiaConstraint, "anchor", anchor))
                    {
                        TrySetMember(sdata.inertiaConstraint, "anchorInertia", influence);
                        ctx.Report.Approximated(Category, data.Root.name,
                            $"Inertia anchored to \"{anchor.name}\" at {influence:0.##} — Immobile Type was " +
                            "\"All Motion\", which in VRChat cancels motion from the parent bone too, not " +
                            "just the avatar walking. MagicaCloth2 measures inertia at the cloth object, " +
                            "which never moves when the head turns, so the anchor is what carries that " +
                            "half across. Clear Inertia > Anchor on this cloth if you would rather it " +
                            "reacted to that bone.");
                    }
                }
            }
        }

        static void ApplyMotionLeash(BridgeContext ctx, PhysBoneChainData data, ClothSerializeData sdata)
        {
            if (data.LimitTypeName == "None" || string.IsNullOrEmpty(data.LimitTypeName))
            {
                return;   // the author set no limit, so there is nothing to honour
            }
            float limitAngle = Mathf.Max(data.MaxAngleX, data.MaxAngleZ);
            if (limitAngle <= 0f)
            {
                return;
            }
            float length = MeasureChainLength(data.Root);
            if (length <= 0f)
            {
                return;   // single bone with no reach; a leash would only pin it
            }

            float chord = 2f * length * Mathf.Sin(Mathf.Min(limitAngle, 180f) * 0.5f * Mathf.Deg2Rad);
            if (chord <= 0f)
            {
                return;
            }

            // The curve makes this exact. A bone's chord grows linearly
            // with distance along the chain, and MagicaCloth2 evaluates
            // the curve linearly over depth, so a straight 0-to-1 curve
            // gives every bone its own allowance.
            bool applied = TrySetMember(sdata.motionConstraint, "useMaxDistance", true)
                           && TrySetCurveValue(sdata.motionConstraint, "maxDistance", chord, 0f, 1f);
            if (applied)
            {
                ctx.Report.Converted(Category, data.Root.name,
                    $"Swing bounded to {chord:0.###} from rest, converted from the source's {limitAngle:0}° " +
                    $"{data.LimitTypeName} limit over a {length:0.###} chain, easing to nothing at the root. " +
                    "That limit is what kept this " +
                    "chain presentable in VRChat, and without it a loose chain converts loose and unbounded. " +
                    "It is applied as a distance bound rather than MagicaCloth2's angle limit because a " +
                    "distance bound removes motion instead of adding a restoring force, so it cannot set the " +
                    "chain vibrating. Clear Movement Limit > Use Max Distance on the cloth to undo it.");
            }
            else
            {
                ctx.Report.Skipped(Category, data.Root.name,
                    $"Source {data.LimitTypeName} limit ({limitAngle:0}°) could not be applied as a movement " +
                    "bound on this MagicaCloth2 version — the chain will swing further here than in VRChat.");
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
                $"{label} speed limit raised {current:0.##} → {floor:0.##} — VRChat has no such clamp, so this " +
                "chain was tuned receiving the avatar's movement in full. Past the limit MagicaCloth2 stops " +
                "passing movement to the chain and it rides rigidly with the body, and the preset's value sat " +
                "below walking pace. This is MagicaCloth2's own default rather than no limit at all, so a " +
                "teleport still cannot fling the chain.");
        }

        static float MeasureChainLength(Transform root)
        {
            if (root == null)
            {
                return 0f;
            }
            float longest = 0f;
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                float branch = Vector3.Distance(root.position, child.position) + MeasureChainLength(child);
                if (branch > longest)
                {
                    longest = branch;
                }
            }
            return longest;
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

            var go = new GameObject("MagicaCollider_" + parent.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pbCollider.position;
            go.transform.localRotation = pbCollider.rotation;

            ColliderComponent collider;
            string fitted = null;
            if (shape.Contains("Capsule"))
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

        const int MeshSampleTarget = 200000;

        const int MinMeshSamples = 12;

        const float MinBoneWeight = 0.2f;

        static float MeasureMeshRadius(BridgeContext ctx, PhysBoneChainData data, out int sampled)
            => MeasureMesh(ctx, data, out sampled, out _);

        static float MeasureMeshRadius(BridgeContext ctx, PhysBoneChainData data, out int sampled,
            out bool grown)
            => MeasureMesh(ctx, data, out sampled, out _, out _, out grown);

        static float MeasureMesh(BridgeContext ctx, PhysBoneChainData data, out int sampled,
            out Vector3 centre)
            => MeasureMesh(ctx, data, out sampled, out centre, out _);

        static float MeasureMesh(BridgeContext ctx, PhysBoneChainData data, out int sampled,
            out Vector3 centre, out HashSet<Transform> meshBones)
            => MeasureMesh(ctx, data, out sampled, out centre, out meshBones, out _);

        static float MeasureMesh(BridgeContext ctx, PhysBoneChainData data, out int sampled,
            out Vector3 centre, out HashSet<Transform> meshBones, out bool grown)
        {
            grown = false;
            float saved = MeasureMeshAt(ctx, data, out sampled, out centre, out meshBones, false);
            if (!ctx.Settings.sizePhysicsForLargest || BlendShapeReach(ctx).Count == 0)
            {
                return saved;
            }
            // Measured again with every animated shape at full reach,
            // keeping the larger. That catches a growth slider without
            // deciding per shape which way it goes.
            float atReach = MeasureMeshAt(ctx, data, out _, out _, out _, true);
            grown = atReach > saved;
            return Mathf.Max(saved, atReach);
        }

        static float MeasureMeshAt(BridgeContext ctx, PhysBoneChainData data, out int sampled,
            out Vector3 centre, out HashSet<Transform> meshBones, bool atReach)
        {
            sampled = 0;
            centre = Vector3.zero;
            meshBones = new HashSet<Transform>();
            var chain = new HashSet<Transform>();
            CollectChainBones(data.Root, data.Ignores, chain);
            if (chain.Count == 0)
            {
                return 0f;
            }

            var distances = new List<float>();
            var positions = new List<Vector3>();
            var flat = new Dictionary<Transform, List<Vector2>>();
            foreach (var renderer in ctx.Target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
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
                    vertices = DeformedVertices(ctx, renderer, mesh, atReach);
                    weights = mesh.boneWeights;
                    binds = mesh.bindposes;
                }
                catch
                {
                    continue;   // unreadable mesh — nothing to measure, and not worth failing over
                }
                var bones = renderer.bones;
                if (bones == null || vertices.Length == 0 || weights.Length != vertices.Length)
                {
                    continue;
                }

                // One sample in N on a dense mesh: a 60k-vertex body does not need every vertex
                // to say how thick a breast is, and every chain on every avatar pays this cost.
                int stride = Mathf.Max(1, vertices.Length / MeshSampleTarget);
                for (int i = 0; i < vertices.Length; i += stride)
                {
                    var w = weights[i];
                    AddRadiusSample(distances, positions, flat, meshBones, vertices[i], w.boneIndex0, w.weight0, bones, binds, chain);
                    AddRadiusSample(distances, positions, flat, meshBones, vertices[i], w.boneIndex1, w.weight1, bones, binds, chain);
                    AddRadiusSample(distances, positions, flat, meshBones, vertices[i], w.boneIndex2, w.weight2, bones, binds, chain);
                    AddRadiusSample(distances, positions, flat, meshBones, vertices[i], w.boneIndex3, w.weight3, bones, binds, chain);
                }
            }

            sampled = distances.Count;
            if (distances.Count < MinMeshSamples)
            {
                return 0f;
            }
            foreach (var p in positions)
            {
                centre += p;
            }
            centre /= positions.Count;
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

        static BridgeContext reachOwner;
        static Dictionary<string, float> reachCache;

        static Dictionary<string, float> BlendShapeReach(BridgeContext ctx)
        {
            if (ReferenceEquals(reachOwner, ctx) && reachCache != null)
            {
                return reachCache;
            }
            var reach = new Dictionary<string, float>(StringComparer.Ordinal);
            if (ctx.Settings.sizePhysicsForLargest && ctx.SourceDescriptor != null)
            {
                var seen = new HashSet<AnimationClip>();
                foreach (var entry in AnimatorMerger.GetSelectedVrcControllers(ctx))
                {
                    if (entry.controller == null)
                    {
                        continue;
                    }
                    foreach (var clip in entry.controller.animationClips)
                    {
                        if (clip == null || !seen.Add(clip))
                        {
                            continue;
                        }
                        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                        {
                            if (binding.type != typeof(SkinnedMeshRenderer)
                                || !binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                            {
                                continue;
                            }
                            var curve = AnimationUtility.GetEditorCurve(clip, binding);
                            if (curve == null || curve.keys.Length == 0)
                            {
                                continue;
                            }
                            float high = curve.keys[0].value;
                            foreach (var key in curve.keys)
                            {
                                high = Mathf.Max(high, key.value);
                            }
                            string key2 = binding.path + "|" + binding.propertyName.Substring("blendShape.".Length);
                            reach[key2] = reach.TryGetValue(key2, out var had) ? Mathf.Max(had, high) : high;
                        }
                    }
                }
            }
            reachOwner = ctx;
            reachCache = reach;
            return reach;
        }

        static BridgeContext deformedOwner;
        static Dictionary<string, Vector3[]> deformedCache;

        static Vector3[] DeformedVertices(BridgeContext ctx, SkinnedMeshRenderer renderer, Mesh mesh,
            bool atReach = false)
        {
            if (!ReferenceEquals(deformedOwner, ctx) || deformedCache == null)
            {
                deformedOwner = ctx;
                deformedCache = new Dictionary<string, Vector3[]>(StringComparer.Ordinal);
            }
            string path = AnimationUtility.CalculateTransformPath(renderer.transform, ctx.Target.transform);
            string cacheKey = (atReach ? "max|" : "saved|") + path;
            if (deformedCache.TryGetValue(cacheKey, out var known))
            {
                return known;
            }

            var reach = atReach ? BlendShapeReach(ctx) : null;
            var vertices = mesh.vertices;
            int shapes = mesh.blendShapeCount;
            if (shapes > 0)
            {
                Vector3[] lower = null, upper = null;
                for (int s = 0; s < shapes; s++)
                {
                    float weight = renderer.GetBlendShapeWeight(s);
                    // The far end of what the animator can reach, when asked for it. A slider the
                    // avatar ships at zero still grows the body once someone moves it.
                    if (reach != null && reach.TryGetValue(path + "|" + mesh.GetBlendShapeName(s), out var high))
                    {
                        weight = Mathf.Max(weight, high);
                    }
                    if (Mathf.Abs(weight) < 0.01f)
                    {
                        continue;   // off, and reading its frames is the expensive part
                    }
                    if (lower == null)
                    {
                        lower = new Vector3[vertices.Length];
                        upper = new Vector3[vertices.Length];
                    }
                    ApplyBlendShape(mesh, s, weight, vertices, lower, upper);
                }
            }
            deformedCache[cacheKey] = vertices;
            return vertices;
        }

        static void ApplyBlendShape(Mesh mesh, int shape, float weight, Vector3[] into,
            Vector3[] lower, Vector3[] upper)
        {
            int frames = mesh.GetBlendShapeFrameCount(shape);
            if (frames <= 0)
            {
                return;
            }
            int high = frames - 1;
            for (int f = 0; f < frames; f++)
            {
                if (mesh.GetBlendShapeFrameWeight(shape, f) >= weight)
                {
                    high = f;
                    break;
                }
            }
            float highWeight = mesh.GetBlendShapeFrameWeight(shape, high);
            if (high == 0)
            {
                float scale = highWeight > 0f ? weight / highWeight : 0f;
                mesh.GetBlendShapeFrameVertices(shape, 0, lower, null, null);
                for (int i = 0; i < into.Length; i++)
                {
                    into[i] += lower[i] * scale;
                }
                return;
            }
            float lowWeight = mesh.GetBlendShapeFrameWeight(shape, high - 1);
            float span = highWeight - lowWeight;
            float t = span > 0f ? (weight - lowWeight) / span : 0f;
            mesh.GetBlendShapeFrameVertices(shape, high - 1, lower, null, null);
            mesh.GetBlendShapeFrameVertices(shape, high, upper, null, null);
            for (int i = 0; i < into.Length; i++)
            {
                into[i] += Vector3.LerpUnclamped(lower[i], upper[i], t);
            }
        }

        static BridgeContext boneVertexOwner;
        static Dictionary<Transform, List<Vector3>> boneVertexCache;

        static Dictionary<Transform, List<Vector3>> BoneVertices(BridgeContext ctx)
        {
            if (ReferenceEquals(boneVertexOwner, ctx) && boneVertexCache != null)
            {
                return boneVertexCache;
            }
            var byBone = new Dictionary<Transform, List<Vector3>>();
            foreach (var renderer in ctx.Target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
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
                    vertices = DeformedVertices(ctx, renderer, mesh);
                    weights = mesh.boneWeights;
                    binds = mesh.bindposes;
                }
                catch
                {
                    continue;   // unreadable mesh — nothing to measure, and not worth failing over
                }
                var bones = renderer.bones;
                if (bones == null || vertices.Length == 0 || weights.Length != vertices.Length)
                {
                    continue;
                }

                int stride = Mathf.Max(1, vertices.Length / MeshSampleTarget);
                for (int i = 0; i < vertices.Length; i += stride)
                {
                    var w = weights[i];
                    AddBoneVertex(byBone, vertices[i], w.boneIndex0, w.weight0, bones, binds);
                    AddBoneVertex(byBone, vertices[i], w.boneIndex1, w.weight1, bones, binds);
                    AddBoneVertex(byBone, vertices[i], w.boneIndex2, w.weight2, bones, binds);
                    AddBoneVertex(byBone, vertices[i], w.boneIndex3, w.weight3, bones, binds);
                }
            }
            boneVertexOwner = ctx;
            boneVertexCache = byBone;
            return byBone;
        }

        static void AddBoneVertex(Dictionary<Transform, List<Vector3>> byBone, Vector3 vertex,
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
            // The bind pose puts the vertex in the bone's own space; that bone's current matrix
            // puts it back where it is skinned to, which is the same route the particle radius
            // takes to find the middle of a mesh.
            Vector3 bindLocal = binds[boneIndex].MultiplyPoint3x4(vertex);
            if (!byBone.TryGetValue(bone, out var list))
            {
                byBone[bone] = list = new List<Vector3>();
            }
            list.Add(bone.localToWorldMatrix.MultiplyPoint3x4(bindLocal));
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

        static void AddRadiusSample(List<float> into, List<Vector3> positions,
            Dictionary<Transform, List<Vector2>> flat,
            HashSet<Transform> meshBones, Vector3 vertex, int boneIndex, float weight,
            Transform[] bones, Matrix4x4[] binds, HashSet<Transform> chain)
        {
            if (weight < MinBoneWeight || boneIndex < 0
                || boneIndex >= bones.Length || boneIndex >= binds.Length)
            {
                return;
            }
            var bone = bones[boneIndex];
            if (bone == null || !chain.Contains(bone))
            {
                return;
            }

            // The bind pose puts the vertex in the bone's own space, so this does not move when
            // the avatar does.
            Vector3 local = binds[boneIndex].MultiplyPoint3x4(vertex);
            Vector3 axis = bone.childCount > 0 ? bone.GetChild(0).localPosition : Vector3.zero;
            Vector3 perpendicular = axis.sqrMagnitude > 1e-10f
                ? Vector3.ProjectOnPlane(local, axis.normalized)
                : local;
            float distance = perpendicular.magnitude;

            // Bone-local units become world units, which is what MagicaCloth2's radius is in.
            Vector3 scale = bone.lossyScale;
            float mean = (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f;
            if (distance > 0f && mean > 0f)
            {
                into.Add(distance * mean);
                // Bind-pose local put through the bone's current world matrix IS where that
                // vertex is skinned to, which is what "the middle of the mesh" has to mean.
                positions.Add(bone.localToWorldMatrix.MultiplyPoint3x4(local));
                meshBones.Add(bone);

                // The same offset in the plane across the bone, kept PER BONE so the
                // cross-section's narrowest width can be measured. Per bone matters: each one
                // has its own frame, and pooling a panel whose bones fan out smears the section
                // into a cloud wider than any single bone's, which is most of what a flat mesh
                // needed measuring for. See MinimumCaliperRadius.
                Vector3 a = axis.sqrMagnitude > 1e-10f ? axis.normalized : Vector3.up;
                Vector3 e1 = Vector3.Cross(a, Mathf.Abs(a.x) < 0.9f ? Vector3.right : Vector3.forward).normalized;
                Vector3 e2 = Vector3.Cross(a, e1).normalized;
                if (!flat.TryGetValue(bone, out var section))
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
            float power = Mathf.Max(presetPower, SoftBodySpringPower);
            spring &= TrySetMember(sdata.springConstraint, "springPower", power);

            var collisionBones = ChooseCollisionBones(ctx, data, out float collisionRadius);
            bool collision = false;
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
                if (collision && collisionRadius > 0f)
                {
                    sdata.radius.SetValue(collisionRadius);
                }
            }
            string collisionBone = collisionBones.Count > 0
                ? string.Join("\", \"", collisionBones.Select(b => b.name))
                : null;

            ctx.Report.Converted(Category, data.Root.name,
                $"Built as a MagicaCloth2 SOFT BODY (\"{chainClass}\"), not as a chain of bones. A breast, " +
                "belly or thigh is a volume anchored to a bone rather than something that hangs, so it is " +
                "held near its rest position by a spring" +
                (spring ? $" (power {power:0.###})" : " (spring settings unavailable on this MagicaCloth2 version)") +
                (collision
                    ? $", and only \"{collisionBone}\" ({collisionBones.Count} of {data.Root.childCount} " +
                      $"branch(es)) is offered for collision, sized {collisionRadius:0.###} " +
                      "from the mesh — so other people touch the part that is actually there instead of every " +
                      "bone in the chain"
                    : ", though its collision bone could not be set on this MagicaCloth2 version") +
                ". Its inertia is also left at the preset's value rather than converted from Immobile: an " +
                "anchored body cannot be thrown off the avatar, so holding inertia down only stops it " +
                "answering the body's movement.");
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
            radius = MeasureMesh(ctx, data, out int samples, out _,
                out HashSet<Transform> meshBones);
            if (samples < MinMeshSamples || meshBones.Count == 0)
            {
                return chosen;   // nothing measurable; better no collision bone than a guessed one
            }

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
                }
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

        static bool TrySetCurveValue(object target, string fieldName, float value)
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
            if (field.GetValue(target) is CurveSerializeData curveData)
            {
                curveData.SetValue(value);
                return true;
            }
            if (field.FieldType == typeof(float))
            {
                field.SetValue(target, value);
                return true;
            }
            return false;
        }

        static string PathOf(Transform t) => t != null ? t.name : "(null)";
    }
}
#endif
