#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Animations;

namespace AvatarBridge
{
    /// <summary>
    /// VRC Constraints -> Unity constraints (which ChilloutVR runs natively).
    ///
    /// VRC constraints mirror Unity's, so sources/weights/offsets/rest values transfer
    /// almost 1:1. Access to the VRC components is via reflection so this file compiles
    /// against any VRChat SDK version; missing members degrade to report entries.
    /// </summary>
    public static class ConstraintConverter
    {
        const string Category = "Constraints";

        public static void Run(BridgeContext ctx)
        {
            if (!ctx.Settings.convertConstraints)
            {
                return;
            }

            int converted = 0;
            var localSpaceRelays = new List<string>();
            foreach (var component in ctx.Target.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    continue;
                }
                string typeName = component.GetType().Name;
                bool ok;
                try
                {
                    switch (typeName)
                    {
                        case "VRCParentConstraint": ok = ConvertParent(ctx, component); break;
                        case "VRCPositionConstraint": ok = ConvertPosition(ctx, component); break;
                        case "VRCRotationConstraint": ok = ConvertRotation(ctx, component); break;
                        case "VRCScaleConstraint": ok = ConvertScale(ctx, component); break;
                        case "VRCAimConstraint": ok = ConvertAim(ctx, component); break;
                        case "VRCLookAtConstraint": ok = ConvertLookAt(ctx, component); break;
                        default: continue;
                    }
                }
                catch (Exception e)
                {
                    // One malformed constraint must not abort the whole conversion.
                    // The throwing site goes into the REPORT (not just the console) so the
                    // report alone is enough to diagnose — no Editor.log needed.
                    ctx.Report.Warning(Category, component.name,
                        $"{typeName} conversion failed and was skipped: {e.GetType().Name}: {e.Message} [{TopFrame(e)}]");
                    Debug.LogWarning($"[AvatarBridge] {typeName} on '{component.name}' " +
                        $"(path '{ctx.PathInTarget(component.transform)}') could not be converted:\n{e}");
                    continue;
                }
                if (ok)
                {
                    converted++;
                    NoteLocalSpace(ctx, component, localSpaceRelays);
                    UnityEngine.Object.DestroyImmediate(component);
                }
            }

            if (converted > 0)
            {
                ctx.Report.Converted(Category, $"{converted} VRC constraint(s) -> Unity constraints");
            }
            ReportLocalSpace(ctx, localSpaceRelays);
        }

        /// <summary>
        /// VRChat's constraints can solve in LOCAL space — <c>VRCConstraintJob</c> reads the
        /// source's <c>localPosition</c>/<c>localRotation</c> rather than its world pose — and
        /// that is the default in the SDK's own inspector. Unity's constraints have no such mode:
        /// they always solve in world space, and ChilloutVR ships no local-space equivalent (its
        /// constraint types are Unity's own).
        ///
        /// The two agree exactly while the constrained transform and its source hang off the SAME
        /// parent, because that parent's rotation appears on both sides and cancels. They diverge
        /// by <c>inverse(targetParent.rotation) * sourceParent.rotation</c> the moment the two sit
        /// in different chains — so only those are worth naming.
        ///
        /// Which is precisely how a "hidden rig" quadruped is built: a decoy humanoid skeleton
        /// nothing renders, relayed bone by bone onto the real one. Every source is in the other
        /// chain, so solving in world space hands each real bone the biped's orientation instead
        /// of its pose.
        /// </summary>
        static void NoteLocalSpace(BridgeContext ctx, Component vrc, List<string> crossChain)
        {
            if (!Get(vrc, "SolveInLocalSpace", false))
            {
                return;
            }
            // The Unity constraint may have been placed on TargetTransform rather than here.
            //
            // NOT "?? vrc.transform": an unassigned Transform field comes back as Unity's FAKE
            // null — a live C# reference whose overloaded == reports null while ?? does not, so
            // ?? hands back the fake and the next .parent throws UnassignedReferenceException.
            // Every other TargetTransform read in this file already compares with !=; this one
            // has to as well.
            var target = Get<Transform>(vrc, "TargetTransform", null);
            var constrained = target != null ? target : vrc.transform;
            foreach (var s in ReadSources(vrc))
            {
                if (s.Transform == null || Mathf.Approximately(s.Weight, 0f)
                    || s.Transform.parent == constrained.parent)
                {
                    continue;
                }
                crossChain.Add($"`{ctx.PathInTarget(constrained)}` from `{ctx.PathInTarget(s.Transform)}`");
                return; // one line per constraint is enough to find it
            }
        }

        static void ReportLocalSpace(BridgeContext ctx, List<string> crossChain)
        {
            if (crossChain.Count == 0)
            {
                return;
            }
            ctx.Report.Warning(Category,
                $"{crossChain.Count} constraint(s) relayed a bone from another chain in local space",
                "These will not follow their source correctly, and there is no option to change — " +
                "it is a gap in the conversion, so the avatar is worth reporting.\n\n" +
                "VRChat solved them against the source's **local** rotation. Unity's constraints " +
                "only ever solve in world space, and ChilloutVR ships no local-space equivalent, so " +
                "each of these now inherits its source's world orientation instead of its pose. " +
                "Constraints whose source shares their own parent are unaffected — there the two " +
                "spaces agree exactly — and are not listed.\n\n" +
                "An avatar that drives a real skeleton from a hidden humanoid one (how most " +
                "quadrupeds are built) is made entirely of these, which is why such avatars arrive " +
                "stuck in their rest pose with only the tracked parts moving.\n\n" +
                string.Join("\n", crossChain));
        }

        // ------------------------------------------------------------------------------

        class SourceData
        {
            public Transform Transform;
            public float Weight;
            public Vector3 ParentPositionOffset;
            public Vector3 ParentRotationOffset;
        }

        static List<SourceData> ReadSources(object vrcConstraint)
        {
            var result = new List<SourceData>();
            object sources = Get<object>(vrcConstraint, "Sources", null);
            if (!(sources is IEnumerable enumerable))
            {
                return result;
            }
            foreach (var item in enumerable)
            {
                if (item == null)
                {
                    continue;
                }
                var transform = Get<Transform>(item, "SourceTransform", null);
                if (transform == null)
                {
                    continue; // a source with no transform constrains to nothing — and null-sources crash AddSource
                }
                result.Add(new SourceData
                {
                    Transform = transform,
                    Weight = Get(item, "Weight", 1f),
                    ParentPositionOffset = Get(item, "ParentPositionOffset", Vector3.zero),
                    ParentRotationOffset = Get(item, "ParentRotationOffset", Vector3.zero)
                });
            }
            return result;
        }

        static void ApplyCommon<T>(object vrc, T unity) where T : Behaviour, IConstraint
        {
            unity.weight = Get(vrc, "GlobalWeight", 1f);
            unity.locked = Get(vrc, "Locked", true);
            // Activate last so Unity doesn't recompute rest values.
            unity.constraintActive = Get(vrc, "IsActive", true);
        }

        static bool WarnIfUnsupported(BridgeContext ctx, object vrc, Component component)
        {
            if (Get(vrc, "FreezeToWorld", false))
            {
                ctx.Report.Approximated(Category, component.name,
                    "'Freeze To World' has no Unity constraint equivalent and was dropped.");
            }
            var target = Get<Transform>(vrc, "TargetTransform", null);
            if (target != null && target != component.transform)
            {
                var host = HostFor(ctx, component);
                if (host == target.gameObject)
                {
                    ctx.Report.Converted(Category, component.name,
                        $"Drove another transform via VRC 'Target Transform'; the Unity constraint was " +
                        $"placed on that target ({ctx.PathInTarget(target)}) instead, since Unity's " +
                        "constraints only ever affect the object they sit on.");
                }
                else
                {
                    ctx.Report.Approximated(Category, component.name,
                        $"'Target Transform' points at \"{target.name}\", which is outside this avatar, so the " +
                        "redirection was dropped and the constraint now affects its own transform. " +
                        "Whatever it was driving will not move.");
                }
            }
            return true;
        }

        static bool ConvertParent(BridgeContext ctx, Component vrc)
        {
            var unity = GetOrAdd<ParentConstraint>(ctx, vrc, out bool existed);
            var sources = ReadSources(vrc);
            foreach (var s in sources)
            {
                int idx = unity.AddSource(new ConstraintSource { sourceTransform = s.Transform, weight = s.Weight });
                unity.SetTranslationOffset(idx, s.ParentPositionOffset);
                unity.SetRotationOffset(idx, s.ParentRotationOffset);
            }
            if (existed)
            {
                ReportMerged(ctx, vrc, "parent");
                return true; // keep the first constraint's rest/axis; just merge these sources in
            }
            unity.translationAtRest = Get(vrc, "PositionAtRest", vrc.transform.localPosition);
            unity.rotationAtRest = Get(vrc, "RotationAtRest", vrc.transform.localEulerAngles);
            unity.translationAxis = AxesFrom(vrc, "AffectsPositionX", "AffectsPositionY", "AffectsPositionZ");
            unity.rotationAxis = AxesFrom(vrc, "AffectsRotationX", "AffectsRotationY", "AffectsRotationZ");
            WarnIfUnsupported(ctx, vrc, vrc);
            ApplyCommon(vrc, unity);
            ctx.Report.Converted(Category, ctx.PathInTarget(vrc.transform), "Parent constraint");
            return true;
        }

        static bool ConvertPosition(BridgeContext ctx, Component vrc)
        {
            var unity = GetOrAdd<PositionConstraint>(ctx, vrc, out bool existed);
            foreach (var s in ReadSources(vrc))
            {
                unity.AddSource(new ConstraintSource { sourceTransform = s.Transform, weight = s.Weight });
            }
            if (existed)
            {
                ReportMerged(ctx, vrc, "position");
                return true;
            }
            unity.translationOffset = Get(vrc, "PositionOffset", Vector3.zero);
            unity.translationAtRest = Get(vrc, "PositionAtRest", vrc.transform.localPosition);
            unity.translationAxis = AxesFrom(vrc, "AffectsPositionX", "AffectsPositionY", "AffectsPositionZ");
            WarnIfUnsupported(ctx, vrc, vrc);
            ApplyCommon(vrc, unity);
            ctx.Report.Converted(Category, ctx.PathInTarget(vrc.transform), "Position constraint");
            return true;
        }

        static bool ConvertRotation(BridgeContext ctx, Component vrc)
        {
            var unity = GetOrAdd<RotationConstraint>(ctx, vrc, out bool existed);
            foreach (var s in ReadSources(vrc))
            {
                unity.AddSource(new ConstraintSource { sourceTransform = s.Transform, weight = s.Weight });
            }
            if (existed)
            {
                ReportMerged(ctx, vrc, "rotation");
                return true;
            }
            unity.rotationOffset = Get(vrc, "RotationOffset", Vector3.zero);
            unity.rotationAtRest = Get(vrc, "RotationAtRest", vrc.transform.localEulerAngles);
            unity.rotationAxis = AxesFrom(vrc, "AffectsRotationX", "AffectsRotationY", "AffectsRotationZ");
            WarnIfUnsupported(ctx, vrc, vrc);
            ApplyCommon(vrc, unity);
            ctx.Report.Converted(Category, ctx.PathInTarget(vrc.transform), "Rotation constraint");
            return true;
        }

        static bool ConvertScale(BridgeContext ctx, Component vrc)
        {
            var unity = GetOrAdd<ScaleConstraint>(ctx, vrc, out bool existed);
            foreach (var s in ReadSources(vrc))
            {
                unity.AddSource(new ConstraintSource { sourceTransform = s.Transform, weight = s.Weight });
            }
            if (existed)
            {
                ReportMerged(ctx, vrc, "scale");
                return true;
            }
            unity.scaleOffset = Get(vrc, "ScaleOffset", Vector3.one);
            unity.scaleAtRest = Get(vrc, "ScaleAtRest", vrc.transform.localScale);
            unity.scalingAxis = AxesFrom(vrc, "AffectsScaleX", "AffectsScaleY", "AffectsScaleZ");
            WarnIfUnsupported(ctx, vrc, vrc);
            ApplyCommon(vrc, unity);
            ctx.Report.Converted(Category, ctx.PathInTarget(vrc.transform), "Scale constraint");
            return true;
        }

        static bool ConvertAim(BridgeContext ctx, Component vrc)
        {
            var unity = GetOrAdd<AimConstraint>(ctx, vrc, out bool existed);
            foreach (var s in ReadSources(vrc))
            {
                unity.AddSource(new ConstraintSource { sourceTransform = s.Transform, weight = s.Weight });
            }
            if (existed)
            {
                ReportMerged(ctx, vrc, "aim");
                return true;
            }
            unity.aimVector = Get(vrc, "AimAxis", Vector3.forward);
            unity.upVector = Get(vrc, "UpAxis", Vector3.up);
            unity.rotationAtRest = Get(vrc, "RotationAtRest", vrc.transform.localEulerAngles);
            unity.rotationOffset = Get(vrc, "RotationOffset", Vector3.zero);
            ctx.Report.Approximated(Category, ctx.PathInTarget(vrc.transform),
                "Aim constraint: world-up mode settings are not transferred; verify behaviour.");
            WarnIfUnsupported(ctx, vrc, vrc);
            ApplyCommon(vrc, unity);
            return true;
        }

        static bool ConvertLookAt(BridgeContext ctx, Component vrc)
        {
            var unity = GetOrAdd<LookAtConstraint>(ctx, vrc, out bool existed);
            foreach (var s in ReadSources(vrc))
            {
                unity.AddSource(new ConstraintSource { sourceTransform = s.Transform, weight = s.Weight });
            }
            if (existed)
            {
                ReportMerged(ctx, vrc, "look-at");
                return true;
            }
            unity.roll = Get(vrc, "Roll", 0f);
            var upTransform = Get<Transform>(vrc, "WorldUpTransform", null);
            if (upTransform != null)
            {
                unity.worldUpObject = upTransform;
                unity.useUpObject = Get(vrc, "UseUpTransform", true);
            }
            unity.rotationAtRest = Get(vrc, "RotationAtRest", vrc.transform.localEulerAngles);
            unity.rotationOffset = Get(vrc, "RotationOffset", Vector3.zero);
            WarnIfUnsupported(ctx, vrc, vrc);
            ApplyCommon(vrc, unity);
            ctx.Report.Converted(Category, ctx.PathInTarget(vrc.transform), "LookAt constraint");
            return true;
        }

        // ---------------------------------------------------------------- helpers ----

        /// <summary>
        /// Unity's constraint components are [DisallowMultipleComponent], but a VRChat object
        /// can carry several VRC constraints of the same kind. Reuse an existing Unity
        /// constraint (from converting the first of its kind) instead of letting
        /// AddComponent return null and NRE on the next use.
        /// </summary>
        /// <summary>
        /// Which object the Unity constraint goes on.
        ///
        /// A VRC constraint can sit on one object and drive another through its Target Transform.
        /// Unity's constraints have no such field — they always affect the transform they are
        /// attached to — so the redirection is honoured by moving the component instead: put the
        /// Unity constraint on the target, with the same sources.
        ///
        /// This is how Avatar Limb Scaling works, and it is not a niche trick. Its scale
        /// constraints live on proxy objects inside its own prefab and point at the avatar's real
        /// arm and leg bones. Dropping the redirection left each constraint scaling a hidden proxy,
        /// so the menu sliders moved, synced, and changed nothing anyone could see.
        ///
        /// Only redirected inside the avatar. A target somewhere else in the scene is not ours to
        /// add components to, and would not survive the upload anyway.
        /// </summary>
        static GameObject HostFor(BridgeContext ctx, Component vrc)
        {
            var target = Get<Transform>(vrc, "TargetTransform", null);
            if (target == null || target == vrc.transform)
            {
                return vrc.gameObject;
            }
            if (ctx.Target != null && !target.IsChildOf(ctx.Target.transform))
            {
                return vrc.gameObject;
            }
            return target.gameObject;
        }

        static T GetOrAdd<T>(BridgeContext ctx, Component vrc, out bool existed) where T : Component
        {
            var host = HostFor(ctx, vrc);
            var existing = host.GetComponent<T>();
            existed = existing != null;
            return existed ? existing : host.AddComponent<T>();
        }

        static void ReportMerged(BridgeContext ctx, Component vrc, string kind)
        {
            ctx.Report.Approximated(Category, ctx.PathInTarget(vrc.transform),
                $"Object had multiple {kind} constraints; Unity and ChilloutVR allow only one {kind} " +
                $"constraint per object, so this one's sources were merged into the existing constraint " +
                $"(the first constraint's offsets and rest values are kept).");
        }

        /// <summary>First AvatarBridge frame of an exception's stack, for the report line.</summary>
        static string TopFrame(Exception e)
        {
            if (string.IsNullOrEmpty(e.StackTrace))
            {
                return "no stack";
            }
            foreach (var raw in e.StackTrace.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("at AvatarBridge."))
                {
                    return line.Substring(3); // drop the leading "at "
                }
            }
            return e.StackTrace.Split('\n')[0].Trim();
        }

        static Axis AxesFrom(object vrc, string x, string y, string z)
        {
            Axis axes = Axis.None;
            if (Get(vrc, x, true)) axes |= Axis.X;
            if (Get(vrc, y, true)) axes |= Axis.Y;
            if (Get(vrc, z, true)) axes |= Axis.Z;
            return axes;
        }

        static T Get<T>(object target, string memberName, T fallback)
        {
            if (target == null)
            {
                return fallback;
            }
            var type = target.GetType();
            try
            {
                var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
                if (property != null && typeof(T).IsAssignableFrom(property.PropertyType))
                {
                    return (T)property.GetValue(target);
                }
                var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
                if (field != null && typeof(T).IsAssignableFrom(field.FieldType))
                {
                    return (T)field.GetValue(target);
                }
            }
            catch
            {
                // fall through to fallback
            }
            return fallback;
        }
    }
}
#endif
