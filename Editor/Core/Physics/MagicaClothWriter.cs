#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;
using MagicaCloth2;

namespace AvatarBridge
{
    /// <summary>
    /// Writes a VRCPhysBone chain as a MagicaCloth2 BoneCloth.
    ///
    /// This is a deliberately plain mapping. It transfers the settings whose meaning is the
    /// same on both sides and reports the rest, rather than trying to reconstruct VRChat
    /// behaviour out of MagicaCloth2 features that merely share a name.
    ///
    ///   pull / stiffness  -> angle restoration stiffness (force back to rest pose)
    ///   spring (momentum) -> damping (inverted) + velocity attenuation
    ///   gravity           -> gravity in m/s^2 (PB 0..1 scaled by ~9.8), sign picks direction
    ///   gravityFalloff    -> gravityFalloff (identical 0..1 semantics)
    ///   immobile          -> world inertia reduction
    ///   radius (+curve)   -> particle radius (+curve), capped so particles cannot overlap
    ///   ignoreTransforms  -> bone attribute "Invalid"
    ///   colliders         -> Magica sphere/capsule/plane colliders
    ///
    /// Everything else — angle limits, stretch & squish, multi-child blending, whether the
    /// chain is animated — is written to the report with its value, for you to apply by hand
    /// on the chains that actually want it.
    ///
    /// The history behind that split is worth keeping: earlier versions mapped several of
    /// those automatically, on the reasoning that the two systems had matching features. They
    /// do not. MagicaCloth2's angle limit constrains particle POSITIONS against a baseline
    /// pose where VRChat's limits bone ROTATION against its parent; its radius is the particle
    /// size that shapes the whole proxy where VRChat's is only a collision radius. Reasoning
    /// from the names produced avatars that shook, snapped back, or inflated to metre-wide
    /// spheres. A value in the report is worth more than a confident wrong setting.
    /// </summary>
    public static class MagicaClothWriter
    {
        const string Category = "PhysBones -> MagicaCloth2";

        // Tunable feel constants; adjust if conversions come out too stiff/loose.
        public static float GravityScale = 9.8f;
        public static float MaxDamping = 0.15f;
        public static float MinDamping = 0.01f;

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

            // Presets carry physics parameters only. MagicaCloth2's own ImportJson deliberately
            // preserves the structural fields — clothType, rootBones, colliderList, updateMode,
            // animationPoseRatio, rootRotation — so applying one neither needs nor destroys the
            // work below, and the order of the two is free.
            string preset = null;
            if (ctx.Settings.useMagicaPresets)
            {
                preset = MagicaPresetLibrary.ChooseFor(data);
                if (!MagicaPresetLibrary.TryApply(sdata, preset, out string presetError))
                {
                    ctx.Report.Approximated(Category, data.Root.name,
                        $"MagicaCloth2 preset not applied — {presetError}. Falling back to values derived " +
                        "from the PhysBone.");
                    preset = null;
                }
            }

            sdata.clothType = ClothProcess.ClothType.BoneCloth;
            sdata.rootBones.Add(data.Root);

            if (preset == null)
            {
                ApplyDerivedParameters(ctx, data, sdata);
            }

            // Particle radius bound, applied whichever route set it. PhysBone's radius only
            // decides what the chain collides with, so a chain with no colliders ignores it and
            // large leftover values are harmless. MagicaCloth2's is the particle size, and a
            // particle wider than the gap between bones overlaps its neighbours, which the
            // solver resolves by shoving them apart. Half the bone spacing is the largest value
            // where particles can touch but never overlap. Presets are authored for roughly
            // human-sized chains, so this matters for them too on a dense or tiny rig.
            float spacing = MeasureBoneSpacing(data.Root);
            if (ctx.Settings.capParticleRadius && spacing > 0f && sdata.radius.value > spacing * 0.5f)
            {
                float was = sdata.radius.value;
                sdata.radius.value = spacing * 0.5f;   // assign directly, keeping any depth curve
                ctx.Report.Approximated(Category, data.Root.name,
                    $"Particle radius {was:0.###} reduced to {sdata.radius.value:0.###} — anything wider than " +
                    "the gap between bones makes neighbouring particles overlap and shove each other apart.");
            }

            if (ctx.Settings.transferAngleLimits)
            {
                ApplyAngleLimit(ctx, data, sdata);
            }

            // Ignored transforms become "Invalid" (excluded) bones.
            if (data.Ignores.Count > 0)
            {
                var sdata2 = cloth.GetSerializeData2();
                foreach (var ignore in data.Ignores)
                {
                    foreach (var t in ignore.GetComponentsInChildren<Transform>(true))
                    {
                        if (!sdata2.boneAttributeDict.ContainsKey(t))
                        {
                            sdata2.boneAttributeDict.Add(t, VertexAttribute.Invalid);
                        }
                    }
                }
                ctx.Report.Approximated(Category, data.Root.name,
                    $"{data.Ignores.Count} ignored transform(s) marked Invalid (their children are excluded too).");
            }

            // Colliders.
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

            ReportUnconvertibleFeatures(ctx, data);
            if (preset != null)
            {
                ctx.Report.Converted(Category, data.Root.name,
                    $"BoneCloth from the MagicaCloth2 \"{MagicaPresetLibrary.DisplayName(preset)}\" preset, " +
                    $"{data.Colliders.Count} collider(s). Source PhysBone was pull {data.Pull:0.##}, spring " +
                    $"{data.Spring:0.##}, gravity {data.Gravity:0.##}, immobile {data.Immobile:0.##} — tune " +
                    "from the preset if this chain wants a different feel.");
            }
            else
            {
                ctx.Report.Converted(Category, data.Root.name,
                    $"BoneCloth with {data.Colliders.Count} collider(s).");
            }

            return cloth;
        }

        /// <summary>
        /// The direct PhysBone-value mapping, used when presets are switched off or the preset
        /// files aren't in the project. Transfers only what means the same on both sides.
        /// </summary>
        static void ApplyDerivedParameters(BridgeContext ctx, PhysBoneChainData data, ClothSerializeData sdata)
        {
            ApplyCurve(sdata.radius, Mathf.Max(0.001f, data.Radius), data.RadiusCurve);

            // Restoration toward the rest pose: PB pull, plus stiffness in advanced mode.
            float restoration = Mathf.Clamp01(Mathf.Max(data.Pull, data.Stiffness));
            ApplyCurve(sdata.angleRestorationConstraint.stiffness, restoration,
                PhysBoneChainData.HasCurve(data.PullCurve) ? data.PullCurve : data.StiffnessCurve);

            // Springiness: high PB spring = wobbly = low damping / low attenuation.
            float spring = Mathf.Clamp01(data.Spring);
            sdata.damping.SetValue(Mathf.Lerp(MaxDamping, MinDamping, spring));
            sdata.angleRestorationConstraint.velocityAttenuation = Mathf.Clamp01(1f - spring);

            // Gravity. PhysBone's 0..1 is a fraction of real gravity; a negative value points up.
            if (!Mathf.Approximately(data.Gravity, 0f))
            {
                sdata.gravity = Mathf.Abs(data.Gravity) * GravityScale;
                sdata.gravityDirection = new Unity.Mathematics.float3(0f, data.Gravity >= 0f ? -1f : 1f, 0f);
                sdata.gravityFalloff = Mathf.Clamp01(data.GravityFalloff);
            }
            else
            {
                sdata.gravity = 0f;
            }

            // Immobile: reduce how much the avatar's own movement shakes the chain. Applied to
            // world inertia only — MagicaCloth2 splits world and local inertia, but which of
            // them a given PhysBone immobile type corresponds to is a guess.
            if (data.Immobile > 0f)
            {
                if (!TrySetMember(sdata.inertiaConstraint, "worldInertia", Mathf.Clamp01(1f - data.Immobile)))
                {
                    ctx.Report.Approximated(Category, data.Root.name,
                        "Immobile could not be mapped to inertia on this MagicaCloth2 version.");
                }
                else if (!string.IsNullOrEmpty(data.ImmobileTypeName))
                {
                    ctx.Report.Approximated(Category, data.Root.name,
                        $"Immobile {data.Immobile:0.##} (type '{data.ImmobileTypeName}') applied as world inertia. " +
                        "If the chain still drags when you walk, raise Local Inertia on the cloth too.");
                }
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

        /// <summary>
        /// Everything the mapping deliberately leaves alone, reported with its value so it can
        /// be applied by hand where a chain actually needs it.
        /// </summary>
        static void ReportUnconvertibleFeatures(BridgeContext ctx, PhysBoneChainData data)
        {
            if (data.LimitTypeName != "None" && !string.IsNullOrEmpty(data.LimitTypeName)
                && !ctx.Settings.transferAngleLimits)
            {
                bool polar = data.LimitTypeName == "Polar";
                float limitAngle = polar ? Mathf.Max(data.MaxAngleX, data.MaxAngleZ) : data.MaxAngleX;
                ctx.Report.Skipped(Category, data.Root.name,
                    $"{data.LimitTypeName} limit ({limitAngle:0}°) not applied — MagicaCloth2's angle limit " +
                    "constrains particle positions against a baseline pose rather than bone rotation, and on a " +
                    "chain that also follows animation it fights it rather than settling. To add it anyway, tick " +
                    $"Angle Limit on this cloth, set Limit Angle to {limitAngle:0} and lower Stiffness until it " +
                    "stops snapping.");
            }

            if (data.MaxStretch > 0f || data.MaxSquish > 0f)
            {
                ctx.Report.Skipped(Category, data.Root.name,
                    $"Stretch & Squish (max stretch {data.MaxStretch:0.##}, max squish {data.MaxSquish:0.##}) is " +
                    "not converted — MagicaCloth2's BoneCloth keeps each bone at its rest length, so a chain " +
                    "swings but never lengthens or compresses. Chains that leaned on stretching will sit tighter " +
                    "than they did in VRChat.");
            }

            if (data.IsAnimated)
            {
                ctx.Report.Approximated(Category, data.Root.name,
                    "Source PhysBone had 'Is Animated' on. The cloth settles back to its INITIAL pose; if an " +
                    "animation moves these bones and the chain fights it, set Animation Pose Ratio to 1 on this " +
                    "cloth so it settles to the animated pose instead.");
            }

            if (data.RootHasMultipleChildren && !string.IsNullOrEmpty(data.MultiChildTypeName)
                && data.MultiChildTypeName != "Ignore")
            {
                ctx.Report.Approximated(Category, data.Root.name,
                    $"Multi Child Type '{data.MultiChildTypeName}' has no MagicaCloth2 equivalent — every branch " +
                    "off this root simulates independently, where VRChat blended them.");
            }
            else if (data.RootHasMultipleChildren && data.MultiChildTypeName == "Ignore")
            {
                ctx.Report.Approximated(Category, data.Root.name,
                    "Multi Child Type 'Ignore' pins a branching root in VRChat. If this root swings when it " +
                    "shouldn't, set Root Rotation to 0 on this cloth.");
            }

            if (!string.IsNullOrEmpty(data.Parameter))
            {
                if (ctx.Settings.grabbyBonesSupport)
                {
                    ctx.Report.Approximated(Category, data.Root.name,
                        $"PhysBone parameter \"{data.Parameter}\": _IsGrabbed and _Angle work via the GrabbyBones " +
                        "mod (cloth object named to match). _Stretch/_Squish/_IsPosed have no equivalent.");
                }
                else
                {
                    ctx.Report.Skipped(Category, data.Root.name,
                        $"PhysBone parameter \"{data.Parameter}\" (_IsGrabbed/_Angle/_Stretch) has no CVR equivalent.");
                }
            }
        }

        /// <summary>
        /// The 1.1.2 angle-limit transfer, behind the "Transfer angle limits" option.
        ///
        /// MagicaCloth2's limit constrains particle POSITIONS against a baseline pose where
        /// PhysBone's constrains bone ROTATION against its parent, and MagicaCloth2's stiffness
        /// defaults to 1 — a rigid snap-back. On a chain that is also animated, that fights the
        /// animation every frame, which is what wrecked one test avatar's jiggle chains. On
        /// another avatar the same transfer gave the best result the tool has produced. It is
        /// avatar-dependent, so it is offered rather than decided; lower Stiffness on the cloth
        /// if a chain snaps.
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

        static void ApplyCurve(CurveSerializeData target, float value, AnimationCurve curve)
        {
            if (PhysBoneChainData.HasCurve(curve))
            {
                target.SetValue(value, Mathf.Clamp01(curve.Evaluate(0f)), Mathf.Clamp01(curve.Evaluate(1f)), true);
            }
            else
            {
                target.SetValue(value);
            }
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

        static string PathOf(Transform t) => t != null ? t.name : "(null)";
    }
}
#endif
