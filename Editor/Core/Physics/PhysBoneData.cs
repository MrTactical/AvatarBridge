#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge
{
    /// <summary>
    /// SDK-agnostic snapshot of one VRCPhysBone chain, so the MagicaCloth2 and
    /// DynamicBone writers don't each need to know the VRC API.
    /// All 0..1 factors keep VRChat semantics; the writers do the mapping.
    /// </summary>
    public class PhysBoneChainData
    {
        public VRCPhysBone Source;
        public GameObject SourceGameObject;
        public Transform Root;
        // VRChat avatars often stack several PhysBones on the same chain and toggle
        // between them via the animator; converted physics must start in the same state.
        public bool InitiallyActive;
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
        public string Parameter;          // PhysBone -> animator parameter feature

        public List<VRCPhysBoneCollider> Colliders = new List<VRCPhysBoneCollider>();

        public static PhysBoneChainData Read(VRCPhysBone pb)
        {
            var data = new PhysBoneChainData
            {
                Source = pb,
                SourceGameObject = pb.gameObject,
                Root = pb.rootTransform != null ? pb.rootTransform : pb.transform,
                InitiallyActive = pb.isActiveAndEnabled,
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
                Parameter = pb.parameter
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
            return data;
        }

        public static bool HasCurve(AnimationCurve curve) => curve != null && curve.length > 0;
    }
}
#endif
