#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using ABI.CCK.Components;

namespace AvatarBridge
{
    /// <summary>
    /// Shared state for one conversion run. Created by BridgeConverter and passed through
    /// every conversion pass.
    /// </summary>
    public class BridgeContext
    {
        public BridgeSettings Settings;
        public BridgeReport Report;

        /// <summary>The original, untouched VRChat avatar.</summary>
        public VRCAvatarDescriptor SourceDescriptor;

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

        public Animator TargetAnimator => Target != null ? Target.GetComponent<Animator>() : null;

        public string PathInTarget(Transform child) => RelativePath(Target.transform, child);

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
