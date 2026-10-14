#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge
{
    // Folds sibling PhysBones that would produce identical cloths into one
    // multi-root cloth.
    //
    // A VRCPhysBone has one root; authors who want 26 hair strands write 26
    // components, and converting one-for-one lands 26 solvers on the avatar
    // where MagicaCloth2 wants one component holding 26 root bones. Every
    // extra component is its own proxy mesh and its own solver team, so the
    // count is a real cost, not just a long hierarchy.
    //
    // The bar for merging is "the two components would have been identical
    // anyway": same settings, same colliders, same on/off state, same
    // parent, same classified kind, same segment lengths. Anything a merge could not
    // express (an ignore list, a PhysBone parameter, a toggle on one chain) keeps
    // its own component.
    public static class PhysBoneChainGrouping
    {
        const string Category = "PhysBones -> MagicaCloth2";

        public static List<PhysBoneChainData> Merge(BridgeContext ctx, List<PhysBoneChainData> chains)
        {
            if (!ctx.Settings.groupIdenticalPhysBones || chains.Count < 2)
            {
                return chains;
            }

            var toggled = ToggledPaths(ctx);
            var result = new List<PhysBoneChainData>();
            var buckets = new Dictionary<string, PhysBoneChainData>();
            int folded = 0;
            var names = new SortedSet<string>(StableSampleOrder.Instance);

            foreach (var chain in chains)
            {
                string key = KeyFor(chain, ctx.Target.transform, toggled);
                if (key == null)
                {
                    result.Add(chain);
                    continue;
                }
                if (!buckets.TryGetValue(key, out var head))
                {
                    buckets[key] = chain;
                    result.Add(chain);
                    continue;
                }
                // Stacked PhysBones on one root are variants the animator switches
                // between. One cloth would carry the root twice and a toggle for
                // one variant would switch all of them.
                if (head.AllRoots.Contains(chain.Root))
                {
                    result.Add(chain);
                    continue;
                }
                head.MergedRoots.Add(chain.Root);
                head.MergedSources.Add(chain.SourceGameObject);
                folded++;
                var parent = chain.Root.parent;
                names.Add(parent != null ? parent.name : chain.Root.name);
            }

            if (folded > 0)
            {
                int groups = buckets.Values.Count(c => c.MergedRoots.Count > 0);
                ctx.Report.Converted(Category,
                    $"{BridgeElements.Count(folded + groups, "identical chain", "identical chains")} simulated by " +
                    BridgeElements.Count(groups, "cloth", "cloths"),
                    "These PhysBones were separate components with the same settings, the same " +
                    "colliders, the same parent and the same bone lengths: a hairstyle built one strand at a time, a pair " +
                    "of ears, a row of charms. One MagicaCloth2 component simulates any number of " +
                    "independent root bones, and each component it saves is a proxy mesh and a " +
                    "solver team the avatar no longer pays for. The chains still move separately: " +
                    "they share settings, not physics. Grouped under " +
                    string.Join(", ", names.Take(6)) + (names.Count > 6 ? ", …" : "") +
                    ". Turn off \"Group identical PhysBone chains\" for one component per chain, " +
                    "the way the VRChat avatar had them.");
            }
            return result;
        }

        // Null means "keep this one on its own".
        static string KeyFor(PhysBoneChainData data, Transform avatar, HashSet<string> toggled)
        {
            if (data.Root == null || data.SourceGameObject == null || data.Synthesized)
            {
                return null;
            }
            // A cloth has one enabled flag. A strand the animator switches by itself, through
            // its own object, its root or its PhysBone, would stop every strand it shared with.
            if (toggled.Contains(BridgeContext.RelativePath(avatar, data.SourceGameObject.transform))
                || toggled.Contains(BridgeContext.RelativePath(avatar, data.Root)))
            {
                return null;
            }
            // A stand-in has its own spring and collision-bone logic, written
            // for the one bone it replaces.
            if (data.StandIn)
            {
                return null;
            }
            // The holder's name IS the GrabbyBones parameter, so a chain the
            // animator names cannot share a holder with anything.
            if (!string.IsNullOrEmpty(data.Parameter))
            {
                return null;
            }
            // Exclusions are per component in MagicaCloth2: it has no ignore
            // list, so they are expressed by rooting around the excluded
            // bones, and that root set belongs to one chain.
            if (data.Ignores.Count > 0 || data.HumanoidExclusions.Count > 0 || data.ToeExclusions.Count > 0)
            {
                return null;
            }
            // A curve is sampled along the chain by normalised depth, so two
            // chains of different length read different values off the same
            // curve even when every other number matches.
            if (HasCurve(data.PullCurve) || HasCurve(data.SpringCurve) || HasCurve(data.StiffnessCurve)
                || HasCurve(data.ImmobileCurve) || HasCurve(data.RadiusCurve))
            {
                return null;
            }
            // Anchored volumes are measured and given collision bones per side.
            // A merged left and right would share one radius and one collision
            // bone, so they stay apart whatever their settings say.
            var cls = MagicaPresetLibrary.Classify(data);
            if (cls == null || MagicaClothWriter.SoftBodyClasses.Contains(cls.Name))
            {
                return null;
            }
            // Same parent on both the bone and the component's own object: with
            // neither switched on its own (above), a toggle that hides one of these
            // hides all of them, which is what lets them share an enabled flag.
            var rootParent = data.Root.parent;
            var sourceParent = data.SourceGameObject.transform.parent;
            if (rootParent == null || sourceParent == null)
            {
                return null;
            }

            // Gravity and the swing bound are solved from the path one cloth bends, so a
            // longer or shorter strand would hang differently than it did in VRChat. Every
            // segment to within 5%, which keeps the profile as well as the length.
            // ponytail: hard bucket edges split strands a hair apart; cluster if groups fragment.
            string shape = string.Join(",", MagicaClothWriter.SimulatedPathFrom(data.Root, data)
                .Select(s => s > 1e-6f ? Mathf.RoundToInt(Mathf.Log(s) / Mathf.Log(1.05f)).ToString() : "0"));

            var colliders = string.Join(",", data.Colliders
                .Where(c => c != null)
                .Select(c => c.GetInstanceID().ToString())
                .OrderBy(s => s, StableSampleOrder.Instance));

            return string.Join("|",
                cls.Name,
                shape,
                rootParent.GetInstanceID().ToString(),
                sourceParent.GetInstanceID().ToString(),
                colliders,
                data.IsAdvancedIntegration ? "adv" : "simple",
                // 1.0 and 1.1 derive different damping, restoration and gravity from the same numbers.
                data.IsVersion10 ? "v1.0" : "v1.1",
                F(data.Pull), F(data.Spring), F(data.Stiffness),
                F(data.Gravity), F(data.GravityFalloff), F(data.Immobile), F(data.Radius),
                data.ImmobileTypeName, data.LimitTypeName,
                F(data.MaxAngleX), F(data.MaxAngleZ), F(data.MaxStretch), F(data.MaxSquish),
                F(data.EndpointPosition.x), F(data.EndpointPosition.y), F(data.EndpointPosition.z),
                data.MultiChildTypeName,
                data.RootHasMultipleChildren ? "multi" : "single",
                data.IsAnimated ? "animated" : "static",
                data.InitiallyActive ? "on" : "off",
                data.ComponentEnabled ? "enabled" : "disabled");
        }

        static bool HasCurve(AnimationCurve curve) => curve != null && curve.length > 0;

        // Paths the animator switches: an object's active flag, or any curve on a PhysBone or a
        // base of it (its enabled flag is the Behaviour's). Read from the source's own
        // controllers, since this pass runs before the merge.
        static HashSet<string> ToggledPaths(BridgeContext ctx)
        {
            var controllers = new List<RuntimeAnimatorController>();
            var descriptor = ctx.SourceDescriptor;
            if (descriptor != null)
            {
                foreach (var layer in (descriptor.baseAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0])
                             .Concat(descriptor.specialAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0]))
                {
                    if (layer.animatorController != null) controllers.Add(layer.animatorController);
                }
            }
            foreach (var animator in ctx.Target.GetComponentsInChildren<Animator>(true))
            {
                if (animator.runtimeAnimatorController != null) controllers.Add(animator.runtimeAnimatorController);
            }
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var clip in controllers.SelectMany(c => c.animationClips).Where(c => c != null).Distinct())
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip)
                             .Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
                {
                    if ((binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive")
                        || (binding.type != null && binding.type.IsAssignableFrom(typeof(VRCPhysBone))))
                    {
                        paths.Add(binding.path);
                    }
                }
            }
            return paths;
        }

        // Rounded, because two strands of one hairstyle are authored by
        // copying a component and the odd float differs in the last bit.
        static string F(float value) => value.ToString("0.####");
    }
}
#endif
