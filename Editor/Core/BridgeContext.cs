#if CVR_CCK_EXISTS
using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif
using ABI.CCK.Components;

namespace AvatarBridge
{
    // Shared state for one run, passed through every pass.
    //
    // Only the VRChat-conversion half needs the VRChat SDK, so the descriptor (and the
    // source->target lookup built on it) is gated: the CVR-side passes; face tracking,
    // the scaler, blendshape work; run identically in Setup mode, where there is no
    // VRChat avatar at all, just a humanoid being prepared for ChilloutVR.
    public class BridgeContext
    {
        public BridgeSettings Settings;
        public BridgeReport Report;

        // One entry per converted physics chain: the GameObject the source PhysBone lived on,
        // the object now hosting the generated physics, and the generated component itself.
        // AnimatorMerger uses this to re-wire toggle animations; a hair-swap that activated
        // the original PhysBone's object must also activate the MagicaCloth holder, which
        // lives at the avatar root on its own path and would otherwise stay off forever.
        public class ConvertedPhysicsChain
        {
            public UnityEngine.GameObject Source;
            public UnityEngine.GameObject Host;
            public UnityEngine.Behaviour Physics;
            public UnityEngine.Transform Root;
        }

        public System.Collections.Generic.List<ConvertedPhysicsChain> ConvertedPhysicsChains =
            new System.Collections.Generic.List<ConvertedPhysicsChain>();

#if VRC_SDK_VRCSDK3
        public VRCAvatarDescriptor SourceDescriptor;
#endif

        public GameObject Target;
        public CVRAvatar CvrAvatar;

        public AnimatorController MergedController;

        public string OutputDir;

        // Parameter bookkeeping, filled by ParameterMenuConverter / ContactsConverter and
        // consumed by the animator rename pass.
        public HashSet<string> PreserveParameters = new HashSet<string>();
        public HashSet<string> ImpulseParameters = new HashSet<string>();
        public HashSet<string> ContactParameters = new HashSet<string>();

        public HashSet<string> LocalContactParameters = new HashSet<string>();

        public System.Collections.Generic.List<(string local, string synced)> BridgedContacts =
            new System.Collections.Generic.List<(string, string)>();

        // Proximity contacts whose parameter a menu also drives.
        // Readers move to a per-frame max of the two.
        public System.Collections.Generic.List<(string local, string synced)> AnalogBridgedContacts =
            new System.Collections.Generic.List<(string, string)>();
        public List<string> ParameterOrder = new List<string>();

        public Dictionary<(string path, bool sender), List<string>> ContactHosts =
            new Dictionary<(string, bool), List<string>>();

        public Dictionary<string, List<string>> PhysicsColliderHosts =
            new Dictionary<string, List<string>>();

        public Dictionary<string, string> ForcedRenames = new Dictionary<string, string>();

        public Dictionary<string, string> AppliedParameterRenames = new Dictionary<string, string>();

        public HashSet<string> AutoExposedParameters = new HashSet<string>();

        public bool AnimatorBlinkPending;

        public Animator TargetAnimator => Target != null ? Target.GetComponent<Animator>() : null;

        public string PathInTarget(Transform child) => RelativePath(Target.transform, child);

#if VRC_SDK_VRCSDK3
        public Transform FindInTarget(Transform sourceChild)
        {
            if (sourceChild == null)
            {
                return null;
            }
            if (sourceChild == SourceDescriptor.transform)
            {
                return Target.transform;
            }
            string path = RelativePath(SourceDescriptor.transform, sourceChild);
            return Target.transform.Find(path);
        }
#endif

        public static string RelativePath(Transform parent, Transform child)
        {
            if (child == parent)
            {
                return "";
            }
            string path = child.name;
            while (child.parent != null && child.parent != parent)
            {
                child = child.parent;
                path = child.name + "/" + path;
            }
            return path;
        }
    }
}
#endif
