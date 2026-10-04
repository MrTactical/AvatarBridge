#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using ABI.CCK.Components;
using ABI.CCK.Scripts;

namespace AvatarBridge
{
    // CVR-VRCFT (parameter-based) face tracking, using DragonSkyRunner's "CVR Eye & Face
    // Tracking" animator (bundled under Assets/AvatarBridge/FaceTracking). Copies the rig's
    // layers and parameters into the generated CVR animator, then makes it work on THIS
    // avatar without any mesh edits:
    //
    //   * The eye-gaze layer rotates two empties ("EyeTracking.L/.R"); those are generated
    //     empties at the avatar's eye bones and drive each eye bone from its empty via a
    //     RotationConstraint. The bundled ON/OFF clips toggle that constraint's source
    //     weight against CVR's native eye-look/blink.
    //   * The rig's clips are authored against a fixed hierarchy ("Armature/Hips/Spine/
    //     Chest/Neck/Head/...") and a face mesh named "Body". Unity animation paths are
    //     case-sensitive, so every referenced clip is cloned on write and its curves repathed
    //     onto the real avatar's eye bones, generated empties, and face mesh.
    //
    // Any existing FT rig is stripped first (see AnimatorMerger). Eye gaze magnitude still
    // wants per-avatar tuning per the package readme; this is a working starting point.
    public static class FaceTrackingInjector
    {
        const string Category = "Face tracking";

        // Float parameters the readme says must not be left at 0.
        static readonly Dictionary<string, float> RequiredDefaults = new Dictionary<string, float>
        {
            { "#Direct", 1f },
            { "LeftEyeLidExpandedSqueeze", 0.8f },
            { "RightEyeLidExpandedSqueeze", 0.8f },
            { "EyesDilation", 0.5f }
        };

        // The hierarchy the bundled clips are authored against.
        const string PkgHead = "Armature/Hips/Spine/Chest/Neck/Head";
        const string PkgEyeL = PkgHead + "/Eye.L";
        const string PkgEyeR = PkgHead + "/Eye.R";
        const string PkgEmptyL = PkgHead + "/EyeTracking.L";
        const string PkgEmptyR = PkgHead + "/EyeTracking.R";
        const string PkgFaceMesh = "Body";

        public static void Inject(AnimatorController master, BridgeContext ctx)
        {
            if (ctx.Settings.faceTrackingMode != FaceTrackingMode.DragonSkyRunner)
            {
                return;
            }
            var source = FaceTrackingPackages.LoadController();
            if (source == null)
            {
                ctx.Report.Error(Category, "\"Unity Animator Blendtrees (DSR)\" face tracking selected, but its animator wasn't found",
                    $"The bundled \"{FaceTrackingPackages.DisplayName}\" assets are missing from the project.");
                return;
            }

            // ---- copy layers (deep-copied so the bundled asset is never mutated) --------
            var copier = new AnimatorDeepCopier();
            var layers = master.layers.ToList();
            var existingNames = new HashSet<string>(layers.Select(l => l.name));
            var injectedLayers = new List<AnimatorControllerLayer>();
            foreach (var srcLayer in source.layers)
            {
                var clone = copier.CloneLayer(srcLayer);
                string name = "[FT] " + srcLayer.name;
                int suffix = 2;
                while (!existingNames.Add(name))
                {
                    name = $"[FT] {srcLayer.name} {suffix++}";
                }
                clone.name = name;
                clone.defaultWeight = srcLayer.defaultWeight <= 0f ? 1f : srcLayer.defaultWeight;
                layers.Add(clone);
                injectedLayers.Add(clone);
            }

            // ---- copy parameters --------------------------------------------------------
            var parameters = master.parameters.ToList();
            var have = new HashSet<string>(parameters.Select(p => p.name));
            int addedParams = 0;
            foreach (var p in source.parameters)
            {
                if (have.Add(p.name))
                {
                    parameters.Add(AnimatorDeepCopier.CloneParameter(p));
                    addedParams++;
                }
            }
            foreach (var param in parameters)
            {
                if (RequiredDefaults.TryGetValue(param.name, out var value))
                {
                    param.defaultFloat = value;
                }
            }
            master.parameters = parameters.ToArray();

            // ---- generate the eye rig and repath the clips onto this avatar -------------
            // Do this while the authoritative layer references are still held, before handing
            // them to master.layers (whose setter can detach the state-machine objects).
            var pathRemap = new Dictionary<string, string>();
            try
            {
                BuildEyeRig(ctx, pathRemap);
            }
            catch (Exception e)
            {
                ctx.Report.Warning(Category, "Eye-tracking rig setup failed",
                    $"Face shapes still injected; eye gaze not wired. {e.Message}");
                Debug.LogException(e);
            }

            // Resolve the face mesh once; used for both the path repath and the blendshape
            // reconciliation below.
            var faceMesh = ResolveFaceMesh(ctx);
            string faceMeshPath = faceMesh != null ? ctx.PathInTarget(faceMesh.transform) : PkgFaceMesh;
            if (faceMesh != null && !string.IsNullOrEmpty(faceMeshPath) && faceMeshPath != PkgFaceMesh)
            {
                pathRemap[PkgFaceMesh] = faceMeshPath;
                ctx.Report.Converted(Category, $"Face-tracking blendshapes repathed to \"{faceMeshPath}\"",
                    "The rig assumes a face mesh named \"Body\"; rebound to this avatar's face mesh instead.");
            }

            // Reconcile the rig's shape vocabulary against what the mesh actually has (combined
            // vs split Unified-Expressions shapes).
            Dictionary<string, ShapeAction> shapePlan = null;
            if (faceMesh != null && faceMesh.sharedMesh != null)
            {
                var meshActual = new Dictionary<string, string>();
                var mesh = faceMesh.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string n = mesh.GetBlendShapeName(i);
                    meshActual[n.ToLowerInvariant()] = n;
                }
                var rigShapes = CollectRigFaceShapes(injectedLayers);
                shapePlan = UnifiedBlendshapes.BuildPlan(rigShapes, meshActual, out var unmapped);
                ReportReconciliation(ctx, faceMesh.name, shapePlan, unmapped);
            }

            // No gaze rig means nothing moves the eyes, so the toggle's ON clip must not switch
            // CVR's own eye look off. Its blink switch stays: the eyelids are still tracked.
            bool keepNativeEyeLook = !pathRemap.ContainsKey(PkgEyeL);

            int rewritten = 0;
            if (pathRemap.Count > 0 || (shapePlan != null && shapePlan.Count > 0) || keepNativeEyeLook)
            {
                var cache = new Dictionary<AnimationClip, AnimationClip>();
                foreach (var layer in injectedLayers)
                {
                    RewriteMachine(layer.stateMachine, pathRemap, shapePlan, faceMeshPath, keepNativeEyeLook, cache);
                }
                rewritten = cache.Count(kv => kv.Key != kv.Value);
            }

            master.layers = layers.ToArray();

            // The rig needs Eye/Face Tracking toggles in Advanced Avatar Settings (per its readme).
            AddTrackingToggles(ctx);

            ctx.Report.Converted(Category,
                $"Injected \"Unity Animator Blendtrees (DSR)\" face tracking: {injectedLayers.Count} layer(s), {addedParams} parameter(s)",
                $"DragonSkyRunner's rig, rebuilt onto this avatar ({rewritten} clip(s) rebound). Added \"Eye Tracking\" " +
                "and \"Face Tracking\" menu toggles. Eye gaze magnitude may want tuning per the package readme; " +
                "verify the eye RotationConstraints in play mode.");
        }

        static void AddTrackingToggles(BridgeContext ctx)
        {
            var settings = ctx.CvrAvatar?.avatarSettings?.settings;
            if (settings == null)
            {
                return;
            }
            AddToggle(settings, "Eye Tracking", "EyeTracking");
            AddToggle(settings, "Face Tracking", "FaceTracking");
        }

        static void AddToggle(List<CVRAdvancedSettingsEntry> settings, string menuName, string paramName)
        {
            if (settings.Any(s => s != null && s.machineName == paramName))
            {
                return; // already exposed
            }
            settings.Add(new CVRAdvancedSettingsEntry
            {
                name = menuName,
                machineName = paramName,
                unlinkNameFromMachineName = true,
                setting = new CVRAdvancesAvatarSettingGameObjectToggle
                {
                    defaultValue = true, // the rig defaults both on
                    usedType = CVRAdvancesAvatarSettingBase.ParameterType.Bool
                }
            });
        }

        static HashSet<string> CollectRigFaceShapes(List<AnimatorControllerLayer> layers)
        {
            var shapes = new HashSet<string>();
            var seen = new HashSet<AnimationClip>();
            void Walk(Motion m)
            {
                if (m is BlendTree bt)
                {
                    foreach (var child in bt.children) Walk(child.motion);
                }
                else if (m is AnimationClip clip && clip != null && seen.Add(clip))
                {
                    foreach (var b in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (b.path == PkgFaceMesh && b.propertyName.StartsWith("blendShape."))
                        {
                            shapes.Add(b.propertyName.Substring("blendShape.".Length));
                        }
                    }
                }
            }
            foreach (var layer in layers)
            {
                WalkMachine(layer.stateMachine, Walk);
            }
            return shapes;
        }

        static void WalkMachine(AnimatorStateMachine machine, Action<Motion> onMotion)
        {
            if (machine == null) return;
            foreach (var s in machine.states) onMotion(s.state.motion);
            foreach (var child in machine.stateMachines) WalkMachine(child.stateMachine, onMotion);
        }

        static void ReportReconciliation(BridgeContext ctx, string meshName,
            Dictionary<string, ShapeAction> plan, List<string> unmapped)
        {
            int redirected = plan.Count(kv => kv.Value.Op == ShapeOp.Redirect && kv.Value.Scale >= 1f);
            int collapsed = plan.Count(kv => kv.Value.Op == ShapeOp.Redirect && kv.Value.Scale < 1f);
            int expanded = plan.Count(kv => kv.Value.Op == ShapeOp.Expand);
            if (redirected + collapsed + expanded > 0)
            {
                ctx.Report.Converted(Category,
                    $"Reconciled {redirected + collapsed + expanded} FT blendshape(s) to \"{meshName}\"",
                    $"Mapped the rig's Unified-Expressions shapes onto what the mesh actually has: " +
                    $"{redirected} name remap(s) (ARKit / casing), {collapsed} split→combined (scaled to avoid " +
                    $"over-driving), {expanded} combined→split. Combined/split is an approximation; asymmetric " +
                    "expressions on combined-only meshes land at partial strength.");
            }
            if (unmapped != null && unmapped.Count > 0)
            {
                string list = string.Join(", ", unmapped.Take(12));
                if (unmapped.Count > 12) list += $", +{unmapped.Count - 12} more";
                ctx.Report.Approximated(Category,
                    $"{unmapped.Count} FT shape(s) have no equivalent on \"{meshName}\"",
                    $"These won't move: the mesh has neither the shape nor a combined/split match: {list}.");
            }
        }

        // ---------------------------------------------------------------- eye rig -------

        static void BuildEyeRig(BridgeContext ctx, Dictionary<string, string> remap)
        {
            FindEyeBones(ctx, out var head, out var leftEye, out var rightEye);
            if (leftEye == null || rightEye == null || head == null)
            {
                ctx.Report.Warning(Category, "Eye bones not found: eye gaze left unwired",
                    "The avatar has no mapped Left/Right Eye humanoid bones (and none named Eye.L/Eye.R). " +
                    "Face shapes still work; CVR's native eye look stays on.");
                return;
            }

            var driver = RotationDriver(leftEye) ?? RotationDriver(rightEye);
            if (driver != null)
            {
                ctx.Report.Warning(Category, "Eyes already constrained: eye gaze left unwired",
                    $"\"{driver.name}\" is turned by its own {driver.GetType().Name}, which a gaze constraint " +
                    "would fight, and \"Eye Tracking\" could no longer release it. Face shapes still work; " +
                    "CVR's native eye look stays on.");
                return;
            }

            var frame = MakeGazeFrame(ctx, head);
            var leftEmpty = MakeEyeTarget("EyeTracking.L", frame, leftEye);
            var rightEmpty = MakeEyeTarget("EyeTracking.R", frame, rightEye);
            ConstrainEye(leftEye, leftEmpty);
            ConstrainEye(rightEye, rightEmpty);

            remap[PkgEyeL] = ctx.PathInTarget(leftEye);
            remap[PkgEyeR] = ctx.PathInTarget(rightEye);
            remap[PkgEmptyL] = ctx.PathInTarget(leftEmpty);
            remap[PkgEmptyR] = ctx.PathInTarget(rightEmpty);

            ctx.Report.Converted(Category, "Eye-tracking rig generated",
                $"Empties \"EyeTracking.L/.R\" under \"{head.name}\", each driving its eye bone via a " +
                "RotationConstraint. The ON/OFF clips toggle the constraint against CVR's native eye look.");
        }

        static void FindEyeBones(BridgeContext ctx, out Transform head, out Transform leftEye, out Transform rightEye)
        {
            head = leftEye = rightEye = null;
            var animator = ctx.TargetAnimator;
            if (animator != null && animator.isHuman)
            {
                head = animator.GetBoneTransform(HumanBodyBones.Head);
                leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
                rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            }
            leftEye = leftEye ?? FindByName(ctx.Target.transform, "Eye.L", "LeftEye", "Eye_L", "eye.L");
            rightEye = rightEye ?? FindByName(ctx.Target.transform, "Eye.R", "RightEye", "Eye_R", "eye.R");
            if (head == null && leftEye != null)
            {
                head = leftEye.parent;
            }
        }

        static Transform FindByName(Transform root, params string[] names)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                foreach (var n in names)
                {
                    if (string.Equals(t.name, n, StringComparison.OrdinalIgnoreCase))
                    {
                        return t;
                    }
                }
            }
            return null;
        }

        // The gaze clips key the empties' local Euler angles in Unity's own frame ("Down" is +X,
        // "Left" is -Y, neutral is zero), so the empties' parent must face the way the avatar
        // does. The head bone's own axes only do on a rig that happens to be axis-aligned; on a
        // rolled one the eyes turned the wrong way. Under the head, so gaze still follows it.
        static Transform MakeGazeFrame(BridgeContext ctx, Transform head)
        {
            var frame = MakeEyeTarget("EyeTracking.Frame", head, head);
            frame.rotation = ctx.Target.transform.rotation;
            // Earlier builds hung the empties straight off the head; a re-run in place adopts
            // them rather than leaving dead twins beside the new ones.
            foreach (var name in new[] { "EyeTracking.L", "EyeTracking.R" })
            {
                var old = head.Find(name);
                if (old != null && frame.Find(name) == null)
                {
                    Undo.SetTransformParent(old, frame, "AvatarBridge FT eye rig");
                }
            }
            return frame;
        }

        static Transform MakeEyeTarget(string name, Transform parent, Transform at)
        {
            var existing = parent.Find(name);
            var go = existing != null ? existing.gameObject : new GameObject(name);
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(go, "AvatarBridge FT eye rig");
                go.transform.SetParent(parent, false);
            }
            go.transform.position = at.position;
            go.transform.localRotation = Quaternion.identity;   // what the neutral clip keys
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        // The avatar's own rotation driver on an eye, Unity or VRChat. Not wiped: the Constraints
        // pass later merges a VRC constraint's sources into the same RotationConstraint at equal
        // weight. A constraint left by an earlier run, driven only by the gaze empties, is ours.
        static Component RotationDriver(Transform eye)
        {
            foreach (var c in eye.GetComponents<Component>())
            {
                if (c == null || c is PositionConstraint || c is ScaleConstraint)
                {
                    continue;
                }
                if (c is RotationConstraint rc && DrivenByGazeEmpties(rc))
                {
                    continue;
                }
                string name = c.GetType().Name;
                if (c is IConstraint
                    || (name.StartsWith("VRC", StringComparison.Ordinal) && name.EndsWith("Constraint", StringComparison.Ordinal)
                        && name != "VRCPositionConstraint" && name != "VRCScaleConstraint"))
                {
                    return c;
                }
            }
            return null;
        }

        static bool DrivenByGazeEmpties(RotationConstraint rc)
        {
            for (int i = 0; i < rc.sourceCount; i++)
            {
                var source = rc.GetSource(i).sourceTransform;
                if (source != null && source.name != "EyeTracking.L" && source.name != "EyeTracking.R")
                {
                    return false;
                }
            }
            return true;
        }

        static void ConstrainEye(Transform eye, Transform target)
        {
            var rc = eye.GetComponent<RotationConstraint>();
            if (rc == null)
            {
                rc = eye.gameObject.AddComponent<RotationConstraint>();
            }
            for (int i = rc.sourceCount - 1; i >= 0; i--)
            {
                rc.RemoveSource(i);
            }
            rc.AddSource(new ConstraintSource { sourceTransform = target, weight = 1f });
            rc.rotationAxis = Axis.X | Axis.Y | Axis.Z;
            rc.weight = 1f;
            // The offset that keeps the eye at rest while the source sits at its neutral pose
            // (eye.world = source.world * offset == eye rest world).
            var relToSource = Quaternion.Inverse(target.rotation) * eye.rotation;
            rc.rotationOffset = relToSource.eulerAngles;
            rc.rotationAtRest = eye.localEulerAngles;
            rc.locked = true;
            rc.constraintActive = true;
            EditorUtility.SetDirty(rc);
        }

        // The native path's resolver, so both modes agree on the face. The descriptor's mesh is the
        // VISEME mesh, and wins only when it carries the tracking shapes itself: taken blindly,
        // an avatar with visemes on one mesh and tracking shapes on another lost every shape.
        static SkinnedMeshRenderer ResolveFaceMesh(BridgeContext ctx)
        {
            var named = ctx.CvrAvatar != null ? ctx.CvrAvatar.bodyMesh : null;
            // Below the detection bar too: the best scorer is still the likeliest face.
            FaceTrackingConverter.DetectShapes(ctx.Target, named, out var mesh, out _, out _);
            return mesh != null ? mesh : named;
        }

        // ------------------------------------------------------ path + shape rewrite ----

        static void RewriteMachine(AnimatorStateMachine machine, Dictionary<string, string> pathRemap,
            Dictionary<string, ShapeAction> shapePlan, string faceMeshPath, bool keepNativeEyeLook,
            Dictionary<AnimationClip, AnimationClip> cache)
        {
            if (machine == null)
            {
                return;
            }
            var states = machine.states;
            for (int i = 0; i < states.Length; i++)
            {
                states[i].state.motion = RewriteMotion(states[i].state.motion, pathRemap, shapePlan, faceMeshPath, keepNativeEyeLook, cache);
            }
            machine.states = states;
            foreach (var child in machine.stateMachines)
            {
                RewriteMachine(child.stateMachine, pathRemap, shapePlan, faceMeshPath, keepNativeEyeLook, cache);
            }
        }

        static Motion RewriteMotion(Motion motion, Dictionary<string, string> pathRemap,
            Dictionary<string, ShapeAction> shapePlan, string faceMeshPath, bool keepNativeEyeLook,
            Dictionary<AnimationClip, AnimationClip> cache)
        {
            if (motion is BlendTree tree)
            {
                var kids = tree.children;
                for (int i = 0; i < kids.Length; i++)
                {
                    kids[i].motion = RewriteMotion(kids[i].motion, pathRemap, shapePlan, faceMeshPath, keepNativeEyeLook, cache);
                }
                bool auto = tree.useAutomaticThresholds;
                tree.useAutomaticThresholds = false;
                tree.children = kids;
                tree.useAutomaticThresholds = auto;
                return tree;
            }
            if (motion is AnimationClip clip)
            {
                return RewriteClip(clip, pathRemap, shapePlan, faceMeshPath, keepNativeEyeLook, cache);
            }
            return motion;
        }

        const string ShapePrefix = "blendShape.";

        static AnimationClip RewriteClip(AnimationClip clip, Dictionary<string, string> pathRemap,
            Dictionary<string, ShapeAction> shapePlan, string faceMeshPath, bool keepNativeEyeLook,
            Dictionary<AnimationClip, AnimationClip> cache)
        {
            if (clip == null)
            {
                return null;
            }
            if (cache.TryGetValue(clip, out var done))
            {
                return done;
            }

            var floatBindings = AnimationUtility.GetCurveBindings(clip);
            var objBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            bool IsEyeLook(EditorCurveBinding b) => keepNativeEyeLook && b.path == "" && b.propertyName == "useEyeMovement";
            bool needs = floatBindings.Any(b => NeedsRewrite(b, pathRemap, shapePlan) || IsEyeLook(b))
                         || objBindings.Any(b => pathRemap.ContainsKey(b.path));
            if (!needs)
            {
                cache[clip] = clip;
                return clip;
            }

            var clone = UnityEngine.Object.Instantiate(clip);
            clone.name = clip.name;
            clone.hideFlags = HideFlags.None;

            // Rebuild the float curves from scratch so shape collapses can merge onto one
            // binding. Keyed by "path|type|property" -> summed curve.
            var outCurves = new Dictionary<string, KeyValuePair<EditorCurveBinding, AnimationCurve>>();
            void Emit(EditorCurveBinding binding, AnimationCurve curve)
            {
                string key = binding.path + "|" + binding.type.FullName + "|" + binding.propertyName;
                if (outCurves.TryGetValue(key, out var existing))
                {
                    outCurves[key] = new KeyValuePair<EditorCurveBinding, AnimationCurve>(
                        binding, SumCurves(existing.Value, curve));
                }
                else
                {
                    outCurves[key] = new KeyValuePair<EditorCurveBinding, AnimationCurve>(binding, curve);
                }
            }

            foreach (var b in floatBindings)
            {
                if (IsEyeLook(b))
                {
                    continue;   // cleared below with the rest, and never written back
                }
                var curve = AnimationUtility.GetEditorCurve(clone, b);
                string newPath = pathRemap.TryGetValue(b.path, out var p) ? p : b.path;

                bool isFaceShape = b.path == PkgFaceMesh && b.propertyName.StartsWith(ShapePrefix);
                string shape = isFaceShape ? b.propertyName.Substring(ShapePrefix.Length) : null;

                if (isFaceShape && shapePlan != null && shapePlan.TryGetValue(shape, out var action))
                {
                    foreach (var target in action.Targets)
                    {
                        var nb = b;
                        nb.path = faceMeshPath;
                        nb.propertyName = ShapePrefix + target;
                        Emit(nb, action.Scale == 1f ? curve : ScaleCurve(curve, action.Scale));
                    }
                }
                else
                {
                    var nb = b;
                    nb.path = newPath;
                    Emit(nb, curve);
                }
            }

            // Replace all float curves: clear the originals, then write the merged set.
            foreach (var b in floatBindings)
            {
                AnimationUtility.SetEditorCurve(clone, b, null);
            }
            foreach (var kv in outCurves.Values)
            {
                AnimationUtility.SetEditorCurve(clone, kv.Key, kv.Value);
            }

            // Object-reference curves (none in this rig, but keep them path-correct).
            foreach (var b in objBindings)
            {
                if (!pathRemap.TryGetValue(b.path, out var newPath))
                {
                    continue;
                }
                var keys = AnimationUtility.GetObjectReferenceCurve(clone, b);
                AnimationUtility.SetObjectReferenceCurve(clone, b, null);
                var nb = b;
                nb.path = newPath;
                AnimationUtility.SetObjectReferenceCurve(clone, nb, keys);
            }

            cache[clip] = clone;
            return clone;
        }

        static bool NeedsRewrite(EditorCurveBinding b, Dictionary<string, string> pathRemap,
            Dictionary<string, ShapeAction> shapePlan)
        {
            if (pathRemap.ContainsKey(b.path))
            {
                return true;
            }
            if (shapePlan != null && b.path == PkgFaceMesh && b.propertyName.StartsWith(ShapePrefix))
            {
                return shapePlan.ContainsKey(b.propertyName.Substring(ShapePrefix.Length));
            }
            return false;
        }

        static AnimationCurve ScaleCurve(AnimationCurve src, float scale)
        {
            var keys = src.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].value *= scale;
                keys[i].inTangent *= scale;
                keys[i].outTangent *= scale;
            }
            return new AnimationCurve(keys) { preWrapMode = src.preWrapMode, postWrapMode = src.postWrapMode };
        }

        static AnimationCurve SumCurves(AnimationCurve a, AnimationCurve b)
        {
            var times = a.keys.Select(k => k.time).Concat(b.keys.Select(k => k.time))
                         .Distinct().OrderBy(t => t).ToArray();
            var result = new AnimationCurve { preWrapMode = a.preWrapMode, postWrapMode = a.postWrapMode };
            foreach (var t in times)
            {
                result.AddKey(new Keyframe(t, a.Evaluate(t) + b.Evaluate(t)));
            }
            return result;
        }
    }
}
#endif
