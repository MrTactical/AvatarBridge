#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_DYNBONE
using System.Collections.Generic;
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
    ///   gravity   -> m_Gravity/m_Force split by gravityFalloff using the relation
    ///                gravity² = m_Gravity² + m_Force² (mirrors VRChat's own
    ///                DynamicBone->PhysBone import math, inverted)
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
            if (!data.InitiallyActive)
            {
                db.enabled = false;
                ctx.Report.Approximated(Category, data.Root.name,
                    "Source PhysBone was disabled; DynamicBone created disabled. Animator toggles that enabled it are not re-wired.");
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

            // Split PB gravity into DB gravity (with falloff at rest) and force (constant),
            // preserving overall magnitude.
            float g = Mathf.Abs(data.Gravity) * GravityScale;
            if (g > 0f)
            {
                float sign = data.Gravity >= 0f ? -1f : 1f;
                float falloff = Mathf.Clamp01(data.GravityFalloff);
                db.m_Gravity = new Vector3(0f, sign * g * Mathf.Sqrt(falloff), 0f);
                db.m_Force = new Vector3(0f, sign * g * Mathf.Sqrt(1f - falloff), 0f);
            }

            db.m_EndOffset = data.EndpointPosition;
            db.m_Exclusions = new List<Transform>(data.Ignores);

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
                $"DynamicBone with {data.Colliders.Count} collider(s).");
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
