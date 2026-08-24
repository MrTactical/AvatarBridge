#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_DYNBONE
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge
{
    // Fallback writer: VRCPhysBone -> DynamicBone (ChilloutVR supports DynamicBone
    // natively; the VRLabs stub also works for conversion-only projects).
    //
    // Mapping notes:
    //   pull      -> m_Elasticity (scaled; DB's useful elasticity range is much smaller)
    //   spring    -> m_Damping (inverted)
    //   stiffness -> m_Stiffness
    //   immobile  -> m_Inert
    //   gravity   -> m_Force only, scaled by ElasticityScale so the force/restore balance .
    //                and therefore the resting pose; matches VRChat; m_Gravity is forced to
    //                zero because ChilloutVR's rest-pose cancellation for it is
    //                scale-dependent (see the comment on the gravity block below)
    //   curves    -> distribution curves (identical multiplier-along-chain semantics)
    public static class DynamicBoneWriter
    {
        const string Category = "PhysBones -> DynamicBone";
        public const string CollectionName = "DynamicBone Phys";

        // Tunable feel constants.
        public static float ElasticityScale = 0.2f;
        public static float MaxDamping = 0.2f;
        public static float MinDamping = 0.02f;
        public static float GravityScale = 1.0f;

        public static void Write(BridgeContext ctx, PhysBoneChainData data,
            Dictionary<VRCPhysBoneCollider, DynamicBoneColliderBase> colliderCache)
        {
            // Collected under one object beside the avatar, the way the cloth
            // writer does it. m_Root is what a DynamicBone simulates, so
            // where the component itself lives does not matter to it.
            //
            // One object PER CHAIN, not one shared object: an animation
            // binding names a path and a component TYPE, so two DynamicBones
            // on the same GameObject could not be toggled apart.
            var home = PhysBoneConverter.CollectionUnder(ctx, CollectionName);
            var holder = new GameObject(PhysBoneConverter.UniqueChildName(home, "DynamicBone_" + data.Root.name));
            holder.transform.SetParent(home, false);
            var db = holder.AddComponent<DynamicBone>();
            db.m_Root = data.Root;
            // An object toggle no longer carries this for free; the animator
            // pass re-wires both object and component toggles onto the
            // component through the registration below.
            ctx.ConvertedPhysicsChains.Add(new BridgeContext.ConvertedPhysicsChain
            {
                Source = data.SourceGameObject,
                Host = holder,
                Physics = db,
                Root = data.Root
            });
            // Created active with the off state on the component, so a
            // toggle can switch it back on: a component enabled on an
            // inactive object never runs.
            if (!data.ComponentEnabled)
            {
                db.enabled = false;
                ctx.Report.Approximated(Category, data.Root.name,
                    "Source PhysBone component was disabled; DynamicBone created disabled. Component " +
                    "toggles are re-wired by the animator pass.");
            }
            else if (!data.InitiallyActive)
            {
                db.enabled = false;
                ctx.Report.Approximated(Category, data.Root.name,
                    "Style was inactive at conversion; DynamicBone created disabled. Its toggle is " +
                    "re-wired to switch this chain on — see the Animator section of this report.");
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
            // The same growth the MagicaCloth path applies: a slider
            // that grows the body past the authored radius leaves the
            // chain colliding with a body that is not the one shown.
            if (ctx.Settings.sizePhysicsForLargest && data.Root != null)
            {
                var rootScale = data.Root.lossyScale;
                float rootMean = (Mathf.Abs(rootScale.x) + Mathf.Abs(rootScale.y) + Mathf.Abs(rootScale.z)) / 3f;
                float worldRadius = data.Radius * Mathf.Max(rootMean, 1e-4f);
                float push = MeshGrowth.Around(ctx, data.Root.position,
                    Mathf.Max(worldRadius * 2.5f, 0.06f));
                if (push >= 0.005f && worldRadius > 0f)
                {
                    float growth = Mathf.Min((worldRadius + push) / worldRadius, 3f);
                    db.m_Radius *= growth;
                    ctx.Report.Converted(Category, data.Root.name,
                        $"Chain radius grown ×{growth:0.00} ({push * 100f:0.#} cm of surface travel) " +
                        "for the largest the sliders make the body — measured at rest and with " +
                        "every animated shape at full reach.");
                }
            }

            // Gravity, all through m_Force; see docs/SolverCalibration.md.
            db.m_Gravity = Vector3.zero;

            float g = Mathf.Abs(data.Gravity) * GravityScale * ElasticityScale;
            if (g > 0f)
            {
                float sign = data.Gravity >= 0f ? -1f : 1f;
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
            go.transform.localPosition = pbCollider.position;
            go.transform.localRotation = pbCollider.rotation;

            DynamicBoneColliderBase collider;
            if (shape.Contains("Plane"))
            {
                var plane = go.AddComponent<DynamicBonePlaneCollider>();
                plane.m_Center = Vector3.zero;
                plane.m_Direction = DynamicBoneColliderBase.Direction.Y;
                collider = plane;
            }
            else
            {
                var round = go.AddComponent<DynamicBoneCollider>();
                round.m_Center = Vector3.zero;
                round.m_Radius = pbCollider.radius;
                round.m_Height = shape.Contains("Capsule") ? pbCollider.height : 0f;
                round.m_Direction = DynamicBoneColliderBase.Direction.Y;
                round.m_Bound = pbCollider.insideBounds
                    ? DynamicBoneColliderBase.Bound.Inside
                    : DynamicBoneColliderBase.Bound.Outside;
                // An inside-bound collider is a cage; growing the body
                // it contains would shrink the room inside, so only
                // ordinary colliders follow the sliders.
                if (ctx.Settings.sizePhysicsForLargest
                    && round.m_Bound == DynamicBoneColliderBase.Bound.Outside)
                {
                    var goScale = go.transform.lossyScale;
                    float goMean = (Mathf.Abs(goScale.x) + Mathf.Abs(goScale.y) + Mathf.Abs(goScale.z)) / 3f;
                    float worldRadius = round.m_Radius * Mathf.Max(goMean, 1e-4f);
                    float push = MeshGrowth.Around(ctx,
                        go.transform.TransformPoint(round.m_Center),
                        Mathf.Max(worldRadius * 2.5f, 0.06f));
                    if (push >= 0.005f && worldRadius > 0f)
                    {
                        float growth = Mathf.Min((worldRadius + push) / worldRadius, 3f);
                        round.m_Radius *= growth;
                        round.m_Height *= growth;
                        ctx.Report.Converted("PhysBone colliders", parent.name,
                            $"Collider grown ×{growth:0.00} ({push * 100f:0.#} cm of surface travel) " +
                            "for the largest the sliders make the body — measured at rest and with " +
                            "every animated shape at full reach.");
                    }
                }
                collider = round;
            }

            ctx.Report.Converted("PhysBone colliders", parent.name, shape + " -> DynamicBone collider");
            PhysBoneConverter.RecordColliderHost(ctx, pbCollider, go);
            cache[pbCollider] = collider;
            return collider;
        }
    }
}
#endif
