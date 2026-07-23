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

            // Stacked systems (e.g. cake PB) put several PhysBones on the same root and let
            // the animator switch between them; all get converted, but only the ones that
            // were enabled start active, and the user should review which they keep.
            foreach (var group in physBones
                .GroupBy(pb => pb.rootTransform != null ? pb.rootTransform : pb.transform)
                .Where(g => g.Count() > 1))
            {
                int active = group.Count(pb => pb.isActiveAndEnabled);
                ctx.Report.Warning(Category, $"{group.Count()} PhysBones share root \"{group.Key.name}\"",
                    $"VRChat toggles between them at runtime; {active} started enabled and only those were " +
                    "activated. Review the generated components and delete the variants you don't need.");
            }

            switch (ctx.Settings.physicsTarget)
            {
                case PhysicsTarget.MagicaCloth2:
#if AVATARBRIDGE_MAGICA
                    var magicaColliderCache = new Dictionary<VRCPhysBoneCollider, MagicaCloth2.ColliderComponent>();
                    foreach (var pb in physBones)
                    {
                        MagicaClothWriter.Write(ctx, PhysBoneChainData.Read(pb), magicaColliderCache);
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
