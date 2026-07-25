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
    /// It transfers STRUCTURE and nothing else:
    ///
    ///   which bone the chain hangs from     -> rootBones
    ///   which colliders it collides with    -> colliderList
    ///   which transforms to leave out       -> bone attribute "Invalid"
    ///   whether it started enabled          -> the holder's active state
    ///
    /// Every physics value is left at MagicaCloth2's own defaults. That is the whole idea.
    ///
    /// Earlier versions derived MagicaCloth2 settings from PhysBone settings — gravity scaled
    /// into m/s², spring inverted into damping, immobile inverted into inertia, pull folded
    /// into angle restoration. Each mapping looked reasonable and each one had to be walked
    /// back after a real avatar misbehaved, because the two systems are not the same kind of
    /// simulation. PhysBones, like DynamicBone before them, are per-bone ROTATIONAL SPRINGS.
    /// MagicaCloth2 is a PARTICLE POSITION solver: it moves particles through space and reads
    /// bone rotations back out of where they land. A number that means "springiness" to one
    /// does not mean anything in particular to the other, so arithmetic between them produces
    /// confident nonsense.
    ///
    /// So no arithmetic. A stock MagicaCloth2 BoneCloth is a known-good configuration tuned by
    /// the solver's own author, every converted chain behaves the same predictable way, and the
    /// PhysBone's own numbers go into the report for anyone who wants to tune from there.
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
            sdata.rootBones.Add(data.Root);

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

            if (ctx.Settings.transferAngleLimits)
            {
                ApplyAngleLimit(ctx, data, sdata);
            }

            ReportSourceSettings(ctx, data, preset, chainClass, customPreset);
            return cloth;
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
            ctx.Report.Converted(Category, data.Root.name,
                $"BoneCloth on {baseline}, {data.Colliders.Count} collider(s). Source PhysBone was pull " +
                $"{data.Pull:0.##}, spring {data.Spring:0.##}, stiffness {data.Stiffness:0.##}, gravity " +
                $"{data.Gravity:0.##}, immobile {data.Immobile:0.##}, radius {data.Radius:0.###} — none of " +
                "those transfer, because MagicaCloth2 solves particle positions where PhysBones rotate bones. " +
                "Tune the cloth directly if this chain wants a different feel.");

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
