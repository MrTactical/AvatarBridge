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
    /// Mapping (VRC semantics -> MagicaCloth2):
    ///   pull / stiffness  -> angle restoration stiffness (force back to rest pose)
    ///   spring (momentum) -> damping (inverted) + velocity attenuation
    ///   gravity           -> gravity in m/s^2 (PB 0..1 scaled by ~9.8)
    ///   gravityFalloff    -> gravityFalloff (identical 0..1 semantics)
    ///   immobile          -> world inertia reduction
    ///   radius (+curve)   -> particle radius (+curve)
    ///   limitType Angle   -> angle limit
    ///   ignoreTransforms  -> bone attribute "Invalid"
    ///   colliders         -> Magica sphere/capsule/plane colliders
    /// </summary>
    public static class MagicaClothWriter
    {
        const string Category = "PhysBones -> MagicaCloth2";

        // Tunable feel constants. For reference when adjusting these, MagicaCloth2's own
        // defaults are: gravity 5.0, damping 0.05, radius 0.02, angle-restoration stiffness
        // 0.2 (tapering 1.0 at the root to 0.2 at the tip), velocity attenuation 0.8,
        // world/local inertia 1.0.

        /// <summary>
        /// PhysBone gravity is a 0..1 fraction of standard gravity, MagicaCloth2's is an
        /// acceleration, so a PhysBone at 1.0 converts to real gravity. Note this lands above
        /// Magica's own default of 5.0 — that default is an artistic choice for cloth, whereas
        /// the goal here is to reproduce what the PhysBone was actually doing.
        /// </summary>
        public static float GravityScale = 9.81f;

        // PhysBone "spring" is bounciness, which is the inverse of air resistance. The range
        // brackets Magica's 0.05 default so a mid-range PhysBone lands near Magica-native feel.
        public static float MaxDamping = 0.15f;
        public static float MinDamping = 0.01f;

        public static void Write(BridgeContext ctx, PhysBoneChainData data,
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

            sdata.clothType = ClothProcess.ClothType.BoneCloth;
            sdata.rootBones.Add(data.Root);

            // Particle radius.
            ApplyCurve(sdata.radius, Mathf.Max(0.001f, data.Radius), data.RadiusCurve);

            // Restoration toward the animated pose: PB pull, plus stiffness in advanced mode.
            float restoration = Mathf.Clamp01(Mathf.Max(data.Pull, data.Stiffness));
            ApplyCurve(sdata.angleRestorationConstraint.stiffness, restoration,
                PhysBoneChainData.HasCurve(data.PullCurve) ? data.PullCurve : data.StiffnessCurve);

            // Springiness: high PB spring = wobbly = low damping / low attenuation.
            float spring = Mathf.Clamp01(data.Spring);
            sdata.damping.SetValue(Mathf.Lerp(MaxDamping, MinDamping, spring));
            sdata.angleRestorationConstraint.velocityAttenuation = Mathf.Clamp01(1f - spring);

            // Gravity.
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

            // Immobile: how much the chain ignores being moved around.
            // PhysBone's immobileType decides *which* movement is ignored, and MagicaCloth2
            // splits exactly the same way — world inertia is the character travelling through
            // the world, local inertia is animation moving the bones. Mapping onto the matching
            // axis is a 1:1 conversion; applying both for "World" would wrongly deaden the
            // chain against the avatar's own animation.
            if (data.Immobile > 0f)
            {
                float inertia = Mathf.Clamp01(1f - data.Immobile);
                bool allMotion = !data.ImmobileTypeName.Contains("World");

                bool applied = TrySetMember(sdata.inertiaConstraint, "worldInertia", inertia);
                if (allMotion)
                {
                    // "All Motion" also damps the animation's own movement.
                    applied &= TrySetMember(sdata.inertiaConstraint, "localInertia", inertia);
                }
                if (!applied)
                {
                    ctx.Report.Approximated(Category, data.Root.name,
                        "Immobile could not be mapped to inertia on this MagicaCloth2 version.");
                }
            }

            // Angle limits. Only the axes the limit type actually uses are read: an 'Angle'
            // (cone) or 'Hinge' limit is defined by maxAngleX alone, and taking the larger of
            // X/Z there would let a stale Z value quietly widen the limit.
            if (data.LimitTypeName != "None")
            {
                bool polar = data.LimitTypeName == "Polar";
                float limitAngle = polar ? Mathf.Max(data.MaxAngleX, data.MaxAngleZ) : data.MaxAngleX;

                bool applied = TrySetMember(sdata.angleLimitConstraint, "useAngleLimit", true) &&
                               TrySetCurveValue(sdata.angleLimitConstraint, "limitAngle", limitAngle);
                if (!applied)
                {
                    ctx.Report.Skipped(Category, data.Root.name,
                        $"Angle limit ({data.LimitTypeName}) could not be applied on this MagicaCloth2 version.");
                }
                else if (polar && !Mathf.Approximately(data.MaxAngleX, data.MaxAngleZ))
                {
                    // Magica's limit is a single cone, so a two-axis polar limit loses its shape.
                    ctx.Report.Approximated(Category, data.Root.name,
                        $"Polar limit ({data.MaxAngleX:0}° / {data.MaxAngleZ:0}°) became a symmetric " +
                        $"{limitAngle:0}° cone — MagicaCloth2 limits a single angle per bone.");
                }
                else if (data.LimitTypeName == "Hinge")
                {
                    ctx.Report.Approximated(Category, data.Root.name,
                        $"Hinge limit became a {limitAngle:0}° cone — MagicaCloth2 has no single-axis " +
                        "hinge, so the bone can also swing off-axis.");
                }
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
            ctx.Report.Converted(Category, data.Root.name,
                $"BoneCloth with {data.Colliders.Count} collider(s).");
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

        static void ReportUnconvertibleFeatures(BridgeContext ctx, PhysBoneChainData data)
        {
            if (data.MaxStretch > 0f)
            {
                ctx.Report.Skipped(Category, data.Root.name,
                    $"Max Stretch ({data.MaxStretch:0.##}) is not converted — MagicaCloth2's BoneCloth keeps " +
                    "each bone at its rest length, so a chain can swing but never stretch. Chains that relied " +
                    "on stretching for their look will sit tighter than they did in VRChat.");
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
