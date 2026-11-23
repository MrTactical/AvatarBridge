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
    // Shared state for one run. The descriptor is SDK-gated: CVR-side passes also run in
    // Setup mode, where there is no VRChat avatar.
    public class BridgeContext
    {
        public BridgeSettings Settings;
        public BridgeReport Report;

        // AnimatorMerger re-wires toggles with this: the cloth holder lives at the avatar
        // root on its own path and would stay off forever when the source object is toggled on.
        public class ConvertedPhysicsChain
        {
            public UnityEngine.GameObject Source;
            public UnityEngine.GameObject Host;
            public UnityEngine.Behaviour Physics;
            public UnityEngine.Transform Root;
        }

        public System.Collections.Generic.List<ConvertedPhysicsChain> ConvertedPhysicsChains =
            new System.Collections.Generic.List<ConvertedPhysicsChain>();

        // A chain on unskinned bones that drives real bones through constraints. Recorded
        // when physics skips it: the constraints naming the driven bone exist only passes later.
        public class HelperRigChain
        {
            public Transform Root;
            public float Pull, Spring, Stiffness, Gravity, Immobile;
            public bool IsAdvancedIntegration;
        }

        public System.Collections.Generic.List<HelperRigChain> HelperRigChains =
            new System.Collections.Generic.List<HelperRigChain>();

        // Every source PhysBone's Ignore Transforms. A stand-in cloth has no PhysBone of its own, and
        // Modular Avatar's hidden-skin bones are only marked here.
        public System.Collections.Generic.HashSet<Transform> PhysBoneIgnores =
            new System.Collections.Generic.HashSet<Transform>();

#if VRC_SDK_VRCSDK3
        public VRCAvatarDescriptor SourceDescriptor;

        // Paths before any pass renamed or deleted one. A baked copy or in-place run
        // converts the source itself, so its live hierarchy cannot say what was there.
        public HashSet<string> SourcePaths;

        public void SnapshotSourcePaths()
        {
            var root = SourceDescriptor.transform;
            SourcePaths = new HashSet<string>(root.GetComponentsInChildren<Transform>(true)
                .Select(t => RelativePath(root, t)), StringComparer.Ordinal);
        }

        // No snapshot: never claim the conversion broke a path.
        public bool ResolvedInSource(string path) =>
            SourcePaths != null && !string.IsNullOrEmpty(path) && SourcePaths.Contains(path);

        // Trees deleted on purpose: their curves are dead by design, neither a loss to
        // report nor a path to repair onto whatever shares the name.
        public List<string> RemovedPaths = new List<string>();

        public bool RemovedOnPurpose(string path) =>
            !string.IsNullOrEmpty(path) && RemovedPaths.Any(r => path == r
                || path.StartsWith(r + "/", StringComparison.Ordinal));
#endif

        public GameObject Target;
        public CVRAvatar CvrAvatar;

        public AnimatorController MergedController;

        public string OutputDir;

        // Toolkit: checked or fixed in place, never converted, so texts naming converter options change.
        public bool Standalone;

        // Filled by ParameterMenuConverter / ContactsConverter, read by the rename pass.
        public HashSet<string> PreserveParameters = new HashSet<string>();
        public HashSet<string> ImpulseParameters = new HashSet<string>();
        public HashSet<string> ContactParameters = new HashSet<string>();

        // Ping-pong pairs rewired into plain toggles. A contact pulse on one reads as on for
        // one frame, so HoldUnlatchedContacts turns it into hold-while-touching.
        public HashSet<string> UnlatchedParameters = new HashSet<string>();
        public List<string> ParameterOrder = new List<string>();

        public Dictionary<(string path, bool sender), List<string>> ContactHosts =
            new Dictionary<(string, bool), List<string>>();
        // Hosts whose component shipped disabled: a native toggle must not wake them with their object.
        public HashSet<string> DisabledHosts = new HashSet<string>();

        // Zones animated in step with the one slider that grows them.
        public List<(string zonePath, string shapeKey, float growth, float reach, string reportPath)>
            ZoneSliderGrowth = new List<(string, string, float, float, string)>();

        public class YapsPlug
        {
            public Transform Root;
            public Renderer Renderer;
            public Material Material;
            public int MaterialSlot;
            // Every material the plug uses on every mesh: a knob missing one leaves half the plug behaving differently.
            public System.Collections.Generic.List<Material> Materials =
                new System.Collections.Generic.List<Material>();
            // Renderer's slots, the first of Materials in the same order.
            public System.Collections.Generic.List<int> MaterialSlots =
                new System.Collections.Generic.List<int>();
            public System.Collections.Generic.List<YapsPlugMesh> Extras =
                new System.Collections.Generic.List<YapsPlugMesh>();
            public float Length;
            public System.Collections.Generic.List<string> Shapes = new System.Collections.Generic.List<string>();
            public System.Collections.Generic.List<string> MovingShapes = new System.Collections.Generic.List<string>();
            // Bone a size animation scales: the mesh's level if a bone, else the first bone below.
            public Transform ChainRoot;

            // From the mesh, not the plug object, which can sit elsewhere and leave a contact box short.
            public UnityEngine.Vector3 Origin;
            public UnityEngine.Quaternion Rotation;
            public float Radius;
        }

        // Extra plug mesh with its own measured shapes, so size curves reach it too.
        public class YapsPlugMesh
        {
            public Renderer Renderer;
            public List<int> Slots = new List<int>();
            public List<string> Shapes = new List<string>();
            public List<string> MovingShapes = new List<string>();
        }

        public List<YapsPlug> YapsPlugs = new List<YapsPlug>();

        // So slot-swap animations can follow. Keyed by renderer and slot: other meshes
        // usually wear the original material and must keep it.
        public Dictionary<(Renderer renderer, int slot), (Material from, Material to)>
            YapsMaterialSwaps = new Dictionary<(Renderer, int), (Material, Material)>();

        // Repointed once the clips are the conversion's own copies.
        public List<(Renderer renderer, int slot, Material from, Material to)>
            YapsVariantSwaps = new List<(Renderer, int, Material, Material)>();

        // Texture name to the avatar sharing it, so the weight report skips them.
        public Dictionary<string, string> SlimLeftAlone;

        public Dictionary<string, List<string>> PhysicsColliderHosts =
            new Dictionary<string, List<string>>();

        public Dictionary<string, string> ForcedRenames = new Dictionary<string, string>();

        // New name to old name and why. Null: the merger did not run.
        public Dictionary<string, string> RenamedFrom;

        public HashSet<string> AutoExposedParameters = new HashSet<string>();

        // Contact-driven parameters must be local: a synced twin gets the stream written back over it.
        public List<string> ForceLocalPrefixes = new List<string>();

        // Same rule by shape: VRCFury stamps a per-component id, the shape after it is stable.
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

        // One matcher for all callers: two once disagreed, the report called a
        // controller fine while the crash guard refused to assign it.
        public static IEnumerable<string> ReferencedGuids(string yaml)
        {
            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(
                         yaml, @"\{fileID:\s*-?\d+,\s*guid:\s*([0-9a-f]{32}),\s*type:\s*-?\d+\}"))
            {
                yield return match.Groups[1].Value;
            }
        }

        // The base under any override controllers, which is what layer and parameter readers want.
        public static UnityEditor.Animations.AnimatorController Underlying(RuntimeAnimatorController runtime)
        {
            for (int guard = 0; runtime != null && guard < 8; guard++)
            {
                if (runtime is UnityEditor.Animations.AnimatorController controller) return controller;
                if (runtime is AnimatorOverrideController over) { runtime = over.runtimeAnimatorController; continue; }
                break;
            }
            return null;
        }

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
