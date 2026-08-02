#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using ABI.CCK.Components;

namespace AvatarBridge
{
    /// <summary>
    /// Small component conversions plus the final VRC-component cleanup:
    ///   VRCHeadChop           -> FPRExclusion (CVR first-person hiding/showing)
    ///   VRCSpatialAudioSource -> plain AudioSource spatial settings
    ///   leftover VRC.* components + PipelineManager -> deleted
    /// </summary>
    public static class MiscConverter
    {
        public static void Run(BridgeContext ctx)
        {
            if (ctx.Settings.convertHeadChop)
            {
                ConvertHeadChops(ctx);
            }
            if (ctx.Settings.convertSpatialAudio)
            {
                ConvertSpatialAudio(ctx);
            }
            NormalizeSkinnedBounds(ctx);
            SanitizeAudioSources(ctx);
            GroundAnimationPoseRatio(ctx);
        }

        /// <summary>
        /// Clears MagicaCloth2's <c>animationPoseRatio</c> on chains that nothing actually animates.
        ///
        /// The ratio picks what the cloth RESTORES TOWARD: 0 the bind pose, 1 the animated pose
        /// ("復元を基本姿勢で行うかアニメーション後の姿勢で行うかの判定" in MagicaCloth2's own
        /// distance constraint). The physics pass sets it to 1 whenever the source PhysBone had
        /// "Is Animated" ticked, so a chest slider that scales its bones wins over the cloth
        /// instead of fighting it.
        ///
        /// But "Is Animated" is the AUTHOR'S CLAIM, not evidence. When nothing drives those bones
        /// — the author ticked it speculatively, or the animation belonged to a system this
        /// conversion stripped — the "animated pose" is just wherever the transform currently
        /// sits, which is what the cloth itself wrote last frame. The restore target then chases
        /// its own output, no restoring force exists, and the chain rotates freely forever.
        ///
        /// Reported as a rear that span on its own, with the tell that made it obvious: playing
        /// ANY animation stopped it dead, and stopping the animation started it again. That is
        /// this loop being broken by an authoritative pose and then handed back to itself.
        ///
        /// Runs after the merge because only the FINAL controller knows what survived. Reflection
        /// rather than a direct reference so this file needs no MagicaCloth2 define.
        /// </summary>
        static void GroundAnimationPoseRatio(BridgeContext ctx)
        {
            if (ctx.ConvertedPhysicsChains == null || ctx.ConvertedPhysicsChains.Count == 0
                || ctx.MergedController == null || ctx.Target == null)
            {
                return;
            }

            // Every transform path any surviving clip animates.
            var animated = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var clip in ctx.MergedController.animationClips)
            {
                if (clip == null) continue;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    animated.Add(binding.path);
                }
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    animated.Add(binding.path);
                }
            }

            var grounded = new List<string>();
            foreach (var chain in ctx.ConvertedPhysicsChains)
            {
                if (chain.Physics == null || chain.Root == null) continue;
                var sdata = chain.Physics.GetType().GetProperty("SerializeData")?.GetValue(chain.Physics);
                var field = sdata?.GetType().GetField("animationPoseRatio");
                if (field == null || !(field.GetValue(sdata) is float ratio) || ratio <= 0.001f)
                {
                    continue;
                }

                // Animated if the chain root, or anything under it, carries a curve.
                string root = BridgeContext.RelativePath(ctx.Target.transform, chain.Root);
                bool drives = animated.Any(p => p == root
                    || (p.Length > root.Length && p.StartsWith(root, System.StringComparison.Ordinal)
                        && p[root.Length] == '/'));
                if (drives)
                {
                    continue;
                }

                field.SetValue(sdata, 0f);
                EditorUtility.SetDirty(chain.Physics);
                grounded.Add(chain.Root.name);
            }

            if (grounded.Count > 0)
            {
                ctx.Report.Converted("PhysBones -> MagicaCloth2",
                    $"{grounded.Count} cloth chain(s) settled back to their built pose — nothing animates them",
                    string.Join(", ", grounded) + " — their source PhysBone had \"Is Animated\" ticked, which " +
                    "makes the cloth restore toward the ANIMATED pose instead of the pose the avatar was built " +
                    "in. That is right when something drives those bones; here nothing in the converted " +
                    "animator does, so the \"animated pose\" would just be wherever the cloth itself last put " +
                    "them — a target chasing its own output, which leaves the chain free to rotate forever " +
                    "with nothing pulling it back. The tell is that playing any animation stops it dead. If a " +
                    "slider is supposed to move these bones and no longer does, that animation did not survive " +
                    "conversion, and this line names the chain to check.");
            }
        }

        /// <summary>
        /// VRChat-parity clamps for avatar AudioSources. VRChat force-limits avatar audio
        /// (doppler zeroed, distance floors/caps), so avatars are AUTHORED against those
        /// clamps and never feel them. ChilloutVR instead routes every fully-3D avatar
        /// source straight into Steam Audio with its authored settings (decompiled
        /// SharedFilter.ProcessAudioSource: spatialize = spatialBlend >= 1) — and an
        /// unclamped source can take the whole mix down: minDistance 0 puts a divide-by-
        /// distance in the spatializer's attenuation for a source mounted on the wearer's
        /// own body, where the listener can reach distance ~0; one inf/NaN gain poisons the
        /// master bus and EVERY sound in the game goes silent until the avatar unloads.
        /// Observed in the wild: wearing one converted avatar muted voice, video players
        /// and prop music game-wide, recovering on avatar switch. Doppler goes to zero for
        /// the same reason VRChat zeroes it — sources ride animated and simulated bones,
        /// whose frame-to-frame velocity is pitch chaos.
        /// </summary>
        static void SanitizeAudioSources(BridgeContext ctx)
        {
            int clamped = 0;
            var notes = new List<string>();
            foreach (var source in ctx.Target.GetComponentsInChildren<AudioSource>(true))
            {
                bool changed = false;
                if (source.dopplerLevel != 0f)
                {
                    source.dopplerLevel = 0f;
                    changed = true;
                }
                if (source.minDistance < 0.3f)
                {
                    source.minDistance = 0.3f;
                    changed = true;
                }
                if (source.maxDistance > 40f)
                {
                    source.maxDistance = 40f;
                    changed = true;
                }
                if (source.maxDistance < source.minDistance)
                {
                    source.maxDistance = source.minDistance;
                    changed = true;
                }
                if (changed)
                {
                    clamped++;
                    if (notes.Count < 6)
                    {
                        notes.Add(source.gameObject.name);
                    }
                    EditorUtility.SetDirty(source);
                }
            }
            if (clamped > 0)
            {
                ctx.Report.Approximated("Audio",
                    $"{clamped} audio source(s) clamped to VRChat's avatar audio limits",
                    $"On: {string.Join(", ", notes)}{(clamped > notes.Count ? ", …" : "")} — doppler 0, " +
                    "min distance at least 0.3 m, max distance at most 40 m. VRChat silently enforces " +
                    "these on every avatar, so this is how the avatar actually sounded there. ChilloutVR " +
                    "feeds avatar sources to its spatializer unclamped, and a source with min distance 0 " +
                    "mounted on the wearer's own body can silence the ENTIRE game's audio (voice, video, " +
                    "props) while the avatar is worn — the mix recovers when it unloads.");
            }
        }

        /// <summary>
        /// Unity culls a skinned mesh by its AUTHORED bounding box, not by where animation,
        /// physics or cloth actually put the vertices — the box is baked from the bind pose
        /// and never follows. The moment the stale box leaves the camera frustum the whole
        /// mesh blinks out: classically at screen edges, or for another player looking from
        /// the side. Centre zero puts the box on each mesh's root bone; the extent floor is
        /// the avatar's own measured height (at least 1.5 m), so a chibi doesn't drag a
        /// stadium-sized box around and a giant isn't clipped by a human-sized one. Authored
        /// extents larger than the floor are kept — shrinking a deliberately large box could
        /// CAUSE the very culling this prevents.
        /// </summary>
        static void NormalizeSkinnedBounds(BridgeContext ctx)
        {
            float floor = Mathf.Max(AvatarScalerInjector.MeasureHeight(ctx), 1.5f);
            int changed = 0;
            foreach (var renderer in ctx.Target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bounds = renderer.localBounds;

                // localBounds is expressed in the ROOT BONE's space, but the floor is a real
                // measurement in metres — so the two are only comparable when that bone sits at
                // scale 1. Convert the floor into this renderer's own units before using it.
                //
                // Without this, the same "1.5" meant 1.5 m on an ordinary rig, 1.5 cm on a bone
                // scaled to 0.01, and 150 m on one scaled to 100 (Second Life conversions run
                // around 100x) — so the box was alternately too small to stop the culling it
                // exists to prevent, or absurdly large. Reported as "1.5 is sometimes too small
                // or too big", which is exactly what a unit mismatch looks like from outside.
                var boneScale = (renderer.rootBone != null ? renderer.rootBone : renderer.transform).lossyScale;
                float Local(float axis)
                {
                    // A zero or degenerate axis carries no usable ratio; fall back to the metre
                    // value rather than dividing by ~0 and producing an infinite box.
                    float s = Mathf.Abs(axis);
                    return s > 1e-4f ? floor / s : floor;
                }
                var localFloor = new Vector3(Local(boneScale.x), Local(boneScale.y), Local(boneScale.z));

                var extents = new Vector3(
                    Mathf.Max(bounds.extents.x, localFloor.x),
                    Mathf.Max(bounds.extents.y, localFloor.y),
                    Mathf.Max(bounds.extents.z, localFloor.z));
                if (bounds.center == Vector3.zero && bounds.extents == extents)
                {
                    continue;
                }
                renderer.localBounds = new Bounds(Vector3.zero, extents * 2f);
                EditorUtility.SetDirty(renderer);
                changed++;
            }
            if (changed > 0)
            {
                ctx.Report.Converted("Meshes",
                    $"{changed} skinned mesh bounding box(es) normalized — centre 0, extents at least {floor:0.##} m",
                    "Unity culls a skinned mesh by its authored bind-pose box, not by where animation, physics " +
                    "or cloth actually put the vertices — so a mesh can vanish at screen edges while plainly on " +
                    "camera. Each box now sits centred on its mesh's root bone and reaches at least the avatar's " +
                    "own measured height in every direction, scaled with the avatar if it resizes; authored boxes " +
                    "larger than that are kept, since shrinking one could cause the very culling this prevents.");
            }
        }

        static void ConvertHeadChops(BridgeContext ctx)
        {
            const string category = "Head chop";
            // Path of each VRC Head Chop GameObject -> the FPRExclusion transforms made from it,
            // so animations that toggled the head chop can be re-pointed at the exclusions.
            var pathToExclusions = new Dictionary<string, List<Transform>>();

            var headChops = ctx.Target.GetComponentsInChildren<VRCHeadChop>(true);
            foreach (var headChop in headChops)
            {
                string headChopPath = ctx.PathInTarget(headChop.transform);
                foreach (var setting in headChop.targetBones)
                {
                    if (setting.transform == null)
                    {
                        continue;
                    }
                    float scaleFactor = setting.scaleFactor * headChop.globalScaleFactor;
                    bool isShown = Mathf.Approximately(scaleFactor, 1f);
                    bool isHidden = Mathf.Approximately(scaleFactor, 0f);
                    if (!isShown && !isHidden)
                    {
                        ctx.Report.Skipped(category, setting.transform.name,
                            $"Scale factor {scaleFactor:0.##} cannot be represented; FPRExclusion is show/hide only.");
                        continue;
                    }

                    var go = new GameObject("FPRExclusion_" + setting.transform.name);
                    go.transform.SetParent(ctx.Target.transform, false);
                    var exclusion = go.AddComponent<FPRExclusion>();
                    exclusion.isShown = isShown;
                    exclusion.shrinkToZero = true;
                    exclusion.target = setting.transform;
                    ctx.Report.Converted(category, setting.transform.name,
                        isShown ? "Shown in first person" : "Hidden in first person");

                    if (!string.IsNullOrEmpty(headChopPath))
                    {
                        if (!pathToExclusions.TryGetValue(headChopPath, out var list))
                        {
                            pathToExclusions[headChopPath] = list = new List<Transform>();
                        }
                        list.Add(go.transform);
                    }
                }
            }

            // Re-point any animation that drove the VRC Head Chop onto FPRExclusion.isShown,
            // BEFORE the head-chop components are deleted below (otherwise the toggles go dead).
            RewriteHeadChopAnimations(ctx, pathToExclusions);

            foreach (var headChop in headChops)
            {
                if (headChop != null)
                {
                    Object.DestroyImmediate(headChop);
                }
            }
        }

        /// <summary>
        /// VRChat toggles Head Chop by animating the VRCHeadChop component (scale factor 1=shown,
        /// 0=hidden). CVR's FPRExclusion exposes that as an animatable `isShown` bool, so we clone
        /// the driving clips and rebind the head-chop curves onto each FPRExclusion's isShown —
        /// scale factor maps straight across (1→shown, 0→hidden); a `m_Enabled` curve is inverted.
        /// </summary>
        static void RewriteHeadChopAnimations(BridgeContext ctx, Dictionary<string, List<Transform>> map)
        {
            var controller = ctx.MergedController;
            if (controller == null || map.Count == 0)
            {
                return;
            }

            var cache = new Dictionary<AnimationClip, AnimationClip>();
            var animated = new HashSet<Transform>();
            foreach (var layer in controller.layers)
            {
                RewriteHeadChopMachine(ctx, layer.stateMachine, map, cache, animated);
            }

            int cloned = cache.Count(kv => kv.Key != kv.Value);
            if (cloned == 0)
            {
                return;
            }

            foreach (var kv in cache)
            {
                if (kv.Key != kv.Value && !AssetDatabase.Contains(kv.Value))
                {
                    kv.Value.hideFlags = HideFlags.HideInHierarchy;
                    AssetDatabase.AddObjectToAsset(kv.Value, controller);
                }
            }
            // Every clone leaves its original behind inside the controller, still carrying the
            // head-chop curves that now point at a component this pass is about to delete. Nothing
            // references them once the states have been re-pointed, but they stay in the file and
            // read as live conversion bugs to anyone grepping it — one avatar shipped 22 dead
            // globalScaleFactor curves in copies no state could reach.
            int orphans = RemoveUnreferencedSubAssets(controller,
                cache.Where(kv => kv.Key != kv.Value).Select(kv => kv.Key));
            // Animated exclusions start Shown, so the toggle drives them from a sensible baseline.
            foreach (var t in animated)
            {
                var excl = t.GetComponent<FPRExclusion>();
                if (excl != null)
                {
                    excl.isShown = true;
                    EditorUtility.SetDirty(excl);
                }
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            ctx.Report.Converted("Head chop", $"Rewired {cloned} head-chop toggle animation(s) to FPRExclusion",
                "The toggles now drive each FPRExclusion's IsShown instead of the removed VRC Head Chop." +
                (orphans > 0
                    ? $" {orphans} superseded cop(ies) of those clips were removed from the controller — " +
                      "rewiring works on a copy, and the original was being left in the file still " +
                      "animating the deleted VRChat component, where it looked like a live bug."
                    : ""));
        }

        /// <summary>
        /// Deletes sub-assets of a controller that nothing in it points at any more. Used after a
        /// pass that replaces clips with rewritten copies: the originals are dead weight, and dead
        /// weight inside a controller is indistinguishable from a broken reference when someone
        /// reads the file to work out why something doesn't animate.
        ///
        /// Only objects handed in are considered, and only if the controller genuinely no longer
        /// reaches them — a clip still used by one state and replaced in another must stay.
        /// </summary>
        static int RemoveUnreferencedSubAssets(AnimatorController controller,
            IEnumerable<AnimationClip> suspects)
        {
            var live = new HashSet<Motion>();
            void Reach(Motion motion)
            {
                if (motion == null || !live.Add(motion) || !(motion is BlendTree tree))
                {
                    return;
                }
                foreach (var child in tree.children)
                {
                    Reach(child.motion);
                }
            }
            void Walk(AnimatorStateMachine machine)
            {
                if (machine == null)
                {
                    return;
                }
                foreach (var child in machine.states)
                {
                    if (child.state != null)
                    {
                        Reach(child.state.motion);
                    }
                }
                foreach (var child in machine.stateMachines)
                {
                    Walk(child.stateMachine);
                }
            }
            foreach (var layer in controller.layers)
            {
                Walk(layer.stateMachine);
            }

            string controllerPath = AssetDatabase.GetAssetPath(controller);
            int removed = 0;
            foreach (var clip in suspects.Distinct())
            {
                if (clip == null || live.Contains(clip))
                {
                    continue;
                }
                // Only ever our own controller's sub-assets: never touch a clip that lives in the
                // user's project as a file of its own.
                if (!AssetDatabase.IsSubAsset(clip) || AssetDatabase.GetAssetPath(clip) != controllerPath)
                {
                    continue;
                }
                AssetDatabase.RemoveObjectFromAsset(clip);
                Object.DestroyImmediate(clip, true);
                removed++;
            }
            return removed;
        }

        static void RewriteHeadChopMachine(BridgeContext ctx, AnimatorStateMachine machine,
            Dictionary<string, List<Transform>> map, Dictionary<AnimationClip, AnimationClip> cache,
            HashSet<Transform> animated)
        {
            if (machine == null)
            {
                return;
            }
            var states = machine.states;
            for (int i = 0; i < states.Length; i++)
            {
                states[i].state.motion = RewriteHeadChopMotion(ctx, states[i].state.motion, map, cache, animated);
            }
            machine.states = states;
            foreach (var child in machine.stateMachines)
            {
                RewriteHeadChopMachine(ctx, child.stateMachine, map, cache, animated);
            }
        }

        static Motion RewriteHeadChopMotion(BridgeContext ctx, Motion motion,
            Dictionary<string, List<Transform>> map, Dictionary<AnimationClip, AnimationClip> cache,
            HashSet<Transform> animated)
        {
            if (motion is BlendTree tree)
            {
                var kids = tree.children;
                for (int i = 0; i < kids.Length; i++)
                {
                    kids[i].motion = RewriteHeadChopMotion(ctx, kids[i].motion, map, cache, animated);
                }
                bool auto = tree.useAutomaticThresholds;
                tree.useAutomaticThresholds = false;
                tree.children = kids;
                tree.useAutomaticThresholds = auto;
                return tree;
            }
            if (motion is AnimationClip clip)
            {
                return RewriteHeadChopClip(ctx, clip, map, cache, animated);
            }
            return motion;
        }

        static AnimationClip RewriteHeadChopClip(BridgeContext ctx, AnimationClip clip,
            Dictionary<string, List<Transform>> map, Dictionary<AnimationClip, AnimationClip> cache,
            HashSet<Transform> animated)
        {
            if (clip == null)
            {
                return null;
            }
            if (cache.TryGetValue(clip, out var done))
            {
                return done;
            }
            var hits = AnimationUtility.GetCurveBindings(clip)
                .Where(b => b.type == typeof(VRCHeadChop) && map.ContainsKey(b.path))
                .ToArray();
            if (hits.Length == 0)
            {
                cache[clip] = clip;
                return clip;
            }

            var clone = Object.Instantiate(clip);
            clone.name = clip.name;
            clone.hideFlags = HideFlags.None;
            foreach (var b in hits)
            {
                var curve = AnimationUtility.GetEditorCurve(clone, b);
                AnimationUtility.SetEditorCurve(clone, b, null); // drop the (dead) head-chop binding
                var mapped = b.propertyName == "m_Enabled" ? Invert(curve) : curve;
                foreach (var fpr in map[b.path])
                {
                    var nb = new EditorCurveBinding
                    {
                        path = ctx.PathInTarget(fpr),
                        type = typeof(FPRExclusion),
                        propertyName = "isShown"
                    };
                    AnimationUtility.SetEditorCurve(clone, nb, mapped);
                    animated.Add(fpr);
                }
            }
            cache[clip] = clone;
            return clone;
        }

        static AnimationCurve Invert(AnimationCurve src)
        {
            var keys = src.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].value = 1f - keys[i].value;
                keys[i].inTangent = -keys[i].inTangent;
                keys[i].outTangent = -keys[i].outTangent;
            }
            return new AnimationCurve(keys) { preWrapMode = src.preWrapMode, postWrapMode = src.postWrapMode };
        }

        static void ConvertSpatialAudio(BridgeContext ctx)
        {
            const string category = "Audio";
            foreach (var spatial in ctx.Target.GetComponentsInChildren<VRCSpatialAudioSource>(true))
            {
                var audioSource = spatial.GetComponent<AudioSource>();
                if (audioSource != null)
                {
                    audioSource.spatialBlend = spatial.EnableSpatialization ? 1f : 0f;
                    if (!spatial.UseAudioSourceVolumeCurve)
                    {
                        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
                        audioSource.minDistance = spatial.Near;
                        audioSource.maxDistance = spatial.Far;
                    }
                    EditorUtility.SetDirty(audioSource);
                    ctx.Report.Approximated(category, spatial.gameObject.name,
                        "Spatial audio mapped to standard AudioSource settings (gain curve approximated).");
                }
                Object.DestroyImmediate(spatial);
            }
        }

        /// <summary>Removes every remaining VRC component. Run this last.</summary>
        public static void DeleteVrcComponents(BridgeContext ctx)
        {
            const string category = "Cleanup";

            var pipeline = ctx.Target.GetComponent(typeof(VRC.Core.PipelineManager));
            if (pipeline != null)
            {
                Object.DestroyImmediate(pipeline);
            }

            // Multiple passes: some VRC components depend on each other (RequireComponent).
            for (int pass = 0; pass < 4; pass++)
            {
                var vrcComponents = ctx.Target.GetComponentsInChildren(typeof(Component), true)
                    .Where(c => c != null && c.GetType().Name.StartsWith("VRC"))
                    // Respect the "keep PhysBones" option (e.g. converting them later by hand).
                    .Where(c => ctx.Settings.deleteConvertedPhysBones || !c.GetType().Name.Contains("PhysBone"))
                    .ToList();
                if (vrcComponents.Count == 0)
                {
                    break;
                }
                foreach (var component in vrcComponents)
                {
                    Object.DestroyImmediate(component);
                }
            }

            int remaining = ctx.Target.GetComponentsInChildren(typeof(Component), true)
                .Count(c => c != null && c.GetType().Name.StartsWith("VRC"));
            if (remaining > 0)
            {
                ctx.Report.Warning(category, $"{remaining} VRC component(s) could not be removed",
                    "Remove them manually before uploading.");
            }
            else
            {
                ctx.Report.Converted(category, "All VRC components removed");
            }

            StripCamerasAndListeners(ctx, category);
            StripMissingScripts(ctx, category);
        }

        /// <summary>
        /// Cameras (and their AudioListener companions) on an avatar break ChilloutVR:
        /// its asset filter walks every Camera to sanitise render textures
        /// (SharedFilter.HandleRenderTextureForCamera), and a stray/half-set camera makes
        /// that NRE, aborting the whole avatar filter — the avatar then shows as the "Error"
        /// robot. Avatars have no business carrying a Camera or AudioListener in CVR, so drop
        /// them. The GameObjects (often constraint targets, e.g. a "3rd Person Camera" rig)
        /// stay; only these components go.
        /// </summary>
        static void StripCamerasAndListeners(BridgeContext ctx, string category)
        {
            int cameras = 0, listeners = 0;
            foreach (var cam in ctx.Target.GetComponentsInChildren<Camera>(true))
            {
                if (cam == null) continue;
                var flare = cam.GetComponent<FlareLayer>();
                if (flare != null) Object.DestroyImmediate(flare);
                Object.DestroyImmediate(cam);
                cameras++;
            }
            foreach (var listener in ctx.Target.GetComponentsInChildren<AudioListener>(true))
            {
                if (listener == null) continue;
                Object.DestroyImmediate(listener);
                listeners++;
            }
            if (cameras > 0 || listeners > 0)
            {
                ctx.Report.Converted(category,
                    $"Removed {cameras} camera(s) and {listeners} audio listener(s)",
                    "ChilloutVR's asset filter crashes on avatar cameras (blocking the whole avatar); " +
                    "avatars shouldn't carry a Camera or AudioListener. The GameObjects were kept.");
            }
        }

        /// <summary>
        /// Missing scripts (e.g. a VRChat component whose script isn't present in this project)
        /// survive the VRC sweep as null component slots — the sweep skips nulls — and then
        /// trip CVR up on load ("The referenced script on this Behaviour ... is missing!").
        /// Strip them from every GameObject.
        /// </summary>
        static void StripMissingScripts(BridgeContext ctx, string category)
        {
            int removed = 0;
            foreach (var t in ctx.Target.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            }
            if (removed > 0)
            {
                ctx.Report.Converted(category, $"Removed {removed} missing-script component(s)",
                    "Empty/missing MonoBehaviour slots left over from VRChat components — CVR flags these on load.");
            }
        }
    }
}
#endif
