#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;

namespace AvatarBridge
{
    /// <summary>
    /// CVR-VRCFT (parameter-based) face tracking, using DragonSkyRunner's "CVR Eye &amp; Face
    /// Tracking" animator (bundled under Assets/AvatarBridge/FaceTracking). Copies the rig's
    /// layers and parameters into the generated CVR animator, then makes it work on THIS
    /// avatar without any mesh edits:
    ///
    ///   * The eye-gaze layer rotates two empties ("EyeTracking.L/.R"); we generate those
    ///     empties at the avatar's eye bones and drive each eye bone from its empty via a
    ///     RotationConstraint. The bundled ON/OFF clips toggle that constraint's source
    ///     weight against CVR's native eye-look/blink.
    ///   * The rig's clips are authored against a fixed hierarchy ("Armature/Hips/Spine/
    ///     Chest/Neck/Head/…") and a face mesh named "Body". Unity animation paths are
    ///     case-sensitive, so we clone-on-write every referenced clip and repath its curves
    ///     onto the real avatar's eye bones, generated empties, and face mesh.
    ///
    /// Any existing FT rig is stripped first (see AnimatorMerger). Eye gaze magnitude still
    /// wants per-avatar tuning per the package readme — this is a working starting point.
    /// </summary>
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
                ctx.Report.Error(Category, "CVR-VRCFT face tracking selected, but its animator wasn't found",
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
            // Do this while we still hold the authoritative layer references, BEFORE handing
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

            // Resolve the face mesh once — used for both the path repath and the blendshape
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

            int rewritten = 0;
            if (pathRemap.Count > 0 || (shapePlan != null && shapePlan.Count > 0))
            {
                var cache = new Dictionary<AnimationClip, AnimationClip>();
                foreach (var layer in injectedLayers)
                {
                    RewriteMachine(layer.stateMachine, pathRemap, shapePlan, faceMeshPath, cache);
                }
                rewritten = cache.Count(kv => kv.Key != kv.Value);
            }

            master.layers = layers.ToArray();

            ctx.Report.Converted(Category,
                $"Injected CVR-VRCFT face tracking — {injectedLayers.Count} layer(s), {addedParams} parameter(s)",
                $"DragonSkyRunner's rig, rebuilt onto this avatar ({rewritten} clip(s) rebound). Eye gaze " +
                "magnitude may want tuning per the package readme; verify the eye RotationConstraints in play mode.");
        }

        /// <summary>All blendshape names the injected clips drive on the package's face mesh.</summary>
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
            int collapsed = plan.Count(kv => kv.Value.Op == ShapeOp.Redirect);
            int expanded = plan.Count(kv => kv.Value.Op == ShapeOp.Expand);
            if (collapsed > 0 || expanded > 0)
            {
                ctx.Report.Converted(Category,
                    $"Reconciled {collapsed + expanded} FT blendshape(s) to \"{meshName}\"",
                    $"Mapped the rig's shapes to what the mesh has via VRCFT blended-shape rules — " +
                    $"{collapsed} split→combined (scaled to avoid over-driving), {expanded} combined→split. " +
                    "An approximation; asymmetric expressions on combined-only meshes land at partial strength.");
            }
            if (unmapped != null && unmapped.Count > 0)
            {
                string list = string.Join(", ", unmapped.Take(12));
                if (unmapped.Count > 12) list += $", +{unmapped.Count - 12} more";
                ctx.Report.Approximated(Category,
                    $"{unmapped.Count} FT shape(s) have no equivalent on \"{meshName}\"",
                    $"These won't move — the mesh has neither the shape nor a combined/split match: {list}.");
            }
        }

        // ---------------------------------------------------------------- eye rig -------

        /// <summary>
        /// Finds the eye bones, spawns "EyeTracking.L/.R" empties at them, constrains each
        /// eye bone to its empty, and fills <paramref name="remap"/> with the package→avatar
        /// path rewrites for the eye and empty transforms.
        /// </summary>
        static void BuildEyeRig(BridgeContext ctx, Dictionary<string, string> remap)
        {
            FindEyeBones(ctx, out var head, out var leftEye, out var rightEye);
            if (leftEye == null || rightEye == null || head == null)
            {
                ctx.Report.Warning(Category, "Eye bones not found — eye gaze left unwired",
                    "The avatar has no mapped Left/Right Eye humanoid bones (and none named Eye.L/Eye.R). " +
                    "Face shapes still work; CVR's native eye look stays on.");
                return;
            }

            var leftEmpty = MakeEyeTarget("EyeTracking.L", head, leftEye);
            var rightEmpty = MakeEyeTarget("EyeTracking.R", head, rightEye);
            ConstrainEye(leftEye, leftEmpty, head);
            ConstrainEye(rightEye, rightEmpty, head);

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

        /// <summary>
        /// Creates (or reuses) a head-aligned empty positioned at the eye bone. The gaze
        /// clips rotate it in the head's frame, matching how the package's empties are set up.
        /// </summary>
        static Transform MakeEyeTarget(string name, Transform head, Transform eye)
        {
            var existing = head.Find(name);
            var go = existing != null ? existing.gameObject : new GameObject(name);
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(go, "AvatarBridge FT eye rig");
                go.transform.SetParent(head, false);
            }
            go.transform.position = eye.position;
            go.transform.rotation = head.rotation; // head-forward frame
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        static void ConstrainEye(Transform eye, Transform target, Transform head)
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
            // Source is head-aligned; the offset that keeps the eye at rest is its rotation
            // relative to the head (so eye.world = source.world * offset == eye rest world).
            var relToHead = Quaternion.Inverse(head.rotation) * eye.rotation;
            rc.rotationOffset = relToHead.eulerAngles;
            rc.rotationAtRest = eye.localEulerAngles;
            rc.locked = true;
            rc.constraintActive = true;
            EditorUtility.SetDirty(rc);
        }

        /// <summary>
        /// The face mesh the FT blendshapes belong on. Prefers the descriptor's own viseme
        /// mesh (CVRAvatar.bodyMesh) — the authoritative choice — then a mesh literally named
        /// "Body", then a shape-count heuristic that skips VRCFury FT *debug* meshes (which
        /// carry the full Unified-Expressions set purely for visualisation and otherwise win
        /// the heuristic).
        /// </summary>
        static SkinnedMeshRenderer ResolveFaceMesh(BridgeContext ctx)
        {
            if (ctx.CvrAvatar != null && ctx.CvrAvatar.bodyMesh != null)
            {
                return ctx.CvrAvatar.bodyMesh;
            }
            var meshes = ctx.Target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in meshes)
            {
                if (string.Equals(smr.name, "Body", StringComparison.OrdinalIgnoreCase))
                {
                    return smr;
                }
            }
            SkinnedMeshRenderer best = null;
            int bestScore = 0;
            foreach (var smr in meshes)
            {
                var m = smr.sharedMesh;
                if (m == null || m.blendShapeCount == 0 || IsDebugMesh(ctx, smr))
                {
                    continue;
                }
                int score = 0;
                for (int i = 0; i < m.blendShapeCount; i++)
                {
                    string s = m.GetBlendShapeName(i).ToLowerInvariant();
                    if (s.Contains("jawopen") || s.Contains("eyelookout") || s.Contains("mouthclosed"))
                    {
                        score++;
                    }
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = smr;
                }
            }
            return bestScore > 0 ? best : null;
        }

        /// <summary>VRCFury's Unified-Expressions debug window and similar non-face meshes.</summary>
        static bool IsDebugMesh(BridgeContext ctx, SkinnedMeshRenderer smr)
        {
            string name = smr.name.ToLowerInvariant();
            string path = ctx.PathInTarget(smr.transform).ToLowerInvariant();
            return name.Contains("debug") || path.Contains("debug")
                   || path.Contains("vf_ue") || path.Contains("ft_debug") || path.Contains("worldobject");
        }

        // ------------------------------------------------------ path + shape rewrite ----

        static void RewriteMachine(AnimatorStateMachine machine, Dictionary<string, string> pathRemap,
            Dictionary<string, ShapeAction> shapePlan, string faceMeshPath,
            Dictionary<AnimationClip, AnimationClip> cache)
        {
            if (machine == null)
            {
                return;
            }
            var states = machine.states;
            for (int i = 0; i < states.Length; i++)
            {
                states[i].state.motion = RewriteMotion(states[i].state.motion, pathRemap, shapePlan, faceMeshPath, cache);
            }
            machine.states = states;
            foreach (var child in machine.stateMachines)
            {
                RewriteMachine(child.stateMachine, pathRemap, shapePlan, faceMeshPath, cache);
            }
        }

        static Motion RewriteMotion(Motion motion, Dictionary<string, string> pathRemap,
            Dictionary<string, ShapeAction> shapePlan, string faceMeshPath,
            Dictionary<AnimationClip, AnimationClip> cache)
        {
            if (motion is BlendTree tree)
            {
                var kids = tree.children;
                for (int i = 0; i < kids.Length; i++)
                {
                    kids[i].motion = RewriteMotion(kids[i].motion, pathRemap, shapePlan, faceMeshPath, cache);
                }
                bool auto = tree.useAutomaticThresholds;
                tree.useAutomaticThresholds = false;
                tree.children = kids;
                tree.useAutomaticThresholds = auto;
                return tree;
            }
            if (motion is AnimationClip clip)
            {
                return RewriteClip(clip, pathRemap, shapePlan, faceMeshPath, cache);
            }
            return motion;
        }

        const string ShapePrefix = "blendShape.";

        /// <summary>
        /// Clone-on-write copy of a clip with its curves rebound: paths remapped onto this
        /// avatar, and face blendshapes reconciled to the mesh's shape set (combined/split).
        /// </summary>
        static AnimationClip RewriteClip(AnimationClip clip, Dictionary<string, string> pathRemap,
            Dictionary<string, ShapeAction> shapePlan, string faceMeshPath,
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
            bool needs = floatBindings.Any(b => NeedsRewrite(b, pathRemap, shapePlan))
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
            // binding. Keyed by "path|type|property" → summed curve.
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

        /// <summary>Sums two curves over the union of their keyframe times (blend-tree semantics).</summary>
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
