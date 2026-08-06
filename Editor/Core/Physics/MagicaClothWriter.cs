#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;
using MagicaCloth2;

namespace AvatarBridge
{
    /// <summary>
    /// Writes a VRCPhysBone chain as a MagicaCloth2 BoneCloth.
    ///
    /// It transfers STRUCTURE and nothing else:
    ///
    ///   which bone the chain hangs from     -> rootBones
    ///   which colliders it collides with    -> colliderList
    ///   which transforms to leave out       -> expressed as root bones (see WriteRootsExcluding)
    ///   whether it started enabled          -> the holder's active state
    ///
    /// By default every physics value is left to MagicaCloth2 — either its own defaults or a
    /// preset matched to the kind of chain.
    ///
    /// Earlier versions derived those values from the PhysBone and had to walk each attempt back
    /// after a real avatar misbehaved. The reason given at the time was that the two systems are
    /// different kinds of simulation — PhysBones per-bone rotational springs, MagicaCloth2 a
    /// particle position solver — so no arithmetic between them could mean anything.
    ///
    /// That reason was wrong, and reading `PhysBoneManager.PhysBoneJob.SolveChain` out of the
    /// SDK's own (unobfuscated) `VRC.Dynamics.dll` is what settled it: PhysBone integrates bone
    /// ENDPOINTS and reads rotations back out of where they land, exactly as MagicaCloth2 does.
    /// The real problem was calibration — per-step coefficients at two different fixed rates,
    /// 60 Hz against 90 Hz — and <see cref="PhysBoneSolverMap"/> now derives that conversion from
    /// both solvers' source. It is opt-in ("Derive physics from PhysBone") because derived
    /// values have a history here, not because the derivation is in doubt.
    ///
    /// With it off, the PhysBone's numbers still go into the report for anyone tuning by hand.
    ///
    /// Optional extras that add no arithmetic of their own: "Start from MagicaCloth2 presets"
    /// swaps the global defaults for one matched to the kind of chain, and "Transfer angle
    /// limits" copies the limit across verbatim.
    /// </summary>
    public static class MagicaClothWriter
    {
        const string Category = "PhysBones -> MagicaCloth2";

        /// <summary>Writes the cloth and returns it, so later passes can extend what it references.</summary>
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
            // Sibling-unique, because animation paths address children by name: an avatar with
            // four hairstyles produces several chains rooted at a bone called "Hair_root", and
            // two holders both named "MagicaCloth_Hair_root" mean every animation curve aimed at
            // one of them resolves to whichever Unity finds first.
            holderName = UniqueChildName(ctx.Target.transform, holderName);
            var holder = new GameObject(holderName);
            holder.transform.SetParent(ctx.Target.transform, false);
            if (!data.InitiallyActive)
            {
                holder.SetActive(false);
                ctx.Report.Approximated(Category, data.Root.name, data.Synthesized
                    ? "Style was inactive at conversion; cloth created disabled. Its toggle is " +
                      "re-wired to activate this cloth — see the Animator section of this report."
                    : "Source PhysBone was disabled; cloth created disabled. Animator toggles that " +
                      "activated the original object (hair swaps, outfit toggles) are re-wired to " +
                      "activate this cloth too — see the Animator section of this report.");
            }

            var cloth = holder.AddComponent<MagicaCloth>();
            ctx.ConvertedPhysicsChains.Add(new BridgeContext.ConvertedPhysicsChain
            {
                Source = data.SourceGameObject,
                Host = holder,
                Physics = cloth,
                Root = data.Root
            });
            var sdata = cloth.SerializeData;

            // Optional: start from a preset for this kind of chain instead of the global
            // defaults. Still no arithmetic — it swaps one author-tuned baseline for another.
            // MagicaCloth2's ImportJson preserves the structural fields, so this is free to run
            // either side of the wiring below.
            string preset = null, chainClass = null;
            bool customPreset = false;
            if (ctx.Settings.useMagicaPresets)
            {
                var cls = MagicaPresetLibrary.Classify(data);
                chainClass = cls.Name;
                if (!MagicaPresetLibrary.TryApply(sdata, cls, out preset, out customPreset, out string presetError))
                {
                    ctx.Report.Approximated(Category, data.Root.name,
                        $"No preset applied for chain class \"{cls.Name}\" — {presetError}. Using " +
                        "MagicaCloth2's defaults instead.");
                    preset = null;
                }
            }

            // --- structure ----------------------------------------------------------------
            sdata.clothType = ClothProcess.ClothType.BoneCloth;

            // "Is Animated" on the source PhysBone means exactly one thing: an animation moves
            // these bones. MagicaCloth2 settles a chain back to its INITIAL pose by default, so
            // the animation and the cloth then fight and the cloth wins — which is not subtle. On
            // the avatar that found this, a chest slider scales the breast bones to 0.75 at its
            // lowest setting, the cloth held them at 1.0, and the converted avatar had a visibly
            // different figure from the original at identical menu settings.
            //
            // Settling to the ANIMATED pose is what the source PhysBone was already doing. This
            // used to be reported as something for the user to go and tick by hand, which is a
            // poor trade when the flag that decides it is sitting in the source data.
            if (data.IsAnimated)
            {
                sdata.animationPoseRatio = 1f;
            }

            // Same trade as animationPoseRatio above: the source data already answers this, so
            // answering it here beats printing an instruction.
            //
            // VRChat's Multi Child Type 'Ignore' PINS a branching root — the root itself is not
            // simulated, only the branches below it. MagicaCloth2's nearest control is
            // rootRotation, whose own documentation reads "0.0=does not rotate, 0.5=middle,
            // 1.0=child-based" and which defaults to 0.5. Left at the default, a pinned root got
            // half the rotation of its children, so a chain VRChat held still swung in ChilloutVR
            // — reported on an avatar whose rear visibly rotated on its own with no input.
            //
            // Presets do not carry rootRotation ("[NG] Export/Import with Presets" in MagicaCloth2's
            // own source), so this survives preset application in either order.
            if (data.RootHasMultipleChildren && data.MultiChildTypeName == "Ignore")
            {
                sdata.rootRotation = 0f;
            }

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

            // VRChat's Endpoint Position appends a VIRTUAL bone to every leaf of the chain —
            // that is the tip PhysBone actually simulates. MagicaCloth2 has no such concept: a
            // BoneCloth simulates the transforms that exist, and a root with no children is one
            // FIXED particle, i.e. a chain that converts cleanly and never moves. Single-bone
            // PhysBones with an endpoint offset (ears, antennae, accessory bones) are exactly
            // that shape. So the virtual bone is made real: every leaf of the simulated tree
            // gets a "<leaf>_End" child at the endpoint offset, which is the same trick
            // DynamicBone's own m_EndOffset performs internally.
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
                float measured = MeasureMeshRadius(ctx, data, out int samples);
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
                        "governs contact with PhysBone colliders, so it is routinely near zero.");
                }
                else
                {
                    ctx.Report.Skipped(Category, data.Root.name,
                        "Particle radius left at the preset's value — no mesh vertices are weighted to " +
                        "these bones (or the mesh could not be read), so there was nothing to measure. " +
                        "Set it on the cloth by hand if this chain collides at the wrong size.");
                }
            }

            // Particle radius bound. Not a conversion — a safety rail. MagicaCloth2's radius is
            // the particle size, and a particle wider than the gap between bones overlaps its
            // neighbour, which the solver resolves by shoving them apart. Applies to the
            // default and preset values alike, since both assume roughly human-sized chains.
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
                // No source PhysBone exists, so there is nothing to derive, fit or limit from —
                // the preset stands exactly as its author wrote it.
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

            if (ctx.Settings.fitToPhysBone)
            {
                FitToPhysBone(ctx, data, sdata);
            }

            if (ctx.Settings.boundSwingToSourceLimit)
            {
                ApplyMotionLeash(ctx, data, sdata);
            }

            if (ctx.Settings.transferAngleLimits)
            {
                ApplyAngleLimit(ctx, data, sdata);
            }

            ReportSourceSettings(ctx, data, preset, chainClass, customPreset);
            return cloth;
        }

        /// <summary>
        /// Creates a cloth for a rig that never had a PhysBone — the "Add physics to toggled
        /// rigs that have none" option. The chain is a plain BoneCloth rooted at the rig's own
        /// root, preset by classification (which reads ancestor names, so a nondescript rig
        /// under a container called "Vampy Hair" still lands on a hair preset), with every
        /// PhysBone-derivation step skipped — there is no source to derive from.
        /// </summary>
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

        /// <summary>
        /// Creates the "_End" tip bone VRChat's Endpoint Position implies, on every leaf of the
        /// simulated tree. Walks each cloth root, skipping ignored branches (they are not part of
        /// this cloth), and gives childless transforms a real child at the endpoint offset.
        /// </summary>
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
                if (leaf)
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

        /// <summary>
        /// Expresses a PhysBone's Ignore Transforms as MagicaCloth2 root bones.
        ///
        /// MagicaCloth2 has no exclusion for BoneCloth. Its own comment where it builds the chain
        /// reads "root以下をすべて登録する" — register everything under root — and it means it:
        /// every root walks its whole subtree, unconditionally. There is a boneAttributeDict that
        /// takes VertexAttribute.Invalid per transform, and AvatarBridge used to write the ignores
        /// into it, but MagicaCloth2 declares that field [System.NonSerialized]. It looked right in
        /// the editor and was gone the instant the avatar was serialized for upload, so in game the
        /// eyes, jaw and hair of a head-rooted chain were all being simulated.
        ///
        /// What is serialized is the plain rootBones list, so the ignores are expressed by
        /// decomposition instead: descend from the root and collect the largest subtrees that
        /// contain no ignored transform. A branch that is entirely clean becomes one root; a branch
        /// with an ignored bone somewhere inside is descended into further.
        ///
        /// The cost is honest and worth stating: MagicaCloth2 fixes each root bone in place, so a
        /// branch promoted to a root loses motion at its own base joint — an ear rooted this way
        /// swings from its second joint rather than its first. The alternative was writing
        /// position-matched selection data, which reproduces the original exactly and fails
        /// silently when the match is off. A slightly stiffer ear that is visible in the report
        /// beats a chain that is subtly wrong for reasons nobody can see.
        ///
        /// That cost has a degenerate end, found on a tester's breast chains: when the ignored
        /// bones sit at the TIPS of the tree (squish/deform helpers excluded by the author), the
        /// decomposition descends past every real joint and the "largest clean subtrees" are bare
        /// leaves. A leaf root is one pinned particle — the whole cloth converts cleanly and
        /// never moves. So each promoted root is now checked for movable content (a non-ignored
        /// descendant, or an endpoint tip about to be synthesized): dead roots are dropped, and if
        /// none survive the cloth falls back to the PhysBone's own root with the ignores left
        /// unhonoured — a few helper bones jiggling is the far smaller error than the chain being
        /// a statue. The fallback is refused when the ignores include humanoid-mapped bones,
        /// because simulating those fights the animator and IK (see PhysBoneChainData); a dead
        /// chain is safer than that, and the report says which trade was taken.
        /// </summary>
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

            void Collect(Transform t)
            {
                for (int i = 0; i < t.childCount; i++)
                {
                    var child = t.GetChild(i);
                    if (ignored.Contains(child))
                    {
                        continue; // an ignored transform takes its whole subtree with it
                    }
                    if (SubtreeHasIgnored(child))
                    {
                        Collect(child); // something inside is ignored, so this can't be one root
                    }
                    else
                    {
                        sdata.rootBones.Add(child);
                    }
                }
            }

            Collect(data.Root);

            // A promoted root only produces motion through its descendants — MagicaCloth2 pins
            // every root in place, and a pinned particle with nothing below it is a statue. When
            // the author's ignores sit at the tips of the tree, the decomposition above collapses
            // to exactly those statues (a tester's breast chains converted to two pinned leaves
            // and never moved). An endpoint offset rescues a leaf root — SynthesizeEndpointBones
            // will give it a real "_End" child to swing — so leaves only count as dead without one.
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

            if (sdata.rootBones.Count > deadRoots.Count)
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
                        : ""));
                return;
            }

            // Nothing movable survives the decomposition — every clean subtree is a bare leaf, or
            // everything under the root was ignored outright. Honouring the ignores here means
            // shipping a cloth that can never move, so the choice is between over-simulating and
            // a statue.
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
                "a bone by rooting the cloth below it, and here that leaves nothing that can move (the " +
                "exclusions sit at the chain's tips, or cover everything under the root). The cloth is " +
                $"rooted at \"{data.Root.name}\" with the full tree simulating instead, so the excluded " +
                "bones (usually squish/deform helpers) now jiggle where VRChat held them rigid — the far " +
                "smaller error than the chain freezing solid. Delete those bones from the cloth by hand " +
                "if it matters.");
        }

        /// <summary>
        /// Puts the PhysBone's own numbers in the report. Nothing here is applied — this is the
        /// information you'd need to tune a chain by hand, kept somewhere findable rather than
        /// being turned into MagicaCloth2 values it doesn't correspond to.
        /// </summary>
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

            if (data.LimitTypeName != "None" && !string.IsNullOrEmpty(data.LimitTypeName)
                && !ctx.Settings.transferAngleLimits)
            {
                bool polar = data.LimitTypeName == "Polar";
                float limitAngle = polar ? Mathf.Max(data.MaxAngleX, data.MaxAngleZ) : data.MaxAngleX;
                ctx.Report.Skipped(Category, data.Root.name,
                    $"{data.LimitTypeName} limit ({limitAngle:0}°) not applied. To add it, tick Angle Limit on " +
                    $"this cloth and set Limit Angle to {limitAngle:0}, then lower Stiffness until it stops " +
                    "snapping — or turn on \"Transfer angle limits\" to do this for every chain.");
            }

            if (data.MaxStretch > 0f || data.MaxSquish > 0f)
            {
                ctx.Report.Skipped(Category, data.Root.name,
                    $"Stretch & Squish (max stretch {data.MaxStretch:0.##}, max squish {data.MaxSquish:0.##}) is " +
                    "not converted — MagicaCloth2's BoneCloth keeps each bone at its rest length, so a chain " +
                    "swings but never lengthens or compresses.");
            }

            if (data.IsAnimated)
            {
                // "Is Animated" on a PhysBone means exactly one thing: an animation moves these
                // bones. MagicaCloth2 settles a chain back to its INITIAL pose by default, so the
                // animation and the cloth then fight, and the cloth wins — which is not a subtle
                // effect. On the avatar that found this, a chest slider scales the breast bones to
                // 0.75 at its lowest setting, the cloth held them at 1.0, and the converted avatar
                // simply had a different figure from the original at the same menu settings.
                //
                // animationPoseRatio = 1 tells the cloth to settle to the ANIMATED pose instead,
                // which is what the source PhysBone was already doing. Previously this was reported
                // as something for the user to go and tick by hand; the flag that decides it is
                // right there in the source data, so there is nothing to ask.
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

        /// <summary>
        /// Replaces the preset's damping and angle restoration with values derived from this
        /// PhysBone's own pull, spring and stiffness.
        ///
        /// The derivation and its evidence are in <see cref="PhysBoneSolverMap"/>. In short: both
        /// solvers integrate positions with per-step coefficients at a fixed known rate, so a
        /// retention at PhysBone's 60 Hz re-expresses at MagicaCloth2's 90 Hz as `r^(60/90)`.
        /// PhysBone's stiffness is not an independent axis — the algebra collapses it into a
        /// scale on both of the others — and Simplified integration ignores it outright.
        ///
        /// Falloff curves carry across because both systems mean the same thing by them: a base
        /// value multiplied by a 0..1 curve over the chain's depth. Only the endpoints survive,
        /// since MagicaCloth2 builds its curve with <c>AnimationCurve.Linear</c>.
        /// </summary>
        static void DerivePhysics(BridgeContext ctx, PhysBoneChainData data, ClothSerializeData sdata)
        {
            bool advanced = data.IsAdvancedIntegration;

            // The preset's own numbers, read BEFORE they are overwritten, and kept as a floor.
            //
            // The derivation below is faithful — re-derived by hand against a reported avatar it
            // reproduces the shipped numbers exactly — but faithful to a loose PhysBone means
            // mush in MagicaCloth2. A chain of pull 0.22 against spring 0.81 derives to damping
            // 0.135 and restoration 0.048, where MagicaCloth2's SOFTEST stock spring preset is
            // 0.20/0.20 and its hard one 0.30/0.60. Reported from the wild as breasts that swing
            // far too freely, and confirmed by hand: the same chain on the Hard Spring preset
            // "looks great".
            //
            // MagicaCloth2's authors evidently treat their preset values as the floor of a
            // usable spring, and those presets are already matched to the KIND of chain this is.
            // So the derivation may FIRM a preset with the source's character and may not soften
            // it below that baseline. Each end is floored on its own, so wherever the source
            // asked for more than the floor its shape still carries.
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

        /// <summary>
        /// Nudges the preset toward what the PhysBone actually asked for — but only for the
        /// facts that mean the SAME THING in both systems, which is a very short list.
        ///
        /// Two kinds of statement need no conversion at all, so they apply whether or not the
        /// derived mapping is on. **Categorical ones**: "this never falls", "this falls upward",
        /// "VRChat has no wind so this was tuned without any" — both systems
        /// express those the same way, as a gravity of zero or a flipped direction. And **a
        /// dimensionless ratio with the same meaning on both sides**: MagicaCloth2 documents
        /// `worldInertia` as "World Influence (0.0 ~ 1.0)" and PhysBone's `immobile` is how much
        /// the chain IGNORES that same movement — the same question in the same units, just
        /// inverted. Neither involves converting one system's numbers into the other's.
        ///
        /// Pull, spring and stiffness are not here. They do have an exchange rate — see
        /// <see cref="PhysBoneSolverMap"/>, which derives it — but converting them is a bigger
        /// claim than this method makes, so it lives behind its own setting in
        /// <see cref="DerivePhysics"/>. Everything else stays with the preset.
        /// </summary>
        static void FitToPhysBone(BridgeContext ctx, PhysBoneChainData data, ClothSerializeData sdata)
        {
            // VRChat has no wind. There is no PhysBone field for it, nothing in SolveChain reads
            // one, and a VRChat world cannot blow a chain around — so every PhysBone ever authored
            // was tuned with wind out of the picture.
            //
            // MagicaCloth2 ships `WindSettings.influence` at 1.0, and ChilloutVR worlds carry wind
            // zones that drive it (the CCK's own wind component has a "Magica Cloth Specific
            // Settings" section). Left alone, a converted chain picks up motion in game that it
            // never had in VRChat, from a source the avatar's author never accounted for — and one
            // that cannot be previewed in a Unity scene with no wind zone in it.
            //
            // This is the same kind of statement as "the author gave this chain no gravity": a
            // categorical fact about the source, not a number being converted.
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

            // immobile -> inertia influence. Same 0..1 question on both sides ("how much does
            // motion shake this chain"), opposite polarity. Only applied when the author actually
            // set it, so a chain they left alone keeps the preset's own tuning.
            //
            // BOTH of MagicaCloth2's inertia values, not just worldInertia. They are not "local
            // player" and "networked" — MagicaCloth2 has no networking, every client simulates
            // every avatar. They are the same motion (`cdata.stepVector`, the cloth component
            // transform's world delta) answered at two granularities:
            //
            //   movementShift        = 1 - worldInertia   // per frame, shifts the reference frame
            //   localMovementInertia = 1 - localInertia   // per step, shifts each particle
            //
            // Same polarity, same source. Setting one to 0.1 and leaving the other at 1.0 asks the
            // chain to hold still and swing freely at once, which is what shipped until 2.37.0:
            // a 0.9-immobile PhysBone still swung freely in game because localInertia was never
            // touched. Every MagicaCloth2 preset keeps the pair equal (both 1.0); the split was
            // this tool's invention, not the solver's design.
            if (data.Immobile > 0.01f)
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

                // The other half of immobile. PhysBone's default type is "All Motion" ("World" is
                // labelled Experimental), and All Motion cancels motion relative to the chain's
                // ROOT PARENT — a head turn or an animation, not just walking. The solver picks
                // that matrix directly:
                //
                //   SolveChain(chain, rootParentMatrix,
                //              immobileType == AllMotion ? rootParentMatrix : sceneRootState, ...)
                //
                // The two values above cannot express it, because MagicaCloth2 measures inertia at
                // the cloth component's transform — which sits on the avatar root, and does not
                // move when the head turns.
                //
                // `inertiaConstraint.anchor` is MagicaCloth2's own answer: "Anchor that cancels
                // inertia. Anchor translation and rotation are excluded from simulation." Pointing
                // it at the chain's parent bone reproduces All Motion's reference frame exactly,
                // and its influence carries the same polarity as the other two:
                //
                //   anchorRatio = 1 - anchorInertia;                       // TeamManager
                //   oldComponentWorldPosition += anchorDelta * anchorRatio;  // 打ち消す, "cancel out"
                //
                // so a low anchorInertia cancels MORE of the parent's motion, exactly as a low
                // worldInertia absorbs more of the avatar's. All three therefore take 1 - immobile
                // and stay consistent with each other.
                //
                // Only for All Motion: a "World" PhysBone deliberately keeps reacting to its
                // parent, and anchoring it would take away motion its author wanted. Anchor is
                // [NG] for preset import, so no preset can clobber this afterwards.
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

        /// <summary>
        /// Copies the PhysBone's angle limit across verbatim, behind the "Transfer angle limits"
        /// option. MagicaCloth2's limit constrains particle positions against a baseline pose
        /// where PhysBone's constrains bone rotation against its parent, and MagicaCloth2's
        /// stiffness defaults to a rigid snap-back — so on some avatars this shakes the chain
        /// and on others it is the best result the tool gives. Hence the option.
        /// </summary>
        static void ApplyAngleLimit(BridgeContext ctx, PhysBoneChainData data, ClothSerializeData sdata)
        {
            if (data.LimitTypeName == "None" || string.IsNullOrEmpty(data.LimitTypeName))
            {
                return;
            }
            float limitAngle = Mathf.Max(data.MaxAngleX, data.MaxAngleZ);
            bool applied = TrySetMember(sdata.angleLimitConstraint, "useAngleLimit", true)
                           && TrySetCurveValue(sdata.angleLimitConstraint, "limitAngle", limitAngle);
            if (applied)
            {
                ctx.Report.Approximated(Category, data.Root.name,
                    $"{data.LimitTypeName} limit transferred as a symmetric {limitAngle:0}° angle limit. " +
                    "If this chain snaps back or shakes, lower Angle Limit > Stiffness on the cloth, or turn " +
                    "off \"Transfer angle limits\" and convert again.");
            }
            else
            {
                ctx.Report.Skipped(Category, data.Root.name,
                    $"Angle limit ({data.LimitTypeName}) could not be applied on this MagicaCloth2 version.");
            }
        }

        /// <summary>
        /// Bounds how far the chain may swing, from the limit its author already set.
        ///
        /// A PhysBone's Angle/Hinge/Polar limit is not decoration: it is the reason a floaty
        /// chain stays presentable in VRChat. The avatar that prompted this carries pull 0.22
        /// against spring 0.81 — genuinely loose, and faithfully converted as such — with a 48°
        /// limit holding it in. Converted without that limit the chain is loose and UNBOUNDED,
        /// which is the "way too swaying" this was reported as.
        ///
        /// The limit is applied as a POSITIONAL leash (MotionConstraint) rather than as
        /// MagicaCloth2's own angle limit, deliberately. The angle constraint runs three
        /// iterations per step against a stiffness that snaps back hard, and its own author's
        /// comment calls rotating about a point near the parent 酷い振動の温床 — a hotbed of
        /// severe vibration. That is why "Transfer angle limits" is off by default and warns
        /// that it shakes some chains. maxDistance is a plain clamp on how far a particle may
        /// travel from rest: it cannot oscillate, because it removes motion rather than adding
        /// a restoring force.
        ///
        /// The conversion is geometry. A bone d along the chain, swung θ from rest, moves a
        /// chord of 2·d·sin(θ/2). MagicaCloth2 wants one value with a 0..1 curve over the
        /// chain's depth, so the tip's chord is the value and the curve carries the rest: the
        /// root is pinned and may not move at all, the tip may move furthest.
        /// </summary>
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

            // The curve is what makes this exact rather than approximate. A bone's chord is
            // 2·d·sin(θ/2), which grows LINEARLY with its distance d along the chain, and
            // MagicaCloth2 evaluates the curve linearly over depth — so the tip's chord with a
            // straight 0→1 curve gives every bone in between precisely its own allowance. Set
            // as a flat value instead, a bone near the root could travel the tip's full distance.
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

        /// <summary>Root to furthest tip, following the longest path.</summary>
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
            if (shape.Contains("Capsule"))
            {
                var capsule = go.AddComponent<MagicaCapsuleCollider>();
                capsule.direction = MagicaCapsuleCollider.Direction.Y; // PB capsules extend along local Y
                capsule.SetSize(pbCollider.radius, pbCollider.radius, Mathf.Max(pbCollider.height, pbCollider.radius * 2f));
                collider = capsule;
            }
            else if (shape.Contains("Plane"))
            {
                collider = go.AddComponent<MagicaPlaneCollider>();
            }
            else
            {
                var sphere = go.AddComponent<MagicaSphereCollider>();
                sphere.SetSize(pbCollider.radius);
                collider = sphere;
            }

            ctx.Report.Converted("PhysBone colliders", PathOf(pbCollider.transform), shape + " -> Magica collider");
            PhysBoneConverter.RecordColliderHost(ctx, pbCollider, go);
            cache[pbCollider] = collider;
            return collider;
        }

        /// <summary>Average distance between bones down the chain, used to bound the particle radius.</summary>
        /// <summary>One sample per this many vertices on a dense mesh.</summary>
        const int MeshSampleTarget = 4000;

        /// <summary>Fewer usable samples than this and the measurement is not worth trusting.</summary>
        const int MinMeshSamples = 12;

        /// <summary>Below this a vertex is barely attached to the bone and says nothing about its size.</summary>
        const float MinBoneWeight = 0.2f;

        /// <summary>
        /// The radius a particle needs in order to stand for the part of the body it drives,
        /// measured from the mesh instead of guessed.
        ///
        /// MagicaCloth2's radius is the collision body of a simulated bone, and nothing in this
        /// conversion ever set it: the matched preset's value simply stood, so a breast chain and
        /// a hair strand both arrived at whatever that preset happened to ship. Reported from a
        /// real avatar as collision points a fraction of the size of the body they belong to.
        ///
        /// The source PhysBone's own radius is deliberately NOT the answer, tempting as it looks.
        /// In VRChat that field only governs contact against PhysBone colliders, so an author who
        /// never used that leaves it near zero — the avatar that prompted this carries 0.005,
        /// 0.007 and 0.01 on chains whose meshes are the size of a head. Same word, different
        /// quantity, and copying it across makes the collision smaller still.
        ///
        /// So: for every vertex weighted to a bone in the chain, the distance from that bone's
        /// AXIS — not its origin, because a hair strand's vertices run down the length of the
        /// bone and their distance from its origin is the strand's length rather than its
        /// thickness. The median is taken rather than the extreme, so one vertex weighted across
        /// half the body cannot size the whole chain. Everything is measured in the bind pose,
        /// so the answer does not depend on how the avatar happens to be posed in the scene.
        /// </summary>
        static float MeasureMeshRadius(BridgeContext ctx, PhysBoneChainData data, out int sampled)
        {
            sampled = 0;
            var chain = new HashSet<Transform>();
            CollectChainBones(data.Root, data.Ignores, chain);
            if (chain.Count == 0)
            {
                return 0f;
            }

            var distances = new List<float>();
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
                    vertices = mesh.vertices;
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
                    AddRadiusSample(distances, vertices[i], w.boneIndex0, w.weight0, bones, binds, chain);
                    AddRadiusSample(distances, vertices[i], w.boneIndex1, w.weight1, bones, binds, chain);
                    AddRadiusSample(distances, vertices[i], w.boneIndex2, w.weight2, bones, binds, chain);
                    AddRadiusSample(distances, vertices[i], w.boneIndex3, w.weight3, bones, binds, chain);
                }
            }

            sampled = distances.Count;
            if (distances.Count < MinMeshSamples)
            {
                return 0f;
            }
            distances.Sort();
            return distances[distances.Count / 2];
        }

        static void AddRadiusSample(List<float> into, Vector3 vertex, int boneIndex, float weight,
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
            float distance = axis.sqrMagnitude > 1e-10f
                ? Vector3.ProjectOnPlane(local, axis.normalized).magnitude
                : local.magnitude;

            // Bone-local units become world units, which is what MagicaCloth2's radius is in.
            Vector3 scale = bone.lossyScale;
            float mean = (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f;
            if (distance > 0f && mean > 0f)
            {
                into.Add(distance * mean);
            }
        }

        /// <summary>Every transform the chain simulates: the root and its descendants, minus the
        /// branches VRChat's Ignore Transforms cut out.</summary>
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
        /// <summary>
        /// Assigns a reference-typed field. <see cref="TrySetMember"/> goes through
        /// <c>Convert.ChangeType</c>, which throws on anything that isn't <c>IConvertible</c> —
        /// a <c>Transform</c> included — so object references need their own path.
        /// </summary>
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

        /// <summary>
        /// Sets a curve-backed value with a linear shape over the chain's depth, root to tip.
        /// MagicaCloth2 stores these as one value scaled by a 0..1 curve, so a bound that grows
        /// with depth is expressed as the TIP's value with the curve carrying everything above it.
        /// </summary>
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
