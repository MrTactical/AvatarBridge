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
    /// <summary>
    /// Shared state for one run, passed through every pass.
    ///
    /// Only the VRChat-conversion half needs the VRChat SDK, so the descriptor (and the
    /// source→target lookup built on it) is gated: the CVR-side passes — face tracking,
    /// the scaler, blendshape work — run identically in Setup mode, where there is no
    /// VRChat avatar at all, just a humanoid being prepared for ChilloutVR.
    /// </summary>
    public class BridgeContext
    {
        public BridgeSettings Settings;
        public BridgeReport Report;

        /// <summary>
        /// One entry per converted physics chain: the GameObject the source PhysBone lived on,
        /// the object now hosting the generated physics, and the generated component itself.
        /// AnimatorMerger uses this to re-wire toggle animations — a hair-swap that activated
        /// the original PhysBone's object must also activate the MagicaCloth holder, which
        /// lives at the avatar root on its own path and would otherwise stay off forever.
        /// </summary>
        public class ConvertedPhysicsChain
        {
            public UnityEngine.GameObject Source;
            public UnityEngine.GameObject Host;
            public UnityEngine.Behaviour Physics;
            /// <summary>
            /// The first bone of the simulated chain. Every transform at or under it is moved by
            /// the solver, so later passes know not to hang anything new off it — a new child of a
            /// simulated bone becomes a new particle, which changes how the chain behaves.
            /// </summary>
            public UnityEngine.Transform Root;
        }

        public System.Collections.Generic.List<ConvertedPhysicsChain> ConvertedPhysicsChains =
            new System.Collections.Generic.List<ConvertedPhysicsChain>();

#if VRC_SDK_VRCSDK3
        /// <summary>The original, untouched VRChat avatar. Null in Setup mode.</summary>
        public VRCAvatarDescriptor SourceDescriptor;
#endif

        /// <summary>The avatar being converted (a clone unless cloning is disabled).</summary>
        public GameObject Target;
        public CVRAvatar CvrAvatar;

        public AnimatorController MergedController;

        /// <summary>Asset folder for this avatar's generated assets ("Assets/...").</summary>
        public string OutputDir;

        // Parameter bookkeeping, filled by ParameterMenuConverter / ContactsConverter and
        // consumed by the animator rename pass.
        public HashSet<string> PreserveParameters = new HashSet<string>();
        public HashSet<string> ImpulseParameters = new HashSet<string>();
        public HashSet<string> ContactParameters = new HashSet<string>();
        public List<string> ParameterOrder = new List<string>();

        /// <summary>
        /// Renames the animator merge must apply verbatim, decided before it runs.
        ///
        /// ChilloutVR's Joystick2D control doesn't take two parameter names — it derives them,
        /// driving "&lt;machineName&gt;-x" and "&lt;machineName&gt;-y". So a VRChat two-axis puppet can only
        /// become one if the avatar's two axis parameters are renamed to match, and that has to be
        /// settled while the menu is being built and obeyed later by the rename pass.
        /// </summary>
        public Dictionary<string, string> ForcedRenames = new Dictionary<string, string>();

        /// <summary>
        /// Menu entries this conversion invented for synced parameters the VRChat menu never
        /// exposed. Recorded because they are the only entries safe to withdraw later: an entry
        /// the author made is theirs, while one we added on a guess can be taken back once the
        /// merged animator shows the avatar drives that parameter itself.
        /// </summary>
        public HashSet<string> AutoExposedParameters = new HashSet<string>();

        /// <summary>
        /// The blendshape the avatar's OWN animator blinks with (e.g. "vrc.Blink"), when the
        /// conversion decided to replace that system with ChilloutVR's native blink. Set by
        /// DescriptorConverter, read by AnimatorMerger, which strips the animator layer that
        /// drives the shape — two blink systems on one pair of eyes fight, and the animator one
        /// depends on empty-state semantics that don't survive conversion.
        /// </summary>
        public string AnimatorBlinkShape;

        public Animator TargetAnimator => Target != null ? Target.GetComponent<Animator>() : null;

        public string PathInTarget(Transform child) => RelativePath(Target.transform, child);

#if VRC_SDK_VRCSDK3
        /// <summary>Finds the transform in the target that corresponds to one in the source.</summary>
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
