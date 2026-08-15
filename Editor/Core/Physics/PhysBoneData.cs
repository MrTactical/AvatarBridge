#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge
{
    // SDK-agnostic snapshot of one VRCPhysBone chain, so the MagicaCloth2 and
    // DynamicBone writers don't each need to know the VRC API.
    // All 0..1 factors keep VRChat semantics; the writers do the mapping.
    public class PhysBoneChainData
    {
        public VRCPhysBone Source;
        public GameObject SourceGameObject;
        public Transform Root;
        public bool Synthesized;
        // VRChat avatars often stack several PhysBones on the same chain and toggle
        // between them via the animator; converted physics must start in the same state.
        public bool InitiallyActive;
        public bool ComponentEnabled = true;
        public List<Transform> Ignores = new List<Transform>();
        public Vector3 EndpointPosition;

        public bool IsAdvancedIntegration;
        public float Pull;
        public AnimationCurve PullCurve;
        public float Spring;              // "Momentum" in advanced mode
        public AnimationCurve SpringCurve;
        public float Stiffness;           // advanced mode only
        public AnimationCurve StiffnessCurve;
        public float Gravity;
        public float GravityFalloff;
        public float Immobile;
        public AnimationCurve ImmobileCurve;
        public string ImmobileTypeName;

        public float Radius;
        public AnimationCurve RadiusCurve;

        public string LimitTypeName;      // None / Angle / Hinge / Polar
        public float MaxAngleX;
        public float MaxAngleZ;

        public float MaxStretch;
        public float MaxSquish;
        public string Parameter;          // PhysBone -> animator parameter feature

        public bool IsAnimated;
        public string MultiChildTypeName;
        public bool RootHasMultipleChildren;

        public List<VRCPhysBoneCollider> Colliders = new List<VRCPhysBoneCollider>();

        public List<Transform> HumanoidExclusions = new List<Transform>();

        public List<Transform> ToeExclusions = new List<Transform>();

        public static PhysBoneChainData Read(VRCPhysBone pb) => Read(pb, null);

        public static PhysBoneChainData Read(VRCPhysBone pb, Animator animator) => Read(pb, animator, false);

        public static PhysBoneChainData Read(VRCPhysBone pb, Animator animator, bool excludeToes)
        {
            var data = new PhysBoneChainData
            {
                Source = pb,
                SourceGameObject = pb.gameObject,
                Root = pb.rootTransform != null ? pb.rootTransform : pb.transform,
                InitiallyActive = pb.isActiveAndEnabled,
                ComponentEnabled = pb.enabled,
                EndpointPosition = pb.endpointPosition,
                IsAdvancedIntegration = pb.integrationType.ToString().Contains("Advanced"),
                Pull = pb.pull,
                PullCurve = pb.pullCurve,
                Spring = pb.spring,
                SpringCurve = pb.springCurve,
                Gravity = pb.gravity,
                GravityFalloff = pb.gravityFalloff,
                Immobile = pb.immobile,
                ImmobileCurve = pb.immobileCurve,
                ImmobileTypeName = pb.immobileType.ToString(),
                Radius = pb.radius,
                RadiusCurve = pb.radiusCurve,
                LimitTypeName = pb.limitType.ToString(),
                MaxAngleX = pb.maxAngleX,
                MaxAngleZ = pb.maxAngleZ,
                MaxStretch = pb.maxStretch,
                MaxSquish = ReadFloat(pb, "maxSquish", 0f),
                Parameter = pb.parameter,
                IsAnimated = pb.isAnimated,
                MultiChildTypeName = pb.multiChildType.ToString()
            };

            // Stiffness only exists meaningfully in advanced integration.
            data.Stiffness = data.IsAdvancedIntegration ? pb.stiffness : 0f;
            data.StiffnessCurve = data.IsAdvancedIntegration ? pb.stiffnessCurve : null;

            if (pb.ignoreTransforms != null)
            {
                foreach (var t in pb.ignoreTransforms)
                {
                    if (t != null)
                    {
                        data.Ignores.Add(t);
                    }
                }
            }
            if (pb.colliders != null)
            {
                foreach (var collider in pb.colliders)
                {
                    if (collider is VRCPhysBoneCollider pbCollider)
                    {
                        data.Colliders.Add(pbCollider);
                    }
                }
            }

            // Humanoid-mapped bones must never SIMULATE. The animator and IK write them every
            // frame; locomotion curls the toes, IK plants the feet; and a physics solver
            // writing the same transform fights them for it. VRChat semi-tolerates a PhysBone
            // there; MagicaCloth2 and DynamicBone stomp the animated pose outright. Anchoring is
            // different and stays allowed: a chain ROOTED at a humanoid bone (hair on Head, tail
            // on Hips) is correct, because the root is kinematic and only its descendants
            // simulate. So the rule is: any humanoid bone strictly BELOW the root joins the
            // ignore list, exactly as if the author had excluded it; the MagicaCloth2 writer
            // roots around it (its non-humanoid children still simulate, re-anchored) and the
            // DynamicBone writer turns it into an exclusion.
            if (animator != null && animator.isHuman)
            {
                for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
                {
                    var mapped = animator.GetBoneTransform(bone);
                    if (mapped == null || mapped == data.Root || !mapped.IsChildOf(data.Root)
                        || data.Ignores.Contains(mapped))
                    {
                        continue;
                    }
                    data.Ignores.Add(mapped);
                    data.HumanoidExclusions.Add(mapped);
                }
            }

            // Toes, wherever they sit in the chain. The humanoid rule above only catches MAPPED
            // bones, and a rig maps "Toes"; not Toe1_1, Toe2_1, or the digits under it; so a
            // chain rooted higher up (a leg, a skirt, a whole-body wobble) still ran the solver
            // through every toe joint. That is what busted feet look like after a DynamicBone
            // conversion: the digits splay and swing while the foot itself is planted by IK.
            // Skipping a chain whose ROOT is a toe was never enough on its own; this excludes
            // toe bones found anywhere below the root, for both physics targets, and "Convert
            // toe PhysBones" turns the whole rule off.
            if (excludeToes && data.Root != null)
            {
                var toeRoots = new List<Transform>();
                if (animator != null && animator.isHuman)
                {
                    foreach (var bone in new[] { HumanBodyBones.LeftToes, HumanBodyBones.RightToes })
                    {
                        var toes = animator.GetBoneTransform(bone);
                        if (toes != null)
                        {
                            toeRoots.Add(toes);
                        }
                    }
                }
                foreach (var descendant in data.Root.GetComponentsInChildren<Transform>(true))
                {
                    if (descendant == data.Root || data.Ignores.Contains(descendant))
                    {
                        continue;
                    }
                    bool isToe = descendant.name.IndexOf("toe", System.StringComparison.OrdinalIgnoreCase) >= 0
                                 || toeRoots.Any(t => descendant == t || descendant.IsChildOf(t));
                    if (!isToe)
                    {
                        continue;
                    }
                    // Only the TOP of each toe branch needs listing: both writers drop the
                    // subtree with it, and a list of every joint would bury the report.
                    if (data.Ignores.Any(existing => descendant.IsChildOf(existing)))
                    {
                        continue;
                    }
                    data.Ignores.Add(descendant);
                    data.ToeExclusions.Add(descendant);
                }
            }

            // Multi Child Type only means anything when the root actually branches; with a
            // single child it's inert, so the writers shouldn't act on it. Counted after the
            // ignore list is filled, since ignored children don't count as branches.
            int children = 0;
            for (int i = 0; i < data.Root.childCount; i++)
            {
                if (!data.Ignores.Contains(data.Root.GetChild(i)))
                {
                    children++;
                }
            }
            data.RootHasMultipleChildren = children > 1;

            return data;
        }

        static float ReadFloat(object source, string fieldName, float fallback)
        {
            var type = source.GetType();
            try
            {
                var field = type.GetField(fieldName,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (field != null && field.FieldType == typeof(float))
                {
                    return (float)field.GetValue(source);
                }
                var property = type.GetProperty(fieldName,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (property != null && property.PropertyType == typeof(float))
                {
                    return (float)property.GetValue(source);
                }
            }
            catch
            {
                // fall through
            }
            return fallback;
        }

        public static bool HasCurve(AnimationCurve curve) => curve != null && curve.length > 0;
    }
}
#endif
