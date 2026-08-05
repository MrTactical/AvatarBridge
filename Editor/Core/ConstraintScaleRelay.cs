#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

namespace AvatarBridge
{
    /// <summary>
    /// Makes <see cref="ParentConstraint"/> offsets follow the avatar's scale, so a hat stays on a
    /// shrinking head instead of hanging above it.
    ///
    /// THE PROBLEM. Unity evaluates a parent constraint as
    /// <c>source.position + source.rotation * translationOffset</c>. The offset is rotated by the
    /// source but never SCALED by it — it is a fixed number of metres, whatever the rig is doing.
    /// The avatar scaler animates the root's localScale, so every bone moves closer together while
    /// each constraint keeps holding its target the same absolute distance away. Shrink and props
    /// hang off you; grow and they sink inside you. On the avatar this was found on, the hat sat
    /// 14.9 cm above the head bone and stayed there at every size.
    ///
    /// THE FIX. Don't animate the offset — spend the offset. For each source, a small empty
    /// ("AvatarBridge_ScaleRelay_&lt;target&gt;") is created as a CHILD of that source, placed at
    /// exactly the world point the offset described, and the constraint is re-pointed at the relay
    /// with a zero offset. Being a real child, the relay inherits the source's scale for free, so
    /// the gap between bone and prop is now part of the hierarchy and scales with it. Rotation is
    /// untouched: the relay's local rotation is identity, so the source's rotation reaches the
    /// constraint exactly as before and the existing rotation offset still applies.
    ///
    /// At 1× this changes nothing — by construction the relay sits where the offset already put
    /// the target, so nothing on the avatar moves. That is also how to check it: convert, scale
    /// the avatar root in the scene, and watch the prop track instead of drift.
    ///
    /// WHY NOT IN THE ANIMATION. 3.4.5 tried the obvious thing — write scaled copies of every
    /// offset into the nine generated scale clips — and it made an avatar render pure white in
    /// play mode and crashed the editor on scene reload. Two reasons it was the wrong shape:
    /// the scaler runs inside AnimatorMerger, BEFORE constraints are converted and before
    /// AlignLocalSpaceRelays re-parents transforms (so it baked paths that were about to change),
    /// and the Size layer's state has Write Defaults ON and sits LAST, so anything its clips touch
    /// it asserts over the whole avatar every frame. This pass touches no clip and no layer.
    ///
    /// WHAT IT DELIBERATELY LEAVES ALONE, and why each one is skipped whole rather than partly:
    ///   - constraints whose offsets are ANIMATED — zeroing an offset a clip drives would hand
    ///     control of the prop to a curve that no longer matches it;
    ///   - sources inside a converted physics chain — a new child of a simulated bone becomes a
    ///     new particle, and quietly changing how someone's tail moves is not a fair trade for a
    ///     hat that fits;
    ///   - sources outside this avatar — an offset from a world anchor is in metres on purpose;
    ///   - UNLOCKED constraints — Unity re-derives their offsets from the live transform, so it
    ///     would write the old offset straight back;
    ///   - sources flattened on an axis — the world→local conversion would divide by zero and put
    ///     a NaN somewhere it can spread from.
    ///
    /// The relay is placed by MEASUREMENT wherever the constraint's output is unambiguous (one
    /// source, full weight, all three axes, live), so the common case rests on no belief about
    /// Unity's evaluation at all. Where it can't be measured the documented formula stands in, and
    /// the report carries the largest gap ever seen between the two — normally zero, and the first
    /// thing to look at if a prop lands wrong.
    /// </summary>
    public static class ConstraintScaleRelay
    {
        const string Category = "Avatar scaler";
        const string RelayPrefix = "AvatarBridge_ScaleRelay";

        /// <summary>Below this an offset is rounding noise from the FBX, not a deliberate gap.
        /// Squared, so 1e-8 is a tenth of a millimetre.</summary>
        const float OffsetEpsilonSq = 1e-8f;

        public static void Run(BridgeContext ctx)
        {
            // Only when we are the thing changing the scale. An avatar that never resizes has
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
                    $"{e.GetType().Name}: {e.Message}. The avatar is otherwise fine — hats and held " +
                    "items may drift when the height slider moves away from its default.");
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
                        // A zero on any axis makes the world→local conversion divide by zero, and
                        // a NaN reaching a constraint spreads to everything downstream of it.
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

        /// <summary>
        /// Re-points every offset source of one constraint at a relay child. Returns how many were
        /// moved. The constraint is deactivated across the edit and restored afterwards — Unity
        /// recomputes rest values off a live constraint, and this pass exists precisely because
        /// nobody wants it re-deriving offsets while the sources are being swapped.
        /// </summary>
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
                    // Assigning a WORLD position lets Unity work out the local one, which matters
                    // on rigs whose bones carry a scale of their own — the FBX import scale is the
                    // common case. The fallback is the documented evaluation: Unity rotates a
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

        /// <summary>
        /// True when the constrained transform's current world position IS this constraint's
        /// output and nothing else's: one source at full weight, full constraint weight, all three
        /// translation axes driven, and everything live enough to have been evaluated.
        /// </summary>
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

        /// <summary>
        /// Metres between where the constraint has actually put its target and where the documented
        /// evaluation says it should be. Negative when the constraint isn't in a state that can be
        /// measured. Reported rather than acted on: it is only ever non-zero if the assumption this
        /// pass rests on is wrong, and a number in the report beats finding that out from a photo
        /// of a hat in the wrong place.
        /// </summary>
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

        /// <summary>A scale with a zero (or near-zero) axis: world→local would divide by it.</summary>
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

        /// <summary>Named after what it holds up, so the hierarchy explains itself; suffixed only
        /// if that bone already carries a relay, which happens when several props hang off the
        /// head.</summary>
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

        /// <summary>Every transform a converted cloth or dynamic-bone chain simulates.</summary>
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

        /// <summary>
        /// Paths of constraints whose translation offsets some clip drives. VRChat's own offset
        /// curves are dropped during conversion (Unity has no matching binding), so anything found
        /// here came from a Unity constraint the avatar already had — rare, and exactly the case
        /// where zeroing an offset would be destructive.
        /// </summary>
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
