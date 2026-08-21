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
            }

            // Every relay reading from the rig, wherever it sits and whatever
            // it writes. No exceptions: each one now copies a frozen pose.
            int dropped = 0;
            var suggest = new SortedSet<string>(System.StringComparer.Ordinal);
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
                    if (MovesMesh(t, skinned)) suggest.Add(t.name);
                    Object.DestroyImmediate(component);
                    dropped++;
                }
            }

            string where = suggest.Count > 0
                ? $"Put a MagicaCloth or DynamicBone on {string.Join(" and ", suggest.Take(4))}" +
                  (suggest.Count > 4 ? $" (and {suggest.Count - 4} more)" : "") +
                  " to get the movement back — that is what this rig was driving, and a cloth there moves " +
                  "the mesh hanging below it. A guess worth checking, not a measurement."
                : "Nothing it drove is weighted to a mesh, so there is no obvious bone to put a cloth on.";

            Report(ctx, dropped, where);
        }

        // Would a cloth rooted here move anything? True if this bone or
        // anything under it deforms a mesh.
        static bool MovesMesh(Transform t, HashSet<Transform> skinned)
            => t.GetComponentsInChildren<Transform>(true).Any(skinned.Contains);

        static void Report(BridgeContext ctx, int dropped, string where)
        {
            ctx.Report.Warning(Category, $"A physics addon did not survive conversion ({ctx.HelperRigChains.Count} chains)",
                "This avatar carries a staged physics rig — a cascade of helper bones, each doing one job and " +
                "feeding the next, with constraints copying the result onto the bones your mesh actually uses. " +
                "No mesh is skinned to any of it. VRChat composes that cascade; MagicaCloth2 and DynamicBone " +
                "simulate each chain independently, which does not compose and lands on the body as " +
                $"deformation, so none of it was converted. {dropped} constraint(s) reading from it were removed " +
                "as well, since they would copy a pose nothing moves any more, and a position or scale relay " +
                $"also overrides animation. {where}");
        }
    }
}
#endif
