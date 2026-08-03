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
        /// Where each VRC contact's replacement ended up, for curve repointing after the merge.
        /// Key: the ORIGINAL component's animator path plus whether it was a sender — exactly what
        /// an m_Enabled curve binding carries. Value: the generated host object path(s); a sender
        /// with several tags becomes several pointer objects on the legacy path, so one enable
        /// curve fans out. Filled by ContactsConverter, consumed by RepointContactEnableCurves.
        /// </summary>
        public Dictionary<(string path, bool sender), List<string>> ContactHosts =
            new Dictionary<(string, bool), List<string>>();

        /// <summary>
        /// Where each VRCPhysBoneCollider's replacement landed, for curve repointing after the
        /// merge — the collider twin of <see cref="ContactHosts"/>. Key: the ORIGINAL component's
        /// animator path, which is what an m_Enabled curve binding carries. Both solver writers
        /// fill it; PhysBoneConverter.RepointColliderEnableCurves consumes it.
        /// </summary>
        public Dictionary<string, List<string>> PhysicsColliderHosts =
            new Dictionary<string, List<string>>();

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
        /// True when the avatar blinks from its own animator (VRCFury-style), so the native-blink
        /// takeover decision is DEFERRED to AnimatorMerger.ReplaceAnimatorBlink, which can inspect
        /// the merged layers. The shape is chosen there, from the layer that gets stripped — not
        /// here: choosing by name first picked "Blink" off an expression clip while the receiver
        /// drove "vrc.Blink", and the takeover missed entirely.
        /// </summary>
        public bool AnimatorBlinkPending;

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
