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
            var holder = new GameObject(holderName);
            holder.transform.SetParent(ctx.Target.transform, false);
            if (!data.InitiallyActive)
            {
                holder.SetActive(false);
                ctx.Report.Approximated(Category, data.Root.name,
                    "Source PhysBone was disabled; cloth created disabled. Animator toggles that enabled it are not re-wired.");
            }

            var cloth = holder.AddComponent<MagicaCloth>();
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

            if (data.Ignores.Count == 0)
            {
                sdata.rootBones.Add(data.Root);
            }
            else
            {
                WriteRootsExcluding(ctx, sdata, data);
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

            if (ctx.Settings.derivePhysicsFromPhysBone)
            {
                DerivePhysics(ctx, data, sdata);
            }

            if (ctx.Settings.fitToPhysBone)
            {
                FitToPhysBone(ctx, data, sdata);
            }

            if (ctx.Settings.transferAngleLimits)
            {
                ApplyAngleLimit(ctx, data, sdata);
            }

            ReportSourceSettings(ctx, data, preset, chainClass, customPreset);
            return cloth;
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

            if (sdata.rootBones.Count == 0)
            {
                // Everything under the root was ignored. Nothing to simulate, and a BoneCloth with
                // no roots errors at runtime, so fall back to the root and say what happened.
                sdata.rootBones.Add(data.Root);
                ctx.Report.Warning(Category, data.Root.name,
                    $"All {data.Ignores.Count} branch(es) under this PhysBone's root were in its Ignore " +
                    "Transforms list, leaving nothing to simulate. The chain was left rooted as-is, which " +
                    "means it now simulates bones VRChat excluded — delete this cloth if it misbehaves.");
                return;
            }

            var names = sdata.rootBones.Select(b => b.name).Take(6).ToList();
            ctx.Report.Approximated(Category, data.Root.name,
                $"{data.Ignores.Count} Ignore Transform(s) honoured by rooting the cloth at " +
                $"{sdata.rootBones.Count} branch(es) instead: {string.Join(", ", names)}" +
                $"{(sdata.rootBones.Count > names.Count ? ", …" : "")}. MagicaCloth2 has no ignore list — " +
                "every root simulates its whole subtree — so the excluded bones are left out by not " +
                "rooting anything above them. MagicaCloth2 holds a root bone still, so each of these " +
                "branches now bends from its second joint rather than its first; if one feels stiff at " +
                "the base, that is why.");
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
                ? "Pull, spring and stiffness were converted into damping and angle restoration; gravity, " +
                  "immobile and radius are handled separately. Tune the cloth directly if this chain wants " +
                  "a different feel."
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
                ctx.Report.Approximated(Category, data.Root.name,
                    "Source PhysBone had 'Is Animated' on. The cloth settles back to its INITIAL pose; if an " +
                    "animation moves these bones and the chain fights it, set Animation Pose Ratio to 1 on this " +
                    "cloth so it settles to the animated pose instead.");
            }

            if (data.RootHasMultipleChildren && !string.IsNullOrEmpty(data.MultiChildTypeName))
            {
                ctx.Report.Approximated(Category, data.Root.name, data.MultiChildTypeName == "Ignore"
                    ? "Multi Child Type 'Ignore' pins a branching root in VRChat. If this root swings when it " +
                      "shouldn't, set Root Rotation to 0 on this cloth."
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
            PhysBoneSolverMap.MapCurve(dampRoot, dampTip,
                out float dampValue, out float dampStart, out float dampEnd, out bool dampCurve);
            sdata.damping.SetValue(dampValue, dampStart, dampEnd, dampCurve);

            float restRoot = PhysBoneSolverMap.RestorationStiffness(
                pullRoot, springRoot, stiffRoot, advanced, out bool satRoot);
            float restTip = PhysBoneSolverMap.RestorationStiffness(
                pullTip, springTip, stiffTip, advanced, out bool satTip);
            PhysBoneSolverMap.MapCurve(restRoot, restTip,
                out float restValue, out float restStart, out float restEnd, out bool restCurve);

            sdata.angleRestorationConstraint.useAngleRestoration = restValue > 0.0001f;
            sdata.angleRestorationConstraint.stiffness.SetValue(restValue, restStart, restEnd, restCurve);

            ctx.Report.Approximated(Category, data.Root.name,
                $"Physics derived from the PhysBone ({(advanced ? "Advanced" : "Simplified")} integration): " +
                $"damping {dampValue:0.###}, angle restoration {restValue:0.###}. Both solvers integrate " +
                "positions per step at a fixed rate, so PhysBone's 60 Hz coefficients were re-expressed at " +
                "MagicaCloth2's 90 Hz. This replaces the preset's feel — if the chain moves wrong, turning " +
                "\"Derive physics from PhysBone\" off restores it.");

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
        /// derived mapping is on. **Categorical ones**: "this never falls", "this falls upward" — both systems
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

            // immobile -> world influence. Same 0..1 question on both sides ("how much does the
            // avatar moving shake this chain"), opposite polarity. Only applied when the author
            // actually set it, so a chain they left alone keeps the preset's own tuning.
            if (data.Immobile > 0.01f)
            {
                float influence = Mathf.Clamp01(1f - data.Immobile);
                if (TrySetMember(sdata.inertiaConstraint, "worldInertia", influence))
                {
                    ctx.Report.Approximated(Category, data.Root.name,
                        $"World influence set to {influence:0.##} — the source PhysBone was " +
                        $"{data.Immobile:0.##} immobile, and MagicaCloth2 measures the same thing the " +
                        "other way round.");
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
            cache[pbCollider] = collider;
            return collider;
        }

        /// <summary>Average distance between bones down the chain, used to bound the particle radius.</summary>
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
