#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

namespace AvatarBridge
{
    // Makes ParentConstraint offsets follow the avatar's scale, so a
    // hat stays on a shrinking head.
    //
    // Unity rotates a constraint offset by its source but never scales
    // it; the offset is a fixed number of metres. The fix: spend the
    // offset. A relay empty is created as a child of the source at the
    // world point the offset described, and the constraint repoints at
    // it with a zero offset. As a real child it inherits scale for
    // free. At 1x nothing moves, by construction.
    //
    // Not done in the animation: the scaler runs before constraints
    // convert, and the Size layer asserts with Write Defaults on.
    // This pass touches no clip and no layer.
    //
    // Left alone, each skipped whole: animated offsets, sources inside
    // a converted physics chain, sources outside the avatar, unlocked
    // constraints, sources flattened on an axis (divide by zero).
    //
    // The relay is placed by measurement wherever the output is
    // unambiguous; the formula stands in elsewhere, and the report
    // carries the largest gap seen between the two.
    public static class ConstraintScaleRelay
    {
        const string Category = "Avatar scaler";
        const string RelayPrefix = "AvatarBridge_ScaleRelay";

        const float OffsetEpsilonSq = 1e-8f;

        public static void Run(BridgeContext ctx)
        {
            // Only when this tool is the thing changing the scale. An avatar that never resizes has
            // nothing to gain here, and every constraint left untouched is one that cannot break.
            if (ctx == null || ctx.Target == null || ctx.Settings == null
                || !ctx.Settings.addAvatarScaler || !ctx.Settings.convertConstraints)
            {
                return;
            }
            try
            {
                Apply(ctx);
            }
            catch (Exception e)
            {
                ctx.Report.Warning(Category, "Could not re-anchor constraint offsets to the scale",
                    $"{e.GetType().Name}: {e.Message}. This pass walks the constraints in order, so " +
                    "a failure part way through leaves the ones before it re-anchored and the rest " +
                    "as they were — the avatar is otherwise fine, but hats and held items may drift " +
                    "when the height slider moves away from its default, and they may not all drift " +
                    "the same way. Converting again after fixing the cause re-anchors the lot.");
                Debug.LogException(e);
            }
        }

        static void Apply(BridgeContext ctx)
        {
            var root = ctx.Target.transform;
            var constraints = ctx.Target.GetComponentsInChildren<ParentConstraint>(true);
            if (constraints.Length == 0)
            {
                return;
            }

            var clothDriven = ClothDrivenTransforms(ctx);
            var animatedOffsets = ConstraintsWithAnimatedOffsets(ctx);

            int constraintsFixed = 0, relaysMade = 0, leftAlone = 0;
            var animated = new SortedSet<string>(StableSampleOrder.Instance);
            var simulated = new SortedSet<string>(StableSampleOrder.Instance);
            var external = new SortedSet<string>(StableSampleOrder.Instance);
            var degenerate = new SortedSet<string>(StableSampleOrder.Instance);
            var unlocked = new SortedSet<string>(StableSampleOrder.Instance);
            float worstDisagreement = -1f;
            string worstAt = null;

            foreach (var constraint in constraints)
            {
                if (constraint == null || constraint.sourceCount == 0 || !HasOffset(constraint))
                {
                    continue;
                }
                string path = ctx.PathInTarget(constraint.transform);

                if (!constraint.locked)
                {
                    unlocked.Add(path);
                    leftAlone++;
                    continue;
                }
                if (animatedOffsets.Contains(path))
                {
                    animated.Add(path);
                    leftAlone++;
                    continue;
                }

                string blocked = null;
                for (int i = 0; i < constraint.sourceCount && blocked == null; i++)
                {
                    var source = constraint.GetSource(i).sourceTransform;
                    if (source == null)
                    {
                        continue; // contributes nothing either way
                    }
                    if (!source.IsChildOf(root))
                    {
                        external.Add(path);
                        blocked = "external";
                    }
                    else if (clothDriven.Contains(source))
                    {
                        simulated.Add(path);
                        blocked = "simulated";
                    }
                    else if (IsFlat(source.lossyScale))
                    {
                        // A zero on any axis divides by zero, and a NaN
                        // reaching a constraint spreads downstream.
                        degenerate.Add(path);
                        blocked = "degenerate";
                    }
                }
                if (blocked != null)
                {
                    leftAlone++;
                    continue;
                }

                float disagreement = Disagreement(constraint);
                if (disagreement > worstDisagreement)
                {
                    worstDisagreement = disagreement;
                    worstAt = path;
                }

                int made = Relay(constraint);
                if (made > 0)
                {
                    constraintsFixed++;
                    relaysMade += made;
                }
            }

            if (constraintsFixed == 0 && leftAlone == 0)
            {
                return;
            }

            string note =
                "A parent constraint holds its target a fixed number of METRES from its source, and " +
                "Unity never scales that gap — so with the height slider the body moved and the props " +
                "didn't. Each offset is now carried by a small empty parented to the source bone " +
                $"(\"{RelayPrefix}_…\"), which inherits the avatar's scale, so the gap grows and shrinks " +
                "with you. Nothing moves at the default size: the relays are placed exactly where the " +
                "offsets already put things.";
            if (animated.Count > 0)
            {
                note += $"\n\nLeft as they were, offsets are animated ({animated.Count}): " +
                        Join(animated) + ". Re-anchoring these would fight the animation driving them.";
            }
            if (simulated.Count > 0)
            {
                note += $"\n\nLeft as they were, constrained to a simulated bone ({simulated.Count}): " +
                        Join(simulated) + ". Hanging a relay off a cloth or dynamic-bone chain would " +
                        "add a particle to it and change how the chain moves.";
            }
            if (external.Count > 0)
            {
                note += $"\n\nLeft as they were, the source is outside this avatar ({external.Count}): " +
                        Join(external) + ". An offset from a world anchor is meant to be in metres.";
            }
            if (unlocked.Count > 0)
            {
                note += $"\n\nLeft as they were, the constraint is unlocked ({unlocked.Count}): " +
                        Join(unlocked) + ". Unity re-derives an unlocked constraint's offset from " +
                        "the live transform, so it would put the old one straight back. Ticking " +
                        "Lock on those in the inspector and converting again picks them up.";
            }
            if (degenerate.Count > 0)
            {
                note += $"\n\nLeft as they were, the source bone has a zero on one scale axis " +
                        $"({degenerate.Count}): " + Join(degenerate) + ". Converting a position through " +
                        "a flattened transform produces NaN, which spreads.";
            }
            if (worstDisagreement > 0.001f)
            {
                note += $"\n\nWorth knowing: the largest gap between where a constraint had actually " +
                        $"put its target and where the offset says it should be was {worstDisagreement:0.###} m " +
                        $"(at {worstAt}). Those two normally agree exactly. They were placed by measurement, " +
                        "so the avatar is right either way — but if props sit wrong after this, that number " +
                        "is the thing to report.";
            }
            if (constraintsFixed > 0)
            {
                ctx.Report.Converted(Category,
                    $"{constraintsFixed} constraint(s) re-anchored so props follow the size slider " +
                    $"({relaysMade} relay object(s))", note);
            }
            else
            {
                ctx.Report.Approximated(Category,
                    $"{leftAlone} constraint(s) with an offset will drift when you resize", note);
            }
        }

        static int Relay(ParentConstraint constraint)
        {
            // Where the target is standing right now, when that is unambiguously the constraint's
            // own doing. Measuring beats predicting: it needs no belief about how Unity treats an
            // offset, and it lands the relay on the exact pixel the avatar already shows.
            bool measured = Measurable(constraint);
            Vector3 truth = constraint.transform.position;

            bool wasActive = constraint.constraintActive;
            constraint.constraintActive = false;
            int made = 0;
            try
            {
                for (int i = 0; i < constraint.sourceCount; i++)
                {
                    var source = constraint.GetSource(i);
                    var bone = source.sourceTransform;
                    Vector3 offset = constraint.GetTranslationOffset(i);
                    if (bone == null || offset.sqrMagnitude <= OffsetEpsilonSq)
                    {
                        continue;
                    }

                    var relay = new GameObject(RelayName(bone, constraint.transform));
                    relay.transform.SetParent(bone, false);
                    relay.transform.localRotation = Quaternion.identity;
                    relay.transform.localScale = Vector3.one;
                    // Assigning a world position lets Unity work out the
                    // local one, which matters on scaled bones.
                    // The fallback is the documented evaluation: Unity rotates a
                    // parent constraint's offset by the source and never scales it.
                    relay.transform.position = measured
                        ? truth
                        : bone.position + bone.rotation * offset;

                    source.sourceTransform = relay.transform;
                    constraint.SetSource(i, source);
                    constraint.SetTranslationOffset(i, Vector3.zero);
                    made++;
                }
            }
            finally
            {
                constraint.constraintActive = wasActive;
                EditorUtility.SetDirty(constraint);
            }
            return made;
        }

        static bool Measurable(ParentConstraint constraint)
        {
            const Axis All = Axis.X | Axis.Y | Axis.Z;
            if (constraint.sourceCount != 1 || constraint.translationAxis != All)
            {
                return false;
            }
            if (!constraint.constraintActive || !constraint.enabled
                || !constraint.gameObject.activeInHierarchy)
            {
                return false;
            }
            var source = constraint.GetSource(0);
            return source.sourceTransform != null
                && constraint.weight >= 0.999f && source.weight >= 0.999f;
        }

        static float Disagreement(ParentConstraint constraint)
        {
            if (!Measurable(constraint))
            {
                return -1f;
            }
            var bone = constraint.GetSource(0).sourceTransform;
            Vector3 predicted = bone.position + bone.rotation * constraint.GetTranslationOffset(0);
            return Vector3.Distance(predicted, constraint.transform.position);
        }

        static bool IsFlat(Vector3 scale)
        {
            const float Tiny = 1e-5f;
            return Mathf.Abs(scale.x) < Tiny || Mathf.Abs(scale.y) < Tiny || Mathf.Abs(scale.z) < Tiny;
        }

        static bool HasOffset(ParentConstraint constraint)
        {
            for (int i = 0; i < constraint.sourceCount; i++)
            {
                if (constraint.GetTranslationOffset(i).sqrMagnitude > OffsetEpsilonSq)
                {
                    return true;
                }
            }
            return false;
        }

        static string RelayName(Transform bone, Transform constrained)
        {
            // '/' would make Transform.Find read the name as a path and never match.
            string wanted = $"{RelayPrefix}_{constrained.name.Replace('/', '_')}";
            string candidate = wanted;
            int suffix = 2;
            while (bone.Find(candidate) != null)
            {
                candidate = $"{wanted} {suffix++}";
            }
            return candidate;
        }

        static HashSet<Transform> ClothDrivenTransforms(BridgeContext ctx)
        {
            var driven = new HashSet<Transform>();
            foreach (var chain in ctx.ConvertedPhysicsChains)
            {
                if (chain == null)
                {
                    continue;
                }
                var root = chain.Root != null ? chain.Root
                    : (chain.Source != null ? chain.Source.transform : null);
                if (root == null)
                {
                    continue;
                }
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    driven.Add(t);
                }
            }
            return driven;
        }

        static HashSet<string> ConstraintsWithAnimatedOffsets(BridgeContext ctx)
        {
            var paths = new HashSet<string>();
            var clips = new HashSet<AnimationClip>();
            foreach (var animator in ctx.Target.GetComponentsInChildren<Animator>(true))
            {
                if (animator.runtimeAnimatorController != null)
                {
                    Collect(animator.runtimeAnimatorController, clips);
                }
            }
            // Directly as well as through the Animator: a controller that would crash Unity is
            // deliberately never assigned to one, and those avatars need the check just as much.
            if (ctx.MergedController != null)
            {
                Collect(ctx.MergedController, clips);
            }

            foreach (var clip in clips)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type == typeof(ParentConstraint) &&
                        binding.propertyName.StartsWith("m_TranslationOffsets", StringComparison.Ordinal))
                    {
                        paths.Add(binding.path);
                    }
                }
            }
            return paths;
        }

        static void Collect(RuntimeAnimatorController controller, HashSet<AnimationClip> into)
        {
            foreach (var clip in controller.animationClips)
            {
                if (clip != null)
                {
                    into.Add(clip);
                }
            }
        }

        static string Join(SortedSet<string> paths)
        {
            var shown = new List<string>();
            foreach (var p in paths)
            {
                if (shown.Count == 6)
                {
                    shown.Add($"and {paths.Count - 6} more");
                    break;
                }
                shown.Add(p);
            }
            return string.Join(", ", shown);
        }
    }
}
#endif
