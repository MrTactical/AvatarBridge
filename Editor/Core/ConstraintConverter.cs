#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

namespace AvatarBridge
{
    // VRC Constraints -> Unity constraints (which ChilloutVR runs natively).
    //
    // VRC constraints mirror Unity's, so sources/weights/offsets/rest values transfer
    // almost 1:1. Access to the VRC components is via reflection so this file compiles
    // against any VRChat SDK version; missing members degrade to report entries.
    public static class ConstraintConverter
    {
        const string Category = "Constraints";

        static HashSet<Transform> reparented = new HashSet<Transform>();

        static Dictionary<string, string> relocated = new Dictionary<string, string>();

        internal static readonly string[] VrcConstraintTypeNames =
        {
            "VRCParentConstraint", "VRCPositionConstraint", "VRCRotationConstraint",
            "VRCScaleConstraint", "VRCAimConstraint", "VRCLookAtConstraint"
        };

        public static void Run(BridgeContext ctx)
        {
            if (!ctx.Settings.convertConstraints)
            {
                return;
            }

            int converted = 0;
            var localSpaceRelays = new List<string>();
            var mirrored = new List<string>();
            var disabledLocalSpace = new List<string>();
            animatedRotationPaths = null;   // per conversion, like `relocated` below
            // MUST run before anything converts: a Unity constraint bakes its rest offsets
            // against the parent the transform has when it is created.
            relocated = new Dictionary<string, string>();   // per conversion, never carried over
            reparented = AlignLocalSpaceRelays(ctx);
            // VrcConstraintTypeNames below gates this loop deliberately, rather than sitting
            // beside it as a list someone has to remember to update: AvatarAdvisor counts what
            // this pass would convert, and a name in one place but not the other would have it
            // promising a conversion that never happens (or staying quiet about one that does).
            var realigned = reparented;
            foreach (var component in ctx.Target.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    continue;
                }
                string typeName = component.GetType().Name;
                if (Array.IndexOf(VrcConstraintTypeNames, typeName) < 0)
                {
                    continue;
                }
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
                        default: continue;   // unreachable: the guard above already filtered
                    }
                }
                catch (Exception e)
                {
                    // One malformed constraint must not abort the whole
                    // conversion. The throwing site goes into the report
                    // so the report alone diagnoses it.
                    ctx.Report.Warning(Category, component.name,
                        $"{typeName} conversion failed and was skipped: {e.GetType().Name}: {e.Message} [{TopFrame(e)}]");
                    Debug.LogWarning($"[AvatarBridge] {typeName} on '{component.name}' " +
                        $"(path '{ctx.PathInTarget(component.transform)}') could not be converted:\n{e}");
                    continue;
                }
                if (ok)
                {
                    converted++;
                    if (!realigned.Contains(component.transform))
                    {
                        NoteLocalSpace(ctx, component, localSpaceRelays);
                        DisableUnfollowableLocalSpace(ctx, component, typeName, disabledLocalSpace);
                    }
                    NoteMirrored(ctx, component, mirrored);
                    UnityEngine.Object.DestroyImmediate(component);
                }
            }

            if (converted > 0)
            {
                ctx.Report.Converted(Category, $"{converted} VRC constraint(s) -> Unity constraints");
            }
            // Curve rewriting happens later, on owned copies.
            // See RepointCurvesOnOurCopies.
            ReportLocalSpace(ctx, localSpaceRelays);
            ReportMirrored(ctx, mirrored);
            ReportDisabledLocalSpace(ctx, disabledLocalSpace);
        }

        static void DisableUnfollowableLocalSpace(BridgeContext ctx, Component vrc, string typeName,
            List<string> disabled)
        {
            if (typeName != "VRCRotationConstraint" || !Get(vrc, "SolveInLocalSpace", false))
            {
                return;
            }
            string path = ctx.PathInTarget(vrc.transform);
            if (!AnimatedRotationPaths(ctx).Contains(path))
            {
                return;
            }
            var unity = vrc.GetComponent<RotationConstraint>();
            if (unity == null)
            {
                return;
            }
            unity.constraintActive = false;
            unity.enabled = false;
            disabled.Add(path);
        }

        static HashSet<string> animatedRotationPaths;

        static HashSet<string> AnimatedRotationPaths(BridgeContext ctx)
        {
            if (animatedRotationPaths != null)
            {
                return animatedRotationPaths;
            }
            animatedRotationPaths = new HashSet<string>();
            var controllers = new List<RuntimeAnimatorController>();
            foreach (var animator in ctx.Target.GetComponentsInChildren<Animator>(true))
            {
                if (animator.runtimeAnimatorController != null)
                {
                    controllers.Add(animator.runtimeAnimatorController);
                }
            }
            if (ctx.MergedController != null)
            {
                controllers.Add(ctx.MergedController);
            }
            foreach (var controller in controllers)
            {
                foreach (var clip in controller.animationClips)
                {
                    if (clip == null)
                    {
                        continue;
                    }
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type == typeof(Transform)
                            && (binding.propertyName.StartsWith("m_LocalRotation", StringComparison.Ordinal)
                                || binding.propertyName.StartsWith("localEulerAngles", StringComparison.Ordinal)))
                        {
                            animatedRotationPaths.Add(binding.path);
                        }
                    }
                }
            }
            return animatedRotationPaths;
        }

        static void ReportDisabledLocalSpace(BridgeContext ctx, List<string> disabled)
        {
            if (disabled.Count == 0)
            {
                return;
            }
            ctx.Report.Converted(Category,
                $"{disabled.Count} unfollowable local-space constraint(s) disabled — their animation takes over",
                $"{string.Join(", ", disabled)} — each solved in LOCAL space in VRChat, could not be " +
                "re-parented to make Unity's world-space solving equivalent, AND is posed by an " +
                "animation in the controller. A wrong constraint overrides a right animation " +
                "(constraints evaluate after animators), so the constraint is disabled and the " +
                "animation keeps the bone where the author keyed it. What is lost is only the live " +
                "follow — on the avatar that forced this, windshield pupils that mirrored the eye " +
                "bones stay posed by the car animation instead of tracking eye movement.");
        }

        public static void RepointCurvesOnOurCopies(BridgeContext ctx) => RepointConstraintCurves(ctx);

        static bool SafeToRewrite(AnimationClip clip, BridgeContext ctx, out string sourcePath)
        {
            sourcePath = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(sourcePath))
            {
                return true;
            }
            string path = sourcePath.Replace('\\', '/');
            string output = (ctx.OutputDir ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            if (output.Length > 0 && path.StartsWith(output + "/", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            // NDMF writes Modular Avatar's and VRCFury's bake output here; "GeneratedAssets" is
            // the older spelling. Both are rebuilt from scratch on every bake.
            return path.Contains("__Generated")
                   || path.Contains("GeneratedAssets")
                   || path.Contains("/VRCFury/Generated");
        }

        static void RepointConstraintCurves(BridgeContext ctx)
        {
            // Nothing was built to repoint at. The VRC curves die with
            // their components; warning about them here would blame the
            // bake for a choice the user made.
            if (!ctx.Settings.convertConstraints)
            {
                return;
            }
            var clips = new HashSet<AnimationClip>();
            var controllers = new List<RuntimeAnimatorController>();
            foreach (var animator in ctx.Target.GetComponentsInChildren<Animator>(true))
            {
                if (animator.runtimeAnimatorController != null)
                {
                    controllers.Add(animator.runtimeAnimatorController);
                }
            }
            // The merged controller directly as well as via the Animator. A controller that would
            // crash Unity is deliberately never assigned to an Animator (see AnimatorMerger), and
            // reading the clip list only through the component would silently skip every curve on
            // exactly the avatars that need the most repair.
            if (ctx.MergedController != null)
            {
                controllers.Add(ctx.MergedController);
            }
            foreach (var controller in controllers)
            {
                foreach (var clip in controller.animationClips)
                {
                    if (clip != null)
                    {
                        clips.Add(clip);
                    }
                }
            }

            int repointed = 0, followed = 0;
            var dropped = new SortedSet<string>(StableSampleOrder.Instance);
            var lost = new SortedSet<string>(StableSampleOrder.Instance);        // the object is gone: something here removed it
            var neverBuilt = new SortedSet<string>(StableSampleOrder.Instance);  // the object is there, but never had a constraint
            var movedByBake = new SortedSet<string>(StableSampleOrder.Instance); // the object exists under a different parent

            var protectedSources = new SortedSet<string>(StableSampleOrder.Instance);

            foreach (var clip in clips)
            {
                bool mayRewrite = SafeToRewrite(clip, ctx, out string sourcePath);
                // GetCurveBindings hands back a copy, so rewriting inside the loop is safe.
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type == null
                        || !binding.type.Name.StartsWith("VRC", StringComparison.Ordinal)
                        || !binding.type.Name.EndsWith("Constraint", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    // Never write into the author's own asset. This runs
                    // before AnimationSelfContainer copies anything, so
                    // clips here can still be source files on disk.
                    // Breaking the VRChat original is the one failure
                    // this tool must never have.
                    // Checked here, not per clip, so the guard speaks
                    // only when it actually refused work.
                    if (!mayRewrite)
                    {
                        protectedSources.Add(sourcePath);
                        continue;
                    }
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    string property = MapConstraintProperty(binding.propertyName);

                    // Follow the constraint if it moved. A Target
                    // Transform constraint is rebuilt on the object it
                    // drives, so the clip's path names somewhere the
                    // constraint no longer is.
                    string path = binding.path;
                    var replacement = ConstraintTypeOnPath(ctx, path, binding.type.Name);
                    if (replacement == null && relocated.TryGetValue(path, out string movedTo))
                    {
                        var afterMove = ConstraintTypeOnPath(ctx, movedTo, binding.type.Name);
                        if (afterMove != null)
                        {
                            path = movedTo;
                            replacement = afterMove;
                            followed++;
                        }
                    }
                    string where = $"\"{clip.name}\" -> {binding.path} ({binding.propertyName})";

                    // Either way the old binding goes: it names a component that no longer exists.
                    AnimationUtility.SetEditorCurve(clip, binding, null);
                    if (property == null)
                    {
                        dropped.Add(where);
                        continue;
                    }
                    if (replacement == null)
                    {
                        // Two different situations. Object missing:
                        // this conversion removed it. Object present but
                        // constraint-less: nothing ever put one there,
                        // usually a partial bake.
                        if (ObjectExists(ctx, binding.path))
                        {
                            neverBuilt.Add(where);
                        }
                        else if (ElsewhereByName(ctx, binding.path, out string actualPath))
                        {
                            // Same object, different parent. VRCFury
                            // moves generated objects at upload time,
                            // and a test-copy bake may not. Not this
                            // conversion's bug; say so.
                            movedByBake.Add($"{where} — found at `{actualPath}`");
                        }
                        else
                        {
                            lost.Add(where);
                        }
                        continue;
                    }
                    AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding
                    {
                        path = path,
                        type = replacement,
                        propertyName = property
                    }, curve);
                    repointed++;
                }
            }

            if (protectedSources.Count > 0)
            {
                ctx.Report.Warning(Category,
                    $"{protectedSources.Count} clip(s) left untouched to protect your original files",
                    $"{string.Join(", ", System.Linq.Enumerable.Take(protectedSources, 6))}" +
                    $"{(protectedSources.Count > 6 ? ", …" : "")} — these animate a VRChat constraint " +
                    "and live OUTSIDE this conversion's output folder, so they are the avatar's own " +
                    "assets rather than copies. Rewriting them would have repaired the conversion by " +
                    "damaging the VRChat original, which is never the right trade. The cost is that " +
                    "whatever those curves switched — limb lock, sit, lay-down, flight toggles are the " +
                    "usual ones — will not work here. Avatars built with VRCFury or Modular Avatar are " +
                    "unaffected, because their bake already hands us copies; if you see this, the fix " +
                    "is to let the converter work from a baked copy of the avatar.");
            }
            if (repointed > 0)
            {
                ctx.Report.Converted(Category,
                    $"{repointed} animation curve(s) repointed at the Unity constraints",
                    "Clips that switch a constraint on and off carry the component TYPE and its " +
                    "serialized property name, and both change during conversion — a curve left " +
                    "saying \"VRCParentConstraint.IsActive\" plays as silence, with nothing to see " +
                    "in the animator. This is how limb-lock, sit, lay-down and flight toggles work " +
                    "on constraint-driven avatars, so they now work again." +
                    (followed > 0
                        ? $" {followed} of them also had to follow their constraint to a new object: " +
                          "a VRC constraint using 'Target Transform' is rebuilt ON the thing it drives, " +
                          "because Unity's constraints only affect the object they sit on, and the clips " +
                          "still named the old one."
                        : ""));
            }
            if (dropped.Count > 0)
            {
                ctx.Report.Approximated(Category,
                    $"{dropped.Count} curve(s) drove a constraint property Unity doesn't have",
                    "Dropped rather than left pointing at nothing — almost always 'Freeze To " +
                    "World', which has no Unity or ChilloutVR equivalent. A toggle relying on it " +
                    "will change the constraint but not pin anything in place: " +
                    string.Join("; ", dropped) + ".");
            }
            if (lost.Count > 0)
            {
                ctx.Report.Warning(Category,
                    $"{lost.Count} curve(s) drove a constraint on an object that is now GONE",
                    "The clip animates a constraint on an object this conversion no longer has. A " +
                    "stripped system (GoGo, SPS) taking an object a clip still references is the " +
                    "usual innocent cause — turn that strip off and convert again to check. " +
                    "Anything else is worth reporting as a bug: " + string.Join("; ", lost) + ".");
            }
            if (movedByBake.Count > 0)
            {
                ctx.Report.Warning(Category,
                    $"{movedByBake.Count} curve(s) name a path the avatar's own build step didn't produce",
                    "The object the clip is looking for EXISTS on this avatar — under a different " +
                    "parent. VRCFury and Modular Avatar move their generated objects into place " +
                    "during the upload build, and the clips are authored against where they end up; " +
                    "a bake that stops short leaves them where they started, so every path misses " +
                    "by a parent. This is not something the conversion did, and repointing them here " +
                    "would not help — an object left at the wrong path is usually missing the " +
                    "constraint the curve wanted as well. Get the SOURCE avatar building cleanly " +
                    "and convert again. Found: " + string.Join("; ", movedByBake) + ".");
            }
            if (neverBuilt.Count > 0)
            {
                ctx.Report.Warning(Category,
                    $"{neverBuilt.Count} curve(s) drove a constraint that was never built",
                    "The object is right there on the avatar, but it has no constraint and never " +
                    "did — so there was nothing for this conversion to convert, and nothing it " +
                    "could have moved. That normally means the avatar's own build step didn't " +
                    "finish: VRCFury and Modular Avatar generate constraints during the bake, and " +
                    "a bake that errors partway generates some sets and not others. Build a test " +
                    "copy of the SOURCE avatar on its own and check it completes without errors — " +
                    "on the avatar this was found on, the ear, tongue, wrist and toe constraints " +
                    "were generated and the finger set never was. These curves were dead before " +
                    "conversion started: " + string.Join("; ", neverBuilt) + ".");
            }
        }

        static bool ElsewhereByName(BridgeContext ctx, string path, out string actualPath)
        {
            actualPath = null;
            if (ctx.Target == null || string.IsNullOrEmpty(path))
            {
                return false;
            }
            int slash = path.LastIndexOf('/');
            string leaf = slash >= 0 ? path.Substring(slash + 1) : path;
            Transform found = null;
            foreach (var t in ctx.Target.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != leaf)
                {
                    continue;
                }
                if (found != null)
                {
                    return false;   // ambiguous — a shared leaf name proves nothing
                }
                found = t;
            }
            if (found == null)
            {
                return false;
            }
            actualPath = ctx.PathInTarget(found);
            return true;
        }

        static bool ObjectExists(BridgeContext ctx, string path)
        {
            if (ctx.Target == null)
            {
                return false;
            }
            return string.IsNullOrEmpty(path) || ctx.Target.transform.Find(path) != null;
        }

        static string MapConstraintProperty(string vrcProperty)
        {
            switch (vrcProperty)
            {
                case "IsActive": return "m_Active";
                case "GlobalWeight": return "m_Weight";
                case "Locked": return "m_IsLocked";
                case "Enabled":
                case "m_Enabled": return "m_Enabled";
            }
            // Per-source weights. The spelling in the wild is
            // "Sources.source0.Weight": VRC's source list is a class
            // with sixteen fixed fields, which is what makes the
            // weights animatable. The array form never occurs.
            const string fieldPrefix = "Sources.source";
            const string suffix = ".Weight";
            if (vrcProperty.StartsWith(fieldPrefix, StringComparison.Ordinal) &&
                vrcProperty.EndsWith(suffix, StringComparison.Ordinal))
            {
                string index = vrcProperty.Substring(fieldPrefix.Length,
                    vrcProperty.Length - fieldPrefix.Length - suffix.Length);
                if (int.TryParse(index, out _))
                {
                    return $"m_Sources.Array.data[{index}].weight";
                }
            }
            // The array spelling, kept in case any serialization path ever produces it.
            const string prefix = "Sources.Array.data[";
            const string arraySuffix = "].Weight";
            if (vrcProperty.StartsWith(prefix, StringComparison.Ordinal) &&
                vrcProperty.EndsWith(arraySuffix, StringComparison.Ordinal))
            {
                string index = vrcProperty.Substring(prefix.Length,
                    vrcProperty.Length - prefix.Length - arraySuffix.Length);
                return $"m_Sources.Array.data[{index}].weight";
            }
            return null;
        }

        static Type ConstraintTypeOnPath(BridgeContext ctx, string path, string vrcTypeName)
        {
            var transform = string.IsNullOrEmpty(path)
                ? ctx.Target.transform
                : ctx.Target.transform.Find(path);
            if (transform == null)
            {
                return null;
            }
            string sameKind = vrcTypeName.Substring("VRC".Length);
            Type any = null;
            foreach (var component in transform.GetComponents<Component>())
            {
                if (component == null || !(component is IConstraint))
                {
                    continue;
                }
                var type = component.GetType();
                if (type.Name == sameKind)
                {
                    return type;
                }
                any = any ?? type;
            }
            return any;
        }

        static void NoteMirrored(BridgeContext ctx, Component vrc, List<string> mirrored)
        {
            var target = Get<Transform>(vrc, "TargetTransform", null);
            var constrained = target != null ? target : vrc.transform;
            var parent = constrained.parent;
            if (parent == null)
            {
                return;
            }
            var s = parent.lossyScale;
            bool mixedSign = Mathf.Min(s.x, Mathf.Min(s.y, s.z)) < 0f
                             && Mathf.Max(s.x, Mathf.Max(s.y, s.z)) >= 0f;
            if (mixedSign)
            {
                mirrored.Add($"`{ctx.PathInTarget(constrained)}` (parent \"{parent.name}\" scale " +
                             $"{s.x:0.##}, {s.y:0.##}, {s.z:0.##})");
            }
        }

        static void ReportMirrored(BridgeContext ctx, List<string> mirrored)
        {
            if (mirrored.Count == 0)
            {
                return;
            }
            ctx.Report.Warning(Category,
                $"{mirrored.Count} constraint(s) sit under a mirrored parent and will be reflected",
                "Un-mirror those bones in your 3D package and re-rig, or drive them some other " +
                "way — there is no setting here that changes it.\n\n" +
                "VRChat's solver corrects a constraint result when the parent's scale has one axis " +
                "negative, flipping the quaternion components that axis inverts. Unity's " +
                "constraints have no such step and ChilloutVR ships no type that does, so these " +
                "land mirrored along that axis.\n\n" +
                "A negative scale on a bone usually means a limb was duplicated and mirrored " +
                "rather than rigged twice. On a quadruped built from a hidden humanoid rig, the " +
                "hind legs are typically the front legs mirrored — which is also why their relays " +
                "cross left to right.\n\n" +
                string.Join("\n", mirrored));
        }

        static HashSet<Transform> AlignLocalSpaceRelays(BridgeContext ctx)
        {
            var moved = new HashSet<Transform>();
            var skinned = SkinningBones(ctx.Target);

            var animatedPaths = new HashSet<string>();
            foreach (var animator in ctx.Target.GetComponentsInChildren<Animator>(true))
            {
                var rac = animator.runtimeAnimatorController;
                if (rac == null)
                {
                    continue;
                }
                foreach (var clip in rac.animationClips)
                {
                    if (clip == null)
                    {
                        continue;
                    }
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        animatedPaths.Add(binding.path);
                    }
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    {
                        animatedPaths.Add(binding.path);
                    }
                }
            }

            foreach (var component in ctx.Target.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.GetType().Name != "VRCRotationConstraint"
                    || !Get(component, "SolveInLocalSpace", false))
                {
                    continue;
                }
                var targetField = Get<Transform>(component, "TargetTransform", null);
                var constrained = targetField != null ? targetField : component.transform;
                var source = FirstWeightedSource(component);
                if (source == null || source.parent == null || constrained.parent == null
                    || source.parent == constrained.parent
                    || !constrained.IsChildOf(ctx.Target.transform)
                    || !source.IsChildOf(ctx.Target.transform))
                {
                    continue;
                }

                string why = BlocksMove(ctx, constrained, skinned, animatedPaths)
                             ?? BlocksMirroredMove(constrained, source.parent);
                if (why != null)
                {
                    ctx.Report.Approximated(Category, ctx.PathInTarget(constrained),
                        $"Solved in local space against \"{source.name}\", which Unity cannot do, and " +
                        $"the bone could not be moved under that source's parent to make it " +
                        $"equivalent: {why}. The relay is converted as a world-space constraint and " +
                        "will not follow correctly.");
                    continue;
                }

                string before = ctx.PathInTarget(constrained);
                constrained.SetParent(source.parent, worldPositionStays: true);
                moved.Add(constrained);
                ctx.Report.Converted(Category, ctx.PathInTarget(constrained),
                    $"Local-space rotation relay from \"{source.name}\" — moved under that bone's " +
                    $"parent (\"{source.parent.name}\") so a world-space constraint reproduces it " +
                    "exactly. Was " + before + ". Rotation is all this bone supplies: nothing skins " +
                    "to it and no animation addresses it, so where it sits does not matter.");
            }

            if (moved.Count > 0)
            {
                RepointMaskPaths(ctx);
            }
            return moved;
        }

        static HashSet<Transform> SkinningBones(GameObject root)
        {
            var used = new HashSet<Transform>();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                var bones = smr.bones;
                if (mesh == null || bones == null || bones.Length == 0)
                {
                    continue;
                }
                var indices = new HashSet<int>();
                try
                {
                    var weights = mesh.GetAllBoneWeights();
                    for (int i = 0; i < weights.Length; i++)
                    {
                        if (weights[i].weight > 0f)
                        {
                            indices.Add(weights[i].boneIndex);
                        }
                    }
                }
                catch (Exception)
                {
                    // Unreadable for some reason: assume every listed bone deforms, which only
                    // ever refuses a move that might have been safe.
                    foreach (var bone in bones)
                    {
                        if (bone != null)
                        {
                            used.Add(bone);
                        }
                    }
                    continue;
                }
                foreach (int index in indices)
                {
                    if (index >= 0 && index < bones.Length && bones[index] != null)
                    {
                        used.Add(bones[index]);
                    }
                }
            }
            return used;
        }

        static string BlocksMirroredMove(Transform constrained, Transform newParent)
        {
            bool Mirrored(Vector3 s) => s.x < 0f || s.y < 0f || s.z < 0f;

            foreach (var t in constrained.GetComponentsInChildren<Transform>(true))
            {
                if (Mirrored(t.localScale))
                {
                    return $"\"{t.name}\" is mirrored (negative scale), and handedness cannot " +
                           "survive re-parenting";
                }
            }
            if (constrained.parent != null
                && Mirrored(constrained.parent.lossyScale) != Mirrored(newParent.lossyScale))
            {
                return $"\"{constrained.parent.name}\" and \"{newParent.name}\" have opposite " +
                       "handedness, so the move would reflect the bone";
            }
            return null;
        }

        static string BlocksMove(BridgeContext ctx, Transform root, HashSet<Transform> skinned,
                                 HashSet<string> animatedPaths)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (skinned.Contains(t))
                {
                    return $"\"{t.name}\" is a skinning bone";
                }
                if (animatedPaths.Contains(ctx.PathInTarget(t)))
                {
                    return $"an animation animates \"{t.name}\", and curves are addressed by path";
                }
            }
            return null;
        }

        static Transform FirstWeightedSource(object vrc)
        {
            foreach (var s in ReadSources(vrc))
            {
                if (s.Transform != null && !Mathf.Approximately(s.Weight, 0f))
                {
                    return s.Transform;
                }
            }
            return null;
        }

        static void RepointMaskPaths(BridgeContext ctx)
        {
            if (ctx.MergedController == null)
            {
                return;
            }
            var live = new HashSet<string>();
            foreach (var t in ctx.Target.GetComponentsInChildren<Transform>(true))
            {
                live.Add(ctx.PathInTarget(t));
            }
            string controllerPath = AssetDatabase.GetAssetPath(ctx.MergedController);
            foreach (var layer in ctx.MergedController.layers)
            {
                var mask = layer.avatarMask;
                if (mask == null)
                {
                    continue;
                }
                // Only a mask this conversion owns. This runs before the
                // self-container copies foreign masks, so the CCK's, the
                // package's and the source avatar's are still theirs.
                string maskPath = AssetDatabase.GetAssetPath(mask);
                bool ours = string.IsNullOrEmpty(maskPath) || maskPath == controllerPath
                            || maskPath.StartsWith(ctx.OutputDir + "/", StringComparison.Ordinal);
                if (!ours)
                {
                    continue;
                }
                bool dirty = false;
                for (int i = mask.transformCount - 1; i >= 0; i--)
                {
                    string path = mask.GetTransformPath(i);
                    if (string.IsNullOrEmpty(path) || live.Contains(path))
                    {
                        continue;
                    }
                    // The leaf still exists somewhere; find it and rewrite, else drop the entry.
                    string leaf = path.Substring(path.LastIndexOf('/') + 1);
                    string replacement = null;
                    foreach (string candidate in live)
                    {
                        if (candidate.EndsWith("/" + leaf, StringComparison.Ordinal) || candidate == leaf)
                        {
                            if (replacement != null)
                            {
                                replacement = null; // ambiguous — leave it rather than guess
                                break;
                            }
                            replacement = candidate;
                        }
                    }
                    if (replacement != null)
                    {
                        mask.SetTransformPath(i, replacement);
                        dirty = true;
                    }
                }
                if (dirty)
                {
                    EditorUtility.SetDirty(mask);
                }
            }
        }

        static void NoteLocalSpace(BridgeContext ctx, Component vrc, List<string> crossChain)
        {
            if (!Get(vrc, "SolveInLocalSpace", false))
            {
                return;
            }
            // The Unity constraint may sit on TargetTransform instead.
            // Not "?? vrc.transform": an unassigned Transform is
            // Unity's fake null, and ?? passes it through.
            var target = Get<Transform>(vrc, "TargetTransform", null);
            var constrained = target != null ? target : vrc.transform;
            bool isScale = vrc.GetType().Name == "VRCScaleConstraint";
            foreach (var s in ReadSources(vrc))
            {
                if (s.Transform == null || Mathf.Approximately(s.Weight, 0f)
                    || s.Transform.parent == constrained.parent)
                {
                    continue;
                }
                // A scale constraint asks a different question. VRChat
                // copies localScale, Unity lossyScale; identical while
                // every ancestor of the source is unit-scaled, the
                // normal case. When the spaces provably coincide,
                // there is nothing to warn about.
                if (isScale && AncestorsAreUnitScaled(ctx, s.Transform))
                {
                    continue;
                }
                crossChain.Add($"`{ctx.PathInTarget(constrained)}` from `{ctx.PathInTarget(s.Transform)}`");
                return; // one line per constraint is enough to find it
            }
        }

        static bool AncestorsAreUnitScaled(BridgeContext ctx, Transform t)
        {
            var root = ctx.Target != null ? ctx.Target.transform : null;
            for (var p = t.parent; p != null && p != root; p = p.parent)
            {
                var s = p.localScale;
                if (!Mathf.Approximately(s.x, 1f) || !Mathf.Approximately(s.y, 1f)
                    || !Mathf.Approximately(s.z, 1f))
                {
                    return false;
                }
            }
            return true;
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
                "VRChat solved them against the source's **local** value. Unity's constraints only " +
                "ever solve in world space, and ChilloutVR ships no local-space equivalent, so a " +
                "rotation relay now inherits its source's world orientation instead of its pose, " +
                "and a position or scale relay reads a world value where a local one was meant.\n\n" +
                "Two cases are provably identical and are NOT listed: a constraint whose source " +
                "shares its own parent (the parent rotation cancels on both sides), and a SCALE " +
                "constraint every one of whose source's ancestors is unit-scaled (lossyScale then " +
                "equals localScale). A limb-scaling rig is normally the second case and converts " +
                "exactly.\n\n" +
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
            // The component's own checkbox travels too: a constraint left
            // disabled at rest and switched on by a toggle must start off,
            // or the toggle can only ever release it.
            if (vrc is Behaviour source)
            {
                unity.enabled = source.enabled;
            }
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
            var parentDriven = DrivenBy(ctx, vrc);
            unity.translationAtRest = parentDriven != vrc.transform
                ? parentDriven.localPosition
                : Get(vrc, "PositionAtRest", vrc.transform.localPosition);
            unity.rotationAtRest = parentDriven != vrc.transform
                ? parentDriven.localEulerAngles
                : Get(vrc, "RotationAtRest", vrc.transform.localEulerAngles);
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
            var posDriven = DrivenBy(ctx, vrc);
            unity.translationOffset = Get(vrc, "PositionOffset", Vector3.zero);
            unity.translationAtRest = posDriven != vrc.transform
                ? posDriven.localPosition
                : Get(vrc, "PositionAtRest", vrc.transform.localPosition);
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
            // Measured from the live pose whenever the constraint's
            // output is unambiguous; the VRChat field is the fallback.
            // Copying the field trusts both engines to apply the offset
            // in the same space, which fails across differently
            // oriented bones. The scene pose at conversion time is
            // VRChat's own solver output, so:
            // result = source.rotation * Euler(offset), hence
            // offset = Inverse(source.rotation) * current.rotation. World rotations throughout,
            // so AlignLocalSpaceRelays re-parenting cannot disturb the measurement.
            var driven = DrivenBy(ctx, vrc);
            var rotSources = ReadSources(vrc);
            bool rotMeasured = Get(vrc, "IsActive", true)
                && Mathf.Approximately(Get(vrc, "GlobalWeight", 1f), 1f)
                && rotSources.Count == 1 && rotSources[0].Weight >= 0.999f
                && rotSources[0].Transform != null
                && vrc.gameObject.activeInHierarchy;
            unity.rotationOffset = rotMeasured
                ? (Quaternion.Inverse(rotSources[0].Transform.rotation) * driven.rotation).eulerAngles
                : Get(vrc, "RotationOffset", Vector3.zero);
            // A bone AlignLocalSpaceRelays moved has a new parent, so
            // the authored RotationAtRest no longer describes it.
            // Its rest is where it sits now; the move preserved world
            // rotation. A relocated constraint reads the target's rest
            // for the same reason: the authored field describes whatever
            // the VRC component was driving, which is the target.
            unity.rotationAtRest = reparented.Contains(driven) || driven != vrc.transform
                ? driven.localEulerAngles
                : Get(vrc, "RotationAtRest", vrc.transform.localEulerAngles);
            unity.rotationAxis = AxesFrom(vrc, "AffectsRotationX", "AffectsRotationY", "AffectsRotationZ");
            WarnIfUnsupported(ctx, vrc, vrc);
            ApplyCommon(vrc, unity);
            ctx.Report.Converted(Category, ctx.PathInTarget(vrc.transform),
                rotMeasured
                    ? "Rotation constraint — offset measured from the pose VRChat's own solver left in the scene"
                    : "Rotation constraint");
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
            var scaleDriven = DrivenBy(ctx, vrc);
            unity.scaleAtRest = scaleDriven != vrc.transform
                ? scaleDriven.localScale
                : Get(vrc, "ScaleAtRest", vrc.transform.localScale);
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
            var aimDriven = DrivenBy(ctx, vrc);
            unity.aimVector = Get(vrc, "AimAxis", Vector3.forward);
            unity.upVector = Get(vrc, "UpAxis", Vector3.up);
            unity.rotationAtRest = aimDriven != vrc.transform
                ? aimDriven.localEulerAngles
                : Get(vrc, "RotationAtRest", vrc.transform.localEulerAngles);
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
            var lookDriven = DrivenBy(ctx, vrc);
            unity.rotationAtRest = lookDriven != vrc.transform
                ? lookDriven.localEulerAngles
                : Get(vrc, "RotationAtRest", vrc.transform.localEulerAngles);
            unity.rotationOffset = Get(vrc, "RotationOffset", Vector3.zero);
            WarnIfUnsupported(ctx, vrc, vrc);
            ApplyCommon(vrc, unity);
            ctx.Report.Converted(Category, ctx.PathInTarget(vrc.transform), "LookAt constraint");
            return true;
        }

        // ---------------------------------------------------------------- helpers ----

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
            if (ctx.Target != null)
            {
                // Recorded before the component is destroyed, while both paths still resolve.
                relocated[ctx.PathInTarget(vrc.transform)] = ctx.PathInTarget(target);
            }
            return target.gameObject;
        }

        // The transform the converted constraint actually drives. With
        // VRC's 'Target Transform' that is the target, not the object the
        // VRC component sat on, and Unity's constraint only ever affects
        // the object it sits on. Every rest value and every measured
        // offset has to describe THAT transform: measuring the component's
        // own instead pins the driven bone to a pose belonging to another
        // object entirely, which is how a hand-swap rig's finger
        // constraints snapped the fingers into a pose nobody authored.
        static Transform DrivenBy(BridgeContext ctx, Component vrc)
        {
            var host = HostFor(ctx, vrc);
            return host != null ? host.transform : vrc.transform;
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
