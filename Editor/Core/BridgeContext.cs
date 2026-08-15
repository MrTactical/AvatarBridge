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

            // Measured from the mesh, world space, at conversion time. The
            // plug OBJECT is routinely somewhere else entirely — a quarter
            // of a metre up the body on a real avatar — so a contact box
            // placed on the object measures from a different origin than the
            // shader reconstructs against, and the socket lands short by
            // exactly that gap.
            public UnityEngine.Vector3 Origin;
            public UnityEngine.Quaternion Rotation;
        }

        public List<YapsPlug> YapsPlugs = new List<YapsPlug>();

        public Dictionary<string, List<string>> PhysicsColliderHosts =
            new Dictionary<string, List<string>>();

        public Dictionary<string, string> ForcedRenames = new Dictionary<string, string>();

        public HashSet<string> AutoExposedParameters = new HashSet<string>();

        // Parameter prefixes that must end up "#" local no matter what else
        // claims them. A contact-driven parameter has to be local — the
        // native path writes straight at the animator and a synced twin gets
        // the incoming stream written back over it — and being local also
        // means it costs nothing against the sync budget. Both reasons point
        // the same way, which is what makes depth animation free here and
        // expensive in VRChat.
        public List<string> ForceLocalPrefixes = new List<string>();

        // The same rule for names a prefix cannot catch. VRCFury stamps every
        // penetration parameter with a per-component id — VF80_ on one
        // avatar, VF77_ on another — so a fixed prefix list is a list of the
        // avatars someone happened to test. What is stable is the SHAPE that
        // follows the id: contact-driven depth on the sockets, auto-distance
        // on the plug. Anything of that shape is written by a contact on
        // every client and syncing it transmits nothing anyone needs, at
        // 32 bits a time. Menu toggles and modes under the same id do not
        // match and stay synced, which is the whole point of matching the
        // shape rather than the prefix.
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

        // Resolves an animation path the way the ANIMATOR does, which is not
        // the way Transform.Find does. Find splits on every "/" and walks
        // one child per segment; the animator hashes the whole path and
        // matches it against each object's full path, so an object whose
        // own NAME contains a slash is perfectly animatable and perfectly
        // invisible to Find. VRCFury names every contact trigger it bakes
        // after its parameter — "CVRTrigger_OGB/Orf/Pussy/PenOthersNewRoot"
        // is ONE object — so a converted avatar has dozens of them, and any
        // check built on Find calls each one dead. Eighty-six of them, on
        // one avatar, all working in game.
        //
        // Greedy on the name: at each level, try the longest child name that
        // is a prefix of the remainder first, then shorter. Depth-first, so a
        // "Haptics" child and a "Haptics/PenOthers" child under the same
        // parent both resolve.
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
            // The common case first — it is faster and it is exactly what
            // the animator finds when no name contains a slash.
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
