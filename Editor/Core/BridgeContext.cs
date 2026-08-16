#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
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

        // Set by the ChilloutVR Toolkit: an avatar being checked or fixed
        // in place, never converted. Texts that name the converter's
        // options or its origin read differently.
        public bool Standalone;

        // Parameter bookkeeping, filled by ParameterMenuConverter / ContactsConverter and
        // consumed by the animator rename pass.
        public HashSet<string> PreserveParameters = new HashSet<string>();
        public HashSet<string> ImpulseParameters = new HashSet<string>();
        public HashSet<string> ContactParameters = new HashSet<string>();
        public List<string> ParameterOrder = new List<string>();

        public Dictionary<(string path, bool sender), List<string>> ContactHosts =
            new Dictionary<(string, bool), List<string>>();

        // Zones whose growth one slider owns: the scale pass animates
        // each zone in step with that slider's blendshape curves.
        public List<(string zonePath, string shapeKey, float growth, float reach, string reportPath)>
            ZoneSliderGrowth = new List<(string, string, float, float, string)>();

        // One entry per plug the YAPS pass converted, so the channel pass
        // knows which renderers and materials it is wiring to.
        public class YapsPlug
        {
            public Transform Root;
            public Renderer Renderer;
            public Material Material;
            public int MaterialSlot;
            public float Length;
            public System.Collections.Generic.List<string> Shapes = new System.Collections.Generic.List<string>();
            public System.Collections.Generic.List<string> MovingShapes = new System.Collections.Generic.List<string>();

            // Measured from the mesh in world space. The plug object can sit
            // elsewhere; a contact box placed there lands short.
            public UnityEngine.Vector3 Origin;
            public UnityEngine.Quaternion Rotation;
            public float Radius;
        }

        public List<YapsPlug> YapsPlugs = new List<YapsPlug>();

        public Dictionary<string, List<string>> PhysicsColliderHosts =
            new Dictionary<string, List<string>>();

        public Dictionary<string, string> ForcedRenames = new Dictionary<string, string>();

        public HashSet<string> AutoExposedParameters = new HashSet<string>();

        // Prefixes forced "#" local. A contact-driven parameter must be local:
        // a synced twin gets the stream written back over it.
        public List<string> ForceLocalPrefixes = new List<string>();

        // The same rule by shape, for names a prefix cannot catch. VRCFury
        // stamps a per-component id; the shape after it is what is stable.
        // Toggles and modes under the same id do not match and stay synced.
        public List<System.Text.RegularExpressions.Regex> ForceLocalPatterns =
            new List<System.Text.RegularExpressions.Regex>();

        public bool ForcesLocal(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (ForceLocalPrefixes.Count > 0
                && ForceLocalPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            for (int i = 0; i < ForceLocalPatterns.Count; i++)
            {
                if (ForceLocalPatterns[i].IsMatch(name)) return true;
            }
            return false;
        }

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
            return FindByAnimationPath(Target.transform, path);
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

        // Resolves a path the way the animator does: whole path against each
        // object, so a name containing a slash still matches. Plain Find
        // first, then a greedy longest-name walk.
        public static Transform FindByAnimationPath(Transform root, string path)
        {
            if (root == null)
            {
                return null;
            }
            if (string.IsNullOrEmpty(path))
            {
                return root;
            }
            // The common case first.
            var direct = root.Find(path);
            if (direct != null)
            {
                return direct;
            }
            return FindGreedy(root, path);
        }

        static Transform FindGreedy(Transform at, string remainder)
        {
            for (int i = 0; i < at.childCount; i++)
            {
                var child = at.GetChild(i);
                string name = child.name;
                if (remainder == name)
                {
                    return child;
                }
                if (remainder.Length > name.Length
                    && remainder.StartsWith(name, StringComparison.Ordinal)
                    && remainder[name.Length] == '/')
                {
                    var deeper = FindGreedy(child, remainder.Substring(name.Length + 1));
                    if (deeper != null)
                    {
                        return deeper;
                    }
                }
            }
            return null;
        }
    }
}
#endif
