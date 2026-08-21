// Clears up after a physics addon that could not be converted, and says
// where to put the physics back.
//
// Rigs like cake PB are a cascade: a dozen staged bones, each doing one
// job and feeding the next, with constraints copying the composed result
// onto the avatar's real bones. No mesh is skinned to any of it.
//
// That cascade cannot be rebuilt out of independent solvers. Converting
// each stage separately gives a dozen things swinging their own way, and
// the constraints faithfully copy the mess onto the mesh. Five attempts at
// reproducing one of these produced five different wrong avatars, so the
// tool no longer tries.
//
// What it does instead: PhysBoneConverter skips the rig's chains, this
// pass removes every relay reading from the dead rig, and the report names
// the bone to put a cloth on. A relay left behind would copy a pose
// nothing moves any more, and a position or scale relay OVERRIDES
// animation, so removing it is also what lets a contact-driven squish
// reach the bone.
//
// Runs after the constraint pass: the relays do not exist until then.
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

        public static void Run(BridgeContext ctx)
        {
            if (ctx.HelperRigChains.Count == 0) return;

            var skinned = new HashSet<Transform>();
            foreach (var skin in ctx.Target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin == null || skin.bones == null) continue;
                foreach (var b in skin.bones)
                {
                    if (b != null) skinned.Add(b);
                }
            }

            // The rig is the highest ancestor whose whole subtree moves no
            // mesh. Climbing stops at the first ancestor holding a skinned
            // bone, which keeps an ordinary chain from dragging the armature
            // in with it. Its scale and position targets live outside the
            // PhysBones, so the chain roots alone are not the rig.
            var rig = new HashSet<Transform>();
            var tops = new HashSet<Transform>();
            foreach (var chain in ctx.HelperRigChains)
            {
                if (chain.Root == null) continue;
                var top = chain.Root;
                for (var up = chain.Root.parent; up != null && up != ctx.Target.transform; up = up.parent)
                {
                    if (up.GetComponentsInChildren<Transform>(true).Any(skinned.Contains)) break;
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

                    // The bone the rig was driving, which is where a cloth
                    // goes. It is often not weighted itself — the relay
                    // writes a holder and the mesh hangs below it — so the
                    // test is whether a cloth there would move any mesh at
                    // all, not whether this exact bone deforms. Climbing to
                    // a skinned ancestor instead lands on Hips, which would
                    // simulate the whole lower body.
                    if (MovesMesh(t, skinned) && !targets.Contains(t))
                    {
                        suggest.Add(t.name);
                        targets.Add(t);
                    }
                    Object.DestroyImmediate(component);
                    dropped++;
                }
            }

            // The rig is gone and nothing writes these bones any more, so a
            // cloth here is free to move them. Earlier attempts put one on
            // the same bone while relays were still writing it, and the two
            // fought; the relays going is what makes this work.
            //
            // Settings come from the springiest chain the rig had. The author
            // tuned those numbers, just on a bone nobody can see.
            var feel = ctx.HelperRigChains.OrderByDescending(c => c.Spring).FirstOrDefault();
            int made = 0;
            foreach (var bone in targets)
            {
                if (Write(ctx, bone, feel)) made++;
            }

            // Nothing simulates it, nothing reads from it, and no surviving
            // cloth borrows a collider out of it, so what remains is a bone
            // update per transform per frame for no motion. On one corpus
            // avatar that was 110 of 468 transforms.
            //
            // Only the tops, computed from what moves no mesh: a rig called
            // "cake_PB" also has real skinned bones called "cake_PB_L" beside
            // the armature, and deleting by name would take the wrong one.
            // Unless a chain that SURVIVED was handed one of the rig's
            // colliders. Deleting then leaves a null in that chain's list and
            // it silently loses collision. Keeping a rig is cheap; breaking a
            // hair chain's collision is not, and would not be noticed until
            // somebody's hair went through their shoulder in game.
            int removedBones = 0, removedCurves = 0, kept = 0;
            foreach (var top in tops)
            {
                if (top == null) continue;
                if (Borrowed(ctx, top) || CarriesContacts(top))
                {
                    kept++;
                    continue;
                }
                removedCurves += StripCurvesUnder(ctx, top);
                removedBones += top.GetComponentsInChildren<Transform>(true).Length;
                Object.DestroyImmediate(top.gameObject);
            }

            string where = made > 0
                ? $"A cloth was put on {string.Join(" and ", suggest.Take(4))} instead, tuned from the " +
                  "rig's own numbers. That is one chain where the source had a cascade, so it will not " +
                  "feel identical — tune it, or delete it and build your own."
                : suggest.Count > 0
                ? $"Put a MagicaCloth or DynamicBone on {string.Join(" and ", suggest.Take(4))}" +
                  (suggest.Count > 4 ? $" (and {suggest.Count - 4} more)" : "") +
                  " to get the movement back — that is what this rig was driving, and a cloth there moves " +
                  "the mesh hanging below it. A guess worth checking, not a measurement."
                : "Nothing it drove is weighted to a mesh, so there is no obvious bone to put a cloth on.";

            if (kept > 0)
            {
                ctx.Report.Approximated(Category, kept + " helper rig(s) left in place",
                    "Either a cloth that survived borrows a collider from inside them, or they carry contacts an " +
                    "avatar still needs. Deleting would have left a chain with no collision, or a trigger " +
                    "nobody can touch any more. The rig is inert either way; the transforms are the price.");
            }
            Report(ctx, dropped, removedBones, removedCurves, where);
        }

        // Would a cloth rooted here move anything? True if this bone or
        // anything under it deforms a mesh.
        static bool MovesMesh(Transform t, HashSet<Transform> skinned)
            => t.GetComponentsInChildren<Transform>(true).Any(skinned.Contains);

        // Contacts living inside the rig. Cake PB keeps its squish triggers
        // in there, and the corpus caught this: BHFBunny went from five
        // contacts to none, CowBotSFW from two to none. The rig's physics is
        // dead but a trigger in it is still what a hand touches, so the whole
        // rig stays rather than lose them. Transforms are cheaper than a
        // feature that silently stops responding.
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
            return false;
        }

        // Curves addressing what is about to be deleted. They drove constraint
        // weights and collider toggles inside the rig; left behind they would
        // address objects that no longer exist.
        static int StripCurvesUnder(BridgeContext ctx, Transform top)
        {
            if (ctx.MergedController == null) return 0;
            string prefix = BridgeContext.RelativePath(ctx.Target.transform, top);
            if (string.IsNullOrEmpty(prefix)) return 0;

            int removed = 0;
            var clips = new HashSet<AnimationClip>();
            foreach (var clip in ctx.MergedController.animationClips)
            {
                if (clip != null) clips.Add(clip);
            }
            foreach (var clip in clips)
            {
                foreach (var binding in UnityEditor.AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.path != prefix
                        && !binding.path.StartsWith(prefix + "/", System.StringComparison.Ordinal))
                    {
                        continue;
                    }
                    UnityEditor.AnimationUtility.SetEditorCurve(clip, binding, null);
                    removed++;
                }
            }
            return removed;
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
                Pull = from.Pull,
                Spring = from.Spring,
                Stiffness = from.Stiffness,
                Gravity = from.Gravity,
                Immobile = from.Immobile,
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

        static void Report(BridgeContext ctx, int dropped, int bones, int curves, string where)
        {
            ctx.Report.Warning(Category, $"A physics addon did not survive conversion ({ctx.HelperRigChains.Count} chains)",
                "This avatar carries a staged physics rig — a cascade of helper bones, each doing one job and " +
                "feeding the next, with constraints copying the result onto the bones your mesh actually uses. " +
                "No mesh is skinned to any of it. VRChat composes that cascade; MagicaCloth2 and DynamicBone " +
                "simulate each chain independently, which does not compose and lands on the body as " +
                $"deformation, so none of it was converted. {dropped} constraint(s) reading from it were removed " +
                "as well, since they would copy a pose nothing moves any more, and a position or scale relay " +
                $"also overrides animation. The rig itself was deleted with it: {bones} transform(s) that " +
                $"nothing simulated any more, and {curves} curve(s) that addressed them. {where}");
        }
    }
}
#endif
