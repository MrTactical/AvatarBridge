// Cleans up an unconvertible helper-rig physics addon (a cake PB style cascade of unskinned
// staged bones relayed onto real bones by constraints). Independent solvers cannot rebuild it:
// five attempts gave five wrong results. So its chains are skipped, relays reading from it are
// removed (a position or scale relay overrides animation, blocking squish), and the report names
// the bone for a cloth. After the constraint pass, which creates the relays.
#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;

namespace AvatarBridge
{
    public static class HelperRigCleanup
    {
        const string Category = "PhysBones";

        // Paths of the rigs Run deleted, for StripCurves. Per conversion.
        static readonly List<string> deletedPaths = new List<string>();

        public static void Run(BridgeContext ctx)
        {
            Clean(ctx);
#if AVATARBRIDGE_MAGICA
            // The last pass that sizes a cloth from the mesh, so its vertex caches
            // go here rather than holding this avatar's meshes until the next run.
            MagicaClothWriter.ReleaseCaches();
#endif
        }

        static void Clean(BridgeContext ctx)
        {
            deletedPaths.Clear();
            if (ctx.HelperRigChains.Count == 0) return;

            var skinned = PhysBoneConverter.SkinnedBones(ctx);

            // Highest ancestor moving no mesh (chain roots alone miss its targets). Stops at skinned
            // bones and below anything visible: climbing into rigid accessories deleted them.
            var rig = new HashSet<Transform>();
            var tops = new HashSet<Transform>();
            foreach (var chain in ctx.HelperRigChains)
            {
                if (chain.Root == null) continue;
                var top = chain.Root;
                for (var up = chain.Root.parent; up != null && up != ctx.Target.transform; up = up.parent)
                {
                    if (up.GetComponentsInChildren<Transform>(true).Any(skinned.Contains) || HoldsContent(up)) break;
                    top = up;
                }
                foreach (var b in top.GetComponentsInChildren<Transform>(true)) rig.Add(b);
                if (top != ctx.Target.transform) tops.Add(top);
            }

            // Every relay reading from the rig, wherever it sits and whatever
            // it writes. No exceptions: each one now copies a frozen pose.
            int dropped = 0;
            var suggest = new SortedSet<string>(System.StringComparer.Ordinal);
            var targets = new List<Transform>();
            foreach (var t in ctx.Target.GetComponentsInChildren<Transform>(true))
            {
                foreach (var component in t.GetComponents<Component>())
                {
                    if (!(component is IConstraint constraint)) continue;
                    var sources = new List<ConstraintSource>();
                    constraint.GetSources(sources);
                    if (!sources.Any(s => s.sourceTransform != null && rig.Contains(s.sourceTransform))) continue;

                    // The driven bone is often an unweighted holder, so test whether a cloth there moves
                    // any mesh. Climbing to a skinned ancestor lands on Hips.
                    if (MovesMesh(t, skinned) && !targets.Contains(t))
                    {
                        suggest.Add(t.name);
                        targets.Add(t);
                    }
                    Object.DestroyImmediate(component);
                    dropped++;
                }
            }

            // Works only with the relays gone; with them, cloth and relay fought. Settings from the rig's springiest chain.
            var feel = ctx.HelperRigChains.OrderByDescending(c => c.Spring).FirstOrDefault();
            int made = 0;
            foreach (var bone in targets)
            {
                if (Write(ctx, bone, feel)) made++;
            }

            // Dead transforms cost an update each frame (110 of 468 on one avatar). Tops found by
            // mesh, not name ("cake_PB_L" bones are real). Kept if a surviving chain uses one of
            // its colliders: deleting leaves a null and that chain silently loses collision.
            int removedBones = 0, kept = 0;
            foreach (var top in tops)
            {
                if (top == null) continue;
                if (Borrowed(ctx, top) || CarriesContacts(top))
                {
                    kept++;
                    continue;
                }
                string path = BridgeContext.RelativePath(ctx.Target.transform, top);
                if (!string.IsNullOrEmpty(path)) deletedPaths.Add(path);
                removedBones += top.GetComponentsInChildren<Transform>(true).Length;
                Object.DestroyImmediate(top.gameObject);
            }

            string where = made > 0
                ? $"A cloth was put on {string.Join(" and ", suggest.Take(4))} instead, tuned from the " +
                  "rig's own numbers. That is one chain where the source had a cascade, so it will not " +
                  "feel identical: tune it, or delete it and build your own."
                : suggest.Count > 0
                ? $"Put a MagicaCloth or DynamicBone on {string.Join(" and ", suggest.Take(4))}" +
                  (suggest.Count > 4 ? $" (and {suggest.Count - 4} more)" : "") +
                  " to get the movement back: that is what this rig was driving, and a cloth there moves " +
                  "the mesh hanging below it. A guess worth checking, not a measurement."
                : "Nothing it drove is weighted to a mesh, so there is no obvious bone to put a cloth on.";

            if (kept > 0)
            {
                ctx.Report.Approximated(Category, BridgeElements.Count(kept, "helper rig", "helper rigs") + " left in place",
                    "A surviving cloth borrows their colliders, or they carry contacts still needed. They are inert.");
            }
            Report(ctx, dropped, removedBones, where);
        }

        // Would a cloth rooted here move anything? True if this bone or
        // anything under it deforms a mesh.
        static bool MovesMesh(Transform t, HashSet<Transform> skinned)
            => t.GetComponentsInChildren<Transform>(true).Any(skinned.Contains);

        // Something a player sees or hears under this transform. A rigid
        // accessory skins no mesh, yet it is content, never a helper stage.
        internal static bool HoldsContent(Transform t)
            => t.GetComponentInChildren<Renderer>(true) != null
               || t.GetComponentInChildren<Light>(true) != null
               || t.GetComponentInChildren<AudioSource>(true) != null;

        // A rig holding contacts (squish triggers) stays: deleting it took them all.
        static bool CarriesContacts(Transform top)
            => top.GetComponentsInChildren<ABI.CCK.Components.CVRPointer>(true).Any(c => c != null)
               || top.GetComponentsInChildren<ABI.CCK.Components.CVRAdvancedAvatarSettingsTrigger>(true)
                   .Any(c => c != null);

        // Does any cloth OUTSIDE this rig reference a collider inside it?
        // Collider auto-assign hands a chain whatever it could swing into,
        // and a helper rig's colliders sit in the same space as the body's.
        static bool Borrowed(BridgeContext ctx, Transform top)
        {
            var inside = new HashSet<Transform>(top.GetComponentsInChildren<Transform>(true));
            foreach (var cloth in ctx.Target.GetComponentsInChildren<Component>(true))
            {
                if (cloth == null || cloth.GetType().Name != "MagicaCloth") continue;
                if (inside.Contains(cloth.transform)) continue;

                var data = cloth.GetType().GetProperty("SerializeData")?.GetValue(cloth);
                var constraint = data?.GetType().GetField("colliderCollisionConstraint")?.GetValue(data);
                var list = constraint?.GetType().GetField("colliderList")?.GetValue(constraint)
                    as System.Collections.IEnumerable;
                if (list == null) continue;
                foreach (var entry in list)
                {
                    if (entry is Component c && c != null && inside.Contains(c.transform)) return true;
                }
            }
#if AVATARBRIDGE_DYNBONE
            // The DynamicBone target parents its colliders under the PhysBone
            // collider's own bone, which can sit inside the rig just the same.
            foreach (var db in ctx.Target.GetComponentsInChildren<DynamicBone>(true))
            {
                if (db == null || db.m_Colliders == null || inside.Contains(db.transform)) continue;
                if (db.m_Colliders.Any(c => c != null && inside.Contains(c.transform))) return true;
            }
#endif
            return false;
        }

        // Curves into what Run deleted. Own pass after self-containment: during Run the clips are still the author's.
        public static void StripCurves(BridgeContext ctx)
        {
            if (ctx.MergedController == null || deletedPaths.Count == 0) return;

            var clips = new HashSet<AnimationClip>();
            foreach (var clip in ctx.MergedController.animationClips)
            {
                if (clip != null) clips.Add(clip);
            }
            foreach (var clip in clips)
            {
                foreach (var binding in UnityEditor.AnimationUtility.GetCurveBindings(clip))
                {
                    if (deletedPaths.Any(prefix => binding.path == prefix
                            || binding.path.StartsWith(prefix + "/", System.StringComparison.Ordinal)))
                    {
                        UnityEditor.AnimationUtility.SetEditorCurve(clip, binding, null);
                    }
                }
            }
            deletedPaths.Clear();
        }

        // No source component, the way a synthesized chain has none, but the
        // rig's own numbers rather than a preset.
        static bool Write(BridgeContext ctx, Transform bone, BridgeContext.HelperRigChain from)
        {
            if (bone == null || from == null) return false;
            var data = new PhysBoneChainData
            {
                SourceGameObject = bone.gameObject,
                Root = bone,
                InitiallyActive = bone.gameObject.activeInHierarchy,
                ComponentEnabled = true,
                IsAdvancedIntegration = from.IsAdvancedIntegration,
                Pull = from.Pull,
                Spring = from.Spring,
                Stiffness = from.Stiffness,
                Gravity = from.Gravity,
                Immobile = from.Immobile,
                LimitTypeName = "None",
                StandIn = true,
                Ignores = ctx.PhysBoneIgnores.Where(t => t != null && t != bone && t.IsChildOf(bone)).ToList(),
            };
#if AVATARBRIDGE_MAGICA
            if (ctx.Settings.physicsTarget == PhysicsTarget.MagicaCloth2)
            {
                return MagicaClothWriter.Write(ctx, data,
                    new Dictionary<VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider,
                        MagicaCloth2.ColliderComponent>()) != null;
            }
#endif
#if AVATARBRIDGE_DYNBONE
            if (ctx.Settings.physicsTarget == PhysicsTarget.DynamicBone)
            {
                DynamicBoneWriter.Write(ctx, data,
                    new Dictionary<VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider,
                        DynamicBoneColliderBase>());
                return true;
            }
#endif
            return false;
        }

        static void Report(BridgeContext ctx, int dropped, int bones, string where)
        {
            // No relay read from the rig, so there was no cascade to lose: say
            // what was found rather than describe one.
            if (dropped == 0)
            {
                ctx.Report.Warning(Category, $"{BridgeElements.Count(ctx.HelperRigChains.Count, "chain", "chains")} moved no mesh; no cloth made",
                    "None is weighted to a mesh, nothing rendered sits under them, and no constraint reads them, " +
                    "so a cloth there would move nothing. " +
                    (bones > 0 ? $"Removed with them: {BridgeElements.Count(bones, "transform", "transforms")}." : "Their bones are left in place."));
                return;
            }
            ctx.Report.Warning(Category, $"A physics addon did not survive conversion ({ctx.HelperRigChains.Count} chains)",
                "Helper bones feeding each other through constraints, with no mesh on them. Cloth simulates each " +
                "chain alone and would deform the body, so none was converted. " +
                $"Removed with it: {BridgeElements.Count(dropped, "constraint", "constraints")}, {BridgeElements.Count(bones, "transform", "transforms")}. {where}");
        }
    }
}
#endif
