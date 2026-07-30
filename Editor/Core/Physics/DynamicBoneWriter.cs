#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_DYNBONE
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge
{
    /// <summary>
    /// Fallback writer: VRCPhysBone -> DynamicBone (ChilloutVR supports DynamicBone
    /// natively; the VRLabs stub also works for conversion-only projects).
    ///
    /// Mapping notes:
    ///   pull      -> m_Elasticity (scaled; DB's useful elasticity range is much smaller)
    ///   spring    -> m_Damping (inverted)
    ///   stiffness -> m_Stiffness
    ///   immobile  -> m_Inert
    ///   gravity   -> m_Force only, scaled by ElasticityScale so the force/restore balance —
    ///                and therefore the resting pose — matches VRChat; m_Gravity is forced to
    ///                zero because ChilloutVR's rest-pose cancellation for it is
    ///                scale-dependent (see the comment on the gravity block below)
    ///   curves    -> distribution curves (identical multiplier-along-chain semantics)
    /// </summary>
    public static class DynamicBoneWriter
    {
        const string Category = "PhysBones -> DynamicBone";

        // Tunable feel constants.
        public static float ElasticityScale = 0.2f;
        public static float MaxDamping = 0.2f;
        public static float MinDamping = 0.02f;
        public static float GravityScale = 1.0f;

        public static void Write(BridgeContext ctx, PhysBoneChainData data,
            Dictionary<VRCPhysBoneCollider, DynamicBoneColliderBase> colliderCache)
        {
            var db = data.SourceGameObject.AddComponent<DynamicBone>();
            db.m_Root = data.Root;
            // Same GameObject as the source PhysBone, so animations that toggle the OBJECT keep
            // working untouched; animations that toggled the PhysBone COMPONENT are re-wired to
            // this component by AnimatorMerger via this registration.
            ctx.ConvertedPhysicsChains.Add(new BridgeContext.ConvertedPhysicsChain
            {
                Source = data.SourceGameObject,
                Host = data.SourceGameObject,
                Physics = db,
                Root = data.Root
            });
            if (!data.InitiallyActive)
            {
                db.enabled = false;
                ctx.Report.Approximated(Category, data.Root.name,
                    "Source PhysBone was disabled; DynamicBone created disabled. Animator toggles that " +
                    "activated the original object or component keep working — object toggles hit this " +
                    "same GameObject, and component toggles are re-wired by the animator pass.");
            }

            db.m_Elasticity = Mathf.Clamp01(data.Pull) * ElasticityScale;
            if (PhysBoneChainData.HasCurve(data.PullCurve))
            {
                db.m_ElasticityDistrib = new AnimationCurve(data.PullCurve.keys);
            }

            db.m_Damping = Mathf.Lerp(MaxDamping, MinDamping, Mathf.Clamp01(data.Spring));

            db.m_Stiffness = Mathf.Clamp01(data.Stiffness);
            if (PhysBoneChainData.HasCurve(data.StiffnessCurve))
            {
                db.m_StiffnessDistrib = new AnimationCurve(data.StiffnessCurve.keys);
            }

            db.m_Inert = Mathf.Clamp01(data.Immobile);
            if (PhysBoneChainData.HasCurve(data.ImmobileCurve))
            {
                db.m_InertDistrib = new AnimationCurve(data.ImmobileCurve.keys);
            }

            db.m_Radius = data.Radius;
            if (PhysBoneChainData.HasCurve(data.RadiusCurve))
            {
                db.m_RadiusDistrib = new AnimationCurve(data.RadiusCurve.keys);
            }

            // PhysBone gravity, applied entirely through m_Force.
            //
            // DynamicBone splits the idea across two fields: m_Gravity cancels out the part
            // already baked into the character's rest pose ("partial force apply to character's
            // initial pose is cancelled out"), while m_Force is a plain constant pull. This used
            // to mirror that split, sending gravityFalloff to m_Gravity and the remainder to
            // m_Force with gravity² = m_Gravity² + m_Force².
            //
            // ChilloutVR's m_Gravity is not safe to use. In Zettai/UpdateParticlesJob.GetForce
            // the rest-pose cancellation is divided by the avatar's lossy scale while gravity
            // itself is multiplied by it:
            //
            //     (gravity - dir * max(dot(x, dir), 0) / scale + m_Force + wind) * scale * dt
            //
            // The two only balance at scale 1. AvatarBridge injects a height scaler, so converted
            // avatars sit off that point by construction, and below scale 1 the cancellation term
            // can exceed gravity and push bones upward. m_Force is added after the cancellation
            // and scales uniformly, so it behaves the same at any scale. A tester reported this as
            // "CVR doesn't play nicely with them at all".
            //
            // The two halves were collinear parts of one magnitude, so collapsing them into one
            // field means the full magnitude — summing the halves would overshoot it by up to 41%.
            // Added rather than assigned so any force already on the component still composes.
            //
            // Scaled by ElasticityScale, and that factor is load-bearing: where a chain settles
            // is the BALANCE of constant force against elastic restore, and the restore was
            // already scaled down by ElasticityScale to sit in DynamicBone's useful range.
            // Carrying the force over at full strength made every gravity-tinted chain deflect
            // ~5x further than VRChat — a tester's tail with gravity -0.07 (a gentle upward
            // bias in VRChat) converted to a force that pinned the tail at the sky. Scaling
            // force and restore by the same factor preserves the resting pose exactly.
            float g = Mathf.Abs(data.Gravity) * GravityScale * ElasticityScale;
            if (g > 0f)
            {
                float sign = data.Gravity >= 0f ? -1f : 1f;
                db.m_Gravity = Vector3.zero;
                db.m_Force += new Vector3(0f, sign * g, 0f);

                // Falloff is the one thing that cannot survive the move: it only existed as
                // m_Gravity's rest-pose cancellation. A chain the author expected to hang almost
                // weightlessly at rest now feels the full pull all the time.
                if (Mathf.Clamp01(data.GravityFalloff) > 0.01f)
                {
                    ctx.Report.Approximated(Category, data.Root.name,
                        $"Gravity falloff {data.GravityFalloff:0.##} is not preserved. ChilloutVR's " +
                        "rest-pose gravity cancellation is scale-dependent and misbehaves on scaled " +
                        "avatars, so the whole pull is applied as a constant force instead. This chain " +
                        "will hang lower at rest than it did in VRChat; lower its Gravity to compensate.");
                }
            }

            db.m_EndOffset = data.EndpointPosition;
            db.m_Exclusions = new List<Transform>(data.Ignores);
            if (data.HumanoidExclusions.Count > 0)
            {
                ctx.Report.Approximated(Category, data.Root.name,
                    $"{data.HumanoidExclusions.Count} humanoid-mapped bone(s) added to the exclusions — " +
                    $"{string.Join(", ", data.HumanoidExclusions.Take(4).Select(t => t.name))}" +
                    $"{(data.HumanoidExclusions.Count > 4 ? ", …" : "")}. The animator and IK drive " +
                    "humanoid bones every frame, so simulating one fights them for the transform.");
            }

            if (data.ToeExclusions.Count > 0)
            {
                ctx.Report.Approximated(Category, data.Root.name,
                    $"{data.ToeExclusions.Count} toe branch(es) added to the exclusions — " +
                    $"{string.Join(", ", data.ToeExclusions.Take(4).Select(t => t.name))}" +
                    $"{(data.ToeExclusions.Count > 4 ? ", …" : "")} (with everything under them). " +
                    "Simulated toes splay and swing while the foot itself is planted by IK, which " +
                    "reads as broken feet rather than as physics. Turn on \"Convert toe PhysBones\" " +
                    "in the physics options if this avatar's toe physics are deliberate.");
            }

            if (data.Colliders.Count > 0)
            {
                db.m_Colliders = new List<DynamicBoneColliderBase>();
                foreach (var pbCollider in data.Colliders)
                {
                    var collider = GetOrCreateCollider(ctx, pbCollider, colliderCache);
                    if (collider != null && !db.m_Colliders.Contains(collider))
                    {
                        db.m_Colliders.Add(collider);
                    }
                }
            }

            if (data.LimitTypeName != "None")
            {
                ctx.Report.Skipped(Category, data.Root.name,
                    $"Limit type '{data.LimitTypeName}' has no DynamicBone equivalent.");
            }
            if (data.MaxStretch > 0f)
            {
                ctx.Report.Skipped(Category, data.Root.name, "Max Stretch is not converted.");
            }
            if (!string.IsNullOrEmpty(data.Parameter))
            {
                ctx.Report.Skipped(Category, data.Root.name,
                    $"PhysBone parameter \"{data.Parameter}\" has no CVR equivalent.");
            }

            ctx.Report.Converted(Category, data.Root.name,
                $"DynamicBone with {data.Colliders.Count} collider(s). Source PhysBone was " +
                $"pull {data.Pull:0.##}, spring {data.Spring:0.##}, stiffness {data.Stiffness:0.##}, " +
                $"gravity {data.Gravity:0.##}, immobile {data.Immobile:0.##}, radius {data.Radius:0.###}. " +
                "Tune the DynamicBone directly if this chain wants a different feel.");
        }

        static DynamicBoneColliderBase GetOrCreateCollider(BridgeContext ctx, VRCPhysBoneCollider pbCollider,
            Dictionary<VRCPhysBoneCollider, DynamicBoneColliderBase> cache)
        {
            if (cache.TryGetValue(pbCollider, out var cached))
            {
                return cached;
            }

            Transform parent = pbCollider.rootTransform != null ? pbCollider.rootTransform : pbCollider.transform;
            string shape = pbCollider.shapeType.ToString();

            var go = new GameObject("DBCollider_" + parent.name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = pbCollider.rotation;

            DynamicBoneColliderBase collider;
            if (shape.Contains("Plane"))
            {
                var plane = go.AddComponent<DynamicBonePlaneCollider>();
                plane.m_Center = pbCollider.position;
                plane.m_Direction = DynamicBoneColliderBase.Direction.Y;
                collider = plane;
            }
            else
            {
                var round = go.AddComponent<DynamicBoneCollider>();
                round.m_Center = pbCollider.position;
                round.m_Radius = pbCollider.radius;
                round.m_Height = shape.Contains("Capsule") ? pbCollider.height : 0f;
                round.m_Direction = DynamicBoneColliderBase.Direction.Y;
                round.m_Bound = pbCollider.insideBounds
                    ? DynamicBoneColliderBase.Bound.Inside
                    : DynamicBoneColliderBase.Bound.Outside;
                collider = round;
            }

            ctx.Report.Converted("PhysBone colliders", parent.name, shape + " -> DynamicBone collider");
            cache[pbCollider] = collider;
            return collider;
        }
    }
}
#endif
