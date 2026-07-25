#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge
{
    /// <summary>
    /// Converts every VRCPhysBone on the avatar to the chosen ChilloutVR-compatible
    /// physics system (MagicaCloth2 preferred, DynamicBone as fallback) and removes the
    /// original PhysBone components afterwards.
    /// </summary>
    public static class PhysBoneConverter
    {
        const string Category = "PhysBones";

        public static void Run(BridgeContext ctx)
        {
            var physBones = ctx.Target.GetComponentsInChildren<VRCPhysBone>(true);
            if (physBones.Length == 0)
            {
                ctx.Report.Converted(Category, "No PhysBones found");
                return;
            }

            GrabbyBonesSupport.Reset();

            // Stacked systems (e.g. cake PB) put several PhysBones on the same root and let
            // the animator switch between them; all get converted, but only the ones that
            // were enabled start active, and the user should review which they keep.
            foreach (var group in physBones
                .GroupBy(pb => pb.rootTransform != null ? pb.rootTransform : pb.transform)
                .Where(g => g.Count() > 1))
            {
                int active = group.Count(pb => pb.isActiveAndEnabled);
                if (active == 0)
                {
                    ctx.Report.Warning(Category, $"\"{group.Key.name}\" has NO active physics",
                        $"All {group.Count()} PhysBones on this chain were disabled when baked, so every " +
                        "generated cloth starts disabled and the chain will not move. Enable the variant " +
                        "you want on the converted avatar.");
                }
                else
                {
                    ctx.Report.Warning(Category, $"{group.Count()} PhysBones share root \"{group.Key.name}\"",
                        $"VRChat toggles between them at runtime; {active} started enabled and only those were " +
                        "activated. Review the generated components and delete the variants you don't need.");
                }
            }

            switch (ctx.Settings.physicsTarget)
            {
                case PhysicsTarget.MagicaCloth2:
#if AVATARBRIDGE_MAGICA
                    var magicaColliderCache = new Dictionary<VRCPhysBoneCollider, MagicaCloth2.ColliderComponent>();

                    // Built once for the whole avatar, then shared by any chain that arrived
                    // without colliders of its own. Only worth generating if such a chain
                    // exists — an avatar whose author set colliders up everywhere needs none.
                    var chains = physBones.Select(PhysBoneChainData.Read).ToList();
                    var bodyColliders = chains.Any(c => c.Colliders.Count == 0)
                        ? MagicaBodyColliders.Build(ctx)
                        : new List<MagicaCloth2.ColliderComponent>();
                    if (bodyColliders.Count > 0)
                    {
                        ctx.Report.Converted(Category, $"Generated {bodyColliders.Count} body collider(s)",
                            "Sized from this avatar's own bone lengths and given to every chain that had no " +
                            "colliders of its own, so hair and tails collide with the body instead of passing " +
                            "through it. Chains that already had colliders were left as authored.");
                    }

                    foreach (var chain in chains)
                    {
                        MagicaClothWriter.Write(ctx, chain, magicaColliderCache, bodyColliders);
                    }
#else
                    ctx.Report.Error(Category, "MagicaCloth2 is not installed",
                        "Import MagicaCloth2 (or choose the DynamicBone target) and convert again.");
                    return;
#endif
                    break;

                case PhysicsTarget.DynamicBone:
#if AVATARBRIDGE_DYNBONE
                    var dbColliderCache = new Dictionary<VRCPhysBoneCollider, DynamicBoneColliderBase>();
                    foreach (var pb in physBones)
                    {
                        DynamicBoneWriter.Write(ctx, PhysBoneChainData.Read(pb), dbColliderCache);
                    }
#else
                    ctx.Report.Error(Category, "DynamicBone is not installed",
                        "Import DynamicBone or the VRLabs Dynamic-Bones-Stub, or choose MagicaCloth2.");
                    return;
#endif
                    break;

                default:
                    ctx.Report.Skipped(Category, $"{physBones.Length} PhysBone(s)",
                        "Physics conversion disabled in settings.");
                    return;
            }

            if (ctx.Settings.deleteConvertedPhysBones)
            {
                foreach (var pb in physBones)
                {
                    Object.DestroyImmediate(pb);
                }
                foreach (var collider in ctx.Target.GetComponentsInChildren<VRCPhysBoneCollider>(true))
                {
                    Object.DestroyImmediate(collider);
                }
            }
        }
    }
}
#endif
