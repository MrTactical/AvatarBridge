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
                    UnityEngine.Object.DestroyImmediate(component);
                }
            }

            if (converted > 0)
            {
                ctx.Report.Converted(Category, $"{converted} VRC constraint(s) -> Unity constraints");
            }
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
            if (Get<Transform>(vrc, "TargetTransform", null) != null)
            {
                ctx.Report.Approximated(Category, component.name,
                    "VRC 'Target Transform' redirection is not supported; the constraint now affects its own transform.");
            }
            return true;
        }

        static bool ConvertParent(BridgeContext ctx, Component vrc)
        {
            var unity = GetOrAdd<ParentConstraint>(vrc, out bool existed);
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
            var unity = GetOrAdd<PositionConstraint>(vrc, out bool existed);
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
            var unity = GetOrAdd<RotationConstraint>(vrc, out bool existed);
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
            var unity = GetOrAdd<ScaleConstraint>(vrc, out bool existed);
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
            var unity = GetOrAdd<AimConstraint>(vrc, out bool existed);
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
            var unity = GetOrAdd<LookAtConstraint>(vrc, out bool existed);
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
        static T GetOrAdd<T>(Component vrc, out bool existed) where T : Component
        {
            var existing = vrc.gameObject.GetComponent<T>();
            existed = existing != null;
            return existed ? existing : vrc.gameObject.AddComponent<T>();
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
